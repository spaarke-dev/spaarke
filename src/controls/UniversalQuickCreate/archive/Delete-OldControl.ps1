# Delete Old cc_ Control Registration
# This script finds and deletes the old cc_Spaarke.Controls.UniversalDocumentUpload control

Write-Host "Querying for cc_ custom control..." -ForegroundColor Cyan

# Query for the custom control with cc_ prefix
$query = @"
<fetch>
  <entity name='customcontrol'>
    <attribute name='customcontrolid' />
    <attribute name='name' />
    <attribute name='version' />
    <filter>
      <condition attribute='name' operator='like' value='%cc_%UniversalDocument%' />
    </filter>
  </entity>
</fetch>
"@

Write-Host "Executing FetchXML query..." -ForegroundColor Yellow
$result = pac data query --query $query 2>&1

if ($result -match "customcontrolid") {
    Write-Host "Found cc_ control(s). Here are the results:" -ForegroundColor Green
    Write-Host $result -ForegroundColor White

    Write-Host "`nTo delete these controls, you need to:" -ForegroundColor Yellow
    Write-Host "1. Go to Power Apps maker portal (make.powerapps.com)" -ForegroundColor White
    Write-Host "2. Go to Solutions -> Default Solution" -ForegroundColor White
    Write-Host "3. Search for 'cc_Spaarke.Controls.UniversalDocumentUpload'" -ForegroundColor White
    Write-Host "4. Delete the custom control AND all its web resources (bundle.js, CSS)" -ForegroundColor White
    Write-Host "5. Publish all customizations" -ForegroundColor White
} else {
    Write-Host "No cc_ controls found (or query failed)" -ForegroundColor Red
    Write-Host $result -ForegroundColor Gray
}
