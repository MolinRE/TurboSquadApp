"use client";

import { useEffect, useEffectEvent, useRef, useState, type ReactNode } from "react";
import { ArrowLeft, ArrowRight, ArrowUp, Hand, Zap } from "lucide-react";
import { cn } from "cn";
import { Button } from "@/components/ui/button";
import { Countdown } from "@/components/game/countdown";
import { ScalesPanel } from "@/components/game/scale-meter";
import { Stopwatch } from "@/components/game/stopwatch";
import { swipesApi } from "@/lib/swipes/api";
import type {
  AnswerOutcome,
  CycleInfo,
  ShiftCard,
  ShiftMode,
  ShiftState,
  SwipeAnswer,
} from "@/lib/swipes/contract";
import { DeckProgress } from "./deck-progress";
import { ExplanationText, SourceLine, VerdictLabel } from "./explanation";
import { formatSeconds } from "./format";
import { ModeChoice } from "./mode-choice";
import { ShiftSummary } from "./shift-summary";
import { SWIPE_EXIT_MS, SwipeCard } from "./swipe-card";
import { useSwipeDrag } from "./use-swipe-drag";

/** Пауза после верного ответа: успеть взглянуть на Вопрос и Пояснение. В Циклах 2–3 её нет. */
const CORRECT_PAUSE_MS = 2500;
const GESTURE_HINT_KEY = "turbo-brigada:swipes:gesture-hint-seen";
/** Меньше этой высоты фон-подсказка не помещается целиком и не показывается. */
const HINT_MIN_HEIGHT = 128;

type Phase =
  | { kind: "choosing" }
  | { kind: "loading" }
  | { kind: "loadFailed"; message: string }
  /** Карточка на экране: печатается или ждёт ответа. */
  | { kind: "playing" }
  | { kind: "sending"; exitTo: SwipeAnswer }
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

/** Показ жеста — один раз на браузер; если хранилище недоступно, не показываем. */
function takeGestureHint() {
  try {
    if (localStorage.getItem(GESTURE_HINT_KEY)) return false;
    localStorage.setItem(GESTURE_HINT_KEY, "1");
    return true;
  } catch {
    return false;
  }
}

/** В Циклах 2–3 после верного ответа сразу следующая карточка: темп важнее паузы. */
function skipsPause(shift: ShiftState, outcome: AnswerOutcome) {
  return outcome.verdict === "correct" && (shift.cycle?.number ?? 0) >= 2;
}

/**
 * Смена на свайпах. Правила и время считает сервер (сейчас подменный модуль), экран показывает.
 * Сначала выбор Режима. Формулировка печатается, затем появляются варианты и идёт секундомер
 * или, в Циклах, обратный отсчёт. После ответа — Пояснение: после ошибки, «Не знаю» и
 * «Время вышло» — по «Понятно», после верного — через паузу (в Циклах 2–3 без неё).
 * Ошибку «В своём темпе» сервер возвращает Повтором; в итоге — «Что повторить» и Работа
 * над ошибками. Свайп работает в любой части экрана.
 */
export function SwipeShift() {
  const [shift, setShift] = useState<ShiftState | null>(null);
  const [phase, setPhase] = useState<Phase>({ kind: "choosing" });
  const [last, setLast] = useState<LastAnswer | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [timing, setTiming] = useState<Timing>(notStarted);
  /** Номер показа карточки: одна и та же карточка при Повторе — новый показ. */
  const [turn, setTurn] = useState(0);

  const ready = timing.startedAt !== null;
  const { drag, handlers, playDemo } = useSwipeDrag({
    enabled: phase.kind === "playing" && ready,
    onAnswer: answerCard,
  });

  function showCard(state: ShiftState) {
    setShift(state);
    setTurn((value) => value + 1);
    setTiming(notStarted);
    setPhase({ kind: "playing" });
  }

  function load(request: () => Promise<ShiftState>) {
    setPhase({ kind: "loading" });
    setLast(null);
    setActionError(null);
    request().then(showCard, (error) => setPhase({ kind: "loadFailed", message: messageOf(error) }));
  }

  function choose(mode: ShiftMode) {
    load(() => swipesApi.startShift(mode));
  }

  function nextCycle() {
    if (shift) load(() => swipesApi.startNextCycle(shift.shiftId));
  }

  function workOnMistakes() {
    if (shift) load(() => swipesApi.startWorkOnMistakes(shift.shiftId));
  }

  function restart() {
    setShift(null);
    setLast(null);
    setPhase({ kind: "choosing" });
  }

  function startTimer() {
    setTiming({ startedAt: performance.now(), stoppedAt: null });
  }

  function onCardReady() {
    if (takeGestureHint()) playDemo(startTimer);
    else startTimer();
  }

  async function submit(exitTo: SwipeAnswer, request: (card: ShiftCard, shiftId: string) => Promise<AnswerOutcome>) {
    if (phase.kind !== "playing" || !ready || !shift?.card) return;
    const card = shift.card;
    setPhase({ kind: "sending", exitTo });
    setTiming((value) => ({ ...value, stoppedAt: performance.now() }));
    setActionError(null);
    try {
      const [outcome] = await Promise.all([request(card, shift.shiftId), wait(SWIPE_EXIT_MS)]);
      setLast({ card, outcome });
      setShift(outcome.shift);
      if (skipsPause(shift, outcome)) advance(outcome.shift);
      else setPhase({ kind: "feedback" });
    } catch (error) {
      setActionError(`Ответ не отправлен: ${messageOf(error)}. Попробуйте ещё раз.`);
      setTiming((value) => ({ ...value, stoppedAt: null }));
      setPhase({ kind: "playing" });
    }
  }

  function answerCard(answer: SwipeAnswer) {
    void submit(answer, (card, shiftId) => swipesApi.answer(shiftId, card.questionId, answer));
  }

  function timeOut() {
    void submit("unknown", (card, shiftId) => swipesApi.timeOut(shiftId, card.questionId));
  }

  /** Следующая карточка или итог, если Смена закончилась. */
  function advance(state: ShiftState) {
    if (state.status !== "running") {
      setPhase({ kind: "finished" });
      return;
    }
    setPhase({ kind: "advancing" });
    setActionError(null);
    swipesApi.showNextCard(state.shiftId).then(showCard, (error) => {
      setActionError(`Следующая карточка не пришла: ${messageOf(error)}. Попробуйте ещё раз.`);
      setPhase({ kind: "feedback" });
    });
  }

  function proceed() {
    if (phase.kind === "feedback" && shift) advance(shift);
  }

  const onKeyDown = useEffectEvent((event: KeyboardEvent) => {
    if (event.repeat || event.altKey || event.ctrlKey || event.metaKey) return;
    const answer = keyAnswers[event.key];
    if (phase.kind === "playing" && answer) {
      event.preventDefault();
      answerCard(answer);
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

  if (phase.kind === "choosing") return <ModeChoice onChoose={choose} />;

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
    return (
      <ShiftSummary
        shift={shift}
        onRestart={restart}
        onNextCycle={nextCycle}
        onWorkOnMistakes={workOnMistakes}
      />
    );
  }

  const showingFeedback = (phase.kind === "feedback" || phase.kind === "advancing") && last;
  const card = shift.card;
  const dragEnabled = phase.kind === "playing" && ready;

  return (
    <div
      {...handlers}
      className={cn(
        "flex flex-1 flex-col gap-3",
        dragEnabled && "cursor-grab touch-none select-none active:cursor-grabbing",
      )}
    >
      {shift.cycle ? <CycleBadge cycle={shift.cycle} /> : null}
      <ScalesPanel scales={shift.scales} changes={last?.outcome.scaleChanges} />

      {showingFeedback ? (
        <FeedbackPanel
          shift={shift}
          last={last}
          busy={phase.kind === "advancing"}
          onContinue={proceed}
        />
      ) : card ? (
        <>
          <CardStack remaining={shift.progress.total - shift.progress.done}>
            <SwipeCard
              key={turn}
              card={card}
              drag={drag}
              exitTo={phase.kind === "sending" ? phase.exitTo : null}
              onReady={onCardReady}
              header={
                <div className="flex items-center gap-3">
                  <DeckProgress progress={shift.progress} isRepeat={card.isRepeat} />
                  {card.timeLimitMs === null ? (
                    <Stopwatch startedAt={timing.startedAt} stoppedAt={timing.stoppedAt} />
                  ) : (
                    <Countdown
                      limitMs={card.timeLimitMs}
                      startedAt={timing.startedAt}
                      stoppedAt={timing.stoppedAt}
                      onExpire={timeOut}
                    />
                  )}
                </div>
              }
            />
          </CardStack>
          <AnswerButtons
            card={card}
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

      {!showingFeedback && card ? <SwipeAnywhereHint /> : null}
    </div>
  );
}

/**
 * Фон пустой зоны под кнопками: свайпать можно здесь, не обязательно тянуть саму карточку.
 * Касания проходят сквозь него к экрану. Если места мало, не показывается, чтобы не обрезаться.
 */
function SwipeAnywhereHint() {
  const zone = useRef<HTMLDivElement>(null);
  const [fits, setFits] = useState(false);

  useEffect(() => {
    const element = zone.current;
    if (!element) return;
    const observer = new ResizeObserver(([entry]) => setFits(entry.contentRect.height >= HINT_MIN_HEIGHT));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  return (
    <div ref={zone} aria-hidden className="pointer-events-none relative min-h-0 flex-1">
      {fits ? (
        <div className="absolute inset-0 flex flex-col items-center justify-center gap-2 text-muted-foreground/45">
          <ArrowUp className="size-5" />
          <div className="flex items-center gap-3">
            <ArrowLeft className="size-5" />
            <Hand className="size-10 motion-safe:animate-[swipe-sway_2.4s_ease-in-out_infinite]" />
            <ArrowRight className="size-5" />
          </div>
          <p className="text-xs font-bold">Смахивайте в любом месте экрана</p>
        </div>
      ) : null}
    </div>
  );
}

function CycleBadge({ cycle }: { cycle: CycleInfo }) {
  return (
    <p className="flex items-center gap-1.5 text-sm font-bold">
      <Zap className="size-4 text-brand" aria-hidden />
      Цикл {cycle.number} из {cycle.timeLimitsMs.length}
      <span className="font-normal text-muted-foreground">
        · {cycle.timeLimitsMs[cycle.number - 1] / 1000} с на карточку
      </span>
    </p>
  );
}

/** Колода: под текущей карточкой видны края следующих, пока они есть. */
function CardStack({ remaining, children }: { remaining: number; children: ReactNode }) {
  return (
    <div className="relative mb-3">
      {remaining > 2 ? (
        <div aria-hidden className="absolute inset-x-6 top-4 -bottom-3 rounded-xl bg-card/60" />
      ) : null}
      {remaining > 1 ? (
        <div aria-hidden className="absolute inset-x-3 top-2 -bottom-1.5 rounded-xl bg-card/85 shadow-sm" />
      ) : null}
      {children}
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
  shift,
  last,
  busy,
  onContinue,
}: {
  shift: ShiftState;
  last: LastAnswer;
  busy: boolean;
  onContinue: () => void;
}) {
  const { card, outcome } = last;
  const correct = outcome.verdict === "correct";
  const finished = shift.status !== "running";
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
      <DeckProgress progress={shift.progress} />
      <div className="flex items-center justify-between gap-2">
        <VerdictLabel verdict={outcome.verdict} timedOut={outcome.timedOut} className="text-lg" />
        {outcome.timedOut ? null : (
          <span className="text-xs font-bold text-muted-foreground tabular-nums">
            Ответ за {formatSeconds(outcome.elapsedMs)} с
          </span>
        )}
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
