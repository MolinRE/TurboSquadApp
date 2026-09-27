"use client";

import { useEffect, useState } from "react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { getVoiceAnalytics, type VoiceAnalytics, type VoiceLatency } from "@/lib/api";

const percentage = (value: number) => `${Math.round(value * 100)}%`;
const milliseconds = (value: number) => `${Math.round(value)} мс`;

export default function Page() {
  const [analytics, setAnalytics] = useState<VoiceAnalytics | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    getVoiceAnalytics()
      .then((result) => { if (active) setAnalytics(result); })
      .catch((reason) => { if (active) setError(reason instanceof Error ? reason.message : "Не удалось загрузить аналитику"); });
    return () => { active = false; };
  }, []);

  if (error) return <p role="alert" className="rounded-lg bg-danger-soft p-4 text-danger">{error}</p>;
  if (!analytics) return <p className="text-muted-foreground">Загружаем аналитику голосовых ответов…</p>;

  const latencies: Array<[string, VoiceLatency]> = [
    ["STT", analytics.stt],
    ["Laya", analytics.laya],
    ["LLM", analytics.llm],
  ];

  return <div className="flex flex-col gap-4">
    <div className="grid gap-3 md:grid-cols-3">
      <Card><CardHeader><CardTitle>Голосовые попытки</CardTitle></CardHeader><CardContent className="text-2xl font-semibold tabular-nums">{analytics.attempts}</CardContent></Card>
      <Card><CardHeader><CardTitle>Неуверенные решения</CardTitle></CardHeader><CardContent className="text-2xl font-semibold tabular-nums">{percentage(analytics.uncertainRate)} <span className="text-sm font-normal text-muted-foreground">({analytics.uncertainAttempts})</span></CardContent></Card>
      <Card><CardHeader><CardTitle>Fallback</CardTitle></CardHeader><CardContent className="text-2xl font-semibold tabular-nums">{percentage(analytics.fallbackRate)} <span className="text-sm font-normal text-muted-foreground">({analytics.fallbackAttempts}, режимы отключены)</span></CardContent></Card>
    </div>

    <Card>
      <CardHeader><CardTitle>Задержки компонентов</CardTitle></CardHeader>
      <CardContent className="overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead><tr className="border-b text-muted-foreground"><th className="py-2">Компонент</th><th>Измерений</th><th>Среднее</th><th>p50</th><th>p95</th></tr></thead>
          <tbody>{latencies.map(([name, latency]) => <tr key={name} className="border-b last:border-0"><th className="py-2 font-semibold">{name}</th><td>{latency.count}</td><td>{latency.count ? milliseconds(latency.averageMs) : "—"}</td><td>{latency.count ? milliseconds(latency.p50Ms) : "—"}</td><td>{latency.count ? milliseconds(latency.p95Ms) : "—"}</td></tr>)}</tbody>
        </table>
      </CardContent>
    </Card>

    <Card>
      <CardHeader><CardTitle>Шаги Событий</CardTitle></CardHeader>
      <CardContent className="overflow-x-auto">
        {analytics.steps.length === 0 ? <p className="text-sm text-muted-foreground">Голосовых попыток пока нет.</p> : (
          <table className="w-full text-left text-sm">
            <thead><tr className="border-b text-muted-foreground"><th className="py-2">Событие / Шаг</th><th>Попыток</th><th>Применено</th><th>Неуверенных</th><th>Вежливость</th><th>STT p95</th><th>Laya p95</th><th>LLM p95</th></tr></thead>
            <tbody>{analytics.steps.map((step) => <tr key={`${step.eventId}:${step.eventVersion}:${step.stepId}`} className="border-b last:border-0">
              <th className="py-2 font-semibold">{step.eventId} v{step.eventVersion} / {step.stepId}</th>
              <td>{step.attempts}</td><td>{step.appliedAttempts}</td><td>{step.uncertainAttempts}</td>
              <td>{step.averageScore === null ? "—" : percentage(step.averageScore)}</td>
              <td>{step.stt.count ? milliseconds(step.stt.p95Ms) : "—"}</td>
              <td>{step.laya.count ? milliseconds(step.laya.p95Ms) : "—"}</td>
              <td>{step.llm.count ? milliseconds(step.llm.p95Ms) : "—"}</td>
            </tr>)}</tbody>
          </table>
        )}
      </CardContent>
    </Card>
  </div>;
}
