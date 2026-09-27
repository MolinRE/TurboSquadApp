import type { Metadata } from "next";
import { getScreen } from "@/lib/screens";
import { ProfileOverview } from "@/components/profile/profile-overview";

export const metadata: Metadata = { title: getScreen("profile").title };

export default function Page() {
  return <ProfileOverview />;
}
