import { Router } from "express";
import { db } from "@workspace/db";
import { aiInsightsTable, assetsTable, movementsTable, locationsTable } from "@workspace/db";
import { eq, desc, and } from "drizzle-orm";
import { AiQueryBody } from "@workspace/api-zod";

const router = Router();

async function runInsightAnalysis() {
  try {
    const allAssets = await db.select().from(assetsTable);
    const sevenDaysAgo = new Date(Date.now() - 7 * 24 * 60 * 60 * 1000);

    for (const asset of allAssets) {
      if (asset.status === "active") {
        const [lastMove] = await db
          .select({ timestamp: movementsTable.timestamp })
          .from(movementsTable)
          .where(eq(movementsTable.assetId, asset.id))
          .orderBy(desc(movementsTable.timestamp))
          .limit(1);

        if (!lastMove || lastMove.timestamp < sevenDaysAgo) {
          const [existing] = await db.select().from(aiInsightsTable).where(
            and(
              eq(aiInsightsTable.assetId, asset.id),
              eq(aiInsightsTable.type, "idle_asset"),
              eq(aiInsightsTable.dismissed, false)
            )
          );
          if (!existing) {
            await db.insert(aiInsightsTable).values({
              type: "idle_asset",
              message: `Asset "${asset.name}" has not moved in over 7 days — verify its location and status`,
              assetId: asset.id,
              severity: "info",
              dismissed: false,
            });
          }
        }
      }

      if (asset.status === "missing") {
        const [existing] = await db.select().from(aiInsightsTable).where(
          and(
            eq(aiInsightsTable.assetId, asset.id),
            eq(aiInsightsTable.type, "missing_asset"),
            eq(aiInsightsTable.dismissed, false)
          )
        );
        if (!existing) {
          await db.insert(aiInsightsTable).values({
            type: "missing_asset",
            message: `Asset "${asset.name}" (SN: ${asset.serialNumber}) is marked missing — initiate recovery procedure`,
            assetId: asset.id,
            severity: "critical",
            dismissed: false,
          });
        }
      }
    }
  } catch (_) {
    // Non-fatal
  }
}

// GET /insights
router.get("/insights", async (req, res) => {
  try {
    await runInsightAnalysis();
    const insights = await db
      .select()
      .from(aiInsightsTable)
      .orderBy(desc(aiInsightsTable.createdAt));

    const allAssets = await db.select({ id: assetsTable.id, name: assetsTable.name }).from(assetsTable);
    const assetMap: Record<number, string> = Object.fromEntries(allAssets.map(a => [a.id, a.name]));

    res.json(insights.map(i => ({
      ...i,
      assetName: i.assetId != null ? (assetMap[i.assetId] ?? null) : null,
    })));
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to list insights" });
  }
});

// POST /insights/:id/dismiss
router.post("/insights/:id/dismiss", async (req, res): Promise<void> => {
  try {
    const id = parseInt(req.params.id);
    await db.update(aiInsightsTable).set({ dismissed: true }).where(eq(aiInsightsTable.id, id));
    const [insight] = await db.select().from(aiInsightsTable).where(eq(aiInsightsTable.id, id));
    if (!insight) {
      res.status(404).json({ error: "Insight not found" });
      return;
    }
    const allAssets = await db.select({ id: assetsTable.id, name: assetsTable.name }).from(assetsTable);
    const assetMap: Record<number, string> = Object.fromEntries(allAssets.map(a => [a.id, a.name]));
    res.json({ ...insight, assetName: insight.assetId != null ? (assetMap[insight.assetId] ?? null) : null });
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "Failed to dismiss insight" });
  }
});

// POST /ai/query
router.post("/ai/query", async (req, res): Promise<void> => {
  try {
    const parsed = AiQueryBody.safeParse(req.body);
    if (!parsed.success) {
      res.status(400).json({ error: parsed.error.message });
      return;
    }

    const { question } = parsed.data;
    const q = question.toLowerCase();

    const allAssets = await db.select().from(assetsTable);
    const allLocations = await db.select().from(locationsTable);
    const locMap: Record<number, string> = Object.fromEntries(allLocations.map(l => [l.id, l.name]));

    let answer = "";
    let relatedAssets: typeof allAssets = [];

    if (q.includes("missing") || q.includes("lost")) {
      relatedAssets = allAssets.filter(a => a.status === "missing");
      answer = relatedAssets.length === 0
        ? "No assets are currently marked as missing."
        : `${relatedAssets.length} missing asset${relatedAssets.length > 1 ? "s" : ""}: ${relatedAssets.map(a => `${a.name} (${a.serialNumber})`).join(", ")}.`;
    } else if (q.includes("maintenance")) {
      relatedAssets = allAssets.filter(a => a.status === "maintenance");
      answer = relatedAssets.length === 0
        ? "No assets are currently in maintenance."
        : `${relatedAssets.length} asset${relatedAssets.length > 1 ? "s are" : " is"} in maintenance: ${relatedAssets.map(a => a.name).join(", ")}.`;
    } else if (q.includes("where") || q.includes("location")) {
      const matched = allAssets.filter(a => q.includes(a.name.toLowerCase()));
      if (matched.length > 0) {
        relatedAssets = matched;
        answer = matched.map(a => {
          const locName = a.currentLocationId != null ? (locMap[a.currentLocationId] ?? "Unknown") : "No fixed location";
          return `${a.name} is currently at: ${locName}`;
        }).join(". ");
      } else {
        answer = "Please mention the specific asset name to locate it. For example: 'Where is the Dell Latitude?'";
      }
    } else if (q.includes("laptop") || q.includes("laptops")) {
      relatedAssets = allAssets.filter(a => a.category.toLowerCase().includes("laptop"));
      answer = `There are ${relatedAssets.length} laptop${relatedAssets.length !== 1 ? "s" : ""} in the system.`;
    } else if (q.includes("total") || q.includes("how many") || q.includes("count")) {
      answer = `Total assets: ${allAssets.length}. Active: ${allAssets.filter(a => a.status === "active").length}. In maintenance: ${allAssets.filter(a => a.status === "maintenance").length}. Missing: ${allAssets.filter(a => a.status === "missing").length}.`;
    } else if (q.includes("unassigned") || q.includes("not assigned")) {
      relatedAssets = allAssets.filter(a => !a.assignedTo);
      answer = `${relatedAssets.length} asset${relatedAssets.length !== 1 ? "s are" : " is"} currently unassigned.`;
    } else {
      answer = `I found ${allAssets.length} assets in the system. Try asking: "Where is the Dell Latitude?", "Which assets are missing?", "How many assets are in maintenance?", or "Show me unassigned assets."`;
    }

    const enrichedAssets = relatedAssets.map(a => ({
      ...a,
      currentLocationName: a.currentLocationId != null ? (locMap[a.currentLocationId] ?? null) : null,
    }));

    res.json({
      question,
      answer,
      relatedAssets: enrichedAssets,
      timestamp: new Date().toISOString(),
    });
  } catch (err) {
    req.log.error(err);
    res.status(500).json({ error: "AI query failed" });
  }
});

export default router;
