<#
.SYNOPSIS
  One-time Azure bootstrap for Tether, run BY THE PRODUCT OWNER in his own terminal, signed in as himself.
  Agents never run this and never see its output beyond what the PO pastes.

.WHAT IT CREATES (nothing else)
  1. Resource group            tether-dev (region below)
  2. Budget                    USD 25/month on the whole subscription, email alerts at 50/80/100 percent actual and 100 percent forecast
  3. Entra app registration    tether-deployer  (NO password, NO secret, NO certificate)
  4. Federated credential      lets ONLY the GitHub environment "azure-dev" of this repo sign in as tether-deployer
  5. Role assignment           Contributor on resource group tether-dev ONLY (cannot assign roles, cannot touch anything outside the group)
  6. Resource provider registrations the app will need (subscription-level, free)

.WHAT IT DOES NOT DO
  It does not deploy anything, accept any Marketplace or Anthropic terms, request quota, create keys, or change the
  existing read-only tether-reader identity. A budget only ALERTS; it does not stop spending.

.USAGE
  az login                                   # as yourself, in your own terminal
  .\scripts\azure\bootstrap-tether-dev.ps1 -AlertEmail you@example.com
  Add -Remove to delete everything this script created (the resource group and its contents, the app, the budget).
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$AlertEmail,
  [string]$Location = 'westus3',
  [string]$ResourceGroup = 'tether-dev',
  [string]$AppName = 'tether-deployer',
  [string]$Repo = 'TipsyFuddledBoozyGroggy/AIStudentNotesApp',
  [string]$GitHubEnvironment = 'azure-dev',
  [int]$MonthlyBudgetUsd = 25,
  [switch]$Remove
)
$ErrorActionPreference = 'Stop'

$acct = az account show --output json | ConvertFrom-Json
if (-not $acct) { throw 'Not signed in. Run: az login' }
$sub = $acct.id; $tenant = $acct.tenantId
Write-Host ""
Write-Host "Subscription : $($acct.name)  ($sub)"
Write-Host "Signed in as : $($acct.user.name)"
Write-Host "Resource grp : $ResourceGroup in $Location"
Write-Host "Budget       : USD $MonthlyBudgetUsd per month, alerts to $AlertEmail"
Write-Host "GitHub trust : repo $Repo, environment '$GitHubEnvironment' only"
Write-Host ""
$ans = Read-Host "Type YES to continue with the subscription above"
if ($ans -ne 'YES') { Write-Host 'Cancelled. Nothing changed.'; exit 1 }

if ($Remove) {
  az group delete --name $ResourceGroup --yes --no-wait
  $appId = az ad app list --display-name $AppName --query '[0].appId' -o tsv
  if ($appId) { az ad app delete --id $appId }
  az rest --method delete --url "https://management.azure.com/subscriptions/$sub/providers/Microsoft.Consumption/budgets/tether-monthly?api-version=2023-05-01" 2>$null
  Write-Host 'Removal started.'; exit 0
}

# 1. Resource group
az group create --name $ResourceGroup --location $Location --tags project=tether env=dev | Out-Null

# 2. Budget with alerts (subscription scope so model and speech spend is covered)
$start = (Get-Date -Day 1).ToString('yyyy-MM-01')
$end = (Get-Date -Day 1).AddYears(3).ToString('yyyy-MM-01')
$notif = @{}
foreach ($t in 50, 80, 100) {
  $notif["actual$t"] = @{ enabled = $true; operator = 'GreaterThanOrEqualTo'; threshold = $t; thresholdType = 'Actual'; contactEmails = @($AlertEmail) }
}
$notif['forecast100'] = @{ enabled = $true; operator = 'GreaterThanOrEqualTo'; threshold = 100; thresholdType = 'Forecasted'; contactEmails = @($AlertEmail) }
$body = @{ properties = @{ category = 'Cost'; amount = $MonthlyBudgetUsd; timeGrain = 'Monthly'
  timePeriod = @{ startDate = $start; endDate = $end }; notifications = $notif } } | ConvertTo-Json -Depth 8
$tmp = New-TemporaryFile
Set-Content -Path $tmp -Value $body -Encoding utf8
az rest --method put --url "https://management.azure.com/subscriptions/$sub/providers/Microsoft.Consumption/budgets/tether-monthly?api-version=2023-05-01" --body "@$tmp" | Out-Null
Remove-Item $tmp

# 3. App registration and service principal, no secret
$appId = az ad app list --display-name $AppName --query '[0].appId' -o tsv
if (-not $appId) { $appId = az ad app create --display-name $AppName --query appId -o tsv }
if (-not (az ad sp show --id $appId 2>$null)) { az ad sp create --id $appId | Out-Null }

# 4. Federated credential: only this repo's protected environment
$fed = @{ name = 'github-azure-dev'; issuer = 'https://token.actions.githubusercontent.com'
  subject = "repo:${Repo}:environment:${GitHubEnvironment}"; audiences = @('api://AzureADTokenExchange') } | ConvertTo-Json
$tmp = New-TemporaryFile
Set-Content -Path $tmp -Value $fed -Encoding utf8
$existing = az ad app federated-credential list --id $appId --query "[?name=='github-azure-dev'].name" -o tsv
if (-not $existing) { az ad app federated-credential create --id $appId --parameters "@$tmp" | Out-Null }
Remove-Item $tmp

# 5. Contributor on the one resource group
$scope = "/subscriptions/$sub/resourceGroups/$ResourceGroup"
az role assignment create --assignee $appId --role Contributor --scope $scope | Out-Null

# 6. Providers (free; registration is a subscription-level action)
foreach ($ns in 'Microsoft.App', 'Microsoft.ContainerRegistry', 'Microsoft.DBforPostgreSQL', 'Microsoft.KeyVault',
  'Microsoft.OperationalInsights', 'Microsoft.Insights', 'Microsoft.CognitiveServices', 'Microsoft.Storage', 'Microsoft.Consumption') {
  az provider register --namespace $ns | Out-Null
}

Write-Host ""
Write-Host "DONE. Paste these three IDs (identifiers, not secrets) back to the Lead:"
Write-Host "  AZURE_CLIENT_ID       = $appId"
Write-Host "  AZURE_TENANT_ID       = $tenant"
Write-Host "  AZURE_SUBSCRIPTION_ID = $sub"
Write-Host ""
Write-Host "Still for you to do yourself: create the GitHub environment '$GitHubEnvironment' with yourself as a required reviewer."
