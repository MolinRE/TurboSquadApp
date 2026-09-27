import { apiJson } from "@/lib/api";
import type { AnswerOutcome, ShiftState, SwipesApi } from "./contract";

const SHIFTS = "/api/swipe-shifts";

function post<T>(path: string, body?: unknown) {
  return apiJson<T>(path, { method: "POST", body: body === undefined ? undefined : JSON.stringify(body) });
}

/**
 * Смена на свайпах через API (#27). Отказ правил приходит ошибкой 409 с кодом в payload.reason:
 * например, StaleCard — на эту карточку уже ответили.
 */
export const swipesApi: SwipesApi = {
  startShift: (mode) => post<ShiftState>(SHIFTS, { mode }),
  getShift: (shiftId) => apiJson<ShiftState>(`${SHIFTS}/${shiftId}`),
  showNextCard: (shiftId) => post<ShiftState>(`${SHIFTS}/${shiftId}/next-card`),
  answer: (shiftId, questionId, answer) => post<AnswerOutcome>(`${SHIFTS}/${shiftId}/answer`, { questionId, answer }),
  timeOut: (shiftId, questionId) => post<AnswerOutcome>(`${SHIFTS}/${shiftId}/timeout`, { questionId }),
  startNextCycle: (shiftId) => post<ShiftState>(`${SHIFTS}/${shiftId}/next-cycle`),
  startWorkOnMistakes: (shiftId) => post<ShiftState>(`${SHIFTS}/${shiftId}/work-on-mistakes`),
};
