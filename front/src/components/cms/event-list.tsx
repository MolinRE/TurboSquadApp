"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ApiError } from "@/lib/api";
import { listEvents, type EventSummary } from "@/lib/events";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

export function EventList() {
  const router = useRouter();
  const [events, setEvents] = useState<EventSummary[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    listEvents()
      .then(setEvents)
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

  return <div className="space-y-5">
    <div><h1 className="text-2xl font-bold">События</h1><p className="text-sm text-muted-foreground">Опубликованные События Симулятора рейса и их версии</p></div>
    {error ? <p role="alert" className="rounded-lg bg-danger-soft p-4 text-danger">{error}</p> :
      <Card><CardHeader><CardTitle>Список Событий</CardTitle></CardHeader><CardContent>
        {loading ? <p className="text-sm text-muted-foreground">Загрузка…</p> : events.length === 0 ?
          <p className="text-sm text-muted-foreground">Событий пока нет</p> :
          <ul className="space-y-3">{events.map((event) => <li key={event.id}>
            <Link href={`/cms/events/${encodeURIComponent(event.id)}`} className="block rounded-lg border border-border p-4 transition-colors hover:bg-muted focus-visible:ring-3 focus-visible:ring-ring/50">
              <span className="flex flex-wrap items-center justify-between gap-2"><span className="font-semibold">{event.title}</span><Badge>v{event.latestVersion}</Badge></span>
              <span className="mt-1 block text-sm text-muted-foreground">{event.topic} · {event.serviceClasses.length ? event.serviceClasses.join(", ") : "Все классы"} · {event.id}</span>
              <span className="mt-1 block text-xs text-muted-foreground">Версии: {event.versions.map((version) => `v${version.version}`).join(", ")}</span>
            </Link>
          </li>)}</ul>}
      </CardContent></Card>}
  </div>;
}
