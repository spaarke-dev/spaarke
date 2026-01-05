# Deploy-Playbooks.ps1
# Populate Playbook records with N:N relationships for AI Document Intelligence R4
#
# Usage:
#   .\Deploy-Playbooks.ps1
#   .\Deploy-Playbooks.ps1 -DryRun
#
# Prerequisites:
#   - Actions, Tools, Knowledge, and Skills must be deployed first
#   - Run: .\Deploy-Actions.ps1, .\Deploy-Tools.ps1, .\Deploy-Knowledge.ps1, .\Deploy-Skills.ps1

param(
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",
    [switch]$DryRun = $false,
    [switch]$Force = $false
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$JsonPath = Join-Path $ScriptDir "playbooks.json"

Write-Host "=== Deploy Playbooks ===" -ForegroundColor Cyan
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

# Build lookup tables for scope IDs
Write-Host "Building scope lookups..." -ForegroundColor Gray

# Load Actions JSON for ID mapping
$actionsJson = Get-Content (Join-Path $ScriptDir "actions.json") -Raw | ConvertFrom-Json
$actionNameToId = @{}
foreach ($action in $actionsJson.actions) {
    $actionNameToId[$action.id] = $null  # Will be populated from Dataverse
}

# Load Tools JSON for ID mapping
$toolsJson = Get-Content (Join-Path $ScriptDir "tools.json") -Raw | ConvertFrom-Json
$toolNameToId = @{}
foreach ($tool in $toolsJson.tools) {
    $toolNameToId[$tool.id] = $null
}

# Load Knowledge JSON for ID mapping
$knowledgeJson = Get-Content (Join-Path $ScriptDir "knowledge.json") -Raw | ConvertFrom-Json
$knowledgeNameToId = @{}
foreach ($knowledge in $knowledgeJson.knowledge) {
    $knowledgeNameToId[$knowledge.id] = $null
}

# Load Skills JSON for ID mapping
$skillsJson = Get-Content (Join-Path $ScriptDir "skills.json") -Raw | ConvertFrom-Json
$skillNameToId = @{}
foreach ($skill in $skillsJson.skills) {
    $skillNameToId[$skill.id] = $null
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

# Populate Action GUIDs from Dataverse
Write-Host "  Loading Actions from Dataverse..." -ForegroundColor Gray
foreach ($action in $actionsJson.actions) {
    $guid = Get-RecordGuid -EntitySet "sprk_analysisactions" -Name $action.sprk_name -IdField "sprk_analysisactionid"
    if ($guid) {
        $actionNameToId[$action.id] = $guid
    } else {
        Write-Host "    Warning: Action '$($action.id)' not found in Dataverse" -ForegroundColor Yellow
    }
}

# Populate Tool GUIDs from Dataverse
Write-Host "  Loading Tools from Dataverse..." -ForegroundColor Gray
foreach ($tool in $toolsJson.tools) {
    $guid = Get-RecordGuid -EntitySet "sprk_analysistools" -Name $tool.sprk_name -IdField "sprk_analysistoolid"
    if ($guid) {
        $toolNameToId[$tool.id] = $guid
    } else {
        Write-Host "    Warning: Tool '$($tool.id)' not found in Dataverse" -ForegroundColor Yellow
    }
}

# Populate Knowledge GUIDs from Dataverse
Write-Host "  Loading Knowledge from Dataverse..." -ForegroundColor Gray
foreach ($knowledge in $knowledgeJson.knowledge) {
    $guid = Get-RecordGuid -EntitySet "sprk_analysisknowledges" -Name $knowledge.sprk_name -IdField "sprk_analysisknowledgeid"
    if ($guid) {
        $knowledgeNameToId[$knowledge.id] = $guid
    } else {
        Write-Host "    Warning: Knowledge '$($knowledge.id)' not found in Dataverse" -ForegroundColor Yellow
    }
}

# Populate Skill GUIDs from Dataverse
Write-Host "  Loading Skills from Dataverse..." -ForegroundColor Gray
foreach ($skill in $skillsJson.skills) {
    $guid = Get-RecordGuid -EntitySet "sprk_analysisskills" -Name $skill.sprk_name -IdField "sprk_analysisskillid"
    if ($guid) {
        $skillNameToId[$skill.id] = $guid
    } else {
        Write-Host "    Warning: Skill '$($skill.id)' not found in Dataverse" -ForegroundColor Yellow
    }
}

Write-Host ""

# Function to check if playbook exists
function Test-PlaybookExists {
    param([string]$Name)

    $filter = "`$filter=sprk_name eq '$Name'"
    $uri = "$EnvironmentUrl/api/data/v9.2/sprk_analysisplaybooks?$filter&`$select=sprk_analysisplaybookid"

    try {
        $result = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get
        if ($result.value.Count -gt 0) {
            return $result.value[0].sprk_analysisplaybookid
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

# Function to create playbook with associations
function New-PlaybookRecord {
    param([object]$Playbook)

    $name = $Playbook.sprk_name
    $existingId = Test-PlaybookExists -Name $name

    # Check if exists
    if (-not $Force -and $existingId) {
        Write-Host "  SKIP: '$name' already exists (ID: $existingId)" -ForegroundColor Yellow
        return @{ Status = "Skipped"; Name = $name; Id = $existingId }
    }

    if ($DryRun) {
        Write-Host "  WOULD INSERT: '$name'" -ForegroundColor Gray
        Write-Host "    Skills: $($Playbook.scopes.skills -join ', ')"
        Write-Host "    Actions: $($Playbook.scopes.actions -join ', ')"
        Write-Host "    Knowledge: $($Playbook.scopes.knowledge -join ', ')"
        Write-Host "    Tools: $($Playbook.scopes.tools -join ', ')"
        return @{ Status = "DryRun"; Name = $name }
    }

    # Create the playbook record
    $record = @{
        "sprk_name" = $Playbook.sprk_name
        "sprk_description" = $Playbook.sprk_description
        "sprk_ispublic" = $Playbook.sprk_ispublic
    }

    $uri = "$EnvironmentUrl/api/data/v9.2/sprk_analysisplaybooks"
    $body = $record | ConvertTo-Json -Depth 10

    try {
        $result = Invoke-RestMethod -Uri $uri -Headers $headers -Method Post -Body $body

        # Get the created playbook ID from response headers
        $responseHeaders = $result.PSObject.Properties.Name

        # Query to get the ID
        $createdPlaybook = Test-PlaybookExists -Name $name
        if (-not $createdPlaybook) {
            throw "Failed to retrieve created playbook ID"
        }
        $playbookId = $createdPlaybook

        Write-Host "  INSERTED: '$name' (ID: $playbookId)" -ForegroundColor Green

        # Create N:N associations
        $associationErrors = 0

        # Associate Skills
        foreach ($skillId in $Playbook.scopes.skills) {
            $targetGuid = $skillNameToId[$skillId]
            if ($targetGuid) {
                $success = Add-Association -PlaybookId $playbookId -RelationshipName "sprk_playbook_skill" -TargetEntitySet "sprk_analysisskills" -TargetId $targetGuid
                if ($success) {
                    Write-Host "    + Skill: $skillId" -ForegroundColor DarkGreen
                } else {
                    Write-Host "    ! Skill: $skillId failed" -ForegroundColor Red
                    $associationErrors++
                }
            } else {
                Write-Host "    ! Skill: $skillId not found" -ForegroundColor Red
                $associationErrors++
            }
        }

        # Associate Actions
        foreach ($actionId in $Playbook.scopes.actions) {
            $targetGuid = $actionNameToId[$actionId]
            if ($targetGuid) {
                $success = Add-Association -PlaybookId $playbookId -RelationshipName "sprk_analysisplaybook_action" -TargetEntitySet "sprk_analysisactions" -TargetId $targetGuid
                if ($success) {
                    Write-Host "    + Action: $actionId" -ForegroundColor DarkGreen
                } else {
                    Write-Host "    ! Action: $actionId failed" -ForegroundColor Red
                    $associationErrors++
                }
            } else {
                Write-Host "    ! Action: $actionId not found" -ForegroundColor Red
                $associationErrors++
            }
        }

        # Associate Knowledge
        foreach ($knowledgeId in $Playbook.scopes.knowledge) {
            $targetGuid = $knowledgeNameToId[$knowledgeId]
            if ($targetGuid) {
                $success = Add-Association -PlaybookId $playbookId -RelationshipName "sprk_playbook_knowledge" -TargetEntitySet "sprk_analysisknowledges" -TargetId $targetGuid
                if ($success) {
                    Write-Host "    + Knowledge: $knowledgeId" -ForegroundColor DarkGreen
                } else {
                    Write-Host "    ! Knowledge: $knowledgeId failed" -ForegroundColor Red
                    $associationErrors++
                }
            } else {
                Write-Host "    ! Knowledge: $knowledgeId not found" -ForegroundColor Red
                $associationErrors++
            }
        }

        # Associate Tools
        foreach ($toolId in $Playbook.scopes.tools) {
            $targetGuid = $toolNameToId[$toolId]
            if ($targetGuid) {
                $success = Add-Association -PlaybookId $playbookId -RelationshipName "sprk_playbook_tool" -TargetEntitySet "sprk_analysistools" -TargetId $targetGuid
                if ($success) {
                    Write-Host "    + Tool: $toolId" -ForegroundColor DarkGreen
                } else {
                    Write-Host "    ! Tool: $toolId failed" -ForegroundColor Red
                    $associationErrors++
                }
            } else {
                Write-Host "    ! Tool: $toolId not found" -ForegroundColor Red
                $associationErrors++
            }
        }

        if ($associationErrors -gt 0) {
            return @{ Status = "InsertedWithWarnings"; Name = $name; Id = $playbookId; Warnings = $associationErrors }
        }

        return @{ Status = "Inserted"; Name = $name; Id = $playbookId }

    } catch {
        $errorMsg = $_.Exception.Message
        Write-Host "  ERROR: '$name' - $errorMsg" -ForegroundColor Red
        return @{ Status = "Error"; Name = $name; Error = $errorMsg }
    }
}

# Process playbooks
$summary = @{
    Inserted = 0
    InsertedWithWarnings = 0
    Skipped = 0
    Errors = 0
    DryRun = 0
}

Write-Host "Processing $($data.playbooks.Count) Playbook records..." -ForegroundColor Cyan
Write-Host ""

foreach ($playbook in $data.playbooks) {
    $result = New-PlaybookRecord -Playbook $playbook
    $summary[$result.Status]++
    Write-Host ""
}

# Summary
Write-Host "=== Summary ===" -ForegroundColor Cyan
Write-Host "Inserted:              $($summary.Inserted)" -ForegroundColor Green
Write-Host "Inserted with warnings: $($summary.InsertedWithWarnings)" -ForegroundColor Yellow
Write-Host "Skipped:               $($summary.Skipped)" -ForegroundColor Yellow
Write-Host "Errors:                $($summary.Errors)" -ForegroundColor Red
if ($DryRun) {
    Write-Host "DryRun:                $($summary.DryRun)" -ForegroundColor Gray
}

if ($summary.Errors -gt 0) {
    exit 1
}

Write-Host ""
Write-Host "Done!" -ForegroundColor Green
