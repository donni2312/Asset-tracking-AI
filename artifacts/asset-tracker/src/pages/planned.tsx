import { Layout } from "@/components/layout";

const PLANNED: Record<string, { title: string; stories: string }> = {
  stores: { title: "Stores & Receiving", stories: "ST-01 to ST-05: receive against a PO, capture and tag, stock, issue and returns" },
  people: { title: "People & Toolboxes", stories: "HR-01, HR-03, HR-04: employee toolboxes, acknowledgements and exit clearance" },
  departments: { title: "Departments", stories: "LM-01, LM-02: department equipment and staff holdings" },
  fleet: { title: "Fleet & Non-IT", stories: "AM-02 to AM-04: fleet register, document reminders and service schedules" },
};

/** Sidebar entries whose screens are built in Phase 4 (non-IT registration already works on Asset Register). */
export default function Planned({ screen }: { screen: keyof typeof PLANNED }) {
  const p = PLANNED[screen];
  return (
    <Layout>
      <div className="max-w-2xl space-y-3">
        <h1 className="text-2xl font-bold tracking-tight">{p.title}</h1>
        <p className="rounded-md border bg-card p-4 text-sm text-muted-foreground">Built in Phase 4 ({p.stories}).</p>
      </div>
    </Layout>
  );
}
