"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ArrowLeft, LoaderCircle } from "lucide-react";
import { ApiError } from "@/lib/api";
import {
  getEventVersion, listEvents, publishEvent, validateEvent,
  type EventSummary, type EventVersion, type ValidationReport,
} from "@/lib/events";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { EventForm } from "@/components/cms/event-form";

function editableJson(version: EventVersion): string {
  const document = JSON.parse(version.document) as Record<string, unknown>;
  document.version = version.version + 1;
  return JSON.stringify(document, null, 2);
}

function reportFromError(reason: ApiError): ValidationReport | null {
  const report = reason.payload as unknown as Partial<ValidationReport> | undefined;
  return report && Array.isArray(report.errors) && Array.isArray(report.warnings)
    ? report as ValidationReport : null;
}

export function EventEditor({ eventId }: { eventId: string }) {
  const router = useRouter();
  const [summary, setSummary] = useState<EventSummary | null>(null);
  const [selectedVersion, setSelectedVersion] = useState<number | null>(null);
  const [document, setDocument] = useState("");
  const [original, setOriginal] = useState("");
  const [report, setReport] = useState<ValidationReport | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [accessDenied, setAccessDenied] = useState(false);
  const [versionConflict, setVersionConflict] = useState(false);
  const [view, setView] = useState<"form" | "json">("form");

  const showError = useCallback((reason: unknown) => {
    if (reason instanceof ApiError) {
      if (reason.status === 401) {
        window.localStorage.removeItem("turbo.accessToken");
        router.push("/login");
        setMessage("Срок входа истёк");
        return;
      }
      if (reason.status === 403) {
        setAccessDenied(true);
        return;
      }
      if (reason.status === 409) setVersionConflict(true);
    }
    setMessage(reason instanceof Error ? reason.message : "Не удалось связаться с сервером");
  }, [router]);

  const openVersion = useCallback(async (event: EventSummary, version: number) => {
    const loaded = await getEventVersion(event.id, version);
    const text = version === event.latestVersion
      ? editableJson(loaded)
      : JSON.stringify(JSON.parse(loaded.document), null, 2);
    setDocument(text);
    setOriginal(text);
    setSelectedVersion(version);
    setReport(null);
    setVersionConflict(false);
  }, []);

  useEffect(() => {
    listEvents().then(async (events) => {
      const event = events.find((item) => item.id === eventId);
      if (!event) { setMessage("Событие не найдено"); return; }
      setSummary(event);
      await openVersion(event, event.latestVersion);
    }).catch(showError).finally(() => setLoading(false));
  }, [eventId, openVersion, showError]);

  async function selectVersion(version: number) {
    if (!summary || busy) return;
    if (document !== original && !window.confirm("Несохранённые изменения будут потеряны. Открыть другую версию?")) return;
    setBusy(true);
    setMessage(null);
    try { await openVersion(summary, version); }
    catch (reason) { showError(reason); }
    finally { setBusy(false); }
  }

  async function reloadLatest() {
    if (document !== original && !window.confirm("Несохранённые изменения будут потеряны. Загрузить актуальную версию?")) return;
    setBusy(true);
    try {
      const latest = (await listEvents()).find((item) => item.id === eventId);
      if (!latest) { setMessage("Событие не найдено"); return; }
      setSummary(latest);
      await openVersion(latest, latest.latestVersion);
      setMessage("Актуальная версия загружена");
    } catch (reason) { showError(reason); }
    finally { setBusy(false); }
  }

  async function check() {
    if (!summary) return;
    setBusy(true);
    setMessage(null);
    try {
      const result = await validateEvent(eventId, document);
      setReport(result);
      setMessage(result.isValid ? "Ошибок нет — можно публиковать" : "Исправьте ошибки перед публикацией");
    } catch (reason) { showError(reason); }
    finally { setBusy(false); }
  }

  async function publish() {
    if (!summary || selectedVersion !== summary.latestVersion) return;
    setBusy(true);
    setMessage(null);
    try {
      const published = await publishEvent(eventId, summary.latestVersion, document);
      const fresh = (await listEvents()).find((item) => item.id === eventId);
      if (fresh) {
        setSummary(fresh);
        await openVersion(fresh, published.version);
      }
      setMessage(`Опубликована версия ${published.version}`);
    } catch (reason) {
      if (reason instanceof ApiError && reason.status === 400) {
        const validation = reportFromError(reason);
        if (validation) { setReport(validation); setMessage("Исправьте ошибки перед публикацией"); }
        else showError(reason);
      } else showError(reason);
    } finally { setBusy(false); }
  }

  if (accessDenied) return <p role="alert" className="rounded-lg bg-danger-soft p-4 text-danger">Для редактора Событий нужна роль Методиста.</p>;

  return <div className="space-y-5">
    <Link href="/cms/events" className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground"><ArrowLeft className="size-4" aria-hidden />К списку Событий</Link>
    <div><h1 className="text-2xl font-bold">{summary?.title ?? "Редактор События"}</h1><p className="text-sm text-muted-foreground">{summary?.id ?? eventId} · JSON-документ с графом Шагов</p></div>
    {message && <div role="status" className="flex flex-wrap items-center justify-between gap-3 rounded-lg bg-brand-soft p-4 text-sm"><span>{message}</span>{versionConflict && <Button variant="outline" size="sm" onClick={() => void reloadLatest()} disabled={busy}>Загрузить актуальную версию</Button>}</div>}
    {loading ? <p className="text-sm text-muted-foreground">Загрузка…</p> : summary && selectedVersion !== null ? <Card>
      <CardHeader><CardTitle className="flex flex-wrap items-center gap-3">Документ <Badge>v{selectedVersion}</Badge></CardTitle></CardHeader>
      <CardContent className="space-y-4">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <label className="space-y-1.5 text-sm font-semibold">Версия для просмотра
            <select className="flex h-9 min-w-40 rounded-lg border border-input bg-transparent px-3 text-sm" value={selectedVersion} onChange={(event) => void selectVersion(Number(event.target.value))} disabled={busy}>
              {[...summary.versions].reverse().map((version) => <option key={version.version} value={version.version}>v{version.version}{version.version === summary.latestVersion ? " · актуальная" : ""}</option>)}
            </select>
          </label>
          <p className="text-xs text-muted-foreground">{selectedVersion === summary.latestVersion ? `Следующая версия: v${summary.latestVersion + 1}` : "Прошлая версия доступна только для просмотра"}</p>
        </div>
        <div className="inline-flex rounded-lg bg-muted p-1 text-sm font-semibold" role="tablist">
          {([["form", "Форма"], ["json", "JSON"]] as const).map(([value, label]) => (
            <button key={value} type="button" role="tab" aria-selected={view === value} onClick={() => setView(value)}
              className={view === value ? "rounded-md bg-card px-3 py-1 shadow-sm" : "px-3 py-1 text-muted-foreground"}>{label}</button>
          ))}
        </div>
        {view === "form" ? <EventForm document={document} readOnly={selectedVersion !== summary.latestVersion} onChange={(json) => { setDocument(json); setReport(null); setMessage(null); }} /> : <>
        <label htmlFor="event-json" className="block text-sm font-semibold">JSON События</label>
        <textarea id="event-json" spellCheck={false} className="min-h-[28rem] w-full resize-y rounded-lg border border-input bg-transparent p-3 font-mono text-xs leading-relaxed outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50" value={document} readOnly={selectedVersion !== summary.latestVersion} onChange={(event) => { setDocument(event.target.value); setReport(null); setMessage(null); }} />
        <p className="text-xs text-muted-foreground">Поменяйте переход `transitions` или изменение Шкалы `scaleDeltas` у Варианта, затем проверьте граф.</p>
        </>}
        {selectedVersion === summary.latestVersion && <div className="flex flex-wrap gap-2">
          <Button variant="outline" onClick={() => void check()} disabled={busy}>{busy && <LoaderCircle className="animate-spin" aria-hidden />}Проверить</Button>
          <Button onClick={() => void publish()} disabled={busy}>Опубликовать новую версию</Button>
        </div>}
        {report && <div className="space-y-3 border-t pt-4" aria-live="polite">
          <h2 className="font-semibold">Проверка графа</h2>
          {report.errors.length === 0 && report.warnings.length === 0 && <p className="text-sm text-brand">Ошибок и предупреждений нет.</p>}
          {report.errors.map((issue, index) => <p key={`error-${index}`} className="rounded-lg bg-danger-soft p-3 text-sm text-danger"><strong>{issue.where} · {issue.rule}:</strong> {issue.message}</p>)}
          {report.warnings.map((issue, index) => <p key={`warning-${index}`} className="rounded-lg bg-warning-soft p-3 text-sm text-warning-foreground"><strong>{issue.where} · {issue.rule}:</strong> {issue.message}</p>)}
        </div>}
      </CardContent>
    </Card> : null}
  </div>;
}
