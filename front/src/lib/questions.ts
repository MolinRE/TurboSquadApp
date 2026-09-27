import { apiJson } from "@/lib/api";

export type QuestionType = "single" | "multiple" | "sequence" | "swipe";
export type QuestionStatus = "draft" | "published";
export type ChoiceOption = { id: string; text: string; correct: boolean; quote?: string; source?: string };
export type SequenceStep = { id: string; text: string; quote?: string; source?: string };
export type SwipeSide = { label: string; scaleDeltas: Record<string, number>; quote?: string; source?: string };
export type QuestionOptions =
  | { options: ChoiceOption[] }
  | { steps: SequenceStep[] }
  | { right: SwipeSide; left: SwipeSide; correct: "right" | "left" };

export type QuestionInput = {
  type: QuestionType;
  statement: string;
  options: QuestionOptions;
  explanationText: string;
  explanationKeyFact: string;
  quote: string | null;
  source: string;
  topic: string;
  categories: string[];
  serviceClasses: string[];
  baseFrequency: number;
  timeLimitSec: number | null;
  knowledgeCost: number;
};

export type Question = QuestionInput & { id: string; status: QuestionStatus };
export type QuestionCatalog = {
  topics: string[];
  categories: string[];
  serviceClasses: Array<{ code: string; name: string }>;
  scales: Array<{ code: string; name: string }>;
};

export type QuestionFilters = Partial<Record<"topic" | "category" | "serviceClass" | "type" | "status", string>>;

export function listQuestions(filters: QuestionFilters = {}) {
  const query = new URLSearchParams();
  Object.entries(filters).forEach(([key, value]) => { if (value) query.set(key, value); });
  return apiJson<Question[]>(`/api/cms/questions?${query}`);
}

export function getQuestionCatalog() {
  return apiJson<QuestionCatalog>("/api/cms/questions/catalog");
}

export function createQuestion(type: QuestionType) {
  return apiJson<Question>("/api/cms/questions", { method: "POST", body: JSON.stringify({ type }) });
}

export function saveQuestion(id: string, input: QuestionInput) {
  return apiJson<Question>(`/api/cms/questions/${encodeURIComponent(id)}`, { method: "PUT", body: JSON.stringify(input) });
}

export function publishQuestion(id: string) {
  return apiJson<Question>(`/api/cms/questions/${encodeURIComponent(id)}/publish`, { method: "POST" });
}

export function unpublishQuestion(id: string) {
  return apiJson<Question>(`/api/cms/questions/${encodeURIComponent(id)}/unpublish`, { method: "POST" });
}

export function deleteQuestion(id: string) {
  return apiJson<void>(`/api/cms/questions/${encodeURIComponent(id)}`, { method: "DELETE" });
}

export function emptyOptions(type: QuestionType): QuestionOptions {
  if (type === "swipe") return { right: { label: "", scaleDeltas: {} }, left: { label: "", scaleDeltas: {} }, correct: "right" };
  if (type === "sequence") return { steps: [] };
  return { options: [] };
}
