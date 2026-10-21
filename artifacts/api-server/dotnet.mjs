// Runs the .NET CLI for the pnpm scripts in this package (dev, build, test, typecheck).
// It finds `dotnet` even when it is not on PATH yet — e.g. a VS Code window opened before the
// SDK was installed — by also looking in the standard install locations.
import { spawn } from "node:child_process";
import { existsSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const exe = process.platform === "win32" ? "dotnet.exe" : "dotnet";
const candidates = [
  process.env.DOTNET_ROOT && path.join(process.env.DOTNET_ROOT, exe),
  ...(process.env.PATH ?? "").split(path.delimiter).filter(Boolean).map((dir) => path.join(dir, exe)),
  process.platform === "win32" && process.env.ProgramFiles && path.join(process.env.ProgramFiles, "dotnet", exe),
  path.join(os.homedir(), ".dotnet", exe),
  "/usr/local/share/dotnet/dotnet",
  "/usr/share/dotnet/dotnet",
  "/usr/lib/dotnet/dotnet",
].filter(Boolean);
const dotnet = candidates.find((candidate) => existsSync(candidate));

if (!dotnet) {
  console.error("The .NET 8 SDK was not found. Install it from https://dotnet.microsoft.com/download/dotnet/8.0 (or set DOTNET_ROOT).");
  process.exit(1);
}

const child = spawn(dotnet, process.argv.slice(2), {
  cwd: path.dirname(fileURLToPath(import.meta.url)),
  stdio: "inherit",
  env: { DOTNET_NOLOGO: "1", ...process.env },
});
// Ctrl+C reaches dotnet straight from the terminal; wait for it to shut down cleanly instead of killing it.
process.on("SIGINT", () => {});
process.on("SIGTERM", () => child.kill("SIGTERM"));
child.on("exit", (code, signal) => process.exit(code ?? (signal ? 1 : 0)));
