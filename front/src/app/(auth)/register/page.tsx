import type { Metadata } from "next";
import { RegisterForm } from "@/components/auth/register-form";
import { getScreen } from "@/lib/screens";

export const metadata: Metadata = { title: getScreen("register").title };

export default function Page() {
  return <RegisterForm />;
}
