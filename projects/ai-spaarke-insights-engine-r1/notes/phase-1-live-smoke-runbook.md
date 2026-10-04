# Phase 1 Live-Environment Smoke Runbook (D-P16 Artifact B)

> **Owner**: task 080 (deploy verification) — NOT run in CI.
> **Purpose**: Exercise the REAL Spaarke Insights Engine pipeline against the deployed Spaarke Dev environment to satisfy SPEC §1 acceptance bar ("real Observations from real documents, not infrastructure with mock data").
> **Companion artifact**: in-process smoke test at `tests/unit/Sprk.Bff.Api.Tests/EndToEnd/Phase1SmokeTest.cs` (CI-friendly, deterministic, mocked facade) handles wire-contract verification on every PR.

---

## Why two artifacts

| Artifact | Where it runs | What it verifies | Speed | Cost |
|---|---|---|---|---|
| **A — In-process smoke** (`Phase1SmokeTest.cs` + `PredictMatterCostEvalHarnessTests.cs`) | CI on every PR via `dotnet test` + `.github/workflows/insights-eval.yml` | Wire contract — auth, validation, ProblemDetails, envelope shape, observability headers, decline path, §3.5 facade boundary, golden-dataset baseline metrics with mocked `IInsightsAi` | ~15s | $0 |
| **B — Live-environment runbook** (this file) | Manual, after task 080 deploys to Spaarke Dev | End-to-end REAL pipeline: SPE upload → ingest playbook → Observations in `spaarke-insights-index` → synthesis playbook → Inference / Decline. Real LLM, real Azure AI Search, real Dataverse, real Service Bus | ~10-20 min per fixture | ~$0.10-$0.50 per fixture per run |

Artifact A proves the wire contract holds in isolation; Artifact B proves the wire contract holds when the real pipeline is doing the work. **Both are required for Phase 1 acceptance — neither replaces the other.**

---

## `/api/insights/ask` subject contract (updated 2026-10-04 — unified-access-control-r2 task 163, GitHub #1102)

The route authorization sweep changed what `/api/insights/ask` accepts. Steps 7-9 below are written to this contract;
an older copy of this runbook (display-number subjects, a raw playbook GUID, `matterType` / `lookBackYears`
parameters) now gets 400 or 404 at every step.

| Field | Contract | What happens otherwise |
|---|---|---|
| `subject` | `matter:{sprk_matterid}` — the GUID of a REAL `sprk_matter` row. Display numbers (`matter:M-2024-0341`) are not ids. | 400 (not a GUID) |
| `question` | The `sprk_consumercode` of an **enabled `insights-ask` Binding row** (`sprk_playbookconsumer`, ADR-039) — e.g. `predict-matter-cost`, `matter-health-single`. A raw playbook GUID is accepted only when the Binding that targets that playbook is an `insights-ask` one. | 400 "must be either a valid playbook Guid id OR a canonical name registered as an enabled sprk_playbookconsumer row" |
| `parameters` | Task 164's SHARED playbook-parameter policy — the same one `/api/ai/playbooks/{id}/execute` applies (a declared allow-list). Refused: server-owned keys (`userId`, `tenantId`, `userName`, `run.*`, `start.*`, `userPreferences.*`), a record key (`matterId` / `projectId` / `invoiceId`) that is not a GUID, a typed key of the wrong type (`timeWindowHours`, `dueWithinDays`, `todayUtc`, `dueSoonWindowUtc`), a GUID on a text key, and **every undeclared key — including `matterType`, `lookBackYears`, `currency`** (no Insights playbook node consumes them). Send `{}` unless you need a declared text key (e.g. `matterDescription`). | 400, `errorCode = playbook.parameter-rejected` |
| The caller's rights | Asked of Dataverse AS THE CALLER, before anything runs: **Read** on the subject matter for a playbook that cannot write to it (**predict-matter-cost**), **Write** for one that can (**matter-health-single** persists its envelope to `sprk_matter.sprk_performancesummary`), plus the same rule on any record parameter. | the uniform 404 (`reasonCode = sdap.access.deny.record_unavailable`) — identical for an absent matter, one you cannot read, and one you can read but not write when the playbook writes |

The same subject rule applies to `/api/insights/assistant/query`: a playbook the Assistant picks for a caller without
Write on the subject runs only if it cannot write to it (otherwise 404 before the stream opens, or an `error` frame
with `errorCode = insights.subject.write_required` after it opened).

---

## Prerequisites (run once)

1. **Spaarke Dev environment provisioned**:
   - `spaarke-insights-index` AI Search index exists (D-P2 — verify via portal or `az search service show`)
   - `sprk_precedent` Dataverse entity deployed (D-P3 via task 011)
   - `sprk_analysis` extended with disposition fields (D-P11 via task 052)
   - BFF API deployed to App Service (task 080 prerequisite)
   - `text-embedding-3-large` deployed in `spaarke-openai-dev` (verified per EXT-2 dependency)
   - `predict-matter-cost` playbook row created in Dataverse via `scripts/Deploy-Playbook.ps1` (task 060 prerequisite).
     As of 2026-10-04 it is **NOT deployed on spaarkedev1** (read-only check: `sprk_analysisplaybook` has
     `matter-health-single` only) — deploy it first.
   - **An enabled `insights-ask` Binding row for `predict-matter-cost`** (the subject contract above). The repo seed
     (`infra/dataverse/sprk_playbookconsumer-rows.json`) binds only `matter-health-single`; the mirror is a projection
     of the live table, so the row is created LIVE and then exported. This is a live write — dry run, apply, verify:
     ```pwsh
     $org = 'https://spaarkedev1.crm.dynamics.com'; $api = "$org/api/data/v9.2"
     $h = @{ Authorization = "Bearer $(az account get-access-token --resource $org --query accessToken -o tsv)";
             'OData-Version' = '4.0'; Accept = 'application/json'; 'Content-Type' = 'application/json' }
     $pb = (Invoke-RestMethod "$api/sprk_analysisplaybooks?`$select=sprk_analysisplaybookid,sprk_name&`$filter=sprk_name eq 'predict-matter-cost@v1' and statecode eq 0" -Headers $h).value
     if ($pb.Count -ne 1) { throw "expected exactly one active predict-matter-cost@v1 playbook, found $($pb.Count) — deploy it first" }
     $key = "sprk_consumertype='insights-ask',sprk_consumercode='predict-matter-cost',sprk_environment='*'"
     $row = [ordered]@{ sprk_name = 'Insights Ask - predict-matter-cost'; sprk_consumertype = 'insights-ask'
                        sprk_consumercode = 'predict-matter-cost'; sprk_environment = '*'; sprk_priority = 400; sprk_enabled = $true
                        sprk_ucid = 'UC-C-2'; sprk_disposition = 100000000; sprk_risk = 100000000; sprk_capturemode = 100000000
                        'sprk_Playbook@odata.bind' = "/sprk_analysisplaybooks($($pb[0].sprk_analysisplaybookid))" }
     # 1. DRY RUN — print what would be written; nothing is sent.
     $row | ConvertTo-Json; "PATCH $api/sprk_playbookconsumers($key)"
     # 2. APPLY (only after reviewing the dry run) — idempotent upsert on the alternate key.
     Invoke-WebRequest "$api/sprk_playbookconsumers($key)" -Method Patch -Headers $h -Body ($row | ConvertTo-Json) -UseBasicParsing | Out-Null
     # 3. VERIFY — the row reads back enabled, targeting the playbook; then refresh and commit the mirror.
     Invoke-RestMethod "$api/sprk_playbookconsumers($key)?`$select=sprk_enabled,_sprk_playbook_value" -Headers $h
     .\scripts\dataverse\Seed-PlaybookConsumers.ps1 -Export; .\scripts\dataverse\Seed-PlaybookConsumers.ps1 -DiffOnly   # exit 0
     ```
   - **Fixture matters are real `sprk_matter` rows.** Note the `sprk_matterid` GUID of the matter each fixture
     (M-2024-0341, M-2024-0188, M-2024-0512) is filed under, plus one TEST matter with no comparable cohort (Step 9).
2. **Operator authenticated**:
   - `az login` against the Spaarke Dev tenant
   - `pac auth create --environment <env-url>` for Dataverse MCP
3. **Local repo on the deployment branch**:
   - `git fetch origin && git checkout work/ai-spaarke-insights-engine-r1`
4. **Authentication for `/api/insights/ask`**:
   - Bearer token for a test tenant user who can **Read** each fixture matter (enough for `predict-matter-cost`, which
     writes nothing; `matter-health-single` needs **Write** on the matter). A user without that right gets the uniform
     404 — use that for the negative check in Step 7. Acquire via:
     ```pwsh
     az account get-access-token --resource api://<API_APP_ID> --query accessToken -o tsv
     ```

---

## Runbook — one-time per Phase 1 acceptance review

### Step 1: Enable Insights ingest for the fixture upload path

By default (per task 050 design), `AiProcessingOptions.InsightsIngest = false` so production upload events do NOT trigger Insights ingest. For Phase 1 smoke:

1. Either flip the flag in `appsettings.Development.json` for the deployed BFF (App Service Configuration → `AiProcessingOptions__InsightsIngest = true`), OR
2. Send the upload request with the flag in the `OfficeJobMessage.AiOptions.InsightsIngest = true` (per task 050's opt-in design — preferred for scoped testing).

### Step 2: Upload 3 fixture documents to SPE

Use the existing upload endpoint or PAC CLI:

```pwsh
# Fixture 1 — closing letter (M-2024-0341 in tests/Insights/fixtures/)
# Fixture 2 — settlement agreement (M-2024-0188)
# Fixture 3 — decision memo (M-2024-0512)

$bearerToken = az account get-access-token --resource api://<API_APP_ID> --query accessToken -o tsv

foreach ($fixture in @(
    @{ File = 'tests/Insights/fixtures/closing-letter-M-2024-0341.txt';   MatterId = 'M-2024-0341' },
    @{ File = 'tests/Insights/fixtures/settlement-agreement-M-2024-0188.txt'; MatterId = 'M-2024-0188' },
    @{ File = 'tests/Insights/fixtures/decision-memo-M-2024-0512.txt';    MatterId = 'M-2024-0512' }
)) {
    # Upload via SPE document endpoint with InsightsIngest opt-in
    # (exact endpoint depends on task 080 deployment surface; e.g. POST /api/documents/upload)
    Invoke-RestMethod -Uri "$baseUrl/api/documents/upload" `
        -Method Post `
        -Headers @{ Authorization = "Bearer $bearerToken"; 'X-Matter-Id' = $fixture.MatterId; 'X-Ai-Insights-Ingest' = 'true' } `
        -InFile $fixture.File `
        -ContentType 'application/octet-stream'
}
```

### Step 3: Wait for ingest completion

Ingest is async (Service Bus → `InsightsIngestJobHandler`). Poll `spaarke-insights-index` for Observation arrival:

```pwsh
$searchEndpoint = 'https://spaarke-search-dev.search.windows.net'
$indexName = 'spaarke-insights-index'
$searchKey = az keyvault secret show --vault-name spaarke-kv-dev --name 'spaarke-search-admin-key' --query value -o tsv

$documentIds = @('doc-id-1', 'doc-id-2', 'doc-id-3')  # from upload responses
foreach ($docId in $documentIds) {
    $maxTries = 30; $delayMs = 5000
    for ($i = 0; $i -lt $maxTries; $i++) {
        $result = Invoke-RestMethod -Uri "$searchEndpoint/indexes/$indexName/docs/`$count?api-version=2024-07-01&`$filter=documentId eq '$docId' and artifactType eq 'observation'" `
            -Headers @{ 'api-key' = $searchKey }
        if ($result -gt 0) {
            Write-Host "✅ Document $docId produced $result Observations"
            break
        }
        Start-Sleep -Milliseconds $delayMs
    }
    if ($result -eq 0) {
        Write-Error "❌ Document $docId did not produce Observations after $($maxTries * $delayMs / 1000)s"
    }
}
```

Acceptance: each fixture should produce ≥ 1 Observation (Layer 1 classification), and outcome-bearing fixtures (closing-letter, settlement-agreement, decision-memo) should produce ≥ 4 Observations (Layer 2 per-field).

### Step 4: Verify mirror to `sprk_analysis`

```pwsh
# Via Dataverse MCP or Web API
$dataverseUrl = 'https://spaarke-dev.crm.dynamics.com'
$dataverseToken = (Get-DataverseToken).access_token

$resp = Invoke-RestMethod -Uri "$dataverseUrl/api/data/v9.2/sprk_analyses?`$filter=sprk_searchprofile eq 'insights-observation@v1' and sprk_documentid_value eq <fixtureDocId>&`$count=true" `
    -Headers @{ Authorization = "Bearer $dataverseToken"; 'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0'; 'Prefer' = 'odata.include-annotations=*' }

Write-Host "Mirror rows for doc: $($resp.'@odata.count')"
```

Acceptance: mirror row count = Observation count (NoOpObservationMirror replaced by `DataverseObservationMirror` per task 051).

### Step 5: Create a fixture Precedent via admin endpoint

```pwsh
$body = @{
    name = 'Phase 1 Smoke — IP-licensing 30-day cure period'
    patternStatement = 'In IP-licensing matters with a 30-day cure period and NA territory, settlement amounts cluster at $150-200K with 12-month resolution timelines.'
    practiceArea = 'ip'
    scope = @{ matterType = 'ip-licensing'; territory = 'north-america' }
    supportingMatterIds = @('M-2024-0341')
} | ConvertTo-Json -Depth 5

$resp = Invoke-RestMethod -Uri "$baseUrl/api/insights/admin/precedents" `
    -Method Post `
    -Headers @{ Authorization = "Bearer $bearerToken"; 'X-Spaarke-Tenant-Id' = $tenantId } `
    -Body $body -ContentType 'application/json'

$precedentId = $resp.id
Write-Host "Created Precedent $precedentId"

# Promote to Confirmed (fires projection sync)
Invoke-RestMethod -Uri "$baseUrl/api/insights/admin/precedents/$precedentId/confirm" `
    -Method Post `
    -Headers @{ Authorization = "Bearer $bearerToken"; 'X-Spaarke-Tenant-Id' = $tenantId }
```

### Step 6: Verify Precedent projection to `spaarke-insights-index`

```pwsh
$maxTries = 30; $delayMs = 2000
for ($i = 0; $i -lt $maxTries; $i++) {
    $result = Invoke-RestMethod -Uri "$searchEndpoint/indexes/$indexName/docs/`$count?api-version=2024-07-01&`$filter=artifactType eq 'precedent' and id eq 'prec:$precedentId`:v1'" `
        -Headers @{ 'api-key' = $searchKey }
    if ($result -eq 1) {
        Write-Host "✅ Precedent projected to spaarke-insights-index"
        break
    }
    Start-Sleep -Milliseconds $delayMs
}
```

### Step 7: POST `/api/insights/ask` — sufficient-evidence path

```pwsh
# Subject contract (see the section above): a real sprk_matterid, the Binding's canonical name, parameters through the
# shared policy. predict-matter-cost writes nothing, so the caller needs Read on the matter.
$fixtureMatterId = '<sprk_matterid of the matter M-2024-0341 is filed under>'

$body = @{
    question = 'predict-matter-cost'      # the insights-ask Binding's sprk_consumercode (prerequisite above)
    subject = "matter:$fixtureMatterId"
    parameters = @{}                      # matterType / lookBackYears are refused (400 playbook.parameter-rejected)
} | ConvertTo-Json

$resp = Invoke-WebRequest -Uri "$baseUrl/api/insights/ask" `
    -Method Post -Headers @{ Authorization = "Bearer $bearerToken" } `
    -Body $body -ContentType 'application/json'

# Negative checks (the route authorization contract) — each must hold:
#   a user WITHOUT Read on the matter        -> 404, reasonCode sdap.access.deny.record_unavailable
#   parameters = @{ lookBackYears = '3' }    -> 400, errorCode playbook.parameter-rejected
#   question = '<a GUID with no insights-ask Binding>' -> 400 "not registered"

$cacheHit = $resp.Headers['X-Insights-Cache']
$elapsed = $resp.Headers['X-Insights-Elapsed-Ms']
$content = $resp.Content | ConvertFrom-Json

Write-Host "Cache: $cacheHit | Elapsed: ${elapsed}ms"
if ($content.artifact) {
    Write-Host "✅ Artifact returned: $($content.artifact.predicate) = $($content.artifact.value.raw)"
    Write-Host "   Evidence refs: $($content.artifact.evidence.Count)"
    Write-Host "   Confidence: $($content.artifact.confidence)"
} elseif ($content.decline) {
    Write-Host "⚠️ Decline returned: $($content.decline.reason) — $($content.decline.explanation)"
}
```

Acceptance:
- Response is 200 OK
- Either `artifact` populated (InferenceArtifact with `predicate=predictedCost`, ≥ 12 evidence refs, confidence ∈ [0, 1], displayHint=currency-usd, producedBy.version set) OR
- `decline` populated with `reason=insufficient-evidence` + `minimumEvidenceNeeded.comparableMatters` populated
- `X-Insights-Cache: false` on first call; `X-Insights-Elapsed-Ms` populated

### Step 8: POST `/api/insights/ask` — repeat for cache hit

Re-run Step 7 with identical body. Acceptance: `X-Insights-Cache: true` on the second call (D-P13 cache wraps `ExecuteBatchAsync`).

### Step 9: POST `/api/insights/ask` — insufficient-evidence path

Use a REAL test matter with no comparable cohort (a fictional id is not a record: it gets the uniform 404, not a
decline):

```pwsh
$unicornMatterId = '<sprk_matterid of a TEST matter of a novel type, e.g. "novel-quantum-licensing">'
$body = @{
    question = 'predict-matter-cost'
    subject = "matter:$unicornMatterId"   # no cohort exists for its matter type
    parameters = @{}
} | ConvertTo-Json
# ... POST as Step 7
```

Acceptance: `decline.reason = 'insufficient-evidence'` + `minimumEvidenceNeeded.comparableMatters.need = 12` + `confidenceInDecline > 0.85`.

### Step 10: Inject one synthetic bad citation + verify GroundingVerifier strip

This step requires temporary fixture manipulation:
1. Manually upload a 4th fixture document whose Layer 2 extraction is known to emit a quote that doesn't appear in the source chunks (e.g., a closing letter with the extracted quote sabotaged before persistence — only feasible by patching one of the fixture files with a quote-bearing field whose exact text is then deleted from the body before re-upload).
2. Verify the resulting `sprk_analysis` row for that field shows `sprk_disposition = NULL` (Observation was suppressed at extraction time by GroundingVerifier) OR the Observation in `spaarke-insights-index` is missing for that specific field.

Alternative (preferred — easier): rely on the unit test `tests/.../Services/Ai/CitationVerification/GroundingVerifierTests.cs` (task 030) for synthetic-bad-citation verification. The runbook only verifies the wire contract — that no document evidence ref leaks without a Quote field — which the in-process smoke (`Smoke_PredictMatterCost_EvidenceMatchesGroundedSet`) already proves at the wire layer.

### Step 11: Cleanup

```pwsh
# Delete fixture Precedents
Invoke-RestMethod -Uri "$dataverseUrl/api/data/v9.2/sprk_precedents($precedentId)" `
    -Method Delete -Headers @{ Authorization = "Bearer $dataverseToken" }

# Delete fixture documents from SPE (use existing delete endpoint)
foreach ($docId in $documentIds) {
    Invoke-RestMethod -Uri "$baseUrl/api/documents/$docId" `
        -Method Delete -Headers @{ Authorization = "Bearer $bearerToken" }
}

# Optional: delete Observations from spaarke-insights-index (re-runs of the runbook are idempotent at the index level; cleanup is for hygiene)
$obsIds = (Invoke-RestMethod -Uri "$searchEndpoint/indexes/$indexName/docs/search?api-version=2024-07-01" `
    -Method Post `
    -Headers @{ 'api-key' = $searchKey; 'Content-Type' = 'application/json' } `
    -Body (@{ search = '*'; filter = "documentId in ('$($documentIds -join "','")')"; select = 'id' } | ConvertTo-Json)).value.id
$deleteBody = @{ value = $obsIds | ForEach-Object { @{ '@search.action' = 'delete'; id = $_ } } } | ConvertTo-Json
Invoke-RestMethod -Uri "$searchEndpoint/indexes/$indexName/docs/index?api-version=2024-07-01" `
    -Method Post `
    -Headers @{ 'api-key' = $searchKey } `
    -Body $deleteBody -ContentType 'application/json'

# Disable Insights ingest if you enabled it in Step 1
```

---

## SPEC §5.1 acceptance criteria — live verification

The in-process artifact A verifies the wire contract; this runbook verifies the live pipeline. Both together attest to SPEC §5.1 acceptance:

| SPEC §5.1 criterion | Verified by | Status |
|---|---|---|
| Bicep deploys cleanly to Spaarke Dev | task 010 + task 080 deploy | LIVE-RUNBOOK |
| spaarke-insights-index provisioned correctly | This runbook Step 3 + 6 (index queries return shape per SPEC §3.4) | LIVE-RUNBOOK |
| sprk_precedent entity queryable | This runbook Step 5 (admin endpoint succeeds) | LIVE-RUNBOOK |
| sprk_analysis polymorphic source-type | This runbook Step 4 (mirror rows exist with sprk_searchprofile='insights-observation@v1' per task 051) | LIVE-RUNBOOK |
| 4-tier envelope round-trips | In-process `Smoke_InferenceArtifact_RoundTripsThroughEnvelope` | PR-CI ✅ |
| E2E ingest smoke (Layer 1 + Layer 2 + GroundingVerifier + Observations) | This runbook Steps 2-4 + in-process IngestOrchestratorTests subset | LIVE-RUNBOOK |
| E2E Precedent smoke (admin create → projection) | This runbook Steps 5-6 + in-process `PrecedentProjectionSyncTests` | LIVE-RUNBOOK |
| E2E synthesis smoke (predict-matter-cost) | This runbook Steps 7-9 + in-process Phase1SmokeTest | LIVE-RUNBOOK + PR-CI ✅ |
| GroundingVerifier strips bad citations | In-process `GroundingVerifierTests` (task 030) — sufficient | PR-CI ✅ |
| DeclineToFindNode produces structured Decline | In-process `PredictMatterCostPlaybookTests` (task 060) + `Smoke_PredictMatterCost_InsufficientEvidence_ReturnsDecline` | PR-CI ✅ |
| Observation review surface (Dataverse view) | task 052 deploy + manual reviewer login | LIVE-RUNBOOK (no automation needed — UI workflow) |
| Prompt versioning (producedBy version field) | In-process `Smoke_PredictMatterCost_ReturnsArtifact` (Version assertion) + this runbook Step 7 (verify in response) | PR-CI ✅ |
| Cache hit/miss telemetry | In-process `Smoke_FacadeReportsCacheHit_HeaderSurfacesTrue` + this runbook Step 8 (re-call verifies header flip) | PR-CI ✅ + LIVE-RUNBOOK |
| Eval harness baseline run | In-process `EvalHarness_BaselineRun_AllThresholdsMet` (mocked facade with 15 tuples) | PR-CI ✅ |
| §3.5 facade boundary grep | Insights-eval workflow + this file's grep step | PR-CI ✅ |
| Zero new SAS keys / ClientSecretCredential | adr-check skill on every PR | PR-CI ✅ (skill gate) |

**PR-CI = verified by Artifact A on every PR.**
**LIVE-RUNBOOK = verified by Artifact B execution during task 080 deploy.**

---

## Failure modes + rollback

| Failure | Likely cause | Recovery |
|---|---|---|
| Step 3 timeout (no Observations after 5 min) | InsightsIngest opt-in flag not set OR Service Bus queue blocked OR Layer 1 model deployment missing | Check App Service log streams for `InsightsIngestJobHandler` events; verify `text-embedding-3-large` deployment per EXT-2 |
| Step 6 timeout (no Precedent projection) | Confirm endpoint succeeded but projection sync failed silently | Check App Service logs for `PrecedentProjectionSync` errors; verify `IInsightsAi.EmbedTextAsync` returns vector |
| Step 7 returns 500 | Playbook resolution failed OR LLM error OR cache stampede | Check ProblemDetails body for `INSIGHTS_FACADE_EMPTY_RESULT` vs `INSIGHTS_INTERNAL_ERROR`; check synthesis prompt versioning |
| Step 7 returns 401 | Bearer token missing `tid` claim | Re-acquire token; verify Azure AD app config |
| Step 7 returns 404 (`sdap.access.deny.record_unavailable`) | The subject is not a real `sprk_matterid`, the token's user cannot Read it, or (matter-health-single) cannot Write it | Use the matter's GUID; use a user with the right on it (the same answer is given for all three on purpose) |
| Step 7 returns 400 `playbook.parameter-rejected` | A parameter outside the shared policy (`matterType`, `lookBackYears`, `tenantId`, …) | Send `parameters = @{}` |
| Step 7 returns 400 "not registered" | No enabled `insights-ask` Binding row for `predict-matter-cost` (or a raw GUID it does not target) | Run the Binding prerequisite (dry run, apply, verify) |
| Step 7 returns 429 | Rate limit (60/min/oid per ai-context policy from task 061) | Wait 60s; reduce smoke iteration cadence |
| Step 8 doesn't return cache hit | D-P13 cache invalidation triggered by token rotation OR `AccessibleScopeHash` differs | Re-call within 5 min with same token + body |

**Rollback** for any unrecoverable failure: revert `appsettings.Development.json` `AiProcessingOptions.InsightsIngest` to `false` and re-deploy via `Deploy-BffApi.ps1` (task 080's deploy script).
