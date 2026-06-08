# Check-SprkEventFormViewDeps.ps1
# Scans sprk_event forms (systemforms) and views (savedqueries) for references to the four to-do fields.
$ErrorActionPreference = "Stop"
$Environment = "spaarkedev1.crm.dynamics.com"
$BaseUrl = "https://$Environment/api/data/v9.2"
$token = (az account get-access-token --resource "https://$Environment" --query accessToken -o tsv)
$headers = @{ "Authorization" = "Bearer $token"; "OData-MaxVersion" = "4.0"; "OData-Version" = "4.0"; "Accept" = "application/json" }

$fields = @("sprk_todoflag", "sprk_todostatus", "sprk_todocolumn", "sprk_todopinned")

# 1. Saved queries (views) on sprk_event
Write-Host "`n-- Saved queries (views) on sprk_event --" -ForegroundColor Yellow
$svUrl = "$BaseUrl/savedqueries?`$filter=returnedtypecode eq 'sprk_event'&`$select=savedqueryid,name,fetchxml,layoutxml,querytype,statecode"
$svs = (Invoke-RestMethod -Uri $svUrl -Headers $headers).value
Write-Host ("  Total views on sprk_event: {0}" -f $svs.Count) -ForegroundColor Gray
$hitCount = 0
foreach ($sv in $svs) {
    $combined = ($sv.fetchxml + " " + $sv.layoutxml)
    $hits = $fields | Where-Object { $combined -match $_ }
    if ($hits) {
        $hitCount++
        Write-Host ("  [HIT] {0}  (id={1}, state={2}, querytype={3}) refs: {4}" -f $sv.name, $sv.savedqueryid, $sv.statecode, $sv.querytype, ($hits -join ",")) -ForegroundColor Yellow
    }
}
if ($hitCount -eq 0) { Write-Host "  (no view references to the four fields)" -ForegroundColor Green }

# 2. System forms on sprk_event
Write-Host "`n-- System forms (systemform) on sprk_event --" -ForegroundColor Yellow
$sfUrl = "$BaseUrl/systemforms?`$filter=objecttypecode eq 'sprk_event'&`$select=formid,name,type,formxml,formactivationstate"
$sfs = (Invoke-RestMethod -Uri $sfUrl -Headers $headers).value
Write-Host ("  Total forms on sprk_event: {0}" -f $sfs.Count) -ForegroundColor Gray
$hitCount = 0
foreach ($sf in $sfs) {
    $hits = $fields | Where-Object { $sf.formxml -match $_ }
    if ($hits) {
        $hitCount++
        Write-Host ("  [HIT] {0}  (id={1}, type={2}, active={3}) refs: {4}" -f $sf.name, $sf.formid, $sf.type, $sf.formactivationstate, ($hits -join ",")) -ForegroundColor Yellow
    }
}
if ($hitCount -eq 0) { Write-Host "  (no form references to the four fields)" -ForegroundColor Green }

# 3. User queries (personal views)
Write-Host "`n-- User queries (userquery) on sprk_event --" -ForegroundColor Yellow
$uqUrl = "$BaseUrl/userqueries?`$filter=returnedtypecode eq 'sprk_event'&`$select=userqueryid,name,fetchxml,layoutxml"
$uqs = (Invoke-RestMethod -Uri $uqUrl -Headers $headers).value
Write-Host ("  Total user queries on sprk_event: {0}" -f $uqs.Count) -ForegroundColor Gray
$hitCount = 0
foreach ($uq in $uqs) {
    $combined = ($uq.fetchxml + " " + $uq.layoutxml)
    $hits = $fields | Where-Object { $combined -match $_ }
    if ($hits) {
        $hitCount++
        Write-Host ("  [HIT] {0}  (id={1}) refs: {2}" -f $uq.name, $uq.userqueryid, ($hits -join ",")) -ForegroundColor Yellow
    }
}
if ($hitCount -eq 0) { Write-Host "  (no user query references to the four fields)" -ForegroundColor Green }

# 4. Charts (savedqueryvisualizations)
Write-Host "`n-- Chart visualizations on sprk_event --" -ForegroundColor Yellow
$chUrl = "$BaseUrl/savedqueryvisualizations?`$filter=primaryentitytypecode eq 'sprk_event'&`$select=savedqueryvisualizationid,name,datadescription,presentationdescription"
$chs = (Invoke-RestMethod -Uri $chUrl -Headers $headers).value
Write-Host ("  Total charts on sprk_event: {0}" -f $chs.Count) -ForegroundColor Gray
$hitCount = 0
foreach ($ch in $chs) {
    $combined = ($ch.datadescription + " " + $ch.presentationdescription)
    $hits = $fields | Where-Object { $combined -match $_ }
    if ($hits) {
        $hitCount++
        Write-Host ("  [HIT] {0}  (id={1}) refs: {2}" -f $ch.name, $ch.savedqueryvisualizationid, ($hits -join ",")) -ForegroundColor Yellow
    }
}
if ($hitCount -eq 0) { Write-Host "  (no chart references to the four fields)" -ForegroundColor Green }

Write-Host "`n-- Done --" -ForegroundColor Cyan
