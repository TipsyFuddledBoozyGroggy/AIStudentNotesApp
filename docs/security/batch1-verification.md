# Batch 1 verification (commit 096ba8e)

Date 2026-10-29. Reviewer: security-engineer. Read the diff with `git show`, the real code and the tests; no source or test file in the repo was changed. Probes and mutations ran in a throwaway copy under the session scratchpad (never in the repo). The dev API container was not touched.

## What I ran
`docker compose -f docker-compose.yml -f docker-compose.dev.yml run --rm dotnet dotnet test src/api/Tether.slnx`: exit 0. ContractTests 830, SyllabusTests 99, AiTests 241, ArchitectureTests 22, IntegrationTests 534; 0 failed, 0 skipped.

Scratch copy runs (evidence for the findings below, not committed): log mutations of Program.cs, a provider-scoped logging probe, validator probes, a lone-surrogate probe, a Host-header probe. One of my own probes first failed to compile (a raw-string literal) and I fixed it.

## Results per item

| Item | Result | Evidence |
|---|---|---|
| P2B1-02 log canary | FIXED-VERIFIED for the original scope; new MEDIUM B1V-01 | Tests LogPipelineCanaryTests.cs:97 and :155 pass. Mutations: all filters removed fails (Hosting.Diagnostics URL with query token, EF Update); removing only Hosting.Diagnostics fails; removing only ExceptionHandlerMiddleware+EF Update fails; removing the EF Database.Command/Query/Npgsql group fails only on the structural 'no Command records' assertion (line 57); removing only Query+Npgsql.Exception+Npgsql.Connection, or only Npgsql.Command, PASSES (not proven). |
| B1-11 root secret / HKDF | FIXED-VERIFIED; residuals LOW | RootSecretTests.cs (HKDF vs hand-computed RFC 5869 at :37, labels distinct :54, Test/Testing/Staging refuse at :71, short :81, undecodable not echoed :90, samples :105/:119, dev persisted 0600 :126, host refuses to start :178/:188, secret never logged :214) all pass. Only IsDevelopment() unlocks the fallback (RootSecret.cs:68); no 'Test' escape here. Messages never include the secret (tested). |
| B1-10 NFC / ICU | FIXED-VERIFIED | TextAndIdTests.cs:171 now asserts the composed value and ICU on; guard at SyllabusModule.cs:29 (tested :322). I grepped src for culture-sensitive string calls (StartsWith/EndsWith/IndexOf without comparison, ToLower/ToUpper, string.Compare) and for IgnoreCase regexes without CultureInvariant: none besides Guid.CompareTo. Container culture is invariant unless LANG is set. Downside found: B1V-07. No Dockerfile exists, so the ICU/tzdata image requirement is only enforced by the startup guards (fail closed). |
| B1-14 DST / tzdata | FIXED-VERIFIED | TextAndIdTests.cs:335,342,351. The missing-tzdata case uses an injected resolver, not a real image. |
| M1R-N01 trusted proxies | FIXED-VERIFIED | EdgeConfigTests.cs:22,35,42,108. Probe: ::/32 is accepted but does not match IPv4 clients (no bypass); public 1.0.0.0/8 accepted (B1V-08). |
| M1R-N02 AllowedHosts / HSTS | FIXED-VERIFIED | EdgeConfigTests.cs:56,89,98,128. Probe: Production, Host evil.test and localhost.evil.test get 400; localhost and api.example.test get 200 with HSTS. HSTS is unconditional outside Development (Program.cs:171). |

## New findings

**MEDIUM B1V-01 Provider-scoped logging config defeats the code filters.** `builder.Logging.AddFilter(category, None)` creates rules with no provider name. In .NET's rule selection, a rule scoped to a provider beats one that is not, whatever the category. So `Logging:Console:LogLevel:Default=Debug` (a common troubleshooting setting; also Debug and EventSource providers) turns Hosting.Diagnostics, ExceptionHandlerMiddleware and EF Update back on. Reproduction: scratch test with only `Logging:<provider>:LogLevel:Default=Trace` for the capture provider, same traffic as the canary: 9 records carry sentinels, including `Request starting HTTP/1.1 GET .../courses?token=CANARY-02-QUERY-TOKEN-91e3...` and the EF Update exception with the failing row. I used the capture provider's type name, not the real Console provider; the selection rule is the same. Impact: student text and tokens in logs after an operator edit. Fix: add the same None/Warning rules per registered provider (or rewrite LoggerFilterOptions.Rules in a PostConfigure so no provider rule is more permissive), and add a canary case with a provider-scoped rule. The comment at Program.cs:29-31 and the commit message overstate the control. Blocks production, not push.

**LOW findings** (details in the register): B1V-02 canary coverage gaps; B1V-03 explicit key overrides skip all strength checks; B1V-04 denylist-only sample detection (probe accepted bytes 0..31, an alphabet string and a repeating pattern; the public test key is also accepted); B1V-05 no rotation story or operator docs; B1V-06 dev fallback file edge cases; B1V-07 Canonicalize throws under ICU on a lone surrogate (latent, HTTP gave 400); B1V-08 lenient edge validators and unconditional HSTS includeSubDomains.

Checked and found fine: app code logs exception type and trace id only (grep of Log* calls in src); the 500 path logs no exception object; ETag key and sub-keys come from HKDF; shared label etag.v1 across modules is harmless because the MAC input differs per resource.

## Not verified
A real Console/Debug provider (I used a capture provider with a provider-scoped rule); Kestrel logs (tests use TestServer); a real image without ICU or tzdata; HOME-unset behaviour of the dev secret path; rotation; forwarded headers through a real proxy; web tests; CI; scanners.

## Open counts after this batch
CRITICAL 0, HIGH 1 (GR-1, unchanged, push still blocked), MEDIUM 27, LOW 51 not resolved. Register greps print 1, 27 and 51 rows for HIGH, MEDIUM and LOW respectively. This verification clears no release gate.
