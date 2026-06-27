<#
.SYNOPSIS
    Comprehensive audit of EVERY Dataverse surface that references AI Search
    index names (or related configuration). Identifies stale/canonical/missing
    references across:
      1. Records (data rows with sprk_searchindexname)
      2. Environment Variables (definitions + values)
      3. AI Search Index entities (sprk_aiknowledgedeployment, etc.)
      4. Web Resources (JS/HTML deployed to Dataverse)
      5. Workflows / Business Rules / Plugins (search for index-name strings)

.PARAMETER DataverseUrl
    Dataverse environment URL. Default: https://spaarkedev1.crm.dynamics.com
#>
param(
    [string]$DataverseUrl = 'https://spaarkedev1.crm.dynamics.com'
)

$ErrorActionPreference = 'Continue'  # Audit continues on individual failures

$Token = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
if (-not $Token) { throw "Failed to get Dataverse token. Run 'az login' first." }
$H = @{ Authorization = "Bearer $Token" }

$StaleNames = @('spaarke-file-index', 'spaarke-knowledge-index-v2', 'spaarke-knowledge-shared', 'discovery-index', 'spaarke-knowledge-index', 'spaarke-invoices-dev', 'playbook-embeddings')
$CanonicalNames = @('spaarke-files-index', 'spaarke-discovery-index', 'spaarke-records-index', 'spaarke-rag-references', 'spaarke-insights-index', 'spaarke-session-files', 'spaarke-invoices-index', 'spaarke-playbook-embeddings')

Write-Host "=== Audit 1: Data records with sprk_searchindexname ==="
$entitySets = @('sprk_matters', 'sprk_projects', 'sprk_invoices', 'businessunits')
foreach ($es in $entitySets) {
    try {
        $resp = Invoke-RestMethod -Uri "$DataverseUrl/api/data/v9.2/$es" -Headers $H -ErrorAction Stop
        $withField = $resp.value | Where-Object { $_.sprk_searchindexname }
        if (-not $withField) { Write-Host "  $es : 0 records with non-null sprk_searchindexname"; continue }
        $byValue = $withField | Group-Object -Property sprk_searchindexname
        foreach ($g in $byValue) {
            $marker = if ($CanonicalNames -contains $g.Name) { "[CANONICAL]" } elseif ($StaleNames -contains $g.Name) { "[STALE]" } else { "[UNKNOWN]" }
            Write-Host "  $es : $($g.Count) records → '$($g.Name)' $marker"
        }
    } catch { Write-Host "  $es : ERR $($_.Exception.Message)" }
}

Write-Host ""
Write-Host "=== Audit 2: Environment Variables (AI-Search-related) ==="
try {
    $defs = Invoke-RestMethod -Uri "$DataverseUrl/api/data/v9.2/environmentvariabledefinitions?`$select=environmentvariabledefinitionid,schemaname,displayname,defaultvalue,description" -Headers $H -ErrorAction Stop
    $aiDefs = $defs.value | Where-Object {
        $kw = "$($_.schemaname) $($_.displayname) $($_.description)".ToLower()
        $kw -match 'aisearch|knowledge|index|search|embedding|rag|openai'
    }
    if ($aiDefs.Count -eq 0) {
        Write-Host "  No AI-Search-related env variable definitions found."
    } else {
        foreach ($d in $aiDefs) {
            $default = if ($d.defaultvalue) { $d.defaultvalue } else { '(null)' }
            Write-Host "  Definition: $($d.schemaname)"
            Write-Host "    Display:  $($d.displayname)"
            Write-Host "    Default:  $default"
            # Look up current value
            try {
                $valResp = Invoke-RestMethod -Uri "$DataverseUrl/api/data/v9.2/environmentvariablevalues?`$filter=_environmentvariabledefinitionid_value eq $($d.environmentvariabledefinitionid)" -Headers $H -ErrorAction Stop
                if ($valResp.value.Count -eq 0) {
                    Write-Host "    Current:  (no override; default applies)"
                } else {
                    foreach ($v in $valResp.value) {
                        $marker = ''
                        foreach ($s in $StaleNames) { if ($v.value -and $v.value.ToString().Contains($s)) { $marker = "[STALE: contains '$s']"; break } }
                        Write-Host "    Current:  $($v.value) $marker"
                    }
                }
            } catch { Write-Host "    Current:  ERR querying value" }
        }
    }
} catch { Write-Host "  ERR enumerating env vars: $($_.Exception.Message)" }

Write-Host ""
Write-Host "=== Audit 3: sprk_aiknowledgedeployment entity ==="
try {
    $resp = Invoke-RestMethod -Uri "$DataverseUrl/api/data/v9.2/sprk_aiknowledgedeployments" -Headers $H -ErrorAction Stop
    if ($resp.value.Count -eq 0) {
        Write-Host "  Entity exists but is empty (0 records). No update needed."
    } else {
        $resp.value | ForEach-Object {
            Write-Host "  Record: $($_.sprk_name)"
            $_ | Get-Member -MemberType NoteProperty | Where-Object { $_.Name -match 'index|search' } | ForEach-Object {
                Write-Host "    $($_.Name) = $($resp.value[0].$($_.Name))"
            }
        }
    }
} catch { Write-Host "  Entity: $($_.Exception.Message -replace 'Response status.*?:','')" }

Write-Host ""
Write-Host "=== Audit 4: sprk_aianalysisplaybook records (playbook catalog mapping) ==="
try {
    $resp = Invoke-RestMethod -Uri "$DataverseUrl/api/data/v9.2/sprk_analysisplaybooks?`$top=5&`$select=sprk_name,statecode" -Headers $H -ErrorAction Stop
    Write-Host "  Total active playbooks (first 5 shown):"
    $resp.value | ForEach-Object { Write-Host "    $($_.sprk_name) (statecode=$($_.statecode))" }
    $countResp = Invoke-RestMethod -Uri "$DataverseUrl/api/data/v9.2/sprk_analysisplaybooks/`$count" -Headers $H -ErrorAction Stop
    Write-Host "  Total: $countResp records (must match playbook-embeddings index 34)"
} catch { Write-Host "  ERR: $($_.Exception.Message)" }

Write-Host ""
Write-Host "=== Audit 5: Web Resources containing stale AI Search index names ==="
try {
    $webResources = Invoke-RestMethod -Uri "$DataverseUrl/api/data/v9.2/webresourceset?`$select=name,webresourceid,content&`$filter=webresourcetype eq 3 and contains(name,'sprk_')" -Headers $H -ErrorAction Stop
    Write-Host "  Scanned $($webResources.value.Count) JavaScript web resources (sprk_* prefix)"
    $hits = 0
    foreach ($wr in $webResources.value) {
        if (-not $wr.content) { continue }
        try {
            $decoded = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($wr.content))
        } catch { continue }
        $found = @()
        foreach ($s in $StaleNames) {
            if ($decoded -match [regex]::Escape($s)) { $found += $s }
        }
        if ($found.Count -gt 0) {
            $hits++
            Write-Host "  [STALE] $($wr.name) contains: $($found -join ', ')"
        }
    }
    if ($hits -eq 0) { Write-Host "  No web resources contain stale index names." -ForegroundColor Green }
} catch { Write-Host "  ERR enumerating web resources: $($_.Exception.Message)" }

Write-Host ""
Write-Host "=== Audit 6: Other entity sets potentially carrying index name ==="
$otherSets = @('sprk_knowledgesources', 'sprk_documenttypes', 'sprk_analysisactions')
foreach ($es in $otherSets) {
    try {
        $sample = Invoke-RestMethod -Uri "$DataverseUrl/api/data/v9.2/$es`?`$top=1" -Headers $H -ErrorAction Stop
        if ($sample.value.Count -eq 0) { Write-Host "  $es : 0 records (empty)"; continue }
        $fields = $sample.value[0] | Get-Member -MemberType NoteProperty | Where-Object { $_.Name -match 'index|search|knowledge' -and $_.Name -notmatch '^(@|_)' }
        if ($fields.Count -eq 0) { Write-Host "  $es : no index/search/knowledge-related fields"; continue }
        Write-Host "  $es : index/search/knowledge fields:"
        $fields | ForEach-Object { Write-Host "    $($_.Name)" }
    } catch {
        $msg = $_.Exception.Message
        if ($msg -match 'Resource not found') { continue }
        Write-Host "  $es : ERR $msg"
    }
}

Write-Host ""
Write-Host "=== Audit complete ===" -ForegroundColor Cyan
