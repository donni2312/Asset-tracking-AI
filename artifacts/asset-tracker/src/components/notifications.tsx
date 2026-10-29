import { useState } from "react";
import { Link } from "wouter";
import { useQueryClient } from "@tanstack/react-query";
import { useListNotifications, useMarkNotificationsRead } from "@workspace/api-client-react";
import { Bell } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";

/**
 * In-app notifications (Phase 4): approvals waiting, reminders, escalations, decisions, transfers. The same
 * messages are logged as emails until Microsoft 365 / SMTP is connected.
 */
export function NotificationsButton({ count }: { count: number }) {
  const [open, setOpen] = useState(false);
  const { data } = useListNotifications({}, { query: { enabled: open, queryKey: ["notifications", "all"] } });
  const read = useMarkNotificationsRead();
  const qc = useQueryClient();
  return (
    <>
      <button type="button" onClick={() => setOpen(true)} className="flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-xs hover:bg-sidebar-accent">
        <Bell className="h-4 w-4 flex-shrink-0 text-sidebar-foreground/60" />
        <span className="flex-1">Notifications</span>
        {count > 0 && <span className="min-w-5 h-5 rounded-full bg-amber-500 px-1.5 text-center text-[10px] font-bold leading-5 text-slate-900">{count}</span>}
      </button>
      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent className="max-h-[80vh] overflow-y-auto">
          <DialogHeader><DialogTitle>Notifications</DialogTitle></DialogHeader>
          <Button size="sm" variant="outline" className="w-fit" onClick={() => read.mutate({ data: {} }, { onSuccess: () => qc.invalidateQueries({ queryKey: ["notifications"] }) })}>Mark all read</Button>
          <ul className="space-y-2">
            {(data ?? []).map((n) => (
              <li key={n.id} className={`rounded border p-2 text-sm ${n.readAt ? "opacity-60" : ""}`}>
                <div className="font-medium">{n.title}</div>
                <div className="text-xs text-muted-foreground">{n.body}</div>
                <div className="mt-1 flex justify-between text-[11px] text-muted-foreground">
                  <span>{new Date(n.createdAt).toLocaleString("en-GB")}</span>
                  {n.link && <Link href={n.link} onClick={() => setOpen(false)} className="text-primary hover:underline">Open</Link>}
                </div>
              </li>
            ))}
            {data?.length === 0 && <li className="text-sm text-muted-foreground">No notifications.</li>}
          </ul>
        </DialogContent>
      </Dialog>
    </>
  );
}
