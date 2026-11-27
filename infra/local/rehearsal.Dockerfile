# LOCAL ONLY. A small tool image with what the infra scripts and their case tests need: bash, curl, jq (the outside
# probes of infra/scripts/smoke.sh), plus node and openssl (the stand-in site and throwaway certificate of
# infra/scripts/tests/smoke-cases.sh). Nothing from the repository is copied in; the scripts are mounted read-only.
#
# Used two ways:
#   1. docker-compose.poc-prod.yml, service `rehearsal`: the six invited probes against the local Production stack.
#   2. the smoke case tests, with no network at all:
#        docker build -t tether-smoke-rehearsal:local -f infra/local/rehearsal.Dockerfile infra/local
#        docker run --rm --network none -v "<repository folder>:/repo:ro" -w /repo tether-smoke-rehearsal:local infra/scripts/tests/smoke-cases.sh
#
# alpine:3.22 resolved 2026-11-27 to this multi-arch index digest (pinned like the other images in this repository).
FROM alpine:3.22@sha256:49340b346798875b5592e5d420241f2f9cfba5f47e62ebb1446a1b34734e898f
RUN apk add --no-cache bash curl jq coreutils nodejs openssl
USER 1000
ENTRYPOINT ["bash"]
