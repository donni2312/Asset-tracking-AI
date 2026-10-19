import type { Request, Response } from "express";

/** An error that maps directly to an HTTP status (thrown by services, rendered by routes). */
export class HttpError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = "HttpError";
    this.status = status;
  }
}

/** Render `err` as JSON: HttpErrors keep their status and message, anything else becomes a logged 500. */
export function sendError(req: Request, res: Response, err: unknown, fallbackMessage: string): void {
  if (err instanceof HttpError) {
    res.status(err.status).json({ error: err.message });
    return;
  }
  req.log.error(err);
  res.status(500).json({ error: fallbackMessage });
}

/** Parse a positive integer route parameter, or throw a 400. */
export function parseIdParam(raw: string | string[] | undefined): number {
  const value = Array.isArray(raw) ? raw[0] : raw;
  const id = Number(value);
  if (!Number.isInteger(id) || id <= 0) throw new HttpError(400, `Invalid id: ${value}`);
  return id;
}
