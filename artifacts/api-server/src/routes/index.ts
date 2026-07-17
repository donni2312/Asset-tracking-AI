import { Router, type IRouter } from "express";
import healthRouter from "./health";
import sbusRouter from "./sbus";
import assetsRouter from "./assets";
import locationsRouter from "./locations";
import movementsRouter from "./movements";
import lifecycleRouter from "./lifecycle";
import gatePassesRouter from "./gate-passes";
import rfidRouter from "./rfid";
import dashboardRouter from "./dashboard";
import insightsRouter from "./insights";
import bcRouter from "./bc";

const router: IRouter = Router();

router.use(healthRouter);
router.use(sbusRouter);
router.use(assetsRouter);
router.use(locationsRouter);
router.use(movementsRouter);
router.use(lifecycleRouter);
router.use(gatePassesRouter);
router.use(rfidRouter);
router.use(dashboardRouter);
router.use(insightsRouter);
router.use(bcRouter);

export default router;
