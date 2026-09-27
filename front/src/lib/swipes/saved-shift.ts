// Незаконченная Смена этого браузера: после перезагрузки страницы экран продолжает её.
// Без хранилища (приватный режим, запрет сайтам) Смена просто не переживёт перезагрузку.

const SAVED_SHIFT_KEY = "turbo-brigada:swipes:shift";

function withStorage<T>(action: (storage: Storage) => T, fallback: T): T {
  try {
    return action(localStorage);
  } catch {
    return fallback;
  }
}

/** Хранилище не сообщает о своих записях в той же вкладке, а после них экран и так перерисовывается. */
export function noSubscription() {
  return () => {};
}

export function savedShift(): string | null {
  return withStorage((storage) => storage.getItem(SAVED_SHIFT_KEY), null);
}

export function saveShift(shiftId: string) {
  withStorage((storage) => storage.setItem(SAVED_SHIFT_KEY, shiftId), undefined);
}

export function forgetShift() {
  withStorage((storage) => storage.removeItem(SAVED_SHIFT_KEY), undefined);
}
