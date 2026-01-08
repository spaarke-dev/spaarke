$token = az account get-access-token --resource "https://spaarkedev1.crm.dynamics.com" --query "accessToken" -o tsv

$headers = @{
    'Authorization' = "Bearer $token"
    'Accept' = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version' = '4.0'
}

$recordId = "dbf58906-12ec-f011-8406-7c1e520aa4df"
$baseUrl = "https://spaarkedev1.crm.dynamics.com/api/data/v9.2"

Write-Host "Checking what entity this record belongs to: $recordId" -ForegroundColor Cyan
Write-Host ""

# Try each entity type
$entitiesToCheck = @(
    @{Name="Analysis Playbook"; Entity="sprk_analysisplaybooks"; IdField="sprk_analysisplaybookid"},
    @{Name="Analysis Action"; Entity="sprk_analysisactions"; IdField="sprk_analysisactionid"},
    @{Name="Analysis Skill"; Entity="sprk_analysisskills"; IdField="sprk_analysisskillid"},
    @{Name="Analysis Knowledge"; Entity="sprk_analysisknowledges"; IdField="sprk_analysisknowledgeid"},
    @{Name="Analysis Tool"; Entity="sprk_analysistools"; IdField="sprk_analysistoolid"},
    @{Name="AI Output Type"; Entity="sprk_aioutputtypes"; IdField="sprk_aioutputtypeid"},
    @{Name="Document"; Entity="sprk_documents"; IdField="sprk_documentid"}
)

foreach ($entityInfo in $entitiesToCheck) {
    try {
        $url = "$baseUrl/$($entityInfo.Entity)($recordId)"
        $response = Invoke-RestMethod -Uri $url -Headers $headers -Method Get -ErrorAction Stop
        
        Write-Host "✅ FOUND in $($entityInfo.Name)" -ForegroundColor Green
        Write-Host "Entity: $($entityInfo.Entity)" -ForegroundColor Yellow
        
        # Show key fields
        if ($response.sprk_name) {
            Write-Host "Name: $($response.sprk_name)" -ForegroundColor Yellow
        }
        if ($response._ownerid_value) {
            Write-Host "Owner ID: $($response._ownerid_value)" -ForegroundColor Yellow
        }
        
        $response | ConvertTo-Json -Depth 3
        exit 0
    }
    catch {
        # Not this entity, continue
    }
}

Write-Host "❌ Record not found in any known entity" -ForegroundColor Red
