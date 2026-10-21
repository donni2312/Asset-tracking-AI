import { useState } from "react";
import { useLocation } from "wouter";
import { useQueryClient } from "@tanstack/react-query";
import { useCreateLocation, type LocationInput } from "@workspace/api-client-react";
import { Layout } from "@/components/layout";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { LocationForm, EMPTY_LOCATION, type LocationFormValues } from "@/components/location-form";
import { useToast } from "@/hooks/use-toast";
import { apiErrorMessage } from "@/lib/api-error";
import { PermissionNotice } from "@/components/permission";

export default function NewLocation() {
  const [, navigate] = useLocation();
  const qc = useQueryClient();
  const { toast } = useToast();
  const createLocation = useCreateLocation();
  const [error, setError] = useState<string | null>(null);

  function submit(values: LocationFormValues) {
    setError(null);
    const data: LocationInput = {
      name: values.name.trim(),
      type: values.type as LocationInput["type"],
      building: values.building.trim(),
      floor: values.floor.trim() || undefined,
      description: values.description.trim() || undefined,
      sbuId: values.sbuId ? Number(values.sbuId) : undefined,
    };
    createLocation.mutate(
      { data },
      {
        onSuccess: (loc) => {
          toast({ title: "Location created", description: `${loc.name} is ready for assets and RFID readers.` });
          qc.invalidateQueries();
          navigate("/locations");
        },
        onError: (err) => setError(apiErrorMessage(err, "Could not create the location")),
      },
    );
  }

  return (
    <Layout>
      <div className="max-w-2xl mx-auto space-y-6">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Add Location</h1>
          <p className="text-muted-foreground text-sm">Register an office, gate, warehouse or other facility.</p>
        </div>
        <PermissionNotice role="operator" action="add locations" />
        <Card>
          <CardHeader>
            <CardTitle>Location Details</CardTitle>
          </CardHeader>
          <CardContent>
            <LocationForm
              initial={EMPTY_LOCATION}
              submitLabel="Create Location"
              submitting={createLocation.isPending}
              error={error}
              onSubmit={submit}
              onCancel={() => navigate("/locations")}
            />
          </CardContent>
        </Card>
      </div>
    </Layout>
  );
}
