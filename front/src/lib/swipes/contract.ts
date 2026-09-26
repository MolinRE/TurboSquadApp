// Контракт Смены на свайпах: какие операции и данные экран ждёт от сервера.
// Правила Смены живут на сервере, как у Рейса (ADR-0001): вердикт и Шкалы
// считает он, экран только показывает. Поэтому верная сторона карточки
// приходит лишь в ответе на неё.

/** Ответ на карточку: вправо — «да / можно / сделать», влево — «нет / нельзя / не делать», вверх — «Не знаю». */
export type SwipeAnswer = "right" | "left" | "unknown";

export type SwipeSide = "right" | "left";

export type Verdict = "correct" | "wrong" | "unknown";

export type ShiftStatus = "running" | "passed" | "failed";

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

export type SwipeCard = {
  questionId: string;
  statement: string;
  rightLabel: string;
  leftLabel: string;
  topic: string;
  /** Классы обслуживания, к которым относится Вопрос; пустой список — все классы. */
  serviceClasses: ServiceClassRef[];
  /** Карточка вернулась Повтором после ошибки или «Не знаю». */
  isRepeat: boolean;
};

/** Пояснение: ключевой факт — дословный фрагмент текста, экран его выделяет. */
export type Explanation = {
  text: string;
  keyFact: string;
};

export type ShiftProgress = {
  /** Вопросы колоды, с которыми Смена закончила. */
  done: number;
  total: number;
};

/** Вопрос с ошибкой или «Не знаю» — строка «Что повторить» в итоге Смены. */
export type MistakeItem = {
  questionId: string;
  statement: string;
  explanation: Explanation;
  source: string;
};

export type ShiftResult = {
  /** Код Шкалы, дошедшей до порога Срыва; null, если Смена пройдена. */
  failedScale: string | null;
  /** Вопросы, отвеченные верно с первого показа. */
  firstTryCorrect: number;
  total: number;
  /** «Что повторить» — из них собирается Работа над ошибками. */
  mistakes: MistakeItem[];
};

export type ShiftState = {
  shiftId: string;
  status: ShiftStatus;
  scales: ScaleState[];
  progress: ShiftProgress;
  /** Текущая карточка; null, когда Смена закончена. */
  card: SwipeCard | null;
  /** Итог; есть только у законченной Смены. */
  result: ShiftResult | null;
};

export type AnswerOutcome = {
  verdict: Verdict;
  correctSide: SwipeSide;
  explanation: Explanation;
  source: string;
  /** Фактические изменения Шкал после обрезки по границам: код Шкалы → изменение. */
  scaleChanges: Record<string, number>;
  /** Смена после ответа: следующая карточка или итог. */
  shift: ShiftState;
};

export interface SwipesApi {
  startShift(): Promise<ShiftState>;
  /** Текущее состояние, чтобы продолжить Смену после перезагрузки. */
  getShift(shiftId: string): Promise<ShiftState>;
  /** questionId — карточка, на которую отвечают: ответ на уже отвеченную отклоняется. */
  answer(shiftId: string, questionId: string, answer: SwipeAnswer): Promise<AnswerOutcome>;
  /** Работа над ошибками: новая Смена из Вопросов с ошибкой или «Не знаю» законченной Смены. */
  startWorkOnMistakes(shiftId: string): Promise<ShiftState>;
}
