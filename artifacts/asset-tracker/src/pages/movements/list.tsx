import { Layout } from "@/components/layout";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Badge } from "@/components/ui/badge";
import { useListMovements } from "@workspace/api-client-react";
import { formatDate } from "@/lib/utils";
import { Activity, ArrowRight } from "lucide-react";
import { Link } from "wouter";

export default function MovementList() {
  const { data: movements, isLoading } = useListMovements({ limit: 100 });

  return (
    <Layout>
      <div className="space-y-6">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Movement Log</h1>
          <p className="text-muted-foreground text-sm">Real-time log of all asset movements across facilities.</p>
        </div>

        <Card>
          <CardContent className="p-0">
            {isLoading ? (
              <div className="p-8 text-center text-muted-foreground animate-pulse">Loading movements...</div>
            ) : movements?.length === 0 ? (
              <div className="p-16 text-center">
                <Activity className="w-12 h-12 text-muted-foreground/30 mx-auto mb-4" />
                <h3 className="text-lg font-medium">No movements recorded</h3>
              </div>
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Timestamp</TableHead>
                    <TableHead>Asset</TableHead>
                    <TableHead>Movement Route</TableHead>
                    <TableHead>Trigger</TableHead>
                    <TableHead>Actor</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {movements?.map((event) => (
                    <TableRow key={event.id}>
                      <TableCell className="font-mono text-xs whitespace-nowrap">
                        {formatDate(event.timestamp)}
                      </TableCell>
                      <TableCell>
                        <Link href={`/assets/${event.assetId}`} className="font-medium hover:underline text-primary">
                          {event.assetName}
                        </Link>
                        {event.rfidTag && <div className="text-[10px] text-muted-foreground font-mono mt-0.5">{event.rfidTag}</div>}
                      </TableCell>
                      <TableCell>
                        <div className="flex items-center gap-2 text-sm">
                          <span className="text-muted-foreground max-w-[120px] truncate" title={event.fromLocationName || 'Unknown'}>
                            {event.fromLocationName || 'Unknown'}
                          </span>
                          <ArrowRight className="w-3 h-3 text-muted-foreground shrink-0" />
                          <span className="font-medium max-w-[120px] truncate" title={event.toLocationName || 'Unknown'}>
                            {event.toLocationName || 'Unknown'}
                          </span>
                        </div>
                      </TableCell>
                      <TableCell>
                        <Badge variant="outline" className="text-[10px] uppercase font-mono tracking-wider">
                          {event.triggeredBy}
                        </Badge>
                      </TableCell>
                      <TableCell className="text-sm text-muted-foreground">
                        {event.movedBy || (event.triggeredBy === 'rfid' ? 'System' : 'Unknown')}
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
