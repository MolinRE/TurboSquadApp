import { apiJson } from "@/lib/api";

export type EventVersionSummary = { version: number; publishedAt: string };
export type EventSummary = {
  id: string;
  title: string;
  topic: string;
  serviceClasses: string[];
  latestVersion: number;
  publishedAt: string;
  versions: EventVersionSummary[];
};
export type EventVersion = { id: string; version: number; publishedAt: string; document: string };
export type EventDraft = { id: string; sourceId: string | null; document: string; createdAt: string };
export type ValidationIssue = {
  severity: "Error" | "Warning";
  location: { stepId: string | null; variantId: string | null; timeout: boolean; transition: number | null };
  where: string;
  message: string;
  rule: string;
};
export type ValidationReport = { isValid: boolean; errors: ValidationIssue[]; warnings: ValidationIssue[] };

const eventPath = (id: string) => `/api/cms/events/${encodeURIComponent(id)}`;
const draftPath = (id: string) => `/api/cms/events/drafts/${encodeURIComponent(id)}`;

export function listEvents() {
  return apiJson<EventSummary[]>("/api/cms/events");
}

export function listEventDrafts(sourceId?: string) {
  return apiJson<EventDraft[]>(`/api/cms/events/drafts${sourceId ? `?sourceId=${encodeURIComponent(sourceId)}` : ""}`);
}

export function createEventDraft() {
  return apiJson<EventDraft>("/api/cms/events/drafts", { method: "POST" });
}

export function getEventDraft(id: string) {
  return apiJson<EventDraft>(draftPath(id));
}

export function saveEventDraft(id: string, document: string) {
  return apiJson<EventDraft>(draftPath(id), {
    method: "PUT", body: JSON.stringify({ document }),
  });
}

export function validateEventDraft(id: string) {
  return apiJson<ValidationReport>(`${draftPath(id)}/validate`, { method: "POST" });
}

export function publishEventDraft(id: string) {
  return apiJson<EventVersion>(`${draftPath(id)}/publish`, { method: "POST" });
}

export function getEventVersion(id: string, version: number) {
  return apiJson<EventVersion>(`${eventPath(id)}/versions/${version}`);
}

export function validateEvent(id: string, document: string) {
  return apiJson<ValidationReport>(`${eventPath(id)}/validate`, {
    method: "POST", body: JSON.stringify({ document }),
  });
}

export function publishEvent(id: string, expectedVersion: number, document: string) {
  return apiJson<EventVersion>(`${eventPath(id)}/publish`, {
    method: "POST", body: JSON.stringify({ expectedVersion, document }),
  });
}
