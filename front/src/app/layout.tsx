import type { Metadata, Viewport } from "next";
import { Manrope } from "next/font/google";
import { TooltipProvider } from "@/components/ui/tooltip";
import "./globals.css";

const manrope = Manrope({
  variable: "--font-manrope",
  subsets: ["latin", "cyrillic"],
});

export const metadata: Metadata = {
  title: { default: "Турбо-Бригада", template: "%s · Турбо-Бригада" },
  description: "Тренажёр для проводников ВСМ",
};

export const viewport: Viewport = {
  themeColor: "#ecebea",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="ru" className={`${manrope.variable} h-full antialiased`}>
      <body className="min-h-full">
        <TooltipProvider>{children}</TooltipProvider>
      </body>
    </html>
  );
}
