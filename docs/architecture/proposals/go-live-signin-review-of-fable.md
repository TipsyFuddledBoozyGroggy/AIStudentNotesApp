# Review (Opus) of Fable's half of ADR-022

Status: **REVIEW**, pairing protocol step 4. Reviewed in the working tree after commit 18c85df (uncommitted):
`infra/main.bicep`, `infra/poc.bicepparam`, `.github/workflows/deploy-azure-dev.yml`, `infra/scripts/{preflight,smoke,api-signin-mode}.sh`,
`infra/scripts/tests/*`, `scripts/azure/set-google-signin.ps1`, `scripts/azure/set-invites.ps1`, `infra/README.md`,
`docker-compose.poc-prod.yml`, `infra/local/*`. Skipped as asked: the `access=closed` workflow path, preflight's closed exemption, `ci.yml`.

**Run here:**
- `bash infra/scripts/tests/preflight-cases.sh`: 66 passed, 0 failed.
- `az bicep build` and `az bicep lint` on `main.bicep`: exit 0, no findings.
- **Not run:** `smoke-cases.sh`. It needs jq, which is not on this host, and Docker is reserved for QA.

**The Lead's dev-login wording (API_CONTRACTS_AUTH row 26, ADR row 32) is correct.** `AuthBoundaryTests` pins 401 for anonymous callers
and 404 for signed-in ones. One caveat worth adding: a POST without an allow-listed `Origin` gets 403 `csrf_failed`, because the Origin
check runs before routing. That is why the probe must send the web Origin, and it does (`smoke.sh:196`).

## Verdict
**No blocker.** In every transition I traced, the site is guarded either by Entra or by a Production API with Google sign-in and the
invite list. R2 is enforced in code. The outside probes match my API's real answers: the 401 path at `smoke.sh:272-285`, cookie attributes
compared in lower case at `:334`, the exact `Location` at `:354`, and the binder-expiry check accepts the `expires=Thu, 01 Jan 1970` that
ASP.NET writes (`:357`). I found 3 majors, 4 minors and 4 nits.

## Findings

| # | Sev | Where | Finding | Suggested fix |
|---|---|---|---|---|
| F1 | major | `deploy-azure-dev.yml:348-352`; `smoke.sh:130-137` | **A failed read counts as "Entra off" (fail-open on the rules, not an exposure).** `entra_on=false` is the default when `az rest` fails for any reason (throttling, transient 5xx), not only when no auth settings exist (404). An `invited` run without `gate_only` could then take the Microsoft gate off and deploy new code in one go, which the header forbids (line 31). In smoke part 2, `web_auth_enabled` prints `false` on any error, so "built-in auth is OFF" PASSES without evidence. The outside layer-gone wait still protects the gate itself. | Treat only HTTP 404 (`ResourceNotFound`) as off. Any other failure is `unknown`, which refuses `invited` in the live step and fails part 2 (`mode_failed=1`). |
| F2 | major | `deploy-azure-dev.yml:372-377`; README:86, :147 | **Rollback to `team` with `gate_only` is refused once main has moved past the live release.** It then needs the live SHA, looked up in the portal. S4.6 asks for one step within minutes. That is exactly the moment someone may have merged a fix. | For `access=team` with `gate_only`, adopt the live images instead of refusing, on two conditions: `api_mode` is `google` (the live image is a post-go-live Production image, so R2 holds) and `release_sha` is empty. Keep the strict comparison for `invited`. Rehearse it in Run R. |
| F3 | major | `smoke.sh:258-268`, `:272-285`, `:410` | **Invited probes can fail closed at random after the layer-gone wait.** The wait passes on the first answer that is not from Entra. If a later request still reaches a lingering Easy Auth replica or edge, its 401 has no problem code and probe 1 calls it a **breach** (exit 2, ingress closed, an alarming log). That is safe, but it is the most likely way for Run B to fail with no real cause. | Require N consecutive non-Entra answers in the wait (for example 3, 15 s apart). In probe 1, treat a 401 with no body and no problem code as "try again" (rc 1). Keep "breach" for a 401 that carries a different problem code or a Set-Cookie. |
| F4 | minor | `set-invites.ps1:242` | The invite list goes on a command line (`--secrets "invite-emails=$joined"`). Azure CLI also keeps per-command logs with their arguments under the profile's `commands` folder [A]. So the addresses persist at rest in `%USERPROFILE%\.azure-po\commands\`, outside the list file the README describes as "the only readable copy". This is local and outside the repo, so it is not a leak. | Say so in the script header and README, or have the script remove the matching command log afterwards. `az config set core.collect_telemetry=false` does not cover the log. |
| F5 | minor | `infra/README.md:75`, `:100` | The README says "Sign in with Google". The button reads **"Continue with Google"**: I changed it to S1 item 1's wording after an e2e name clash. A non-specialist will look for the exact words. | Change both lines. |
| F6 | minor | `main.bicep:488` (web resource) | **No `dependsOn: [api]` on the web app.** Fable's proposal had one. In a non-gate-only run the web and the API revisions roll out in parallel, so for a minute new web code can talk to the old API (for example, before `/auth/methods` exists the page shows its "can't reach" state). Not a gate issue. | Add `dependsOn: [api]`, or say in a comment why parallel is fine. |
| F7 | minor | `smoke.sh:394`, `:424` | A run that asked for `invited` but ended with `webIsPublic=false` takes the first branch, passes "not public", and writes `gate_verified=true`. That is not fail-open, but the summary then reads like success for a run that did not do what was asked. | When `GATE` is `google`/`google+ip` and `WEB_PUBLIC` is not `true`, fail (rc 1) with "invited was asked for but the web app is not public". |
| N1 | nit | `set-google-signin.ps1:213-224` | A second rotation overwrites `DP_CERT_PFX_PREVIOUS`, so keys wrapped two rotations ago stop opening and those cookies end. That is acceptable, since cookies last at most 14 days. | Say in the header "rotate at most once per 14 days". |
| N2 | nit | `set-google-signin.ps1:269-276` | The secret is written before the client id. If the second write fails, GitHub holds a mismatched pair until the script is re-run. The message says to re-run, so this is only noted. | Write the id first. |
| N3 | nit | `set-invites.ps1:240-252` | No guard against a deploy running at the same moment. The next deploy reapplies the GitHub copy, so the end state is right. | A one-line warning in the header. |
| N4 | nit | `deploy-azure-dev.yml:94`, `:140-153` | The `go-live-base` tag is trusted as given. If it is placed on a commit that still has dev login, the R2 check is defeated. | Add an extra check in the plan job: `git show $base:infra/main.bicep` must not contain `Auth__DevLogin__Enabled`. Cheap, and it pins R2 to content rather than to a tag. |

## Checked and fine
- **R1.**
  - The template has one literal, `ASPNETCORE_ENVIRONMENT=Production` (`main.bicep:417`), and no dev-login variable anywhere.
  - A missing Google, invite or certificate value fails both preflight (`:86-161`) and the Bicep guard (`main.bicep:210`).
  - `Auth__Google__ClientId` is tied to `guardsPassed`.
- **R2.**
  - `release_sha` must be on main and at or after `go-live-base`; with no tag it is refused (`deploy-azure-dev.yml:140-153`).
  - The deploy checks out `target_sha`, so infra and images always come from the same release.
  - `bicepparam` pins `tether_live`, so nothing points back at `tether`.
- **Transitions.** In each case the site is guarded the whole time, or the run stops or closes the ingress:

  | From | To | How it is guarded |
  |---|---|---|
  | dev era, Entra on (Run A) | team | One pass behind Entra |
  | team | invited | Allowed only when the live API is `google` and the run is `gate_only`; flipped in place, no internal window (S4.3) |
  | invited | team | Public throughout, guarded by the Production API, then `wait_for_signin`; a failure closes the ingress |
  | no proven gate | team | Two passes (internal first) |

  The fail-closed step covers every public attempt, including failed outputs (`deploy-azure-dev.yml:535-536`).
- **Section 2 conformance.**
  - Env and secret names match my options binding exactly: `Auth__Google__{ClientId,ClientSecret,RedirectUri,Admission,HostedDomainPolicy}`, `Auth__Invite__Emails`, `DataProtection__Certificate`, `DataProtection__PreviousCertificates__0`.
  - The secret names are `google-client-secret`, `invite-emails`, `dp-cert`, `dp-cert-previous`.
  - The redirect address string is identical in Bicep (`main.bicep:244`), the PO script (`:181`) and the smoke probe (`smoke.sh`, probe 4).
  - `api-signin-mode.sh` reads exactly the two facts (integration point 6) and fails closed on every doubt.
- **Logs.**
  - Invite entries are masked one by one before anything prints, and only counts are printed.
  - The client id is masked; the secure parameters are not echoed; `--output none` is used.
  - Azure error text is masked; smoke `detail` strings never hold a URL, a host or a cookie.
  - I found nothing that prints a secret, an address, an IP or the domain.
- **PO scripts.**
  - Hidden prompt with BSTR zeroing.
  - Values go to `gh` on stdin, not argv.
  - Service-principal guard in both scripts.
  - The certificate is created only when `DP_CERT_PFX` is absent (C1). Rotation copies the live certificate to `PREVIOUS`.
  - The invite file is refused inside the repository; `-List` prints the count only; an empty list is refused.
  - First run before the API has the setting: GitHub only, with a clear message.
  - The restart covers every active revision, and my startup revoke ends removed people's sessions.
  - Not checked (as instructed): the PFX from Windows PowerShell 5.1 at `set-google-signin.ps1:117-125`.
- **README.** A non-specialist can follow it: cmd lines are copy-paste, there is one table for the four runs with what to check, and
  rollback and emergency stop are each one line. Only F5 and F2's consequence (README:147) need changing.

## Recommendation
Fix F1 to F3 before Run B, and F5 before the PO does Run A. The minors and nits can follow.
