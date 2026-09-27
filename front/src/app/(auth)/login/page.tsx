"use client";

import Link from "next/link";
import { useEffect, useState, type FormEvent } from "react";
import { ChevronRight, LoaderCircle } from "lucide-react";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { demoAccounts, type DemoAccount } from "@/lib/demo";
import { demoLogin, getCurrentUser, login, signOut } from "@/lib/api";

/** Вход по логину и паролю, ниже «Войти как…» — демо-аккаунты без пароля. Сюда же ведёт «Сменить аккаунт», поэтому экран сначала выходит. */
export default function Page() {
  const [entering, setEntering] = useState<DemoAccount | "form" | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => signOut(), []);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    setEntering("form");
    setError(null);
    try {
      await login(String(form.get("username")), String(form.get("password")));
      const user = await getCurrentUser();
      window.location.assign(user.roles.includes("manager") ? "/analytics/voice" : "/home");
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Не удалось войти");
      setEntering(null);
    }
  }

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
      <h1 className="text-center text-2xl font-extrabold tracking-tight">Вход</h1>

      <form onSubmit={submit} className="flex flex-col gap-3 rounded-xl bg-card p-4">
        <div className="flex flex-col gap-1.5">
          <label htmlFor="login-username" className="text-sm font-semibold">Логин</label>
          <Input id="login-username" name="username" autoComplete="username" required className="h-9" />
        </div>
        <div className="flex flex-col gap-1.5">
          <label htmlFor="login-password" className="text-sm font-semibold">Пароль</label>
          <Input id="login-password" name="password" type="password" autoComplete="current-password" required className="h-9" />
        </div>
        <Button type="submit" size="lg" disabled={entering !== null}>
          {entering === "form" && <LoaderCircle className="animate-spin" aria-hidden />}
          Войти
        </Button>
      </form>

      <p className="pt-2 text-center text-sm text-muted-foreground">или демо-аккаунт без пароля</p>

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
              {entering === account ? (
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
