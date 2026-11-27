<#
.SYNOPSIS
  Adds or removes people on the Tether invite list (ADR-022 R3, docs/architecture/go-live-signin-adr.md), run BY THE
  PRODUCT OWNER in their own terminal, signed in as themselves in both tools (az login, gh auth login). Agents never run
  this and never see the list. Works on Windows PowerShell 5.1 and PowerShell 7.

.WHY
  Tether is invite-only. Google decides who gets as far as Tether (the "Test users" list in the Google Cloud console);
  Tether then lets in only the Google addresses on ITS OWN list. This script is the one command that changes Tether's
  list. Adding the same person as a Google test user is still a separate step in the console.

.WHERE THE LIST LIVES
  %USERPROFILE%\.tether\invited-emails.txt, one address per line. That file is yours and is the only readable copy
  (GitHub never shows a secret again). It is outside the repository on purpose and is never committed. This script
  prints COUNTS only, never an address; open the file yourself to read it.

.WHAT IT DOES
  1. Updates the list file.
  2. Writes the whole list to the GitHub environment secret TETHER_INVITED_EMAILS, so every later deploy keeps it.
  3. If the running site already has the invite-list setting (the Container Apps secret invite-emails on tether-api):
     writes it there too and RESTARTS the API so the change takes effect now. The restart takes about a minute. People
     stay signed in; a lecture that is recording pauses itself and can be resumed. Someone you REMOVED is signed out
     by that restart.
     If the site does not have the setting yet (before the first deploy of this version): it writes GitHub only and
     says so; the list then applies on the next deploy.

.WHAT IT DOES NOT DO
  It does not deploy, does not touch the Google console, and never empties the list (the site refuses to start with
  an empty list; to shut everyone out, run the deploy workflow with access = closed).

.WHERE THE ADDRESSES GO ON THIS MACHINE
  Only into the list file above and, for the few seconds of step 3, into a second file in the same private folder
  that the Azure CLI reads the value from and that is deleted straight afterwards. They are never put on a command
  line (so they do not show in the list of running programs or in the Azure CLI's own command log) and never printed.

.DO NOT RUN IT WHILE A DEPLOY IS RUNNING
  Wait for the deploy to finish first. Nothing breaks if you forget: the next deploy applies the GitHub copy, which is
  the list this script wrote. But the restart from this script and the deploy's own restart would overlap.

.USAGE
  Use your own az profile, not the default one (that holds the agents' read-only identity). From cmd, in the
  repository folder, one line:
    set "AZURE_CONFIG_DIR=%USERPROFILE%\.azure-po" && powershell -ExecutionPolicy Bypass -File .\scripts\azure\set-invites.ps1 -Add someone@example.com
  More than one address: separate them with commas, no spaces:  -Add one@example.com,two@example.com
    ... -File .\scripts\azure\set-invites.ps1 -Remove someone@example.com
    ... -File .\scripts\azure\set-invites.ps1 -List        (how many are on the list and where the file is; changes nothing, needs no sign-in)
  Running the same command again is safe; do that if a step failed part way.
  Not from PowerShell ISE: it cannot answer the YES prompt of a script started this way.
#>
[CmdletBinding()]
param(
  [string[]]$Add = @(),
  [string[]]$Remove = @(),
  [switch]$List,
  [string]$ResourceGroup = 'tether-dev',
  [string]$ApiAppName = 'tether-api',
  [string]$Repo = 'TipsyFuddledBoozyGroggy/AIStudentNotesApp',
  [string]$GitHubEnvironment = 'azure-dev',
  [string]$ListDirectory = (Join-Path $env:USERPROFILE '.tether')
)
$ErrorActionPreference = 'Stop'

$SecretName = 'TETHER_INVITED_EMAILS'    # GitHub environment secret; infra/main.bicep copies it on every deploy
$LiveSecretName = 'invite-emails'        # Container Apps secret on the API (ADR-022 section 2)
$MaxInvites = 100                        # Google's test-user cap, and the limit the API enforces
$ListFileName = 'invited-emails.txt'
# Deliberately narrower than "any email": lower-case letters, digits and . _ + - before the @. Every ordinary Google
# address fits. It also keeps characters that mean something to the Windows command line (& % ^ | < > " space) out
# of the value this script hands to az.
$AddressPattern = '^[a-z0-9._+-]+@[a-z0-9-]+(\.[a-z0-9-]+)+$'

# Native commands (az, gh) do not stop PowerShell when they fail, so every call goes through this and is checked.
function Invoke-Checked {
  param([Parameter(Mandatory = $true)][string]$What, [Parameter(Mandatory = $true)][scriptblock]$Call)
  $global:LASTEXITCODE = 0
  $result = & $Call
  if ($LASTEXITCODE -ne 0) { throw "$What failed (exit code $LASTEXITCODE)." }
  return $result
}

function ConvertFrom-JsonObject {
  param([object]$Lines)
  $text = (@($Lines) -join "`n")
  if ([string]::IsNullOrWhiteSpace($text)) { return $null }
  return ($text | ConvertFrom-Json)
}

# Trimmed and lower-cased, the form the API compares with. Throws (naming the position, never the address) when one is not usable.
function ConvertTo-InviteAddresses {
  param([string[]]$Values, [Parameter(Mandatory = $true)][string]$Source)
  $result = New-Object System.Collections.Generic.List[string]
  $position = 0
  foreach ($value in @($Values)) {
    foreach ($part in ("$value" -split '[,\r\n]')) {
      $address = $part.Trim().ToLowerInvariant()
      if ($address.Length -eq 0) { continue }
      $position++
      if ($address.Length -gt 320 -or $address -cnotmatch $AddressPattern) {
        throw "Entry $position in $Source is not an email address this script accepts (letters, digits and . _ + - before the @; no spaces). Nothing was changed."
      }
      if (-not $result.Contains($address)) { $result.Add($address) }
    }
  }
  return , $result.ToArray()
}

# Sends a value to `gh secret set` on standard input, EXACTLY as given (no added line break), without ever putting it
# on a command line or in a file.
function Set-GitHubSecret {
  param([Parameter(Mandatory = $true)][string]$Name, [Parameter(Mandatory = $true)][string]$Value)
  $start = New-Object System.Diagnostics.ProcessStartInfo
  $start.FileName = 'gh'
  $start.Arguments = "secret set $Name --env $GitHubEnvironment --repo $Repo"
  $start.UseShellExecute = $false
  $start.RedirectStandardInput = $true
  $start.RedirectStandardOutput = $true
  $start.RedirectStandardError = $true
  $start.CreateNoWindow = $true
  $process = New-Object System.Diagnostics.Process
  $process.StartInfo = $start
  try {
    [void]$process.Start()
    $process.StandardInput.Write($Value)
    $process.StandardInput.Close()
    [void]$process.StandardOutput.ReadToEnd()
    # What gh says when it fails ("not logged in", "environment not found", "no permission"). The value went in on
    # standard input, so gh's messages never hold it; they are shown so the failure can be acted on.
    $said = ("$($process.StandardError.ReadToEnd())").Trim()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
      if ([string]::IsNullOrWhiteSpace($said)) { $said = 'gh gave no message; check with: gh auth status' }
      throw "Storing the GitHub secret $Name failed (exit code $($process.ExitCode)). gh said: $said"
    }
  }
  finally {
    $process.Dispose()
  }
}

# The signed-in Azure account, when it is YOU. Otherwise stops with the exact sign-in line, including your tenant id when
# it can be read (the agents' read-only identity on this machine lives in the same directory, so its profile knows it).
function Get-OwnAzureAccount {
  # Local to this function: Windows PowerShell 5.1 turns anything a program writes to its error stream into a
  # terminating error when it is redirected under 'Stop'.
  $ErrorActionPreference = 'Continue'
  $account = $null
  $global:LASTEXITCODE = 0
  $lines = az account show --output json 2>$null
  if ($LASTEXITCODE -eq 0) { $account = ConvertFrom-JsonObject $lines }
  if ($null -ne $account -and $account.user.type -eq 'user') { return $account }

  $tenant = ''
  if ($null -ne $account) { $tenant = "$($account.tenantId)" }
  else {
    # Not signed in under this profile. The default profile (the agents' identity) can still say which directory it is.
    $saved = $env:AZURE_CONFIG_DIR
    try {
      $env:AZURE_CONFIG_DIR = $null
      $global:LASTEXITCODE = 0
      $answer = az account show --query tenantId --output tsv 2>$null
      if ($LASTEXITCODE -eq 0) { $tenant = ("$answer").Trim() }
    }
    finally { $env:AZURE_CONFIG_DIR = $saved }
  }
  if ($tenant -notmatch '^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$') {
    $tenant = '<your tenant id: Azure portal, Microsoft Entra ID, Overview, "Tenant ID">'
  }
  $why = 'You are not signed in to az under your own profile.'
  if ($null -ne $account) { $why = 'az is signed in as a service principal (the agents'' read-only identity), not as you.' }
  throw ("$why Nothing was changed.`n" +
    "In cmd, sign in once (a browser window opens):`n" +
    "  set ""AZURE_CONFIG_DIR=%USERPROFILE%\.azure-po"" && az login --tenant $tenant`n" +
    "Then start this script again exactly as in its USAGE lines (with the same set ""AZURE_CONFIG_DIR=..."" in front).")
}

function Write-ListFile {
  param([Parameter(Mandatory = $true)][string]$Path, [string[]]$Addresses)
  $text = if (@($Addresses).Count -gt 0) { (@($Addresses) -join "`r`n") + "`r`n" } else { '' }
  [IO.File]::WriteAllText($Path, $text, (New-Object System.Text.UTF8Encoding($false)))
}

# Started through `powershell -File`, a comma list arrives as ONE string ("a@x,b@x"); ConvertTo-InviteAddresses splits it.
$adds = ConvertTo-InviteAddresses -Values $Add -Source '-Add'
$removes = ConvertTo-InviteAddresses -Values $Remove -Source '-Remove'

# The list never lives inside the repository folder (it holds people's addresses, and the repository is public).
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..')).TrimEnd('\')
$listDirectoryFull = [IO.Path]::GetFullPath($ListDirectory).TrimEnd('\')
if (($listDirectoryFull + '\').StartsWith($repoRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
  throw "The invite list must not be kept inside the repository folder ($repoRoot). Use the default (%USERPROFILE%\.tether) or another private folder."
}
$listPath = Join-Path $listDirectoryFull $ListFileName

$current = @()
if (Test-Path -LiteralPath $listPath) {
  $current = ConvertTo-InviteAddresses -Values @(Get-Content -LiteralPath $listPath) -Source 'the list file'
}

if ($List) {
  Write-Host "Invite list: $($current.Count) address(es)."
  Write-Host "The file    : $listPath  (open it yourself to read the addresses; this script never prints them)"
  exit 0
}
if ($adds.Count -eq 0 -and $removes.Count -eq 0) {
  throw 'Nothing to do. Use -Add <address>, -Remove <address> or -List. Nothing was changed.'
}
$both = @($adds | Where-Object { $removes -contains $_ })
if ($both.Count -gt 0) { throw 'The same address is given to -Add and -Remove. Nothing was changed.' }

$updated = New-Object System.Collections.Generic.List[string]
foreach ($address in $current) { if (-not ($removes -contains $address)) { $updated.Add($address) } }
$removed = $current.Count - $updated.Count
$added = 0
foreach ($address in $adds) { if (-not $updated.Contains($address)) { $updated.Add($address); $added++ } }
$final = $updated.ToArray()

if ($final.Count -eq 0) {
  throw 'That would leave the invite list empty, and the site refuses to start with an empty list. Nothing was changed. To shut everyone out, run the deploy workflow with access = closed.'
}
if ($final.Count -gt $MaxInvites) {
  throw "That would make $($final.Count) addresses; the limit is $MaxInvites. Nothing was changed."
}

foreach ($tool in 'az', 'gh') {
  if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "'$tool' is not installed or not on PATH. Nothing was changed." }
}
# The default az profile on this machine belongs to the agents' read-only identity (a service principal). You sign in
# under a separate profile; when AZURE_CONFIG_DIR is not pointing at it, stop here instead of failing part way.
$account = Get-OwnAzureAccount
Invoke-Checked 'Checking the GitHub sign-in (run: gh auth login)' { gh auth status } | Out-Null
Invoke-Checked "Finding the GitHub environment '$GitHubEnvironment' in $Repo (create it first: repository Settings, Environments)" {
  gh api "repos/$Repo/environments/$GitHubEnvironment" --jq .name
} | Out-Null

# Does the running site have the invite-list setting yet? Names only are read; no secret value is fetched.
$liveHasSetting = $false
$apps = @(Invoke-Checked "Looking for the container app '$ApiAppName' in '$ResourceGroup'" {
    az resource list --resource-group $ResourceGroup --resource-type Microsoft.App/containerApps --query "[?name=='$ApiAppName'].name" --output tsv
  })
if (@($apps | Where-Object { ("$_").Trim() -eq $ApiAppName }).Count -gt 0) {
  $liveNames = @(Invoke-Checked "Listing the secret names of '$ApiAppName'" {
      az containerapp secret list --resource-group $ResourceGroup --name $ApiAppName --query "[].name" --output tsv
    })
  $liveHasSetting = @($liveNames | Where-Object { ("$_").Trim() -eq $LiveSecretName }).Count -gt 0
}

Write-Host ""
Write-Host "Signed in to Azure as : $($account.user.name)"
Write-Host "GitHub                : $Repo, environment '$GitHubEnvironment'"
Write-Host "Invite list           : $($current.Count) now, $($final.Count) after this ($added added, $removed removed)"
Write-Host "List file             : $listPath"
if ($liveHasSetting) {
  Write-Host "Running site          : updated now. THE API RESTARTS (about a minute). People stay signed in; a lecture that is"
  Write-Host "                        recording pauses itself and can be resumed. Anyone removed is signed out."
}
else {
  Write-Host "Running site          : not changed. It does not have the invite-list setting yet, so the list applies on the NEXT DEPLOY."
}
if ($added -gt 0) { Write-Host "Reminder              : each new person must also be a Test user in the Google Cloud console (project tether-oauth-dev, Audience)." }
Write-Host ""
$answer = Read-Host "Type YES to continue"
if ($answer -ne 'YES') { Write-Host 'Cancelled. Nothing changed.'; exit 1 }

# 1. The list file first (the only readable copy), put back as it was if GitHub cannot be written.
New-Item -ItemType Directory -Force -Path $listDirectoryFull | Out-Null
Write-ListFile -Path $listPath -Addresses $final
$joined = $final -join ','
try {
  # 2. GitHub: every later deploy applies this list.
  Set-GitHubSecret -Name $SecretName -Value $joined
}
catch {
  Write-ListFile -Path $listPath -Addresses $current
  throw "$($_.Exception.Message) Nothing was changed: the list file was put back as it was."
}
Write-Host "List file and GitHub secret $SecretName updated ($($final.Count) address(es))."

if (-not $liveHasSetting) {
  Write-Host ""
  Write-Host "DONE. The running site was not changed: the list applies on the next deploy"
  Write-Host "(GitHub, Actions, 'Deploy Azure dev (POC)', Run workflow, approve it)."
  exit 0
}

# 3. The running site: the secret, then a restart so the API reads it (a changed secret is not picked up by itself).
#    The value is handed to the Azure CLI in a FILE (key=@file: the CLI reads the value from the file itself), never on
#    the command line. The file sits next to the list file, in the same private folder, and is deleted straight away.
function Set-LiveSecretFromFile {
  param([Parameter(Mandatory = $true)][string]$Name, [Parameter(Mandatory = $true)][string]$Value)
  $path = Join-Path $listDirectoryFull ('.' + $Name + '-' + [Guid]::NewGuid().ToString('N') + '.tmp')
  try {
    [IO.File]::WriteAllText($path, $Value, (New-Object System.Text.UTF8Encoding($false)))
    Invoke-Checked "Writing the setting '$Name' on '$ApiAppName'" {
      az containerapp secret set --resource-group $ResourceGroup --name $ApiAppName --secrets "$Name=@$path" --output none
    } | Out-Null
  }
  finally {
    Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
  }
}
# True when the stored value of a setting is exactly the expected one. The value stays in memory; it is not printed.
function Test-LiveSecret {
  param([Parameter(Mandatory = $true)][string]$Name, [Parameter(Mandatory = $true)][string]$Expected)
  $stored = Invoke-Checked "Reading back the setting '$Name' on '$ApiAppName'" {
    az containerapp secret show --resource-group $ResourceGroup --name $ApiAppName --secret-name $Name --query value --output tsv
  }
  return ((@($stored) -join '').Trim() -ceq $Expected)
}

$changedLive = $false
try {
  # First prove, on a throwaway setting, that THIS Azure CLI really reads a value from a file. If it did not, the real
  # setting would end up holding the file's NAME, and the API would refuse to start at its next restart. So: try it on
  # a setting nothing uses, read it back, remove it; only then touch the real one.
  $probeName = 'invite-selftest'
  $probeValue = 'selftest-' + [Guid]::NewGuid().ToString('N')
  Set-LiveSecretFromFile -Name $probeName -Value $probeValue
  $probeOk = Test-LiveSecret -Name $probeName -Expected $probeValue
  $global:LASTEXITCODE = 0
  az containerapp secret remove --resource-group $ResourceGroup --name $ApiAppName --secret-names $probeName --output none
  if ($LASTEXITCODE -ne 0) { Write-Host "Note: the unused setting '$probeName' could not be removed from '$ApiAppName'. It holds nothing of yours and does no harm." }
  if (-not $probeOk) {
    throw "This Azure CLI did not read a value from a file the way this script needs, so the running site was left exactly as it was."
  }

  Set-LiveSecretFromFile -Name $LiveSecretName -Value $joined
  $changedLive = $true
  if (-not (Test-LiveSecret -Name $LiveSecretName -Expected $joined)) {
    throw "The invite list stored on the running site does not read back as the list just written. The API was NOT restarted, so the site still runs with the old list. RUN A DEPLOY NOW: it writes the correct list (the GitHub copy) and restarts the API."
  }

  $revisions = @(Invoke-Checked "Listing the active revisions of '$ApiAppName'" {
      az containerapp revision list --resource-group $ResourceGroup --name $ApiAppName --query "[?properties.active].name" --output tsv
    }) | ForEach-Object { ("$_").Trim() } | Where-Object { $_ }
  if (@($revisions).Count -eq 0) { throw "No active revision of '$ApiAppName' was found to restart." }
  foreach ($revision in @($revisions)) {
    Invoke-Checked "Restarting '$ApiAppName'" {
      az containerapp revision restart --resource-group $ResourceGroup --name $ApiAppName --revision $revision --output none
    } | Out-Null
  }
}
catch {
  $state = 'the RUNNING SITE STILL USES THE OLD LIST.'
  if ($changedLive) { $state = 'the RUNNING SITE MAY STILL USE THE OLD LIST until its API restarts.' }
  throw ("$($_.Exception.Message) Your list file and GitHub are up to date, but $state " +
    "Run a deploy (it applies the GitHub copy), or run the same command again.")
}
finally {
  $joined = $null
}

Write-Host ""
Write-Host "DONE. The running site has the new list and the API was restarted. Give it a minute before trying to sign in."
