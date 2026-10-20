import type { NextFunction, Request, Response } from "express";
import { eq } from "drizzle-orm";
import { db, usersTable } from "@workspace/db";
import { getConfig } from "../lib/config";
import { verifyToken } from "../lib/auth";
import { HttpError, sendError } from "../lib/http-error";

export type Role = "viewer" | "operator" | "admin";

export interface AuthUser {
  id: number;
  username: string;
  name: string;
  role: Role;
}

declare global {
  // eslint-disable-next-line @typescript-eslint/no-namespace
  namespace Express {
    interface Request {
      /** Set by `authenticate` when the request carries a valid bearer token. */
      user?: AuthUser;
    }
  }
}

/** Least to most privileged: a role satisfies every requirement at or below it. */
const RANK: Record<Role, number> = { viewer: 0, operator: 1, admin: 2 };

export function hasRole(user: AuthUser | undefined, required: Role): boolean {
  return !!user && RANK[user.role] >= RANK[required];
}

/**
 * Reads the bearer token and attaches `req.user`. It never rejects a request —
 * public endpoints keep working — so routes state their own requirement with
 * `requireRole`.
 *
 * The role is re-read from the database rather than trusted from the token, so
 * demoting or disabling an account takes effect on the next request instead of
 * when the token expires.
 */
export async function authenticate(req: Request, _res: Response, next: NextFunction): Promise<void> {
  const header = req.header("authorization");
  const token = header?.toLowerCase().startsWith("bearer ") ? header.slice(7).trim() : null;
  if (!token) return next();

  const payload = verifyToken(token, getConfig().auth.secret);
  if (!payload) return next();

  try {
    const [user] = await db
      .select({
        id: usersTable.id,
        username: usersTable.username,
        name: usersTable.name,
        role: usersTable.role,
        active: usersTable.active,
      })
      .from(usersTable)
      .where(eq(usersTable.id, payload.sub));

    if (user?.active) {
      req.user = { id: user.id, username: user.username, name: user.name, role: user.role as Role };
    }
  } catch (err) {
    req.log?.error(err, "could not load the user behind a token");
  }
  next();
}

/**
 * 401 when nobody is signed in, 403 when they are but lack the role — the two
 * are different to the caller: one means "sign in", the other "ask someone else".
 */
export function requireRole(required: Role) {
  return (req: Request, res: Response, next: NextFunction): void => {
    if (!req.user) {
      sendError(req, res, new HttpError(401, "Sign in to continue"), "Unauthorized");
      return;
    }
    if (!hasRole(req.user, required)) {
      sendError(
        req,
        res,
        new HttpError(403, `This action needs the ${required} role; you are signed in as ${req.user.role}.`),
        "Forbidden",
      );
      return;
    }
    next();
  };
}
