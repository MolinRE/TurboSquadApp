"use client";

import { useEffect, useRef, useState, type PointerEvent } from "react";
import type { SwipeAnswer } from "@/lib/swipes/contract";

/** Смещение, после которого отпущенная карточка засчитывается ответом; короткое касание не отвечает. */
export const SWIPE_THRESHOLD = 96;
const DEMO_STEP_MS = 450;

export type Offset = { x: number; y: number };

const atRest: Offset = { x: 0, y: 0 };

/** Показ жеста: вправо, влево, вверх — каждый раз с возвратом. */
const demoSteps: Offset[] = [
  { x: 80, y: 0 },
  atRest,
  { x: -80, y: 0 },
  atRest,
  { x: 0, y: -80 },
  atRest,
];

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

export type SwipeDrag = {
  offset: Offset;
  dragging: boolean;
  /** Куда клонится карточка, пока её тянут или показывают жест. */
  leaning: SwipeAnswer | null;
  /** 0…1: насколько близко отпускание к ответу. */
  pull: number;
};

/**
 * Свайп в любой части игрового экрана: handlers вешаются на весь экран, карточка только
 * рисует offset. Нажатия на кнопки и ссылки жестом не считаются. Смещение сбрасывается при
 * любом отпускании: улетает карточка за счёт exitTo, а если ответ не дошёл до сервера,
 * она возвращается в центр, и следующее касание ответом не засчитается.
 */
export function useSwipeDrag({
  enabled,
  onAnswer,
}: {
  enabled: boolean;
  onAnswer: (answer: SwipeAnswer) => void;
}) {
  const start = useRef<Offset | null>(null);
  const demoTimers = useRef<ReturnType<typeof setTimeout>[]>([]);
  const [offset, setOffset] = useState<Offset>(atRest);
  const [dragging, setDragging] = useState(false);
  const [demo, setDemo] = useState(false);

  useEffect(() => () => demoTimers.current.forEach(clearTimeout), []);

  function endDrag() {
    start.current = null;
    setDragging(false);
    setOffset(atRest);
  }

  const handlers = {
    onPointerDown(event: PointerEvent<HTMLElement>) {
      if (!enabled || demo) return;
      if (event.pointerType === "mouse" && event.button !== 0) return;
      if ((event.target as HTMLElement).closest("button, a")) return;
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

  /** Один раз показывает жест: карточка сама качается со штампами, затем onDone. */
  function playDemo(onDone: () => void) {
    setDemo(true);
    demoTimers.current = [
      ...demoSteps.map((step, index) => setTimeout(() => setOffset(step), index * DEMO_STEP_MS)),
      setTimeout(() => {
        setDemo(false);
        onDone();
      }, demoSteps.length * DEMO_STEP_MS),
    ];
  }

  const leaning = dragging || demo ? leaningTo(offset) : null;
  const drag: SwipeDrag = {
    offset,
    dragging,
    leaning,
    pull: Math.min(1, pullOf(offset, leaning) / SWIPE_THRESHOLD),
  };
  return { drag, handlers, playDemo };
}
