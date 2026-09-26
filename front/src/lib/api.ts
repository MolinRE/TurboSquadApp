export type VoiceAttempt = {
  transcript: string | null;
  choice: string | null;
  confidence: number | null;
  latencyMs: number;
  applied: boolean;
  errorCode: string | null;
  providerRequestId: string | null;
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

type ApiErrorPayload = {
  reason?: string;
  detail?: string;
  message?: string;
  trip?: TripView;
  attempt?: VoiceAttempt;
};

export type TokenResponse = {
  accessToken: string;
  tokenType: string;
  expiresAt: string;
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

export async function login(username: string, password: string): Promise<TokenResponse> {
  const response = await fetch(`${API_BASE_URL}/api/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username, password }),
  });
  if (!response.ok) throw new ApiError("Неверный логин или пароль", response.status);
  const result = (await response.json()) as TokenResponse;
  window.localStorage.setItem("turbo.accessToken", result.accessToken);
  return result;
}

const API_BASE_URL = (process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5017").replace(/\/$/, "");

function token(): string | null {
  if (typeof window === "undefined") return null;
  return window.localStorage.getItem("turbo.accessToken");
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
    const payload = await readPayload(response);
    throw new ApiError(payload?.detail ?? payload?.message ?? "API вернул ошибку", response.status, payload);
  }
  return (await response.json()) as T;
}

export function startTrip(serviceClass: string) {
  return json<TripView>("/api/trips", {
    method: "POST",
    body: JSON.stringify({ serviceClass }),
  });
}

export function chooseVariant(tripId: string, eventId: string, stepId: string, variantId: string) {
  return json<TripView>(`/api/trips/${tripId}/variant`, {
    method: "POST",
    body: JSON.stringify({ eventId, stepId, variantId }),
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
  audio: Blob,
): Promise<TripView> {
  const form = new FormData();
  form.append("eventId", eventId);
  form.append("stepId", stepId);
  form.append("audio", audio, "voice-answer.webm");
  const response = await fetch(`${API_BASE_URL}/api/trips/${tripId}/voice`, {
    method: "POST",
    headers: headers(),
    body: form,
  });
  const payload = await readPayload(response);
  if (!response.ok) {
    throw new ApiError(payload?.detail ?? payload?.message ?? "Голосовая попытка не прошла", response.status, payload);
  }
  return payload as TripView;
}
