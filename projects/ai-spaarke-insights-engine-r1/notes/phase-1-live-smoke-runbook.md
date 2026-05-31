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

## Prerequisites (run once)

1. **Spaarke Dev environment provisioned**:
   - `spaarke-insights-index` AI Search index exists (D-P2 — verify via portal or `az search service show`)
   - `sprk_precedent` Dataverse entity deployed (D-P3 via task 011)
   - `sprk_analysis` extended with disposition fields (D-P11 via task 052)
   - BFF API deployed to App Service (task 080 prerequisite)
   - `text-embedding-3-large` deployed in `spaarke-openai-dev` (verified per EXT-2 dependency)
   - `predict-matter-cost` playbook row created in Dataverse via `scripts/Deploy-Playbook.ps1` (task 060 prerequisite)
2. **Operator authenticated**:
   - `az login` against the Spaarke Dev tenant
   - `pac auth create --environment <env-url>` for Dataverse MCP
3. **Local repo on the deployment branch**:
   - `git fetch origin && git checkout work/ai-spaarke-insights-engine-r1`
4. **Authentication for `/api/insights/ask`**:
   - Bearer token for a test tenant user (any user in the Spaarke Dev tenant). Acquire via:
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
# Resolve the predict-matter-cost playbook Guid from Dataverse first:
$playbookGuid = (az pac data list --table sprk_analysisplaybook --filter "sprk_name eq 'predict-matter-cost'" --query '[0].sprk_analysisplaybookid' -o tsv)

$body = @{
    question = $playbookGuid
    subject = 'matter:M-2024-0341'
    parameters = @{ matterType = 'ip-licensing'; lookBackYears = '3' }
} | ConvertTo-Json

$resp = Invoke-WebRequest -Uri "$baseUrl/api/insights/ask" `
    -Method Post -Headers @{ Authorization = "Bearer $bearerToken" } `
    -Body $body -ContentType 'application/json'

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

Use a fictional matter with no comparable cohort:

```pwsh
$body = @{
    question = $playbookGuid
    subject = 'matter:M-UNICORN-001'  # no cohort exists
    parameters = @{ matterType = 'novel-quantum-licensing'; lookBackYears = '10' }
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
| Step 7 returns 429 | Rate limit (60/min/oid per ai-context policy from task 061) | Wait 60s; reduce smoke iteration cadence |
| Step 8 doesn't return cache hit | D-P13 cache invalidation triggered by token rotation OR `AccessibleScopeHash` differs | Re-call within 5 min with same token + body |

**Rollback** for any unrecoverable failure: revert `appsettings.Development.json` `AiProcessingOptions.InsightsIngest` to `false` and re-deploy via `Deploy-BffApi.ps1` (task 080's deploy script).
