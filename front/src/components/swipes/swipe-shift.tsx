"use client";

import { useEffect, useEffectEvent, useRef, useState, useSyncExternalStore, type ReactNode } from "react";
import Link from "next/link";
import { ArrowLeft, ArrowRight, ArrowUp, Hand, Zap } from "lucide-react";
import { cn } from "cn";
import { Button } from "@/components/ui/button";
import { Countdown } from "@/components/game/countdown";
import { ScalesPanel } from "@/components/game/scale-meter";
import { Stopwatch } from "@/components/game/stopwatch";
import { ApiError } from "@/lib/api";
import { swipesApi } from "@/lib/swipes/api";
import type {
  AnswerOutcome,
  CycleInfo,
  ShiftCard,
  ShiftMode,
  ShiftRejection,
  ShiftState,
  SwipeAnswer,
} from "@/lib/swipes/contract";
import { forgetShift, noSubscription, saveShift, savedShift } from "@/lib/swipes/saved-shift";
import { DeckProgress } from "./deck-progress";
import { ExplanationText, SourceLine, VerdictLabel } from "./explanation";
import { formatSeconds } from "./format";
import { ModeChoice } from "./mode-choice";
import { ShiftSummary } from "./shift-summary";
import { SwipeReview } from "./swipe-review";
import { SWIPE_EXIT_MS, SwipeCard } from "./swipe-card";
import { useSwipeDrag } from "./use-swipe-drag";

/** Пауза после верного ответа: успеть взглянуть на Вопрос и Пояснение. В Циклах 2–3 её нет. */
const CORRECT_PAUSE_MS = 2500;
const GESTURE_HINT_KEY = "turbo-brigada:swipes:gesture-hint-seen";
/** Меньше этой высоты фон-подсказка не помещается целиком и не показывается. */
const HINT_MIN_HEIGHT = 128;
/**
 * Отказы 409, после которых ответ уже засчитан или Смена ушла дальше: например, первый ответ
 * дошёл до сервера, а его результат потерялся в сети. Экран берёт состояние с сервера.
 */
const SETTLED_REJECTIONS: ShiftRejection[] = ["StaleCard", "CardNotShown", "ShiftNotRunning"];
const LOADING_DECK = "Собираем колоду…";
const RESUMING = "Возвращаемся к Смене…";

type Phase =
  /** Открыли экран: продолжаем незаконченную Смену этого браузера, если она есть, иначе выбор Режима. */
  | { kind: "restoring" }
  | { kind: "choosing" }
  | { kind: "loading"; label: string }
  | { kind: "loadFailed"; message: string; retry: () => void }
  /** Карточка на экране: печатается или ждёт ответа. */
  | { kind: "playing" }
  | { kind: "sending"; exitTo: SwipeAnswer }
  | { kind: "feedback" }
  /** Просим у сервера следующую карточку. */
  | { kind: "advancing" }
  | { kind: "finished" };

type LastAnswer = { card: ShiftCard; outcome: AnswerOutcome };

/** Ответ на карточку; «Время вышло» — отдельная операция, засчитывается как «Не знаю». */
type CardAnswer = SwipeAnswer | "timeOut";

/** Ошибка действия; resend — ответ, который не дошёл до сервера: кнопка отправляет его ещё раз. */
type ActionError = { message: string; resend: CardAnswer | null };

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

/** Текст ошибки из единого формата ошибок API; fetch без ответа сервера — нет связи. */
function messageOf(error: unknown) {
  if (!(error instanceof ApiError)) return "Не удалось связаться с сервером";
  return error.status === 403 ? "Смену на свайпах проходит Проводник — войдите его демо-аккаунтом" : error.message;
}

function alreadySettled(error: unknown) {
  return (
    error instanceof ApiError &&
    error.status === 409 &&
    SETTLED_REJECTIONS.some((reason) => reason === error.payload?.reason)
  );
}

/** Смены нет или она чужая: например, в этом браузере потом входил другой проводник. */
function shiftGone(error: unknown) {
  return error instanceof ApiError && error.status === 404;
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
 * Смена на свайпах через API: правила и время считает сервер, экран показывает.
 * Сначала выбор Режима, а незаконченную Смену экран продолжает и после перезагрузки.
 * Формулировка печатается, затем появляются варианты и идёт секундомер или, в Циклах,
 * обратный отсчёт. Карточка улетает сразу после свайпа, вердикт приходит с ответом сервера;
 * не дошёл ответ — карточка возвращается, и его можно отправить ещё раз. После ответа —
 * Пояснение: после ошибки, «Не знаю» и «Время вышло» — по «Понятно», после верного — через
 * паузу (в Циклах 2–3 без неё). Ошибку «В своём темпе» сервер возвращает Повтором; в итоге —
 * «Что повторить» и Работа над ошибками. Свайп работает в любой части экрана.
 */
export function SwipeShift() {
  const [shift, setShift] = useState<ShiftState | null>(null);
  const [phase, setPhase] = useState<Phase>({ kind: "restoring" });
  const [last, setLast] = useState<LastAnswer | null>(null);
  const [actionError, setActionError] = useState<ActionError | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [timing, setTiming] = useState<Timing>(notStarted);
  /** Номер показа карточки: одна и та же карточка при Повторе — новый показ. */
  const [turn, setTurn] = useState(0);
  /** Ответ в пути: второй свайп или нажатие до смены фазы не отправляет второй ответ. */
  const sending = useRef(false);
  /** Сохранённая незаконченная Смена; undefined — пока неизвестно (отрисовка на сервере). */
  const saved = useSyncExternalStore(noSubscription, savedShift, () => undefined);

  const ready = timing.startedAt !== null;
  const { drag, handlers, playDemo } = useSwipeDrag({
    enabled: phase.kind === "playing" && ready,
    onAnswer: answerCard,
  });

  function clearMessages() {
    setActionError(null);
    setNotice(null);
  }

  function showLoading(label: string) {
    setPhase({ kind: "loading", label });
    setLast(null);
    clearMessages();
  }

  function showCard(state: ShiftState) {
    setShift(state);
    setTurn((value) => value + 1);
    setTiming(notStarted);
    setPhase({ kind: "playing" });
  }

  function finish(state: ShiftState) {
    forgetShift();
    setShift(state);
    setPhase({ kind: "finished" });
  }

  /** Новая Смена: старт, следующий Цикл или Работа над ошибками. */
  function load(request: () => Promise<ShiftState>) {
    showLoading(LOADING_DECK);
    request().then(
      (state) => {
        saveShift(state.shiftId);
        showCard(state);
      },
      (error) =>
        setPhase({
          kind: "loadFailed",
          message: `Не удалось начать Смену: ${messageOf(error)}`,
          retry: () => load(request),
        }),
    );
  }

  /** Экран загрузки и состояние Смены с сервера, например когда ответ уже засчитан. */
  function resync(shiftId: string, note: string | null) {
    showLoading(RESUMING);
    continueShift(shiftId, note);
  }

  /**
   * Продолжает Смену по состоянию с сервера, не трогая экран до ответа. Если ответ дан, а
   * следующую карточку не просили, просит её: Пояснение к тому ответу уже не показать.
   */
  function continueShift(shiftId: string, note: string | null) {
    swipesApi
      .getShift(shiftId)
      .then((state) => (state.status === "running" && !state.card ? swipesApi.showNextCard(shiftId) : state))
      .then(
        (state) => {
          setNotice(note);
          if (state.status === "running") showCard(state);
          else finish(state);
        },
        (error) => {
          if (shiftGone(error)) {
            restart();
            return;
          }
          setPhase({
            kind: "loadFailed",
            message: `Не удалось вернуться к Смене: ${messageOf(error)}`,
            retry: () => resync(shiftId, note),
          });
        },
      );
  }

  const continueSavedShift = useEffectEvent((shiftId: string) => {
    if (phase.kind === "restoring") continueShift(shiftId, null);
  });

  useEffect(() => {
    if (saved) continueSavedShift(saved);
  }, [saved]);

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
    forgetShift();
    setShift(null);
    setLast(null);
    clearMessages();
    setPhase({ kind: "choosing" });
  }

  function startTimer() {
    setTiming({ startedAt: performance.now(), stoppedAt: null });
  }

  function onCardReady() {
    if (takeGestureHint()) playDemo(startTimer);
    else startTimer();
  }

  function send(shiftId: string, card: ShiftCard, answer: CardAnswer) {
    return answer === "timeOut"
      ? swipesApi.timeOut(shiftId, card.questionId)
      : swipesApi.answer(shiftId, card.questionId, answer);
  }

  async function submit(answer: CardAnswer) {
    if (sending.current || phase.kind !== "playing" || !ready || !shift?.card) return;
    sending.current = true;
    const card = shift.card;
    setPhase({ kind: "sending", exitTo: answer === "timeOut" ? "unknown" : answer });
    // Таймер стоит с первой попытки: после ошибки сети он не идёт дальше и не шлёт «Время вышло» сам.
    setTiming((value) => (value.stoppedAt === null ? { ...value, stoppedAt: performance.now() } : value));
    clearMessages();
    try {
      const [outcome] = await Promise.all([send(shift.shiftId, card, answer), wait(SWIPE_EXIT_MS)]);
      setLast({ card, outcome });
      setShift(outcome.shift);
      if (skipsPause(shift, outcome)) advance(outcome.shift);
      else setPhase({ kind: "feedback" });
    } catch (error) {
      if (shiftGone(error)) {
        restart();
      } else if (alreadySettled(error)) {
        resync(shift.shiftId, "Ответ на прошлую карточку уже был засчитан — продолжаем.");
      } else {
        setActionError({ message: `Ответ не отправлен: ${messageOf(error)}.`, resend: answer });
        setPhase({ kind: "playing" });
      }
    } finally {
      sending.current = false;
    }
  }

  function answerCard(answer: SwipeAnswer) {
    void submit(answer);
  }

  function timeOut() {
    void submit("timeOut");
  }

  /** Следующая карточка или итог, если Смена закончилась. */
  function advance(state: ShiftState) {
    if (state.status !== "running") {
      finish(state);
      return;
    }
    setPhase({ kind: "advancing" });
    setActionError(null);
    swipesApi.showNextCard(state.shiftId).then(showCard, (error) => {
      setActionError({ message: `Следующая карточка не пришла: ${messageOf(error)}. Попробуйте ещё раз.`, resend: null });
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

  if (phase.kind === "restoring") {
    if (saved === undefined) return null;
    if (saved === null) return <ModeChoice onChoose={choose} />;
    return <p className="m-auto text-sm text-muted-foreground">{RESUMING}</p>;
  }

  if (phase.kind === "choosing") return <ModeChoice onChoose={choose} />;

  if (phase.kind === "loadFailed") {
    return (
      <div className="m-auto flex flex-col items-center gap-3 text-center">
        <p role="alert" className="text-sm text-danger">
          {phase.message}
        </p>
        <div className="flex flex-wrap justify-center gap-2">
          <Button onClick={phase.retry}>Попробовать снова</Button>
          <Button variant="outline" onClick={restart} className="bg-card">
            Новая Смена
          </Button>
        </div>
      </div>
    );
  }

  if (phase.kind === "loading" || !shift) {
    return (
      <p className="m-auto text-sm text-muted-foreground">
        {phase.kind === "loading" ? phase.label : LOADING_DECK}
      </p>
    );
  }

  if (phase.kind === "finished") {
    return (
      <div className="flex flex-1 flex-col gap-3">
        <ShiftSummary
          shift={shift}
          onRestart={restart}
          onNextCycle={nextCycle}
          onWorkOnMistakes={workOnMistakes}
        />
        <SwipeReview shiftId={shift.shiftId} embedded />
        <Button asChild variant="outline"><Link href={`/reviews/swipe-${shift.shiftId}`}>Открыть Разбор</Link></Button>
      </div>
    );
  }

  const showingFeedback = (phase.kind === "feedback" || phase.kind === "advancing") && last;
  const card = shift.card;
  const dragEnabled = phase.kind === "playing" && ready;
  const resend = phase.kind === "playing" ? actionError?.resend : null;

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
          <CardStack remaining={shift.progress.total - shift.progress.done} checking={phase.kind === "sending"}>
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
        <div role="alert" className="flex flex-col items-start gap-2 text-sm">
          <p className="text-danger">{actionError.message}</p>
          {resend ? (
            <Button variant="outline" onClick={() => void submit(resend)} className="bg-card">
              Отправить ещё раз
            </Button>
          ) : null}
        </div>
      ) : null}
      {notice ? <p className="text-sm text-muted-foreground">{notice}</p> : null}

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

/**
 * Колода: под текущей карточкой видны края следующих, пока они есть. checking — ответ улетел
 * на сервер: на месте карточки «Сверяем ответ…», с задержкой, чтобы быстрый ответ не мигал.
 */
function CardStack({
  remaining,
  checking,
  children,
}: {
  remaining: number;
  checking: boolean;
  children: ReactNode;
}) {
  return (
    <div className="relative mb-3">
      {remaining > 2 ? (
        <div aria-hidden className="absolute inset-x-6 top-4 -bottom-3 rounded-xl bg-card/60" />
      ) : null}
      {remaining > 1 ? (
        <div aria-hidden className="absolute inset-x-3 top-2 -bottom-1.5 rounded-xl bg-card/85 shadow-sm" />
      ) : null}
      <p
        aria-hidden={!checking}
        className={cn(
          "absolute inset-0 grid place-items-center text-sm font-bold text-muted-foreground opacity-0 transition-opacity",
          checking && "opacity-100 delay-500",
        )}
      >
        Сверяем ответ…
      </p>
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
