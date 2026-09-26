import type { Metadata } from "next";
import { ScreenPlaceholder } from "@/components/shell/screen-placeholder";
import { getScreen } from "@/lib/screens";

export const metadata: Metadata = { title: getScreen("leaderboard").title };

export default function Page() {
  return <ScreenPlaceholder id="leaderboard" />;
}
