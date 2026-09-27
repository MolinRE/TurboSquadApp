import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import type { TripDebrief } from "@/lib/api";

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

export function TripDebriefFacts({ debrief }: { debrief: TripDebrief }) {
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
                {decision.comment && <p className="mt-2 rounded-lg bg-brand-soft p-3 text-sm">{decision.comment}</p>}
              </section>
            ))}
            {item.outcomeSituation && <p className="border-t border-border pt-3 text-sm">{item.outcomeSituation}</p>}
          </CardContent>
        </Card>
      ))}
      {debrief.voiceAttempts.length > 0 && (
        <section aria-label="Разбор голосовых ответов" className="flex flex-col gap-3">
          <h3 className="font-semibold">Разбор голосовых ответов</h3>
          {debrief.voiceAttempts.map((attempt) => (
            <div key={attempt.attemptId} className="rounded-lg bg-muted p-3 text-sm">
              <p className="font-semibold">Событие {attempt.eventId}, версия {attempt.eventVersion}, Шаг {attempt.stepId}</p>
              {attempt.transcript && <p className="mt-1">«{attempt.transcript}»</p>}
              {attempt.score !== null && <p className="mt-1">Вежливость: {Math.round(attempt.score * 100)}%{attempt.scoreConfidence !== null && ` · уверенность ${Math.round(attempt.scoreConfidence * 100)}%`}</p>}
              {attempt.roleStages && <p className="mt-1">Ролевая модель: {roleStageSummary(attempt.roleStages)}</p>}
              {attempt.safetyViolation !== null && <p className="mt-1">Риск нарушения безопасности: {Math.round(attempt.safetyViolation * 100)}%{attempt.safetyConfidence !== null && ` · уверенность ${Math.round(attempt.safetyConfidence * 100)}%`}</p>}
              {attempt.errorCode && <p className="mt-1 text-danger">Ошибка: {attempt.errorCode}</p>}
            </div>
          ))}
        </section>
      )}
    </div>
  );
}
