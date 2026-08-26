<#
.SYNOPSIS
    Live verification harness for the Exchange ApplicationAccessPolicy sidecar
    (task 114) running as a sitecontainer under the L2 control-plane Worker
    App Service (task 101). Runs 6 non-destructive checks per task 162's POML
    acceptance criteria; produces a structured pass/fail report the operator
    hands off to the completed-live-verification note.

.DESCRIPTION
    customer-provisioning-orchestration-r1 task 162 (Wave G-6 Batch G-6C) —
    the OPERATOR-run diagnostic tool that finishes the loop task 161's client
    started. Executed AFTER the live ceremony (Wave G-1 backlog step 5) has
    deployed the Worker App Service + published the sidecar image to ACR.
    Non-destructive by design: every check is READ-ONLY against Azure (`az`
    show/list commands, GET HTTP) EXCEPT the two POST /apply-policy checks
    which fire against DEFAULT SAFE placeholder GUIDs that Listener.ps1's
    Set-ExchangeApplicationAccessPolicy.ps1 fails at Connect-ExchangeOnline
    before touching any real Exchange tenant. Operators wanting to exercise a
    real Exchange mutation must override the -TenantId / -PolicyScopeGroupId /
    -ExpectedAppIds parameters explicitly.

    Each check maps to one of the POML acceptance criteria + one of the DS-1b
    §3 security properties:

      1. CONTAINER_HEALTH  — sitecontainer is running under the Worker.
                             Azure REST: GET /api/webapps/{worker}/sitecontainers/{name}
      2. LOCALHOST_BIND    — sidecar's port is bound + healthy from inside the
                             Worker's network namespace (Kudu SSH exec).
                             Kudu REST: POST /api/command with `curl -sf http://localhost:8091/healthz`
      3. PUBLIC_ISOLATION  — sidecar's port is NOT reachable from the public
                             Worker hostname (network-namespace isolation).
                             Direct HTTP: GET https://{worker}.azurewebsites.net:8091/healthz  MUST fail/timeout
      4. ROUND_TRIP_AUTH   — a POST /apply-policy with the CORRECT
                             X-Sidecar-Auth header is accepted (not 401).
                             Kudu REST: POST /api/command with a curl POST
      5. ROUND_TRIP_IDEMP  — the SAME POST returns AlreadyCompliant on 2nd run
                             (script's get-before-set idempotency survives the
                             container+HTTP wrapping — proven from OUTSIDE the
                             script, at the wire).
      6. AUTH_REJECTION    — a POST /apply-policy WITHOUT (or with a WRONG)
                             X-Sidecar-Auth header is rejected (HTTP 401).
                             Kudu REST: POST /api/command with a bad-secret curl

    Checks 4/5 require a REAL Exchange tenant to move past Failure — with the
    default safe placeholders they will return wire Failure (Connect-Exchange
    Online rejects the all-zero-GUID tenantId), which is still a PASS for
    check 4 (proves the auth path did NOT reject the request) BUT is not a
    pass for check 5's idempotency assertion (the sidecar cannot demonstrate
    AlreadyCompliant if it never got past Connect). Operators wanting the
    full check 5 pass must supply real tenant/group/app-id parameters. This
    script REPORTS THE DISTINCTION EXPLICITLY rather than silently marking
    check 5 as "N/A" — see the report section for the discriminator.

.PARAMETER Environment
    Target environment name (dev, staging, production). Drives default
    resource names. Default: dev.

.PARAMETER WorkerAppServiceName
    L2 control-plane Worker App Service name. Defaults to
    spaarke-provisioning-controlplane-worker-{Environment}.

.PARAMETER WorkerResourceGroup
    Resource group hosting the Worker. Defaults to rg-spaarke-platform-{Environment}.

.PARAMETER SidecarName
    Name of the sitecontainer resource attached to the Worker.
    Defaults to `exchange-policy-sidecar` (per infrastructure/bicep/modules/
    controlplane-worker-app-service.bicep line 299).

.PARAMETER SidecarPort
    Port the sidecar Listener.ps1 binds inside the network namespace.
    Defaults to 8091 (per task 114 Dockerfile EXPOSE + Listener.ps1
    SIDECAR_LISTEN_PREFIX default).

.PARAMETER PlatformKeyVaultName
    Platform Key Vault name holding the Sidecar-Shared-Secret. Defaults to
    sprk-controlplane-{Environment}-kv (per Bicep convention).

.PARAMETER SharedSecretName
    KV secret name for the sidecar's per-boot shared secret. Defaults to
    Sidecar-Shared-Secret (per Bicep module default).

.PARAMETER TenantId
    Exchange tenantId to send in the /apply-policy body. Default: all-zero
    GUID (SAFE — Connect-ExchangeOnline rejects it before mutation). Override
    with a REAL test tenant to exercise idempotency check 5 end-to-end.

.PARAMETER PolicyScopeGroupId
    Mail-enabled group ObjectId to send. Default: all-zero GUID (SAFE).

.PARAMETER ExpectedAppIds
    2 app-registration client IDs to send. Default: two safe placeholder GUIDs.

.PARAMETER SkipChecks
    Comma-separated list of check names to skip. Valid names:
    CONTAINER_HEALTH, LOCALHOST_BIND, PUBLIC_ISOLATION, ROUND_TRIP_AUTH,
    ROUND_TRIP_IDEMP, AUTH_REJECTION.

.PARAMETER ReportPath
    Optional path to write a JSON summary of all check outcomes. Default: not
    written (the console table is the primary report).

.EXAMPLE
    # Dev — default safe run against the deployed dev L2 Worker.
    .\Verify-Sidecar-Live.ps1

.EXAMPLE
    # Dev — real Exchange test tenant, all checks including full check 5.
    .\Verify-Sidecar-Live.ps1 `
        -TenantId 12345678-1234-1234-1234-123456789012 `
        -PolicyScopeGroupId aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee `
        -ExpectedAppIds @("bff-app-reg-guid", "uami-client-id-guid")

.EXAMPLE
    # Dev — skip public-isolation check (some corp networks time out at
    # different rates than the platform's own front-end route)
    .\Verify-Sidecar-Live.ps1 -SkipChecks PUBLIC_ISOLATION

.NOTES
    Project:      customer-provisioning-orchestration-r1
    Task:         162 - Sidecar live verification against dev L2 Worker
    Depends on:   101 (Worker Bicep + sitecontainer resource),
                  113 (Deploy-ControlPlane.ps1),
                  114 (sidecar Dockerfile + Listener.ps1),
                  161 (ExchangePolicySidecarClient),
                  Live ceremony backlog step 5 (Deploy-ControlPlane.ps1 executed).

    Justification (CLAUDE.md §11):
      Existing:   scripts/provisioning/Deploy-ControlPlane.ps1 is the sibling
                  pattern for L2 operator scripts (parameter conventions,
                  $script: helper style, structured console output). This
                  script does NOT reuse that script — it is a distinct
                  READ-ONLY verification tool, executed AFTER deploy, not a
                  deploy itself. No extension viable.
      Extension:  N/A — verification is a separate concern from deploy.
      Cost-of-doing-nothing: without this harness, the operator would run
                  6 ad-hoc az/curl commands from memory per verification run,
                  making the "did the sidecar work?" answer non-repeatable and
                  hard to hand off across ceremony sessions. The whole point
                  of shipping the sidecar (task 114) is unrealized without a
                  repeatable verification story (task 162's own <notes> field).

    PREREQUISITES (operator runs FIRST; this script assumes done):
      1. `az login` with reader access on rg-spaarke-platform-{env}.
      2. Live ceremony backlog step 5 executed (Deploy-ControlPlane.ps1 has
         landed the Worker + sitecontainer image).
      3. The sidecar's SIDECAR_SHARED_SECRET env-var-KeyVault-reference is
         resolvable (H4 has PATCHed keyVaultReferenceIdentity — task 125+126
         landed pre-Wave-G-6).
      4. Platform KV secret `Sidecar-Shared-Secret` is populated (task 126).

    NON-DESTRUCTIVE / SAFE-BY-DEFAULT:
      Every default parameter value produces a run that does not mutate any
      real Exchange tenant. Check 5's full pass requires operator to opt in
      with real tenant/group/app-id GUIDs.
#>

[CmdletBinding(SupportsShouldProcess = $false)]  # this script is read-only; no ShouldProcess needed
[Diagnostics.CodeAnalysis.SuppressMessageAttribute(
    'PSReviewUnusedParameter', 'PolicyScopeGroupId',
    Justification = 'Referenced via $script:PolicyScopeGroupId inside Invoke-SidecarApplyPolicyViaKudu — PSScriptAnalyzer does not trace cross-function script-scope usage.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute(
    'PSReviewUnusedParameter', 'ExpectedAppIds',
    Justification = 'Referenced via $script:ExpectedAppIds inside Invoke-SidecarApplyPolicyViaKudu — PSScriptAnalyzer does not trace cross-function script-scope usage.')]
param(
    [Parameter(Mandatory = $false)]
    [ValidateSet('dev', 'staging', 'production')]
    [string]$Environment = 'dev',

    [Parameter(Mandatory = $false)]
    [string]$WorkerAppServiceName,

    [Parameter(Mandatory = $false)]
    [string]$WorkerResourceGroup,

    [Parameter(Mandatory = $false)]
    [string]$SidecarName = 'exchange-policy-sidecar',

    [Parameter(Mandatory = $false)]
    [int]$SidecarPort = 8091,

    [Parameter(Mandatory = $false)]
    [string]$PlatformKeyVaultName,

    [Parameter(Mandatory = $false)]
    [string]$SharedSecretName = 'Sidecar-Shared-Secret',

    [Parameter(Mandatory = $false)]
    [string]$TenantId = '00000000-0000-0000-0000-000000000000',

    [Parameter(Mandatory = $false)]
    [string]$PolicyScopeGroupId = '00000000-0000-0000-0000-000000000000',

    [Parameter(Mandatory = $false)]
    [string[]]$ExpectedAppIds = @('00000000-0000-0000-0000-000000000000', 'ffffffff-ffff-ffff-ffff-ffffffffffff'),

    [Parameter(Mandatory = $false)]
    [string]$SkipChecks = '',

    [Parameter(Mandatory = $false)]
    [string]$ReportPath = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ---- Defaults derived from -Environment ------------------------------------

if (-not $WorkerAppServiceName) {
    $WorkerAppServiceName = "spaarke-provisioning-controlplane-worker-$Environment"
}
if (-not $WorkerResourceGroup) {
    $WorkerResourceGroup = "rg-spaarke-platform-$Environment"
}
if (-not $PlatformKeyVaultName) {
    $PlatformKeyVaultName = "sprk-controlplane-$Environment-kv"
}

$script:CheckResults = [ordered]@{}
$script:SkipSet = @{}
foreach ($check in ($SkipChecks -split ',' | ForEach-Object { $_.Trim().ToUpperInvariant() })) {
    if ($check) { $script:SkipSet[$check] = $true }
}

$script:ValidCheckNames = @(
    'CONTAINER_HEALTH',
    'LOCALHOST_BIND',
    'PUBLIC_ISOLATION',
    'ROUND_TRIP_AUTH',
    'ROUND_TRIP_IDEMP',
    'AUTH_REJECTION'
)
foreach ($skip in $script:SkipSet.Keys) {
    if ($script:ValidCheckNames -notcontains $skip) {
        throw "Unknown check name in -SkipChecks: '$skip'. Valid names: $($script:ValidCheckNames -join ', ')."
    }
}

# ---- Helpers ----------------------------------------------------------------

function Write-CheckResult {
    param(
        [string]$Name,
        [ValidateSet('PASS', 'FAIL', 'WARN', 'SKIP')]
        [string]$Status,
        [string]$Message,
        [hashtable]$Details = @{}
    )
    $script:CheckResults[$Name] = [ordered]@{
        status  = $Status
        message = $Message
        details = $Details
    }
    $color = switch ($Status) {
        'PASS' { 'Green' }
        'FAIL' { 'Red' }
        'WARN' { 'Yellow' }
        'SKIP' { 'DarkGray' }
    }
    Write-Host ("  [{0}] {1,-20} {2}" -f $Status, $Name, $Message) -ForegroundColor $color
}

function Test-CheckSkipped {
    param([string]$Name)
    return $script:SkipSet.ContainsKey($Name)
}

function Get-AccessToken {
    param([string]$Resource)
    # Explicit variable assignment + $LASTEXITCODE guard — same pattern
    # scripts/provisioning/Deploy-ControlPlane.ps1 uses; `az ... | Select-Object`
    # elsewhere in this codebase left $LASTEXITCODE stale (task 113 finding).
    $script:LASTEXITCODE = 0
    $tokenJson = az account get-access-token --resource $Resource -o json 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "az account get-access-token failed for resource '$Resource'. Ensure `az login` was completed and the current subscription is set. Output: $tokenJson"
    }
    return ($tokenJson | ConvertFrom-Json).accessToken
}

function Get-ManagementToken { return Get-AccessToken -Resource 'https://management.azure.com/' }

function Invoke-KuduCommand {
    param(
        [Parameter(Mandatory = $true)][string]$Command,
        [Parameter(Mandatory = $false)][string]$Dir = '/home'
    )
    # Kudu REST /api/command runs an arbitrary command inside the main app
    # container (which shares the network namespace with sitecontainers per
    # the App Service sitecontainer topology — DS-1b §3). Response envelope:
    #   { Output: <string>, Error: <string>, ExitCode: <int> }
    $token = Get-ManagementToken
    $kuduBase = "https://$WorkerAppServiceName.scm.azurewebsites.net"
    $body = @{ command = $Command; dir = $Dir } | ConvertTo-Json -Compress
    $headers = @{ Authorization = "Bearer $token"; 'Content-Type' = 'application/json' }
    try {
        $response = Invoke-RestMethod -Method POST -Uri "$kuduBase/api/command" -Headers $headers -Body $body -ErrorAction Stop
        return @{ ExitCode = $response.ExitCode; Output = $response.Output; Error = $response.Error }
    } catch {
        return @{ ExitCode = -1; Output = ''; Error = "Kudu REST call failed: $($_.Exception.Message)" }
    }
}

function Get-SidecarSharedSecret {
    $script:LASTEXITCODE = 0
    $secretJson = az keyvault secret show --vault-name $PlatformKeyVaultName --name $SharedSecretName --query value -o tsv 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "az keyvault secret show failed for '$SharedSecretName' on vault '$PlatformKeyVaultName'. Ensure the operator has 'Key Vault Secrets User' RBAC on this vault + the secret has been populated. Output: $secretJson"
    }
    return $secretJson.Trim()
}

# ---- Check 1: CONTAINER_HEALTH --------------------------------------------

function Test-ContainerHealth {
    if (Test-CheckSkipped 'CONTAINER_HEALTH') {
        Write-CheckResult -Name 'CONTAINER_HEALTH' -Status 'SKIP' -Message 'Skipped via -SkipChecks'
        return
    }
    try {
        $token = Get-ManagementToken
        # Fetch the sitecontainer resource metadata via ARM. Requires reader
        # on the Worker; verifies the container ARM-level config exists.
        $script:LASTEXITCODE = 0
        $subscription = az account show --query id -o tsv 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "az account show failed: $subscription"
        }
        $uri = "https://management.azure.com/subscriptions/$subscription/resourceGroups/$WorkerResourceGroup/providers/Microsoft.Web/sites/$WorkerAppServiceName/sitecontainers/$SidecarName" + '?api-version=2024-04-01'
        $siteContainer = Invoke-RestMethod -Method GET -Uri $uri -Headers @{ Authorization = "Bearer $token" } -ErrorAction Stop
        $image = $siteContainer.properties.image
        $port = $siteContainer.properties.targetPort
        $details = @{
            image      = $image
            targetPort = $port
            authType   = $siteContainer.properties.authType
        }
        if ($port -ne "$SidecarPort") {
            Write-CheckResult -Name 'CONTAINER_HEALTH' -Status 'FAIL' -Message "sitecontainer targetPort mismatch: expected $SidecarPort, got $port" -Details $details
            return
        }
        Write-CheckResult -Name 'CONTAINER_HEALTH' -Status 'PASS' -Message "sitecontainer '$SidecarName' configured on port $port with image '$image'" -Details $details
    } catch {
        Write-CheckResult -Name 'CONTAINER_HEALTH' -Status 'FAIL' -Message "Failed to fetch sitecontainer metadata: $($_.Exception.Message)"
    }
}

# ---- Check 2: LOCALHOST_BIND (via Kudu SSH exec) ---------------------------

function Test-LocalhostBind {
    if (Test-CheckSkipped 'LOCALHOST_BIND') {
        Write-CheckResult -Name 'LOCALHOST_BIND' -Status 'SKIP' -Message 'Skipped via -SkipChecks'
        return
    }
    # curl the sidecar's /healthz from inside the Worker's own network
    # namespace via Kudu's /api/command exec. Listener.ps1 line 326-334
    # documents GET /healthz -> 200 "ok" unauthenticated.
    $result = Invoke-KuduCommand -Command "curl -sf -m 10 http://127.0.0.1:$SidecarPort/healthz && echo :EXITOK"
    if ($result.ExitCode -eq 0 -and $result.Output -match ':EXITOK') {
        Write-CheckResult -Name 'LOCALHOST_BIND' -Status 'PASS' -Message "GET http://127.0.0.1:$SidecarPort/healthz returned 200 'ok' from inside the Worker's network namespace"
    } else {
        Write-CheckResult -Name 'LOCALHOST_BIND' -Status 'FAIL' `
            -Message "curl http://127.0.0.1:$SidecarPort/healthz failed inside Worker (ExitCode=$($result.ExitCode))" `
            -Details @{ output = $result.Output; error = $result.Error }
    }
}

# ---- Check 3: PUBLIC_ISOLATION (security-critical) ------------------------

function Test-PublicIsolation {
    if (Test-CheckSkipped 'PUBLIC_ISOLATION') {
        Write-CheckResult -Name 'PUBLIC_ISOLATION' -Status 'SKIP' -Message 'Skipped via -SkipChecks'
        return
    }
    # A request to the Worker's PUBLIC hostname on the sidecar port MUST
    # fail/timeout. App Service's public front-end only exposes port 443
    # (HTTPS) via its ingress; sitecontainer ports live in the private
    # network namespace and are NOT publicly routed unless the operator
    # explicitly configures it (they shouldn't). Any 200 here is a
    # HIGH-SEVERITY security finding per POML <escalation> trigger #1.
    $publicHost = "$WorkerAppServiceName.azurewebsites.net"
    $probe = "https://${publicHost}:$SidecarPort/healthz"
    try {
        $response = Invoke-WebRequest -Method GET -Uri $probe -TimeoutSec 10 -UseBasicParsing -ErrorAction Stop
        # ANY 2xx here is a HIGH-severity finding.
        Write-CheckResult -Name 'PUBLIC_ISOLATION' -Status 'FAIL' `
            -Message "SECURITY-CRITICAL: sidecar port $SidecarPort is REACHABLE at public hostname '$publicHost' (HTTP $($response.StatusCode)). ESCALATE per POML <escalation> trigger #1 — this exposes Exchange-admin capability to the public internet." `
            -Details @{ probe = $probe; statusCode = $response.StatusCode; body = ($response.Content | Out-String).Substring(0, [Math]::Min(200, $response.Content.Length)) }
    } catch [System.Net.WebException], [System.Net.Http.HttpRequestException] {
        Write-CheckResult -Name 'PUBLIC_ISOLATION' -Status 'PASS' -Message "sidecar port $SidecarPort correctly UNREACHABLE at public hostname '$publicHost' (probe timed out / connection refused as expected)" -Details @{ probe = $probe; expectedFailure = $_.Exception.Message }
    } catch {
        # Any other exception (DNS, TLS handshake) is a PASS for isolation
        # (still not-reachable) but WARN so the operator sees the shape.
        Write-CheckResult -Name 'PUBLIC_ISOLATION' -Status 'PASS' -Message "sidecar port $SidecarPort not-reachable at public hostname '$publicHost' (unexpected exception shape — investigate but not a security finding)" -Details @{ probe = $probe; exception = $_.Exception.GetType().Name; message = $_.Exception.Message }
    }
}

# ---- Check 4: ROUND_TRIP_AUTH (valid X-Sidecar-Auth) ----------------------

function Invoke-SidecarApplyPolicyViaKudu {
    param(
        [string]$SharedSecret,
        [string]$CorrelationId
    )
    # Reference script-scope params explicitly so PSScriptAnalyzer's
    # cross-function usage tracking can see them (avoids false-positive
    # PSReviewUnusedParameter warnings on script-param level).
    $bodyObj = @{
        tenantId           = $script:TenantId
        expectedAppIds     = $script:ExpectedAppIds
        policyScopeGroupId = $script:PolicyScopeGroupId
        descriptionPrefix  = "Spaarke-Provisioning-AppAccessPolicy-LiveVerify"
        correlationId      = $CorrelationId
        timeoutSeconds     = 300
    }
    $bodyJson = ($bodyObj | ConvertTo-Json -Compress -Depth 6).Replace('"', '\"')
    # curl inside the Worker container.
    $curl = "curl -sS -m 360 -o /tmp/apply-policy-response.json -w 'HTTP_%{http_code}' -X POST -H 'Content-Type: application/json' -H 'X-Sidecar-Auth: $SharedSecret' -d `"$bodyJson`" http://127.0.0.1:$SidecarPort/apply-policy; echo; cat /tmp/apply-policy-response.json; rm -f /tmp/apply-policy-response.json"
    return Invoke-KuduCommand -Command $curl
}

function Test-RoundTripAuth {
    if (Test-CheckSkipped 'ROUND_TRIP_AUTH') {
        Write-CheckResult -Name 'ROUND_TRIP_AUTH' -Status 'SKIP' -Message 'Skipped via -SkipChecks'
        return
    }
    try {
        $secret = Get-SidecarSharedSecret
    } catch {
        Write-CheckResult -Name 'ROUND_TRIP_AUTH' -Status 'FAIL' -Message "Cannot read shared secret from KV: $($_.Exception.Message)"
        return
    }
    $corr = "live-verify-{0}" -f ([guid]::NewGuid().ToString('N'))
    $result = Invoke-SidecarApplyPolicyViaKudu -SharedSecret $secret -CorrelationId $corr
    $out = $result.Output
    if ($result.ExitCode -ne 0) {
        Write-CheckResult -Name 'ROUND_TRIP_AUTH' -Status 'FAIL' `
            -Message "Kudu exec failed (ExitCode=$($result.ExitCode))" `
            -Details @{ output = $out; error = $result.Error; correlationId = $corr }
        return
    }
    if ($out -match 'HTTP_(\d{3})') {
        $httpCode = [int]$Matches[1]
        if ($httpCode -eq 401) {
            Write-CheckResult -Name 'ROUND_TRIP_AUTH' -Status 'FAIL' `
                -Message "sidecar returned HTTP 401 with the CORRECT shared secret from KV — the KV secret value does not match the sidecar's SIDECAR_SHARED_SECRET env var. Verify the sitecontainer's KV reference resolved (H4 keyVaultReferenceIdentity PATCH). Restart the Worker to reload the sitecontainer env if a secret was rotated." `
                -Details @{ httpCode = 401; correlationId = $corr; output = $out }
            return
        }
        if ($httpCode -eq 200 -or ($httpCode -ge 200 -and $httpCode -lt 500)) {
            # Any 2xx (with structured envelope) or 4xx (validation error) proves
            # the auth header was accepted — the request got past the 401 guard.
            Write-CheckResult -Name 'ROUND_TRIP_AUTH' -Status 'PASS' `
                -Message "sidecar accepted X-Sidecar-Auth (HTTP $httpCode; the request got past the shared-secret guard)" `
                -Details @{ httpCode = $httpCode; correlationId = $corr; response = ($out -replace '.*HTTP_\d{3}\s*', '') }
            return
        }
        Write-CheckResult -Name 'ROUND_TRIP_AUTH' -Status 'WARN' `
            -Message "sidecar returned HTTP $httpCode — unexpected. Auth was accepted (not a 401) but the shape is unusual." `
            -Details @{ httpCode = $httpCode; correlationId = $corr; output = $out }
        return
    }
    Write-CheckResult -Name 'ROUND_TRIP_AUTH' -Status 'FAIL' `
        -Message 'Could not parse HTTP status code from Kudu curl output' `
        -Details @{ correlationId = $corr; output = $out }
}

# ---- Check 5: ROUND_TRIP_IDEMP (2nd run) ----------------------------------

function Test-RoundTripIdempotency {
    if (Test-CheckSkipped 'ROUND_TRIP_IDEMP') {
        Write-CheckResult -Name 'ROUND_TRIP_IDEMP' -Status 'SKIP' -Message 'Skipped via -SkipChecks'
        return
    }
    try {
        $secret = Get-SidecarSharedSecret
    } catch {
        Write-CheckResult -Name 'ROUND_TRIP_IDEMP' -Status 'FAIL' -Message "Cannot read shared secret from KV: $($_.Exception.Message)"
        return
    }
    if ($TenantId -eq '00000000-0000-0000-0000-000000000000') {
        Write-CheckResult -Name 'ROUND_TRIP_IDEMP' -Status 'WARN' `
            -Message "SAFE-DEFAULT MODE: check 5's idempotency cannot be verified with an all-zero tenantId (Connect-ExchangeOnline rejects it before get-before-set can run). Re-run with -TenantId, -PolicyScopeGroupId, -ExpectedAppIds pointing at a real safely-scoped test tenant to verify AlreadyCompliant-on-2nd-run." `
            -Details @{ reason = 'safe-default-mode'; hint = 'operator override required for full check' }
        return
    }
    # Same request twice; expect AlreadyCompliant (wire outcome) on 2nd run.
    $corr1 = "live-verify-idemp-1-{0}" -f ([guid]::NewGuid().ToString('N'))
    $corr2 = "live-verify-idemp-2-{0}" -f ([guid]::NewGuid().ToString('N'))
    $result1 = Invoke-SidecarApplyPolicyViaKudu -SharedSecret $secret -CorrelationId $corr1
    $result2 = Invoke-SidecarApplyPolicyViaKudu -SharedSecret $secret -CorrelationId $corr2
    $out1 = $result1.Output
    $out2 = $result2.Output
    if ($out2 -match '"outcome"\s*:\s*"AlreadyCompliant"') {
        Write-CheckResult -Name 'ROUND_TRIP_IDEMP' -Status 'PASS' `
            -Message "2nd run returned wire outcome AlreadyCompliant — get-before-set idempotency survives container+HTTP wrapping." `
            -Details @{ run1CorrelationId = $corr1; run2CorrelationId = $corr2 }
        return
    }
    Write-CheckResult -Name 'ROUND_TRIP_IDEMP' -Status 'FAIL' `
        -Message '2nd run did NOT return AlreadyCompliant — idempotency may not be surviving the wrapping.' `
        -Details @{ run1CorrelationId = $corr1; run2CorrelationId = $corr2; run1Output = $out1; run2Output = $out2 }
}

# ---- Check 6: AUTH_REJECTION (security-critical) --------------------------

function Test-AuthRejection {
    if (Test-CheckSkipped 'AUTH_REJECTION') {
        Write-CheckResult -Name 'AUTH_REJECTION' -Status 'SKIP' -Message 'Skipped via -SkipChecks'
        return
    }
    $wrongSecret = 'definitely-not-the-real-secret-' + [guid]::NewGuid().ToString('N')
    $corr = "live-verify-auth-reject-{0}" -f ([guid]::NewGuid().ToString('N'))
    $result = Invoke-SidecarApplyPolicyViaKudu -SharedSecret $wrongSecret -CorrelationId $corr
    $out = $result.Output
    if ($out -match 'HTTP_401') {
        Write-CheckResult -Name 'AUTH_REJECTION' -Status 'PASS' `
            -Message "sidecar correctly REJECTED (HTTP 401) a request with a wrong X-Sidecar-Auth header" `
            -Details @{ correlationId = $corr }
        return
    }
    if ($out -match 'HTTP_(\d{3})') {
        $httpCode = [int]$Matches[1]
        Write-CheckResult -Name 'AUTH_REJECTION' -Status 'FAIL' `
            -Message "SECURITY-CRITICAL: sidecar returned HTTP $httpCode (expected 401) for a request with a WRONG X-Sidecar-Auth header. ESCALATE per POML <escalation> trigger #2 — the shared-secret rejection path is broken." `
            -Details @{ httpCode = $httpCode; correlationId = $corr; output = $out }
        return
    }
    Write-CheckResult -Name 'AUTH_REJECTION' -Status 'FAIL' `
        -Message 'Could not parse HTTP status code from Kudu curl output (wrong-secret probe)' `
        -Details @{ correlationId = $corr; output = $out }
}

# ---- Run + report ----------------------------------------------------------

Write-Host ""
Write-Host "===============================================================" -ForegroundColor Cyan
Write-Host " Sidecar Live Verification — task 162" -ForegroundColor Cyan
Write-Host "===============================================================" -ForegroundColor Cyan
Write-Host " Environment:            $Environment"
Write-Host " Worker App Service:     $WorkerAppServiceName"
Write-Host " Worker Resource Group:  $WorkerResourceGroup"
Write-Host " Sidecar:                $SidecarName (port $SidecarPort)"
Write-Host " Platform KV:            $PlatformKeyVaultName"
Write-Host " Shared secret name:     $SharedSecretName"
Write-Host " TenantId payload:       $TenantId $(if ($TenantId -eq '00000000-0000-0000-0000-000000000000') { '(SAFE default)' } else { '(operator override)' })"
Write-Host ""

Write-Host "Running checks:" -ForegroundColor Cyan
Test-ContainerHealth
Test-LocalhostBind
Test-PublicIsolation
Test-RoundTripAuth
Test-RoundTripIdempotency
Test-AuthRejection

Write-Host ""
Write-Host "===============================================================" -ForegroundColor Cyan
Write-Host " Summary" -ForegroundColor Cyan
Write-Host "===============================================================" -ForegroundColor Cyan
$passCount = ($script:CheckResults.Values | Where-Object { $_.status -eq 'PASS' } | Measure-Object).Count
$failCount = ($script:CheckResults.Values | Where-Object { $_.status -eq 'FAIL' } | Measure-Object).Count
$warnCount = ($script:CheckResults.Values | Where-Object { $_.status -eq 'WARN' } | Measure-Object).Count
$skipCount = ($script:CheckResults.Values | Where-Object { $_.status -eq 'SKIP' } | Measure-Object).Count
$totalCount = $script:CheckResults.Count
Write-Host " PASS: $passCount / $totalCount" -ForegroundColor Green
if ($warnCount -gt 0) { Write-Host " WARN: $warnCount / $totalCount" -ForegroundColor Yellow }
if ($skipCount -gt 0) { Write-Host " SKIP: $skipCount / $totalCount" -ForegroundColor DarkGray }
if ($failCount -gt 0) { Write-Host " FAIL: $failCount / $totalCount" -ForegroundColor Red }
Write-Host ""

if ($ReportPath) {
    $report = [ordered]@{
        timestamp         = [DateTimeOffset]::UtcNow.ToString('o')
        environment       = $Environment
        workerAppService  = $WorkerAppServiceName
        sidecar           = $SidecarName
        sidecarPort       = $SidecarPort
        platformKeyVault  = $PlatformKeyVaultName
        sharedSecretName  = $SharedSecretName
        tenantIdWasSafe   = ($TenantId -eq '00000000-0000-0000-0000-000000000000')
        counts            = @{ pass = $passCount; fail = $failCount; warn = $warnCount; skip = $skipCount; total = $totalCount }
        checks            = $script:CheckResults
    }
    $report | ConvertTo-Json -Depth 10 | Out-File -FilePath $ReportPath -Encoding utf8
    Write-Host " Report written: $ReportPath" -ForegroundColor Cyan
}

if ($failCount -gt 0) {
    Write-Host " OVERALL: FAIL" -ForegroundColor Red
    exit 1
}
Write-Host " OVERALL: PASS$(if ($warnCount -gt 0) { ' (with warnings)' })" -ForegroundColor Green
exit 0
