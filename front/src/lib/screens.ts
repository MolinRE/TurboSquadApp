import {
  Activity,
  BarChart3,
  BookOpen,
  ClipboardCheck,
  FileText,
  Grid3x3,
  History,
  IdCard,
  ListChecks,
  Network,
  Sparkles,
  type LucideIcon,
} from "lucide-react";

// Карта экранов по PRD v7 и docs/mvp-priorities.md.
// Из неё строятся навигация, заголовки и страницы-заглушки,
// поэтому новый экран сначала добавляется сюда.

export type Template = "auth" | "player" | "game" | "workspace";

/** Этап из docs/mvp-priorities.md: 1 — мини-MVP, 2 — глубина, 3 — бонус. */
export type Stage = 1 | 2 | 3;

export type WorkspaceSection = "analytics" | "cms";

export type Screen = {
  id: string;
  path: string;
  title: string;
  template: Template;
  role: "Все" | "Проводник" | "Руководитель" | "Методист";
  stage: Stage;
  summary: string;
  contents: string[];
  /** Только для шаблона «Рабочее место»: раздел бокового меню. */
  section?: WorkspaceSection;
  /** Экран не показывается в меню (открывается из другого экрана). */
  hidden?: boolean;
  icon?: LucideIcon;
};

export const screens: Screen[] = [
  // ---------- Вход ----------
  {
    id: "login",
    path: "/login",
    title: "Вход",
    template: "auth",
    role: "Все",
    stage: 1,
    summary: "Вход в демо по кнопкам «Войти как…».",
    contents: [
      "Проводник-отличник, Проводник-новичок, Руководитель-методист",
      "Ссылка на регистрацию",
    ],
  },
  {
    id: "register",
    path: "/register",
    title: "Регистрация",
    template: "auth",
    role: "Все",
    stage: 2,
    summary: "Простая регистрация без подтверждения почты.",
    contents: ["Имя", "Депо и Бригада из списка", "Пароль"],
  },

  // ---------- Проводник: приложение ----------
  {
    id: "home",
    path: "/home",
    title: "Главная",
    template: "player",
    role: "Проводник",
    stage: 1,
    summary: "С чего начинается день проводника.",
    contents: [
      "Звание и Очки компетенций",
      "Рейс дня",
      "Челлендж недели",
      "Быстрый вход в Свайпы и Блиц",
    ],
  },
  {
    id: "games",
    path: "/games",
    title: "Игры",
    template: "player",
    role: "Проводник",
    stage: 1,
    summary: "Выбор игры и Тем перед тренировкой.",
    contents: [
      "Симулятор рейса, Смена на свайпах, Блиц",
      "Выбор и смешивание Тем",
      "Процент успеваемости и Покрытие по Темам",
    ],
  },
  {
    id: "profile",
    path: "/profile",
    title: "Профиль",
    template: "player",
    role: "Проводник",
    stage: 1,
    summary: "Прогресс проводника и история тренировок.",
    contents: [
      "Звание и Очки компетенций",
      "Радар Компетенций",
      "Коллекция Исходов",
      "Ачивки",
      "История Разборов",
    ],
  },
  {
    id: "leaderboard",
    path: "/leaderboard",
    title: "Рейтинг",
    template: "player",
    role: "Проводник",
    stage: 1,
    summary: "Лидерборд по Очкам компетенций.",
    contents: [
      "Общий лидерборд по Бригаде",
      "Этап 2: уровни компания / депо / бригада, сезонный лидерборд, рейтинг бригад",
    ],
  },
  {
    id: "notifications",
    path: "/notifications",
    title: "Уведомления",
    template: "player",
    role: "Проводник",
    stage: 1,
    summary: "Центр уведомлений (колокольчик).",
    contents: [
      "Новое Событие опубликовано",
      "Этап 2: Челлендж недели, Ачивки и Звания, сгорание Бонуса регулярности, конец Сезона",
    ],
  },
  {
    id: "review",
    path: "/reviews/[id]",
    title: "Разбор",
    template: "player",
    role: "Проводник",
    stage: 1,
    summary: "Разбор после Рейса или игры.",
    contents: [
      "Слой А: каждое решение, изменения Шкал и очков, комментарий Варианта",
      "Этап 2: цитаты Источника, этапы Ролевой модели, слой Б — ИИ-объяснение последствий",
    ],
  },

  // ---------- Проводник: игры ----------
  {
    id: "trip",
    path: "/play/trip",
    title: "Симулятор рейса",
    template: "game",
    role: "Проводник",
    stage: 1,
    summary: "Рейс: Заступ → Проактивный выбор → События → Прибытие или Срыв.",
    contents: [
      "Выбор Класса обслуживания",
      "Шаги События: кнопки и один голосовой Шаг",
      "Шкалы Лояльности пассажира и Рейтинга безопасности",
      "Таймер с веткой таймаута",
    ],
  },
  {
    id: "swipes",
    path: "/play/swipes",
    title: "Смена на свайпах",
    template: "game",
    role: "Проводник",
    stage: 1,
    summary: "Колода Вопросов на тренировку памяти: свайп вправо, влево или вверх — «Не знаю».",
    contents: [
      "Колода из 10 Вопросов (этап 2 — из 20)",
      "Шкалы меняются от ответов",
      "Формулировка печатается, затем варианты и секундомер",
      "Пояснение с пунктом Источника сразу после ответа",
      "Повтор ошибок внутри Смены и Работа над ошибками",
      "Этап 2: Циклы Woodpecker",
    ],
  },
  {
    id: "blitz",
    path: "/play/blitz",
    title: "Блиц",
    template: "game",
    role: "Проводник",
    stage: 1,
    summary: "Вопросы на время, Темы вперемешку.",
    contents: [
      "Один ответ на время",
      "Этап 2: несколько ответов, сборка последовательности",
    ],
  },

  // ---------- Руководитель: аналитика ----------
  {
    id: "blind-spots",
    path: "/analytics/blind-spots",
    title: "Слепые зоны",
    template: "workspace",
    section: "analytics",
    role: "Руководитель",
    stage: 1,
    icon: Grid3x3,
    summary: "Тепловая карта Тема × Компетенция.",
    contents: [
      "Выбор уровня: компания / депо / бригада / проводник",
      "Серые ячейки при низком Покрытии",
      "Клик по ячейке — худшие Вопросы и Шаги",
      "Выводы текстом по правилам",
    ],
  },
  {
    id: "blocks",
    path: "/analytics/blocks",
    title: "Успехи блоков",
    template: "workspace",
    section: "analytics",
    role: "Руководитель",
    stage: 2,
    icon: BarChart3,
    summary: "Сравнение бригад и депо.",
    contents: [
      "Очки компетенций",
      "Процент успеваемости по Темам",
      "Динамика за 4 недели",
    ],
  },
  {
    id: "content-quality",
    path: "/analytics/content-quality",
    title: "Качество контента",
    template: "workspace",
    section: "analytics",
    role: "Руководитель",
    stage: 2,
    icon: ClipboardCheck,
    summary: "Какие Вопросы и События работают плохо.",
    contents: [
      "Фактическая сложность и дискриминативность",
      "Реальное время ответа и доля таймаутов",
      "Где чаще Срыв, какие Варианты никто не выбирает",
    ],
  },
  {
    id: "engagement",
    path: "/analytics/engagement",
    title: "Вовлечённость",
    template: "workspace",
    section: "analytics",
    role: "Руководитель",
    stage: 2,
    icon: Activity,
    summary: "Возвращаются ли проводники к тренировкам.",
    contents: [
      "Доля активных за неделю по бригадам",
      "Возврат на 1-й и 7-й день",
      "Рейс дня, Челленджи, Цели бригады, Коллекция Исходов",
    ],
  },
  {
    id: "employees",
    path: "/analytics/employees",
    title: "Карточки проводников",
    template: "workspace",
    section: "analytics",
    role: "Руководитель",
    stage: 2,
    icon: IdCard,
    summary: "Список проводников подразделения.",
    contents: ["Поиск и фильтр по Бригаде", "Переход в карточку проводника"],
  },
  {
    id: "employee",
    path: "/analytics/employees/[id]",
    title: "Карточка проводника",
    template: "workspace",
    section: "analytics",
    role: "Руководитель",
    stage: 2,
    hidden: true,
    summary: "Карточка проводника для HR.",
    contents: [
      "Радар Компетенций",
      "Знания и Регулярность раздельно, динамика",
      "Пробелы",
      "История Рейсов с Разборами",
    ],
  },

  // ---------- Методист: CMS ----------
  {
    id: "generation",
    path: "/cms/generation",
    title: "ИИ-генерация",
    template: "workspace",
    section: "cms",
    role: "Методист",
    stage: 1,
    icon: Sparkles,
    summary: "Вопросы-Черновики из текста Источника.",
    contents: [
      "Вставка текста (этап 2 — файлы .pdf, .docx, .csv, .xlsx)",
      "Черновики рядом с цитатой",
      "Правка и публикация",
    ],
  },
  {
    id: "questions",
    path: "/cms/questions",
    title: "Банк Вопросов",
    template: "workspace",
    section: "cms",
    role: "Методист",
    stage: 1,
    icon: ListChecks,
    summary: "Все Вопросы для Свайпов и Блица.",
    contents: [
      "Список с фильтрами по Теме, Категории и Классу",
      "Редактирование и публикация",
    ],
  },
  {
    id: "events",
    path: "/cms/events",
    title: "События",
    template: "workspace",
    section: "cms",
    role: "Методист",
    stage: 1,
    icon: Network,
    summary: "События Симулятора рейса и их версии.",
    contents: ["Список Событий с Темой, Классом и версией", "Переход в редактор"],
  },
  {
    id: "event",
    path: "/cms/events/[id]",
    title: "Редактор События",
    template: "workspace",
    section: "cms",
    role: "Методист",
    stage: 1,
    hidden: true,
    summary: "Граф Шагов События.",
    contents: [
      "JSON-редактор с валидатором графа",
      "Публикация новой версии",
      "Этап 2: превью графа",
    ],
  },
  {
    id: "sources",
    path: "/cms/sources",
    title: "Источники",
    template: "workspace",
    section: "cms",
    role: "Методист",
    stage: 2,
    icon: FileText,
    summary: "Документы, из которых генерируется контент.",
    contents: ["Загрузка, замена, удаление", "Предзагрузка датасета хакатона"],
  },
  {
    id: "dictionaries",
    path: "/cms/dictionaries",
    title: "Справочники",
    template: "workspace",
    section: "cms",
    role: "Методист",
    stage: 1,
    icon: BookOpen,
    summary: "Настройки контента и игры.",
    contents: [
      "Этап 1: справочники из сидов, только просмотр",
      "Этап 2: Классы с коэффициентами, Шкалы, Ачивки, Звания, Бонус регулярности, Акцент сезона",
    ],
  },
  {
    id: "publications",
    path: "/cms/publications",
    title: "Журнал публикаций",
    template: "workspace",
    section: "cms",
    role: "Методист",
    stage: 2,
    icon: History,
    summary: "Кто и что опубликовал.",
    contents: ["Список публикаций с автором, временем и версией"],
  },
];

export const workspaceSections: Record<WorkspaceSection, string> = {
  analytics: "Аналитика",
  cms: "Контент",
};

export const stageLabels: Record<Stage, string> = {
  1: "Этап 1 · мини-MVP",
  2: "Этап 2 · глубина",
  3: "Этап 3 · бонус",
};

export function getScreen(id: string): Screen {
  const screen = screens.find((s) => s.id === id);
  if (!screen) throw new Error(`Экран «${id}» не найден в src/lib/screens.ts`);
  return screen;
}

/** Экран по адресу страницы с учётом динамических сегментов вида [id]. */
export function findScreenByPathname(pathname: string): Screen | undefined {
  return screens.find((s) => {
    const pattern = new RegExp(
      "^" + s.path.replace(/\[[^\]]+\]/g, "[^/]+") + "$",
    );
    return pattern.test(pathname);
  });
}
