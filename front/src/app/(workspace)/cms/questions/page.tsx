import type { Metadata } from "next";
import { QuestionBank } from "@/components/cms/question-bank";
import { getScreen } from "@/lib/screens";

export const metadata: Metadata = { title: getScreen("questions").title };

export default function Page() {
  return <QuestionBank />;
}
