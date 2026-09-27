import { apiJson } from "@/lib/api";
import type { AnswerOutcome, ShiftState, SwipeDebrief, SwipeDebriefListItem, SwipesApi } from "./contract";

const shiftsPath = "/api/swipe-shifts";
const shiftPath = (shiftId: string) => `${shiftsPath}/${encodeURIComponent(shiftId)}`;

function post<T>(path: string, body?: unknown) {
  return apiJson<T>(path, { method: "POST", body: body === undefined ? undefined : JSON.stringify(body) });
}

/** Смена на свайпах через API (#27). Отказ правил приходит ошибкой 409 с кодом из ShiftRejection в payload.reason. */
export const swipesApi: SwipesApi = {
  startShift: (mode) => post<ShiftState>(shiftsPath, { mode }),
  getShift: (shiftId) => apiJson<ShiftState>(shiftPath(shiftId)),
  showNextCard: (shiftId) => post<ShiftState>(`${shiftPath(shiftId)}/next-card`),
  answer: (shiftId, questionId, answer) => post<AnswerOutcome>(`${shiftPath(shiftId)}/answer`, { questionId, answer }),
  timeOut: (shiftId, questionId) => post<AnswerOutcome>(`${shiftPath(shiftId)}/timeout`, { questionId }),
  startNextCycle: (shiftId) => post<ShiftState>(`${shiftPath(shiftId)}/next-cycle`),
  startWorkOnMistakes: (shiftId) => post<ShiftState>(`${shiftPath(shiftId)}/work-on-mistakes`),
};

export const getSwipeDebrief = (shiftId: string) =>
  apiJson<SwipeDebrief>(`${shiftPath(shiftId)}/debrief`);

export const listSwipeDebriefs = () =>
  apiJson<SwipeDebriefListItem[]>(`${shiftsPath}/debriefs`);
