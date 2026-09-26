import type { Metadata } from "next";
import Link from "next/link";
import { ChevronRight } from "lucide-react";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { demoAccounts } from "@/lib/demo";
import { getScreen } from "@/lib/screens";

export const metadata: Metadata = { title: getScreen("login").title };

export default function Page() {
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col gap-1 text-center">
        <h1 className="text-2xl font-extrabold tracking-tight">Войти как…</h1>
        <p className="text-sm text-muted-foreground">
          Демо-аккаунты с заранее насчитанной историей
        </p>
      </div>

      <ul className="flex flex-col gap-2">
        {demoAccounts.map((account) => (
          <li key={account.id}>
            <Link
              href={account.href}
              className="flex items-center gap-3 rounded-xl bg-card p-3 pr-4 outline-none transition-colors hover:bg-accent focus-visible:ring-3 focus-visible:ring-ring/50"
            >
              <Avatar className="size-11">
                <AvatarFallback className="bg-primary text-sm font-extrabold text-primary-foreground">
                  {account.initials}
                </AvatarFallback>
              </Avatar>
              <span className="flex min-w-0 flex-1 flex-col">
                <span className="text-xs font-bold tracking-wider text-brand uppercase">
                  {account.label}
                </span>
                <span className="truncate font-bold">{account.name}</span>
                <span className="truncate text-xs text-muted-foreground">
                  {account.description}
                </span>
              </span>
              <ChevronRight
                className="size-5 shrink-0 text-muted-foreground"
                aria-hidden
              />
            </Link>
          </li>
        ))}
      </ul>

      <p className="text-center text-sm text-muted-foreground">
        Нет аккаунта?{" "}
        <Link
          href="/register"
          className="font-semibold text-foreground underline-offset-4 hover:underline"
        >
          Зарегистрироваться
        </Link>
      </p>
    </div>
  );
}
