"use client";

import { useRef, useState, type PointerEvent } from "react";
import { cn } from "cn";
import { Badge } from "@/components/ui/badge";
import { TopicIcon } from "@/components/game/topic-icon";
import type { SwipeAnswer, SwipeCard as Card } from "@/lib/swipes/contract";

/** Смещение, после которого отпущенная карточка засчитывается ответом; короткое касание не отвечает. */
const SWIPE_THRESHOLD = 96;
export const SWIPE_EXIT_MS = 250;

type Offset = { x: number; y: number };

/** Куда тянут карточку: вверх — «Не знаю», иначе по горизонтали. */
function leaningTo({ x, y }: Offset): SwipeAnswer | null {
  if (x === 0 && y === 0) return null;
  if (-y > Math.abs(x)) return "unknown";
  return x > 0 ? "right" : "left";
}

function answerOf(offset: Offset): SwipeAnswer | null {
  const direction = leaningTo(offset);
  const distance = direction === "unknown" ? -offset.y : Math.abs(offset.x);
  return distance >= SWIPE_THRESHOLD ? direction : null;
}

const exitTransform: Record<SwipeAnswer, string> = {
  right: "translate(140%, 0) rotate(18deg)",
  left: "translate(-140%, 0) rotate(-18deg)",
  unknown: "translate(0, -130%)",
};

/**
 * Карточка Смены: тянется пальцем или мышью. Отпущенная за порогом — ответ,
 * до порога — возвращается на место. exitTo уводит её в сторону ответа
 * (в том числе когда ответили кнопкой).
 */
export function SwipeCard({
  card,
  exitTo,
  disabled,
  onAnswer,
}: {
  card: Card;
  exitTo: SwipeAnswer | null;
  disabled: boolean;
  onAnswer: (answer: SwipeAnswer) => void;
}) {
  const start = useRef<Offset | null>(null);
  const [offset, setOffset] = useState<Offset>({ x: 0, y: 0 });
  const [dragging, setDragging] = useState(false);

  function release() {
    start.current = null;
    setDragging(false);
    const answer = answerOf(offset);
    if (answer) onAnswer(answer);
    else setOffset({ x: 0, y: 0 });
  }

  const handlers = {
    onPointerDown(event: PointerEvent<HTMLElement>) {
      if (disabled) return;
      event.currentTarget.setPointerCapture(event.pointerId);
      start.current = { x: event.clientX - offset.x, y: event.clientY - offset.y };
      setDragging(true);
    },
    onPointerMove(event: PointerEvent<HTMLElement>) {
      if (!start.current) return;
      setOffset({ x: event.clientX - start.current.x, y: event.clientY - start.current.y });
    },
    onPointerUp() {
      if (start.current) release();
    },
    onPointerCancel() {
      start.current = null;
      setDragging(false);
      setOffset({ x: 0, y: 0 });
    },
  };

  const leaning = dragging ? leaningTo(offset) : null;
  const pull = leaning === "unknown" ? -offset.y : Math.abs(offset.x);
  const hintOpacity = Math.min(1, pull / SWIPE_THRESHOLD);

  const transform = exitTo
    ? exitTransform[exitTo]
    : `translate(${offset.x}px, ${offset.y}px) rotate(${offset.x * 0.05}deg)`;

  return (
    <article
      aria-label="Карточка"
      {...handlers}
      className={cn(
        "absolute inset-0 flex touch-none flex-col gap-4 rounded-2xl bg-card p-5 shadow-[0_24px_40px_-28px_rgba(20,32,51,0.45)] select-none",
        !disabled && "cursor-grab active:cursor-grabbing",
      )}
      style={{
        transform,
        opacity: exitTo ? 0 : 1,
        transition: dragging
          ? "none"
          : `transform ${SWIPE_EXIT_MS}ms ease-out, opacity ${SWIPE_EXIT_MS}ms ease-in`,
      }}
    >
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

      <p className="text-xl leading-snug font-bold text-pretty">{card.statement}</p>

      <SwipeHint answer={leaning} opacity={hintOpacity} card={card} />
    </article>
  );
}

/** Надпись-штамп: что будет ответом, если отпустить карточку сейчас. */
function SwipeHint({
  answer,
  opacity,
  card,
}: {
  answer: SwipeAnswer | null;
  opacity: number;
  card: Card;
}) {
  if (!answer) return null;
  const hint = {
    right: { text: card.rightLabel, className: "left-5 -rotate-6 border-brand text-brand" },
    left: { text: card.leftLabel, className: "right-5 rotate-6 border-foreground text-foreground" },
    unknown: {
      text: "Не знаю",
      className: "left-1/2 -translate-x-1/2 border-warning-foreground text-warning-foreground",
    },
  }[answer];

  return (
    <span
      aria-hidden
      className={cn(
        "pointer-events-none absolute bottom-6 max-w-[70%] rounded-lg border-2 bg-card px-3 py-1.5 text-center text-sm font-extrabold uppercase",
        hint.className,
      )}
      style={{ opacity }}
    >
      {hint.text}
    </span>
  );
}
