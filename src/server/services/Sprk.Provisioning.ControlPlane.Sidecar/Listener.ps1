<#
.SYNOPSIS
    HTTP listener of the H14a Exchange sidecar. Runs as PID 1 in the sidecar container beside
    the L2 Worker (same App Service, shared localhost). The logic lives in SidecarCore.psm1.

.DESCRIPTION
    Routes (localhost only -- App Service does not route this port publicly):

      GET  /healthz   200 "ok", or 200 "degraded: <missing settings>". Always answers, so App
                      Service can start the site even when a setting is missing (G30: the
                      first sidecar refused to start and held the Worker at 503).

      POST /apply-mailbox-access   (H14a; X-Sidecar-Auth + X-Exchange-Access-Token)
        Body: { tenantId, organization, appId, servicePrincipalObjectId, displayName?,
                scopeGroupId, assignments: [{ name, role }], correlationId, timeoutSeconds? }
        200:  { outcome: Success|AlreadyCompliant|Drift|Failure, createdCount,
                assignments: [{ name, role, scope, inExpectedScope }], conflicts: [..], diagnostic }

      POST /read-mailbox-access    (H13 T4; read-only; same headers)
        Body: { tenantId, organization, appId, scopeGroupId, roles: [..], correlationId }
              organization = the tenant's initial domain (contoso.onmicrosoft.com), required: with
              the tenant GUID Exchange connects and reads, but every write fails.
        200:  { outcome: Success|Failure, servicePrincipalRegistered, assignments: [..], diagnostic }
              assignments = EVERY "Application *" role the app holds (not only `roles`), each
              marked inExpectedScope; an unknown scope group is outcome Failure.

      POST /ensure-customer-mailbox   (H14m, task 263; same headers)
        Body: { tenantId, organization, appId (stamp identity), scopeGroupId, expectedScopeGroupName
                (Spaarke-AppAccess-{customerId}), name (sprk-{customerId}-mail), displayName, primarySmtpAddress,
                roles: [..], correlationId }
        200:  { outcome: Success|AlreadyCompliant|Drift|Failure, created, verified,
                authorization: [{ role, inScope }], conflicts: [..], diagnostic }

      POST /read-customer-mailbox     (H13, task 263; read-only; same headers and body)
        200:  { outcome: Success|Failure, exists, conflicts: [..], authorization: [..], diagnostic }

    One request at a time: an apply and a T4 read queue behind each other's Exchange connect
    (seconds). timeoutSeconds is advisory — the Worker's HttpClient.Timeout is the bound.

      400 invalid body / no Exchange token . 401 bad X-Sidecar-Auth . 404 unknown route
      503 the sidecar is missing a setting (named in the diagnostic).

.SECURITY
    Worker -> sidecar: localhost bind + X-Sidecar-Auth compared in constant time with
    SIDECAR_SHARED_SECRET (a Key Vault reference App Service resolves).
    Sidecar -> Exchange: Connect-ExchangeOnline -AccessToken with the token the Worker sends in
    X-Exchange-Access-Token ('Spaarke Exchange Admin', federated credential trusting the Worker's
    managed identity -- owner D24). The sidecar holds no credential; tokens are never logged.

.OBSERVABILITY
    One JSON log line per event on stdout (App Service log stream -> Log Analytics);
    correlationId = the provisioning RunId.

.ENVIRONMENT
    SIDECAR_SHARED_SECRET  (required)  SIDECAR_LISTEN_PREFIX (optional; default http://127.0.0.1:8091/)
#>

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'SidecarCore.psm1') -Force

function Write-JsonLog {
    param([ValidateSet('INFO', 'WARN', 'ERROR')][string]$Level, [string]$Message, [string]$CorrelationId = '', [hashtable]$Fields = @{})
    $entry = [ordered]@{ timestamp = [DateTimeOffset]::UtcNow.ToString('o'); level = $Level; component = 'exchange-policy-sidecar'; correlationId = $CorrelationId; message = $Message }
    foreach ($k in $Fields.Keys) { $entry[$k] = $Fields[$k] }
    [Console]::Out.WriteLine(($entry | ConvertTo-Json -Compress -Depth 6))
}

function Write-JsonResponse {
    param([System.Net.HttpListenerResponse]$Response, [int]$StatusCode, [object]$Body, [string]$CorrelationId = '')
    $Response.StatusCode = $StatusCode
    $Response.ContentType = 'application/json'
    if ($CorrelationId) { $Response.Headers.Add('X-Correlation-Id', $CorrelationId) }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Compress -Depth 8))
    $Response.ContentLength64 = $bytes.Length
    $Response.OutputStream.Write($bytes, 0, $bytes.Length)
    $Response.OutputStream.Close()
}

function Write-TextResponse([System.Net.HttpListenerResponse]$Response, [int]$StatusCode, [string]$Text) {
    $Response.StatusCode = $StatusCode
    $Response.ContentType = 'text/plain'
    $bytes = [Text.Encoding]::UTF8.GetBytes($Text)
    $Response.ContentLength64 = $bytes.Length
    $Response.OutputStream.Write($bytes, 0, $bytes.Length)
    $Response.OutputStream.Close()
}

function Invoke-WithExchange {
    <# Connects with the caller's token, runs the operation, always disconnects. #>
    param([string]$Token, [string]$Organization, [scriptblock]$Operation)
    $connect = Get-ExchangeConnectParameters -AccessToken $Token -Organization $Organization
    try {
        Connect-ExchangeOnline @connect -ErrorAction Stop
        return & $Operation
    }
    finally { Disconnect-ExchangeOnline -Confirm:$false -ErrorAction SilentlyContinue | Out-Null }
}

$settings = Get-SidecarSettings -Environment ([Environment]::GetEnvironmentVariables())
if ($settings.Missing.Count -gt 0) {
    Write-JsonLog -Level ERROR -Message 'Sidecar is missing settings -- binding anyway; every request will be refused until they are set.' -Fields @{ missing = $settings.Missing }
}

# Bind FIRST, then load the Exchange module: nothing may stop the port from binding (G30).
$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add($settings.ListenPrefix)
try { $listener.Start() }
catch { Write-JsonLog -Level ERROR -Message 'HttpListener.Start() failed' -Fields @{ error = $_.Exception.Message; prefix = $settings.ListenPrefix }; exit 1 }
try { Import-Module ExchangeOnlineManagement -ErrorAction Stop }
catch {
    $settings.Missing = @($settings.Missing) + "ExchangeOnlineManagement module ($($_.Exception.Message))"
    Write-JsonLog -Level ERROR -Message 'ExchangeOnlineManagement failed to load -- every request will be refused.' -Fields @{ error = $_.Exception.Message }
}
Write-JsonLog -Level INFO -Message 'Sidecar listening' -Fields @{ prefix = $settings.ListenPrefix; degraded = ($settings.Missing.Count -gt 0) }

while ($listener.IsListening) {
    try { $ctx = $listener.GetContext() }
    catch { Write-JsonLog -Level WARN -Message 'GetContext failed' -Fields @{ error = $_.Exception.Message }; continue }
    $request = $ctx.Request; $response = $ctx.Response
    $route = "$($request.HttpMethod) $($request.Url.AbsolutePath)"

    try {
        if ($route -eq 'GET /healthz') {
            Write-TextResponse $response 200 $(if ($settings.Missing.Count -gt 0) { "degraded: missing $($settings.Missing -join ', ')" } else { 'ok' })
            continue
        }
        if ($route -notin 'POST /apply-mailbox-access', 'POST /read-mailbox-access', 'POST /ensure-customer-mailbox', 'POST /read-customer-mailbox') {
            Write-JsonResponse $response 404 @{ outcome = 'Failure'; diagnostic = "Unknown route: $route. Served: GET /healthz, POST /apply-mailbox-access, POST /read-mailbox-access, POST /ensure-customer-mailbox, POST /read-customer-mailbox." }
            continue
        }
        if ($settings.Missing.Count -gt 0) {
            Write-JsonResponse $response 503 @{ outcome = 'Failure'; diagnostic = "Sidecar is missing settings: $($settings.Missing -join ', ')." }
            continue
        }
        if (-not (Test-SecretEqual -Provided $request.Headers['X-Sidecar-Auth'] -Expected $settings.SharedSecret)) {
            Write-JsonLog -Level WARN -Message "Rejected $route`: missing or mismatched X-Sidecar-Auth"
            Write-JsonResponse $response 401 @{ outcome = 'Failure'; diagnostic = 'Missing or invalid X-Sidecar-Auth header.' }
            continue
        }

        $reader = [IO.StreamReader]::new($request.InputStream, $request.ContentEncoding)
        try { $body = $reader.ReadToEnd() | ConvertFrom-Json -ErrorAction Stop } catch { $body = $null } finally { $reader.Close() }
        if ($null -eq $body) { Write-JsonResponse $response 400 @{ outcome = 'Failure'; diagnostic = 'Body must be a JSON object.' }; continue }
        $correlationId = if ($body.PSObject.Properties['correlationId']) { [string]$body.correlationId } else { '' }
        $token = $request.Headers['X-Exchange-Access-Token']
        if ([string]::IsNullOrWhiteSpace($token)) {
            Write-JsonResponse $response 400 -CorrelationId $correlationId @{ outcome = 'Failure'; diagnostic = 'X-Exchange-Access-Token header is required (the sidecar holds no Exchange credential).' }
            continue
        }
        # Validate BEFORE reading any field (StrictMode: a missing property would throw -> 500).
        $errors = switch ($route) {
            'POST /apply-mailbox-access' { Test-ApplyRequest -Body $body }
            'POST /read-mailbox-access' { Test-ReadRequest -Body $body }
            default { Test-CustomerMailboxRequest -Body $body }   # both customer-mailbox routes take the same body
        }
        if ($errors.Count -gt 0) { Write-JsonResponse $response 400 -CorrelationId $correlationId @{ outcome = 'Failure'; diagnostic = ($errors -join '; ') }; continue }
        $organization = [string]$body.organization   # required + shape-checked above; never the tenant GUID

        if ($route -eq 'POST /apply-mailbox-access') {
            Write-JsonLog -Level INFO -CorrelationId $correlationId -Message 'Received /apply-mailbox-access' -Fields @{ tenantId = $body.tenantId; appId = $body.appId; scopeGroupId = $body.scopeGroupId; assignmentCount = @($body.assignments).Count }
            try { $result = Invoke-WithExchange -Token $token -Organization $organization -Operation { Invoke-MailboxAccessApply -Request $body } }
            catch { $result = @{ outcome = 'Failure'; createdCount = 0; assignments = @(); conflicts = @(); diagnostic = "Exchange call failed: $($_.Exception.Message)" } }
            Write-JsonLog -Level INFO -CorrelationId $correlationId -Message 'Completed /apply-mailbox-access' -Fields @{ outcome = $result.outcome; createdCount = $result.createdCount }
        }
        elseif ($route -eq 'POST /ensure-customer-mailbox') {
            Write-JsonLog -Level INFO -CorrelationId $correlationId -Message 'Received /ensure-customer-mailbox' -Fields @{ tenantId = $body.tenantId; appId = $body.appId; scopeGroupId = $body.scopeGroupId; name = $body.name }
            try { $result = Invoke-WithExchange -Token $token -Organization $organization -Operation { Invoke-CustomerMailboxEnsure -Request $body } }
            catch { $result = @{ outcome = 'Failure'; created = $false; verified = $false; authorization = @(); conflicts = @(); diagnostic = "Exchange call failed: $($_.Exception.Message)" } }
            Write-JsonLog -Level INFO -CorrelationId $correlationId -Message 'Completed /ensure-customer-mailbox' -Fields @{ outcome = $result.outcome; created = $result.created; verified = $result.verified }
        }
        elseif ($route -eq 'POST /read-customer-mailbox') {
            Write-JsonLog -Level INFO -CorrelationId $correlationId -Message 'Received /read-customer-mailbox' -Fields @{ tenantId = $body.tenantId; appId = $body.appId; name = $body.name }
            try { $result = Invoke-WithExchange -Token $token -Organization $organization -Operation { Get-CustomerMailboxState -Request $body } }
            catch { $result = @{ outcome = 'Failure'; exists = $false; conflicts = @(); authorization = @(); diagnostic = "Exchange call failed: $($_.Exception.Message)" } }
            Write-JsonLog -Level INFO -CorrelationId $correlationId -Message 'Completed /read-customer-mailbox' -Fields @{ outcome = $result.outcome; exists = $result.exists }
        }
        else {
            Write-JsonLog -Level INFO -CorrelationId $correlationId -Message 'Received /read-mailbox-access' -Fields @{ tenantId = $body.tenantId; appId = $body.appId }
            try { $result = Invoke-WithExchange -Token $token -Organization $organization -Operation { Get-MailboxAccessState -AppId ([string]$body.appId) -ScopeGroupId ([string]$body.scopeGroupId) } }
            catch { $result = @{ outcome = 'Failure'; servicePrincipalRegistered = $false; assignments = @(); diagnostic = "Exchange call failed: $($_.Exception.Message)" } }
            Write-JsonLog -Level INFO -CorrelationId $correlationId -Message 'Completed /read-mailbox-access' -Fields @{ outcome = $result.outcome; assignmentCount = @($result.assignments).Count }
        }
        Write-JsonResponse $response 200 -CorrelationId $correlationId $result
    }
    catch {
        Write-JsonLog -Level ERROR -Message "Unhandled error on $route" -Fields @{ error = $_.Exception.Message }
        try { Write-JsonResponse $response 500 @{ outcome = 'Failure'; diagnostic = "Sidecar error: $($_.Exception.Message)" } } catch { }
    }
}
$listener.Stop()
