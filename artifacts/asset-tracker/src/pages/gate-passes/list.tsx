import { Layout } from "@/components/layout";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { useListGatePasses, useApproveGatePass, useRejectGatePass } from "@workspace/api-client-react";
import { formatDate } from "@/lib/utils";
import { ShieldAlert, CheckCircle, XCircle } from "lucide-react";
import { useState } from "react";
import { useToast } from "@/hooks/use-toast";
import { Link } from "wouter";

export default function GatePassList() {
  const [filter, setFilter] = useState<'pending' | 'approved' | 'rejected'>('pending');
  const { data: passes, isLoading, refetch } = useListGatePasses({ status: filter });
  const approvePass = useApproveGatePass();
  const rejectPass = useRejectGatePass();
  const { toast } = useToast();

  const handleAction = (id: number, action: 'approve' | 'reject') => {
    const fn = action === 'approve' ? approvePass : rejectPass;
    fn.mutate(
      { id, data: { decidedBy: "Admin User", note: "Processed via dashboard" } },
      {
        onSuccess: () => {
          toast({ title: `Gate pass ${action}d successfully` });
          refetch();
        },
        onError: () => {
          toast({ title: "Action failed", variant: "destructive" });
        }
      }
    );
  };

  return (
    <Layout>
      <div className="space-y-6">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
          <div>
            <h1 className="text-2xl font-bold tracking-tight">Gate Passes</h1>
            <p className="text-muted-foreground text-sm">Manage authorization for assets leaving the premises.</p>
          </div>
          <div className="flex gap-2 bg-muted p-1 rounded-lg">
            {(['pending', 'approved', 'rejected'] as const).map(status => (
              <button
                key={status}
                onClick={() => setFilter(status)}
                className={`px-4 py-1.5 text-sm font-medium rounded-md capitalize transition-colors ${
                  filter === status ? 'bg-background shadow-sm text-foreground' : 'text-muted-foreground hover:text-foreground'
                }`}
              >
                {status}
              </button>
            ))}
          </div>
        </div>

        <Card>
          <CardContent className="p-0">
            {isLoading ? (
              <div className="p-8 text-center text-muted-foreground animate-pulse">Loading gate passes...</div>
            ) : passes?.length === 0 ? (
              <div className="p-16 text-center">
                <ShieldAlert className="w-12 h-12 text-muted-foreground/30 mx-auto mb-4" />
                <h3 className="text-lg font-medium">No {filter} gate passes</h3>
              </div>
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Asset</TableHead>
                    <TableHead>Requested By</TableHead>
                    <TableHead>Destination & Reason</TableHead>
                    <TableHead>Return Date</TableHead>
                    <TableHead className="text-right">Actions</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {passes?.map((gp) => (
                    <TableRow key={gp.id}>
                      <TableCell>
                        <Link href={`/assets/${gp.assetId}`} className="font-medium hover:underline text-primary">
                          {gp.assetName}
                        </Link>
                        <div className="text-xs text-muted-foreground mt-0.5">Created: {formatDate(gp.createdAt)}</div>
                      </TableCell>
                      <TableCell className="font-medium text-sm">
                        {gp.requestedBy}
                      </TableCell>
                      <TableCell>
                        <div className="text-sm font-medium">{gp.destination || 'Unspecified'}</div>
                        <div className="text-xs text-muted-foreground max-w-[200px] truncate" title={gp.reason}>{gp.reason}</div>
                      </TableCell>
                      <TableCell className="text-sm font-mono">
                        {gp.returnDate ? formatDate(gp.returnDate) : 'Indefinite'}
                      </TableCell>
                      <TableCell className="text-right">
                        {filter === 'pending' ? (
                          <div className="flex justify-end gap-2">
                            <Button 
                              size="sm" 
                              variant="outline" 
                              className="h-8 text-destructive hover:text-destructive hover:bg-destructive/10"
                              onClick={() => handleAction(gp.id, 'reject')}
                              disabled={approvePass.isPending || rejectPass.isPending}
                            >
                              <XCircle className="w-4 h-4 mr-1" /> Reject
                            </Button>
                            <Button 
                              size="sm" 
                              className="h-8 bg-emerald-600 hover:bg-emerald-700"
                              onClick={() => handleAction(gp.id, 'approve')}
                              disabled={approvePass.isPending || rejectPass.isPending}
                            >
                              <CheckCircle className="w-4 h-4 mr-1" /> Approve
                            </Button>
                          </div>
                        ) : (
                          <Badge variant={filter === 'approved' ? 'success' : 'destructive'} className="uppercase text-[10px]">
                            {gp.status}
                          </Badge>
                        )}
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
