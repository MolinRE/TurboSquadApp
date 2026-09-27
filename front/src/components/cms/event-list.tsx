"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ApiError } from "@/lib/api";
import { createEventDraft, listEventDrafts, listEvents, type EventDraft, type EventSummary } from "@/lib/events";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

export function EventList() {
  const router = useRouter();
  const [events, setEvents] = useState<EventSummary[]>([]);
  const [drafts, setDrafts] = useState<EventDraft[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [creating, setCreating] = useState(false);

  useEffect(() => {
    Promise.all([listEvents(), listEventDrafts()])
      .then(([published, unpublished]) => { setEvents(published); setDrafts(unpublished); })
      .catch((reason: unknown) => {
        if (reason instanceof ApiError && reason.status === 401) {
          window.localStorage.removeItem("turbo.accessToken");
          router.push("/login");
        }
        setError(reason instanceof ApiError && reason.status === 403
          ? "Для редактора Событий нужна роль Методиста"
          : reason instanceof Error ? reason.message : "Не удалось загрузить События");
      })
      .finally(() => setLoading(false));
  }, [router]);

  async function create() {
    setCreating(true);
    setError(null);
    try {
      const draft = await createEventDraft();
      router.push(`/cms/events/drafts/${draft.id}`);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Не удалось создать Событие");
      setCreating(false);
    }
  }

  return <div className="space-y-5">
    <div className="flex flex-wrap items-start justify-between gap-3"><div><h1 className="text-2xl font-bold">События</h1><p className="text-sm text-muted-foreground">Черновики и опубликованные версии Событий Симулятора рейса</p></div>
      {!loading && !error && <Button onClick={() => void create()} disabled={creating}>{creating ? "Создание…" : "Добавить Событие"}</Button>}
    </div>
    {error ? <p role="alert" className="rounded-lg bg-danger-soft p-4 text-danger">{error}</p> :
      <>
      <Card><CardHeader><CardTitle>Черновики</CardTitle></CardHeader><CardContent>
        {loading ? <p className="text-sm text-muted-foreground">Загрузка…</p> : drafts.length === 0 ?
          <p className="text-sm text-muted-foreground">Черновиков пока нет</p> :
          <ul className="space-y-3">{drafts.map((draft) => {
            const document = JSON.parse(draft.document) as { title?: string; topic?: string; source?: string };
            return <li key={draft.id}><Link href={`/cms/events/drafts/${draft.id}`} className="block rounded-lg border border-border p-4 transition-colors hover:bg-muted focus-visible:ring-3 focus-visible:ring-ring/50">
              <span className="flex flex-wrap items-center justify-between gap-2"><span className="font-semibold">{document.title || "Новое Событие"}</span><Badge variant="secondary">Черновик</Badge></span>
              <span className="mt-1 block text-sm text-muted-foreground">{document.topic || "Тема не указана"}{document.source ? ` · ${document.source}` : ""}</span>
            </Link></li>;
          })}</ul>}
      </CardContent></Card>
      <Card><CardHeader><CardTitle>Опубликованные События</CardTitle></CardHeader><CardContent>
        {loading ? <p className="text-sm text-muted-foreground">Загрузка…</p> : events.length === 0 ?
          <p className="text-sm text-muted-foreground">Событий пока нет</p> :
          <ul className="space-y-3">{events.map((event) => <li key={event.id}>
            <Link href={`/cms/events/${encodeURIComponent(event.id)}`} className="block rounded-lg border border-border p-4 transition-colors hover:bg-muted focus-visible:ring-3 focus-visible:ring-ring/50">
              <span className="flex flex-wrap items-center justify-between gap-2"><span className="font-semibold">{event.title}</span><Badge>v{event.latestVersion}</Badge></span>
              <span className="mt-1 block text-sm text-muted-foreground">{event.topic} · {event.serviceClasses.length ? event.serviceClasses.join(", ") : "Все классы"} · {event.id}</span>
              <span className="mt-1 block text-xs text-muted-foreground">Версии: {event.versions.map((version) => `v${version.version}`).join(", ")}</span>
            </Link>
          </li>)}</ul>}
      </CardContent></Card></>}
  </div>;
}
