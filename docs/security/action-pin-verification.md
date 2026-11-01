# GitHub Actions pin verification (SBR-01)

Retrieved: **2026-10-31** (all checks below, by the vendor-verification-researcher). Scope: every third-party `uses:` in `.github/workflows/ci.yml` and `codeql.yml` (9 distinct pins, 29 `uses:` lines). Workflow files were **not** edited. Nothing was committed.

Method, all primary sources:
- `git ls-remote` against each official repo over HTTPS to read the live tag-to-SHA mapping.
- Bare/blobless clones of each repo, to check the SHA exists, which tag points at it, whether it is in default-branch history, and its commit date.
- Each repo's GitHub **Security Advisories** page, plus the Trivy ecosystem advisory GHSA-69fq-xp46-6x23 / CVE-2026-33634 and the trivy-action v0.35.0 release page.

Limits, stated plainly:
- "Tag points at SHA today" is **weak evidence**. A force-pushed tag can be moved and later restored. What the checks add is that each SHA is a real commit in the official repo's default-branch history, with a plausible date and message, and not an orphan or fork-only commit.
- I did not read the action source code line by line for backdoors. "VERIFIED" means authenticity of the pin, not a code audit.
- The GitHub web UI renders for advisories were read through a summarising fetch tool. Where exact wording matters (the Trivy advisory), the quotes are from that tool's extraction, not a raw page copy. Re-read the advisory yourself before citing it externally.
- I can't see GitHub's API rate-limited data, repository "imposter commit" fork-network lookups, or private advisories. A SHA that is in default-branch ancestry cannot be an imposter commit, which is why that check was used.
- The repo's CI has **never run on GitHub** (ci.yml header), so no run could have executed an affected action. The pins were read on 2026-10-31, after the incident window.

## Verdict table

| Action | Pinned SHA | Claimed tag | SHA exists, official repo | Tag resolves to SHA today | Verdict | Advisory / incident |
|---|---|---|---|---|---|---|
| actions/checkout | `11bd71901bbe5b1630ceea73d27597364c9af683` | v4.2.2 | yes, on `main`, 2024-10-23 "Prepare 4.2.2 Release (#1953)" | yes (lightweight tag) | **VERIFIED** | None published on repo advisories page |
| actions/setup-node | `39370e3970a6d050c480ffad4ff0ed4d3fdee5af` | v4.1.0 | yes, on `main`, 2024-10-23 | yes | **VERIFIED** | None published |
| actions/setup-dotnet | `3e891b0cb619bf60e2c25674b222b8940e2c1c25` | v4.1.0 | yes, on `main`, 2024-10-21 | yes | **VERIFIED** | None published |
| actions/cache | `1bd1e32a3bdc45362d1e726936510720a7c30a57` | v4.2.0 | yes, on `main`, 2024-12-05 | yes | **VERIFIED** | None published |
| actions/upload-artifact | `65c4c4a1ddee5b72f698fdd19549f0f0fb45cf08` | v4.6.0 | yes, on `main`, 2025-01-09 | yes | **VERIFIED** | None published |
| actions/dependency-review-action | `3b139cfc5fae8b618d3eae3675e383bb1769c019` | v4.5.0 | yes, on `main`, 2024-11-20 | yes | **VERIFIED** | None published |
| github/codeql-action (init, analyze) | `48ab28a6f5dbc2a99bf1e0131198dd8f1df78169` | v3.28.0 | yes, on `main`, 2024-12-20 | yes (annotated tag, `^{}` peels to the SHA) | **VERIFIED as authentic, but replace** | GHSA-vqf5-2xx6-9wfm (High, 2025-01-24) affects Action 3.26.11 to 3.28.2; **v3.28.0 is in range**. v3 deprecated December 2026 |
| aquasecurity/trivy-action | `6c175e9c4083a92bbca2f9724c8a5e33bc2d97a5` | v0.30.0 | yes, on `master`, 2025-03-13 "chore: bump trivy to v0.60.0 (#453)" | yes (`v0.30.0`, lightweight) | **VERIFIED as authentic, but replace (broken and in an affected range)** | **Confirmed**: CVE-2026-33634 / GHSA-69fq-xp46-6x23, Critical, March 2026 |

There are **no MISMATCH** results and no UNVERIFIABLE pins. Two pins are authentic but should be replaced, for reasons below.

## aquasecurity/trivy-action (the SBR-01 question)

### The reviewer's recollection is confirmed
- Advisory: **"Trivy ecosystem supply chain was briefly compromised"**, GHSA-69fq-xp46-6x23 / CVE-2026-33634, severity Critical (CVSS v4 9.4, CWE-506). URL: https://github.com/advisories/GHSA-69fq-xp46-6x23 (also https://github.com/aquasecurity/trivy/security/advisories/GHSA-69fq-xp46-6x23). Published 2026-03-21, NVD 2026-03-23, last updated 2026-03-30. Retrieved 2026-10-31.
- Per the advisory: an attacker force-pushed **76 of 77** trivy-action version tags (and all setup-trivy tags) to malicious commits. trivy-action exposure was about 2026-03-19 17:43 UTC to 2026-03-20 05:40 UTC. Trivy binary v0.69.4 was malicious (about 18:22 to 21:42 UTC on 03-19). Docker Hub images 0.69.5 and 0.69.6 were also malicious (2026-03-22 to 03-23).
- Affected `trivy-action`: all tags before **0.35.0** (0.0.1 to 0.34.2). `v0.30.0` is inside that range. `0.35.0` was not affected because it "is protected by GitHub's immutable releases feature".
- Aqua deleted the original unprefixed tags (`0.0.1` to `0.34.2`) and published **new `v`-prefixed tags** (`v0.0.1` to `v0.34.2`) "pointing to the original legitimate commits". `v0.0.10`, `v0.34.1` and `v0.34.2` were not restored at the time of the advisory.
- Remediation in the advisory: use trivy-action `v0.35.0`, setup-trivy `v0.2.6`, Trivy binary v0.69.2 or v0.69.3; treat secrets of any pipeline that ran an affected version as exposed; **pin to full commit SHAs**.
- Release page https://github.com/aquasecurity/trivy-action/releases/tag/v0.35.0 (retrieved 2026-10-31): marked "Immutable release", commit `57a97c7e7821a5776cebc9bb87c984fa69cba8f1`, verified signature; says 0.35.0 "was not compromised" and workflows using it are safe.
- Secondary corroboration only (not relied on): NHS England cyber alert, db.gcve.eu CVE record.
- Not confirmed from a primary source: the malicious trivy-action commit SHAs. The advisory does not list them.

### Is the repo's pin affected?
- **The SHA itself is clean.** `6c175e9c...` is the commit "chore: bump trivy to v0.60.0 (#453)", authored 2025-03-14, an ancestor of current `master`. That is about a year before the incident, in the legitimate history, and not an imposter commit. `git ls-remote` on 2026-10-31 shows `v0.30.0` -> `6c175e9c...`, which matches the restored tag.
- The pin was read on 2026-10-31, after restoration, and CI has never run, so no execution happened during the 2026-03-19/20 window. Because it is a SHA pin, it would not have followed the force-pushed tags even if CI had run then. **Residual risk from the incident to date: none identified.**
- **But the pin is broken, which is a new finding.** At `6c175e9c`, `action.yaml` line 125 has `uses: aquasecurity/setup-trivy@v0.2.2`, a mutable tag inside the pinned action. The advisory says setup-trivy tags `v0.2.0` to `v0.2.5` "were not restored". `git ls-remote --tags` on `aquasecurity/setup-trivy` on 2026-10-31 lists only `v0.2.6`, `v0.3.0`, `v0.3.1`. So the pinned action refers to a tag that no longer exists. **Inference, not executed:** each Trivy step will fail at action resolution time (fail closed). No run has confirmed this. It also means the SBR-01 hardening of this job cannot be demonstrated until the pin changes.
- Same action also uses mutable `actions/cache@v4` inside (line 138). The `cache: false` input in ci.yml disables that path, per the existing SBR-01 comment, but the reference is still mutable and is resolved when the composite action loads.
- The default Trivy binary at that commit is `v0.60.0`, downloaded by setup-trivy at run time (version input). Not one of the compromised versions, but old (will lack newer vuln DB features).

### Recommended replacement
Preferred: **`aquasecurity/trivy-action@57a97c7e7821a5776cebc9bb87c984fa69cba8f1  # v0.35.0`**
- Vendor-named safe version, immutable release, signed commit, in `master` history, tag `v0.35.0` currently resolves to it (verified 2026-10-31 by `ls-remote` and by clone).
- `action.yaml` there pins its nested actions by SHA: `setup-trivy@e6c2c5e321ed9123bda567646e2f96565e34abe1 # equivalent to v0.2.4` and `actions/cache@0400d5f644dc74513175e3cd8d07132dd4860809 # v4.2.4`. Default Trivy `v0.69.3` (named safe by the advisory).
- Caveat: the setup-trivy SHA `e6c2c5e...` is in setup-trivy `main` history (2025-08-29, "pin actions/checkout and actions/cache (#22)") but its comment says "v0.2.4", a tag the advisory lists as unrestored. The advisory's own safe reference for setup-trivy is v0.2.6. The vendor's safe release uses the SHA anyway; the advisory deems `trivy-action` 0.35.0 safe. I could not independently audit setup-trivy's code at that SHA. Treat as vendor-endorsed, not independently audited.

Alternative: v0.36.0, tag object `a9c7b0f06e461e9d4b4d1711f154ee024b8d7ab8`, peeled commit **`ed142fd0673e97e23eac54620cfb913e5ce36c25`** (2026-04-22). It pins `setup-trivy@3fb12ec12f41e471780db15c232d5dd185dcb514 # v0.2.6` (the advisory's safe setup-trivy), default Trivy v0.70.0, `actions/cache` v5.0.5. I did **not** verify that v0.36.0 is an immutable release or read any release notes for it. Use only after you review it.

Do not use the `master` branch, or any tag, as the pin. Pin the commit SHA. Also keep the existing SBR-01 isolation (PR-only job, no cache), which this finding does not make unnecessary: the runtime cache token exposure is a property of any third-party action.

Longer term (already noted in ci.yml): replacing the action with a checksum-verified Trivy binary or a digest-pinned container (`aquasec/trivy@sha256:...`) removes the third-party action entirely. The advisory says images pinned by digest were not affected.

## github/codeql-action (init and analyze)
- SHA `48ab28a6...` = tag `v3.28.0` (annotated; peeled value matches), on `main`, 2024-12-20.
- Advisory **GHSA-vqf5-2xx6-9wfm** "GitHub PAT written to debug artifacts", High, published 2025-01-24 (https://github.com/github/codeql-action/security/advisories/GHSA-vqf5-2xx6-9wfm; retrieved 2026-10-31). Affected CodeQL Action `>= 3.26.11 and <= 3.28.2`; patched **3.28.3**. v3.28.0 is therefore in the affected range.
- **Exploitability here is low, by the advisory's own conditions:** it requires debug artifacts enabled, a repository with Java/Kotlin (Kotlin extractor logging environment variables), and a run failing before database finalisation. This repo's CodeQL languages are C# and JavaScript/TypeScript (confirm in `codeql.yml`; I did not re-check which languages are configured) and debug artifacts are not requested. Practical impact: none identified. It is still a known-vulnerable pin and will be flagged by scanners.
- **Deprecation:** CodeQL Action v3 is scheduled for deprecation in December 2026 (https://github.blog/changelog/2025-10-28-upcoming-deprecation-of-codeql-action-v3/, retrieved 2026-10-31); v4 released 2025-10-07 on Node 24.
- Recommended replacement: `github/codeql-action/init` and `/analyze` at **`9b3254085494b55a0f7c4c2b3d2d52cc712ce370  # v4.39.1`** (peeled commit of tag `v4.39.1`, current at retrieval; ls-remote 2026-10-31). Use the same SHA for both steps. Check `v4.39.1` is still the newest, and read the v4 release notes for changed inputs, before adopting. Minimum acceptable if staying on v3: `dd196fa9ce80b6bacc74ca1c32bd5b0ba22efca7  # v3.28.3`, but that is a short-lived option.
- I did not verify the v4.39.1 SHA beyond `ls-remote` peeling and the clone. Mark it UNVERIFIED for release-note content.

## actions/* official actions
Each official repo's Security Advisories page (retrieved 2026-10-31) states "There aren't any published security advisories": checkout, cache, upload-artifact, setup-node, setup-dotnet, dependency-review-action. No web search result documented a tag-hijack of these. I found 2026 tag-poisoning incidents involving other third-party actions (xygeni/xygeni-action GHSA-f8q5-h5qh-33mh March 2026; actions-cool/issues-helper May 2026, per StepSecurity, secondary), none of which this repo uses.

Observations, not defects:
- All six are 10 to 22 months old and several majors behind (checkout, setup-node, upload-artifact, cache have newer majors). They are pinned and authentic. Dependabot (github-actions ecosystem) is configured to propose bumps; review each bump's SHA the same way as here.
- `actions/dependency-review-action` v4.5.0 runs on Node 20. GitHub has been moving runners to Node 24 (the codeql v4 notice cites Node 24); I did not verify an end date for Node 20 actions. Check before it bites.
- None of the official pins are marked as immutable releases that I could confirm; I did not check.

## Recommended changes (for the Lead Architect to apply; I did not edit workflows)
1. `ci.yml` (3 steps): `aquasecurity/trivy-action@57a97c7e7821a5776cebc9bb87c984fa69cba8f1  # v0.35.0`. Re-check the `cache: false`, `scan-type`, `image-ref`, `severity`, `ignore-unfixed` and `exit-code` inputs are still accepted by v0.35.0 (not tested; the input list at v0.35.0 was not diffed).
2. `codeql.yml` (init and analyze): `9b3254085494b55a0f7c4c2b3d2d52cc712ce370  # v4.39.1`.
3. Update `ci.yml` line 5-7 comment and `docs/ops/ci-matrix.md`: the SHAs were cross-checked against clones and advisories on 2026-10-31, not just read from tags.
4. The first real GitHub run is the only thing that will confirm the pins resolve. Treat G5 as NOT_RUN until a run record exists.
5. SBR-01 can be downgraded from "pin unverified" to "pin verified, replacement pending" once step 1 lands. The cache-token exposure half remains until the action is replaced by a checksum-pinned binary or digest-pinned image.

## Open items (honest unknowns)
- Malicious trivy-action commit SHAs: not published in the advisory, so I could not rule out by SHA that `6c175e9c` is one. Ancestry in `master` and its 2025-03 date make it very unlikely (the attacker's commits were imposter commits, per the advisory's description of 1885610c and 70379aad for the Trivy release).
- Whether the nested `setup-trivy@v0.2.2` fails exactly as inferred: not executed.
- Source code of any action was not audited.
- Whether v0.36.0 is immutable and what changed in v4.39.1: not checked.
