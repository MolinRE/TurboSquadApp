import { PlayerHeader } from "@/components/shell/player-header";
import { PlayerTabBar } from "@/components/shell/player-tab-bar";

/** Шаблон «Приложение проводника»: телефон, шапка и нижние вкладки. */
export default function PlayerLayout({ children }: LayoutProps<"/">) {
  return (
    <div className="min-h-svh bg-canvas">
      <div className="mx-auto flex min-h-svh w-full max-w-md flex-col bg-background">
        <PlayerHeader />
        <main className="flex flex-1 flex-col gap-3 px-4 pt-1 pb-28">
          {children}
        </main>
        <PlayerTabBar />
      </div>
    </div>
  );
}
