import { afterAll, beforeAll, beforeEach, describe, expect, it } from "vitest";
import { usersTable } from "@workspace/db/schema";
import { eq } from "drizzle-orm";
import { resetDb, testDb } from "../test/test-db";
import { seedBasics, type World } from "../test/fixtures";
import { call, startTestServer, type TestServer } from "../test/http";
import { hashPassword, signToken } from "../lib/auth";

/**
 * Sign-in and the permission matrix on asset records.
 *
 *   viewer   read only
 *   operator may change an asset's operational fields (status, holder,
 *            location, notes)
 *   admin    may also change the record itself and register / delete assets
 */

let server: TestServer;
let w: World;

async function createUser(username: string, role: string, password = "secret123", active = true) {
  const [user] = await testDb
    .insert(usersTable)
    .values({ username, name: `${username} user`, role, passwordHash: await hashPassword(password), active })
    .returning();
  return user;
}

async function tokenFor(username: string, password = "secret123") {
  const res = await call(server, "POST", "/auth/login", { username, password });
  expect(res.status).toBe(200);
  return res.body.token as string;
}

beforeAll(async () => {
  server = await startTestServer();
});
afterAll(async () => {
  await server.close();
});
beforeEach(async () => {
  await resetDb();
  w = await seedBasics();
});

describe("POST /auth/login", () => {
  it("returns a token and the user for the right password", async () => {
    await createUser("rudo", "admin");
    const res = await call(server, "POST", "/auth/login", { username: "rudo", password: "secret123" });

    expect(res.status).toBe(200);
    expect(res.body.token).toEqual(expect.any(String));
    expect(res.body.user).toMatchObject({ username: "rudo", role: "admin" });
    expect(new Date(res.body.expiresAt).getTime()).toBeGreaterThan(Date.now());
    // The hash must never travel to the client.
    expect(JSON.stringify(res.body)).not.toContain("scrypt$");
  });

  it("records the sign-in time", async () => {
    const user = await createUser("rudo", "admin");
    expect(user.lastLoginAt).toBeNull();
    await tokenFor("rudo");

    const [after] = await testDb.select().from(usersTable).where(eq(usersTable.id, user.id));
    expect(after.lastLoginAt).not.toBeNull();
  });

  it("answers a wrong password exactly like an unknown user", async () => {
    await createUser("rudo", "admin");
    const wrongPassword = await call(server, "POST", "/auth/login", { username: "rudo", password: "nope" });
    const unknownUser = await call(server, "POST", "/auth/login", { username: "ghost", password: "nope" });

    expect(wrongPassword.status).toBe(401);
    expect(unknownUser.status).toBe(401);
    expect(wrongPassword.body.error).toBe(unknownUser.body.error);
  });

  it("refuses a disabled account", async () => {
    await createUser("gone", "admin", "secret123", false);
    const res = await call(server, "POST", "/auth/login", { username: "gone", password: "secret123" });
    expect(res.status).toBe(401);
  });

  it("is case-insensitive about the username", async () => {
    await createUser("rudo", "viewer");
    const res = await call(server, "POST", "/auth/login", { username: "  RUDO  ", password: "secret123" });
    expect(res.status).toBe(200);
  });
});

describe("GET /auth/me", () => {
  it("returns the signed-in user", async () => {
    await createUser("rudo", "operator");
    const res = await call(server, "GET", "/auth/me", undefined, await tokenFor("rudo"));
    expect(res.status).toBe(200);
    expect(res.body).toMatchObject({ username: "rudo", role: "operator" });
  });

  it("rejects a missing, forged or expired token", async () => {
    const user = await createUser("rudo", "operator");
    const forged = signToken(
      { sub: user.id, username: "rudo", role: "admin", exp: Math.floor(Date.now() / 1000) + 3600 },
      "the-wrong-secret",
    );
    const expired = signToken(
      { sub: user.id, username: "rudo", role: "admin", exp: Math.floor(Date.now() / 1000) - 60 },
      "test-secret-do-not-use-in-production",
    );

    expect((await call(server, "GET", "/auth/me")).status).toBe(401);
    expect((await call(server, "GET", "/auth/me", undefined, "not.a.token")).status).toBe(401);
    expect((await call(server, "GET", "/auth/me", undefined, forged)).status).toBe(401);
    expect((await call(server, "GET", "/auth/me", undefined, expired)).status).toBe(401);
  });
});

describe("asset records are role-protected", () => {
  it("refuses an edit with no token (401, not 403)", async () => {
    const res = await call(server, "PATCH", `/assets/${w.assets.laptop.id}`, { name: "Renamed" });
    expect(res.status).toBe(401);
  });

  it("refuses a viewer (403) and leaves the asset untouched", async () => {
    await createUser("nyasha", "viewer");
    const res = await call(server, "PATCH", `/assets/${w.assets.laptop.id}`, { status: "missing" }, await tokenFor("nyasha"));

    expect(res.status).toBe(403);
    const after = await call(server, "GET", `/assets/${w.assets.laptop.id}`);
    expect(after.body.status).toBe("active");
  });

  it("lets an operator change the operational fields", async () => {
    await createUser("tendai", "operator");
    const res = await call(
      server,
      "PATCH",
      `/assets/${w.assets.laptop.id}`,
      { status: "maintenance", assignedTo: "Rudo Chikwanha", notes: "In for a battery" },
      await tokenFor("tendai"),
    );

    expect(res.status).toBe(200);
    expect(res.body).toMatchObject({ status: "maintenance", assignedTo: "Rudo Chikwanha" });
  });

  it("stops an operator editing the record itself, and says which field", async () => {
    await createUser("tendai", "operator");
    const res = await call(
      server,
      "PATCH",
      `/assets/${w.assets.laptop.id}`,
      { name: "Not my laptop", category: "Projector" },
      await tokenFor("tendai"),
    );

    expect(res.status).toBe(403);
    expect(res.body.error).toContain("name");
    expect(res.body.error).toContain("admin");
    const after = await call(server, "GET", `/assets/${w.assets.laptop.id}`);
    expect(after.body.name).toBe("Dell Latitude 5440");
  });

  it("stops an operator smuggling a record field in beside an allowed one", async () => {
    await createUser("tendai", "operator");
    const res = await call(
      server,
      "PATCH",
      `/assets/${w.assets.laptop.id}`,
      { status: "maintenance", sbuId: w.sbus.bak.id },
      await tokenFor("tendai"),
    );

    expect(res.status).toBe(403);
    const after = await call(server, "GET", `/assets/${w.assets.laptop.id}`);
    expect(after.body.status).toBe("active"); // nothing was applied
    expect(after.body.sbuId).toBe(w.sbus.tsas.id);
  });

  it("lets an admin edit the whole record", async () => {
    await createUser("rudo", "admin");
    const res = await call(
      server,
      "PATCH",
      `/assets/${w.assets.laptop.id}`,
      { name: "Dell Latitude 5450", category: "Laptop", sbuId: w.sbus.bak.id, status: "active" },
      await tokenFor("rudo"),
    );

    expect(res.status).toBe(200);
    expect(res.body).toMatchObject({ name: "Dell Latitude 5450", sbuId: w.sbus.bak.id });
  });

  it("only lets an admin register or delete an asset", async () => {
    await createUser("tendai", "operator");
    await createUser("rudo", "admin");
    const operator = await tokenFor("tendai");
    const admin = await tokenFor("rudo");
    const asset = {
      name: "HP EliteBook 840",
      category: "Laptop",
      serialNumber: "SN-HP-900",
      rfidTag: "TAG-HP-900",
      sbuId: w.sbus.tsas.id,
    };

    expect((await call(server, "POST", "/assets", asset)).status).toBe(401);
    expect((await call(server, "POST", "/assets", asset, operator)).status).toBe(403);

    const created = await call(server, "POST", "/assets", asset, admin);
    expect(created.status).toBe(201);

    expect((await call(server, "DELETE", `/assets/${created.body.id}`, undefined, operator)).status).toBe(403);
    expect((await call(server, "DELETE", `/assets/${created.body.id}`, undefined, admin)).status).toBe(204);
  });

  it("applies a demotion to the next request, without waiting for the token to expire", async () => {
    const user = await createUser("rudo", "admin");
    const token = await tokenFor("rudo");
    expect((await call(server, "PATCH", `/assets/${w.assets.laptop.id}`, { name: "Renamed" }, token)).status).toBe(200);

    await testDb.update(usersTable).set({ role: "viewer" }).where(eq(usersTable.id, user.id));
    const res = await call(server, "PATCH", `/assets/${w.assets.laptop.id}`, { name: "Renamed again" }, token);
    expect(res.status).toBe(403);
  });

  it("stops a disabled account dead, token or not", async () => {
    const user = await createUser("rudo", "admin");
    const token = await tokenFor("rudo");

    await testDb.update(usersTable).set({ active: false }).where(eq(usersTable.id, user.id));
    expect((await call(server, "PATCH", `/assets/${w.assets.laptop.id}`, { name: "Renamed" }, token)).status).toBe(401);
  });

  it("leaves reading open — the dashboard still works signed out", async () => {
    expect((await call(server, "GET", "/assets")).status).toBe(200);
    expect((await call(server, "GET", `/assets/${w.assets.laptop.id}`)).status).toBe(200);
  });
});
