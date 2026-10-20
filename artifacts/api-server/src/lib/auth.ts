import { createHmac, randomBytes, scrypt as scryptCallback, timingSafeEqual } from "node:crypto";
import { promisify } from "node:util";
import { HttpError } from "./http-error";

/**
 * Password hashing and bearer tokens, built on node:crypto so the project adds
 * no dependency (installs on the TSL network are slow and often time out).
 *
 * Passwords: scrypt with a per-password random salt, compared in constant time.
 * Tokens:    compact HS256 JWTs signed with AUTH_SECRET.
 */

const scrypt = promisify(scryptCallback) as (
  password: string,
  salt: Buffer,
  keylen: number,
) => Promise<Buffer>;

const KEY_LENGTH = 64;

// ── Passwords ───────────────────────────────────────────────────────────────

/** `scrypt$<salt-hex>$<hash-hex>` — self-describing, so the format can change later. */
export async function hashPassword(password: string): Promise<string> {
  const salt = randomBytes(16);
  const derived = await scrypt(password, salt, KEY_LENGTH);
  return `scrypt$${salt.toString("hex")}$${derived.toString("hex")}`;
}

export async function verifyPassword(password: string, stored: string): Promise<boolean> {
  const [scheme, saltHex, hashHex] = stored.split("$");
  if (scheme !== "scrypt" || !saltHex || !hashHex) return false;
  const expected = Buffer.from(hashHex, "hex");
  const derived = await scrypt(password, Buffer.from(saltHex, "hex"), expected.length);
  return expected.length === derived.length && timingSafeEqual(expected, derived);
}

// ── Tokens ──────────────────────────────────────────────────────────────────

export interface TokenPayload {
  /** User id. */
  sub: number;
  username: string;
  role: string;
  /** Expiry, seconds since the epoch. */
  exp: number;
}

const b64url = (input: Buffer | string) =>
  Buffer.from(input).toString("base64").replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");

const fromB64url = (input: string) => Buffer.from(input.replace(/-/g, "+").replace(/_/g, "/"), "base64");

function sign(data: string, secret: string): string {
  return b64url(createHmac("sha256", secret).update(data).digest());
}

export function signToken(payload: TokenPayload, secret: string): string {
  const header = b64url(JSON.stringify({ alg: "HS256", typ: "JWT" }));
  const body = b64url(JSON.stringify(payload));
  return `${header}.${body}.${sign(`${header}.${body}`, secret)}`;
}

/** Returns the payload, or null when the token is malformed, forged or expired. */
export function verifyToken(token: string, secret: string): TokenPayload | null {
  const parts = token.split(".");
  if (parts.length !== 3) return null;
  const [header, body, signature] = parts;

  const expected = fromB64url(sign(`${header}.${body}`, secret));
  const actual = fromB64url(signature);
  if (expected.length !== actual.length || !timingSafeEqual(expected, actual)) return null;

  try {
    const payload = JSON.parse(fromB64url(body).toString("utf8")) as TokenPayload;
    if (typeof payload.exp !== "number" || payload.exp * 1000 <= Date.now()) return null;
    if (typeof payload.sub !== "number") return null;
    return payload;
  } catch {
    return null;
  }
}

/** Throws a 401 with the message the routes use everywhere else. */
export function unauthorized(message = "Sign in to continue"): HttpError {
  return new HttpError(401, message);
}
