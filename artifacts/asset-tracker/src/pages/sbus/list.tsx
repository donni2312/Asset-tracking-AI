import { Layout } from "@/components/layout";
import { useListSbus } from "@workspace/api-client-react";
import { Building2, Box, MapPin, ArrowRight } from "lucide-react";
import { Link } from "wouter";

export default function SbuList() {
  const { data: sbus, isLoading } = useListSbus();

  return (
    <Layout>
      <div className="space-y-6">
        <div className="flex items-start justify-between">
          <div>
            <h1 className="text-2xl font-bold tracking-tight">Strategic Business Units</h1>
          </div>
        </div>

        {isLoading ? (
          <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-5">
            {[1, 2, 3, 4, 5].map(i => (
              <div key={i} className="h-48 bg-muted rounded-xl animate-pulse" />
            ))}
          </div>
        ) : (
          <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-5">
            {sbus?.map((sbu) => (
              <div
                key={sbu.id}
                className="rounded-xl border bg-card overflow-hidden shadow-sm hover:shadow-md transition-shadow"
              >
                {/* Color accent bar */}
                <div className="h-1.5 w-full" style={{ backgroundColor: sbu.color }} />

                <div className="p-5">
                  <div className="flex items-start justify-between mb-3">
                    <div
                      className="inline-flex items-center px-3 py-1 rounded-full text-sm font-bold border"
                      style={{ color: sbu.color, borderColor: sbu.color, backgroundColor: `${sbu.color}15` }}
                    >
                      {sbu.code}
                    </div>
                    <Building2 className="w-5 h-5 text-muted-foreground" />
                  </div>

                  <h2 className="font-semibold text-base leading-snug mb-1">{sbu.name}</h2>
                  {sbu.headOffice && (
                    <p className="text-xs text-muted-foreground mb-4 flex items-start gap-1">
                      <MapPin className="w-3 h-3 mt-0.5 flex-shrink-0" />
                      {sbu.headOffice}
                    </p>
                  )}

                  <div className="grid grid-cols-2 gap-3 mb-4">
                    <div className="rounded-lg bg-muted/60 p-3">
                      <div className="text-2xl font-bold font-mono" style={{ color: sbu.color }}>
                        {sbu.assetCount ?? 0}
                      </div>
                      <div className="text-xs text-muted-foreground mt-0.5 flex items-center gap-1">
                        <Box className="w-3 h-3" /> Assets
                      </div>
                    </div>
                    <div className="rounded-lg bg-muted/60 p-3">
                      <div className="text-2xl font-bold font-mono">{sbu.locationCount ?? 0}</div>
                      <div className="text-xs text-muted-foreground mt-0.5 flex items-center gap-1">
                        <MapPin className="w-3 h-3" /> Locations
                      </div>
                    </div>
                  </div>

                  <div className="flex gap-2">
                    <Link
                      href={`/assets?sbuId=${sbu.id}`}
                      className="flex-1 flex items-center justify-center gap-1.5 text-xs font-medium py-2 px-3 rounded-lg border hover:bg-muted transition-colors"
                    >
                      <Box className="w-3 h-3" /> Assets <ArrowRight className="w-3 h-3 ml-auto" />
                    </Link>
                    <Link
                      href={`/locations?sbuId=${sbu.id}`}
                      className="flex-1 flex items-center justify-center gap-1.5 text-xs font-medium py-2 px-3 rounded-lg border hover:bg-muted transition-colors"
                    >
                      <MapPin className="w-3 h-3" /> Locations <ArrowRight className="w-3 h-3 ml-auto" />
                    </Link>
                  </div>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </Layout>
  );
}
