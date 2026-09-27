import type { Metadata } from "next";
import { LeaderboardOverview } from "@/components/leaderboard/leaderboard-overview";
import { getScreen } from "@/lib/screens";

export const metadata: Metadata = { title: getScreen("leaderboard").title };

export default function Page() {
  return <LeaderboardOverview />;
}
