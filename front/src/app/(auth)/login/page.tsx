"use client";

import Link from "next/link";
import { useState, type FormEvent } from "react";
import { ChevronRight, LoaderCircle } from "lucide-react";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { demoAccounts, type DemoAccount } from "@/lib/demo";
import { login } from "@/lib/api";

export default function Page() {
  const [selected, setSelected] = useState<DemoAccount>(demoAccounts[0]);
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await login(selected.username, password);
      window.location.assign(selected.href);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Не удалось войти");
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="flex flex-col gap-4" onSubmit={submit}>
      <div className="flex flex-col gap-1 text-center">
        <h1 className="text-2xl font-extrabold tracking-tight">Войти как…</h1>
        <p className="text-sm text-muted-foreground">Выберите демо-аккаунт и введите его пароль</p>
      </div>

      <ul className="flex flex-col gap-2">
        {demoAccounts.map((account) => (
          <li key={account.id}>
            <button
              type="button"
              onClick={() => { setSelected(account); setError(null); }}
              className={`flex w-full items-center gap-3 rounded-xl bg-card p-3 pr-4 text-left outline-none transition-colors hover:bg-accent focus-visible:ring-3 focus-visible:ring-ring/50 ${selected.id === account.id ? "ring-2 ring-primary" : ""}`}
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
            </button>
          </li>
        ))}
      </ul>

      <div className="flex flex-col gap-2">
        <label className="text-sm font-semibold" htmlFor="login-username">Логин</label>
        <Input id="login-username" value={selected.username} readOnly />
        <label className="text-sm font-semibold" htmlFor="login-password">Пароль</label>
        <Input id="login-password" type="password" value={password} onChange={(event) => setPassword(event.target.value)} required autoComplete="current-password" />
      </div>

      {error && <p role="alert" className="rounded-lg bg-danger-soft p-3 text-sm text-danger">{error}</p>}
      <Button type="submit" disabled={busy || !password} className="h-11">
        {busy && <LoaderCircle className="animate-spin" aria-hidden />}
        Войти
      </Button>
      <p className="text-center text-sm text-muted-foreground">
        Нет аккаунта? <Link href="/register" className="font-semibold text-foreground underline-offset-4 hover:underline">Зарегистрироваться</Link>
      </p>
    </form>
  );
}
