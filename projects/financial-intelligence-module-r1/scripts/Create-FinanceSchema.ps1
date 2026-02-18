# Finance Intelligence Module R1 -Dataverse Schema Deployment
# Source: notes/scratch/001-entity-field-diff.yaml + 002-document-field-diff.yaml
# Generated: 2026-02-11
#
# Deployment order:
#   Phase 1: Global Option Sets (7 choices)
#   Phase 2: Local Choice + Non-Lookup Fields (all entities)
#   Phase 3: Lookup Relationships (via RelationshipDefinitions)
#   Phase 4: Publish Customizations
#
# NOTE: Alternate keys are NOT created by this script -create manually in Dataverse.
# NOTE: This script is idempotent -re-running will skip existing components.

param(
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com"
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host " Finance Intelligence Module R1 -Schema Deployment" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl" -ForegroundColor Gray
Write-Host ""

# ===================================================================
# AUTHENTICATION
# ===================================================================

Write-Host "Authenticating via Azure CLI..." -ForegroundColor Yellow
$token = az account get-access-token --resource "$EnvironmentUrl" --query accessToken -o tsv
if (-not $token) {
    throw "Failed to get access token. Run 'az login' first."
}
Write-Host "  Authenticated." -ForegroundColor Green

$headers = @{
    "Authorization"    = "Bearer $token"
    "Content-Type"     = "application/json"
    "Accept"           = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version"    = "4.0"
}

$apiUrl = "$EnvironmentUrl/api/data/v9.2"

# ===================================================================
# HELPER FUNCTIONS
# ===================================================================

function New-Label {
    param([string]$Text)
    return @{
        "@odata.type"    = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(
            @{
                "@odata.type"  = "Microsoft.Dynamics.CRM.LocalizedLabel"
                "Label"        = $Text
                "LanguageCode" = 1033
            }
        )
    }
}

function Invoke-DataverseApi {
    param(
        [string]$Endpoint,
        [string]$Method = "GET",
        [object]$Body = $null
    )
    $uri = "$apiUrl/$Endpoint"
    $params = @{
        Uri     = $uri
        Headers = $headers
        Method  = $Method
    }
    if ($Body) {
        $params.Body = ($Body | ConvertTo-Json -Depth 20 -Compress)
    }
    try {
        $response = Invoke-RestMethod @params
        return @{ Success = $true; Data = $response }
    }
    catch {
        $errorMessage = $_.Exception.Message
        try {
            $errorDetails = $_.ErrorDetails.Message | ConvertFrom-Json
            $errorMessage = $errorDetails.error.message
        } catch {}
        return @{ Success = $false; Error = $errorMessage }
    }
}

function Test-GlobalOptionSetExists {
    param([string]$Name)
    $result = Invoke-DataverseApi -Endpoint "GlobalOptionSetDefinitions(Name='$Name')"
    return $result.Success
}

function Test-AttributeExists {
    param([string]$EntityLogicalName, [string]$AttributeLogicalName)
    $result = Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')"
    return $result.Success
}

function New-DataverseColumn {
    param(
        [string]$TableLogicalName,
        [hashtable]$AttributeMetadata
    )
    $schemaName = $AttributeMetadata.SchemaName
    $logicalName = $schemaName.ToLower()

    if (Test-AttributeExists -EntityLogicalName $TableLogicalName -AttributeLogicalName $logicalName) {
        Write-Host "  -- Exists: $schemaName" -ForegroundColor Yellow
        return $true
    }

    try {
        $body = $AttributeMetadata | ConvertTo-Json -Depth 20
        Invoke-RestMethod -Uri "$apiUrl/EntityDefinitions(LogicalName='$TableLogicalName')/Attributes" `
            -Method Post -Headers $headers -Body $body | Out-Null
        Write-Host "  ++ Created: $schemaName" -ForegroundColor Green
        return $true
    }
    catch {
        $msg = $_.Exception.Message
        try { $msg = ($_.ErrorDetails.Message | ConvertFrom-Json).error.message } catch {}
        Write-Host "  ** Failed: $schemaName - $msg" -ForegroundColor Red
        return $false
    }
}

function New-DataverseString {
    param(
        [string]$Table, [string]$SchemaName, [string]$DisplayName,
        [int]$MaxLength, [bool]$Required = $false, [string]$Description = ""
    )
    $metadata = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"  = $SchemaName
        "DisplayName" = New-Label $DisplayName
        "Description" = New-Label $Description
        "RequiredLevel" = @{ "Value" = if ($Required) { "ApplicationRequired" } else { "None" } }
        "MaxLength"   = $MaxLength
        "FormatName"  = @{ "Value" = "Text" }
    }
    New-DataverseColumn -TableLogicalName $Table -AttributeMetadata $metadata
}

function New-DataverseMemo {
    param(
        [string]$Table, [string]$SchemaName, [string]$DisplayName,
        [int]$MaxLength, [bool]$Required = $false, [string]$Description = ""
    )
    $metadata = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
        "SchemaName"  = $SchemaName
        "DisplayName" = New-Label $DisplayName
        "Description" = New-Label $Description
        "RequiredLevel" = @{ "Value" = if ($Required) { "ApplicationRequired" } else { "None" } }
        "MaxLength"   = $MaxLength
        "Format"      = "Text"
    }
    New-DataverseColumn -TableLogicalName $Table -AttributeMetadata $metadata
}

function New-DataverseDateTime {
    param(
        [string]$Table, [string]$SchemaName, [string]$DisplayName,
        [bool]$Required = $false, [string]$Description = "",
        [string]$Format = "DateAndTime"
    )
    $metadata = @{
        "@odata.type"      = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
        "SchemaName"       = $SchemaName
        "DisplayName"      = New-Label $DisplayName
        "Description"      = New-Label $Description
        "RequiredLevel"    = @{ "Value" = if ($Required) { "ApplicationRequired" } else { "None" } }
        "Format"           = $Format
        "DateTimeBehavior" = @{ "Value" = "UserLocal" }
    }
    New-DataverseColumn -TableLogicalName $Table -AttributeMetadata $metadata
}

function New-DataverseMoney {
    param(
        [string]$Table, [string]$SchemaName, [string]$DisplayName,
        [bool]$Required = $false, [string]$Description = ""
    )
    $metadata = @{
        "@odata.type"    = "Microsoft.Dynamics.CRM.MoneyAttributeMetadata"
        "SchemaName"     = $SchemaName
        "DisplayName"    = New-Label $DisplayName
        "Description"    = New-Label $Description
        "RequiredLevel"  = @{ "Value" = if ($Required) { "ApplicationRequired" } else { "None" } }
        "Precision"      = 2
        "PrecisionSource" = 0
    }
    New-DataverseColumn -TableLogicalName $Table -AttributeMetadata $metadata
}

function New-DataverseDecimal {
    param(
        [string]$Table, [string]$SchemaName, [string]$DisplayName,
        [int]$Precision = 4, [bool]$Required = $false, [string]$Description = "",
        [decimal]$MinValue = -100000000000, [decimal]$MaxValue = 100000000000
    )
    $metadata = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.DecimalAttributeMetadata"
        "SchemaName"  = $SchemaName
        "DisplayName" = New-Label $DisplayName
        "Description" = New-Label $Description
        "RequiredLevel" = @{ "Value" = if ($Required) { "ApplicationRequired" } else { "None" } }
        "Precision"   = $Precision
        "MinValue"    = $MinValue
        "MaxValue"    = $MaxValue
    }
    New-DataverseColumn -TableLogicalName $Table -AttributeMetadata $metadata
}

function New-DataverseInteger {
    param(
        [string]$Table, [string]$SchemaName, [string]$DisplayName,
        [bool]$Required = $false, [string]$Description = "",
        [int]$MinValue = 0, [int]$MaxValue = 2147483647
    )
    $metadata = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
        "SchemaName"  = $SchemaName
        "DisplayName" = New-Label $DisplayName
        "Description" = New-Label $Description
        "RequiredLevel" = @{ "Value" = if ($Required) { "ApplicationRequired" } else { "None" } }
        "MinValue"    = $MinValue
        "MaxValue"    = $MaxValue
        "Format"      = "None"
    }
    New-DataverseColumn -TableLogicalName $Table -AttributeMetadata $metadata
}

function New-DataverseBoolean {
    param(
        [string]$Table, [string]$SchemaName, [string]$DisplayName,
        [bool]$Required = $false, [string]$Description = ""
    )
    $metadata = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.BooleanAttributeMetadata"
        "SchemaName"  = $SchemaName
        "DisplayName" = New-Label $DisplayName
        "Description" = New-Label $Description
        "RequiredLevel" = @{ "Value" = if ($Required) { "ApplicationRequired" } else { "None" } }
        "OptionSet" = @{
            "TrueOption"  = @{ "Value" = 1; "Label" = New-Label "Yes" }
            "FalseOption" = @{ "Value" = 0; "Label" = New-Label "No" }
        }
    }
    New-DataverseColumn -TableLogicalName $Table -AttributeMetadata $metadata
}

function New-DataverseGlobalChoice {
    param(
        [string]$Table, [string]$SchemaName, [string]$DisplayName,
        [string]$GlobalOptionSetName,
        [bool]$Required = $false, [string]$Description = ""
    )
    # Look up the global option set GUID by name (odata.bind requires GUID, not Name filter)
    $lookup = Invoke-DataverseApi -Endpoint "GlobalOptionSetDefinitions(Name='$GlobalOptionSetName')"
    if (-not $lookup.Success) {
        Write-Host "  ** Failed: $SchemaName - Could not find global option set '$GlobalOptionSetName'" -ForegroundColor Red
        return $false
    }
    $optionSetId = $lookup.Data.MetadataId

    $metadata = @{
        "@odata.type"                = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
        "SchemaName"                 = $SchemaName
        "DisplayName"                = New-Label $DisplayName
        "Description"                = New-Label $Description
        "RequiredLevel"              = @{ "Value" = if ($Required) { "ApplicationRequired" } else { "None" } }
        "GlobalOptionSet@odata.bind" = "/GlobalOptionSetDefinitions($optionSetId)"
    }
    New-DataverseColumn -TableLogicalName $Table -AttributeMetadata $metadata
}

function New-DataverseLocalChoice {
    param(
        [string]$Table, [string]$SchemaName, [string]$DisplayName,
        [hashtable[]]$Options,
        [bool]$Required = $false, [string]$Description = ""
    )
    $optionSetOptions = @()
    foreach ($opt in $Options) {
        $optionSetOptions += @{
            "Value" = $opt.Value
            "Label" = New-Label $opt.Label
        }
    }
    $metadata = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
        "SchemaName"  = $SchemaName
        "DisplayName" = New-Label $DisplayName
        "Description" = New-Label $Description
        "RequiredLevel" = @{ "Value" = if ($Required) { "ApplicationRequired" } else { "None" } }
        "OptionSet" = @{
            "@odata.type"  = "Microsoft.Dynamics.CRM.OptionSetMetadata"
            "IsGlobal"     = $false
            "OptionSetType" = "Picklist"
            "Options"      = $optionSetOptions
        }
    }
    New-DataverseColumn -TableLogicalName $Table -AttributeMetadata $metadata
}

function New-DataverseLookup {
    param(
        [string]$ChildEntity, [string]$SchemaName, [string]$DisplayName,
        [string]$ParentEntity,
        [bool]$Required = $false, [string]$Description = "",
        [string]$DeleteBehavior = "RemoveLink"
    )
    $logicalName = $SchemaName.ToLower()
    if (Test-AttributeExists -EntityLogicalName $ChildEntity -AttributeLogicalName $logicalName) {
        Write-Host "  -- Exists: $SchemaName (lookup)" -ForegroundColor Yellow
        return $true
    }

    $relationshipName = "sprk_${ParentEntity}_${ChildEntity}_${logicalName}"
    $relationshipDef = @{
        "@odata.type"          = "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata"
        "SchemaName"           = $relationshipName
        "ReferencedEntity"     = $ParentEntity
        "ReferencingEntity"    = $ChildEntity
        "CascadeConfiguration" = @{
            "Assign"   = "NoCascade"
            "Delete"   = $DeleteBehavior
            "Merge"    = "NoCascade"
            "Reparent" = "NoCascade"
            "Share"    = "NoCascade"
            "Unshare"  = "NoCascade"
        }
        "Lookup" = @{
            "SchemaName"    = $SchemaName
            "DisplayName"   = New-Label $DisplayName
            "Description"   = New-Label $Description
            "RequiredLevel" = @{ "Value" = if ($Required) { "ApplicationRequired" } else { "None" } }
        }
    }

    try {
        $body = $relationshipDef | ConvertTo-Json -Depth 20
        Invoke-RestMethod -Uri "$apiUrl/RelationshipDefinitions" `
            -Method Post -Headers $headers -Body $body | Out-Null
        Write-Host "  ++ Created: $SchemaName (lookup -> $ParentEntity)" -ForegroundColor Green
        return $true
    }
    catch {
        $msg = $_.Exception.Message
        try { $msg = ($_.ErrorDetails.Message | ConvertFrom-Json).error.message } catch {}
        if ($msg -match "already exists") {
            Write-Host "  -- Exists: $SchemaName (lookup)" -ForegroundColor Yellow
            return $true
        }
        Write-Host "  ** Failed: $SchemaName (lookup) - $msg" -ForegroundColor Red
        return $false
    }
}


# ===================================================================
# PHASE 1: GLOBAL OPTION SETS
# ===================================================================

Write-Host ""
Write-Host 'Phase 1: Global Option Sets (7 choices)' -ForegroundColor Cyan
Write-Host "--------------------------------------------------------" -ForegroundColor Gray

$globalChoices = @(
    @{
        Name = "sprk_visibilitystate"; DisplayName = "Visibility State"
        Options = @(
            @{ Value = 100000000; Label = "Invoiced" },
            @{ Value = 100000001; Label = "InternalWIP" },
            @{ Value = 100000002; Label = "PreBill" },
            @{ Value = 100000003; Label = "Paid" },
            @{ Value = 100000004; Label = "WrittenOff" }
        )
    },
    @{
        Name = "sprk_costtype"; DisplayName = "Cost Type"
        Options = @(
            @{ Value = 100000000; Label = "Fee" },
            @{ Value = 100000001; Label = "Expense" }
        )
    },
    @{
        Name = "sprk_periodtype"; DisplayName = "Period Type"
        Options = @(
            @{ Value = 100000000; Label = "Month" },
            @{ Value = 100000001; Label = "Quarter" },
            @{ Value = 100000002; Label = "Year" },
            @{ Value = 100000003; Label = "ToDate" }
        )
    },
    @{
        Name = "sprk_signaltype"; DisplayName = "Signal Type"
        Options = @(
            @{ Value = 100000000; Label = "BudgetExceeded" },
            @{ Value = 100000001; Label = "BudgetWarning" },
            @{ Value = 100000002; Label = "VelocitySpike" },
            @{ Value = 100000003; Label = "AnomalyDetected" }
        )
    },
    @{
        Name = "sprk_signalseverity"; DisplayName = "Signal Severity"
        Options = @(
            @{ Value = 100000000; Label = "Info" },
            @{ Value = 100000001; Label = "Warning" },
            @{ Value = 100000002; Label = "Critical" }
        )
    },
    @{
        Name = "sprk_invoicestatus"; DisplayName = "Invoice Status"
        Options = @(
            @{ Value = 100000000; Label = "ToReview" },
            @{ Value = 100000001; Label = "Reviewed" }
        )
    },
    @{
        Name = "sprk_extractionstatus"; DisplayName = "Extraction Status"
        Options = @(
            @{ Value = 100000000; Label = "NotRun" },
            @{ Value = 100000001; Label = "Extracted" },
            @{ Value = 100000002; Label = "Failed" }
        )
    }
)

foreach ($choice in $globalChoices) {
    if (Test-GlobalOptionSetExists -Name $choice.Name) {
        Write-Host "  -- Exists: $($choice.Name)" -ForegroundColor Yellow
        continue
    }

    $optionItems = @()
    foreach ($opt in $choice.Options) {
        $optionItems += @{ "Value" = $opt.Value; "Label" = New-Label $opt.Label }
    }

    $optionSetDef = @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.OptionSetMetadata"
        "Name"          = $choice.Name
        "DisplayName"   = New-Label $choice.DisplayName
        "IsGlobal"      = $true
        "OptionSetType" = "Picklist"
        "Options"       = $optionItems
    }

    $result = Invoke-DataverseApi -Endpoint "GlobalOptionSetDefinitions" -Method "POST" -Body $optionSetDef
    if ($result.Success) {
        Write-Host "  ++ Created: $($choice.Name)" -ForegroundColor Green
    } else {
        Write-Host "  ** Failed: $($choice.Name) - $($result.Error)" -ForegroundColor Red
    }
}


# ===================================================================
# PHASE 2: NON-LOOKUP FIELDS (all entities)
# ===================================================================

Write-Host ""
Write-Host "Phase 2: Non-Lookup Fields" -ForegroundColor Cyan
Write-Host "--------------------------------------------------------" -ForegroundColor Gray

# --- sprk_invoice ---
Write-Host ""
Write-Host '  sprk_invoice (5 non-lookup fields)' -ForegroundColor White
New-DataverseString   -Table "sprk_invoice" -SchemaName "sprk_invoicenumber"   -DisplayName "Invoice Number"    -MaxLength 100  -Description "From extraction or reviewer correction"
New-DataverseDateTime -Table "sprk_invoice" -SchemaName "sprk_invoicedate"     -DisplayName "Invoice Date"      -Format "DateOnly"  -Description "From extraction or reviewer correction"
New-DataverseString   -Table "sprk_invoice" -SchemaName "sprk_currency"        -DisplayName "Currency"          -MaxLength 10   -Description "ISO 4217 currency code"
New-DataverseMoney    -Table "sprk_invoice" -SchemaName "sprk_totalamount"     -DisplayName "Total Amount"      -Description "Invoice total"
New-DataverseString   -Table "sprk_invoice" -SchemaName "sprk_correlationid"   -DisplayName "Correlation ID"    -MaxLength 100  -Required $true  -Description "Job chain traceability"
New-DataverseGlobalChoice -Table "sprk_invoice" -SchemaName "sprk_invoicestatus"     -DisplayName "Invoice Status"     -GlobalOptionSetName "sprk_invoicestatus"     -Required $true -Description "ToReview | Reviewed"
New-DataverseGlobalChoice -Table "sprk_invoice" -SchemaName "sprk_visibilitystate"   -DisplayName "Visibility State"   -GlobalOptionSetName "sprk_visibilitystate"   -Required $true -Description "Always Invoiced for MVP"
New-DataverseGlobalChoice -Table "sprk_invoice" -SchemaName "sprk_extractionstatus"  -DisplayName "Extraction Status"  -GlobalOptionSetName "sprk_extractionstatus"  -Required $true -Description "NotRun | Extracted | Failed"

# --- sprk_billingevent ---
Write-Host ""
Write-Host '  sprk_billingevent (8 non-lookup fields)' -ForegroundColor White
New-DataverseDateTime -Table "sprk_billingevent" -SchemaName "sprk_eventdate"      -DisplayName "Event Date"       -Format "DateOnly" -Required $true -Description "Line date or invoice date fallback"
New-DataverseGlobalChoice -Table "sprk_billingevent" -SchemaName "sprk_costtype"       -DisplayName "Cost Type"        -GlobalOptionSetName "sprk_costtype"       -Required $true -Description "Fee | Expense"
New-DataverseMoney    -Table "sprk_billingevent" -SchemaName "sprk_amount"          -DisplayName "Amount"           -Required $true -Description "Line amount"
New-DataverseString   -Table "sprk_billingevent" -SchemaName "sprk_currency"        -DisplayName "Currency"         -MaxLength 10  -Required $true -Description "ISO 4217 currency code"
New-DataverseString   -Table "sprk_billingevent" -SchemaName "sprk_roleclass"       -DisplayName "Role Class"       -MaxLength 100 -Description "Timekeeper role if extractable"
New-DataverseGlobalChoice -Table "sprk_billingevent" -SchemaName "sprk_visibilitystate" -DisplayName "Visibility State" -GlobalOptionSetName "sprk_visibilitystate" -Required $true -Description "Always Invoiced for MVP"
New-DataverseMemo     -Table "sprk_billingevent" -SchemaName "sprk_description"     -DisplayName "Description"      -MaxLength 2000 -Description "Line item narrative"
New-DataverseInteger  -Table "sprk_billingevent" -SchemaName "sprk_linesequence"    -DisplayName "Line Sequence"    -Required $true -MinValue 1 -Description "1-based line position"
New-DataverseString   -Table "sprk_billingevent" -SchemaName "sprk_correlationid"   -DisplayName "Correlation ID"   -MaxLength 100 -Required $true -Description "Job chain traceability"

# --- sprk_budget ---
Write-Host ""
Write-Host '  sprk_budget (2 non-lookup fields)' -ForegroundColor White
New-DataverseMoney    -Table "sprk_budget" -SchemaName "sprk_totalbudget"   -DisplayName "Total Budget"  -Required $true -Description "Overall budget amount"
New-DataverseString   -Table "sprk_budget" -SchemaName "sprk_currency"     -DisplayName "Currency"      -MaxLength 10  -Required $true -Description "ISO 4217 currency code"

# --- sprk_budgetbucket ---
Write-Host ""
Write-Host '  sprk_budgetbucket (4 non-lookup fields)' -ForegroundColor White
New-DataverseString   -Table "sprk_budgetbucket" -SchemaName "sprk_bucketkey"    -DisplayName "Bucket Key"    -MaxLength 100 -Required $true -Description "TOTAL for MVP"
New-DataverseDateTime -Table "sprk_budgetbucket" -SchemaName "sprk_periodstart"  -DisplayName "Period Start"  -Format "DateOnly" -Description "Bucket period start (null for lifetime)"
New-DataverseDateTime -Table "sprk_budgetbucket" -SchemaName "sprk_periodend"    -DisplayName "Period End"    -Format "DateOnly" -Description "Bucket period end (null for lifetime)"
New-DataverseMoney    -Table "sprk_budgetbucket" -SchemaName "sprk_amount"       -DisplayName "Amount"        -Required $true -Description "Budget allocation for this bucket"

# --- sprk_spendsnapshot ---
Write-Host ""
Write-Host '  sprk_spendsnapshot (12 non-lookup fields)' -ForegroundColor White
New-DataverseGlobalChoice -Table "sprk_spendsnapshot" -SchemaName "sprk_periodtype"        -DisplayName "Period Type"        -GlobalOptionSetName "sprk_periodtype" -Required $true -Description "Month | Quarter | Year | ToDate"
New-DataverseString   -Table "sprk_spendsnapshot" -SchemaName "sprk_periodkey"         -DisplayName "Period Key"         -MaxLength 20  -Required $true -Description "e.g. 2026-01, TO_DATE"
New-DataverseString   -Table "sprk_spendsnapshot" -SchemaName "sprk_bucketkey"         -DisplayName "Bucket Key"         -MaxLength 100 -Required $true -Description "TOTAL for MVP"
New-DataverseString   -Table "sprk_spendsnapshot" -SchemaName "sprk_visibilityfilter"  -DisplayName "Visibility Filter"  -MaxLength 50  -Required $true -Description "ACTUAL_INVOICED for MVP"
New-DataverseMoney    -Table "sprk_spendsnapshot" -SchemaName "sprk_invoicedamount"    -DisplayName "Invoiced Amount"    -Required $true -Description "Sum of BillingEvents"
New-DataverseMoney    -Table "sprk_spendsnapshot" -SchemaName "sprk_budgetamount"      -DisplayName "Budget Amount"      -Description "From matching BudgetBucket"
New-DataverseMoney    -Table "sprk_spendsnapshot" -SchemaName "sprk_budgetvariance"    -DisplayName "Budget Variance"    -Description "Budget - Invoiced"
New-DataverseDecimal  -Table "sprk_spendsnapshot" -SchemaName "sprk_budgetvariancepct" -DisplayName "Budget Variance Pct" -Precision 4 -MinValue -100000000000 -MaxValue 100000000000 -Description "Variance / Budget * 100"
New-DataverseDecimal  -Table "sprk_spendsnapshot" -SchemaName "sprk_velocitypct"       -DisplayName "Velocity Pct"       -Precision 4 -MinValue -100000000000 -MaxValue 100000000000 -Description "% change from prior period"
New-DataverseMoney    -Table "sprk_spendsnapshot" -SchemaName "sprk_priorperiodamount" -DisplayName "Prior Period Amount" -Description "Amount from comparison period"
New-DataverseString   -Table "sprk_spendsnapshot" -SchemaName "sprk_priorperiodkey"    -DisplayName "Prior Period Key"    -MaxLength 20 -Description "Which period was compared"
New-DataverseDateTime -Table "sprk_spendsnapshot" -SchemaName "sprk_generatedat"       -DisplayName "Generated At"       -Required $true -Description "Snapshot computation timestamp"
New-DataverseString   -Table "sprk_spendsnapshot" -SchemaName "sprk_correlationid"     -DisplayName "Correlation ID"     -MaxLength 100 -Required $true -Description "Triggering job chain traceability"

# --- sprk_spendsignal ---
Write-Host ""
Write-Host '  sprk_spendsignal (5 non-lookup fields)' -ForegroundColor White
New-DataverseGlobalChoice -Table "sprk_spendsignal" -SchemaName "sprk_signaltype" -DisplayName "Signal Type" -GlobalOptionSetName "sprk_signaltype"    -Required $true -Description "BudgetExceeded | BudgetWarning | VelocitySpike | AnomalyDetected"
New-DataverseGlobalChoice -Table "sprk_spendsignal" -SchemaName "sprk_severity"   -DisplayName "Severity"    -GlobalOptionSetName "sprk_signalseverity" -Required $true -Description "Info | Warning | Critical"
New-DataverseString   -Table "sprk_spendsignal" -SchemaName "sprk_message"      -DisplayName "Message"       -MaxLength 500 -Required $true -Description "Human-readable signal description"
New-DataverseBoolean  -Table "sprk_spendsignal" -SchemaName "sprk_isactive"     -DisplayName "Is Active"     -Required $true -Description "Active until resolved/superseded"
New-DataverseDateTime -Table "sprk_spendsignal" -SchemaName "sprk_generatedat"  -DisplayName "Generated At"  -Required $true -Description "Signal detection timestamp"

# --- sprk_document (Task 002 -11 non-lookup fields) ---
Write-Host ""
Write-Host '  sprk_document (11 non-lookup fields from task 002)' -ForegroundColor White

# Classification + Review (3 non-lookup fields)
New-DataverseLocalChoice -Table "sprk_document" -SchemaName "sprk_classification" -DisplayName "Classification" -Options @(
    @{ Value = 100000000; Label = "InvoiceCandidate" },
    @{ Value = 100000001; Label = "NotInvoice" },
    @{ Value = 100000002; Label = "Unknown" }
) -Description "AI classification result from Playbook A"

New-DataverseDecimal  -Table "sprk_document" -SchemaName "sprk_classificationconfidence" -DisplayName "Classification Confidence" -Precision 4 -MinValue 0 -MaxValue 1 -Description "AI confidence score (0..1)"

New-DataverseLocalChoice -Table "sprk_document" -SchemaName "sprk_invoicereviewstatus" -DisplayName "Invoice Review Status" -Options @(
    @{ Value = 100000000; Label = "ToReview" },
    @{ Value = 100000001; Label = "ConfirmedInvoice" },
    @{ Value = 100000002; Label = "RejectedNotInvoice" }
) -Description "Human review state"

# Review tracking (1 non-lookup field -reviewedby is lookup, handled in Phase 3)
New-DataverseDateTime -Table "sprk_document" -SchemaName "sprk_invoicereviewedon"    -DisplayName "Reviewed On"        -Description "Review timestamp"

# Invoice Header Hints (6 fields)
New-DataverseString   -Table "sprk_document" -SchemaName "sprk_invoicevendornamehint" -DisplayName "Vendor Name Hint"   -MaxLength 200 -Description "AI-extracted vendor name"
New-DataverseString   -Table "sprk_document" -SchemaName "sprk_invoicenumberhint"     -DisplayName "Invoice Number Hint" -MaxLength 100 -Description "AI-extracted invoice number"
New-DataverseDateTime -Table "sprk_document" -SchemaName "sprk_invoicedatehint"       -DisplayName "Invoice Date Hint"   -Format "DateOnly" -Description "AI-extracted invoice date"
New-DataverseMoney    -Table "sprk_document" -SchemaName "sprk_invoicetotalhint"      -DisplayName "Total Amount Hint"   -Description "AI-extracted invoice total"
New-DataverseString   -Table "sprk_document" -SchemaName "sprk_invoicecurrencyhint"   -DisplayName "Currency Hint"       -MaxLength 10  -Description "AI-extracted ISO 4217 currency code"
New-DataverseMemo     -Table "sprk_document" -SchemaName "sprk_invoicehintsjson"      -DisplayName "Hints JSON"          -MaxLength 10000 -Description "Full structured hints JSON"

# Association Suggestions (2 non-lookup fields -3 lookups handled in Phase 3)
New-DataverseString   -Table "sprk_document" -SchemaName "sprk_mattersuggestedref"    -DisplayName "Matter Suggestion"      -MaxLength 500   -Description "AI-extracted matter reference string"
New-DataverseMemo     -Table "sprk_document" -SchemaName "sprk_mattersuggestionjson"  -DisplayName "Matter Suggestion JSON" -MaxLength 10000 -Description "Structured match candidates with confidence scores"


# ===================================================================
# PHASE 3: LOOKUP RELATIONSHIPS
# ===================================================================

Write-Host ""
Write-Host "Phase 3: Lookup Relationships" -ForegroundColor Cyan
Write-Host "--------------------------------------------------------" -ForegroundColor Gray

# --- sprk_invoice lookups (4) ---
Write-Host ""
Write-Host '  sprk_invoice lookups (4)' -ForegroundColor White
New-DataverseLookup -ChildEntity "sprk_invoice" -SchemaName "sprk_document"            -DisplayName "Source Document"         -ParentEntity "sprk_document"       -Required $true  -Description "Confirmed attachment that became this invoice"
New-DataverseLookup -ChildEntity "sprk_invoice" -SchemaName "sprk_regardingrecordtype" -DisplayName "Regarding Record Type"   -ParentEntity "sprk_recordtype_ref" -Required $true  -Description "Whether parent is Matter or Project"
New-DataverseLookup -ChildEntity "sprk_invoice" -SchemaName "sprk_project"             -DisplayName "Project"                 -ParentEntity "sprk_project"        -Required $false -Description "Parent project (alternative to matter)"
New-DataverseLookup -ChildEntity "sprk_invoice" -SchemaName "sprk_vendororg"           -DisplayName "Vendor Organization"     -ParentEntity "sprk_organization"   -Required $true  -Description "Invoice vendor/firm"

# --- sprk_billingevent lookups (3) ---
Write-Host ""
Write-Host '  sprk_billingevent lookups (3)' -ForegroundColor White
New-DataverseLookup -ChildEntity "sprk_billingevent" -SchemaName "sprk_matter"    -DisplayName "Matter"              -ParentEntity "sprk_matter"       -Description "Parent matter (denormalized)"
New-DataverseLookup -ChildEntity "sprk_billingevent" -SchemaName "sprk_project"   -DisplayName "Project"             -ParentEntity "sprk_project"      -Description "Parent project (denormalized)"
New-DataverseLookup -ChildEntity "sprk_billingevent" -SchemaName "sprk_vendororg" -DisplayName "Vendor Organization" -ParentEntity "sprk_organization" -Required $true -Description "Source vendor (denormalized)"

# --- sprk_budget lookups (1) ---
Write-Host ""
Write-Host '  sprk_budget lookups (1)' -ForegroundColor White
New-DataverseLookup -ChildEntity "sprk_budget" -SchemaName "sprk_project" -DisplayName "Project" -ParentEntity "sprk_project" -Description "Parent project"

# --- sprk_budgetbucket lookups (1) ---
Write-Host ""
Write-Host '  sprk_budgetbucket lookups (1)' -ForegroundColor White
New-DataverseLookup -ChildEntity "sprk_budgetbucket" -SchemaName "sprk_budget" -DisplayName "Budget" -ParentEntity "sprk_budget" -Required $true -DeleteBehavior "Cascade" -Description "Parent budget plan"

# --- sprk_spendsnapshot lookups (2) ---
Write-Host ""
Write-Host '  sprk_spendsnapshot lookups (2)' -ForegroundColor White
New-DataverseLookup -ChildEntity "sprk_spendsnapshot" -SchemaName "sprk_matter"  -DisplayName "Matter"  -ParentEntity "sprk_matter"  -Description "Parent matter"
New-DataverseLookup -ChildEntity "sprk_spendsnapshot" -SchemaName "sprk_project" -DisplayName "Project" -ParentEntity "sprk_project" -Description "Parent project"

# --- sprk_spendsignal lookups (3) ---
Write-Host ""
Write-Host '  sprk_spendsignal lookups (3)' -ForegroundColor White
New-DataverseLookup -ChildEntity "sprk_spendsignal" -SchemaName "sprk_matter"   -DisplayName "Matter"   -ParentEntity "sprk_matter"        -Description "Parent matter"
New-DataverseLookup -ChildEntity "sprk_spendsignal" -SchemaName "sprk_project"  -DisplayName "Project"  -ParentEntity "sprk_project"       -Description "Parent project"
New-DataverseLookup -ChildEntity "sprk_spendsignal" -SchemaName "sprk_snapshot" -DisplayName "Snapshot" -ParentEntity "sprk_spendsnapshot" -Required $true -Description "Source snapshot that triggered this signal"

# --- sprk_document lookups (4, from task 002) ---
Write-Host ""
Write-Host '  sprk_document lookups (4)' -ForegroundColor White
New-DataverseLookup -ChildEntity "sprk_document" -SchemaName "sprk_invoicereviewedby" -DisplayName "Reviewed By"         -ParentEntity "systemuser"        -Description "Reviewer identity"
New-DataverseLookup -ChildEntity "sprk_document" -SchemaName "sprk_relatedmatter"     -DisplayName "Related Matter"      -ParentEntity "sprk_matter"       -Description "Confirmed matter this invoice relates to"
New-DataverseLookup -ChildEntity "sprk_document" -SchemaName "sprk_relatedproject"    -DisplayName "Related Project"     -ParentEntity "sprk_project"      -Description "Confirmed project this invoice relates to"
New-DataverseLookup -ChildEntity "sprk_document" -SchemaName "sprk_relatedvendororg"  -DisplayName "Related Vendor Org"  -ParentEntity "sprk_organization" -Description "Confirmed vendor organization"


# ===================================================================
# PHASE 4: PUBLISH CUSTOMIZATIONS
# ===================================================================

Write-Host ""
Write-Host "Phase 4: Publishing Customizations" -ForegroundColor Cyan
Write-Host "--------------------------------------------------------" -ForegroundColor Gray

$entities = @("sprk_invoice", "sprk_billingevent", "sprk_budget", "sprk_budgetbucket",
              "sprk_spendsnapshot", "sprk_spendsignal", "sprk_document")
$entityXml = ($entities | ForEach-Object { "<entity>$_</entity>" }) -join ""
$publishRequest = @{
    "ParameterXml" = "<importexportxml><entities>$entityXml</entities></importexportxml>"
}

$result = Invoke-DataverseApi -Endpoint "PublishXml" -Method "POST" -Body $publishRequest
if ($result.Success) {
    Write-Host "  Published customizations for all 7 entities." -ForegroundColor Green
} else {
    Write-Host "  ** Publish failed: $($result.Error)" -ForegroundColor Red
}


# ===================================================================
# SUMMARY
# ===================================================================

Write-Host ""
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host " Deployment Complete" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Global Option Sets: 7" -ForegroundColor White
Write-Host '  Local Option Sets:  2 (on sprk_document)' -ForegroundColor White
Write-Host "  Non-Lookup Fields:  49" -ForegroundColor White
Write-Host "  Lookup Fields:      18" -ForegroundColor White
Write-Host "  Total Fields:       67" -ForegroundColor White
Write-Host ""
Write-Host "  MANUAL STEPS REQUIRED:" -ForegroundColor Yellow
Write-Host "    1. Create alternate key on sprk_invoice: sprk_document" -ForegroundColor Gray
Write-Host "    2. Create alternate key on sprk_billingevent: sprk_invoice + sprk_linesequence" -ForegroundColor Gray
Write-Host "    3. Create alternate key on sprk_spendsnapshot: sprk_matter + sprk_periodtype + sprk_periodkey + sprk_bucketkey + sprk_visibilityfilter" -ForegroundColor Gray
Write-Host "    4. Verify all fields are in the correct solution" -ForegroundColor Gray
Write-Host "    5. Configure security roles for finance entities" -ForegroundColor Gray
Write-Host ""
