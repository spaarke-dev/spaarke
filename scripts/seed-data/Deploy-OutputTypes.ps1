# Deploy-OutputTypes.ps1
# Populate Output Type records and associate with Document Profile playbook
#
# Usage:
#   .\Deploy-OutputTypes.ps1
#   .\Deploy-OutputTypes.ps1 -DryRun
#
# Prerequisites:
#   - Playbooks must be deployed first (PB-011 "Document Profile" must exist)
#   - Run: .\Deploy-Playbooks.ps1

param(
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",
    [switch]$DryRun = $false,
    [switch]$Force = $false
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$JsonPath = Join-Path $ScriptDir "output-types.json"

Write-Host "=== Deploy Output Types ===" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl"
Write-Host "JSON Source: $JsonPath"
if ($DryRun) {
    Write-Host "Mode: DRY RUN" -ForegroundColor Yellow
} else {
    Write-Host "Mode: LIVE"
}
Write-Host ""

# Load JSON
if (-not (Test-Path $JsonPath)) {
    Write-Error "JSON file not found: $JsonPath"
    exit 1
}

$data = Get-Content $JsonPath -Raw | ConvertFrom-Json

# Get access token
Write-Host "Getting access token..." -ForegroundColor Gray
$token = az account get-access-token --resource $EnvironmentUrl --query 'accessToken' -o tsv

if (-not $token) {
    Write-Error "Failed to get access token. Run 'az login' first."
    exit 1
}

$headers = @{
    'Authorization' = "Bearer $token"
    'Accept' = 'application/json'
    'Content-Type' = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version' = '4.0'
    'Prefer' = 'return=representation'
}

# Function to query record GUID by name
function Get-RecordGuid {
    param(
        [string]$EntitySet,
        [string]$Name,
        [string]$IdField
    )

    $filter = "`$filter=sprk_name eq '$Name'"
    $uri = "$EnvironmentUrl/api/data/v9.2/${EntitySet}?$filter&`$select=$IdField"

    try {
        $result = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get
        if ($result.value.Count -gt 0) {
            return $result.value[0].$IdField
        }
        return $null
    } catch {
        Write-Host "  Warning: Failed to query $EntitySet for '$Name'" -ForegroundColor Yellow
        return $null
    }
}

# Function to check if output type exists
function Test-OutputTypeExists {
    param([string]$Name)

    $filter = "`$filter=sprk_name eq '$Name'"
    $uri = "$EnvironmentUrl/api/data/v9.2/sprk_aioutputtypes?$filter&`$select=sprk_aioutputtypeid"

    try {
        $result = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get
        if ($result.value.Count -gt 0) {
            return $result.value[0].sprk_aioutputtypeid
        }
        return $null
    } catch {
        return $null
    }
}

# Function to associate N:N relationship
function Add-Association {
    param(
        [string]$PlaybookId,
        [string]$RelationshipName,
        [string]$TargetEntitySet,
        [string]$TargetId
    )

    $uri = "$EnvironmentUrl/api/data/v9.2/sprk_analysisplaybooks($PlaybookId)/$RelationshipName/`$ref"
    $body = @{
        "@odata.id" = "$EnvironmentUrl/api/data/v9.2/$TargetEntitySet($TargetId)"
    } | ConvertTo-Json

    try {
        Invoke-RestMethod -Uri $uri -Headers $headers -Method Post -Body $body | Out-Null
        return $true
    } catch {
        # Association may already exist - ignore 400 errors
        if ($_.Exception.Response.StatusCode -eq 400) {
            return $true
        }
        return $false
    }
}

# Function to create output type
function New-OutputTypeRecord {
    param([object]$OutputType)

    $name = $OutputType.sprk_name
    $existingId = Test-OutputTypeExists -Name $name

    # Check if exists
    if (-not $Force -and $existingId) {
        Write-Host "  SKIP: '$name' already exists (ID: $existingId)" -ForegroundColor Yellow
        return @{ Status = "Skipped"; Name = $name; Id = $existingId }
    }

    if ($DryRun) {
        Write-Host "  WOULD INSERT: '$name'" -ForegroundColor Gray
        Write-Host "    Target Entity: $($OutputType.fieldMapping.targetEntity)" -ForegroundColor Gray
        Write-Host "    Target Field: $($OutputType.fieldMapping.targetField)" -ForegroundColor Gray
        return @{ Status = "DryRun"; Name = $name }
    }

    # Create the output type record
    $record = @{
        "sprk_name" = $OutputType.sprk_name
        "sprk_description" = $OutputType.sprk_description
    }

    # Add field mapping properties if they exist
    if ($OutputType.fieldMapping) {
        if ($OutputType.fieldMapping.targetEntity) {
            $record["sprk_targetentity"] = $OutputType.fieldMapping.targetEntity
        }
        if ($OutputType.fieldMapping.targetField) {
            $record["sprk_targetfield"] = $OutputType.fieldMapping.targetField
        }
        if ($OutputType.fieldMapping.fieldType) {
            $record["sprk_fieldtype"] = $OutputType.fieldMapping.fieldType
        }
        if ($OutputType.fieldMapping.maxLength) {
            $record["sprk_maxlength"] = $OutputType.fieldMapping.maxLength
        }
    }

    # Add tool reference if it exists
    if ($OutputType.toolReference) {
        $record["sprk_toolreference"] = $OutputType.toolReference
    }

    # Add priority if it exists
    if ($OutputType.priority) {
        $record["sprk_priority"] = $OutputType.priority
    }

    $uri = "$EnvironmentUrl/api/data/v9.2/sprk_aioutputtypes"
    $body = $record | ConvertTo-Json -Depth 10

    try {
        $result = Invoke-RestMethod -Uri $uri -Headers $headers -Method Post -Body $body

        # Query to get the ID
        $createdOutputType = Test-OutputTypeExists -Name $name
        if (-not $createdOutputType) {
            throw "Failed to retrieve created output type ID"
        }
        $outputTypeId = $createdOutputType

        Write-Host "  INSERTED: '$name' (ID: $outputTypeId)" -ForegroundColor Green

        return @{ Status = "Inserted"; Name = $name; Id = $outputTypeId }

    } catch {
        $errorMsg = $_.Exception.Message
        Write-Host "  ERROR: '$name' - $errorMsg" -ForegroundColor Red
        return @{ Status = "Error"; Name = $name; Error = $errorMsg }
    }
}

# Process output types
$summary = @{
    Inserted = 0
    Skipped = 0
    Errors = 0
    DryRun = 0
}

Write-Host "Processing $($data.outputTypes.Count) Output Type records..." -ForegroundColor Cyan
Write-Host ""

$createdOutputTypes = @{}

foreach ($outputType in $data.outputTypes) {
    $result = New-OutputTypeRecord -OutputType $outputType
    $summary[$result.Status]++

    if ($result.Id) {
        $createdOutputTypes[$outputType.id] = $result.Id
    }

    Write-Host ""
}

# Associate output types with playbooks
if (-not $DryRun -and $data.playbookAssociations) {
    Write-Host ""
    Write-Host "Associating output types with playbooks..." -ForegroundColor Cyan
    Write-Host ""

    foreach ($association in $data.playbookAssociations) {
        $playbookName = $association.playbookName
        $playbookId = Get-RecordGuid -EntitySet "sprk_analysisplaybooks" -Name $playbookName -IdField "sprk_analysisplaybookid"

        if (-not $playbookId) {
            Write-Host "  WARNING: Playbook '$playbookName' not found - skipping associations" -ForegroundColor Yellow
            continue
        }

        Write-Host "  Associating with playbook: $playbookName (ID: $playbookId)" -ForegroundColor Gray

        $associationCount = 0
        foreach ($outputTypeRefId in $association.outputTypeIds) {
            $outputTypeGuid = $createdOutputTypes[$outputTypeRefId]

            if (-not $outputTypeGuid) {
                # Try to find it in Dataverse
                $outputTypeName = ($data.outputTypes | Where-Object { $_.id -eq $outputTypeRefId }).sprk_name
                if ($outputTypeName) {
                    $outputTypeGuid = Test-OutputTypeExists -Name $outputTypeName
                }
            }

            if ($outputTypeGuid) {
                $success = Add-Association `
                    -PlaybookId $playbookId `
                    -RelationshipName "sprk_playbook_outputtype" `
                    -TargetEntitySet "sprk_aioutputtypes" `
                    -TargetId $outputTypeGuid

                if ($success) {
                    $associationCount++
                    Write-Host "    + Output Type: $outputTypeRefId" -ForegroundColor DarkGreen
                } else {
                    Write-Host "    ! Output Type: $outputTypeRefId failed" -ForegroundColor Red
                }
            } else {
                Write-Host "    ! Output Type: $outputTypeRefId not found" -ForegroundColor Red
            }
        }

        Write-Host "  Associated $associationCount output types with $playbookName" -ForegroundColor Green
        Write-Host ""
    }
}

# Summary
Write-Host "=== Summary ===" -ForegroundColor Cyan
Write-Host "Inserted: $($summary.Inserted)" -ForegroundColor Green
Write-Host "Skipped:  $($summary.Skipped)" -ForegroundColor Yellow
Write-Host "Errors:   $($summary.Errors)" -ForegroundColor Red
if ($DryRun) {
    Write-Host "DryRun:   $($summary.DryRun)" -ForegroundColor Gray
}

if ($summary.Errors -gt 0) {
    exit 1
}

Write-Host ""
Write-Host "Done!" -ForegroundColor Green
