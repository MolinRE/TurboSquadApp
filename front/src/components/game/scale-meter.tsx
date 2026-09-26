import { cn } from "cn";
import type { ScaleState } from "@/lib/swipes/contract";

type Tone = "ok" | "warn" | "danger";

/** Опасная зона считается от порога Срыва: нижняя четверть — красная, до половины — оранжевая. */
function toneOf(scale: ScaleState): Tone {
  const share = (scale.value - scale.failureThreshold) / (scale.max - scale.failureThreshold);
  if (share <= 0.25) return "danger";
  if (share < 0.5) return "warn";
  return "ok";
}

const barTone: Record<Tone, string> = {
  ok: "bg-brand",
  warn: "bg-warning",
  danger: "bg-danger",
};

function percentOf(scale: ScaleState, value: number) {
  return ((value - scale.min) / (scale.max - scale.min)) * 100;
}

function formatDelta(delta: number) {
  return `${delta > 0 ? "+" : "−"}${Math.abs(delta)}`;
}

/** Шкала: значение, последнее изменение и полоса с отметкой старта. */
export function ScaleMeter({ scale, delta }: { scale: ScaleState; delta?: number }) {
  return (
    <div>
      <div className="mb-1.5 flex items-baseline justify-between gap-2 text-[13px] font-semibold">
        <span>{scale.name}</span>
        <span className="flex items-baseline gap-1.5">
          <b className="text-[17px] font-extrabold tabular-nums">{scale.value}</b>
          {delta ? (
            <em
              className={cn(
                "rounded-md px-1.5 py-px text-[11.5px] font-extrabold not-italic tabular-nums",
                delta < 0 ? "bg-danger-soft text-danger" : "bg-brand-soft text-brand",
              )}
            >
              {formatDelta(delta)}
            </em>
          ) : null}
        </span>
      </div>
      <div
        role="meter"
        aria-label={scale.name}
        aria-valuemin={scale.min}
        aria-valuemax={scale.max}
        aria-valuenow={scale.value}
        className="relative h-2.5 overflow-hidden rounded-full bg-background"
      >
        <span
          className={cn(
            "absolute inset-y-0 left-0 rounded-full transition-[width] duration-300",
            barTone[toneOf(scale)],
          )}
          style={{ width: `${percentOf(scale, scale.value)}%` }}
        />
        <span
          aria-hidden
          title="Старт"
          className="absolute inset-y-0 w-0.5 bg-foreground/25"
          style={{ left: `${percentOf(scale, scale.start)}%` }}
        />
      </div>
    </div>
  );
}

/** Карточка со Шкалами игры; changes — изменения от последнего решения. */
export function ScalesPanel({
  scales,
  changes,
}: {
  scales: ScaleState[];
  changes?: Record<string, number>;
}) {
  return (
    <section aria-label="Шкалы" className="grid gap-3 rounded-xl bg-card p-4">
      {scales.map((scale) => (
        <ScaleMeter key={scale.code} scale={scale} delta={changes?.[scale.code]} />
      ))}
    </section>
  );
}
