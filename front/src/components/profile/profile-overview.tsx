"use client";

import { useEffect, useState } from "react";
import { Award, BookOpen } from "lucide-react";
import { apiJson } from "@/lib/api";
import { useCurrentUser } from "@/lib/use-current-user";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";

type Profile = { knowledgePoints: number; competencePoints: number; rank: string };

export function ProfileOverview() {
  const user = useCurrentUser();
  const [profile, setProfile] = useState<Profile | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    apiJson<Profile>("/api/profile").then(
      (value) => { if (active) setProfile(value); },
      () => { if (active) setError("Не удалось загрузить Очки. Попробуйте открыть профиль ещё раз."); },
    );
    return () => { active = false; };
  }, []);

  return (
    <main className="space-y-4 px-4 pb-8">
      <div>
        <p className="text-sm text-muted-foreground">Профиль Проводника</p>
        <h1 className="text-2xl font-extrabold">{user?.displayName ?? "Мой профиль"}</h1>
      </div>
      {error && <p role="alert" className="text-sm text-danger">{error}</p>}
      <Card>
        <CardHeader><CardTitle className="flex items-center gap-2"><Award className="size-5 text-brand" aria-hidden />Звание</CardTitle></CardHeader>
        <CardContent>{profile ? <p className="text-xl font-extrabold">{profile.rank}</p> : <Skeleton className="h-7 w-40" />}</CardContent>
      </Card>
      <Card>
        <CardHeader><CardTitle className="flex items-center gap-2"><BookOpen className="size-5 text-brand" aria-hidden />Очки компетенций</CardTitle></CardHeader>
        <CardContent>{profile ? <><p className="text-4xl font-extrabold tabular-nums">{profile.competencePoints}</p><p className="mt-2 text-sm text-muted-foreground">Очки знаний: {profile.knowledgePoints}. На этом этапе Очки компетенций складываются из Знания регламента.</p></> : <Skeleton className="h-10 w-28" />}</CardContent>
      </Card>
    </main>
  );
}
