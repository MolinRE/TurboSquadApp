import Link from "next/link";
import { ChevronRight, GalleryHorizontalEnd, TrainFront, Zap, type LucideIcon } from "lucide-react";

type Game = {
  href: string;
  icon: LucideIcon;
  title: string;
  tag?: string;
  text: string;
  benefit: string;
  meta: string;
};

const games: Game[] = [
  {
    href: "/play/trip",
    icon: TrainFront,
    title: "Симулятор рейса",
    tag: "главная игра",
    text: "Смена в поезде ВСМ: пассажиры спорят, нарушают правила, просят помощи. Отвечаете голосом, своими словами — пассажир реагирует на смысл и тон.",
    benefit: "Учит говорить с пассажиром по Ролевой модели и принимать безопасные решения под давлением.",
    meta: "~5 мин · голос · Лояльность и Безопасность",
  },
  {
    href: "/play/swipes",
    icon: GalleryHorizontalEnd,
    title: "Смена на свайпах",
    text: "10 карточек по регламенту: вправо — «да», влево — «нет», вверх — «не знаю». После ответа — пояснение с пунктом документа.",
    benefit: "Закрепляет правила в памяти, а режим на скорость доводит ответ до автоматизма.",
    meta: "~3 мин · в своём темпе или 3 круга на скорость",
  },
  {
    href: "/play/blitz",
    icon: Zap,
    title: "Блиц",
    text: "10 вопросов на время: один верный ответ, несколько верных или порядок действий.",
    benefit: "Проверяет, насколько быстро и точно вы вспоминаете регламент, когда счёт идёт на секунды.",
    meta: "~3 мин · 20 с на вопрос",
  },
];

/** Выбор игры: карточка-ссылка на каждую игру — что ждёт, чем полезна и сколько займёт. */
export function GameList() {
  return (
    <div className="flex flex-col gap-3">
      {games.map(({ href, icon: Icon, title, tag, text, benefit, meta }) => (
        <Link
          key={href}
          href={href}
          className="flex gap-3 rounded-xl bg-card p-4 outline-none transition-shadow hover:ring-2 hover:ring-brand-light focus-visible:ring-3 focus-visible:ring-ring/50"
        >
          <span className="grid size-10 shrink-0 place-items-center rounded-full bg-brand-soft text-brand">
            <Icon className="size-5" aria-hidden />
          </span>
          <span className="flex min-w-0 flex-1 flex-col gap-1">
            <span className="flex flex-wrap items-baseline gap-x-2 text-lg font-extrabold">
              {title}
              {tag ? <span className="text-xs font-bold text-muted-foreground">{tag}</span> : null}
            </span>
            <span className="text-sm leading-snug">{text}</span>
            <span className="text-sm leading-snug text-muted-foreground">
              <b className="font-semibold text-foreground">Польза:</b> {benefit}
            </span>
            <span className="text-xs font-bold text-brand">{meta}</span>
          </span>
          <ChevronRight className="size-5 shrink-0 self-center text-muted-foreground" aria-hidden />
        </Link>
      ))}
    </div>
  );
}
