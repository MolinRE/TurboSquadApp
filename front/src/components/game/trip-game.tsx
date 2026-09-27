"use client";

import { useEffect, useRef, useState, type ReactNode } from "react";
import Link from "next/link";
import { Info, LoaderCircle, Mic, Square } from "lucide-react";
import { cn } from "cn";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Countdown } from "@/components/game/countdown";
import { ScalesPanel } from "@/components/game/scale-meter";
import { Stopwatch } from "@/components/game/stopwatch";
import { TripDebriefFacts, TripExplanation, roleStageSummary } from "@/components/game/trip-debrief-facts";
import {
  ApiError,
  chooseProactive,
  chooseVariant,
  getTripDebrief,
  startTrip,
  streamPassengerReply,
  timeOutStep,
  uploadVoice,
  type TripView,
  type TripDebrief,
  type VoiceAttempt,
} from "@/lib/api";

const serviceClasses = [
  ["standard", "Стандарт"],
  ["comfort", "Комфорт"],
  ["business", "Бизнес"],
  ["first", "Первый"],
] as const;

function apiMessage(error: unknown): string {
  return error instanceof ApiError ? error.message : "Не удалось связаться с сервером";
}

/** Строка диалога Рейса: ситуация Шага, реплики проводника и пассажира, служебные отметки. */
type Line =
  | { key: string; kind: "event"; text: string }
  | { key: string; kind: "situation"; eventId: string; stepKey: string; text: string }
  | { key: string; kind: "you"; text: string; attempt: VoiceAttempt | null }
  | { key: string; kind: "passenger"; text: string; clarification: boolean }
  | { key: string; kind: "note"; text: string };
type Situation = Extract<Line, { kind: "situation" }>;
type NewLine = { kind: "you"; text: string; attempt: null } | { kind: "note"; text: string };

/**
 * Дописывает в диалог то, что принёс новый TripView, в порядке разговора: ответ проводника,
 * реплика пассажира и только потом ситуация следующего Шага (пока реплика пишется, её не показываем).
 */
function withTrip(lines: Line[], next: TripView): Line[] {
  let result = lines;
  const attempt = next.voiceAttempt;
  if (attempt) {
    const you: Line = { key: `you-${attempt.attemptId}`, kind: "you", text: attempt.transcript ?? "", attempt };
    result = result.some((line) => line.key === you.key)
      ? result.map((line) => (line.key === you.key ? you : line))
      : [...result, you];
    const replyKey = `reply-${attempt.attemptId}`;
    if (attempt.passengerReply && !result.some((line) => line.key === replyKey)) {
      result = [...result, {
        key: replyKey, kind: "passenger", text: attempt.passengerReply, clarification: attempt.errorCode === "LowConfidence",
      }];
    }
  }
  const step = next.step;
  if (step && !attempt?.pending) {
    const stepKey = `${step.eventId}/${step.stepId}`;
    const last = result.findLast((line): line is Situation => line.kind === "situation");
    if (last?.stepKey !== stepKey) {
      if (last?.eventId !== step.eventId) result = [...result, { key: `event-${result.length}`, kind: "event", text: step.eventTitle }];
      result = [...result, { key: `step-${result.length}`, kind: "situation", eventId: step.eventId, stepKey, text: step.situation }];
    }
  }
  return result;
}

export function TripGame() {
  const [serviceClass, setServiceClass] = useState("business");
  const [trip, setTrip] = useState<TripView | null>(null);
  const [lines, setLines] = useState<Line[]>([]);
  const [changes, setChanges] = useState<Record<string, number>>({});
  const [startScales, setStartScales] = useState<Record<string, number>>({});
  const lastScales = useRef<TripView["scales"] | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [passengerReply, setPassengerReply] = useState("");
  const [replyError, setReplyError] = useState<string | null>(null);
  const [debrief, setDebrief] = useState<TripDebrief | null>(null);
  const [debriefError, setDebriefError] = useState<string | null>(null);
  const bottom = useRef<HTMLDivElement>(null);
  const finishedTripId = trip?.result ? trip.id : null;

  useEffect(() => {
    if (!finishedTripId) return;
    let active = true;
    getTripDebrief(finishedTripId)
      .then((result) => { if (active) setDebrief(result); })
      .catch((reason) => { if (active) setDebriefError(apiMessage(reason)); });
    return () => { active = false; };
  }, [finishedTripId]);

  const streaming = Boolean(trip?.voiceAttempt?.pending);
  useEffect(() => {
    bottom.current?.scrollIntoView({ behavior: "smooth", block: "end" });
  }, [lines.length, streaming]);

  /** Новое состояние Рейса: Шкалы с изменением от прошлого решения и строки диалога. */
  function show(next: TripView) {
    const before = lastScales.current;
    if (!before) {
      setStartScales(Object.fromEntries(next.scales.map((scale) => [scale.code, scale.value])));
    } else {
      const diff: Record<string, number> = {};
      for (const scale of next.scales) {
        const old = before.find((item) => item.code === scale.code);
        if (old && old.value !== scale.value) diff[scale.code] = scale.value - old.value;
      }
      if (Object.keys(diff).length > 0) setChanges(diff);
    }
    lastScales.current = next.scales;
    setTrip(next);
    setLines((current) => withTrip(current, next));
  }

  function addLine(line: NewLine) {
    setLines((current) => [...current, { ...line, key: `${line.kind}-${current.length}` }]);
  }

  function reset() {
    setTrip(null);
    setLines([]);
    setChanges({});
    lastScales.current = null;
    setDebrief(null);
    setError(null);
  }

  async function run(action: () => Promise<TripView>) {
    setBusy(true);
    setError(null);
    setPassengerReply("");
    setReplyError(null);
    setDebrief(null);
    setDebriefError(null);
    try {
      show(await action());
    } catch (reason) {
      if (reason instanceof ApiError && reason.payload?.trip) show(reason.payload.trip);
      setError(apiMessage(reason));
    } finally {
      setBusy(false);
    }
  }

  async function begin() {
    setError(null);
    // Доступ к микрофону — до первого голосового Шага, чтобы запрос браузера не всплыл посреди ответа.
    let micWarning: string | null = null;
    try {
      const probe = await navigator.mediaDevices.getUserMedia({ audio: true });
      probe.getTracks().forEach((track) => track.stop());
    } catch {
      micWarning = "Микрофон недоступен: разрешите его в браузере, иначе голосовые Шаги не пройти.";
    }
    await run(() => startTrip(serviceClass));
    if (micWarning) setError(micWarning);
  }

  async function handleVoiceTrip(next: TripView) {
    show(next);
    setPassengerReply("");
    setReplyError(null);
    const attemptId = next.voiceAttempt?.attemptId;
    if (!attemptId || !next.voiceAttempt?.pending) return;
    setBusy(true);
    try {
      await streamPassengerReply(
        next.id,
        attemptId,
        (token) => setPassengerReply((current) => current + token),
        (streamedTrip) => show(streamedTrip),
      );
    } catch (reason) {
      setReplyError(apiMessage(reason));
    } finally {
      setBusy(false);
      setPassengerReply("");
    }
  }

  if (!trip) {
    return (
      <Card className="mt-4">
        <CardHeader>
          <Badge className="w-fit" variant="secondary">Голосовой Рейс</Badge>
          <CardTitle>Начать Рейс</CardTitle>
          <CardDescription>
            Вы — проводник ВСМ. Пассажиры обращаются к вам, а вы отвечаете голосом, своими словами. От решений
            зависят Лояльность пассажира и Рейтинг безопасности.
          </CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <ul className="flex list-disc flex-col gap-1 pl-5 text-sm text-muted-foreground">
            <li>Нажмите на микрофон, скажите ответ и нажмите ещё раз.</li>
            <li>Начать отвечать нужно, пока не кончился таймер.</li>
            <li>Браузер попросит доступ к микрофону — разрешите его.</li>
          </ul>
          <label className="flex flex-col gap-1 text-sm font-semibold" htmlFor="service-class">
            Класс обслуживания
            <select
              id="service-class"
              className="h-10 rounded-lg border border-input bg-background px-3 font-normal outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
              value={serviceClass}
              onChange={(event) => setServiceClass(event.target.value)}
            >
              {serviceClasses.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
            </select>
          </label>
          {error && <p className="rounded-lg bg-danger-soft p-3 text-sm text-danger">{error}</p>}
          <Button disabled={busy} className="h-11" onClick={() => void begin()}>
            {busy && <LoaderCircle className="animate-spin" aria-hidden />}
            Начать Рейс
          </Button>
        </CardContent>
      </Card>
    );
  }

  const scales = trip.scales.map((scale) => ({
    ...scale, start: startScales[scale.code] ?? scale.value, failureThreshold: scale.min,
  }));
  const clarifying = trip.voiceAttempt?.errorCode === "LowConfidence";

  return (
    <div className="flex flex-col gap-3 pt-2">
      <div className="sticky top-0 z-10 -mx-4 bg-background px-4 pb-1">
        <ScalesPanel scales={scales} changes={changes} />
      </div>
      {error && <p role="alert" className="rounded-lg bg-danger-soft p-3 text-sm text-danger">{error}</p>}
      {lines.length > 0 && (
        <Dialog lines={lines} streaming={streaming} streamedText={passengerReply} clarifying={clarifying} replyError={replyError} />
      )}
      {trip.result ? (
        <Card>
          <CardHeader><Badge variant={trip.status === "arrived" ? "default" : "destructive"}>Рейс завершён</Badge><CardTitle>{trip.result.summary}</CardTitle></CardHeader>
          <CardContent className="flex flex-col gap-4">
            {debriefError && <p role="alert" className="text-sm text-danger">Разбор не загрузился: {debriefError}</p>}
            {debrief && <TripDebriefFacts debrief={debrief} />}
            {debrief && <TripExplanation tripId={trip.id} />}
            {debrief && <Button asChild variant="outline"><Link href={`/reviews/trip-${trip.id}`}>Открыть Разбор</Link></Button>}
            <div className="grid grid-cols-2 gap-2">
              <Button variant="outline" onClick={reset}>Новый Рейс</Button>
              <Button asChild variant="outline"><Link href="/games">К играм</Link></Button>
            </div>
          </CardContent>
        </Card>
      ) : trip.proactiveChoice ? (
        <Card>
          <CardHeader><CardTitle>Что проверить дальше?</CardTitle><CardDescription>{trip.proactiveChoice.situation}</CardDescription></CardHeader>
          <CardContent className="flex flex-col gap-2">
            {trip.proactiveChoice.options.map((option) => (
              <Button key={option.id} variant="outline" className="h-auto min-h-11 justify-start whitespace-normal py-3 text-left" disabled={busy} onClick={() => {
                addLine({ kind: "note", text: `Вы решили: ${option.text}` });
                void run(() => chooseProactive(trip.id, option.id));
              }}>{option.text}</Button>
            ))}
          </CardContent>
        </Card>
      ) : trip.step ? (
        <StepControls
          key={`${trip.step.eventId}/${trip.step.stepId}`}
          trip={trip}
          busy={busy}
          clarifying={clarifying && !streaming}
          onTrip={show}
          onVoiceTrip={handleVoiceTrip}
          onError={setError}
          onLine={addLine}
        />
      ) : null}
      <div ref={bottom} />
    </div>
  );
}

function Dialog({
  lines,
  streaming,
  streamedText,
  clarifying,
  replyError,
}: {
  lines: Line[];
  streaming: boolean;
  streamedText: string;
  clarifying: boolean;
  replyError: string | null;
}) {
  const lastSituation = lines.findLastIndex((line) => line.kind === "situation");
  return (
    <Card>
      <CardContent className="flex flex-col gap-3" aria-live="polite">
        {lines.map((line, index) => {
          if (line.kind === "event") {
            return <Badge key={line.key} variant="secondary" className="h-auto max-w-full self-center whitespace-normal">{line.text}</Badge>;
          }
          if (line.kind === "situation") {
            return <p key={line.key} className={index === lastSituation ? "text-base font-semibold" : "text-sm text-muted-foreground"}>{line.text}</p>;
          }
          if (line.kind === "note") {
            return <p key={line.key} className="self-center text-center text-xs font-semibold text-muted-foreground">{line.text}</p>;
          }
          if (line.kind === "passenger") return <PassengerBubble key={line.key} text={line.text} clarification={line.clarification} />;
          return <YouBubble key={line.key} text={line.text} attempt={line.attempt} />;
        })}
        {streaming && <PassengerBubble text={streamedText} clarification={clarifying} typing />}
        {replyError && <p role="alert" className="rounded-lg bg-danger-soft p-3 text-sm text-danger">Пассажир не ответил: {replyError}</p>}
      </CardContent>
    </Card>
  );
}

/** Реплика пассажира: пузырь слева, как в эскизе ui-kit; уточнение — оранжевым. */
function PassengerBubble({ text, clarification, typing = false }: { text: string; clarification: boolean; typing?: boolean }) {
  return (
    <div className={cn("mr-8 rounded-[4px_16px_16px_16px] px-3.5 py-3", clarification ? "bg-warning-soft" : "bg-brand-soft")}>
      <p className={cn("mb-1 text-[11px] font-bold uppercase tracking-wider", clarification ? "text-warning-foreground" : "text-brand")}>
        {clarification ? "Пассажир уточняет" : "Пассажир"}
      </p>
      {text ? (
        <p className="text-[15px] font-semibold leading-snug">
          {text}
          {typing && <span className="ml-px inline-block h-[1em] w-0.5 translate-y-[0.15em] animate-pulse bg-brand" aria-hidden />}
        </p>
      ) : (
        <p className="text-sm text-muted-foreground">печатает…</p>
      )}
    </div>
  );
}

function attemptStatus(attempt: VoiceAttempt): string {
  if (attempt.applied) return "Ответ применён";
  if (attempt.errorCode === "LowConfidence") return "Laya не уверена: пассажир переспрашивает";
  if (attempt.errorCode === "VoiceDeadlineExceeded") return "Ответ начат после таймера";
  if (attempt.pending && !attempt.errorCode) return "Ответ принят, пассажир отвечает";
  return `Попытка не применена${attempt.errorCode ? ` (${attempt.errorCode})` : ""}`;
}

/** Ответ проводника: пузырь справа; технические данные голосовой попытки — под (i). */
function YouBubble({ text, attempt }: { text: string; attempt: VoiceAttempt | null }) {
  const failed = attempt && !attempt.applied && !attempt.pending && attempt.errorCode !== "LowConfidence";
  const body = (
    <>
      <span className="mb-1 flex items-center justify-between gap-2 text-[11px] font-bold uppercase tracking-wider text-primary-foreground/70">
        Вы
        {attempt && <Info className="size-4" aria-label="Технические данные" />}
      </span>
      <span className="block text-[15px] leading-snug">{!attempt ? text : text ? `«${text}»` : "Речь не распознана"}</span>
      {failed && <span className="mt-1 block text-xs font-semibold text-primary-foreground/80">{attemptStatus(attempt)}</span>}
    </>
  );
  const bubble = "ml-8 rounded-[16px_4px_16px_16px] bg-primary px-3.5 py-3 text-primary-foreground";
  if (!attempt) return <div className={bubble}>{body}</div>;

  const latency = [
    attempt.sttLatencyMs !== null && `распознавание ${attempt.sttLatencyMs}`,
    attempt.layaLatencyMs !== null && `Laya ${attempt.layaLatencyMs}`,
    attempt.llmLatencyMs !== null && `реплика ${attempt.llmLatencyMs}`,
  ].filter(Boolean).join(" · ");
  const stages = attempt.roleStages ? roleStageSummary(attempt.roleStages) : "";
  return (
    <details className={bubble}>
      <summary className="cursor-pointer list-none [&::-webkit-details-marker]:hidden">{body}</summary>
      <ul className="mt-2 flex flex-col gap-0.5 border-t border-primary-foreground/20 pt-2 text-xs text-primary-foreground/80">
        <li className="font-semibold text-primary-foreground">{attemptStatus(attempt)}</li>
        {attempt.confidence !== null && <li>Уверенность Laya: {Math.round(attempt.confidence * 100)}%</li>}
        <li className="tabular-nums">Задержка: {attempt.latencyMs} мс{latency && ` (${latency})`}</li>
        {attempt.score !== null && <li>Вежливость: {Math.round(attempt.score * 100)}%</li>}
        {stages && <li>Ролевая модель: {stages}</li>}
        {attempt.safetyViolation !== null && <li>Риск безопасности: {Math.round(attempt.safetyViolation * 100)}%</li>}
      </ul>
    </details>
  );
}

function StepControls({
  trip,
  busy,
  clarifying,
  onTrip,
  onVoiceTrip,
  onError,
  onLine,
}: {
  trip: TripView;
  busy: boolean;
  clarifying: boolean;
  onTrip: (trip: TripView) => void;
  onVoiceTrip: (trip: TripView) => Promise<void>;
  onError: (message: string | null) => void;
  onLine: (line: NewLine) => void;
}) {
  const step = trip.step!;
  const [answering, setAnswering] = useState(false);
  const expiredFor = useRef<string | null>(null);

  function expire() {
    // Таймер истёк без ответа: сервер перепроверит по своим часам.
    if (!step.expiresAt || expiredFor.current === step.expiresAt) return;
    expiredFor.current = step.expiresAt;
    onError(null);
    onLine({ kind: "note", text: "Время вышло" });
    void timeOutStep(trip.id, step.eventId, step.stepId).then(onTrip).catch((reason) => onError(apiMessage(reason)));
  }

  // Кольцо — время на то, чтобы начать отвечать; пока проводник отвечает или ждёт сервер, его нет.
  const timer = step.expiresAt && step.timerSec && !busy && !answering
    ? <StepCountdown key={step.expiresAt} expiresAt={step.expiresAt} timerSec={step.timerSec} onExpire={expire} />
    : null;

  if (step.answerType === "voice") {
    return <VoiceDock trip={trip} busy={busy} clarifying={clarifying} timer={timer} onVoiceTrip={onVoiceTrip} onError={onError} onAnswering={setAnswering} />;
  }
  return (
    <Card>
      <CardHeader>
        <div className="flex items-center justify-between gap-2"><CardTitle>Что ответите?</CardTitle>{timer}</div>
      </CardHeader>
      <CardContent className="flex flex-col gap-2">
        {step.variants.map((variant) => <Button key={variant.id} variant="outline" className="h-auto min-h-11 justify-start whitespace-normal py-3 text-left" disabled={busy || answering} onClick={() => {
          onError(null);
          setAnswering(true);
          onLine({ kind: "you", text: variant.text, attempt: null });
          void chooseVariant(trip.id, step.eventId, step.stepId, variant.id).then(onTrip).catch((reason) => onError(apiMessage(reason)))
            .finally(() => setAnswering(false));
        }}>{variant.text}</Button>)}
      </CardContent>
    </Card>
  );
}

/** Countdown из ui-kit по серверному дедлайну Шага. */
function StepCountdown({ expiresAt, timerSec, onExpire }: { expiresAt: string; timerSec: number; onExpire: () => void }) {
  const limitMs = timerSec * 1000;
  const [startedAt] = useState(() => performance.now() - (limitMs - (Date.parse(expiresAt) - Date.now())));
  return <Countdown limitMs={limitMs} startedAt={startedAt} stoppedAt={null} onExpire={onExpire} />;
}

/** Голосовой ответ: круглая кнопка микрофона (эскиз ui-kit), статус и таймер или секундомер записи. */
function VoiceDock({
  trip,
  busy,
  clarifying,
  timer,
  onVoiceTrip,
  onError,
  onAnswering,
}: {
  trip: TripView;
  busy: boolean;
  clarifying: boolean;
  timer: ReactNode;
  onVoiceTrip: (trip: TripView) => Promise<void>;
  onError: (message: string | null) => void;
  onAnswering: (answering: boolean) => void;
}) {
  const [recordingSince, setRecordingSince] = useState<number | null>(null);
  const startedAt = useRef(0);
  const [processing, setProcessing] = useState(false);
  const recorder = useRef<MediaRecorder | null>(null);
  const stream = useRef<MediaStream | null>(null);
  const chunks = useRef<Blob[]>([]);
  const recording = recordingSince !== null;

  async function start() {
    onError(null);
    startedAt.current = performance.now();
    onAnswering(true);
    try {
      stream.current = await navigator.mediaDevices.getUserMedia({ audio: true });
      const nextRecorder = new MediaRecorder(stream.current);
      chunks.current = [];
      nextRecorder.ondataavailable = (event) => { if (event.data.size > 0) chunks.current.push(event.data); };
      nextRecorder.onstop = () => {
        const blob = new Blob(chunks.current, { type: nextRecorder.mimeType || "audio/webm" });
        const attemptId = crypto.randomUUID();
        stream.current?.getTracks().forEach((track) => track.stop());
        stream.current = null;
        setRecordingSince(null);
        setProcessing(true);
        void uploadVoice(trip.id, trip.step!.eventId, trip.step!.stepId, attemptId, blob, performance.now() - startedAt.current)
          .then(onVoiceTrip)
          .catch((reason) => {
            const failedTrip = reason instanceof ApiError ? reason.payload?.trip : undefined;
            if (failedTrip) void onVoiceTrip(failedTrip);
            // Низкая уверенность Laya — не ошибка: пассажир переспросит, и проводник ответит снова.
            if (!failedTrip?.voiceAttempt?.pending) onError(apiMessage(reason));
          })
          .finally(() => {
            setProcessing(false);
            onAnswering(false);
          });
      };
      recorder.current = nextRecorder;
      nextRecorder.start();
      setRecordingSince(performance.now());
    } catch {
      stream.current?.getTracks().forEach((track) => track.stop());
      onAnswering(false);
      onError("Нет доступа к микрофону. Разрешите микрофон и повторите попытку.");
    }
  }

  function stop() {
    recorder.current?.stop();
    recorder.current = null;
  }

  const waiting = !recording && (busy || processing);
  const [title, hint] = recording
    ? ["Идёт запись", "Нажмите ещё раз, когда закончите"]
    : processing
      ? ["Распознаём речь…", "GigaAM распознаёт, Laya сверяет с Вариантами"]
      : busy
        ? ["Пассажир отвечает…", "Ответ получен"]
        : clarifying
          ? ["Ответьте ещё раз", "Пассажир не понял — уточните ответ"]
          : ["Ответить голосом", "Нажмите и скажите ответ своими словами"];

  return (
    <div className={cn("flex items-center gap-3 rounded-xl bg-card p-3 pr-4", clarifying && !recording && !waiting && "ring-2 ring-warning")}>
      <button
        type="button"
        disabled={waiting}
        onClick={recording ? stop : () => void start()}
        aria-label={recording ? "Завершить ответ" : "Начать речь"}
        className={cn(
          "grid size-13 shrink-0 place-items-center rounded-full text-primary-foreground outline-none transition-colors focus-visible:ring-3 focus-visible:ring-ring/50 disabled:opacity-60",
          recording ? "bg-danger shadow-[0_0_0_5px_var(--color-danger-soft)]" : "bg-primary shadow-[0_0_0_5px_var(--color-brand-soft)]",
        )}
      >
        {waiting ? <LoaderCircle className="size-5 animate-spin" aria-hidden /> : recording ? <Square className="size-5" aria-hidden /> : <Mic className="size-5" aria-hidden />}
      </button>
      <div className="min-w-0 flex-1">
        <p className="flex items-center gap-1.5 text-[14.5px] font-extrabold">
          {recording && <span className="size-2 animate-pulse rounded-full bg-danger" aria-hidden />}
          {title}
        </p>
        <p className="text-[12.5px] text-muted-foreground">{hint}</p>
      </div>
      {recording ? <Stopwatch startedAt={recordingSince} stoppedAt={null} /> : waiting ? null : timer}
    </div>
  );
}
