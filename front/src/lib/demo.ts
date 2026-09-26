// Демо-аккаунты из PRD v7, §17. Пока нет бэкенда, входа как такового нет:
// кнопки ведут на стартовую страницу роли, данные захардкожены.

export type DemoAccount = {
  id: string;
  label: string;
  name: string;
  initials: string;
  description: string;
  href: string;
};

export const demoAccounts: DemoAccount[] = [
  {
    id: "top-conductor",
    label: "Проводник-отличник",
    name: "Марина Соколова",
    initials: "МС",
    description: "Старший проводник · Бригада 3",
    href: "/home",
  },
  {
    id: "new-conductor",
    label: "Проводник-новичок",
    name: "Игорь Лебедев",
    initials: "ИЛ",
    description: "Стажёр · Бригада 1",
    href: "/home",
  },
  {
    id: "manager",
    label: "Руководитель-методист",
    name: "Ольга Верещагина",
    initials: "ОВ",
    description: "Аналитика и CMS · Депо Санкт-Петербург",
    href: "/analytics/blind-spots",
  },
];

export const demoPlayer = {
  name: "Марина Соколова",
  initials: "МС",
  unit: "Бригада 3 · Депо Санкт-Петербург",
  unreadNotifications: 2,
};

export const demoManager = {
  name: "Ольга Верещагина",
  initials: "ОВ",
  roles: "Руководитель · Методист",
};
