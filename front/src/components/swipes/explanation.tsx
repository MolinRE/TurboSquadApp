import { CircleCheck, CircleQuestionMark, CircleX, Timer } from "lucide-react";
import { cn } from "cn";
import type { Explanation, Verdict } from "@/lib/swipes/contract";

const verdicts = {
  correct: { label: "Верно", icon: CircleCheck, className: "text-brand" },
  wrong: { label: "Неверно", icon: CircleX, className: "text-danger" },
  unknown: { label: "Не знаю", icon: CircleQuestionMark, className: "text-warning-foreground" },
} satisfies Record<Verdict, unknown>;

/** Вердикт ответа; «Время вышло» — это «Не знаю» по истёкшему лимиту, со своей подписью. */
export function VerdictLabel({
  verdict,
  timedOut = false,
  className,
}: {
  verdict: Verdict;
  timedOut?: boolean;
  className?: string;
}) {
  const { label, icon: Icon, className: tone } = timedOut
    ? { ...verdicts.unknown, label: "Время вышло", icon: Timer }
    : verdicts[verdict];
  return (
    <span className={cn("inline-flex items-center gap-1.5 font-extrabold", tone, className)}>
      <Icon className="size-4" aria-hidden />
      {label}
    </span>
  );
}

/** Текст Пояснения с выделенным ключевым фактом; если факта в тексте нет — текст как есть. */
export function ExplanationText({ explanation }: { explanation: Explanation }) {
  const { text, keyFact } = explanation;
  const at = keyFact ? text.indexOf(keyFact) : -1;
  if (at < 0) return <>{text}</>;
  return (
    <>
      {text.slice(0, at)}
      <strong className="font-extrabold text-foreground">{keyFact}</strong>
      {text.slice(at + keyFact.length)}
    </>
  );
}

export function SourceLine({ source }: { source: string }) {
  return <p className="text-xs text-muted-foreground">Источник: {source}</p>;
}
