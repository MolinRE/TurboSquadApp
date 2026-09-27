import type { Metadata } from "next";
import { SwipeShift } from "@/components/swipes/swipe-shift";
import { getScreen } from "@/lib/screens";

export const metadata: Metadata = { title: getScreen("swipes").title };

export default function Page() {
  return <SwipeShift />;
}
