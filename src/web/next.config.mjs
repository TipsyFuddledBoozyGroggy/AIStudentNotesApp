/** @type {import('next').NextConfig} */
// Dev-only harness routes are files named page.dev.tsx. They are only routable outside production builds,
// or when TETHER_ENABLE_DEV_HARNESS=1 is set. Otherwise they are not in the route table at all.
const enableHarness = process.env.NODE_ENV !== "production" || process.env.TETHER_ENABLE_DEV_HARNESS === "1";
const nextConfig = {
  pageExtensions: enableHarness ? ["tsx", "ts", "jsx", "js", "dev.tsx"] : ["tsx", "ts", "jsx", "js"],
  reactStrictMode: true,
  // Container image (src/web/Dockerfile): `next build` also emits .next/standalone (server.js plus only the node_modules it
  // needs). `next dev` is unaffected. `next start` still serves but prints a warning that it "does not work with
  // output: standalone"; the image runs `node server.js` and nothing in CI uses `next start`.
  output: "standalone",
  // Do not advertise the framework in every response.
  poweredByHeader: false,
  // The floating dev "N" badge overlaps content in demos and screenshots; build/runtime error overlays still show.
  devIndicators: false,
  webpack(config, { isServer }) {
    // msw/browser is exported with "node": null; it is only ever loaded in the browser.
    if (isServer) config.resolve.alias["msw/browser"] = false;
    return config;
  },
  // HSTS on everything the web server itself answers: pages, static files and its own 404s. Responses proxied from the
  // API (/api, /hubs, /ingest) pass through WITHOUT it (checked against the container on 2026-11-26), which is fine: a
  // browser only has to see the header once per host, and every page load carries it. One year, this host only: no
  // includeSubDomains (the Azure host name sits under a shared platform domain) and no preload. Browsers ignore the
  // header over plain http, so localhost dev and E2E are unaffected.
  // The rest mirror the API's own headers (Tether.Host Program.cs) where they make sense for pages: no framing (the app
  // never frames itself; CSP frame-ancestors plus X-Frame-Options for older browsers), no MIME sniffing, no full URL
  // (session ids are in paths) sent to other sites, and only this origin may ask for the microphone. A full CSP
  // (scripts, styles) needs its own design pass; this one restricts framing only.
  async headers() {
    return [
      {
        source: "/:path*",
        headers: [
          { key: "Strict-Transport-Security", value: "max-age=31536000" },
          { key: "Content-Security-Policy", value: "frame-ancestors 'none'" },
          { key: "X-Frame-Options", value: "DENY" },
          { key: "X-Content-Type-Options", value: "nosniff" },
          { key: "Referrer-Policy", value: "same-origin" },
          { key: "Permissions-Policy", value: "microphone=(self), camera=(), geolocation=(), payment=()" },
        ],
      },
    ];
  },
  // Real backend: same-origin proxy (ADR-006). Only active when a target is configured.
  // /hubs (SignalR lecture.v1) and /ingest (speech.v1 audio WebSocket) must be same-origin too: the API checks the
  // Origin header against its allow-list and the session cookie is SameSite=Lax (IdentityModule.cs). The proxy must pass WebSocket upgrades.
  // In a production build the rewrites are evaluated by `next build` and frozen into .next/routes-manifest.json, so
  // API_PROXY_TARGET is a BUILD-time value there (Docker build arg); setting it on the running container changes nothing.
  async rewrites() {
    const target = process.env.API_PROXY_TARGET;
    return target
      ? ["api", "hubs", "ingest"].map((p) => ({ source: `/${p}/:path*`, destination: `${target}/${p}/:path*` }))
      : [];
  },
};
export default nextConfig;
