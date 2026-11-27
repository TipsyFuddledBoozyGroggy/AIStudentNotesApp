# Tether on Azure: proof-of-concept deployment

Written 2026-11-26. **Nothing here has been run against Azure yet.** Providers are mocks only: no AI model and no speech service is created. This is not production and no release gate changes (G1 stays BLOCKED, G5 stays NOT_VERIFIED).

## What gets created (all inside the resource group `tether-dev`, West US 3)

| Thing | Purpose |
|---|---|
| Container Apps environment (pay per use) | Runs the two containers |
| `tether-web` container app | The website. The only public entry, and only behind sign-in |
| `tether-api` container app | The API. Internal only: never reachable from the internet |
| PostgreSQL 16 server (smallest size, 32 GB) | The database. TLS required |
| Log Analytics workspace | Container logs, capped at 0.15 GB a day |

The images are built by GitHub and stored as **public** packages on ghcr.io (the repository is already public and no secret is inside an image). Secrets live in GitHub (environment `azure-dev`) and are copied into Container Apps secrets on each deploy.

## Who can reach it

The cloud copy still uses the developer login (pick any email, no password), so the address is never left open:

- **Microsoft sign-in first.** Every request must come from a signed-in account that you assigned. Anyone else is sent to the Microsoft sign-in page and refused there.
- **Optional extra layer (only together with Entra sign-in; on its own it never opens the web app):** a list of allowed IP addresses (GitHub environment secret `ALLOWED_IP_RANGES`, comma separated, IPv4, `/16` to `/32`).
- With neither set up, the website has **no public address at all**. The template cannot open it without a gate, and each deploy tests from outside that an anonymous visitor is refused. If a deploy that opens the website does not end with that test passed (the test got the page, timed out, or the deploy failed or was cancelled part way), the deploy closes the website again; the next successful run opens it.

## One-time steps only the Product Owner can do

If Windows says "running scripts is disabled", start each script as `powershell -ExecutionPolicy Bypass -File <script>` instead.

1. **Azure identity:** `az login`, then `.\scripts\azure\bootstrap-tether-dev.ps1 -AlertEmail <your email>`. Keep the three IDs it prints.
2. **GitHub environment:** repository Settings, Environments, New environment `azure-dev`. Add yourself under Required reviewers (leave "Prevent self-review" off). Under Deployment branches choose Selected branches and add `main`. Add three environment **secrets** (identifiers, but secrets keep them out of the public logs): `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`.
3. **Generated secrets:** `gh auth login`, then `.\scripts\azure\set-deploy-secrets.ps1`. It creates the database password and the API root secret and sends them straight to GitHub. Nobody sees them.
4. **Deploy, run 1:** GitHub, Actions, "Deploy Azure dev (POC)", Run workflow (branch `main`), approve it.
   - If the build job stops and prints two links, the image packages are private: open each link, Danger Zone, Change visibility, Public, then "Re-run failed jobs". On the first run (2026-11-26) this was not needed: the packages, pushed by the workflow and linked to this public repository, were public from the first push (checked with an anonymous pull).
   - Run 1 creates everything, with the website **internal only**.
5. **Sign-in gate:** `.\scripts\azure\create-signin-app.ps1` (add `-TeamMembers a@..., b@...` for other people; they must already be users or guests in your directory). It creates the Microsoft sign-in registration, restricts it to assigned people, and stores its id and secret in GitHub. It prints the website address.
6. **Deploy, run 2:** run the workflow again and approve it. It applies sign-in while the website is still internal, then makes it public. Open the address, sign in with Microsoft, then use the developer login.

Later deploys are one run each: Actions, Run workflow, approve. To add a person: step 5 again with `-TeamMembers`. The sign-in secret lasts one year; step 5 renews it (then deploy).

## Estimated monthly cost

Prices fetched **2026-11-26** from the public Azure Retail Prices API (`GET https://prices.azure.com/api/retail/prices?$filter=...`, no sign-in), region `westus3`, USD, pay-as-you-go, before tax. An estimate, not a quote. A month is 730 hours.

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

The budget is USD 25 and only sends email. The expected total fits; the worst case does not. **Check after two days** in Cost Management which meter the API is on ("Standard vCPU Idle Usage" or "Active Usage"). If it is Active, cut in this order:

1. `apiMinReplicas = 0` in `infra/poc.bicepparam`: about **$16.09** in total. Cost: the first request after 30 quiet minutes waits for a cold start and may fail once, the API's timers do not run while it sleeps, and everyone is signed out of the app each time it wakes.
2. Stop the database when not testing (portal, the PostgreSQL server, Stop): compute billing pauses. Azure restarts it after 7 days.
3. Tear down between test periods (below).

Never add to the environment without a cost decision: a dedicated workload profile, a private endpoint or a maintenance window ($0.10 per hour each, about $73 a month), or its own virtual network (load balancer and addresses, about $26 a month).

## Known limits of this proof of concept

- Two sign-ins: Microsoft first, then the app's developer login.
- The Microsoft sign-in lasts 12 hours. When it lapses on an open page, the page's background requests are redirected to sign-in and fail until you reload. During a lecture that includes Pause and Stop: reload, or close the tab (the audio stops and the server pauses the session by itself).
- Every API restart or deploy signs everyone out of the app (cookie keys are not stored).
- The API sees all visitors as one address, so its per-address limits (for example 10 logins a minute) are shared.
- The database has a public endpoint that accepts connections from any address inside Azure. It needs TLS and a 52-character random password. A private database costs more than the whole budget.
- Anyone with Contributor on the resource group can read the app secrets.

## If a deploy fails

- "Images must be pullable": make the two packages public (step 4).
- Preflight errors name the missing variable or secret and the script that sets it.
- Database region: the deploy keeps the region of the server that exists; with none yet it tries `TETHER_POSTGRES_CANDIDATES` in the workflow (westus3, westus2, centralus) and moves on ONLY when Azure refuses the region or the SKU (`SkuNotAvailable`, restricted). Run 1 on 2026-11-26 got `SkuNotAvailable` for B1MS in westus3. B1MS is $0.017 an hour in westus2 too, $0.01921 in centralus (about $1.60 a month more), read 2026-11-26. A database outside westus3 adds a cross-region round trip (tens of milliseconds) to every query; acceptable for the POC, and a reason to move the app to the same region later.
- Every `/api` call answers 400 after sign-in: the platform sends a different Host header than expected. Tell the Lead (the fix is the `AllowedHosts` line in `infra/main.bicep`).
- The API revision never becomes healthy: usually the database connection. Tell the Lead; the container log is in the Log Analytics workspace `tether-dev-logs`.

## Tear down

- **Everything:** `.\scripts\azure\create-signin-app.ps1 -Remove`, then `.\scripts\azure\bootstrap-tether-dev.ps1 -Remove` (deletes the resource group with all data, the deploy identity and the budget). Optionally delete the two packages and the environment secrets in GitHub.
- **Only the running resources, keeping the identity:** delete the resources inside `tether-dev` in the portal. The next deploy recreates them with a **new** website address, so run step 5 again before run 2.
