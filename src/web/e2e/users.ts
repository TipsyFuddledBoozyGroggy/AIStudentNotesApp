import path from "node:path";

// Fresh per-run users (auto-created by /dev/login) avoid dependence on shared DB state.
// Workers re-import this module, so the run id is shared through the environment.
const id = (process.env.E2E_RUN_ID ??= `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`);
export const AUTH_DIR = path.join(__dirname, ".auth");
export const USERS = {
  alice: "alice@dev.local",
  bob: "bob@dev.local",
  empty: `e2e-empty-${id}@dev.local`,
  signout: `e2e-signout-${id}@dev.local`,
  keyboard: `e2e-kbd-${id}@dev.local`,
  reload: `e2e-reload-${id}@dev.local`,
  axe: `e2e-axe-${id}@dev.local`,
  axeEmpty: `e2e-axeempty-${id}@dev.local`,
} as const;
export const authFile = (email: string) => path.join(AUTH_DIR, `${email}.json`);
export const stateFor = (k: keyof typeof USERS) => authFile(USERS[k]);
