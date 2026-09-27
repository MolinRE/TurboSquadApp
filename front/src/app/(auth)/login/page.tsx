"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { ChevronRight, LoaderCircle } from "lucide-react";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { demoAccounts, type DemoAccount } from "@/lib/demo";
import { demoLogin, signOut } from "@/lib/api";

/** «Войти как…»: кнопка входит в демо-аккаунт без пароля. Сюда же ведёт «Сменить аккаунт», поэтому экран сначала выходит. */
export default function Page() {
  const [entering, setEntering] = useState<DemoAccount | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => signOut(), []);

  async function enter(account: DemoAccount) {
    setEntering(account);
    setError(null);
    try {
      await demoLogin(account.username);
      window.location.assign(account.href);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Не удалось войти");
      setEntering(null);
    }
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col gap-1 text-center">
        <h1 className="text-2xl font-extrabold tracking-tight">Войти как…</h1>
        <p className="text-sm text-muted-foreground">Выберите демо-аккаунт</p>
      </div>

      <ul className="flex flex-col gap-2">
        {demoAccounts.map((account) => (
          <li key={account.id}>
            <button
              type="button"
              disabled={entering !== null}
              onClick={() => enter(account)}
              className="flex w-full items-center gap-3 rounded-xl bg-card p-3 pr-4 text-left outline-none transition-colors hover:bg-accent focus-visible:ring-3 focus-visible:ring-ring/50 disabled:opacity-60"
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
              {entering?.id === account.id ? (
                <LoaderCircle className="size-5 shrink-0 animate-spin text-muted-foreground" aria-hidden />
              ) : (
                <ChevronRight className="size-5 shrink-0 text-muted-foreground" aria-hidden />
              )}
            </button>
          </li>
        ))}
      </ul>

      {error && <p role="alert" className="rounded-lg bg-danger-soft p-3 text-sm text-danger">{error}</p>}
      <p className="text-center text-sm text-muted-foreground">
        Нет аккаунта? <Link href="/register" className="font-semibold text-foreground underline-offset-4 hover:underline">Зарегистрироваться</Link>
      </p>
    </div>
  );
}
