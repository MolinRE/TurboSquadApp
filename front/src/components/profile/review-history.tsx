"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { listTripDebriefs, type TripDebriefListItem } from "@/lib/api";
import { listSwipeDebriefs } from "@/lib/swipes/api";
import type { SwipeDebriefListItem } from "@/lib/swipes/contract";

type ReviewItem =
  | { kind: "trip"; item: TripDebriefListItem }
  | { kind: "swipe"; item: SwipeDebriefListItem };

export function ReviewHistory() {
  const [reviews, setReviews] = useState<ReviewItem[] | null>(null);
  const [error, setError] = useState(false);

  useEffect(() => {
    let active = true;
    Promise.all([listTripDebriefs(), listSwipeDebriefs()]).then(
      ([trips, shifts]) => {
        if (!active) return;
        setReviews([
          ...trips.map((item): ReviewItem => ({ kind: "trip", item })),
          ...shifts.map((item): ReviewItem => ({ kind: "swipe", item })),
        ].sort((a, b) => Date.parse(b.item.finishedAt) - Date.parse(a.item.finishedAt)));
      },
      () => { if (active) setError(true); },
    );
    return () => { active = false; };
  }, []);

  return (
    <Card>
      <CardHeader><CardTitle>История Разборов</CardTitle></CardHeader>
      <CardContent>
        {error && <p role="alert" className="text-sm text-danger">Не удалось загрузить историю Разборов.</p>}
        {!reviews && !error && <Skeleton className="h-16 w-full" />}
        {reviews?.length === 0 && <p className="text-sm text-muted-foreground">Завершённых Рейсов и Смен пока нет.</p>}
        {reviews && reviews.length > 0 && <ul className="divide-y divide-border">
          {reviews.map(({ kind, item }) => <li key={`${kind}-${item.id}`}>
            <Link className="block py-3 text-sm hover:text-brand focus-visible:underline" href={`/reviews/${kind}-${item.id}`}>
              <span className="font-semibold">{kind === "trip"
                ? `Рейс — ${item.result === "arrived" ? "Прибытие" : "Срыв рейса"}`
                : `Смена на свайпах — ${item.result === "passed" ? "пройдена" : "Срыв смены"}`}</span>
              <span className="mt-1 block text-xs text-muted-foreground">{new Date(item.finishedAt).toLocaleString("ru-RU")}</span>
            </Link>
          </li>)}
        </ul>}
      </CardContent>
    </Card>
  );
}
