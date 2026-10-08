# Deletes the 21 empty test SPE containers left by the 2026-10-06 dev live gates (owner rounds 68 + 72).
# Re-checked 2026-10-06: no project, matter, work assignment, business unit, document or sprk_container row references any of them.
# Run as a SharePoint administrator. Deleted containers go to the deleted-container collection (restorable ~93 days).
# Requires: Install-Module Microsoft.Online.SharePoint.PowerShell (Windows PowerShell 5.1, or pwsh with -UseWindowsPowerShell).
param([switch]$WhatIfOnly)
Connect-SPOService -Url 'https://spaarke-admin.sharepoint.com'
$ids = @(
  'b!1jQ90fRNREqIkel3wEl2txG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN',
  'b!4ltjdXaQMEGKlLr47q0m6RG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN',
  'b!8qdU0gEubUSu29rQVtpvehG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN',
  'b!DuRySBK9FkKk2wSDxRigwhG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN',
  'b!ECpwsE3P2Em14S3B84uYOxG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN',
  'b!HBRbokLXnUGzaDLSTdNFvM5RFHtaaUZCi0Jm-xs-hDQV_6QuLuKmR4jrMdC6UgMm',
  'b!HkqHE0XETUCPMjmQJXU3YBG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN',
  'b!IRjcuov6dU-QV2n8wNvGgxG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN',
  'b!Q93-JrZ7fUmviHObJDHmyhG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN',
  'b!UhVkyYyZrEKopsCjLi3dfxG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN',
  'b!UhxDf-Qp9UKCLyeglKKvdhG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN',
  'b!ZxwACqn6EEKP99-lPgOiBhG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN',
  'b!_0H4eQgAXkm7AAo66_apUBG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN',
  'b!_yZXxbMiOESIRAX0EKNSlhG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN',
  'b!qZ9Qj5EIfkKIBIESyeRL1xG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN',
  'b!FpQN0FPDuUuKn-aoY7I67xG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN',
  'b!QVo-0vBMH0qWO1U5nZGwPRG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN',
  'b!UoL_Ekv0hE6Ak89yNAKEcBG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN',
  'b!7zUY_IuXrUKb3492YsfnVhG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN',
  'b!tEidDpRRbU2whrkDMjCtzBG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN',
  'b!6BMp2UkK50GCP66tPeRxURG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN'
)
foreach ($id in $ids) {
  try {
    $c = Get-SPOContainer -Identity $id -ErrorAction Stop
    "{0}  {1}  storage={2}" -f $id.Substring(0,12), $c.ContainerName, $c.StorageUsedInBytes
    if (-not $WhatIfOnly) { Remove-SPOContainer -Identity $id -ErrorAction Stop; "   removed" }
  } catch { "{0}  SKIPPED: {1}" -f $id.Substring(0,12), $_.Exception.Message }
}
