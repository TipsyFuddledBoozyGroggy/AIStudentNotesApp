<#
.SYNOPSIS
  One-time creation of the two generated secrets the Tether proof-of-concept deploy needs, run BY THE PRODUCT OWNER in
  his own terminal, signed in to GitHub as himself (gh auth login). Agents never run this. Works on Windows PowerShell
  5.1 and PowerShell 7. It does not touch Azure and costs nothing.

.WHAT IT CREATES (nothing else)
  Two secrets on the GitHub environment "azure-dev" of this repository:
  1. TETHER_DB_ADMIN_PASSWORD   the database administrator password (random, 52 characters)
  2. TETHER_ROOT_SECRET         the API's root secret (48 random bytes, base64)
  Each value is generated here and sent straight to GitHub. It is never shown, never written to disk and nobody,
  including you, needs to know it. The deploy workflow hands both to Azure on every run, so the database and the app
  always agree, and a tear-down followed by a new deploy keeps working.

.WHAT IT DOES NOT DO
  It does not overwrite a secret that already exists (a new database password is harmless, but a new root secret
  invalidates saved idempotency keys and audit digests). Use -Rotate to replace one on purpose, then run the deploy.

.USAGE
  gh auth login                                             # as yourself, once
  .\scripts\azure\set-deploy-secrets.ps1
  .\scripts\azure\set-deploy-secrets.ps1 -Rotate DbPassword  # or RootSecret, or both: -Rotate DbPassword, RootSecret
#>
[CmdletBinding()]
param(
  [string]$Repo = 'TipsyFuddledBoozyGroggy/AIStudentNotesApp',
  [string]$GitHubEnvironment = 'azure-dev',
  [ValidateSet('DbPassword', 'RootSecret')][string[]]$Rotate = @()
)
$ErrorActionPreference = 'Stop'

# Native commands (gh) do not stop PowerShell when they fail, so every call is checked.
function Invoke-Checked {
  param([Parameter(Mandatory = $true)][string]$What, [Parameter(Mandatory = $true)][scriptblock]$Call)
  $global:LASTEXITCODE = 0
  $result = & $Call
  if ($LASTEXITCODE -ne 0) { throw "$What failed (exit code $LASTEXITCODE). Stopped here; nothing after this step was done." }
  return $result
}

function New-RandomBytes {
  param([int]$Count)
  $bytes = New-Object byte[] $Count
  $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
  try { $generator.GetBytes($bytes) } finally { $generator.Dispose() }
  return , $bytes
}

function Set-GitHubSecret {
  param([string]$Name, [string]$Value)
  $global:LASTEXITCODE = 0
  $Value | gh secret set $Name --env $GitHubEnvironment --repo $Repo | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "Storing the GitHub secret $Name failed (exit code $LASTEXITCODE)." }
}

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw "'gh' (GitHub CLI) is not installed or not on PATH." }
Invoke-Checked 'Checking the GitHub sign-in (run: gh auth login)' { gh auth status } | Out-Null
Invoke-Checked "Finding the GitHub environment '$GitHubEnvironment' in $Repo (create it first: repository Settings, Environments)" {
  gh api "repos/$Repo/environments/$GitHubEnvironment" --jq .name
} | Out-Null
$existing = @(Invoke-Checked 'Listing GitHub environment secrets' { gh secret list --env $GitHubEnvironment --repo $Repo --json name --jq '.[].name' })

Write-Host ""
Write-Host "GitHub: $Repo, environment '$GitHubEnvironment'"
$answer = Read-Host "Type YES to create the deploy secrets there"
if ($answer -ne 'YES') { Write-Host 'Cancelled. Nothing changed.'; exit 1 }

# 1. Database password: letters, digits, hyphen and underscore only (it is placed in a connection string).
#    The fixed 'Pg1-' start guarantees Azure's rule (upper case, lower case, digit, symbol) whatever the random part is.
if (($existing -contains 'TETHER_DB_ADMIN_PASSWORD') -and -not ($Rotate -contains 'DbPassword')) {
  Write-Host "TETHER_DB_ADMIN_PASSWORD already exists: left as it is."
}
else {
  $random = [Convert]::ToBase64String((New-RandomBytes 36)).Replace('+', '-').Replace('/', '_').TrimEnd('=')
  Set-GitHubSecret -Name 'TETHER_DB_ADMIN_PASSWORD' -Value ('Pg1-' + $random)
  $random = $null
  Write-Host "TETHER_DB_ADMIN_PASSWORD stored. The next deploy run applies it to the database and to the app together."
}

# 2. Root secret: base64 of 48 random bytes, as the API requires (Security:RootSecret).
if (($existing -contains 'TETHER_ROOT_SECRET') -and -not ($Rotate -contains 'RootSecret')) {
  Write-Host "TETHER_ROOT_SECRET already exists: left as it is."
}
else {
  $value = [Convert]::ToBase64String((New-RandomBytes 48))
  Set-GitHubSecret -Name 'TETHER_ROOT_SECRET' -Value $value
  $value = $null
  Write-Host "TETHER_ROOT_SECRET stored."
}

Write-Host ""
Write-Host "DONE. Next: GitHub, Actions, 'Deploy Azure dev (POC)', Run workflow."
