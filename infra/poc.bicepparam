// Parameters for the Tether proof-of-concept deployment (resource group tether-dev).
// Nothing secret or personal is written in this file. The deploy workflow supplies those values as environment
// variables taken from the GitHub environment `azure-dev`; they are read here and never printed.
using './main.bicep'

// --- Set by the workflow on every run ---
param apiImage = readEnvironmentVariable('TETHER_API_IMAGE')
param webImage = readEnvironmentVariable('TETHER_WEB_IMAGE')
param apiAppName = readEnvironmentVariable('TETHER_API_APP_NAME', 'tether-api')
param webAppName = readEnvironmentVariable('TETHER_WEB_APP_NAME', 'tether-web')
param deploymentStamp = readEnvironmentVariable('TETHER_DEPLOY_STAMP', '')
param exposeWeb = toLower(readEnvironmentVariable('TETHER_EXPOSE_WEB', 'false')) == 'true'
// Empty = the main region. infra/scripts/postgres-location.sh decides it (keeps the region of an existing server).
param postgresLocation = readEnvironmentVariable('TETHER_POSTGRES_LOCATION', '')

// --- Secrets (GitHub environment secrets; set once with scripts/azure/set-deploy-secrets.ps1) ---
param postgresAdminPassword = readEnvironmentVariable('TETHER_DB_ADMIN_PASSWORD')
param rootSecret = readEnvironmentVariable('TETHER_ROOT_SECRET')

// --- Access gate ---
// Primary: Microsoft Entra sign-in. Both values are written by scripts/azure/create-signin-app.ps1 (variable and secret).
param easyAuthClientId = readEnvironmentVariable('TETHER_EASYAUTH_CLIENT_ID', '')
param easyAuthClientSecret = readEnvironmentVariable('TETHER_EASYAUTH_CLIENT_SECRET', '')
// Optional extra: comma-separated IPv4 addresses or CIDR ranges from the GitHub secret ALLOWED_IP_RANGES. Off when empty.
param allowedIpRanges = filter(map(split(readEnvironmentVariable('TETHER_ALLOWED_IP_RANGES', ''), ','), r => trim(r)), r => !empty(r))

// --- Sizing (see the cost table in infra/README.md before changing) ---
param apiMinReplicas = 1
param webMinReplicas = 0
param postgresSkuName = 'Standard_B1ms'
param postgresStorageGb = 32
param postgresFirewallMode = 'azureServices'
