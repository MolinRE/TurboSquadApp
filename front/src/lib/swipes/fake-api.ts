// Подменный модуль: правила Смены на свайпах в памяти браузера, пока нет API Смены.
// Ведёт себя как сервер: карточка приходит без верной стороны, вердикт, Шкалы
// и время ответа считает он.

import type {
  AnswerOutcome,
  CycleResult,
  ShiftCard,
  ShiftMode,
  ShiftResult,
  ShiftState,
  ShiftStatus,
  SwipeAnswer,
  SwipesApi,
  Verdict,
} from "./contract";
import { localQuestions, localScales, type SwipeQuestion } from "./local-data";

const DECK_SIZE = 10;
/** Задержка «сети», чтобы экран с первого дня жил с асинхронными ответами. */
const LATENCY_MS = 150;
/** Темп печати в режиме «В своём темпе» — калибруется (open-questions §2). */
const CALM_TYPING_MS_PER_CHAR = 35;
/** Циклы метода Woodpecker: лимит на Карточку короче, печать быстрее (open-questions §2). */
const CYCLES = [
  { timeLimitMs: 10_000, typingMsPerChar: 35 },
  { timeLimitMs: 7_000, typingMsPerChar: 21 },
  { timeLimitMs: 5_000, typingMsPerChar: 12 },
];
/** Допуск на сеть, как у таймера Рейса: ответ позже лимита больше чем на секунду — «Время вышло». */
const TIMEOUT_TOLERANCE_MS = 1000;

type AnswerRecord = { questionId: string; verdict: Verdict; elapsedMs: number };

type FakeShift = {
  id: string;
  mode: ShiftMode;
  /** Номер Цикла в режиме «На скорость»; null — «В своём темпе». */
  cycle: number | null;
  previousCycles: CycleResult[];
  deck: SwipeQuestion[];
  position: number;
  scales: Record<string, number>;
  status: ShiftStatus;
  failedScale: string | null;
  answers: AnswerRecord[];
  /** Когда показали текущую карточку; null — следующую ещё не просили. */
  shownAt: number | null;
};

const shifts = new Map<string, FakeShift>();

function delay() {
  return new Promise((resolve) => setTimeout(resolve, LATENCY_MS));
}

function shuffled<T>(items: readonly T[]): T[] {
  const result = [...items];
  for (let i = result.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [result[i], result[j]] = [result[j], result[i]];
  }
  return result;
}

function createShift(
  deck: SwipeQuestion[],
  mode: ShiftMode,
  cycle: number | null,
  previousCycles: CycleResult[],
): FakeShift {
  const shift: FakeShift = {
    id: crypto.randomUUID(),
    mode,
    cycle,
    previousCycles,
    deck,
    position: 0,
    scales: Object.fromEntries(localScales.map((scale) => [scale.code, scale.start])),
    status: "running",
    failedScale: null,
    answers: [],
    shownAt: Date.now(),
  };
  shifts.set(shift.id, shift);
  return shift;
}

function findShift(shiftId: string): FakeShift {
  const shift = shifts.get(shiftId);
  if (!shift) throw new Error("Смена не найдена");
  return shift;
}

function cycleOf(shift: FakeShift) {
  return shift.cycle === null ? null : CYCLES[shift.cycle - 1];
}

function readingMsOf(shift: FakeShift, question: SwipeQuestion) {
  const msPerChar = cycleOf(shift)?.typingMsPerChar ?? CALM_TYPING_MS_PER_CHAR;
  return question.statement.length * msPerChar;
}

function toCard(shift: FakeShift, question: SwipeQuestion): ShiftCard {
  return {
    questionId: question.id,
    statement: question.statement,
    rightLabel: question.right.label,
    leftLabel: question.left.label,
    topic: question.topic,
    serviceClasses: question.serviceClasses,
    isRepeat: false,
    readingMs: readingMsOf(shift, question),
    timeLimitMs: cycleOf(shift)?.timeLimitMs ?? null,
  };
}

function mistakesOf(shift: FakeShift): SwipeQuestion[] {
  return shift.answers
    .filter((answer) => answer.verdict !== "correct")
    .map((answer) => shift.deck.find((question) => question.id === answer.questionId)!);
}

function averageOf(values: number[]) {
  return values.length ? Math.round(values.reduce((sum, value) => sum + value, 0) / values.length) : null;
}

function resultOf(shift: FakeShift): ShiftResult {
  return {
    failedScale: shift.failedScale,
    firstTryCorrect: shift.answers.filter((answer) => answer.verdict === "correct").length,
    total: shift.deck.length,
    averageAnswerMs: averageOf(shift.answers.map((answer) => answer.elapsedMs)),
    mistakes: mistakesOf(shift).map((question) => ({
      questionId: question.id,
      statement: question.statement,
      explanation: question.explanation,
    })),
  };
}

function cycleResultOf(shift: FakeShift, number: number): CycleResult {
  const { failedScale, firstTryCorrect, total, averageAnswerMs } = resultOf(shift);
  return {
    number,
    timeLimitMs: CYCLES[number - 1].timeLimitMs,
    failedScale,
    firstTryCorrect,
    total,
    averageAnswerMs,
  };
}

function toState(shift: FakeShift): ShiftState {
  const running = shift.status === "running";
  return structuredClone({
    shiftId: shift.id,
    mode: shift.mode,
    cycle:
      shift.cycle === null
        ? null
        : {
            number: shift.cycle,
            timeLimitsMs: CYCLES.map((cycle) => cycle.timeLimitMs),
            previous: shift.previousCycles,
          },
    status: shift.status,
    scales: localScales.map((scale) => ({ ...scale, value: shift.scales[scale.code] })),
    progress: {
      done: shift.answers.length,
      total: shift.deck.length,
      verdicts: shift.answers.map((answer) => answer.verdict),
    },
    card: running && shift.shownAt !== null ? toCard(shift, shift.deck[shift.position]) : null,
    result: running ? null : resultOf(shift),
  });
}

function verdictOf(question: SwipeQuestion, answer: SwipeAnswer): Verdict {
  if (answer === "unknown") return "unknown";
  return answer === question.correct ? "correct" : "wrong";
}

/**
 * «Не знаю» — половина штрафов неверной стороны с округлением к нулю: столько в среднем
 * стоит угадывание. Плюсов неверной стороны «Не знаю» не даёт.
 */
function deltasOf(question: SwipeQuestion, answer: SwipeAnswer): Record<string, number> {
  if (answer !== "unknown") return question[answer].scaleDeltas;
  const wrongSide = question.correct === "right" ? question.left : question.right;
  return Object.fromEntries(
    Object.entries(wrongSide.scaleDeltas)
      .filter(([, delta]) => delta < 0)
      .map(([code, delta]) => [code, Math.trunc(delta / 2)]),
  );
}

/** Применяет изменения с обрезкой по границам Шкал и возвращает фактические. */
function applyDeltas(shift: FakeShift, deltas: Record<string, number>): Record<string, number> {
  const applied: Record<string, number> = {};
  for (const scale of localScales) {
    const delta = deltas[scale.code];
    if (!delta) continue;
    const before = shift.scales[scale.code];
    const after = Math.min(scale.max, Math.max(scale.min, before + delta));
    shift.scales[scale.code] = after;
    if (after !== before) applied[scale.code] = after - before;
  }
  return applied;
}

/** Текущая карточка, на которую можно ответить, и время ответа от конца её печати. */
function currentCard(shift: FakeShift, questionId: string, answeredAt: number) {
  const question = shift.deck[shift.position];
  if (shift.status !== "running" || shift.shownAt === null || question.id !== questionId) {
    throw new Error("На эту карточку уже ответили");
  }
  const elapsedMs = Math.max(0, answeredAt - shift.shownAt - readingMsOf(shift, question));
  return { question, elapsedMs };
}

function settle(
  shift: FakeShift,
  question: SwipeQuestion,
  answer: SwipeAnswer,
  elapsedMs: number,
  timedOut: boolean,
): AnswerOutcome {
  const verdict = verdictOf(question, answer);
  const scaleChanges = applyDeltas(shift, deltasOf(question, answer));
  shift.answers.push({ questionId: question.id, verdict, elapsedMs });
  shift.position += 1;
  shift.shownAt = null;

  const broken = localScales.find((scale) => shift.scales[scale.code] <= scale.failureThreshold);
  if (broken) {
    shift.status = "failed";
    shift.failedScale = broken.code;
  } else if (shift.position >= shift.deck.length) {
    shift.status = "passed";
  }

  return {
    verdict,
    correctSide: question.correct,
    explanation: structuredClone(question.explanation),
    scaleChanges,
    elapsedMs,
    timedOut,
    shift: toState(shift),
  };
}

export const fakeSwipesApi: SwipesApi = {
  async startShift(mode) {
    await delay();
    const deck = shuffled(localQuestions).slice(0, DECK_SIZE);
    return toState(createShift(deck, mode, mode === "woodpecker" ? 1 : null, []));
  },

  async getShift(shiftId) {
    await delay();
    return toState(findShift(shiftId));
  },

  async showNextCard(shiftId) {
    await delay();
    const shift = findShift(shiftId);
    if (shift.status === "running" && shift.shownAt === null) shift.shownAt = Date.now();
    return toState(shift);
  },

  async answer(shiftId, questionId, answer) {
    const answeredAt = Date.now();
    await delay();
    const shift = findShift(shiftId);
    const { question, elapsedMs } = currentCard(shift, questionId, answeredAt);
    const limit = cycleOf(shift)?.timeLimitMs;
    if (limit !== undefined && elapsedMs > limit + TIMEOUT_TOLERANCE_MS) {
      return settle(shift, question, "unknown", limit, true);
    }
    return settle(shift, question, answer, elapsedMs, false);
  },

  async timeOut(shiftId, questionId) {
    const answeredAt = Date.now();
    await delay();
    const shift = findShift(shiftId);
    const { question, elapsedMs } = currentCard(shift, questionId, answeredAt);
    const limit = cycleOf(shift)?.timeLimitMs;
    if (limit === undefined || elapsedMs < limit - TIMEOUT_TOLERANCE_MS) {
      throw new Error("Время ещё не вышло");
    }
    return settle(shift, question, "unknown", limit, true);
  },

  async startNextCycle(shiftId) {
    await delay();
    const shift = findShift(shiftId);
    if (shift.cycle === null || shift.status === "running" || shift.cycle >= CYCLES.length) {
      throw new Error("Следующего Цикла нет");
    }
    const previous = [...shift.previousCycles, cycleResultOf(shift, shift.cycle)];
    return toState(createShift(shuffled(shift.deck), "woodpecker", shift.cycle + 1, previous));
  },

  async startWorkOnMistakes(shiftId) {
    await delay();
    const shift = findShift(shiftId);
    const mistakes = mistakesOf(shift);
    if (shift.status === "running" || mistakes.length === 0) {
      throw new Error("Работа над ошибками доступна после Смены с ошибками");
    }
    return toState(createShift(mistakes, "calm", null, []));
  },
};
