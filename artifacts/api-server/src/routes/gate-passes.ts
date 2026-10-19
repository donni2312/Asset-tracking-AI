import { Router } from "express";
import {
  CreateGatePassBody,
  ListGatePassesQueryParams,
  ApproveGatePassBody,
  RejectGatePassBody,
} from "@workspace/api-zod";
import { parseIdParam, sendError } from "../lib/http-error";
import {
  approveGatePass,
  createGatePassRequest,
  getGatePass,
  listGatePasses,
  rejectGatePass,
} from "../services/gate-pass-workflow";

const router = Router();

// GET /gate-passes?status=&sbuId=
router.get("/gate-passes", async (req, res) => {
  try {
    const parsed = ListGatePassesQueryParams.safeParse(req.query);
    const filters = parsed.success ? parsed.data : {};
    res.json(await listGatePasses({ status: filters.status, sbuId: filters.sbuId ?? null }));
  } catch (err) {
    sendError(req, res, err, "Failed to list gate passes");
  }
});

// POST /gate-passes - manual "Request gate pass"
router.post("/gate-passes", async (req, res): Promise<void> => {
  try {
    const parsed = CreateGatePassBody.safeParse(req.body);
    if (!parsed.success) { res.status(400).json({ error: parsed.error.message }); return; }
    res.status(201).json(await createGatePassRequest(parsed.data));
  } catch (err) {
    sendError(req, res, err, "Failed to create gate pass");
  }
});

// GET /gate-passes/:id
router.get("/gate-passes/:id", async (req, res): Promise<void> => {
  try {
    res.json(await getGatePass(parseIdParam(req.params.id)));
  } catch (err) {
    sendError(req, res, err, "Failed to get gate pass");
  }
});

// POST /gate-passes/:id/approve - records the gate_pass exit when the asset is waiting at a gate
router.post("/gate-passes/:id/approve", async (req, res): Promise<void> => {
  try {
    const id = parseIdParam(req.params.id);
    const parsed = ApproveGatePassBody.safeParse(req.body);
    if (!parsed.success) { res.status(400).json({ error: parsed.error.message }); return; }
    const { gatePass, exitMovement } = await approveGatePass(id, parsed.data);
    req.log.info({ gatePassId: id, decidedBy: gatePass.approvedBy, exitMovementId: exitMovement?.id ?? null }, "Gate pass approved");
    res.json(gatePass);
  } catch (err) {
    sendError(req, res, err, "Failed to approve gate pass");
  }
});

// POST /gate-passes/:id/reject - flags the asset
router.post("/gate-passes/:id/reject", async (req, res): Promise<void> => {
  try {
    const id = parseIdParam(req.params.id);
    const parsed = RejectGatePassBody.safeParse(req.body);
    if (!parsed.success) { res.status(400).json({ error: parsed.error.message }); return; }
    const { gatePass, flag } = await rejectGatePass(id, parsed.data);
    req.log.info({ gatePassId: id, decidedBy: gatePass.approvedBy, flagInsightId: flag?.id ?? null }, "Gate pass rejected");
    res.json(gatePass);
  } catch (err) {
    sendError(req, res, err, "Failed to reject gate pass");
  }
});

export default router;
