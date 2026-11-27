<#
.SYNOPSIS
  One-time Azure bootstrap for Tether, run BY THE PRODUCT OWNER in their own terminal, signed in as themselves.
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
  Use your own az profile, not the default one (that holds the agents' read-only identity):
    PowerShell:  $env:AZURE_CONFIG_DIR = "$HOME\.azure-po"      cmd:  set "AZURE_CONFIG_DIR=%USERPROFILE%\.azure-po"
  az login --tenant <your tenant id>         # as yourself (naming the tenant makes az ask for MFA when it is required)
  powershell -ExecutionPolicy Bypass -File .\scripts\azure\bootstrap-tether-dev.ps1 -AlertEmail you@example.com
  (Script execution is disabled on this machine, hence powershell.exe -ExecutionPolicy Bypass. Not from PowerShell ISE:
  it cannot answer the YES prompt of a script started this way.)
  First run 2026-11-26. Running it again is how to correct the alert address or the GitHub trust subject: the budget is
  updated in place, the trust subject is replaced when it differs from what GitHub sends, everything else is skipped.
  Needs gh signed in (it reads this repository's OIDC subject format).
  Add -Remove to delete everything this script created (the resource group and its contents, the app, the budget).
  Safe to run again: each step skips what already exists. It stops at the first failed step and says which one.
  Works in Windows PowerShell 5.1 and PowerShell 7.
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

# Native commands (az) do not stop PowerShell when they fail, so every call goes through this check.
function Assert-Ok([string]$Step, [int]$Code = $global:LASTEXITCODE) {
  if ($Code -ne 0) { throw "Step failed: $Step (az exit code $Code). Nothing after this step ran. Fix the error above and run the script again." }
}
# UTF-8 without a byte-order mark; Windows PowerShell 5.1 Set-Content -Encoding utf8 adds one, which az can reject.
function Write-JsonTemp([string]$Json) {
  $path = [System.IO.Path]::GetTempFileName()
  [System.IO.File]::WriteAllText($path, $Json, (New-Object System.Text.UTF8Encoding($false)))
  return $path
}

$acctJson = az account show --output json
if ($LASTEXITCODE -ne 0 -or -not $acctJson) { throw 'Not signed in. Run: az login' }
$acct = $acctJson | Out-String | ConvertFrom-Json
# The default az profile on this machine belongs to the agents' read-only identity (a service principal). The PO signs in
# under a separate profile; when AZURE_CONFIG_DIR is not pointing at it, stop here instead of failing at step 1.
if ($acct.user.type -ne 'user') {
  throw 'az is signed in as a service principal, not as you. Point AZURE_CONFIG_DIR at your own sign-in first (PowerShell: $env:AZURE_CONFIG_DIR = "$HOME\.azure-po"   cmd: set AZURE_CONFIG_DIR=%USERPROFILE%\.azure-po), check with: az account show --query user.name -o tsv, then run this script again.'
}
$sub = $acct.id; $tenant = $acct.tenantId
$budgetUrl = "https://management.azure.com/subscriptions/$sub/providers/Microsoft.Consumption/budgets/tether-monthly?api-version=2023-05-01"
Write-Host ""
Write-Host "Subscription : $($acct.name)  ($sub)"
Write-Host "Signed in as : $($acct.user.name)"
Write-Host "Resource grp : $ResourceGroup in $Location"
Write-Host "Budget       : USD $MonthlyBudgetUsd per month, alerts to $AlertEmail"
Write-Host "GitHub trust : repo $Repo, environment '$GitHubEnvironment' only"
if ($Remove) { Write-Host "MODE         : REMOVE everything listed above" -ForegroundColor Yellow }
Write-Host ""
$ans = Read-Host "Type YES to continue with the subscription above"
if ($ans -ne 'YES') { Write-Host 'Cancelled. Nothing changed.'; exit 1 }

if ($Remove) {
  az group delete --name $ResourceGroup --yes --no-wait; Assert-Ok 'delete resource group'
  $appId = az ad app list --display-name $AppName --query '[0].appId' -o tsv; Assert-Ok 'find app registration'
  if ($appId) { az ad app delete --id $appId; Assert-Ok 'delete app registration' }
  az rest --method delete --url $budgetUrl | Out-Null
  if ($LASTEXITCODE -ne 0) { Write-Warning 'Budget tether-monthly was not deleted (it may not exist). Check Cost Management > Budgets.' }
  Write-Host 'Removal started (the resource group deletes in the background, a few minutes).'; exit 0
}

# 1. Resource group
Write-Host '1/6 Resource group'
az group create --name $ResourceGroup --location $Location --tags project=tether env=dev | Out-Null; Assert-Ok 'create resource group'

# 2. Budget with alerts (subscription scope so model and speech spend is covered)
Write-Host '2/6 Budget and alerts'
$start = (Get-Date -Day 1).ToString('yyyy-MM-01')
$end = (Get-Date -Day 1).AddYears(3).ToString('yyyy-MM-01')
$notif = @{}
foreach ($t in 50, 80, 100) {
  $notif["actual$t"] = @{ enabled = $true; operator = 'GreaterThanOrEqualTo'; threshold = $t; thresholdType = 'Actual'; contactEmails = @($AlertEmail) }
}
$notif['forecast100'] = @{ enabled = $true; operator = 'GreaterThanOrEqualTo'; threshold = 100; thresholdType = 'Forecasted'; contactEmails = @($AlertEmail) }
$budget = @{ properties = @{ category = 'Cost'; amount = $MonthlyBudgetUsd; timeGrain = 'Monthly'
  timePeriod = @{ startDate = $start; endDate = $end }; notifications = $notif } }
# Run again (for example to correct the alert address), the budget already exists: keep its period (a start date may not
# move) and send its eTag, so the PUT is an update of that budget.
$ErrorActionPreference = 'Continue'   # Windows PowerShell 5.1 turns a redirected native stderr line into a terminating error under 'Stop'
$existingBudgetJson = az rest --method get --url $budgetUrl 2>$null; $getRc = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
if ($getRc -eq 0 -and $existingBudgetJson) {
  $existingBudget = $existingBudgetJson | Out-String | ConvertFrom-Json
  if ($existingBudget.eTag) { $budget.eTag = $existingBudget.eTag }
  $period = $existingBudget.properties.timePeriod
  if ($period.startDate) { $budget.properties.timePeriod = @{ startDate = $period.startDate; endDate = $period.endDate } }
  Write-Host '  budget exists: updating its amount and alert address'
}
$body = $budget | ConvertTo-Json -Depth 8
$tmp = Write-JsonTemp $body
az rest --method put --url $budgetUrl --body "@$tmp" | Out-Null; $rc = $LASTEXITCODE
Remove-Item $tmp
Assert-Ok 'create budget (some subscription types do not support budgets; tell the Lead if this fails)' $rc

# 3. App registration and service principal, no secret
Write-Host '3/6 Deployer identity (no secret)'
$appId = az ad app list --display-name $AppName --query '[0].appId' -o tsv; Assert-Ok 'look up app registration'
if (-not $appId) { $appId = az ad app create --display-name $AppName --query appId -o tsv; Assert-Ok 'create app registration' }
$spId = az ad sp list --filter "appId eq '$appId'" --query '[0].id' -o tsv; Assert-Ok 'look up service principal'
if (-not $spId) { $spId = az ad sp create --id $appId --query id -o tsv; Assert-Ok 'create service principal' }

# 4. Federated credential: only this repo's protected environment
Write-Host '4/6 GitHub trust (environment only)'
# The subject must equal, character for character, what GitHub puts in the job's token. A repository with GitHub's
# "immutable subject" on (this one, seen on deploy run 1, 2026-11-26) uses owner and repository IDs:
#   repo:Owner@123/Name@456:environment:azure-dev        (otherwise repo:Owner/Name:environment:azure-dev)
# so read the prefix from GitHub instead of assuming it.
$oidcJson = gh api "repos/$Repo/actions/oidc/customization/sub"
if ($LASTEXITCODE -ne 0 -or -not $oidcJson) { throw 'Step failed: read the GitHub OIDC subject format (gh must be installed and signed in: gh auth login). Nothing after this step ran.' }
$oidc = $oidcJson | Out-String | ConvertFrom-Json
if (-not $oidc.use_default) { throw 'Step failed: this repository uses a custom OIDC subject template; tell the Lead (the trust has to be built from it). Nothing after this step ran.' }
$prefix = if ($oidc.sub_claim_prefix) { $oidc.sub_claim_prefix } else { "repo:$Repo" }
$subject = "${prefix}:environment:${GitHubEnvironment}"
Write-Host "  subject: $subject"
$fed = @{ name = 'github-azure-dev'; issuer = 'https://token.actions.githubusercontent.com'
  subject = $subject; audiences = @('api://AzureADTokenExchange') } | ConvertTo-Json
$current = @(az ad app federated-credential list --id $appId --query "[?name=='github-azure-dev'].subject" -o tsv); Assert-Ok 'list federated credentials'
if (-not $current[0]) {
  $tmp = Write-JsonTemp $fed
  az ad app federated-credential create --id $appId --parameters "@$tmp" | Out-Null; $rc = $LASTEXITCODE
  Remove-Item $tmp
  Assert-Ok 'create federated credential' $rc
} elseif ($current[0] -cne $subject) {
  Write-Host "  replacing the old subject: $($current[0])"
  $tmp = Write-JsonTemp $fed
  az ad app federated-credential update --id $appId --federated-credential-id github-azure-dev --parameters "@$tmp" | Out-Null; $rc = $LASTEXITCODE
  Remove-Item $tmp
  Assert-Ok 'update federated credential subject' $rc
} else { Write-Host '  already correct' }

# 5. Contributor on the one resource group. A new identity can take a minute to be visible to role assignment, so retry.
Write-Host '5/6 Contributor on the resource group only'
$scope = "/subscriptions/$sub/resourceGroups/$ResourceGroup"
$found = @(az role assignment list --scope $scope --role Contributor --query "[?principalId=='$spId'].id" -o tsv); Assert-Ok 'look up the existing role assignment'
$assigned = [bool]$found[0]
if ($assigned) { Write-Host '  already assigned' }
for ($i = 1; $i -le 6 -and -not $assigned; $i++) {
  az role assignment create --assignee-object-id $spId --assignee-principal-type ServicePrincipal --role Contributor --scope $scope | Out-Null
  if ($LASTEXITCODE -eq 0) { $assigned = $true } else { Write-Host "  not visible yet, retrying in 20s ($i/6)"; Start-Sleep -Seconds 20 }
}
if (-not $assigned) { throw 'Step failed: role assignment (identity still not visible after 2 minutes). Run the script again; earlier steps will be skipped.' }

# 6. Providers (free; registration is a subscription-level action)
Write-Host '6/6 Resource providers'
foreach ($ns in 'Microsoft.App', 'Microsoft.ContainerRegistry', 'Microsoft.DBforPostgreSQL', 'Microsoft.KeyVault',
  'Microsoft.OperationalInsights', 'Microsoft.Insights', 'Microsoft.CognitiveServices', 'Microsoft.Storage', 'Microsoft.Consumption') {
  az provider register --namespace $ns | Out-Null; Assert-Ok "register provider $ns"
}

Write-Host ""
Write-Host "DONE. Paste these three IDs (identifiers, not secrets) back to the Lead:"
Write-Host "  AZURE_CLIENT_ID       = $appId"
Write-Host "  AZURE_TENANT_ID       = $tenant"
Write-Host "  AZURE_SUBSCRIPTION_ID = $sub"
