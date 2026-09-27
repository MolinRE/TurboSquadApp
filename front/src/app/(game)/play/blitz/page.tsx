import type { Metadata } from "next";
import { BlitzSession } from "@/components/blitz/blitz-session";
import { getScreen } from "@/lib/screens";

export const metadata: Metadata = { title: getScreen("blitz").title };

export default function Page() {
  return <BlitzSession />;
}
