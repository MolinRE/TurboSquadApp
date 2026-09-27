"use client";

/* eslint-disable @typescript-eslint/no-explicit-any -- форма правит произвольный JSON События на месте */

const inputClass = "h-9 w-full rounded-lg border border-input bg-card px-3 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:opacity-70";
const areaClass = "min-h-16 w-full resize-y rounded-lg border border-input bg-card p-2.5 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:opacity-70";
const scales = [["loyalty", "Лояльность ±"], ["safety", "Безопасность ±"]] as const;

/**
 * Форма поверх JSON События: правит название, ситуации, таймеры, тексты Вариантов, изменения
 * Шкал, комментарии и цели переходов. Всё остальное (Условия, Флаги, новые Шаги) — во вкладке JSON.
 */
export function EventForm({ document, readOnly, onChange }: { document: string; readOnly: boolean; onChange: (json: string) => void }) {
  let event: any;
  try { event = JSON.parse(document); }
  catch { return <p className="rounded-lg bg-warning-soft p-3 text-sm text-warning-foreground">JSON не разбирается — исправьте его во вкладке JSON.</p>; }
  if (!event || !Array.isArray(event.steps)) return <p className="rounded-lg bg-warning-soft p-3 text-sm text-warning-foreground">В документе нет Шагов — откройте вкладку JSON.</p>;

  const stepIds: string[] = event.steps.map((step: any) => step.id);
  function update(change: (draft: any) => void) {
    const draft = structuredClone(event);
    change(draft);
    onChange(JSON.stringify(draft, null, 2));
  }

  return (
    <div className="space-y-4">
      <Field label="Название События">
        <input className={inputClass} disabled={readOnly} value={event.title ?? ""} onChange={(e) => update((d) => { d.title = e.target.value; })} />
      </Field>
      {event.steps.map((step: any, s: number) => (
        <section key={step.id ?? s} className="space-y-3 rounded-xl border border-border p-4">
          <div className="flex flex-wrap items-center gap-2 text-sm">
            <span className="rounded-md bg-muted px-2 py-0.5 font-mono text-xs">{step.id}</span>
            {step.id === event.start && <span className="text-xs font-bold text-brand">старт</span>}
            {step.outcome && <span className="text-xs font-bold text-muted-foreground">Исход · {step.outcome === "success" ? "удачный" : "неудачный"}</span>}
            {step.answerType && <span className="text-xs text-muted-foreground">{step.answerType === "voice" ? "голосом" : "кнопками"}</span>}
          </div>
          <div className="grid gap-3 sm:grid-cols-[1fr_8rem]">
            <Field label="Ситуация">
              <textarea className={areaClass} disabled={readOnly} value={step.situation ?? ""} onChange={(e) => update((d) => { d.steps[s].situation = e.target.value; })} />
            </Field>
            {!step.outcome && (
              <Field label="Таймер, с">
                <input type="number" min={1} className={inputClass} disabled={readOnly} value={step.timerSec ?? ""} placeholder="нет"
                  onChange={(e) => update((d) => { if (e.target.value === "") delete d.steps[s].timerSec; else d.steps[s].timerSec = Number(e.target.value); })} />
              </Field>
            )}
          </div>
          {(step.variants ?? []).map((variant: any, v: number) => (
            <Reaction key={variant.id ?? v} title={`Вариант ${variant.id}`} reaction={variant} stepIds={stepIds} readOnly={readOnly}
              onChange={(change) => update((d) => change(d.steps[s].variants[v]))} />
          ))}
          {step.timeout && (
            <Reaction title="Если время вышло" reaction={step.timeout} stepIds={stepIds} readOnly={readOnly}
              onChange={(change) => update((d) => change(d.steps[s].timeout))} />
          )}
        </section>
      ))}
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <label className="block space-y-1 text-xs font-semibold text-muted-foreground">{label}{children}</label>;
}

/** Вариант или таймаут: текст, изменения обязательных Шкал, комментарий и цели переходов. */
function Reaction({ title, reaction, stepIds, readOnly, onChange }: {
  title: string; reaction: any; stepIds: string[]; readOnly: boolean; onChange: (change: (draft: any) => void) => void;
}) {
  return (
    <div className="space-y-2 rounded-lg bg-muted/60 p-3">
      <p className="text-xs font-extrabold">{title}</p>
      <Field label="Текст">
        <textarea className={areaClass} disabled={readOnly} value={reaction.text ?? ""} onChange={(e) => onChange((d) => { d.text = e.target.value; })} />
      </Field>
      <div className="grid grid-cols-2 gap-2">
        {scales.map(([code, label]) => (
          <Field key={code} label={label}>
            <input type="number" className={inputClass} disabled={readOnly} value={reaction.scaleDeltas?.[code] ?? ""} placeholder="0"
              onChange={(e) => onChange((d) => {
                d.scaleDeltas ??= {};
                if (e.target.value === "" || Number(e.target.value) === 0) delete d.scaleDeltas[code];
                else d.scaleDeltas[code] = Number(e.target.value);
              })} />
          </Field>
        ))}
      </div>
      <Field label="Комментарий в Разборе">
        <textarea className={areaClass} disabled={readOnly} value={reaction.comment ?? ""} onChange={(e) => onChange((d) => { d.comment = e.target.value; })} />
      </Field>
      {(reaction.transitions ?? []).map((transition: any, t: number) => (
        <Field key={t} label={transition.conditions?.length ? `Переход при условии: ${conditionsText(transition.conditions)}` : "Переход →"}>
          <select className={inputClass} disabled={readOnly} value={transition.to} onChange={(e) => onChange((d) => { d.transitions[t].to = e.target.value; })}>
            {stepIds.map((id) => <option key={id} value={id}>{id}</option>)}
          </select>
        </Field>
      ))}
    </div>
  );
}

function conditionsText(conditions: any[]): string {
  return conditions.map((c) =>
    c.type === "flag" ? `${c.present === false ? "нет Флага" : "Флаг"} ${c.flag}`
      : c.type === "scale" ? `Шкала ${c.scale} ${c.op ?? ""} ${c.value ?? ""}`.trim()
        : c.type === "serviceClass" ? `класс ${[].concat(c.classes ?? c.value ?? []).join(", ")}`
          : JSON.stringify(c)).join(" и ");
}
