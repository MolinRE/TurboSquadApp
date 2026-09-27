// Незаконченная сессия Блица этого браузера: после перезагрузки страницы экран продолжает её.
// Без хранилища (приватный режим, запрет сайтам) сессия просто не переживёт перезагрузку.

const SAVED_SESSION_KEY = "turbo-brigada:blitz:session";

function withStorage<T>(action: (storage: Storage) => T, fallback: T): T {
  try {
    return action(localStorage);
  } catch {
    return fallback;
  }
}

export function savedSession(): string | null {
  return withStorage((storage) => storage.getItem(SAVED_SESSION_KEY), null);
}

export function saveSession(sessionId: string) {
  withStorage((storage) => storage.setItem(SAVED_SESSION_KEY, sessionId), undefined);
}

export function forgetSession() {
  withStorage((storage) => storage.removeItem(SAVED_SESSION_KEY), undefined);
}
