import { Layout } from "@/components/layout";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useCreateAsset, useListLocations } from "@workspace/api-client-react";
import { useLocation } from "wouter";
import { useState } from "react";
import { useToast } from "@/hooks/use-toast";
import { PermissionNotice, usePermission } from "@/components/permission";
import { apiErrorMessage } from "@/lib/api-error";

export default function NewAsset() {
  const [, setLocation] = useLocation();
  const { toast } = useToast();
  const createAsset = useCreateAsset();
  const { allowed: canRegister } = usePermission("operator", "register assets");
  const { data: locations } = useListLocations();

  const [formData, setFormData] = useState({
    name: "",
    category: "",
    serialNumber: "",
    rfidTag: "",
    status: "active" as any,
    purchaseValue: "",
    currentLocationId: "",
  });

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    createAsset.mutate({
      data: {
        ...formData,
        purchaseValue: formData.purchaseValue ? Number(formData.purchaseValue) : undefined,
        currentLocationId: formData.currentLocationId ? Number(formData.currentLocationId) : undefined,
      }
    }, {
      onSuccess: (asset) => {
        toast({ title: "Asset registered successfully" });
        setLocation(`/assets/${asset.id}`);
      },
      onError: (err) => {
        toast({ title: "Failed to register asset", description: apiErrorMessage(err), variant: "destructive" });
      }
    });
  };

  return (
    <Layout>
      <div className="max-w-2xl mx-auto space-y-6">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Register New Asset</h1>
          <p className="text-muted-foreground text-sm">Enter the details to add a new asset to the registry.</p>
        </div>

        <PermissionNotice role="operator" action="register assets" />

        <Card>
          <CardHeader>
            <CardTitle>Asset Details</CardTitle>
          </CardHeader>
          <CardContent>
            <form onSubmit={handleSubmit} className="space-y-4">
              <div className="grid grid-cols-2 gap-4">
                <div className="space-y-2">
                  <label className="text-sm font-medium">Name</label>
                  <Input required value={formData.name} onChange={e => setFormData({...formData, name: e.target.value})} placeholder="e.g. MacBook Pro M3" />
                </div>
                <div className="space-y-2">
                  <label className="text-sm font-medium">Category</label>
                  <Input required value={formData.category} onChange={e => setFormData({...formData, category: e.target.value})} placeholder="e.g. Laptop" />
                </div>
              </div>

              <div className="grid grid-cols-2 gap-4">
                <div className="space-y-2">
                  <label className="text-sm font-medium">Serial Number</label>
                  <Input required value={formData.serialNumber} onChange={e => setFormData({...formData, serialNumber: e.target.value})} placeholder="e.g. C02..."/>
                </div>
                <div className="space-y-2">
                  <label className="text-sm font-medium">RFID Tag</label>
                  <Input required value={formData.rfidTag} onChange={e => setFormData({...formData, rfidTag: e.target.value})} placeholder="e.g. EPC-..."/>
                </div>
              </div>

              <div className="grid grid-cols-2 gap-4">
                <div className="space-y-2">
                  <label className="text-sm font-medium">Initial Location</label>
                  <select 
                    className="flex h-9 w-full rounded-md border border-input bg-transparent px-3 py-1 text-sm shadow-sm transition-colors focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
                    value={formData.currentLocationId}
                    onChange={e => setFormData({...formData, currentLocationId: e.target.value})}
                  >
                    <option value="">Select location...</option>
                    {locations?.map(loc => (
                      <option key={loc.id} value={loc.id}>{loc.name}</option>
                    ))}
                  </select>
                </div>
                <div className="space-y-2">
                  <label className="text-sm font-medium">Purchase Value ($)</label>
                  <Input type="number" step="0.01" value={formData.purchaseValue} onChange={e => setFormData({...formData, purchaseValue: e.target.value})} placeholder="0.00"/>
                </div>
              </div>

              <div className="pt-4 flex justify-end gap-2">
                <Button type="button" variant="outline" onClick={() => setLocation('/assets')}>Cancel</Button>
                <Button type="submit" disabled={createAsset.isPending || !canRegister}>
                  {createAsset.isPending ? 'Registering...' : 'Register Asset'}
                </Button>
              </div>
            </form>
          </CardContent>
        </Card>
      </div>
    </Layout>
  );
}
