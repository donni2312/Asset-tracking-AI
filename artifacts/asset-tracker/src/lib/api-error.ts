/** Best human-readable message from a failed API call (the API returns `{ error: string }`). */
export function apiErrorMessage(err: unknown, fallback = "Something went wrong"): string {
  const data = (err as { data?: unknown } | null)?.data;
  if (data && typeof data === "object" && "error" in data) {
    const message = (data as { error?: unknown }).error;
    if (typeof message === "string" && message.trim()) {
      // Validation errors arrive as serialised Zod issues - show the first message only.
      if (message.trim().startsWith("[")) {
        try {
          const issues = JSON.parse(message) as Array<{ message?: string; path?: unknown[] }>;
          const first = issues[0];
          if (first?.message) return `${first.path?.join(".") ?? "input"}: ${first.message}`;
        } catch {
          // fall through to the raw message
        }
      }
      return message;
    }
  }
  return err instanceof Error && err.message ? err.message : fallback;
}
