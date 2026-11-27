// Tether proof-of-concept hosting on Azure. Authored 2026-11-26. NEVER DEPLOYED as of this writing.
// Deployed by .github/workflows/deploy-azure-dev.yml as the `tether-deployer` identity, which holds Contributor on the
// resource group `tether-dev` and nothing else (scripts/azure/bootstrap-tether-dev.ps1). Read infra/README.md first.
//
// What this creates (all in the one resource group):
//   - Log Analytics workspace (container logs; daily cap so it stays inside the free allowance)
//   - Container Apps environment, Consumption only (no dedicated profile, no own network, no private endpoint,
//     no planned-maintenance window: each of those adds a USD 0.10 per hour management charge)
//   - PostgreSQL 16 Flexible Server, smallest Burstable size, TLS required, password sign-in
//   - Container app `tether-api`: INTERNAL ingress only, reachable only from inside the environment
//   - Container app `tether-web`: the only public entry, and only when an access gate is configured (see below)
//
// ACCESS GATE (the API runs as Development with dev login ON, so the public URL must never be open):
//   - Primary gate: Microsoft Entra sign-in in front of the web app (Container Apps built-in authentication).
//   - Optional extra: an IP allowlist on the web ingress.
//   - With neither configured the web app gets INTERNAL ingress only. There is no parameter that opens it without a gate.
//
// What the deploy identity cannot do, and how this file works around it:
//   - It cannot create role assignments, so there is no managed-identity image pull and no Key Vault access role.
//     Images are public packages on ghcr.io (the repository is public; nothing secret is in an image) and secrets are
//     Container Apps secrets. Anyone with Contributor on the resource group can read those secrets.
//   - It cannot touch Microsoft Entra, so the sign-in app registration is created by the Product Owner
//     (scripts/azure/create-signin-app.ps1) and only its client id and secret are passed in here.

targetScope = 'resourceGroup'

// ---------------------------------------------------------------------------------------------------------------------
// Parameters
// ---------------------------------------------------------------------------------------------------------------------

@description('Region for everything except (optionally) the database. Defaults to the resource group region (westus3).')
param location string = resourceGroup().location

@description('Region for the PostgreSQL server; empty means the main region. The deploy workflow picks it: the region of the server that already exists, otherwise the first region in its list that accepts a Burstable server (deploy run 1, 2026-11-26: westus3 answered SkuNotAvailable although not restricted for the subscription).')
param postgresLocation string = ''

@description('Container Apps environment name. scripts/azure/create-signin-app.ps1 reads its default domain by this name.')
param environmentName string = 'tether-dev-env'

@description('API container app name. It is also the internal host name the web image proxies to (http://<name>), which is baked into the web image at build time. Change it in the deploy workflow, not here.')
@minLength(2)
@maxLength(32)
param apiAppName string = 'tether-api'

@description('Web container app name. Part of the public host name and of the Entra redirect URI.')
@minLength(2)
@maxLength(32)
param webAppName string = 'tether-web'

@description('Full image reference for the API, for example ghcr.io/<owner>/tether-api:<commit sha>. Must be anonymously pullable.')
param apiImage string

@description('Full image reference for the web app. It must have been built with API_PROXY_TARGET=http://<apiAppName>.')
param webImage string

@description('Changes on every deploy (the workflow passes the run id). Forces a new revision so changed secrets are picked up.')
param deploymentStamp string = ''

// --- Access gate ---

@description('Ask for PUBLIC web ingress. Honoured only when a gate is configured; asking for it without a gate fails the deployment.')
param exposeWeb bool = false

@description('Client id of the Entra sign-in app registration the Product Owner created (scripts/azure/create-signin-app.ps1). Empty = built-in sign-in off.')
param easyAuthClientId string = ''

@description('Client secret of that app registration. Stored as a Container Apps secret, never in the template or its outputs.')
@secure()
param easyAuthClientSecret string = ''

@description('Entra tenant that issues the sign-in tokens. Defaults to the tenant of the subscription.')
param easyAuthTenantId string = tenant().tenantId

@description('Optional extra gate: IPv4 addresses or CIDR ranges allowed to reach the web app. Empty = no IP restriction. Never commit real addresses; the workflow passes them from a GitHub secret.')
param allowedIpRanges string[] = []

// --- Secrets (GitHub environment secrets, set once; see infra/README.md) ---

@description('PostgreSQL administrator password. Letters, digits, hyphen and underscore only (it is placed in a connection string). Applied to the server on every deploy, so the server and the app always agree.')
@secure()
@minLength(24)
param postgresAdminPassword string

@description('Security:RootSecret for the API: base64 of at least 32 random bytes. Must stay the same between deploys (ETags, idempotency keys and audit digests derive from it).')
@secure()
@minLength(44)
param rootSecret string

// --- Database ---

@description('PostgreSQL administrator login. The POC app connects as this login (it runs the migrations).')
param postgresAdminLogin string = 'tetheradmin'

@description('Database name.')
param postgresDatabaseName string = 'tether'

@description('PostgreSQL compute size. Standard_B1ms is the smallest Burstable size.')
param postgresSkuName string = 'Standard_B1ms'

@description('PostgreSQL storage in GiB. 32 is the minimum.')
@minValue(32)
param postgresStorageGb int = 32

@description('How strictly the API checks the database certificate. VerifyFull checks the chain and the host name.')
@allowed(['VerifyFull', 'Require'])
param postgresSslMode string = 'VerifyFull'

@description('''Which addresses may open a connection to the database server (it has a public endpoint; see the note at the firewall rule).
azureServices: any address inside Azure (one rule; robust).
containerAppOutboundIps: only the outbound addresses the Container Apps environment reports (tighter; they can change, redeploy to refresh; UNVERIFIED).''')
@allowed(['azureServices', 'containerAppOutboundIps'])
param postgresFirewallMode string = 'azureServices'

// --- Sizing and cost ---

@description('API replicas kept running. 1 = always on (SignalR state, background reapers and sign-in sessions survive idle periods). 0 = sleeps after the cooldown and cold-starts on the next request.')
@minValue(0)
@maxValue(1)
param apiMinReplicas int = 1

@description('Web replicas kept running. 0 = sleeps when nobody is using it (a few seconds to wake).')
@minValue(0)
@maxValue(1)
param webMinReplicas int = 0

@description('Seconds without requests before a min-0 app scales to zero.')
@minValue(300)
@maxValue(3600)
param scaleToZeroCooldownSeconds int = 1800

@description('Syllabus worker poll interval in milliseconds (API default 300). Raised so an unused API stays under the Container Apps idle-billing thresholds.')
@minValue(300)
param apiSyllabusWorkerPollMs int = 1000

@description('Log Analytics daily ingestion cap in GB. 0.15 GB a day stays under the 5 GB a month free allowance.')
param logDailyCapGb string = '0.15'

@description('Tags applied to every resource.')
param tags object = {
  project: 'tether'
  env: 'dev'
  managedBy: 'infra/main.bicep'
}

// ---------------------------------------------------------------------------------------------------------------------
// Derived values and guards
// ---------------------------------------------------------------------------------------------------------------------

var apiPort = 8080 // src/api/Dockerfile: ASPNETCORE_HTTP_PORTS=8080
var webPort = 3000 // src/web/Dockerfile: PORT=3000

var cleanIpRanges = filter(map(allowedIpRanges, r => trim(r)), r => !empty(r))
var authOn = !empty(easyAuthClientId) && !empty(easyAuthClientSecret)
var ipGateOn = !empty(cleanIpRanges)
// PO-20cf: public access always needs Entra sign-in. The IP allowlist is only an extra layer on top of it, never a gate on
// its own (a wide range would hand dev login, and so every account, to everyone behind it).
var gateOn = authOn

// Guards. Each one stops the deployment with a plain message instead of creating something unsafe.
var authHalfConfigured = empty(easyAuthClientId) != empty(easyAuthClientSecret)
// Same floor as infra/scripts/preflight.sh (/16), so a deployment started without the workflow cannot widen the allowlist.
var openIpRange = !empty(filter(cleanIpRanges, r => contains(r, '/') && int(last(split(r, '/'))) < 16))
var guardMessage = authHalfConfigured
  ? 'easyAuthClientId and easyAuthClientSecret must be set together (run scripts/azure/create-signin-app.ps1).'
  : openIpRange
      ? 'allowedIpRanges contains a range wider than /16. That is not an allowlist.'
      : (exposeWeb && !gateOn)
          ? 'exposeWeb is true but Entra sign-in is not configured. The web app is never made public without it (an IP allowlist is only an extra layer).'
          : ''
// Evaluated wherever webExternal is used (the web ingress), so a failed guard stops the deployment before the web app is touched.
// Belt and braces: even if fail() were skipped, webExternal is false unless Entra sign-in is configured.
var webExternal = empty(guardMessage) ? (exposeWeb && gateOn) : fail(guardMessage)

var webIpRestrictions = map(range(0, length(cleanIpRanges)), i => {
  name: 'allow-${i}'
  description: 'POC access gate (allowlist)'
  ipAddressRange: contains(cleanIpRanges[i], '/') ? cleanIpRanges[i] : '${cleanIpRanges[i]}/32'
  action: 'Allow'
})

var pgLocation = empty(postgresLocation) ? location : toLower(postgresLocation)
// The region is part of the name: a server left in state Failed in one region can never block the attempt in the next,
// and each region's server has its own name. (The first name, without the region, appeared in a public log on
// 2026-11-26 before that server was refused; it is no longer used.)
var postgresServerName = 'tether-dev-pg-${uniqueString(resourceGroup().id, pgLocation)}'
var logWorkspaceName = 'tether-dev-logs'

var webFqdn = '${webAppName}.${containerEnv.properties.defaultDomain}'
var apiInternalFqdn = '${apiAppName}.internal.${containerEnv.properties.defaultDomain}'
var webOrigin = 'https://${webFqdn}'

var stampEnv = empty(deploymentStamp) ? [] : [{ name: 'TETHER_DEPLOY_STAMP', value: deploymentStamp }]
var scaleToZeroCooldown = { cooldownPeriod: scaleToZeroCooldownSeconds }

// ---------------------------------------------------------------------------------------------------------------------
// Logs
// ---------------------------------------------------------------------------------------------------------------------

// Container stdout and stderr land here. The API already keeps lecture text and row values out of its logs (Program.cs
// log filters); keep read access to this workspace narrow anyway.
resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logWorkspaceName
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30 // the first 31 days of retention are free
    workspaceCapping: { dailyQuotaGb: json(logDailyCapGb) } // ingestion stops for the day at the cap: a cost guard, not an alert
  }
}

// ---------------------------------------------------------------------------------------------------------------------
// Container Apps environment (Consumption only)
// ---------------------------------------------------------------------------------------------------------------------

resource containerEnv 'Microsoft.App/managedEnvironments@2025-01-01' = {
  name: environmentName
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
    // Only the built-in Consumption profile. Do NOT add a dedicated profile, a private endpoint or a maintenance window
    // here without a cost decision: each is billed USD 0.10 per hour (about USD 73 a month).
    workloadProfiles: [
      { name: 'Consumption', workloadProfileType: 'Consumption' }
    ]
    zoneRedundant: false
  }
}

// ---------------------------------------------------------------------------------------------------------------------
// PostgreSQL 16 Flexible Server
// ---------------------------------------------------------------------------------------------------------------------

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: postgresServerName
  location: pgLocation
  tags: tags
  sku: { name: postgresSkuName, tier: 'Burstable' }
  properties: {
    version: '16'
    administratorLogin: postgresAdminLogin
    administratorLoginPassword: postgresAdminPassword
    authConfig: { passwordAuth: 'Enabled', activeDirectoryAuth: 'Disabled' }
    storage: { storageSizeGB: postgresStorageGb, autoGrow: 'Disabled' }
    backup: { backupRetentionDays: 7, geoRedundantBackup: 'Disabled' }
    highAvailability: { mode: 'Disabled' }
    // Public endpoint with firewall rules. A private (virtual network) database needs the Container Apps environment in
    // its own network too, and that adds a load balancer and public IP addresses billed at about USD 25 a month, the whole
    // budget. The deploy identity COULD create that; it is rejected on cost, not on permission. See infra/README.md.
    network: { publicNetworkAccess: 'Enabled' }
  }
}

resource postgresDatabase 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: postgresDatabaseName
  properties: { charset: 'UTF8', collation: 'en_US.utf8' }
}

// Start and end 0.0.0.0 is Azure's "allow any address inside Azure" rule, including other customers' resources. They
// still need TLS and the random administrator password. Chosen as the default because the Consumption environment has no
// fixed outbound address.
resource postgresAllowAzure 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = if (postgresFirewallMode == 'azureServices') {
  parent: postgres
  name: 'AllowAllAzureServicesAndResourcesWithinAzureIps'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
  dependsOn: [postgresDatabase]
}

// Tighter alternative (UNVERIFIED): one rule per outbound address the environment reports. Read from the web app because
// it does not need the database, so the API can wait for the rules. When switching to this mode, delete the
// AllowAllAzureServicesAndResourcesWithinAzureIps rule by hand: an incremental deployment does not remove it.
module postgresAllowOutbound 'modules/postgres-firewall.bicep' = if (postgresFirewallMode == 'containerAppOutboundIps') {
  name: 'tether-postgres-firewall'
  params: {
    serverName: postgres.name
    ipAddresses: web.properties.outboundIpAddresses
  }
  dependsOn: [postgresDatabase]
}

// Server parameters. One at a time (the server refuses parallel changes).
//  - require_secure_transport: TLS is required for every connection (also the server default; stated so it cannot drift).
//  - SB-01 (docs/security/syllabus-built-review.md): keep row and statement text out of the SERVER log. These three are
//    the ones whose Azure default differs from the repo baseline in docker-compose.yml. The other five (log_statement,
//    log_duration, log_min_duration_statement, log_min_duration_sample, log_parameter_max_length_on_error) already
//    default to the baseline value per docs/vendors/azure-platform-verification.md section 3.4.
// No diagnostic setting is created, so server logs are NOT exported to Log Analytics.
var postgresParameters = [
  { name: 'require_secure_transport', value: 'on' }
  { name: 'log_error_verbosity', value: 'terse' }
  { name: 'log_min_error_statement', value: 'panic' }
  { name: 'log_parameter_max_length', value: '0' }
]

@batchSize(1)
resource postgresConfiguration 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = [
  for p in postgresParameters: {
    parent: postgres
    name: p.name
    properties: { value: p.value, source: 'user-override' }
    dependsOn: [postgresDatabase, postgresAllowAzure, postgresAllowOutbound]
  }
]

// ---------------------------------------------------------------------------------------------------------------------
// API container app: internal ingress only
// ---------------------------------------------------------------------------------------------------------------------

resource api 'Microsoft.App/containerApps@2025-01-01' = {
  name: apiAppName
  location: location
  tags: tags
  properties: {
    environmentId: containerEnv.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: false // never reachable from the internet; only other apps in this environment (the web app)
        targetPort: apiPort
        transport: 'auto' // HTTP/1.1 and WebSocket upgrades (/hubs, /ingest)
        // The web image proxies to http://<apiAppName>. That hop stays inside the environment and never leaves it;
        // the https name contains a per-environment suffix that is unknown when the web image is built.
        allowInsecure: true
      }
      secrets: [
        {
          name: 'db-connection'
          // Password charset is limited to letters, digits, hyphen and underscore (checked by the workflow), so it cannot break out of the string.
          // GSS Encryption Mode=Disable: password sign-in only. Without it Npgsql 10 probes for Kerberos and the chiseled image
          // (no libgssapi) prints "Cannot load library libgssapi_krb5.so.2" to stderr on the first connection (keyword checked
          // against Npgsql 10.0.3 and the line confirmed gone, 2026-11-26).
          value: 'Host=${postgres.properties.fullyQualifiedDomainName};Port=5432;Database=${postgresDatabaseName};Username=${postgresAdminLogin};Password=${postgresAdminPassword};SSL Mode=${postgresSslMode};GSS Encryption Mode=Disable;Maximum Pool Size=20'
        }
        { name: 'root-secret', value: rootSecret }
      ]
      // No `registries` entry: the images are public and pulled anonymously.
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: concat(
            [
              // POC ONLY. Development is the only environment in which the API allows dev login, and dev login is the only
              // sign-in that exists. The access gate on the web app is what keeps strangers out.
              { name: 'ASPNETCORE_ENVIRONMENT', value: 'Development' }
              { name: 'Auth__DevLogin__Enabled', value: 'true' }
              { name: 'ConnectionStrings__Default', secretRef: 'db-connection' }
              { name: 'Security__RootSecret', secretRef: 'root-secret' }
              // The origin the BROWSER uses (the web app), checked on every unsafe request and WebSocket upgrade.
              { name: 'Security__AllowedOrigins__0', value: webOrigin }
              // Host header the API accepts: the name the web proxy calls it by, plus its internal full name.
              // If every /api call answers 400, the platform is sending a different Host: see infra/README.md.
              { name: 'AllowedHosts', value: '${apiAppName};${apiInternalFqdn}' }
              { name: 'Database__MigrateOnStartup', value: 'true' }
              { name: 'Syllabus__Worker__PollMilliseconds', value: string(apiSyllabusWorkerPollMs) }
              // Deliberately NOT set:
              //  - Security__TrustedProxies / TrustedProxyNetworks: the platform proxy's address range is not known, and the
              //    API refuses wide ranges. Effect: the API sees every caller as one address (per-IP limits are shared).
              //  - DataProtection__KeysPath: cookie keys are ephemeral. Effect: every API restart or deploy signs everyone out.
              //  - Ai__Provider, Speech__Provider, Docs__Provider, Syllabus__*: defaults are the mocks; the API refuses anything else.
            ],
            stampEnv
          )
          probes: [
            {
              // Kestrel starts listening only after the migrations finish, so this also waits for the database.
              type: 'Startup'
              httpGet: { path: '/health/live', port: apiPort, scheme: 'HTTP', httpHeaders: [{ name: 'Host', value: apiAppName }] }
              initialDelaySeconds: 10
              periodSeconds: 20
              timeoutSeconds: 5
              failureThreshold: 10
            }
            {
              type: 'Liveness'
              httpGet: { path: '/health/live', port: apiPort, scheme: 'HTTP', httpHeaders: [{ name: 'Host', value: apiAppName }] }
              periodSeconds: 30
              timeoutSeconds: 5
              failureThreshold: 3
            }
            {
              // /health/ready opens a database connection. Probes count against the API's anonymous per-IP limit
              // (60 a minute); these three together stay under 6 a minute.
              type: 'Readiness'
              httpGet: { path: '/health/ready', port: apiPort, scheme: 'HTTP', httpHeaders: [{ name: 'Host', value: apiAppName }] }
              periodSeconds: 30
              timeoutSeconds: 5
              failureThreshold: 3
            }
          ]
        }
      ]
      // Exactly one replica at most: hub connections, partial transcripts, rate limits and cookie keys live in memory.
      scale: union({ minReplicas: apiMinReplicas, maxReplicas: 1 }, apiMinReplicas == 0 ? scaleToZeroCooldown : {})
    }
  }
  dependsOn: [postgresDatabase, postgresAllowAzure, postgresAllowOutbound, postgresConfiguration]
}

// ---------------------------------------------------------------------------------------------------------------------
// Web container app: the only public entry, and only behind a gate
// ---------------------------------------------------------------------------------------------------------------------

resource web 'Microsoft.App/containerApps@2025-01-01' = {
  name: webAppName
  location: location
  tags: tags
  properties: {
    environmentId: containerEnv.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: webExternal // false unless exposeWeb AND a gate; see the guards above
        targetPort: webPort
        transport: 'auto'
        allowInsecure: false // http is redirected to https
        ipSecurityRestrictions: webIpRestrictions // any Allow rule makes the ingress deny every other address
      }
      secrets: authOn ? [{ name: 'easyauth-client-secret', value: easyAuthClientSecret }] : []
    }
    template: {
      containers: [
        {
          name: 'web'
          image: webImage
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: stampEnv
          // TCP probes on purpose: with built-in sign-in on, an HTTP probe could be answered by the sign-in layer.
          probes: [
            { type: 'Startup', tcpSocket: { port: webPort }, initialDelaySeconds: 2, periodSeconds: 5, timeoutSeconds: 3, failureThreshold: 10 }
            { type: 'Liveness', tcpSocket: { port: webPort }, periodSeconds: 30, timeoutSeconds: 3, failureThreshold: 3 }
            { type: 'Readiness', tcpSocket: { port: webPort }, periodSeconds: 15, timeoutSeconds: 3, failureThreshold: 3 }
          ]
        }
      ]
      scale: union({ minReplicas: webMinReplicas, maxReplicas: 1 }, webMinReplicas == 0 ? scaleToZeroCooldown : {})
    }
  }
}

// Built-in authentication ("Easy Auth"): every request to the web app, including /api, the SignalR negotiate POST and
// the /hubs and /ingest WebSocket upgrades, must carry the platform's sign-in session cookie or is redirected to
// Microsoft sign-in. Who may sign in is decided in Entra: the app registration is single-tenant and its enterprise
// application has "assignment required" on, so only users the Product Owner assigns get a token.
// The deploy workflow applies this while the web ingress is still internal, then opens the ingress in a second pass.
resource webAuth 'Microsoft.App/containerApps/authConfigs@2025-01-01' = if (authOn) {
  parent: web
  name: 'current'
  properties: {
    platform: { enabled: true }
    globalValidation: {
      unauthenticatedClientAction: 'RedirectToLoginPage'
      redirectToProvider: 'azureactivedirectory'
    }
    httpSettings: { requireHttps: true }
    identityProviders: {
      azureActiveDirectory: {
        enabled: true
        registration: {
          clientId: easyAuthClientId
          clientSecretSettingName: 'easyauth-client-secret' // the Container Apps secret above
          openIdIssuer: '${environment().authentication.loginEndpoint}${easyAuthTenantId}/v2.0'
        }
        validation: {
          allowedAudiences: [easyAuthClientId, 'api://${easyAuthClientId}']
        }
      }
    }
    login: {
      // Fixed 12 hours from sign-in. When it lapses, background requests are redirected to sign-in and fail until the
      // page is reloaded (see "Known limits" in infra/README.md).
      cookieExpiration: { convention: 'FixedTime', timeToExpiration: '12:00:00' }
      preserveUrlFragmentsForLogins: false
    }
  }
}

// ---------------------------------------------------------------------------------------------------------------------
// NOT provisioned (placeholders only). All AI and speech providers are mocks in this deployment.
// Adding either needs the Product Owner to accept the vendor terms first (Marketplace and Anthropic terms for Claude),
// a spend decision, and the app code to support a real provider; today the API refuses to start with anything but mocks.
// ---------------------------------------------------------------------------------------------------------------------
// resource foundry 'Microsoft.CognitiveServices/accounts@<api-version>' = { kind: 'AIServices' ... }      // Claude on Microsoft Foundry
// resource speech  'Microsoft.CognitiveServices/accounts@<api-version>' = { kind: 'SpeechServices' ... }  // Azure AI Speech

// ---------------------------------------------------------------------------------------------------------------------
// Outputs (no secrets)
// ---------------------------------------------------------------------------------------------------------------------

@description('Public host name of the web app once it is exposed.')
output webFqdn string = webFqdn

@description('Redirect URI the Entra sign-in app registration must carry.')
output signInRedirectUri string = '${webOrigin}/.auth/login/aad/callback'

@description('True when the web app has public ingress after this deployment.')
output webIsPublic bool = webExternal

@description('none, entra, ip or entra+ip.')
output accessGate string = authOn ? (ipGateOn ? 'entra+ip' : 'entra') : (ipGateOn ? 'ip' : 'none')

output environmentName string = containerEnv.name
output environmentDefaultDomain string = containerEnv.properties.defaultDomain
output apiAppName string = api.name
output webAppName string = web.name
output postgresServerName string = postgres.name
output logWorkspaceName string = logs.name
