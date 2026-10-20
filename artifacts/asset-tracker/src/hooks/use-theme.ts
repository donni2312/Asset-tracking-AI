import { useSyncExternalStore } from "react";

/**
 * Colour theme of the dashboard. Stored in this browser only (there is no user
 * account in the MVP), so it is kept next to the operator name in localStorage.
 *
 * "system" follows the operating system's light/dark setting and keeps following
 * it while the app is open. The chosen theme is applied as a `dark` class on
 * <html>; `index.html` applies the stored value before first paint so a reload in
 * dark mode never flashes white.
 */
export const THEMES = ["light", "dark", "system"] as const;
export type Theme = (typeof THEMES)[number];

const STORAGE_KEY = "assetiq.theme";
const listeners = new Set<() => void>();

const isTheme = (v: unknown): v is Theme => THEMES.includes(v as Theme);

function read(): Theme {
  try {
    const stored = window.localStorage.getItem(STORAGE_KEY);
    return isTheme(stored) ? stored : "system";
  } catch {
    return "system";
  }
}

const systemQuery = () => window.matchMedia("(prefers-color-scheme: dark)");

/** The theme actually on screen: "system" resolved against the OS setting. */
export function resolveTheme(theme: Theme): "light" | "dark" {
  if (theme !== "system") return theme;
  try {
    return systemQuery().matches ? "dark" : "light";
  } catch {
    return "light";
  }
}

export function applyTheme(theme: Theme) {
  document.documentElement.classList.toggle("dark", resolveTheme(theme) === "dark");
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  // Another tab changed the preference.
  const onStorage = (e: StorageEvent) => {
    if (e.key === STORAGE_KEY) listener();
  };
  window.addEventListener("storage", onStorage);
  // The OS flipped light/dark while we are following it.
  const media = systemQuery();
  const onSystemChange = () => {
    if (read() === "system") {
      applyTheme("system");
      listener();
    }
  };
  media.addEventListener("change", onSystemChange);
  return () => {
    listeners.delete(listener);
    window.removeEventListener("storage", onStorage);
    media.removeEventListener("change", onSystemChange);
  };
}

export function setTheme(theme: Theme) {
  try {
    window.localStorage.setItem(STORAGE_KEY, theme);
  } catch {
    // storage unavailable (private mode) - the choice applies but isn't remembered
  }
  applyTheme(theme);
  listeners.forEach((l) => l());
}

/** The stored preference ("system" by default) and a setter. */
export function useTheme(): [Theme, (theme: Theme) => void] {
  const theme = useSyncExternalStore(subscribe, read, () => "system" as Theme);
  return [theme, setTheme];
}

/** What is on screen right now — use this to label the toggle, not the preference. */
export function useResolvedTheme(): "light" | "dark" {
  const [theme] = useTheme();
  return resolveTheme(theme);
}
