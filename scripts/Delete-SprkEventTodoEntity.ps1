# Delete the sprk_eventtodo entity end-to-end from spaarkedev1.
#
# Order (relationship-before-entity, dependencies-before-relationship):
#   1. Delete aiskillconfig FormFillFieldOptOut blockers (7 records)
#   2. Delete appmodulecomponents linking sprk_eventtodo to model-driven apps (26 records)
#   3. Delete saved queries scoped to sprk_eventtodo (7 records)
#   4. Delete the two user-defined ManyToOne relationships
#         (sprk_eventtodo_RegardingEvent_n1, sprk_eventtodo_AssignedTo_n1)
#   5. Delete the entity itself via DELETE EntityDefinitions(LogicalName='sprk_eventtodo')
#         (cascades the eight system OneToMany rels + the entity icon binding)
#
# Per smart-todo-decoupling-r3 task 005 (spec FR-02 / OS-1).
# Reusable pattern - documented in projects/smart-todo-decoupling-r3/notes/task-005-schema-cut.md.

$ErrorActionPreference = "Stop"
$Environment = "spaarkedev1.crm.dynamics.com"
$BaseUrl = "https://$Environment/api/data/v9.2"
$token = (az account get-access-token --resource "https://$Environment" --query accessToken -o tsv)
$headers = @{
    "Authorization"    = "Bearer $token"
    "OData-MaxVersion" = "4.0"
    "OData-Version"    = "4.0"
    "Content-Type"     = "application/json"
    "Accept"           = "application/json"
}

$entityLogical = "sprk_eventtodo"
$entityMetaId  = "08219a5e-a40c-f111-8341-6045bded546b"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  task 005 - delete sprk_eventtodo entity" -ForegroundColor Cyan
Write-Host "  Environment: $Environment" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

# -----------------------------------------------------------------------------
# Step 1 - aiskillconfig FormFillFieldOptOut blockers
# -----------------------------------------------------------------------------
Write-Host "`n[1/5] Delete aiskillconfig FormFillFieldOptOut blockers..." -ForegroundColor Yellow
$aiSkillIds = @(
    "62062bac-c60d-f111-8342-7ced8d1dc1f8",
    "e2d21d81-2b13-f111-8343-7ced8d1dc988",
    "70213e7c-c60d-f111-8342-7c1e520aa4df",
    "a4fa9b6f-c60d-f111-8342-7c1e520aa4df",
    "39062bac-c60d-f111-8342-7ced8d1dc1f8",
    "30b25339-1714-f111-8343-7ced8d1dc988",
    "4b2c6de4-5713-f111-8343-7ced8d1dc988"
)
foreach ($id in $aiSkillIds) {
    try {
        Invoke-RestMethod -Uri "$BaseUrl/aiskillconfigs($id)" -Headers $headers -Method Delete -ErrorAction Stop | Out-Null
        Write-Host ("  [OK]  aiskillconfig {0}" -f $id) -ForegroundColor Green
    } catch {
        if ($_.Exception.Response.StatusCode.value__ -eq 404) {
            Write-Host ("  [SKIP] aiskillconfig {0} already gone" -f $id) -ForegroundColor DarkGray
        } else {
            Write-Host ("  [FAIL] aiskillconfig {0}: {1}" -f $id, $_.Exception.Message) -ForegroundColor Red
            try { Write-Host ("       body: $($_.ErrorDetails.Message)") -ForegroundColor DarkGray } catch {}
        }
    }
}

# -----------------------------------------------------------------------------
# Step 2 - appmodulecomponents (model-driven app entity registrations)
# -----------------------------------------------------------------------------
Write-Host "`n[2/5] Delete appmodulecomponents for sprk_eventtodo..." -ForegroundColor Yellow
$amcQuery = "$BaseUrl/appmodulecomponents?`$filter=objectid eq $entityMetaId and componenttype eq 1&`$select=appmodulecomponentid"
$amcs = Invoke-RestMethod -Uri $amcQuery -Headers $headers -Method Get
Write-Host ("  Found {0} appmodulecomponent(s)" -f $amcs.value.Count) -ForegroundColor Cyan
foreach ($c in $amcs.value) {
    try {
        Invoke-RestMethod -Uri "$BaseUrl/appmodulecomponents($($c.appmodulecomponentid))" -Headers $headers -Method Delete -ErrorAction Stop | Out-Null
        Write-Host ("  [OK]  appmodulecomponent {0}" -f $c.appmodulecomponentid) -ForegroundColor Green
    } catch {
        if ($_.Exception.Response.StatusCode.value__ -eq 404) {
            Write-Host ("  [SKIP] appmodulecomponent {0} already gone" -f $c.appmodulecomponentid) -ForegroundColor DarkGray
        } else {
            Write-Host ("  [FAIL] appmodulecomponent {0}: {1}" -f $c.appmodulecomponentid, $_.Exception.Message) -ForegroundColor Red
            try { Write-Host ("       body: $($_.ErrorDetails.Message)") -ForegroundColor DarkGray } catch {}
        }
    }
}

# -----------------------------------------------------------------------------
# Step 3 - saved queries (system views) scoped to sprk_eventtodo
# -----------------------------------------------------------------------------
Write-Host "`n[3/5] Delete saved queries for sprk_eventtodo..." -ForegroundColor Yellow
$sqUrl = "$BaseUrl/savedqueries?`$filter=returnedtypecode eq '$entityLogical'&`$select=savedqueryid,name"
$sqs = Invoke-RestMethod -Uri $sqUrl -Headers $headers -Method Get
Write-Host ("  Found {0} saved queries" -f $sqs.value.Count) -ForegroundColor Cyan
foreach ($q in $sqs.value) {
    try {
        Invoke-RestMethod -Uri "$BaseUrl/savedqueries($($q.savedqueryid))" -Headers $headers -Method Delete -ErrorAction Stop | Out-Null
        Write-Host ("  [OK]  savedquery {0}  '{1}'" -f $q.savedqueryid, $q.name) -ForegroundColor Green
    } catch {
        if ($_.Exception.Response.StatusCode.value__ -eq 404) {
            Write-Host ("  [SKIP] savedquery {0} already gone" -f $q.savedqueryid) -ForegroundColor DarkGray
        } else {
            Write-Host ("  [FAIL] savedquery {0}: {1}" -f $q.savedqueryid, $_.Exception.Message) -ForegroundColor Red
            try { Write-Host ("       body: $($_.ErrorDetails.Message)") -ForegroundColor DarkGray } catch {}
        }
    }
}

# -----------------------------------------------------------------------------
# Step 4 - user-defined ManyToOne relationships
# -----------------------------------------------------------------------------
Write-Host "`n[4/5] Delete user-defined relationships..." -ForegroundColor Yellow
$relations = @("sprk_eventtodo_RegardingEvent_n1", "sprk_eventtodo_AssignedTo_n1")
foreach ($rel in $relations) {
    try {
        Invoke-RestMethod -Uri "$BaseUrl/RelationshipDefinitions(SchemaName='$rel')" -Headers $headers -Method Delete -ErrorAction Stop | Out-Null
        Write-Host ("  [OK]  relationship {0}" -f $rel) -ForegroundColor Green
    } catch {
        if ($_.Exception.Response.StatusCode.value__ -eq 404) {
            Write-Host ("  [SKIP] relationship {0} already gone" -f $rel) -ForegroundColor DarkGray
        } else {
            Write-Host ("  [FAIL] relationship {0}: {1}" -f $rel, $_.Exception.Message) -ForegroundColor Red
            try { Write-Host ("       body: $($_.ErrorDetails.Message)") -ForegroundColor DarkGray } catch {}
        }
    }
}

# -----------------------------------------------------------------------------
# Step 5 - entity itself
# -----------------------------------------------------------------------------
Write-Host "`n[5/5] Delete entity definition for sprk_eventtodo..." -ForegroundColor Yellow
try {
    Invoke-RestMethod -Uri "$BaseUrl/EntityDefinitions(LogicalName='$entityLogical')" -Headers $headers -Method Delete -ErrorAction Stop | Out-Null
    Write-Host ("  [OK]  entity sprk_eventtodo deleted") -ForegroundColor Green
} catch {
    if ($_.Exception.Response.StatusCode.value__ -eq 404) {
        Write-Host ("  [SKIP] entity already gone") -ForegroundColor DarkGray
    } else {
        Write-Host ("  [FAIL] entity delete: {0}" -f $_.Exception.Message) -ForegroundColor Red
        try { Write-Host ("       body: $($_.ErrorDetails.Message)") -ForegroundColor DarkGray } catch {}
        exit 1
    }
}

# -----------------------------------------------------------------------------
# Verification
# -----------------------------------------------------------------------------
Write-Host "`n[verify] Confirm entity is gone..." -ForegroundColor Yellow
try {
    $r = Invoke-RestMethod -Uri "$BaseUrl/EntityDefinitions(LogicalName='$entityLogical')?`$select=LogicalName" -Headers $headers -Method Get
    Write-Host ("  [FAIL] entity is STILL PRESENT: {0}" -f $r.LogicalName) -ForegroundColor Red
} catch {
    if ($_.Exception.Response.StatusCode.value__ -eq 404) {
        Write-Host ("  [OK]  entity sprk_eventtodo confirmed absent (404 on EntityDefinitions)") -ForegroundColor Green
    } else {
        Write-Host ("  [UNK] verification error: {0}" -f $_.Exception.Message) -ForegroundColor DarkYellow
    }
}

Write-Host "`n==================================================" -ForegroundColor Cyan
Write-Host "  Complete." -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
