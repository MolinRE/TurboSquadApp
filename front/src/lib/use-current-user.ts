"use client";

import { useEffect, useState } from "react";
import { getCurrentUser, type CurrentUser } from "./api";

const roleNames: Record<string, string> = {
  conductor: "Проводник",
  manager: "Руководитель",
  methodologist: "Методист",
};

/** Кто вошёл: из API, а не из захардкоженного списка. null — ещё грузится; без входа API вернёт на «Войти как…». */
export function useCurrentUser(): CurrentUser | null {
  const [user, setUser] = useState<CurrentUser | null>(null);

  useEffect(() => {
    let active = true;
    getCurrentUser().then(
      (value) => active && setUser(value),
      () => undefined,
    );
    return () => {
      active = false;
    };
  }, []);

  return user;
}

/** «Игорь Лебедев» → «ИЛ». */
export function initialsOf(name: string) {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((word) => word[0].toUpperCase())
    .join("");
}

/** Роли по-русски через точку: «Руководитель · Методист». */
export function roleLabelOf(roles: string[]) {
  return roles.map((role) => roleNames[role] ?? role).join(" · ");
}

/** «Бригада 2 · Северное депо». */
export function unitOf(user: CurrentUser) {
  return [user.brigade, user.depot].filter(Boolean).join(" · ");
}
