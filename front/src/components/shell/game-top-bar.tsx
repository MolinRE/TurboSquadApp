"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { X } from "lucide-react";
import { findScreenByPathname } from "@/lib/screens";

/** Верх шаблона «Игра»: выход и название игры. Шкалы и таймер рисует сама игра. */
export function GameTopBar() {
  const screen = findScreenByPathname(usePathname());

  return (
    <header className="grid grid-cols-[2.75rem_1fr_2.75rem] items-center gap-2 px-4 pt-[calc(env(safe-area-inset-top)+0.75rem)] pb-3">
      <Link
        href="/games"
        aria-label="Выйти из игры"
        className="grid size-11 place-items-center rounded-full bg-card text-foreground outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
      >
        <X className="size-5" aria-hidden />
      </Link>
      <p className="truncate text-center text-sm font-extrabold">
        {screen?.title}
      </p>
      <span aria-hidden />
    </header>
  );
}
