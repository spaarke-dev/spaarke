# Remove-SprkEventTodoFields.ps1
# smart-todo-decoupling-r3 / task 004 — schema cut.
# Removes 4 legacy to-do fields from sprk_event:
#   sprk_todoflag (bool), sprk_todostatus (picklist), sprk_todocolumn (picklist), sprk_todopinned (bool)
# Keeps: sprk_priorityscore, sprk_effortscore, sprk_duedate (per FR-03 / D-4).
#
# Approach: direct Web API DELETE on attribute metadata against spaarkedev1.
# Pre-flight: re-check field exists; capture metadata id; DELETE; verify gone.
# Post-step: PublishAllXml to refresh customizations.

$ErrorActionPreference = "Stop"
$Environment = "spaarkedev1.crm.dynamics.com"
$BaseUrl = "https://$Environment/api/data/v9.2"

$token = (az account get-access-token --resource "https://$Environment" --query accessToken -o tsv)
if (-not $token) { throw "Failed to obtain Dataverse access token." }

$headers = @{
    "Authorization" = "Bearer $token"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
    "Content-Type" = "application/json"
    "Accept" = "application/json"
}

$fieldsToRemove = @("sprk_todoflag", "sprk_todostatus", "sprk_todocolumn", "sprk_todopinned")
$fieldsToKeep   = @("sprk_priorityscore", "sprk_effortscore", "sprk_duedate")

Write-Host "=== task 004: Remove 4 to-do fields from sprk_event ===" -ForegroundColor Cyan
Write-Host "Environment: $Environment" -ForegroundColor Gray
Write-Host ("Timestamp:   {0}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss zzz")) -ForegroundColor Gray

# --- Pre-flight: capture metadata IDs for the cut log ---
Write-Host "`n-- Pre-flight: capture attribute metadata ids --" -ForegroundColor Yellow
$preIds = @{}
foreach ($f in $fieldsToRemove) {
    $url = "$BaseUrl/EntityDefinitions(LogicalName='sprk_event')/Attributes(LogicalName='$f')?`$select=MetadataId,LogicalName,AttributeType,SchemaName"
    try {
        $r = Invoke-RestMethod -Uri $url -Headers $headers -Method Get
        $preIds[$f] = @{ MetadataId = $r.MetadataId; AttributeType = $r.AttributeType; SchemaName = $r.SchemaName }
        Write-Host ("  [{0}] present  MetadataId={1}  Type={2}  Schema={3}" -f $f, $r.MetadataId, $r.AttributeType, $r.SchemaName) -ForegroundColor Green
    } catch {
        Write-Host ("  [{0}] NOT FOUND -- skipping" -f $f) -ForegroundColor Yellow
    }
}

# --- Pre-flight: confirm fields to keep are still present ---
Write-Host "`n-- Pre-flight: verify fields to RETAIN --" -ForegroundColor Yellow
foreach ($f in $fieldsToKeep) {
    $url = "$BaseUrl/EntityDefinitions(LogicalName='sprk_event')/Attributes(LogicalName='$f')?`$select=LogicalName,AttributeType"
    try {
        $r = Invoke-RestMethod -Uri $url -Headers $headers -Method Get
        Write-Host ("  [KEEP] {0} present (Type={1})" -f $f, $r.AttributeType) -ForegroundColor Green
    } catch {
        Write-Host ("  [KEEP] {0} MISSING -- regression!" -f $f) -ForegroundColor Red
        throw "Retention regression: $f is missing from sprk_event."
    }
}

# --- DELETE each attribute ---
Write-Host "`n-- DELETE attributes --" -ForegroundColor Yellow
$deleted = @()
foreach ($f in $fieldsToRemove) {
    if (-not $preIds.ContainsKey($f)) {
        Write-Host ("  [{0}] already absent -- skipped" -f $f) -ForegroundColor Yellow
        continue
    }
    $url = "$BaseUrl/EntityDefinitions(LogicalName='sprk_event')/Attributes(LogicalName='$f')"
    try {
        Invoke-RestMethod -Uri $url -Headers $headers -Method Delete | Out-Null
        Write-Host ("  [{0}] DELETED" -f $f) -ForegroundColor Green
        $deleted += $f
    } catch {
        Write-Host ("  [{0}] DELETE FAILED: {1}" -f $f, $_.Exception.Message) -ForegroundColor Red
        $errBody = $null
        try { $errBody = $_.ErrorDetails.Message } catch {}
        if ($errBody) { Write-Host ("      Body: {0}" -f $errBody) -ForegroundColor Red }
        throw
    }
}

# --- Publish customizations ---
Write-Host "`n-- Publish customizations (sprk_event) --" -ForegroundColor Yellow
$publishBody = @{ ParameterXml = "<importexportxml><entities><entity>sprk_event</entity></entities></importexportxml>" } | ConvertTo-Json -Compress
$publishHeaders = $headers.Clone()
$publishHeaders["Content-Type"] = "application/json"
try {
    Invoke-RestMethod -Uri "$BaseUrl/PublishXml" -Headers $publishHeaders -Method Post -Body $publishBody | Out-Null
    Write-Host "  PublishXml succeeded." -ForegroundColor Green
} catch {
    Write-Host ("  PublishXml FAILED: {0}" -f $_.Exception.Message) -ForegroundColor Red
    throw
}

# --- Post-cut verification: removed fields ---
Write-Host "`n-- Post-cut: verify removed fields are gone --" -ForegroundColor Yellow
foreach ($f in $fieldsToRemove) {
    $url = "$BaseUrl/EntityDefinitions(LogicalName='sprk_event')/Attributes(LogicalName='$f')?`$select=LogicalName"
    try {
        $r = Invoke-RestMethod -Uri $url -Headers $headers -Method Get
        Write-Host ("  [{0}] STILL PRESENT! (regression)" -f $f) -ForegroundColor Red
        throw "Field $f still exists after delete attempt."
    } catch {
        Write-Host ("  [{0}] confirmed absent" -f $f) -ForegroundColor Green
    }
}

# --- Post-cut verification: retained fields ---
Write-Host "`n-- Post-cut: verify retained fields are still present --" -ForegroundColor Yellow
foreach ($f in $fieldsToKeep) {
    $url = "$BaseUrl/EntityDefinitions(LogicalName='sprk_event')/Attributes(LogicalName='$f')?`$select=LogicalName"
    try {
        $r = Invoke-RestMethod -Uri $url -Headers $headers -Method Get
        Write-Host ("  [{0}] still present" -f $f) -ForegroundColor Green
    } catch {
        Write-Host ("  [{0}] MISSING (regression!)" -f $f) -ForegroundColor Red
        throw "Retention field $f is missing after delete."
    }
}

Write-Host "`n=== task 004 schema cut COMPLETE ===" -ForegroundColor Cyan
Write-Host "`nCapture for cut log:" -ForegroundColor Gray
foreach ($f in $deleted) {
    $info = $preIds[$f]
    Write-Host ("  {0}  MetadataId={1}  Type={2}  SchemaName={3}" -f $f, $info.MetadataId, $info.AttributeType, $info.SchemaName) -ForegroundColor Gray
}
