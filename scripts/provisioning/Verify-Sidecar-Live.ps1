<#
.SYNOPSIS
    Live verification of the H14a Exchange sidecar (task 114; RBAC for Applications + token sign-in
    since task 251) running as a sitecontainer under the L2 control-plane Worker App Service. Six
    checks; a structured pass/fail report the operator hands off.

.DESCRIPTION
    customer-provisioning-orchestration-r1 task 162, reworked in task 251 after the first live run.

    HOW THE SIDECAR CAN BE REACHED (found live, 2026-10-04): on Linux App Service, Kudu runs in its
    OWN container, so Kudu /api/command cannot reach the Worker's 127.0.0.1:8091 (connection
    refused), and /api/command runs no shell (`a && b` arrives as arguments of `a`). The earlier
    version of this script curled the sidecar through Kudu and could never pass. Now:

      1. CONTAINER_HEALTH  — the sitecontainer is configured on the Worker with the expected port.
                             ARM: GET /sites/{worker}/sitecontainers/{name}
      2. LOCALHOST_BIND    — the sidecar's own log says it bound its port with every setting present:
                             the latest "Sidecar listening" line in the Worker's container log
                             (Kudu /api/logs/docker) has degraded = false.
      3. PUBLIC_ISOLATION  — the sidecar port is NOT reachable at the Worker's public hostname.
      4. ROUND_TRIP_AUTH   — (-InTenant) POST /read-mailbox-access with the right shared secret and a
                             real Exchange token returns HTTP 200, outcome Success.
      5. ROUND_TRIP_IDEMP  — (-InTenant + a TEST app and group) POST /apply-mailbox-access twice; the
                             second returns AlreadyCompliant. CREATES a group-scoped role assignment
                             for the test app -- remove it afterwards.
      6. AUTH_REJECTION    — (-InTenant) a wrong X-Sidecar-Auth is rejected with HTTP 401, and the
                             tenant id as organization is rejected with HTTP 400.

    -InTenant runs checks 4-6 the way the Worker would, inside Azure: a temporary container instance
    from the Worker's sidecar image, carrying the Worker's managed identity, starts the image's own
    Listener.ps1, signs in as 'Spaarke Exchange Admin' through the federated credential, reads the
    tenant's initial domain from Graph GET /organization, and calls the routes on localhost. The
    Exchange token never leaves that container and is never printed. The container is deleted at the
    end. Without -InTenant, checks 4-6 are WARN (nothing is created).

.PARAMETER Environment
    dev, staging or production. Drives the default resource names. Default: dev.

.PARAMETER WorkerAppServiceName
    L2 Worker App Service. Default: spaarke-provisioning-controlplane-worker-{Environment}.

.PARAMETER WorkerResourceGroup
    Resource group of the Worker. Default: rg-spaarke-platform-{Environment}.

.PARAMETER SidecarName
    Sitecontainer name. Default: exchange-policy-sidecar.

.PARAMETER SidecarPort
    Sidecar port. Default: 8091.

.PARAMETER InTenant
    Run checks 4-6 in a temporary container instance (see DESCRIPTION). Creates and deletes one
    container instance in the Worker's resource group; read-only against Exchange unless check 5's
    test-app parameters are supplied.

.PARAMETER PolicyScopeGroupId
    Entra object id of a TEST mail-enabled security group (checks 4-6). Default: all-zero GUID --
    the read then reports "group not found", which still proves routing, auth and the Exchange connect.

.PARAMETER AppId
    Client id of a TEST app. Check 4 reads its assignments; check 5 grants it Application Mail.Read in
    the test group. Default: the Worker's IntegrationWiring__ExchangeAdminAppId (read only; check 5
    never runs against the admin app).

.PARAMETER ServicePrincipalObjectId
    The TEST app's Entra service-principal object id. Check 5 runs only when this and a non-default
    -AppId and -PolicyScopeGroupId are given.

.PARAMETER SkipChecks
    Comma-separated check names to skip: CONTAINER_HEALTH, LOCALHOST_BIND, PUBLIC_ISOLATION,
    ROUND_TRIP_AUTH, ROUND_TRIP_IDEMP, AUTH_REJECTION.

.PARAMETER ReportPath
    Optional path for a JSON summary.

.EXAMPLE
    # Configuration checks only (creates nothing).
    .\Verify-Sidecar-Live.ps1

.EXAMPLE
    # Full in-tenant run, read-only against Exchange.
    .\Verify-Sidecar-Live.ps1 -InTenant -PolicyScopeGroupId <test group object id>

.EXAMPLE
    # Including check 5 (creates assignment 'Spaarke-liveverify-MailRead' for the test app).
    .\Verify-Sidecar-Live.ps1 -InTenant -PolicyScopeGroupId <test group> -AppId <test app> -ServicePrincipalObjectId <its SP object id>

.NOTES
    Project: customer-provisioning-orchestration-r1 -- task 162, reworked by task 251.

    Justification (CLAUDE.md §11): Existing -- Deploy-ControlPlane.ps1 deploys; this verifies after
    deploy (separate concern). Cost of doing nothing -- no repeatable answer to "does the sidecar work
    with the real identity and tenant?" after a deploy.

    PREREQUISITES: `az login` as the operator (NFR-11) with read on the Worker and its resource group,
    Kudu (SCM) access on the Worker, and -- for -InTenant -- rights to create a container instance in
    that resource group and to assign the Worker's user-assigned identity to it.
#>

[CmdletBinding()]
param(
    [ValidateSet('dev', 'staging', 'production')]
    [string]$Environment = 'dev',
    [string]$WorkerAppServiceName,
    [string]$WorkerResourceGroup,
    [string]$SidecarName = 'exchange-policy-sidecar',
    [int]$SidecarPort = 8091,
    [switch]$InTenant,
    # GUIDs only: these values are written into the generated in-container script.
    [ValidatePattern('^([0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12})?$')]
    [string]$PolicyScopeGroupId = '00000000-0000-0000-0000-000000000000',
    [ValidatePattern('^([0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12})?$')]
    [string]$AppId = '',
    [ValidatePattern('^([0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12})?$')]
    [string]$ServicePrincipalObjectId = '',
    [string]$SkipChecks = '',
    [string]$ReportPath = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $WorkerAppServiceName) { $WorkerAppServiceName = "spaarke-provisioning-controlplane-worker-$Environment" }
if (-not $WorkerResourceGroup) { $WorkerResourceGroup = "rg-spaarke-platform-$Environment" }
$ZeroGuid = '00000000-0000-0000-0000-000000000000'

$script:CheckResults = [ordered]@{}
$script:ValidCheckNames = @('CONTAINER_HEALTH', 'LOCALHOST_BIND', 'PUBLIC_ISOLATION', 'ROUND_TRIP_AUTH', 'ROUND_TRIP_IDEMP', 'AUTH_REJECTION')
$script:SkipSet = @{}
foreach ($check in ($SkipChecks -split ',' | ForEach-Object { $_.Trim().ToUpperInvariant() } | Where-Object { $_ })) {
    if ($script:ValidCheckNames -notcontains $check) { throw "Unknown check name in -SkipChecks: '$check'. Valid names: $($script:ValidCheckNames -join ', ')." }
    $script:SkipSet[$check] = $true
}

# ---- Helpers ----------------------------------------------------------------

function Write-CheckResult {
    param([string]$Name, [ValidateSet('PASS', 'FAIL', 'WARN', 'SKIP')][string]$Status, [string]$Message, [hashtable]$Details = @{})
    $script:CheckResults[$Name] = [ordered]@{ status = $Status; message = $Message; details = $Details }
    $color = @{ PASS = 'Green'; FAIL = 'Red'; WARN = 'Yellow'; SKIP = 'DarkGray' }[$Status]
    Write-Host ("  [{0}] {1,-18} {2}" -f $Status, $Name, $Message) -ForegroundColor $color
}

function Test-CheckSkipped([string]$Name) {
    if ($script:SkipSet.ContainsKey($Name)) { Write-CheckResult -Name $Name -Status 'SKIP' -Message 'Skipped via -SkipChecks'; return $true }
    return $false
}

function Invoke-Az {
    # No param block on purpose: az's own -o/-g/-n must reach az, not bind as PowerShell parameters.
    # Explicit $LASTEXITCODE guard (task 113 finding: piping az output leaves it stale).
    $script:LASTEXITCODE = 0
    $out = & az @args 2>&1
    if ($LASTEXITCODE -ne 0) { throw "az $($args[0..1] -join ' ') failed: $out" }
    return ($out | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] }) -join "`n"
}

function Get-ManagementToken { return (Invoke-Az account get-access-token --resource 'https://management.azure.com/' --query accessToken -o tsv).Trim() }

function Invoke-Arm {
    # ARM REST with a JSON body: nothing passes through az.cmd / cmd.exe quoting.
    param([string]$Method, [string]$Path, $Body = $null)
    $p = @{ Method = $Method; Uri = "https://management.azure.com$Path"; Headers = @{ Authorization = "Bearer $(Get-ManagementToken)" } }
    if ($null -ne $Body) { $p.Body = ($Body | ConvertTo-Json -Depth 20 -Compress); $p.ContentType = 'application/json' }
    return Invoke-RestMethod @p
}

# ---- Check 1: CONTAINER_HEALTH --------------------------------------------

function Test-ContainerHealth {
    if (Test-CheckSkipped 'CONTAINER_HEALTH') { return }
    try {
        $subscription = (Invoke-Az account show --query id -o tsv).Trim()
        $uri = "https://management.azure.com/subscriptions/$subscription/resourceGroups/$WorkerResourceGroup/providers/Microsoft.Web/sites/$WorkerAppServiceName/sitecontainers/$SidecarName" + '?api-version=2024-04-01'
        $sc = Invoke-RestMethod -Uri $uri -Headers @{ Authorization = "Bearer $(Get-ManagementToken)" }
        $script:SidecarImage = [string]$sc.properties.image
        $details = @{ image = $script:SidecarImage; targetPort = $sc.properties.targetPort; authType = $sc.properties.authType }
        if ("$($sc.properties.targetPort)" -ne "$SidecarPort") {
            Write-CheckResult -Name 'CONTAINER_HEALTH' -Status 'FAIL' -Message "targetPort is $($sc.properties.targetPort), expected $SidecarPort" -Details $details
            return
        }
        Write-CheckResult -Name 'CONTAINER_HEALTH' -Status 'PASS' -Message "sitecontainer '$SidecarName' on port $SidecarPort, image '$script:SidecarImage'" -Details $details
    } catch {
        Write-CheckResult -Name 'CONTAINER_HEALTH' -Status 'FAIL' -Message "Could not read the sitecontainer: $($_.Exception.Message)"
    }
}

# ---- Check 2: LOCALHOST_BIND (the sidecar's own log) -----------------------

function Test-LocalhostBind {
    if (Test-CheckSkipped 'LOCALHOST_BIND') { return }
    try {
        $headers = @{ Authorization = "Bearer $(Get-ManagementToken)" }
        $kudu = "https://$WorkerAppServiceName.scm.azurewebsites.net"
        # The app's container log (*_default_docker.log) carries the sidecar's stdout too.
        # Assign first: Invoke-RestMethod emits the JSON array as ONE pipeline object.
        $listing = Invoke-RestMethod -Uri "$kudu/api/logs/docker" -Headers $headers
        $logs = @($listing | Where-Object { $_.href -match '_default_docker\.log$' } | Sort-Object { [DateTime]$_.lastUpdated } -Descending)
        if ($logs.Count -eq 0) { Write-CheckResult -Name 'LOCALHOST_BIND' -Status 'FAIL' -Message 'No *_default_docker.log on the Worker'; return }
        $line = $null
        foreach ($log in $logs | Select-Object -First 3) {
            $text = Invoke-RestMethod -Uri $log.href -Headers $headers
            $line = ($text -split "`n") | Where-Object { $_ -match '"component":"exchange-policy-sidecar"' -and $_ -match '"message":"Sidecar listening"' } | Select-Object -Last 1
            if ($line) { break }
        }
        if (-not $line) { Write-CheckResult -Name 'LOCALHOST_BIND' -Status 'FAIL' -Message "No 'Sidecar listening' line in the Worker's recent container logs -- the sidecar never bound its port"; return }
        $entry = $line.Substring($line.IndexOf('{')) | ConvertFrom-Json
        # ConvertFrom-Json turns the ISO timestamp into a local DateTime; report it in UTC.
        $at = if ($entry.timestamp -is [DateTime]) { $entry.timestamp.ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss') + 'Z' } else { [string]$entry.timestamp }
        $details = @{ timestamp = $at; prefix = $entry.prefix; degraded = $entry.degraded }
        if ($entry.degraded) {
            Write-CheckResult -Name 'LOCALHOST_BIND' -Status 'FAIL' -Message "Sidecar bound $($entry.prefix) at $at but DEGRADED -- a setting is missing (see its 'missing settings' log line)" -Details $details
            return
        }
        Write-CheckResult -Name 'LOCALHOST_BIND' -Status 'PASS' -Message "Sidecar bound $($entry.prefix) at $at, all settings present" -Details $details
    } catch {
        Write-CheckResult -Name 'LOCALHOST_BIND' -Status 'FAIL' -Message "Could not read the Worker's container logs via Kudu: $($_.Exception.Message)"
    }
}

# ---- Check 3: PUBLIC_ISOLATION (security-critical) ------------------------

function Test-PublicIsolation {
    if (Test-CheckSkipped 'PUBLIC_ISOLATION') { return }
    $probe = "https://${WorkerAppServiceName}.azurewebsites.net:$SidecarPort/healthz"
    try {
        $response = Invoke-WebRequest -Uri $probe -TimeoutSec 10 -UseBasicParsing
        Write-CheckResult -Name 'PUBLIC_ISOLATION' -Status 'FAIL' -Message "SECURITY-CRITICAL: sidecar port $SidecarPort is REACHABLE publicly (HTTP $($response.StatusCode)). Escalate." -Details @{ probe = $probe }
    } catch {
        Write-CheckResult -Name 'PUBLIC_ISOLATION' -Status 'PASS' -Message "port $SidecarPort not reachable at the public hostname ($($_.Exception.GetType().Name))" -Details @{ probe = $probe }
    }
}

# ---- Checks 4-6: in-tenant replay -----------------------------------------

function Get-InTenantScript {
    param([string]$TenantId, [string]$UamiClientId, [string]$AdminAppId, [string]$ReadAppId, [bool]$RunApply)
    # Runs inside the container instance. Prints RESULT lines only -- never a token or the secret.
    return @"
`$ErrorActionPreference = 'Stop'
function Result(`$name, `$obj) { Write-Host ("RESULT {0} {1}" -f `$name, (`$obj | ConvertTo-Json -Compress -Depth 6)) }
`$secret = [guid]::NewGuid().ToString('N'); `$env:SIDECAR_SHARED_SECRET = `$secret
`$p = Start-Process pwsh -ArgumentList '-NoProfile','-NonInteractive','-File','/app/Listener.ps1' -PassThru -RedirectStandardOutput /tmp/l.out -RedirectStandardError /tmp/l.err
`$up = `$false; for (`$i = 0; `$i -lt 60 -and -not `$up; `$i++) { try { Invoke-RestMethod http://127.0.0.1:$SidecarPort/healthz -TimeoutSec 5 | Out-Null; `$up = `$true } catch { Start-Sleep 2 } }
if (-not `$up) { Result 'listener' @{ ok = `$false }; exit 10 }
`$imds = 'http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&client_id=$UamiClientId&resource='
`$assertion = (Invoke-RestMethod -Headers @{ Metadata = 'true' } -Uri (`$imds + 'api://AzureADTokenExchange')).access_token
`$exo = (Invoke-RestMethod -Method Post -Uri 'https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token' -Body @{ client_id = '$AdminAppId'; scope = 'https://outlook.office365.com/.default'; grant_type = 'client_credentials'; client_assertion_type = 'urn:ietf:params:oauth:client-assertion-type:jwt-bearer'; client_assertion = `$assertion }).access_token
`$graph = (Invoke-RestMethod -Headers @{ Metadata = 'true' } -Uri (`$imds + 'https://graph.microsoft.com')).access_token
`$org = Invoke-RestMethod -Uri 'https://graph.microsoft.com/v1.0/organization?`$select=verifiedDomains' -Headers @{ Authorization = "Bearer `$graph" }
`$domain = @(`$org.value[0].verifiedDomains | Where-Object { `$_.isInitial })[0].name
function Call(`$path, `$body, `$secretValue) {
    `$r = Invoke-WebRequest -Method Post -Uri "http://127.0.0.1:$SidecarPort`$path" -Body (`$body | ConvertTo-Json -Compress -Depth 6) -ContentType 'application/json' -Headers @{ 'X-Sidecar-Auth' = `$secretValue; 'X-Exchange-Access-Token' = `$exo } -SkipHttpErrorCheck -TimeoutSec 360
    `$j = try { `$r.Content | ConvertFrom-Json } catch { `$null }
    return @{ status = [int]`$r.StatusCode; outcome = `$j.outcome; diagnostic = `$j.diagnostic; assignments = @(`$j.assignments).Count }
}
`$read = @{ tenantId = '$TenantId'; organization = `$domain; appId = '$ReadAppId'; scopeGroupId = '$PolicyScopeGroupId'; roles = @('Application Mail.Read'); correlationId = 'live-verify-read' }
Result 'domain' @{ organization = `$domain }
Result 'read' (Call '/read-mailbox-access' `$read `$secret)
Result 'wrongsecret' (Call '/read-mailbox-access' `$read 'definitely-not-the-secret')
`$guidOrg = `$read.Clone(); `$guidOrg.organization = '$TenantId'
Result 'guidorg' (Call '/read-mailbox-access' `$guidOrg `$secret)
if (`$$RunApply) {
    `$apply = @{ tenantId = '$TenantId'; organization = `$domain; appId = '$ReadAppId'; servicePrincipalObjectId = '$ServicePrincipalObjectId'; displayName = 'Spaarke-liveverify-test-app'; scopeGroupId = '$PolicyScopeGroupId'; assignments = @(@{ name = 'Spaarke-liveverify-MailRead'; role = 'Application Mail.Read' }); correlationId = 'live-verify-apply'; timeoutSeconds = 300 }
    Result 'apply1' (Call '/apply-mailbox-access' `$apply `$secret)
    Result 'apply2' (Call '/apply-mailbox-access' `$apply `$secret)
}
Stop-Process -Id `$p.Id -Force -ErrorAction SilentlyContinue
Write-Host 'INTENANT DONE'
"@
}

function Invoke-InTenantChecks {
    $names = 'ROUND_TRIP_AUTH', 'ROUND_TRIP_IDEMP', 'AUTH_REJECTION'
    if (-not $InTenant) {
        foreach ($n in $names) { if (-not (Test-CheckSkipped $n)) { Write-CheckResult -Name $n -Status 'WARN' -Message 'Not run: needs -InTenant (runs the request inside Azure as the Worker identity; see -InTenant help)' } }
        return
    }
    $aci = "sprk-sidecar-verify-$Environment"
    $groupPath = $null
    try {
        $account = Invoke-Az account show -o json | ConvertFrom-Json
        $tenantId = $account.tenantId
        $groupPath = "/subscriptions/$($account.id)/resourceGroups/$WorkerResourceGroup/providers/Microsoft.ContainerInstance/containerGroups/$aci"
        $identity = Invoke-Az webapp identity show -g $WorkerResourceGroup -n $WorkerAppServiceName -o json | ConvertFrom-Json
        $uamiId = @($identity.userAssignedIdentities.PSObject.Properties)[0].Name
        $uamiClientId = @($identity.userAssignedIdentities.PSObject.Properties)[0].Value.clientId
        $settings = Invoke-Az webapp config appsettings list -g $WorkerResourceGroup -n $WorkerAppServiceName -o json | ConvertFrom-Json
        $adminAppId = [string](@($settings | Where-Object name -eq 'IntegrationWiring__ExchangeAdminAppId')[0].value)
        if (-not $adminAppId) { throw "Worker setting IntegrationWiring__ExchangeAdminAppId is empty" }
        if (-not $script:SidecarImage) { throw 'Sidecar image unknown (CONTAINER_HEALTH did not run or failed)' }
        $readAppId = if ($AppId) { $AppId } else { $adminAppId }
        $runApply = [bool]($AppId -and $ServicePrincipalObjectId -and $PolicyScopeGroupId -ne $ZeroGuid -and $AppId -ne $adminAppId)
        $script = Get-InTenantScript -TenantId $tenantId -UamiClientId $uamiClientId -AdminAppId $adminAppId -ReadAppId $readAppId -RunApply $runApply
        $location = (Invoke-Az group show -n $WorkerResourceGroup --query location -o tsv).Trim()
        Write-Host "  [--] creating container instance '$aci' from $script:SidecarImage (Worker identity) ..." -ForegroundColor DarkGray
        $definition = @{
            location   = $location
            identity   = @{ type = 'UserAssigned'; userAssignedIdentities = @{ $uamiId = @{} } }
            properties = @{
                osType                   = 'Linux'
                restartPolicy            = 'Never'
                imageRegistryCredentials = @(@{ server = $script:SidecarImage.Split('/')[0]; identity = $uamiId })
                containers               = @(@{
                    name       = 'verify'
                    properties = @{
                        image        = $script:SidecarImage
                        command      = @('pwsh', '-NoProfile', '-NonInteractive', '-File', '/verify/verify.ps1')
                        resources    = @{ requests = @{ cpu = 1; memoryInGB = 1.5 } }
                        volumeMounts = @(@{ name = 'verify'; mountPath = '/verify'; readOnly = $true })
                    }
                })
                volumes                  = @(@{ name = 'verify'; secret = @{ 'verify.ps1' = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($script)) } })
            }
        }
        Invoke-Arm PUT "$groupPath`?api-version=2023-05-01" $definition | Out-Null
        $log = ''
        for ($i = 0; $i -lt 120; $i++) {
            Start-Sleep -Seconds 5
            try { $log = [string](Invoke-Arm GET "$groupPath/containers/verify/logs?api-version=2023-05-01").content } catch { $log = '' }
            if ($log -match 'INTENANT DONE' -or $log -match 'RESULT listener') { break }
        }
        $results = @{}
        foreach ($l in ($log -split "`n") | Where-Object { $_ -match '^RESULT (\S+) (.*)$' }) {
            $null = $l -match '^RESULT (\S+) (.*)$'; $results[$Matches[1]] = $Matches[2] | ConvertFrom-Json
        }
        if (-not $results.ContainsKey('read')) { throw "The in-tenant run produced no read result. Container log: $log" }
        $org = $results['domain'].organization

        if (-not (Test-CheckSkipped 'ROUND_TRIP_AUTH')) {
            $r = $results['read']
            $groupMissing = $PolicyScopeGroupId -eq $ZeroGuid -and $r.outcome -eq 'Failure' -and "$($r.diagnostic)" -match 'not found'
            if ($r.status -eq 200 -and ($r.outcome -eq 'Success' -or $groupMissing)) {
                Write-CheckResult -Name 'ROUND_TRIP_AUTH' -Status 'PASS' -Message "read as $readAppId via $org : HTTP 200, $($r.outcome) ($($r.diagnostic))" -Details @{ organization = $org; outcome = $r.outcome; assignments = $r.assignments }
            } else {
                Write-CheckResult -Name 'ROUND_TRIP_AUTH' -Status 'FAIL' -Message "read returned HTTP $($r.status), outcome '$($r.outcome)': $($r.diagnostic)" -Details @{ organization = $org }
            }
        }
        if (-not (Test-CheckSkipped 'ROUND_TRIP_IDEMP')) {
            if (-not $runApply) {
                Write-CheckResult -Name 'ROUND_TRIP_IDEMP' -Status 'WARN' -Message 'Not run: needs -AppId, -ServicePrincipalObjectId and -PolicyScopeGroupId for a TEST app and group (it creates a role assignment)'
            } elseif ($results['apply2'].outcome -eq 'AlreadyCompliant') {
                Write-CheckResult -Name 'ROUND_TRIP_IDEMP' -Status 'PASS' -Message "apply #1 $($results['apply1'].outcome), apply #2 AlreadyCompliant. Remove assignment 'Spaarke-liveverify-MailRead' and the test app's Exchange service principal when done."
            } else {
                Write-CheckResult -Name 'ROUND_TRIP_IDEMP' -Status 'FAIL' -Message "apply #2 returned '$($results['apply2'].outcome)': $($results['apply2'].diagnostic)" -Details @{ apply1 = $results['apply1']; apply2 = $results['apply2'] }
            }
        }
        if (-not (Test-CheckSkipped 'AUTH_REJECTION')) {
            $w = $results['wrongsecret']; $g = $results['guidorg']
            if ($w.status -eq 401 -and $g.status -eq 400) {
                Write-CheckResult -Name 'AUTH_REJECTION' -Status 'PASS' -Message 'wrong shared secret -> 401; tenant id as organization -> 400'
            } else {
                Write-CheckResult -Name 'AUTH_REJECTION' -Status 'FAIL' -Message "SECURITY-CRITICAL: wrong secret -> HTTP $($w.status) (expected 401); tenant id as organization -> HTTP $($g.status) (expected 400). Escalate." -Details @{ wrongSecret = $w; guidOrganization = $g }
            }
        }
    } catch {
        foreach ($n in $names) { if (-not $script:SkipSet.ContainsKey($n)) { Write-CheckResult -Name $n -Status 'FAIL' -Message "In-tenant run failed: $($_.Exception.Message)" } }
    } finally {
        if ($groupPath) {
            try { Invoke-Arm DELETE "$groupPath`?api-version=2023-05-01" | Out-Null }
            catch { Write-Host "  [!!] could not delete container instance '$aci' in $WorkerResourceGroup -- delete it by hand" -ForegroundColor Yellow }
        }
    }
}

# ---- Run + report ----------------------------------------------------------

Write-Host ''
Write-Host '===============================================================' -ForegroundColor Cyan
Write-Host ' Sidecar live verification' -ForegroundColor Cyan
Write-Host '===============================================================' -ForegroundColor Cyan
Write-Host " Worker:   $WorkerAppServiceName ($WorkerResourceGroup)"
Write-Host " Sidecar:  $SidecarName (port $SidecarPort)"
Write-Host " Mode:     $(if ($InTenant) { 'in-tenant (checks 4-6 run inside Azure as the Worker identity)' } else { 'configuration only (checks 4-6 WARN)' })"
Write-Host ''
Test-ContainerHealth
Test-LocalhostBind
Test-PublicIsolation
Invoke-InTenantChecks

$counts = @{}
foreach ($s in 'PASS', 'FAIL', 'WARN', 'SKIP') { $counts[$s] = @($script:CheckResults.Values | Where-Object { $_.status -eq $s }).Count }
Write-Host ''
Write-Host (" PASS {0} · WARN {1} · FAIL {2} · SKIP {3}" -f $counts.PASS, $counts.WARN, $counts.FAIL, $counts.SKIP)
if ($ReportPath) {
    [ordered]@{
        timestamp = [DateTimeOffset]::UtcNow.ToString('o'); environment = $Environment; worker = $WorkerAppServiceName
        sidecar = $SidecarName; inTenant = [bool]$InTenant; counts = $counts; checks = $script:CheckResults
    } | ConvertTo-Json -Depth 10 | Out-File -FilePath $ReportPath -Encoding utf8
    Write-Host " Report: $ReportPath"
}
if ($counts.FAIL -gt 0) { Write-Host ' OVERALL: FAIL' -ForegroundColor Red; exit 1 }
Write-Host " OVERALL: PASS$(if ($counts.WARN -gt 0) { ' (with warnings)' })" -ForegroundColor Green
exit 0
