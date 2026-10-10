# Deletes the 2 test SPE containers left by the task 039 live gate (spaarkedev1, 2026-10-10): the provisioned zz-039 Secure
# matter and Secure work assignment, both already deleted in Dataverse. Same recipe as uac-r2's notes/Remove-TestContainers.ps1.
# Run as a SharePoint administrator (Connect-SPOService is interactive). Deleted containers go to the deleted-container
# collection (restorable ~93 days). Run with -WhatIfOnly first to list them.
# Requires: Install-Module Microsoft.Online.SharePoint.PowerShell (Windows PowerShell 5.1, or pwsh with -UseWindowsPowerShell).
param([switch]$WhatIfOnly)
Connect-SPOService -Url 'https://spaarke-admin.sharepoint.com'
$ids = @(
  'b!QPhJA-4NgU6Tx4SFIauctdNtZCRkudVMkCm7XnMdUkAEyFUFBjlmQJQzNXlrHp-y',  # 2026-10-10 task 039 gate (zz-039 probe)
  'b!vlLJSaMQZE6HXqqB9TzOr9NtZCRkudVMkCm7XnMdUkAEyFUFBjlmQJQzNXlrHp-y'   # 2026-10-10 task 039 gate (zz-039 probe)
)
foreach ($id in $ids) {
  try {
    $c = Get-SPOContainer -Identity $id -ErrorAction Stop
    "{0}  {1}  storage={2}" -f $id.Substring(0,12), $c.ContainerName, $c.StorageUsedInBytes
    if (-not $WhatIfOnly) { Remove-SPOContainer -Identity $id -ErrorAction Stop; "   removed" }
  } catch { "{0}  SKIPPED: {1}" -f $id.Substring(0,12), $_.Exception.Message }
}
