<#
.SYNOPSIS
    Extends sprk_processingjob so one record can hold one intake run (spaarke-intake-pipeline-r1, FR-06).

.DESCRIPTION
    Adds, to the EXISTING sprk_processingjob entity (no new entity):

      Lookups
      - sprk_servicerequest      : lookup -> sprk_servicerequest. The request this run belongs to.
                                   One request has MANY runs (1:N; a re-drive or a re-run is a new row).
      - sprk_outputdocument      : lookup -> sprk_document. The run's output document (the redline or the
                                   clause table), set once the Record stage has filed it.
                                   (sprk_document, the existing lookup, is the Office save's target and is untouched.)

      Columns (ADR-015: ids, keys and blob prefixes only; never document content, document-derived file names or plan text)
      - sprk_harnesskey          : string(100)  the harness the run was routed to (e.g. "nda-review").
      - sprk_engineexecutionid   : string(200)  the engine Job execution id, so a resumed run re-attaches to it.
      - sprk_stagingblobprefix   : string(500)  the run's staging blob prefix (claim-check path, ids only).
      - sprk_redrivecount        : whole number  how many times an operator re-drove the run. No schema default
                                   (Dataverse whole-number columns take none): the BFF reads null as 0.
      - sprk_lastredriveon       : datetime      when the run was last re-driven.

      Job type option
      - sprk_jobtype gets the option "Intake run" at an explicit value (default 7: the next free value after
        Deep Analysis = 6). Office runs keep their values. Intake stage names (Receive ... Record, spec FR-07)
        are stored in the EXISTING sprk_currentstage (string 100) and sprk_stages (memo 10000), unchanged.

    Writers: the BFF only (the orchestrator and the admin re-drive). No plugin and no client write path (ADR-002).

    Idempotent: every step checks first and skips what exists. -WhatIf makes ONLY read-only metadata calls and
    reports what a real run would create; it creates, changes and publishes nothing.
    Requires Azure CLI authentication (az login).

.PARAMETER EnvironmentUrl
    The Dataverse environment URL (default: https://spaarkedev1.crm.dynamics.com). Dev only for this project.

.PARAMETER IntakeRunJobTypeValue
    The sprk_jobtype option value for "Intake run" (default 7).

.EXAMPLE
    .\Add-ProcessingJobIntakeRunColumns.ps1 -WhatIf

.EXAMPLE
    .\Add-ProcessingJobIntakeRunColumns.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"

.NOTES
    Project: spaarke-intake-pipeline-r1, task 011. Structure copied from Add-BindingContextTypeTagsColumn.ps1.
    Owner approval is required before a real (non -WhatIf) run (project CLAUDE.md section 5).
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",

    [Parameter(Mandatory = $false)]
    [int]$IntakeRunJobTypeValue = 7
)

$ErrorActionPreference = "Stop"

$EntityLogicalName = "sprk_processingjob"
$IntakeRunLabel = "Intake run"

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
        [object]$Body = $null
    )

    $headers = @{
        "Authorization"    = "Bearer $Token"
        "OData-MaxVersion" = "4.0"
        "OData-Version"    = "4.0"
        "Accept"           = "application/json"
        "Content-Type"     = "application/json; charset=utf-8"
        "Prefer"           = "odata.include-annotations=*"
    }

    $uri = "$BaseUrl/api/data/v9.2/$Endpoint"

    $params = @{
        Uri     = $uri
        Method  = $Method
        Headers = $headers
    }

    if ($Body) {
        $params.Body = ($Body | ConvertTo-Json -Depth 20)
    }

    try {
        return Invoke-RestMethod @params
    }
    catch {
        $statusCode = $null
        if ($_.Exception.Response) { $statusCode = [int]$_.Exception.Response.StatusCode }
        $errorDetails = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            $errorJson = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($errorJson.error.message) {
                $errorDetails = $errorJson.error.message
            }
        }
        $ex = [System.Exception]::new("API Error ($Method $Endpoint): $errorDetails")
        $ex.Data["StatusCode"] = $statusCode
        throw $ex
    }
}

function Test-NotFound {
    param($ErrorRecord)
    # Only a 404 means "does not exist". Any other failure (expired token, 5xx) must stop the run, not read as "missing".
    return ($ErrorRecord.Exception.Data["StatusCode"] -eq 404)
}

function New-Label {
    param([string]$Text)
    return @{
        "@odata.type"     = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(
            @{
                "@odata.type"  = "Microsoft.Dynamics.CRM.LocalizedLabel"
                "Label"        = $Text
                "LanguageCode" = 1033
            }
        )
    }
}

function Test-EntityExists {
    param([string]$Token, [string]$BaseUrl, [string]$LogicalName)
    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Endpoint "EntityDefinitions(LogicalName='$LogicalName')?`$select=LogicalName" | Out-Null
        return $true
    }
    catch {
        if (Test-NotFound $_) { return $false }
        throw
    }
}

function Test-AttributeExists {
    param([string]$Token, [string]$BaseUrl, [string]$EntityLogicalName, [string]$AttributeLogicalName)
    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')?`$select=LogicalName" | Out-Null
        return $true
    }
    catch {
        if (Test-NotFound $_) { return $false }
        throw
    }
}

function Test-RelationshipExists {
    param([string]$Token, [string]$BaseUrl, [string]$SchemaName)
    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Endpoint "RelationshipDefinitions(SchemaName='$SchemaName')?`$select=SchemaName" | Out-Null
        return $true
    }
    catch {
        if (Test-NotFound $_) { return $false }
        throw
    }
}

function Add-EntityAttribute {
    param([string]$Token, [string]$BaseUrl, [string]$EntityLogicalName, [object]$AttributeDef)

    $schemaName = $AttributeDef.SchemaName
    Write-Host "  Attribute: $schemaName" -ForegroundColor Gray

    if (Test-AttributeExists -Token $Token -BaseUrl $BaseUrl -EntityLogicalName $EntityLogicalName -AttributeLogicalName $schemaName.ToLower()) {
        Write-Host "    Already exists, skipping." -ForegroundColor Yellow
        return
    }

    if ($PSCmdlet.ShouldProcess("$EntityLogicalName.$schemaName", "Create attribute")) {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes" -Method "POST" -Body $AttributeDef | Out-Null
        Write-Host "    Added: $schemaName" -ForegroundColor Green
    }
    else {
        Write-Host "    WhatIf: would create $schemaName" -ForegroundColor Magenta
    }
}

function Add-LookupRelationship {
    param(
        [string]$Token, [string]$BaseUrl,
        [string]$RelationshipSchemaName,
        [string]$ReferencedEntity,
        [string]$ReferencingEntity,
        [string]$LookupSchemaName,
        [string]$LookupLogicalName,
        [string]$DisplayName,
        [string]$Description,
        [string]$DeleteBehavior
    )

    Write-Host "  Lookup: $LookupSchemaName -> $ReferencedEntity ($RelationshipSchemaName)" -ForegroundColor Gray

    if (Test-AttributeExists -Token $Token -BaseUrl $BaseUrl -EntityLogicalName $ReferencingEntity -AttributeLogicalName $LookupLogicalName) {
        Write-Host "    Already exists, skipping." -ForegroundColor Yellow
        return
    }
    if (Test-RelationshipExists -Token $Token -BaseUrl $BaseUrl -SchemaName $RelationshipSchemaName) {
        Write-Host "    Relationship $RelationshipSchemaName already exists, skipping." -ForegroundColor Yellow
        return
    }

    $body = @{
        "@odata.type"          = "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata"
        "SchemaName"           = $RelationshipSchemaName
        "ReferencedEntity"     = $ReferencedEntity
        "ReferencingEntity"    = $ReferencingEntity
        "CascadeConfiguration" = @{
            "Assign"   = "NoCascade"
            "Delete"   = $DeleteBehavior
            "Merge"    = "NoCascade"
            "Reparent" = "NoCascade"
            "Share"    = "NoCascade"
            "Unshare"  = "NoCascade"
        }
        "Lookup"               = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.LookupAttributeMetadata"
            "SchemaName"    = $LookupSchemaName
            "DisplayName"   = New-Label -Text $DisplayName
            "Description"   = New-Label -Text $Description
            "RequiredLevel" = @{ "Value" = "None" }
        }
    }

    if ($PSCmdlet.ShouldProcess("$ReferencingEntity.$LookupSchemaName", "Create lookup relationship (Delete=$DeleteBehavior)")) {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Endpoint "RelationshipDefinitions" -Method "POST" -Body $body | Out-Null
        Write-Host "    Added: $LookupSchemaName (Delete=$DeleteBehavior)" -ForegroundColor Green
    }
    else {
        Write-Host "    WhatIf: would create lookup $LookupSchemaName (Delete=$DeleteBehavior)" -ForegroundColor Magenta
    }
}

function Add-IntakeRunJobType {
    param([string]$Token, [string]$BaseUrl, [string]$EntityLogicalName, [int]$Value, [string]$Label)

    Write-Host "  Option: sprk_jobtype = $Value '$Label'" -ForegroundColor Gray

    $attr = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='sprk_jobtype')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Options)"
    $options = @($attr.OptionSet.Options)

    $byValue = $options | Where-Object { $_.Value -eq $Value } | Select-Object -First 1
    $byLabel = $options | Where-Object { $_.Label.UserLocalizedLabel.Label -eq $Label } | Select-Object -First 1

    if ($byLabel -and $byLabel.Value -ne $Value) {
        throw "Option '$Label' already exists at value $($byLabel.Value), not $Value. Re-run with -IntakeRunJobTypeValue $($byLabel.Value) or resolve by hand; refusing to create a second option."
    }
    if ($byValue) {
        $existingLabel = $byValue.Label.UserLocalizedLabel.Label
        if ($existingLabel -eq $Label) {
            Write-Host "    Already exists, skipping." -ForegroundColor Yellow
            return
        }
        throw "Value $Value is already taken by '$existingLabel'. Choose another with -IntakeRunJobTypeValue."
    }

    if ($PSCmdlet.ShouldProcess("$EntityLogicalName.sprk_jobtype", "Insert option $Value '$Label'")) {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Endpoint "InsertOptionValue" -Method "POST" -Body @{
            "EntityLogicalName"    = $EntityLogicalName
            "AttributeLogicalName" = "sprk_jobtype"
            "Value"                = $Value
            "Label"                = New-Label -Text $Label
        } | Out-Null
        Write-Host "    Added option $Value '$Label'" -ForegroundColor Green
    }
    else {
        Write-Host "    WhatIf: would add option $Value '$Label'" -ForegroundColor Magenta
    }
}

# ============================================================================
# Main Execution
# ============================================================================

function Main {
    Write-Host ""
    Write-Host "==============================================================" -ForegroundColor Cyan
    Write-Host " Extend sprk_processingjob as the intake run record (FR-06)" -ForegroundColor Cyan
    Write-Host "==============================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Environment: $EnvironmentUrl" -ForegroundColor Yellow
    if ($WhatIfPreference) { Write-Host "Mode:        -WhatIf (read-only; nothing is created or published)" -ForegroundColor Magenta }
    Write-Host ""

    $token = Get-DataverseToken -EnvironmentUrl $EnvironmentUrl
    Write-Host "Authentication successful" -ForegroundColor Green
    Write-Host ""

    # --- Step 1: Verify entities ---
    Write-Host "Step 1: Verifying entities..." -ForegroundColor Cyan
    foreach ($e in @($EntityLogicalName, "sprk_servicerequest", "sprk_document")) {
        if (-not (Test-EntityExists -Token $token -BaseUrl $EnvironmentUrl -LogicalName $e)) {
            throw "$e entity does not exist on $EnvironmentUrl. Cannot extend $EntityLogicalName."
        }
        Write-Host "  $e exists." -ForegroundColor Green
    }

    # --- Step 2: Verify the stage columns can hold intake stages (no type change allowed: Office runs share them) ---
    Write-Host ""
    Write-Host "Step 2: Verifying sprk_currentstage / sprk_stages hold the intake stage values..." -ForegroundColor Cyan
    $cs = Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
        -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='sprk_currentstage')?`$select=AttributeType"
    $st = Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
        -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='sprk_stages')?`$select=AttributeType"
    if ($cs.AttributeType -ne "String" -or $st.AttributeType -ne "Memo") {
        throw "sprk_currentstage is $($cs.AttributeType) and sprk_stages is $($st.AttributeType); expected String and Memo. Intake stages would need a type change that affects Office runs: STOP and escalate."
    }
    Write-Host "  sprk_currentstage = String, sprk_stages = Memo: intake stages fit, no change." -ForegroundColor Green

    # --- Step 3: Lookups ---
    Write-Host ""
    Write-Host "Step 3: Lookups (one request -> many runs)..." -ForegroundColor Cyan
    Add-LookupRelationship -Token $token -BaseUrl $EnvironmentUrl `
        -RelationshipSchemaName "sprk_servicerequest_processingjob_servicerequest" `
        -ReferencedEntity "sprk_servicerequest" -ReferencingEntity $EntityLogicalName `
        -LookupSchemaName "sprk_ServiceRequest" -LookupLogicalName "sprk_servicerequest" `
        -DisplayName "Service Request" `
        -Description "The request this intake run belongs to. One request has many runs. Written only by the BFF." `
        -DeleteBehavior "Restrict"
    Add-LookupRelationship -Token $token -BaseUrl $EnvironmentUrl `
        -RelationshipSchemaName "sprk_document_processingjob_outputdocument" `
        -ReferencedEntity "sprk_document" -ReferencingEntity $EntityLogicalName `
        -LookupSchemaName "sprk_OutputDocument" -LookupLogicalName "sprk_outputdocument" `
        -DisplayName "Output Document" `
        -Description "The document this intake run produced (the redline or clause table), set when the Record stage files it. Written only by the BFF." `
        -DeleteBehavior "RemoveLink"

    # --- Step 4: Columns ---
    Write-Host ""
    Write-Host "Step 4: Columns..." -ForegroundColor Cyan

    Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName $EntityLogicalName -AttributeDef @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = "sprk_harnesskey"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 100
        "DisplayName"   = New-Label -Text "Harness Key"
        "Description"   = New-Label -Text "The harness the intake run was routed to (a routing key such as nda-review). A key only; never content. Written only by the BFF."
    }
    Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName $EntityLogicalName -AttributeDef @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = "sprk_engineexecutionid"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 200
        "DisplayName"   = New-Label -Text "Engine Execution Id"
        "Description"   = New-Label -Text "The Container Apps Job execution id of the engine run, so a resumed run re-attaches instead of starting a second engine. An id only. Written only by the BFF."
    }
    Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName $EntityLogicalName -AttributeDef @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = "sprk_stagingblobprefix"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 500
        "DisplayName"   = New-Label -Text "Staging Blob Prefix"
        "Description"   = New-Label -Text "The run's claim-check blob prefix in the stamp-private staging container (built from ids only, never from document file names). Written only by the BFF."
    }
    Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName $EntityLogicalName -AttributeDef @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
        "SchemaName"    = "sprk_redrivecount"
        "RequiredLevel" = @{ "Value" = "None" }
        "Format"        = "None"
        "MinValue"      = 0
        "MaxValue"      = 2147483647
        "DisplayName"   = New-Label -Text "Re-drive Count"
        "Description"   = New-Label -Text "How many times an operator re-drove this run. Dataverse whole-number columns take no schema default, so null means 0. Written only by the BFF admin re-drive."
    }
    Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName $EntityLogicalName -AttributeDef @{
        "@odata.type"      = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
        "SchemaName"       = "sprk_lastredriveon"
        "RequiredLevel"    = @{ "Value" = "None" }
        "Format"           = "DateAndTime"
        "DateTimeBehavior" = @{ "Value" = "UserLocal" }
        "DisplayName"      = New-Label -Text "Last Re-drive On"
        "Description"      = New-Label -Text "When an operator last re-drove this run. Written only by the BFF admin re-drive."
    }

    # --- Step 5: Job type option ---
    Write-Host ""
    Write-Host "Step 5: Job type option..." -ForegroundColor Cyan
    Add-IntakeRunJobType -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName $EntityLogicalName -Value $IntakeRunJobTypeValue -Label $IntakeRunLabel

    # --- Step 6: Publish ---
    Write-Host ""
    Write-Host "Step 6: Publishing customizations for $EntityLogicalName..." -ForegroundColor Cyan
    if ($PSCmdlet.ShouldProcess($EntityLogicalName, "Publish customizations")) {
        $publishXml = @{
            "ParameterXml" = "<importexportxml><entities><entity>$EntityLogicalName</entity><entity>sprk_servicerequest</entity><entity>sprk_document</entity></entities></importexportxml>"
        }
        try {
            Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl -Endpoint "PublishXml" -Method "POST" -Body $publishXml | Out-Null
            Write-Host "  Customizations published" -ForegroundColor Green
        }
        catch {
            Write-Host "  Warning: publish may have timed out, but the columns should be available: $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }
    else {
        Write-Host "  WhatIf: would publish" -ForegroundColor Magenta
    }

    # --- Step 7: Verify ---
    Write-Host ""
    Write-Host "Step 7: Verifying..." -ForegroundColor Cyan
    $expected = @("sprk_servicerequest", "sprk_outputdocument", "sprk_harnesskey", "sprk_engineexecutionid",
        "sprk_stagingblobprefix", "sprk_redrivecount", "sprk_lastredriveon")
    $missing = @()
    foreach ($col in $expected) {
        if (Test-AttributeExists -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName $EntityLogicalName -AttributeLogicalName $col) {
            Write-Host "  $col - EXISTS" -ForegroundColor Green
        }
        else {
            Write-Host "  $col - MISSING" -ForegroundColor $(if ($WhatIfPreference) { "Magenta" } else { "Red" })
            $missing += $col
        }
    }
    if (-not $WhatIfPreference -and $missing.Count -gt 0) {
        throw "Verification failed: missing $($missing -join ', ')"
    }

    Write-Host ""
    Write-Host "==============================================================" -ForegroundColor Green
    Write-Host " sprk_processingjob intake-run extension $(if ($WhatIfPreference) { 'dry run' } else { 'complete' })" -ForegroundColor Green
    Write-Host "==============================================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Job type: $IntakeRunJobTypeValue = '$IntakeRunLabel'" -ForegroundColor White
    Write-Host "Lookups (Web API @odata.bind names): sprk_ServiceRequest@odata.bind, sprk_OutputDocument@odata.bind" -ForegroundColor Gray
    Write-Host ""
}

Main
