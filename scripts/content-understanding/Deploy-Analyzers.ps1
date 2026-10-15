#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    Idempotently deploys Content Understanding analyzer definitions (analyzer-as-code) to a
    Content Understanding resource, on the pinned GA API 2025-11-01.

.DESCRIPTION
    Reads one or more analyzer definition files (default: sprk-invoice-v1.json), and for each:

      1. GET the analyzer from the resource.
      2. Missing            -> create it (PUT) and poll the long-running operation to completion.
      3. Present and equal  -> report "NO CHANGE" and make no write at all (idempotent).
      4. Present and differs-> list the differing properties and EXIT NON-ZERO without writing,
                               unless -Replace is passed (PUT with allowReplace=true).

    "Equal" is judged after dropping the properties the service adds on its own (createdAt,
    lastModifiedAt, status, warnings, supportedModels). The definition files therefore carry every
    property the service would otherwise default (for example config.chartFormat), so that the
    comparison is exact: a property added in Studio that is not in the file is reported as a difference.

    A changed definition is a NEW VERSION (sprk-invoice-v2.json with its own analyzerId); -Replace is
    for deliberate same-id correction only. The script never deletes an analyzer and never touches an
    analyzer id that starts with "prebuilt-".

    Authentication is an Entra bearer token for the cognitive services audience, obtained with
    "az account get-access-token" (developer login, or "az login --identity" / workload identity in
    the pipeline) with Get-AzAccessToken as a fallback. No shared secret is read, accepted or sent
    by this script (ADR-028). The caller needs the Cognitive Services Content Understanding
    Contributor (or Owner) role on the resource to create or replace analyzers.

    All requests use the date-only GA api-version (default 2025-11-01); a value that is not a
    plain date is refused by parameter validation (D-53).

    Analyzer ids: the service allows letters, digits, "." and "_" only (a hyphen is rejected with
    InvalidAnalyzerId), so the analyzerId inside the file is sprk_invoice_v1 although the file name
    keeps hyphens.

.PARAMETER Endpoint
    Resource endpoint, e.g. https://spaarke-docintel-invoices-dev.cognitiveservices.azure.com
    Defaults to the CONTENT_UNDERSTANDING_ENDPOINT environment variable. Must be https.

.PARAMETER AnalyzerFile
    One or more analyzer definition files. Relative paths resolve against the current directory,
    then against the repository root. Default: infrastructure/content-understanding/sprk-invoice-v1.json

.PARAMETER ApiVersion
    GA REST api-version. Default 2025-11-01. Must be a plain date.

.PARAMETER Replace
    Allow replacing an existing analyzer whose definition differs from the file.

.PARAMETER TimeoutSeconds
    Maximum time to wait for each create / replace operation. Default 300.

.PARAMETER WhatIf
    Native -WhatIf: the GET (read) calls run, every write is skipped and reported.

.EXAMPLE
    pwsh ./scripts/content-understanding/Deploy-Analyzers.ps1 -Endpoint https://spaarke-docintel-invoices-dev.cognitiveservices.azure.com -WhatIf

.EXAMPLE
    pwsh ./scripts/content-understanding/Deploy-Analyzers.ps1 -Endpoint $env:CONTENT_UNDERSTANDING_ENDPOINT

.NOTES
    Project : ebilling-r1, task 004 (FR-47, D-49, D-53)
    Exit codes:
      0  success (created, replaced, or no change; also -WhatIf)
      2  bad arguments (endpoint, analyzer id)
      3  analyzer file missing or invalid
      4  could not acquire an access token
      5  an existing analyzer differs and -Replace was not passed (nothing written)
      6  a service call or operation failed
      7  after a write, the deployed analyzer still differs from the file
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$Endpoint = $env:CONTENT_UNDERSTANDING_ENDPOINT,

    [string[]]$AnalyzerFile = @('infrastructure/content-understanding/sprk-invoice-v1.json'),

    [ValidatePattern('^\d{4}-\d{2}-\d{2}$')]
    [string]$ApiVersion = '2025-11-01',

    [switch]$Replace,

    [ValidateRange(10, 3600)]
    [int]$TimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

# Properties the service sets itself; never part of the definition we compare or send.
$ServerAddedProperties = @('createdAt', 'lastModifiedAt', 'status', 'warnings', 'supportedModels')

# ---------------------------------------------------------------------------
# Arguments
# ---------------------------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($Endpoint)) {
    Write-Host 'ERROR: -Endpoint (or CONTENT_UNDERSTANDING_ENDPOINT) is required.' -ForegroundColor Red
    exit 2
}
$Endpoint = $Endpoint.Trim().TrimEnd('/')
$endpointUri = $null
if (-not [Uri]::TryCreate($Endpoint, [UriKind]::Absolute, [ref]$endpointUri) -or $endpointUri.Scheme -ne 'https') {
    Write-Host "ERROR: -Endpoint must be an absolute https URL (got '$Endpoint')." -ForegroundColor Red
    exit 2
}

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
function Get-AccessToken {
    $token = $null
    try {
        $token = az account get-access-token --resource 'https://cognitiveservices.azure.com' --query accessToken -o tsv 2>$null
    } catch { $token = $null }
    if (-not $token -and (Get-Command Get-AzAccessToken -ErrorAction SilentlyContinue)) {
        try {
            $r = Get-AzAccessToken -ResourceUrl 'https://cognitiveservices.azure.com'
            $token = if ($r.Token -is [System.Security.SecureString]) {
                [System.Net.NetworkCredential]::new('', $r.Token).Password
            } else { $r.Token }
        } catch { $token = $null }
    }
    return $token
}

function Invoke-Cu {
    param(
        [Parameter(Mandatory)] [string]$Method,
        [Parameter(Mandatory)] [string]$Uri,
        [string]$Body
    )
    $target = [Uri]$Uri
    if ($target.Host -ne $endpointUri.Host) {
        throw "Refusing to send the access token to '$($target.Host)' (expected '$($endpointUri.Host)')."
    }
    $headers = @{ Authorization = "Bearer $script:AccessToken" }
    $attempt = 0
    while ($true) {
        $attempt++
        $request = @{ Uri = $Uri; Method = $Method; Headers = $headers; SkipHttpErrorCheck = $true }
        if ($Body) { $request.Body = $Body; $request.ContentType = 'application/json' }
        $resp = Invoke-WebRequest @request
        if (($resp.StatusCode -eq 429 -or $resp.StatusCode -eq 503) -and $attempt -lt 4) {
            Start-Sleep -Seconds (2 * $attempt)
            continue
        }
        return $resp
    }
}

function Remove-ServerProperties {
    param([System.Collections.IDictionary]$Definition)
    $copy = [ordered]@{}
    foreach ($name in @($Definition.GetEnumerator() | ForEach-Object { $_.Name })) {
        if ($ServerAddedProperties -notcontains $name) { $copy[$name] = $Definition[$name] }
    }
    return $copy
}

function ConvertTo-FlatMap {
    param($Node, [string]$Path = '', [hashtable]$Map = @{})
    if ($Node -is [System.Collections.IDictionary]) {
        if ($Node.Count -eq 0) { $Map[$Path] = '{}' }
        foreach ($name in @($Node.GetEnumerator() | ForEach-Object { $_.Name })) { $null = ConvertTo-FlatMap -Node $Node[$name] -Path "$Path/$name" -Map $Map }
    } elseif ($Node -is [System.Collections.IList]) {
        if ($Node.Count -eq 0) { $Map[$Path] = '[]' }
        for ($i = 0; $i -lt $Node.Count; $i++) { $null = ConvertTo-FlatMap -Node $Node[$i] -Path "$Path[$i]" -Map $Map }
    } elseif ($null -eq $Node) {
        $Map[$Path] = '<null>'
    } elseif ($Node -is [bool]) {
        $Map[$Path] = $Node.ToString().ToLowerInvariant()
    } elseif ($Node -is [IFormattable]) {
        $Map[$Path] = $Node.ToString($null, [Globalization.CultureInfo]::InvariantCulture)
    } else {
        $Map[$Path] = [string]$Node
    }
    return $Map
}

function Get-Differences {
    param([System.Collections.IDictionary]$Desired, [System.Collections.IDictionary]$Deployed)
    $want = ConvertTo-FlatMap -Node (Remove-ServerProperties $Desired)
    $have = ConvertTo-FlatMap -Node (Remove-ServerProperties $Deployed)
    $diffs = [System.Collections.Generic.List[string]]::new()
    $paths = @($want.GetEnumerator() | ForEach-Object { $_.Name }) + @($have.GetEnumerator() | ForEach-Object { $_.Name })
    foreach ($p in ($paths | Sort-Object -Unique)) {
        $inWant = $want.Contains($p); $inHave = $have.Contains($p)
        if ($inWant -and -not $inHave)      { $diffs.Add("$p : in file, missing on service") }
        elseif ($inHave -and -not $inWant)  { $diffs.Add("$p : on service, not in file") }
        elseif ($want[$p] -ne $have[$p])    {
            # Long text: show the window around the first character that differs.
            $a = $want[$p]; $b = $have[$p]
            if ($a.Length -gt 70 -or $b.Length -gt 70) {
                $n = [Math]::Min($a.Length, $b.Length); $i = 0
                while ($i -lt $n -and $a[$i] -eq $b[$i]) { $i++ }
                $from = [Math]::Max(0, $i - 25)
                $a = '...' + $a.Substring($from, [Math]::Min(60, $a.Length - $from))
                $b = '...' + $b.Substring($from, [Math]::Min(60, $b.Length - $from))
            }
            $diffs.Add("$p : file='$a' service='$b'")
        }
    }
    return , $diffs
}

function Resolve-AnalyzerPath {
    param([string]$Path)
    if (Test-Path -LiteralPath $Path) { return (Resolve-Path -LiteralPath $Path).Path }
    $alt = Join-Path $repoRoot $Path
    if (Test-Path -LiteralPath $alt) { return (Resolve-Path -LiteralPath $alt).Path }
    return $null
}

# ---------------------------------------------------------------------------
# Banner
# ---------------------------------------------------------------------------
Write-Host 'Deploy-Analyzers.ps1' -ForegroundColor Cyan
Write-Host "  Endpoint   : $Endpoint"
Write-Host "  ApiVersion : $ApiVersion"
Write-Host "  Mode       : $(if ($WhatIfPreference) { 'what-if (no writes)' } elseif ($Replace) { 'deploy, replace allowed' } else { 'deploy, no replace' })"

# ---------------------------------------------------------------------------
# Load and validate definition files (before touching Azure)
# ---------------------------------------------------------------------------
$plans = @()
foreach ($file in $AnalyzerFile) {
    $path = Resolve-AnalyzerPath $file
    if (-not $path) { Write-Host "ERROR: analyzer file not found: $file" -ForegroundColor Red; exit 3 }
    try {
        $definition = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable -Depth 50
    } catch {
        Write-Host "ERROR: $path is not valid JSON: $($_.Exception.Message)" -ForegroundColor Red; exit 3
    }
    $id = [string]$definition['analyzerId']
    if ($id -like 'prebuilt*') {
        Write-Host "ERROR: $path analyzerId '$id' is in the prebuilt namespace; this script never writes prebuilt analyzers." -ForegroundColor Red; exit 2
    }
    if ($id -notmatch '^[A-Za-z0-9._]{1,64}$') {
        Write-Host "ERROR: $path analyzerId '$id' is invalid (letters, digits, '.' and '_' only, 1-64 chars; a hyphen is not allowed)." -ForegroundColor Red; exit 2
    }
    $plans += [pscustomobject]@{ File = $path; Id = $id; Desired = (Remove-ServerProperties $definition); State = $null; Diffs = $null }
}
if (($plans.Id | Sort-Object -Unique).Count -ne $plans.Count) {
    Write-Host 'ERROR: two analyzer files declare the same analyzerId.' -ForegroundColor Red; exit 2
}

# ---------------------------------------------------------------------------
# Token
# ---------------------------------------------------------------------------
$script:AccessToken = Get-AccessToken
if (-not $script:AccessToken) {
    Write-Host "ERROR: could not acquire an Entra access token for https://cognitiveservices.azure.com (run 'az login', or 'az login --identity' in the pipeline)." -ForegroundColor Red
    exit 4
}

# ---------------------------------------------------------------------------
# Plan pass: read every analyzer, decide, and refuse before any write
# ---------------------------------------------------------------------------
foreach ($plan in $plans) {
    $url = "$Endpoint/contentunderstanding/analyzers/$($plan.Id)?api-version=$ApiVersion"
    $resp = Invoke-Cu -Method GET -Uri $url
    if ($resp.StatusCode -eq 404) {
        $plan.State = 'create'
    } elseif ($resp.StatusCode -eq 200) {
        $deployed = $resp.Content | ConvertFrom-Json -AsHashtable -Depth 50
        $plan.Diffs = Get-Differences -Desired $plan.Desired -Deployed $deployed
        $plan.State = if ($plan.Diffs.Count -eq 0) { 'nochange' } else { 'differs' }
    } else {
        Write-Host "ERROR: GET $($plan.Id) returned HTTP $($resp.StatusCode): $($resp.Content)" -ForegroundColor Red
        exit 6
    }
    Write-Host "  $($plan.Id): $($plan.State)  ($([IO.Path]::GetFileName($plan.File)))"
}

$blocked = @($plans | Where-Object { $_.State -eq 'differs' })
if ($blocked.Count -gt 0 -and -not $Replace) {
    foreach ($plan in $blocked) {
        Write-Host ''
        Write-Host "REFUSED: deployed analyzer '$($plan.Id)' differs from $($plan.File)" -ForegroundColor Red
        Write-Host '  A changed definition is a new version (new analyzerId + file); pass -Replace only to correct the same id on purpose.' -ForegroundColor Red
        Write-Host "  $($plan.Diffs.Count) differing propert$(if ($plan.Diffs.Count -eq 1) { 'y' } else { 'ies' }):" -ForegroundColor Red
        foreach ($d in $plan.Diffs) { Write-Host "    - $d" -ForegroundColor Red }
    }
    Write-Host ''
    Write-Host 'Nothing was written.' -ForegroundColor Red
    exit 5
}

# ---------------------------------------------------------------------------
# Write pass
# ---------------------------------------------------------------------------
$failed = 0
foreach ($plan in $plans) {
    if ($plan.State -eq 'nochange') {
        Write-Host "RESULT $($plan.Id): NO CHANGE (deployed definition equals the file; no PUT performed)" -ForegroundColor Green
        continue
    }

    $replacing = ($plan.State -eq 'differs')
    $verb = if ($replacing) { 'REPLACE' } else { 'CREATE' }
    if ($replacing) {
        Write-Host "  $($plan.Id) differs from the file ($($plan.Diffs.Count) propert$(if ($plan.Diffs.Count -eq 1) { 'y' } else { 'ies' })):"
        foreach ($d in $plan.Diffs) { Write-Host "    - $d" }
    }
    $putUrl = "$Endpoint/contentunderstanding/analyzers/$($plan.Id)?api-version=$ApiVersion$(if ($replacing) { '&allowReplace=true' })"

    if (-not $PSCmdlet.ShouldProcess("analyzer '$($plan.Id)' at $Endpoint", "PUT ($verb) from $([IO.Path]::GetFileName($plan.File))")) {
        Write-Host "RESULT $($plan.Id): WOULD $verb (what-if, nothing written)" -ForegroundColor Yellow
        continue
    }

    $body = ConvertTo-Json -InputObject $plan.Desired -Depth 50 -Compress
    $put = Invoke-Cu -Method PUT -Uri $putUrl -Body $body
    if ($put.StatusCode -notin 200, 201, 202) {
        Write-Host "RESULT $($plan.Id): $verb FAILED (HTTP $($put.StatusCode)): $($put.Content)" -ForegroundColor Red
        $failed++
        continue
    }

    $opLocation = $put.Headers['Operation-Location'] | Select-Object -First 1
    if ($opLocation) {
        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        $status = 'Running'
        $opBody = $null
        while ($status -in 'Running', 'NotStarted') {
            if ([DateTime]::UtcNow -gt $deadline) { $status = 'TimedOut'; break }
            Start-Sleep -Seconds 2
            $poll = Invoke-Cu -Method GET -Uri $opLocation
            if ($poll.StatusCode -ne 200) { $status = "PollHttp$($poll.StatusCode)"; $opBody = $poll.Content; break }
            $opBody = $poll.Content | ConvertFrom-Json -AsHashtable -Depth 50
            $status = [string]$opBody['status']
        }
        if ($status -ne 'Succeeded') {
            $detail = if ($opBody -is [System.Collections.IDictionary] -and $opBody['error']) { ConvertTo-Json -InputObject $opBody['error'] -Depth 10 -Compress } else { "$opBody" }
            Write-Host "RESULT $($plan.Id): $verb FAILED (operation $status): $detail" -ForegroundColor Red
            $failed++
            continue
        }
    }

    # Post-write verification: the deployed analyzer must now equal the file.
    $verify = Invoke-Cu -Method GET -Uri "$Endpoint/contentunderstanding/analyzers/$($plan.Id)?api-version=$ApiVersion"
    if ($verify.StatusCode -ne 200) {
        Write-Host "RESULT $($plan.Id): $verb verification GET returned HTTP $($verify.StatusCode)" -ForegroundColor Red
        $failed++
        continue
    }
    $after = Get-Differences -Desired $plan.Desired -Deployed ($verify.Content | ConvertFrom-Json -AsHashtable -Depth 50)
    if ($after.Count -gt 0) {
        Write-Host "RESULT $($plan.Id): $verb completed but the service now differs from the file:" -ForegroundColor Red
        foreach ($d in $after) { Write-Host "    - $d" -ForegroundColor Red }
        Write-Host '  Add the service-defaulted property to the definition file so the comparison stays exact.' -ForegroundColor Red
        exit 7
    }
    Write-Host "RESULT $($plan.Id): $($verb)D and verified (deployed definition equals the file)" -ForegroundColor Green
}

if ($failed -gt 0) {
    Write-Host "Deploy-Analyzers.ps1: $failed analyzer(s) failed." -ForegroundColor Red
    exit 6
}
Write-Host 'Deploy-Analyzers.ps1 completed.' -ForegroundColor Green
exit 0
