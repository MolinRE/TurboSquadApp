"use client";

import { useEffect, useEffectEvent, useState } from "react";

/**
 * Текст, который «печатается» за durationMs, после чего вызывается onDone. Место под весь текст
 * занято сразу, чтобы блок не рос во время печати. При «уменьшении движения» текст виден
 * целиком сразу, а onDone всё равно ждёт durationMs: время на прочтение одинаково для всех.
 */
export function TypedText({
  text,
  durationMs,
  onDone,
}: {
  text: string;
  durationMs: number;
  onDone: () => void;
}) {
  const [shown, setShown] = useState(0);
  const finish = useEffectEvent(onDone);

  useEffect(() => {
    const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    const startedAt = performance.now();
    let frame = requestAnimationFrame(function tick(now) {
      const progress = Math.min(1, (now - startedAt) / durationMs);
      setShown(reduceMotion ? text.length : Math.ceil(progress * text.length));
      if (progress < 1) frame = requestAnimationFrame(tick);
      else finish();
    });
    return () => cancelAnimationFrame(frame);
  }, [text, durationMs]);

  const typing = shown < text.length;
  return (
    <>
      <span className="sr-only">{text}</span>
      <span aria-hidden>
        {text.slice(0, shown)}
        {typing ? (
          <span className="ml-px inline-block h-[1em] w-0.5 translate-y-[0.15em] animate-pulse bg-brand" />
        ) : null}
        <span className="invisible">{text.slice(shown)}</span>
      </span>
    </>
  );
}
