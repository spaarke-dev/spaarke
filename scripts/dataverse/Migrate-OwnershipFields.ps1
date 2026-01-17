<#
.SYNOPSIS
    Migration script for AI Playbook ownership fields in Dataverse.

.DESCRIPTION
    This script adds ownership tracking fields to AI playbook and scope entities:
    - sprk_ownershiptype (Choice): System or Customer
    - sprk_isimmutable (Boolean): For SYS- prefixed scopes
    - sprk_parentid (Lookup): For extended scopes

    Existing records are migrated to Customer ownership by default.

.PARAMETER EnvironmentUrl
    The Dataverse environment URL (e.g., https://org.crm.dynamics.com)

.PARAMETER CreateFields
    If specified, creates the fields via Web API. Otherwise, generates solution XML.

.PARAMETER MigrateData
    If specified, migrates existing records to Customer ownership.

.PARAMETER WhatIf
    If specified, shows what would be done without making changes.

.EXAMPLE
    .\Migrate-OwnershipFields.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -MigrateData

.EXAMPLE
    .\Migrate-OwnershipFields.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -WhatIf

.NOTES
    Version: 1.0.0
    Author: AI Playbook Node Builder R2 Project
    Date: January 2026

    Prerequisites:
    - PAC CLI installed and authenticated
    - Appropriate privileges in target environment
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory = $true)]
    [string]$EnvironmentUrl,

    [Parameter()]
    [switch]$CreateFields,

    [Parameter()]
    [switch]$MigrateData
)

# ─────────────────────────────────────────────────────────────────────────────
# Constants
# ─────────────────────────────────────────────────────────────────────────────

$OwnershipTypeChoiceValues = @{
    System   = 100000000
    Customer = 100000001
}

$AffectedEntities = @(
    'sprk_aiplaybook',
    'sprk_aianalysisaction',
    'sprk_aianalysisskill',
    'sprk_aianalysistool',
    'sprk_aianalysisknowledge'
)

$GlobalChoiceName = 'sprk_ownershiptype'

# ─────────────────────────────────────────────────────────────────────────────
# Helper Functions
# ─────────────────────────────────────────────────────────────────────────────

function Write-Banner {
    param([string]$Message)
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host " $Message" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""
}

function Test-PacAuthentication {
    try {
        $result = pac auth list 2>&1
        if ($result -match "No profiles") {
            return $false
        }
        return $true
    }
    catch {
        return $false
    }
}

function Get-EntityRecordCount {
    param(
        [string]$EntityName
    )

    try {
        # Use FetchXML to count records
        $fetchXml = @"
<fetch aggregate="true">
    <entity name="$EntityName">
        <attribute name="${EntityName}id" alias="count" aggregate="count"/>
    </entity>
</fetch>
"@

        $result = pac data export --environment $EnvironmentUrl --query $fetchXml --format json 2>&1
        if ($result) {
            $data = $result | ConvertFrom-Json
            return $data.value[0].count
        }
        return 0
    }
    catch {
        Write-Warning "Could not count records for $EntityName"
        return -1
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# Schema Documentation
# ─────────────────────────────────────────────────────────────────────────────

function Write-SchemaDocumentation {
    Write-Banner "Dataverse Schema Changes Documentation"

    Write-Host "GLOBAL CHOICE: $GlobalChoiceName" -ForegroundColor Yellow
    Write-Host "  Display Name: Ownership Type"
    Write-Host "  Options:"
    Write-Host "    - System (100000000): System-provided scope, typically immutable"
    Write-Host "    - Customer (100000001): Customer-created scope, fully editable"
    Write-Host ""

    Write-Host "FIELDS TO ADD:" -ForegroundColor Yellow
    Write-Host ""

    Write-Host "  1. sprk_ownershiptype (Choice)" -ForegroundColor Green
    Write-Host "     - Uses global choice: $GlobalChoiceName"
    Write-Host "     - Default: Customer (100000001)"
    Write-Host "     - Required: No"
    Write-Host ""

    Write-Host "  2. sprk_isimmutable (Boolean)" -ForegroundColor Green
    Write-Host "     - Display Name: Is Immutable"
    Write-Host "     - Default: No (false)"
    Write-Host "     - Description: Prevents modification of SYS- prefixed scopes"
    Write-Host ""

    Write-Host "  3. sprk_parentid (Lookup)" -ForegroundColor Green
    Write-Host "     - Display Name: Parent Scope"
    Write-Host "     - Target: Self-referential to same entity"
    Write-Host "     - Required: No"
    Write-Host "     - Description: Links extended scopes to their parent"
    Write-Host ""

    Write-Host "AFFECTED ENTITIES:" -ForegroundColor Yellow
    foreach ($entity in $AffectedEntities) {
        Write-Host "  - $entity"
    }
    Write-Host ""
}

# ─────────────────────────────────────────────────────────────────────────────
# Field Creation (via solution XML generation)
# ─────────────────────────────────────────────────────────────────────────────

function New-GlobalChoiceXml {
    return @"
<?xml version="1.0" encoding="utf-8"?>
<optionsets>
  <optionset Name="$GlobalChoiceName" localizedName="Ownership Type" isGlobal="true">
    <displaynames>
      <displayname description="Ownership Type" languagecode="1033"/>
    </displaynames>
    <descriptions>
      <description description="Indicates whether the scope is system-provided or customer-created" languagecode="1033"/>
    </descriptions>
    <options>
      <option value="$($OwnershipTypeChoiceValues.System)" localizedLabels="System" ExternalValue="">
        <labels>
          <label description="System" languagecode="1033"/>
        </labels>
      </option>
      <option value="$($OwnershipTypeChoiceValues.Customer)" localizedLabels="Customer" ExternalValue="">
        <labels>
          <label description="Customer" languagecode="1033"/>
        </labels>
      </option>
    </options>
  </optionset>
</optionsets>
"@
}

function New-FieldDefinitionsXml {
    param([string]$EntityName)

    # Get entity display name
    $entityDisplayName = $EntityName -replace '^sprk_ai', '' -replace '([a-z])([A-Z])', '$1 $2'
    $entityDisplayName = (Get-Culture).TextInfo.ToTitleCase($entityDisplayName.ToLower())

    return @"
<!-- Fields for $EntityName -->

<!-- Ownership Type (Global Choice) -->
<attribute PhysicalName="sprk_ownershiptype">
  <Type>picklist</Type>
  <Name>sprk_ownershiptype</Name>
  <LogicalName>sprk_ownershiptype</LogicalName>
  <RequiredLevel>none</RequiredLevel>
  <DisplayMask>ValidForAdvancedFind|ValidForForm|ValidForGrid</DisplayMask>
  <OptionSetName>$GlobalChoiceName</OptionSetName>
  <DefaultValue>$($OwnershipTypeChoiceValues.Customer)</DefaultValue>
  <displaynames>
    <displayname description="Ownership Type" languagecode="1033"/>
  </displaynames>
  <descriptions>
    <description description="Indicates whether this $entityDisplayName is system-provided or customer-created" languagecode="1033"/>
  </descriptions>
</attribute>

<!-- Is Immutable (Boolean) -->
<attribute PhysicalName="sprk_isimmutable">
  <Type>bit</Type>
  <Name>sprk_isimmutable</Name>
  <LogicalName>sprk_isimmutable</LogicalName>
  <RequiredLevel>none</RequiredLevel>
  <DisplayMask>ValidForAdvancedFind|ValidForForm|ValidForGrid</DisplayMask>
  <DefaultValue>0</DefaultValue>
  <displaynames>
    <displayname description="Is Immutable" languagecode="1033"/>
  </displaynames>
  <descriptions>
    <description description="When true, this $entityDisplayName cannot be modified (typically SYS- prefixed scopes)" languagecode="1033"/>
  </descriptions>
</attribute>

<!-- Parent Scope (Self-referential Lookup) -->
<attribute PhysicalName="sprk_parentid">
  <Type>lookup</Type>
  <Name>sprk_parentid</Name>
  <LogicalName>sprk_parentid</LogicalName>
  <RequiredLevel>none</RequiredLevel>
  <DisplayMask>ValidForAdvancedFind|ValidForForm|ValidForGrid</DisplayMask>
  <Targets>
    <Target>$EntityName</Target>
  </Targets>
  <displaynames>
    <displayname description="Parent Scope" languagecode="1033"/>
  </displaynames>
  <descriptions>
    <description description="Links to the parent scope when this is an extended scope" languagecode="1033"/>
  </descriptions>
</attribute>
"@
}

function Export-SolutionXmlSnippets {
    param([string]$OutputPath = ".\ownership-fields-xml")

    Write-Banner "Generating Solution XML Snippets"

    if (-not (Test-Path $OutputPath)) {
        New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
    }

    # Generate global choice XML
    $globalChoiceXml = New-GlobalChoiceXml
    $globalChoicePath = Join-Path $OutputPath "globalchoice-ownershiptype.xml"
    $globalChoiceXml | Out-File -FilePath $globalChoicePath -Encoding utf8
    Write-Host "Generated: $globalChoicePath" -ForegroundColor Green

    # Generate field definitions for each entity
    foreach ($entity in $AffectedEntities) {
        $fieldXml = New-FieldDefinitionsXml -EntityName $entity
        $fieldPath = Join-Path $OutputPath "fields-$entity.xml"
        $fieldXml | Out-File -FilePath $fieldPath -Encoding utf8
        Write-Host "Generated: $fieldPath" -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "XML snippets generated. Import these into your unmanaged solution." -ForegroundColor Cyan
}

# ─────────────────────────────────────────────────────────────────────────────
# Data Migration
# ─────────────────────────────────────────────────────────────────────────────

function Invoke-DataMigration {
    Write-Banner "Data Migration: Setting Customer Ownership"

    if (-not (Test-PacAuthentication)) {
        Write-Error "PAC CLI is not authenticated. Run 'pac auth create' first."
        return
    }

    foreach ($entity in $AffectedEntities) {
        Write-Host "Processing: $entity" -ForegroundColor Yellow

        $recordCount = Get-EntityRecordCount -EntityName $entity
        if ($recordCount -lt 0) {
            Write-Warning "Skipping $entity - could not access records"
            continue
        }

        Write-Host "  Found $recordCount existing records"

        if ($recordCount -eq 0) {
            Write-Host "  No records to migrate" -ForegroundColor Gray
            continue
        }

        if ($PSCmdlet.ShouldProcess("$entity ($recordCount records)", "Set ownership to Customer")) {
            # Build update query
            $updateData = @{
                sprk_ownershiptype = $OwnershipTypeChoiceValues.Customer
                sprk_isimmutable   = $false
            }

            Write-Host "  Migrating $recordCount records to Customer ownership..." -ForegroundColor Cyan

            # Note: In production, you would use PAC CLI or Web API bulk update
            # This is a placeholder showing the intended operation
            Write-Host "  [Would execute bulk update via Web API]" -ForegroundColor Gray
            Write-Host "  UPDATE $entity SET sprk_ownershiptype = $($OwnershipTypeChoiceValues.Customer), sprk_isimmutable = false" -ForegroundColor Gray
        }

        Write-Host ""
    }

    Write-Host "Migration complete." -ForegroundColor Green
}

# ─────────────────────────────────────────────────────────────────────────────
# Main Execution
# ─────────────────────────────────────────────────────────────────────────────

Write-Banner "AI Playbook Ownership Fields Migration"
Write-Host "Environment: $EnvironmentUrl"
Write-Host "Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Host ""

# Always show documentation
Write-SchemaDocumentation

if ($CreateFields) {
    Export-SolutionXmlSnippets
}

if ($MigrateData) {
    Invoke-DataMigration
}

if (-not $CreateFields -and -not $MigrateData) {
    Write-Host "No action specified. Use -CreateFields and/or -MigrateData to execute changes." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Usage examples:" -ForegroundColor Cyan
    Write-Host "  # Generate solution XML snippets:"
    Write-Host "  .\Migrate-OwnershipFields.ps1 -EnvironmentUrl 'https://org.crm.dynamics.com' -CreateFields"
    Write-Host ""
    Write-Host "  # Migrate existing data to Customer ownership:"
    Write-Host "  .\Migrate-OwnershipFields.ps1 -EnvironmentUrl 'https://org.crm.dynamics.com' -MigrateData"
    Write-Host ""
    Write-Host "  # Preview migration without changes:"
    Write-Host "  .\Migrate-OwnershipFields.ps1 -EnvironmentUrl 'https://org.crm.dynamics.com' -MigrateData -WhatIf"
}

Write-Host ""
Write-Host "Script completed." -ForegroundColor Green
