// Контракт Смены на свайпах: какие операции и данные экран ждёт от сервера.
// Правила Смены живут на сервере, как у Рейса (ADR-0001): вердикт, Шкалы и время
// ответа считает он, экран только показывает. Поэтому верная сторона карточки
// приходит лишь в ответе на неё, а следующую карточку экран просит, когда готов
// её показать: с этого момента сервер и засекает время.

/** Ответ на карточку: вправо — «да / можно / сделать», влево — «нет / нельзя / не делать», вверх — «Не знаю». */
export type SwipeAnswer = "right" | "left" | "unknown";

export type SwipeSide = "right" | "left";

export type Verdict = "correct" | "wrong" | "unknown";

export type ShiftStatus = "running" | "passed" | "failed";

/** Режим Смены: «В своём темпе» — один проход без лимита; «На скорость» — три Цикла по методу Woodpecker. */
export type ShiftMode = "calm" | "woodpecker";

export type ScaleState = {
  code: string;
  name: string;
  value: number;
  min: number;
  max: number;
  start: number;
  /** Срыв смены, когда значение дошло до порога (≤). */
  failureThreshold: number;
};

export type ServiceClassRef = {
  code: string;
  name: string;
};

export type ShiftCard = {
  questionId: string;
  statement: string;
  rightLabel: string;
  leftLabel: string;
  topic: string;
  /** Классы обслуживания, к которым относится Вопрос; пустой список — все классы. */
  serviceClasses: ServiceClassRef[];
  /** Карточка вернулась Повтором после ошибки или «Не знаю». */
  isRepeat: boolean;
  /**
   * Сколько длится печать формулировки. Варианты ответа появляются после неё, и время
   * ответа сервер считает тоже от её конца: длинный Вопрос не даёт форы короткому.
   */
  readingMs: number;
  /** Лимит на ответ после печати; null — без лимита. Истёк — «Время вышло». */
  timeLimitMs: number | null;
};

/** Пояснение: ключевой факт — дословный фрагмент текста, экран его выделяет; source — пункт Источника. */
export type Explanation = {
  text: string;
  keyFact: string;
  source: string;
};

export type ShiftProgress = {
  /** Вопросы колоды, с которыми Смена закончила. */
  done: number;
  total: number;
  /** Вердикты по порядку ответов — первый ответ на каждый Вопрос. */
  verdicts: Verdict[];
};

/** Вопрос с ошибкой или «Не знаю» — строка «Что повторить» в итоге Смены. */
export type MistakeItem = {
  questionId: string;
  statement: string;
  explanation: Explanation;
};

export type ShiftResult = {
  /** Код Шкалы, дошедшей до порога Срыва; null, если Смена пройдена. */
  failedScale: string | null;
  /** Вопросы, отвеченные верно с первого показа. */
  firstTryCorrect: number;
  total: number;
  /** Среднее время ответа по часам сервера; null, если ответов не было. */
  averageAnswerMs: number | null;
  /** «Что повторить» — из них собирается Работа над ошибками. */
  mistakes: MistakeItem[];
};

/** Итог одного Цикла — строка сравнения Циклов. */
export type CycleResult = Omit<ShiftResult, "mistakes"> & { number: number; timeLimitMs: number };

export type CycleInfo = {
  /** Номер Цикла, с 1. */
  number: number;
  /** Лимиты на Карточку по Циклам: их число — сколько всего Циклов. */
  timeLimitsMs: number[];
  /** Итоги прошлых Циклов той же колоды. */
  previous: CycleResult[];
};

export type ShiftState = {
  shiftId: string;
  mode: ShiftMode;
  /** Цикл в режиме «На скорость»; null — «В своём темпе». */
  cycle: CycleInfo | null;
  status: ShiftStatus;
  scales: ScaleState[];
  progress: ShiftProgress;
  /** Показанная карточка; null, когда Смена закончена или следующую ещё не показали. */
  card: ShiftCard | null;
  /** Итог; есть только у законченной Смены. */
  result: ShiftResult | null;
};

export type AnswerOutcome = {
  verdict: Verdict;
  correctSide: SwipeSide;
  explanation: Explanation;
  /** Фактические изменения Шкал после обрезки по границам: код Шкалы → изменение. */
  scaleChanges: Record<string, number>;
  /** Время ответа по часам сервера: от конца печати формулировки до ответа. */
  elapsedMs: number;
  /** Время вышло: лимит истёк, ответ засчитан как «Не знаю». */
  timedOut: boolean;
  /** Смена после ответа: без карточки, пока экран не попросит следующую, или итог. */
  shift: ShiftState;
};

export interface SwipesApi {
  startShift(mode: ShiftMode): Promise<ShiftState>;
  /** Текущее состояние, чтобы продолжить Смену после перезагрузки. */
  getShift(shiftId: string): Promise<ShiftState>;
  /** Показать следующую карточку: с этого момента сервер засекает время на неё. Повторный вызов её не меняет. */
  showNextCard(shiftId: string): Promise<ShiftState>;
  /** questionId — карточка, на которую отвечают: ответ на уже отвеченную отклоняется. */
  answer(shiftId: string, questionId: string, answer: SwipeAnswer): Promise<AnswerOutcome>;
  /**
   * Время вышло: засчитывается как «Не знаю». Сервер сверяет лимит по своим часам;
   * ответ, пришедший позже лимита больше чем на секунду, он тоже считает «Время вышло».
   */
  timeOut(shiftId: string, questionId: string): Promise<AnswerOutcome>;
  /** Следующий Цикл той же колоды: новый порядок, лимит короче, Шкалы с начала. */
  startNextCycle(shiftId: string): Promise<ShiftState>;
  /** Работа над ошибками: новая Смена из Вопросов с ошибкой или «Не знаю» законченной Смены. */
  startWorkOnMistakes(shiftId: string): Promise<ShiftState>;
}
