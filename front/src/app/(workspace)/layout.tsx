import { SidebarInset, SidebarProvider } from "@/components/ui/sidebar";
import { AppSidebar } from "@/components/shell/app-sidebar";
import { WorkspaceHeader } from "@/components/shell/workspace-header";

/** Шаблон «Рабочее место»: десктоп, тёмное боковое меню, Руководитель и Методист. */
export default function WorkspaceLayout({ children }: LayoutProps<"/">) {
  return (
    <SidebarProvider>
      <AppSidebar />
      <SidebarInset className="bg-background">
        <WorkspaceHeader />
        <div className="flex flex-1 flex-col gap-4 p-6">{children}</div>
      </SidebarInset>
    </SidebarProvider>
  );
}
