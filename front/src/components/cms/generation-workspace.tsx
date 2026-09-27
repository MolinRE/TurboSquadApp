"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ApiError } from "@/lib/api";
import { generateQuestions, listSourceDrafts, listSources, type GenerationResult, type SourceSummary } from "@/lib/sources";
import type { Question } from "@/lib/questions";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

export function GenerationWorkspace() {
  const router = useRouter();
  const [sources, setSources] = useState<SourceSummary[]>([]);
  const [sourceId, setSourceId] = useState("");
  const [drafts, setDrafts] = useState<Question[]>([]);
  const [result, setResult] = useState<GenerationResult | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");

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
    listSourceDrafts(sourceId).then(setDrafts).catch((error) =>
      setMessage(error instanceof Error ? error.message : "Не удалось загрузить Черновики"));
  }, [sourceId]);

  async function generate() {
    setBusy(true);
    setMessage("");
    try {
      const generated = await generateQuestions(sourceId);
      setResult(generated);
      setDrafts(await listSourceDrafts(sourceId));
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Не удалось сгенерировать Вопросы");
    } finally { setBusy(false); }
  }

  return <div className="space-y-6">
    <div><h1 className="text-2xl font-bold">Генерация Вопросов</h1><p className="text-sm text-muted-foreground">Результаты сохраняются Черновиками. Проверьте каждый Вопрос перед публикацией.</p></div>
    {message && <p role="alert" className="rounded-lg bg-danger-soft p-3 text-sm text-danger">{message}</p>}
    <Card><CardContent className="flex flex-wrap items-end gap-3 pt-6">
      <label className="min-w-64 flex-1 text-sm font-semibold">Источник
        <select className="mt-2 block h-9 w-full rounded-lg border border-input bg-transparent px-3 text-sm" value={sourceId} onChange={(event) => { setSourceId(event.target.value); setResult(null); }}>
          {sources.map((source) => <option key={source.id} value={source.id}>{source.title}</option>)}
        </select>
      </label>
      <Button onClick={() => void generate()} disabled={!sourceId || busy}>{busy ? "Генерация…" : "Сгенерировать Вопросы"}</Button>
      <Button asChild variant="outline"><Link href="/cms/sources">Добавить Источник</Link></Button>
    </CardContent></Card>
    {result && <div role="status" className="rounded-lg bg-brand-soft p-3 text-sm">
      Сохранено Черновиков: {result.created}. Повторов пропущено: {result.duplicates}.
      {result.errors.length > 0 && <ul className="mt-2 list-disc pl-5 text-danger">{result.errors.map((error, index) => <li key={index}>{error}</li>)}</ul>}
    </div>}
    <div className="grid gap-4 lg:grid-cols-2">
      {drafts.length === 0 ? <p className="text-sm text-muted-foreground">У этого Источника пока нет Черновиков.</p> : drafts.map((draft) =>
        <Card key={draft.id}><CardHeader><CardTitle className="flex items-start justify-between gap-3 text-base"><span>{draft.statement}</span><Badge variant="secondary">Черновик</Badge></CardTitle></CardHeader>
          <CardContent className="space-y-3 text-sm">
            <div className="rounded-lg border-l-4 border-brand bg-brand-soft p-3"><p className="font-semibold">{draft.source}</p><p className="mt-1 whitespace-pre-wrap">«{draft.quote}»</p></div>
            <p>{draft.explanationText}</p>
            <Button asChild variant="outline"><Link href={`/cms/questions?question=${encodeURIComponent(draft.id)}`}>Открыть в банке Вопросов</Link></Button>
          </CardContent></Card>)}
    </div>
  </div>;
}
