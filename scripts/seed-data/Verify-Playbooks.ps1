# Verify-Playbooks.ps1
# Verify Playbook records and N:N relationships for AI Document Intelligence R4
#
# Usage:
#   .\Verify-Playbooks.ps1

param(
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Verify Playbooks ===" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl"
Write-Host ""

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
}

# Expected playbooks
$expectedPlaybooks = @(
    @{ Name = "Quick Document Review"; Id = "PB-001" },
    @{ Name = "Full Contract Analysis"; Id = "PB-002" },
    @{ Name = "Risk Scan"; Id = "PB-010" }
)

# Query all playbooks
$uri = "$EnvironmentUrl/api/data/v9.2/sprk_analysisplaybooks?`$select=sprk_analysisplaybookid,sprk_name,sprk_description,sprk_ispublic"

try {
    $result = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get
    Write-Host "Found $($result.value.Count) playbook(s) in Dataverse" -ForegroundColor Cyan
    Write-Host ""

    $foundCount = 0
    $missingCount = 0

    foreach ($expected in $expectedPlaybooks) {
        $playbook = $result.value | Where-Object { $_.sprk_name -eq $expected.Name }

        if ($playbook) {
            $foundCount++
            Write-Host "FOUND: $($expected.Id) - $($expected.Name)" -ForegroundColor Green
            Write-Host "  ID: $($playbook.sprk_analysisplaybookid)"
            Write-Host "  Public: $($playbook.sprk_ispublic)"
            Write-Host "  Description: $($playbook.sprk_description.Substring(0, [Math]::Min(80, $playbook.sprk_description.Length)))..."

            # Query N:N relationships
            $playbookId = $playbook.sprk_analysisplaybookid

            # Skills
            $skillsUri = "$EnvironmentUrl/api/data/v9.2/sprk_analysisplaybooks($playbookId)/sprk_playbook_skill?`$select=sprk_name"
            try {
                $skills = Invoke-RestMethod -Uri $skillsUri -Headers $headers -Method Get
                Write-Host "  Skills: $($skills.value.Count) linked" -ForegroundColor DarkCyan
                foreach ($skill in $skills.value) {
                    Write-Host "    - $($skill.sprk_name)" -ForegroundColor DarkGray
                }
            } catch {
                Write-Host "  Skills: Unable to query" -ForegroundColor Yellow
            }

            # Actions
            $actionsUri = "$EnvironmentUrl/api/data/v9.2/sprk_analysisplaybooks($playbookId)/sprk_analysisplaybook_action?`$select=sprk_name"
            try {
                $actions = Invoke-RestMethod -Uri $actionsUri -Headers $headers -Method Get
                Write-Host "  Actions: $($actions.value.Count) linked" -ForegroundColor DarkCyan
                foreach ($action in $actions.value) {
                    Write-Host "    - $($action.sprk_name)" -ForegroundColor DarkGray
                }
            } catch {
                Write-Host "  Actions: Unable to query" -ForegroundColor Yellow
            }

            # Knowledge
            $knowledgeUri = "$EnvironmentUrl/api/data/v9.2/sprk_analysisplaybooks($playbookId)/sprk_playbook_knowledge?`$select=sprk_name"
            try {
                $knowledge = Invoke-RestMethod -Uri $knowledgeUri -Headers $headers -Method Get
                Write-Host "  Knowledge: $($knowledge.value.Count) linked" -ForegroundColor DarkCyan
                foreach ($k in $knowledge.value) {
                    Write-Host "    - $($k.sprk_name)" -ForegroundColor DarkGray
                }
            } catch {
                Write-Host "  Knowledge: Unable to query" -ForegroundColor Yellow
            }

            # Tools
            $toolsUri = "$EnvironmentUrl/api/data/v9.2/sprk_analysisplaybooks($playbookId)/sprk_playbook_tool?`$select=sprk_name"
            try {
                $tools = Invoke-RestMethod -Uri $toolsUri -Headers $headers -Method Get
                Write-Host "  Tools: $($tools.value.Count) linked" -ForegroundColor DarkCyan
                foreach ($tool in $tools.value) {
                    Write-Host "    - $($tool.sprk_name)" -ForegroundColor DarkGray
                }
            } catch {
                Write-Host "  Tools: Unable to query" -ForegroundColor Yellow
            }

            Write-Host ""

        } else {
            $missingCount++
            Write-Host "MISSING: $($expected.Id) - $($expected.Name)" -ForegroundColor Red
            Write-Host ""
        }
    }

    # Summary
    Write-Host "=== Summary ===" -ForegroundColor Cyan
    Write-Host "Expected: $($expectedPlaybooks.Count)"
    Write-Host "Found:    $foundCount" -ForegroundColor Green
    Write-Host "Missing:  $missingCount" -ForegroundColor $(if ($missingCount -gt 0) { "Red" } else { "Green" })

    # Additional playbooks (not in expected list)
    $additionalPlaybooks = $result.value | Where-Object { $_.sprk_name -notin $expectedPlaybooks.Name }
    if ($additionalPlaybooks.Count -gt 0) {
        Write-Host ""
        Write-Host "Additional playbooks found:" -ForegroundColor Yellow
        foreach ($p in $additionalPlaybooks) {
            Write-Host "  - $($p.sprk_name)" -ForegroundColor DarkGray
        }
    }

    if ($missingCount -gt 0) {
        Write-Host ""
        Write-Host "Run .\Deploy-Playbooks.ps1 to create missing playbooks" -ForegroundColor Yellow
        exit 1
    }

} catch {
    Write-Host "ERROR: Failed to query playbooks" -ForegroundColor Red
    Write-Host $_.Exception.Message
    exit 1
}

Write-Host ""
Write-Host "Verification complete!" -ForegroundColor Green
