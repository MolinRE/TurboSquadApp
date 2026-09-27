import { apiJson } from "@/lib/api";
import type { BlitzAnswerOutcome, BlitzApi, BlitzSession } from "./contract";

const sessionsPath = "/api/blitz-sessions";
const sessionPath = (sessionId: string) => `${sessionsPath}/${encodeURIComponent(sessionId)}`;

function post<T>(path: string, body?: unknown) {
  return apiJson<T>(path, { method: "POST", body: body === undefined ? undefined : JSON.stringify(body) });
}

/** Блиц через API (#41). Отказ правил приходит ошибкой 409 с кодом из BlitzRejection в payload.reason. */
export const blitzApi: BlitzApi = {
  startSession: () => post<BlitzSession>(sessionsPath, {}),
  getSession: (sessionId) => apiJson<BlitzSession>(sessionPath(sessionId)),
  showNextQuestion: (sessionId) => post<BlitzSession>(`${sessionPath(sessionId)}/next-question`),
  answer: (sessionId, questionId, answer) =>
    post<BlitzAnswerOutcome>(`${sessionPath(sessionId)}/answer`, { questionId, ...answer }),
  timeOut: (sessionId, questionId) => post<BlitzAnswerOutcome>(`${sessionPath(sessionId)}/timeout`, { questionId }),
};
