# Seeds a UAT test corpus of sprk_communication rows (+ Job B / Job C sprk_emailreviewlog
# proposals) into spaarkedev1 to exercise the POST-CAPTURE review features:
# reconciliation grid (needs-review + per-team), triage display, queue-feed ranking,
# association review, Job B apply/dismiss, Job C create-task/apply.
#
# Idempotent: each row carries a unique sprk_correlationid (uat-seed-20260813-NN).
# Re-running SKIPS rows that already exist. Pass -Clean to delete the marked corpus first.
#
# These are DIRECTLY-SEEDED rows (they bypass real capture) — they exercise the review UI
# + apply/dismiss/create-task endpoints, NOT the capture-time rungs/dedup/triage-AI.
param([switch]$Clean)
$ErrorActionPreference = 'Stop'
$org = 'https://spaarkedev1.crm.dynamics.com'
$api = "$org/api/data/v9.2"
$token = az account get-access-token --resource $org --query accessToken -o tsv
$H = @{ Authorization="Bearer $token"; 'Content-Type'='application/json'; 'OData-MaxVersion'='4.0'; 'OData-Version'='4.0'; Accept='application/json' }
$Hc = $H.Clone(); $Hc['Prefer'] = 'return=representation'

# --- Reference GUIDs (verified live 2026-08-13) ---
$M_HMD='00aef0ab-3385-f111-8075-7c1e5268570d'; $M_NDA='6e6869ee-6f96-f111-b8dc-7ced8ddc4a05'
$M_HING='ddacf68c-5082-f111-ab0f-70a8a590c51c'; $M_NET='7cf177b7-4165-f111-ab0c-7ced8ddc4cc6'
$CAT_COURT='65310056-598b-f111-8077-7ced8ddc4cc6'; $CAT_CLIENT='5ac90050-598b-f111-8077-7ced8ddc4cc6'
$CAT_OPP='7d310056-598b-f111-8077-7ced8ddc4cc6'; $CAT_INV='71310056-598b-f111-8077-7ced8ddc4cc6'
$CAT_SCHED='73310056-598b-f111-8077-7ced8ddc4cc6'; $CAT_ADMIN='80310056-598b-f111-8077-7ced8ddc4cc6'
$CAT_NOISE='8b310056-598b-f111-8077-7ced8ddc4cc6'
$TEAM_SPAARKE='09fbf21c-1872-f011-b4cb-7c1e52671ad0'
$MARKER='uat-seed-20260813'
# choice consts
$URGENT=100000000;$HIGH=100000001;$MED=100000002;$LOW=100000003
$FILE=100000000;$UPDATE=100000001;$ROUTE=100000002;$DISMISS=100000003;$PENDING=100000004
$SUGG=100000003;$AMBIG=100000004;$PENDREV=100000001;$UNRES=100000002

if ($Clean) {
  $ex = Invoke-RestMethod -Headers $H -Uri "$api/sprk_communications?`$filter=startswith(sprk_correlationid,'$MARKER')&`$select=sprk_communicationid"
  foreach ($r in $ex.value) { Invoke-RestMethod -Headers $H -Method Delete -Uri "$api/sprk_communications($($r.sprk_communicationid))" | Out-Null }
  Write-Host "Cleaned $($ex.value.Count) prior corpus rows."
}

# One corpus row = a hashtable of Web API fields. 'regmatter'/'regproject' are helper keys expanded to @odata.bind.
$corpus = @(
 @{n='01';subj='USPTO Office Action - Response due within 60 days (App. 11/420,624)';from='examiner@uspto.gov';to='ralph.schroeder@spaarke.com';cat=$CAT_COURT;pri=$URGENT;out=$FILE;st=$SUGG;conf=0.94;regmatter=$M_HMD;sum='USPTO non-final office action; response deadline in 60 days. Rejections under 35 USC 103.'}
 @{n='02';subj='Re: Settlement terms - client approval needed before Friday';from='gc@drowsydigital.com';to='ralph.schroeder@spaarke.com';cat=$CAT_CLIENT;pri=$HIGH;out=$FILE;st=$SUGG;conf=0.88;regmatter=$M_NDA;sum='Client requests approval of revised settlement terms for the NDA dispute.'}
 @{n='03';subj='Both matters - please advise on PAT-411021 and PAT-415062 strategy';from='cio@drowsydigital.com';to='ralph.schroeder@spaarke.com';cat=$CAT_CLIENT;pri=$HIGH;out=$ROUTE;st=$AMBIG;conf=0.55;sum='Email references TWO matters (PAT-411021 + PAT-415062) - engine must withhold as Ambiguous, never auto-crown.'}
 @{n='04';subj='Invoice 10044725 - August professional fees';from='billing@outsidecounsel.com';to='ralph.schroeder@spaarke.com';cat=$CAT_INV;pri=$MED;out=$UPDATE;st=$SUGG;conf=0.72;regmatter=$M_HING;sum='Monthly invoice for the Hing Canadian patent response. Attachment: Invoice-10044725.pdf.'}
 @{n='05';subj='Deposition scheduling - Hing matter, week of Sept 15';from='paralegal@outsidecounsel.com';to='ralph.schroeder@spaarke.com';cat=$CAT_SCHED;pri=$MED;out=$ROUTE;st=$PENDREV;conf=0.66;regmatter=$M_HING;sum='Proposed deposition dates for the week of September 15.'}
 @{n='06';subj='Opposing counsel - request for 14-day extension';from='counsel@opposingfirm.com';to='ralph.schroeder@spaarke.com';cat=$CAT_OPP;pri=$HIGH;out=$ROUTE;st=$SUGG;conf=0.79;regmatter=$M_NET;sum='Opposing counsel requests a 14-day extension to respond to the network patent claims.'}
 @{n='07';subj='IP Law Monthly - August newsletter';from='newsletter@iplawmonthly.com';to='ralph.schroeder@spaarke.com';cat=$CAT_NOISE;pri=$LOW;out=$DISMISS;st=$UNRES;conf=0.15;sum='Marketing newsletter - no action, dismiss candidate.'}
 @{n='08';subj='Please update the next review date to September 30 for the Drowsy Digital matter';from='gc@drowsydigital.com';to='ralph.schroeder@spaarke.com';cat=$CAT_CLIENT;pri=$MED;out=$UPDATE;st=$SUGG;conf=0.81;regmatter=$M_NDA;sum='Client asks to set the next review date to Sept 30 - Job B field-update proposal target.'}
 @{n='09';subj='Action required: file the amendment by this Friday';from='partner@spaarke.com';to='ralph.schroeder@spaarke.com';cat=$CAT_COURT;pri=$URGENT;out=$FILE;st=$SUGG;conf=0.90;regmatter=$M_HMD;sum='Explicit follow-up task implied - Job C create-task proposal target.'}
 @{n='10';subj='Re: [PAT-545148] follow-up on claim amendments';from='agent@cdnpatents.ca';to='ralph.schroeder@spaarke.com';cat=$CAT_ADMIN;pri=$LOW;out=$PENDING;st=$PENDREV;conf=0.60;regmatter=$M_HING;sum='Threaded reply on claim amendments (In-Reply-To an earlier message).'}
 @{n='11';subj='New patent application - internal ref PAT-942665';from='inventor@drowsydigital.com';to='ralph.schroeder@spaarke.com';cat=$CAT_CLIENT;pri=$MED;out=$ROUTE;st=$AMBIG;conf=0.50;sum='References PAT-942665 which does not match an existing matter - new-record-referenced ("Looks like a new Project").'}
 @{n='12';subj='Filing deadline reminder - Customized Network patent (team queue)';from='docket@spaarke.com';to='ralph.schroeder@spaarke.com';cat=$CAT_COURT;pri=$HIGH;out=$FILE;st=$SUGG;conf=0.85;regmatter=$M_NET;team=$TEAM_SPAARKE;sum='Routed to the Spaarke team queue - per-team grid row (ownerid = team).'}
 @{n='13';subj='Wire instructions change - please verify before remitting';from='accounts@outsidecounsel.com';to='ralph.schroeder@spaarke.com';cat=$CAT_ADMIN;pri=$URGENT;out=$ROUTE;st=$SUGG;conf=0.70;regmatter=$M_NDA;sum='Bank detail change request - high-priority admin/security review.'}
 @{n='14';subj='Meeting notes - internal sync, no action items';from='assistant@spaarke.com';to='ralph.schroeder@spaarke.com';cat=$CAT_ADMIN;pri=$LOW;out=$DISMISS;st=$UNRES;conf=0.20;sum='Internal notes - low-priority noise.'}
)

$ids = @{}
foreach ($c in $corpus) {
  $cid = "$MARKER-$($c.n)"
  $existing = Invoke-RestMethod -Headers $H -Uri "$api/sprk_communications?`$filter=sprk_correlationid eq '$cid'&`$select=sprk_communicationid"
  if ($existing.value.Count -gt 0) { $ids[$c.n]=$existing.value[0].sprk_communicationid; Write-Host "SKIP comm $($c.n) (exists)"; continue }
  $body = [ordered]@{
    sprk_name="Email: $($c.subj)"; sprk_subject=$c.subj; sprk_from=$c.from; sprk_to=$c.to
    sprk_body=$c.sum; sprk_communicationtype=100000000; sprk_direction=100000000; statuscode=659490003
    sprk_triagepriority=$c.pri; sprk_reviewoutcome=$c.out; sprk_associationstatus=$c.st
    sprk_riconfidence=$c.conf; sprk_triagesummary=$c.sum; sprk_correlationid=$cid
    sprk_receiveddate=(Get-Date).ToUniversalTime().ToString('o')
    'sprk_TriageCategory@odata.bind'="/sprk_triagecategories($($c.cat))"
  }
  if ($c.regmatter) { $body['sprk_RegardingMatter@odata.bind']="/sprk_matters($($c.regmatter))" }
  if ($c.regproject){ $body['sprk_RegardingProject@odata.bind']="/sprk_projects($($c.regproject))" }
  if ($c.team)      { $body['ownerid@odata.bind']="/teams($($c.team))" }
  $created = Invoke-RestMethod -Headers $Hc -Method Post -Uri "$api/sprk_communications" -Body ($body|ConvertTo-Json)
  $ids[$c.n]=$created.sprk_communicationid
  Write-Host "CREATE comm $($c.n) -> $($created.sprk_communicationid)  [$($c.subj.Substring(0,[Math]::Min(48,$c.subj.Length)))]"
}

# --- Job B / Job C proposals (sprk_emailreviewlog) linked to specific comms ---
function New-Proposal($commN,$name,$entity,$field,$recid,$suggestion,$conf,$sourceref) {
  $comm = $ids[$commN]; if (-not $comm) { return }
  $pcid = "$MARKER-p-$commN-$field"
  $ex = Invoke-RestMethod -Headers $H -Uri "$api/sprk_emailreviewlogs?`$filter=sprk_sourceref eq '$pcid'&`$select=sprk_emailreviewlogid"
  if ($ex.value.Count -gt 0) { Write-Host "SKIP proposal $pcid (exists)"; return }
  $b = [ordered]@{
    sprk_name=$name; sprk_action=100000001; sprk_actortype=100000000; sprk_actor='Job B/C (UAT seed)'
    sprk_targetentity=$entity; sprk_targetfield=$field; sprk_targetrecordid=$recid
    sprk_aisuggestion=$suggestion; sprk_confidence=$conf; sprk_sourceref=$pcid
    'sprk_Communication@odata.bind'="/sprk_communications($comm)"
  }
  Invoke-RestMethod -Headers $Hc -Method Post -Uri "$api/sprk_emailreviewlogs" -Body ($b|ConvertTo-Json) | Out-Null
  Write-Host "CREATE proposal ($commN) $field"
}
# Job B: next-review-date on the Drowsy Digital matter (from comm 08)
New-Proposal '08' 'Propose sprk_nextreviewdate = 2026-09-30' 'sprk_matter' 'sprk_nextreviewdate' $M_NDA '{"value":"2026-09-30","citation":"Please update the next review date to September 30"}' 0.81 'body:offset-27'
# Job B: matter description enrichment (from comm 02)
New-Proposal '02' 'Propose sprk_matterdescription addition' 'sprk_matter' 'sprk_matterdescription' $M_NDA '{"value":"Revised settlement terms proposed; client approval pending (Aug 2026).","citation":"revised settlement terms"}' 0.77 'body:offset-14'
# Job C: create-task from comm 09 (sentinel target field)
$jobC = @{ subject='File the amendment (App. 11/420,624)'; dueDate='2026-08-15'; regardingMatter=$M_HMD } | ConvertTo-Json -Compress
New-Proposal '09' 'Create task: File the amendment by Friday' 'sprk_event' '__create_task__:file-amendment' $M_HMD $jobC 0.90 'body:offset-0'

Write-Host ''
Write-Host "Corpus seeded. Communications: $($ids.Count). Marker: sprk_correlationid startswith '$MARKER'."
Write-Host "Needs-review grid should now show the non-Resolved rows; comm 12 is team-owned (per-team grid)."
