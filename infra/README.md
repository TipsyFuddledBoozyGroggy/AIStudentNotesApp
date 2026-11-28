# Tether on Azure: deployment and go-live with Google sign-in

First written 2026-11-26 (the proof of concept went live behind Microsoft sign-in that day). Reworked 2026-11-27 for ADR-022 (`docs/architecture/go-live-signin-adr.md`): invite-only Google sign-in, the API always in Production. **The ADR-022 version was first deployed on 2026-11-27 in Run A (access=team, run 46083406628). The invited and rollback runs have not happened yet.** Providers are mocks only: no AI model and no speech service is created. No release gate changes (G1 stays BLOCKED, G5 stays NOT_VERIFIED).

## What gets created (all inside the resource group `tether-dev`, West US 3)

| Thing | Purpose |
|---|---|
| Container Apps environment (pay per use) | Runs the two containers |
| `tether-web` container app | The website. The only public entry |
| `tether-api` container app | The API. Internal only: never reachable from the internet. Always runs as Production |
| PostgreSQL 16 server (smallest size, 32 GB) | The database. TLS required. The site uses a new, empty database named `tether_live` |
| Log Analytics workspace | Container logs, capped at 0.15 GB a day |

The images are built by GitHub and stored as **public** packages on ghcr.io (the repository is already public and no secret is inside an image). Secrets live in GitHub (environment `azure-dev`) and are copied into Container Apps secrets on each deploy. Nothing new costs money: Google sign-in, the invite list and the sign-in certificate add no Azure resource.

## Who can get in

Two things together decide it:

1. **Google sign-in, invite-only.** A person signs in with a Google account. Google lets in only the accounts you list as "Test users" in the Google Cloud console. Tether then lets in only the addresses on **its own invite list**. A person must be on both. Anyone else sees "not on the invite list" and nothing is saved.
2. **The access mode of the site**, chosen each time the deploy workflow is run:

| Access | What a visitor meets | When |
|---|---|---|
| `team` | Microsoft sign-in first (only people you assigned), then Google sign-in | The first Production run, and the **way back** if anything looks wrong |
| `invited` | Google sign-in only | After you have tried it yourself behind the Microsoft gate |
| `closed` | Nothing: the website has no public address | The emergency stop |

There is **no developer login in Azure any more**, in any mode. The deploy template cannot produce one, and each run checks from outside that it does not exist.

An optional extra layer for either public mode: a list of allowed IP addresses (GitHub environment secret `ALLOWED_IP_RANGES`, comma separated, IPv4, `/16` to `/32`). On its own it never opens the site.

## Your steps for go-live (Product Owner)

Everything below is typed in **cmd**, in the repository folder. If Windows says "running scripts is disabled", the commands below already work around it. Do not use PowerShell ISE (it cannot answer the prompts).

Before the first time on this machine, sign in once with your own Azure profile (not the default one, which belongs to the agents' read-only identity) and to GitHub:

```
set "AZURE_CONFIG_DIR=%USERPROFILE%\.azure-po" && az login --tenant <your tenant id>
gh auth login
```

Your tenant id is in the Azure portal: **Microsoft Entra ID, Overview, "Tenant ID"**. Easier: skip this and start the script in step 1. If you are not signed in yet it stops, changes nothing, and prints this sign-in line with your tenant id already filled in.

### Step 1. Google sign-in values (once)

```
set "AZURE_CONFIG_DIR=%USERPROFILE%\.azure-po" && powershell -ExecutionPolicy Bypass -File .\scripts\azure\set-google-signin.ps1
```

The script walks you through it:

1. It prints **one web address** ending in `/api/v1/auth/google/callback`. In a browser open the Google Cloud console, choose the project **tether-oauth-dev** at the top, open **Google Auth Platform, Clients, Tether**. Under **Authorized redirect URIs** press **Add URI**, paste the address exactly, **Save**. You do not need a localhost address; if one is there, leave it.
2. Still in the console: **Audience, Test users, Add users**. Add each Google account you want to let in (at most 100). The page must still say **Testing**. **Do not press "Publish app"**: that would let any Google account reach Tether's own check.
3. Back in the script, press Enter. Paste the **Client ID** from the Tether client page.
4. Paste the **Client secret** (what you type is hidden). The console no longer shows an old secret: if you do not have it, press **Add secret** on the client page and copy the new one. Never paste the secret into a chat or a file.
5. Type YES. The script stores both in GitHub. The first time it also creates the certificate that keeps people signed in across restarts. Nobody sees the secret or the certificate, and neither is written to disk.

To see the address again later: add `-ShowRedirectAddressOnly` to the command.

### Step 2. The invite list

```
set "AZURE_CONFIG_DIR=%USERPROFILE%\.azure-po" && powershell -ExecutionPolicy Bypass -File .\scripts\azure\set-invites.ps1 -Add you@example.com
```

Use the Google address you will sign in with. Until "download my data" and "delete my account" exist, the list should hold **only your own address** (PO-20ax). The list is kept in `%USERPROFILE%\.tether\invited-emails.txt` on your machine, outside the repository; the script only ever prints how many addresses there are, and never puts them on a command line. Do not run it while a deploy is running. Before the first Production deploy it says the list "applies on the next deploy"; that is expected.

### Step 3. Four runs, each approved by you

Each run: GitHub, **Actions**, **Deploy Azure dev (POC)**, **Run workflow** (branch `main`), choose the values below, then approve it when GitHub asks. The Lead tells you before each approval what the run changes and how to undo it.

| Run | Access | "Change the access mode only" | What changes | What you check afterwards |
|---|---|---|---|---|
| **A** | `team` | not ticked | The API becomes Production with Google sign-in, on the new empty database. The Microsoft gate stays in front. Your earlier test courses are no longer shown. | Microsoft sign-in, then the button **Continue with Google**: you land signed in. A second Google account that is not invited is refused with a plain message. The sign-in page has no developer form. Sign out, sign in again. Tell the Lead what Google showed you. |
| **B** | `invited` | ticked | The Microsoft gate comes off. Nothing else. | In a private window or on your phone: the Tether sign-in page appears with no Microsoft prompt; Google sign-in works. |
| **R** | `team` | ticked | The rollback, done once on purpose and timed: the Microsoft gate goes back on. | The Microsoft prompt is back; after it, your Google account still works and your data is still there. Note how many minutes it took. |
| **B'** | `invited` | ticked | The Microsoft gate comes off for good. | As after B. Only now tell the people you invited. |

Run B is refused unless Run A is live and working as a Production API. A run with "Change the access mode only" ticked keeps the release that is live and takes a few minutes; it restarts the site once (about a minute).

## Later

- **Add or remove a person:** add or remove them under Test users in the Google console, then one command: `... -File .\scripts\azure\set-invites.ps1 -Add person@example.com` (or `-Remove`). If the site is running it is updated at once and the API restarts (about a minute; people stay signed in, a lecture that is recording pauses itself, a removed person is signed out). `-List` shows how many are on the list and where the file is.
- **An ordinary deploy** (new code): run the workflow with the access the site has now, nothing ticked, approve. The default is `team`: if the site is on `invited`, choose `invited`, otherwise the Microsoft gate comes back (safe, but your invitees would meet a Microsoft prompt).
- **Something looks wrong:** run the workflow with `team` and "Change the access mode only" ticked. That puts the Microsoft gate back in front of the same site; Google accounts and data are untouched. This is the rollback, and it is always this one step: leave "release_sha" empty, and if newer code has been merged since the last deploy the run simply keeps what is live.
- **Emergency stop:** run the workflow with `closed` and approve it. The website loses its public address within a few minutes. This run changes one setting only, the website's "reachable from the internet" switch. It builds nothing, deploys nothing and does not touch the API, the database or anyone's data, and it works even if a secret is missing or broken (it needs only the three Azure ids). The other two boxes are ignored. Nobody is signed out; they simply cannot reach the site. To open it again, run `team` (or `invited`). If the workflow itself cannot run: Azure portal, container app `tether-web`, Ingress, untick "Accepting traffic from anywhere".
- **An older release:** the "release_sha" box takes the full commit id of an earlier release. The workflow refuses anything from before go-live (the tag `go-live-base`), because those releases have the developer login and must never run against the database that holds Google accounts.
- **Change the Google client secret:** run step 1 again. Nobody is signed out.
- **Replace the sign-in certificate** (rare, on purpose): `... -File .\scripts\azure\set-google-signin.ps1 -RotateKeyRingCertificate`, then a deploy. The old certificate is kept alongside so people stay signed in.
- **Remove the Microsoft sign-in registration:** only after B' has been fine for a while, and only while the site is on `invited`: `.\scripts\azure\create-signin-app.ps1 -Remove`. After that `team` is not available until you run that script again without `-Remove`.
- The Microsoft sign-in secret lasts one year; `create-signin-app.ps1` renews it (then deploy).

## What each run checks by itself

Before touching Azure: every value the run needs is present and well formed (the list is reported by count only; `closed` needs only the Azure ids). After deploying, from Azure: both apps are healthy, the API really runs as Production with no developer-login setting, and the Microsoft gate is on or off as asked. Then from outside, as a stranger:

- `closed`: Azure reports the website as internal, and from outside the site does not answer. Nothing else is checked.
- `team`: an anonymous visitor is sent to Microsoft sign-in.
- `invited`: the developer login does not exist; anonymous requests to the app are refused; "Continue with Google" would send the browser to Google with this site's own return address; a made-up return from Google is turned away; the sign-in page loads over HTTPS only.

If a run that opens the site does not end with those checks passed (a check failed, never got a clear answer, or the run was cancelled), **the run closes the website again**. Running `team` again opens it behind the Microsoft gate.

What no run can check: that a real person can sign in with Google and use the app. That is your check after each run.

## Estimated monthly cost

Prices fetched **2026-11-26** from the public Azure Retail Prices API (`GET https://prices.azure.com/api/retail/prices?$filter=...`, no sign-in), region `westus3`, USD, pay-as-you-go, before tax. An estimate, not a quote. A month is 730 hours. ADR-022 adds no billed resource.

| Item | Price used | Month |
|---|---|---|
| PostgreSQL `B1MS` compute | $0.017 per hour | $12.41 |
| PostgreSQL storage, 32 GB (the minimum) | $0.115 per GB per month | $3.68 |
| API container, always on, 0.25 vCPU and 0.5 GiB, billed at the idle rate when unused | vCPU $0.000003 idle / $0.000024 active per second; memory $0.000003 per GiB-second; after the free 180,000 vCPU-seconds and 360,000 GiB-seconds | $4.78 to $7.05 |
| Web container, sleeps when unused (assumed 60 hours of use) | same rates | included above |
| Log Analytics | first 5 GB a month free, then $2.30 per GB; capped at 0.15 GB a day | $0.00 |
| Image registry (ghcr.io, public packages) | free | $0.00 |
| **Expected total** | | **$20.87 to $23.14** |
| Worst case: Azure never counts the API as idle | vCPU at the active rate all month | **$32.02** |

Filters used: `serviceName eq 'Azure Container Apps' and armRegionName eq 'westus3' and priceType eq 'Consumption'`; `serviceName eq 'Azure Database for PostgreSQL' and armRegionName eq 'westus3' and priceType eq 'Consumption' and contains(productName,'Flexible Server')` (compute) and `... and contains(meterName,'Stor')` (storage); `serviceName eq 'Log Analytics' and armRegionName eq 'westus3' and priceType eq 'Consumption'`. The free grant and the idle rules are from the Azure Container Apps pricing page and billing article, read the same day.

The budget is USD 25 and only sends email. The expected total fits; the worst case does not. **Check in Cost Management** which meter the API is on ("Standard vCPU Idle Usage" or "Active Usage"). If it is Active, cut in this order:

1. `apiMinReplicas = 0` in `infra/poc.bicepparam`: about **$16.09** in total. Cost: the first request after 30 quiet minutes waits for a cold start and may fail once, and the API's timers do not run while it sleeps. People stay signed in (the sign-in keys are in the database now).
2. Stop the database when not testing (portal, the PostgreSQL server, Stop): compute billing pauses. Azure restarts it after 7 days.
3. Tear down between test periods (below).

Never add to the environment without a cost decision: a dedicated workload profile, a private endpoint or a maintenance window ($0.10 per hour each, about $73 a month), or its own virtual network (load balancer and addresses, about $26 a month).

## Known limits

- **Two lists to keep in step:** Google's Test users (in the console, by hand) and Tether's invite list (the script). A person missing from Google's list is stopped on a Google page that Tether cannot word.
- **Sign-in can be stalled by a flood.** The API sees every visitor as one address, so someone hammering the sign-in button's address can use up the shared budget and make *new* sign-ins answer "try again in a moment" while it lasts. People already signed in are not affected. The same flood during a deploy can make the outside checks fail, and then the run closes the site. The Lead decides on a fix before anyone but you is invited (ADR-022, residual risk 1).
- **School or work Google accounts are refused** (a Google account that belongs to an organisation). An account Google does not mark that way is treated as personal.
- A change to the invite list, and every deploy, restarts the API: live connections drop for about a minute and a lecture that is recording pauses itself.
- In `team` mode the Microsoft sign-in lasts 12 hours. When it lapses on an open page, the page's background requests fail until you reload.
- The old database `tether` (the developer-login test data) stays on the server, unused, until you delete it in the portal. Nothing points at it.
- The database has a public endpoint that accepts connections from any address inside Azure. It needs TLS and a 52-character random password. A private database costs more than the whole budget.
- Anyone with Contributor on the resource group can read the app secrets: database password, root secret, Google client secret, invite list and the sign-in certificate.
- Assumed, to be seen on the first runs: that Azure switches the Microsoft gate off in place when the template says so, and what exactly Google shows a test user.

## If a deploy fails

- Preflight errors name the missing or malformed secret and the script that sets it. Nothing was deployed.
- "access=invited was refused": Run A is not live yet, or it is not confirmed as a Production API. Run `team` first, try it, then `invited` with "Change the access mode only" ticked.
- "gate_only was refused" (only when taking the Microsoft gate off, `invited`): the release that is live is not the newest commit. Either put the live commit id in "release_sha", or deploy the newest code first with `team` and nothing ticked, try it, then run `invited` again. Putting the gate back (`team`) is never refused for this reason.
- "Azure could not say whether the Microsoft gate is on or off": a read from Azure failed. Nothing was changed; run the workflow again.
- "The web app was made internal again": the outside checks did not pass, so the run closed the site. Read the failed check in the step above it, then run `team`.
- "Images must be pullable": make the two packages public (repository page, Packages, the package, Package settings, Change visibility).
- Database region: the deploy keeps the region of the server that exists; with none yet it tries `TETHER_POSTGRES_CANDIDATES` in the workflow (westus3, westus2, centralus) and moves on ONLY when Azure refuses the region or the SKU. Run 1 on 2026-11-26 got `SkuNotAvailable` for B1MS in westus3; the server is in westus2.
- Every `/api` call answers 400 after sign-in: the platform sends a different Host header than expected. Tell the Lead (the fix is the `AllowedHosts` line in `infra/main.bicep`).
- The API revision never becomes healthy: the API refuses to start when a sign-in setting is wrong and says which one (never its value) in the container log, Log Analytics workspace `tether-dev-logs`. Tell the Lead.

## Local rehearsal (no Azure, no Google)

- Case tests for the deploy checks and the emergency stop: `bash infra/scripts/tests/preflight-cases.sh` (plain bash) and `infra/scripts/tests/smoke-cases.sh` (needs jq, curl, openssl and node; the header of `infra/local/rehearsal.Dockerfile` shows how to run it in a container with no network). CI runs both on every code change (job `infra scripts` in `.github/workflows/ci.yml`).
- The local Production stack: `docker-compose.poc-prod.yml` runs the two images the way Azure does after go-live (API as Production, new empty database, https on `https://localhost:3443`) with placeholder Google values, and `docker compose ... run --rm rehearsal` runs the outside checks against it. The first lines of that file give the three commands. It cannot complete a real Google sign-in.

## Tear down

- **Everything:** `.\scripts\azure\create-signin-app.ps1 -Remove`, then `.\scripts\azure\bootstrap-tether-dev.ps1 -Remove` (deletes the resource group with all data, the deploy identity and the budget). Optionally delete the two packages and the environment secrets in GitHub, the redirect address in the Google console, and `%USERPROFILE%\.tether` on your machine.
- **Only the running resources, keeping the identity:** delete the resources inside `tether-dev` in the portal. The next deploy recreates them with a **new** website address: run step 1 again with `-ShowRedirectAddressOnly` and put the new address in the Google console (and run `create-signin-app.ps1` again if you use `team`) before deploying.
