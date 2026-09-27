import { BookOpen, Zap, type LucideIcon } from "lucide-react";
import type { ShiftMode } from "@/lib/swipes/contract";

type ModeOption = {
  mode: ShiftMode;
  icon: LucideIcon;
  title: string;
  method?: string;
  text: string;
  meta: string;
};

const options: ModeOption[] = [
  {
    mode: "calm",
    icon: BookOpen,
    title: "В своём темпе",
    text: "Один проход без ограничения времени. После каждого ответа — пояснение с пунктом регламента. Чтобы познакомиться с правилами.",
    meta: "1 проход · без таймера",
  },
  {
    mode: "woodpecker",
    icon: Zap,
    title: "На скорость",
    method: "метод Woodpecker",
    text: "Та же колода три раза подряд, и каждый круг быстрее. Не успели — засчитается «Не знаю». Чтобы ответ приходил на автомате.",
    meta: "3 Цикла · 10 → 7 → 5 с на карточку",
  },
];

/** Выбор Режима Смены перед игрой. */
export function ModeChoice({ onChoose }: { onChoose: (mode: ShiftMode) => void }) {
  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-extrabold tracking-tight">Как пройдёте колоду?</h1>
        <p className="text-sm text-muted-foreground">
          10 Вопросов по регламенту. Смахивайте карточку в любом месте экрана или жмите кнопки.
        </p>
      </div>
      {options.map(({ mode, icon: Icon, title, method, text, meta }) => (
        <button
          key={mode}
          type="button"
          onClick={() => onChoose(mode)}
          className="flex gap-3 rounded-xl bg-card p-4 text-left outline-none transition-shadow hover:ring-2 hover:ring-brand-light focus-visible:ring-3 focus-visible:ring-ring/50"
        >
          <span className="grid size-10 shrink-0 place-items-center rounded-full bg-brand-soft text-brand">
            <Icon className="size-5" aria-hidden />
          </span>
          <span className="flex flex-col gap-1">
            <span className="flex flex-wrap items-baseline gap-x-2 text-lg font-extrabold">
              {title}
              {method ? <span className="text-xs font-bold text-muted-foreground">{method}</span> : null}
            </span>
            <span className="text-sm leading-snug">{text}</span>
            <span className="text-xs font-bold text-brand">{meta}</span>
          </span>
        </button>
      ))}
    </div>
  );
}
