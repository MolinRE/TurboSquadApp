import { GameTopBar } from "@/components/shell/game-top-bar";

/** Шаблон «Игра»: на весь экран телефона, без вкладок, только выход. */
export default function GameLayout({ children }: LayoutProps<"/">) {
  return (
    <div className="min-h-svh bg-canvas">
      <div className="mx-auto flex min-h-svh w-full max-w-md flex-col bg-background">
        <GameTopBar />
        <main className="flex flex-1 flex-col gap-3 px-4 pb-[calc(env(safe-area-inset-bottom)+1.5rem)]">
          {children}
        </main>
      </div>
    </div>
  );
}
