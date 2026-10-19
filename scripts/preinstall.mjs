// Cross-platform replacement for the old `sh -c '...'` preinstall hook.
// It keeps the same behaviour (delete npm/yarn lockfiles, refuse non-pnpm
// installs) but runs under plain Node, so `pnpm install` also works from
// PowerShell / cmd.exe on Windows where `sh` is not on the PATH.
import { rmSync } from "node:fs";

for (const lockfile of ["package-lock.json", "yarn.lock"]) {
  rmSync(lockfile, { force: true });
}

const userAgent = process.env.npm_config_user_agent ?? "";
if (!userAgent.startsWith("pnpm/")) {
  console.error("Use pnpm instead");
  process.exit(1);
}
