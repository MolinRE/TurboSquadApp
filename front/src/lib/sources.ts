import { apiJson } from "@/lib/api";
import type { Question } from "@/lib/questions";

export type SourceSummary = { id: string; title: string; createdAt: string };
export type Source = SourceSummary & { text: string };
export type GenerationResult = { created: number; duplicates: number; errors: string[]; drafts: Question[] };

export function listSources() { return apiJson<SourceSummary[]>("/api/cms/sources"); }
export function getSource(id: string) { return apiJson<Source>(`/api/cms/sources/${encodeURIComponent(id)}`); }
export function createSource(title: string, text: string) {
  return apiJson<Source>("/api/cms/sources", { method: "POST", body: JSON.stringify({ title, text }) });
}
export function generateQuestions(id: string) {
  return apiJson<GenerationResult>(`/api/cms/sources/${encodeURIComponent(id)}/generate`, { method: "POST" });
}
export function listSourceDrafts(id: string) {
  return apiJson<Question[]>(`/api/cms/sources/${encodeURIComponent(id)}/drafts`);
}
