import { useState } from "react";
import { Check, Pencil, UserCircle2 } from "lucide-react";
import { useOperatorName } from "@/hooks/use-operator";

/** Sidebar control for the operator's name (used for approvals, requests and manual moves). */
export function OperatorBadge() {
  const [name, setName] = useOperatorName();
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState("");

  if (editing) {
    return (
      <form
        className="flex items-center gap-1.5"
        onSubmit={(e) => {
          e.preventDefault();
          setName(draft);
          setEditing(false);
        }}
      >
        <input
          autoFocus
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          placeholder="Your full name"
          aria-label="Your full name"
          className="min-w-0 flex-1 rounded border border-sidebar-border bg-sidebar-accent px-2 py-1 text-xs text-sidebar-foreground placeholder:text-sidebar-foreground/40 focus:outline-none focus:ring-1 focus:ring-sidebar-ring"
        />
        <button type="submit" className="rounded p-1 hover:bg-sidebar-accent" aria-label="Save name">
          <Check className="h-3.5 w-3.5" />
        </button>
      </form>
    );
  }

  return (
    <button
      type="button"
      onClick={() => {
        setDraft(name);
        setEditing(true);
      }}
      className="group flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-xs hover:bg-sidebar-accent"
      title="Your name is recorded on approvals, requests and manual movements"
    >
      <UserCircle2 className="h-4 w-4 flex-shrink-0 text-sidebar-foreground/60" />
      <span className="min-w-0 flex-1">
        <span className="block text-[10px] uppercase tracking-wider text-sidebar-foreground/40">Acting as</span>
        <span className={name ? "block truncate font-medium" : "block truncate italic text-amber-300"}>
          {name || "Set your name"}
        </span>
      </span>
      <Pencil className="h-3 w-3 flex-shrink-0 opacity-0 transition-opacity group-hover:opacity-60" />
    </button>
  );
}
