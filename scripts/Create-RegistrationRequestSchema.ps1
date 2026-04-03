<#
.SYNOPSIS
    Creates the sprk_registrationrequest entity and all columns in a Dataverse environment.
.PARAMETER EnvironmentDomain
    The Dataverse environment domain, e.g. spaarkedev1.crm.dynamics.com
.EXAMPLE
    .\Create-RegistrationRequestSchema.ps1 -EnvironmentDomain "spaarkedev1.crm.dynamics.com"
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
    Invoke-RestMethod -Uri "$BaseUrl/EntityDefinitions(LogicalName='sprk_registrationrequest')?`$select=LogicalName" -Headers $headers -Method GET -UseBasicParsing -ErrorAction Stop | Out-Null
    Write-Host "Entity already exists - skipping" -ForegroundColor Yellow; exit 0
} catch { Write-Host "Entity not found - creating" -ForegroundColor Cyan }

# Step 1: Entity
Write-Host "Step 1: Create Entity" -ForegroundColor Cyan
$r = Invoke-DV -Ep "EntityDefinitions" -Method "POST" -Body @{
    "@odata.type" = "Microsoft.Dynamics.CRM.EntityMetadata"
    "SchemaName" = "sprk_registrationrequest"
    "DisplayName" = New-Label "Registration Request"
    "DisplayCollectionName" = New-Label "Registration Requests"
    "Description" = New-Label "Tracks demo access requests."
    "OwnershipType" = "OrganizationOwned"
    "IsActivity" = $false; "HasNotes" = $false; "HasActivities" = $true
    "PrimaryNameAttribute" = "sprk_name"
    "Attributes" = @(@{
        "@odata.type" = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName" = "sprk_name"; "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
        "MaxLength" = 200; "DisplayName" = New-Label "Name"; "IsPrimaryName" = $true
    })
}
if ($r.Success) { Write-Host "  OK" -ForegroundColor Green } else { Write-Host "  FAIL: $($r.Error)" -ForegroundColor Red; exit 1 }

# Step 2: String columns
Write-Host "Step 2: String columns" -ForegroundColor Cyan
$cols = @(
    @{N="sprk_firstname";D="First Name";L=100;R="ApplicationRequired"},
    @{N="sprk_lastname";D="Last Name";L=100;R="ApplicationRequired"},
    @{N="sprk_email";D="Email";L=200;R="ApplicationRequired"},
    @{N="sprk_organization";D="Organization";L=200;R="ApplicationRequired"},
    @{N="sprk_jobtitle";D="Job Title";L=200;R="None"},
    @{N="sprk_phone";D="Phone";L=50;R="None"},
    @{N="sprk_trackingid";D="Tracking ID";L=50;R="None"},
    @{N="sprk_rejectionreason";D="Rejection Reason";L=500;R="None"},
    @{N="sprk_demousername";D="Demo Username";L=200;R="None"},
    @{N="sprk_demouserobjectid";D="Demo User Object ID";L=50;R="None"}
)
foreach ($c in $cols) {
    $r = Invoke-DV -Ep "EntityDefinitions(LogicalName='sprk_registrationrequest')/Attributes" -Method "POST" -Body @{
        "@odata.type"="Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"=$c.N; "RequiredLevel"=@{"Value"=$c.R}; "MaxLength"=$c.L; "DisplayName"=New-Label $c.D
    }
    if ($r.Success) { Write-Host "  + $($c.N)" -ForegroundColor Green } else { Write-Host "  x $($c.N)" -ForegroundColor Red }
}

# Step 3: Memo
Write-Host "Step 3: Memo" -ForegroundColor Cyan
$r = Invoke-DV -Ep "EntityDefinitions(LogicalName='sprk_registrationrequest')/Attributes" -Method "POST" -Body @{
    "@odata.type"="Microsoft.Dynamics.CRM.MemoAttributeMetadata"
    "SchemaName"="sprk_notes"; "RequiredLevel"=@{"Value"="None"}; "MaxLength"=10000; "DisplayName"=New-Label "Notes"
}
if ($r.Success) { Write-Host "  + sprk_notes" -ForegroundColor Green } else { Write-Host "  x sprk_notes" -ForegroundColor Red }

# Step 4: Choices
Write-Host "Step 4: Choices" -ForegroundColor Cyan

$r = Invoke-DV -Ep "EntityDefinitions(LogicalName='sprk_registrationrequest')/Attributes" -Method "POST" -Body @{
    "@odata.type"="Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
    "SchemaName"="sprk_usecase"; "RequiredLevel"=@{"Value"="ApplicationRequired"}; "DisplayName"=New-Label "Use Case"
    "OptionSet"=@{"@odata.type"="Microsoft.Dynamics.CRM.OptionSetMetadata";"IsGlobal"=$false;"OptionSetType"="Picklist"
        "Options"=@(
            @{"Value"=0;"Label"=New-Label "Document Management"},
            @{"Value"=1;"Label"=New-Label "AI Analysis"},
            @{"Value"=2;"Label"=New-Label "Financial Intelligence"},
            @{"Value"=3;"Label"=New-Label "General"}
        )
    }
}
if ($r.Success) { Write-Host "  + sprk_usecase" -ForegroundColor Green } else { Write-Host "  x sprk_usecase" -ForegroundColor Red }

$r = Invoke-DV -Ep "EntityDefinitions(LogicalName='sprk_registrationrequest')/Attributes" -Method "POST" -Body @{
    "@odata.type"="Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
    "SchemaName"="sprk_referralsource"; "RequiredLevel"=@{"Value"="None"}; "DisplayName"=New-Label "Referral Source"
    "OptionSet"=@{"@odata.type"="Microsoft.Dynamics.CRM.OptionSetMetadata";"IsGlobal"=$false;"OptionSetType"="Picklist"
        "Options"=@(
            @{"Value"=0;"Label"=New-Label "Conference"},@{"Value"=1;"Label"=New-Label "Website"},
            @{"Value"=2;"Label"=New-Label "Referral"},@{"Value"=3;"Label"=New-Label "Search"},
            @{"Value"=4;"Label"=New-Label "Other"}
        )
    }
}
if ($r.Success) { Write-Host "  + sprk_referralsource" -ForegroundColor Green } else { Write-Host "  x sprk_referralsource" -ForegroundColor Red }

$r = Invoke-DV -Ep "EntityDefinitions(LogicalName='sprk_registrationrequest')/Attributes" -Method "POST" -Body @{
    "@odata.type"="Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
    "SchemaName"="sprk_status"; "RequiredLevel"=@{"Value"="ApplicationRequired"}; "DisplayName"=New-Label "Status"
    "OptionSet"=@{"@odata.type"="Microsoft.Dynamics.CRM.OptionSetMetadata";"IsGlobal"=$false;"OptionSetType"="Picklist"
        "Options"=@(
            @{"Value"=0;"Label"=New-Label "Submitted"},@{"Value"=1;"Label"=New-Label "Approved"},
            @{"Value"=2;"Label"=New-Label "Rejected"},@{"Value"=3;"Label"=New-Label "Provisioned"},
            @{"Value"=4;"Label"=New-Label "Expired"},@{"Value"=5;"Label"=New-Label "Revoked"}
        )
    }
}
if ($r.Success) { Write-Host "  + sprk_status" -ForegroundColor Green } else { Write-Host "  x sprk_status" -ForegroundColor Red }

# Step 5: DateTimes
Write-Host "Step 5: DateTimes" -ForegroundColor Cyan
foreach ($d in @("sprk_requestdate","sprk_reviewdate","sprk_provisioneddate","sprk_expirationdate","sprk_consentdate")) {
    $dn = ($d -replace "sprk_","" -replace "date","" -replace "([a-z])([A-Z])",'$1 $2').Trim()
    $r = Invoke-DV -Ep "EntityDefinitions(LogicalName='sprk_registrationrequest')/Attributes" -Method "POST" -Body @{
        "@odata.type"="Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
        "SchemaName"=$d; "RequiredLevel"=@{"Value"="None"}; "DisplayName"=New-Label $d
        "Format"="DateAndTime"; "DateTimeBehavior"=@{"Value"="UserLocal"}
    }
    if ($r.Success) { Write-Host "  + $d" -ForegroundColor Green } else { Write-Host "  x $d" -ForegroundColor Red }
}

# Step 6: Boolean
Write-Host "Step 6: Boolean" -ForegroundColor Cyan
$r = Invoke-DV -Ep "EntityDefinitions(LogicalName='sprk_registrationrequest')/Attributes" -Method "POST" -Body @{
    "@odata.type"="Microsoft.Dynamics.CRM.BooleanAttributeMetadata"
    "SchemaName"="sprk_consentaccepted"; "RequiredLevel"=@{"Value"="None"}; "DisplayName"=New-Label "Consent Accepted"
    "OptionSet"=@{"TrueOption"=@{"Value"=1;"Label"=New-Label "Yes"};"FalseOption"=@{"Value"=0;"Label"=New-Label "No"}}
}
if ($r.Success) { Write-Host "  + sprk_consentaccepted" -ForegroundColor Green } else { Write-Host "  x sprk_consentaccepted" -ForegroundColor Red }

# Step 7: Lookup
Write-Host "Step 7: Lookup" -ForegroundColor Cyan
$r = Invoke-DV -Ep "RelationshipDefinitions" -Method "POST" -Body @{
    "@odata.type"="Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata"
    "SchemaName"="sprk_systemuser_rr_reviewedby"; "ReferencedEntity"="systemuser"; "ReferencingEntity"="sprk_registrationrequest"
    "CascadeConfiguration"=@{"Assign"="NoCascade";"Delete"="RemoveLink";"Merge"="NoCascade";"Reparent"="NoCascade";"Share"="NoCascade";"Unshare"="NoCascade"}
    "Lookup"=@{"SchemaName"="sprk_reviewedby";"DisplayName"=New-Label "Reviewed By";"RequiredLevel"=@{"Value"="None"}}
}
if ($r.Success) { Write-Host "  + sprk_reviewedby" -ForegroundColor Green } else { Write-Host "  x lookup" -ForegroundColor Red }

# Step 8: Publish
Write-Host "Step 8: Publish" -ForegroundColor Cyan
$r = Invoke-DV -Ep "PublishXml" -Method "POST" -Body @{
    "ParameterXml"="<importexportxml><entities><entity>sprk_registrationrequest</entity></entities></importexportxml>"
}
if ($r.Success) { Write-Host "  Published" -ForegroundColor Green } else { Write-Host "  Publish failed" -ForegroundColor Red }

Write-Host "DONE - 22 columns created in $EnvironmentDomain" -ForegroundColor Green
