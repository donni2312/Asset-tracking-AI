import { Layout } from "@/components/layout";
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import {
  useListRfidReaders, useSimulateRfidRead, useListAssets, useListSbus, type RFIDScanResult,
} from "@workspace/api-client-react";
import {
  Radio, ArrowRight, Activity, Zap, Building2, Clock, ShieldCheck, ShieldAlert, LogIn, AlertTriangle, Info, CheckCircle2,
} from "lucide-react";
import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Link } from "wouter";
import { useToast } from "@/hooks/use-toast";
import { apiErrorMessage } from "@/lib/api-error";
import { cn, formatDate } from "@/lib/utils";
import { PermissionNotice, usePermission } from "@/components/permission";

/** datetime-local value (browser time) → ISO string. */
const localInputToIso = (value: string) => new Date(value).toISOString();

/** "Last night 02:00" in the browser's timezone, as a datetime-local value. */
function lastNight2am(): string {
  const d = new Date();
  d.setHours(2, 0, 0, 0);
  if (d.getTime() > Date.now()) d.setDate(d.getDate() - 1);
  const local = new Date(d.getTime() - d.getTimezoneOffset() * 60_000);
  return local.toISOString().slice(0, 16);
}

const OUTCOMES: Record<RFIDScanResult["gateCheck"]["outcome"], { title: string; tone: string; icon: typeof Radio }> = {
  not_gate: { title: "Internal scan — no gate check needed", tone: "border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-800/40 text-slate-700 dark:text-slate-300", icon: Radio },
  entry: { title: "Entry — asset returned on site", tone: "border-emerald-200 dark:border-emerald-900 bg-emerald-50 dark:bg-emerald-950/40 text-emerald-800 dark:text-emerald-200", icon: LogIn },
  authorized: { title: "Authorised exit — covered by an approved gate pass", tone: "border-emerald-200 dark:border-emerald-900 bg-emerald-50 dark:bg-emerald-950/40 text-emerald-800 dark:text-emerald-200", icon: ShieldCheck },
  pending_created: { title: "No approved gate pass — pending pass raised, approvers notified", tone: "border-amber-300 dark:border-amber-800 bg-amber-50 dark:bg-amber-950/40 text-amber-900 dark:text-amber-100", icon: ShieldAlert },
  pending_existing: { title: "No approved gate pass — still awaiting approval", tone: "border-amber-300 dark:border-amber-800 bg-amber-50 dark:bg-amber-950/40 text-amber-900 dark:text-amber-100", icon: ShieldAlert },
};

const SEVERITY_ICON = { critical: AlertTriangle, warning: AlertTriangle, info: Info } as const;
const SEVERITY_TONE = {
  critical: "border-destructive/40 bg-destructive/5 text-destructive",
  warning: "border-amber-400/50 bg-amber-50 dark:bg-amber-950/40 text-amber-900 dark:text-amber-100",
  info: "border-blue-300/50 dark:border-blue-800 bg-blue-50 dark:bg-blue-950/40 text-blue-900 dark:text-blue-100",
} as const;

export default function RfidSimulator() {
  const { data: readers, isLoading: loadingReaders } = useListRfidReaders();
  const { data: assets, isLoading: loadingAssets } = useListAssets({ status: "active" });
  const { data: sbus } = useListSbus();
  const simulate = useSimulateRfidRead();
  const { allowed: canScan, reason: scanBlockedReason } = usePermission("rfidSimulator", "run the RFID simulator");
  const qc = useQueryClient();
  const { toast } = useToast();

  const [selectedReader, setSelectedReader] = useState<number | null>(null);
  const [selectedTag, setSelectedTag] = useState<string | null>(null);
  const [lastResult, setLastResult] = useState<RFIDScanResult | null>(null);
  const [sbuFilter, setSbuFilter] = useState<number | null>(null);
  const [customTime, setCustomTime] = useState(false);
  const [scanTime, setScanTime] = useState("");

  const handleSimulate = () => {
    if (!selectedReader || !selectedTag) return;
    simulate.mutate(
      {
        data: {
          readerId: selectedReader,
          rfidTag: selectedTag,
          ...(customTime && scanTime ? { scannedAt: localInputToIso(scanTime) } : {}),
        },
      },
      {
        onSuccess: (result) => {
          setLastResult(result);
          qc.invalidateQueries();
          toast({ title: "RFID scan processed", description: OUTCOMES[result.gateCheck.outcome].title });
        },
        onError: (err) => {
          toast({ title: "Simulation failed", description: apiErrorMessage(err), variant: "destructive" });
        },
      },
    );
  };

  const sbuColorMap = Object.fromEntries((sbus ?? []).map((s) => [s.code, s.color]));

  // Group readers by SBU
  const filteredReaders = sbuFilter ? readers?.filter((r) => r.sbuId === sbuFilter) : readers;
  const readersBySbu: Record<string, typeof readers> = {};
  for (const r of filteredReaders ?? []) {
    const code = r.sbuCode ?? "Other";
    if (!readersBySbu[code]) readersBySbu[code] = [];
    readersBySbu[code]!.push(r);
  }

  const filteredAssets = sbuFilter ? assets?.filter((a) => a.sbuId === sbuFilter) : assets;
  const movement = lastResult?.movement;
  const gateCheck = lastResult?.gateCheck;
  const outcome = gateCheck ? OUTCOMES[gateCheck.outcome] : null;

  return (
    <Layout>
      <div className="space-y-6">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">IoT RFID Simulator</h1>
        </div>

        <PermissionNotice role="rfidSimulator" action="run the RFID simulator" />

        {/* SBU Filter */}
        <div className="flex flex-wrap gap-2">
          <button
            onClick={() => { setSbuFilter(null); setSelectedReader(null); setSelectedTag(null); }}
            className={cn("px-3 py-1.5 rounded-md text-xs font-medium border transition-all", !sbuFilter ? "bg-foreground text-background border-foreground" : "hover:bg-muted")}
          >All SBUs</button>
          {sbus?.map((sbu) => (
            <button
              key={sbu.id}
              onClick={() => { setSbuFilter(sbuFilter === sbu.id ? null : sbu.id); setSelectedReader(null); setSelectedTag(null); }}
              className={cn("px-3 py-1.5 rounded-md text-xs font-medium border transition-all text-white border-transparent")}
              style={{ backgroundColor: sbuFilter === sbu.id ? sbu.color : "transparent", borderColor: sbu.color, color: sbuFilter === sbu.id ? "white" : sbu.color }}
            >{sbu.code}</button>
          ))}
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-2 gap-8">
          {/* Control Panel */}
          <div className="space-y-6">
            <Card className="bg-slate-900 border-slate-800 text-slate-100 shadow-xl relative overflow-hidden">
              <div className="absolute top-0 left-0 w-full h-1 bg-gradient-to-r from-blue-600 via-cyan-400 to-blue-600" />
              <CardHeader>
                <CardTitle className="flex items-center gap-2 text-slate-100">
                  <Radio className="w-5 h-5 text-cyan-400" />
                  Control Panel
                </CardTitle>
                <CardDescription className="text-slate-400">
                  Select an asset tag and target reader to trigger a scan event.
                </CardDescription>
              </CardHeader>
              <CardContent className="space-y-6">

                {/* Asset tags */}
                <div className="space-y-3">
                  <h3 className="text-xs font-medium uppercase tracking-wider text-slate-400">1. Select Asset (RFID Tag)</h3>
                  <div className="grid grid-cols-2 gap-2 max-h-52 overflow-y-auto pr-1">
                    {loadingAssets ? (
                      <div className="col-span-2 text-center py-4 text-slate-500">Loading tags...</div>
                    ) : filteredAssets?.filter((a) => a.rfidTag).map((asset) => (
                      <button
                        key={asset.id}
                        onClick={() => setSelectedTag(asset.rfidTag ?? null)}
                        className={cn(
                          "text-left p-2.5 rounded-md border text-sm transition-all",
                          selectedTag === asset.rfidTag
                            ? "bg-cyan-900/30 border-cyan-400/50 text-cyan-100"
                            : "bg-slate-800 border-slate-700 text-slate-300 hover:border-slate-600"
                        )}
                      >
                        <div className="font-medium truncate text-xs">{asset.name}</div>
                        <div className="font-mono text-[10px] mt-0.5 opacity-60 truncate">{asset.rfidTag}</div>
                        <div className="text-[10px] mt-0.5 opacity-50 truncate">{asset.currentLocationName ?? "Off-premises"}</div>
                        {asset.sbuCode && (
                          <div
                            className="mt-1 inline-block text-[9px] font-bold px-1.5 py-0.5 rounded border"
                            style={{ color: sbuColorMap[asset.sbuCode] ?? "#9ca3af", borderColor: sbuColorMap[asset.sbuCode] ?? "#9ca3af" }}
                          >{asset.sbuCode}</div>
                        )}
                      </button>
                    ))}
                  </div>
                </div>

                {/* Readers grouped by SBU */}
                <div className="space-y-3">
                  <h3 className="text-xs font-medium uppercase tracking-wider text-slate-400">2. Select Target Reader</h3>
                  {loadingReaders ? (
                    <div className="text-center py-4 text-slate-500">Loading readers...</div>
                  ) : (
                    <div className="space-y-3 max-h-56 overflow-y-auto pr-1">
                      {Object.entries(readersBySbu).map(([sbuCode, sbuReaders]) => (
                        <div key={sbuCode}>
                          <div className="flex items-center gap-1.5 mb-1.5">
                            <Building2 className="w-3 h-3 text-slate-500" />
                            <span className="text-[10px] font-bold" style={{ color: sbuColorMap[sbuCode] ?? "#9ca3af" }}>{sbuCode}</span>
                          </div>
                          <div className="grid grid-cols-2 gap-2">
                            {sbuReaders?.map((reader) => (
                              <button
                                key={reader.id}
                                onClick={() => reader.status === "active" && setSelectedReader(reader.id)}
                                disabled={reader.status !== "active"}
                                className={cn(
                                  "text-left p-2.5 rounded-md border text-xs transition-all",
                                  reader.status !== "active" && "opacity-40 cursor-not-allowed",
                                  selectedReader === reader.id
                                    ? "bg-blue-900/30 border-blue-400/50 text-blue-100"
                                    : "bg-slate-800 border-slate-700 text-slate-300 hover:border-slate-600"
                                )}
                              >
                                <div className="font-medium flex items-center justify-between gap-1">
                                  <span className="truncate text-[11px]">{reader.name}</span>
                                  <div className={cn("w-2 h-2 rounded-full flex-shrink-0", reader.status === "active" ? "bg-emerald-400" : "bg-slate-600")} />
                                </div>
                                <div className="text-[10px] mt-0.5 opacity-60 truncate">{reader.locationName}</div>
                              </button>
                            ))}
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </div>

                {/* Optional scan time (out-of-hours demo) */}
                <div className="space-y-2">
                  <h3 className="text-xs font-medium uppercase tracking-wider text-slate-400">3. Scan Time (optional)</h3>
                  <label className="flex items-center gap-2 text-xs text-slate-300">
                    <input
                      type="checkbox"
                      checked={customTime}
                      onChange={(e) => {
                        setCustomTime(e.target.checked);
                        if (e.target.checked && !scanTime) setScanTime(lastNight2am());
                      }}
                      className="accent-cyan-400"
                    />
                    Replay the scan at a specific time instead of now
                  </label>
                  {customTime && (
                    <div className="flex flex-wrap items-center gap-2">
                      <input
                        type="datetime-local"
                        value={scanTime}
                        max={new Date(Date.now() - new Date().getTimezoneOffset() * 60_000).toISOString().slice(0, 16)}
                        onChange={(e) => setScanTime(e.target.value)}
                        className="rounded-md border border-slate-700 bg-slate-800 px-2 py-1.5 text-xs text-slate-100 [color-scheme:dark]"
                      />
                      <button
                        type="button"
                        onClick={() => setScanTime(lastNight2am())}
                        className="flex items-center gap-1 rounded-md border border-slate-700 px-2 py-1.5 text-[11px] text-slate-300 hover:border-slate-500"
                      >
                        <Clock className="w-3 h-3" /> Last night 02:00
                      </button>
                      <p className="w-full text-[10px] text-slate-500">
                        Scans outside Mon–Fri 07:00–18:00 (Harare time) trigger the out-of-hours rule.
                      </p>
                    </div>
                  )}
                </div>

                <Button
                  className={cn(
                    "w-full h-12 text-base font-bold tracking-wide transition-all shadow-lg",
                    selectedReader && selectedTag
                      ? "bg-cyan-500 hover:bg-cyan-400 text-slate-900"
                      : "bg-slate-800 text-slate-500 hover:bg-slate-800"
                  )}
                  disabled={!canScan || !selectedReader || !selectedTag || simulate.isPending || (customTime && !scanTime)}
                  title={scanBlockedReason || undefined}
                  onClick={handleSimulate}
                >
                  {simulate.isPending ? "SIMULATING..." : "TRIGGER SCAN"}
                  <Zap className="w-5 h-5 ml-2" />
                </Button>
              </CardContent>
            </Card>
          </div>

          {/* Event Result Panel */}
          <div>
            <h3 className="text-sm font-semibold text-muted-foreground uppercase tracking-wider mb-4 px-1">System Response</h3>
            {lastResult && movement && gateCheck && outcome ? (
              <div className="bg-background border rounded-lg p-6 space-y-5 shadow-sm">
                {/* Gate check */}
                <div className={cn("rounded-md border p-3 text-sm flex items-start gap-2", outcome.tone)}>
                  <outcome.icon className="w-4 h-4 mt-0.5 flex-shrink-0" />
                  <div className="space-y-0.5">
                    <div className="font-semibold">{outcome.title}</div>
                    {gateCheck.gatePass && (
                      <div className="text-xs">
                        Gate pass <Link href="/gate-passes" className="underline font-medium">#{gateCheck.gatePass.id}</Link>
                        {gateCheck.outcome === "authorized"
                          ? ` approved by ${gateCheck.gatePass.approvedBy ?? "—"} — exit recorded, asset now off-premises.`
                          : ` (${gateCheck.gatePass.source === "rfid_gate" ? "auto-raised" : "requested"}) is waiting for an approver.`}
                      </div>
                    )}
                  </div>
                </div>

                <div className="flex items-center justify-center py-5 bg-muted/50 rounded-lg border border-dashed">
                  <div className="text-center px-4 max-w-[40%]">
                    <div className="text-xs font-medium text-muted-foreground mb-1">From</div>
                    <div className="font-semibold text-sm">{movement.fromLocationName || "Off-premises"}</div>
                  </div>
                  <div className="px-4 text-primary">
                    <ArrowRight className="w-6 h-6" />
                  </div>
                  <div className="text-center px-4 max-w-[40%]">
                    <div className="text-xs font-medium text-muted-foreground mb-1">To</div>
                    <div className="font-semibold text-sm text-primary">{movement.toLocationName || "Off-premises"}</div>
                  </div>
                </div>

                <div className="grid grid-cols-2 gap-4 text-sm">
                  <div>
                    <span className="text-muted-foreground text-xs uppercase tracking-wider">Asset</span>
                    <div className="font-medium mt-0.5">{movement.assetName}</div>
                  </div>
                  <div>
                    <span className="text-muted-foreground text-xs uppercase tracking-wider">RFID Tag</span>
                    <div className="font-mono text-xs mt-0.5">{movement.rfidTag}</div>
                  </div>
                  <div>
                    <span className="text-muted-foreground text-xs uppercase tracking-wider">Recorded as</span>
                    <div className="font-mono bg-muted inline-block px-2 py-0.5 rounded text-xs mt-0.5">{movement.triggeredBy}</div>
                  </div>
                  <div>
                    <span className="text-muted-foreground text-xs uppercase tracking-wider">Scan time</span>
                    <div className="text-xs mt-0.5">{formatDate(movement.timestamp)}</div>
                  </div>
                  {movement.sbuName && (
                    <div>
                      <span className="text-muted-foreground text-xs uppercase tracking-wider">Reader SBU</span>
                      <div className="text-xs mt-0.5">{movement.sbuName}</div>
                    </div>
                  )}
                  <div>
                    <span className="text-muted-foreground text-xs uppercase tracking-wider">Movement ID</span>
                    <div className="font-mono text-xs mt-0.5">#{movement.id}</div>
                  </div>
                </div>

                {/* Anomaly engine */}
                <div className="space-y-2">
                  <div className="text-xs uppercase tracking-wider text-muted-foreground">Anomaly engine</div>
                  {lastResult.insights.length === 0 ? (
                    <div className="flex items-center gap-2 text-sm text-emerald-700 dark:text-emerald-300">
                      <CheckCircle2 className="w-4 h-4" /> No anomalies — nothing raised for this scan.
                    </div>
                  ) : (
                    lastResult.insights.map((insight) => {
                      const Icon = SEVERITY_ICON[insight.severity];
                      return (
                        <div key={insight.id} className={cn("rounded-md border p-2.5 text-xs flex items-start gap-2", SEVERITY_TONE[insight.severity])}>
                          <Icon className="w-3.5 h-3.5 mt-0.5 flex-shrink-0" />
                          <div>
                            <span className="font-semibold uppercase tracking-wide mr-1">{insight.severity}</span>
                            {insight.message}
                          </div>
                        </div>
                      );
                    })
                  )}
                  {lastResult.insights.length > 0 && (
                    <Link href="/insights" className="text-xs text-primary hover:underline">Open AI Insights →</Link>
                  )}
                </div>
              </div>
            ) : (
              <div className="border border-dashed rounded-lg h-72 flex flex-col items-center justify-center text-muted-foreground bg-muted/10">
                <Activity className="w-8 h-8 mb-3 opacity-20" />
                <p className="text-sm">Awaiting simulation event...</p>
                <p className="text-xs text-muted-foreground/60 mt-1">Select an asset and reader, then trigger a scan</p>
              </div>
            )}
          </div>
        </div>
      </div>
    </Layout>
  );
}
