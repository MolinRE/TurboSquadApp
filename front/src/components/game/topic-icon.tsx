import {
  BookOpen,
  ClipboardCheck,
  Coffee,
  Flame,
  HeartPulse,
  MessagesSquare,
  Ticket,
  Wrench,
  type LucideIcon,
} from "lucide-react";

/** Иконки Тем из PRD §7.1; для новой Темы из справочника — общая иконка. */
const topicIcons: Record<string, LucideIcon> = {
  Медпомощь: HeartPulse,
  "Пожарная безопасность": Flame,
  Техника: Wrench,
  "Посадка и документы": Ticket,
  "Сервис и питание": Coffee,
  Конфликты: MessagesSquare,
  "Допуск к смене": ClipboardCheck,
};

export function TopicIcon({ topic, className }: { topic: string; className?: string }) {
  const Icon = topicIcons[topic] ?? BookOpen;
  return <Icon className={className} aria-hidden />;
}
