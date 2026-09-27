"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { getSwipeDebrief } from "@/lib/swipes/api";
import type { SwipeDebrief, SwipeDebriefAnswer } from "@/lib/swipes/contract";
import { ExplanationText, SourceLine, VerdictLabel } from "./explanation";

const signed = (value: number) => `${value > 0 ? "+" : ""}${value}`;

function answerText(answer: SwipeDebriefAnswer) {
  if (answer.answer === "timeout") return "Время вышло";
  if (answer.answer === "unknown") return "Не знаю";
  return answer.answer === "right" ? answer.rightLabel : answer.leftLabel;
}

export function SwipeReview({ shiftId, embedded = false }: { shiftId: string; embedded?: boolean }) {
  const [debrief, setDebrief] = useState<SwipeDebrief | null>(null);
  const [error, setError] = useState(false);

  useEffect(() => {
    let active = true;
    getSwipeDebrief(shiftId).then(
      (value) => { if (active) setDebrief(value); },
      () => { if (active) setError(true); },
    );
    return () => { active = false; };
  }, [shiftId]);

  const Container = embedded ? "section" : "main";
  return (
    <Container className={embedded ? "space-y-4" : "space-y-4 px-4 pb-8"}>
      <div>
        {!embedded && <p className="text-sm text-muted-foreground">Смена на свайпах</p>}
        {embedded ? <h2 className="text-xl font-extrabold">Разбор Смены</h2> : <h1 className="text-2xl font-extrabold">Разбор Смены</h1>}
      </div>
      {error && <p role="alert" className="text-sm text-danger">Разбор не найден или недоступен.</p>}
      {!error && !debrief && <Skeleton className="h-32 w-full" />}
      {debrief && <>
        <p className={debrief.result === "failed" ? "text-danger" : "text-brand"}>
          {debrief.result === "failed" ? "Срыв смены" : "Смена пройдена"}
          {debrief.cycle && ` · Цикл ${debrief.cycle}`}
        </p>
        {debrief.answers.map((answer) => (
          <Card key={answer.seq} size="sm">
            <CardHeader>
              <div className="flex items-center justify-between gap-2">
                <CardTitle>Вопрос {answer.seq + 1}{answer.isRepeat ? " · Повтор" : ""}</CardTitle>
                <VerdictLabel verdict={answer.verdict} timedOut={answer.answer === "timeout"} />
              </div>
            </CardHeader>
            <CardContent className="space-y-2 text-sm">
              <p className="font-semibold">{answer.statement}</p>
              <p>Ответ: {answerText(answer)}</p>
              <p>Верно: {answer.correctSide === "right" ? answer.rightLabel : answer.leftLabel}</p>
              {Object.entries(answer.scaleChanges).map(([code, value]) => (
                <p key={code}>{debrief.scaleNames[code] ?? code}: {signed(value)}</p>
              ))}
              <p>Очки знаний: {signed(answer.knowledgeDelta)}</p>
              <p className="text-xs text-muted-foreground">Время ответа: {(answer.elapsedMs / 1000).toFixed(1)} с</p>
              <p className="rounded-lg bg-brand-soft p-3"><ExplanationText explanation={answer.explanation} /></p>
              <SourceLine source={answer.explanation.source} />
            </CardContent>
          </Card>
        ))}
      </>}
      {!embedded && <Button asChild variant="outline"><Link href="/profile">К истории Разборов</Link></Button>}
    </Container>
  );
}
