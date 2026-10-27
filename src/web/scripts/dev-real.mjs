// Runs the dev server against the real API (docker compose "api" service) instead of MSW.
import { spawn } from "node:child_process";
const env = {
  ...process.env,
  NEXT_PUBLIC_API_MOCKING: "disabled",
  API_PROXY_TARGET: process.env.API_PROXY_TARGET ?? "http://localhost:5080",
};
const child = spawn("npx", ["next", "dev"], { stdio: "inherit", env, shell: true });
child.on("exit", (code) => process.exit(code ?? 0));
