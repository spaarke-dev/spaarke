<#
.SYNOPSIS
    Deploys the sprk_precedent entity + relationships to Dataverse (Spaarke Dev).

.DESCRIPTION
    Creates the sprk_precedent entity, the sprk_precedentstatus global option set,
    all attributes, N:N relationships to sprk_matter (supporting matters) and
    self (related precedents). Adds entity to the spaarke_insights solution
    (creates solution if missing). Per task 011 (D-P3) of the Spaarke Insights
    Engine Phase 1 project.

    Implements SPEC-phase-1-minimum.md §2 (Precedent SME mockup) data model.
    Manual SME authoring mode only — per D-61 two-mode authoring (Phase 1).

    sprk_precedent_observation N:N is deferred — sprk_observation entity does
    not exist in Phase 1; will be added when D-P11 lands.

.PARAMETER EnvironmentUrl
    The Dataverse environment URL (default: spaarkedev1.crm.dynamics.com)

.PARAMETER SolutionName
    The Dataverse solution unique name to add the entity to (default: spaarke_insights)

.EXAMPLE
    .\Deploy-PrecedentEntity.ps1

.NOTES
    Project: ai-spaarke-insights-engine-r1
    Task:    011 — D-P3 sprk_precedent entity + relationship tables
    Created: 2026-05-28
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",

    [Parameter(Mandatory = $false)]
    [string]$SolutionName = "spaarke_insights"
)

$ErrorActionPreference = "Stop"

# ============================================================================
# Helper Functions
# ============================================================================

function Get-DataverseToken {
    param([string]$EnvironmentUrl)
    Write-Host "Getting authentication token from Azure CLI..." -ForegroundColor Cyan
    $tokenResult = az account get-access-token --resource $EnvironmentUrl --query "accessToken" -o tsv 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to get token from Azure CLI. Error: $tokenResult. Make sure you're logged in with 'az login'"
    }
    return $tokenResult.Trim()
}

function Invoke-DataverseApi {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$Endpoint,
        [string]$Method = "GET",
        [object]$Body = $null,
        [hashtable]$ExtraHeaders = @{}
    )

    $headers = @{
        "Authorization"    = "Bearer $Token"
        "OData-MaxVersion" = "4.0"
        "OData-Version"    = "4.0"
        "Accept"           = "application/json"
        "Content-Type"     = "application/json; charset=utf-8"
        "Prefer"           = "odata.include-annotations=*"
    }
    foreach ($k in $ExtraHeaders.Keys) { $headers[$k] = $ExtraHeaders[$k] }

    $uri = "$BaseUrl/api/data/v9.2/$Endpoint"
    $params = @{ Uri = $uri; Method = $Method; Headers = $headers }
    if ($Body) { $params.Body = ($Body | ConvertTo-Json -Depth 20) }

    try {
        return Invoke-RestMethod @params
    }
    catch {
        $errorDetails = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            $errorJson = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($errorJson.error.message) { $errorDetails = $errorJson.error.message }
        }
        throw "API Error ($Method $Endpoint): $errorDetails"
    }
}

function New-Label {
    param([string]$Text)
    return @{
        "@odata.type"     = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(
            @{
                "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                "Label"       = $Text
                "LanguageCode" = 1033
            }
        )
    }
}

function Test-EntityExists {
    param([string]$Token, [string]$BaseUrl, [string]$LogicalName)
    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Endpoint "EntityDefinitions(LogicalName='$LogicalName')" -Method "GET" | Out-Null
        return $true
    } catch {
        if ($_.Exception.Message -match "does not exist|404|9870979") { return $false }
        throw
    }
}

function Test-AttributeExists {
    param([string]$Token, [string]$BaseUrl, [string]$EntityLogicalName, [string]$AttributeLogicalName)
    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')" -Method "GET" | Out-Null
        return $true
    } catch { return $false }
}

function Test-RelationshipExists {
    param([string]$Token, [string]$BaseUrl, [string]$SchemaName)
    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "RelationshipDefinitions(SchemaName='$SchemaName')" -Method "GET" | Out-Null
        return $true
    } catch { return $false }
}

function Get-GlobalOptionSet {
    param([string]$Token, [string]$BaseUrl, [string]$Name)
    try {
        return Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "GlobalOptionSetDefinitions(Name='$Name')" -Method "GET"
    } catch { return $null }
}

function Test-SolutionExists {
    param([string]$Token, [string]$BaseUrl, [string]$UniqueName)
    try {
        $r = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "solutions?`$filter=uniquename eq '$UniqueName'&`$select=solutionid,uniquename" -Method "GET"
        return $r.value.Count -gt 0
    } catch { return $false }
}

function New-Solution {
    param([string]$Token, [string]$BaseUrl, [string]$UniqueName, [string]$FriendlyName, [string]$Description)

    # Get Spaarke publisher id
    $pub = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "publishers?`$filter=uniquename eq 'Spaarke'&`$select=publisherid,uniquename,customizationprefix" -Method "GET"
    if ($pub.value.Count -eq 0) {
        # Fall back to lowercase
        $pub = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "publishers?`$filter=customizationprefix eq 'sprk'&`$select=publisherid,uniquename,customizationprefix" -Method "GET"
    }
    if ($pub.value.Count -eq 0) {
        throw "Could not find Spaarke publisher (looked up by uniquename='Spaarke' and prefix='sprk')."
    }
    $publisherId = $pub.value[0].publisherid
    Write-Host "  Publisher: $($pub.value[0].uniquename) ($publisherId)" -ForegroundColor Gray

    $solutionDef = @{
        "uniquename"     = $UniqueName
        "friendlyname"   = $FriendlyName
        "description"    = $Description
        "version"        = "1.0.0.0"
        "publisherid@odata.bind" = "/publishers($publisherId)"
    }

    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "solutions" -Method "POST" -Body $solutionDef | Out-Null
    Write-Host "  Created solution: $UniqueName" -ForegroundColor Green
}

function Add-SolutionComponent {
    param(
        [string]$Token, [string]$BaseUrl,
        [string]$ComponentId,
        [int]$ComponentType,
        [string]$SolutionUniqueName,
        [bool]$AddRequiredComponents = $false
    )
    try {
        $body = @{
            "ComponentId"           = $ComponentId
            "ComponentType"         = $ComponentType
            "SolutionUniqueName"    = $SolutionUniqueName
            "AddRequiredComponents" = $AddRequiredComponents
        }
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Endpoint "AddSolutionComponent" -Method "POST" -Body $body | Out-Null
        Write-Host "    Added component $ComponentId (type $ComponentType) to $SolutionUniqueName" -ForegroundColor Green
    } catch {
        Write-Host "    Warning: AddSolutionComponent failed for $ComponentId (type $ComponentType): $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

# ============================================================================
# Main
# ============================================================================

Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host " Deploy sprk_precedent Entity (Task 011 / D-P3)" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl" -ForegroundColor Yellow
Write-Host "Solution   : $SolutionName" -ForegroundColor Yellow
Write-Host ""

$token = Get-DataverseToken -EnvironmentUrl $EnvironmentUrl
Write-Host "Authentication successful" -ForegroundColor Green
Write-Host ""

# ---------------------------------------------------------------------------
# Step 1: Ensure solution exists
# ---------------------------------------------------------------------------
Write-Host "Step 1: Ensure solution '$SolutionName' exists..." -ForegroundColor Cyan
if (Test-SolutionExists -Token $token -BaseUrl $EnvironmentUrl -UniqueName $SolutionName) {
    Write-Host "  Solution exists." -ForegroundColor Green
} else {
    Write-Host "  Solution not found; creating..." -ForegroundColor Gray
    New-Solution -Token $token -BaseUrl $EnvironmentUrl `
        -UniqueName $SolutionName `
        -FriendlyName "Spaarke Insights" `
        -Description "Spaarke Insights Engine — Precedent entity + relationships (Phase 1, D-P3)"
}
Write-Host ""

# ---------------------------------------------------------------------------
# Step 2: Create global option set sprk_precedentstatus
# ---------------------------------------------------------------------------
Write-Host "Step 2: Create global option set sprk_precedentstatus..." -ForegroundColor Cyan
$statusOptionSet = Get-GlobalOptionSet -Token $token -BaseUrl $EnvironmentUrl -Name "sprk_precedentstatus"
if ($statusOptionSet) {
    Write-Host "  Option set sprk_precedentstatus already exists." -ForegroundColor Green
} else {
    $opts = @(
        @{ "Value" = 100000000; "Label" = (New-Label "Tentative") }
        @{ "Value" = 100000001; "Label" = (New-Label "Confirmed") }
        @{ "Value" = 100000002; "Label" = (New-Label "Under Drift Review") }
        @{ "Value" = 100000003; "Label" = (New-Label "Deprecated") }
        @{ "Value" = 100000004; "Label" = (New-Label "Retired") }
    )
    $osDef = @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.OptionSetMetadata"
        "Name"          = "sprk_precedentstatus"
        "DisplayName"   = (New-Label "Precedent Status")
        "Description"   = (New-Label "Lifecycle status for a Precedent (D-46/D-61).")
        "IsGlobal"      = $true
        "OptionSetType" = "Picklist"
        "Options"       = $opts
    }
    Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
        -Endpoint "GlobalOptionSetDefinitions" -Method "POST" -Body $osDef `
        -ExtraHeaders @{ "MSCRM.SolutionUniqueName" = $SolutionName } | Out-Null
    Write-Host "  Created sprk_precedentstatus (5 values)" -ForegroundColor Green
    $statusOptionSet = Get-GlobalOptionSet -Token $token -BaseUrl $EnvironmentUrl -Name "sprk_precedentstatus"
}
$statusOptionSetId = $statusOptionSet.MetadataId
Write-Host "  Option set MetadataId: $statusOptionSetId" -ForegroundColor Gray
Write-Host ""

# ---------------------------------------------------------------------------
# Step 3: Create sprk_precedent entity (primary name only)
# ---------------------------------------------------------------------------
Write-Host "Step 3: Create sprk_precedent entity..." -ForegroundColor Cyan
if (Test-EntityExists -Token $token -BaseUrl $EnvironmentUrl -LogicalName "sprk_precedent") {
    Write-Host "  Entity sprk_precedent already exists." -ForegroundColor Green
} else {
    $entityDef = @{
        "@odata.type"           = "Microsoft.Dynamics.CRM.EntityMetadata"
        "SchemaName"            = "sprk_Precedent"
        "DisplayName"           = (New-Label "Precedent")
        "DisplayCollectionName" = (New-Label "Precedents")
        "Description"           = (New-Label "Insights Engine — SME-confirmed pattern across many Observations (D-46). Phase 1 ships manual SME authoring mode only (D-61).")
        "OwnershipType"         = "OrganizationOwned"
        "IsActivity"            = $false
        "HasNotes"              = $true
        "HasActivities"         = $false
        "PrimaryNameAttribute"  = "sprk_name"
        "Attributes"            = @(
            @{
                "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
                "SchemaName"    = "sprk_Name"
                "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
                "MaxLength"     = 200
                "DisplayName"   = (New-Label "Pattern Title")
                "Description"   = (New-Label "Short title for the Precedent (e.g., 'IP licensing matters with BigFirm LLP')")
                "IsPrimaryName" = $true
                "FormatName"    = @{ "Value" = "Text" }
            }
        )
    }
    Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
        -Endpoint "EntityDefinitions" -Method "POST" -Body $entityDef `
        -ExtraHeaders @{ "MSCRM.SolutionUniqueName" = $SolutionName } | Out-Null
    Write-Host "  Entity sprk_precedent created" -ForegroundColor Green
}
Write-Host ""

# ---------------------------------------------------------------------------
# Step 4: Add attributes (per SPEC-phase-1-minimum.md §2 mockup)
# ---------------------------------------------------------------------------
Write-Host "Step 4: Add attributes to sprk_precedent..." -ForegroundColor Cyan

$attributes = @(
    # sprk_patternstatement — Memo (multiline) 4000
    @{
        "name" = "sprk_patternstatement"
        "def"  = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
            "SchemaName"    = "sprk_PatternStatement"
            "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
            "MaxLength"     = 4000
            "DisplayName"   = (New-Label "Pattern Statement")
            "Description"   = (New-Label "Human-readable pattern statement; the artifact synthesis playbooks cite by name.")
            "Format"        = "TextArea"
        }
    },
    # sprk_status — Picklist (global option set)
    @{
        "name" = "sprk_status"
        "def"  = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
            "SchemaName"    = "sprk_Status"
            "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
            "DisplayName"   = (New-Label "Status")
            "Description"   = (New-Label "Lifecycle status (Tentative/Confirmed/UnderDriftReview/Deprecated/Retired)")
            "GlobalOptionSet@odata.bind" = "/GlobalOptionSetDefinitions($statusOptionSetId)"
        }
    },
    # sprk_reviewdate — DateTime
    @{
        "name" = "sprk_reviewdate"
        "def"  = @{
            "@odata.type"        = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
            "SchemaName"         = "sprk_ReviewDate"
            "RequiredLevel"      = @{ "Value" = "None" }
            "DisplayName"        = (New-Label "Review Date")
            "Description"        = (New-Label "Date the Precedent was last reviewed/confirmed by an SME")
            "Format"             = "DateOnly"
            "DateTimeBehavior"   = @{ "Value" = "DateOnly" }
        }
    },
    # sprk_effectivenessscore — Decimal (Phase 1.5+ usage)
    @{
        "name" = "sprk_effectivenessscore"
        "def"  = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.DecimalAttributeMetadata"
            "SchemaName"    = "sprk_EffectivenessScore"
            "RequiredLevel" = @{ "Value" = "None" }
            "DisplayName"   = (New-Label "Effectiveness Score")
            "Description"   = (New-Label "Phase 1.5+ — automated score (0.0–1.0) of how often this Precedent improves Inferences when cited.")
            "Precision"     = 2
            "MinValue"      = 0
            "MaxValue"      = 1
        }
    },
    # sprk_clusterdefinition — Memo (text)
    @{
        "name" = "sprk_clusterdefinition"
        "def"  = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
            "SchemaName"    = "sprk_ClusterDefinition"
            "RequiredLevel" = @{ "Value" = "None" }
            "MaxLength"     = 2000
            "DisplayName"   = (New-Label "Cluster Definition")
            "Description"   = (New-Label "Phase 1.5+ — narrative or expression for the grouping dimensions and thresholds that defined the cluster.")
            "Format"        = "TextArea"
        }
    },
    # sprk_samplesize — Integer
    @{
        "name" = "sprk_samplesize"
        "def"  = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
            "SchemaName"    = "sprk_SampleSize"
            "RequiredLevel" = @{ "Value" = "None" }
            "DisplayName"   = (New-Label "Sample Size")
            "Description"   = (New-Label "Number of supporting matters underlying the pattern statement.")
            "MinValue"      = 0
            "MaxValue"      = 1000000
            "Format"        = "None"
        }
    },
    # sprk_producedby — String (e.g. "manual-sme-author" or "closure-pattern-detection@v1")
    @{
        "name" = "sprk_producedby"
        "def"  = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"    = "sprk_ProducedBy"
            "RequiredLevel" = @{ "Value" = "None" }
            "MaxLength"     = 200
            "DisplayName"   = (New-Label "Produced By")
            "Description"   = (New-Label "Producer identifier: 'manual-sme-author' (Phase 1) or '<cluster-detector>@v1' (Phase 1.5+).")
            "FormatName"    = @{ "Value" = "Text" }
        }
    }
)

foreach ($a in $attributes) {
    if (Test-AttributeExists -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_precedent" -AttributeLogicalName $a.name) {
        Write-Host "  Attribute $($a.name) already exists, skipping." -ForegroundColor Yellow
    } else {
        Write-Host "  Adding attribute: $($a.name)..." -ForegroundColor Gray
        Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
            -Endpoint "EntityDefinitions(LogicalName='sprk_precedent')/Attributes" -Method "POST" -Body $a.def `
            -ExtraHeaders @{ "MSCRM.SolutionUniqueName" = $SolutionName } | Out-Null
        Write-Host "    Added: $($a.name)" -ForegroundColor Green
    }
}
Write-Host ""

# ---------------------------------------------------------------------------
# Step 5: Add lookup sprk_reviewerby -> systemuser (1:N from systemuser)
# ---------------------------------------------------------------------------
Write-Host "Step 5: Add lookup sprk_reviewerby -> systemuser..." -ForegroundColor Cyan
$reviewerLookupName = "sprk_ReviewerBy"
$reviewerRelSchema = "sprk_systemuser_sprk_precedent_reviewerby"

if (Test-AttributeExists -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_precedent" -AttributeLogicalName "sprk_reviewerby") {
    Write-Host "  Lookup sprk_reviewerby already exists." -ForegroundColor Yellow
} else {
    $relDef = @{
        "@odata.type"        = "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata"
        "SchemaName"         = $reviewerRelSchema
        "ReferencedEntity"   = "systemuser"
        "ReferencingEntity"  = "sprk_precedent"
        "CascadeConfiguration" = @{
            "Assign"    = "NoCascade"
            "Delete"    = "RemoveLink"
            "Merge"     = "NoCascade"
            "Reparent"  = "NoCascade"
            "Share"     = "NoCascade"
            "Unshare"   = "NoCascade"
        }
        "Lookup" = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.LookupAttributeMetadata"
            "SchemaName"    = $reviewerLookupName
            "DisplayName"   = (New-Label "Reviewer")
            "Description"   = (New-Label "User who last reviewed/confirmed this Precedent.")
            "RequiredLevel" = @{ "Value" = "None" }
        }
    }
    Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
        -Endpoint "RelationshipDefinitions" -Method "POST" -Body $relDef `
        -ExtraHeaders @{ "MSCRM.SolutionUniqueName" = $SolutionName } | Out-Null
    Write-Host "  Created lookup sprk_reviewerby" -ForegroundColor Green
}
Write-Host ""

# ---------------------------------------------------------------------------
# Step 6: Create N:N relationships
# ---------------------------------------------------------------------------
Write-Host "Step 6: Create N:N relationships..." -ForegroundColor Cyan

# 6a: sprk_precedent <-> sprk_matter (supporting matters)
$nnMatterSchema = "sprk_precedent_matter"
if (Test-RelationshipExists -Token $token -BaseUrl $EnvironmentUrl -SchemaName $nnMatterSchema) {
    Write-Host "  N:N $nnMatterSchema already exists." -ForegroundColor Yellow
} else {
    $nnDef = @{
        "@odata.type"           = "Microsoft.Dynamics.CRM.ManyToManyRelationshipMetadata"
        "SchemaName"            = $nnMatterSchema
        "Entity1LogicalName"    = "sprk_precedent"
        "Entity2LogicalName"    = "sprk_matter"
        "IntersectEntityName"   = $nnMatterSchema
        "Entity1AssociatedMenuConfiguration" = @{
            "Behavior" = "UseCollectionName"
            "Group"    = "Details"
            "Order"    = 10000
            "Label"    = (New-Label "Supporting Matters")
        }
        "Entity2AssociatedMenuConfiguration" = @{
            "Behavior" = "UseCollectionName"
            "Group"    = "Details"
            "Order"    = 10000
            "Label"    = (New-Label "Precedents")
        }
    }
    Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
        -Endpoint "RelationshipDefinitions" -Method "POST" -Body $nnDef `
        -ExtraHeaders @{ "MSCRM.SolutionUniqueName" = $SolutionName } | Out-Null
    Write-Host "  Created N:N $nnMatterSchema (sprk_precedent <-> sprk_matter)" -ForegroundColor Green
}

# 6b: sprk_precedent <-> sprk_precedent (related self N:N)
$nnRelatedSchema = "sprk_precedent_related"
if (Test-RelationshipExists -Token $token -BaseUrl $EnvironmentUrl -SchemaName $nnRelatedSchema) {
    Write-Host "  N:N $nnRelatedSchema already exists." -ForegroundColor Yellow
} else {
    $nnDef = @{
        "@odata.type"           = "Microsoft.Dynamics.CRM.ManyToManyRelationshipMetadata"
        "SchemaName"            = $nnRelatedSchema
        "Entity1LogicalName"    = "sprk_precedent"
        "Entity2LogicalName"    = "sprk_precedent"
        "IntersectEntityName"   = $nnRelatedSchema
        "Entity1AssociatedMenuConfiguration" = @{
            "Behavior" = "UseLabel"
            "Group"    = "Details"
            "Order"    = 10010
            "Label"    = (New-Label "Related Precedents (As Source)")
        }
        "Entity2AssociatedMenuConfiguration" = @{
            "Behavior" = "UseLabel"
            "Group"    = "Details"
            "Order"    = 10011
            "Label"    = (New-Label "Related Precedents (As Target)")
        }
    }
    Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
        -Endpoint "RelationshipDefinitions" -Method "POST" -Body $nnDef `
        -ExtraHeaders @{ "MSCRM.SolutionUniqueName" = $SolutionName } | Out-Null
    Write-Host "  Created N:N $nnRelatedSchema (sprk_precedent self)" -ForegroundColor Green
}

# 6c: sprk_precedent_observation — DEFERRED (sprk_observation entity does not exist yet)
Write-Host "  N:N sprk_precedent_observation: DEFERRED — sprk_observation entity does not yet exist (D-P11/Phase 1.5+ scope)." -ForegroundColor DarkYellow
Write-Host ""

# ---------------------------------------------------------------------------
# Step 7: Publish customizations
# ---------------------------------------------------------------------------
Write-Host "Step 7: Publish customizations..." -ForegroundColor Cyan
$publishXml = @{
    "ParameterXml" = "<importexportxml><entities><entity>sprk_precedent</entity></entities></importexportxml>"
}
try {
    Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl -Endpoint "PublishXml" -Method "POST" -Body $publishXml | Out-Null
    Write-Host "  Customizations published." -ForegroundColor Green
} catch {
    Write-Host "  Publish warning (may have timed out): $($_.Exception.Message)" -ForegroundColor Yellow
}
Write-Host ""

# ---------------------------------------------------------------------------
# Step 8: Add entity + option set to solution (idempotent)
# ---------------------------------------------------------------------------
Write-Host "Step 8: Add components to solution '$SolutionName'..." -ForegroundColor Cyan

# Entity (ComponentType 1)
$entityMeta = Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
    -Endpoint "EntityDefinitions(LogicalName='sprk_precedent')?`$select=MetadataId" -Method "GET"
Add-SolutionComponent -Token $token -BaseUrl $EnvironmentUrl `
    -ComponentId $entityMeta.MetadataId -ComponentType 1 -SolutionUniqueName $SolutionName -AddRequiredComponents $true

# Global option set (ComponentType 9)
$os = Get-GlobalOptionSet -Token $token -BaseUrl $EnvironmentUrl -Name "sprk_precedentstatus"
Add-SolutionComponent -Token $token -BaseUrl $EnvironmentUrl `
    -ComponentId $os.MetadataId -ComponentType 9 -SolutionUniqueName $SolutionName -AddRequiredComponents $false

# Relationships (ComponentType 10)
foreach ($relName in @($nnMatterSchema, $nnRelatedSchema, $reviewerRelSchema)) {
    try {
        $rel = Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
            -Endpoint "RelationshipDefinitions(SchemaName='$relName')?`$select=MetadataId" -Method "GET"
        Add-SolutionComponent -Token $token -BaseUrl $EnvironmentUrl `
            -ComponentId $rel.MetadataId -ComponentType 10 -SolutionUniqueName $SolutionName -AddRequiredComponents $false
    } catch {
        Write-Host "    Skipping relationship $relName (not found in metadata query): $($_.Exception.Message)" -ForegroundColor Yellow
    }
}
Write-Host ""

# ---------------------------------------------------------------------------
# Step 9: Verify via Web API
# ---------------------------------------------------------------------------
Write-Host "Step 9: Verify sprk_precedent via Web API..." -ForegroundColor Cyan
$verifyEntity = Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
    -Endpoint "EntityDefinitions(LogicalName='sprk_precedent')?`$expand=Attributes(`$select=LogicalName,AttributeType)" -Method "GET"

Write-Host "  Entity LogicalName: $($verifyEntity.LogicalName)" -ForegroundColor Green
Write-Host "  EntitySetName    : $($verifyEntity.EntitySetName)" -ForegroundColor Green
Write-Host "  PrimaryNameAttr  : $($verifyEntity.PrimaryNameAttribute)" -ForegroundColor Green

$customAttrs = $verifyEntity.Attributes | Where-Object { $_.LogicalName -like "sprk_*" } | Sort-Object LogicalName
Write-Host "  Custom attributes:" -ForegroundColor Green
foreach ($attr in $customAttrs) {
    Write-Host "    - $($attr.LogicalName) ($($attr.AttributeType))" -ForegroundColor Gray
}

# Verify the entity set is queryable
try {
    $q = Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
        -Endpoint "sprk_precedents?`$top=1&`$select=sprk_name" -Method "GET"
    Write-Host "  Web API query OK (returned $($q.value.Count) record(s))." -ForegroundColor Green
} catch {
    Write-Host "  Web API query failed: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host " DEPLOYMENT COMPLETE" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "Entity   : sprk_precedent" -ForegroundColor Green
Write-Host "Solution : $SolutionName" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps (handled outside this script):" -ForegroundColor Yellow
Write-Host "  - Create views + admin form via Maker Portal or separate scripts" -ForegroundColor Gray
Write-Host "  - pac solution export + unpack to src/solutions/spaarke_insights/" -ForegroundColor Gray
