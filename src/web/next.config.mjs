/** @type {import('next').NextConfig} */
// Dev-only harness routes are files named page.dev.tsx. They are only routable outside production builds,
// or when TETHER_ENABLE_DEV_HARNESS=1 is set. Otherwise they are not in the route table at all.
const enableHarness = process.env.NODE_ENV !== "production" || process.env.TETHER_ENABLE_DEV_HARNESS === "1";
const nextConfig = {
  pageExtensions: enableHarness ? ["tsx", "ts", "jsx", "js", "dev.tsx"] : ["tsx", "ts", "jsx", "js"],
  reactStrictMode: true,
  webpack(config, { isServer }) {
    // msw/browser is exported with "node": null; it is only ever loaded in the browser.
    if (isServer) config.resolve.alias["msw/browser"] = false;
    return config;
  },
  // Real backend: same-origin proxy (ADR-006). Only active when a target is configured.
  // /hubs (SignalR lecture.v1) and /ingest (speech.v1 audio WebSocket) must be same-origin too: the API checks the
  // Origin header against its allow-list and the session cookie is SameSite=Strict. The proxy must pass WebSocket upgrades.
  async rewrites() {
    const target = process.env.API_PROXY_TARGET;
    return target
      ? ["api", "hubs", "ingest"].map((p) => ({ source: `/${p}/:path*`, destination: `${target}/${p}/:path*` }))
      : [];
  },
};
export default nextConfig;
