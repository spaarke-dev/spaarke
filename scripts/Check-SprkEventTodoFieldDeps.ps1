# Check-SprkEventTodoFieldDeps.ps1
# Checks Dataverse dependencies on the four `sprk_event` to-do fields slated for removal:
# sprk_todoflag, sprk_todostatus, sprk_todocolumn, sprk_todopinned
# Part of smart-todo-decoupling-r3 / task 004 (pre-cut dependency check).

$ErrorActionPreference = "Stop"
$Environment = "spaarkedev1.crm.dynamics.com"
$BaseUrl = "https://$Environment/api/data/v9.2"

$token = (az account get-access-token --resource "https://$Environment" --query accessToken -o tsv)
if (-not $token) { throw "Failed to obtain Dataverse access token." }

$headers = @{
    "Authorization" = "Bearer $token"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
    "Content-Type" = "application/json"
    "Accept" = "application/json"
}

$fields = @("sprk_todoflag", "sprk_todostatus", "sprk_todocolumn", "sprk_todopinned")

Write-Host "=== Dependency check for sprk_event to-do fields ===" -ForegroundColor Cyan

# 1. Get metadata IDs for each attribute on sprk_event
Write-Host "`n-- Fetching attribute metadata IDs --" -ForegroundColor Yellow
$attrIds = @{}
foreach ($f in $fields) {
    $url = "$BaseUrl/EntityDefinitions(LogicalName='sprk_event')/Attributes(LogicalName='$f')?`$select=MetadataId,LogicalName,AttributeType,SchemaName"
    try {
        $r = Invoke-RestMethod -Uri $url -Headers $headers -Method Get
        $attrIds[$f] = $r.MetadataId
        Write-Host ("  [OK] {0}  MetadataId={1}  Type={2}  SchemaName={3}" -f $f, $r.MetadataId, $r.AttributeType, $r.SchemaName) -ForegroundColor Green
    } catch {
        Write-Host ("  [MISSING] {0} not found on sprk_event: {1}" -f $f, $_.Exception.Message) -ForegroundColor Red
    }
}

# 2. Retrieve dependencies for each attribute via RetrieveDependenciesForDelete
# Web API v9.2 function syntax requires GUID literal without quotes; ComponentType=2 = Attribute
Write-Host "`n-- Dependency scan (RetrieveDependenciesForDelete) --" -ForegroundColor Yellow
foreach ($f in $fields) {
    if (-not $attrIds.ContainsKey($f)) { continue }
    $componentId = $attrIds[$f]
    $url = "$BaseUrl/RetrieveDependenciesForDelete(ObjectId=$componentId,ComponentType=2)"
    try {
        $r = Invoke-RestMethod -Uri $url -Headers $headers -Method Get
        $count = if ($null -eq $r.value) { 0 } else { @($r.value).Count }
        if ($count -eq 0) {
            Write-Host ("  [{0}] 0 dependencies" -f $f) -ForegroundColor Green
        } else {
            Write-Host ("  [{0}] {1} dependencies:" -f $f, $count) -ForegroundColor Yellow
            foreach ($dep in $r.value) {
                Write-Host ("      DepType={0}  DependentComponentType={1}  DependentComponentId={2}  DependentComponentObjectId={3}" -f $dep.dependencytype, $dep.dependentcomponenttype, $dep.dependentcomponentid, $dep.dependentcomponentobjectid) -ForegroundColor Gray
            }
        }
    } catch {
        Write-Host ("  [{0}] ERROR retrieving dependencies: {1}" -f $f, $_.Exception.Message) -ForegroundColor Red
    }
}

Write-Host "`n=== Done ===" -ForegroundColor Cyan
