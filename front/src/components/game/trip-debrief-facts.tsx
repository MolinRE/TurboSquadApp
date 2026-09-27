"use client";

import { useEffect, useState } from "react";
import { Check, Sparkles, X } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { SourceLine } from "@/components/swipes/explanation";
import { getTripDebriefExplanation, type TripDebrief, type TripDebriefExplanation } from "@/lib/api";

const signed = (value: number) => `${value > 0 ? "+" : ""}${value}`;
const roleStages = [
  ["acknowledge", "Признание"],
  ["rule", "Правило"],
  ["solution", "Решение"],
  ["reassure", "Заверение"],
] as const;

export function roleStageSummary(stages: Record<string, number>): string {
  return roleStages
    .filter(([code]) => stages[code] !== undefined)
    .map(([code, name]) => `${name} ${Math.round(stages[code] * 100)}%`)
    .join(" · ");
}

/** Этап Ролевой модели засчитан, если Laya оценила его хотя бы в половину. */
const STAGE_HEARD = 0.5;

type VoiceAttempt = TripDebrief["voiceAttempts"][number];

/** Что проводник сказал на Шаге: последняя принятая попытка, этапы Ролевой модели ✓/✗, вежливость и риск. */
function VoiceFacts({ attempt }: { attempt: VoiceAttempt }) {
  const stages = roleStages.filter(([code]) => attempt.roleStages?.[code] !== undefined);
  return (
    <div className="mt-2 flex flex-col gap-1 rounded-lg bg-muted p-3 text-sm">
      {attempt.transcript && <p>Вы сказали: «{attempt.transcript}»</p>}
      {stages.length > 0 && (
        <p className="flex flex-wrap gap-x-3 gap-y-1">
          {stages.map(([code, name]) => {
            const heard = attempt.roleStages![code] >= STAGE_HEARD;
            const Icon = heard ? Check : X;
            return (
              <span key={code} className={heard ? "flex items-center gap-1 text-brand" : "flex items-center gap-1 text-danger"}>
                <Icon className="size-3.5" aria-hidden />
                {name} <span className="sr-only">{heard ? "прозвучало" : "пропущено"}</span>
              </span>
            );
          })}
        </p>
      )}
      <p className="text-xs text-muted-foreground">
        {[
          attempt.score !== null && `Вежливость ${Math.round(attempt.score * 100)}%`,
          attempt.safetyViolation !== null && `риск нарушения безопасности ${Math.round(attempt.safetyViolation * 100)}%`,
        ].filter(Boolean).join(" · ")}
      </p>
    </div>
  );
}

export function TripDebriefFacts({ debrief }: { debrief: TripDebrief }) {
  const attemptOf = (eventId: string, stepId: string) =>
    debrief.voiceAttempts.findLast((attempt) => attempt.eventId === eventId && attempt.stepId === stepId && attempt.applied);
  return (
    <div className="flex flex-col gap-3">
      {debrief.items.map((item, index) => item.kind === "proactiveChoice" ? (
        <Card key={`choice-${index}`} size="sm">
          <CardHeader><CardTitle>Проактивный выбор</CardTitle></CardHeader>
          <CardContent className="text-sm">
            <p className="text-muted-foreground">{item.situation}</p>
            <p className="mt-1 font-semibold">{item.choice}</p>
          </CardContent>
        </Card>
      ) : (
        <Card key={`${item.eventId}-${index}`} size="sm">
          <CardHeader>
            <div className="flex items-center justify-between gap-2">
              <CardTitle>{item.title}</CardTitle>
              <Badge variant={item.result === "interrupted" ? "destructive" : "secondary"}>
                {item.result === "interrupted" ? "Прервано" : item.result === "failure" ? "Неудачный исход" : "Завершено"}
              </Badge>
            </div>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            {item.decisions.map((decision, decisionIndex) => (
              <section key={`${decision.stepId}-${decisionIndex}`} className="border-t border-border pt-3 first:border-0 first:pt-0">
                <p className="text-sm text-muted-foreground">{decision.situation}</p>
                <p className="mt-1 font-semibold">{decision.timedOut ? "Время вышло" : decision.text}</p>
                {decision.criticalError && <p className="mt-1 text-sm font-semibold text-danger">Критическая ошибка</p>}
                {decision.changes.length > 0 && (
                  <ul className="mt-2 space-y-1 text-sm">
                    {decision.changes.map((change) => (
                      <li key={change.scale}>{change.name}: {change.before} → {change.after} ({signed(change.applied)})</li>
                    ))}
                  </ul>
                )}
                <p className="mt-2 text-sm">Очки знаний: {signed(decision.knowledgeDelta)}</p>
                {decision.elapsedMs !== null && <p className="text-xs text-muted-foreground">Время ответа: {(decision.elapsedMs / 1000).toFixed(1)} с</p>}
                {(() => {
                  const attempt = attemptOf(item.eventId, decision.stepId);
                  return attempt ? <VoiceFacts attempt={attempt} /> : null;
                })()}
                {decision.comment && <p className="mt-2 rounded-lg bg-brand-soft p-3 text-sm">{decision.comment}</p>}
                {decision.source && <div className="mt-1"><SourceLine source={decision.source} /></div>}
              </section>
            ))}
            {item.outcomeSituation && <p className="border-t border-border pt-3 text-sm">{item.outcomeSituation}</p>}
          </CardContent>
        </Card>
      ))}
    </div>
  );
}

/**
 * Разбор, слой Б: ИИ-объяснение последствий. Грузится после слоя А и пишется заново при каждом
 * открытии; не ответила модель — слой А остаётся, а здесь короткая строка.
 */
export function TripExplanation({ tripId }: { tripId: string }) {
  const [explanation, setExplanation] = useState<TripDebriefExplanation | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let active = true;
    getTripDebriefExplanation(tripId).then(
      (value) => { if (active) setExplanation(value); },
      () => { if (active) setFailed(true); },
    );
    return () => { active = false; };
  }, [tripId]);

  return (
    <section aria-label="Почему это важно" aria-busy={!explanation && !failed} className="flex flex-col gap-2 rounded-xl bg-brand-soft p-4">
      <h3 className="flex items-center gap-2 font-extrabold">
        <Sparkles className="size-4 text-brand" aria-hidden />
        Почему это важно
      </h3>
      {explanation ? (
        <>
          <p className="text-sm leading-relaxed">{explanation.text}</p>
          <p className="text-xs text-muted-foreground">Сгенерировано моделью {explanation.model} · на очки не влияет</p>
        </>
      ) : failed ? (
        <p className="text-sm text-muted-foreground">Объяснение сейчас недоступно — факты Разбора выше.</p>
      ) : (
        <div className="flex flex-col gap-2">
          <Skeleton className="h-4 w-full bg-card" />
          <Skeleton className="h-4 w-11/12 bg-card" />
          <Skeleton className="h-4 w-2/3 bg-card" />
        </div>
      )}
    </section>
  );
}
