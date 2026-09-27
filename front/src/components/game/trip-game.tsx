"use client";

import { useEffect, useRef, useState } from "react";
import { LoaderCircle, Mic, Square } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Progress } from "@/components/ui/progress";
import {
  ApiError,
  chooseProactive,
  chooseVariant,
  startTrip,
  streamPassengerReply,
  uploadVoice,
  type TripView,
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

export function TripGame() {
  const [serviceClass, setServiceClass] = useState("business");
  const [trip, setTrip] = useState<TripView | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [passengerReply, setPassengerReply] = useState("");
  const [replyError, setReplyError] = useState<string | null>(null);

  async function run(action: () => Promise<TripView>) {
    setBusy(true);
    setError(null);
    setPassengerReply("");
    setReplyError(null);
    try {
      setTrip(await action());
    } catch (reason) {
      if (reason instanceof ApiError && reason.payload?.trip) setTrip(reason.payload.trip);
      setError(apiMessage(reason));
    } finally {
      setBusy(false);
    }
  }

  async function handleVoiceTrip(next: TripView) {
    setTrip(next);
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
        (streamedTrip) => setTrip(streamedTrip),
      );
    } catch (reason) {
      setReplyError(apiMessage(reason));
    } finally {
      setBusy(false);
    }
  }

  if (!trip) {
    return (
      <Card className="mt-4">
        <CardHeader>
          <Badge className="w-fit" variant="secondary">Голосовой Рейс</Badge>
          <CardTitle>Начать Рейс</CardTitle>
          <CardDescription>Сервер будет считать дедлайн и сохранять только результат голосовой попытки.</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
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
          <Button disabled={busy} className="h-11" onClick={() => void run(() => startTrip(serviceClass))}>
            {busy && <LoaderCircle className="animate-spin" aria-hidden />}
            Начать Рейс
          </Button>
          <p className="text-xs text-muted-foreground">
            Перед началом войдите через страницу авторизации, чтобы получить доступ к Рейсу.
          </p>
        </CardContent>
      </Card>
    );
  }

  return (
    <div className="flex flex-col gap-3 pt-2">
      <TripScales trip={trip} />
      {error && <p role="alert" className="rounded-lg bg-danger-soft p-3 text-sm text-danger">{error}</p>}
      {trip.result ? (
        <Card>
          <CardHeader><Badge variant={trip.status === "arrived" ? "default" : "destructive"}>Рейс завершён</Badge><CardTitle>{trip.result.summary}</CardTitle></CardHeader>
          <CardContent><Button variant="outline" onClick={() => { setTrip(null); setError(null); }}>Новый Рейс</Button></CardContent>
        </Card>
      ) : trip.proactiveChoice ? (
        <Card>
          <CardHeader><CardTitle>Что проверить дальше?</CardTitle><CardDescription>{trip.proactiveChoice.situation}</CardDescription></CardHeader>
          <CardContent className="flex flex-col gap-2">
            {trip.proactiveChoice.options.map((option) => (
              <Button key={option.id} variant="outline" className="h-auto min-h-11 justify-start whitespace-normal py-3 text-left" disabled={busy} onClick={() => void run(() => chooseProactive(trip.id, option.id))}>{option.text}</Button>
            ))}
          </CardContent>
        </Card>
      ) : trip.step ? (
        <StepCard trip={trip} busy={busy} onTrip={setTrip} onVoiceTrip={handleVoiceTrip} onError={setError} passengerReply={passengerReply} replyError={replyError} />
      ) : null}
    </div>
  );
}

function TripScales({ trip }: { trip: TripView }) {
  return (
    <div className="grid grid-cols-2 gap-2">
      {trip.scales.map((scale) => {
        const percent = ((scale.value - scale.min) / Math.max(1, scale.max - scale.min)) * 100;
        return <Card key={scale.code} size="sm" className="gap-2 p-3">
          <div className="flex items-center justify-between gap-2 text-xs"><span className="truncate text-muted-foreground">{scale.name}</span><strong className="tabular-nums">{scale.value}</strong></div>
          <Progress value={percent} className="bg-muted" />
        </Card>;
      })}
    </div>
  );
}

function StepCard({
  trip,
  busy,
  onTrip,
  onVoiceTrip,
  onError,
  passengerReply,
  replyError,
}: {
  trip: TripView;
  busy: boolean;
  onTrip: (trip: TripView) => void;
  onVoiceTrip: (trip: TripView) => Promise<void>;
  onError: (message: string | null) => void;
  passengerReply: string;
  replyError: string | null;
}) {
  const step = trip.step!;
  return (
    <Card>
      <CardHeader>
        <div className="flex items-center justify-between gap-2"><Badge variant="secondary">{step.eventTitle}</Badge><StepTimer expiresAt={step.expiresAt} /></div>
        <CardTitle>{step.situation}</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {step.answerType === "voice" ? (
          <VoiceRecorder trip={trip} busy={busy} onVoiceTrip={onVoiceTrip} onError={onError} />
        ) : (
          <div className="flex flex-col gap-2">
            {step.variants.map((variant) => <Button key={variant.id} variant="outline" className="h-auto min-h-11 justify-start whitespace-normal py-3 text-left" disabled={busy} onClick={() => {
              onError(null);
              void chooseVariant(trip.id, step.eventId, step.stepId, variant.id).then(onTrip).catch((reason) => onError(apiMessage(reason)));
            }}>{variant.text}</Button>)}
          </div>
        )}
        {trip.voiceAttempt && <VoiceAttemptNotice trip={trip} />}
        {passengerReply && <div className="rounded-lg bg-brand-soft p-3 text-sm"><strong className="text-brand">Пассажир</strong><p className="mt-1">{passengerReply}</p></div>}
        {replyError && <p role="alert" className="rounded-lg bg-danger-soft p-3 text-sm text-danger">{replyError}</p>}
      </CardContent>
    </Card>
  );
}

function StepTimer({ expiresAt }: { expiresAt: string | null }) {
  const [remaining, setRemaining] = useState<number | null>(null);
  useEffect(() => {
    if (!expiresAt) return;
    const update = () => setRemaining(Math.max(0, Math.ceil((Date.parse(expiresAt) - Date.now()) / 1000)));
    update();
    const timer = window.setInterval(update, 250);
    return () => window.clearInterval(timer);
  }, [expiresAt]);
  return remaining === null ? null : <Badge variant={remaining <= 3 ? "destructive" : "outline"}>до {remaining} с</Badge>;
}

function VoiceRecorder({
  trip,
  busy,
  onVoiceTrip,
  onError,
}: {
  trip: TripView;
  busy: boolean;
  onVoiceTrip: (trip: TripView) => Promise<void>;
  onError: (message: string | null) => void;
}) {
  const [recording, setRecording] = useState(false);
  const [processing, setProcessing] = useState(false);
  const recorder = useRef<MediaRecorder | null>(null);
  const stream = useRef<MediaStream | null>(null);
  const chunks = useRef<Blob[]>([]);

  async function start() {
    onError(null);
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
        setRecording(false);
        setProcessing(true);
        void uploadVoice(trip.id, trip.step!.eventId, trip.step!.stepId, attemptId, blob)
          .then(onVoiceTrip)
          .catch((reason) => {
            if (reason instanceof ApiError && reason.payload?.trip) void onVoiceTrip(reason.payload.trip);
            onError(apiMessage(reason));
          })
          .finally(() => setProcessing(false));
      };
      recorder.current = nextRecorder;
      nextRecorder.start();
      setRecording(true);
    } catch {
      stream.current?.getTracks().forEach((track) => track.stop());
      onError("Нет доступа к микрофону. Разрешите микрофон и повторите попытку.");
    }
  }

  function stop() {
    recorder.current?.stop();
    recorder.current = null;
  }

  const waiting = busy || processing;
  return (
    <div className="flex flex-col gap-3 rounded-xl bg-brand-soft p-4">
      <div className="flex items-center gap-2 text-sm font-semibold"><Mic className="size-4 text-brand" aria-hidden />Ответьте голосом</div>
      <p className="text-xs text-muted-foreground">Нажмите «Начать речь», скажите ответ без пауз и нажмите «Завершить ответ».</p>
      <Button className="h-12" disabled={waiting} variant={recording ? "destructive" : "default"} onClick={recording ? stop : () => void start()}>
        {waiting && !recording ? <LoaderCircle className="animate-spin" aria-hidden /> : recording ? <Square aria-hidden /> : <Mic aria-hidden />}
        {processing ? "Распознаём и оцениваем…" : recording ? "Завершить ответ" : "Начать речь"}
      </Button>
    </div>
  );
}

function VoiceAttemptNotice({ trip }: { trip: TripView }) {
  const attempt = trip.voiceAttempt!;
  return <div className="rounded-lg bg-muted p-3 text-xs">
    <div className="flex justify-between gap-2 font-semibold"><span>{attempt.applied ? "Ответ применён" : "Попытка не применена"}</span><span className="tabular-nums">{attempt.latencyMs} мс</span></div>
    {attempt.transcript && <p className="mt-1 text-muted-foreground">«{attempt.transcript}»</p>}
    {attempt.confidence !== null && <p className="mt-1 text-muted-foreground">Уверенность Laya: {Math.round(attempt.confidence * 100)}%</p>}
    {attempt.score !== null && <p className="mt-1 text-muted-foreground">Вежливость: {Math.round(attempt.score * 100)}%</p>}
    {attempt.roleStages && <p className="mt-1 text-muted-foreground">Ролевая модель: {Object.entries(attempt.roleStages).map(([stage, value]) => `${stage} ${Math.round(value * 100)}%`).join(" · ")}</p>}
    {attempt.safetyViolation !== null && <p className="mt-1 text-muted-foreground">Риск безопасности: {Math.round(attempt.safetyViolation * 100)}%</p>}
  </div>;
}
