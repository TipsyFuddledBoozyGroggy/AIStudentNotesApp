<#
.SYNOPSIS
  One-time set-up of the Microsoft sign-in gate for the Tether proof of concept, run BY THE PRODUCT OWNER in their own
  terminal, signed in as themselves in both tools (az login, gh auth login). Agents never run this and never see its
  output beyond what the PO pastes. Works on Windows PowerShell 5.1 and PowerShell 7.

.WHY
  The cloud copy of Tether has no real sign-in of its own yet (only the developer login), so its web address must sit
  behind Microsoft sign-in. The deploy identity (tether-deployer) cannot create anything in Microsoft Entra, so the
  sign-in "app registration" has to be created by you. Run this AFTER the first deploy run (the address it needs only
  exists once the Container Apps environment does) and BEFORE the second.

.WHAT IT CREATES (nothing else)
  1. Entra app registration   tether-web-signin, single tenant (only accounts in YOUR directory can ever sign in),
                              with exactly one redirect address:
                              https://<web app>.<environment domain>/.auth/login/aad/callback
  2. Its enterprise app       with "assignment required" ON: only the people assigned below get in.
  3. Assignments              you, plus anyone you list in -TeamMembers (they must already be users or guests in your directory).
  4. One client secret        valid 1 year. It replaces any earlier secret of this app. It is sent straight into the
                              GitHub environment secret EASYAUTH_CLIENT_SECRET. It is never shown and never written to disk.
  5. GitHub secret            EASYAUTH_CLIENT_ID on the same GitHub environment (an identifier, stored as a secret so the
                              public workflow logs never show it).
  6. Consent                  for basic sign-in only (openid, profile, email, User.Read), given once for your directory.
                              An app with "assignment required" cannot be consented to by each user, so it is done here.

.WHAT IT DOES NOT DO
  It does not deploy anything, change any Azure resource, give the app access to mail, files or anything beyond basic
  sign-in, or touch the tether-deployer and tether-reader identities. It costs nothing.

.USAGE
  Use your own az profile, not the default one (that holds the agents' read-only identity):
    PowerShell:  $env:AZURE_CONFIG_DIR = "$HOME\.azure-po"      cmd:  set "AZURE_CONFIG_DIR=%USERPROFILE%\.azure-po"
  az login --tenant <your tenant id>          # as yourself (naming the tenant makes az ask for MFA when it is required)
  gh auth login                               # as yourself, once
  Script execution is disabled on this machine, so start it through powershell.exe (works from cmd and PowerShell):
  powershell -ExecutionPolicy Bypass -File .\scripts\azure\create-signin-app.ps1
  powershell -ExecutionPolicy Bypass -File .\scripts\azure\create-signin-app.ps1 -TeamMembers someone@yourdirectory.example,another@yourdirectory.example
  (Not from PowerShell ISE: it cannot answer the YES prompt of a script started this way.)
  Safe to run again: it reuses the app, adds missing people and issues a fresh secret (then run the deploy again).
  Add -Remove to delete the app registration and the two GitHub values. After the next deploy the web app is then
  internal only again (no gate, no public address).
#>
[CmdletBinding()]
param(
  [string]$ResourceGroup = 'tether-dev',
  [string]$EnvironmentName = 'tether-dev-env',
  [string]$WebAppName = 'tether-web',
  [string]$AppName = 'tether-web-signin',
  [string]$Repo = 'TipsyFuddledBoozyGroggy/AIStudentNotesApp',
  [string]$GitHubEnvironment = 'azure-dev',
  [string[]]$TeamMembers = @(),
  [switch]$Remove
)
$ErrorActionPreference = 'Stop'
# Started through `powershell -File` (the only way on a machine where scripts are disabled), a comma list arrives as ONE
# string ("a@x,b@x"), not an array. Split it here so both ways of starting the script give the same list.
$TeamMembers = @($TeamMembers | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })

$SecretName = 'EASYAUTH_CLIENT_SECRET'
$ClientIdSecretName = 'EASYAUTH_CLIENT_ID'
$GraphAppId = '00000003-0000-0000-c000-000000000000'   # Microsoft Graph, the same in every directory
$DefaultAccessRole = '00000000-0000-0000-0000-000000000000'   # "default access": the app defines no roles of its own
$SignInScopes = 'openid profile email User.Read'

# Native commands (az, gh) do not stop PowerShell when they fail, so every call goes through this and is checked.
function Invoke-Checked {
  param([Parameter(Mandatory = $true)][string]$What, [Parameter(Mandatory = $true)][scriptblock]$Call)
  $global:LASTEXITCODE = 0
  $result = & $Call
  if ($LASTEXITCODE -ne 0) { throw "$What failed (exit code $LASTEXITCODE). Stopped here; nothing after this step was done." }
  return $result
}

# A freshly created Entra object can take a few seconds to be visible to the next call.
function Invoke-CheckedWithRetry {
  param([Parameter(Mandatory = $true)][string]$What, [Parameter(Mandatory = $true)][scriptblock]$Call, [int]$Attempts = 5, [int]$DelaySeconds = 6)
  for ($i = 1; $i -le $Attempts; $i++) {
    $global:LASTEXITCODE = 0
    $result = & $Call
    if ($LASTEXITCODE -eq 0) { return $result }
    if ($i -lt $Attempts) { Write-Host "  $What did not succeed yet (attempt $i of $Attempts); waiting $DelaySeconds seconds." ; Start-Sleep -Seconds $DelaySeconds }
  }
  throw "$What failed after $Attempts attempts (exit code $LASTEXITCODE). Stopped here; nothing after this step was done."
}

# Always an array, on PowerShell 5.1 and 7 alike (they differ in how ConvertFrom-Json hands back arrays).
function ConvertFrom-JsonArray {
  param([object]$Lines)
  $text = (@($Lines) -join "`n")
  if ([string]::IsNullOrWhiteSpace($text)) { return , @() }
  $parsed = $text | ConvertFrom-Json
  if ($null -eq $parsed) { return , @() }
  return , @($parsed)
}

function ConvertFrom-JsonObject {
  param([object]$Lines)
  $text = (@($Lines) -join "`n")
  if ([string]::IsNullOrWhiteSpace($text)) { return $null }
  return ($text | ConvertFrom-Json)
}

# The "value" list of a Microsoft Graph answer, as an array (empty when absent).
function Get-GraphValues {
  param([object]$Lines)
  $answer = ConvertFrom-JsonObject $Lines
  if ($null -eq $answer -or -not ($answer.PSObject.Properties.Name -contains 'value') -or $null -eq $answer.value) { return , @() }
  return , @($answer.value)
}

# Temp files for request bodies: UTF-8 WITHOUT a byte order mark (az rejects a BOM), always removed.
function Invoke-WithJsonFile {
  param([Parameter(Mandatory = $true)][string]$Json, [Parameter(Mandatory = $true)][scriptblock]$Use)
  $path = [IO.Path]::GetTempFileName()
  try {
    [IO.File]::WriteAllText($path, $Json, (New-Object System.Text.UTF8Encoding($false)))
    & $Use $path
  }
  finally {
    Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
  }
}

function Find-SignInApp {
  $apps = ConvertFrom-JsonArray (Invoke-Checked "Looking up the app registration '$AppName'" { az ad app list --display-name $AppName --output json })
  # --display-name matches by prefix; keep exact matches only.
  $exact = @($apps | Where-Object { $_.displayName -eq $AppName })
  if ($exact.Count -gt 1) { throw "There are $($exact.Count) app registrations named '$AppName'. Delete the extra ones in the Entra admin center (App registrations), then run this again." }
  if ($exact.Count -eq 1) { return $exact[0] }
  return $null
}

foreach ($tool in 'az', 'gh') {
  if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "'$tool' is not installed or not on PATH." }
}

$account = ConvertFrom-JsonObject (Invoke-Checked 'Reading the signed-in Azure account (run: az login)' { az account show --output json })
if ($null -eq $account) { throw 'Not signed in to Azure. Run: az login' }
# The default az profile on this machine belongs to the agents' read-only identity (a service principal). The PO signs in
# under a separate profile; when AZURE_CONFIG_DIR is not pointing at it, stop here instead of failing part way.
if ($account.user.type -ne 'user') {
  throw 'az is signed in as a service principal, not as you. Point AZURE_CONFIG_DIR at your own sign-in first (PowerShell: $env:AZURE_CONFIG_DIR = "$HOME\.azure-po"   cmd: set AZURE_CONFIG_DIR=%USERPROFILE%\.azure-po), check with: az account show --query user.name -o tsv, then run this script again.'
}
Invoke-Checked 'Checking the GitHub sign-in (run: gh auth login)' { gh auth status } | Out-Null
Invoke-Checked "Finding the GitHub environment '$GitHubEnvironment' in $Repo (create it first: repository Settings, Environments)" {
  gh api "repos/$Repo/environments/$GitHubEnvironment" --jq .name
} | Out-Null

Write-Host ""
Write-Host "Azure directory : $($account.tenantId)"
Write-Host "Signed in as    : $($account.user.name)"
Write-Host "App registration: $AppName (single tenant, assignment required)"
Write-Host "GitHub          : $Repo, environment '$GitHubEnvironment'"
if ($Remove) { Write-Host "Action          : REMOVE the app registration and the secrets $SecretName and $ClientIdSecretName" }
else { Write-Host "Team members    : you" $(if ($TeamMembers.Count -gt 0) { "+ $($TeamMembers.Count) more" } else { "(nobody else; add people later with -TeamMembers)" }) }
Write-Host ""
$answer = Read-Host "Type YES to continue with the directory above"
if ($answer -ne 'YES') { Write-Host 'Cancelled. Nothing changed.'; exit 1 }

if ($Remove) {
  $app = Find-SignInApp
  if ($null -ne $app) {
    Invoke-Checked "Deleting the app registration '$AppName'" { az ad app delete --id $app.appId } | Out-Null
    Write-Host "Deleted the app registration (its enterprise app and secret go with it)."
  }
  else { Write-Host "No app registration named '$AppName' exists." }
  $secretNames = @(Invoke-Checked 'Listing GitHub environment secrets' { gh secret list --env $GitHubEnvironment --repo $Repo --json name --jq '.[].name' })
  if ($secretNames -contains $SecretName) {
    Invoke-Checked "Deleting the GitHub secret $SecretName" { gh secret delete $SecretName --env $GitHubEnvironment --repo $Repo } | Out-Null
  }
  if ($secretNames -contains $ClientIdSecretName) {
    Invoke-Checked "Deleting the GitHub secret $ClientIdSecretName" { gh secret delete $ClientIdSecretName --env $GitHubEnvironment --repo $Repo } | Out-Null
  }
  Write-Host ""
  Write-Host "REMOVED. The web app keeps its current state until the next deploy run."
  Write-Host "Run the deploy workflow again now: with no gate configured it makes the web app internal only."
  exit 0
}

# 0. The web address. It exists only after the first deploy run created the Container Apps environment.
$defaultDomain = Invoke-Checked "Reading the Container Apps environment '$EnvironmentName' in '$ResourceGroup' (has the first deploy run finished?)" {
  az resource show --resource-group $ResourceGroup --name $EnvironmentName --resource-type Microsoft.App/managedEnvironments --query properties.defaultDomain --output tsv
}
$defaultDomain = ("$defaultDomain").Trim()
if ([string]::IsNullOrWhiteSpace($defaultDomain)) { throw "The environment '$EnvironmentName' has no default domain yet. Run the deploy workflow once first." }
$webUrl = "https://$WebAppName.$defaultDomain"
$redirectUri = "$webUrl/.auth/login/aad/callback"

# 1. App registration: single tenant, one redirect address, ID tokens on (the built-in sign-in needs them).
$app = Find-SignInApp
if ($null -eq $app) {
  $app = ConvertFrom-JsonObject (Invoke-Checked "Creating the app registration '$AppName'" {
      az ad app create --display-name $AppName --sign-in-audience AzureADMyOrg --web-redirect-uris $redirectUri --enable-id-token-issuance true --output json
    })
  Write-Host "Created the app registration."
}
else {
  Invoke-Checked "Updating the app registration '$AppName'" {
    az ad app update --id $app.appId --sign-in-audience AzureADMyOrg --web-redirect-uris $redirectUri --enable-id-token-issuance true
  } | Out-Null
  Write-Host "The app registration already existed; its redirect address was set again."
}
$appId = $app.appId
if ([string]::IsNullOrWhiteSpace($appId)) { throw 'The app registration has no application (client) id. Stopped.' }

# 2. Enterprise app (service principal) with "assignment required" ON.
$principals = ConvertFrom-JsonArray (Invoke-CheckedWithRetry 'Looking up the enterprise app' { az ad sp list --filter "appId eq '$appId'" --output json })
if ($principals.Count -eq 0) {
  $principal = ConvertFrom-JsonObject (Invoke-CheckedWithRetry 'Creating the enterprise app' { az ad sp create --id $appId --output json })
}
else { $principal = $principals[0] }
$principalId = $principal.id
if ([string]::IsNullOrWhiteSpace($principalId)) { throw 'The enterprise app has no object id. Stopped.' }
Invoke-CheckedWithRetry 'Turning on "assignment required"' { az ad sp update --id $principalId --set appRoleAssignmentRequired=true } | Out-Null
$required = Invoke-CheckedWithRetry 'Confirming "assignment required"' { az ad sp show --id $principalId --query appRoleAssignmentRequired --output tsv }
if (("$required").Trim() -ne 'true') { throw '"Assignment required" is not on for the enterprise app. Stopped before creating a secret; without it every account in the directory could sign in.' }

# 3. Who may sign in: you, plus -TeamMembers.
$wanted = @()
$me = Invoke-Checked 'Reading your own user id' { az ad signed-in-user show --query id --output tsv }
$wanted += ("$me").Trim()
foreach ($member in $TeamMembers) {
  $memberId = Invoke-Checked "Finding the user '$member' in your directory (invite them as a guest first if they are not in it)" { az ad user show --id $member --query id --output tsv }
  $wanted += ("$memberId").Trim()
}
$assignmentsUrl = "https://graph.microsoft.com/v1.0/servicePrincipals/$principalId/appRoleAssignedTo"
$existing = @(Get-GraphValues (Invoke-CheckedWithRetry 'Listing who is assigned' { az rest --method get --url $assignmentsUrl --output json }) | ForEach-Object { $_.principalId })
$added = 0
foreach ($userId in ($wanted | Select-Object -Unique)) {
  if ($existing -contains $userId) { continue }
  $body = @{ principalId = $userId; resourceId = $principalId; appRoleId = $DefaultAccessRole } | ConvertTo-Json -Compress
  Invoke-WithJsonFile -Json $body -Use {
    param($path)
    Invoke-CheckedWithRetry 'Assigning a user to the app' { az rest --method post --url $assignmentsUrl --body "@$path" --output none } | Out-Null
  }
  $added++
}
Write-Host "People allowed to sign in: $(@($wanted | Select-Object -Unique).Count) ($added newly assigned)."

# 4. Client secret, straight into the GitHub environment secret. Never displayed, never written to a file.
$secret = $null
try {
  $secret = Invoke-CheckedWithRetry 'Creating the client secret' {
    az ad app credential reset --id $appId --display-name "tether-poc-$(Get-Date -Format 'yyyy-MM-dd')" --years 1 --query password --output tsv
  }
  $secret = ("$secret").Trim()
  if ([string]::IsNullOrWhiteSpace($secret)) { throw 'Azure returned an empty client secret. Stopped.' }
  $global:LASTEXITCODE = 0
  $secret | gh secret set $SecretName --env $GitHubEnvironment --repo $Repo | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "Storing the GitHub secret $SecretName failed (exit code $LASTEXITCODE). Run this script again; it issues a fresh secret." }
}
finally {
  $secret = $null
}
Write-Host "Client secret stored as GitHub environment secret $SecretName (valid 1 year; run this script again to renew it)."

# 5. Client id as a GitHub environment secret: an identifier, but only secrets are masked in the public workflow logs.
$global:LASTEXITCODE = 0
$appId | gh secret set $ClientIdSecretName --env $GitHubEnvironment --repo $Repo | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Storing the GitHub secret $ClientIdSecretName failed (exit code $LASTEXITCODE). Run this script again." }

# 6. Consent for basic sign-in, once for the directory. Last on purpose: everything above is already complete if this fails.
$grants = ConvertFrom-JsonArray (Invoke-CheckedWithRetry 'Listing existing consent' { az ad app permission list-grants --id $appId --output json })
$hasConsent = @($grants | Where-Object { $_.consentType -eq 'AllPrincipals' -and ("$($_.scope)" -split '\s+') -contains 'openid' }).Count -gt 0
if (-not $hasConsent) {
  $global:LASTEXITCODE = 0
  az ad app permission grant --id $appId --api $GraphAppId --scope $SignInScopes --output none
  if ($LASTEXITCODE -ne 0) {
    throw ("Giving directory-wide consent failed (exit code $LASTEXITCODE). Everything else is done. Either run this script again, or sign in to the app " +
      "yourself first and tick 'Consent on behalf of your organization' on the Microsoft permissions screen.")
  }
}

Write-Host ""
Write-Host "DONE."
Write-Host "  App address (works after the next deploy run): $webUrl"
Write-Host "  Sign-in app client id (stored for you as the secret $ClientIdSecretName): $appId"
Write-Host ""
Write-Host "Next: GitHub, Actions, 'Deploy Azure dev (POC)', Run workflow, approve it. That run puts the web app behind this sign-in."
Write-Host "To let someone else in later: run this again with -TeamMembers, or use Entra admin center, Enterprise applications, $AppName, Users and groups."
