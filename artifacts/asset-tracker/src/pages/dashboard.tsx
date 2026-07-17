import { Layout } from "@/components/layout";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  useGetDashboardSummary, useGetDashboardActivity, useGetLocationBreakdown,
  useListInsights, useGetGateActivity,
} from "@workspace/api-client-react";
import { Activity, AlertCircle, Box, MapPin, Radio, ShieldAlert, Wrench, UserCheck, ArrowRightLeft, MessageSquare } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { formatDate } from "@/lib/utils";
import { Link } from "wouter";

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
            <p className="text-muted-foreground text-sm mt-0.5">TSL Limited Group — Real-time asset intelligence</p>
          </div>
          <Badge variant="outline" className="font-mono bg-green-500/10 text-green-600 border-green-500/20">LIVE DATA</Badge>
        </div>

        {/* Critical insights banner */}
        {activeInsights.filter(i => i.severity === "critical").length > 0 && (
          <div className="bg-red-500/10 border border-red-500/20 rounded-lg p-4 flex items-start gap-3">
            <AlertCircle className="w-5 h-5 text-red-600 mt-0.5 flex-shrink-0" />
            <div>
              <h3 className="text-sm font-semibold text-red-800">
                {activeInsights.filter(i => i.severity === "critical").length} Critical Alert{activeInsights.filter(i => i.severity === "critical").length > 1 ? "s" : ""}
              </h3>
              <p className="text-sm text-red-700/80 mt-0.5">
                {activeInsights.filter(i => i.severity === "critical")[0]?.message}
                {" "}<Link href="/insights" className="underline font-medium hover:text-red-900">Review all insights →</Link>
              </p>
            </div>
          </div>
        )}
        {activeInsights.filter(i => i.severity !== "critical").length > 0 && activeInsights.filter(i => i.severity === "critical").length === 0 && (
          <div className="bg-amber-500/10 border border-amber-500/20 rounded-lg p-4 flex items-start gap-3">
            <AlertCircle className="w-5 h-5 text-amber-600 mt-0.5 flex-shrink-0" />
            <div>
              <h3 className="text-sm font-semibold text-amber-800">Active AI Insights ({activeInsights.length})</h3>
              <p className="text-sm text-amber-700/80 mt-0.5">Anomalies or recommendations detected. <Link href="/insights" className="underline font-medium hover:text-amber-900">Review insights</Link></p>
            </div>
          </div>
        )}

        {/* Summary stat cards */}
        <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
          <Card>
            <CardHeader className="flex flex-row items-center justify-between pb-2">
              <CardTitle className="text-sm font-medium text-muted-foreground">Total Assets</CardTitle>
              <Box className="w-4 h-4 text-muted-foreground" />
            </CardHeader>
            <CardContent>
              <div className="text-2xl font-bold font-mono">{summary?.totalAssets}</div>
              <p className="text-xs text-muted-foreground mt-1">{summary?.activeAssets} active</p>
            </CardContent>
          </Card>
          <Card>
            <CardHeader className="flex flex-row items-center justify-between pb-2">
              <CardTitle className="text-sm font-medium text-muted-foreground">Missing Assets</CardTitle>
              <AlertCircle className="w-4 h-4 text-destructive" />
            </CardHeader>
            <CardContent>
              <div className="text-2xl font-bold font-mono text-destructive">{summary?.missingAssets}</div>
              <p className="text-xs text-muted-foreground mt-1">Requires attention</p>
            </CardContent>
          </Card>
          <Card>
            <CardHeader className="flex flex-row items-center justify-between pb-2">
              <CardTitle className="text-sm font-medium text-muted-foreground">Pending Gate Passes</CardTitle>
              <ShieldAlert className="w-4 h-4 text-muted-foreground" />
            </CardHeader>
            <CardContent>
              <div className="text-2xl font-bold font-mono">{summary?.pendingGatePasses}</div>
              <p className="text-xs text-muted-foreground mt-1">Awaiting approval</p>
            </CardContent>
          </Card>
          <Card>
            <CardHeader className="flex flex-row items-center justify-between pb-2">
              <CardTitle className="text-sm font-medium text-muted-foreground">Today's Movements</CardTitle>
              <Activity className="w-4 h-4 text-muted-foreground" />
            </CardHeader>
            <CardContent>
              <div className="text-2xl font-bold font-mono">{summary?.todayMovements}</div>
              <p className="text-xs text-muted-foreground mt-1">Tracked events</p>
            </CardContent>
          </Card>
        </div>

        {/* SBU Breakdown */}
        {summary?.sbuBreakdown && summary.sbuBreakdown.length > 0 && (
          <div>
            <h2 className="text-sm font-semibold text-muted-foreground uppercase tracking-wider mb-3">SBU Asset Breakdown</h2>
            <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-5 gap-3">
              {summary.sbuBreakdown.map((sbu) => (
                <Link key={sbu.sbuId} href={`/sbus`}>
                  <div className="rounded-xl border bg-card p-4 hover:shadow-md transition-shadow cursor-pointer">
                    <div className="flex items-center justify-between mb-2">
                      <SbuBadge code={sbu.sbuCode} color={sbu.sbuColor} />
                      {sbu.missingAssets > 0 && (
                        <AlertCircle className="w-3.5 h-3.5 text-destructive" />
                      )}
                    </div>
                    <div className="text-2xl font-bold font-mono mt-1" style={{ color: sbu.sbuColor }}>
                      {sbu.totalAssets}
                    </div>
                    <div className="text-[10px] text-muted-foreground mt-0.5 truncate">{sbu.sbuName.split(" ")[0]}</div>
                    <div className="mt-2 flex gap-2 text-[10px] text-muted-foreground">
                      <span className="text-green-600 font-medium">{sbu.activeAssets} active</span>
                      {sbu.missingAssets > 0 && <span className="text-red-500 font-medium">{sbu.missingAssets} missing</span>}
                      {sbu.maintenanceAssets > 0 && <span className="text-amber-500 font-medium">{sbu.maintenanceAssets} repair</span>}
                    </div>
                  </div>
                </Link>
              ))}
            </div>
          </div>
        )}

        {/* Main grid: activity + gate activity + distribution */}
        <div className="grid grid-cols-1 xl:grid-cols-3 gap-6">
          {/* Recent Activity */}
          <Card className="xl:col-span-2">
            <CardHeader>
              <CardTitle className="text-lg">Recent Activity</CardTitle>
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

          {/* Right column: Gate Activity + Asset Distribution */}
          <div className="space-y-6">
            {/* Gate Activity */}
            <Card>
              <CardHeader>
                <CardTitle className="text-base flex items-center gap-2">
                  <Radio className="w-4 h-4 text-blue-500" />
                  Gate Activity
                </CardTitle>
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
                          <p className="text-muted-foreground font-mono truncate">{ev.locationName}</p>
                        </div>
                        <span className="text-muted-foreground font-mono flex-shrink-0">
                          {new Date(ev.timestamp).toLocaleTimeString("en-GB", { hour: "2-digit", minute: "2-digit" })}
                        </span>
                      </div>
                    ))}
                  </div>
                )}
              </CardContent>
            </Card>

            {/* Asset Distribution */}
            <Card>
              <CardHeader>
                <CardTitle className="text-base flex items-center gap-2">
                  <MapPin className="w-4 h-4" />
                  Asset Distribution
                </CardTitle>
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
          </div>
        </div>
      </div>
    </Layout>
  );
}
