/** Секунды с одним знаком после запятой: 3400 → «3,4». */
export function formatSeconds(ms: number) {
  return (ms / 1000).toLocaleString("ru-RU", {
    minimumFractionDigits: 1,
    maximumFractionDigits: 1,
  });
}
