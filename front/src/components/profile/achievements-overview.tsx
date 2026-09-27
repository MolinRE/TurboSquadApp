"use client";

import { useEffect, useState } from "react";
import { Award, Check } from "lucide-react";
import { apiJson } from "@/lib/api";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";

type Achievement = { id: string; name: string; description: string; earnedAt: string | null };

export function AchievementsOverview() {
  const [items, setItems] = useState<Achievement[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    apiJson<Achievement[]>("/api/achievements", { cache: "no-store" }).then(
      value => active && setItems(value),
      () => active && setError("Не удалось загрузить Ачивки. Попробуйте открыть профиль ещё раз."),
    );
    return () => { active = false; };
  }, []);

  return (
    <Card>
      <CardHeader><CardTitle className="flex items-center gap-2"><Award className="size-5 text-brand" aria-hidden />Ачивки</CardTitle></CardHeader>
      <CardContent className="space-y-3">
        {error && <p role="alert" className="text-sm text-danger">{error}</p>}
        {!items && !error && Array.from({ length: 3 }, (_, index) => <Skeleton key={index} className="h-14 w-full" />)}
        {items?.map(item => (
          <div key={item.id} className={`flex gap-3 rounded-lg p-3 ${item.earnedAt ? "bg-brand-soft" : "bg-muted/50 opacity-75"}`}>
            <div className={`grid size-8 shrink-0 place-items-center rounded-full ${item.earnedAt ? "bg-brand text-white" : "bg-background text-muted-foreground"}`}>
              {item.earnedAt ? <Check className="size-4" aria-hidden /> : <Award className="size-4" aria-hidden />}
            </div>
            <div className="min-w-0">
              <p className="font-bold">{item.name}</p>
              <p className="text-xs text-muted-foreground">{item.description}</p>
              {item.earnedAt && <p className="mt-1 text-xs font-semibold text-brand">Получена {new Date(item.earnedAt).toLocaleDateString("ru-RU")}</p>}
            </div>
          </div>
        ))}
      </CardContent>
    </Card>
  );
}
