"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { TripDebriefFacts } from "@/components/game/trip-debrief-facts";
import { getTripDebrief, type TripDebrief } from "@/lib/api";

export function TripReview({ tripId }: { tripId: string }) {
  const [debrief, setDebrief] = useState<TripDebrief | null>(null);
  const [error, setError] = useState(false);

  useEffect(() => {
    let active = true;
    getTripDebrief(tripId).then(
      (value) => { if (active) setDebrief(value); },
      () => { if (active) setError(true); },
    );
    return () => { active = false; };
  }, [tripId]);

  return (
    <main className="space-y-4 px-4 pb-8">
      <div>
        <p className="text-sm text-muted-foreground">Симулятор рейса</p>
        <h1 className="text-2xl font-extrabold">Разбор Рейса</h1>
      </div>
      {error && <p role="alert" className="text-sm text-danger">Разбор не найден или недоступен.</p>}
      {!error && !debrief && <Skeleton className="h-32 w-full" />}
      {debrief && <>
        <p className={debrief.result === "failed" ? "text-danger" : "text-brand"}>{debrief.summary}</p>
        <TripDebriefFacts debrief={debrief} />
      </>}
      <Button asChild variant="outline"><Link href="/profile">К истории Разборов</Link></Button>
    </main>
  );
}
