import type { Metadata } from "next";
import { SourcesWorkspace } from "@/components/cms/sources-workspace";
import { getScreen } from "@/lib/screens";

export const metadata: Metadata = { title: getScreen("sources").title };

export default function Page() {
  return <SourcesWorkspace />;
}
