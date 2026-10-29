import { useListSbus } from "@workspace/api-client-react";

/** The approved SBU badge colours (UI standard); other SBUs use the colour stored on the SBU. */
const SBU_COLOURS: Record<string, string> = { AGRI: "#16A34A", BAK: "#DC2626", PROP: "#7C3AED", TSAS: "#2563EB", TSF: "#D97706", "PROP-H": "#65A30D" };

/** SBU code badge in the SBU's colour. */
export function SbuBadge({ code, color }: { code: string; color?: string | null }) {
  const { data: sbus } = useListSbus();
  const c = color ?? SBU_COLOURS[code] ?? sbus?.find((s) => s.code === code)?.color ?? "#6B7280";
  return (
    <span className="inline-flex items-center rounded border px-1.5 py-0.5 text-[10px] font-bold" style={{ color: c, borderColor: c, backgroundColor: `${c}18` }}>
      {code}
    </span>
  );
}
