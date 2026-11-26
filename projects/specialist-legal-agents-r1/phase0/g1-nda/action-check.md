# G1-NDA: agreement-review Action check (task 010, FR-02, Unresolved Question 1)

Checked 2026-11-26, read-only. Status: **blocked on owner go** for the one DEV write below. Nothing was created, updated or deleted in Dataverse, and `Deploy-AnalysisAction.ps1` was not run.

## Result

| Question | Answer |
|---|---|
| Is the `agreement-review` row deployed in DEV? | **Yes.** `sprk_analysisaction` `34c9ecf2-cb10-f111-8342-7ced8d1dc988`, active, last modified 2026-11-01 16:03 UTC. |
| `sprk_actioncode` | `agreement-review` (exactly one row). No `nda-review` row remains. No `agreement-review-spike` row exists yet. |
| Model tier | `sprk_modeltier` = 100000002 = **Reasoning**. `sprk_temperature` is empty. `sprk_kind` is empty. Allows knowledge, tools and skills: yes. Allows delivery: no. |
| Declared input schema | `documentText` only (string, max 60000, required). No profile input is declared. |
| Does the mirror match the row? | Yes. `sprk_systemprompt` (8907 chars), `sprk_description`, `sprk_name` and `sprk_outputschemajson` equal `infra/dataverse/actions/agreement-review.action.json`. The row's `sprk_inputschema` equals `infra/dataverse/inputschemas/agreement-review.input.schema.json`. |
| Can the deployed schema carry the five profile positions? | **No.** `ContextBinder.CollectDeclaredCompanions` forwards a companion input to the model only when the Action declares it in `sprk_inputschema` (ADR-043). With only `documentText` declared, `term`, `residuals`, `nonSolicit`, `governingLaw` and `uncappedIndemnityFlag` would be accepted and dropped. |
| Copy needed? | **Yes.** `agreement-review-spike`, DEV only. The production row is never edited (FR-02). |

### Query used

DEV org `https://spaarkedev1.crm.dynamics.com` (from `scripts/Deploy-AnalysisAction.ps1` and `infra/dataverse/inputschemas/agreement-review.input.schema.json`), Web API v9.2, `az account get-access-token --resource <org>`, GET only. The token was held in a variable and never printed or written.

```
GET /api/data/v9.2/sprk_analysisactions
  ?$filter=sprk_actioncode eq 'agreement-review' or sprk_actioncode eq 'agreement-review-spike' or sprk_actioncode eq 'nda-review'
  &$select=sprk_analysisactionid,sprk_actioncode,sprk_name,sprk_modeltier,sprk_inputschema,sprk_outputschemajson,modifiedon,statecode
```

A second GET read the full row (attribute names, truncated values) and entity metadata (`EntityDefinitions(LogicalName='sprk_analysisaction')/Attributes`).

## The spike copy

File: `phase0/g1-nda/agreement-review-spike.action.json` (prepared, not deployed).

- `actionCode` is `agreement-review-spike`.
- `inputSchema` declares `documentText` (required, verbatim from the production mirror) and the five profile positions. The names are the `inputName` values in `profiles/profile-a.json` and `profile-b.json` (task 002): `term`, `residuals`, `nonSolicit`, `governingLaw` as strings (max 1500) and `uncappedIndemnityFlag` as a boolean. The four strings carry the position text (preferred, fallback, escalate); the tool (task 011) renders them from the profile.
- `systemPrompt` is the production prompt plus two additions, because a declared input only reaches the `## Input` block and the production prompt says the model may reason only from the agreement and the retrieved standard:
  - grounding source 3 (the declared profile inputs);
  - a `REVIEWER PROFILE POSITIONS` section. It makes the supplied position replace the retrieved standard on those five topics only, emits one finding per topic that has a clause (Low meets, Medium fallback, High or Critical escalate), requires a High-or-above finding for an uncapped indemnity when the flag is true, and sets `standardRef` to `Reviewer profile: <input name>`.
- `outputSchema`, `description`, `modelTier` (Reasoning) and `outputDeterminism` (advisory) are unchanged. The output contract stays closed, so no consumer changes.
- No `temperature` key: the deployed row leaves it empty, and Reasoning deployments may reject a non-default value.

Tier handling for the spike: the row carries Reasoning, but the linear path ignores Binding tier overrides (#1934), so `spike_review_nda` still applies the test profile's tier explicitly (`action with { ModelTier = ... }`).

### Deviations from the plan (also for `notes/decisions.md`)

1. **`Deploy-AnalysisAction.ps1` cannot create this row.** It is stale: it needs an `actions[]` wrapper and a mandatory `actionTypeName`, and it writes `sprk_ActionTypeId@odata.bind`. That lookup does not exist on `sprk_analysisaction` in DEV (verified from entity metadata: the only non-system lookups are `sprk_analysisid` and `sprk_modeldeploymentid`). It also never writes `sprk_modeltier`. `scripts/Deploy-ActionMirrors.ps1` documents the same retirement. Running the old script would fail on the POST (or, for a row without the wrapper, do nothing).
2. **`scripts/ebilling/Ensure-ActionRow.ps1` + `Deploy-ActionMirrors.ps1` are the repo's supported route**, but both read only `infra/dataverse/actions/<code>.action.json` and `infra/dataverse/inputschemas/`. Using them would put the spike files under `infra/`, which this task may not touch. `Ensure-ActionRow.ps1` also creates new rows with `sprk_allowsknowledge = false`, which differs from the production row.
3. So the command below is a one-off Web API create that reads the spike file where it sits. It was dry-run on 2026-11-26: it checked that no spike row exists and printed the body columns, and wrote nothing.

## Awaiting the owner's go: one DEV write

Creates one row, `agreement-review-spike`, in `https://spaarkedev1.crm.dynamics.com`. It refuses to run if a row with that code already exists. It touches no other row, and the production row is not read for writing. Removal is at FR-14 clean-up (delete the row by its code).

Reply "go" in chat to run it. The command, from `C:\code_files\spaarke-wt-specialist-legal-agents-r1` in PowerShell 7, after `az login`:

```powershell
$apply = $false   # set to $true ONLY after the owner has said go in chat
$ErrorActionPreference = 'Stop'
$org  = 'https://spaarkedev1.crm.dynamics.com'
$file = 'C:\code_files\spaarke-wt-specialist-legal-agents-r1\projects\specialist-legal-agents-r1\phase0\g1-nda\agreement-review-spike.action.json'
$def  = Get-Content $file -Raw -Encoding UTF8 | ConvertFrom-Json
if ($def.actionCode -ne 'agreement-review-spike') { throw 'wrong actionCode in spike file' }
$tok = az account get-access-token --resource $org --query accessToken -o tsv
$h = @{ Authorization = "Bearer $tok"; Accept = 'application/json'; 'Content-Type' = 'application/json; charset=utf-8'; 'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0' }
$api = "$org/api/data/v9.2"
$ex = Invoke-RestMethod -Uri "$api/sprk_analysisactions?`$filter=sprk_actioncode eq 'agreement-review-spike'&`$select=sprk_analysisactionid" -Headers $h
if ($ex.value.Count -gt 0) { throw "agreement-review-spike already exists ($($ex.value[0].sprk_analysisactionid)); not creating a second row" }
$body = [ordered]@{
  sprk_actioncode       = $def.actionCode
  sprk_name             = $def.name
  sprk_description      = $def.description
  sprk_systemprompt     = $def.systemPrompt
  sprk_outputschemajson = ($def.outputSchema | ConvertTo-Json -Depth 30 -Compress)
  sprk_inputschema      = ($def.inputSchema  | ConvertTo-Json -Depth 30 -Compress)
  sprk_modeltier        = 100000002   # Reasoning, copied from the deployed agreement-review row
  sprk_tags             = $def.tags
  sprk_allowsknowledge  = $true
  sprk_allowstools      = $true
  sprk_allowsskills     = $true
  sprk_allowsdelivery   = $false
}
"body columns: $($body.Keys -join ', ')"
if (-not $apply) { 'DRY RUN: nothing written'; return }
$bytes = [Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 30))
$resp = Invoke-WebRequest -Uri "$api/sprk_analysisactions" -Headers $h -Method Post -Body $bytes
"CREATED: $($resp.Headers['OData-EntityId'])"
```

After the create: wait about 45 minutes before a test agent uses a changed action, or use a fresh app id (BR-2/BR-3). Task 011 then points `spike_review_nda` at `agreement-review-spike` and records the call's latency.

### Not performed

- No row was created, updated or deleted in DEV. `Deploy-AnalysisAction.ps1` was not run.
- The spike row's prompt addition has not been tested against the model. Task 011/021 tune the wording if the pilot shows a profile position is not applied.

## Result (2026-11-26)

- Owner go in chat 2026-11-26 ("Yes go"). The command above was run once with `$apply = $true` from a scratch copy; the file keeps `$apply = $false`.
- Created: `sprk_analysisactions(17285c7b-98e9-f111-84ea-00224829aef9)`, `sprk_actioncode` `agreement-review-spike`, active, tier 100000002 (Reasoning).
- Read back: declared inputs `documentText, term, residuals, nonSolicit, governingLaw, uncappedIndemnityFlag`; exactly one row with this action code.
- The production `agreement-review` row (`34c9ecf2-…`) was not touched.
- Removal: task 031.
