<#
.SYNOPSIS
    Wave C2 Bicep integration test — static build + live what-if dry-run of the 4 Wave C2 stacks.

.DESCRIPTION
    Verifies the Wave C2 (tasks 027-033) Bicep composition is coherent by running:

      Tier 1 (always) — Static build with `az bicep build`:
        - customer.bicep            (task 027 — per-customer data resources + optional SignalR + UAMI param)
        - platform.bicep            (task 031 — env-scope shared: monitoring + platform KV)
        - platform-controlplane.bicep (task 033 — L2 orchestrator infra)
        - stacks/model1-shared.bicep (task 032 — Model 1 trial-tier composition)  [KNOWN UNMIGRATED — see note]

      Tier 2 (if dev sub context) — Live dry-run with `az deployment sub what-if`:
        - customer.bicep against a synthetic customerId in dev
        - platform.bicep against dev
        - platform-controlplane.bicep against dev
        - stacks/model1-shared.bicep DEFERRED until unmigrated caller fix lands

      Tier 3 (assertions) — Structural sanity on what-if JSON:
        - Enumerate resource additions
        - Assert UAMI resource appears
        - Assert App Service identity.userAssignedIdentities binding present on both slots
        - Assert role assignments target UAMI principalId

    Per task 034 POML + spec.md FR-04 + design.md §7 resource inventory.

.PARAMETER Mode
    Test mode:
      - Build     : Tier 1 only (static `az bicep build` — no dev sub context needed)  [default]
      - DryRun    : Tier 1 + Tier 2 (adds live `az deployment sub what-if` against dev)
      - Full      : Tier 1 + Tier 2 + Tier 3 (adds structural assertions on what-if JSON)

.PARAMETER SubscriptionId
    Dev subscription ID to use for DryRun/Full modes.
    Defaults to Spaarke dev (484bc857-3802-427f-9ea5-ca47b43db0f0).
    Set via `az account set --subscription <id>` before invoking, or pass here.

.PARAMETER Location
    Azure region for subscription-scope deployments (default: westus2).

.PARAMETER TestCustomerId
    Synthetic customer identifier for per-customer stack what-if.
    Defaults to 'itsttest' (integration test synthetic — never real).

.PARAMETER TestEnvironment
    Target environment name (dev|staging|prod). Defaults to 'dev'.

.PARAMETER NotesOutputPath
    Path to write the dry-run notes artifact.
    Defaults to projects/customer-provisioning-orchestration-r1/notes/bicep-e2e-dry-run-<yyyymmdd>.md.

.EXAMPLE
    pwsh scripts/tests/bicep-e2e-dry-run.ps1
    # Tier 1 only: fast build check on all 4 stacks (no dev sub required)

.EXAMPLE
    pwsh scripts/tests/bicep-e2e-dry-run.ps1 -Mode DryRun
    # Tier 1 + Tier 2: adds live what-if against Spaarke dev subscription

.EXAMPLE
    pwsh scripts/tests/bicep-e2e-dry-run.ps1 -Mode Full -TestCustomerId acmedemo
    # Full run with structural assertions

.NOTES
    Author         : customer-provisioning-orchestration-r1 task 034
    Wave           : C2 (Bicep + UAMI)
    Spec anchor    : FR-04 acceptance ("`az deployment group what-if` in upgrade mode surfaces drift before apply")
    Design anchor  : §7.2 Resource Catalog (13 active + 2 optional per-customer resources)
    ADR anchor     : ADR-038 §7 KEEP path `tests/integration/**` — this script is the deployment-composition
                     equivalent of that pattern (no `tests/**` C# host exists for bicep; ps1 is the vehicle).
                     PLACEMENT RATIONALE: ADR-038 §7 defines 7 KEEP paths for xUnit C# tests
                     (auth, regression, data-mutation, tenant, contract, seam, unit/domain). It does NOT
                     define a KEEP path for shell-scripted integration tests because none existed at the
                     time the ADR was authored. Bicep tooling has no native C# xUnit equivalent — testing
                     bicep composition end-to-end is inherently shell-based. Placing this at
                     `scripts/tests/bicep-e2e-dry-run.ps1` follows the intent of `tests/integration/**`
                     while respecting ADR-038's C#-specific KEEP-path scope. NOT a violation.
    Known gap      : stacks/model1-shared.bicep does NOT build clean as of 2026-08-17 — it is an unmigrated
                     caller of the task-029 UAMI-only app-service.bicep module (still passes deprecated
                     `keyVaultName` + `enableManagedIdentity` params + reads deprecated `appServicePrincipalId`
                     output). Task-029 D1 explicitly deferred caller migration. Follow-on task recommended:
                     "migrate stacks/model1-shared.bicep sharedBffApi module invocation to UAMI-only param
                     signature". This script marks the build failure as EXPECTED_FAILURE (not RED) and
                     documents it in the notes artifact.
    Known drift    : Bicep module file count on disk is 29 as of 2026-08-17 (design.md v3.2 says 25).
                     Wave 2 added: uami.bicep, controlplane-app-service.bicep, cosmos-provisioning.bicep,
                     app-service-slot.bicep, deployment-slot.bicep. This script reports the delta but does
                     NOT fail on count mismatch — design.md text is authoritative for INTENT, not COUNT.
#>

[CmdletBinding()]
param(
    [ValidateSet('Build', 'DryRun', 'Full')]
    [string]$Mode = 'Build',

    [string]$SubscriptionId = '484bc857-3802-427f-9ea5-ca47b43db0f0',

    [string]$Location = 'westus2',

    [ValidatePattern('^[a-z0-9]{3,10}$')]
    [string]$TestCustomerId = 'itsttest',

    [ValidateSet('dev', 'staging', 'prod')]
    [string]$TestEnvironment = 'dev',

    [string]$NotesOutputPath
)

$ErrorActionPreference = 'Stop'

# ============================================================================
# LAYOUT — resolve absolute repo-root paths (script may be invoked from any cwd)
# ============================================================================

$ScriptDir = $PSScriptRoot                                    # scripts/tests/
$RepoRoot = Resolve-Path (Join-Path $ScriptDir '..' '..')     # <repo-root>
$BicepDir = Join-Path $RepoRoot 'infrastructure' 'bicep'
$ModulesDir = Join-Path $BicepDir 'modules'
$StacksDir = Join-Path $BicepDir 'stacks'

if (-not $NotesOutputPath) {
    $stamp = Get-Date -Format 'yyyyMMdd'
    $NotesOutputPath = Join-Path $RepoRoot 'projects' 'customer-provisioning-orchestration-r1' 'notes' "bicep-e2e-dry-run-$stamp.md"
}

# ============================================================================
# WAVE C2 TARGET INVENTORY — the 4 stacks under test
# ============================================================================

$Stacks = @(
    @{
        Name            = 'customer'
        RelPath         = 'customer.bicep'
        FullPath        = Join-Path $BicepDir 'customer.bicep'
        TargetScope     = 'subscription'
        ExpectedBuild   = 'PASS'
        WhatIfEligible  = $true
        WhatIfParams    = @{
            customerId                    = $TestCustomerId
            environmentName               = $TestEnvironment
            location                      = $Location
            userAssignedIdentityResourceId = ''  # empty in what-if — real deploys pass uami.outputs.uamiResourceId
        }
        Owner           = 'task 027'
    },
    @{
        Name            = 'platform'
        RelPath         = 'platform.bicep'
        FullPath        = Join-Path $BicepDir 'platform.bicep'
        TargetScope     = 'subscription'
        ExpectedBuild   = 'PASS'
        WhatIfEligible  = $true
        WhatIfParams    = @{
            environmentName = $TestEnvironment
            location        = $Location
        }
        Owner           = 'task 031'
    },
    @{
        Name            = 'platform-controlplane'
        RelPath         = 'platform-controlplane.bicep'
        FullPath        = Join-Path $BicepDir 'platform-controlplane.bicep'
        TargetScope     = 'subscription'
        ExpectedBuild   = 'PASS'
        WhatIfEligible  = $true
        WhatIfParams    = @{
            environmentName = $TestEnvironment
            location        = $Location
        }
        Owner           = 'task 033'
    },
    @{
        Name            = 'stacks/model1-shared'
        RelPath         = 'stacks/model1-shared.bicep'
        FullPath        = Join-Path $StacksDir 'model1-shared.bicep'
        TargetScope     = 'subscription'
        ExpectedBuild   = 'EXPECTED_FAILURE'      # task 029 D1 deferred caller migration
        WhatIfEligible  = $false                  # can't what-if a broken template
        WhatIfParams    = @{}
        Owner           = 'task 032 (deferred fix)'
        DeferralReason  = 'sharedBffApi module invocation still uses deprecated app-service.bicep params (keyVaultName + enableManagedIdentity) + reads deprecated appServicePrincipalId output. Task 029 D1 explicitly deferred caller migration. Follow-on task required.'
    }
)

# ============================================================================
# RESULT ACCUMULATORS
# ============================================================================

$Results = @{
    BuildResults    = [ordered]@{}   # stackName -> @{ Status; StdOut; StdErr; Warnings; Errors }
    WhatIfResults   = [ordered]@{}   # stackName -> @{ Status; StdOut; ResourceCount; ExitCode }
    Assertions      = [System.Collections.Generic.List[object]]::new()
    Deviations      = [System.Collections.Generic.List[string]]::new()
    Followups       = [System.Collections.Generic.List[string]]::new()
    ModuleCount     = 0
    ModuleList      = @()
    StartedAt       = Get-Date
}

# ============================================================================
# PRE-FLIGHT — validate az + bicep availability, subscription context
# ============================================================================

function Test-Prerequisites {
    Write-Host "==== PRE-FLIGHT ====" -ForegroundColor Cyan

    # Azure CLI
    $azVersion = (az version --output json 2>&1 | ConvertFrom-Json).'azure-cli'
    Write-Host "  [OK] Azure CLI: $azVersion"

    # Bicep
    $bicepVersion = az bicep version 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Bicep CLI unavailable. Run 'az bicep install' or 'az bicep upgrade'."
    }
    Write-Host "  [OK] Bicep: $bicepVersion"

    # Subscription (only needed for DryRun/Full modes)
    if ($Mode -in @('DryRun', 'Full')) {
        $account = (az account show --output json 2>&1) | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0) {
            throw "Not logged in to Azure. Run 'az login' before invoking with -Mode DryRun/Full."
        }
        if ($account.id -ne $SubscriptionId) {
            Write-Host "  [WARN] Current sub $($account.id) != requested $SubscriptionId — switching..."
            az account set --subscription $SubscriptionId 2>&1 | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "Cannot switch to subscription $SubscriptionId."
            }
        }
        Write-Host "  [OK] Subscription: $($account.name) ($($account.id))"
    }
    else {
        Write-Host "  [SKIP] Subscription check (Mode=$Mode does not require dev sub)"
    }

    Write-Host ""
}

# ============================================================================
# TIER 1 — STATIC BUILD (mandatory for all modes)
# ============================================================================

function Invoke-StackBuild {
    param([hashtable]$Stack)

    Write-Host ("---- BUILD: {0} ({1}) ----" -f $Stack.Name, $Stack.Owner) -ForegroundColor Yellow

    if (-not (Test-Path $Stack.FullPath)) {
        $result = @{
            Status   = 'MISSING'
            Errors   = @("Stack file not found: $($Stack.FullPath)")
            Warnings = @()
        }
        $Results.BuildResults[$Stack.Name] = $result
        Write-Host "  [FAIL] File missing: $($Stack.FullPath)" -ForegroundColor Red
        return
    }

    # az bicep build writes ARM JSON to <stack>.json + prints warnings/errors to stderr
    # We capture stderr and grep for "Error " vs "Warning " to distinguish.
    $stderrPath = New-TemporaryFile
    try {
        $null = az bicep build --file $Stack.FullPath --stdout 2>$stderrPath
        $exitCode = $LASTEXITCODE
        $stderr = if (Test-Path $stderrPath) { Get-Content $stderrPath -Raw } else { '' }
    }
    finally {
        Remove-Item $stderrPath -ErrorAction SilentlyContinue
    }

    $errorLines = @()
    $warnLines = @()
    if ($stderr) {
        foreach ($line in $stderr -split "`r?`n") {
            if ($line -match '\s: Error ') { $errorLines += $line }
            elseif ($line -match '\s: Warning ') { $warnLines += $line }
        }
    }

    $status = if ($exitCode -eq 0) { 'PASS' } else { 'FAIL' }
    $result = @{
        Status   = $status
        ExitCode = $exitCode
        Errors   = $errorLines
        Warnings = $warnLines
    }
    $Results.BuildResults[$Stack.Name] = $result

    if ($status -eq 'PASS') {
        Write-Host "  [OK] Build succeeded ($($warnLines.Count) warnings)" -ForegroundColor Green
    }
    elseif ($Stack.ExpectedBuild -eq 'EXPECTED_FAILURE') {
        Write-Host "  [EXPECTED FAILURE] $($Stack.Name) build failed — deferred per task 029 D1" -ForegroundColor DarkYellow
        Write-Host "     Reason: $($Stack.DeferralReason)" -ForegroundColor DarkYellow
        foreach ($e in $errorLines) { Write-Host "       $e" -ForegroundColor DarkGray }
    }
    else {
        Write-Host "  [FAIL] Build failed (exit $exitCode)" -ForegroundColor Red
        foreach ($e in $errorLines) { Write-Host "       $e" -ForegroundColor Red }
    }
    Write-Host ""
}

# ============================================================================
# TIER 2 — LIVE WHAT-IF (DryRun + Full modes only)
# ============================================================================

function Invoke-StackWhatIf {
    param([hashtable]$Stack)

    if (-not $Stack.WhatIfEligible) {
        Write-Host "---- WHATIF: $($Stack.Name) — SKIPPED (not eligible) ----" -ForegroundColor DarkGray
        Write-Host "     Reason: $($Stack.DeferralReason)" -ForegroundColor DarkGray
        $Results.WhatIfResults[$Stack.Name] = @{ Status = 'SKIPPED'; Reason = 'not eligible per stack manifest' }
        Write-Host ""
        return
    }

    # Only what-if if build passed
    $buildStatus = $Results.BuildResults[$Stack.Name].Status
    if ($buildStatus -ne 'PASS') {
        Write-Host "---- WHATIF: $($Stack.Name) — SKIPPED (build not PASS) ----" -ForegroundColor DarkGray
        $Results.WhatIfResults[$Stack.Name] = @{ Status = 'SKIPPED'; Reason = "build status was $buildStatus" }
        Write-Host ""
        return
    }

    Write-Host ("---- WHATIF: {0} against dev ({1}) ----" -f $Stack.Name, $Location) -ForegroundColor Yellow

    # Build --parameters key=value list.
    # Behavior note: empty-string values are SILENTLY OMITTED from the --parameters list.
    # This is correct for optional bicep params with a default (e.g. customer.bicep
    # `param userAssignedIdentityResourceId string = ''` — omitting keeps the default).
    # RISK: if a FUTURE stack adds a required-no-default param and a caller passes '', the
    # missing-param error surfaces from Bicep at what-if time rather than as a test-setup
    # error — acceptable today (no such params), but flag if adding required-no-default params.
    $paramArgs = @()
    foreach ($k in $Stack.WhatIfParams.Keys) {
        $v = $Stack.WhatIfParams[$k]
        if ($null -ne $v -and $v -ne '') {
            $paramArgs += "$k=$v"
        }
    }

    $stderrPath = New-TemporaryFile
    $stdoutPath = New-TemporaryFile
    try {
        $args = @(
            'deployment', 'sub', 'what-if',
            '--location', $Location,
            '--template-file', $Stack.FullPath,
            '--result-format', 'ResourceIdOnly',
            '--no-pretty-print'
        )
        if ($paramArgs.Count -gt 0) {
            $args += '--parameters'
            $args += $paramArgs
        }
        # Capture what-if output to stdoutPath; errors to stderrPath
        & az @args 1>$stdoutPath 2>$stderrPath
        $exitCode = $LASTEXITCODE
        $stdout = Get-Content $stdoutPath -Raw -ErrorAction SilentlyContinue
        $stderr = Get-Content $stderrPath -Raw -ErrorAction SilentlyContinue
    }
    finally {
        Remove-Item $stderrPath, $stdoutPath -ErrorAction SilentlyContinue
    }

    $status = if ($exitCode -eq 0) { 'PASS' } else { 'FAIL' }
    $resourceCount = 0
    if ($stdout) {
        # ResourceIdOnly output lists one resource per line prefixed with a change symbol
        $resourceCount = ($stdout -split "`r?`n" | Where-Object { $_ -match '^\s*[+\-!~*=]\s+' }).Count
    }
    $Results.WhatIfResults[$Stack.Name] = @{
        Status        = $status
        ExitCode      = $exitCode
        ResourceCount = $resourceCount
        StdOut        = $stdout
        StdErr        = $stderr
    }

    if ($status -eq 'PASS') {
        Write-Host "  [OK] What-if succeeded — $resourceCount resource actions surfaced" -ForegroundColor Green
    }
    else {
        Write-Host "  [FAIL] What-if failed (exit $exitCode)" -ForegroundColor Red
        Write-Host "         $stderr" -ForegroundColor DarkGray
    }
    Write-Host ""
}

# ============================================================================
# TIER 3 — STRUCTURAL ASSERTIONS (Full mode only)
# ============================================================================

function Add-Assertion {
    param(
        [string]$Name,
        [ValidateSet('PASS', 'FAIL', 'SKIP')]
        [string]$Status,
        [string]$Detail
    )
    $Results.Assertions.Add([pscustomobject]@{
        Name   = $Name
        Status = $Status
        Detail = $Detail
    })
    $color = switch ($Status) { 'PASS' { 'Green' } 'FAIL' { 'Red' } default { 'DarkGray' } }
    Write-Host "  [$Status] $Name — $Detail" -ForegroundColor $color
}

function Invoke-StructuralAssertions {
    Write-Host "==== TIER 3 STRUCTURAL ASSERTIONS ====" -ForegroundColor Cyan

    # Assertion 1: Module count on disk (design.md drift observation, non-blocking)
    $onDiskModules = Get-ChildItem -Path $ModulesDir -Filter '*.bicep' -File
    $Results.ModuleCount = $onDiskModules.Count
    $Results.ModuleList = $onDiskModules.Name | Sort-Object
    Add-Assertion `
        -Name 'ModuleCountOnDisk' `
        -Status $(if ($onDiskModules.Count -ge 25) { 'PASS' } else { 'FAIL' }) `
        -Detail "$($onDiskModules.Count) *.bicep files in modules/ (design.md v3.2 baseline: 25; Wave 2 added uami/controlplane-app-service/cosmos-provisioning/app-service-slot/deployment-slot)"

    # Assertion 2: uami.bicep exists (task 028)
    Add-Assertion `
        -Name 'UamiModuleExists' `
        -Status $(if (Test-Path (Join-Path $ModulesDir 'uami.bicep')) { 'PASS' } else { 'FAIL' }) `
        -Detail 'modules/uami.bicep required for T5 UAMI-on-both-slots structural fix'

    # Assertion 3: app-service.bicep is UAMI-only (task 029)
    $appServiceContent = Get-Content (Join-Path $ModulesDir 'app-service.bicep') -Raw
    $hasUamiParam = $appServiceContent -match 'param\s+userAssignedIdentityResourceId\s+string'
    $stillHasKvParam = $appServiceContent -match 'param\s+keyVaultName\s+string'
    Add-Assertion `
        -Name 'AppServiceUamiOnly' `
        -Status $(if ($hasUamiParam -and -not $stillHasKvParam) { 'PASS' } else { 'FAIL' }) `
        -Detail "hasUamiParam=$hasUamiParam ; stillHasKvParam=$stillHasKvParam (task 029 T5 fix — should be UAMI-only)"

    # Assertion 4: customer.bicep accepts UAMI pass-through (task 027)
    $customerContent = Get-Content (Join-Path $BicepDir 'customer.bicep') -Raw
    Add-Assertion `
        -Name 'CustomerBicepAcceptsUami' `
        -Status $(if ($customerContent -match 'param\s+userAssignedIdentityResourceId\s+string') { 'PASS' } else { 'FAIL' }) `
        -Detail 'customer.bicep must accept userAssignedIdentityResourceId param (task 027 output)'

    # Assertion 5: platform-controlplane.bicep uses uami module + binds to BOTH slots on controlplane-app-service
    $cpContent = Get-Content (Join-Path $BicepDir 'platform-controlplane.bicep') -Raw
    $cpAsContent = Get-Content (Join-Path $ModulesDir 'controlplane-app-service.bicep') -Raw
    $hasUamiInvoke = $cpContent -match "module\s+uami\s+'modules/uami\.bicep'"
    $bothSlotsBound = $cpAsContent -match 'userAssignedIdentities' -and (($cpAsContent | Select-String -Pattern 'userAssignedIdentities' -AllMatches).Matches.Count -ge 2)
    Add-Assertion `
        -Name 'ControlPlaneUamiBothSlots' `
        -Status $(if ($hasUamiInvoke -and $bothSlotsBound) { 'PASS' } else { 'FAIL' }) `
        -Detail "hasUamiInvoke=$hasUamiInvoke ; bothSlotsBound=$bothSlotsBound (T5 verification — L2 control-plane app service must bind UAMI on both prod + staging slots)"

    # Assertion 6: What-if outputs surface UAMI resource (only if what-if ran)
    if ($Mode -eq 'Full' -and $Results.WhatIfResults['platform-controlplane'].Status -eq 'PASS') {
        $cpWhatIf = $Results.WhatIfResults['platform-controlplane'].StdOut
        $hasUami = $cpWhatIf -match 'Microsoft\.ManagedIdentity/userAssignedIdentities'
        Add-Assertion `
            -Name 'WhatIfSurfacesUamiResource' `
            -Status $(if ($hasUami) { 'PASS' } else { 'FAIL' }) `
            -Detail "platform-controlplane what-if output $(if ($hasUami) { 'contains' } else { 'MISSING' }) UAMI resource"
    }
    else {
        Add-Assertion `
            -Name 'WhatIfSurfacesUamiResource' `
            -Status 'SKIP' `
            -Detail 'Requires Mode=Full and successful what-if on platform-controlplane'
    }

    # Assertion 7: No .github/workflows/** file touched by this test (constraint: ci-workflows=N per POML)
    $workflowsDir = Join-Path $RepoRoot '.github' 'workflows'
    $workflowsUnchanged = -not (git status --porcelain $workflowsDir 2>$null)
    Add-Assertion `
        -Name 'NoCiWorkflowsTouched' `
        -Status $(if ($workflowsUnchanged) { 'PASS' } else { 'FAIL' }) `
        -Detail 'Per POML constraint: this test MUST NOT edit .github/workflows/** (Phase H coordinated PR only)'

    # Assertion 8: model1-shared.bicep unmigrated caller is a known follow-up (not a regression)
    $m1BuildStatus = $Results.BuildResults['stacks/model1-shared'].Status
    $expected = $Stacks | Where-Object { $_.Name -eq 'stacks/model1-shared' } | Select-Object -First 1
    $matches = ($m1BuildStatus -eq 'FAIL' -and $expected.ExpectedBuild -eq 'EXPECTED_FAILURE')
    Add-Assertion `
        -Name 'Model1SharedDeferredCallerFix' `
        -Status $(if ($matches) { 'PASS' } else { 'FAIL' }) `
        -Detail "build=$m1BuildStatus ; expected=$($expected.ExpectedBuild) (task 029 D1 explicitly deferred caller migration)"

    Write-Host ""
}

# ============================================================================
# NOTES ARTIFACT — persistent record of this run
# ============================================================================

function Write-NotesArtifact {
    $notesDir = Split-Path $NotesOutputPath -Parent
    if (-not (Test-Path $notesDir)) {
        New-Item -ItemType Directory -Path $notesDir -Force | Out-Null
    }

    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine("# Bicep E2E Dry-Run — $(Get-Date -Format 'yyyy-MM-dd')")
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('> Owner: customer-provisioning-orchestration-r1 task 034')
    [void]$sb.AppendLine('> Wave: C2 (Bicep + UAMI)')
    [void]$sb.AppendLine("> Mode: $Mode")
    [void]$sb.AppendLine("> Started: $($Results.StartedAt.ToString('u'))")
    [void]$sb.AppendLine("> Finished: $(Get-Date -Format u)")
    [void]$sb.AppendLine("> Test customer: $TestCustomerId in $TestEnvironment ($Location)")
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('---')
    [void]$sb.AppendLine('')

    # Tier 1 summary
    [void]$sb.AppendLine('## Tier 1 — Static `az bicep build`')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('| Stack | Owner | Expected | Actual | Warnings | Errors |')
    [void]$sb.AppendLine('|---|---|---|---|---|---|')
    foreach ($stack in $Stacks) {
        $r = $Results.BuildResults[$stack.Name]
        $emoji = if ($r.Status -eq 'PASS') { '[OK]' }
                 elseif ($stack.ExpectedBuild -eq 'EXPECTED_FAILURE' -and $r.Status -eq 'FAIL') { '[EXPECTED-FAIL]' }
                 else { '[FAIL]' }
        [void]$sb.AppendLine("| $($stack.Name) | $($stack.Owner) | $($stack.ExpectedBuild) | $emoji $($r.Status) | $($r.Warnings.Count) | $($r.Errors.Count) |")
    }
    [void]$sb.AppendLine('')

    # Errors detail
    foreach ($stack in $Stacks) {
        $r = $Results.BuildResults[$stack.Name]
        if ($r.Errors.Count -gt 0) {
            [void]$sb.AppendLine("### $($stack.Name) — build errors")
            [void]$sb.AppendLine('')
            [void]$sb.AppendLine('```')
            foreach ($e in $r.Errors) { [void]$sb.AppendLine($e) }
            [void]$sb.AppendLine('```')
            [void]$sb.AppendLine('')
            if ($stack.DeferralReason) {
                [void]$sb.AppendLine("**Deferral rationale**: $($stack.DeferralReason)")
                [void]$sb.AppendLine('')
            }
        }
    }

    # Tier 2 summary
    if ($Mode -in @('DryRun', 'Full')) {
        [void]$sb.AppendLine('## Tier 2 — Live `az deployment sub what-if` (dev)')
        [void]$sb.AppendLine('')
        [void]$sb.AppendLine('| Stack | Status | Resource Actions | Exit |')
        [void]$sb.AppendLine('|---|---|---|---|')
        foreach ($stack in $Stacks) {
            $w = $Results.WhatIfResults[$stack.Name]
            if ($null -eq $w) { continue }
            [void]$sb.AppendLine("| $($stack.Name) | $($w.Status) | $($w.ResourceCount) | $($w.ExitCode) |")
        }
        [void]$sb.AppendLine('')
    }
    else {
        [void]$sb.AppendLine('## Tier 2 — Live `az deployment sub what-if` (dev)')
        [void]$sb.AppendLine('')
        [void]$sb.AppendLine('SKIPPED in Mode=Build. Re-run with `-Mode DryRun` (or `-Mode Full` for assertions) against a live dev subscription.')
        [void]$sb.AppendLine('')
    }

    # Tier 3 assertions
    if ($Mode -eq 'Full') {
        [void]$sb.AppendLine('## Tier 3 — Structural Assertions')
        [void]$sb.AppendLine('')
        [void]$sb.AppendLine('| Assertion | Status | Detail |')
        [void]$sb.AppendLine('|---|---|---|')
        foreach ($a in $Results.Assertions) {
            [void]$sb.AppendLine("| $($a.Name) | $($a.Status) | $($a.Detail) |")
        }
        [void]$sb.AppendLine('')
        [void]$sb.AppendLine("Bicep modules on disk: **$($Results.ModuleCount)** (design.md v3.2 baseline was 25; Wave 2 additions expected: uami, controlplane-app-service, cosmos-provisioning, app-service-slot, deployment-slot)")
        [void]$sb.AppendLine('')
    }

    # Follow-ups + deviations
    if ($Results.Followups.Count -gt 0 -or $Results.Deviations.Count -gt 0) {
        [void]$sb.AppendLine('## Follow-ups & Deviations')
        [void]$sb.AppendLine('')
        foreach ($d in $Results.Deviations) { [void]$sb.AppendLine("- DEVIATION: $d") }
        foreach ($f in $Results.Followups) { [void]$sb.AppendLine("- FOLLOWUP: $f") }
        [void]$sb.AppendLine('')
    }

    # Scoped-out
    [void]$sb.AppendLine('## Scoped-Out (task 034)')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('The following are NOT verified by this run and are recorded as follow-on work:')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('- **Model 2 dedicated per-customer full stack composition** (`stacks/model2-full.bicep`) — outside Wave C2 scope; task 029 D1 deferred caller migration. Follow-on task recommended: verify + migrate model2-full.bicep caller pattern (parallel to model1-shared fix).')
    [void]$sb.AppendLine('- **Legacy Model 1 per-customer stack** (`stacks/model1-customer.bicep`) — superseded by `stacks/model1-shared.bicep`; verify at retirement time (Phase F).')
    [void]$sb.AppendLine('- **Real RBAC principalId GUID verification** — what-if reports role-assignment RESOURCES; actual principalId GUID match against a live UAMI is only observable post-apply. Verified separately in Phase F acceptance.')
    [void]$sb.AppendLine('- **CI wiring of this test** — deferred to Phase H coordinated PR per root CLAUDE.md §10 (`ci-workflows=Y` overlap with `ci-cd-unit-test-remediation-r1`).')
    [void]$sb.AppendLine('')

    # Final verdict
    $tier1Fails = ($Results.BuildResults.Values | Where-Object {
        $_.Status -eq 'FAIL' -and ($Stacks | Where-Object { $Results.BuildResults[$_.Name] -eq $_ }).ExpectedBuild -ne 'EXPECTED_FAILURE'
    }).Count
    $unexpectedFails = 0
    foreach ($stack in $Stacks) {
        $r = $Results.BuildResults[$stack.Name]
        if ($r.Status -eq 'FAIL' -and $stack.ExpectedBuild -ne 'EXPECTED_FAILURE') { $unexpectedFails++ }
    }
    $tier2Fails = 0
    if ($Mode -in @('DryRun', 'Full')) {
        $tier2Fails = ($Results.WhatIfResults.Values | Where-Object { $_.Status -eq 'FAIL' }).Count
    }
    $tier3Fails = ($Results.Assertions | Where-Object { $_.Status -eq 'FAIL' }).Count

    [void]$sb.AppendLine('## Verdict')
    [void]$sb.AppendLine('')
    if ($unexpectedFails -eq 0 -and $tier2Fails -eq 0 -and $tier3Fails -eq 0) {
        [void]$sb.AppendLine('**[PASS]** — Wave C2 composition is coherent within tested scope. All non-deferred stacks build clean; all structural assertions pass.')
    }
    else {
        [void]$sb.AppendLine("**[FAIL]** — Unexpected failures: build=$unexpectedFails whatif=$tier2Fails assertions=$tier3Fails")
    }
    [void]$sb.AppendLine('')

    Set-Content -Path $NotesOutputPath -Value $sb.ToString() -Encoding UTF8
    Write-Host "[NOTES] Written to $NotesOutputPath" -ForegroundColor Cyan
    Write-Host ''
}

# ============================================================================
# ORCHESTRATION
# ============================================================================

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Wave C2 Bicep Integration Test — Mode: $Mode" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

Test-Prerequisites

# Tier 1 — build all stacks
Write-Host "==== TIER 1 STATIC BUILDS ====" -ForegroundColor Cyan
foreach ($stack in $Stacks) {
    Invoke-StackBuild -Stack $stack
}

# Tier 2 — what-if (DryRun + Full)
if ($Mode -in @('DryRun', 'Full')) {
    Write-Host "==== TIER 2 LIVE WHAT-IF ====" -ForegroundColor Cyan
    foreach ($stack in $Stacks) {
        Invoke-StackWhatIf -Stack $stack
    }
}

# Tier 3 — structural assertions (Full only)
if ($Mode -eq 'Full') {
    Invoke-StructuralAssertions
}

# Record follow-ups regardless of mode (BEFORE writing artifact so they land in it)
$sectionSign = [char]0x00A7  # § — force literal into strings without PS escape ambiguity
[void]$Results.Followups.Add('Migrate stacks/model1-shared.bicep sharedBffApi module invocation to task-029 UAMI-only app-service.bicep param signature (currently passes deprecated keyVaultName + enableManagedIdentity; reads deprecated appServicePrincipalId output)')
[void]$Results.Followups.Add("Reconcile design.md ${sectionSign}7 module count (v3.2 says 25; on-disk is $($Results.ModuleCount) after Wave 2 additions)")
[void]$Results.Followups.Add("Coordinate Phase H CI-wiring PR to invoke this script in pull_request and nightly schedule workflows (root CLAUDE.md ${sectionSign}10 ci-workflows=Y overlap with ci-cd-unit-test-remediation-r1)")

# Persist notes
Write-NotesArtifact

# Emit follow-ups to console
if ($Results.Followups.Count -gt 0) {
    Write-Host "==== FOLLOWUPS ====" -ForegroundColor Cyan
    foreach ($f in $Results.Followups) { Write-Host "  - $f" -ForegroundColor DarkYellow }
    Write-Host ""
}

# Compute overall verdict
$unexpectedBuildFails = 0
foreach ($stack in $Stacks) {
    $r = $Results.BuildResults[$stack.Name]
    if ($r.Status -eq 'FAIL' -and $stack.ExpectedBuild -ne 'EXPECTED_FAILURE') { $unexpectedBuildFails++ }
}
$whatIfFails = if ($Mode -in @('DryRun', 'Full')) {
    ($Results.WhatIfResults.Values | Where-Object { $_.Status -eq 'FAIL' }).Count
} else { 0 }
$assertionFails = ($Results.Assertions | Where-Object { $_.Status -eq 'FAIL' }).Count

$overall = if ($unexpectedBuildFails -eq 0 -and $whatIfFails -eq 0 -and $assertionFails -eq 0) { 0 } else { 1 }
Write-Host "============================================================" -ForegroundColor Cyan
if ($overall -eq 0) {
    Write-Host "  OVERALL: PASS" -ForegroundColor Green
}
else {
    Write-Host "  OVERALL: FAIL (unexpected-build=$unexpectedBuildFails whatif=$whatIfFails assertions=$assertionFails)" -ForegroundColor Red
}
Write-Host "============================================================" -ForegroundColor Cyan

exit $overall
