"use client";

import { useEffect, useState } from "react";
import { RotateCw, Trophy } from "lucide-react";
import { apiJson } from "@/lib/api";
import { useCurrentUser } from "@/lib/use-current-user";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";

type ConductorEntry = {
  position: number;
  userId: string;
  displayName: string;
  brigadeName: string;
  depotName: string;
  competencePoints: number;
};

type BrigadeEntry = {
  position: number;
  brigadeId: string;
  brigadeName: string;
  depotName: string;
  averageCompetencePoints: number | null;
  memberCount: number;
};

type Leaderboard = {
  currentUserId: string;
  brigade: ConductorEntry[];
  depot: ConductorEntry[];
  company: ConductorEntry[];
  brigades: BrigadeEntry[];
};

type Scope = "brigade" | "depot" | "company" | "brigades";

const scopes: Array<{ key: Scope; label: string }> = [
  { key: "brigade", label: "Моя бригада" },
  { key: "depot", label: "Моё депо" },
  { key: "company", label: "Компания" },
  { key: "brigades", label: "Бригады" },
];

const number = new Intl.NumberFormat("ru-RU", { maximumFractionDigits: 1 });

export function LeaderboardOverview() {
  const user = useCurrentUser();
  const [scope, setScope] = useState<Scope>("brigade");
  const [board, setBoard] = useState<Leaderboard | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [reload, setReload] = useState(0);

  useEffect(() => {
    let active = true;
    apiJson<Leaderboard>("/api/leaderboard", { cache: "no-store" }).then(
      (value) => { if (active) { setBoard(value); setError(null); } },
      () => { if (active) setError("Не удалось загрузить рейтинг. Попробуйте обновить его."); },
    );
    return () => { active = false; };
  }, [reload]);

  const entries = board && scope !== "brigades" ? board[scope] : [];
  const ownBrigade = user ? `${user.brigade} · ${user.depot}` : null;

  return (
    <section className="space-y-4 pb-8">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-sm text-muted-foreground">Очки компетенций</p>
          <h1 className="text-2xl font-extrabold">Рейтинг</h1>
        </div>
        <Button variant="outline" size="icon" aria-label="Обновить рейтинг" onClick={() => setReload(value => value + 1)}>
          <RotateCw aria-hidden />
        </Button>
      </div>

      <div className="grid grid-cols-2 gap-2" role="group" aria-label="Уровень рейтинга">
        {scopes.map(item => (
          <Button key={item.key} type="button" variant={scope === item.key ? "default" : "outline"}
            aria-pressed={scope === item.key} onClick={() => setScope(item.key)}>
            {item.label}
          </Button>
        ))}
      </div>

      <p className="text-sm text-muted-foreground">
        {scope === "brigades" ? "Средние Очки компетенций участников каждой бригады." :
          "Общий лидерборд по текущим Очкам компетенций Проводников."}
      </p>
      {error && <p role="alert" className="text-sm text-danger">{error}</p>}

      {!board && !error && Array.from({ length: 4 }, (_, index) => (
        <Card key={index}><CardContent className="flex items-center gap-3">
          <Skeleton className="size-10" /><Skeleton className="h-5 flex-1" /><Skeleton className="h-7 w-12" />
        </CardContent></Card>
      ))}

      {board && scope === "brigades" && board.brigades.map(entry => (
        <Card key={entry.brigadeId} className={ownBrigade === `${entry.brigadeName} · ${entry.depotName}` ? "ring-2 ring-brand" : ""}>
          <CardContent className="flex items-center gap-3">
            <span className="w-8 text-center text-lg font-extrabold tabular-nums text-muted-foreground">{entry.position}</span>
            <div className="min-w-0 flex-1">
              <p className="truncate font-bold">{entry.brigadeName}</p>
              <p className="text-xs text-muted-foreground">{entry.depotName} · {entry.memberCount} чел.</p>
            </div>
            <div className="text-right">
              <p className="text-xl font-extrabold tabular-nums">{entry.averageCompetencePoints === null ? "—" : number.format(entry.averageCompetencePoints)}</p>
              <p className="text-xs text-muted-foreground">в среднем</p>
            </div>
          </CardContent>
        </Card>
      ))}

      {board && scope !== "brigades" && entries.map(entry => (
        <Card key={entry.userId} className={entry.userId === board.currentUserId ? "ring-2 ring-brand" : ""}>
          <CardContent className="flex items-center gap-3">
            <span className="w-8 text-center text-lg font-extrabold tabular-nums text-muted-foreground">{entry.position}</span>
            <div className="min-w-0 flex-1">
              <p className="truncate font-bold">{entry.displayName}{entry.userId === board.currentUserId ? " · Вы" : ""}</p>
              <p className="truncate text-xs text-muted-foreground">{entry.brigadeName} · {entry.depotName}</p>
            </div>
            <p className="text-xl font-extrabold tabular-nums">{entry.competencePoints}</p>
          </CardContent>
        </Card>
      ))}

      {board && (scope === "brigades" ? board.brigades.length === 0 : entries.length === 0) && (
        <Card><CardContent className="flex items-center gap-3 text-muted-foreground">
          <Trophy className="size-5" aria-hidden />Пока нет участников рейтинга.
        </CardContent></Card>
      )}
    </section>
  );
}
