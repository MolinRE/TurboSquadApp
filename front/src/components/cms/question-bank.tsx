"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { LoaderCircle, Plus, Trash2 } from "lucide-react";
import { ApiError } from "@/lib/api";
import {
  createQuestion, deleteQuestion, emptyOptions, getQuestionCatalog, listQuestions,
  publishQuestion, saveQuestion, unpublishQuestion,
  type ChoiceOption, type Question, type QuestionCatalog, type QuestionFilters,
  type QuestionInput, type QuestionOptions, type QuestionType, type SequenceStep,
} from "@/lib/questions";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";

type FieldIssue = { path: string; message: string };
const types: Array<{ value: QuestionType; label: string }> = [
  { value: "swipe", label: "Свайп" },
  { value: "single", label: "Один ответ" },
  { value: "multiple", label: "Несколько ответов" },
  { value: "sequence", label: "Последовательность" },
];
const inputStyle = "flex h-9 w-full rounded-lg border border-input bg-transparent px-3 py-1 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50";
const textAreaStyle = `${inputStyle} min-h-24 resize-y py-2`;

function editorOf(question: Question): QuestionInput {
  const options = question.options as Partial<{ options: ChoiceOption[]; steps: SequenceStep[] }>;
  const normalized = question.type === "swipe"
    ? {
        right: { ...(question.options as { right?: { quote?: string; source?: string } }).right, label: (question.options as { right?: { label?: string } }).right?.label ?? "", scaleDeltas: (question.options as { right?: { scaleDeltas?: Record<string, number> } }).right?.scaleDeltas ?? {} },
        left: { ...(question.options as { left?: { quote?: string; source?: string } }).left, label: (question.options as { left?: { label?: string } }).left?.label ?? "", scaleDeltas: (question.options as { left?: { scaleDeltas?: Record<string, number> } }).left?.scaleDeltas ?? {} },
        correct: (question.options as { correct?: "right" | "left" }).correct ?? "right",
      } as QuestionOptions
    : question.type === "sequence"
      ? { steps: Array.isArray(options.steps) ? options.steps : [] }
      : { options: Array.isArray(options.options) ? options.options : [] };
  return { ...question, options: normalized };
}

function errorMessage(reason: unknown): string {
  if (reason instanceof ApiError) {
    if (reason.status === 401) return "Срок входа истёк";
    if (reason.status === 403) return "Для банка Вопросов нужна роль Методиста";
    return reason.message;
  }
  return reason instanceof Error ? reason.message : "Не удалось связаться с сервером";
}

export function QuestionBank() {
  const router = useRouter();
  const [questions, setQuestions] = useState<Question[]>([]);
  const [catalog, setCatalog] = useState<QuestionCatalog | null>(null);
  const [filters, setFilters] = useState<QuestionFilters>({});
  const [selected, setSelected] = useState<Question | null>(null);
  const [form, setForm] = useState<QuestionInput | null>(null);
  const [categoriesText, setCategoriesText] = useState("");
  const [issues, setIssues] = useState<FieldIssue[]>([]);
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [accessDenied, setAccessDenied] = useState(false);

  useEffect(() => {
    Promise.all([listQuestions(), getQuestionCatalog()])
      .then(([items, reference]) => {
        setQuestions(items);
        setCatalog(reference);
        const requested = new URLSearchParams(window.location.search).get("question");
        const question = items.find((item) => item.id === requested);
        if (question) {
          setSelected(question);
          setForm(editorOf(question));
          setCategoriesText(question.categories.join(", "));
        }
      })
      .catch((reason) => {
        if (reason instanceof ApiError && reason.status === 401) {
          window.localStorage.removeItem("turbo.accessToken");
          router.push("/login");
        }
        if (reason instanceof ApiError && reason.status === 403) setAccessDenied(true);
        setMessage(errorMessage(reason));
      })
      .finally(() => setLoading(false));
  }, [router]);

  async function reload(nextFilters = filters) {
    const items = await listQuestions(nextFilters);
    setQuestions(items);
    setCatalog(await getQuestionCatalog());
  }

  function choose(question: Question) {
    setSelected(question);
    setForm(editorOf(question));
    setCategoriesText(question.categories.join(", "));
    setIssues([]);
    setMessage(null);
  }

  async function run(action: () => Promise<void>) {
    setBusy(true);
    setMessage(null);
    setIssues([]);
    try { await action(); }
    catch (reason) {
      if (reason instanceof ApiError && reason.payload?.errors) setIssues(reason.payload.errors);
      else {
        if (reason instanceof ApiError && reason.status === 401) {
          window.localStorage.removeItem("turbo.accessToken");
          router.push("/login");
        }
        if (reason instanceof ApiError && reason.status === 403) setAccessDenied(true);
        setMessage(errorMessage(reason));
      }
    }
    finally { setBusy(false); }
  }

  function issueAt(path: string) {
    return issues.filter((issue) => issue.path === path).map((issue) => issue.message).join("; ");
  }

  function field(path: string, label: string, value: string, onChange: (value: string) => void, multiline = false) {
    const id = `question-${path}`;
    return <div className="space-y-1.5" key={path}>
      <label htmlFor={id} className="text-sm font-semibold">{label}</label>
      {multiline
        ? <textarea id={id} className={textAreaStyle} value={value} onChange={(event) => onChange(event.target.value)} />
        : <Input id={id} value={value} onChange={(event) => onChange(event.target.value)} />}
      {issueAt(path) && <p className="text-xs text-danger">{issueAt(path)}</p>}
    </div>;
  }

  function change<K extends keyof QuestionInput>(key: K, value: QuestionInput[K]) {
    setForm((current) => current ? { ...current, [key]: value } : current);
  }

  function changeOptions(options: QuestionOptions) { change("options", options); }

  async function create() {
    await run(async () => {
      const question = await createQuestion("single");
      await reload();
      choose(question);
      setMessage("Черновик создан. Заполните его и сохраните.");
    });
  }

  async function save(publish = false) {
    if (!selected || !form) return;
    await run(async () => {
      const saved = await saveQuestion(selected.id, form);
      const result = publish ? await publishQuestion(saved.id) : saved;
      choose(result);
      await reload();
      setMessage(publish ? "Вопрос опубликован" : "Вопрос сохранён");
    });
  }

  async function unpublish() {
    if (!selected) return;
    await run(async () => {
      const result = await unpublishQuestion(selected.id);
      choose(result);
      await reload();
      setMessage("Вопрос снят с публикации");
    });
  }

  async function remove() {
    if (!selected || !window.confirm("Удалить Черновик?")) return;
    await run(async () => {
      await deleteQuestion(selected.id);
      setSelected(null);
      setForm(null);
      await reload();
      setMessage("Черновик удалён");
    });
  }

  const choiceOptions = form && form.type !== "swipe" && form.type !== "sequence"
    ? (form.options as { options: ChoiceOption[] }).options : [];
  const steps = form?.type === "sequence" ? (form.options as { steps: SequenceStep[] }).steps : [];
  const swipe = form?.type === "swipe"
    ? form.options as { right: { label: string; scaleDeltas: Record<string, number> }; left: { label: string; scaleDeltas: Record<string, number> }; correct: "right" | "left" }
    : null;

  if (loading) return <p className="text-sm text-muted-foreground">Загрузка Вопросов…</p>;
  if (accessDenied) return <p role="alert" className="rounded-lg bg-danger-soft p-4 text-danger">Для банка Вопросов нужна роль Методиста.</p>;

  return <div className="space-y-5">
    <div className="flex flex-wrap items-center justify-between gap-3">
      <div><h1 className="text-2xl font-bold">Банк Вопросов</h1><p className="text-sm text-muted-foreground">Общий контент для Смены на свайпах и Блица</p></div>
      <Button onClick={() => void create()} disabled={busy}><Plus aria-hidden />Новый Черновик</Button>
    </div>
    {message && <p role="status" className="rounded-lg bg-brand-soft px-4 py-3 text-sm">{message}</p>}
    <div className="grid items-start gap-5 xl:grid-cols-[minmax(280px,1fr)_minmax(420px,1.35fr)]">
      <Card>
        <CardHeader><CardTitle>Вопросы</CardTitle></CardHeader>
        <CardContent className="space-y-4">
          <div className="grid grid-cols-2 gap-2">
            <Input aria-label="Тема" placeholder="Тема" list="question-topics" value={filters.topic ?? ""} onChange={(event) => setFilters({ ...filters, topic: event.target.value })} />
            <datalist id="question-topics">{catalog?.topics.map((topic) => <option key={topic} value={topic} />)}</datalist>
            <Input aria-label="Категория" placeholder="Категория" list="question-filter-categories" value={filters.category ?? ""} onChange={(event) => setFilters({ ...filters, category: event.target.value })} />
            <datalist id="question-filter-categories">{catalog?.categories.map((category) => <option key={category} value={category} />)}</datalist>
            <select aria-label="Класс обслуживания" className={inputStyle} value={filters.serviceClass ?? ""} onChange={(event) => setFilters({ ...filters, serviceClass: event.target.value })}>
              <option value="">Все классы</option>{catalog?.serviceClasses.map((item) => <option key={item.code} value={item.code}>{item.name}</option>)}
            </select>
            <select aria-label="Тип Вопроса" className={inputStyle} value={filters.type ?? ""} onChange={(event) => setFilters({ ...filters, type: event.target.value })}>
              <option value="">Все типы</option>{types.map((type) => <option key={type.value} value={type.value}>{type.label}</option>)}
            </select>
            <select aria-label="Статус Вопроса" className={inputStyle} value={filters.status ?? ""} onChange={(event) => setFilters({ ...filters, status: event.target.value })}>
              <option value="">Все статусы</option><option value="draft">Черновики</option><option value="published">Опубликованные</option>
            </select>
            <Button variant="outline" onClick={() => void run(() => reload())} disabled={busy}>Применить фильтры</Button>
          </div>
          {questions.length === 0 ? <p className="text-sm text-muted-foreground">Вопросы не найдены</p> :
            <ul className="max-h-[70vh] space-y-2 overflow-y-auto">
              {questions.map((question) => <li key={question.id}>
                <button type="button" onClick={() => choose(question)} className={`w-full rounded-lg border p-3 text-left hover:bg-muted ${selected?.id === question.id ? "border-brand bg-brand-soft" : "border-border"}`}>
                  <span className="flex items-center justify-between gap-2"><span className="font-semibold">{question.statement || "Без формулировки"}</span><Badge variant={question.status === "published" ? "default" : "secondary"}>{question.status === "published" ? "Опубликован" : "Черновик"}</Badge></span>
                  <span className="mt-1 block text-xs text-muted-foreground">{types.find((type) => type.value === question.type)?.label} · {question.topic || "Без Темы"} · {question.id}</span>
                </button>
              </li>)}
            </ul>}
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle>{selected ? `Вопрос ${selected.id}` : "Откройте Вопрос"}</CardTitle></CardHeader>
        <CardContent>
          {!form || !selected ? <p className="text-sm text-muted-foreground">Выберите Вопрос слева или создайте Черновик.</p> : <div className="space-y-5">
            <div className="grid gap-3 sm:grid-cols-2">
              <div className="space-y-1.5"><label htmlFor="question-type" className="text-sm font-semibold">Тип</label><select id="question-type" className={inputStyle} value={form.type} onChange={(event) => { const type = event.target.value as QuestionType; setForm({ ...form, type, options: emptyOptions(type) }); }}>
                {types.map((type) => <option key={type.value} value={type.value}>{type.label}</option>)}
              </select>{issueAt("type") && <p className="text-xs text-danger">{issueAt("type")}</p>}</div>
              <div className="space-y-1.5"><label htmlFor="question-timeLimitSec" className="text-sm font-semibold">Время на ответ, с</label><Input id="question-timeLimitSec" type="number" min="1" value={form.timeLimitSec ?? ""} onChange={(event) => change("timeLimitSec", event.target.value === "" ? null : Number(event.target.value))} />{issueAt("timeLimitSec") && <p className="text-xs text-danger">{issueAt("timeLimitSec")}</p>}</div>
            </div>
            {field("statement", "Формулировка", form.statement, (value) => change("statement", value), true)}
            <div className="grid gap-3 sm:grid-cols-2">
              {field("topic", "Тема", form.topic, (value) => change("topic", value))}
              <div className="space-y-1.5"><label htmlFor="question-baseFrequency" className="text-sm font-semibold">Базовая частота показа</label><Input id="question-baseFrequency" type="number" min="0" step="0.1" value={form.baseFrequency} onChange={(event) => change("baseFrequency", Number(event.target.value))} /></div>
              <div className="space-y-1.5"><label htmlFor="question-knowledgeCost" className="text-sm font-semibold">Стоимость по Знанию</label><Input id="question-knowledgeCost" type="number" min="1" step="1" value={form.knowledgeCost} onChange={(event) => change("knowledgeCost", Number(event.target.value))} />{issueAt("knowledgeCost") && <p className="text-xs text-danger">{issueAt("knowledgeCost")}</p>}</div>
            </div>
            {field("categories", "Категории через запятую", categoriesText, (value) => { setCategoriesText(value); change("categories", value.split(",").map((item) => item.trim()).filter(Boolean)); })}
            <div className="space-y-2"><p className="text-sm font-semibold">Классы обслуживания</p><p className="text-xs text-muted-foreground">Ничего не отмечено — все классы</p><div className="flex flex-wrap gap-3">{catalog?.serviceClasses.map((item) => <label key={item.code} className="flex items-center gap-1.5 text-sm"><input type="checkbox" checked={form.serviceClasses.includes(item.code)} onChange={(event) => change("serviceClasses", event.target.checked ? [...form.serviceClasses, item.code] : form.serviceClasses.filter((code) => code !== item.code))} />{item.name}</label>)}</div>{issueAt("serviceClasses") && <p className="text-xs text-danger">{issueAt("serviceClasses")}</p>}</div>
            <div className="border-t pt-4"><h2 className="mb-3 font-semibold">Варианты и верный ответ</h2>
              {form.type === "single" || form.type === "multiple" ? <div className="space-y-3">
                {choiceOptions.map((option, index) => <div key={`${option.id}-${index}`} className="grid grid-cols-[5rem_1fr_auto_auto] items-center gap-2">
                  <Input aria-label={`ID варианта ${index + 1}`} value={option.id} onChange={(event) => changeOptions({ options: choiceOptions.map((item, at) => at === index ? { ...item, id: event.target.value } : item) })} />
                  <Input aria-label={`Текст варианта ${index + 1}`} value={option.text} onChange={(event) => changeOptions({ options: choiceOptions.map((item, at) => at === index ? { ...item, text: event.target.value } : item) })} />
                  <label className="flex items-center gap-1 text-xs"><input type={form.type === "single" ? "radio" : "checkbox"} name="correct-choice" checked={option.correct} onChange={(event) => changeOptions({ options: choiceOptions.map((item, at) => ({ ...item, correct: form.type === "single" ? at === index : at === index ? event.target.checked : item.correct })) })} />Верный</label>
                  <Button variant="ghost" size="icon" aria-label={`Удалить вариант ${index + 1}`} onClick={() => changeOptions({ options: choiceOptions.filter((_, at) => at !== index) })}><Trash2 aria-hidden /></Button>
                  {(issueAt(`options.options[${index}].id`) || issueAt(`options.options[${index}].text`) || issueAt(`options.options[${index}].correct`)) && <p className="col-span-4 text-xs text-danger">{issueAt(`options.options[${index}].id`)} {issueAt(`options.options[${index}].text`)} {issueAt(`options.options[${index}].correct`)}</p>}
                </div>)}
                <Button variant="outline" onClick={() => changeOptions({ options: [...choiceOptions, { id: `v${choiceOptions.length + 1}`, text: "", correct: false }] })}><Plus aria-hidden />Добавить вариант</Button>
                {issueAt("options.options") && <p className="text-xs text-danger">{issueAt("options.options")}</p>}
              </div> : null}
              {form.type === "sequence" ? <div className="space-y-3">
                <p className="text-xs text-muted-foreground">Шаги сверху вниз задают верный порядок.</p>
                {steps.map((step, index) => <div key={`${step.id}-${index}`} className="grid grid-cols-[5rem_1fr_auto_auto_auto] items-center gap-2">
                  <Input aria-label={`ID шага ${index + 1}`} value={step.id} onChange={(event) => changeOptions({ steps: steps.map((item, at) => at === index ? { ...item, id: event.target.value } : item) })} />
                  <Input aria-label={`Текст шага ${index + 1}`} value={step.text} onChange={(event) => changeOptions({ steps: steps.map((item, at) => at === index ? { ...item, text: event.target.value } : item) })} />
                  <Button variant="outline" size="icon" aria-label={`Поднять шаг ${index + 1}`} disabled={index === 0} onClick={() => { const next = [...steps]; [next[index - 1], next[index]] = [next[index], next[index - 1]]; changeOptions({ steps: next }); }}>↑</Button>
                  <Button variant="outline" size="icon" aria-label={`Опустить шаг ${index + 1}`} disabled={index === steps.length - 1} onClick={() => { const next = [...steps]; [next[index], next[index + 1]] = [next[index + 1], next[index]]; changeOptions({ steps: next }); }}>↓</Button>
                  <Button variant="ghost" size="icon" aria-label={`Удалить шаг ${index + 1}`} onClick={() => changeOptions({ steps: steps.filter((_, at) => at !== index) })}><Trash2 aria-hidden /></Button>
                  {(issueAt(`options.steps[${index}].id`) || issueAt(`options.steps[${index}].text`)) && <p className="col-span-5 text-xs text-danger">{issueAt(`options.steps[${index}].id`)} {issueAt(`options.steps[${index}].text`)}</p>}
                </div>)}
                <Button variant="outline" onClick={() => changeOptions({ steps: [...steps, { id: `s${steps.length + 1}`, text: "" }] })}><Plus aria-hidden />Добавить шаг</Button>
                {issueAt("options.steps") && <p className="text-xs text-danger">{issueAt("options.steps")}</p>}
              </div> : null}
              {swipe ? <div className="space-y-4">
                {(["right", "left"] as const).map((side) => <div key={side} className="rounded-lg border border-border p-3">
                  <div className="mb-2 flex items-center gap-3"><h3 className="font-semibold">{side === "right" ? "Вправо" : "Влево"}</h3><label className="text-xs"><input type="radio" name="correct-swipe" checked={swipe.correct === side} onChange={() => changeOptions({ ...swipe, correct: side })} /> Верная сторона</label></div>
                  <Input aria-label={side === "right" ? "Подпись справа" : "Подпись слева"} value={swipe[side].label} onChange={(event) => changeOptions({ ...swipe, [side]: { ...swipe[side], label: event.target.value } })} />
                  {issueAt(`options.${side}.label`) && <p className="text-xs text-danger">{issueAt(`options.${side}.label`)}</p>}
                  <div className="mt-3 grid gap-2 sm:grid-cols-2">{catalog?.scales.map((scale) => <label key={scale.code} className="text-xs">{scale.name}<Input type="number" value={swipe[side].scaleDeltas[scale.code] ?? 0} onChange={(event) => changeOptions({ ...swipe, [side]: { ...swipe[side], scaleDeltas: { ...swipe[side].scaleDeltas, [scale.code]: Number(event.target.value) } } })} /></label>)}</div>
                </div>)}
                {issues.filter((issue) => issue.path.startsWith("options.") && issue.path !== "options.right.label" && issue.path !== "options.left.label").map((issue) => <p key={`${issue.path}-${issue.message}`} className="text-xs text-danger">{issue.path}: {issue.message}</p>)}
              </div> : null}
            </div>
            <div className="space-y-3 border-t pt-4"><h2 className="font-semibold">Пояснение и Источник</h2>
              {field("explanationText", "Пояснение", form.explanationText, (value) => change("explanationText", value), true)}
              {field("explanationKeyFact", "Ключевой факт", form.explanationKeyFact, (value) => change("explanationKeyFact", value))}
              {field("source", "Пункт Источника", form.source, (value) => change("source", value))}
              {field("quote", "Цитата", form.quote ?? "", (value) => change("quote", value || null), true)}
            </div>
            <div className="flex flex-wrap gap-2 border-t pt-4">
              <Button onClick={() => void save()} disabled={busy}>{busy && <LoaderCircle className="animate-spin" aria-hidden />}Сохранить</Button>
              <Button variant="outline" onClick={() => void save(true)} disabled={busy}>Сохранить и опубликовать</Button>
              {selected.status === "published" ? <Button variant="secondary" onClick={() => void unpublish()} disabled={busy}>Снять с публикации</Button> : <Button variant="destructive" onClick={() => void remove()} disabled={busy}><Trash2 aria-hidden />Удалить Черновик</Button>}
            </div>
          </div>}
        </CardContent>
      </Card>
    </div>
  </div>;
}
