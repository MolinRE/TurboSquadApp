"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { House, LayoutGrid, Trophy, UserRound } from "lucide-react";
import { cn } from "cn";

const tabs = [
  { href: "/home", label: "Главная", icon: House },
  { href: "/games", label: "Игры", icon: LayoutGrid },
  { href: "/profile", label: "Профиль", icon: UserRound },
  { href: "/leaderboard", label: "Рейтинг", icon: Trophy },
];

export function PlayerTabBar() {
  const pathname = usePathname();

  return (
    <nav
      aria-label="Разделы"
      className="fixed inset-x-0 bottom-0 z-20 mx-auto grid w-full max-w-md grid-cols-4 border-t bg-card px-1.5 pt-2 pb-[calc(env(safe-area-inset-bottom)+0.75rem)]"
    >
      {tabs.map(({ href, label, icon: Icon }) => {
        const active = pathname === href || pathname.startsWith(href + "/");
        return (
          <Link
            key={href}
            href={href}
            aria-current={active ? "page" : undefined}
            className={cn(
              "flex flex-col items-center gap-1 rounded-lg py-1 text-[11px] font-semibold text-muted-foreground outline-none focus-visible:ring-3 focus-visible:ring-ring/50",
              active && "text-foreground",
            )}
          >
            <Icon
              className={cn("size-[22px]", active && "text-brand")}
              aria-hidden
            />
            {label}
          </Link>
        );
      })}
    </nav>
  );
}
