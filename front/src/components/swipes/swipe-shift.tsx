"use client";

import Link from "next/link";
import { useCallback, useEffect, useEffectEvent, useState } from "react";
import { ArrowLeft, ArrowRight, ArrowUp, CircleCheck, CircleX } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Progress } from "@/components/ui/progress";
import { ScalesPanel } from "@/components/game/scale-meter";
import { swipesApi } from "@/lib/swipes/api";
import type {
  AnswerOutcome,
  ShiftProgress,
  ShiftState,
  SwipeAnswer,
  SwipeCard as Card,
} from "@/lib/swipes/contract";
import { ExplanationText, SourceLine, VerdictLabel } from "./explanation";
import { SWIPE_EXIT_MS, SwipeCard } from "./swipe-card";

type Phase =
  | { kind: "loading" }
  | { kind: "loadFailed"; message: string }
  | { kind: "playing" }
  | { kind: "sending"; answer: SwipeAnswer }
  | { kind: "feedback" }
  | { kind: "finished" };

type LastAnswer = { card: Card; outcome: AnswerOutcome };

const keyAnswers: Record<string, SwipeAnswer> = {
  ArrowLeft: "left",
  ArrowRight: "right",
  ArrowUp: "unknown",
};

function wait(ms: number) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function messageOf(error: unknown) {
  return error instanceof Error ? error.message : "Что-то пошло не так";
}

/**
 * Смена на свайпах. Правила считает сервер (сейчас подменный модуль), экран показывает:
 * после верного ответа Пояснение видно над следующей карточкой, после ошибки и «Не знаю»
 * карточка ждёт «Понятно».
 */
export function SwipeShift() {
  const [shift, setShift] = useState<ShiftState | null>(null);
  const [phase, setPhase] = useState<Phase>({ kind: "loading" });
  const [last, setLast] = useState<LastAnswer | null>(null);
  const [answerError, setAnswerError] = useState<string | null>(null);
  /** Номер показа карточки: одна и та же карточка при Повторе — новый показ. */
  const [turn, setTurn] = useState(0);

  const loadShift = useCallback((isActive: () => boolean) => {
    swipesApi.startShift().then(
      (state) => {
        if (!isActive()) return;
        setShift(state);
        setPhase({ kind: "playing" });
      },
      (error) => {
        if (isActive()) setPhase({ kind: "loadFailed", message: messageOf(error) });
      },
    );
  }, []);

  useEffect(() => {
    let active = true;
    loadShift(() => active);
    return () => {
      active = false;
    };
  }, [loadShift]);

  function restart() {
    setPhase({ kind: "loading" });
    setLast(null);
    setAnswerError(null);
    loadShift(() => true);
  }

  async function answerCard(answer: SwipeAnswer) {
    if (phase.kind !== "playing" || !shift?.card) return;
    const card = shift.card;
    setPhase({ kind: "sending", answer });
    setAnswerError(null);
    try {
      const [outcome] = await Promise.all([
        swipesApi.answer(shift.shiftId, card.questionId, answer),
        wait(SWIPE_EXIT_MS),
      ]);
      setLast({ card, outcome });
      setShift(outcome.shift);
      setTurn((value) => value + 1);
      const keepGoing = outcome.verdict === "correct" && outcome.shift.status === "running";
      setPhase(keepGoing ? { kind: "playing" } : { kind: "feedback" });
    } catch (error) {
      setAnswerError(messageOf(error));
      setPhase({ kind: "playing" });
    }
  }

  function proceed() {
    if (phase.kind !== "feedback" || !shift) return;
    setPhase(shift.status === "running" ? { kind: "playing" } : { kind: "finished" });
  }

  const onKeyDown = useEffectEvent((event: KeyboardEvent) => {
    if (event.repeat || event.altKey || event.ctrlKey || event.metaKey) return;
    const answer = keyAnswers[event.key];
    if (phase.kind === "playing" && answer) {
      event.preventDefault();
      void answerCard(answer);
    } else if (phase.kind === "feedback" && (event.key === "Enter" || event.key === " ")) {
      event.preventDefault();
      proceed();
    }
  });

  useEffect(() => {
    const listener = (event: KeyboardEvent) => onKeyDown(event);
    window.addEventListener("keydown", listener);
    return () => window.removeEventListener("keydown", listener);
  }, []);

  if (phase.kind === "loadFailed") {
    return (
      <div className="m-auto flex flex-col items-center gap-3 text-center">
        <p className="text-sm text-danger">Не удалось начать Смену: {phase.message}</p>
        <Button onClick={restart}>Попробовать снова</Button>
      </div>
    );
  }

  if (phase.kind === "loading" || !shift) {
    return <p className="m-auto text-sm text-muted-foreground">Собираем колоду…</p>;
  }

  if (phase.kind === "finished") {
    return <ShiftSummary shift={shift} onRestart={restart} />;
  }

  const showCorrectStrip = phase.kind !== "feedback" && last?.outcome.verdict === "correct";

  return (
    <div className="flex flex-1 flex-col gap-3">
      <ShiftProgressBar progress={shift.progress} />
      <ScalesPanel scales={shift.scales} changes={last?.outcome.scaleChanges} />

      {phase.kind === "feedback" && last ? (
        <FeedbackPanel
          last={last}
          finished={shift.status !== "running"}
          onContinue={proceed}
        />
      ) : (
        <>
          {showCorrectStrip ? <CorrectStrip outcome={last.outcome} /> : null}
          <div className="relative min-h-52 flex-1">
            {shift.card ? (
              <SwipeCard
                key={turn}
                card={shift.card}
                exitTo={phase.kind === "sending" ? phase.answer : null}
                disabled={phase.kind !== "playing"}
                onAnswer={answerCard}
              />
            ) : null}
          </div>
          {answerError ? (
            <p role="alert" className="text-sm text-danger">
              Ответ не отправлен: {answerError}. Попробуйте ещё раз.
            </p>
          ) : null}
          {shift.card ? (
            <AnswerButtons
              card={shift.card}
              disabled={phase.kind !== "playing"}
              onAnswer={answerCard}
            />
          ) : null}
        </>
      )}
    </div>
  );
}

function ShiftProgressBar({ progress }: { progress: ShiftProgress }) {
  return (
    <div className="flex items-center gap-3">
      <div className="flex flex-1 flex-col gap-1.5">
        <div className="flex justify-between text-xs font-bold">
          <span>Карточки</span>
          <span className="tabular-nums">
            {progress.done} из {progress.total}
          </span>
        </div>
        <Progress value={(progress.done / progress.total) * 100} className="h-1.5" />
      </div>
    </div>
  );
}

function AnswerButtons({
  card,
  disabled,
  onAnswer,
}: {
  card: Card;
  disabled: boolean;
  onAnswer: (answer: SwipeAnswer) => void;
}) {
  const sideClass =
    "h-auto min-h-14 min-w-0 gap-1.5 bg-card px-2.5 py-2 text-[13px] font-bold break-words hyphens-auto whitespace-normal";
  return (
    <div className="grid grid-cols-[minmax(0,1fr)_auto_minmax(0,1fr)] gap-2">
      <Button
        variant="outline"
        disabled={disabled}
        onClick={() => onAnswer("left")}
        className={`${sideClass} justify-start text-left`}
      >
        <ArrowLeft aria-hidden />
        {card.leftLabel}
      </Button>
      <Button
        variant="outline"
        disabled={disabled}
        onClick={() => onAnswer("unknown")}
        className="h-auto min-h-14 flex-col gap-0.5 bg-card px-3 text-xs font-bold"
      >
        <ArrowUp aria-hidden />
        Не знаю
      </Button>
      <Button
        variant="outline"
        disabled={disabled}
        onClick={() => onAnswer("right")}
        className={`${sideClass} justify-end text-right`}
      >
        {card.rightLabel}
        <ArrowRight aria-hidden />
      </Button>
    </div>
  );
}

function CorrectStrip({ outcome }: { outcome: AnswerOutcome }) {
  return (
    <div aria-live="polite" className="flex flex-col gap-1 rounded-xl bg-brand-soft px-4 py-3 text-sm">
      <p>
        <VerdictLabel verdict="correct" className="mr-1.5 align-[-2px]" />
        <ExplanationText explanation={outcome.explanation} />
      </p>
      <SourceLine source={outcome.source} />
    </div>
  );
}

function FeedbackPanel({
  last,
  finished,
  onContinue,
}: {
  last: LastAnswer;
  finished: boolean;
  onContinue: () => void;
}) {
  const { card, outcome } = last;
  const correctLabel = outcome.correctSide === "right" ? card.rightLabel : card.leftLabel;
  const CorrectArrow = outcome.correctSide === "right" ? ArrowRight : ArrowLeft;

  return (
    <section aria-live="polite" className="flex flex-1 flex-col gap-3 rounded-xl bg-card p-5">
      <VerdictLabel verdict={outcome.verdict} className="text-lg" />
      <p className="text-sm text-muted-foreground">{card.statement}</p>
      {outcome.verdict !== "correct" ? (
        <p className="flex items-center gap-1.5 text-sm font-bold">
          Верный ответ:
          <CorrectArrow className="size-4 text-brand" aria-hidden />
          {correctLabel}
        </p>
      ) : null}
      <p className="text-base leading-relaxed">
        <ExplanationText explanation={outcome.explanation} />
      </p>
      <SourceLine source={outcome.source} />
      <Button autoFocus onClick={onContinue} className="mt-auto h-12 text-base font-bold">
        {finished ? "К итогу" : "Понятно"}
      </Button>
    </section>
  );
}

function ShiftSummary({ shift, onRestart }: { shift: ShiftState; onRestart: () => void }) {
  const result = shift.result!;
  const passed = shift.status === "passed";
  const failedScale = shift.scales.find((scale) => scale.code === result.failedScale);

  return (
    <div className="flex flex-1 flex-col gap-3">
      <section className="flex flex-col items-center gap-2 rounded-xl bg-card p-6 text-center">
        {passed ? (
          <CircleCheck className="size-10 text-brand" aria-hidden />
        ) : (
          <CircleX className="size-10 text-danger" aria-hidden />
        )}
        <h1 className="text-2xl font-extrabold tracking-tight">
          {passed ? "Смена пройдена" : "Срыв смены"}
        </h1>
        {failedScale ? (
          <p className="text-sm text-muted-foreground">
            Шкала «{failedScale.name}» упала до {failedScale.value}
          </p>
        ) : null}
        <p className="text-sm">
          Верно с первого раза:{" "}
          <b className="tabular-nums">
            {result.firstTryCorrect} из {result.total}
          </b>
        </p>
      </section>
      <ScalesPanel scales={shift.scales} />
      <div className="mt-auto grid gap-2">
        <Button onClick={onRestart} className="h-12 text-base font-bold">
          Новая Смена
        </Button>
        <Button asChild variant="outline" className="h-12 bg-card text-base font-bold">
          <Link href="/games">К играм</Link>
        </Button>
      </div>
    </div>
  );
}
