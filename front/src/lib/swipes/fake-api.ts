// Подменный модуль: правила Смены на свайпах в памяти браузера, пока нет API Смены.
// Ведёт себя как сервер: карточка приходит без верной стороны, вердикт, Шкалы
// и время ответа считает он.

import type {
  AnswerOutcome,
  ShiftState,
  ShiftStatus,
  ShiftCard,
  SwipeAnswer,
  SwipesApi,
  Verdict,
} from "./contract";
import { localQuestions, localScales, type SwipeQuestion } from "./local-data";

const DECK_SIZE = 10;
/** Задержка «сети», чтобы экран с первого дня жил с асинхронными ответами. */
const LATENCY_MS = 150;
/** Темп печати формулировки — калибруется (open-questions §2). */
const TYPING_MS_PER_CHAR = 35;

type AnswerRecord = { questionId: string; verdict: Verdict; elapsedMs: number };

type FakeShift = {
  id: string;
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

function createShift(deck: SwipeQuestion[]): FakeShift {
  const shift: FakeShift = {
    id: crypto.randomUUID(),
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

function readingMsOf(question: SwipeQuestion) {
  return question.statement.length * TYPING_MS_PER_CHAR;
}

function toCard(question: SwipeQuestion): ShiftCard {
  return {
    questionId: question.id,
    statement: question.statement,
    rightLabel: question.right.label,
    leftLabel: question.left.label,
    topic: question.topic,
    serviceClasses: question.serviceClasses,
    isRepeat: false,
    readingMs: readingMsOf(question),
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

function toState(shift: FakeShift): ShiftState {
  const running = shift.status === "running";
  return structuredClone({
    shiftId: shift.id,
    status: shift.status,
    scales: localScales.map((scale) => ({ ...scale, value: shift.scales[scale.code] })),
    progress: { done: shift.answers.length, total: shift.deck.length },
    card: running && shift.shownAt !== null ? toCard(shift.deck[shift.position]) : null,
    result: running
      ? null
      : {
          failedScale: shift.failedScale,
          firstTryCorrect: shift.answers.filter((answer) => answer.verdict === "correct").length,
          total: shift.deck.length,
          averageAnswerMs: averageOf(shift.answers.map((answer) => answer.elapsedMs)),
          mistakes: mistakesOf(shift).map((question) => ({
            questionId: question.id,
            statement: question.statement,
            explanation: question.explanation,
          })),
        },
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

export const fakeSwipesApi: SwipesApi = {
  async startShift() {
    await delay();
    return toState(createShift(shuffled(localQuestions).slice(0, DECK_SIZE)));
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

  async answer(shiftId, questionId, answer): Promise<AnswerOutcome> {
    const answeredAt = Date.now();
    await delay();
    const shift = findShift(shiftId);
    const question = shift.deck[shift.position];
    if (shift.status !== "running" || shift.shownAt === null || question.id !== questionId) {
      throw new Error("На эту карточку уже ответили");
    }

    const elapsedMs = Math.max(0, answeredAt - shift.shownAt - readingMsOf(question));
    const verdict = verdictOf(question, answer);
    const scaleChanges = applyDeltas(shift, deltasOf(question, answer));
    shift.answers.push({ questionId, verdict, elapsedMs });
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
      shift: toState(shift),
    };
  },

  async startWorkOnMistakes(shiftId) {
    await delay();
    const shift = findShift(shiftId);
    const mistakes = mistakesOf(shift);
    if (shift.status === "running" || mistakes.length === 0) {
      throw new Error("Работа над ошибками доступна после Смены с ошибками");
    }
    return toState(createShift(mistakes));
  },
};
