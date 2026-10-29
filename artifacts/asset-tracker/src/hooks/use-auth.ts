import { useSyncExternalStore } from "react";
import { setAuthTokenGetter, type LoginResult, type User } from "@workspace/api-client-react";
import { inGroup, type RoleGroup } from "@/lib/roles";

/**
 * The signed-in user.
 *
 * The API issues a bearer token (POST /auth/login); it is kept in localStorage
 * and attached to every request by the generated client's token getter,
 * registered once below. Since Phase 2 every screen needs a sign-in (the API
 * refuses signed-out requests with 401).
 *
 * Nothing here is a security boundary: the role in this store only decides what
 * the UI offers. Every protected action is checked again by the API, so a user
 * who edits this value gets a 403 rather than an edit.
 */
const STORAGE_KEY = "assetiq.auth";
const listeners = new Set<() => void>();

export interface Session {
  token: string;
  /** ISO timestamp; the session is treated as ended once it passes. */
  expiresAt: string;
  user: User;
}

function readRaw(): Session | null {
  try {
    const stored = window.localStorage.getItem(STORAGE_KEY);
    if (!stored) return null;
    const session = JSON.parse(stored) as Session;
    if (!session?.token || !session.user) return null;
    if (new Date(session.expiresAt).getTime() <= Date.now()) return null;
    return session;
  } catch {
    return null;
  }
}

// Cached so useSyncExternalStore gets a stable reference between renders —
// parsing afresh each time would return a new object and loop.
let snapshot: Session | null = readRaw();
let snapshotRaw: string | null = null;

function read(): Session | null {
  let current: string | null = null;
  try {
    current = window.localStorage.getItem(STORAGE_KEY);
  } catch {
    current = null;
  }
  if (current !== snapshotRaw) {
    snapshotRaw = current;
    snapshot = readRaw();
  } else if (snapshot && new Date(snapshot.expiresAt).getTime() <= Date.now()) {
    snapshot = null;
  }
  return snapshot;
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  const onStorage = (e: StorageEvent) => {
    if (e.key === STORAGE_KEY) listener();
  };
  window.addEventListener("storage", onStorage);
  return () => {
    listeners.delete(listener);
    window.removeEventListener("storage", onStorage);
  };
}

function write(session: Session | null) {
  try {
    if (session) window.localStorage.setItem(STORAGE_KEY, JSON.stringify(session));
    else window.localStorage.removeItem(STORAGE_KEY);
  } catch {
    // storage unavailable (private mode) - the session lasts for this page only
  }
  snapshotRaw = session ? JSON.stringify(session) : null;
  snapshot = session;
  listeners.forEach((l) => l());
}

export function startSession(result: LoginResult) {
  write({ token: result.token, expiresAt: result.expiresAt, user: result.user });
}

export function endSession() {
  write(null);
}

/** Registered once: the generated client sends this as `Authorization: Bearer …`. */
setAuthTokenGetter(() => read()?.token ?? null);

/** The bearer token, for the few requests the generated client does not make (multipart uploads, file previews). */
export const currentToken = (): string | null => read()?.token ?? null;

export function useSession(): Session | null {
  return useSyncExternalStore(subscribe, read, () => null);
}

export function useCurrentUser(): User | null {
  return useSession()?.user ?? null;
}

/** Whether the signed-in user holds one of the roles of `group` (mirrors the API's role groups, lib/roles.ts). */
export function useHasRole(group: RoleGroup): boolean {
  return inGroup(useCurrentUser(), group);
}
