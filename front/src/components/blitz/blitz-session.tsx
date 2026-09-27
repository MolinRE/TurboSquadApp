"use client";

import { useEffect, useEffectEvent, useRef, useState, useSyncExternalStore } from "react";
import { CircleCheck, CircleX, Square, SquareCheck, Timer, Zap } from "lucide-react";
import { cn } from "cn";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Countdown } from "@/components/game/countdown";
import { TopicIcon } from "@/components/game/topic-icon";
import { DeckProgress } from "@/components/swipes/deck-progress";
import { ExplanationText, SourceLine, VerdictLabel } from "@/components/swipes/explanation";
import { formatSeconds } from "@/components/swipes/format";
import { SummaryActions, WhatToRepeat } from "@/components/swipes/shift-summary";
import { ApiError } from "@/lib/api";
import { blitzApi } from "@/lib/blitz/api";
import type { BlitzAnswerOutcome, BlitzQuestion, BlitzRejection, BlitzSession as Session } from "@/lib/blitz/contract";
import { forgetSession, saveSession, savedSession } from "@/lib/blitz/saved-session";
import { noSubscription } from "@/lib/swipes/saved-shift";

/**
 * Отказы 409, после которых ответ уже засчитан или сессия ушла дальше: например, первый ответ
 * дошёл до сервера, а его результат потерялся в сети. Экран берёт состояние с сервера.
 */
const SETTLED_REJECTIONS: BlitzRejection[] = ["StaleQuestion", "QuestionNotShown", "SessionNotRunning"];
const LOADING = "Собираем Вопросы…";
const RESUMING = "Возвращаемся к Блицу…";

type Phase =
  /** Открыли экран: продолжаем незаконченную сессию этого браузера, если она есть, иначе старт. */
  | { kind: "restoring" }
  | { kind: "start" }
  | { kind: "loading"; label: string }
  | { kind: "loadFailed"; message: string; retry: () => void }
  | { kind: "playing" }
  | { kind: "sending"; selected: string[] | null }
  | { kind: "feedback" }
  /** Просим у сервера следующий Вопрос. */
  | { kind: "advancing" }
  | { kind: "finished" };

type LastAnswer = { question: BlitzQuestion; selected: string[] | null; outcome: BlitzAnswerOutcome };

/** Ошибка действия; resend — выбор, который не дошёл до сервера: кнопка отправляет его ещё раз. */
type ActionError = { message: string; resend: string[] | null };

/** Текст ошибки из единого формата ошибок API; fetch без ответа сервера — нет связи. */
function messageOf(error: unknown) {
  if (!(error instanceof ApiError)) return "Не удалось связаться с сервером";
  return error.status === 403 ? "Блиц проходит Проводник — войдите его демо-аккаунтом" : error.message;
}

function alreadySettled(error: unknown) {
  return (
    error instanceof ApiError &&
    error.status === 409 &&
    SETTLED_REJECTIONS.some((reason) => reason === error.payload?.reason)
  );
}

/** Сессии нет или она чужая: например, в этом браузере потом входил другой проводник. */
function sessionGone(error: unknown) {
  return error instanceof ApiError && error.status === 404;
}

/**
 * Блиц через API: вердикт и время считает сервер, экран показывает. Вопрос с одним ответом
 * (нажатие на вариант — ответ) или с несколькими (отметить варианты и «Ответить»), обратный
 * отсчёт от показа; истёк — «Время вышло». После ответа — панель с верными вариантами
 * и Пояснением, следующий Вопрос (и его таймер) — по «Дальше». Незаконченную сессию экран
 * продолжает и после перезагрузки.
 */
export function BlitzSession() {
  const [session, setSession] = useState<Session | null>(null);
  const [phase, setPhase] = useState<Phase>({ kind: "restoring" });
  const [last, setLast] = useState<LastAnswer | null>(null);
  const [actionError, setActionError] = useState<ActionError | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  /** Отметки performance.now(): обратный отсчёт текущего Вопроса. */
  const [startedAt, setStartedAt] = useState<number | null>(null);
  const [stoppedAt, setStoppedAt] = useState<number | null>(null);
  /** Номер показа Вопроса: сбрасывает таймер и фокус. */
  const [turn, setTurn] = useState(0);
  /** Ответ в пути: второе нажатие до смены фазы не отправляет второй ответ. */
  const sending = useRef(false);
  /** Сохранённая незаконченная сессия; undefined — пока неизвестно (отрисовка на сервере). */
  const saved = useSyncExternalStore(noSubscription, savedSession, () => undefined);

  function clearMessages() {
    setActionError(null);
    setNotice(null);
  }

  function showLoading(label: string) {
    setPhase({ kind: "loading", label });
    setLast(null);
    clearMessages();
  }

  function showQuestion(state: Session) {
    setSession(state);
    setTurn((value) => value + 1);
    setStartedAt(performance.now());
    setStoppedAt(null);
    setPhase({ kind: "playing" });
  }

  function finish(state: Session) {
    forgetSession();
    setSession(state);
    setPhase({ kind: "finished" });
  }

  function start() {
    showLoading(LOADING);
    blitzApi.startSession().then(
      (state) => {
        saveSession(state.sessionId);
        showQuestion(state);
      },
      (error) => setPhase({ kind: "loadFailed", message: `Не удалось начать Блиц: ${messageOf(error)}`, retry: start }),
    );
  }

  function resync(sessionId: string, note: string | null) {
    showLoading(RESUMING);
    continueSession(sessionId, note);
  }

  /**
   * Продолжает сессию по состоянию с сервера. Если ответ дан, а следующий Вопрос не просили,
   * просит его: Пояснение к тому ответу уже не показать.
   */
  function continueSession(sessionId: string, note: string | null) {
    blitzApi
      .getSession(sessionId)
      .then((state) => (state.status === "running" && !state.question ? blitzApi.showNextQuestion(sessionId) : state))
      .then(
        (state) => {
          setNotice(note);
          if (state.status === "running") showQuestion(state);
          else finish(state);
        },
        (error) => {
          if (sessionGone(error)) {
            restart();
            return;
          }
          setPhase({
            kind: "loadFailed",
            message: `Не удалось вернуться к Блицу: ${messageOf(error)}`,
            retry: () => resync(sessionId, note),
          });
        },
      );
  }

  const continueSaved = useEffectEvent((sessionId: string) => {
    if (phase.kind === "restoring") continueSession(sessionId, null);
  });

  useEffect(() => {
    if (saved) continueSaved(saved);
  }, [saved]);

  function restart() {
    forgetSession();
    setSession(null);
    setLast(null);
    clearMessages();
    setPhase({ kind: "start" });
  }

  /** selected — выбранные варианты; null — «Время вышло». */
  async function submit(selected: string[] | null) {
    if (sending.current || phase.kind !== "playing" || !session?.question) return;
    sending.current = true;
    const question = session.question;
    setPhase({ kind: "sending", selected });
    // Таймер стоит с первой попытки: после ошибки сети он не идёт дальше и не шлёт «Время вышло» сам.
    setStoppedAt((value) => value ?? performance.now());
    clearMessages();
    try {
      const outcome =
        selected === null
          ? await blitzApi.timeOut(session.sessionId, question.questionId)
          : await blitzApi.answer(session.sessionId, question.questionId, selected);
      setLast({ question, selected, outcome });
      setSession(outcome.session);
      setPhase({ kind: "feedback" });
    } catch (error) {
      if (sessionGone(error)) {
        restart();
      } else if (alreadySettled(error)) {
        resync(session.sessionId, "Ответ на прошлый Вопрос уже был засчитан — продолжаем.");
      } else {
        setActionError({ message: `Ответ не отправлен: ${messageOf(error)}.`, resend: selected });
        setPhase({ kind: "playing" });
      }
    } finally {
      sending.current = false;
    }
  }

  function proceed() {
    if (phase.kind !== "feedback" || !session) return;
    if (session.status !== "running") {
      finish(session);
      return;
    }
    setPhase({ kind: "advancing" });
    setActionError(null);
    blitzApi.showNextQuestion(session.sessionId).then(showQuestion, (error) => {
      setActionError({ message: `Следующий Вопрос не пришёл: ${messageOf(error)}. Попробуйте ещё раз.`, resend: null });
      setPhase({ kind: "feedback" });
    });
  }

  if (phase.kind === "restoring") {
    if (saved === undefined) return null;
    if (saved === null) return <BlitzStart onStart={start} />;
    return <p className="m-auto text-sm text-muted-foreground">{RESUMING}</p>;
  }

  if (phase.kind === "start") return <BlitzStart onStart={start} />;

  if (phase.kind === "loadFailed") {
    return (
      <div className="m-auto flex flex-col items-center gap-3 text-center">
        <p role="alert" className="text-sm text-danger">
          {phase.message}
        </p>
        <div className="flex flex-wrap justify-center gap-2">
          <Button onClick={phase.retry}>Попробовать снова</Button>
          <Button variant="outline" onClick={restart} className="bg-card">
            Новый Блиц
          </Button>
        </div>
      </div>
    );
  }

  if (phase.kind === "loading" || !session) {
    return <p className="m-auto text-sm text-muted-foreground">{phase.kind === "loading" ? phase.label : LOADING}</p>;
  }

  if (phase.kind === "finished") return <BlitzSummary session={session} onRestart={restart} />;

  const showingFeedback = (phase.kind === "feedback" || phase.kind === "advancing") && last;
  const question = session.question;
  const resend = phase.kind === "playing" ? actionError?.resend : null;

  return (
    <div className="flex flex-1 flex-col gap-3">
      {showingFeedback ? (
        <FeedbackPanel session={session} last={last} busy={phase.kind === "advancing"} onContinue={proceed} />
      ) : question ? (
        <QuestionCard
          key={turn}
          session={session}
          question={question}
          startedAt={startedAt}
          stoppedAt={stoppedAt}
          sending={phase.kind === "sending" ? phase.selected : undefined}
          disabled={phase.kind !== "playing"}
          onAnswer={(optionIds) => void submit(optionIds)}
          onExpire={() => void submit(null)}
        />
      ) : null}

      {actionError ? (
        <div role="alert" className="flex flex-col items-start gap-2 text-sm">
          <p className="text-danger">{actionError.message}</p>
          {resend !== undefined && resend !== null ? (
            <Button variant="outline" onClick={() => void submit(resend)} className="bg-card">
              Отправить ещё раз
            </Button>
          ) : null}
        </div>
      ) : null}
      {notice ? <p className="text-sm text-muted-foreground">{notice}</p> : null}
    </div>
  );
}

function BlitzStart({ onStart }: { onStart: () => void }) {
  return (
    <div className="flex flex-1 flex-col gap-3">
      <section className="flex flex-col items-center gap-3 rounded-xl bg-card p-6 text-center">
        <span className="grid size-14 place-items-center rounded-full bg-brand-soft">
          <Zap className="size-7 text-brand" aria-hidden />
        </span>
        <h1 className="text-2xl font-extrabold tracking-tight">Блиц</h1>
        <p className="text-sm text-muted-foreground">
          Вопросы на время: выберите верный ответ, а если верных несколько — отметьте все, пока не закончился отсчёт.
          После каждого ответа — Пояснение с пунктом Источника.
        </p>
        <p className="flex items-center gap-1.5 text-sm font-bold text-brand">
          <Timer className="size-4" aria-hidden />
          Таймер на каждый Вопрос
        </p>
      </section>
      <Button onClick={onStart} className="mt-auto h-12 text-base font-bold">
        Начать Блиц
      </Button>
    </div>
  );
}

/**
 * Вопрос: прогресс и обратный отсчёт в шапке, Тема, формулировка и варианты во всю ширину.
 * single — нажатие на вариант сразу отвечает; multiple — варианты отмечаются, ответ по «Ответить».
 */
function QuestionCard({
  session,
  question,
  startedAt,
  stoppedAt,
  sending,
  disabled,
  onAnswer,
  onExpire,
}: {
  session: Session;
  question: BlitzQuestion;
  startedAt: number | null;
  stoppedAt: number | null;
  /** Выбор в пути: выделен, пока сервер сверяет ответ. */
  sending: string[] | null | undefined;
  disabled: boolean;
  onAnswer: (optionIds: string[]) => void;
  onExpire: () => void;
}) {
  const multiple = question.type === "multiple";
  /** Отмеченные варианты multiple в порядке нажатия: сервер порядок не учитывает. */
  const [picked, setPicked] = useState<string[]>([]);

  function toggle(optionId: string) {
    setPicked((ids) => (ids.includes(optionId) ? ids.filter((id) => id !== optionId) : [...ids, optionId]));
  }

  return (
    <article aria-label="Вопрос" className="flex flex-col gap-4 rounded-xl bg-card p-5">
      <div className="flex items-center gap-3">
        <DeckProgress progress={session.progress} />
        <Countdown limitMs={question.timeLimitMs} startedAt={startedAt} stoppedAt={stoppedAt} onExpire={onExpire} />
      </div>
      <Badge variant="secondary" className="h-6 w-fit gap-1.5 bg-background px-2.5 font-bold">
        <TopicIcon topic={question.topic} className="text-brand" />
        {question.topic}
      </Badge>
      <p className="text-lg leading-snug font-bold">{question.statement}</p>
      {multiple ? <p className="-mt-2 text-sm text-muted-foreground">Верных несколько — отметьте все</p> : null}
      <div className="grid gap-2">
        {question.options.map((option) => {
          const checked = multiple && picked.includes(option.id);
          return (
            <Button
              key={option.id}
              variant="outline"
              disabled={disabled}
              aria-pressed={multiple ? checked : undefined}
              onClick={() => (multiple ? toggle(option.id) : onAnswer([option.id]))}
              className={cn(
                "h-auto min-h-12 justify-start bg-card px-4 py-3 text-left text-base font-bold whitespace-normal",
                checked && "border-brand bg-brand-soft hover:bg-brand-soft",
                sending?.includes(option.id) && "border-foreground disabled:opacity-100",
              )}
            >
              {multiple ? (
                checked ? (
                  <SquareCheck className="size-5 text-brand" aria-hidden />
                ) : (
                  <Square className="size-5 text-muted-foreground" aria-hidden />
                )
              ) : null}
              {option.text}
            </Button>
          );
        })}
      </div>
      {multiple ? (
        <Button disabled={disabled || picked.length === 0} onClick={() => onAnswer(picked)} className="h-12 text-base font-bold">
          Ответить
        </Button>
      ) : null}
      {sending !== undefined ? (
        <p className="text-center text-sm font-bold text-muted-foreground">Сверяем ответ…</p>
      ) : null}
    </article>
  );
}

/**
 * Панель после ответа: вердикт, варианты с верными и выбранными, Пояснение; следующий Вопрос — по кнопке.
 * У multiple верный, но не отмеченный вариант подписан «Не отмечен».
 */
function FeedbackPanel({
  session,
  last,
  busy,
  onContinue,
}: {
  session: Session;
  last: LastAnswer;
  busy: boolean;
  onContinue: () => void;
}) {
  const { question, selected, outcome } = last;
  const correct = new Set(outcome.correctOptionIds);
  const finished = session.status !== "running";

  return (
    <section aria-live="polite" className="flex flex-col gap-3 rounded-xl bg-card p-5">
      <DeckProgress progress={session.progress} />
      <div className="flex items-center justify-between gap-2">
        <VerdictLabel verdict={outcome.verdict} timedOut={outcome.timedOut} className="text-lg" />
        {outcome.timedOut ? null : (
          <span className="text-xs font-bold text-muted-foreground tabular-nums">
            Ответ за {formatSeconds(outcome.elapsedMs)} с
          </span>
        )}
      </div>
      <p className="text-sm text-muted-foreground">{question.statement}</p>
      <ul className="grid gap-2">
        {question.options.map((option) => {
          const isCorrect = correct.has(option.id);
          const isPicked = selected?.includes(option.id) ?? false;
          const isWrongPick = isPicked && !isCorrect;
          const isMissed = question.type === "multiple" && selected !== null && isCorrect && !isPicked;
          return (
            <li
              key={option.id}
              className={cn(
                "flex items-start gap-2 rounded-lg border px-3 py-2.5 text-sm",
                isCorrect ? "border-brand bg-brand-soft font-bold" : isWrongPick ? "border-danger bg-danger-soft" : "border-border text-muted-foreground",
              )}
            >
              {isCorrect ? (
                <CircleCheck className="mt-0.5 size-4 shrink-0 text-brand" aria-label="Верный вариант" />
              ) : isWrongPick ? (
                <CircleX className="mt-0.5 size-4 shrink-0 text-danger" aria-label="Ваш ответ" />
              ) : (
                <span className="size-4 shrink-0" aria-hidden />
              )}
              <span className="flex-1">{option.text}</span>
              {isMissed ? <span className="shrink-0 text-xs font-bold text-muted-foreground">Не отмечен</span> : null}
            </li>
          );
        })}
      </ul>
      <p className="text-base leading-relaxed">
        <ExplanationText explanation={outcome.explanation} />
      </p>
      <SourceLine source={outcome.explanation.source} />
      <Button autoFocus disabled={busy} onClick={onContinue} className="mt-1 h-12 text-base font-bold">
        {finished ? "К итогу" : "Дальше"}
      </Button>
    </section>
  );
}

function BlitzSummary({ session, onRestart }: { session: Session; onRestart: () => void }) {
  const result = session.result!;
  return (
    <div className="flex flex-1 flex-col gap-3">
      <section className="flex flex-col items-center gap-2 rounded-xl bg-card p-6 text-center">
        <Zap className="size-10 text-brand" aria-hidden />
        <h1 className="text-2xl font-extrabold tracking-tight">Блиц пройден</h1>
        <p className="text-sm">
          Верно:{" "}
          <b className="tabular-nums">
            {result.correct} из {result.total}
          </b>
        </p>
        {result.averageAnswerMs !== null ? (
          <p className="text-sm">
            Среднее время ответа: <b className="tabular-nums">{formatSeconds(result.averageAnswerMs)} с</b>
          </p>
        ) : null}
      </section>
      {result.mistakes.length ? <WhatToRepeat mistakes={result.mistakes} /> : null}
      <SummaryActions primary={{ label: "Новый Блиц", onClick: onRestart }} />
    </div>
  );
}
