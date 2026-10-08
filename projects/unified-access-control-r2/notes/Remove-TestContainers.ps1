# Deletes the 23 test SPE containers left by the 2026-10-06/07/08 dev live gates (owner rounds 68 + 72; 2 added by the 2026-10-08 gatesA run).
# Re-checked 2026-10-08 (gatesA gate 0): no row in any of the 23 container-id columns (project, matter, work assignment, business unit,
# document container + drive id, sprk_container, communication, attachment, event, invoice, budget, organization, ...) references any of them.
# 20 of the first 21 read 200 'active' via Graph as the owning app; b!HBRbo... answers 400 'Invalid hostname for this tenancy' (expect SKIPPED).
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
  'b!6BMp2UkK50GCP66tPeRxURG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN',
  'b!R6QrqcsZ_02U89YmOYM8qBG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN',  # 2026-10-08 gate 163b secure matter (deleted)
  'b!juvuYNOcskGRJ96iMUBTeRG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN',  # 2026-10-08 gate 166d secure project (deleted)
  'b!CtWYdqDTo0GSJGqApjHauRG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN',  # 2026-10-08 gate #1410 secure parent M (deleted)
  'b!HMt6g1HOGEe9Jp1UZUDQrxG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN'   # 2026-10-08 gate #1410 secure work assignment W1 (deleted)
)
foreach ($id in $ids) {
  try {
    $c = Get-SPOContainer -Identity $id -ErrorAction Stop
    "{0}  {1}  storage={2}" -f $id.Substring(0,12), $c.ContainerName, $c.StorageUsedInBytes
    if (-not $WhatIfOnly) { Remove-SPOContainer -Identity $id -ErrorAction Stop; "   removed" }
  } catch { "{0}  SKIPPED: {1}" -f $id.Substring(0,12), $_.Exception.Message }
}
