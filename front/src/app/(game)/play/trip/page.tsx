import type { Metadata } from "next";
import { TripGame } from "@/components/game/trip-game";
import { getScreen } from "@/lib/screens";

export const metadata: Metadata = { title: getScreen("trip").title };

export default function Page() {
  return <TripGame />;
}
