"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ApiError } from "@/lib/api";
import { createSource, getSource, listSources, type Source, type SourceSummary } from "@/lib/sources";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";

export function SourcesWorkspace() {
  const router = useRouter();
  const [sources, setSources] = useState<SourceSummary[]>([]);
  const [selected, setSelected] = useState<Source | null>(null);
  const [title, setTitle] = useState("");
  const [text, setText] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");

  useEffect(() => {
    listSources().then(setSources).catch((error) => {
      if (error instanceof ApiError && error.status === 401) router.push("/login");
      else setMessage(error instanceof Error ? error.message : "Не удалось загрузить Источники");
    });
  }, [router]);

  async function save() {
    setBusy(true);
    setMessage("");
    try {
      const source = await createSource(title, text);
      setSources(await listSources());
      setSelected(source);
      setTitle("");
      setText("");
      setMessage("Источник сохранён. Теперь можно запустить генерацию.");
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Не удалось сохранить Источник");
    } finally { setBusy(false); }
  }

  async function open(id: string) {
    setMessage("");
    try { setSelected(await getSource(id)); }
    catch (error) { setMessage(error instanceof Error ? error.message : "Не удалось открыть Источник"); }
  }

  return <div className="space-y-6">
    <div><h1 className="text-2xl font-bold">Источники</h1><p className="text-sm text-muted-foreground">Вставьте текст документа и сохраните его для генерации Вопросов.</p></div>
    {message && <p role="status" className="rounded-lg bg-brand-soft p-3 text-sm">{message}</p>}
    <div className="grid gap-5 lg:grid-cols-[minmax(18rem,1fr)_minmax(22rem,2fr)]">
      <Card><CardHeader><CardTitle>Сохранённые Источники</CardTitle></CardHeader><CardContent className="space-y-2">
        {sources.length === 0 ? <p className="text-sm text-muted-foreground">Пока нет Источников</p> : sources.map((source) =>
          <button key={source.id} type="button" onClick={() => void open(source.id)} className="block w-full rounded-lg border border-border p-3 text-left hover:bg-muted">
            <span className="block font-semibold">{source.title}</span><span className="text-xs text-muted-foreground">{new Date(source.createdAt).toLocaleDateString("ru-RU")}</span>
          </button>)}
      </CardContent></Card>
      <div className="space-y-5">
        <Card><CardHeader><CardTitle>Новый Источник</CardTitle></CardHeader><CardContent className="space-y-3">
          <label className="block text-sm font-semibold" htmlFor="source-title">Название</label>
          <Input id="source-title" maxLength={200} value={title} onChange={(event) => setTitle(event.target.value)} />
          <label className="block text-sm font-semibold" htmlFor="source-text">Текст</label>
          <textarea id="source-text" className="min-h-64 w-full resize-y rounded-lg border border-input bg-transparent p-3 text-sm" value={text} onChange={(event) => setText(event.target.value)} />
          <Button onClick={() => void save()} disabled={busy || !title.trim() || !text.trim()}>{busy ? "Сохранение…" : "Сохранить Источник"}</Button>
        </CardContent></Card>
        {selected && <Card><CardHeader><CardTitle>{selected.title}</CardTitle></CardHeader><CardContent className="space-y-3">
          <p className="max-h-64 overflow-y-auto whitespace-pre-wrap text-sm">{selected.text}</p>
          <Button asChild><Link href={`/cms/generation?source=${encodeURIComponent(selected.id)}`}>Перейти к генерации</Link></Button>
        </CardContent></Card>}
      </div>
    </div>
  </div>;
}
