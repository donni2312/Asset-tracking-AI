import { Router } from "express";
import { db, aiInsightsTable } from "@workspace/db";
import { desc, eq } from "drizzle-orm";
import { AiQueryBody } from "@workspace/api-zod";
import { HttpError, parseIdParam, sendError } from "../lib/http-error";
import { enrichInsights, runPeriodicInsightRules } from "../services/insights";
import { answerQuestion } from "../services/nl-query";

const router = Router();

// GET /insights - evaluates the periodic rules (idle / missing) first
router.get("/insights", async (req, res) => {
  try {
    await runPeriodicInsightRules();
    const rows = await db.select().from(aiInsightsTable).orderBy(desc(aiInsightsTable.createdAt), desc(aiInsightsTable.id));
    res.json(await enrichInsights(db, rows));
  } catch (err) {
    sendError(req, res, err, "Failed to list insights");
  }
});

// POST /insights/:id/dismiss
router.post("/insights/:id/dismiss", async (req, res): Promise<void> => {
  try {
    const id = parseIdParam(req.params.id);
    const [row] = await db.update(aiInsightsTable).set({ dismissed: true }).where(eq(aiInsightsTable.id, id)).returning();
    if (!row) throw new HttpError(404, "Insight not found");
    const [dto] = await enrichInsights(db, [row]);
    res.json(dto);
  } catch (err) {
    sendError(req, res, err, "Failed to dismiss insight");
  }
});

// POST /ai/query - natural-language query (OpenAI intent parsing when configured, rules as fallback)
router.post("/ai/query", async (req, res): Promise<void> => {
  try {
    const parsed = AiQueryBody.safeParse(req.body);
    if (!parsed.success) { res.status(400).json({ error: parsed.error.message }); return; }
    const question = parsed.data.question.trim();
    if (!question) { res.status(400).json({ error: "question is required" }); return; }
    const result = await answerQuestion(question);
    req.log.info({ intent: result.intent, source: result.source }, "AI query answered");
    res.json(result);
  } catch (err) {
    sendError(req, res, err, "AI query failed");
  }
});

export default router;
