"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ArrowLeft, LoaderCircle } from "lucide-react";
import { ApiError } from "@/lib/api";
import { getEventDraft, publishEventDraft, saveEventDraft, validateEventDraft, type ValidationReport } from "@/lib/events";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

function formatted(json: string): string {
  try { return JSON.stringify(JSON.parse(json), null, 2); }
  catch { return json; }
}

export function EventDraftEditor({ draftId }: { draftId: string }) {
  const router = useRouter();
  const [document, setDocument] = useState("");
  const [original, setOriginal] = useState("");
  const [report, setReport] = useState<ValidationReport | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [accessDenied, setAccessDenied] = useState(false);

  useEffect(() => {
    getEventDraft(draftId).then((draft) => {
      const text = formatted(draft.document);
      setDocument(text);
      setOriginal(text);
    }).catch(showError).finally(() => setLoading(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [draftId]);

  function showError(reason: unknown) {
    if (reason instanceof ApiError && reason.status === 401) {
      router.push("/login");
      return;
    }
    if (reason instanceof ApiError && reason.status === 403) {
      setAccessDenied(true);
      return;
    }
    if (reason instanceof ApiError && reason.status === 400) {
      const payload = reason.payload as unknown as Partial<ValidationReport> | undefined;
      if (payload && Array.isArray(payload.errors) && Array.isArray(payload.warnings))
        setReport(payload as ValidationReport);
    }
    setMessage(reason instanceof Error ? reason.message : "Не удалось связаться с сервером");
  }

  async function persist() {
    if (document !== original) {
      await saveEventDraft(draftId, document);
      setOriginal(document);
    }
  }

  async function save() {
    setBusy(true);
    setMessage(null);
    try { await persist(); setMessage("Черновик сохранён"); }
    catch (reason) { showError(reason); }
    finally { setBusy(false); }
  }

  async function check() {
    setBusy(true);
    setMessage(null);
    try {
      await persist();
      const validation = await validateEventDraft(draftId);
      setReport(validation);
      setMessage(validation.isValid ? "Ошибок нет — можно публиковать" : "Исправьте ошибки перед публикацией");
    } catch (reason) { showError(reason); }
    finally { setBusy(false); }
  }

  async function publish() {
    setBusy(true);
    setMessage(null);
    try {
      await persist();
      const published = await publishEventDraft(draftId);
      router.push(`/cms/events/${encodeURIComponent(published.id)}`);
    } catch (reason) { showError(reason); setBusy(false); }
  }

  if (accessDenied) return <p role="alert" className="rounded-lg bg-danger-soft p-4 text-danger">Для редактора Событий нужна роль Методиста.</p>;

  return <div className="space-y-5">
    <Link href="/cms/events" className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground"><ArrowLeft className="size-4" aria-hidden />К списку Событий</Link>
    <div><h1 className="text-2xl font-bold">Новое Событие</h1><p className="text-sm text-muted-foreground">Черновик · JSON-документ с графом Шагов</p></div>
    {message && <p role="status" className="rounded-lg bg-brand-soft p-4 text-sm">{message}</p>}
    {loading ? <p className="text-sm text-muted-foreground">Загрузка…</p> : <Card>
      <CardHeader><CardTitle className="flex items-center gap-3">Документ <Badge variant="secondary">Черновик</Badge></CardTitle></CardHeader>
      <CardContent className="space-y-4">
        <label htmlFor="event-draft-json" className="block text-sm font-semibold">JSON События</label>
        <textarea id="event-draft-json" spellCheck={false} className="min-h-[28rem] w-full resize-y rounded-lg border border-input bg-transparent p-3 font-mono text-xs leading-relaxed outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50" value={document} onChange={(event) => { setDocument(event.target.value); setReport(null); setMessage(null); }} />
        <p className="text-xs text-muted-foreground">Заполните ситуацию, Варианты и Исходы, затем проверьте граф. В Рейс Событие попадёт после публикации.</p>
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" onClick={() => void save()} disabled={busy}>{busy && <LoaderCircle className="animate-spin" aria-hidden />}Сохранить Черновик</Button>
          <Button variant="outline" onClick={() => void check()} disabled={busy}>Проверить</Button>
          <Button onClick={() => void publish()} disabled={busy}>Опубликовать Событие</Button>
        </div>
        {report && <div className="space-y-3 border-t pt-4" aria-live="polite">
          <h2 className="font-semibold">Проверка графа</h2>
          {report.errors.length === 0 && report.warnings.length === 0 && <p className="text-sm text-brand">Ошибок и предупреждений нет.</p>}
          {report.errors.map((issue, index) => <p key={`error-${index}`} className="rounded-lg bg-danger-soft p-3 text-sm text-danger"><strong>{issue.where} · {issue.rule}:</strong> {issue.message}</p>)}
          {report.warnings.map((issue, index) => <p key={`warning-${index}`} className="rounded-lg bg-warning-soft p-3 text-sm text-warning-foreground"><strong>{issue.where} · {issue.rule}:</strong> {issue.message}</p>)}
        </div>}
      </CardContent>
    </Card>}
  </div>;
}
