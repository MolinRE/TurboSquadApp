import Link from "next/link";
import { CircleCheck, CircleX, Zap } from "lucide-react";
import { cn } from "cn";
import { Button } from "@/components/ui/button";
import { ScalesPanel } from "@/components/game/scale-meter";
import type { CycleInfo, CycleResult, MistakeItem, ShiftState } from "@/lib/swipes/contract";
import { ExplanationText, SourceLine } from "./explanation";
import { formatSeconds } from "./format";

/**
 * Итог законченной Смены. «В своём темпе» — итог, Шкалы и «Что повторить» с Работой над
 * ошибками, в том числе после Срыва; «На скорость» — между Циклами итог Цикла и что будет
 * дальше, после последнего — сравнение Циклов.
 */
export function ShiftSummary({
  shift,
  onRestart,
  onNextCycle,
  onWorkOnMistakes,
}: {
  shift: ShiftState;
  onRestart: () => void;
  onNextCycle: () => void;
  onWorkOnMistakes: () => void;
}) {
  const { cycle } = shift;
  if (cycle && cycle.number < cycle.timeLimitsMs.length) {
    return <CycleBreak shift={shift} cycle={cycle} onNextCycle={onNextCycle} />;
  }
  if (cycle) return <CyclesSummary shift={shift} cycle={cycle} onRestart={onRestart} />;

  const { mistakes } = shift.result!;
  return (
    <div className="flex flex-1 flex-col gap-3">
      <ResultHeadline shift={shift} title={shift.status === "passed" ? "Смена пройдена" : "Срыв смены"} />
      <ScalesPanel scales={shift.scales} />
      {mistakes.length ? (
        <>
          <WhatToRepeat mistakes={mistakes} />
          <SummaryActions
            primary={{ label: "Работа над ошибками", onClick: onWorkOnMistakes }}
            secondary={{ label: "Новая Смена", onClick: onRestart }}
          />
        </>
      ) : (
        <SummaryActions primary={{ label: "Новая Смена", onClick: onRestart }} />
      )}
    </div>
  );
}

/** «Что повторить»: Вопросы с ошибкой или «Не знаю» — формулировка, Пояснение и пункт Источника. */
function WhatToRepeat({ mistakes }: { mistakes: MistakeItem[] }) {
  return (
    <section className="flex flex-col gap-3 rounded-xl bg-card p-5">
      <h2 className="flex items-baseline justify-between gap-2 text-lg font-extrabold">
        Что повторить
        <span className="text-sm font-bold text-muted-foreground tabular-nums">{mistakes.length}</span>
      </h2>
      <ol className="flex flex-col divide-y divide-border">
        {mistakes.map((mistake) => (
          <li key={mistake.questionId} className="flex flex-col gap-1.5 py-3 first:pt-0 last:pb-0">
            <p className="text-sm text-muted-foreground">{mistake.statement}</p>
            <p className="text-sm leading-relaxed">
              <ExplanationText explanation={mistake.explanation} />
            </p>
            <SourceLine source={mistake.explanation.source} />
          </li>
        ))}
      </ol>
    </section>
  );
}

function ResultHeadline({ shift, title }: { shift: ShiftState; title: string }) {
  const result = shift.result!;
  const failedScale = shift.scales.find((scale) => scale.code === result.failedScale);
  return (
    <section className="flex flex-col items-center gap-2 rounded-xl bg-card p-6 text-center">
      {failedScale ? (
        <CircleX className="size-10 text-danger" aria-hidden />
      ) : (
        <CircleCheck className="size-10 text-brand" aria-hidden />
      )}
      <h1 className="text-2xl font-extrabold tracking-tight">{title}</h1>
      {failedScale ? (
        <p className="text-sm text-muted-foreground">
          Шкала «{failedScale.name}» упала до {failedScale.value}
        </p>
      ) : null}
      <p className="text-sm">
        Верно с первого раза:{" "}
        <b className="tabular-nums">
          {result.firstTryCorrect} из {result.total}
        </b>
      </p>
      {result.averageAnswerMs !== null ? (
        <p className="text-sm">
          Среднее время ответа: <b className="tabular-nums">{formatSeconds(result.averageAnswerMs)} с</b>
        </p>
      ) : null}
    </section>
  );
}

function CycleBreak({
  shift,
  cycle,
  onNextCycle,
}: {
  shift: ShiftState;
  cycle: CycleInfo;
  onNextCycle: () => void;
}) {
  const total = cycle.timeLimitsMs.length;
  const next = cycle.number + 1;
  const title =
    shift.status === "passed"
      ? `Цикл ${cycle.number} из ${total} пройден`
      : `Срыв смены в Цикле ${cycle.number}`;

  return (
    <div className="flex flex-1 flex-col gap-3">
      <ResultHeadline shift={shift} title={title} />
      <section className="flex gap-3 rounded-xl bg-brand-soft p-4">
        <Zap className="mt-0.5 size-5 shrink-0 text-brand" aria-hidden />
        <p className="text-sm leading-snug">
          <b>
            Дальше — Цикл {next} из {total}.
          </b>{" "}
          Та же колода в новом порядке, {cycle.timeLimitsMs[next - 1] / 1000} секунд на карточку, текст
          печатается быстрее. Шкалы — с начала.
        </p>
      </section>
      <SummaryActions primary={{ label: `Начать Цикл ${next}`, onClick: onNextCycle }} />
    </div>
  );
}

function CyclesSummary({
  shift,
  cycle,
  onRestart,
}: {
  shift: ShiftState;
  cycle: CycleInfo;
  onRestart: () => void;
}) {
  const result = shift.result!;
  const rows: CycleResult[] = [
    ...cycle.previous,
    {
      number: cycle.number,
      timeLimitMs: cycle.timeLimitsMs[cycle.number - 1],
      failedScale: result.failedScale,
      firstTryCorrect: result.firstTryCorrect,
      total: result.total,
      averageAnswerMs: result.averageAnswerMs,
    },
  ];

  return (
    <div className="flex flex-1 flex-col gap-3">
      <section className="flex flex-col gap-4 rounded-xl bg-card p-6">
        <div className="flex flex-col items-center gap-2 text-center">
          <Zap className="size-10 text-brand" aria-hidden />
          <h1 className="text-2xl font-extrabold tracking-tight">Тренировка на скорость пройдена</h1>
          <p className="text-sm text-muted-foreground">Так от Цикла к Циклу менялись точность и время.</p>
        </div>
        <table className="w-full text-sm">
          <thead>
            <tr className="text-left text-xs text-muted-foreground">
              <th className="pb-2 font-bold">Цикл</th>
              <th className="pb-2 font-bold">Верно</th>
              <th className="pb-2 text-right font-bold">Среднее время</th>
            </tr>
          </thead>
          <tbody className="tabular-nums">
            {rows.map((row) => (
              <tr key={row.number} className="border-t border-border">
                <td className="py-2 font-bold">
                  {row.number} <span className="font-normal text-muted-foreground">· {row.timeLimitMs / 1000} с</span>
                </td>
                <td className="py-2">
                  {row.firstTryCorrect} из {row.total}
                  {row.failedScale ? <span className="ml-1.5 font-bold text-danger">Срыв</span> : null}
                </td>
                <td className="py-2 text-right">
                  {row.averageAnswerMs === null ? "—" : `${formatSeconds(row.averageAnswerMs)} с`}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>
      <SummaryActions primary={{ label: "Новая Смена", onClick: onRestart }} />
    </div>
  );
}

type SummaryAction = { label: string; onClick: () => void };

/** Главное действие во всю ширину; под ним «К играм», а если есть второе действие — оба в ряд. */
function SummaryActions({ primary, secondary }: { primary: SummaryAction; secondary?: SummaryAction }) {
  const outlineClass = "h-12 bg-card text-base font-bold";
  return (
    <div className="mt-auto grid gap-2">
      <Button onClick={primary.onClick} className="h-12 text-base font-bold">
        {primary.label}
      </Button>
      <div className={cn("grid gap-2", secondary && "grid-cols-2")}>
        {secondary ? (
          <Button variant="outline" onClick={secondary.onClick} className={outlineClass}>
            {secondary.label}
          </Button>
        ) : null}
        <Button asChild variant="outline" className={outlineClass}>
          <Link href="/games">К играм</Link>
        </Button>
      </div>
    </div>
  );
}
