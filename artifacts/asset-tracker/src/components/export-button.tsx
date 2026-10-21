import { useState } from "react";
import { Download } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Permitted } from "@/components/permission";
import { useToast } from "@/hooks/use-toast";
import { apiErrorMessage } from "@/lib/api-error";
import { downloadReport, REPORT_TITLES, type ReportRequest } from "@/lib/reports";

/** "Export CSV" — downloads the report behind the current screen (any signed-in role). */
export function ExportCsvButton({
  request,
  label = "Export CSV",
  size = "default",
  variant = "outline",
}: {
  request: ReportRequest;
  label?: string;
  size?: "default" | "sm";
  variant?: "default" | "outline";
}) {
  const { toast } = useToast();
  const [busy, setBusy] = useState(false);

  async function run() {
    setBusy(true);
    try {
      await downloadReport(request);
    } catch (err) {
      toast({ title: `Could not download ${REPORT_TITLES[request.report].toLowerCase()}`, description: apiErrorMessage(err), variant: "destructive" });
    } finally {
      setBusy(false);
    }
  }

  return (
    <Permitted role="viewer" action="download reports" label={label} size={size} variant={variant}>
      <Button type="button" variant={variant} size={size} className="gap-2" onClick={run} disabled={busy}>
        <Download className="h-4 w-4" />
        {busy ? "Preparing…" : label}
      </Button>
    </Permitted>
  );
}
