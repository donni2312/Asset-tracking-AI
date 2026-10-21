import { useState } from "react";
import {
  useListSbus,
  type DownloadAssetsReportStatus,
  type DownloadGatePassesReportStatus,
  type DownloadLifecycleReportEventType,
} from "@workspace/api-client-react";
import { Activity, Box, Database, FileSpreadsheet, History, ShieldAlert } from "lucide-react";
import { Layout } from "@/components/layout";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Label } from "@/components/ui/label";
import { DateField } from "@/components/date-field";
import { ExportCsvButton } from "@/components/export-button";
import { PermissionNotice } from "@/components/permission";
import { SELECT_CLASS } from "@/lib/constants";
import { REPORT_TITLES, type ReportRequest } from "@/lib/reports";

/** Report downloads (CSV). Any signed-in user may download; times are in the organisation's time zone. */
export default function Reports() {
  const { data: sbus } = useListSbus();
  const [sbuId, setSbuId] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [assetStatus, setAssetStatus] = useState<DownloadAssetsReportStatus | "">("");
  const [passStatus, setPassStatus] = useState<DownloadGatePassesReportStatus | "">("");
  const [eventType, setEventType] = useState<DownloadLifecycleReportEventType | "">("");
  const [includeDismissed, setIncludeDismissed] = useState(true);

  const sbu = sbuId ? Number(sbuId) : undefined;
  const period = { from: from || undefined, to: to || undefined };
  const rangeError = from && to && from > to ? "The period ends before it starts." : null;

  const reports: Array<{ icon: typeof Box; description: string; filters: string; request: ReportRequest; extra?: React.ReactNode }> = [
    {
      icon: Box,
      description: "Every asset with its status, holder, location, SBU and purchase details.",
      filters: "SBU, status",
      request: { report: "assets", params: { sbuId: sbu, status: assetStatus || undefined } },
      extra: (
        <select className={SELECT_CLASS} value={assetStatus} onChange={(e) => setAssetStatus(e.target.value as DownloadAssetsReportStatus | "")} aria-label="Asset status">
          <option value="">Any status</option>
          <option value="active">Active</option>
          <option value="maintenance">Maintenance</option>
          <option value="missing">Missing</option>
          <option value="retired">Retired</option>
        </select>
      ),
    },
    {
      icon: Activity,
      description: "RFID scans, manual moves and gate-pass exits, oldest first.",
      filters: "SBU, period",
      request: { report: "movements", params: { sbuId: sbu, ...period } },
    },
    {
      icon: ShieldAlert,
      description: "Requests, decisions (who and when) and when each asset left the premises.",
      filters: "SBU, period (requested), status",
      request: { report: "gate-passes", params: { sbuId: sbu, status: passStatus || undefined, ...period } },
      extra: (
        <select className={SELECT_CLASS} value={passStatus} onChange={(e) => setPassStatus(e.target.value as DownloadGatePassesReportStatus | "")} aria-label="Gate pass status">
          <option value="">Any status</option>
          <option value="pending">Pending</option>
          <option value="approved">Approved</option>
          <option value="rejected">Rejected</option>
        </select>
      ),
    },
    {
      icon: History,
      description: "Deployments, returns, repairs, SBU transfers and notes.",
      filters: "SBU, period, event type",
      request: { report: "lifecycle", params: { sbuId: sbu, eventType: eventType || undefined, ...period } },
      extra: (
        <select className={SELECT_CLASS} value={eventType} onChange={(e) => setEventType(e.target.value as DownloadLifecycleReportEventType | "")} aria-label="Event type">
          <option value="">Any event</option>
          <option value="deployment">Deployment</option>
          <option value="return_to_it">Returned to IT</option>
          <option value="repair_sent">Sent for repair</option>
          <option value="repair_returned">Returned from repair</option>
          <option value="sbu_transfer">SBU transfer</option>
          <option value="note">Note</option>
        </select>
      ),
    },
    {
      icon: Database,
      description: "Anomaly alerts raised by the AI engine: type, severity, asset and message.",
      filters: "open only or all",
      request: { report: "insights", params: { includeDismissed } },
      extra: (
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" checked={includeDismissed} onChange={(e) => setIncludeDismissed(e.target.checked)} />
          Include dismissed alerts
        </label>
      ),
    },
  ];

  return (
    <Layout>
      <div className="space-y-6">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Reports</h1>
          <p className="text-muted-foreground text-sm">
            Download as CSV (opens in Excel). Times are in the organisation&apos;s time zone.
          </p>
        </div>

        <PermissionNotice role="viewer" action="download reports" />

        <Card>
          <CardHeader className="pb-3">
            <CardTitle className="text-base">Filters</CardTitle>
            <CardDescription>The SBU applies to every report except AI alerts; the period to movements, gate passes and lifecycle events.</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-3">
            <div className="space-y-1.5">
              <Label htmlFor="report-sbu">SBU</Label>
              <select id="report-sbu" className={SELECT_CLASS} value={sbuId} onChange={(e) => setSbuId(e.target.value)}>
                <option value="">All SBUs</option>
                {sbus?.map((s) => (
                  <option key={s.id} value={s.id}>{s.code} — {s.name}</option>
                ))}
              </select>
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="report-from">From (optional)</Label>
              <DateField id="report-from" value={from} onChange={setFrom} />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="report-to">To (optional)</Label>
              <DateField id="report-to" value={to} onChange={setTo} />
            </div>
            {rangeError && <p className="text-sm text-destructive sm:col-span-3">{rangeError}</p>}
          </CardContent>
        </Card>

        <div className="grid gap-4 md:grid-cols-2">
          {reports.map(({ icon: Icon, description, filters, request, extra }) => (
            <Card key={request.report} className="flex flex-col">
              <CardHeader className="pb-3">
                <CardTitle className="flex items-center gap-2 text-base">
                  <Icon className="h-4 w-4 text-primary" />
                  {REPORT_TITLES[request.report]}
                </CardTitle>
                <CardDescription>{description}</CardDescription>
              </CardHeader>
              <CardContent className="mt-auto space-y-3">
                {extra}
                <div className="flex items-center justify-between gap-3">
                  <span className="flex items-center gap-1 text-xs text-muted-foreground">
                    <FileSpreadsheet className="h-3.5 w-3.5" /> Filters: {filters}
                  </span>
                  {rangeError && request.report !== "assets" && request.report !== "insights" ? (
                    <span className="text-xs text-destructive">Fix the period first</span>
                  ) : (
                    <ExportCsvButton request={request} label="Download CSV" size="sm" />
                  )}
                </div>
              </CardContent>
            </Card>
          ))}
        </div>
      </div>
    </Layout>
  );
}
