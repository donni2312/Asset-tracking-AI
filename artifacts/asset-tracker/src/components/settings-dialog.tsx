import { useEffect, useState } from "react";
import { Check, Monitor, Moon, Settings, Sun } from "lucide-react";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { cn } from "@/lib/utils";
import { useOperatorName } from "@/hooks/use-operator";
import { resolveTheme, useTheme, type Theme } from "@/hooks/use-theme";
import { DATE_FORMATS, formatDate, useDateFormat } from "@/lib/date-format";

const THEME_OPTIONS: { value: Theme; label: string; hint: string; icon: typeof Sun }[] = [
  { value: "light", label: "Light", hint: "Bright rooms", icon: Sun },
  { value: "dark", label: "Dark", hint: "Low light, easier on the eyes", icon: Moon },
  { value: "system", label: "System", hint: "Follow Windows / macOS", icon: Monitor },
];

/**
 * Personalisation panel. Everything here is stored in this browser (there is no
 * user account in the MVP): the colour theme and the operator name recorded on
 * approvals, requests and manual movements.
 */
export function SettingsDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const [theme, setTheme] = useTheme();
  const [dateFormat, setDateFormat] = useDateFormat();
  const [name, setName] = useOperatorName();
  const [draftName, setDraftName] = useState(name);

  // Re-read the stored name each time the panel opens - it can also be changed
  // from the "Acting as" badge in the sidebar while this component is mounted.
  useEffect(() => {
    if (open) setDraftName(name);
  }, [open, name]);

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Settings</DialogTitle>
          <DialogDescription>
            Personalise this dashboard. Saved in this browser only.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-6">
          {/* Appearance */}
          <fieldset className="space-y-2">
            <legend className="text-sm font-medium">Appearance</legend>
            <p className="text-xs text-muted-foreground">
              Dark mode is easier on the eyes in low light — handy for after-hours gate approvals.
            </p>
            <div className="grid grid-cols-3 gap-2 pt-1">
              {THEME_OPTIONS.map((option) => {
                const active = theme === option.value;
                return (
                  <button
                    key={option.value}
                    type="button"
                    onClick={() => setTheme(option.value)}
                    aria-pressed={active}
                    className={cn(
                      "flex flex-col items-center gap-1.5 rounded-md border p-3 text-xs transition-colors",
                      active
                        ? "border-primary bg-primary/10 text-foreground"
                        : "border-border text-muted-foreground hover:bg-accent hover:text-accent-foreground",
                    )}
                  >
                    <option.icon className="h-4 w-4" />
                    <span className="font-medium">{option.label}</span>
                    {active && <Check className="h-3 w-3 text-primary" aria-hidden />}
                  </button>
                );
              })}
            </div>
            <p className="text-xs text-muted-foreground">
              {THEME_OPTIONS.find((o) => o.value === theme)?.hint}
              {theme === "system" && ` — currently ${resolveTheme("system")}`}
            </p>
          </fieldset>

          {/* Date format */}
          <fieldset className="space-y-2">
            <legend className="text-sm font-medium">Date format</legend>
            <p className="text-xs text-muted-foreground">
              Applies to every date in the system — movement logs, gate passes, asset history and charts.
            </p>
            <div className="space-y-1.5 pt-1">
              {DATE_FORMATS.map((option) => {
                const active = dateFormat === option.value;
                return (
                  <button
                    key={option.value}
                    type="button"
                    onClick={() => setDateFormat(option.value)}
                    aria-pressed={active}
                    className={cn(
                      "flex w-full items-center gap-3 rounded-md border px-3 py-2 text-left text-xs transition-colors",
                      active
                        ? "border-primary bg-primary/10 text-foreground"
                        : "border-border text-muted-foreground hover:bg-accent hover:text-accent-foreground",
                    )}
                  >
                    <span className="font-mono font-medium">{option.label}</span>
                    <span className="flex-1">{option.hint}</span>
                    {active && <Check className="h-3.5 w-3.5 flex-shrink-0 text-primary" aria-hidden />}
                  </button>
                );
              })}
            </div>
            <p className="text-xs text-muted-foreground">
              With a time: <span className="font-mono">{formatDate(new Date(), dateFormat)}</span>
            </p>
          </fieldset>

          {/* Operator */}
          <div className="space-y-2">
            <Label htmlFor="settings-operator">Your name</Label>
            <Input
              id="settings-operator"
              value={draftName}
              onChange={(e) => setDraftName(e.target.value)}
              placeholder="e.g. Tendai Moyo"
            />
            <p className="text-xs text-muted-foreground">
              Recorded on gate-pass approvals, requests and manual movements, and shown as
              “Acting as” in the sidebar.
            </p>
          </div>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
          <Button
            onClick={() => {
              setName(draftName);
              onOpenChange(false);
            }}
          >
            Save
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/** Sidebar button that opens the settings panel, with a one-click theme toggle beside it. */
export function SettingsButton() {
  const [open, setOpen] = useState(false);
  const [theme, setTheme] = useTheme();
  const resolved = resolveTheme(theme);

  return (
    <>
      <div className="flex items-center gap-1">
        <button
          type="button"
          onClick={() => setOpen(true)}
          className="flex flex-1 items-center gap-2 rounded-md px-2 py-1.5 text-xs hover:bg-sidebar-accent"
          title="Theme and your name"
        >
          <Settings className="h-4 w-4 flex-shrink-0 text-sidebar-foreground/60" />
          <span className="font-medium">Settings</span>
        </button>
        <button
          type="button"
          onClick={() => setTheme(resolved === "dark" ? "light" : "dark")}
          className="rounded-md p-1.5 hover:bg-sidebar-accent"
          title={`Switch to ${resolved === "dark" ? "light" : "dark"} mode`}
          aria-label={`Switch to ${resolved === "dark" ? "light" : "dark"} mode`}
        >
          {resolved === "dark"
            ? <Sun className="h-4 w-4 text-sidebar-foreground/70" />
            : <Moon className="h-4 w-4 text-sidebar-foreground/70" />}
        </button>
      </div>
      <SettingsDialog open={open} onOpenChange={setOpen} />
    </>
  );
}
