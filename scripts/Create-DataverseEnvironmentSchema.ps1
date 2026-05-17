<#
.SYNOPSIS
    Creates the sprk_dataverseenvironment entity and all columns in a Dataverse environment.
.DESCRIPTION
    Creates a reusable environment registry entity with 16 columns:
    - Primary name, string columns, URL column
    - Memo columns (description, license JSON, admin emails)
    - Choice columns (environment type, setup status)
    - Boolean columns (active, default)
    - Integer column (default duration days)
    No lookup columns on this entity (lookups TO this entity are created separately).
.PARAMETER EnvironmentDomain
    The Dataverse environment domain, e.g. spaarkedev1.crm.dynamics.com
.EXAMPLE
    .\Create-DataverseEnvironmentSchema.ps1 -EnvironmentDomain "spaarkedev1.crm.dynamics.com"
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$EnvironmentDomain
)

$ErrorActionPreference = 'Continue'
$token = az account get-access-token --resource "https://$EnvironmentDomain" --query accessToken -o tsv
if (-not $token) { Write-Error "Failed to get token"; exit 1 }
Write-Host "Token acquired" -ForegroundColor Green

$BaseUrl = "https://$EnvironmentDomain/api/data/v9.2"
$headers = @{
    "Authorization" = "Bearer $token"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
    "Content-Type" = "application/json"
    "Accept" = "application/json"
    "Prefer" = "return=representation"
}

function New-Label([string]$Text) {
    @{ "@odata.type" = "Microsoft.Dynamics.CRM.Label"; "LocalizedLabels" = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = $Text; "LanguageCode" = 1033 }) }
}

function Invoke-DV([string]$Ep, [string]$Method = "GET", [object]$Body = $null) {
    $p = @{ Uri = "$BaseUrl/$Ep"; Headers = $headers; Method = $Method; UseBasicParsing = $true }
    if ($Body) { $p.Body = $Body | ConvertTo-Json -Depth 20 -Compress }
    try { $r = Invoke-RestMethod @p; @{ Success = $true; Data = $r } }
    catch { @{ Success = $false; Error = $_.Exception.Message } }
}

# Check existence
try {
    Invoke-RestMethod -Uri "$BaseUrl/EntityDefinitions(LogicalName='sprk_dataverseenvironment')?`$select=LogicalName" -Headers $headers -Method GET -UseBasicParsing -ErrorAction Stop | Out-Null
    Write-Host "Entity already exists - skipping" -ForegroundColor Yellow; exit 0
} catch { Write-Host "Entity not found - creating" -ForegroundColor Cyan }

# Step 1: Entity (with primary name attribute)
Write-Host "Step 1: Create Entity" -ForegroundColor Cyan
$r = Invoke-DV -Ep "EntityDefinitions" -Method "POST" -Body @{
    "@odata.type" = "Microsoft.Dynamics.CRM.EntityMetadata"
    "SchemaName" = "sprk_dataverseenvironment"
    "DisplayName" = New-Label "Dataverse Environment"
    "DisplayCollectionName" = New-Label "Dataverse Environments"
    "Description" = New-Label "Central registry of Dataverse environments for provisioning and configuration."
    "OwnershipType" = "OrganizationOwned"
    "IsActivity" = $false; "HasNotes" = $false; "HasActivities" = $false
    "PrimaryNameAttribute" = "sprk_name"
    "Attributes" = @(@{
        "@odata.type" = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName" = "sprk_name"; "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
        "MaxLength" = 200; "DisplayName" = New-Label "Name"; "IsPrimaryName" = $true
        "Description" = New-Label "Environment display name (e.g. Dev, Demo 1, Partner Trial)"
    })
}
if ($r.Success) { Write-Host "  OK" -ForegroundColor Green } else { Write-Host "  FAIL: $($r.Error)" -ForegroundColor Red; exit 1 }

# Step 2: String columns
Write-Host "Step 2: String columns" -ForegroundColor Cyan
$cols = @(
    @{N="sprk_mdaappid";D="App ID";L=100;R="None";Desc="MDA app GUID for deep links"},
    @{N="sprk_envaccountdomain";D="Account Domain";L=200;R="None";Desc="UPN domain (e.g. demo.spaarke.com)"},
    @{N="sprk_businessunitname";D="Business Unit";L=200;R="None";Desc="Target Dataverse business unit name"},
    @{N="sprk_teamname";D="Security Team";L=200;R="None";Desc="Team with inherited security role"},
    @{N="sprk_specontainerid";D="SPE Container ID";L=500;R="None";Desc="SharePoint Embedded container ID"},
    @{N="sprk_securitygroupid";D="Users Security Group";L=100;R="None";Desc="Entra ID security group for demo users"}
)
foreach ($c in $cols) {
    $r = Invoke-DV -Ep "EntityDefinitions(LogicalName='sprk_dataverseenvironment')/Attributes" -Method "POST" -Body @{
        "@odata.type"="Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"=$c.N; "RequiredLevel"=@{"Value"=$c.R}; "MaxLength"=$c.L; "DisplayName"=New-Label $c.D
        "Description"=New-Label $c.Desc
    }
    if ($r.Success) { Write-Host "  + $($c.N)" -ForegroundColor Green } else { Write-Host "  x $($c.N): $($r.Error)" -ForegroundColor Red }
}

# Step 3: URL column (string with URL format)
Write-Host "Step 3: URL column" -ForegroundColor Cyan
$r = Invoke-DV -Ep "EntityDefinitions(LogicalName='sprk_dataverseenvironment')/Attributes" -Method "POST" -Body @{
    "@odata.type"="Microsoft.Dynamics.CRM.StringAttributeMetadata"
    "SchemaName"="sprk_dataverseurl"; "RequiredLevel"=@{"Value"="ApplicationRequired"}; "MaxLength"=500
    "DisplayName"=New-Label "Dataverse URL"; "Description"=New-Label "Environment URL (e.g. https://spaarke-demo.crm.dynamics.com)"
    "FormatName"=@{"Value"="Url"}
}
if ($r.Success) { Write-Host "  + sprk_dataverseurl" -ForegroundColor Green } else { Write-Host "  x sprk_dataverseurl: $($r.Error)" -ForegroundColor Red }

# Step 4: Memo columns
Write-Host "Step 4: Memo columns" -ForegroundColor Cyan
$memos = @(
    @{N="sprk_description";D="Description";L=2000;Desc="Admin notes about this environment"},
    @{N="sprk_licenseconfigjson";D="License Configuration";L=4000;Desc="JSON with license SKU IDs for provisioning"},
    @{N="sprk_adminemails";D="Admin Notification Emails";L=1000;Desc="Comma-separated admin email addresses"}
)
foreach ($m in $memos) {
    $r = Invoke-DV -Ep "EntityDefinitions(LogicalName='sprk_dataverseenvironment')/Attributes" -Method "POST" -Body @{
        "@odata.type"="Microsoft.Dynamics.CRM.MemoAttributeMetadata"
        "SchemaName"=$m.N; "RequiredLevel"=@{"Value"="None"}; "MaxLength"=$m.L; "DisplayName"=New-Label $m.D
        "Description"=New-Label $m.Desc
    }
    if ($r.Success) { Write-Host "  + $($m.N)" -ForegroundColor Green } else { Write-Host "  x $($m.N): $($m.Error)" -ForegroundColor Red }
}

# Step 5: Choice columns (local option sets)
Write-Host "Step 5: Choice columns" -ForegroundColor Cyan

# Environment Type (Required)
$r = Invoke-DV -Ep "EntityDefinitions(LogicalName='sprk_dataverseenvironment')/Attributes" -Method "POST" -Body @{
    "@odata.type"="Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
    "SchemaName"="sprk_environmenttype"; "RequiredLevel"=@{"Value"="ApplicationRequired"}; "DisplayName"=New-Label "Environment Type"
    "Description"=New-Label "Classification of the environment"
    "OptionSet"=@{"@odata.type"="Microsoft.Dynamics.CRM.OptionSetMetadata";"IsGlobal"=$false;"OptionSetType"="Picklist"
        "Options"=@(
            @{"Value"=0;"Label"=New-Label "Development"},
            @{"Value"=1;"Label"=New-Label "Demo"},
            @{"Value"=2;"Label"=New-Label "Sandbox"},
            @{"Value"=3;"Label"=New-Label "Trial"},
            @{"Value"=4;"Label"=New-Label "Partner"},
            @{"Value"=5;"Label"=New-Label "Training"},
            @{"Value"=6;"Label"=New-Label "Production"}
        )
    }
}
if ($r.Success) { Write-Host "  + sprk_environmenttype" -ForegroundColor Green } else { Write-Host "  x sprk_environmenttype: $($r.Error)" -ForegroundColor Red }

# Setup Status (Optional)
$r = Invoke-DV -Ep "EntityDefinitions(LogicalName='sprk_dataverseenvironment')/Attributes" -Method "POST" -Body @{
    "@odata.type"="Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
    "SchemaName"="sprk_setupstatus"; "RequiredLevel"=@{"Value"="None"}; "DisplayName"=New-Label "Setup Status"
    "Description"=New-Label "Current setup progress for this environment"
    "OptionSet"=@{"@odata.type"="Microsoft.Dynamics.CRM.OptionSetMetadata";"IsGlobal"=$false;"OptionSetType"="Picklist"
        "Options"=@(
            @{"Value"=0;"Label"=New-Label "Not Started"},
            @{"Value"=1;"Label"=New-Label "In Progress"},
            @{"Value"=2;"Label"=New-Label "Ready"},
            @{"Value"=3;"Label"=New-Label "Issue"}
        )
    }
}
if ($r.Success) { Write-Host "  + sprk_setupstatus" -ForegroundColor Green } else { Write-Host "  x sprk_setupstatus: $($r.Error)" -ForegroundColor Red }

# Step 6: Boolean columns
Write-Host "Step 6: Boolean columns" -ForegroundColor Cyan

# Is Active (Required, default Yes)
$r = Invoke-DV -Ep "EntityDefinitions(LogicalName='sprk_dataverseenvironment')/Attributes" -Method "POST" -Body @{
    "@odata.type"="Microsoft.Dynamics.CRM.BooleanAttributeMetadata"
    "SchemaName"="sprk_isactive"; "RequiredLevel"=@{"Value"="ApplicationRequired"}; "DisplayName"=New-Label "Active"
    "Description"=New-Label "Whether this environment is active and available for provisioning"
    "DefaultValue"=$true
    "OptionSet"=@{"TrueOption"=@{"Value"=1;"Label"=New-Label "Yes"};"FalseOption"=@{"Value"=0;"Label"=New-Label "No"}}
}
if ($r.Success) { Write-Host "  + sprk_isactive" -ForegroundColor Green } else { Write-Host "  x sprk_isactive: $($r.Error)" -ForegroundColor Red }

# Is Default (Required, default No)
$r = Invoke-DV -Ep "EntityDefinitions(LogicalName='sprk_dataverseenvironment')/Attributes" -Method "POST" -Body @{
    "@odata.type"="Microsoft.Dynamics.CRM.BooleanAttributeMetadata"
    "SchemaName"="sprk_isdefault"; "RequiredLevel"=@{"Value"="ApplicationRequired"}; "DisplayName"=New-Label "Default Environment"
    "Description"=New-Label "Admin-managed flag indicating the default environment"
    "DefaultValue"=$false
    "OptionSet"=@{"TrueOption"=@{"Value"=1;"Label"=New-Label "Yes"};"FalseOption"=@{"Value"=0;"Label"=New-Label "No"}}
}
if ($r.Success) { Write-Host "  + sprk_isdefault" -ForegroundColor Green } else { Write-Host "  x sprk_isdefault: $($r.Error)" -ForegroundColor Red }

# Step 7: Integer column (NO DefaultValue!)
Write-Host "Step 7: Integer column" -ForegroundColor Cyan
$r = Invoke-DV -Ep "EntityDefinitions(LogicalName='sprk_dataverseenvironment')/Attributes" -Method "POST" -Body @{
    "@odata.type"="Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
    "SchemaName"="sprk_defaultdurationdays"; "RequiredLevel"=@{"Value"="None"}
    "MinValue"=1; "MaxValue"=365
    "DisplayName"=New-Label "Default Duration (Days)"; "Description"=New-Label "Default demo access duration in days"
}
if ($r.Success) { Write-Host "  + sprk_defaultdurationdays" -ForegroundColor Green } else { Write-Host "  x sprk_defaultdurationdays: $($r.Error)" -ForegroundColor Red }

# Step 8: Publish
Write-Host "Step 8: Publish" -ForegroundColor Cyan
$r = Invoke-DV -Ep "PublishXml" -Method "POST" -Body @{
    "ParameterXml"="<importexportxml><entities><entity>sprk_dataverseenvironment</entity></entities></importexportxml>"
}
if ($r.Success) { Write-Host "  Published" -ForegroundColor Green } else { Write-Host "  Publish failed: $($r.Error)" -ForegroundColor Red }

Write-Host "DONE - sprk_dataverseenvironment entity created with 16 columns in $EnvironmentDomain" -ForegroundColor Green
