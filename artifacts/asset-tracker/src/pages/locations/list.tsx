import { Layout } from "@/components/layout";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { useListLocations, useListSbus } from "@workspace/api-client-react";
import { Building2, MapPin, Layers, Info, Radio, Plus, Pencil, Server, FlaskConical, Users, DoorOpen } from "lucide-react";
import { useState } from "react";
import { Link, useSearch } from "wouter";
import { cn } from "@/lib/utils";
import { LOCATION_TYPES, locationTypeLabel } from "@/lib/constants";
import { Permitted, usePermission } from "@/components/permission";

const typeIcons: Record<string, typeof Building2> = {
  gate: Radio,
  office: Building2,
  warehouse: Layers,
  meeting_room: Users,
  lab: FlaskConical,
  reception: DoorOpen,
  server_room: Server,
};

const typeColors: Record<string, string> = {
  gate: "text-blue-600 dark:text-blue-400 bg-blue-50 dark:bg-blue-950/40 border-blue-200 dark:border-blue-900",
  office: "text-gray-600 bg-gray-50 border-gray-200",
  warehouse: "text-amber-600 dark:text-amber-400 bg-amber-50 dark:bg-amber-950/40 border-amber-200 dark:border-amber-900",
  meeting_room: "text-purple-600 dark:text-purple-400 bg-purple-50 dark:bg-purple-950/40 border-purple-200 dark:border-purple-900",
  lab: "text-green-600 dark:text-green-400 bg-green-50 dark:bg-green-950/40 border-green-200 dark:border-green-900",
  reception: "text-pink-600 bg-pink-50 border-pink-200",
  server_room: "text-slate-600 dark:text-slate-400 bg-slate-50 dark:bg-slate-800/40 border-slate-200 dark:border-slate-700",
};

export default function LocationList() {
  const searchStr = useSearch();
  const params = new URLSearchParams(searchStr);
  const initialSbuId = params.get("sbuId") ? parseInt(params.get("sbuId")!) : null;

  const [sbuFilter, setSbuFilter] = useState<number | null>(initialSbuId);
  const [typeFilter, setTypeFilter] = useState<string | null>(null);

  const { data: sbus } = useListSbus();
  const { data: locations, isLoading } = useListLocations({
    sbuId: sbuFilter ?? undefined,
    type: typeFilter ?? undefined,
  } as any);

  const activeSbu = sbus?.find(s => s.id === sbuFilter);

  // Group locations by SBU when no filter
  const grouped: Record<string, typeof locations> = {};
  if (!sbuFilter && locations) {
    for (const loc of locations) {
      const key = loc.sbuCode ?? "Shared";
      if (!grouped[key]) grouped[key] = [];
      grouped[key]!.push(loc);
    }
  }

  return (
    <Layout>
      <div className="space-y-5">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
          <div>
            <h1 className="text-2xl font-bold tracking-tight">Facilities & Locations</h1>
            {activeSbu && (
              <p className="text-muted-foreground text-sm">{activeSbu.name} locations</p>
            )}
          </div>
          <Permitted role="operator" action="add locations" label="Add Location" variant="default">
            <Link href="/locations/new" className="inline-flex items-center justify-center whitespace-nowrap rounded-md text-sm font-medium h-9 px-4 py-2 bg-primary text-primary-foreground shadow hover:bg-primary/90">
              <Plus className="w-4 h-4 mr-2" />Add Location
            </Link>
          </Permitted>
        </div>

        {/* SBU filter */}
        <div className="flex flex-wrap gap-2">
          <button
            onClick={() => setSbuFilter(null)}
            className={cn("px-3 py-1.5 rounded-md text-xs font-medium border transition-all", sbuFilter === null ? "bg-foreground text-background border-foreground" : "hover:bg-muted")}
          >All SBUs</button>
          {sbus?.map(sbu => (
            <button
              key={sbu.id}
              onClick={() => setSbuFilter(sbuFilter === sbu.id ? null : sbu.id)}
              className={cn("px-3 py-1.5 rounded-md text-xs font-medium border transition-all", sbuFilter === sbu.id ? "text-white border-transparent" : "hover:bg-muted")}
              style={sbuFilter === sbu.id ? { backgroundColor: sbu.color, borderColor: sbu.color } : {}}
            >{sbu.code}</button>
          ))}
          <div className="w-px bg-border mx-1" />
          {LOCATION_TYPES.map(t => (
            <button
              key={t.value}
              onClick={() => setTypeFilter(typeFilter === t.value ? null : t.value)}
              className={cn("px-3 py-1.5 rounded-md text-xs font-medium border transition-all", typeFilter === t.value ? "bg-muted border-foreground" : "hover:bg-muted")}
            >{t.label}</button>
          ))}
        </div>

        {isLoading ? (
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
            {[1,2,3,4,5,6].map(i => <div key={i} className="h-36 bg-muted rounded-lg animate-pulse" />)}
          </div>
        ) : sbuFilter || typeFilter ? (
          // Flat grid when filtered
          <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-5">
            {locations?.map(loc => <LocationCard key={loc.id} loc={loc} />)}
          </div>
        ) : (
          // Grouped by SBU
          <div className="space-y-8">
            {Object.entries(grouped).map(([sbuCode, locs]) => {
              const sbu = sbus?.find(s => s.code === sbuCode);
              return (
                <div key={sbuCode}>
                  <div className="flex items-center gap-2 mb-4">
                    {sbu && (
                      <span
                        className="inline-flex items-center px-2.5 py-1 rounded-full text-xs font-bold border"
                        style={{ color: sbu.color, borderColor: sbu.color, backgroundColor: `${sbu.color}15` }}
                      >{sbu.code}</span>
                    )}
                    <h2 className="font-semibold text-sm text-muted-foreground">{sbu?.name ?? sbuCode}</h2>
                    <span className="text-xs text-muted-foreground">({locs?.length ?? 0} locations)</span>
                  </div>
                  <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
                    {locs?.map(loc => <LocationCard key={loc.id} loc={loc} />)}
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </div>
    </Layout>
  );
}

function LocationCard({ loc }: { loc: any }) {
  const Icon = typeIcons[loc.type] ?? MapPin;
  const colorClass = typeColors[loc.type] ?? "text-gray-600 bg-gray-50 border-gray-200";
  const { allowed: canEdit } = usePermission("operator", "edit locations");

  return (
    <Card className="hover:border-primary/40 transition-colors">
      <CardHeader className="pb-3">
        <div className="flex justify-between items-start gap-2">
          <CardTitle className="text-base leading-snug">{loc.name}</CardTitle>
          <div className="flex items-center gap-1 flex-shrink-0">
            <Badge className={cn("text-[10px] uppercase tracking-wide border", colorClass)}>
              <Icon className="w-3 h-3 mr-1" />{locationTypeLabel(loc.type)}
            </Badge>
            {canEdit && (
              <Link
                href={`/locations/${loc.id}/edit`}
                className="p-1.5 rounded-md text-muted-foreground hover:text-foreground hover:bg-muted"
                aria-label={`Edit ${loc.name}`}
                title="Edit location"
              >
                <Pencil className="w-3.5 h-3.5" />
              </Link>
            )}
          </div>
        </div>
        {loc.sbuCode && (
          <div className="mt-1">
            <span
              className="text-[10px] font-bold px-1.5 py-0.5 rounded border"
              style={{ color: "#6B7280", borderColor: "#6B7280", backgroundColor: "#6B728015" }}
            >{loc.sbuCode}</span>
          </div>
        )}
      </CardHeader>
      <CardContent>
        <div className="space-y-2 text-sm">
          <div className="flex items-center gap-2 text-muted-foreground">
            <Building2 className="w-3.5 h-3.5" /><span>{loc.building}</span>
          </div>
          {loc.floor && (
            <div className="flex items-center gap-2 text-muted-foreground">
              <Layers className="w-3.5 h-3.5" /><span>Floor {loc.floor}</span>
            </div>
          )}
          {loc.description && (
            <div className="flex items-start gap-2 text-muted-foreground">
              <Info className="w-3.5 h-3.5 mt-0.5 shrink-0" />
              <span className="line-clamp-2 text-xs">{loc.description}</span>
            </div>
          )}
          <div className="pt-3 mt-1 border-t flex justify-between items-center">
            <span className="text-xs text-muted-foreground uppercase tracking-wider">Assets</span>
            <span className="font-mono font-bold text-lg">{loc.assetCount || 0}</span>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}
