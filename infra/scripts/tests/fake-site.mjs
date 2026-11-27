// A stand-in for the deployed site, used ONLY by infra/scripts/tests/smoke-cases.sh. It answers the routes the outside
// smoke probes call (ADR-022 section 2) over HTTPS with a throwaway certificate, and misbehaves on request (SCENARIO) so
// each probe's pass, retry, fail and breach paths can be exercised without Azure and without the real API.
// It is not the API and proves nothing about it: the API's own tests assert the same contract values.
//   PORT=8443 SCENARIO=ok TLS_KEY=key.pem TLS_CERT=cert.pem node fake-site.mjs
import https from "node:https";
import fs from "node:fs";
import crypto from "node:crypto";

const port = Number(process.env.PORT ?? 8443);
const scenario = process.env.SCENARIO ?? "ok";
const origin = `https://localhost:${port}`;
const binderName = "__Host-cc.signin";
const hits = {};
let issuedBinder = null;

const hit = (key) => (hits[key] = (hits[key] ?? 0) + 1);
const is = (...names) => names.includes(scenario);

function send(res, status, headers = {}, body = "") {
  res.writeHead(status, { "Cache-Control": "no-store", ...headers });
  res.end(body);
}
const json = (res, status, value, headers = {}) => send(res, status, { "Content-Type": "application/json", ...headers }, JSON.stringify(value));
const problem = (res, status, code) => json(res, status, { status, code });

function readBody(req) {
  return new Promise((resolve) => {
    let data = "";
    req.on("data", (chunk) => (data += chunk));
    req.on("end", () => resolve(data));
  });
}

function authorizationUrl() {
  const redirect = is("start-wrong-redirect") ? "https://elsewhere.example.invalid/api/v1/auth/google/callback" : `${origin}/api/v1/auth/google/callback`;
  const scope = is("start-extra-scope") ? "openid email profile https://www.googleapis.com/auth/calendar" : "openid email profile";
  const host = is("start-other-host") ? "https://accounts.example.invalid/o/oauth2/v2/auth" : "https://accounts.google.com/o/oauth2/v2/auth";
  const space = is("ok-plus-encoding") ? "+" : "%20";
  const params = [
    ["client_id", "123456789012-fake.apps.googleusercontent.com"],
    ["redirect_uri", encodeURIComponent(redirect)],
    ["response_type", is("start-token-response") ? "token" : "code"],
    ["scope", scope.split(" ").map(encodeURIComponent).join(space)],
    ["state", crypto.randomBytes(32).toString("base64url")],
    ["nonce", crypto.randomBytes(32).toString("base64url")],
    ["code_challenge", crypto.randomBytes(32).toString("base64url")],
    ["code_challenge_method", is("start-plain-challenge") ? "plain" : "S256"],
    ["prompt", "select_account"],
  ];
  return `${host}?${params.map(([k, v]) => `${k}=${v}`).join("&")}`;
}

function binderCookie(value) {
  const name = is("start-cookie-wrong-name") ? "cc.signin" : binderName;
  const parts = [`${name}=${value}`, "max-age=600", "path=/"];
  if (!is("start-cookie-no-secure")) parts.push("secure");
  parts.push(is("start-cookie-samesite-none") ? "samesite=none" : "samesite=lax");
  if (!is("start-cookie-no-httponly")) parts.push("httponly");
  if (is("start-cookie-domain")) parts.push("domain=localhost");
  return parts.join("; ");
}

const key = fs.readFileSync(process.env.TLS_KEY);
const cert = fs.readFileSync(process.env.TLS_CERT);

https
  .createServer({ key, cert }, async (req, res) => {
    const url = new URL(req.url, origin);
    const route = `${req.method} ${url.pathname}`;
    const n = hit(route);
    const unsafe = !["GET", "HEAD", "OPTIONS"].includes(req.method);
    const body = unsafe ? await readBody(req) : "";

    // Whole-site behaviours, for the closed, IP-allowlist and Microsoft sign-in gates.
    if (is("gate-entra-302")) return send(res, 302, { Location: "https://login.microsoftonline.com/00000000-0000-0000-0000-000000000000/oauth2/v2.0/authorize?x=1" });
    if (is("gate-entra-401")) return send(res, 401);
    if (is("gate-403")) return send(res, 403);
    if (is("gate-404")) return send(res, 404);
    if (is("gate-open-200")) return send(res, 200, { "Content-Type": "text/html" }, "<h1>Sign in</h1>");

    // Like the real API: an unsafe request without the site's own Origin is refused before routing.
    if (unsafe && req.headers.origin !== origin) return problem(res, 403, "csrf_failed");

    switch (route) {
      case "POST /api/v1/dev/login": {
        if (is("devlogin-204")) return send(res, 204);
        if (is("devlogin-401")) return problem(res, 401, "unauthenticated");
        if (is("devlogin-401-other-code")) return problem(res, 401, "csrf_failed");
        if (is("devlogin-401-not-json")) return send(res, 401, { "Content-Type": "text/html" }, "<h1>401</h1>");
        // Not the API's own answer (no problem code): what a sign-in layer that has not quite gone would send.
        if (is("devlogin-401-nobody-then-ok") && n <= 2) return send(res, 401);
        if (is("devlogin-401-nobody-then-ok")) return problem(res, 401, "unauthenticated");
        if (is("devlogin-401-set-cookie")) return json(res, 401, { status: 401, code: "unauthenticated" }, { "Set-Cookie": "cc.session=x; path=/" });
        if (is("devlogin-400")) return problem(res, 400, "validation_failed");
        if (is("devlogin-403")) return problem(res, 403, "csrf_failed");
        if (is("devlogin-429-forever")) return problem(res, 429, "rate_limited");
        if (is("devlogin-429-then-404") && n <= 2) return problem(res, 429, "rate_limited");
        if (is("devlogin-503-then-404") && n <= 1) return send(res, 503);
        if (body !== '{"email":"smoke@example.invalid"}') return problem(res, 500, "unexpected_probe_body");
        return problem(res, 404, "not_found");
      }
      case "GET /api/v1/auth/methods": {
        if (is("methods-devlogin-true")) return json(res, 200, { google: true, devLogin: true });
        if (is("methods-google-false")) return json(res, 200, { google: false, devLogin: false });
        if (is("methods-not-json")) return send(res, 200, { "Content-Type": "text/html" }, "<html></html>");
        if (is("methods-404")) return problem(res, 404, "not_found");
        return json(res, 200, { google: true, devLogin: false });
      }
      case "GET /api/v1/me":
        return is("me-200") ? json(res, 200, { id: "x" }) : problem(res, 401, "unauthenticated");
      case "POST /hubs/lecture/negotiate":
        return is("negotiate-200") ? json(res, 200, { connectionId: "x" }) : problem(res, 401, "unauthenticated");
      case "POST /api/v1/auth/google/signin/start": {
        if (is("start-429-then-ok") && n <= 1) return problem(res, 429, "rate_limited");
        if (body !== '{"landing":"home"}') return problem(res, 400, "validation_failed");
        issuedBinder = crypto.randomBytes(32).toString("base64url");
        const headers = is("start-no-cookie") ? {} : { "Set-Cookie": binderCookie(issuedBinder) };
        const payload = { purpose: "signin", authorizationUrl: authorizationUrl(), expiresAt: new Date(Date.now() + 600_000).toISOString() };
        if (is("start-no-url")) delete payload.authorizationUrl;
        return json(res, 200, payload, headers);
      }
      case "GET /api/v1/auth/google/callback": {
        const state = url.searchParams.get("state") ?? "";
        const sentBinder = (req.headers.cookie ?? "") === `${binderName}=${issuedBinder}`;
        // The probe must send a 43-character state, code=x and the binder it was just given; anything else is a test failure.
        if (!/^[A-Za-z0-9_-]{43}$/.test(state) || url.searchParams.get("code") !== "x" || !sentBinder) return problem(res, 500, "unexpected_probe_request");
        if (is("callback-200")) return send(res, 200, { "Content-Type": "text/html" }, "<h1>Signed in</h1>");
        const cleared = is("callback-binder-kept") ? {} : { "Set-Cookie": `${binderName}=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/; secure; samesite=lax; httponly` };
        if (is("callback-rate-limited-then-ok") && n <= 1) return send(res, 302, { Location: "/signin?purpose=signin&outcome=rate_limited", ...cleared });
        if (is("callback-absolute-location")) return send(res, 302, { Location: `${origin}/signin?purpose=signin&outcome=state_invalid`, ...cleared });
        if (is("callback-other-outcome")) return send(res, 302, { Location: "/signin?purpose=signin&outcome=provider_unavailable", ...cleared });
        return send(res, 302, { Location: "/signin?purpose=signin&outcome=state_invalid", ...cleared });
      }
      case "GET /signin": {
        if (is("signin-302")) return send(res, 302, { Location: "/.auth/login/aad" });
        // The Microsoft layer answering now and then while it goes away: once (second request), or every other request for ever.
        const entra = { Location: "https://login.microsoftonline.com/00000000-0000-0000-0000-000000000000/oauth2/v2.0/authorize?x=1" };
        if (is("entra-flap-then-clear") && n === 2) return send(res, 302, entra);
        if (is("entra-flap-forever") && n % 2 === 0) return send(res, 302, entra);
        const headers = { "Content-Type": "text/html" };
        if (!is("signin-no-hsts")) headers["Strict-Transport-Security"] = "max-age=31536000";
        return send(res, 200, headers, "<h1>Sign in</h1>");
      }
      default:
        return problem(res, 404, "not_found");
    }
  })
  .listen(port, "127.0.0.1", () => console.log(`fake site: ${scenario} on ${origin}`));
