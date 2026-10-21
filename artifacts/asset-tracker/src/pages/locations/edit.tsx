import { useMemo, useState } from "react";
import { useLocation, useParams } from "wouter";
import { useQueryClient } from "@tanstack/react-query";
import { getGetLocationQueryKey, useGetLocation, useUpdateLocation, type LocationUpdate } from "@workspace/api-client-react";
import { Layout } from "@/components/layout";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { LocationForm, type LocationFormValues } from "@/components/location-form";
import { useToast } from "@/hooks/use-toast";
import { apiErrorMessage } from "@/lib/api-error";
import { PermissionNotice } from "@/components/permission";
import type { LocationTypeValue } from "@/lib/constants";

export default function EditLocation() {
  const { id } = useParams();
  const locationId = Number(id);
  const [, navigate] = useLocation();
  const qc = useQueryClient();
  const { toast } = useToast();
  const updateLocation = useUpdateLocation();
  const [error, setError] = useState<string | null>(null);

  const { data: location, isLoading, isError } = useGetLocation(locationId, {
    query: { enabled: Number.isInteger(locationId) && locationId > 0, queryKey: getGetLocationQueryKey(locationId) },
  });

  const initial = useMemo<LocationFormValues | null>(
    () =>
      location
        ? {
            name: location.name,
            type: location.type as LocationTypeValue,
            building: location.building,
            floor: location.floor ?? "",
            description: location.description ?? "",
            sbuId: location.sbuId != null ? String(location.sbuId) : "",
          }
        : null,
    [location],
  );

  function submit(values: LocationFormValues) {
    setError(null);
    const data: LocationUpdate = {
      name: values.name.trim(),
      type: values.type as LocationUpdate["type"],
      building: values.building.trim(),
      floor: values.floor.trim() || null,
      description: values.description.trim() || null,
      sbuId: values.sbuId ? Number(values.sbuId) : null,
    };
    updateLocation.mutate(
      { id: locationId, data },
      {
        onSuccess: (loc) => {
          toast({ title: "Location updated", description: loc.name });
          qc.invalidateQueries();
          navigate("/locations");
        },
        onError: (err) => setError(apiErrorMessage(err, "Could not update the location")),
      },
    );
  }

  return (
    <Layout>
      <div className="max-w-2xl mx-auto space-y-6">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Edit Location</h1>
          <p className="text-muted-foreground text-sm">
            {location ? `${location.name} · ${location.assetCount ?? 0} assets currently here` : "Update facility details."}
          </p>
        </div>
        <PermissionNotice role="operator" action="edit locations" />
        <Card>
          <CardHeader>
            <CardTitle>Location Details</CardTitle>
          </CardHeader>
          <CardContent>
            {isLoading ? (
              <div className="h-64 bg-muted rounded animate-pulse" />
            ) : isError || !initial ? (
              <p className="text-sm text-muted-foreground">Location not found.</p>
            ) : (
              <LocationForm
                initial={initial}
                submitLabel="Save Changes"
                submitting={updateLocation.isPending}
                error={error}
                onSubmit={submit}
                onCancel={() => navigate("/locations")}
              />
            )}
          </CardContent>
        </Card>
      </div>
    </Layout>
  );
}
