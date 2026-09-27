"use client";

import { useEffect, useState } from "react";
import { Timer } from "lucide-react";
import { cn } from "cn";

function format(ms: number) {
  const seconds = Math.floor(ms / 1000);
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")}`;
}

/**
 * Секундомер ответа по часам экрана (время в журнал пишет сервер); стоит в шапке карточки,
 * поэтому фон — цвета приложения. startedAt и stoppedAt —
 * отметки performance.now(); пока отсчёт не начался, показывает 0:00 приглушённо.
 */
export function Stopwatch({
  startedAt,
  stoppedAt,
}: {
  startedAt: number | null;
  stoppedAt: number | null;
}) {
  const [now, setNow] = useState(() => performance.now());
  const running = startedAt !== null && stoppedAt === null;

  useEffect(() => {
    if (!running) return;
    const id = setInterval(() => setNow(performance.now()), 250);
    return () => clearInterval(id);
  }, [running]);

  const elapsed = startedAt === null ? 0 : Math.max(0, (stoppedAt ?? now) - startedAt);

  return (
    <span
      role="timer"
      aria-label="Время на ответ"
      className={cn(
        "inline-flex h-7 shrink-0 items-center gap-1 rounded-full bg-background px-2.5 text-xs font-extrabold tabular-nums",
        startedAt === null && "text-muted-foreground",
      )}
    >
      <Timer className="size-3.5 text-brand" aria-hidden />
      {format(elapsed)}
    </span>
  );
}
