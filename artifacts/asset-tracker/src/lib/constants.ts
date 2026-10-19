export const LOCATION_TYPES = [
  { value: "office", label: "Office" },
  { value: "gate", label: "Gate" },
  { value: "warehouse", label: "Warehouse" },
  { value: "meeting_room", label: "Meeting room" },
  { value: "lab", label: "Lab" },
  { value: "reception", label: "Reception" },
  { value: "server_room", label: "Server room" },
] as const;

export type LocationTypeValue = (typeof LOCATION_TYPES)[number]["value"];

export const locationTypeLabel = (type: string) =>
  LOCATION_TYPES.find((t) => t.value === type)?.label ?? type.replace(/_/g, " ");

/** Native <select> styled like the shadcn inputs (same as the Register Asset form). */
export const SELECT_CLASS =
  "flex h-9 w-full rounded-md border border-input bg-transparent px-3 py-1 text-sm shadow-sm transition-colors focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50";

/** Today's date as YYYY-MM-DD in the browser's timezone (for <input type="date" min>). */
export function todayInputValue(): string {
  const now = new Date();
  const local = new Date(now.getTime() - now.getTimezoneOffset() * 60_000);
  return local.toISOString().slice(0, 10);
}
