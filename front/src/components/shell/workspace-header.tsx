"use client";

import { usePathname } from "next/navigation";
import { Separator } from "@/components/ui/separator";
import { SidebarTrigger } from "@/components/ui/sidebar";
import { findScreenByPathname, workspaceSections } from "@/lib/screens";

export function WorkspaceHeader() {
  const screen = findScreenByPathname(usePathname());

  return (
    <header className="sticky top-0 z-10 flex h-14 shrink-0 items-center gap-3 border-b bg-background/95 px-4 backdrop-blur">
      <SidebarTrigger className="-ml-1" />
      <Separator orientation="vertical" className="h-5" />
      <p className="flex min-w-0 items-center gap-1.5 text-sm">
        {screen?.section && (
          <>
            <span className="text-muted-foreground">
              {workspaceSections[screen.section]}
            </span>
            <span className="text-muted-foreground" aria-hidden>
              /
            </span>
          </>
        )}
        <span className="truncate font-bold">{screen?.title}</span>
      </p>
    </header>
  );
}
