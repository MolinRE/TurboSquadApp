"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { LoaderCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { login, register } from "@/lib/api";
import { brigades } from "@/lib/brigades";

const selectStyle = "flex h-9 w-full rounded-lg border border-input bg-transparent px-3 py-1 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50";

/** Регистрация Проводника без подтверждения почты: после неё сразу вход тем же логином и паролем. */
export function RegisterForm() {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const username = String(form.get("username")).trim();
    const password = String(form.get("password"));
    const brigade = brigades.find((item) => item.id === form.get("brigadeId"));
    if (!brigade) {
      setError("Выберите Бригаду из списка");
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await register({
        username,
        displayName: String(form.get("displayName")).trim(),
        depotId: brigade.depotId,
        brigadeId: brigade.id,
        password,
      });
      await login(username, password);
      router.push("/home");
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Не удалось зарегистрироваться");
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-center text-2xl font-extrabold tracking-tight">Регистрация</h1>

      <form onSubmit={submit} className="flex flex-col gap-3 rounded-xl bg-card p-4">
        <div className="flex flex-col gap-1.5">
          <label htmlFor="register-username" className="text-sm font-semibold">Логин</label>
          <Input id="register-username" name="username" autoComplete="username" required minLength={3} maxLength={100} className="h-9" />
        </div>
        <div className="flex flex-col gap-1.5">
          <label htmlFor="register-display-name" className="text-sm font-semibold">Имя и фамилия</label>
          <Input id="register-display-name" name="displayName" autoComplete="name" required maxLength={200} className="h-9" />
        </div>
        <div className="flex flex-col gap-1.5">
          <label htmlFor="register-brigade" className="text-sm font-semibold">Бригада</label>
          <select id="register-brigade" name="brigadeId" required defaultValue="" className={selectStyle}>
            <option value="" disabled>Выберите Бригаду</option>
            {brigades.map((item) => (
              <option key={item.id} value={item.id}>{item.depot} · {item.name}</option>
            ))}
          </select>
        </div>
        <div className="flex flex-col gap-1.5">
          <label htmlFor="register-password" className="text-sm font-semibold">Пароль</label>
          <Input id="register-password" name="password" type="password" autoComplete="new-password" required minLength={8} className="h-9" />
          <p className="text-xs text-muted-foreground">Не короче 8 символов</p>
        </div>
        <Button type="submit" size="lg" disabled={busy}>
          {busy && <LoaderCircle className="animate-spin" aria-hidden />}
          Зарегистрироваться
        </Button>
      </form>

      {error && <p role="alert" className="rounded-lg bg-danger-soft p-3 text-sm text-danger">{error}</p>}
      <p className="text-center text-sm text-muted-foreground">
        Уже есть аккаунт? <Link href="/login" className="font-semibold text-foreground underline-offset-4 hover:underline">Войти</Link>
      </p>
    </div>
  );
}
