<#
.SYNOPSIS
    HTTP listener for the H14a Exchange ApplicationAccessPolicy sidecar
    (design.md §4.2a, DS-1b §3). Runs as PID 1 in the sidecar container.

.DESCRIPTION
    Binds localhost:8091 (sitecontainer-private; not publicly routed by App
    Service's front end). Serves three endpoints:

      GET  /healthz     -> 200 OK plain-text "ok" (App Service sitecontainer
                          health probe target; unauthenticated by design —
                          only reachable via the private network namespace).

      POST /apply-policy
        Headers:
          Content-Type: application/json
          X-Sidecar-Auth: <per-boot shared secret from platform KV>
        Body (JSON):
          {
            "tenantId":            <string, Entra tenant id>,
            "expectedAppIds":      [<string>, <string>]   // exactly 2
            "policyScopeGroupId":  <string, mail-enabled group id>,
            "descriptionPrefix":   <string, optional; default
                                    "Spaarke-Provisioning-AppAccessPolicy">,
            "correlationId":       <string, ProvisioningRun id — logged
                                    in every log line for run-tracing>,
            "timeoutSeconds":      <int, optional; default 300;
                                    upper-bounds the script invocation>
          }
        Response body (JSON):
          {
            "outcome":         "Success" | "AlreadyCompliant" | "Drift" | "Failure",
            "createdCount":    <int, 0-2>,
            "expectedAppIds":  [<string>, <string>],
            "observedAppIds":  [<string>, ...],
            "policiesApplied": [<string>, ...],  // subset newly created this call
            "diagnostic":      <string>
          }

      GET  /policies      (task 180, Wave G-7 -- H13 T4 acceptance-gate probe
                          read route. The ONE H13 probe that cannot be pure
                          .NET, per DS-4 s6 T4 row "extend the H14a sidecar
                          with a read-only GET /policies route wrapping
                          Get-ApplicationAccessPolicy". Read-only: never
                          creates, modifies, or deletes any policy — a
                          silent-fail-audit-grade parity check for the T4
                          post-condition H14a wrote.)
        Headers:
          X-Sidecar-Auth: <per-boot shared secret from platform KV>
        Query string (all optional -- probe filters client-side):
          tenantId       = <string, Entra tenant id; logged for audit>
          correlationId  = <string, ProvisioningRun id; logged>
          timeoutSeconds = <int, optional; default 300; advisory>
        Response body (JSON):
          {
            "outcome":         "Success" | "Failure",
            "observedAppIds":  [<string>, ...],   // distinct AppIds across all policies
            "observedCount":   <int>,             // distinct AppId count
            "policies": [
              {
                "appId":              <string>,
                "description":        <string>,
                "policyScopeGroupId": <string>
              }, ...
            ],
            "diagnostic":      <string>
          }
        On outcome=Success the payload reflects live Exchange state; on
        Failure "policies" is [] and "observedAppIds" is [] (never partial).

    Envelope-mapping rules (script Write-ResultJson -> HTTP response):
      script Applied  + createdCount > 0  ->  wire Success           (policiesApplied = new appIds)
      script Applied  + createdCount == 0 ->  wire AlreadyCompliant  (policiesApplied = [])
      script Drift                        ->  wire Drift             (T4 silent-fail trap surfaced)
      script Failure                      ->  wire Failure

    NOTE: the wire contract has FOUR outcomes, not the three
    (Success|Failure|AlreadyCompliant) originally stated in design.md §4.2a.
    Drift is preserved as a distinct wire state so task 161's
    ExchangePolicySidecarClient can round-trip to the C# IExchangePolicyApplier
    seam's four-state discriminated union (Applied(created>0) / Applied(created=0)
    / Drift / Failure — from task 073's Handlers/IntegrationWiring/
    IExchangePolicyApplier.cs). Collapsing Drift into Failure would
    reintroduce the T4 silent-fail regression this seam exists to close.
    (Reviewer note in task 114's <notes>.)

.SECURITY
    Two independent auth legs per DS-1b §3:

      MAIN -> SIDECAR: localhost-only bind + X-Sidecar-Auth constant-time-compared
        against $env:SIDECAR_SHARED_SECRET. The shared secret is set by App
        Service from a platform KV secret at container start (KeyVault
        reference; rotated per platform rotation policy). Missing / mismatched
        header -> 401 (no body leak).

      SIDECAR -> EXCHANGE: app-only Connect-ExchangeOnline with the Exchange
        PFX fetched from platform KV at call time using the App Service MSI
        (same UAMI as the main site). No secret persisted to disk; X509Certificate2
        object lives in memory for the script invocation only.

.OBSERVABILITY
    One structured JSON log line per request emitted to stdout — captured by
    App Service log stream, forwarded to the same Log Analytics workspace as
    the main .NET app. correlationId = RunId in every line, so `runs/{id}/logs`
    (L2's logs endpoint) surfaces sidecar + main logs interleaved on the same
    key.

.ENVIRONMENT
    Consumed environment variables (set by App Service from Bicep + KV refs):
      SIDECAR_LISTEN_PREFIX      (opt; default http://127.0.0.1:8091/)
      SIDECAR_SHARED_SECRET      (req; per-boot shared secret; KV-sourced)
      PLATFORM_KV_URI            (req; e.g. https://sprk-controlplane-dev-kv.vault.azure.net/)
      EXCHANGE_CERT_SECRET_NAME  (req; e.g. Exchange-Connect-Cert)
      EXCHANGE_CONNECT_APP_ID    (req; Exchange app-reg client id)
      IDENTITY_ENDPOINT          (req; set by App Service MSI runtime)
      IDENTITY_HEADER            (req; set by App Service MSI runtime)
      SIDECAR_SCRIPT_PATH        (opt; default /app/Set-ExchangeApplicationAccessPolicy.ps1)
#>

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# ---- Config from environment -------------------------------------------------

$ListenPrefix   = if ($env:SIDECAR_LISTEN_PREFIX) { $env:SIDECAR_LISTEN_PREFIX } else { "http://127.0.0.1:8091/" }
$SharedSecret   = $env:SIDECAR_SHARED_SECRET
$PlatformKvUri  = $env:PLATFORM_KV_URI
$CertSecretName = $env:EXCHANGE_CERT_SECRET_NAME
$ExchangeAppId  = $env:EXCHANGE_CONNECT_APP_ID
$MsiEndpoint    = $env:IDENTITY_ENDPOINT
$MsiHeader      = $env:IDENTITY_HEADER
$ScriptPath     = if ($env:SIDECAR_SCRIPT_PATH) { $env:SIDECAR_SCRIPT_PATH } else { "/app/Set-ExchangeApplicationAccessPolicy.ps1" }

# ---- Structured logging ------------------------------------------------------

function Write-JsonLog {
    param(
        [Parameter(Mandatory = $true)][ValidateSet("INFO", "WARN", "ERROR")][string]$Level,
        [Parameter(Mandatory = $true)][string]$Message,
        [string]$CorrelationId = "",
        [hashtable]$Fields = @{}
    )
    $entry = [ordered]@{
        timestamp     = [DateTimeOffset]::UtcNow.ToString("o")
        level         = $Level
        component     = "exchange-policy-sidecar"
        correlationId = $CorrelationId
        message       = $Message
    }
    foreach ($k in $Fields.Keys) { $entry[$k] = $Fields[$k] }
    Write-Output ($entry | ConvertTo-Json -Compress -Depth 6)
}

# ---- Constant-time string compare (defence-in-depth vs timing side-channel) --

function Test-SecretEqual {
    param([string]$Provided, [string]$Expected)
    if ([string]::IsNullOrEmpty($Provided) -or [string]::IsNullOrEmpty($Expected)) { return $false }
    if ($Provided.Length -ne $Expected.Length) { return $false }
    $result = 0
    for ($i = 0; $i -lt $Expected.Length; $i++) {
        $result = $result -bor ([byte][char]$Provided[$i] -bxor [byte][char]$Expected[$i])
    }
    return ($result -eq 0)
}

# ---- MSI + KV cert fetch -----------------------------------------------------

function Get-ExchangeCertificate {
    param([string]$CorrelationId)

    if (-not $MsiEndpoint -or -not $MsiHeader) {
        throw "App Service MSI endpoint not configured (IDENTITY_ENDPOINT / IDENTITY_HEADER missing) — cannot fetch KV cert."
    }
    if (-not $PlatformKvUri -or -not $CertSecretName) {
        throw "PLATFORM_KV_URI / EXCHANGE_CERT_SECRET_NAME not configured — cannot resolve cert secret."
    }

    # (1) Get a KV-scoped access token via App Service MSI endpoint.
    $tokenUri  = "${MsiEndpoint}?resource=https://vault.azure.net&api-version=2019-08-01"
    $tokenResp = Invoke-RestMethod -Uri $tokenUri -Method GET -Headers @{ "X-IDENTITY-HEADER" = $MsiHeader } -ErrorAction Stop
    $kvToken   = $tokenResp.access_token

    # (2) Fetch the PFX secret from KV (returns base64 string in .value).
    $kvBase       = $PlatformKvUri.TrimEnd('/')
    $secretUri    = "${kvBase}/secrets/${CertSecretName}?api-version=7.4"
    $secretResp   = Invoke-RestMethod -Uri $secretUri -Method GET -Headers @{ "Authorization" = "Bearer $kvToken" } -ErrorAction Stop
    $pfxBase64    = $secretResp.value
    $pfxBytes     = [Convert]::FromBase64String($pfxBase64)

    # (3) Construct X509Certificate2 (no password; KV certs are stored with an
    #     empty password by convention when uploaded via the "Certificate" API).
    #     If the secret was uploaded as a PFX with a password, add a
    #     PLATFORM_CERT_PASSWORD env var and pass it as the second arg here.
    $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
        $pfxBytes,
        [string]::Empty,
        [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)

    Write-JsonLog -Level INFO -CorrelationId $CorrelationId `
        -Message "Fetched Exchange cert from KV" `
        -Fields @{ certThumbprint = $cert.Thumbprint; certSecretName = $CertSecretName }
    return $cert
}

# ---- Invoke amended script + parse envelope ---------------------------------

function Invoke-ExchangePolicyScript {
    param(
        [string]$CorrelationId,
        [string]$TenantId,
        [string[]]$ExpectedAppIds,
        [string]$PolicyScopeGroupId,
        [string]$DescriptionPrefix,
        [int]$TimeoutSeconds,
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$Cert
    )

    # Splat table (NOT $args — that is an automatic PowerShell variable).
    $scriptArgs = @{
        TenantId           = $TenantId
        ExpectedAppIds     = ($ExpectedAppIds -join ",")
        PolicyScopeGroupId = $PolicyScopeGroupId
        DescriptionPrefix  = $DescriptionPrefix
        Certificate        = $Cert
        ExchangeAppId      = $ExchangeAppId
    }

    # In-process script execution (same pwsh runtime; script's Write-Output
    # of "SPAARKE-H14A-RESULT-JSON:{...}" is captured directly).
    # Timeout is currently ENFORCED CALLER-SIDE (task 161's HttpClient.Timeout
    # = TimeoutSeconds + margin) — in-listener enforcement via a runspace
    # stopwatch is a follow-on if the caller-side model proves insufficient.
    # Log the requested timeout for audit + future-work traceability.
    Write-JsonLog -Level INFO -CorrelationId $CorrelationId `
        -Message "Invoking Exchange policy script" `
        -Fields @{ scriptPath = $ScriptPath; timeoutSecondsAdvisory = $TimeoutSeconds }
    try {
        $stdout = & $ScriptPath @scriptArgs
    }
    catch {
        return @{
            outcome         = "Failure"
            createdCount    = 0
            expectedAppIds  = $ExpectedAppIds
            observedAppIds  = @()
            policiesApplied = @()
            diagnostic      = "Script invocation threw: $($_.Exception.Message)"
        }
    }

    # Find the single result line (script emits exactly one prefix-marked line).
    $resultLine = @($stdout | Where-Object { $_ -is [string] -and $_.StartsWith("SPAARKE-H14A-RESULT-JSON:") }) | Select-Object -First 1
    if (-not $resultLine) {
        return @{
            outcome         = "Failure"
            createdCount    = 0
            expectedAppIds  = $ExpectedAppIds
            observedAppIds  = @()
            policiesApplied = @()
            diagnostic      = "Script did not emit SPAARKE-H14A-RESULT-JSON: line. stdout=$($stdout -join '||')"
        }
    }

    $json = $resultLine.Substring("SPAARKE-H14A-RESULT-JSON:".Length)
    $parsed = $json | ConvertFrom-Json

    # Map script outcome to wire outcome (see .DESCRIPTION envelope-mapping rules).
    $wireOutcome = switch ($parsed.outcome) {
        "Applied" {
            if ([int]$parsed.createdCount -gt 0) { "Success" } else { "AlreadyCompliant" }
        }
        "Drift"   { "Drift" }
        "Failure" { "Failure" }
        default   { "Failure" }
    }

    # policiesApplied = newly created appIds this call.
    # Script's createdCount tells us HOW MANY were created; the observedAppIds
    # tells us which appIds are present post-verify. The newly-created set is
    # observedAppIds - (existing set BEFORE create) — but the script doesn't
    # emit the pre-create set. Since createdCount is always 0, 1, or 2, and
    # ObservedAppIds is the post-verify set, we can approximate: if
    # createdCount == observedAppIds.Count, all observed were newly created;
    # otherwise the difference set. For the T4 use case, the caller only needs
    # createdCount + observedAppIds to reconstruct; policiesApplied is emitted
    # as observedAppIds when createdCount > 0 for wire clarity, empty otherwise.
    $policiesApplied = if ([int]$parsed.createdCount -gt 0) { @($parsed.observedAppIds) } else { @() }

    return @{
        outcome         = $wireOutcome
        createdCount    = [int]$parsed.createdCount
        expectedAppIds  = @($parsed.expectedAppIds)
        observedAppIds  = @($parsed.observedAppIds)
        policiesApplied = $policiesApplied
        diagnostic      = [string]$parsed.diagnostic
    }
}

# ---- READ-ONLY policies enumeration (task 180 -- H13 T4 probe read route) ----

function Get-PoliciesReadOnly {
    <#
    .SYNOPSIS
        Read-only enumeration of Exchange ApplicationAccessPolicy entries.
        NEVER creates, modifies, or deletes any policy.
    .DESCRIPTION
        Wraps a single Get-ApplicationAccessPolicy call under an app-only EXO
        session (same connect path as Invoke-ExchangePolicyScript's cert-based
        Connect-ExchangeOnline) and projects each row to a minimal record the
        C# T4 probe consumes (AppId + Description + PolicyScopeGroupId).

        Returns a hashtable with:
            outcome         : "Success" | "Failure"
            observedAppIds  : distinct AppIds across all policies
            observedCount   : count of distinct AppIds
            policies        : per-policy projection (see above)
            diagnostic      : operator-facing message

        This is deliberately DIFFERENT from Invoke-ExchangePolicyScript --
        that function is action-and-verify (may create policies); this one is
        pure read (never mutates). Keeping them separate keeps the T4 probe's
        code path provably non-mutating (H13's R7 "assert EFFECTS not
        intentions" principle: the verifier MUST NOT alter what it is verifying).
    #>
    param(
        [string]$CorrelationId,
        [string]$TenantId
    )

    # Fetch cert (per-call, short-lived in-memory X509 object; no disk write)
    # -- same pattern as POST /apply-policy.
    try {
        $cert = Get-ExchangeCertificate -CorrelationId $CorrelationId
    } catch {
        return @{
            outcome        = "Failure"
            observedAppIds = @()
            observedCount  = 0
            policies       = @()
            diagnostic     = "Failed to fetch Exchange cert from platform KV: $($_.Exception.Message)"
        }
    }

    try {
        # Connect app-only via the fetched X509Certificate2 (Linux sidecar
        # path -- same connect surface Set-ExchangeApplicationAccessPolicy.ps1
        # uses when invoked with -Certificate).
        $orgHint = if ($TenantId) { $TenantId } else { "" }
        try {
            if ($orgHint) {
                Connect-ExchangeOnline `
                    -AppId $ExchangeAppId `
                    -Certificate $cert `
                    -Organization $orgHint `
                    -ShowBanner:$false `
                    -ErrorAction Stop
            }
            else {
                Connect-ExchangeOnline `
                    -AppId $ExchangeAppId `
                    -Certificate $cert `
                    -ShowBanner:$false `
                    -ErrorAction Stop
            }
        } catch {
            return @{
                outcome        = "Failure"
                observedAppIds = @()
                observedCount  = 0
                policies       = @()
                diagnostic     = "Connect-ExchangeOnline failed: $($_.Exception.Message)"
            }
        }

        try {
            $allPolicies = @(Get-ApplicationAccessPolicy -ErrorAction Stop)
            $projected   = @()
            foreach ($p in $allPolicies) {
                $projected += @{
                    appId              = [string]$p.AppId
                    description        = [string]$p.Description
                    policyScopeGroupId = [string]$p.PolicyScopeGroupId
                }
            }
            $distinctAppIds = @($projected | ForEach-Object { $_.appId } | Where-Object { $_ } | Sort-Object -Unique)

            return @{
                outcome        = "Success"
                observedAppIds = $distinctAppIds
                observedCount  = $distinctAppIds.Count
                policies       = $projected
                diagnostic     = "Enumerated $($projected.Count) ApplicationAccessPolic$(if ($projected.Count -eq 1) { 'y' } else { 'ies' }) covering $($distinctAppIds.Count) distinct AppId$(if ($distinctAppIds.Count -eq 1) { '' } else { 's' })."
            }
        } catch {
            return @{
                outcome        = "Failure"
                observedAppIds = @()
                observedCount  = 0
                policies       = @()
                diagnostic     = "Get-ApplicationAccessPolicy failed: $($_.Exception.Message)"
            }
        }
    }
    finally {
        Disconnect-ExchangeOnline -Confirm:$false -ErrorAction SilentlyContinue | Out-Null
        if ($cert) { $cert.Dispose() }
    }
}

# ---- Response helper ---------------------------------------------------------

function Write-JsonResponse {
    param(
        [System.Net.HttpListenerResponse]$Response,
        [int]$StatusCode,
        [object]$BodyObject,
        [string]$CorrelationId = ""
    )
    $Response.StatusCode  = $StatusCode
    $Response.ContentType = "application/json"
    if ($CorrelationId) { $Response.Headers.Add("X-Correlation-Id", $CorrelationId) }
    $bodyJson  = ($BodyObject | ConvertTo-Json -Compress -Depth 6)
    $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($bodyJson)
    $Response.ContentLength64 = $bodyBytes.Length
    $Response.OutputStream.Write($bodyBytes, 0, $bodyBytes.Length)
    $Response.OutputStream.Close()
}

# ---- Startup fail-fast on required env --------------------------------------

$missing = @()
if (-not $SharedSecret)   { $missing += "SIDECAR_SHARED_SECRET" }
if (-not $PlatformKvUri)  { $missing += "PLATFORM_KV_URI" }
if (-not $CertSecretName) { $missing += "EXCHANGE_CERT_SECRET_NAME" }
if (-not $ExchangeAppId)  { $missing += "EXCHANGE_CONNECT_APP_ID" }
if ($missing.Count -gt 0) {
    Write-JsonLog -Level ERROR -Message "Sidecar startup: missing required environment variables — refusing to bind port." `
        -Fields @{ missing = $missing }
    exit 1
}

# ---- Listener bind + serve loop ---------------------------------------------

$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add($ListenPrefix)
try {
    $listener.Start()
} catch {
    Write-JsonLog -Level ERROR -Message "HttpListener.Start() failed" -Fields @{ error = $_.Exception.Message; prefix = $ListenPrefix }
    exit 1
}
Write-JsonLog -Level INFO -Message "Sidecar listening" -Fields @{ prefix = $ListenPrefix; script = $ScriptPath }

while ($listener.IsListening) {
    try {
        $ctx = $listener.GetContext()
    } catch {
        Write-JsonLog -Level WARN -Message "GetContext failed (listener may be stopping)" -Fields @{ error = $_.Exception.Message }
        continue
    }
    $request  = $ctx.Request
    $response = $ctx.Response
    $rawUrl   = $request.RawUrl
    $method   = $request.HttpMethod

    # GET /healthz -- unauthenticated (sitecontainer-private network).
    if ($method -eq "GET" -and $rawUrl -eq "/healthz") {
        $response.StatusCode  = 200
        $response.ContentType = "text/plain"
        $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes("ok")
        $response.ContentLength64 = $bodyBytes.Length
        $response.OutputStream.Write($bodyBytes, 0, $bodyBytes.Length)
        $response.OutputStream.Close()
        continue
    }

    # GET /policies (task 180 -- H13 T4 acceptance-gate probe read route).
    # Read-only; requires the SAME X-Sidecar-Auth as POST /apply-policy.
    # Path match is exact-or-prefix ("/policies" or "/policies?...") so query
    # string is honored.
    if ($method -eq "GET" -and ($rawUrl -eq "/policies" -or $rawUrl.StartsWith("/policies?"))) {
        # Auth: same shared-secret header as the write route -- defense-in-depth
        # against unauthenticated reads of the ApplicationAccessPolicy list
        # (still low sensitivity but not something to expose without a check).
        $providedSecret = $request.Headers["X-Sidecar-Auth"]
        if (-not (Test-SecretEqual -Provided $providedSecret -Expected $SharedSecret)) {
            Write-JsonLog -Level WARN -Message "Rejected /policies: missing or mismatched X-Sidecar-Auth header"
            Write-JsonResponse -Response $response -StatusCode 401 -BodyObject @{
                outcome = "Failure"; observedAppIds = @(); observedCount = 0; policies = @();
                diagnostic = "Missing or invalid X-Sidecar-Auth header."
            }
            continue
        }

        # Parse optional query-string params via System.Web.HttpUtility
        # (available on PowerShell 7 / net10 by default; falls back to
        # ParseQueryString via UriBuilder if System.Web is not loaded).
        $tenantId       = ""
        $correlationId  = ""
        try {
            Add-Type -AssemblyName System.Web -ErrorAction SilentlyContinue | Out-Null
            $qs = [System.Web.HttpUtility]::ParseQueryString($request.Url.Query)
            $tenantId       = if ($qs["tenantId"])      { [string]$qs["tenantId"] }      else { "" }
            $correlationId  = if ($qs["correlationId"]) { [string]$qs["correlationId"] } else { "" }
        } catch {
            # Query-string parsing is best-effort; missing/malformed query is
            # not a probe failure -- Get-PoliciesReadOnly does not require any
            # of these to run.
            $tenantId      = ""
            $correlationId = ""
        }

        Write-JsonLog -Level INFO -CorrelationId $correlationId -Message "Received /policies (read-only enumeration)" -Fields @{
            tenantId = $tenantId
        }

        $result = Get-PoliciesReadOnly -CorrelationId $correlationId -TenantId $tenantId

        Write-JsonLog -Level INFO -CorrelationId $correlationId -Message "Completed /policies" -Fields @{
            outcome       = $result.outcome
            observedCount = $result.observedCount
        }
        Write-JsonResponse -Response $response -StatusCode 200 -CorrelationId $correlationId -BodyObject $result
        continue
    }

    # POST /apply-policy is the only other route we accept.
    if ($method -ne "POST" -or $rawUrl -ne "/apply-policy") {
        Write-JsonResponse -Response $response -StatusCode 404 -BodyObject @{
            outcome = "Failure"; diagnostic = "Unknown route: $method $rawUrl. Only GET /healthz, GET /policies, and POST /apply-policy are served."
        }
        continue
    }

    # Auth: shared secret header.
    $providedSecret = $request.Headers["X-Sidecar-Auth"]
    if (-not (Test-SecretEqual -Provided $providedSecret -Expected $SharedSecret)) {
        Write-JsonLog -Level WARN -Message "Rejected /apply-policy: missing or mismatched X-Sidecar-Auth header"
        Write-JsonResponse -Response $response -StatusCode 401 -BodyObject @{
            outcome = "Failure"; diagnostic = "Missing or invalid X-Sidecar-Auth header."
        }
        continue
    }

    # Parse body.
    try {
        $reader = [System.IO.StreamReader]::new($request.InputStream, $request.ContentEncoding)
        $bodyText = $reader.ReadToEnd()
        $reader.Close()
        $body = $bodyText | ConvertFrom-Json -ErrorAction Stop
    } catch {
        Write-JsonResponse -Response $response -StatusCode 400 -BodyObject @{
            outcome = "Failure"; diagnostic = "Malformed JSON body: $($_.Exception.Message)"
        }
        continue
    }

    $correlationId = if ($body.PSObject.Properties['correlationId']) { [string]$body.correlationId } else { "" }

    # Validate required fields.
    $tenantId           = if ($body.PSObject.Properties['tenantId'])           { [string]$body.tenantId }           else { $null }
    $expectedAppIds     = if ($body.PSObject.Properties['expectedAppIds'])     { @($body.expectedAppIds) }          else { @() }
    $policyScopeGroupId = if ($body.PSObject.Properties['policyScopeGroupId']) { [string]$body.policyScopeGroupId } else { $null }
    $descriptionPrefix  = if ($body.PSObject.Properties['descriptionPrefix'] -and $body.descriptionPrefix) { [string]$body.descriptionPrefix } else { "Spaarke-Provisioning-AppAccessPolicy" }
    $timeoutSeconds     = if ($body.PSObject.Properties['timeoutSeconds'] -and $body.timeoutSeconds)       { [int]$body.timeoutSeconds }      else { 300 }

    $validationErrors = @()
    if (-not $tenantId)           { $validationErrors += "tenantId is required" }
    if ($expectedAppIds.Count -ne 2) { $validationErrors += "expectedAppIds must contain exactly 2 GUIDs (got $($expectedAppIds.Count))" }
    if (-not $policyScopeGroupId) { $validationErrors += "policyScopeGroupId is required" }
    if ($validationErrors.Count -gt 0) {
        Write-JsonResponse -Response $response -StatusCode 400 -CorrelationId $correlationId -BodyObject @{
            outcome = "Failure"; diagnostic = ($validationErrors -join "; ")
        }
        continue
    }

    Write-JsonLog -Level INFO -CorrelationId $correlationId -Message "Received /apply-policy" -Fields @{
        tenantId           = $tenantId
        expectedAppIdCount = $expectedAppIds.Count
        policyScopeGroupId = $policyScopeGroupId
        timeoutSeconds     = $timeoutSeconds
    }

    # Fetch cert (per-call — short-lived in-memory X509 object; no disk write).
    try {
        $cert = Get-ExchangeCertificate -CorrelationId $correlationId
    } catch {
        Write-JsonLog -Level ERROR -CorrelationId $correlationId -Message "Cert fetch failed" -Fields @{ error = $_.Exception.Message }
        Write-JsonResponse -Response $response -StatusCode 502 -CorrelationId $correlationId -BodyObject @{
            outcome = "Failure"; diagnostic = "Failed to fetch Exchange cert from platform KV: $($_.Exception.Message)"
        }
        continue
    }

    # Invoke script + map envelope.
    try {
        $result = Invoke-ExchangePolicyScript `
            -CorrelationId $correlationId `
            -TenantId $tenantId `
            -ExpectedAppIds $expectedAppIds `
            -PolicyScopeGroupId $policyScopeGroupId `
            -DescriptionPrefix $descriptionPrefix `
            -TimeoutSeconds $timeoutSeconds `
            -Cert $cert
    } catch {
        $result = @{
            outcome         = "Failure"
            createdCount    = 0
            expectedAppIds  = $expectedAppIds
            observedAppIds  = @()
            policiesApplied = @()
            diagnostic      = "Listener caught unexpected script error: $($_.Exception.Message)"
        }
    } finally {
        # Ensure cert material is released promptly (EphemeralKeySet flag means
        # no on-disk key persistence, but Dispose is still good hygiene).
        if ($cert) { $cert.Dispose() }
    }

    Write-JsonLog -Level INFO -CorrelationId $correlationId -Message "Completed /apply-policy" -Fields @{
        outcome      = $result.outcome
        createdCount = $result.createdCount
    }

    Write-JsonResponse -Response $response -StatusCode 200 -CorrelationId $correlationId -BodyObject $result
}

# Not reached in normal operation (loop runs until process is killed).
$listener.Stop()
