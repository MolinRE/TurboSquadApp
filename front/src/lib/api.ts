export type VoiceAttempt = {
  transcript: string | null;
  choice: string | null;
  confidence: number | null;
  latencyMs: number;
  applied: boolean;
  errorCode: string | null;
  providerRequestId: string | null;
  attemptId: string;
  passengerReply: string | null;
  replyError: string | null;
  pending?: boolean;
  score: number | null;
  scoreConfidence: number | null;
  roleStages: Record<string, number> | null;
  safetyViolation: number | null;
  safetyConfidence: number | null;
  sttLatencyMs: number | null;
  layaLatencyMs: number | null;
  llmLatencyMs: number | null;
};

export type TripView = {
  id: string;
  status: "notStarted" | "running" | "arrived" | "failed";
  serviceClass: string;
  scales: Array<{ code: string; name: string; value: number; min: number; max: number }>;
  flags: string[];
  step: {
    eventId: string;
    eventTitle: string;
    stepId: string;
    situation: string;
    answerType: "buttons" | "voice";
    timerSec: number | null;
    expiresAt: string | null;
    variants: Array<{ id: string; text: string }>;
  } | null;
  proactiveChoice: {
    situation: string;
    options: Array<{ id: string; text: string }>;
  } | null;
  result: { status: string; summary: string } | null;
  voiceAttempt: VoiceAttempt | null;
};

export type TripDebrief = {
  result: string;
  summary: string;
  voiceAttempts: Array<VoiceAttempt & {
    eventId: string;
    eventVersion: number;
    stepId: string;
  }>;
};

export type ApiErrorPayload = {
  reason?: string;
  detail?: string;
  message?: string;
  trip?: TripView;
  attempt?: VoiceAttempt;
  errors?: Array<{ path: string; message: string }>;
};

export type TokenResponse = {
  accessToken: string;
  tokenType: string;
  expiresAt: string;
};

/** Кто вошёл: ответ /api/auth/me. */
export type CurrentUser = {
  userId: string;
  username: string;
  displayName: string;
  roles: string[];
  brigade: string | null;
  depot: string | null;
};

export type VoiceLatency = { count: number; averageMs: number; p50Ms: number; p95Ms: number };

export type VoiceAnalytics = {
  attempts: number;
  appliedAttempts: number;
  failedAttempts: number;
  uncertainAttempts: number;
  uncertainRate: number;
  fallbackAttempts: number;
  fallbackRate: number;
  stt: VoiceLatency;
  laya: VoiceLatency;
  llm: VoiceLatency;
  steps: Array<{
    eventId: string;
    eventVersion: number;
    stepId: string;
    attempts: number;
    appliedAttempts: number;
    uncertainAttempts: number;
    averageScore: number | null;
    stt: VoiceLatency;
    laya: VoiceLatency;
    llm: VoiceLatency;
  }>;
};

export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly payload?: ApiErrorPayload,
  ) {
    super(message);
  }
}

const demoLoginErrors: Record<number, string> = {
  401: "Этого демо-аккаунта нет на сервере: включите засев демо-аккаунтов",
  404: "Вход без пароля на сервере выключен",
};

/** Получает токен и запоминает его. Сообщения об отказе — по коду ответа. */
async function requestToken(path: string, body: unknown, errors: Record<number, string>): Promise<TokenResponse> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!response.ok) {
    throw new ApiError(errors[response.status] ?? "Не удалось войти", response.status);
  }
  const result = (await response.json()) as TokenResponse;
  window.localStorage.setItem(TOKEN_KEY, result.accessToken);
  return result;
}

/** Кнопка «Войти как…»: бэкенд выдаёт токен демо-аккаунту без пароля. */
export function demoLogin(username: string): Promise<TokenResponse> {
  return requestToken("/api/auth/demo-login", { username }, demoLoginErrors);
}

/** Вход по логину и паролю. Причину отказа бэкенд не раскрывает. */
export function login(username: string, password: string): Promise<TokenResponse> {
  return requestToken("/api/auth/login", { username, password }, { 401: "Неверный логин или пароль" });
}

export type Registration = {
  username: string;
  displayName: string;
  depotId: string;
  brigadeId: string;
  password: string;
};

/** Регистрация Проводника. Отказ валидации — ApiError с текстом по первому полю с ошибкой. */
export async function register(registration: Registration): Promise<void> {
  const response = await fetch(`${API_BASE_URL}/api/auth/register`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(registration),
  });
  if (response.ok) return;
  const payload = (await readPayload(response)) as { errors?: Record<string, string[]> } | undefined;
  throw new ApiError(registrationError(payload?.errors), response.status);
}

// Бэкенд отвечает ValidationProblem с английскими текстами; длины полей форма проверяет сама.
function registrationError(errors: Record<string, string[]> | undefined): string {
  const [key, messages] = Object.entries(errors ?? {})[0] ?? [];
  const field = key?.toLowerCase();
  if (field === "username") {
    return messages!.some((message) => message.includes("already")) ? "Этот логин уже занят" : "Логин — от 3 до 100 символов";
  }
  if (field === "password") return "Пароль — не короче 8 символов";
  if (field === "displayname") return "Укажите имя";
  if (field === "depotid" || field === "brigadeid") return "Выберите Бригаду из списка";
  return "Не удалось зарегистрироваться";
}

/** Выход из аккаунта: токен забывается. */
export function signOut() {
  window.localStorage.removeItem(TOKEN_KEY);
}

const API_BASE_URL = (process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5017").replace(/\/$/, "");
const TOKEN_KEY = "turbo.accessToken";

function token(): string | null {
  if (typeof window === "undefined") return null;
  return window.localStorage.getItem(TOKEN_KEY);
}

/** 401 — токена нет или он истёк: забываем его и возвращаемся на «Войти как…». */
function leaveIfUnauthorized(response: Response) {
  if (response.status !== 401) return;
  signOut();
  // Из модуля API роутера Next нет; полная перезагрузка заодно сбрасывает состояние экрана со старым токеном.
  // eslint-disable-next-line @next/next/no-location-assign-relative-destination
  window.location.assign("/login");
}

async function readPayload(response: Response): Promise<ApiErrorPayload | undefined> {
  try {
    return (await response.json()) as ApiErrorPayload;
  } catch {
    return undefined;
  }
}

function headers(): HeadersInit {
  const accessToken = token();
  return accessToken ? { Authorization: `Bearer ${accessToken}` } : {};
}

async function json<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    ...init,
    headers: { ...headers(), "Content-Type": "application/json", ...init.headers },
  });
  if (!response.ok) {
    leaveIfUnauthorized(response);
    const payload = await readPayload(response);
    throw new ApiError(payload?.detail ?? payload?.message ?? "API вернул ошибку", response.status, payload);
  }
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

export { json as apiJson };

export function getCurrentUser() {
  return json<CurrentUser>("/api/auth/me");
}

export function startTrip(serviceClass: string) {
  return json<TripView>("/api/trips", {
    method: "POST",
    body: JSON.stringify({ serviceClass }),
  });
}

export function getTripDebrief(tripId: string) {
  return json<TripDebrief>(`/api/trips/${tripId}/debrief`);
}

export function getVoiceAnalytics() {
  return json<VoiceAnalytics>("/api/analytics/voice");
}

export function chooseVariant(tripId: string, eventId: string, stepId: string, variantId: string) {
  return json<TripView>(`/api/trips/${tripId}/variant`, {
    method: "POST",
    body: JSON.stringify({ eventId, stepId, variantId }),
  });
}

export function timeOutStep(tripId: string, eventId: string, stepId: string) {
  return json<TripView>(`/api/trips/${tripId}/timeout`, {
    method: "POST",
    body: JSON.stringify({ eventId, stepId }),
  });
}

export function chooseProactive(tripId: string, optionId: string) {
  return json<TripView>(`/api/trips/${tripId}/proactive`, {
    method: "POST",
    body: JSON.stringify({ optionId }),
  });
}

export async function uploadVoice(
  tripId: string,
  eventId: string,
  stepId: string,
  attemptId: string,
  audio: Blob,
  recordingMs: number,
): Promise<TripView> {
  const form = new FormData();
  form.append("eventId", eventId);
  form.append("stepId", stepId);
  form.append("attemptId", attemptId);
  form.append("audio", audio, "voice-answer.webm");
  // Таймер Шага — время на то, чтобы начать отвечать: сервер вычитает длительность записи.
  form.append("recordingMs", String(Math.round(recordingMs)));
  const response = await fetch(`${API_BASE_URL}/api/trips/${tripId}/voice`, {
    method: "POST",
    headers: headers(),
    body: form,
  });
  const payload = await readPayload(response);
  if (!response.ok) {
    leaveIfUnauthorized(response);
    throw new ApiError(payload?.detail ?? payload?.message ?? "Голосовая попытка не прошла", response.status, payload);
  }
  return payload as TripView;
}

export async function streamPassengerReply(
  tripId: string,
  attemptId: string,
  onToken: (text: string) => void,
  onTrip?: (trip: TripView) => void,
  signal?: AbortSignal,
): Promise<string> {
  const response = await fetch(`${API_BASE_URL}/api/trips/${tripId}/voice/${encodeURIComponent(attemptId)}/reply`, {
    headers: headers(),
    signal,
  });
  if (!response.ok) {
    leaveIfUnauthorized(response);
    const payload = await readPayload(response);
    throw new ApiError(payload?.detail ?? payload?.message ?? "Реплика пассажира не получена", response.status, payload);
  }
  if (!response.body) throw new ApiError("Сервер не открыл поток реплики", response.status);

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  let reply = "";
  let completed = false;
  try {
    while (true) {
      const chunk = await reader.read();
      if (chunk.done) break;
      buffer += decoder.decode(chunk.value, { stream: true });
      const events = buffer.split("\n\n");
      buffer = events.pop() ?? "";
      for (const event of events) {
        const name = event.match(/^event:\s*(.+)$/m)?.[1]?.trim();
        const data = event.match(/^data:\s*(.+)$/m)?.[1];
        if (!name || !data) continue;
        const payload = JSON.parse(data) as { text?: string; reply?: string; reason?: string; message?: string; trip?: TripView };
        if (name === "token" && payload.text) {
          reply += payload.text;
          onToken(payload.text);
        } else if (name === "done") {
          completed = true;
          if (payload.trip) onTrip?.(payload.trip);
          if (payload.reply && payload.reply !== reply) {
            reply = payload.reply;
          }
        } else if (name === "error") {
          if (payload.trip) onTrip?.(payload.trip);
          throw new ApiError(payload.message ?? "Реплика пассажира не сгенерирована", 422, { reason: payload.reason });
        }
      }
    }
  } finally {
    reader.releaseLock();
  }
  if (!completed) throw new ApiError("Поток реплики завершился без итогового события", 502);
  return reply;
}
