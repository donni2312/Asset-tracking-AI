import { Router } from "express";
import { eq, sql } from "drizzle-orm";
import { db, usersTable } from "@workspace/db";
import { LoginBody } from "@workspace/api-zod";
import { signToken, verifyPassword } from "../lib/auth";
import { getConfig } from "../lib/config";
import { HttpError, sendError } from "../lib/http-error";
import { requireRole } from "../middlewares/auth";

const router = Router();

/**
 * A wrong username and a wrong password give the same answer, so the endpoint
 * cannot be used to find out which accounts exist.
 */
const BAD_CREDENTIALS = new HttpError(401, "Incorrect username or password");

// POST /auth/login
router.post("/auth/login", async (req, res) => {
  try {
    const parsed = LoginBody.safeParse(req.body);
    if (!parsed.success) throw BAD_CREDENTIALS;
    const username = parsed.data.username.trim().toLowerCase();

    const [user] = await db.select().from(usersTable).where(eq(usersTable.username, username));
    // Verify even when there is no such user, so the reply takes the same time
    // either way and does not leak which usernames are real.
    const stored = user?.passwordHash ?? "scrypt$00$00";
    const ok = await verifyPassword(parsed.data.password, stored);
    if (!user || !ok || !user.active) throw BAD_CREDENTIALS;

    const { secret, tokenTtlHours } = getConfig().auth;
    const expiresAt = new Date(Date.now() + tokenTtlHours * 3600_000);
    const token = signToken(
      { sub: user.id, username: user.username, role: user.role, exp: Math.floor(expiresAt.getTime() / 1000) },
      secret,
    );

    await db
      .update(usersTable)
      .set({ lastLoginAt: sql`now()`, updatedAt: sql`now()` })
      .where(eq(usersTable.id, user.id));

    res.json({
      token,
      expiresAt: expiresAt.toISOString(),
      user: { id: user.id, username: user.username, name: user.name, role: user.role, lastLoginAt: user.lastLoginAt },
    });
  } catch (err) {
    sendError(req, res, err, "Could not sign in");
  }
});

// GET /auth/me — who the bearer token belongs to (used to restore a session)
router.get("/auth/me", requireRole("viewer"), async (req, res) => {
  try {
    const [user] = await db.select().from(usersTable).where(eq(usersTable.id, req.user!.id));
    if (!user) throw new HttpError(401, "Sign in to continue");
    res.json({
      id: user.id,
      username: user.username,
      name: user.name,
      role: user.role,
      lastLoginAt: user.lastLoginAt,
    });
  } catch (err) {
    sendError(req, res, err, "Could not load the current user");
  }
});

export default router;
