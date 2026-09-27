"use client";

import Link from "next/link";
import { Bell } from "lucide-react";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Skeleton } from "@/components/ui/skeleton";
import { demoUnreadNotifications } from "@/lib/demo";
import { initialsOf, unitOf, useCurrentUser } from "@/lib/use-current-user";

export function PlayerHeader() {
  const user = useCurrentUser();
  const unread = demoUnreadNotifications;

  return (
    <header className="sticky top-0 z-20 flex items-center justify-between gap-3 bg-background/95 px-4 pt-[calc(env(safe-area-inset-top)+0.75rem)] pb-3 backdrop-blur">
      <Link href="/profile" className="flex min-w-0 items-center gap-3">
        <Avatar className="size-10">
          <AvatarFallback className="bg-primary text-sm font-extrabold text-primary-foreground">
            {user ? initialsOf(user.displayName) : null}
          </AvatarFallback>
        </Avatar>
        {user ? (
          <span className="flex min-w-0 flex-col">
            <span className="truncate font-extrabold leading-tight">{user.displayName}</span>
            <span className="truncate text-xs text-muted-foreground">{unitOf(user)}</span>
          </span>
        ) : (
          <span className="flex flex-col gap-1.5">
            <Skeleton className="h-4 w-32" />
            <Skeleton className="h-3 w-40" />
          </span>
        )}
      </Link>
      <Link
        href="/notifications"
        aria-label={`Уведомления: ${unread} новых`}
        className="relative grid size-11 shrink-0 place-items-center rounded-full bg-card text-foreground outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
      >
        <Bell className="size-5" aria-hidden />
        {unread > 0 && (
          <span className="absolute top-1 right-0.5 grid h-[18px] min-w-[18px] place-items-center rounded-full border-2 border-background bg-danger px-1 text-[10px] font-extrabold text-white tabular-nums">
            {unread}
          </span>
        )}
      </Link>
    </header>
  );
}
