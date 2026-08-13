# Phase B — Rung-decision preview harness (READ-ONLY).
# Calls POST /api/communications/{id}/suggest-associations on each seeded UAT row and prints the
# association engine's evaluate-only decision (status + candidates + conflict + top rung). No writes.
#
# Auth: mints a token for the BFF audience via `az account get-access-token`. Requires `az login`
# as a user with access to the BFF API. Target defaults to dev.
param(
  [string]$Bff   = 'https://spaarke-bff-dev.azurewebsites.net',
  [string]$BffAud = 'api://1e40baad-e065-4aea-a8d4-4b7ab273458c',
  [string]$Org   = 'https://spaarkedev1.crm.dynamics.com',
  [string]$Marker= 'uat-seed-20260813'
)
$ErrorActionPreference = 'Stop'
$dvTok  = az account get-access-token --resource $Org    --query accessToken -o tsv
$bffTok = az account get-access-token --resource $BffAud --query accessToken -o tsv
$dvH  = @{ Authorization="Bearer $dvTok"; Accept='application/json' }
$bffH = @{ Authorization="Bearer $bffTok"; 'Content-Type'='application/json' }

# Golden expectations (from projects/.../notes/fixtures/r1-golden-emails.md). Absent = informational only.
$expected = @{ '03'='Ambiguous'; '11'='Suggested|Ambiguous' }

$rows = Invoke-RestMethod -Headers $dvH -Uri "$Org/api/data/v9.2/sprk_communications?`$filter=startswith(sprk_correlationid,'$Marker')&`$select=sprk_communicationid,sprk_correlationid,sprk_subject&`$orderby=sprk_correlationid"
Write-Host ("{0,-4} {1,-14} {2,-4} {3,-7} {4}" -f 'row','eval-status','cand','result','subject')
Write-Host ('-' * 100)
$pass=0; $checked=0
foreach ($r in $rows.value) {
  $n = ($r.sprk_correlationid -split '-')[-1]
  try {
    $d = Invoke-RestMethod -Headers $bffH -Method Post -Uri "$Bff/api/communications/$($r.sprk_communicationid)/suggest-associations"
  } catch { Write-Host ("{0,-4} ERROR: {1}" -f $n, $_.Exception.Message); continue }
  $status = "$($d.status)"
  $cand   = @($d.candidates).Count
  $conflict = (@($d.candidates) | Where-Object { $_.conflict }).Count -gt 0
  $result = ''
  if ($expected.ContainsKey($n)) {
    $checked++
    if ($status -match $expected[$n]) { $result='PASS'; $pass++ } else { $result="FAIL(exp $($expected[$n]))" }
  }
  $subj = $r.sprk_subject; if ($subj.Length -gt 46){ $subj=$subj.Substring(0,46) }
  $flag = if ($conflict) { "$status*conflict" } else { $status }
  Write-Host ("{0,-4} {1,-14} {2,-4} {3,-7} {4}" -f $n, $flag, $cand, $result, $subj)
}
Write-Host ''
Write-Host "Golden assertions: $pass/$checked passed. (Read-only preview — no rows were written.)"
