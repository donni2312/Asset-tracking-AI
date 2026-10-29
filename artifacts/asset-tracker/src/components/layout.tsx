import { Link, useLocation } from "wouter";
import { cn } from "@/lib/utils";
import {
  Activity, Box, Boxes, Building2, CheckCircle2, Database, FileDown, FileText, HardDrive, Inbox, LayoutDashboard, MapPin, Radio, ScanLine,
  Settings2, ShieldAlert, Truck, Users, Gauge, 
} from "lucide-react";
import { useListApprovals, useListNotifications } from "@workspace/api-client-react";
import { NotificationsButton } from "@/components/notifications";
import { useCurrentUser } from "@/hooks/use-auth";
import { inGroup, ROLE_GROUPS, type RoleGroup } from "@/lib/roles";
import type { UserRole } from "@workspace/api-client-react";
import { ReactNode } from "react";
import { usePendingGatePassNotifications } from "@/components/gate-pass-notifications";
import { SettingsButton } from "@/components/settings-dialog";
import { AccountBadge } from "@/components/sign-in";
import { useDateFormat } from "@/lib/date-format";

interface LayoutProps {
  children: ReactNode;
}

export function Layout({ children }: LayoutProps) {
  const [location] = useLocation();
  const pendingGatePasses = usePendingGatePassNotifications();
  // Dates are formatted by plain functions reading the stored preference, so the
  // page content is re-mounted when it changes - that is what keeps every screen
  // consistent after a switch in Settings. The sidebar (and the open Settings
  // panel) sits outside the keyed element and survives.
  const [dateFormat] = useDateFormat();
  const user = useCurrentUser();
  const { data: approvals } = useListApprovals({ query: { enabled: !!user, queryKey: ["approvals"], refetchInterval: 60_000 } });
  const { data: unread } = useListNotifications({ unread: true }, { query: { enabled: !!user, queryKey: ["notifications", "unread"], refetchInterval: 60_000 } });
  const pendingApprovals = approvals?.length ?? 0;

  // Main entries follow the approved designs; the screens that came before them stay under "Tracking".
  // Each entry shows only for the roles that may use it (lib/roles.ts mirrors the API); the API refuses the rest anyway.
  type Entry = { name: string; href: string; icon: typeof Box; roles: RoleGroup | UserRole[] };
  const navigation: Entry[] = [
    { name: "Dashboard",           href: "/",              icon: LayoutDashboard, roles: "everyone" },
    { name: "Gate",                href: "/gate",          icon: ShieldAlert, roles: "gateOfficers" },
    { name: "Scan",                href: "/scan",          icon: ScanLine,  roles: "scanners" },
    { name: "Asset Register",      href: "/register",      icon: Boxes,     roles: "financeReaders" },
    { name: "Reports",             href: "/reports/category", icon: FileText, roles: "financeReaders" },
    { name: "Approvals",           href: "/approvals",     icon: Inbox,     roles: [...ROLE_GROUPS.gatePassApprovers, "group_finance", "auditor"] },
    { name: "Verification",        href: "/verification",  icon: ScanLine,  roles: "verificationReaders" },
    { name: "Stores & Receiving",  href: "/stores",        icon: CheckCircle2, roles: ["stores", "sbu_finance", "group_finance", "sbu_it", "group_it", "sbu_admin", "hod", "sbu_hr"] },
    { name: "My items",            href: "/my-items",      icon: CheckCircle2, roles: ["employee"] },
    { name: "Gate Passes",         href: "/gate-passes",   icon: ShieldAlert, roles: "gatePassReaders" },
    { name: "People & Toolboxes",  href: "/people",        icon: Users,     roles: "peopleReaders" },
    { name: "Departments",         href: "/departments",   icon: Building2, roles: "peopleReaders" },
    { name: "Fleet & Non-IT",      href: "/fleet",         icon: Truck,     roles: ["sbu_admin", "sbu_finance", "group_finance", "auditor"] },
  ];
  const tracking: Entry[] = [
    { name: "Operations",          href: "/operations",    icon: Gauge,     roles: "recordReaders" },
    { name: user?.role === "employee" ? "My items" : "Assets (tracking)", href: "/assets", icon: Box, roles: "assetReaders" },
    { name: "SBUs",                href: "/sbus",          icon: Building2, roles: "recordReaders" },
    { name: "Locations",           href: "/locations",     icon: MapPin,    roles: "recordReaders" },
    { name: "Movements",           href: "/movements",     icon: Activity,  roles: "gateLogReaders" },
    { name: "RFID Simulator",      href: "/rfid-simulator",icon: Radio,     roles: "rfidSimulator" },
    { name: "AI Insights",         href: "/insights",      icon: Database,  roles: "recordReaders" },
    { name: "CSV Reports",         href: "/reports",       icon: FileDown,  roles: "recordReaders" },
    { name: "BC Sync",             href: "/bc-sync",       icon: HardDrive, roles: "bcViewers" },
    { name: "Administration",      href: "/administration", icon: Settings2, roles: [...ROLE_GROUPS.administrators, "sbu_finance", "sbu_it", "auditor"] },
  ];
  const visible = (e: Entry) => (typeof e.roles === "string" ? inGroup(user, e.roles) : !!user && (user.role === "system_admin" || e.roles.includes(user.role)));
  const shownNav = navigation.filter(visible);
  const shownTracking = tracking.filter(visible);
  const isActive = (href: string) =>
    location === href || (href !== "/" && href !== "/reports" && location.startsWith(href)) ||
    (href === "/register" && /^\/assets\/(new|\d+\/card)/.test(location));

  return (
    <div className="flex min-h-[100dvh] w-full bg-background text-foreground flex-col md:h-[100dvh] md:flex-row md:overflow-hidden">
      {/*
       * Sidebar. On desktop it is pinned to the viewport (h-[100dvh] + sticky top-0)
       * and only its own nav list scrolls, so the navigation never scrolls away with
       * the page. On mobile it stays in normal flow above the content.
       */}
      <aside className="w-full flex-shrink-0 flex-col border-r border-sidebar-border bg-sidebar text-sidebar-foreground flex md:sticky md:top-0 md:h-[100dvh] md:w-64">
        <div className="p-4 md:p-6 border-b border-sidebar-border">
          <Link href="/" className="flex items-center gap-3 cursor-pointer">
            <img
              src="/tsl-logo.png"
              alt="TSL Limited"
              className="w-9 h-9 object-contain flex-shrink-0"
            />
            <div className="leading-tight">
              <div className="font-bold text-base tracking-tight">TSL AssetIQ</div>
              <div className="text-[10px] text-sidebar-foreground/50 font-medium tracking-wide whitespace-nowrap">Real-time Asset Intelligence</div>
            </div>
          </Link>
        </div>
        <nav className="flex-1 p-3 space-y-0.5 overflow-y-auto">
          {[...shownNav, ...(shownTracking.length > 0 ? [null, ...shownTracking] : [])].map((item) => {
            if (item === null) {
              return <div key="tracking" className="px-3 pb-1 pt-4 text-[11px] uppercase tracking-wider text-sidebar-foreground/50">Tracking</div>;
            }
            const active = isActive(item.href);
            return (
              <Link key={item.name} href={item.href} className={cn(
                "flex items-center gap-3 px-3 py-2 rounded-md transition-colors text-sm font-medium",
                active
                  ? "bg-sidebar-primary text-sidebar-primary-foreground"
                  : "text-sidebar-foreground/80 hover:bg-sidebar-accent hover:text-sidebar-accent-foreground"
              )}>
                <item.icon className="w-4 h-4 flex-shrink-0" />
                <span className="flex-1">{item.name}</span>
                {item.href === "/gate-passes" && pendingGatePasses > 0 && (
                  <span
                    className="min-w-5 h-5 px-1.5 rounded-full bg-amber-500 text-[10px] font-bold text-slate-900 flex items-center justify-center"
                    aria-label={`${pendingGatePasses} gate passes awaiting approval`}
                    title={`${pendingGatePasses} awaiting approval`}
                  >
                    {pendingGatePasses}
                  </span>
                )}
                {item.href === "/approvals" && pendingApprovals > 0 && (
                  <span
                    className="min-w-5 h-5 px-1.5 rounded-full bg-amber-500 text-[10px] font-bold text-slate-900 flex items-center justify-center"
                    aria-label={`${pendingApprovals} approvals waiting on you`}
                  >
                    {pendingApprovals}
                  </span>
                )}
              </Link>
            );
          })}
        </nav>
        <div className="p-3 border-t border-sidebar-border space-y-2">
          <AccountBadge />
          {user && <NotificationsButton count={unread?.length ?? 0} />}
          <SettingsButton />
          <div className="px-2 text-[10px] text-sidebar-foreground/40 font-mono tracking-wider">
            TSL GROUP — ASSET MGMT v2
          </div>
        </div>
      </aside>

      {/* Main Content */}
      <main className="flex-1 flex flex-col min-w-0 overflow-hidden bg-background">
        <div className="flex-1 overflow-y-auto p-4 md:p-8">
          <div key={dateFormat} className="max-w-7xl mx-auto w-full">
            {children}
          </div>
        </div>
      </main>
    </div>
  );
}
