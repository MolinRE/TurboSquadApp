"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ApiError } from "@/lib/api";
import { generateEvents, generateQuestions, listSourceDrafts, listSources, type EventGenerationResult, type GenerationResult, type SourceSummary } from "@/lib/sources";
import type { Question } from "@/lib/questions";
import { listEventDrafts, type EventDraft } from "@/lib/events";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

export function GenerationWorkspace() {
  const router = useRouter();
  const [sources, setSources] = useState<SourceSummary[]>([]);
  const [sourceId, setSourceId] = useState("");
  const [drafts, setDrafts] = useState<Question[]>([]);
  const [eventDrafts, setEventDrafts] = useState<EventDraft[]>([]);
  const [result, setResult] = useState<GenerationResult | null>(null);
  const [eventResult, setEventResult] = useState<EventGenerationResult | null>(null);
  const [model, setModel] = useState("qwen/qwen3.6-35b-a3b");
  const [busy, setBusy] = useState<"questions" | "events" | null>(null);
  const [message, setMessage] = useState("");

  useEffect(() => {
    if (!busy) return;
    const warnBeforeLeaving = (event: BeforeUnloadEvent) => { event.preventDefault(); };
    window.addEventListener("beforeunload", warnBeforeLeaving);
    return () => window.removeEventListener("beforeunload", warnBeforeLeaving);
  }, [busy]);

  useEffect(() => {
    listSources().then((items) => {
      setSources(items);
      const requested = new URLSearchParams(window.location.search).get("source");
      setSourceId(items.find((item) => item.id === requested)?.id ?? items[0]?.id ?? "");
    }).catch((error) => {
      if (error instanceof ApiError && error.status === 401) router.push("/login");
      else setMessage(error instanceof Error ? error.message : "Не удалось загрузить Источники");
    });
  }, [router]);

  useEffect(() => {
    if (!sourceId) return;
    Promise.all([listSourceDrafts(sourceId), listEventDrafts(sourceId)])
      .then(([questions, events]) => { setDrafts(questions); setEventDrafts(events); })
      .catch((error) =>
      setMessage(error instanceof Error ? error.message : "Не удалось загрузить Черновики"));
  }, [sourceId]);

  async function generate(kind: "questions" | "events") {
    setBusy(kind);
    setMessage("");
    try {
      if (kind === "questions") {
        setResult(await generateQuestions(sourceId, model));
        setDrafts(await listSourceDrafts(sourceId));
      } else {
        setEventResult(await generateEvents(sourceId, model));
        setEventDrafts(await listEventDrafts(sourceId));
      }
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Не удалось сгенерировать контент");
    } finally { setBusy(null); }
  }

  return <div className="space-y-6">
    <div><h1 className="text-2xl font-bold">Генерация контента</h1><p className="text-sm text-muted-foreground">Вопросы и События сохраняются Черновиками. Проверьте их перед публикацией.</p></div>
    {message && <p role="alert" className="rounded-lg bg-danger-soft p-3 text-sm text-danger">{message}</p>}
    {busy && <p role="status" className="rounded-lg bg-warning-soft p-3 text-sm text-warning-foreground">Идёт генерация. Не закрывайте страницу до конца генерации.</p>}
    <Card><CardContent className="flex flex-wrap items-end gap-3 pt-6">
      <label className="min-w-64 flex-1 text-sm font-semibold">Источник
        <select className="mt-2 block h-9 w-full rounded-lg border border-input bg-transparent px-3 text-sm" value={sourceId} disabled={busy !== null} onChange={(event) => { setSourceId(event.target.value); setDrafts([]); setEventDrafts([]); setResult(null); setEventResult(null); }}>
          {sources.map((source) => <option key={source.id} value={source.id}>{source.title}</option>)}
        </select>
      </label>
      <label className="min-w-64 flex-1 text-sm font-semibold">Модель LLM (для сравнения)
        <select className="mt-2 block h-9 w-full rounded-lg border border-input bg-transparent px-3 text-sm" value={model} disabled={busy !== null} onChange={(event) => setModel(event.target.value)}>
          <option value="qwen/qwen3.6-35b-a3b">qwen/qwen3.6-35b-a3b</option>
          <option value="openai/gpt-oss-20b">openai/gpt-oss-20b</option>
          <option value="deepseek/deepseek-v3.2">deepseek/deepseek-v3.2</option>
        </select>
      </label>
      <Button onClick={() => void generate("questions")} disabled={!sourceId || busy !== null}>{busy === "questions" ? "Генерация…" : "Сгенерировать Вопросы"}</Button>
      <Button variant="outline" onClick={() => void generate("events")} disabled={!sourceId || busy !== null}>{busy === "events" ? "Генерация…" : "Сгенерировать События"}</Button>
      {!busy && <Button asChild variant="outline"><Link href="/cms/sources">Добавить Источник</Link></Button>}
    </CardContent></Card>
    {result && <div role="status" className="rounded-lg bg-brand-soft p-3 text-sm">
      Сохранено Вопросов-Черновиков: {result.created}. Повторов пропущено: {result.duplicates}.
      {result.errors.length > 0 && <ul className="mt-2 list-disc pl-5 text-danger">{result.errors.map((error, index) => <li key={index}>{error}</li>)}</ul>}
    </div>}
    {eventResult && <div role="status" className="rounded-lg bg-brand-soft p-3 text-sm">
      Сохранено Событий-Черновиков: {eventResult.created}. Повторов пропущено: {eventResult.duplicates}.
      {eventResult.errors.length > 0 && <ul className="mt-2 list-disc pl-5 text-danger">{eventResult.errors.map((error, index) => <li key={index}>{error}</li>)}</ul>}
    </div>}
    <h2 className="text-lg font-semibold">События-Черновики</h2>
    <div className="grid gap-4 lg:grid-cols-2">
      {eventDrafts.length === 0 ? <p className="text-sm text-muted-foreground">У этого Источника пока нет Событий-Черновиков.</p> : eventDrafts.map((draft) => {
        const document = JSON.parse(draft.document) as { title?: string; source?: string };
        return <Card key={draft.id}><CardHeader><CardTitle className="flex items-start justify-between gap-3 text-base"><span>{document.title || "Новое Событие"}</span><Badge variant="secondary">Черновик</Badge></CardTitle></CardHeader>
          <CardContent className="space-y-3 text-sm"><p className="text-muted-foreground">{document.source}</p>
            <Button asChild variant="outline"><Link href={`/cms/events/drafts/${draft.id}`}>Открыть Событие</Link></Button>
          </CardContent></Card>;
      })}
    </div>
    <h2 className="text-lg font-semibold">Вопросы-Черновики</h2>
    <div className="grid gap-4 lg:grid-cols-2">
      {drafts.length === 0 ? <p className="text-sm text-muted-foreground">У этого Источника пока нет Вопросов-Черновиков.</p> : drafts.map((draft) =>
        <Card key={draft.id}><CardHeader><CardTitle className="flex items-start justify-between gap-3 text-base"><span>{draft.statement}</span><Badge variant="secondary">Черновик</Badge></CardTitle></CardHeader>
          <CardContent className="space-y-3 text-sm">
            <div className="rounded-lg border-l-4 border-brand bg-brand-soft p-3"><p className="font-semibold">{draft.source}</p><p className="mt-1 whitespace-pre-wrap">«{draft.quote}»</p></div>
            <p>{draft.explanationText}</p>
            <Button asChild variant="outline"><Link href={`/cms/questions?question=${encodeURIComponent(draft.id)}`}>Открыть в банке Вопросов</Link></Button>
          </CardContent></Card>)}
    </div>
  </div>;
}
