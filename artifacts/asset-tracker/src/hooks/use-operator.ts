import { useSyncExternalStore } from "react";

/**
 * The person operating the dashboard. There is no authentication in the MVP,
 * so decisions (gate-pass approvals, manual moves, requests) are attributed to
 * the name entered here instead of a hard-coded "Admin User". It is stored in
 * this browser only and pre-fills the "decided by" / "requested by" fields.
 */
const STORAGE_KEY = "assetiq.operatorName";
const listeners = new Set<() => void>();

function read(): string {
  try {
    return window.localStorage.getItem(STORAGE_KEY) ?? "";
  } catch {
    return "";
  }
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

export function setOperatorName(name: string) {
  try {
    window.localStorage.setItem(STORAGE_KEY, name.trim());
  } catch {
    // storage unavailable (private mode) - the name simply isn't remembered
  }
  listeners.forEach((l) => l());
}

export function useOperatorName(): [string, (name: string) => void] {
  const name = useSyncExternalStore(subscribe, read, () => "");
  return [name, setOperatorName];
}
