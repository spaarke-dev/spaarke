# Finance Intelligence Module R1 - Dataverse Views Deployment
# Creates two saved views:
#   1. Invoice Review Queue (on sprk_document)
#   2. Active Invoices (on sprk_invoice)
#
# Prerequisites: Finance schema must be deployed first (Create-FinanceSchema.ps1)
# This script is idempotent - re-running will skip existing views.

param(
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com"
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host " Finance Intelligence Module R1 - Views Deployment" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl" -ForegroundColor Gray
Write-Host ""

# ===================================================================
# AUTHENTICATION
# ===================================================================

Write-Host "Authenticating via Azure CLI..." -ForegroundColor Yellow
$token = az account get-access-token --resource "$EnvironmentUrl" --query accessToken -o tsv
if (-not $token) {
    throw "Failed to get access token. Run 'az login' first."
}
Write-Host "  Authenticated." -ForegroundColor Green

$headers = @{
    "Authorization"    = "Bearer $token"
    "Content-Type"     = "application/json"
    "Accept"           = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version"    = "4.0"
}

$apiUrl = "$EnvironmentUrl/api/data/v9.2"

# ===================================================================
# HELPER FUNCTIONS
# ===================================================================

function Invoke-DataverseApi {
    param(
        [string]$Endpoint,
        [string]$Method = "GET",
        [object]$Body = $null
    )
    $uri = "$apiUrl/$Endpoint"
    $params = @{
        Uri     = $uri
        Headers = $headers
        Method  = $Method
    }
    if ($Body) {
        $params.Body = ($Body | ConvertTo-Json -Depth 20 -Compress)
    }
    try {
        $response = Invoke-RestMethod @params
        return @{ Success = $true; Data = $response }
    }
    catch {
        $errorMessage = $_.Exception.Message
        try {
            $errorDetails = $_.ErrorDetails.Message | ConvertFrom-Json
            $errorMessage = $errorDetails.error.message
        } catch {}
        return @{ Success = $false; Error = $errorMessage }
    }
}

function Test-SavedQueryExists {
    param(
        [string]$Name,
        [string]$EntityLogicalName
    )
    $filter = "name eq '$Name' and returnedtypecode eq '$EntityLogicalName'"
    $encodedFilter = [System.Uri]::EscapeDataString($filter)
    $result = Invoke-DataverseApi -Endpoint "savedqueries?`$filter=$encodedFilter&`$select=savedqueryid,name"
    if ($result.Success -and $result.Data.value -and $result.Data.value.Count -gt 0) {
        return $result.Data.value[0].savedqueryid
    }
    return $null
}

# ===================================================================
# VIEW 1: Invoice Review Queue (sprk_document)
# ===================================================================

Write-Host ""
Write-Host "View 1: Invoice Review Queue (sprk_document)" -ForegroundColor Cyan
Write-Host "--------------------------------------------------------" -ForegroundColor Gray

$view1Name = "Invoice Review Queue"
$view1Entity = "sprk_document"

$existingView1 = Test-SavedQueryExists -Name $view1Name -EntityLogicalName $view1Entity
if ($existingView1) {
    Write-Host '  -- Exists: Invoice Review Queue (skipping)' -ForegroundColor Yellow
} else {
    $view1FetchXml = @"
<fetch version='1.0' mapping='logical'>
  <entity name='sprk_document'>
    <attribute name='sprk_documentname' />
    <attribute name='sprk_classification' />
    <attribute name='sprk_classificationconfidence' />
    <attribute name='sprk_invoicevendornamehint' />
    <attribute name='sprk_invoicenumberhint' />
    <attribute name='sprk_invoicetotalhint' />
    <attribute name='sprk_invoicecurrencyhint' />
    <attribute name='sprk_mattersuggestedref' />
    <attribute name='sprk_invoicereviewstatus' />
    <filter type='and'>
      <condition attribute='statecode' operator='eq' value='0' />
      <condition attribute='sprk_invoicereviewstatus' operator='eq' value='100000000' />
      <filter type='or'>
        <condition attribute='sprk_classification' operator='eq' value='100000000' />
        <condition attribute='sprk_classification' operator='eq' value='100000002' />
      </filter>
    </filter>
    <order attribute='sprk_classificationconfidence' descending='true' />
  </entity>
</fetch>
"@

    $view1LayoutXml = @"
<grid name='resultset' object='1' jump='sprk_documentname' select='1' icon='1' preview='1'>
  <row name='result' id='sprk_documentid'>
    <cell name='sprk_documentname' width='200' />
    <cell name='sprk_classification' width='125' />
    <cell name='sprk_classificationconfidence' width='100' />
    <cell name='sprk_invoicevendornamehint' width='175' />
    <cell name='sprk_invoicenumberhint' width='125' />
    <cell name='sprk_invoicetotalhint' width='100' />
    <cell name='sprk_invoicecurrencyhint' width='75' />
    <cell name='sprk_mattersuggestedref' width='175' />
    <cell name='sprk_invoicereviewstatus' width='125' />
  </row>
</grid>
"@

    $view1Body = @{
        "name"              = $view1Name
        "description"       = "Unreviewed attachment candidates for invoice classification review"
        "returnedtypecode"  = $view1Entity
        "fetchxml"          = $view1FetchXml
        "layoutxml"         = $view1LayoutXml
        "querytype"         = 0
        "isdefault"         = $false
    }

    $result = Invoke-DataverseApi -Endpoint "savedqueries" -Method "POST" -Body $view1Body
    if ($result.Success) {
        Write-Host '  ++ Created: Invoice Review Queue' -ForegroundColor Green
    } else {
        Write-Host "  ** Failed: Invoice Review Queue - $($result.Error)" -ForegroundColor Red
    }
}

# ===================================================================
# VIEW 2: Active Invoices (sprk_invoice)
# ===================================================================

Write-Host ""
Write-Host "View 2: Active Invoices (sprk_invoice)" -ForegroundColor Cyan
Write-Host "--------------------------------------------------------" -ForegroundColor Gray

$view2Name = "Active Invoices"
$view2Entity = "sprk_invoice"

$existingView2 = Test-SavedQueryExists -Name $view2Name -EntityLogicalName $view2Entity
if ($existingView2) {
    Write-Host '  -- Exists: Active Invoices (skipping)' -ForegroundColor Yellow
} else {
    $view2FetchXml = @"
<fetch version='1.0' mapping='logical'>
  <entity name='sprk_invoice'>
    <attribute name='sprk_name' />
    <attribute name='sprk_invoicenumber' />
    <attribute name='sprk_vendororg' />
    <attribute name='sprk_invoicedate' />
    <attribute name='sprk_totalamount' />
    <attribute name='sprk_currency' />
    <attribute name='sprk_invoicestatus' />
    <attribute name='sprk_extractionstatus' />
    <filter type='and'>
      <condition attribute='statecode' operator='eq' value='0' />
    </filter>
    <order attribute='sprk_invoicedate' descending='true' />
  </entity>
</fetch>
"@

    $view2LayoutXml = @"
<grid name='resultset' object='1' jump='sprk_name' select='1' icon='1' preview='1'>
  <row name='result' id='sprk_invoiceid'>
    <cell name='sprk_name' width='200' />
    <cell name='sprk_invoicenumber' width='125' />
    <cell name='sprk_vendororg' width='175' />
    <cell name='sprk_invoicedate' width='100' />
    <cell name='sprk_totalamount' width='100' />
    <cell name='sprk_currency' width='75' />
    <cell name='sprk_invoicestatus' width='125' />
    <cell name='sprk_extractionstatus' width='125' />
  </row>
</grid>
"@

    $view2Body = @{
        "name"              = $view2Name
        "description"       = "All active invoice records"
        "returnedtypecode"  = $view2Entity
        "fetchxml"          = $view2FetchXml
        "layoutxml"         = $view2LayoutXml
        "querytype"         = 0
        "isdefault"         = $false
    }

    $result = Invoke-DataverseApi -Endpoint "savedqueries" -Method "POST" -Body $view2Body
    if ($result.Success) {
        Write-Host '  ++ Created: Active Invoices' -ForegroundColor Green
    } else {
        Write-Host "  ** Failed: Active Invoices - $($result.Error)" -ForegroundColor Red
    }
}

# ===================================================================
# PUBLISH CUSTOMIZATIONS
# ===================================================================

Write-Host ""
Write-Host "Publishing Customizations" -ForegroundColor Cyan
Write-Host "--------------------------------------------------------" -ForegroundColor Gray

$entities = @("sprk_document", "sprk_invoice")
$entityXml = ($entities | ForEach-Object { "<entity>$_</entity>" }) -join ""
$publishRequest = @{
    "ParameterXml" = "<importexportxml><entities>$entityXml</entities></importexportxml>"
}

$result = Invoke-DataverseApi -Endpoint "PublishXml" -Method "POST" -Body $publishRequest
if ($result.Success) {
    Write-Host "  Published customizations for sprk_document and sprk_invoice." -ForegroundColor Green
} else {
    Write-Host "  ** Publish failed: $($result.Error)" -ForegroundColor Red
}

# ===================================================================
# SUMMARY
# ===================================================================

Write-Host ""
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host " Views Deployment Complete" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Views Created:" -ForegroundColor White
Write-Host '    1. Invoice Review Queue (sprk_document) - Unreviewed candidates sorted by confidence' -ForegroundColor Gray
Write-Host '    2. Active Invoices (sprk_invoice) - All active invoices sorted by date' -ForegroundColor Gray
Write-Host ""
