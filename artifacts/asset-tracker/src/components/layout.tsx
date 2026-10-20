import { Link, useLocation } from "wouter";
import { cn } from "@/lib/utils";
import { Activity, Box, Building2, Database, HardDrive, LayoutDashboard, MapPin, Radio, ShieldAlert } from "lucide-react";
import { ReactNode } from "react";
import { usePendingGatePassNotifications } from "@/components/gate-pass-notifications";
import { OperatorBadge } from "@/components/operator-badge";
import { SettingsButton } from "@/components/settings-dialog";
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

  const navigation = [
    { name: "Dashboard",       href: "/",              icon: LayoutDashboard },
    { name: "SBUs",            href: "/sbus",          icon: Building2 },
    { name: "Assets",          href: "/assets",        icon: Box },
    { name: "Locations",       href: "/locations",     icon: MapPin },
    { name: "Movements",       href: "/movements",     icon: Activity },
    { name: "Gate Passes",     href: "/gate-passes",   icon: ShieldAlert },
    { name: "RFID Simulator",  href: "/rfid-simulator",icon: Radio },
    { name: "AI Insights",     href: "/insights",      icon: Database },
    { name: "BC Sync",         href: "/bc-sync",       icon: HardDrive },
  ];

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
          {navigation.map((item) => {
            const isActive = location === item.href || (item.href !== "/" && location.startsWith(item.href));
            return (
              <Link key={item.name} href={item.href} className={cn(
                "flex items-center gap-3 px-3 py-2 rounded-md transition-colors text-sm font-medium",
                isActive
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
              </Link>
            );
          })}
        </nav>
        <div className="p-3 border-t border-sidebar-border space-y-2">
          <OperatorBadge />
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
