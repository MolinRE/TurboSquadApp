import { cn } from "cn";
import type { ShiftProgress, Verdict } from "@/lib/swipes/contract";

const verdictTone: Record<Verdict, string> = {
  correct: "bg-brand",
  wrong: "bg-danger",
  unknown: "bg-warning",
};

/**
 * Прогресс колоды точками — по точке на Вопрос: отвеченные окрашены вердиктом первого ответа,
 * текущая — тёмная, впереди — серые. Повтор новой точки не добавляет и тёмной не делает:
 * его Вопрос уже отмечен.
 */
export function DeckProgress({ progress, isRepeat = false }: { progress: ShiftProgress; isRepeat?: boolean }) {
  const { verdicts, total } = progress;
  return (
    <div className="flex min-w-0 flex-1 items-center gap-2">
      <div
        role="progressbar"
        aria-label="Вопросы колоды"
        aria-valuemin={0}
        aria-valuemax={total}
        aria-valuenow={progress.done}
        className="flex flex-1 gap-1"
      >
        {Array.from({ length: total }, (_, index) => (
          <span
            key={index}
            className={cn(
              "h-1.5 flex-1 rounded-full",
              index < verdicts.length
                ? verdictTone[verdicts[index]]
                : index === verdicts.length && !isRepeat
                  ? "bg-foreground/70"
                  : "bg-muted",
            )}
          />
        ))}
      </div>
      <span className="text-xs font-bold text-muted-foreground tabular-nums">
        {progress.done}/{total}
      </span>
    </div>
  );
}
