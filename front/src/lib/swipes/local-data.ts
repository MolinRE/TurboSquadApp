// Локальные данные подменного модуля: справочник Шкал и Вопросы-свайпы
// в том виде, в каком их хранит сервер (с верной стороной и последствиями).
// Шкалы и Классы — как в справочнике Рейса на бэкенде.

import type { ScaleState, ServiceClassRef, SwipeSide } from "./contract";

export type ScaleDefinition = Omit<ScaleState, "value">;

export const localScales: ScaleDefinition[] = [
  { code: "loyalty", name: "Лояльность пассажира", min: 0, max: 100, start: 70, failureThreshold: 0 },
  { code: "safety", name: "Рейтинг безопасности", min: 0, max: 100, start: 70, failureThreshold: 0 },
];

const classes = {
  standard: { code: "standard", name: "Стандарт" },
  comfort: { code: "comfort", name: "Комфорт" },
  business: { code: "business", name: "Бизнес" },
  first: { code: "first", name: "Первый" },
} satisfies Record<string, ServiceClassRef>;

export type SwipeQuestionSide = {
  label: string;
  scaleDeltas: Record<string, number>;
};

export type SwipeQuestion = {
  id: string;
  statement: string;
  right: SwipeQuestionSide;
  left: SwipeQuestionSide;
  correct: SwipeSide;
  explanation: { text: string; keyFact: string };
  source: string;
  topic: string;
  categories: string[];
  /** Пустой список — Вопрос для всех классов. */
  serviceClasses: ServiceClassRef[];
  baseFrequency: number;
};

const stoClasses = "СТО РЖД 03.011-2026, классы обслуживания";
const situation = (n: number) => `Ситуации на борту, №${n}`;

export const localQuestions: SwipeQuestion[] = [
  {
    id: "sw-pet-carrier",
    statement: "Пассажир просит провезти собаку без переноски: она спокойная. Разрешить?",
    right: { label: "Разрешить", scaleDeltas: { safety: -15, loyalty: 5 } },
    left: { label: "Предложить переноску", scaleDeltas: { safety: 5 } },
    correct: "left",
    explanation: {
      text: "Питомцев провозят только в переноске. Предложите переноску, при отказе вызовите начальника поезда.",
      keyFact: "только в переноске",
    },
    source: situation(4),
    topic: "Посадка и документы",
    categories: ["Штатная"],
    serviceClasses: [],
    baseFrequency: 1,
  },
  {
    id: "sw-doors-closed",
    statement: "Двери закрыты, поезд ещё стоит. Опоздавший пассажир стучит в дверь. Открыть?",
    right: { label: "Открыть", scaleDeltas: { safety: -20, loyalty: 5 } },
    left: { label: "Не открывать", scaleDeltas: { safety: 5 } },
    correct: "left",
    explanation: {
      text: "После закрытия двери не открывают и поезд не задерживают. Направьте пассажира в кассу или приложение, чтобы переоформить билет.",
      keyFact: "двери не открывают",
    },
    source: situation(3),
    topic: "Посадка и документы",
    categories: ["Штатная"],
    serviceClasses: [],
    baseFrequency: 1,
  },
  {
    id: "sw-radio-drunk",
    statement: "Вызываете по рации начальника поезда к нетрезвому пассажиру. Назвать только вагон и место?",
    right: { label: "Только вагон и место", scaleDeltas: { safety: 5 } },
    left: { label: "Сказать, что пьян", scaleDeltas: { safety: -15 } },
    correct: "right",
    explanation: {
      text: "По рации не говорят, что пассажир пьян: он может услышать и стать агрессивным.",
      keyFact: "не говорят, что пассажир пьян",
    },
    source: situation(6),
    topic: "Конфликты",
    categories: ["Нештатная"],
    serviceClasses: [],
    baseFrequency: 1,
  },
  {
    id: "sw-own-medicine",
    statement: "Пассажир просит таблетку от головной боли. Дать из своей аптечки?",
    right: { label: "Дать свою", scaleDeltas: { safety: -15, loyalty: 5 } },
    left: { label: "Не давать", scaleDeltas: { safety: 5 } },
    correct: "left",
    explanation: {
      text: "Личные лекарства не выдают — ответственность ляжет на вас. Действуйте через аптечку поезда, начальника поезда и медиков.",
      keyFact: "Личные лекарства не выдают",
    },
    source: situation(28),
    topic: "Медпомощь",
    categories: ["Нештатная"],
    serviceClasses: [],
    baseFrequency: 1,
  },
  {
    id: "sw-doctor-announcement",
    statement: "Пассажиру стало плохо. Искать медиков среди пассажиров по громкой связи?",
    right: { label: "Объявить", scaleDeltas: { safety: 5 } },
    left: { label: "Не сеять панику", scaleDeltas: { safety: -15 } },
    correct: "right",
    explanation: {
      text: "Вызовите начальника поезда и по громкой связи найдите медиков среди пассажиров. Панике не поддавайтесь, но и время не теряйте.",
      keyFact: "по громкой связи",
    },
    source: situation(19),
    topic: "Медпомощь",
    categories: ["Нештатная"],
    serviceClasses: [],
    baseFrequency: 1,
  },
  {
    id: "sw-vape",
    statement: "Пассажир курит электронную сигарету в тамбуре. Попросить прекратить?",
    right: { label: "Попросить прекратить", scaleDeltas: { safety: 5 } },
    left: { label: "Не вмешиваться", scaleDeltas: { safety: -20, loyalty: 5 } },
    correct: "right",
    explanation: {
      text: "На борту запрещено курить, в том числе электронные сигареты. При отказе вызовите начальника поезда или ПТБ.",
      keyFact: "в том числе электронные сигареты",
    },
    source: situation(20),
    topic: "Пожарная безопасность",
    categories: ["Штатная"],
    serviceClasses: [],
    baseFrequency: 1,
  },
  {
    id: "sw-unattended-bag",
    statement: "Под креслом бесхозная сумка. Заглянуть внутрь, чтобы найти владельца?",
    right: { label: "Заглянуть", scaleDeltas: { safety: -20 } },
    left: { label: "Не трогать", scaleDeltas: { safety: 5 } },
    correct: "left",
    explanation: {
      text: "Бесхозную вещь не трогают. Сообщите начальнику поезда и ПТБ по поездной радиосвязи.",
      keyFact: "не трогают",
    },
    source: situation(41),
    topic: "Пожарная безопасность",
    categories: ["Нештатная"],
    serviceClasses: [],
    baseFrequency: 1,
  },
  {
    id: "sw-window",
    statement: "Пассажиру душно, он просит открыть окно. Открыть?",
    right: { label: "Открыть", scaleDeltas: { safety: -10, loyalty: 5 } },
    left: { label: "Не открывать", scaleDeltas: { safety: 5 } },
    correct: "left",
    explanation: {
      text: "Окна в поезде не открываются: воздух идёт через вентиляцию с очисткой. Предложите отрегулировать климат.",
      keyFact: "воздух идёт через вентиляцию",
    },
    source: situation(35),
    topic: "Техника",
    categories: ["Штатная"],
    serviceClasses: [],
    baseFrequency: 1,
  },
  {
    id: "sw-night-light",
    statement: "Ночью пассажир просит выключить в вагоне весь свет. Выключить полностью?",
    right: { label: "Выключить", scaleDeltas: { safety: -10, loyalty: 5 } },
    left: { label: "Оставить дежурный", scaleDeltas: { safety: 5 } },
    correct: "left",
    explanation: {
      text: "Дежурное освещение остаётся всегда — ради безопасности всех на борту. Предложите маску для сна из каталога товаров.",
      keyFact: "Дежурное освещение остаётся всегда",
    },
    source: situation(34),
    topic: "Техника",
    categories: ["Штатная"],
    serviceClasses: [],
    baseFrequency: 1,
  },
  {
    id: "sw-wait-first",
    statement: "Первый класс: пассажир нажал кнопку вызова. Подойти в течение 10 минут — норма?",
    right: { label: "Норма", scaleDeltas: { loyalty: -15 } },
    left: { label: "Слишком долго", scaleDeltas: { loyalty: 5 } },
    correct: "left",
    explanation: {
      text: "В Первом классе проводник подходит не дольше чем за 5 минут. Бизнес — 10, Комфорт — 15, Стандарт — 20.",
      keyFact: "не дольше чем за 5 минут",
    },
    source: stoClasses,
    topic: "Сервис и питание",
    categories: ["Штатная"],
    serviceClasses: [classes.first],
    baseFrequency: 1,
  },
  {
    id: "sw-wait-business",
    statement: "Бизнес: пассажир ждёт проводника уже 12 минут. Норматив ещё соблюдён?",
    right: { label: "Соблюдён", scaleDeltas: { loyalty: -15 } },
    left: { label: "Нарушен", scaleDeltas: { loyalty: 5 } },
    correct: "left",
    explanation: {
      text: "В Бизнесе проводник подходит не дольше чем за 10 минут. Извинитесь и помогите.",
      keyFact: "не дольше чем за 10 минут",
    },
    source: stoClasses,
    topic: "Сервис и питание",
    categories: ["Штатная"],
    serviceClasses: [classes.business],
    baseFrequency: 1,
  },
  {
    id: "sw-wait-standard",
    statement: "Стандарт: проводник подошёл через 15 минут после вызова. Это в пределах нормы?",
    right: { label: "В норме", scaleDeltas: { loyalty: 5 } },
    left: { label: "Нарушение", scaleDeltas: { loyalty: -10 } },
    correct: "right",
    explanation: {
      text: "В Стандарте норматив ожидания персонала — до 20 минут. В Комфорте — 15, в Бизнесе — 10, в Первом — 5.",
      keyFact: "до 20 минут",
    },
    source: stoClasses,
    topic: "Сервис и питание",
    categories: ["Штатная"],
    serviceClasses: [classes.standard],
    baseFrequency: 1,
  },
  {
    id: "sw-comfort-kids",
    statement: "Комфорт: 12-летний ребёнок едет без взрослых. Такое допустимо?",
    right: { label: "Допустимо", scaleDeltas: { loyalty: 5 } },
    left: { label: "Нельзя", scaleDeltas: { loyalty: -10 } },
    correct: "right",
    explanation: {
      text: "В Комфорте могут ехать дети 10–16 лет без сопровождения. Подходите к ребёнку в течение поездки.",
      keyFact: "дети 10–16 лет без сопровождения",
    },
    source: `${stoClasses}; ${situation(47)}`,
    topic: "Посадка и документы",
    categories: ["Штатная"],
    serviceClasses: [classes.comfort],
    baseFrequency: 1,
  },
  {
    id: "sw-first-alcohol",
    statement: "Первый класс: пассажир навеселе просит вина, оно входит в билет. Подать?",
    right: { label: "Подать", scaleDeltas: { loyalty: 5 } },
    left: { label: "Отказать", scaleDeltas: { loyalty: -15 } },
    correct: "right",
    explanation: {
      text: "Если алкоголь включён в стоимость билета, отказать нельзя. Подайте и предупредите начальника поезда.",
      keyFact: "отказать нельзя",
    },
    source: situation(50),
    topic: "Сервис и питание",
    categories: ["Нештатная"],
    serviceClasses: [classes.first],
    baseFrequency: 1,
  },
  {
    id: "sw-station-warning",
    statement: "Пассажир проспал свою станцию. Проводник должен был заранее предупредить о выходе?",
    right: { label: "Должен", scaleDeltas: { loyalty: 5 } },
    left: { label: "Не обязан", scaleDeltas: { loyalty: -10 } },
    correct: "right",
    explanation: {
      text: "Проводник обязан заблаговременно предупредить пассажира о выходе, если тот на своём месте. Сообщите начальнику поезда и объясните дальнейший порядок.",
      keyFact: "обязан заблаговременно предупредить",
    },
    source: situation(38),
    topic: "Посадка и документы",
    categories: ["Штатная"],
    serviceClasses: [],
    baseFrequency: 1,
  },
];
