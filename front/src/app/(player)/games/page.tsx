import type { Metadata } from "next";
import { GameList } from "@/components/games/game-list";
import { getScreen } from "@/lib/screens";

export const metadata: Metadata = { title: getScreen("games").title };

export default function Page() {
  return (
    <>
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-extrabold tracking-tight">Игры</h1>
        <p className="text-sm text-muted-foreground">Три тренировки по регламенту проводника ВСМ. Очки из каждой идут в Звание и Рейтинг.</p>
      </div>
      <GameList />
    </>
  );
}
