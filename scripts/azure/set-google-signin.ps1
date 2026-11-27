<#
.SYNOPSIS
  One-time set-up of Google sign-in for Tether in Azure (ADR-022, docs/architecture/go-live-signin-adr.md), run BY THE
  PRODUCT OWNER in their own terminal, signed in as themselves in both tools (az login, gh auth login). Agents never run
  this and never see the Google client secret. Works on Windows PowerShell 5.1 and PowerShell 7.

.WHY
  People sign in to Tether with their Google account. For that the site needs three things that only you can provide:
  the "client id" and "client secret" of the Google sign-in client named Tether (Google Cloud project tether-oauth-dev),
  and Google must be told the one address it may send people back to. The site also needs a private certificate that
  keeps people signed in across restarts; this script makes that once.

.WHAT IT DOES (nothing else)
  1. Prints the ONE address to add in the Google Cloud console (Authorized redirect URIs) and waits while you add it.
  2. Asks for the client id, and for the client secret with a HIDDEN prompt. The secret is sent straight into the
     GitHub environment secret GOOGLE_CLIENT_SECRET. It is never shown, never written to disk, never put on a command line.
  3. Stores the client id as the GitHub environment secret GOOGLE_CLIENT_ID (an identifier, stored as a secret so the
     public workflow logs never show it).
  4. Key-ring certificate: ONLY IF the secret DP_CERT_PFX does not exist yet, creates a certificate in memory (RSA 3072,
     10 years, no password) and stores it as DP_CERT_PFX. Running this script again never replaces it, so changing the
     Google secret later does not sign anyone out.

.ROTATING THE KEY-RING CERTIFICATE (rare; only on purpose)
  -RotateKeyRingCertificate does only this: it copies the certificate that is live in Azure to the secret
  DP_CERT_PFX_PREVIOUS (so people who are signed in stay signed in), then stores a new DP_CERT_PFX. Then run a deploy.
  It skips the Google questions.
  Only ONE earlier certificate is kept. Rotate at most once every 14 days (the longest a sign-in lasts), and deploy
  in between: a second rotation sooner signs out the people whose sign-in was protected by the certificate before last.

.WHAT IT DOES NOT DO
  It does not deploy anything or change any Azure resource (it only READS the web address, and for a rotation the
  current certificate), it does not touch the Google console for you, and it costs nothing. The invite list is a
  separate script: scripts\azure\set-invites.ps1.

.USAGE
  Use your own az profile, not the default one (that holds the agents' read-only identity). From cmd, in the
  repository folder, one line:
    set "AZURE_CONFIG_DIR=%USERPROFILE%\.azure-po" && powershell -ExecutionPolicy Bypass -File .\scripts\azure\set-google-signin.ps1
  First time on this machine, sign in once before that:
    set "AZURE_CONFIG_DIR=%USERPROFILE%\.azure-po" && az login --tenant <your tenant id>
    gh auth login
  Your tenant id: Azure portal, Microsoft Entra ID, Overview, "Tenant ID". Or just start this script: when you are not
  signed in yet it stops, changes nothing, and prints the sign-in line with the tenant id already filled in.
  Other forms:
    ... -File .\scripts\azure\set-google-signin.ps1 -ShowRedirectAddressOnly      (print the address again, change nothing;
                                                                                 needs the az sign-in only, not gh)
    ... -File .\scripts\azure\set-google-signin.ps1 -RotateKeyRingCertificate     (see above)
  Not from PowerShell ISE: it cannot answer the prompts of a script started this way.
  Safe to run again: it replaces the Google client id and secret and leaves the certificate alone.
  Never paste the client secret into a chat, an issue or a file in this repository.
#>
[CmdletBinding()]
param(
  [string]$ResourceGroup = 'tether-dev',
  [string]$EnvironmentName = 'tether-dev-env',
  [string]$WebAppName = 'tether-web',
  [string]$ApiAppName = 'tether-api',
  [string]$Repo = 'TipsyFuddledBoozyGroggy/AIStudentNotesApp',
  [string]$GitHubEnvironment = 'azure-dev',
  [switch]$ShowRedirectAddressOnly,
  [switch]$RotateKeyRingCertificate
)
$ErrorActionPreference = 'Stop'

$ClientIdSecretName = 'GOOGLE_CLIENT_ID'
$ClientSecretSecretName = 'GOOGLE_CLIENT_SECRET'
$CertSecretName = 'DP_CERT_PFX'
$PreviousCertSecretName = 'DP_CERT_PFX_PREVIOUS'
$LiveCertSecretName = 'dp-cert'   # the Container Apps secret infra/main.bicep fills from DP_CERT_PFX
# ADR-022 section 2: the callback path is fixed. The API, infra/main.bicep and the outside smoke check use the same string.
$CallbackPath = '/api/v1/auth/google/callback'
# The pattern the API checks at startup and infra/scripts/preflight.sh checks before a deploy.
$ClientIdPattern = '^[0-9]+-[a-z0-9]+\.apps\.googleusercontent\.com$'

# Native commands (az, gh) do not stop PowerShell when they fail, so every call goes through this and is checked.
function Invoke-Checked {
  param([Parameter(Mandatory = $true)][string]$What, [Parameter(Mandatory = $true)][scriptblock]$Call)
  $global:LASTEXITCODE = 0
  $result = & $Call
  if ($LASTEXITCODE -ne 0) { throw "$What failed (exit code $LASTEXITCODE). Stopped here; nothing after this step was done." }
  return $result
}

function ConvertFrom-JsonObject {
  param([object]$Lines)
  $text = (@($Lines) -join "`n")
  if ([string]::IsNullOrWhiteSpace($text)) { return $null }
  return ($text | ConvertFrom-Json)
}

# Sends a value to `gh secret set` on standard input, EXACTLY as given (no added line break), without ever putting it
# on a command line or in a file. A plain PowerShell pipe would add a line break to the secret.
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
      throw "Storing the GitHub secret $Name failed (exit code $($process.ExitCode)). gh said: $said Then run this script again."
    }
  }
  finally {
    $process.Dispose()
  }
}

# A new self-signed certificate with its private key, as base64 of a PFX with no password. Made in memory only:
# no certificate store, no file. RSA 3072, valid 10 years (ADR-022: the API accepts RSA of 2048 bits or more).
function New-KeyRingCertificateBase64 {
  $rsa = New-Object System.Security.Cryptography.RSACng 3072
  $certificate = $null
  try {
    $subject = New-Object System.Security.Cryptography.X509Certificates.X500DistinguishedName 'CN=tether-dp-keyring'
    $request = New-Object System.Security.Cryptography.X509Certificates.CertificateRequest(
      $subject, $rsa, [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $notBefore = [DateTimeOffset]::UtcNow.AddDays(-1)
    $certificate = $request.CreateSelfSigned($notBefore, $notBefore.AddYears(10))
    $bytes = $certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx)
    try { return [Convert]::ToBase64String($bytes) }
    finally { [Array]::Clear($bytes, 0, $bytes.Length) }
  }
  finally {
    if ($null -ne $certificate) { $certificate.Dispose() }
    $rsa.Dispose()
  }
}

# The plain text of a hidden prompt, kept only in memory.
function Read-HiddenValue {
  param([Parameter(Mandatory = $true)][string]$Prompt)
  $secure = Read-Host -Prompt $Prompt -AsSecureString
  $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
  try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
  finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

# The certificate that is live in Azure (the Container Apps secret dp-cert), or $null when the API has not been
# deployed with one yet. Read as you: only the owner of the resource group can list a container app's secrets.
function Get-LiveCertificateBase64 {
  $apps = @(Invoke-Checked "Looking for the container app '$ApiAppName' in '$ResourceGroup'" {
      az resource list --resource-group $ResourceGroup --resource-type Microsoft.App/containerApps --query "[?name=='$ApiAppName'].id" --output tsv
    })
  $appId = ("$($apps | Select-Object -First 1)").Trim()
  if ([string]::IsNullOrWhiteSpace($appId)) { return $null }
  $answer = ConvertFrom-JsonObject (Invoke-Checked "Reading the secrets of '$ApiAppName' (you need to be Owner or Contributor of '$ResourceGroup')" {
      az rest --method post --url "https://management.azure.com$appId/listSecrets?api-version=2025-01-01" --output json
    })
  if ($null -eq $answer -or -not ($answer.PSObject.Properties.Name -contains 'value')) { return $null }
  $match = @($answer.value | Where-Object { $_.name -eq $LiveCertSecretName })
  if ($match.Count -eq 0) { return $null }
  $value = ("$($match[0].value)").Trim()
  if ([string]::IsNullOrWhiteSpace($value)) { return $null }
  return $value
}

foreach ($tool in 'az', 'gh') {
  if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "'$tool' is not installed or not on PATH." }
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

# The default az profile on this machine belongs to the agents' read-only identity (a service principal). You sign in
# under a separate profile; when AZURE_CONFIG_DIR is not pointing at it, stop here instead of failing part way.
$account = Get-OwnAzureAccount

# The web address. It exists once the first deploy run has created the Container Apps environment.
$defaultDomain = Invoke-Checked "Reading the Container Apps environment '$EnvironmentName' in '$ResourceGroup' (has a deploy run finished?)" {
  az resource show --resource-group $ResourceGroup --name $EnvironmentName --resource-type Microsoft.App/managedEnvironments --query properties.defaultDomain --output tsv
}
$defaultDomain = ("$defaultDomain").Trim()
if ([string]::IsNullOrWhiteSpace($defaultDomain)) { throw "The environment '$EnvironmentName' has no default domain yet. Run the deploy workflow once first." }
$redirectAddress = "https://$WebAppName.$defaultDomain$CallbackPath"

if ($ShowRedirectAddressOnly) {
  Write-Host ""
  Write-Host "The address for Google Cloud console, Clients, Tether, Authorized redirect URIs:"
  Write-Host "  $redirectAddress"
  Write-Host "Nothing was changed."
  exit 0
}

Invoke-Checked 'Checking the GitHub sign-in (run: gh auth login)' { gh auth status } | Out-Null
Invoke-Checked "Finding the GitHub environment '$GitHubEnvironment' in $Repo (create it first: repository Settings, Environments)" {
  gh api "repos/$Repo/environments/$GitHubEnvironment" --jq .name
} | Out-Null
# Names only. GitHub never gives a secret's value back.
$secretNames = @(Invoke-Checked 'Listing GitHub environment secrets' { gh secret list --env $GitHubEnvironment --repo $Repo --json name --jq '.[].name' })

Write-Host ""
Write-Host "Signed in to Azure as : $($account.user.name)"
Write-Host "GitHub                : $Repo, environment '$GitHubEnvironment'"

if ($RotateKeyRingCertificate) {
  Write-Host "Action                : ROTATE the key-ring certificate ($CertSecretName). The Google values are not touched."
  Write-Host ""
  Write-Host "The certificate that is live now is kept as $PreviousCertSecretName, so people stay signed in."
  Write-Host "If it cannot be found in Azure, everyone signs in again after the next deploy."
  $answer = Read-Host "Type YES to rotate the key-ring certificate"
  if ($answer -ne 'YES') { Write-Host 'Cancelled. Nothing changed.'; exit 1 }

  $live = $null
  $fresh = $null
  try {
    if ($secretNames -contains $CertSecretName) {
      $live = Get-LiveCertificateBase64
      if ($null -ne $live) {
        Set-GitHubSecret -Name $PreviousCertSecretName -Value $live
        Write-Host "The live certificate was stored as $PreviousCertSecretName."
      }
      else {
        Write-Host "No live certificate was found in Azure (the API has not been deployed with one). $PreviousCertSecretName was not changed."
      }
    }
    $fresh = New-KeyRingCertificateBase64
    Set-GitHubSecret -Name $CertSecretName -Value $fresh
  }
  finally {
    $live = $null
    $fresh = $null
  }
  Write-Host "A new certificate was stored as $CertSecretName (valid 10 years). It was never shown or written to disk."
  Write-Host ""
  Write-Host "DONE. Next: GitHub, Actions, 'Deploy Azure dev (POC)', Run workflow, approve it. The new certificate is used from that run on."
  exit 0
}

Write-Host "Action                : store the Google sign-in client id and secret" $(if ($secretNames -contains $CertSecretName) { "(the key-ring certificate exists and is left alone)" } else { "and create the key-ring certificate (first time)" })
Write-Host ""
Write-Host "STEP 1. In a browser open the Google Cloud console and choose the project tether-oauth-dev at the top."
Write-Host "        Open: Google Auth Platform, Clients, the client named Tether."
Write-Host "        Under 'Authorized redirect URIs' press 'Add URI' and paste exactly this address, then Save:"
Write-Host ""
Write-Host "          $redirectAddress"
Write-Host ""
Write-Host "        Keep the localhost address that is already there. No trailing slash. Google can take a few minutes to apply it."
Write-Host "STEP 2. Still in the console: Audience, Test users, Add users. Add each Google account you will let in (at most 100)."
Write-Host "        The page must still say 'Testing'. Do NOT press 'Publish app'."
Write-Host ""
[void](Read-Host "Press Enter when both steps are done (or Ctrl+C to stop; nothing has been changed yet)")

Write-Host ""
Write-Host "STEP 3. On the Tether client page, copy the 'Client ID' (it ends in .apps.googleusercontent.com)."
$clientId = ("$(Read-Host 'Client ID')").Trim()
if ($clientId -cnotmatch $ClientIdPattern) {
  throw 'That does not look like a Google client id (digits, a hyphen, lower-case letters and digits, then .apps.googleusercontent.com). Nothing was changed.'
}
Write-Host "STEP 4. The client secret. The console no longer shows an old secret: if you do not have it, press 'Add secret' on the"
Write-Host "        client page and copy the new one (later, delete the old secret there). Typing or pasting below is hidden."
$clientSecret = $null
try {
  $clientSecret = Read-HiddenValue -Prompt 'Client secret (hidden)'
  if ([string]::IsNullOrEmpty($clientSecret) -or $clientSecret -cnotmatch '^[!-~]{16,256}$') {
    throw 'The client secret is empty, too short, or has a space or line break in it (a paste slip). Nothing was changed.'
  }

  Write-Host ""
  $answer = Read-Host "Type YES to store the client id and secret on the GitHub environment '$GitHubEnvironment'"
  if ($answer -ne 'YES') { Write-Host 'Cancelled. Nothing changed.'; exit 1 }

  # The id first, then the secret: if the second write fails, GitHub holds the new id with the OLD secret, Google
  # refuses that pair, and running this script again repairs it. (The other order would look right and be wrong.)
  Set-GitHubSecret -Name $ClientIdSecretName -Value $clientId
  Write-Host "Client id stored as $ClientIdSecretName."
  Set-GitHubSecret -Name $ClientSecretSecretName -Value $clientSecret
}
finally {
  $clientSecret = $null
}
Write-Host "Client secret stored as $ClientSecretSecretName. It was never shown or written to disk."

# Key-ring certificate (ADR-022 C1): created ONLY when the secret does not exist. Checked by name, never by value.
if ($secretNames -contains $CertSecretName) {
  Write-Host "$CertSecretName already exists: left as it is (replacing it would sign everyone out; use -RotateKeyRingCertificate on purpose)."
}
else {
  $fresh = $null
  try {
    $fresh = New-KeyRingCertificateBase64
    Set-GitHubSecret -Name $CertSecretName -Value $fresh
  }
  finally {
    $fresh = $null
  }
  Write-Host "Key-ring certificate created and stored as $CertSecretName (valid 10 years). It was never shown or written to disk."
}

Write-Host ""
Write-Host "DONE."
Write-Host "Next:"
Write-Host "  1. The invite list:  powershell -ExecutionPolicy Bypass -File .\scripts\azure\set-invites.ps1 -Add <a Google address>"
Write-Host "  2. GitHub, Actions, 'Deploy Azure dev (POC)', Run workflow with access = team, approve it (infra\README.md, Run A)."
