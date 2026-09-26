import type { Metadata } from "next";
import { ScreenPlaceholder } from "@/components/shell/screen-placeholder";
import { getScreen } from "@/lib/screens";

export const metadata: Metadata = { title: getScreen("dictionaries").title };

export default function Page() {
  return <ScreenPlaceholder id="dictionaries" />;
}
