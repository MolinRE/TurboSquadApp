import { CircleCheck, CircleQuestionMark, CircleX } from "lucide-react";
import { cn } from "cn";
import type { Explanation, Verdict } from "@/lib/swipes/contract";

const verdicts = {
  correct: { label: "Верно", icon: CircleCheck, className: "text-brand" },
  wrong: { label: "Неверно", icon: CircleX, className: "text-danger" },
  unknown: { label: "Не знаю", icon: CircleQuestionMark, className: "text-warning-foreground" },
} satisfies Record<Verdict, unknown>;

export function VerdictLabel({ verdict, className }: { verdict: Verdict; className?: string }) {
  const { label, icon: Icon, className: tone } = verdicts[verdict];
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
