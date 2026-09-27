"use client";

import Link from "next/link";
import { useCallback, useEffect, useEffectEvent, useState } from "react";
import { ArrowLeft, ArrowRight, ArrowUp, CircleCheck, CircleX } from "lucide-react";
import { cn } from "cn";
import { Button } from "@/components/ui/button";
import { Progress } from "@/components/ui/progress";
import { ScalesPanel } from "@/components/game/scale-meter";
import { Stopwatch } from "@/components/game/stopwatch";
import { swipesApi } from "@/lib/swipes/api";
import type {
  AnswerOutcome,
  ShiftCard,
  ShiftProgress,
  ShiftState,
  SwipeAnswer,
} from "@/lib/swipes/contract";
import { ExplanationText, SourceLine, VerdictLabel } from "./explanation";
import { SWIPE_EXIT_MS, SwipeCard } from "./swipe-card";

/** Пауза после верного ответа: успеть взглянуть на Вопрос и Пояснение. */
const CORRECT_PAUSE_MS = 2500;

type Phase =
  | { kind: "loading" }
  | { kind: "loadFailed"; message: string }
  /** Карточка на экране: печатается или ждёт ответа. */
  | { kind: "playing" }
  | { kind: "sending"; answer: SwipeAnswer }
  | { kind: "feedback" }
  /** Просим у сервера следующую карточку. */
  | { kind: "advancing" }
  | { kind: "finished" };

type LastAnswer = { card: ShiftCard; outcome: AnswerOutcome };

/** Отметки performance.now(): отсчёт идёт от конца печати формулировки до ответа. */
type Timing = { startedAt: number | null; stoppedAt: number | null };

const notStarted: Timing = { startedAt: null, stoppedAt: null };

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

function formatSeconds(ms: number) {
  return (ms / 1000).toLocaleString("ru-RU", {
    minimumFractionDigits: 1,
    maximumFractionDigits: 1,
  });
}

/**
 * Смена на свайпах. Правила и время считает сервер (сейчас подменный модуль), экран показывает.
 * Формулировка печатается, затем появляются варианты и идёт секундомер. После ответа —
 * Пояснение: после верного следующая карточка приходит сама через паузу, после ошибки
 * и «Не знаю» — по «Понятно».
 */
export function SwipeShift() {
  const [shift, setShift] = useState<ShiftState | null>(null);
  const [phase, setPhase] = useState<Phase>({ kind: "loading" });
  const [last, setLast] = useState<LastAnswer | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [timing, setTiming] = useState<Timing>(notStarted);
  /** Номер показа карточки: одна и та же карточка при Повторе — новый показ. */
  const [turn, setTurn] = useState(0);

  const showCard = useCallback((state: ShiftState) => {
    setShift(state);
    setTurn((value) => value + 1);
    setTiming(notStarted);
    setPhase({ kind: "playing" });
  }, []);

  const loadShift = useCallback(
    (isActive: () => boolean) => {
      swipesApi.startShift().then(
        (state) => {
          if (isActive()) showCard(state);
        },
        (error) => {
          if (isActive()) setPhase({ kind: "loadFailed", message: messageOf(error) });
        },
      );
    },
    [showCard],
  );

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
    setActionError(null);
    loadShift(() => true);
  }

  const ready = timing.startedAt !== null;

  function startTimer() {
    setTiming({ startedAt: performance.now(), stoppedAt: null });
  }

  async function answerCard(answer: SwipeAnswer) {
    if (phase.kind !== "playing" || !ready || !shift?.card) return;
    const card = shift.card;
    setPhase({ kind: "sending", answer });
    setTiming((value) => ({ ...value, stoppedAt: performance.now() }));
    setActionError(null);
    try {
      const [outcome] = await Promise.all([
        swipesApi.answer(shift.shiftId, card.questionId, answer),
        wait(SWIPE_EXIT_MS),
      ]);
      setLast({ card, outcome });
      setShift(outcome.shift);
      setPhase({ kind: "feedback" });
    } catch (error) {
      setActionError(`Ответ не отправлен: ${messageOf(error)}. Попробуйте ещё раз.`);
      setTiming((value) => ({ ...value, stoppedAt: null }));
      setPhase({ kind: "playing" });
    }
  }

  function proceed() {
    if (phase.kind !== "feedback" || !shift) return;
    if (shift.status !== "running") {
      setPhase({ kind: "finished" });
      return;
    }
    setPhase({ kind: "advancing" });
    setActionError(null);
    swipesApi.showNextCard(shift.shiftId).then(showCard, (error) => {
      setActionError(`Следующая карточка не пришла: ${messageOf(error)}. Попробуйте ещё раз.`);
      setPhase({ kind: "feedback" });
    });
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

  const showingFeedback = phase.kind === "feedback" || phase.kind === "advancing";

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-end gap-3">
        <ShiftProgressBar progress={shift.progress} />
        <Stopwatch startedAt={timing.startedAt} stoppedAt={timing.stoppedAt} />
      </div>
      <ScalesPanel scales={shift.scales} changes={last?.outcome.scaleChanges} />

      {showingFeedback && last ? (
        <FeedbackPanel
          last={last}
          finished={shift.status !== "running"}
          busy={phase.kind === "advancing"}
          onContinue={proceed}
        />
      ) : shift.card ? (
        <>
          <SwipeCard
            key={turn}
            card={shift.card}
            exitTo={phase.kind === "sending" ? phase.answer : null}
            disabled={phase.kind !== "playing" || !ready}
            onReady={startTimer}
            onAnswer={answerCard}
          />
          <AnswerButtons
            card={shift.card}
            ready={ready}
            disabled={phase.kind !== "playing"}
            onAnswer={answerCard}
          />
        </>
      ) : null}

      {actionError ? (
        <p role="alert" className="text-sm text-danger">
          {actionError}
        </p>
      ) : null}
    </div>
  );
}

function ShiftProgressBar({ progress }: { progress: ShiftProgress }) {
  return (
    <div className="flex flex-1 flex-col gap-1.5">
      <div className="flex justify-between text-xs font-bold">
        <span>Вопросы</span>
        <span className="tabular-nums">
          {progress.done} из {progress.total}
        </span>
      </div>
      <Progress value={(progress.done / progress.total) * 100} className="h-1.5" />
    </div>
  );
}

/**
 * «Не знаю» — тонкой строкой над вариантами: свайп для него тоже вверх. Пока формулировка
 * печатается, блок невидим, но место занимает, чтобы экран не прыгал.
 */
function AnswerButtons({
  card,
  ready,
  disabled,
  onAnswer,
}: {
  card: ShiftCard;
  ready: boolean;
  disabled: boolean;
  onAnswer: (answer: SwipeAnswer) => void;
}) {
  const sideClass =
    "h-auto min-h-16 min-w-0 gap-2 bg-card px-4 py-3 text-base font-bold break-words hyphens-auto whitespace-normal";
  return (
    <div
      inert={!ready}
      className={cn("grid gap-2 transition-opacity duration-300", ready ? "opacity-100" : "opacity-0")}
    >
      <Button
        variant="outline"
        disabled={disabled}
        onClick={() => onAnswer("unknown")}
        className="h-9 w-full gap-1.5 bg-card text-xs font-bold text-muted-foreground"
      >
        <ArrowUp aria-hidden />
        Не знаю
      </Button>
      <div className="grid grid-cols-2 gap-2">
        <Button
          variant="outline"
          disabled={disabled}
          onClick={() => onAnswer("left")}
          className={cn(sideClass, "justify-start text-left")}
        >
          <ArrowLeft className="size-5" aria-hidden />
          {card.leftLabel}
        </Button>
        <Button
          variant="outline"
          disabled={disabled}
          onClick={() => onAnswer("right")}
          className={cn(sideClass, "justify-end text-right")}
        >
          {card.rightLabel}
          <ArrowRight className="size-5" aria-hidden />
        </Button>
      </div>
    </div>
  );
}

function FeedbackPanel({
  last,
  finished,
  busy,
  onContinue,
}: {
  last: LastAnswer;
  finished: boolean;
  busy: boolean;
  onContinue: () => void;
}) {
  const { card, outcome } = last;
  const correct = outcome.verdict === "correct";
  const correctLabel = outcome.correctSide === "right" ? card.rightLabel : card.leftLabel;
  const CorrectArrow = outcome.correctSide === "right" ? ArrowRight : ArrowLeft;
  const continueLater = useEffectEvent(onContinue);

  useEffect(() => {
    if (!correct) return;
    const id = setTimeout(() => continueLater(), CORRECT_PAUSE_MS);
    return () => clearTimeout(id);
  }, [correct]);

  return (
    <section aria-live="polite" className="flex flex-col gap-3 rounded-xl bg-card p-5">
      <div className="flex items-center justify-between gap-2">
        <VerdictLabel verdict={outcome.verdict} className="text-lg" />
        <span className="text-xs font-bold text-muted-foreground tabular-nums">
          Ответ за {formatSeconds(outcome.elapsedMs)} с
        </span>
      </div>
      <p className="text-sm text-muted-foreground">{card.statement}</p>
      {correct ? null : (
        <p className="flex items-center gap-1.5 text-sm font-bold">
          Верный ответ:
          <CorrectArrow className="size-4 text-brand" aria-hidden />
          {correctLabel}
        </p>
      )}
      <p className="text-base leading-relaxed">
        <ExplanationText explanation={outcome.explanation} />
      </p>
      <SourceLine source={outcome.explanation.source} />
      <Button
        autoFocus
        disabled={busy}
        onClick={onContinue}
        className="relative mt-1 h-12 overflow-hidden text-base font-bold"
      >
        {correct ? (
          <span
            aria-hidden
            className="absolute inset-x-0 bottom-0 h-1 origin-left animate-[pause-bar_linear_forwards] bg-brand-light"
            style={{ animationDuration: `${CORRECT_PAUSE_MS}ms` }}
          />
        ) : null}
        {finished ? "К итогу" : correct ? "Дальше" : "Понятно"}
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
        {result.averageAnswerMs !== null ? (
          <p className="text-sm">
            Среднее время ответа:{" "}
            <b className="tabular-nums">{formatSeconds(result.averageAnswerMs)} с</b>
          </p>
        ) : null}
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
