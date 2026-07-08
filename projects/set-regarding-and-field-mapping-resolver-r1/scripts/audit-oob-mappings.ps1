#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Audit overlaps between active `sprk_fieldmappingprofile` records and OOB
    Dataverse attribute-mappings on the same source→target 1:N relationships.

.DESCRIPTION
    Report-only. Enumerates every active `sprk_fieldmappingprofile`, resolves
    its source + target entity logical names via `sprk_recordtype_ref`, and for
    each (source, target) pair queries OOB `EntityMap` + expanded
    `AttributeMap` records. Flags any OOB mapping whose source-field or
    target-field appears in the profile's rule-set (potential collision with
    the Field Mapping subsystem).

    NO OOB records are modified. Output is a Markdown report intended for
    manual administrator review, per FR-B6-01 and Appendix A §A.6 of
    `projects/set-regarding-and-field-mapping-resolver-r1/spec.md`.

    Prerequisites:
      - PowerShell 7+
      - Azure CLI authenticated (`az login`) with an account having Dataverse
        read access on the target environment.

.PARAMETER DataverseUrl
    Target Dataverse environment URL. Defaults to $env:SPAARKE_DATAVERSE_URL,
    or `https://spaarkedev1.crm.dynamics.com` if neither is set.

.PARAMETER OutputPath
    Path to write the audit report. Defaults to
    `projects/set-regarding-and-field-mapping-resolver-r1/notes/oob-mapping-audit.md`
    resolved relative to this script's grandparent (the project folder).

.EXAMPLE
    .\audit-oob-mappings.ps1
    # Runs against spaarkedev1 (default) and writes the report to the notes/ folder.

.EXAMPLE
    .\audit-oob-mappings.ps1 -DataverseUrl "https://spaarkeuat.crm.dynamics.com"

.NOTES
    Idempotent — two runs produce identical output modulo the timestamp header.
    Report-only per FR-B6-01. No auto-deletion; no schema mutations.
#>

[CmdletBinding()]
param(
    [string]$DataverseUrl = ($env:SPAARKE_DATAVERSE_URL ?? "https://spaarkedev1.crm.dynamics.com"),
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
$DataverseUrl = $DataverseUrl.TrimEnd('/')

# Resolve project folder from script location so output paths work regardless of cwd.
$ScriptDir  = Split-Path -Parent $PSCommandPath
$ProjectDir = Split-Path -Parent $ScriptDir
if (-not $OutputPath) {
    $OutputPath = Join-Path $ProjectDir "notes/oob-mapping-audit.md"
}

# Human-friendly env label parsed from the URL.
$EnvLabel = ([System.Uri]$DataverseUrl).Host.Split('.')[0]

# ─────────────────────────────────────────────────────────────────────
# Auth + API helpers
# ─────────────────────────────────────────────────────────────────────

function Get-DataverseToken {
    param([string]$Url)
    try {
        $token = az account get-access-token --resource "$Url" --query accessToken -o tsv 2>$null
        if ($token -and $LASTEXITCODE -eq 0) {
            Write-Host "  Auth: Azure CLI token acquired." -ForegroundColor Gray
            return $token
        }
    } catch { }
    throw "Authentication failed. Run 'az login' first with a Dataverse-enabled account."
}

function Invoke-DataverseApi {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$Query,
        [switch]$AllPages
    )

    $headers = @{
        Authorization        = "Bearer $Token"
        Accept               = "application/json"
        "OData-Version"      = "4.0"
        "OData-MaxVersion"   = "4.0"
        Prefer               = "odata.include-annotations=*,odata.maxpagesize=5000"
    }

    $url = "$BaseUrl/api/data/v9.2/$Query"
    $allResults = @()

    do {
        try {
            $response = Invoke-RestMethod -Uri $url -Headers $headers -Method Get -ContentType "application/json"
        } catch {
            $statusCode = $_.Exception.Response.StatusCode.value__
            $errorBody  = $_.ErrorDetails.Message
            Write-Warning "API call failed ($statusCode) on $url : $errorBody"
            return @()
        }

        if ($null -ne $response.value) {
            $allResults += $response.value
        } elseif ($response) {
            return @($response)
        }

        $url = $response.'@odata.nextLink'
    } while ($AllPages -and $url)

    return $allResults
}

# ─────────────────────────────────────────────────────────────────────
# Discovery — active profiles + rules + recordtype resolution
# ─────────────────────────────────────────────────────────────────────

function Get-ActiveProfiles {
    param([string]$Token, [string]$BaseUrl)
    Write-Host "  Querying active sprk_fieldmappingprofile records..." -ForegroundColor Cyan
    $q = "sprk_fieldmappingprofiles?`$filter=statecode eq 0" +
         "&`$select=sprk_fieldmappingprofileid,sprk_name,_sprk_sourcerecordtype_value,_sprk_targetrecordtype_value"
    $profiles = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Query $q -AllPages
    Write-Host "    Found: $($profiles.Count) active profile(s)" -ForegroundColor Green
    return @($profiles)
}

function Get-RecordtypeMap {
    param([string]$Token, [string]$BaseUrl, [string[]]$Ids)
    if ($Ids.Count -eq 0) { return @{} }
    $map = @{}
    foreach ($id in ($Ids | Sort-Object -Unique)) {
        $q  = "sprk_recordtype_refs($id)?`$select=sprk_recordlogicalname,sprk_recordtypename"
        $rt = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Query $q
        if ($rt -and $rt.Count -gt 0) {
            $map[$id] = @{
                LogicalName = $rt[0].sprk_recordlogicalname
                DisplayName = $rt[0].sprk_recordtypename
            }
        }
    }
    return $map
}

function Get-RulesForProfile {
    param([string]$Token, [string]$BaseUrl, [string]$ProfileId)
    $q = "sprk_fieldmappingrules?`$filter=_sprk_fieldmappingprofile_value eq $ProfileId and statecode eq 0" +
         "&`$select=sprk_fieldmappingruleid,sprk_sourcefield,sprk_targetfield,sprk_executionorder"
    return @(Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Query $q -AllPages)
}

# ─────────────────────────────────────────────────────────────────────
# Discovery — OOB EntityMap + AttributeMap
# ─────────────────────────────────────────────────────────────────────

function Get-EntityMapsForPair {
    param([string]$Token, [string]$BaseUrl, [string]$Source, [string]$Target)
    # Note: `sourceentityname` / `targetentityname` are the logical names on
    # the EntityMap entity. The collection-valued nav prop for AttributeMap
    # doesn't support $expand in this environment, so we query children
    # separately by `_entitymapid_value` filter (see Get-AttributeMapsForEntityMap).
    $q = "entitymaps?`$filter=sourceentityname eq '$Source' and targetentityname eq '$Target'" +
         "&`$select=entitymapid,sourceentityname,targetentityname"
    return @(Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Query $q -AllPages)
}

function Get-AttributeMapsForEntityMap {
    param([string]$Token, [string]$BaseUrl, [string]$EntityMapId)
    $q = "attributemaps?`$filter=_entitymapid_value eq $EntityMapId" +
         "&`$select=attributemapid,sourceattributename,targetattributename"
    return @(Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Query $q -AllPages)
}

function Get-RelationshipName {
    param([string]$Token, [string]$BaseUrl, [string]$Source, [string]$Target)
    # Best-effort — look up the 1:N relationship name for context in the report.
    # Silent-fail if the relationship metadata query returns nothing.
    $q = "EntityDefinitions(LogicalName='$Source')/OneToManyRelationships?`$filter=ReferencingEntity eq '$Target'&`$select=SchemaName,ReferencingEntity,ReferencedEntity"
    $rels = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Query $q
    if ($rels -and $rels.Count -gt 0) {
        return ($rels | ForEach-Object { $_.SchemaName }) -join ", "
    }
    return "(no 1:N found — check EntityMap for cross-relationship)"
}

# ─────────────────────────────────────────────────────────────────────
# Main audit flow
# ─────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "========================================================" -ForegroundColor Yellow
Write-Host " OOB Mapping Audit — $EnvLabel" -ForegroundColor Yellow
Write-Host "========================================================" -ForegroundColor Yellow
Write-Host "  URL: $DataverseUrl"
Write-Host "  Output: $OutputPath"

$token = Get-DataverseToken -Url $DataverseUrl

$profiles = Get-ActiveProfiles -Token $token -BaseUrl $DataverseUrl

# Collect recordtype GUIDs (source + target) for one batched resolution pass.
$rtIds = @()
foreach ($p in $profiles) {
    if ($p.'_sprk_sourcerecordtype_value') { $rtIds += $p.'_sprk_sourcerecordtype_value' }
    if ($p.'_sprk_targetrecordtype_value') { $rtIds += $p.'_sprk_targetrecordtype_value' }
}
$rtMap = Get-RecordtypeMap -Token $token -BaseUrl $DataverseUrl -Ids $rtIds

# Build enriched profile records with resolved source/target entity names +
# the rule field-set (source-fields ∪ target-fields, lowercase-normalized).
$enrichedProfiles = @()
foreach ($p in $profiles) {
    $srcId = $p.'_sprk_sourcerecordtype_value'
    $tgtId = $p.'_sprk_targetrecordtype_value'
    $srcEntity = if ($srcId -and $rtMap.ContainsKey($srcId)) { $rtMap[$srcId].LogicalName } else { $null }
    $tgtEntity = if ($tgtId -and $rtMap.ContainsKey($tgtId)) { $rtMap[$tgtId].LogicalName } else { $null }

    $rules = Get-RulesForProfile -Token $token -BaseUrl $DataverseUrl -ProfileId $p.sprk_fieldmappingprofileid

    $ruleFieldSet = New-Object System.Collections.Generic.HashSet[string]
    foreach ($r in $rules) {
        if ($r.sprk_sourcefield) { [void]$ruleFieldSet.Add($r.sprk_sourcefield.ToLowerInvariant()) }
        if ($r.sprk_targetfield) { [void]$ruleFieldSet.Add($r.sprk_targetfield.ToLowerInvariant()) }
    }

    $enrichedProfiles += [pscustomobject]@{
        ProfileId    = $p.sprk_fieldmappingprofileid
        ProfileName  = $p.sprk_name
        SourceEntity = $srcEntity
        TargetEntity = $tgtEntity
        RuleCount    = @($rules).Count
        RuleFieldSet = $ruleFieldSet
    }
}

# For each unique (source, target) pair on an active profile, query OOB EntityMap.
$auditRows = @()
$pairsSeen = @{}
foreach ($ep in $enrichedProfiles) {
    if (-not $ep.SourceEntity -or -not $ep.TargetEntity) {
        Write-Warning "  Skipping profile $($ep.ProfileName): could not resolve source ($($ep.SourceEntity)) / target ($($ep.TargetEntity)) entity."
        continue
    }
    $pairKey = "$($ep.SourceEntity)->$($ep.TargetEntity)"
    if ($pairsSeen.ContainsKey($pairKey)) { continue }
    $pairsSeen[$pairKey] = $true

    Write-Host "  Auditing OOB mappings for $pairKey..." -ForegroundColor Cyan
    $relName    = Get-RelationshipName -Token $token -BaseUrl $DataverseUrl -Source $ep.SourceEntity -Target $ep.TargetEntity
    $entityMaps = Get-EntityMapsForPair    -Token $token -BaseUrl $DataverseUrl -Source $ep.SourceEntity -Target $ep.TargetEntity

    if (-not $entityMaps -or $entityMaps.Count -eq 0) {
        Write-Host "    No OOB EntityMap found for $pairKey." -ForegroundColor Gray
        $auditRows += [pscustomobject]@{
            SourceEntity = $ep.SourceEntity
            TargetEntity = $ep.TargetEntity
            Relationship = $relName
            SourceField  = "(no OOB EntityMap)"
            TargetField  = "(no OOB EntityMap)"
            Collision    = "N"
            EntityMapId  = ""
            AttributeMapId = ""
            ProfileName  = $ep.ProfileName
        }
        continue
    }

    foreach ($em in $entityMaps) {
        $attrMaps = Get-AttributeMapsForEntityMap -Token $token -BaseUrl $DataverseUrl -EntityMapId $em.entitymapid
        if ($attrMaps.Count -eq 0) {
            $auditRows += [pscustomobject]@{
                SourceEntity   = $ep.SourceEntity
                TargetEntity   = $ep.TargetEntity
                Relationship   = $relName
                SourceField    = "(EntityMap has no attribute-maps)"
                TargetField    = "(EntityMap has no attribute-maps)"
                Collision      = "N"
                EntityMapId    = $em.entitymapid
                AttributeMapId = ""
                ProfileName    = $ep.ProfileName
            }
            continue
        }
        foreach ($am in $attrMaps) {
            $srcField = $am.sourceattributename
            $tgtField = $am.targetattributename
            # Skip the auto-generated PK→lookup FK mapping (system-generated, not admin-authored).
            # E.g. sprk_matterid → sprk_regardingmatter. Not useful in a collision report.
            $collision = "N"
            if ($srcField -and $ep.RuleFieldSet.Contains($srcField.ToLowerInvariant())) { $collision = "Y" }
            if ($tgtField -and $ep.RuleFieldSet.Contains($tgtField.ToLowerInvariant())) { $collision = "Y" }
            $auditRows += [pscustomobject]@{
                SourceEntity   = $ep.SourceEntity
                TargetEntity   = $ep.TargetEntity
                Relationship   = $relName
                SourceField    = $srcField
                TargetField    = $tgtField
                Collision      = $collision
                EntityMapId    = $em.entitymapid
                AttributeMapId = $am.attributemapid
                ProfileName    = $ep.ProfileName
            }
        }
    }
}

# ─────────────────────────────────────────────────────────────────────
# Report authoring
# ─────────────────────────────────────────────────────────────────────

$today = (Get-Date -Format "yyyy-MM-dd")
$collisions = @($auditRows | Where-Object { $_.Collision -eq "Y" })
$oobMappingsWithData = @($auditRows | Where-Object { $_.SourceField -notmatch '^\(' })

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("# OOB Dataverse Mapping Audit")
[void]$sb.AppendLine()
[void]$sb.AppendLine("> **Date**: $today · **Environment**: $EnvLabel ($DataverseUrl)")
[void]$sb.AppendLine("> **Purpose**: Enumerate overlaps between active ``sprk_fieldmappingprofile`` records and OOB Dataverse ``EntityMap`` / ``AttributeMap`` records on the same source→target relationships.")
[void]$sb.AppendLine("> **Delivered by**: task SRFR-070 (FR-B6-01) in project ``set-regarding-and-field-mapping-resolver-r1``.")
[void]$sb.AppendLine("> **Policy cross-reference**: spec ``Appendix A §A.6`` — OOB-mapping mutual-exclusivity anti-pattern.")
[void]$sb.AppendLine()
[void]$sb.AppendLine("**REPORT-ONLY** — no OOB ``EntityMap`` or ``AttributeMap`` records were modified. Administrator review required for any flagged collisions.")
[void]$sb.AppendLine()
[void]$sb.AppendLine("---")
[void]$sb.AppendLine()
[void]$sb.AppendLine("## Summary")
[void]$sb.AppendLine()
[void]$sb.AppendLine("| Metric | Value |")
[void]$sb.AppendLine("|---|---|")
[void]$sb.AppendLine("| Active profiles reviewed | $($enrichedProfiles.Count) |")
[void]$sb.AppendLine("| Unique (source, target) pairs audited | $($pairsSeen.Keys.Count) |")
[void]$sb.AppendLine("| OOB attribute-mappings discovered | $($oobMappingsWithData.Count) |")
[void]$sb.AppendLine("| Collisions (OOB field also in profile rule-set) | $($collisions.Count) |")
[void]$sb.AppendLine()
[void]$sb.AppendLine("### Profiles reviewed")
[void]$sb.AppendLine()
[void]$sb.AppendLine("| Profile name | Source entity | Target entity | Rule count |")
[void]$sb.AppendLine("|---|---|---|---|")
if ($enrichedProfiles.Count -eq 0) {
    [void]$sb.AppendLine("| _(no active profiles found)_ | — | — | — |")
} else {
    foreach ($ep in ($enrichedProfiles | Sort-Object ProfileName)) {
        $srcCell = if ($ep.SourceEntity) { $ep.SourceEntity } else { "(unresolved)" }
        $tgtCell = if ($ep.TargetEntity) { $ep.TargetEntity } else { "(unresolved)" }
        [void]$sb.AppendLine("| $($ep.ProfileName) | ``$srcCell`` | ``$tgtCell`` | $($ep.RuleCount) |")
    }
}
[void]$sb.AppendLine()
[void]$sb.AppendLine("---")
[void]$sb.AppendLine()
[void]$sb.AppendLine("## Overlap table")
[void]$sb.AppendLine()

if ($enrichedProfiles.Count -eq 0) {
    [void]$sb.AppendLine("No active ``sprk_fieldmappingprofile`` records were found in this environment. Audit is a no-op until at least one profile is authored. Re-run this script after profiles are added.")
} elseif ($oobMappingsWithData.Count -eq 0) {
    [void]$sb.AppendLine("**No OOB attribute-mappings detected on the surveyed (source, target) pairs.** All active profiles are free of OOB overlap in the current environment ($EnvLabel).")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("Pairs surveyed:")
    [void]$sb.AppendLine()
    foreach ($k in ($pairsSeen.Keys | Sort-Object)) {
        [void]$sb.AppendLine("- ``$k``")
    }
} else {
    [void]$sb.AppendLine("| Source entity | Target entity | OOB relationship | OOB source-field | OOB target-field | Collision | Profile |")
    [void]$sb.AppendLine("|---|---|---|---|---|---|---|")
    foreach ($row in ($auditRows | Sort-Object SourceEntity, TargetEntity, SourceField)) {
        if ($row.SourceField -match '^\(') { continue }  # skip synthetic empty rows
        $collisionCell = if ($row.Collision -eq "Y") { "**Y**" } else { "N" }
        [void]$sb.AppendLine("| ``$($row.SourceEntity)`` | ``$($row.TargetEntity)`` | ``$($row.Relationship)`` | ``$($row.SourceField)`` | ``$($row.TargetField)`` | $collisionCell | $($row.ProfileName) |")
    }
}
[void]$sb.AppendLine()
[void]$sb.AppendLine("---")
[void]$sb.AppendLine()
[void]$sb.AppendLine("## Recommendation")
[void]$sb.AppendLine()

if ($collisions.Count -gt 0) {
    [void]$sb.AppendLine("**$($collisions.Count) OOB attribute-mapping(s) flagged as collisions** with the active profile rule-set. Administrator MUST review each flagged mapping and choose one of:")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("1. **Delete the OOB attribute-mapping** — recommended default; the Field Mapping subsystem is the single source of truth per Appendix A §A.6.")
    [void]$sb.AppendLine("2. **Delete the profile rule** — only if the OOB mapping's semantics fully satisfy the intent and no cascade / manual-refresh behavior is required.")
    [void]$sb.AppendLine("3. **Document a scoped exception** — extremely rare; requires design-doc note explaining why the double-write is acceptable.")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("Flagged rows:")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("| Source | Target | OOB (src → tgt) | AttributeMap ID | Profile |")
    [void]$sb.AppendLine("|---|---|---|---|---|")
    foreach ($c in $collisions) {
        [void]$sb.AppendLine("| ``$($c.SourceEntity)`` | ``$($c.TargetEntity)`` | ``$($c.SourceField)`` → ``$($c.TargetField)`` | ``$($c.AttributeMapId)`` | $($c.ProfileName) |")
    }
} elseif ($oobMappingsWithData.Count -gt 0) {
    [void]$sb.AppendLine("**$($oobMappingsWithData.Count) OOB attribute-mapping(s) present but no field collisions detected.** These OOB mappings currently target different fields than the active profile rules.")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("**Action**: Review each OOB mapping to confirm its intent is compatible with the Field Mapping subsystem. Any new profile rules on these fields will create a collision — re-run this audit after adding rules.")
} else {
    [void]$sb.AppendLine("**No action required.** No OOB attribute-mappings exist on the surveyed (source, target) pairs. The Field Mapping subsystem is the sole automation path for these entity relationships.")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("Re-run this audit whenever:")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("- A new ``sprk_fieldmappingprofile`` is activated on a new (source, target) pair.")
    [void]$sb.AppendLine("- An administrator adds an OOB attribute-mapping via the classic solution explorer.")
    [void]$sb.AppendLine("- Environment is promoted to UAT / production (baseline the new environment).")
}
[void]$sb.AppendLine()
[void]$sb.AppendLine("---")
[void]$sb.AppendLine()
[void]$sb.AppendLine("## Policy reference")
[void]$sb.AppendLine()
[void]$sb.AppendLine("Per ``spec.md`` Appendix A §A.6 (OOB Dataverse mapping mutual-exclusivity, anti-pattern):")
[void]$sb.AppendLine()
[void]$sb.AppendLine("> A source→target entity pair with an active ``sprk_fieldmappingprofile`` MUST NOT ALSO have overlapping OOB attribute-mappings on any 1:N relationship between them. Overlap causes ambiguity, diagnosis pain, and drift.")
[void]$sb.AppendLine()
[void]$sb.AppendLine("This audit is the enforcement surface. It is report-only by design — administrator judgment is required for each remediation.")
[void]$sb.AppendLine()
[void]$sb.AppendLine("---")
[void]$sb.AppendLine()
[void]$sb.AppendLine("*Generated by ``projects/set-regarding-and-field-mapping-resolver-r1/scripts/audit-oob-mappings.ps1``. Idempotent — re-run anytime to refresh.*")

# Ensure notes/ folder exists.
$notesDir = Split-Path -Parent $OutputPath
if (-not (Test-Path $notesDir)) {
    New-Item -ItemType Directory -Path $notesDir -Force | Out-Null
}

Set-Content -Path $OutputPath -Value $sb.ToString() -Encoding UTF8

Write-Host ""
Write-Host "  Report written: $OutputPath" -ForegroundColor Green
Write-Host "  Profiles reviewed: $($enrichedProfiles.Count)" -ForegroundColor Green
Write-Host "  OOB mappings discovered: $($oobMappingsWithData.Count)" -ForegroundColor Green
Write-Host "  Collisions flagged: $($collisions.Count)" -ForegroundColor $(if ($collisions.Count -gt 0) { "Yellow" } else { "Green" })
Write-Host ""

exit 0
