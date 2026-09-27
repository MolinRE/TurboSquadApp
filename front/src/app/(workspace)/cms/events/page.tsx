import type { Metadata } from "next";
import { EventList } from "@/components/cms/event-list";
import { getScreen } from "@/lib/screens";

export const metadata: Metadata = { title: getScreen("events").title };

export default function Page() {
  return <EventList />;
}
