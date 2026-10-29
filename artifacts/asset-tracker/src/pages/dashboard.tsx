import { Layout } from "@/components/layout";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  useGetDashboardSummary, useGetDashboardActivity, useGetLocationBreakdown,
  useListInsights, useGetGateActivity,
} from "@workspace/api-client-react";
import { Activity, AlertCircle, ArrowRight, Box, MapPin, Radio, ShieldAlert, Wrench, UserCheck, ArrowRightLeft, MessageSquare } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { formatDate, formatTime } from "@/lib/utils";
import { Link } from "wouter";
import { MovementTrendChart, SbuStatusChart } from "@/components/dashboard-charts";
import type { ReactNode } from "react";

function SbuBadge({ code, color, size = "sm" }: { code: string; color: string; size?: "xs" | "sm" }) {
  const px = size === "xs" ? "px-1.5 py-0.5 text-[9px]" : "px-2 py-0.5 text-[10px]";
  return (
    <span
      className={`inline-flex items-center font-bold rounded border ${px}`}
      style={{ color, borderColor: color, backgroundColor: `${color}18` }}
    >
      {code}
    </span>
  );
}

/** Whole-card link: the entire card is the click target. */
function CardLink({ href, className = "", children }: { href: string; className?: string; children: ReactNode }) {
  return (
    <Link
      href={href}
      className={`block rounded-xl transition-shadow hover:shadow-md focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring ${className}`}
    >
      {children}
    </Link>
  );
}

/** Non-interactive "view all" affordance, for use inside a CardLink. */
function ViewAllHint({ label = "View all" }: { label?: string }) {
  return (
    <span className="inline-flex items-center gap-1 text-xs font-medium text-muted-foreground">
      {label} <ArrowRight className="w-3 h-3" />
    </span>
  );
}

function StatCard({
  href, title, icon, value, caption, valueClassName = "",
}: {
  href: string; title: string; icon: ReactNode; value: ReactNode; caption: string; valueClassName?: string;
}) {
  return (
    <CardLink href={href}>
      <Card className="h-full">
        <CardHeader className="flex flex-row items-center justify-between pb-2">
          <CardTitle className="text-sm font-medium text-muted-foreground">{title}</CardTitle>
          {icon}
        </CardHeader>
        <CardContent>
          <div className={`text-2xl font-bold font-mono ${valueClassName}`}>{value}</div>
          <p className="text-xs text-muted-foreground mt-1">{caption}</p>
        </CardContent>
      </Card>
    </CardLink>
  );
}

function activityIcon(type: string) {
  if (type === "rfid_scan")       return <Radio className="w-4 h-4 text-blue-500" />;
  if (type === "movement")        return <Activity className="w-4 h-4 text-blue-400" />;
  if (type.includes("gate_pass")) return <ShieldAlert className="w-4 h-4 text-purple-500" />;
  if (type === "insight")         return <AlertCircle className="w-4 h-4 text-amber-500" />;
  if (type === "lifecycle_event") return <Wrench className="w-4 h-4 text-green-500" />;
  return <Box className="w-4 h-4 text-gray-400" />;
}

export default function Dashboard() {
  const { data: summary, isLoading: loadingSummary } = useGetDashboardSummary();
  const { data: activity, isLoading: loadingActivity } = useGetDashboardActivity({ limit: 12 });
  const { data: locations } = useGetLocationBreakdown();
  const { data: insights } = useListInsights();
  const { data: gateActivity } = useGetGateActivity({ limit: 10 });

  const activeInsights = insights?.filter(i => !i.dismissed) || [];

  if (loadingSummary) {
    return (
      <Layout>
        <div className="animate-pulse space-y-6">
          <div className="h-8 bg-muted w-64 rounded" />
          <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
            {[1,2,3,4].map(i => <div key={i} className="h-28 bg-muted rounded-lg" />)}
          </div>
        </div>
      </Layout>
    );
  }

  return (
    <Layout>
      <div className="space-y-6">
        {/* Header */}
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-2xl font-bold tracking-tight">Operations Dashboard</h1>
          </div>
        </div>

        {/* Critical insights banner */}
        {activeInsights.filter(i => i.severity === "critical").length > 0 && (
          <div className="bg-red-500/10 border border-red-500/20 rounded-lg p-4 flex items-start gap-3">
            <AlertCircle className="w-5 h-5 text-red-600 dark:text-red-400 mt-0.5 flex-shrink-0" />
            <div>
              <h3 className="text-sm font-semibold text-red-800 dark:text-red-200">
                {activeInsights.filter(i => i.severity === "critical").length} Critical Alert{activeInsights.filter(i => i.severity === "critical").length > 1 ? "s" : ""}
              </h3>
              <p className="text-sm text-red-700/80 dark:text-red-300/80 mt-0.5">
                {activeInsights.filter(i => i.severity === "critical")[0]?.message}
                {" "}<Link href="/insights" className="underline font-medium hover:text-red-900 dark:hover:text-red-100">Review all insights →</Link>
              </p>
            </div>
          </div>
        )}
        {activeInsights.filter(i => i.severity !== "critical").length > 0 && activeInsights.filter(i => i.severity === "critical").length === 0 && (
          <div className="bg-amber-500/10 border border-amber-500/20 rounded-lg p-4 flex items-start gap-3">
            <AlertCircle className="w-5 h-5 text-amber-600 dark:text-amber-400 mt-0.5 flex-shrink-0" />
            <div>
              <h3 className="text-sm font-semibold text-amber-800 dark:text-amber-200">Active AI Insights ({activeInsights.length})</h3>
              <p className="text-sm text-amber-700/80 dark:text-amber-300/80 mt-0.5">Anomalies or recommendations detected. <Link href="/insights" className="underline font-medium hover:text-amber-900 dark:hover:text-amber-100">Review insights</Link></p>
            </div>
          </div>
        )}

        {/* Summary stat cards */}
        <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
          <StatCard
            href="/assets"
            title="Total Assets"
            icon={<Box className="w-4 h-4 text-muted-foreground" />}
            value={summary?.totalAssets}
            caption={`${summary?.activeAssets ?? 0} active`}
          />
          <StatCard
            href="/assets?status=lost_stolen"
            title="Missing Assets"
            icon={<AlertCircle className="w-4 h-4 text-destructive" />}
            value={summary?.missingAssets}
            valueClassName="text-destructive"
            caption="Requires attention"
          />
          <StatCard
            href="/gate-passes"
            title="Pending Gate Passes"
            icon={<ShieldAlert className="w-4 h-4 text-muted-foreground" />}
            value={summary?.pendingGatePasses}
            caption="Awaiting approval"
          />
          <StatCard
            href="/movements"
            title="Today's Movements"
            icon={<Activity className="w-4 h-4 text-muted-foreground" />}
            value={summary?.todayMovements}
            caption="Tracked events"
          />
        </div>

        {/* SBU Breakdown */}
        {summary?.sbuBreakdown && summary.sbuBreakdown.length > 0 && (
          <div>
            <h2 className="text-sm font-semibold text-muted-foreground uppercase tracking-wider mb-3">SBU Asset Breakdown</h2>
            <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-5 gap-3">
              {summary.sbuBreakdown.map((sbu) => (
                <Link key={sbu.sbuId} href={`/assets?sbuId=${sbu.sbuId}`} className="block rounded-xl focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring">
                  <div className="rounded-xl border bg-card p-4 hover:shadow-md transition-shadow cursor-pointer h-full">
                    <div className="flex items-center justify-between mb-2">
                      <SbuBadge code={sbu.sbuCode} color={sbu.sbuColor} />
                      {sbu.missingAssets > 0 && (
                        <AlertCircle className="w-3.5 h-3.5 text-destructive" />
                      )}
                    </div>
                    <div className="text-2xl font-bold font-mono mt-1" style={{ color: sbu.sbuColor }}>
                      {sbu.totalAssets}
                    </div>
                    <div className="text-[10px] text-muted-foreground mt-0.5 truncate" title={sbu.sbuName}>{sbu.sbuName}</div>
                    <div className="mt-2 flex gap-2 text-[10px] text-muted-foreground">
                      <span className="text-green-600 dark:text-green-400 font-medium">{sbu.activeAssets} active</span>
                      {sbu.missingAssets > 0 && <span className="text-red-500 font-medium">{sbu.missingAssets} missing</span>}
                      {(sbu.maintenanceAssets ?? 0) > 0 && <span className="text-amber-500 font-medium">{sbu.maintenanceAssets} repair</span>}
                    </div>
                  </div>
                </Link>
              ))}
            </div>
          </div>
        )}

        {/* Charts */}
        <div className="grid grid-cols-1 xl:grid-cols-3 gap-6">
          <div className="xl:col-span-2">
            <MovementTrendChart days={14} viewAllHref="/movements" />
          </div>
          {summary?.sbuBreakdown && summary.sbuBreakdown.length > 0 && (
            <SbuStatusChart breakdown={summary.sbuBreakdown} viewAllHref="/sbus" />
          )}
        </div>

        {/* Main grid: activity + gate activity + distribution */}
        <div className="grid grid-cols-1 xl:grid-cols-3 gap-6">
          {/* Recent Activity */}
          <CardLink href="/movements" className="xl:col-span-2">
            <Card className="h-full">
            <CardHeader className="flex flex-row items-center justify-between space-y-0">
              <CardTitle className="text-lg">Recent Activity</CardTitle>
              <ViewAllHint label="All movements" />
            </CardHeader>
            <CardContent>
              {loadingActivity ? (
                <div className="space-y-3">
                  {[1,2,3].map(i => <div key={i} className="h-12 bg-muted rounded animate-pulse" />)}
                </div>
              ) : activity?.length === 0 ? (
                <p className="text-sm text-muted-foreground">No recent activity.</p>
              ) : (
                <div className="space-y-3">
                  {activity?.map((item) => (
                    <div key={item.id} className="flex items-start gap-3 pb-3 border-b last:border-0 last:pb-0">
                      <div className="mt-0.5 flex-shrink-0">{activityIcon(item.type)}</div>
                      <div className="flex-1 min-w-0">
                        <p className="text-sm font-medium leading-snug truncate">{item.description}</p>
                        <p className="text-xs text-muted-foreground font-mono mt-0.5">{formatDate(item.timestamp)}</p>
                      </div>
                      <div className="flex gap-1.5 flex-shrink-0">
                        {item.sbuCode && <SbuBadge code={item.sbuCode} color="#6B7280" size="xs" />}
                        {item.assetName && (
                          <Badge variant="secondary" className="font-mono text-[9px]">{item.assetName.split(" ").slice(0,2).join(" ")}</Badge>
                        )}
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </CardContent>
            </Card>
          </CardLink>

          {/* Right column: Gate Activity + Asset Distribution */}
          <div className="space-y-6">
            {/* Gate Activity */}
            <CardLink href="/gate-passes">
              <Card className="h-full">
              <CardHeader className="flex flex-row items-center justify-between space-y-0">
                <CardTitle className="text-base flex items-center gap-2">
                  <Radio className="w-4 h-4 text-blue-500" />
                  Gate Activity
                </CardTitle>
                <ViewAllHint label="Gate passes" />
              </CardHeader>
              <CardContent>
                {!gateActivity || gateActivity.length === 0 ? (
                  <p className="text-xs text-muted-foreground">No gate scans recorded yet. Use the RFID Simulator to test.</p>
                ) : (
                  <div className="space-y-3">
                    {gateActivity.slice(0, 8).map((ev, i) => (
                      <div key={i} className="flex items-start gap-2 text-xs">
                        <SbuBadge code={ev.sbuCode} color="#6B7280" size="xs" />
                        <div className="flex-1 min-w-0">
                          <p className="font-medium truncate">{ev.assetName}</p>
                          <p className="text-muted-foreground font-mono truncate">
                            {ev.direction !== "internal" && (
                              <span className={ev.direction === "exit" ? "text-purple-600 dark:text-purple-400 font-sans font-semibold" : "text-emerald-600 dark:text-emerald-400 font-sans font-semibold"}>
                                {ev.direction === "exit" ? "EXIT · " : "ENTRY · "}
                              </span>
                            )}
                            {ev.locationName}
                          </p>
                        </div>
                        <span className="text-muted-foreground font-mono flex-shrink-0">
                          {formatTime(ev.timestamp)}
                        </span>
                      </div>
                    ))}
                  </div>
                )}
              </CardContent>
              </Card>
            </CardLink>

            {/* Asset Distribution */}
            <CardLink href="/locations">
              <Card className="h-full">
              <CardHeader className="flex flex-row items-center justify-between space-y-0">
                <CardTitle className="text-base flex items-center gap-2">
                  <MapPin className="w-4 h-4" />
                  Asset Distribution
                </CardTitle>
                <ViewAllHint label="Locations" />
              </CardHeader>
              <CardContent>
                <div className="space-y-3">
                  {locations?.filter(l => l.assetCount > 0).slice(0, 8).map((loc) => (
                    <div key={loc.locationId} className="flex items-center justify-between gap-2">
                      <div className="min-w-0">
                        <p className="text-sm font-medium leading-none truncate">{loc.locationName}</p>
                        <p className="text-xs text-muted-foreground capitalize mt-0.5">{loc.locationType}</p>
                      </div>
                      <div className="flex items-center gap-2 flex-shrink-0">
                        {loc.sbuCode && <SbuBadge code={loc.sbuCode} color="#6B7280" size="xs" />}
                        <div className="font-mono text-sm font-bold w-5 text-right">{loc.assetCount}</div>
                      </div>
                    </div>
                  ))}
                </div>
              </CardContent>
              </Card>
            </CardLink>
          </div>
        </div>
      </div>
    </Layout>
  );
}
