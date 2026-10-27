/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  webpack(config, { isServer }) {
    // msw/browser is exported with "node": null; it is only ever loaded in the browser.
    if (isServer) config.resolve.alias["msw/browser"] = false;
    return config;
  },
  // Real backend: same-origin proxy (ADR-006). Only active when a target is configured.
  async rewrites() {
    const target = process.env.API_PROXY_TARGET;
    return target ? [{ source: "/api/:path*", destination: `${target}/api/:path*` }] : [];
  },
};
export default nextConfig;
