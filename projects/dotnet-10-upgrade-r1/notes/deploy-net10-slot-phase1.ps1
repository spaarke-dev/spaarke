<#
.SYNOPSIS
    Task 051 Phase 1 — deploy the net10 BFF to an ISOLATED dev staging slot. NO SWAP.
    The main dev slot stays net8, so the other BFF worktrees are unaffected.

.DESCRIPTION
    Assumes Steps 1-3 of 051-operator-runbook.md are done (slot created,
    user-assigned MI attached, slot runtime set to DOTNETCORE|10.0). This script
    does Steps 4-5: publish net10 -> zip -> deploy to the slot -> health-check the
    slot hostname. It does NOT swap. Run from the worktree root.

    To run Steps 1-3 automatically too, pass -Provision.

.EXAMPLE
    pwsh -File projects/dotnet-10-upgrade-r1/notes/deploy-net10-slot-phase1.ps1 -Provision
    # First run: creates slot + attaches MI + sets net10 runtime, then deploys + smokes.

.EXAMPLE
    pwsh -File projects/dotnet-10-upgrade-r1/notes/deploy-net10-slot-phase1.ps1
    # Slot already provisioned: just re-publish + re-deploy + health-check.
#>
[CmdletBinding()]
param(
    [string]$ResourceGroup = "rg-spaarke-dev",
    [string]$AppName       = "spaarke-bff-dev",
    [string]$SlotName      = "staging",
    [string]$UamiResourceId = "/subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourcegroups/spe-infrastructure-westus2/providers/Microsoft.ManagedIdentity/userAssignedIdentities/mi-bff-api-dev",
    [switch]$Provision,
    [int]$MaxHealthRetries = 24,   # 24 x 5s = 120s Linux cold-start tolerance
    [switch]$SkipBuild
)
$ErrorActionPreference = "Stop"
$slotUrl = "https://$AppName-$SlotName.azurewebsites.net"

Write-Host "=== Task 051 Phase 1 — net10 -> isolated slot '$SlotName' (NO SWAP) ===" -ForegroundColor Cyan
Write-Host "  App: $AppName / $ResourceGroup   Slot URL: $slotUrl" -ForegroundColor Gray

if ($Provision) {
    Write-Host "[provision] creating slot (clone config), attaching MI, setting net10 runtime..." -ForegroundColor Yellow
    az webapp deployment slot create -g $ResourceGroup -n $AppName --slot $SlotName --configuration-source $AppName | Out-Null
    az webapp identity assign  -g $ResourceGroup -n $AppName --slot $SlotName --identities $UamiResourceId | Out-Null
    # NOTE: the '|' in DOTNETCORE|10.0 is re-parsed as a pipe by cmd.exe when PowerShell calls az.cmd.
    # The escaped-quote wrapper '"..."' makes cmd.exe treat it as one literal token. Do NOT use a plain "..." here.
    az webapp config set       -g $ResourceGroup -n $AppName --slot $SlotName --linux-fx-version '"DOTNETCORE|10.0"' | Out-Null

    $mainRt = az webapp config show -g $ResourceGroup -n $AppName --query linuxFxVersion -o tsv
    $slotRt = az webapp config show -g $ResourceGroup -n $AppName --slot $SlotName --query linuxFxVersion -o tsv
    Write-Host "  main runtime: $mainRt   (MUST be DOTNETCORE|8.0 — unchanged)" -ForegroundColor Gray
    Write-Host "  slot runtime: $slotRt   (MUST be DOTNETCORE|10.0)" -ForegroundColor Gray
    if ($mainRt -ne "DOTNETCORE|8.0") { throw "SAFETY STOP: main dev runtime is '$mainRt', expected DOTNETCORE|8.0. Aborting — will not risk the shared slot." }
    if ($slotRt -ne "DOTNETCORE|10.0") { throw "slot runtime is '$slotRt', expected DOTNETCORE|10.0." }
}

if (-not $SkipBuild) {
    Write-Host "[1/3] publishing net10 (framework-dependent linux-x64)..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force deploy/api-publish -ErrorAction SilentlyContinue
    dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
}

Write-Host "[2/3] zipping publish output..." -ForegroundColor Yellow
$pub = Resolve-Path deploy/api-publish
$zip = Join-Path (Resolve-Path deploy) "bff-net10-slot.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
$files = Get-ChildItem -LiteralPath $pub -Recurse -File
Compress-Archive -Path $files.FullName -DestinationPath $zip -CompressionLevel Optimal
$zipMB = [math]::Round((Get-Item -LiteralPath $zip).Length/1MB,2)
Write-Host "  zip: $zip  ($zipMB MB — expect ~45 MB; <30 MB = incomplete)" -ForegroundColor Gray
if ($zipMB -lt 30) { throw "zip is $zipMB MB (<30) — incomplete package, aborting." }

Write-Host "[3/3] deploying to slot '$SlotName' (synchronous, NO swap)..." -ForegroundColor Yellow
az webapp deploy -g $ResourceGroup -n $AppName --slot $SlotName --type zip --src-path $zip --async false
if ($LASTEXITCODE -ne 0) { throw "slot deploy failed" }

Write-Host "  health-checking $slotUrl/healthz (up to $($MaxHealthRetries*5)s Linux cold start)..." -ForegroundColor Gray
$ok = $false
for ($i = 1; $i -le $MaxHealthRetries; $i++) {
    try {
        $code = (Invoke-WebRequest -Uri "$slotUrl/healthz" -UseBasicParsing -TimeoutSec 10).StatusCode
        if ($code -eq 200) { $ok = $true; break }
    } catch { }
    Start-Sleep -Seconds 5
}

if ($ok) {
    Write-Host "`n✅ SLOT net10 healthy: $slotUrl/healthz = 200" -ForegroundColor Green
    Write-Host "   Next: run the §9 smoke (MI->Dataverse, EXO, OBO/Graph 6.5, telemetry) per 051-operator-runbook.md Step 5," -ForegroundColor Green
    Write-Host "   record GO/NO-GO in notes/051-smoke-result.md. Main dev is UNTOUCHED (still net8). NO swap performed." -ForegroundColor Green
} else {
    Write-Host "`n❌ slot /healthz did not reach 200 after $($MaxHealthRetries*5)s." -ForegroundColor Red
    Write-Host "   Inspect: az webapp log tail -g $ResourceGroup -n $AppName --slot $SlotName" -ForegroundColor Yellow
    Write-Host "   The slot is isolated — main dev is unaffected. Delete + retry: az webapp deployment slot delete -g $ResourceGroup -n $AppName --slot $SlotName" -ForegroundColor Yellow
    exit 1
}
