import type { Server } from "node:http";
import type { AddressInfo } from "node:net";
import app from "../app";

export interface TestServer {
  url: string;
  close: () => Promise<void>;
}

/** Start the real Express app on a random port. */
export async function startTestServer(): Promise<TestServer> {
  const server: Server = await new Promise((resolve) => {
    const s = app.listen(0, () => resolve(s));
  });
  const { port } = server.address() as AddressInfo;
  return {
    url: `http://127.0.0.1:${port}/api`,
    close: () => new Promise<void>((resolve, reject) => server.close((err) => (err ? reject(err) : resolve()))),
  };
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any
export async function call<T = any>(
  server: TestServer,
  method: string,
  path: string,
  body?: unknown,
  /** Bearer token for endpoints behind requireRole. */
  token?: string,
): Promise<{ status: number; body: T }> {
  const headers: Record<string, string> = {};
  if (body !== undefined) headers["content-type"] = "application/json";
  if (token) headers.authorization = `Bearer ${token}`;
  const res = await fetch(`${server.url}${path}`, {
    method,
    headers: Object.keys(headers).length > 0 ? headers : undefined,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
  const text = await res.text();
  return { status: res.status, body: (text ? JSON.parse(text) : null) as T };
}
