import pino from "pino";

const isProduction = process.env.NODE_ENV === "production";
const isTest = process.env.NODE_ENV === "test" || process.env.VITEST !== undefined;

export const logger = pino({
  level: process.env.LOG_LEVEL ?? (isTest ? "silent" : "info"),
  redact: [
    "req.headers.authorization",
    "req.headers.cookie",
    "res.headers['set-cookie']",
  ],
  // Pretty, colourised logs for local development only. Production emits JSON;
  // tests skip the transport so no worker thread is left running.
  ...(isProduction || isTest
    ? {}
    : {
        transport: {
          target: "pino-pretty",
          options: { colorize: true },
        },
      }),
});
