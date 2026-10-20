import { useEffect, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useLogin, type UserRole } from "@workspace/api-client-react";
import { LogIn, LogOut, ShieldCheck } from "lucide-react";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { apiErrorMessage } from "@/lib/api-error";
import { endSession, startSession, useCurrentUser } from "@/hooks/use-auth";

const ROLE_LABEL: Record<UserRole, string> = {
  viewer: "Viewer — read only",
  operator: "Operator — movements, gate passes, asset status",
  admin: "Administrator — full access",
};

export function SignInDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const qc = useQueryClient();
  const login = useLogin();
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setUsername("");
    setPassword("");
    setError(null);
  }, [open]);

  function submit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    login.mutate(
      { data: { username: username.trim(), password } },
      {
        onSuccess: (result) => {
          startSession(result);
          qc.invalidateQueries();
          onOpenChange(false);
        },
        // The API answers a wrong password and an unknown user identically;
        // don't invent a more specific message here either.
        onError: (err) => setError(apiErrorMessage(err, "Could not sign in")),
      },
    );
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-sm">
        <form onSubmit={submit}>
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <ShieldCheck className="h-5 w-5 text-primary" />
              Sign in
            </DialogTitle>
            <DialogDescription>
              Anyone can read the dashboard. Signing in is needed to change asset records.
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-4 py-4">
            <div className="space-y-1.5">
              <Label htmlFor="auth-username">Username</Label>
              <Input
                id="auth-username"
                autoFocus
                autoComplete="username"
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                required
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="auth-password">Password</Label>
              <Input
                id="auth-password"
                type="password"
                autoComplete="current-password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
              />
            </div>
            {error && (
              <p className="rounded-md border border-destructive/20 bg-destructive/10 p-2 text-sm text-destructive">
                {error}
              </p>
            )}
          </div>

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
            <Button type="submit" disabled={login.isPending || !username.trim() || !password}>
              {login.isPending ? "Signing in…" : "Sign in"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

/** Sidebar control: who is signed in, or a way to sign in. */
export function AccountBadge() {
  const user = useCurrentUser();
  const qc = useQueryClient();
  const [open, setOpen] = useState(false);

  if (!user) {
    return (
      <>
        <button
          type="button"
          onClick={() => setOpen(true)}
          className="flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-xs hover:bg-sidebar-accent"
        >
          <LogIn className="h-4 w-4 flex-shrink-0 text-sidebar-foreground/60" />
          <span className="min-w-0 flex-1">
            <span className="block text-[10px] uppercase tracking-wider text-sidebar-foreground/40">Not signed in</span>
            <span className="block truncate font-medium">Sign in to edit</span>
          </span>
        </button>
        <SignInDialog open={open} onOpenChange={setOpen} />
      </>
    );
  }

  return (
    <div className="flex items-center gap-1">
      <div className="flex min-w-0 flex-1 items-center gap-2 rounded-md px-2 py-1.5 text-xs">
        <ShieldCheck className="h-4 w-4 flex-shrink-0 text-emerald-400" />
        <span className="min-w-0 flex-1">
          <span className="block text-[10px] uppercase tracking-wider text-sidebar-foreground/40">
            {user.role}
          </span>
          <span className="block truncate font-medium">{user.name}</span>
        </span>
      </div>
      <button
        type="button"
        onClick={() => {
          endSession();
          qc.invalidateQueries();
        }}
        className="rounded-md p-1.5 hover:bg-sidebar-accent"
        title={`Sign out of ${user.username} (${ROLE_LABEL[user.role]})`}
        aria-label="Sign out"
      >
        <LogOut className="h-4 w-4 text-sidebar-foreground/70" />
      </button>
    </div>
  );
}
