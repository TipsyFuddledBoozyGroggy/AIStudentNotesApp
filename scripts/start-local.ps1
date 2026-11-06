# One-command local start (Windows PowerShell). Thin wrapper: all logic is in start-local.mjs so it behaves the same in Git Bash.
#   .\scripts\start-local.ps1            start or reuse, seed demo data, print the URL
#   .\scripts\start-local.ps1 stop       stop the web server this script started and the compose containers
#   .\scripts\start-local.ps1 status
#   .\scripts\start-local.ps1 --no-seed
$ErrorActionPreference = "Stop"
if (-not (Get-Command node -ErrorAction SilentlyContinue)) { Write-Error "Node.js 22 or newer is required (https://nodejs.org)."; exit 1 }
& node (Join-Path $PSScriptRoot "start-local.mjs") @args
exit $LASTEXITCODE
