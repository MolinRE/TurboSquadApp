"use client";

import type { ReactNode } from "react";
import { cn } from "cn";
import { Badge } from "@/components/ui/badge";
import { TopicIcon } from "@/components/game/topic-icon";
import { TypedText } from "@/components/game/typed-text";
import type { ShiftCard, SwipeAnswer } from "@/lib/swipes/contract";
import type { SwipeDrag } from "./use-swipe-drag";

export const SWIPE_EXIT_MS = 250;

const exitTransform: Record<SwipeAnswer, string> = {
  right: "translate(140%, 0) rotate(18deg)",
  left: "translate(-140%, 0) rotate(-18deg)",
  unknown: "translate(0, -130%)",
};

/**
 * Карточка Смены: сверху header (прогресс и таймер), формулировка печатается за
 * card.readingMs, затем onReady. Тянет карточку жест всего экрана (useSwipeDrag), она
 * только рисует смещение; exitTo уводит её в сторону ответа.
 */
export function SwipeCard({
  card,
  header,
  drag,
  exitTo,
  onReady,
}: {
  card: ShiftCard;
  header: ReactNode;
  drag: SwipeDrag;
  exitTo: SwipeAnswer | null;
  onReady: () => void;
}) {
  const { offset } = drag;
  const transform = exitTo
    ? exitTransform[exitTo]
    : `translate(${offset.x}px, ${offset.y}px) rotate(${offset.x * 0.05}deg)`;

  return (
    <article
      aria-label="Карточка"
      className="relative flex min-h-40 flex-col gap-4 rounded-xl bg-card p-5 shadow-xl shadow-foreground/10"
      style={{
        transform,
        opacity: exitTo ? 0 : 1,
        transition: drag.dragging
          ? "none"
          : `transform ${SWIPE_EXIT_MS}ms ease-out, opacity ${SWIPE_EXIT_MS}ms ease-in`,
      }}
    >
      {header}

      <div className="flex flex-wrap items-center gap-1.5">
        <Badge variant="secondary" className="h-6 gap-1.5 bg-background px-2.5 font-bold">
          <TopicIcon topic={card.topic} className="text-brand" />
          {card.topic}
        </Badge>
        {card.serviceClasses.map((serviceClass) => (
          <Badge key={serviceClass.code} className="h-6 px-2.5 font-bold">
            {serviceClass.name}
          </Badge>
        ))}
        {card.isRepeat ? (
          <Badge className="h-6 bg-warning-soft px-2.5 font-bold text-warning-foreground">Повтор</Badge>
        ) : null}
      </div>

      <p className="text-xl leading-snug font-bold text-pretty">
        <TypedText text={card.statement} durationMs={card.readingMs} onDone={onReady} />
      </p>

      <SwipeHint answer={drag.leaning} opacity={drag.pull} card={card} />
    </article>
  );
}

/** Надпись-штамп посередине карточки: что будет ответом, если отпустить её сейчас. */
function SwipeHint({
  answer,
  opacity,
  card,
}: {
  answer: SwipeAnswer | null;
  opacity: number;
  card: ShiftCard;
}) {
  if (!answer) return null;
  const hint = {
    right: { text: card.rightLabel, className: "-rotate-6 border-brand text-brand" },
    left: { text: card.leftLabel, className: "rotate-6 border-foreground text-foreground" },
    unknown: { text: "Не знаю", className: "border-warning-foreground text-warning-foreground" },
  }[answer];

  return (
    <span
      aria-hidden
      className={cn(
        "pointer-events-none absolute top-1/2 left-1/2 max-w-[80%] -translate-x-1/2 -translate-y-1/2 rounded-lg border-2 bg-card px-4 py-2 text-center text-base font-extrabold uppercase shadow-sm",
        hint.className,
      )}
      style={{ opacity }}
    >
      {hint.text}
    </span>
  );
}
