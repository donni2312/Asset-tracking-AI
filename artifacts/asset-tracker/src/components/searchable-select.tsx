import { useEffect, useId, useMemo, useRef, useState } from "react";
import { Check, ChevronsUpDown, Search, X } from "lucide-react";
import { cn } from "@/lib/utils";

export interface SearchableOption {
  /** Value handed back by onChange. */
  value: string;
  /** Main line, shown in the list and in the closed field. */
  label: string;
  /** Second line in the list (e.g. serial number, location). */
  hint?: string;
  /** Extra text the query is matched against (RFID tag, serial, SBU code…). */
  keywords?: string[];
  /** Small tag on the right of the row (e.g. SBU code, status). */
  badge?: string;
}

interface SearchableSelectProps {
  id?: string;
  value: string;
  onChange: (value: string) => void;
  options: SearchableOption[];
  placeholder?: string;
  /** Shown when the query matches nothing. */
  emptyMessage?: string;
  /** Accept a typed value that is not in the list (used for people's names). */
  allowCustom?: boolean;
  disabled?: boolean;
  required?: boolean;
  "aria-labelledby"?: string;
}

/** Every token of the query must appear somewhere in the option's text. */
function matches(option: SearchableOption, tokens: string[]) {
  if (tokens.length === 0) return true;
  const haystack = [option.label, option.hint, option.badge, ...(option.keywords ?? [])]
    .filter(Boolean)
    .join(" ")
    .toLowerCase();
  return tokens.every((t) => haystack.includes(t));
}

/**
 * Type-to-filter dropdown: the query is matched against the option's label and
 * its keywords, so an asset can be found by name, RFID tag, serial number or SBU
 * code, and a person by any part of their name. Long lists (every asset in the
 * registry) are unusable as a plain <select>.
 *
 * With `allowCustom` the typed text is itself the value — that is how "Requested
 * by" accepts a person who is not in the database yet while still offering
 * everyone who is.
 *
 * Deliberately not a Radix Popover: the list is rendered inline, so it cannot
 * fight the surrounding Dialog over focus.
 */
export function SearchableSelect({
  id,
  value,
  onChange,
  options,
  placeholder = "Search…",
  emptyMessage = "No match.",
  allowCustom = false,
  disabled = false,
  required = false,
  "aria-labelledby": ariaLabelledBy,
}: SearchableSelectProps) {
  const listId = useId();
  const containerRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [active, setActive] = useState(0);

  const selected = options.find((o) => o.value === value);
  const filtered = useMemo(() => {
    const tokens = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
    return options.filter((o) => matches(o, tokens));
  }, [options, query]);

  // What the field shows: the live query while open, otherwise the selection.
  const display = open ? query : selected?.label ?? (allowCustom ? value : "");

  // Closing always drops the query, so re-opening starts from the full list
  // rather than from whatever was half-typed last time.
  function close() {
    setQuery("");
    setOpen(false);
  }

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (e: MouseEvent) => {
      if (!containerRef.current?.contains(e.target as Node)) {
        setQuery("");
        setOpen(false);
      }
    };
    document.addEventListener("mousedown", onPointerDown);
    return () => document.removeEventListener("mousedown", onPointerDown);
  }, [open]);

  useEffect(() => {
    setActive(0);
  }, [query, open]);

  function choose(option: SearchableOption) {
    onChange(option.value);
    setQuery("");
    setOpen(false);
    inputRef.current?.focus();
  }

  function onKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === "ArrowDown" || e.key === "ArrowUp") {
      e.preventDefault();
      if (!open) {
        setOpen(true);
        return;
      }
      if (filtered.length === 0) return;
      const step = e.key === "ArrowDown" ? 1 : -1;
      setActive((i) => (i + step + filtered.length) % filtered.length);
      return;
    }
    if (e.key === "Enter") {
      if (open && filtered[active]) {
        e.preventDefault(); // don't submit the surrounding form
        choose(filtered[active]);
      }
      return;
    }
    if (e.key === "Escape") {
      if (open) {
        e.preventDefault();
        e.stopPropagation(); // keep the dialog open; only close the list
        close();
      }
      return;
    }
    if (e.key === "Tab") close();
  }

  return (
    <div ref={containerRef} className="relative">
      <div className="relative">
        <Search className="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" />
        <input
          id={id}
          ref={inputRef}
          type="text"
          role="combobox"
          aria-expanded={open}
          aria-controls={listId}
          aria-autocomplete="list"
          aria-labelledby={ariaLabelledBy}
          autoComplete="off"
          disabled={disabled}
          // Only a non-custom field can be "empty but required": the real value
          // lives in state, so the input mirrors it for native validation.
          required={required && !value}
          value={display}
          placeholder={placeholder}
          onChange={(e) => {
            setQuery(e.target.value);
            setOpen(true);
            if (allowCustom) onChange(e.target.value);
          }}
          onFocus={() => setOpen(true)}
          onKeyDown={onKeyDown}
          className={cn(
            "flex h-9 w-full rounded-md border border-input bg-transparent pl-8 pr-14 py-1 text-sm shadow-sm transition-colors",
            "placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring",
            "disabled:cursor-not-allowed disabled:opacity-50",
          )}
        />
        <div className="absolute right-1.5 top-1/2 flex -translate-y-1/2 items-center gap-0.5">
          {(value || query) && !disabled && (
            <button
              type="button"
              tabIndex={-1}
              onClick={() => {
                onChange("");
                setQuery("");
                setOpen(true);
                inputRef.current?.focus();
              }}
              className="rounded p-1 text-muted-foreground hover:text-foreground"
              aria-label="Clear"
            >
              <X className="h-3.5 w-3.5" />
            </button>
          )}
          <button
            type="button"
            tabIndex={-1}
            disabled={disabled}
            onClick={() => {
              setOpen((o) => !o);
              inputRef.current?.focus();
            }}
            className="rounded p-1 text-muted-foreground hover:text-foreground"
            aria-label={open ? "Close list" : "Open list"}
          >
            <ChevronsUpDown className="h-3.5 w-3.5" />
          </button>
        </div>
      </div>

      {open && (
        <ul
          id={listId}
          role="listbox"
          className="absolute z-50 mt-1 max-h-60 w-full overflow-y-auto rounded-md border bg-popover p-1 text-popover-foreground shadow-md"
        >
          {filtered.length === 0 ? (
            <li className="px-2 py-3 text-center text-xs text-muted-foreground">
              {allowCustom && query.trim()
                ? <>Not in the list — “{query.trim()}” will be used as typed.</>
                : emptyMessage}
            </li>
          ) : (
            filtered.map((option, i) => {
              const isSelected = option.value === value;
              return (
                <li key={option.value} role="option" aria-selected={isSelected}>
                  <button
                    type="button"
                    // mousedown would blur the input and close the list first
                    onMouseDown={(e) => e.preventDefault()}
                    onClick={() => choose(option)}
                    onMouseEnter={() => setActive(i)}
                    className={cn(
                      "flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-left text-sm",
                      i === active ? "bg-accent text-accent-foreground" : "text-foreground",
                    )}
                  >
                    <Check className={cn("h-3.5 w-3.5 flex-shrink-0", isSelected ? "opacity-100 text-primary" : "opacity-0")} />
                    <span className="min-w-0 flex-1">
                      <span className="block truncate">{option.label}</span>
                      {option.hint && (
                        <span className="block truncate font-mono text-[11px] text-muted-foreground">{option.hint}</span>
                      )}
                    </span>
                    {option.badge && (
                      <span className="flex-shrink-0 rounded border px-1.5 py-0.5 text-[10px] font-medium text-muted-foreground">
                        {option.badge}
                      </span>
                    )}
                  </button>
                </li>
              );
            })
          )}
        </ul>
      )}
    </div>
  );
}
