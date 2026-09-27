"use client";

import { useRef, useState, type PointerEvent } from "react";
import { cn } from "cn";
import { Badge } from "@/components/ui/badge";
import { TopicIcon } from "@/components/game/topic-icon";
import { TypedText } from "@/components/game/typed-text";
import type { ShiftCard, SwipeAnswer } from "@/lib/swipes/contract";

/** Смещение, после которого отпущенная карточка засчитывается ответом; короткое касание не отвечает. */
const SWIPE_THRESHOLD = 96;
export const SWIPE_EXIT_MS = 250;

type Offset = { x: number; y: number };

const atRest: Offset = { x: 0, y: 0 };

/** Куда тянут карточку: вверх — «Не знаю», иначе по горизонтали. */
function leaningTo({ x, y }: Offset): SwipeAnswer | null {
  if (x === 0 && y === 0) return null;
  if (-y > Math.abs(x)) return "unknown";
  return x > 0 ? "right" : "left";
}

/** Насколько карточку утянули в сторону direction. */
function pullOf(offset: Offset, direction: SwipeAnswer | null) {
  if (!direction) return 0;
  return direction === "unknown" ? -offset.y : Math.abs(offset.x);
}

function answerOf(offset: Offset): SwipeAnswer | null {
  const direction = leaningTo(offset);
  return pullOf(offset, direction) >= SWIPE_THRESHOLD ? direction : null;
}

const exitTransform: Record<SwipeAnswer, string> = {
  right: "translate(140%, 0) rotate(18deg)",
  left: "translate(-140%, 0) rotate(-18deg)",
  unknown: "translate(0, -130%)",
};

/**
 * Карточка Смены: формулировка печатается за card.readingMs, затем onReady. Карточка
 * тянется пальцем или мышью: отпущенная за порогом — ответ, до порога — возвращается
 * на место. exitTo уводит её в сторону ответа (в том числе когда ответили кнопкой).
 */
export function SwipeCard({
  card,
  exitTo,
  disabled,
  onReady,
  onAnswer,
}: {
  card: ShiftCard;
  exitTo: SwipeAnswer | null;
  disabled: boolean;
  onReady: () => void;
  onAnswer: (answer: SwipeAnswer) => void;
}) {
  const start = useRef<Offset | null>(null);
  const [offset, setOffset] = useState<Offset>(atRest);
  const [dragging, setDragging] = useState(false);

  /**
   * Смещение сбрасывается при любом отпускании. Улетает карточка за счёт exitTo, а если
   * ответ не дошёл до сервера, она возвращается в центр и касание не засчитается ответом.
   */
  function endDrag() {
    start.current = null;
    setDragging(false);
    setOffset(atRest);
  }

  const handlers = {
    onPointerDown(event: PointerEvent<HTMLElement>) {
      if (disabled) return;
      event.currentTarget.setPointerCapture(event.pointerId);
      start.current = { x: event.clientX, y: event.clientY };
      setDragging(true);
    },
    onPointerMove(event: PointerEvent<HTMLElement>) {
      if (!start.current) return;
      setOffset({ x: event.clientX - start.current.x, y: event.clientY - start.current.y });
    },
    onPointerUp() {
      if (!start.current) return;
      const answer = answerOf(offset);
      endDrag();
      if (answer) onAnswer(answer);
    },
    onPointerCancel: endDrag,
  };

  const leaning = dragging ? leaningTo(offset) : null;
  const hintOpacity = Math.min(1, pullOf(offset, leaning) / SWIPE_THRESHOLD);

  const transform = exitTo
    ? exitTransform[exitTo]
    : `translate(${offset.x}px, ${offset.y}px) rotate(${offset.x * 0.05}deg)`;

  return (
    <article
      aria-label="Карточка"
      {...handlers}
      className={cn(
        "relative flex min-h-40 touch-none flex-col gap-4 rounded-xl bg-card p-5 shadow-xl shadow-foreground/10 select-none",
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

      <p className="text-xl leading-snug font-bold text-pretty">
        <TypedText text={card.statement} durationMs={card.readingMs} onDone={onReady} />
      </p>

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
  card: ShiftCard;
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
        "pointer-events-none absolute top-4 max-w-[70%] rounded-lg border-2 bg-card px-3 py-1.5 text-center text-sm font-extrabold uppercase",
        hint.className,
      )}
      style={{ opacity }}
    >
      {hint.text}
    </span>
  );
}
