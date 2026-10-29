import { Link } from "wouter";
import { useListApprovals, useListAssetUpgrades, type ApprovalItem } from "@workspace/api-client-react";
import { Layout } from "@/components/layout";
import { useCurrentUser } from "@/hooks/use-auth";
import { UpgradeDecisionButtons } from "@/pages/assets/card";
import { money } from "@/lib/finance";

/**
 * Everything waiting on the signed-in user. Phase 1: upgrades (AU-07) and "No serial" reasons (AU-02) for
 * SBU Finance; disposals, gate passes and transfers join this inbox in Phase 4.
 */
export default function Approvals() {
  const user = useCurrentUser();
  const { data, isLoading } = useListApprovals({ query: { enabled: !!user, queryKey: ["approvals"] } });
  return (
    <Layout>
      <div className="space-y-5">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Approvals</h1>
          <p className="text-sm text-muted-foreground">Everything waiting on you. Each request shows only what this role can decide.</p>
        </div>
        {!user && <p className="text-sm text-muted-foreground">Sign in to see your approvals.</p>}
        {user && user.role !== "sbu_finance" && (
          <p className="rounded-md border bg-card p-4 text-sm text-muted-foreground">
            In this release SBU Finance decides upgrades and "No serial" reasons. Disposal, gate-pass and transfer approvals for other roles arrive with the Phase 4 workflows.
          </p>
        )}
        {isLoading && <div className="h-32 animate-pulse rounded-md bg-muted" />}
        <div className="space-y-3">
          {(data ?? []).map((item) => <ApprovalRow key={item.id} item={item} />)}
          {data && data.length === 0 && user?.role === "sbu_finance" && <p className="text-sm text-muted-foreground">Nothing is waiting on you.</p>}
        </div>
      </div>
    </Layout>
  );
}

function ApprovalRow({ item }: { item: ApprovalItem }) {
  const { data: upgrades } = useListAssetUpgrades(item.assetId, { query: { enabled: item.type === "upgrade", queryKey: ["upgrades", item.assetId] } });
  const upgrade = upgrades?.find((u) => u.id === item.upgradeId);
  return (
    <div className="flex flex-wrap items-center justify-between gap-3 rounded-md border bg-card p-4">
      <div className="min-w-0">
        <span className={`text-[11px] font-bold ${item.type === "upgrade" ? "text-violet-400" : "text-amber-500"}`}>{item.type === "upgrade" ? "UPGRADE · AU-07" : "NO SERIAL · AU-02"}</span>
        <div className="mt-1 font-semibold">{item.summary}</div>
        <div className="text-xs text-muted-foreground">
          <Link href={`/assets/${item.assetId}/card`} className="font-mono text-primary hover:underline">{item.assetNumber ?? "pending"}</Link> · {item.assetDescription}
          {upgrade && ` · ${money(upgrade.cost)}`}{item.raisedBy && ` · logged by ${item.raisedBy}`} · {new Date(item.raisedAt).toLocaleDateString("en-GB")}
        </div>
      </div>
      {upgrade ? <UpgradeDecisionButtons upgrade={upgrade} assetId={item.assetId} /> : <Link href={`/assets/${item.assetId}/card`} className="text-sm font-semibold text-amber-500">Review on the card →</Link>}
    </div>
  );
}
