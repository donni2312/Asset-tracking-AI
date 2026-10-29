import type { ReactNode } from "react";
import { groupNames, ROLE_NAME, type RoleGroup } from "@/lib/roles";
import { Lock, ShieldAlert } from "lucide-react";
import { buttonVariants } from "@/components/ui/button";
import { useCurrentUser, useHasRole } from "@/hooks/use-auth";
import { cn } from "@/lib/utils";

/**
 * Who may do what is the Permissions matrix (Phase 2), enforced by the API; `role` names one of the API's role
 * groups (lib/roles.ts). These helpers only decide what the UI offers.
 */
export { ROLE_NAME } from "@/lib/roles";

/** Whether the signed-in user may `action` ("record a movement"), and if not, why. */
export function usePermission(required: RoleGroup, action: string): { allowed: boolean; reason: string } {
  const user = useCurrentUser();
  const allowed = useHasRole(required);
  if (allowed) return { allowed, reason: "" };
  return {
    allowed,
    reason: user
      ? `${ROLE_NAME[user.role] ?? user.role} may not ${action}. It needs one of: ${groupNames(required)}.`
      : `Sign in to ${action}.`,
  };
}

/**
 * Stand-in for an action the user may not take: the same label with a lock, disabled, and the reason
 * in the tooltip (and for screen readers).
 */
export function LockedAction({
  children,
  reason,
  variant = "outline",
  size = "default",
  className,
}: {
  children: ReactNode;
  reason: string;
  variant?: "default" | "outline" | "ghost";
  size?: "default" | "sm" | "icon";
  className?: string;
}) {
  return (
    <span
      role="button"
      aria-disabled="true"
      tabIndex={0}
      title={reason}
      aria-label={typeof children === "string" ? `${children} — ${reason}` : reason}
      className={cn(buttonVariants({ variant, size }), "cursor-not-allowed gap-2 opacity-60 hover:bg-transparent", className)}
    >
      <Lock className="h-4 w-4" />
      {children}
    </span>
  );
}

/** Renders `children` when the user may act, otherwise a locked stand-in labelled `label`. */
export function Permitted({
  role,
  action,
  label,
  children,
  variant,
  size,
  className,
}: {
  role: RoleGroup;
  action: string;
  label: ReactNode;
  children: ReactNode;
  variant?: "default" | "outline" | "ghost";
  size?: "default" | "sm" | "icon";
  className?: string;
}) {
  const { allowed, reason } = usePermission(role, action);
  if (allowed) return <>{children}</>;
  return (
    <LockedAction reason={reason} variant={variant} size={size} className={className}>
      {label}
    </LockedAction>
  );
}

/** Banner for a page whose main action the user may not take (the page itself stays readable). */
export function PermissionNotice({ role, action }: { role: RoleGroup; action: string }) {
  const { allowed, reason } = usePermission(role, action);
  if (allowed) return null;
  return (
    <div className="flex items-start gap-2 rounded-md border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900 dark:border-amber-700/60 dark:bg-amber-950/40 dark:text-amber-200">
      <ShieldAlert className="mt-0.5 h-4 w-4 flex-shrink-0" />
      <span>
        {reason} You can still look around; changes are refused.
      </span>
    </div>
  );
}
