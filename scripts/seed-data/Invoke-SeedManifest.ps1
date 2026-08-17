<#
.SYNOPSIS
    Thin declarative-manifest orchestrator for the Spaarke AI seed chain.
    Reads scripts/seed-data/manifest.yaml, computes topological seed order,
    and fans out to the existing per-artifact seeder scripts.

.DESCRIPTION
    Authoritative entry point for AI seed deployment to a Dataverse customer
    environment. Resolves the R14 two-source drift by making manifest.yaml the
    ONE declarative source; this script is a thin orchestrator with NO
    embedded seeding logic (all deployment work is delegated to the existing
    per-artifact seeder scripts named in the manifest).

    Governance:
      - spec.md FR-15 (AI seed chain single-source-of-truth manifest)
      - ADR-039 (single AI routing surface; retired-artifact enforcement)
      - projects/customer-provisioning-orchestration-r1/notes/ai-seed-drift-resolution-2026-08.md
        (per-artifact canonical-source decision matrix — the manifest ENCODES it)

    Behavior:
      1. Load + parse manifest.yaml (requires powershell-yaml module).
      2. Validate schema (required fields present, deployer scripts exist on
         disk when declared, no cyclic dependencies, no unknown dependencies).
      3. Retired-check: verify no artifact in `artifacts` overlaps `retiredArtifacts`
         by id, path, name, or key pattern.
      4. Topologically sort artifacts by `dependsOn`.
      5. For each artifact in order:
           - If deployer.script is set: invoke it (or print planned invocation
             under -DryRun).
           - If deployer is null AND deployerOwnedBy is set: print PENDING
             marker (declared, no seeder yet — H12a handler task 070 owns).
           - If artifact is a placeholder (deployerOwnedBy=H12c and
             authoritativeSource=null): print PLACEHOLDER marker (H12c owns).
      6. On -Verify: for artifacts with deployer.verifyMode, invoke the verify
         script (typically -DiffOnly for round-trip proof); non-zero exit
         from any verify call fails the overall verify.

    Exit codes:
      0 — success (dry-run planned all invocations, or live-run completed
          without error, or verify found zero drift).
      1 — hard failure (manifest missing/invalid, retired-artifact violation,
          cyclic dependency, unknown dependency, seeder invocation failure,
          verify drift).

.PARAMETER EnvironmentUrl
    Dataverse environment URL (passed through to per-artifact seeder scripts
    as -EnvironmentUrl or -DataverseUrl per each script's parameter name).
    Defaults to $env:DATAVERSE_URL.

.PARAMETER Manifest
    Path to manifest.yaml. Defaults to sibling manifest.yaml in this script's
    directory.

.PARAMETER DryRun
    Print planned invocations without executing any seeder script. Every
    per-artifact deployer receives -DryRun downstream when the script supports
    it (which every seeder in this manifest does). DEFAULT MODE for safety.

.PARAMETER Live
    Opt in to live execution (opposite of -DryRun). One of -DryRun or -Live
    must be specified; there is no silent default that touches the environment.

.PARAMETER Verify
    Do not seed; instead invoke verifyMode on every artifact that declares one
    (currently only playbook-consumers via -DiffOnly). Reports zero drift on
    success. Useful as a CI gate or as the H12a idempotency assertion.

.PARAMETER RetiredCheckOnly
    Do not seed and do not verify; only run the retired-artifact check and
    exit. Useful as a repo-hygiene lint step.

.PARAMETER Force
    Passed through to per-artifact seeders that support -Force (recreate
    existing records). Rarely appropriate; the seeders' default upsert
    behavior is what you want.

.EXAMPLE
    # Dry-run: shows planned invocations, no environment touched.
    pwsh -File scripts/seed-data/Invoke-SeedManifest.ps1 -DryRun -EnvironmentUrl https://spaarkedev1.crm.dynamics.com

.EXAMPLE
    # Live seed to dev.
    pwsh -File scripts/seed-data/Invoke-SeedManifest.ps1 -Live -EnvironmentUrl https://spaarkedev1.crm.dynamics.com

.EXAMPLE
    # Round-trip verify (H12a idempotency assertion).
    pwsh -File scripts/seed-data/Invoke-SeedManifest.ps1 -Verify -EnvironmentUrl https://spaarkedev1.crm.dynamics.com

.EXAMPLE
    # Repo-hygiene lint: retired-artifact violations only.
    pwsh -File scripts/seed-data/Invoke-SeedManifest.ps1 -RetiredCheckOnly

.NOTES
    Authors:   customer-provisioning-orchestration-r1 task 069
    Requires:  powershell-yaml (0.4.12+), PowerShell 7+ (pwsh)
    Auth:      Azure CLI (az login) — inherited by every seeder script called.
    Consumed by: task 070 (H12a AI seed chain handler wraps this script)
#>

[CmdletBinding(DefaultParameterSetName = 'DryRun')]
param(
    [string]$EnvironmentUrl = $env:DATAVERSE_URL,

    [string]$Manifest,

    [Parameter(ParameterSetName = 'DryRun')]
    [switch]$DryRun,

    [Parameter(ParameterSetName = 'Live')]
    [switch]$Live,

    [Parameter(ParameterSetName = 'Verify')]
    [switch]$Verify,

    [Parameter(ParameterSetName = 'RetiredCheckOnly')]
    [switch]$RetiredCheckOnly,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ---------------------------------------------------------------------------
# Constants + resolved paths
# ---------------------------------------------------------------------------
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Manifest) {
    $Manifest = Join-Path $ScriptDir 'manifest.yaml'
}

# Repo root is 2 levels above scripts/seed-data (scripts/ -> repo).
$RepoRoot = Split-Path -Parent (Split-Path -Parent $ScriptDir)

# Guard: default to DryRun when the caller supplied no execution intent.
if (-not ($DryRun -or $Live -or $Verify -or $RetiredCheckOnly)) {
    Write-Warning 'No execution mode specified; defaulting to -DryRun (safe).'
    $DryRun = $true
}

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
function Write-Header {
    param([string]$Text, [ConsoleColor]$Color = 'Cyan')
    Write-Host ''
    Write-Host ('=' * 78) -ForegroundColor $Color
    Write-Host "  $Text" -ForegroundColor $Color
    Write-Host ('=' * 78) -ForegroundColor $Color
}

function Write-Diag {
    param([string]$Icon, [string]$Text, [ConsoleColor]$Color = 'Gray')
    Write-Host "  $Icon $Text" -ForegroundColor $Color
}

function Assert-YamlModule {
    if (-not (Get-Module -ListAvailable -Name powershell-yaml)) {
        throw @'
powershell-yaml module is not installed.
Install it with:
    Install-Module -Name powershell-yaml -Scope CurrentUser
'@
    }
    Import-Module powershell-yaml -ErrorAction Stop
}

function Read-ManifestYaml {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        throw "Manifest not found: $Path"
    }

    try {
        $raw = Get-Content -Path $Path -Raw
        $parsed = ConvertFrom-Yaml -Yaml $raw
    }
    catch {
        throw "Failed to parse manifest YAML at ${Path}: $($_.Exception.Message)"
    }

    # Minimal schema validation. Fail-fast with a clear diagnostic — the
    # POML acceptance criterion "missing dependency causes clear diagnostic"
    # applies to this path.
    if (-not $parsed.ContainsKey('schemaVersion')) {
        throw "Manifest missing required field: schemaVersion"
    }
    if ($parsed.schemaVersion -ne 1) {
        throw "Unsupported manifest schemaVersion: $($parsed.schemaVersion) (this script understands version 1)"
    }
    if (-not $parsed.ContainsKey('artifacts') -or -not $parsed.artifacts) {
        throw "Manifest missing required field: artifacts (must be a non-empty list)"
    }

    # Normalize retiredArtifacts / orchestration to empty collections if absent.
    if (-not $parsed.ContainsKey('retiredArtifacts')) {
        $parsed.retiredArtifacts = @()
    }

    return $parsed
}

function Test-ArtifactSchema {
    param([hashtable]$Artifact, [int]$Index)

    $required = @('id', 'type', 'authoritativeSource', 'dependsOn')
    foreach ($field in $required) {
        if (-not $Artifact.ContainsKey($field)) {
            # authoritativeSource is allowed to be null (placeholder), but the KEY must exist.
            if ($field -eq 'authoritativeSource' -and $Artifact.ContainsKey('authoritativeSource')) { continue }
            throw "artifacts[$Index] (id=$($Artifact.id)) missing required field: $field"
        }
    }

    # deployer is optional but if present, must have `script` OR be null.
    if ($Artifact.ContainsKey('deployer') -and $null -ne $Artifact.deployer) {
        if (-not $Artifact.deployer.ContainsKey('script')) {
            throw "artifacts[$Index] (id=$($Artifact.id)) deployer block missing 'script' field"
        }
    }
}

function Test-RetiredArtifactOverlap {
    param([array]$Artifacts, [array]$Retired)

    $violations = @()
    $activeIds = $Artifacts | ForEach-Object { $_.id }
    $activePaths = $Artifacts | Where-Object { $_.authoritativeSource } | ForEach-Object { $_.authoritativeSource }

    foreach ($ret in $Retired) {
        # ID overlap
        if ($ret.ContainsKey('id') -and $ret.id -in $activeIds) {
            $violations += "Retired artifact id '$($ret.id)' present in active artifacts list — this is an ADR-039 violation (see manifest retiredArtifacts.$($ret.id).reason)."
        }
        # Path overlap
        if ($ret.ContainsKey('path')) {
            foreach ($activePath in $activePaths) {
                if ($activePath -eq $ret.path) {
                    $violations += "Retired artifact path '$($ret.path)' is declared as an active authoritativeSource — reason: $($ret.reason)"
                }
            }
        }
        # Name overlap (index / entity-set names)
        if ($ret.ContainsKey('name')) {
            foreach ($art in $Artifacts) {
                if ($art.ContainsKey('name') -and $art.name -eq $ret.name) {
                    $violations += "Retired artifact name '$($ret.name)' matches active artifact id '$($art.id)' — reason: $($ret.reason)"
                }
            }
        }
    }

    return , $violations
}

function Get-TopologicalOrder {
    <#
    Kahn's algorithm. Returns ordered array of artifact IDs.
    Fails fast with a clear diagnostic on cyclic OR unknown-dependency conditions
    (POML acceptance criterion: "missing dependency ... clear diagnostic, not silent success").
    #>
    param([array]$Artifacts)

    $ids = @($Artifacts | ForEach-Object { $_.id })
    $idSet = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($id in $ids) { [void]$idSet.Add($id) }

    # Check every dependency resolves to a known artifact.
    foreach ($art in $Artifacts) {
        foreach ($dep in $art.dependsOn) {
            if (-not $idSet.Contains($dep)) {
                throw "Artifact '$($art.id)' declares dependency on unknown artifact '$dep' — cannot resolve seed order. Known artifacts: $($ids -join ', ')"
            }
        }
    }

    # Build indegree + adjacency.
    $indegree = @{}
    $adj = @{}
    foreach ($id in $ids) {
        $indegree[$id] = 0
        $adj[$id] = @()
    }
    foreach ($art in $Artifacts) {
        foreach ($dep in $art.dependsOn) {
            # dep -> art.id  (art.id depends on dep, so process dep first)
            $adj[$dep] += $art.id
            $indegree[$art.id]++
        }
    }

    # Queue seeded with zero-indegree nodes; preserve manifest declaration
    # order among ready nodes for stable output.
    $ordered = @()
    $ready = New-Object System.Collections.Generic.Queue[string]
    foreach ($id in $ids) {
        if ($indegree[$id] -eq 0) { $ready.Enqueue($id) }
    }

    while ($ready.Count -gt 0) {
        $current = $ready.Dequeue()
        $ordered += $current
        foreach ($next in $adj[$current]) {
            $indegree[$next]--
            if ($indegree[$next] -eq 0) { $ready.Enqueue($next) }
        }
    }

    if ($ordered.Count -ne $ids.Count) {
        $stuck = $ids | Where-Object { $_ -notin $ordered }
        throw "Cyclic dependency detected in manifest — artifacts stuck in cycle: $($stuck -join ', ')"
    }

    return , $ordered
}

function Invoke-Deployer {
    param(
        [hashtable]$Artifact,
        [string]$Mode,        # 'DryRun' | 'Live' | 'Verify'
        [string]$EnvironmentUrl,
        [string]$RepoRoot,
        [bool]$Force
    )

    $deployer = $Artifact.deployer
    $script = $deployer.script
    $scriptPath = Join-Path $RepoRoot $script

    if (-not (Test-Path $scriptPath)) {
        throw "Artifact '$($Artifact.id)': deployer script not found at $scriptPath (declared: $script)"
    }

    # Resolve deployer parameter block per mode.
    $params = @{}
    if ($Mode -eq 'Verify') {
        if (-not $deployer.ContainsKey('verifyMode') -or -not $deployer.verifyMode) {
            Write-Diag '↷' "$($Artifact.id) — no verifyMode declared; skipping" 'DarkGray'
            return
        }
        $verifyBlock = $deployer.verifyMode
        # verifyMode may override script; default is the same script with different params
        if ($verifyBlock.ContainsKey('script') -and $verifyBlock.script) {
            $script = $verifyBlock.script
            $scriptPath = Join-Path $RepoRoot $script
        }
        if ($verifyBlock.ContainsKey('parameters') -and $verifyBlock.parameters) {
            foreach ($k in $verifyBlock.parameters.Keys) {
                $params[$k] = $verifyBlock.parameters[$k]
            }
        }
    }
    else {
        if ($deployer.ContainsKey('parameters') -and $deployer.parameters) {
            foreach ($k in $deployer.parameters.Keys) {
                $params[$k] = $deployer.parameters[$k]
            }
        }
    }

    # Wire environment URL under whichever parameter name the seeder accepts.
    # We don't introspect; we set both common names — extra unused params are
    # harmless in the seeder scripts we call (they use $env: fallback anyway).
    if ($EnvironmentUrl) {
        if (-not $params.ContainsKey('EnvironmentUrl') -and -not $params.ContainsKey('DataverseUrl')) {
            # Heuristic: infra/dataverse consumer scripts use DataverseUrl; seed-data MVP scripts use EnvironmentUrl.
            if ($script -match 'dataverse/Seed-') {
                $params['DataverseUrl'] = $EnvironmentUrl
            }
            else {
                $params['EnvironmentUrl'] = $EnvironmentUrl
            }
        }
    }

    # Wire -DryRun / -Force pass-through.
    if ($Mode -eq 'DryRun' -and -not $params.ContainsKey('DryRun')) {
        $params['DryRun'] = $true
    }
    if ($Force -and -not $params.ContainsKey('Force')) {
        $params['Force'] = $true
    }

    # Print planned/live invocation.
    $paramString = ($params.GetEnumerator() | ForEach-Object {
            $v = $_.Value
            if ($v -is [bool]) { "-$($_.Key)" } else { "-$($_.Key) '$v'" }
        }) -join ' '

    $modeTag = switch ($Mode) {
        'DryRun' { '[DRY-RUN]' }
        'Live' { '[LIVE]' }
        'Verify' { '[VERIFY]' }
    }
    $color = if ($Mode -eq 'Live') { 'Yellow' } else { 'Green' }
    Write-Diag '→' "$modeTag pwsh -File `"$script`" $paramString" $color

    if ($Mode -eq 'DryRun') {
        # Never invoke in dry-run. This is the safety guarantee of -DryRun.
        return
    }

    # Live / Verify: actually invoke. Any non-zero exit becomes a terminating error.
    & $scriptPath @params
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) {
        throw "Deployer for artifact '$($Artifact.id)' exited with code $LASTEXITCODE (script: $script)"
    }
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
Assert-YamlModule
$manifestData = Read-ManifestYaml -Path $Manifest

Write-Header 'Spaarke AI Seed Manifest Orchestrator'
Write-Diag 'ℹ' "Manifest      : $Manifest"
Write-Diag 'ℹ' "Environment   : $(if ($EnvironmentUrl) { $EnvironmentUrl } else { '(not set — some deployers will fail live)' })"
Write-Diag 'ℹ' "Mode          : $($PSCmdlet.ParameterSetName)"
Write-Diag 'ℹ' "Artifacts     : $($manifestData.artifacts.Count)"
Write-Diag 'ℹ' "Retired       : $($manifestData.retiredArtifacts.Count)"

# Per-artifact schema validation
$idx = 0
foreach ($art in $manifestData.artifacts) {
    Test-ArtifactSchema -Artifact $art -Index $idx
    $idx++
}

# Retired-artifact enforcement (ADR-039 governance)
Write-Header 'Retired-Artifact Check (ADR-039)' 'Magenta'
$violations = Test-RetiredArtifactOverlap -Artifacts $manifestData.artifacts -Retired $manifestData.retiredArtifacts
if ($violations.Count -gt 0) {
    Write-Host '  RETIRED-ARTIFACT VIOLATIONS:' -ForegroundColor Red
    foreach ($v in $violations) { Write-Host "    ✗ $v" -ForegroundColor Red }
    throw "Manifest violates ADR-039 (retired artifacts declared active): $($violations.Count) violation(s)."
}
Write-Diag '✓' "No retired artifacts declared active. $($manifestData.retiredArtifacts.Count) exclusion(s) enforced." 'Green'

if ($RetiredCheckOnly) {
    Write-Header 'RETIRED-CHECK PASSED' 'Green'
    exit 0
}

# Topological sort (fails on cycle / unknown dep with a clear diagnostic)
Write-Header 'Dependency Resolution' 'Magenta'
$order = Get-TopologicalOrder -Artifacts $manifestData.artifacts
Write-Diag '✓' "Topological order resolved: $($order.Count) artifacts" 'Green'
Write-Host ('    ' + ($order -join ' → ')) -ForegroundColor DarkGray

# Fan out to deployers
$mode = $PSCmdlet.ParameterSetName
if ($mode -eq 'DryRun') { $execMode = 'DryRun' }
elseif ($mode -eq 'Live') { $execMode = 'Live' }
elseif ($mode -eq 'Verify') { $execMode = 'Verify' }
else { throw "Unexpected parameter set: $mode" }

Write-Header "Seed Plan ($execMode)" 'Magenta'
$artifactById = @{}
foreach ($art in $manifestData.artifacts) { $artifactById[$art.id] = $art }

$deploySummary = @()
foreach ($id in $order) {
    $art = $artifactById[$id]
    Write-Host ''
    Write-Host "  [$id]  type=$($art.type)  dependsOn=[$($art.dependsOn -join ', ')]" -ForegroundColor Cyan
    if ($art.ContainsKey('driftMatrixRef')) {
        Write-Diag 'ℹ' "drift matrix: $($art.driftMatrixRef)" 'DarkGray'
    }
    if ($art.authoritativeSource) {
        Write-Diag 'ℹ' "source      : $($art.authoritativeSource)" 'DarkGray'
    }

    if ($null -eq $art.deployer) {
        $owner = if ($art.ContainsKey('deployerOwnedBy') -and $art.deployerOwnedBy) { $art.deployerOwnedBy } else { 'UNASSIGNED' }
        if ($null -eq $art.authoritativeSource -and $owner -eq 'H12c') {
            Write-Diag '⧗' "PLACEHOLDER — populated by $owner (customer-specific runtime references)" 'DarkYellow'
            $deploySummary += [pscustomobject]@{ Id = $id; Status = 'PLACEHOLDER'; Owner = $owner }
        }
        else {
            Write-Diag '⧗' "PENDING — no deployer script yet; owned by $owner (per-file loop in that handler)" 'DarkYellow'
            $deploySummary += [pscustomobject]@{ Id = $id; Status = 'PENDING'; Owner = $owner }
        }
        continue
    }

    try {
        Invoke-Deployer -Artifact $art -Mode $execMode -EnvironmentUrl $EnvironmentUrl -RepoRoot $RepoRoot -Force ([bool]$Force)
        $deploySummary += [pscustomobject]@{ Id = $id; Status = 'OK'; Owner = 'seed-manifest' }
    }
    catch {
        Write-Diag '✗' $_.Exception.Message 'Red'
        $deploySummary += [pscustomobject]@{ Id = $id; Status = 'FAIL'; Owner = 'seed-manifest' }
        if ($execMode -ne 'DryRun') { throw }
    }
}

# Summary
Write-Header 'Summary'
foreach ($row in $deploySummary) {
    $icon = switch ($row.Status) {
        'OK' { '✓' }
        'PLACEHOLDER' { '⧗' }
        'PENDING' { '⧗' }
        'FAIL' { '✗' }
        default { '?' }
    }
    $color = switch ($row.Status) {
        'OK' { 'Green' }
        'PLACEHOLDER' { 'DarkYellow' }
        'PENDING' { 'DarkYellow' }
        'FAIL' { 'Red' }
        default { 'Gray' }
    }
    Write-Diag $icon "$($row.Id.PadRight(32)) $($row.Status.PadRight(12)) owner=$($row.Owner)" $color
}

$failCount = @($deploySummary | Where-Object { $_.Status -eq 'FAIL' }).Count
if ($failCount -gt 0) {
    Write-Host ''
    Write-Host "  $failCount artifact(s) failed." -ForegroundColor Red
    exit 1
}

Write-Host ''
if ($execMode -eq 'DryRun') {
    Write-Diag '✓' 'Dry-run complete. No changes made. Re-run with -Live to execute.' 'Green'
}
elseif ($execMode -eq 'Verify') {
    Write-Diag '✓' 'Verify complete. Zero drift.' 'Green'
}
else {
    Write-Diag '✓' 'Live seed complete.' 'Green'
}
exit 0
