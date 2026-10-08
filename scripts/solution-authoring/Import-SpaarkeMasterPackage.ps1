<#
.SYNOPSIS
    Import the CI-published SpaarkeMaster into one of Spaarke's own environments (demo, ...) with the same rules as
    provisioning H6. WRITES to the target environment unless -WhatIf.

.DESCRIPTION
    T218f (owner 2026-10-08, #1401): every environment — customer stamps and Spaarke's own — imports only the
    canonical SpaarkeMaster built by CI from git (publish-dataverse-solutions-manifest.yml), never a solution the dev
    team creates or manages by hand. Customer environments get it through provisioning H6; this script is the same
    path for Spaarke's own environments (Deploy-Release.ps1 Phase 3 calls it). It replaces Deploy-DataverseSolutions.ps1
    and its 9-solution list.

    1. Reads the published manifest (dataverse-solutions-latest.json in the provisioning-artifacts store) — the
       version and blob names H6 would use.
    2. Reads the SpaarkeMaster installed in the target environment.
    3. Plans with Resolve-PackageImportPlan (SpaarkePackageScope.psm1) — H6's rules: never switch managed/unmanaged,
       never downgrade, an equal version is already done, an older managed package is staged and upgraded, an older
       unmanaged one is updated by a plain import.
    4. Downloads the zip of the requested type, imports it with the pac CLI (async), then reads the environment back
       and fails unless it holds exactly that version and type.

    Identity: the operator's own az and pac sign-in (NFR-11) — no service principal, no secret. The storage read uses
    `--auth-mode login` (Storage Blob Data Reader on the store); pac must be signed in with access to the target.

.PARAMETER EnvironmentUrl
    Target Dataverse environment URL. Never the authoring environment (spaarkedev1): SpaarkeMaster is authored there.

.PARAMETER PackageType
    managed | unmanaged — the environment's `solutionPackageType` in config/environments.json (ADR-027 §3: managed by
    default, unmanaged on explicit instruction).

.PARAMETER WhatIf
    Read the manifest and the environment, print the plan and the pac command; import nothing.

.EXAMPLE
    ./scripts/solution-authoring/Import-SpaarkeMasterPackage.ps1 -EnvironmentUrl https://spaarke-demo.crm.dynamics.com -PackageType unmanaged -WhatIf
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory)][string]$EnvironmentUrl,
    [Parameter(Mandatory)][ValidateSet('managed', 'unmanaged', IgnoreCase = $false)][string]$PackageType,
    [string]$StorageAccount = 'sprkcpartifactsdev',
    [string]$Container = 'provisioning-artifacts',
    [string]$ManifestBlobName = 'dataverse-solutions-latest.json',
    [string]$AuthoringEnvironmentUrl = 'https://spaarkedev1.crm.dynamics.com',
    [int]$MaxAsyncWaitMinutes = 60
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SpaarkePackageScope.psm1') -Force
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$managed = $PackageType -eq 'managed'

if ($EnvironmentUrl -ieq $AuthoringEnvironmentUrl.TrimEnd('/')) {
    throw "$EnvironmentUrl is the authoring environment — SpaarkeMaster is authored there and is never imported into it."
}

$work = Join-Path ([IO.Path]::GetTempPath()) "spaarkemaster-import-$(Get-Date -Format yyyyMMddHHmmss)"
New-Item -ItemType Directory -Path $work -Force -WhatIf:$false | Out-Null   # local scratch: needed for the read-only plan too

function Get-StoreBlob([string]$Name, [string]$File) {
    az storage blob download --account-name $StorageAccount --container-name $Container --name $Name --file $File `
        --auth-mode login --overwrite true --only-show-errors --no-progress | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $File)) { throw "Could not download '$Name' from $StorageAccount/$Container (Storage Blob Data Reader needed)." }
}

# 1. The published package.
Write-Host "==> Reading $ManifestBlobName from $StorageAccount/$Container" -ForegroundColor Cyan
$manifestFile = Join-Path $work 'manifest.json'
Get-StoreBlob $ManifestBlobName $manifestFile
$entry = (Get-Content $manifestFile -Raw | ConvertFrom-Json).solutions.SpaarkeMaster
if (-not $entry -or -not $entry.version) {
    throw "$ManifestBlobName has no solutions.SpaarkeMaster entry — the store holds a pre-T218 manifest. Publish with publish-dataverse-solutions-manifest.yml first."
}
$blobName = if ($managed) { $entry.managedBlobName } else { $entry.unmanagedBlobName }
if (-not $blobName) { throw "$ManifestBlobName names no $PackageType zip for SpaarkeMaster $($entry.version) (no managedBlobName/unmanagedBlobName — a pre-T218 manifest). Publish with publish-dataverse-solutions-manifest.yml first." }
Write-Host "    Published: SpaarkeMaster $($entry.version) — $blobName"

# 2. The environment.
Write-Host "==> Reading the installed SpaarkeMaster in $EnvironmentUrl" -ForegroundColor Cyan
$token = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
if ($LASTEXITCODE -ne 0 -or -not $token) { throw "No Dataverse token for $EnvironmentUrl. Run 'az login' first." }
$headers = @{ Authorization = "Bearer $token"; Accept = 'application/json'; 'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0' }
$get = { param($endpoint) Invoke-RestMethod -Uri "$EnvironmentUrl/api/data/v9.2/$endpoint" -Headers $headers -Method Get }.GetNewClosure()
$installed = Get-InstalledPackage -Get $get
Write-Host ("    Installed: " + $(if ($installed) { "SpaarkeMaster $($installed.Version) ($(if ($installed.Managed) { 'managed' } else { 'unmanaged' }))" } else { 'none' }))

# 3. The plan.
$plan = Resolve-PackageImportPlan -Installed $installed -PackageVersion $entry.version -Managed $managed
Write-Host "==> $($plan.Message)" -ForegroundColor $(if ($plan.Action -eq 'Refuse') { 'Red' } else { 'Green' })
if ($plan.Action -eq 'Refuse') { throw "Import refused ($($plan.Refusal)): $($plan.Message)" }
if ($plan.Action -eq 'AlreadyCurrent') { exit 0 }

# 4. Import.
$zip = Join-Path $work $blobName
$pacArgs = @('solution', 'import', '--environment', $EnvironmentUrl, '--path', $zip, '--async', '--max-async-wait-time', "$MaxAsyncWaitMinutes")
if ($plan.StageAndUpgrade) { $pacArgs += '--stage-and-upgrade' }
if (-not $managed) { $pacArgs += '--publish-changes' }   # unmanaged changes take effect only once published

if (-not $PSCmdlet.ShouldProcess($EnvironmentUrl, "$($plan.Action) SpaarkeMaster $($entry.version) ($PackageType)")) {
    Write-Host "    WOULD RUN: pac $($pacArgs -join ' ')" -ForegroundColor DarkYellow
    exit 0
}

Get-StoreBlob $blobName $zip
$packed = Get-PackedSolutionInfo -ZipPath $zip
if ($packed.UniqueName -ne 'SpaarkeMaster' -or $packed.Version -ne $entry.version -or $packed.Managed -ne $managed) {
    throw "$blobName holds $($packed.UniqueName) $($packed.Version) (managed=$($packed.Managed)); the manifest promised SpaarkeMaster $($entry.version) ($PackageType)."
}

Write-Host "==> pac $($pacArgs -join ' ')" -ForegroundColor Cyan
& pac @pacArgs
if ($LASTEXITCODE -ne 0) { throw "pac solution import failed (exit $LASTEXITCODE)." }

# 5. Verify what the environment now holds.
$after = Get-InstalledPackage -Get $get
if (-not $after -or (Compare-PackageVersion -A $after.Version -B $entry.version) -ne 0 -or $after.Managed -ne $managed) {
    $what = if ($after) { "$($after.Version) (managed=$($after.Managed))" } else { 'nothing' }
    throw "After the import the environment holds SpaarkeMaster $what; expected $($entry.version) ($PackageType)."
}
Write-Host "==> SpaarkeMaster $($entry.version) ($PackageType) installed in $EnvironmentUrl." -ForegroundColor Green
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue -WhatIf:$false
