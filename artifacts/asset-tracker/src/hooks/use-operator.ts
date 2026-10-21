import { useCurrentUser } from "@/hooks/use-auth";

/**
 * The person operating the dashboard: the signed-in user's name. It pre-fills the
 * "decided by" / "requested by" / "moved by" fields; empty when signed out (every
 * change needs a sign-in, so there is nothing to attribute then).
 */
export function useOperatorName(): string {
  return useCurrentUser()?.name ?? "";
}
