import { Layout } from "@/components/layout";
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { useListRfidReaders, useSimulateRfidRead, useListAssets, useListSbus } from "@workspace/api-client-react";
import { Radio, ArrowRight, Activity, Zap, Building2 } from "lucide-react";
import { useState } from "react";
import { useToast } from "@/hooks/use-toast";
import { cn } from "@/lib/utils";

export default function RfidSimulator() {
  const { data: readers, isLoading: loadingReaders } = useListRfidReaders();
  const { data: assets, isLoading: loadingAssets } = useListAssets({ status: "active" } as any);
  const { data: sbus } = useListSbus();
  const simulate = useSimulateRfidRead();
  const { toast } = useToast();

  const [selectedReader, setSelectedReader] = useState<number | null>(null);
  const [selectedTag, setSelectedTag] = useState<string | null>(null);
  const [lastEvent, setLastEvent] = useState<any>(null);
  const [sbuFilter, setSbuFilter] = useState<number | null>(null);

  const handleSimulate = () => {
    if (!selectedReader || !selectedTag) return;
    simulate.mutate(
      { data: { readerId: selectedReader, rfidTag: selectedTag } },
      {
        onSuccess: (event) => {
          toast({ title: "RFID scan simulated", description: "Movement event recorded in system." });
          setLastEvent(event);
          setSelectedReader(null);
          setSelectedTag(null);
        },
        onError: () => {
          toast({ title: "Simulation failed", variant: "destructive" });
        },
      }
    );
  };

  const sbuColorMap = Object.fromEntries((sbus ?? []).map(s => [s.code, s.color]));

  // Group readers by SBU
  const filteredReaders = sbuFilter ? readers?.filter(r => (r as any).sbuId === sbuFilter) : readers;
  const readersBySbu: Record<string, typeof readers> = {};
  for (const r of filteredReaders ?? []) {
    const code = (r as any).sbuCode ?? "Other";
    if (!readersBySbu[code]) readersBySbu[code] = [];
    readersBySbu[code]!.push(r);
  }

  const filteredAssets = sbuFilter ? assets?.filter(a => (a as any).sbuId === sbuFilter) : assets;

  return (
    <Layout>
      <div className="space-y-6">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">IoT RFID Simulator</h1>
          <p className="text-muted-foreground text-sm">
            Demonstrate hardware sensor integration across TSL Group gates and offices. Select an asset tag and a reader to simulate a real-time RFID scan.
          </p>
        </div>

        {/* SBU Filter */}
        <div className="flex flex-wrap gap-2">
          <button
            onClick={() => { setSbuFilter(null); setSelectedReader(null); setSelectedTag(null); }}
            className={cn("px-3 py-1.5 rounded-md text-xs font-medium border transition-all", !sbuFilter ? "bg-foreground text-background border-foreground" : "hover:bg-muted")}
          >All SBUs</button>
          {sbus?.map(sbu => (
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
                    ) : filteredAssets?.filter(a => a.rfidTag).map((asset) => (
                      <button
                        key={asset.id}
                        onClick={() => setSelectedTag(asset.rfidTag)}
                        className={cn(
                          "text-left p-2.5 rounded-md border text-sm transition-all",
                          selectedTag === asset.rfidTag
                            ? "bg-cyan-900/30 border-cyan-400/50 text-cyan-100"
                            : "bg-slate-800 border-slate-700 text-slate-300 hover:border-slate-600"
                        )}
                      >
                        <div className="font-medium truncate text-xs">{asset.name}</div>
                        <div className="font-mono text-[10px] mt-0.5 opacity-60 truncate">{asset.rfidTag}</div>
                        {(asset as any).sbuCode && (
                          <div
                            className="mt-1 inline-block text-[9px] font-bold px-1.5 py-0.5 rounded border"
                            style={{ color: sbuColorMap[(asset as any).sbuCode] ?? "#9ca3af", borderColor: sbuColorMap[(asset as any).sbuCode] ?? "#9ca3af" }}
                          >{(asset as any).sbuCode}</div>
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
                            <span
                              className="text-[10px] font-bold"
                              style={{ color: sbuColorMap[sbuCode] ?? "#9ca3af" }}
                            >{sbuCode}</span>
                          </div>
                          <div className="grid grid-cols-2 gap-2">
                            {sbuReaders?.map(reader => (
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

                <Button
                  className={cn(
                    "w-full h-12 text-base font-bold tracking-wide transition-all shadow-lg",
                    selectedReader && selectedTag
                      ? "bg-cyan-500 hover:bg-cyan-400 text-slate-900"
                      : "bg-slate-800 text-slate-500 hover:bg-slate-800"
                  )}
                  disabled={!selectedReader || !selectedTag || simulate.isPending}
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
            {lastEvent ? (
              <div className="bg-background border rounded-lg p-6 space-y-6 shadow-sm">
                <div className="flex items-center justify-center py-6 bg-muted/50 rounded-lg border border-dashed">
                  <div className="text-center px-4 max-w-[40%]">
                    <div className="text-xs font-medium text-muted-foreground mb-1">From</div>
                    <div className="font-semibold text-sm">{lastEvent.fromLocationName || "Unknown"}</div>
                  </div>
                  <div className="px-4 text-primary">
                    <ArrowRight className="w-6 h-6" />
                  </div>
                  <div className="text-center px-4 max-w-[40%]">
                    <div className="text-xs font-medium text-muted-foreground mb-1">To</div>
                    <div className="font-semibold text-sm text-primary">{lastEvent.toLocationName}</div>
                  </div>
                </div>

                <div className="grid grid-cols-2 gap-4 text-sm">
                  <div>
                    <span className="text-muted-foreground text-xs uppercase tracking-wider">Asset</span>
                    <div className="font-medium mt-0.5">{lastEvent.assetName}</div>
                  </div>
                  <div>
                    <span className="text-muted-foreground text-xs uppercase tracking-wider">RFID Tag</span>
                    <div className="font-mono text-xs mt-0.5">{lastEvent.rfidTag}</div>
                  </div>
                  <div>
                    <span className="text-muted-foreground text-xs uppercase tracking-wider">Trigger</span>
                    <div className="font-mono bg-muted inline-block px-2 py-0.5 rounded text-xs mt-0.5">{lastEvent.triggeredBy}</div>
                  </div>
                  {lastEvent.sbuName && (
                    <div>
                      <span className="text-muted-foreground text-xs uppercase tracking-wider">SBU</span>
                      <div className="text-xs mt-0.5">{lastEvent.sbuName}</div>
                    </div>
                  )}
                  <div className="col-span-2">
                    <span className="text-muted-foreground text-xs uppercase tracking-wider">Event ID</span>
                    <div className="font-mono text-xs mt-0.5">#{lastEvent.id}</div>
                  </div>
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
