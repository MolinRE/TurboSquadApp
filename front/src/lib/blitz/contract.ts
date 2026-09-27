// Контракт Блица (#41): какие операции и данные экран ждёт от сервера.
// Правила живут на сервере, как у Рейса и Смены (ADR-0001): вердикт и время ответа
// считает он. Верные варианты приходят лишь в ответе на Вопрос, а следующий Вопрос
// экран просит после панели с Пояснением: с этого момента сервер и засекает время.

import type { Explanation, MistakeItem, Verdict } from "@/lib/swipes/contract";

export type { Explanation, MistakeItem, Verdict };

export type BlitzStatus = "running" | "finished";

/** Тип Вопроса; пока играется только single — один ответ. */
export type BlitzQuestionType = "single" | "multiple" | "sequence";

export type BlitzOption = {
  id: string;
  text: string;
};

/** Показанный Вопрос без признака верного варианта. */
export type BlitzQuestion = {
  questionId: string;
  type: BlitzQuestionType;
  statement: string;
  topic: string;
  options: BlitzOption[];
  /** Лимит на ответ от показа. Истёк — «Время вышло». */
  timeLimitMs: number;
};

export type BlitzProgress = {
  /** Вопросы с ответом. */
  done: number;
  total: number;
  /** Вердикты по порядку ответов. */
  verdicts: Verdict[];
};

export type BlitzResult = {
  correct: number;
  total: number;
  /** Среднее время ответа по часам сервера; null, если ответов не было. */
  averageAnswerMs: number | null;
  /** Вопросы с ошибкой или «Время вышло». */
  mistakes: MistakeItem[];
};

export type BlitzSession = {
  sessionId: string;
  status: BlitzStatus;
  progress: BlitzProgress;
  /** Показанный Вопрос; null, когда сессия закончена или следующий ещё не показали. */
  question: BlitzQuestion | null;
  /** Итог; есть только у законченной сессии. */
  result: BlitzResult | null;
};

export type BlitzAnswerOutcome = {
  verdict: Verdict;
  /** Время вышло: лимит истёк, ответ засчитан как «Не знаю». */
  timedOut: boolean;
  correctOptionIds: string[];
  explanation: Explanation;
  /** Время ответа по часам сервера: от показа Вопроса до ответа. */
  elapsedMs: number;
  /** Сессия после ответа: без Вопроса, пока экран не попросит следующий, или итог. */
  session: BlitzSession;
};

/** Код отказа правил в ответе 409 (payload.reason). */
export type BlitzRejection =
  | "SessionNotRunning"
  | "StaleQuestion"
  | "QuestionNotShown"
  | "TimeNotExpired"
  | "InvalidSelection"
  | "NoPublishedQuestions";

export interface BlitzApi {
  startSession(): Promise<BlitzSession>;
  /** Текущее состояние, чтобы продолжить сессию после перезагрузки. */
  getSession(sessionId: string): Promise<BlitzSession>;
  /** Показать следующий Вопрос: с этого момента сервер засекает время. Повторный вызов его не меняет. */
  showNextQuestion(sessionId: string): Promise<BlitzSession>;
  /** questionId — Вопрос, на который отвечают: повторный ответ отклоняется. */
  answer(sessionId: string, questionId: string, selectedOptionIds: string[]): Promise<BlitzAnswerOutcome>;
  /** Время вышло: засчитывается как «Не знаю». Сервер сверяет лимит по своим часам. */
  timeOut(sessionId: string, questionId: string): Promise<BlitzAnswerOutcome>;
}
