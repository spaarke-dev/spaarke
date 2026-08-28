<#
.SYNOPSIS
    Repeatable deploy for the L2 customer-provisioning control plane
    (Sprk.Provisioning.ControlPlane.Api + .Worker) — publish, zip-deploy,
    and post-deploy verification for both App Service targets.

.DESCRIPTION
    customer-provisioning-orchestration-r1 task 113 (C5.9 / C1.7) — the
    delivery vehicle for every future L2 code change (including task 102's
    dispatcher), and the mechanism that converges the live dev stamp (today
    deployed ad-hoc, ahead of committed source per DS-5 §B.6) with committed
    source going forward.

    For each selected -Target this script:
      1. `dotnet publish -c Release -r linux-x64 --self-contained false`
         (both projects already pin RuntimeIdentifier=linux-x64 and
         SelfContained=false in their .csproj — the explicit flags here are
         belt-and-braces, matching DS-4's literal step wording).
      2. Zips the publish output.
      3. Deploys via `az webapp deploy --type zip`:
           - .Api   -> the STAGING SLOT (never directly to production —
                       DS-3 §3 Option 2 keeps the slot+swap pattern for the
                       public-facing REST host). Pass -Swap to additionally
                       swap staging -> production once staging is healthy
                       (with automatic swap-back on a failed post-swap health
                       check, parity with scripts/Deploy-BffApi.ps1).
           - .Worker -> stop -> zip-deploy -> start against the SLOTLESS
                       production site (DS-3 §3 Option 2: no staging slot
                       for the Worker — an always-on shadow staging slot
                       would silently drain the fleet queue against
                       production Cosmos/Service Bus with old/new code; the
                       stop/start "honest drain" is safe because crash-
                       recovery (I6) + Service Bus redelivery + Level-1/2/3
                       idempotency + §4C rollback already make in-flight-
                       message loss recoverable under EVERY topology).
      4. Post-deploy verification (fails the script, non-zero exit, on ANY
         check failure — the checks are load-bearing, not decorative):
           a. GET /healthz on every site touched by this run -> expect 200.
           b. `az servicebus queue show` on the fleet queue (task 108) ->
              expect requiresSession=true AND requiresDuplicateDetection=true.
           c. `az webapp config appsettings list` on every site touched ->
              expect the NFR-05 fail-fast config keys to be present + non-
              empty (see "CONFIG-KEY VERIFICATION" note below — the .Api and
              .Worker key SHAPES currently differ; this is a real, filed gap,
              not a script bug).

    CONFIG-KEY VERIFICATION — DISCOVERED DRIFT (documented, not silently
    special-cased around; task 113 author-time finding):
      task 109 (DS-5 C5.1) renamed the .Api Bicep module's config keys to
      the canonical NFR-05 shape (Cosmos__AccountEndpoint,
      ServiceBus__FullyQualifiedNamespace, ManagedIdentity__ClientId, MI-only
      -- no connection string). Task 109's own TASK-INDEX row explicitly
      files a follow-on: "controlplane-worker-app-service.bicep (task 101)
      carries the identical C5.1 key-shape drift, out of this task's scope."
      That follow-on was never picked up by a numbered task. The Worker's
      Bicep module (modules/controlplane-worker-app-service.bicep, as of
      this script's authoring) STILL emits the pre-fix shape: Cosmos__Endpoint
      / Cosmos__Database / Cosmos__RunsContainer + ServiceBus__ConnectionString
      (KV-ref) -- and has NO ManagedIdentity__ClientId setting at all (only
      the Azure-native AZURE_CLIENT_ID). Checking the .Api-shaped keys
      against the Worker would make this script permanently fail for
      -Target Worker/Both, not because the Worker is misconfigured, but
      because the check would be wrong. $script:WorkerRequiredConfigKeys
      below therefore checks the Worker's ACTUAL current Bicep-declared
      keys. When a future task closes this gap (rename the Worker's Bicep
      keys to match .Api's canonical shape), update
      $script:WorkerRequiredConfigKeys to match -- and delete this note.

    keyVaultReferenceIdentity PATCH — OWNED BY THIS SCRIPT (post-authoring
    audit defect #8, Wave G-8 Batch 4):
      platform-controlplane.bicep (header "DELIBERATELY OUT OF SCOPE", lines
      76-80) explicitly defers the keyVaultReferenceIdentity PATCH to
      "handler H4 (post-deploy)" -- but H4 patches CUSTOMER-stamp App
      Services, never L2's OWN sites. Nothing else owned this PATCH, so on a
      fresh stamp every @Microsoft.KeyVault app-setting reference on the L2
      sites resolves with the (absent) system-assigned identity -> null ->
      silent downstream failures (T1 family). This script therefore PATCHes
      keyVaultReferenceIdentity -> the L2 UAMI resourceId on every site it
      is about to deploy to (.Api production + staging slot, .Worker),
      BEFORE the code deploy, so restarted sites resolve KV refs with the
      UAMI from first boot. Idempotent: sites already targeting the UAMI
      are skipped; each PATCH is read-back-verified.

    /healthz ON .Api — DISCOVERED + FIXED AT AUTHOR TIME (task 113):
      Prior to this task, Sprk.Provisioning.ControlPlane.Api only mapped
      GET /ping (Endpoints/HealthEndpoints.cs) — there was NO /healthz route,
      even though modules/controlplane-app-service.bicep (task 033) has
      declared `healthCheckPath: '/healthz'` since the module's creation.
      That means the Azure App Service PLATFORM-level instance health probe
      has been 404ing against the live .Api site since it was first deployed
      -- a genuine, independently-discovered defect, not something this
      script introduced. Per the project's "fix drift at time of discovery"
      operating principle, task 113 added GET /healthz to HealthEndpoints.cs
      (mirroring the Worker's existing /healthz — task 100 Program.cs)
      rather than special-casing this script to probe /ping on .Api and
      /healthz on .Worker. See HealthEndpoints.cs file header for the full
      note. This script therefore uses ONE -HealthCheckPath default
      ('/healthz') uniformly across both targets.

    IDEMPOTENCY / SAFETY:
      - Every mutating Azure operation (zip-deploy, stop/start, slot swap)
        is gated behind $PSCmdlet.ShouldProcess() -- supports -WhatIf and
        -Confirm natively. -DryRun is a convenience alias that sets
        $WhatIfPreference = $true for the whole run (same idiom as
        scripts/provisioning/Grant-ControlPlaneIdentity.ps1's -DryRun,
        expressed via the built-in mechanism instead of a second code path).
      - Build/publish/package (local, non-Azure) steps run even in -DryRun /
        -WhatIf, so the operator can inspect the artifact; only the actual
        `az` deploy/stop/start/swap calls are skipped.
      - Verification (health / queue / config) is READ-ONLY and always runs
        (even under -WhatIf) EXCEPT when the corresponding deploy step was
        itself skipped by -WhatIf, in which case the site being probed may
        legitimately still be serving the PRIOR build -- the script says so
        explicitly rather than reporting a misleading pass/fail against
        stale expectations.

    PREREQUISITES (see .NOTES for the full checklist) — this script assumes
    ALL of the following live-ceremony backlog items have already been run
    by an operator; it does NOT perform them:
      - Live Service Bus queue delete + recreate (task 108's runbook) so the
        `sprk-provisioning-jobs` queue actually carries
        requiresSession=true / requiresDuplicateDetection=true (these are
        CREATE-TIME-ONLY properties in Azure Service Bus — landing the Bicep
        declaration is necessary but NOT sufficient on an existing queue).
      - scripts/provisioning/Grant-ControlPlaneIdentity.ps1 (task 111) run
        against the target environment (L2 UAMI registered as a Dataverse
        App User + granted the C5.8 Graph app-role set).
      - task 110's Service Bus Data Sender + Receiver RBAC role assignments
        for the L2 UAMI on the fleet Service Bus namespace.
      - The `rg-spaarke-platform-{env}` stack (platform-controlplane.bicep)
        already deployed at least once (this script does NOT run
        `az deployment sub create` -- it deploys CODE onto EXISTING App
        Service targets; re-run the Bicep stack separately if infra itself
        changed).

.PARAMETER Environment
    Target environment name (dev, staging, production). Drives every
    resource-name default below. Default: dev.

.PARAMETER Target
    Which host(s) to publish + deploy: Api, Worker, or Both. Default: Both.

.PARAMETER ResourceGroupName
    Resource group hosting the L2 App Service(s). Default:
    rg-spaarke-platform-{Environment} (platform-controlplane.bicep convention).

.PARAMETER ApiAppServiceName
    .Api App Service name. Default: spaarke-provisioning-controlplane-{Environment}.

.PARAMETER WorkerAppServiceName
    .Worker App Service name. Default:
    spaarke-provisioning-controlplane-worker-{Environment}.

.PARAMETER UamiName
    Name of the L2 control-plane User-Assigned Managed Identity. Used to
    resolve the UAMI resourceId for the keyVaultReferenceIdentity PATCH
    (defect #8 -- see .DESCRIPTION). Default:
    sprk-controlplane-{Environment}-uami (platform-controlplane.bicep
    convention).

.PARAMETER SkipBuild
    Skip `dotnet publish` for the selected target(s) and deploy the existing
    publish folder(s) instead. Faster iteration once a build is already on
    disk.

.PARAMETER Swap
    After a healthy .Api staging-slot deploy, swap staging -> production and
    re-verify health. Automatically swaps BACK to the prior version if the
    post-swap health check fails (see -RollbackOnFailure). Ignored when
    -Target Worker (the Worker has no slot to swap). Without -Swap, .Api
    deploys land ONLY on the staging slot -- production is untouched until
    an operator explicitly re-runs with -Swap (or performs a manual
    `az webapp deployment slot swap`).

.PARAMETER SlotName
    Name of the .Api staging slot. Default: staging (matches
    modules/controlplane-app-service.bicep's `stagingSlot` resource name).

.PARAMETER RollbackOnFailure
    If the post-swap production health check fails, automatically swap back
    to the previous version. Only meaningful with -Swap. Default: $true.

.PARAMETER ServiceBusNamespaceName
    Fleet-scoped Service Bus namespace hosting the sprk-provisioning-jobs
    queue. Default: spaarke-servicebus-{Environment} (verified live dev
    value; per platform-controlplane.bicep's own documented default).

.PARAMETER ServiceBusResourceGroupName
    Resource group hosting the fleet Service Bus namespace -- a DIFFERENT
    resource group than $ResourceGroupName (legacy dev value: SharePointEmbedded;
    per platform-controlplane.bicep's own parameter description, staging/prod
    MUST override this once the shared Service Bus resource group name for
    those environments is known). Default: SharePointEmbedded.

.PARAMETER QueueName
    Fleet dispatch queue name. Default: sprk-provisioning-jobs (task 108).

.PARAMETER HealthCheckPath
    Path appended to each deployed site's base URL for the post-deploy
    health probe. Default: /healthz (now present on BOTH .Api and .Worker
    -- see the DESCRIPTION note on the drift this script's authoring fixed).

.PARAMETER MaxHealthCheckRetries
    Maximum health-check retry attempts before declaring a site unhealthy.
    Default: 24 (sized like scripts/Deploy-BffApi.ps1 for a cold
    stop -> zip-deploy -> start cycle: 24 x 5s = 120s ceiling).

.PARAMETER HealthCheckIntervalSeconds
    Seconds between health-check retries. Default: 5.

.PARAMETER ApiPublishPath
    Override the .Api publish output directory. Default:
    {repo root}/deploy/controlplane-api-publish.

.PARAMETER WorkerPublishPath
    Override the .Worker publish output directory. Default:
    {repo root}/deploy/controlplane-worker-publish.

.PARAMETER SkipQueueVerification
    Skip the `az servicebus queue show` post-deploy check. Use only when
    diagnosing the deploy path itself in isolation.

.PARAMETER SkipConfigVerification
    Skip the NFR-05 config-key presence check. Use only when diagnosing the
    deploy path itself in isolation -- see the escalation note under
    .DESCRIPTION / .NOTES before reaching for this switch on .Api.

.PARAMETER DryRun
    Preview mode. Sets $WhatIfPreference = $true for the whole run so every
    $PSCmdlet.ShouldProcess()-gated Azure mutation is skipped and reported
    as "What if:" -- build/publish/package still run so the artifact can be
    inspected. Equivalent to -WhatIf; provided as an explicit named switch
    because the project's other provisioning scripts (e.g.
    Grant-ControlPlaneIdentity.ps1) expose the same name and operators
    reasonably expect parity.

.EXAMPLE
    # Dev: publish + deploy BOTH targets, .Api lands on staging slot only
    .\Deploy-ControlPlane.ps1

.EXAMPLE
    # Dev: deploy + swap .Api to production once staging is verified healthy
    .\Deploy-ControlPlane.ps1 -Target Api -Swap

.EXAMPLE
    # Dev: Worker only, using an already-built publish folder
    .\Deploy-ControlPlane.ps1 -Target Worker -SkipBuild

.EXAMPLE
    # Preview a full run without touching Azure
    .\Deploy-ControlPlane.ps1 -DryRun

.EXAMPLE
    # Preview via the native PowerShell switch (equivalent to -DryRun)
    .\Deploy-ControlPlane.ps1 -WhatIf

.EXAMPLE
    # Production: both targets, prod Service Bus RG differs from dev's legacy value
    .\Deploy-ControlPlane.ps1 -Environment production `
        -ServiceBusResourceGroupName "rg-spaarke-platform-production" `
        -Swap

.NOTES
    Project:      customer-provisioning-orchestration-r1
    Task:         113 - Author Deploy-ControlPlane.ps1 (C5.9 / C1.7)
    Depends on:   100 (.Api/.Worker split), 101 (Worker Bicep), 108 (queue
                  Bicep authoring), 109 (Api Bicep config-key fix).
    Consumed by:  every subsequent L2 code-change task (incl. 102's
                  dispatcher), the H9-equivalent live-deploy step in any
                  future L2 acceptance run.

    Justification (CLAUDE.md §11):
      Existing:   scripts/Deploy-BffApi.ps1 is the proven sibling pattern
                  for App Service publish + zip-deploy + verify (incl. the
                  stop -> Kudu zipdeploy -> start auto-recover fallback and
                  the slot-swap-with-rollback flow).
      Extension:  This script extends that pattern to L2's two-site
                  topology (.Api WITH a slot, .Worker WITHOUT one -- DS-3
                  §3 Option 2) plus two L2-specific verification steps
                  (fleet queue session/dedup properties, NFR-05 config-key
                  presence) the BFF script has no equivalent for. It does
                  NOT reuse Deploy-BffApi.ps1's Windows-IIS-specific
                  web.config stdout-logging step or its pre/post-deploy
                  SHA-256 file-hash "did the DLL actually get replaced"
                  verification -- both are workarounds for Windows App
                  Service quirks that do not apply to L2's Linux App
                  Service targets (`kind: 'app,linux'`,
                  `linuxFxVersion: 'DOTNETCORE|10.0'`).
      Cost-of-doing-nothing:
                  Without a repeatable deploy script, every subsequent Wave
                  G task that needs a live deploy (dispatcher smoke test,
                  sidecar verification, H13 live probes) repeats ad-hoc `az`
                  commands, perpetuating the exact "live binary ahead of
                  committed source" drift DS-5 §B.6 flags today.

    PREREQUISITES (live-ceremony backlog -- operator runs these FIRST; this
    script assumes they are already done and does NOT perform them):
      1. `az login` with Contributor on rg-spaarke-platform-{env}.
      2. Live Service Bus queue delete + recreate per task 108's runbook
         (projects/customer-provisioning-orchestration-r1/notes/
         queue-recreate-runbook-2026-08.md) -- requiresSession /
         requiresDuplicateDetection are create-time-only properties;
         landing the Bicep declaration alone does NOT retrofit an existing
         queue.
      3. scripts/provisioning/Grant-ControlPlaneIdentity.ps1 (task 111) run
         against the target environment.
      4. task 110's Service Bus Data Sender + Receiver RBAC grants for the
         L2 UAMI on the fleet namespace.
      5. platform-controlplane.bicep already deployed at least once for
         {Environment} (this script deploys CODE, not infrastructure).

    DISCOVERED-AT-AUTHOR-TIME GAPS (documented per CLAUDE.md §6.5 -- NOT
    silently worked around):
      - .Worker's Bicep-declared config-key shape still diverges from
        .Api's DS-5 C5.1 canonical shape (Cosmos__Endpoint vs
        Cosmos__AccountEndpoint; no ManagedIdentity__ClientId). Filed by
        task 109 as a follow-on with no owning task number as of this
        script's authoring. See $script:WorkerRequiredConfigKeys below.
      - .Api had NO /healthz route despite the App Service platform health
        probe expecting one since task 033. FIXED at discovery in this same
        commit (Endpoints/HealthEndpoints.cs) -- see file header there.

    Idempotent:   Yes for the deploy itself (re-running redeploys the same
                  artifact; `az webapp deploy` is not additive). NOT
                  idempotent for -Swap (each successful swap flips the
                  active slot again) -- this mirrors Deploy-BffApi.ps1's
                  existing UseSlotDeploy/swap semantics, an accepted,
                  understood operator pattern already in production use.

.LINK
    https://learn.microsoft.com/en-us/cli/azure/webapp/deployment/slot
.LINK
    https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-dead-letter-queues
#>

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $false)]
    [ValidateSet('dev', 'staging', 'production')]
    [string]$Environment = 'dev',

    [Parameter(Mandatory = $false)]
    [ValidateSet('Api', 'Worker', 'Both')]
    [string]$Target = 'Both',

    [Parameter(Mandatory = $false)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory = $false)]
    [string]$ApiAppServiceName,

    [Parameter(Mandatory = $false)]
    [string]$WorkerAppServiceName,

    [Parameter(Mandatory = $false)]
    [string]$UamiName,

    [Parameter(Mandatory = $false)]
    [switch]$SkipBuild,

    [Parameter(Mandatory = $false)]
    [switch]$Swap,

    [Parameter(Mandatory = $false)]
    [string]$SlotName = 'staging',

    [Parameter(Mandatory = $false)]
    [bool]$RollbackOnFailure = $true,

    [Parameter(Mandatory = $false)]
    [string]$ServiceBusNamespaceName,

    [Parameter(Mandatory = $false)]
    [string]$ServiceBusResourceGroupName = 'SharePointEmbedded',

    [Parameter(Mandatory = $false)]
    [string]$QueueName = 'sprk-provisioning-jobs',

    [Parameter(Mandatory = $false)]
    [string]$HealthCheckPath = '/healthz',

    [Parameter(Mandatory = $false)]
    [int]$MaxHealthCheckRetries = 24,

    [Parameter(Mandatory = $false)]
    [int]$HealthCheckIntervalSeconds = 5,

    [Parameter(Mandatory = $false)]
    [string]$ApiPublishPath,

    [Parameter(Mandatory = $false)]
    [string]$WorkerPublishPath,

    [Parameter(Mandatory = $false)]
    [switch]$SkipQueueVerification,

    [Parameter(Mandatory = $false)]
    [switch]$SkipConfigVerification,

    [Parameter(Mandatory = $false)]
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

# -DryRun is sugar over the built-in -WhatIf mechanism: every mutating call
# below is gated behind $PSCmdlet.ShouldProcess(), so setting the preference
# variable here makes -DryRun behave identically to -WhatIf without a second
# code path to keep in sync.
if ($DryRun) {
    $WhatIfPreference = $true
}

# -----------------------------------------------------------------------------
# Console helpers (style parity with Grant-ControlPlaneIdentity.ps1)
# -----------------------------------------------------------------------------

function Write-Header {
    param([Parameter(Mandatory)][string]$Title)
    Write-Host ''
    Write-Host ('=' * 78) -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host ('=' * 78) -ForegroundColor Cyan
    Write-Host ''
}

function Write-Section { param([Parameter(Mandatory)][string]$Title) Write-Host ''; Write-Host "  === $Title ===" -ForegroundColor Cyan }
function Write-Step    { param([string]$D) Write-Host "  [STEP] $D" -ForegroundColor Yellow }
function Write-Success { param([string]$M) Write-Host "  [OK] $M" -ForegroundColor Green }
function Write-Info    { param([string]$M) Write-Host "  [--] $M" -ForegroundColor Gray }
function Write-Warn    { param([string]$M) Write-Host "  [!!] $M" -ForegroundColor DarkYellow }
function Write-Skip    { param([string]$M) Write-Host "  [SKIP] $M" -ForegroundColor DarkCyan }
function Write-Fail    { param([string]$M) Write-Host "  [FAIL] $M" -ForegroundColor Red }

# -----------------------------------------------------------------------------
# Resolve defaults (per-environment naming per platform-controlplane.bicep)
# -----------------------------------------------------------------------------

if (-not $ResourceGroupName) { $ResourceGroupName = "rg-spaarke-platform-$Environment" }
if (-not $ApiAppServiceName) { $ApiAppServiceName = "spaarke-provisioning-controlplane-$Environment" }
if (-not $WorkerAppServiceName) { $WorkerAppServiceName = "spaarke-provisioning-controlplane-worker-$Environment" }
if (-not $ServiceBusNamespaceName) { $ServiceBusNamespaceName = "spaarke-servicebus-$Environment" }
if (-not $UamiName) { $UamiName = "sprk-controlplane-$Environment-uami" }

$RepoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path
$ApiProjectPath = Join-Path -Path $RepoRoot -ChildPath 'src\server\services\Sprk.Provisioning.ControlPlane.Api'
$WorkerProjectPath = Join-Path -Path $RepoRoot -ChildPath 'src\server\services\Sprk.Provisioning.ControlPlane.Worker'

if (-not $ApiPublishPath) { $ApiPublishPath = Join-Path -Path $RepoRoot -ChildPath 'deploy\controlplane-api-publish' }
if (-not $WorkerPublishPath) { $WorkerPublishPath = Join-Path -Path $RepoRoot -ChildPath 'deploy\controlplane-worker-publish' }

$ApiZipPath = Join-Path -Path $RepoRoot -ChildPath 'deploy\controlplane-api-publish.zip'
$WorkerZipPath = Join-Path -Path $RepoRoot -ChildPath 'deploy\controlplane-worker-publish.zip'

$deployApi = ($Target -eq 'Api' -or $Target -eq 'Both')
$deployWorker = ($Target -eq 'Worker' -or $Target -eq 'Both')

$ApiBaseUrl = "https://$ApiAppServiceName.azurewebsites.net"
$ApiStagingBaseUrl = "https://$ApiAppServiceName-$SlotName.azurewebsites.net"
$WorkerBaseUrl = "https://$WorkerAppServiceName.azurewebsites.net"

# NFR-05 fail-fast config keys, PER TARGET. See .DESCRIPTION /
# "CONFIG-KEY VERIFICATION — DISCOVERED DRIFT" for why these two lists are
# NOT the same shape today.
$script:ApiRequiredConfigKeys = @(
    'Cosmos__AccountEndpoint',
    'ServiceBus__FullyQualifiedNamespace',
    'ManagedIdentity__ClientId'
)
$script:WorkerRequiredConfigKeys = @(
    'Cosmos__Endpoint',
    'AZURE_CLIENT_ID'
)

# -----------------------------------------------------------------------------
# Banner
# -----------------------------------------------------------------------------

Write-Header 'L2 CONTROL-PLANE DEPLOY (task 113)'

if ($DryRun -or $WhatIfPreference) {
    Write-Host '  *** DRY RUN / -WhatIf MODE - no Azure mutation will occur ***' -ForegroundColor Magenta
    Write-Host ''
}

Write-Host "  Environment:            $Environment" -ForegroundColor Gray
Write-Host "  Target:                 $Target" -ForegroundColor Gray
Write-Host "  Resource group:         $ResourceGroupName" -ForegroundColor Gray
if ($deployApi) {
    Write-Host "  .Api App Service:       $ApiAppServiceName (slot: $SlotName, swap: $(if ($Swap) { 'YES' } else { 'NO -- staging only' }))" -ForegroundColor Gray
}
if ($deployWorker) {
    Write-Host "  .Worker App Service:    $WorkerAppServiceName (slotless -- stop/deploy/start)" -ForegroundColor Gray
}
Write-Host "  Service Bus namespace:  $ServiceBusNamespaceName (rg: $ServiceBusResourceGroupName)" -ForegroundColor Gray
Write-Host "  Fleet queue:            $QueueName" -ForegroundColor Gray
Write-Host "  L2 UAMI:                $UamiName (keyVaultReferenceIdentity target)" -ForegroundColor Gray
Write-Host ''

# -----------------------------------------------------------------------------
# Pre-flight
# -----------------------------------------------------------------------------

Write-Section 'PRE-FLIGHT'

if ($PSVersionTable.PSVersion.Major -lt 7) {
    Write-Fail "PowerShell 7+ required. Detected: $($PSVersionTable.PSVersion)."
    exit 2
}
Write-Success "PowerShell version: $($PSVersionTable.PSVersion)"

# NOTE: capture the native command's full output to a variable BEFORE piping
# through Select-Object -First -- chaining `az ... | Select-Object -First 1`
# directly can short-circuit the pipeline before the native process's exit
# code is captured, leaving $LASTEXITCODE stale/$null (reproduced during this
# script's authoring: `$null -ne 0` evaluates $true, so the stale-null case
# would falsely report "Azure CLI not found" even when it is present and
# healthy). Capturing first avoids the early-pipeline-stop entirely.
$azProbeLines = az --version 2>$null
if ($LASTEXITCODE -ne 0 -or -not $azProbeLines) {
    Write-Fail 'Azure CLI (az) not found on PATH. Install: https://learn.microsoft.com/cli/azure/install-azure-cli'
    exit 2
}
$azProbe = $azProbeLines | Select-Object -First 1
Write-Success "Azure CLI available: $azProbe"

$account = az account show --output json 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Fail "Azure CLI is not authenticated. Run 'az login' with Contributor on '$ResourceGroupName' first."
    exit 2
}
Write-Success "Authenticated as '$($account.user.name)' (subscription: $($account.name))"

if (-not (Test-Path -Path $ApiProjectPath) -and $deployApi) {
    Write-Fail "Api project not found at '$ApiProjectPath'."
    exit 2
}
if (-not (Test-Path -Path $WorkerProjectPath) -and $deployWorker) {
    Write-Fail "Worker project not found at '$WorkerProjectPath'."
    exit 2
}
Write-Success 'Project paths resolved.'

# =============================================================================
# KV-REFERENCE IDENTITY -- PATCH keyVaultReferenceIdentity -> L2 UAMI
# (post-authoring audit defect #8, Wave G-8 Batch 4)
#
# platform-controlplane.bicep deliberately does NOT set
# keyVaultReferenceIdentity (its header defers the PATCH to "handler H4
# post-deploy") -- but H4 patches CUSTOMER-stamp App Services, never L2's own
# sites. Without this PATCH, an L2 site resolves @Microsoft.KeyVault
# app-setting references with its (absent) system-assigned identity, so every
# KV-ref setting resolves null -> silent downstream failures (T1 family).
# This step runs BEFORE the code deploy so the restarted sites resolve KV
# refs with the UAMI from first boot. Idempotent (already-correct sites are
# skipped) + read-back-verified. Scoped to -Target: .Api runs patch the
# production site AND the staging slot; .Worker runs patch the slotless
# Worker site. A PATCH failure stops the script BEFORE any code deploy.
# =============================================================================

Write-Section 'KV-REFERENCE IDENTITY -- keyVaultReferenceIdentity -> L2 UAMI (defect #8)'

Write-Step "Resolving L2 UAMI '$UamiName' resourceId"
$uamiResourceId = az identity show `
    --resource-group $ResourceGroupName `
    --name $UamiName `
    --query 'id' `
    --output tsv 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($uamiResourceId)) {
    Write-Fail "Could not resolve L2 UAMI '$UamiName' in resource group '$ResourceGroupName'. The platform-controlplane.bicep stack must be deployed first (prerequisite 5) -- this script deploys CODE onto EXISTING infrastructure."
    exit 2
}
$uamiResourceId = $uamiResourceId.Trim()
Write-Success "L2 UAMI resolved: $uamiResourceId"

$subscriptionId = $account.id
$kvRefTargets = @()
if ($deployApi) {
    $apiSiteResourceId = "/subscriptions/$subscriptionId/resourceGroups/$ResourceGroupName/providers/Microsoft.Web/sites/$ApiAppServiceName"
    $kvRefTargets += [pscustomobject]@{
        Label      = ".Api production ($ApiAppServiceName)"
        ResourceId = $apiSiteResourceId
    }
    $kvRefTargets += [pscustomobject]@{
        Label      = ".Api staging slot ($ApiAppServiceName/$SlotName)"
        ResourceId = "$apiSiteResourceId/slots/$SlotName"
    }
}
if ($deployWorker) {
    $kvRefTargets += [pscustomobject]@{
        Label      = ".Worker ($WorkerAppServiceName)"
        ResourceId = "/subscriptions/$subscriptionId/resourceGroups/$ResourceGroupName/providers/Microsoft.Web/sites/$WorkerAppServiceName"
    }
}

$kvRefFailures = New-Object System.Collections.Generic.List[string]

foreach ($kvRefTarget in $kvRefTargets) {
    # READ-ONLY probe of the current value. `az resource show --ids` works
    # uniformly for BOTH Microsoft.Web/sites and Microsoft.Web/sites/slots
    # (unlike `az webapp show`, which needs distinct --slot plumbing), so the
    # same code path covers all three targets.
    $currentIdentity = az resource show `
        --ids $kvRefTarget.ResourceId `
        --query 'properties.keyVaultReferenceIdentity' `
        --output tsv 2>$null
    if ($LASTEXITCODE -ne 0) {
        $kvRefFailures.Add("$($kvRefTarget.Label): az resource show failed for '$($kvRefTarget.ResourceId)' -- does the site/slot exist? (prerequisite 5: platform-controlplane.bicep must be deployed first)") | Out-Null
        continue
    }

    if ($currentIdentity -and ($currentIdentity.Trim() -ieq $uamiResourceId)) {
        Write-Success "$($kvRefTarget.Label): keyVaultReferenceIdentity already targets the L2 UAMI (no PATCH needed)."
        continue
    }

    if ($PSCmdlet.ShouldProcess($kvRefTarget.Label, "PATCH keyVaultReferenceIdentity -> $UamiName")) {
        Write-Step "PATCHing $($kvRefTarget.Label): keyVaultReferenceIdentity -> L2 UAMI"
        az resource update `
            --ids $kvRefTarget.ResourceId `
            --set "properties.keyVaultReferenceIdentity=$uamiResourceId" `
            --output none 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            $kvRefFailures.Add("$($kvRefTarget.Label): az resource update (keyVaultReferenceIdentity PATCH) failed with exit code $LASTEXITCODE.") | Out-Null
            continue
        }

        # Read-back verification -- the PATCH is load-bearing (KV-ref
        # resolution), so trust-but-verify like every other check here.
        $verifiedIdentity = az resource show `
            --ids $kvRefTarget.ResourceId `
            --query 'properties.keyVaultReferenceIdentity' `
            --output tsv 2>$null
        if ($LASTEXITCODE -ne 0 -or -not $verifiedIdentity -or ($verifiedIdentity.Trim() -ine $uamiResourceId)) {
            $kvRefFailures.Add("$($kvRefTarget.Label): post-PATCH verification FAILED -- keyVaultReferenceIdentity reads back as '$verifiedIdentity', expected '$uamiResourceId'.") | Out-Null
        }
        else {
            Write-Success "$($kvRefTarget.Label): keyVaultReferenceIdentity PATCHed + verified."
        }
    }
    else {
        $currentDisplay = if ($currentIdentity) { $currentIdentity.Trim() } else { '<unset>' }
        Write-Info "$($kvRefTarget.Label): PATCH skipped (-WhatIf/-DryRun). Current keyVaultReferenceIdentity: $currentDisplay"
    }
}

if ($kvRefFailures.Count -gt 0) {
    Write-Fail "$($kvRefFailures.Count) keyVaultReferenceIdentity failure(s) -- STOPPING before any code deploy (KV-ref app-settings would resolve null on the restarted sites):"
    foreach ($f in $kvRefFailures) {
        Write-Host "    - $f" -ForegroundColor Red
    }
    exit 1
}

# -----------------------------------------------------------------------------
# Helper: publish (dotnet publish -- local, non-Azure; runs even under -WhatIf
# so the operator can inspect the artifact before deciding to deploy it).
# -----------------------------------------------------------------------------

function Publish-ControlPlaneProject {
    param(
        [Parameter(Mandatory)][string]$Label,
        [Parameter(Mandatory)][string]$ProjectPath,
        [Parameter(Mandatory)][string]$PublishPath
    )

    if ($SkipBuild) {
        Write-Skip "$Label build (using existing publish folder)"
        if (-not (Test-Path -Path $PublishPath)) {
            throw "Publish folder not found at '$PublishPath' for $Label. Run without -SkipBuild first."
        }
        return
    }

    Write-Step "Publishing $Label (Release, linux-x64, framework-dependent)"

    if (Test-Path -Path $PublishPath) {
        Remove-Item -Path $PublishPath -Recurse -Force
    }

    Push-Location -Path $ProjectPath
    try {
        dotnet restore --verbosity minimal 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "$Label restore failed with exit code $LASTEXITCODE"
        }

        # -r/--self-contained are already pinned in the .csproj
        # (RuntimeIdentifier=linux-x64, SelfContained=false) -- passed
        # explicitly here too for parity with DS-4's literal step wording
        # and to fail loudly (not silently drift) if a future csproj edit
        # ever removes those properties.
        dotnet publish -c Release -r linux-x64 --self-contained false -o $PublishPath --no-restore 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "$Label publish failed with exit code $LASTEXITCODE"
        }
        Write-Success "$Label publish succeeded -> $PublishPath"
    }
    finally {
        Pop-Location
    }
}

# -----------------------------------------------------------------------------
# Helper: package (zip -- local, non-Azure). L2 App Service targets are
# LINUX (kind: 'app,linux', linuxFxVersion 'DOTNETCORE|10.0'), so the
# web.config stdout-logging step Deploy-BffApi.ps1 performs (a Windows/IIS
# concern) does NOT apply here -- intentionally omitted, not forgotten.
# -----------------------------------------------------------------------------

function Compress-ControlPlanePackage {
    param(
        [Parameter(Mandatory)][string]$Label,
        [Parameter(Mandatory)][string]$PublishPath,
        [Parameter(Mandatory)][string]$ZipPath
    )

    Write-Step "Packaging $Label"

    $deployDir = Split-Path -Path $ZipPath -Parent
    if (-not (Test-Path -Path $deployDir)) {
        New-Item -ItemType Directory -Path $deployDir -Force | Out-Null
    }
    if (Test-Path -Path $ZipPath) {
        Remove-Item -Path $ZipPath -Force
    }

    Compress-Archive -Path "$PublishPath\*" -DestinationPath $ZipPath -Force
    $zipSizeMb = [math]::Round((Get-Item -Path $ZipPath).Length / 1MB, 2)
    Write-Success "$Label package created: $zipSizeMb MB"

    if ($zipSizeMb -gt 150) {
        Write-Warn "$Label package is larger than expected ($zipSizeMb MB > 150 MB) -- consider a clean build."
    }
}

# -----------------------------------------------------------------------------
# Helper: health-check retry loop (parity with Deploy-BffApi.ps1's
# Test-HealthCheck).
# -----------------------------------------------------------------------------

function Test-HealthCheck {
    param(
        [Parameter(Mandatory)][string]$Url,
        [Parameter(Mandatory)][int]$MaxRetries,
        [Parameter(Mandatory)][int]$IntervalSeconds,
        [Parameter(Mandatory)][string]$Label
    )

    Write-Info "Checking health: $Url"
    $retryCount = 0
    $healthy = $false

    while ($retryCount -lt $MaxRetries -and -not $healthy) {
        try {
            $response = Invoke-RestMethod -Uri $Url -TimeoutSec 10 -ErrorAction Stop
            if ($response -eq 'ok' -or $response -eq 'Healthy' -or $response.status -eq 'Healthy') {
                $healthy = $true
                Write-Success "$Label health check passed."
            }
        }
        catch {
            $retryCount++
            if ($retryCount -lt $MaxRetries) {
                Write-Info "Waiting for $Label to respond... (attempt $retryCount/$MaxRetries)"
                Start-Sleep -Seconds $IntervalSeconds
            }
        }
    }

    return $healthy
}

# -----------------------------------------------------------------------------
# Helper: fleet queue property verification (task 108).
# -----------------------------------------------------------------------------

function Test-QueueProperties {
    param(
        [Parameter(Mandatory)][string]$NamespaceName,
        [Parameter(Mandatory)][string]$NamespaceResourceGroup,
        [Parameter(Mandatory)][string]$Name
    )

    $raw = az servicebus queue show `
        --resource-group $NamespaceResourceGroup `
        --namespace-name $NamespaceName `
        --name $Name `
        --query '{requiresSession:requiresSession,requiresDuplicateDetection:requiresDuplicateDetection}' `
        --output json 2>&1

    if ($LASTEXITCODE -ne 0) {
        throw "az servicebus queue show failed for '$Name' in namespace '$NamespaceName' (rg '$NamespaceResourceGroup'): $($raw | Out-String)"
    }

    return ($raw | Out-String | ConvertFrom-Json)
}

# -----------------------------------------------------------------------------
# Helper: NFR-05 config-key presence verification.
# -----------------------------------------------------------------------------

function Get-MissingRequiredConfigKeys {
    param(
        [Parameter(Mandatory)][string]$SiteResourceGroup,
        [Parameter(Mandatory)][string]$AppServiceName,
        [Parameter(Mandatory)][string[]]$RequiredKeys
    )

    $raw = az webapp config appsettings list `
        --resource-group $SiteResourceGroup `
        --name $AppServiceName `
        --output json 2>&1

    if ($LASTEXITCODE -ne 0) {
        throw "az webapp config appsettings list failed for '$AppServiceName': $($raw | Out-String)"
    }

    $settings = $raw | Out-String | ConvertFrom-Json
    $byName = @{}
    foreach ($s in $settings) { $byName[$s.name] = $s.value }

    $missing = New-Object System.Collections.Generic.List[string]
    foreach ($k in $RequiredKeys) {
        if (-not $byName.ContainsKey($k) -or [string]::IsNullOrWhiteSpace($byName[$k])) {
            $missing.Add($k) | Out-Null
        }
    }
    return $missing
}

# -----------------------------------------------------------------------------
# State-changing helpers (each gated by the CALLER's ShouldProcess -- see the
# `if ($PSCmdlet.ShouldProcess(...))` blocks in the main deploy flow below;
# PSScriptAnalyzer's PSUseShouldProcessForStateChangingFunctions is
# suppressed locally with justification, same idiom as
# Grant-ControlPlaneIdentity.ps1's New-SecurityRole / New-DataverseApplicationUser).
# -----------------------------------------------------------------------------

function Invoke-SlotZipDeploy {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute(
        'PSUseShouldProcessForStateChangingFunctions', '',
        Justification = 'Caller owns the ShouldProcess gate.')]
    param(
        [Parameter(Mandatory)][string]$AppServiceRg,
        [Parameter(Mandatory)][string]$AppServiceName,
        [Parameter(Mandatory)][string]$Slot,
        [Parameter(Mandatory)][string]$ZipPath
    )
    $ErrorActionPreference = 'Continue'
    $output = az webapp deploy `
        --resource-group $AppServiceRg `
        --name $AppServiceName `
        --slot $Slot `
        --src-path $ZipPath `
        --type zip `
        --async false 2>&1 | Out-String
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($exitCode -ne 0) {
        throw "az webapp deploy to slot '$Slot' failed (exit $exitCode): $output"
    }
}

function Invoke-ProductionZipDeploy {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute(
        'PSUseShouldProcessForStateChangingFunctions', '',
        Justification = 'Caller owns the ShouldProcess gate.')]
    param(
        [Parameter(Mandatory)][string]$AppServiceRg,
        [Parameter(Mandatory)][string]$AppServiceName,
        [Parameter(Mandatory)][string]$ZipPath
    )
    $ErrorActionPreference = 'Continue'
    $output = az webapp deploy `
        --resource-group $AppServiceRg `
        --name $AppServiceName `
        --src-path $ZipPath `
        --type zip `
        --async false 2>&1 | Out-String
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($exitCode -ne 0) {
        throw "az webapp deploy failed (exit $exitCode): $output"
    }
}

function Invoke-WebAppStop {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute(
        'PSUseShouldProcessForStateChangingFunctions', '',
        Justification = 'Caller owns the ShouldProcess gate.')]
    param(
        [Parameter(Mandatory)][string]$AppServiceRg,
        [Parameter(Mandatory)][string]$AppServiceName
    )
    az webapp stop --resource-group $AppServiceRg --name $AppServiceName | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "az webapp stop failed for '$AppServiceName' (exit $LASTEXITCODE)"
    }
}

function Invoke-WebAppStart {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute(
        'PSUseShouldProcessForStateChangingFunctions', '',
        Justification = 'Caller owns the ShouldProcess gate.')]
    param(
        [Parameter(Mandatory)][string]$AppServiceRg,
        [Parameter(Mandatory)][string]$AppServiceName
    )
    az webapp start --resource-group $AppServiceRg --name $AppServiceName | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "az webapp start failed for '$AppServiceName' (exit $LASTEXITCODE)"
    }
}

function Invoke-SlotSwap {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute(
        'PSUseShouldProcessForStateChangingFunctions', '',
        Justification = 'Caller owns the ShouldProcess gate.')]
    param(
        [Parameter(Mandatory)][string]$AppServiceRg,
        [Parameter(Mandatory)][string]$AppServiceName,
        [Parameter(Mandatory)][string]$Slot
    )
    $ErrorActionPreference = 'Continue'
    $output = az webapp deployment slot swap `
        --resource-group $AppServiceRg `
        --name $AppServiceName `
        --slot $Slot `
        --target-slot production 2>&1 | Out-String
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($exitCode -ne 0) {
        throw "Slot swap failed (exit $exitCode): $output"
    }
}

# -----------------------------------------------------------------------------
# Failure aggregation -- the script exits non-zero on ANY verification
# failure, with a clear message identifying which check failed (POML
# acceptance criterion / DS-4 step 7).
# -----------------------------------------------------------------------------

$script:VerificationFailures = New-Object System.Collections.Generic.List[string]
$script:DeployedApiTarget = $null   # 'staging' | 'production' | $null (not deployed / -WhatIf skipped)
$script:DeployedWorker = $false

# =============================================================================
# BUILD + PACKAGE
# =============================================================================

Write-Section 'BUILD + PACKAGE'

if ($deployApi) {
    Publish-ControlPlaneProject -Label 'Api' -ProjectPath $ApiProjectPath -PublishPath $ApiPublishPath
    Compress-ControlPlanePackage -Label 'Api' -PublishPath $ApiPublishPath -ZipPath $ApiZipPath
}
if ($deployWorker) {
    Publish-ControlPlaneProject -Label 'Worker' -ProjectPath $WorkerProjectPath -PublishPath $WorkerPublishPath
    Compress-ControlPlanePackage -Label 'Worker' -PublishPath $WorkerPublishPath -ZipPath $WorkerZipPath
}

# =============================================================================
# DEPLOY -- .Api (staging slot, optional swap)
# =============================================================================

if ($deployApi) {
    Write-Section 'DEPLOY -- .Api (staging slot)'

    if ($PSCmdlet.ShouldProcess("$ApiAppServiceName/$SlotName", 'az webapp deploy (zip)')) {
        Write-Step "Deploying .Api to staging slot '$SlotName'"
        Invoke-SlotZipDeploy -AppServiceRg $ResourceGroupName -AppServiceName $ApiAppServiceName -Slot $SlotName -ZipPath $ApiZipPath
        Write-Success "Deployed to staging slot '$SlotName'."

        Write-Step 'Waiting for staging slot to warm up'
        Start-Sleep -Seconds 10

        $stagingHealthy = Test-HealthCheck `
            -Url "$ApiStagingBaseUrl$HealthCheckPath" `
            -MaxRetries $MaxHealthCheckRetries `
            -IntervalSeconds $HealthCheckIntervalSeconds `
            -Label 'Api staging slot'

        if (-not $stagingHealthy) {
            $script:VerificationFailures.Add("Api staging slot ('$SlotName') failed health check at $ApiStagingBaseUrl$HealthCheckPath after $MaxHealthCheckRetries attempts. Production is UNCHANGED.") | Out-Null
        }
        else {
            $script:DeployedApiTarget = 'staging'

            if ($Swap) {
                if ($PSCmdlet.ShouldProcess("$ApiAppServiceName", "Swap slot '$SlotName' -> production")) {
                    Write-Step "Swapping '$SlotName' -> production"
                    Invoke-SlotSwap -AppServiceRg $ResourceGroupName -AppServiceName $ApiAppServiceName -Slot $SlotName
                    Write-Success 'Slot swap complete.'

                    Start-Sleep -Seconds 5
                    $prodHealthy = Test-HealthCheck `
                        -Url "$ApiBaseUrl$HealthCheckPath" `
                        -MaxRetries $MaxHealthCheckRetries `
                        -IntervalSeconds $HealthCheckIntervalSeconds `
                        -Label 'Api production (post-swap)'

                    if (-not $prodHealthy) {
                        Write-Fail 'Production health check failed after swap.'
                        if ($RollbackOnFailure) {
                            if ($PSCmdlet.ShouldProcess("$ApiAppServiceName", "ROLLBACK: swap '$SlotName' <-> production")) {
                                Write-Step 'Rolling back: swapping back to previous version'
                                Invoke-SlotSwap -AppServiceRg $ResourceGroupName -AppServiceName $ApiAppServiceName -Slot $SlotName
                                Start-Sleep -Seconds 5
                                $rollbackHealthy = Test-HealthCheck `
                                    -Url "$ApiBaseUrl$HealthCheckPath" `
                                    -MaxRetries $MaxHealthCheckRetries `
                                    -IntervalSeconds $HealthCheckIntervalSeconds `
                                    -Label 'Api production (rollback)'
                                if ($rollbackHealthy) {
                                    $script:VerificationFailures.Add('Api post-swap production health check FAILED; automatic rollback swap succeeded and production is healthy on the PRIOR version. Investigate the staging build before retrying -Swap.') | Out-Null
                                }
                                else {
                                    $script:VerificationFailures.Add('Api post-swap production health check FAILED and the ROLLBACK swap ALSO failed its health check. Manual investigation required immediately.') | Out-Null
                                }
                            }
                        }
                        else {
                            $script:VerificationFailures.Add('Api post-swap production health check FAILED. -RollbackOnFailure is $false -- production was NOT automatically restored. Manual investigation required.') | Out-Null
                        }
                    }
                    else {
                        $script:DeployedApiTarget = 'production'
                    }
                }
            }
            else {
                Write-Info "No -Swap passed -- staging slot '$SlotName' is healthy but PRODUCTION was not touched. Re-run with -Swap once ready to promote."
            }
        }
    }
    else {
        Write-Info 'Api deploy skipped (-WhatIf/-DryRun).'
    }
}

# =============================================================================
# DEPLOY -- .Worker (slotless: stop -> zip-deploy -> start)
# =============================================================================

if ($deployWorker) {
    Write-Section 'DEPLOY -- .Worker (slotless)'

    if ($PSCmdlet.ShouldProcess("$WorkerAppServiceName", 'stop -> zip-deploy -> start')) {
        Write-Step 'Stopping Worker site'
        Invoke-WebAppStop -AppServiceRg $ResourceGroupName -AppServiceName $WorkerAppServiceName
        Write-Success 'Worker stopped.'

        Write-Step 'Deploying Worker package'
        try {
            Invoke-ProductionZipDeploy -AppServiceRg $ResourceGroupName -AppServiceName $WorkerAppServiceName -ZipPath $WorkerZipPath
            Write-Success 'Worker package deployed.'
        }
        finally {
            # Always attempt to start the site back up, even if deploy threw --
            # a stopped Worker silently stops draining the fleet queue, which
            # is a worse operator surprise than a failed-but-running deploy.
            Write-Step 'Starting Worker site'
            Invoke-WebAppStart -AppServiceRg $ResourceGroupName -AppServiceName $WorkerAppServiceName
            Write-Success 'Worker started.'
        }

        Write-Step 'Waiting for Worker to warm up'
        Start-Sleep -Seconds 10

        $workerHealthy = Test-HealthCheck `
            -Url "$WorkerBaseUrl$HealthCheckPath" `
            -MaxRetries $MaxHealthCheckRetries `
            -IntervalSeconds $HealthCheckIntervalSeconds `
            -Label 'Worker'

        if (-not $workerHealthy) {
            $script:VerificationFailures.Add("Worker failed health check at $WorkerBaseUrl$HealthCheckPath after $MaxHealthCheckRetries attempts.") | Out-Null
        }
        else {
            $script:DeployedWorker = $true
        }
    }
    else {
        Write-Info 'Worker deploy skipped (-WhatIf/-DryRun).'
    }
}

# =============================================================================
# VERIFY -- fleet Service Bus queue properties (task 108)
# =============================================================================

if (-not $SkipQueueVerification) {
    Write-Section 'VERIFY -- fleet Service Bus queue properties (task 108)'
    try {
        $queueProps = Test-QueueProperties -NamespaceName $ServiceBusNamespaceName -NamespaceResourceGroup $ServiceBusResourceGroupName -Name $QueueName
        Write-Info "requiresSession=$($queueProps.requiresSession)  requiresDuplicateDetection=$($queueProps.requiresDuplicateDetection)"

        if ($queueProps.requiresSession -ne $true -or $queueProps.requiresDuplicateDetection -ne $true) {
            $script:VerificationFailures.Add("Fleet queue '$QueueName' does NOT have both requiresSession=true AND requiresDuplicateDetection=true (found requiresSession=$($queueProps.requiresSession), requiresDuplicateDetection=$($queueProps.requiresDuplicateDetection)). These are CREATE-TIME-ONLY properties -- the live queue must be deleted + recreated per task 108's runbook (notes/queue-recreate-runbook-2026-08.md); landing the Bicep declaration alone is NOT sufficient.") | Out-Null
        }
        else {
            Write-Success "Queue '$QueueName' has both properties correctly set."
        }
    }
    catch {
        $script:VerificationFailures.Add("Queue property verification threw: $($_.Exception.Message)") | Out-Null
    }
}
else {
    Write-Skip 'Queue property verification (-SkipQueueVerification)'
}

# =============================================================================
# VERIFY -- NFR-05 config-key presence
# =============================================================================

if (-not $SkipConfigVerification) {
    Write-Section 'VERIFY -- NFR-05 fail-fast config-key presence'

    if ($deployApi -and $script:DeployedApiTarget) {
        Write-Step ".Api ($ApiAppServiceName)"
        try {
            $missing = Get-MissingRequiredConfigKeys -SiteResourceGroup $ResourceGroupName -AppServiceName $ApiAppServiceName -RequiredKeys $script:ApiRequiredConfigKeys
            if ($missing.Count -gt 0) {
                # ESCALATION (per POML <escalation><trigger>): a missing key
                # here means task 109's Bicep fix either didn't apply to this
                # live site, or drifted since. Do NOT silently proceed --
                # shipping anyway just moves the discovery to the site's own
                # boot-time ValidateOnStart failure, later and noisier.
                $script:VerificationFailures.Add("Api site '$ApiAppServiceName' is MISSING required NFR-05 config key(s): $($missing -join ', '). These are expected per task 109's Bicep fix (modules/controlplane-app-service.bicep) -- STOP and investigate before relying on this deploy; do not re-run assuming it will self-correct.") | Out-Null
            }
            else {
                Write-Success 'All required Api config keys present and non-empty.'
            }
        }
        catch {
            $script:VerificationFailures.Add("Api config-key verification threw: $($_.Exception.Message)") | Out-Null
        }
    }
    elseif ($deployApi) {
        Write-Skip '.Api config-key check (Api was not successfully deployed this run)'
    }

    if ($deployWorker -and $script:DeployedWorker) {
        Write-Step ".Worker ($WorkerAppServiceName)"
        try {
            $missing = Get-MissingRequiredConfigKeys -SiteResourceGroup $ResourceGroupName -AppServiceName $WorkerAppServiceName -RequiredKeys $script:WorkerRequiredConfigKeys
            if ($missing.Count -gt 0) {
                $script:VerificationFailures.Add("Worker site '$WorkerAppServiceName' is MISSING config key(s): $($missing -join ', '). Note: the Worker's required-key list intentionally reflects its CURRENT (pre-C5.1-fix) Bicep shape -- see this script's .DESCRIPTION 'CONFIG-KEY VERIFICATION - DISCOVERED DRIFT' note. A missing key here means the live site diverges even from that shape.") | Out-Null
            }
            else {
                Write-Success 'All required Worker config keys present and non-empty.'
                Write-Info 'Reminder: Worker key SHAPE still diverges from the canonical NFR-05/C5.1 shape used by .Api (filed gap, no owning task as of this script''s authoring).'
            }
        }
        catch {
            $script:VerificationFailures.Add("Worker config-key verification threw: $($_.Exception.Message)") | Out-Null
        }
    }
    elseif ($deployWorker) {
        Write-Skip '.Worker config-key check (Worker was not successfully deployed this run)'
    }
}
else {
    Write-Skip 'NFR-05 config-key verification (-SkipConfigVerification)'
}

# =============================================================================
# SUMMARY
# =============================================================================

Write-Header 'SUMMARY'
Write-Host "  Environment:      $Environment" -ForegroundColor Gray
Write-Host "  Target:           $Target" -ForegroundColor Gray
if ($deployApi) {
    $apiState = if ($script:DeployedApiTarget) { $script:DeployedApiTarget } else { 'NOT deployed / unhealthy' }
    Write-Host "  Api result:       $apiState" -ForegroundColor $(if ($script:DeployedApiTarget) { 'Green' } else { 'Red' })
}
if ($deployWorker) {
    $workerState = if ($script:DeployedWorker) { 'deployed + healthy' } else { 'NOT deployed / unhealthy' }
    Write-Host "  Worker result:    $workerState" -ForegroundColor $(if ($script:DeployedWorker) { 'Green' } else { 'Red' })
}
Write-Host ''

if ($script:VerificationFailures.Count -eq 0) {
    if ($DryRun -or $WhatIfPreference) {
        Write-Success 'Dry run complete -- no failures reported for the steps that ran under -WhatIf. Re-run without -WhatIf/-DryRun to apply.'
    }
    else {
        Write-Success 'All post-deploy verification checks passed.'
    }
    Write-Host ''
    Write-Host 'Quick test commands:' -ForegroundColor Gray
    if ($deployApi) {
        Write-Host "  curl $ApiBaseUrl$HealthCheckPath" -ForegroundColor Gray
        Write-Host "  curl $ApiStagingBaseUrl$HealthCheckPath" -ForegroundColor Gray
    }
    if ($deployWorker) {
        Write-Host "  curl $WorkerBaseUrl$HealthCheckPath" -ForegroundColor Gray
    }
    Write-Host ''
    exit 0
}
else {
    Write-Fail "$($script:VerificationFailures.Count) verification failure(s):"
    foreach ($f in $script:VerificationFailures) {
        Write-Host "    - $f" -ForegroundColor Red
    }
    Write-Host ''
    exit 1
}
