import type { Metadata } from "next";
import { EventEditor } from "@/components/cms/event-editor";
import { getScreen } from "@/lib/screens";

export const metadata: Metadata = { title: getScreen("event").title };

export default async function Page({ params }: PageProps<"/cms/events/[id]">) {
  const { id } = await params;
  return <EventEditor eventId={id} />;
}
