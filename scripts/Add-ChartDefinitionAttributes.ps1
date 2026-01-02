# Add missing attributes to sprk_chartdefinition entity
param(
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com"
)

$ErrorActionPreference = "Stop"

# Get token
$token = az account get-access-token --resource $EnvironmentUrl --query "accessToken" -o tsv
if ($LASTEXITCODE -ne 0) { throw "Failed to get token" }

Write-Host "Adding attributes to sprk_chartdefinition..." -ForegroundColor Cyan

# Global option set MetadataIds (from previous check)
$visualTypeId = "c16297f1-f5e4-f011-8406-7c1e520aa4df"
$aggTypeId = "564592f7-f5e4-f011-8406-7c1e520aa4df"

# Helper function
function Add-Attribute {
    param([string]$Name, [object]$Def)

    $headers = @{
        "Authorization" = "Bearer $token"
        "Accept" = "application/json"
        "Content-Type" = "application/json"
        "OData-MaxVersion" = "4.0"
        "OData-Version" = "4.0"
    }

    $uri = "$EnvironmentUrl/api/data/v9.2/EntityDefinitions(LogicalName='sprk_chartdefinition')/Attributes"
    $body = $Def | ConvertTo-Json -Depth 20

    Write-Host "  Adding $Name..." -ForegroundColor Gray
    try {
        Invoke-RestMethod -Uri $uri -Method POST -Headers $headers -Body $body | Out-Null
        Write-Host "    Added $Name" -ForegroundColor Green
    } catch {
        $errorMessage = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            $errorJson = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($errorJson.error.message) {
                $errorMessage = $errorJson.error.message
            }
        }
        if ($errorMessage -match "already exists" -or $errorMessage -match "duplicate") {
            Write-Host "    $Name already exists, skipping" -ForegroundColor Yellow
        } else {
            Write-Host "    Failed: $errorMessage" -ForegroundColor Red
        }
    }
}

# 1. sprk_visualtype (Picklist)
Add-Attribute -Name "sprk_visualtype" -Def @{
    "@odata.type" = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
    "SchemaName" = "sprk_visualtype"
    "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
    "DisplayName" = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(@{
            "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
            "Label" = "Visual Type"
            "LanguageCode" = 1033
        })
    }
    "Description" = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(@{
            "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
            "Label" = "Type of visual to render"
            "LanguageCode" = 1033
        })
    }
    "GlobalOptionSet@odata.bind" = "/GlobalOptionSetDefinitions($visualTypeId)"
}

# 2. sprk_entitylogicalname (String)
Add-Attribute -Name "sprk_entitylogicalname" -Def @{
    "@odata.type" = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
    "SchemaName" = "sprk_entitylogicalname"
    "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
    "MaxLength" = 100
    "DisplayName" = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(@{
            "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
            "Label" = "Entity Logical Name"
            "LanguageCode" = 1033
        })
    }
    "Description" = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(@{
            "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
            "Label" = "Dataverse entity to query"
            "LanguageCode" = 1033
        })
    }
}

# 3. sprk_baseviewid (String)
Add-Attribute -Name "sprk_baseviewid" -Def @{
    "@odata.type" = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
    "SchemaName" = "sprk_baseviewid"
    "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
    "MaxLength" = 50
    "DisplayName" = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(@{
            "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
            "Label" = "Base View ID"
            "LanguageCode" = 1033
        })
    }
    "Description" = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(@{
            "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
            "Label" = "GUID of SavedQuery or UserQuery to use"
            "LanguageCode" = 1033
        })
    }
}

# 4. sprk_aggregationfield (String - optional)
Add-Attribute -Name "sprk_aggregationfield" -Def @{
    "@odata.type" = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
    "SchemaName" = "sprk_aggregationfield"
    "RequiredLevel" = @{ "Value" = "None" }
    "MaxLength" = 100
    "DisplayName" = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(@{
            "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
            "Label" = "Aggregation Field"
            "LanguageCode" = 1033
        })
    }
    "Description" = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(@{
            "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
            "Label" = "Field to aggregate (null for count-only)"
            "LanguageCode" = 1033
        })
    }
}

# 5. sprk_aggregationtype (Picklist - optional)
Add-Attribute -Name "sprk_aggregationtype" -Def @{
    "@odata.type" = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
    "SchemaName" = "sprk_aggregationtype"
    "RequiredLevel" = @{ "Value" = "None" }
    "DisplayName" = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(@{
            "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
            "Label" = "Aggregation Type"
            "LanguageCode" = 1033
        })
    }
    "Description" = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(@{
            "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
            "Label" = "Type of aggregation (default: count)"
            "LanguageCode" = 1033
        })
    }
    "GlobalOptionSet@odata.bind" = "/GlobalOptionSetDefinitions($aggTypeId)"
}

# 6. sprk_groupbyfield (String - optional)
Add-Attribute -Name "sprk_groupbyfield" -Def @{
    "@odata.type" = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
    "SchemaName" = "sprk_groupbyfield"
    "RequiredLevel" = @{ "Value" = "None" }
    "MaxLength" = 100
    "DisplayName" = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(@{
            "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
            "Label" = "Group By Field"
            "LanguageCode" = 1033
        })
    }
    "Description" = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(@{
            "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
            "Label" = "Field to group by (for charts with categories)"
            "LanguageCode" = 1033
        })
    }
}

# 7. sprk_optionsjson (Memo/Multiline)
Add-Attribute -Name "sprk_optionsjson" -Def @{
    "@odata.type" = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
    "SchemaName" = "sprk_optionsjson"
    "RequiredLevel" = @{ "Value" = "None" }
    "MaxLength" = 100000
    "DisplayName" = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(@{
            "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
            "Label" = "Options JSON"
            "LanguageCode" = 1033
        })
    }
    "Description" = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(@{
            "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
            "Label" = "JSON blob for per-visual-type advanced options"
            "LanguageCode" = 1033
        })
    }
}

Write-Host ""
Write-Host "Publishing customizations..." -ForegroundColor Cyan
$headers = @{
    "Authorization" = "Bearer $token"
    "Accept" = "application/json"
    "Content-Type" = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
}
$publishBody = @{
    "ParameterXml" = "<importexportxml><entities><entity>sprk_chartdefinition</entity></entities></importexportxml>"
} | ConvertTo-Json

try {
    Invoke-RestMethod -Uri "$EnvironmentUrl/api/data/v9.2/PublishXml" -Method POST -Headers $headers -Body $publishBody | Out-Null
    Write-Host "  Published" -ForegroundColor Green
} catch {
    Write-Host "  Publish warning (may still work): $_" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Done! Verify in Power Apps maker portal." -ForegroundColor Green
