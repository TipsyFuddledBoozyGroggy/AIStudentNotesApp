// Parameters for the Tether deployment in Azure (resource group tether-dev).
// Nothing secret or personal is written in this file. The deploy workflow supplies those values as environment
// variables taken from the GitHub environment `azure-dev`; they are read here and never printed.
using './main.bicep'

// --- Set by the workflow on every run ---
param apiImage = readEnvironmentVariable('TETHER_API_IMAGE')
param webImage = readEnvironmentVariable('TETHER_WEB_IMAGE')
param apiAppName = readEnvironmentVariable('TETHER_API_APP_NAME', 'tether-api')
param webAppName = readEnvironmentVariable('TETHER_WEB_APP_NAME', 'tether-web')
param deploymentStamp = readEnvironmentVariable('TETHER_DEPLOY_STAMP', '')
// Empty = the main region. infra/scripts/postgres-location.sh decides it (keeps the region of an existing server).
param postgresLocation = readEnvironmentVariable('TETHER_POSTGRES_LOCATION', '')

// --- Access mode (ADR-022 section 2) ---
// team | invited | closed. With nothing set the answer is closed: the web app is internal only.
param access = toLower(readEnvironmentVariable('TETHER_ACCESS', 'closed'))
// Public ingress is asked for separately so the workflow can apply Entra sign-in while the web app is still internal
// (pass 1 of a two-pass run). Ignored when access is closed.
param exposeWeb = toLower(readEnvironmentVariable('TETHER_EXPOSE_WEB', 'false')) == 'true'

// --- Secrets (GitHub environment secrets; set once with scripts/azure/set-deploy-secrets.ps1) ---
param postgresAdminPassword = readEnvironmentVariable('TETHER_DB_ADMIN_PASSWORD')
param rootSecret = readEnvironmentVariable('TETHER_ROOT_SECRET')

// --- Google sign-in, invite list, key-ring certificate (required in every access mode; main.bicep stops without them) ---
// GOOGLE_CLIENT_ID, GOOGLE_CLIENT_SECRET, DP_CERT_PFX and DP_CERT_PFX_PREVIOUS are written by scripts/azure/set-google-signin.ps1,
// TETHER_INVITED_EMAILS by scripts/azure/set-invites.ps1.
param googleClientId = readEnvironmentVariable('TETHER_GOOGLE_CLIENT_ID', '')
param googleClientSecret = readEnvironmentVariable('TETHER_GOOGLE_CLIENT_SECRET', '')
param invitedEmails = readEnvironmentVariable('TETHER_INVITED_EMAILS', '')
param dataProtectionCertificate = readEnvironmentVariable('TETHER_DP_CERT_PFX', '')
param dataProtectionPreviousCertificate = readEnvironmentVariable('TETHER_DP_CERT_PFX_PREVIOUS', '')
// School and work Google accounts are refused (ADR-022 R7; the Lead's reading, pending the Product Owner). Changing
// this to 'allow' is a Product Owner decision, not a tuning knob.
param hostedDomainPolicy = 'refuse'

// --- Entra sign-in in front (access=team) ---
// Both values are written by scripts/azure/create-signin-app.ps1.
param easyAuthClientId = readEnvironmentVariable('TETHER_EASYAUTH_CLIENT_ID', '')
param easyAuthClientSecret = readEnvironmentVariable('TETHER_EASYAUTH_CLIENT_SECRET', '')
// Optional extra layer on either public mode: comma-separated IPv4 addresses or CIDR ranges from the GitHub secret
// ALLOWED_IP_RANGES. Off when empty.
param allowedIpRanges = filter(map(split(readEnvironmentVariable('TETHER_ALLOWED_IP_RANGES', ''), ','), r => trim(r)), r => !empty(r))

// --- Database ---
// ADR-022 R8: the first Production run starts on this new, empty database. The earlier database `tether` (dev-login
// test data) stays on the server, unused, until the Product Owner deletes it. Never point this back at `tether`.
param postgresDatabaseName = 'tether_live'

// --- Sizing (see the cost table in infra/README.md before changing) ---
param apiMinReplicas = 1
param webMinReplicas = 0
param postgresSkuName = 'Standard_B1ms'
param postgresStorageGb = 32
param postgresFirewallMode = 'azureServices'
