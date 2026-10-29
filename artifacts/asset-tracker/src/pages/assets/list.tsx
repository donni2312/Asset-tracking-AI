import { Layout } from "@/components/layout";
import { Card, CardContent } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Badge } from "@/components/ui/badge";
import { useListAssets, useListSbus, ListAssetsStatus, type ListAssetsStatus as AssetStatus } from "@workspace/api-client-react";
import { Link, useSearch } from "wouter";
import { Box, Plus, Search, X } from "lucide-react";
import { useState } from "react";
import { formatCurrency } from "@/lib/utils";
import { cn } from "@/lib/utils";
import { ExportCsvButton } from "@/components/export-button";
import { Permitted } from "@/components/permission";

function SbuFilterTab({
  label, color, active, onClick,
}: { label: string; color?: string; active: boolean; onClick: () => void }) {
  return (
    <button
      onClick={onClick}
      className={cn(
        "px-3 py-1.5 rounded-md text-xs font-medium border transition-all",
        active
          ? "text-white border-transparent"
          : "bg-transparent hover:bg-muted"
      )}
      style={active && color ? { backgroundColor: color, borderColor: color } : {}}
    >
      {label}
    </button>
  );
}

function StatusBadge({ status }: { status: string }) {
  switch (status) {
    case "active":      return <Badge className="bg-green-100 dark:bg-green-950/50 text-green-700 dark:text-green-300 border-green-200 dark:border-green-900 border text-[10px]">Active</Badge>;
    case "maintenance": return <Badge className="bg-amber-100 dark:bg-amber-950/50 text-amber-700 dark:text-amber-300 border-amber-200 dark:border-amber-900 border text-[10px]">Maintenance</Badge>;
    case "missing":     return <Badge className="bg-red-100 dark:bg-red-950/50 text-red-700 dark:text-red-300 border-red-200 dark:border-red-900 border text-[10px]">Missing</Badge>;
    case "retired":     return <Badge variant="secondary" className="text-[10px]">Retired</Badge>;
    default:            return <Badge variant="outline" className="text-[10px]">{status}</Badge>;
  }
}

export default function AssetList() {
  const searchStr = useSearch();
  const params = new URLSearchParams(searchStr);
  const initialSbuId = params.get("sbuId") ? parseInt(params.get("sbuId")!) : null;
  const statusParam = params.get("status");
  const initialStatus = statusParam && statusParam in ListAssetsStatus ? (statusParam as AssetStatus) : null;

  const [search, setSearch] = useState("");
  const [sbuFilter, setSbuFilter] = useState<number | null>(initialSbuId);
  const [statusFilter, setStatusFilter] = useState<AssetStatus | null>(initialStatus);

  const { data: sbus } = useListSbus();
  const { data: assets, isLoading } = useListAssets({
    q: search || undefined,
    sbuId: sbuFilter ?? undefined,
    status: statusFilter ?? undefined,
  } as any);

  const activeSbu = sbus?.find(s => s.id === sbuFilter);

  return (
    <Layout>
      <div className="space-y-5">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
          <div>
            <h1 className="text-2xl font-bold tracking-tight">Asset Registry</h1>
            {sbuFilter && activeSbu && (
              <p className="text-muted-foreground text-sm">Showing {activeSbu.name} assets</p>
            )}
            {statusFilter && (
              <button
                onClick={() => setStatusFilter(null)}
                className="mt-1 inline-flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground"
              >
                Filtered to <span className="font-medium capitalize text-foreground">{statusFilter}</span> assets
                <X className="w-3.5 h-3.5" />
              </button>
            )}
          </div>
          <div className="flex items-center gap-2">
            <ExportCsvButton
              request={{ report: "assets", params: { q: search || undefined, sbuId: sbuFilter ?? undefined, status: statusFilter ?? undefined } }}
            />
            <Permitted role="registrars" action="register assets" label="Register Asset" variant="default">
              <Link href="/assets/new" className="inline-flex items-center justify-center whitespace-nowrap rounded-md text-sm font-medium h-9 px-4 py-2 bg-primary text-primary-foreground shadow hover:bg-primary/90">
                <Plus className="w-4 h-4 mr-2" />Register Asset
              </Link>
            </Permitted>
          </div>
        </div>

        {/* SBU Filter Tabs */}
        <div className="flex flex-wrap gap-2">
          <SbuFilterTab label="All SBUs" active={sbuFilter === null} onClick={() => setSbuFilter(null)} />
          {sbus?.map(sbu => (
            <SbuFilterTab
              key={sbu.id}
              label={sbu.code}
              color={sbu.color}
              active={sbuFilter === sbu.id}
              onClick={() => setSbuFilter(sbuFilter === sbu.id ? null : sbu.id)}
            />
          ))}
        </div>

        <Card>
          <div className="p-4 border-b">
            <div className="relative max-w-sm">
              <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
              <Input
                type="search"
                placeholder="Search name, serial, RFID tag..."
                className="pl-9"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
            </div>
          </div>
          <CardContent className="p-0">
            {isLoading ? (
              <div className="p-8 text-center text-muted-foreground animate-pulse">Loading assets...</div>
            ) : assets?.length === 0 ? (
              <div className="p-16 text-center">
                <Box className="w-12 h-12 text-muted-foreground/30 mx-auto mb-4" />
                <h3 className="text-lg font-medium">No assets found</h3>
                <p className="text-sm text-muted-foreground">Adjust your search or register a new asset.</p>
              </div>
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Asset</TableHead>
                    <TableHead>SBU</TableHead>
                    <TableHead>Serial / RFID</TableHead>
                    <TableHead>Location</TableHead>
                    <TableHead>Status</TableHead>
                    <TableHead className="text-right">Value (USD)</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {assets?.map((asset) => (
                    <TableRow key={asset.id} className={asset.status === "missing" ? "bg-red-50/50 dark:bg-red-950/40" : ""}>
                      <TableCell>
                        <Link href={`/assets/${asset.id}`} className="font-medium hover:underline text-primary block">
                          {asset.name}
                        </Link>
                        <span className="text-xs text-muted-foreground">{asset.category}</span>
                      </TableCell>
                      <TableCell>
                        {asset.sbuCode ? (
                          <span
                            className="inline-flex items-center px-2 py-0.5 rounded border text-[10px] font-bold"
                            style={{
                              color: (asset as any).sbuColor ?? "#6B7280",
                              borderColor: (asset as any).sbuColor ?? "#6B7280",
                              backgroundColor: `${(asset as any).sbuColor ?? "#6B7280"}15`,
                            }}
                          >
                            {asset.sbuCode}
                          </span>
                        ) : <span className="text-muted-foreground text-xs">—</span>}
                      </TableCell>
                      <TableCell className="font-mono text-xs">
                        <div className="text-muted-foreground">SN: {asset.serialNumber}</div>
                        <div>TAG: {asset.rfidTag}</div>
                      </TableCell>
                      <TableCell>
                        {asset.currentLocationName
                          ? <span className="text-sm">{asset.currentLocationName}</span>
                          : <span className="text-sm text-muted-foreground italic">Off-premises / untracked</span>}
                      </TableCell>
                      <TableCell><StatusBadge status={asset.status} /></TableCell>
                      <TableCell className="text-right font-mono text-sm">
                        {formatCurrency(asset.purchaseValue)}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </CardContent>
        </Card>
      </div>
    </Layout>
  );
}
