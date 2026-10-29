import { useState } from "react";
import { Bar, BarChart, CartesianGrid, XAxis, YAxis } from "recharts";
import { AlertTriangle, Archive, ArrowRight, CheckCircle2, Wrench } from "lucide-react";
import { Link } from "wouter";
import {
  getGetMovementTrendQueryKey,
  useGetMovementTrend,
  type MovementTrendPoint,
  type SBUBreakdown,
} from "@workspace/api-client-react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { ChartContainer, ChartLegend, ChartLegendContent, ChartTooltip, ChartTooltipContent, type ChartConfig } from "@/components/ui/chart";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { cn } from "@/lib/utils";
import { formatDayLabel } from "@/lib/date-format";

/*
 * Dashboard charts. Colours follow the data-viz method:
 *  - movement triggers are categorical identity → documented palette slots 1-3
 *    (validated: worst adjacent CVD ΔE 9.2, normal-vision 27.6 on the white card);
 *    aqua is below 3:1 on white, so every chart ships a table view (relief channel).
 *  - asset status is a *state* → reserved status colours, always with icon + label.
 */

const SURFACE_GAP = 2; // px of card surface between touching segments
const END_RADIUS = 4; // rounded data-end, square at the baseline

// ── Stacked-segment shapes ──────────────────────────────────────────────────

type SegmentProps = {
  x?: number;
  y?: number;
  width?: number;
  height?: number;
  fill?: string;
  payload?: Record<string, unknown>;
};

const hasValue = (payload: Record<string, unknown> | undefined, key: string) => Number(payload?.[key] ?? 0) > 0;

/** Vertical stack: 2px gap under every segment but the first; rounded top on the last visible one. */
function columnSegment(series: readonly string[], key: string) {
  return function ColumnSegment({ x = 0, y = 0, width = 0, height = 0, fill, payload }: SegmentProps) {
    if (height <= 0 || width <= 0) return <g />;
    const i = series.indexOf(key);
    const isBottom = series.slice(0, i).every((k) => !hasValue(payload, k));
    const isTop = series.slice(i + 1).every((k) => !hasValue(payload, k));
    const h = Math.max(height - (isBottom ? 0 : SURFACE_GAP), 1);
    const r = isTop ? Math.min(END_RADIUS, width / 2, h) : 0;
    const d = `M${x},${y + h} L${x},${y + r} A${r},${r} 0 0 1 ${x + r},${y} L${x + width - r},${y} A${r},${r} 0 0 1 ${x + width},${y + r} L${x + width},${y + h} Z`;
    return <path d={d} fill={fill} />;
  };
}

/** Horizontal stack: 2px gap left of every segment but the first; rounded right end on the last visible one. */
function rowSegment(series: readonly string[], key: string) {
  return function RowSegment({ x = 0, y = 0, width = 0, height = 0, fill, payload }: SegmentProps) {
    if (height <= 0 || width <= 0) return <g />;
    const i = series.indexOf(key);
    const isFirst = series.slice(0, i).every((k) => !hasValue(payload, k));
    const isLast = series.slice(i + 1).every((k) => !hasValue(payload, k));
    const left = x + (isFirst ? 0 : SURFACE_GAP);
    const w = Math.max(width - (isFirst ? 0 : SURFACE_GAP), 1);
    const r = isLast ? Math.min(END_RADIUS, height / 2, w) : 0;
    const right = left + w;
    const d = `M${left},${y} L${right - r},${y} A${r},${r} 0 0 1 ${right},${y + r} L${right},${y + height - r} A${r},${r} 0 0 1 ${right - r},${y + height} L${left},${y + height} Z`;
    return <path d={d} fill={fill} />;
  };
}

/** Header link to the full listing behind a chart. */
function ViewAllLink({ href, label }: { href: string; label: string }) {
  return (
    <Link
      href={href}
      className="inline-flex items-center gap-1 text-xs font-medium text-muted-foreground hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring rounded"
    >
      {label} <ArrowRight className="w-3 h-3" />
    </Link>
  );
}

function ViewToggle({ view, onChange }: { view: "chart" | "table"; onChange: (v: "chart" | "table") => void }) {
  return (
    <div className="flex rounded-md bg-muted p-0.5 text-xs" role="group" aria-label="Chart or table view">
      {(["chart", "table"] as const).map((v) => (
        <button
          key={v}
          type="button"
          onClick={() => onChange(v)}
          aria-pressed={view === v}
          className={cn(
            "rounded px-2 py-1 capitalize transition-colors",
            view === v ? "bg-background shadow-sm text-foreground" : "text-muted-foreground hover:text-foreground",
          )}
        >
          {v}
        </button>
      ))}
    </div>
  );
}

// ── Movements per day ───────────────────────────────────────────────────────

const TREND_SERIES = ["rfid", "manual", "gatePass"] as const;
const trendConfig = {
  rfid: { label: "RFID scan", color: "#2a78d6" },
  manual: { label: "Manual move", color: "#eb6834" },
  gatePass: { label: "Gate-pass exit", color: "#1baf7a" },
} satisfies ChartConfig;

/** Axis and table labels follow the date-format preference (lib/date-format.ts). */
const dayLabel = (key: string, withWeekday = false) => formatDayLabel(key, { withWeekday });

export function MovementTrendChart({ days = 14, viewAllHref }: { days?: number; viewAllHref?: string }) {
  const [view, setView] = useState<"chart" | "table">("chart");
  const params = { days };
  const { data, isLoading, isFetching } = useGetMovementTrend(params, {
    query: { queryKey: getGetMovementTrendQueryKey(params), refetchInterval: 30_000 },
  });
  const points: MovementTrendPoint[] = data ?? [];
  const total = points.reduce((sum, p) => sum + p.total, 0);
  const today = points.at(-1)?.total ?? 0;

  return (
    <Card>
      <CardHeader className="flex flex-row items-start justify-between gap-4 space-y-0">
        <div>
          <CardTitle className="text-lg">Movements per day</CardTitle>
          <CardDescription>
            Last {days} days by trigger · {total} movements · {today} today
          </CardDescription>
        </div>
        <div className="flex items-center gap-3">
          {viewAllHref && <ViewAllLink href={viewAllHref} label="All movements" />}
          <ViewToggle view={view} onChange={setView} />
        </div>
      </CardHeader>
      <CardContent className={cn("transition-opacity", isFetching && !isLoading && "opacity-70")}>
        {isLoading ? (
          <div className="h-[260px] rounded bg-muted animate-pulse" />
        ) : view === "table" ? (
          <div className="max-h-[260px] overflow-y-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Day</TableHead>
                  {TREND_SERIES.map((k) => <TableHead key={k} className="text-right">{trendConfig[k].label}</TableHead>)}
                  <TableHead className="text-right">Total</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {[...points].reverse().map((p) => (
                  <TableRow key={p.date}>
                    <TableCell className="text-sm">{dayLabel(p.date, true)}</TableCell>
                    <TableCell className="text-right tabular-nums">{p.rfid}</TableCell>
                    <TableCell className="text-right tabular-nums">{p.manual}</TableCell>
                    <TableCell className="text-right tabular-nums">{p.gatePass}</TableCell>
                    <TableCell className="text-right tabular-nums font-medium">{p.total}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        ) : (
          <ChartContainer config={trendConfig} className="h-[260px] w-full">
            <BarChart data={points} margin={{ top: 8, right: 4, left: -16, bottom: 0 }} barCategoryGap="20%">
              <CartesianGrid vertical={false} />
              <XAxis dataKey="date" tickLine={false} axisLine={false} tickMargin={8} minTickGap={16} tickFormatter={(v: string) => dayLabel(v)} />
              <YAxis allowDecimals={false} tickLine={false} axisLine={false} width={44} />
              <ChartTooltip
                cursor={{ fill: "hsl(var(--muted))", opacity: 0.6 }}
                content={<ChartTooltipContent indicator="line" labelFormatter={(_, payload) => dayLabel(String(payload?.[0]?.payload?.date ?? ""), true)} />}
              />
              <ChartLegend content={<ChartLegendContent />} />
              {TREND_SERIES.map((key) => (
                <Bar key={key} dataKey={key} stackId="movements" fill={`var(--color-${key})`} maxBarSize={24} shape={columnSegment(TREND_SERIES, key)} />
              ))}
            </BarChart>
          </ChartContainer>
        )}
      </CardContent>
    </Card>
  );
}

// ── Asset status per SBU ────────────────────────────────────────────────────

const STATUS_SERIES = ["active", "maintenance", "missing", "retired"] as const;
const STATUS = {
  active: { label: "Active", color: "#0ca30c", icon: CheckCircle2 },
  maintenance: { label: "In repair", color: "#fab219", icon: Wrench },
  missing: { label: "Missing", color: "#d03b3b", icon: AlertTriangle },
  retired: { label: "Disposed / other", color: "#898781", icon: Archive },
} as const;
const statusConfig = Object.fromEntries(
  STATUS_SERIES.map((k) => [k, { label: STATUS[k].label, color: STATUS[k].color, icon: STATUS[k].icon }]),
) satisfies ChartConfig;

export function SbuStatusChart({ breakdown, viewAllHref }: { breakdown: SBUBreakdown[]; viewAllHref?: string }) {
  const [view, setView] = useState<"chart" | "table">("chart");
  const rows = breakdown.map((s) => {
    const maintenance = s.maintenanceAssets ?? 0;
    return {
      sbuCode: s.sbuCode,
      sbuName: s.sbuName,
      active: s.activeAssets,
      maintenance,
      missing: s.missingAssets,
      retired: Math.max(s.totalAssets - s.activeAssets - maintenance - s.missingAssets, 0),
      total: s.totalAssets,
    };
  });
  const totals = Object.fromEntries(STATUS_SERIES.map((k) => [k, rows.reduce((sum, r) => sum + r[k], 0)])) as Record<
    (typeof STATUS_SERIES)[number],
    number
  >;

  return (
    <Card>
      <CardHeader className="flex flex-row items-start justify-between gap-4 space-y-0">
        <div>
          <CardTitle className="text-lg">Asset status by SBU</CardTitle>
          <CardDescription>Share of each unit's assets by status</CardDescription>
        </div>
        <div className="flex items-center gap-3">
          {viewAllHref && <ViewAllLink href={viewAllHref} label="All SBUs" />}
          <ViewToggle view={view} onChange={setView} />
        </div>
      </CardHeader>
      <CardContent>
        {view === "table" ? (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>SBU</TableHead>
                {STATUS_SERIES.map((k) => <TableHead key={k} className="text-right">{STATUS[k].label}</TableHead>)}
                <TableHead className="text-right">Total</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.map((r) => (
                <TableRow key={r.sbuCode}>
                  <TableCell className="text-sm font-medium" title={r.sbuName}>{r.sbuCode}</TableCell>
                  {STATUS_SERIES.map((k) => <TableCell key={k} className="text-right tabular-nums">{r[k]}</TableCell>)}
                  <TableCell className="text-right tabular-nums font-medium">{r.total}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : (
          <>
            <ChartContainer config={statusConfig} className="w-full" style={{ height: Math.max(rows.length * 44 + 36, 140) }}>
              <BarChart data={rows} layout="vertical" margin={{ top: 4, right: 8, left: 0, bottom: 0 }} barCategoryGap="30%">
                <CartesianGrid horizontal={false} />
                <XAxis type="number" allowDecimals={false} tickLine={false} axisLine={false} tickMargin={6} />
                <YAxis type="category" dataKey="sbuCode" tickLine={false} axisLine={false} width={48} />
                <ChartTooltip
                  cursor={{ fill: "hsl(var(--muted))", opacity: 0.6 }}
                  content={<ChartTooltipContent indicator="line" labelFormatter={(_, payload) => String(payload?.[0]?.payload?.sbuName ?? "")} />}
                />
                {STATUS_SERIES.map((key) => (
                  <Bar key={key} dataKey={key} stackId="status" fill={`var(--color-${key})`} maxBarSize={24} shape={rowSegment(STATUS_SERIES, key)} />
                ))}
              </BarChart>
            </ChartContainer>
            {/* Status legend: colour is never alone — icon + label + count. */}
            <ul className="mt-3 grid grid-cols-2 gap-x-4 gap-y-1.5 text-xs">
              {STATUS_SERIES.map((k) => {
                const Icon = STATUS[k].icon;
                return (
                  <li key={k} className="flex items-center gap-2">
                    <span className="h-2.5 w-2.5 rounded-sm flex-shrink-0" style={{ backgroundColor: STATUS[k].color }} aria-hidden />
                    <Icon className="h-3.5 w-3.5 text-muted-foreground flex-shrink-0" aria-hidden />
                    <span className="text-muted-foreground">{STATUS[k].label}</span>
                    <span className="ml-auto font-medium tabular-nums">{totals[k]}</span>
                  </li>
                );
              })}
            </ul>
          </>
        )}
      </CardContent>
    </Card>
  );
}
