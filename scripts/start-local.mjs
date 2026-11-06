#!/usr/bin/env node
// One command to run Tether locally on mock/synthetic data: Postgres + API (docker compose, dev overlay) + web (dev:real),
// wait for health, seed the demo data, print the URL and how to stop.
//
//   node scripts/start-local.mjs            start (or reuse what is already running), seed, print the URL
//   node scripts/start-local.mjs --no-seed  start only
//   node scripts/start-local.mjs --seed     seed only (needs the stack up)
//   node scripts/start-local.mjs stop       stop the web server this script started and the compose containers (data kept)
//   node scripts/start-local.mjs status     show what is up
//
// Windows PowerShell:  .\scripts\start-local.ps1      Git Bash / anywhere with Node:  node scripts/start-local.mjs
//
// Rules it keeps: never a second `next dev` (L-DEVSERVER: a second one corrupts .next), never `next build`, localhost only,
// mock providers only, no secrets (compose uses its dev-only defaults), nothing leaves this machine.

import { spawn, spawnSync } from "node:child_process";
import { existsSync, openSync, readFileSync, writeFileSync, rmSync, mkdirSync } from "node:fs";
import net from "node:net";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { seed } from "./seed-demo.mjs";

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const WEB_DIR = path.join(ROOT, "src", "web");
const WEB_URL = "http://localhost:3000";
const API_URL = "http://localhost:5080";
const COMPOSE = ["compose", "-f", "docker-compose.yml", "-f", "docker-compose.dev.yml"];
const STATE_DIR = path.join(os.tmpdir(), "tether-local");
const PID_FILE = path.join(STATE_DIR, "web.pid");
const LOG_FILE = path.join(STATE_DIR, "web-dev.log");
const isWin = process.platform === "win32";

const args = process.argv.slice(2);
const cmd = args.find((a) => !a.startsWith("--")) ?? "start";
const flag = (f) => args.includes(`--${f}`);

const say = (m) => console.log(m);
const step = (m) => console.log(`\n== ${m}`);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
function die(m) {
  console.error(`\nERROR: ${m}`);
  process.exit(1);
}

function run(file, a, opts = {}) {
  return spawnSync(file, a, { cwd: ROOT, encoding: "utf8", shell: false, ...opts });
}

async function http(url, timeoutMs = 3000) {
  try {
    const res = await fetch(url, { signal: AbortSignal.timeout(timeoutMs), redirect: "manual" });
    const body = await res.text().catch(() => "");
    return { status: res.status, body };
  } catch {
    return null;
  }
}

function portInUse(port) {
  return new Promise((resolve) => {
    const s = net.connect({ port, host: "127.0.0.1" });
    s.once("connect", () => (s.destroy(), resolve(true)));
    s.once("error", () => resolve(false));
    s.setTimeout(1500, () => (s.destroy(), resolve(false)));
  });
}

async function waitFor(label, fn, timeoutS) {
  const t0 = Date.now();
  let lastNote = 0;
  while ((Date.now() - t0) / 1000 < timeoutS) {
    if (await fn()) return;
    if (Date.now() - lastNote > 15000) {
      say(`   ...still waiting for ${label} (${Math.round((Date.now() - t0) / 1000)} s)`);
      lastNote = Date.now();
    }
    await sleep(1500);
  }
  die(`${label} did not become ready within ${timeoutS} s.`);
}

const apiHealthy = async () => (await http(`${API_URL}/health/live`))?.status === 200;

/** True when :3000 is answered by this app (the sign-in page renders). */
async function webIsTether() {
  const r = await http(`${WEB_URL}/signin`, 8000);
  return r !== null && r.status === 200 && /Tether|Sign in/i.test(r.body);
}

/** True when the web server at :3000 forwards /api to the real API (dev:real) rather than answering from mocks. */
async function webUsesRealApi() {
  const r = await http(`${WEB_URL}/api/v1/me`);
  // Real API, signed out: 401 problem+json. A mock or a missing rewrite answers something else.
  return r !== null && r.status === 401;
}

function stopWeb() {
  if (!existsSync(PID_FILE)) return false;
  const pid = Number(readFileSync(PID_FILE, "utf8").trim());
  rmSync(PID_FILE, { force: true });
  if (!pid) return false;
  if (isWin) run("taskkill", ["/PID", String(pid), "/T", "/F"]);
  else {
    try {
      process.kill(-pid, "SIGTERM");
    } catch {
      /* already gone */
    }
  }
  return true;
}

async function doStop() {
  step("Stopping");
  if (stopWeb()) say("web dev server (the one this script started): stopped");
  else say(`web dev server: not started by this script, left alone. If you started it yourself, stop it in its own terminal.`);
  const r = run("docker", [...COMPOSE, "stop", "api", "postgres"], { stdio: "inherit" });
  if (r.status !== 0) say("docker compose stop reported a problem (is Docker running?)");
  say("Data is kept (the pgdata volume). To wipe the database as well: docker compose -f docker-compose.yml -f docker-compose.dev.yml down -v");
}

async function doStatus() {
  say(`API   ${API_URL}/health/live : ${(await apiHealthy()) ? "healthy" : "not reachable"}`);
  const web = await webIsTether();
  say(`Web   ${WEB_URL}            : ${web ? ((await webUsesRealApi()) ? "up, using the real API" : "up, NOT proxying /api to the real API") : (await portInUse(3000)) ? "port 3000 is taken by something else" : "not running"}`);
  const ps = run("docker", ["ps", "--format", "{{.Names}}  {{.Status}}"]);
  if (ps.status === 0) say(ps.stdout.trim().split("\n").filter((l) => /postgres|api/.test(l)).join("\n"));
}

async function startStack() {
  step("1/4 Docker, Postgres and the API (mock providers, dev overlay)");
  const info = run("docker", ["info", "--format", "{{.ServerVersion}}"]);
  if (info.error || info.status !== 0) die("Docker is not running or not installed. Start Docker Desktop, then run this again.");
  if (await apiHealthy()) {
    say("API already healthy on :5080: reusing it (no restart).");
    run("docker", [...COMPOSE, "up", "-d", "postgres"], { stdio: "inherit" });
  } else {
    // Idempotent: does nothing for containers that are already up and unchanged.
    const up = run("docker", [...COMPOSE, "up", "-d", "postgres", "api"], { stdio: "inherit" });
    if (up.status !== 0) die("docker compose up failed (output above).");
    say("Waiting for the API. The first start restores and builds the .NET project inside the container and can take several minutes.");
    await waitFor("the API (/health/live)", apiHealthy, 600);
  }
  say("API healthy: " + API_URL + "/health/live");
}

/** Returns the child process when this script started the web server, otherwise undefined (reused). */
async function startWeb() {
  step("2/4 Web (npm run dev:real in src/web)");
  if (await webIsTether()) {
    if (!(await webUsesRealApi())) {
      die(`Something Tether-like is on :3000 but does not forward /api to the real API (a mock-mode dev server?). Stop it, then run this again. This script never starts a second next dev.`);
    }
    say("A web dev server is already running on :3000 and uses the real API: reusing it (nothing started).");
    return undefined;
  }
  if (await portInUse(3000)) {
    die("Port 3000 is in use by something that is not answering as Tether (it may still be starting, or be another program). Not starting a second next dev. Check it, then run this again.");
  }
  if (!existsSync(path.join(WEB_DIR, "node_modules"))) {
    say("src/web/node_modules is missing: running npm ci (one time).");
    const i = spawnSync(isWin ? "npm.cmd" : "npm", ["ci"], { cwd: WEB_DIR, stdio: "inherit", shell: isWin });
    if (i.status !== 0) die("npm ci failed (output above).");
  }
  mkdirSync(STATE_DIR, { recursive: true });
  const out = openSync(LOG_FILE, "a");
  const detach = flag("detach");
  // Windows: one command string through the shell (npm is npm.cmd; an args array with shell:true is deprecated). Output goes to a log
  // file. Not detached by default: the web server then lives exactly as long as this window, which is predictable and stops with Ctrl+C.
  const child = spawn(isWin ? "npm.cmd run dev:real" : "npm run dev:real", isWin ? [] : [], {
    cwd: WEB_DIR,
    detached: detach && !isWin,
    stdio: ["ignore", out, out],
    windowsHide: true,
    shell: isWin,
  });
  if (detach) child.unref();
  writeFileSync(PID_FILE, String(child.pid));
  say(`Started (pid ${child.pid}); log: ${LOG_FILE}`);
  await waitFor("the web server", async () => (await webIsTether()) && (await webUsesRealApi()), 240);
  say("Web is serving " + WEB_URL);
  return child;
}

async function doSeed() {
  step("3/4 Demo data (synthetic, idempotent)");
  await seed(API_URL);
}

function banner() {
  step("4/4 Ready");
  say(`
  Open:      ${WEB_URL}
  Sign in:   type  demo@dev.local  (any email works; this one has the demo data). There is no password in dev.
  What is there: two "Demo:" courses, a finished "Demo lecture (synthetic replay)" with transcript, summary and notes,
                 and a "Demo syllabus (synthetic)" waiting for your review. All of it is mock/synthetic. Nothing real is called.
  Stop:      Ctrl+C ends a web server this script started (reused servers: stop them in their own window), then
             node scripts/start-local.mjs stop        (PowerShell: .\\scripts\\start-local.ps1 stop)   stops the containers, data kept
  Re-seed:   node scripts/start-local.mjs --seed      (safe to repeat, never duplicates)
  Web log:   ${LOG_FILE}
`);
}

(async () => {
  if (cmd === "stop") return doStop();
  if (cmd === "status") return doStatus();
  if (cmd !== "start") die(`Unknown command "${cmd}". Use: start (default), stop, status.`);
  if (flag("seed")) {
    if (!(await apiHealthy())) die("The API is not up. Run without --seed first.");
    return doSeed();
  }
  await startStack();
  const web = await startWeb();
  if (flag("no-seed")) say("\n3/4 Seeding skipped (--no-seed).");
  else await doSeed();
  banner();
  if (web && !flag("detach")) {
    say("This window is now the web server's lifetime: leave it open. Ctrl+C stops the web server (the API and Postgres containers keep running).");
    const bye = () => {
      stopWeb();
      say("\nWeb server stopped. API and Postgres are still up; stop them with: node scripts/start-local.mjs stop");
      process.exit(0);
    };
    process.on("SIGINT", bye);
    process.on("SIGTERM", bye);
    web.on("exit", (code) => {
      rmSync(PID_FILE, { force: true });
      console.error(`\nThe web server exited (code ${code}). See ${LOG_FILE}`);
      process.exit(1);
    });
    await new Promise(() => {});
  }
})().catch((e) => die(e instanceof Error ? e.message : String(e)));
