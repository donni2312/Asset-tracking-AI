import { useEffect, useRef, useState } from "react";
import { CalendarDays } from "lucide-react";
import { Calendar } from "@/components/ui/calendar";
import { cn } from "@/lib/utils";
import { DATE_FORMATS, formatDateOnly, useDateFormat, type DateFormat } from "@/lib/date-format";

/** Pattern the user types, in the order they chose in Settings. */
function pattern(format: DateFormat): string {
  return format === "mm/dd/yyyy" ? "mm/dd/yyyy" : format === "yyyy-mm-dd" ? "yyyy-mm-dd" : "dd/mm/yyyy";
}

/** Typed text -> the `YYYY-MM-DD` the API stores, or null if it isn't a real date. */
export function parseDateInput(text: string, format: DateFormat): string | null {
  const digits = text.match(/\d+/g);
  if (!digits || digits.length !== 3) return null;
  const [a, b, c] = digits.map(Number);
  const [year, month, day] =
    format === "yyyy-mm-dd" ? [a, b, c] : format === "mm/dd/yyyy" ? [c, a, b] : [c, b, a];
  if (year < 1900 || year > 2999 || month < 1 || month > 12 || day < 1 || day > 31) return null;
  const iso = `${year}-${String(month).padStart(2, "0")}-${String(day).padStart(2, "0")}`;
  // Reject impossible days (31/02) — Date rolls them over instead of failing.
  const probe = new Date(`${iso}T00:00:00Z`);
  if (probe.getUTCMonth() + 1 !== month || probe.getUTCDate() !== day) return null;
  return iso;
}

interface DateFieldProps {
  id?: string;
  /** `YYYY-MM-DD`, or "" for empty. */
  value: string;
  onChange: (iso: string) => void;
  /** Earliest selectable day as `YYYY-MM-DD`. */
  min?: string;
  disabled?: boolean;
}

/**
 * Date input that reads and writes in the format chosen in Settings.
 *
 * A native <input type="date"> would show the operating system's order (often
 * mm/dd/yyyy) regardless of that setting, which is exactly the inconsistency this
 * avoids. The value handed to the API is still `YYYY-MM-DD`.
 */
export function DateField({ id, value, onChange, min, disabled = false }: DateFieldProps) {
  const [format] = useDateFormat();
  const containerRef = useRef<HTMLDivElement>(null);
  const [text, setText] = useState(() => (value ? formatDateOnly(value, format) : ""));
  const [open, setOpen] = useState(false);

  // Follow the value when it is set elsewhere (form reset, calendar pick) and
  // when the preferred format changes.
  useEffect(() => {
    setText(value ? formatDateOnly(value, format) : "");
  }, [value, format]);

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (e: MouseEvent) => {
      if (!containerRef.current?.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", onPointerDown);
    return () => document.removeEventListener("mousedown", onPointerDown);
  }, [open]);

  const invalid = text.trim() !== "" && parseDateInput(text, format) === null;
  const selected = value ? new Date(`${value}T00:00:00`) : undefined;

  return (
    <div ref={containerRef} className="relative">
      <div className="relative">
        <input
          id={id}
          type="text"
          inputMode="numeric"
          autoComplete="off"
          disabled={disabled}
          value={text}
          placeholder={pattern(format)}
          aria-invalid={invalid}
          onChange={(e) => {
            setText(e.target.value);
            const iso = parseDateInput(e.target.value, format);
            if (iso) onChange(iso);
            else if (e.target.value.trim() === "") onChange("");
          }}
          className={cn(
            "flex h-9 w-full rounded-md border bg-transparent px-3 pr-9 py-1 text-sm shadow-sm transition-colors",
            "placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring",
            "disabled:cursor-not-allowed disabled:opacity-50",
            invalid ? "border-destructive" : "border-input",
          )}
        />
        <button
          type="button"
          tabIndex={-1}
          disabled={disabled}
          onClick={() => setOpen((o) => !o)}
          className="absolute right-1.5 top-1/2 -translate-y-1/2 rounded p-1 text-muted-foreground hover:text-foreground"
          aria-label={open ? "Close calendar" : "Pick a date"}
        >
          <CalendarDays className="h-4 w-4" />
        </button>
      </div>

      {invalid && (
        <p className="mt-1 text-[11px] text-destructive">
          Enter the date as {pattern(format)} (change the order in Settings).
        </p>
      )}

      {open && (
        <div className="absolute z-50 mt-1 rounded-md border bg-popover p-1 text-popover-foreground shadow-md">
          <Calendar
            mode="single"
            selected={selected}
            defaultMonth={selected}
            disabled={min ? { before: new Date(`${min}T00:00:00`) } : undefined}
            onSelect={(day) => {
              if (!day) return;
              const iso = `${day.getFullYear()}-${String(day.getMonth() + 1).padStart(2, "0")}-${String(day.getDate()).padStart(2, "0")}`;
              onChange(iso);
              setOpen(false);
            }}
          />
        </div>
      )}
    </div>
  );
}

/** "dd/mm/yyyy" etc., for a hint line next to the field. */
export function useDatePattern(): string {
  const [format] = useDateFormat();
  return pattern(format);
}

export { DATE_FORMATS };
