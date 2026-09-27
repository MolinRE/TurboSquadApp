import type { Metadata } from "next";
import { GameList } from "@/components/games/game-list";
import { getScreen } from "@/lib/screens";

export const metadata: Metadata = { title: getScreen("home").title };

export default function Page() {
  return (
    <>
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-extrabold tracking-tight">Чем займёмся сегодня?</h1>
        <p className="text-sm text-muted-foreground">
          Начните с Симулятора рейса или разомнитесь коротким Блицем. Прогресс и Разборы — в Профиле, место среди коллег — в Рейтинге.
        </p>
      </div>
      <GameList />
    </>
  );
}
