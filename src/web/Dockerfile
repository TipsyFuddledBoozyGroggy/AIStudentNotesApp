# syntax=docker/dockerfile:1@sha256:572cebbc2b15e6f84c077742952674889cb63cd5e9b80abdffcbe587b6f19183
# (docker/dockerfile:1 resolved 2026-11-26, multi-arch index digest. Pinned like the base images: the frontend runs the whole build.)
# Tether web (Next.js standalone server). Build context: src/web
#   docker build --build-arg API_PROXY_TARGET=http://api:8080 -t tether-web:local src/web
#
# API_PROXY_TARGET is REQUIRED and is baked in at build time: `next build` evaluates the /api, /hubs and /ingest rewrites
# (next.config.mjs) into .next/routes-manifest.json, and the production server reads that file, not the environment.
# One image therefore targets one API address. Use a stable internal name (for example the API container app's name).
# Nothing secret belongs in it: it is an internal URL, and the API is never reached by the browser directly.

# node:22-alpine resolved 2026-11-26 to Node 22.23.5, multi-arch index digest. Node 22 matches CI (NODE_VERSION in ci.yml).
FROM node:22-alpine@sha256:1314fb30ec37592d5845e5a160d522c0e680972aa534549940b8d73bde88481a AS deps
WORKDIR /app
COPY package.json package-lock.json ./
# Dev dependencies are needed to build (TypeScript, Tailwind, PostCSS, ESLint); none of them reach the runtime stage.
RUN --mount=type=cache,id=tether-npm,target=/root/.npm npm ci --no-audit --no-fund

FROM node:22-alpine@sha256:1314fb30ec37592d5845e5a160d522c0e680972aa534549940b8d73bde88481a AS build
WORKDIR /app
ARG API_PROXY_TARGET
ENV NEXT_TELEMETRY_DISABLED=1 \
    NEXT_PUBLIC_API_MOCKING=disabled \
    API_PROXY_TARGET=${API_PROXY_TARGET}
COPY --from=deps /app/node_modules ./node_modules
COPY . .
RUN test -n "$API_PROXY_TARGET" || { echo "Build arg API_PROXY_TARGET is required (for example http://api:8080)." >&2; exit 1; } \
 && npm run build \
 && grep -q "$API_PROXY_TARGET/hubs" .next/routes-manifest.json \
 && rm -f public/mockServiceWorker.js
# The MSW worker script is only for local mock mode (NEXT_PUBLIC_API_MOCKING=enabled); it is dropped so the public image
# does not serve a service worker at /mockServiceWorker.js. The mock handlers themselves still exist as one lazy chunk
# under /_next/static (webpack emits it even though nothing loads it); it holds synthetic demo data only.

# Runtime: only the standalone server, its traced node_modules, static assets and public/. No npm, no source, no dev deps.
FROM node:22-alpine@sha256:1314fb30ec37592d5845e5a160d522c0e680972aa534549940b8d73bde88481a AS runtime
WORKDIR /app
ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    HOSTNAME=0.0.0.0 \
    PORT=3000
# The base image's package managers are never used at run time and carry their own CVEs (Trivy, 2026-11-26: 10 HIGH, all
# inside the bundled npm CLI). Remove them so the shipped image is node + server.js only.
RUN rm -rf /usr/local/lib/node_modules/npm /usr/local/lib/node_modules/corepack \
      /usr/local/bin/npm /usr/local/bin/npx /usr/local/bin/corepack /usr/local/bin/yarn /usr/local/bin/yarnpkg /opt/yarn-*
# Root-owned files; the server runs as the image's unprivileged "node" user (uid 1000) and needs no write access.
COPY --from=build /app/.next/standalone ./
COPY --from=build /app/.next/static ./.next/static
COPY --from=build /app/public ./public
EXPOSE 3000
USER 1000
CMD ["node", "server.js"]
