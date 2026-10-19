import { useEffect } from "react";
import { useLocation } from "wouter";
import { getListGatePassesQueryKey, useListGatePasses } from "@workspace/api-client-react";
import { ToastAction } from "@/components/ui/toast";
import { useToast } from "@/hooks/use-toast";

/**
 * In-app gate-pass notifications (MVP decision: in-app only, no email/SMS).
 * Pending passes are polled every 10 s; a pass that appears while the
 * dashboard is open raises a toast with a "Review" action, and the sidebar
 * shows the number awaiting approval.
 */

const POLL_MS = 10_000;
const PENDING = { status: "pending" as const };

// Module-level so it survives page navigation (each page renders its own Layout).
let seenPendingIds: Set<number> | null = null;

/** Mark a pass as already known, e.g. one the current user just requested themselves. */
export function acknowledgeGatePass(id: number) {
  (seenPendingIds ??= new Set()).add(id);
}

export function usePendingGatePassNotifications(): number {
  const { toast } = useToast();
  const [, navigate] = useLocation();
  const { data } = useListGatePasses(PENDING, {
    query: { queryKey: getListGatePassesQueryKey(PENDING), refetchInterval: POLL_MS },
  });

  useEffect(() => {
    if (!data) return;
    if (seenPendingIds === null) {
      // First load: everything already pending is "known"; only new arrivals notify.
      seenPendingIds = new Set(data.map((p) => p.id));
      return;
    }
    const known = seenPendingIds;
    const fresh = data.filter((p) => !known.has(p.id));
    data.forEach((p) => known.add(p.id));
    if (fresh.length === 0) return;

    const first = fresh[0];
    toast({
      title: fresh.length > 1 ? `${fresh.length} gate passes awaiting approval` : `Gate pass #${first.id} awaiting approval`,
      description:
        first.source === "rfid_gate"
          ? `${first.assetName} was detected at ${first.gateLocationName ?? "a gate"} without an approved pass.`
          : `${first.assetName} — requested by ${first.requestedBy}.`,
      action: (
        <ToastAction altText="Review gate passes" onClick={() => navigate("/gate-passes")}>
          Review
        </ToastAction>
      ),
    });
  }, [data, toast, navigate]);

  return data?.length ?? 0;
}
