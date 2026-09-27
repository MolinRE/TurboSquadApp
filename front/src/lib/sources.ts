import { apiJson } from "@/lib/api";
import type { Question } from "@/lib/questions";
import type { EventDraft } from "@/lib/events";

export type SourceSummary = { id: string; title: string; createdAt: string };
export type Source = SourceSummary & { text: string };
export type GenerationResult = { created: number; duplicates: number; errors: string[]; drafts: Question[] };
export type EventGenerationResult = { created: number; duplicates: number; errors: string[]; drafts: EventDraft[] };

export function listSources() { return apiJson<SourceSummary[]>("/api/cms/sources"); }
export function getSource(id: string) { return apiJson<Source>(`/api/cms/sources/${encodeURIComponent(id)}`); }
export function createSource(title: string, text: string) {
  return apiJson<Source>("/api/cms/sources", { method: "POST", body: JSON.stringify({ title, text }) });
}
export function generateQuestions(id: string, model: string) {
  return apiJson<GenerationResult>(`/api/cms/sources/${encodeURIComponent(id)}/generate`, {
    method: "POST", body: JSON.stringify({ model }),
  });
}
export function generateEvents(id: string, model: string) {
  return apiJson<EventGenerationResult>(`/api/cms/sources/${encodeURIComponent(id)}/generate-events`, {
    method: "POST", body: JSON.stringify({ model }),
  });
}
export function listSourceDrafts(id: string) {
  return apiJson<Question[]>(`/api/cms/sources/${encodeURIComponent(id)}/drafts`);
}
