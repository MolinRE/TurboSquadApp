"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { ChevronsUpDown, FastForward, LogOut } from "lucide-react";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarRail,
} from "@/components/ui/sidebar";
import { BrandMark } from "@/components/shell/brand-mark";
import { initialsOf, roleLabelOf, useCurrentUser } from "@/lib/use-current-user";
import {
  screens,
  workspaceSections,
  type WorkspaceSection,
} from "@/lib/screens";

const sectionOrder: WorkspaceSection[] = ["analytics", "cms"];

// Активный пункт как на эталоне: белая плашка на тёмном меню.
const activeItem =
  "data-[active=true]:bg-card data-[active=true]:text-primary data-[active=true]:font-bold data-[active=true]:[&>svg]:text-brand";

export function AppSidebar() {
  const pathname = usePathname();
  const user = useCurrentUser();

  return (
    <Sidebar collapsible="icon">
      <SidebarHeader className="gap-3">
        <div className="flex items-center gap-2.5 px-1 pt-1 font-extrabold group-data-[collapsible=icon]:px-0">
          <BrandMark />
          <span className="truncate group-data-[collapsible=icon]:hidden">
            Турбо-Бригада
          </span>
        </div>
        <SidebarMenu>
          <SidebarMenuItem>
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <SidebarMenuButton
                  size="lg"
                  className="bg-sidebar-accent data-[state=open]:bg-sidebar-accent"
                >
                  <Avatar className="size-8 rounded-lg">
                    <AvatarFallback className="rounded-lg bg-sidebar-primary text-xs font-extrabold text-sidebar-primary-foreground">
                      {user ? initialsOf(user.displayName) : null}
                    </AvatarFallback>
                  </Avatar>
                  <span className="grid flex-1 text-left leading-tight">
                    <span className="truncate font-semibold">{user?.displayName}</span>
                    <span className="truncate text-xs text-sidebar-foreground/60">
                      {user ? roleLabelOf(user.roles) : null}
                    </span>
                  </span>
                  <ChevronsUpDown className="ml-auto size-4" aria-hidden />
                </SidebarMenuButton>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="start" className="min-w-56">
                <DropdownMenuLabel>{user?.displayName}</DropdownMenuLabel>
                <DropdownMenuSeparator />
                <DropdownMenuItem asChild>
                  <Link href="/login">
                    <LogOut aria-hidden />
                    Сменить аккаунт
                  </Link>
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarHeader>

      <SidebarContent>
        {sectionOrder.map((section) => (
          <SidebarGroup key={section}>
            <SidebarGroupLabel className="text-sidebar-foreground/50 uppercase tracking-wider">
              {workspaceSections[section]}
            </SidebarGroupLabel>
            <SidebarGroupContent>
              <SidebarMenu>
                {screens
                  .filter((s) => s.section === section && !s.hidden)
                  .map((s) => {
                    const Icon = s.icon;
                    const active =
                      pathname === s.path || pathname.startsWith(s.path + "/");
                    return (
                      <SidebarMenuItem key={s.id}>
                        <SidebarMenuButton
                          asChild
                          isActive={active}
                          tooltip={s.title}
                          className={activeItem}
                        >
                          <Link href={s.path}>
                            {Icon && <Icon aria-hidden />}
                            <span>{s.title}</span>
                          </Link>
                        </SidebarMenuButton>
                      </SidebarMenuItem>
                    );
                  })}
              </SidebarMenu>
            </SidebarGroupContent>
          </SidebarGroup>
        ))}
      </SidebarContent>

      <SidebarFooter>
        <SidebarMenu>
          <SidebarMenuItem>
            <SidebarMenuButton
              disabled
              tooltip="Демо: прокрутить день (этап 2)"
              className="border border-dashed border-sidebar-foreground/30 text-sidebar-foreground/70"
            >
              <FastForward aria-hidden />
              <span>Демо: прокрутить день</span>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarFooter>
      <SidebarRail />
    </Sidebar>
  );
}
