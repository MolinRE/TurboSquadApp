import { BrandMark } from "@/components/shell/brand-mark";

/** Шаблон «Вход»: одна колонка по центру. */
export default function AuthLayout({ children }: LayoutProps<"/">) {
  return (
    <div className="flex min-h-svh flex-col items-center justify-center gap-6 px-4 py-10">
      <div className="flex items-center gap-2.5 text-lg font-extrabold">
        <BrandMark />
        Турбо-Бригада
      </div>
      <main className="flex w-full max-w-sm flex-col">{children}</main>
    </div>
  );
}
