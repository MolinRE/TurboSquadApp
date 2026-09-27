import type { Metadata } from "next";
import { EventDraftEditor } from "@/components/cms/event-draft-editor";

export const metadata: Metadata = { title: "Черновик События" };

export default async function Page({ params }: PageProps<"/cms/events/drafts/[draftId]">) {
  const { draftId } = await params;
  return <EventDraftEditor draftId={draftId} />;
}
