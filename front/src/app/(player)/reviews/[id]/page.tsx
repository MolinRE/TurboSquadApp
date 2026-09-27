import type { Metadata } from "next";
import { TripReview } from "@/components/game/trip-review";
import { SwipeReview } from "@/components/swipes/swipe-review";
import { getScreen } from "@/lib/screens";

export const metadata: Metadata = { title: getScreen("review").title };

export default async function Page({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  if (id.startsWith("trip-")) return <TripReview tripId={id.slice(5)} />;
  if (id.startsWith("swipe-")) return <SwipeReview shiftId={id.slice(6)} />;
  return <main className="px-4 py-8"><p role="alert">Разбор не найден.</p></main>;
}
