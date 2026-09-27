import type { Metadata } from "next";
import { GenerationWorkspace } from "@/components/cms/generation-workspace";
import { getScreen } from "@/lib/screens";

export const metadata: Metadata = { title: getScreen("generation").title };

export default function Page() {
  return <GenerationWorkspace />;
}
