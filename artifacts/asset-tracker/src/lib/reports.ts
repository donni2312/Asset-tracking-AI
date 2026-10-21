import {
  downloadAssetsReport,
  downloadGatePassesReport,
  downloadInsightsReport,
  downloadLifecycleReport,
  downloadMovementsReport,
  type DownloadAssetsReportParams,
  type DownloadGatePassesReportParams,
  type DownloadInsightsReportParams,
  type DownloadLifecycleReportParams,
  type DownloadMovementsReportParams,
} from "@workspace/api-client-react";

/**
 * CSV report downloads (GET /api/reports/*.csv). The generated client sends the signed-in user's token,
 * so these work for any signed-in role; signed out, the API answers 401.
 */
export type ReportRequest =
  | { report: "assets"; params?: DownloadAssetsReportParams }
  | { report: "movements"; params?: DownloadMovementsReportParams }
  | { report: "gate-passes"; params?: DownloadGatePassesReportParams }
  | { report: "insights"; params?: DownloadInsightsReportParams }
  | { report: "lifecycle"; params?: DownloadLifecycleReportParams };

export type ReportName = ReportRequest["report"];

export const REPORT_TITLES: Record<ReportName, string> = {
  assets: "Asset register",
  movements: "Movement log",
  "gate-passes": "Gate passes",
  insights: "AI alerts",
  lifecycle: "Lifecycle events",
};

function fetchCsv(request: ReportRequest): Promise<string> {
  switch (request.report) {
    case "assets":
      return downloadAssetsReport(request.params);
    case "movements":
      return downloadMovementsReport(request.params);
    case "gate-passes":
      return downloadGatePassesReport(request.params);
    case "insights":
      return downloadInsightsReport(request.params);
    case "lifecycle":
      return downloadLifecycleReport(request.params);
  }
}

/** Today in the browser as YYYY-MM-DD (for the file name). */
function todayKey(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

/** Fetch the report and save it as `assetiq-<report>-<date>.csv`. Throws the API error on failure. */
export async function downloadReport(request: ReportRequest): Promise<void> {
  const text = await fetchCsv(request);
  // Reading the response as text drops the UTF-8 byte-order mark the API sends; put it back so Excel
  // keeps reading names with accents and dashes correctly.
  const blob = new Blob(["﻿", text], { type: "text/csv;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = `assetiq-${request.report}-${todayKey()}.csv`;
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
