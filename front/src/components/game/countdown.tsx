"use client";

import { useEffect, useEffectEvent, useState } from "react";
import { cn } from "cn";

const RADIUS = 15;
const CIRCUMFERENCE = 2 * Math.PI * RADIUS;
/** С какого остатка кольцо оранжевое. */
const URGENT_MS = 2000;

/**
 * Обратный отсчёт кольцом. startedAt и stoppedAt — отметки performance.now(); пока отсчёт
 * не начался, кольцо полное и приглушённое. На нуле один раз вызывает onExpire.
 */
export function Countdown({
  limitMs,
  startedAt,
  stoppedAt,
  onExpire,
}: {
  limitMs: number;
  startedAt: number | null;
  stoppedAt: number | null;
  onExpire: () => void;
}) {
  const [now, setNow] = useState(() => performance.now());
  const expire = useEffectEvent(onExpire);

  useEffect(() => {
    if (startedAt === null || stoppedAt !== null) return;
    const id = setInterval(() => {
      const current = performance.now();
      setNow(current);
      if (current - startedAt >= limitMs) {
        clearInterval(id);
        expire();
      }
    }, 100);
    return () => clearInterval(id);
  }, [startedAt, stoppedAt, limitMs]);

  const elapsed =
    startedAt === null ? 0 : Math.min(limitMs, Math.max(0, (stoppedAt ?? now) - startedAt));
  const remaining = limitMs - elapsed;
  const urgent = startedAt !== null && remaining <= URGENT_MS;

  return (
    <span
      role="timer"
      aria-label={`Осталось ${Math.ceil(remaining / 1000)} с`}
      className="relative grid size-9 shrink-0 place-items-center"
    >
      <svg viewBox="0 0 36 36" className="absolute inset-0 -rotate-90" aria-hidden>
        <circle cx="18" cy="18" r={RADIUS} fill="none" strokeWidth="3" className="stroke-muted" />
        <circle
          cx="18"
          cy="18"
          r={RADIUS}
          fill="none"
          strokeWidth="3"
          strokeLinecap="round"
          strokeDasharray={CIRCUMFERENCE}
          strokeDashoffset={CIRCUMFERENCE * (elapsed / limitMs)}
          className={cn(
            "transition-[stroke-dashoffset] duration-100 ease-linear",
            startedAt === null ? "stroke-muted-foreground/40" : urgent ? "stroke-warning" : "stroke-brand",
          )}
        />
      </svg>
      <span
        className={cn(
          "text-xs font-extrabold tabular-nums",
          startedAt === null && "text-muted-foreground",
          urgent && "text-warning-foreground",
        )}
      >
        {Math.ceil(remaining / 1000)}
      </span>
    </span>
  );
}
