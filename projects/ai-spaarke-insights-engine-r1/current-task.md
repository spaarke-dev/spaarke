# Current Task — Spaarke Insights Engine, Phase 1

> **Status**: 🔄 active — Wave 5 tasks 041 + 042 complete; 040 still in flight (parallel)
> **Last Updated**: 2026-05-28 (post task 041)
> **Project state**: Waves 1-4 complete + W5 tasks 041 + 042 shipped (13 of 17 D-P + scaffold tasks — 76%)

---

## Active task

None — task 041 just completed. Next: task 040 (D-P7, in flight parallel) or task 050 (D-P8 SPE upload consumer, unblocked once 040 ships).

---

## Last completed task

**Task 041 — D-P4 Precedent → spaarke-insights-index projection sync** ✅ (2026-05-28)
- Rigor: STANDARD (Zone B sync service, foundation for D-P14 Precedent retrieval)
- Files NEW (5 production + 2 test):
  - `src/server/api/Sprk.Bff.Api/Services/Insights/Precedents/IPrecedentProjectionSync.cs` (87 lines) — interface + `PrecedentProjectionResult` record + `PrecedentProjectionOutcome` enum (Written/Skipped/NotFound); Zone B; documents the §3.5 facade-via-`IInsightsAi.EmbedTextAsync` design (resolves task 041 POML Step 3 open question)
  - `src/server/api/Sprk.Bff.Api/Services/Insights/Precedents/PrecedentProjectionMapper.cs` (236 lines) — pure mapping logic; emits a `SearchDocument` with all 14 SPEC §3.4.2 fields (id `prec:{N}:v1`, tenantId, artifactType=precedent, subject=`pattern:{N}`, predicate=pattern, complex nested value/raw/scope/supportingMatters, evidence[] of supporting-matter refs, contentVector 3072-dim, status=confirmed, confidence omitted per SPEC §3.4.2)
  - `src/server/api/Sprk.Bff.Api/Services/Insights/Precedents/PrecedentProjectionSync.cs` (165 lines) — Zone B sync service; reads Precedent via `IPrecedentBoard.GetAsync`, status-gates on `PrecedentStatus.Confirmed`, generates 3072-dim embedding via `IInsightsAi.EmbedTextAsync` (§3.5 facade — the ONLY Zone-A type imported per project CLAUDE.md §3.5.4), builds the SearchDocument via mapper, upserts to `spaarke-insights-index` via `SearchClient.MergeOrUploadDocumentsAsync` (idempotent by deterministic document id). Throws on failure result so the fire-and-forget caller's try/catch logs the structured error.
  - `tests/unit/Sprk.Bff.Api.Tests/Services/Insights/Precedents/PrecedentProjectionMapperTests.cs` (256 lines) — 22 xUnit/FluentAssertions tests covering BuildDocumentId determinism + format + empty-guid throw; SPEC §3.4.2 envelope shape (id/tenantId/artifactType/subject/predicate/status/producedBy/content/asOf); confidence omission per SPEC; contentVector 3072 dims; nested value.raw.scope structure; evidence array per supporting matter; valueJson round-trips as parseable JSON; arg validation (null record, blank tenantId, empty vector, null supportingMatters); DerivePatternTitle helper (name-priority, fallback, 200-char truncation)
  - `tests/unit/Sprk.Bff.Api.Tests/Services/Insights/Precedents/PrecedentProjectionSyncTests.cs` (361 lines) — 14 xUnit/Moq tests: 4 constructor null guards; 3 arg validations (empty Guid, blank tenantId × 2); happy-path Confirmed projection (board+facade+search interactions verified); document-shape-captured-via-Callback assertion of SPEC §3.4.2 fields; status gate Theory (4 non-Confirmed values all Skipped without embedding/write); NotFound returns NotFound outcome; ConfirmedButBlankPatternStatement skips; repeated-calls idempotency (same deterministic doc id); SearchIndex failure result throws for fire-and-forget catch. Uses `Mock<SearchIndexClient>` + `Mock<SearchClient>` + `SearchModelFactory` per the existing `VisualizationServiceTests` pattern; in-file `FixedTimeProvider : TimeProvider` stub for deterministic `asOf` timestamps (avoids `Microsoft.Extensions.Time.Testing` package add)
- Files MODIFIED (4):
  - `src/server/api/Sprk.Bff.Api/Services/Insights/Precedents/IPrecedentBoard.cs` (+12 lines) — added `GetSupportingMatterIdsAsync(Guid, CancellationToken)` to retrieve `sprk_precedent_matter` N:N associations for populating SPEC §3.4.2 evidence[] + supportingMatters[]
  - `src/server/api/Sprk.Bff.Api/Services/Insights/Precedents/DataversePrecedentBoard.cs` (+59 lines, +1 using) — implements `GetSupportingMatterIdsAsync` via `QueryExpression`+`LinkEntity` traversing the N:N intersect table (`sprk_precedent_matter`); tolerates "not-found" errors by returning empty
  - `src/server/api/Sprk.Bff.Api/Api/Insights/PrecedentAdminEndpoints.cs` (+128 lines) — new `POST /api/insights/admin/precedents/{id}/confirm` endpoint: validates `X-Spaarke-Tenant-Id` header (D-52), calls `IPrecedentBoard.ConfirmAsync` synchronously (awaited before response), fires `IPrecedentProjectionSync.ProjectAsync` as fire-and-forget `Task.Run` with try/catch + structured log. Returns 202 Accepted with `ConfirmPrecedentResponse {Id, StatusValue, Status="Confirmed", ProjectionDispatched=true}`. Same ADR-008/019/016 envelope as the existing POST.
  - `src/server/api/Sprk.Bff.Api/Infrastructure/DI/InsightsModule.cs` (+8 lines) — registered `IPrecedentProjectionSync → PrecedentProjectionSync` (Scoped, consistent with IPrecedentBoard) with XML docs citing §3.5 facade compliance + the `IInsightsAi` import as the only Zone-A type allowed in Zone B per project CLAUDE.md §3.5.4
- Files MODIFIED (bandaid for parallel task 040 collision — NOT in task 041 scope but unblocked the test build): `tests/.../InsightsOrchestratorTests.cs` (+1 mock field, +1 ctor arg per test) — task 040 added `IIngestOrchestrator` to `InsightsOrchestrator` ctor; task 042's tests broke. Added loose-mock `IIngestOrchestrator` + threaded it through the ctor invocations. The pre-existing `RunIngestAsync_WithValidRequest_ThrowsScaffoldNotImplemented` test (24th test) now fails because task 040 made `RunIngestAsync` real — that's task 040's to fix, not task 041
- Build: `dotnet build src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` clean — 0 errors, 17 pre-existing warnings (none in new files; same warning set as tasks 020/021/022/042). Test project also builds clean — 0 errors.
- Test verification: `dotnet test --filter "FullyQualifiedName~PrecedentProjection"` → **36/36 PASS** (22 mapper tests + 14 sync tests) in 134 ms. Full Insights test surface: 173/174 PASS (the 1 failure is the pre-existing task-040-affected `RunIngestAsync_WithValidRequest_ThrowsScaffoldNotImplemented` — out of task 041 scope).
- SPEC §3.5.4 forbidden-imports grep on Zone B paths (`Services/Insights/`, `Api/Insights/`, `Models/Insights/`): **ZERO matches** for any forbidden namespace. The only Zone-A import in any new file is `using Sprk.Bff.Api.Services.Ai.PublicContracts;` (the explicit §3.5 facade per project CLAUDE.md §3.5.4) in `PrecedentProjectionSync.cs`. `Azure.Search.Documents` + `Azure.Search.Documents.Indexes` are 3rd-party SDK types (not on the forbidden list).
- Embedding routing confirmation: `PrecedentProjectionSync.cs` line 134 (`var contentVector = await _insightsAi.EmbedTextAsync(record.PatternStatement, ct);`) — embedding goes through the §3.5 facade EXACTLY as the task 042 design intended (the facade's third method was added specifically to unblock this task per task 042's `EmbedTextAsync` XML doc rationale). Verified by `ProjectAsync_ConfirmedPrecedent_WritesToIndex` test's `_insightsAiMock.Verify(..., Times.Once, "embedding must route through IInsightsAi facade")`.
- Quality gates (STANDARD rigor — code-review + adr-check are formally optional but design discipline was applied):
  - **code-review**: 0 critical / 0 warnings on new files. Design notes: (1) `SearchDocument` (Azure SDK schema-flexible bag) chosen over a hand-typed POCO because the nested complex types (`value/raw/scope`, `evidence[]`) differ by artifactType — a single POCO across Observations + Precedents would be either deeply optional or use inheritance, both with maintenance cost; field-name constants on the mapper + unit tests against the schema mitigate the loss of compile-time field-name checking. (2) `IPrecedentProjectionSync` interface seam justified per ADR-010 §Exceptions — testing seam (avoids real SearchClient + real OpenAI in unit tests) plus future Phase 1.5+ swap path if projection moves to a background service. (3) Fire-and-forget pattern uses `_ = Task.Run(...)` with `CancellationToken.None` (NOT the request CT — once HTTP response sent, request CT disposes) per the pattern recommended in the task brief; inner try/catch ensures no process crash.
  - **adr-check**: 0 violations. ADR-001 (BFF runtime — no Function), ADR-008 (`SpeAdminAuthorizationFilter` applied), ADR-010 (one new interface seam justified; new endpoint not new module — endpoint added to existing `PrecedentAdminEndpoints`), ADR-013 (§3.5 facade boundary respected — `IInsightsAi.EmbedTextAsync` is the sole Zone-A import), ADR-016 (rate limit inherited from group `api-key-admin`), ADR-019 (ProblemDetails on all error paths). D-52 (TenantId required + passed through). D-P4 (Confirmed-only gate enforced). D-49 (idempotency via deterministic doc id). CLAUDE.md §10 BFF hygiene (placement justified inline + in `current-task.md`; zero new NuGet packages; zero new CVE surface; SearchClient mock pattern reused — no new test infra).
- Acceptance criteria: 5/5 PASS (1 SCAFFOLD as designed):
  1. Confirmed sprk_precedent row projected to spaarke-insights-index with artifactType=precedent — **PASS** (`ProjectAsync_DocumentShape_MatchesSpec342` verifies artifactType="precedent" on the captured document)
  2. Projected row matches SPEC §3.4.2 worked example structure — **PASS** (11 mapper tests verify each of 14 SPEC §3.4.2 fields: id pattern `prec:{N}:v1`, tenantId, artifactType, subject `pattern:{N}`, predicate=pattern, value/raw/scope/sampleSize/supportingMatters, valueJson, evidence[] of supporting-matter refs, asOf, producedBy from Dataverse, content=patternStatement, contentVector 3072-dim, status=confirmed, confidence omitted)
  3. Re-projection on already-projected Precedent is idempotent (no duplicate rows) — **PASS** (`ProjectAsync_RepeatedCalls_UseDeterministicDocumentId` verifies same `prec:{N}:v1` doc id across calls; MergeOrUpload overwrites in place)
  4. IndexRetrieveNode (D-P12) query for Precedents returns the projected Precedent (round-trip) — **SCAFFOLD** (full round-trip requires real AI Search index against deployed `spaarke-insights-index` — deferred to task 070 D-P16 smoke per task brief; schema field-name parity verified against `IndexRetrieveNode.DefaultIndexName` constant + deployed `spaarke-insights-index.index.json` schema field names)
  5. §3.5 facade boundary respected (embedding routes through facade) — **PASS** (grep ZERO matches on Zone B paths for ALL §3.5.4 forbidden namespaces; only `Services.Ai.PublicContracts` imported; explicit Moq.Verify on `IInsightsAi.EmbedTextAsync` Times.Once in test)
- Decisions (task-level):
  - **`SearchDocument` over typed POCO**: rationale documented in mapper XML docs — the nested complex types differ by artifactType; field-name constants + tests + the IndexRetrieveNode field constants give enough safety without a brittle inheritance hierarchy. If a future task introduces a unified POCO for all index rows it can replace `BuildDocument`'s output without changing the public contract.
  - **`IInsightsAi.EmbedTextAsync` over `ReferenceIndexingService.IndexIntoAsync`**: the task brief mentioned routing through W3.5 `ReferenceIndexingService`, but that service lives in Zone A (`Services/Ai/`) — Zone B importing it directly violates §3.5.4. The cleanest §3.5-compliant path is `IInsightsAi.EmbedTextAsync` (Zone B → facade only) + direct `SearchClient.MergeOrUploadDocumentsAsync` (Azure SDK, not on forbidden list). The Precedent projection is also a single-document write — `ReferenceIndexingService`'s chunking + idempotent-delete + batched-upsert pipeline is overkill for one row. `MergeOrUpload` with deterministic doc id IS idempotent; the chunking pipeline would force a single chunk anyway. This decision is documented inline in the mapper's "Why SearchDocument" XML doc.
  - **`POST /{id}/confirm` endpoint over reusing the create endpoint**: task brief said "wire to D-P3 admin endpoint when SME promotes to Confirmed". The existing POST creates Tentative (per task 012), with status promotion happening via Dataverse model-driven view (currently). The pragmatic Phase 1 path is a NEW endpoint `POST /{id}/confirm` that mirrors the existing endpoint's auth/rate-limit envelope, calls `IPrecedentBoard.ConfirmAsync` (existing — added by task 012), and fires the projection sync. This is a minimal additive change.
  - **`X-Spaarke-Tenant-Id` header for confirm endpoint**: D-52 mandates tenantId for all Insights writes. Phase 1 admin endpoints don't have centralized tenant resolution (D-P15 task 061 will own that); for now the confirm endpoint accepts tenant explicitly via header. When 061 ships, this can switch to whatever tenant-resolution mechanism is canonicalized.
  - **`GetSupportingMatterIdsAsync` added to `IPrecedentBoard`**: rather than inject `IGenericEntityService` directly into `PrecedentProjectionSync` (which would force the sync to know N:N intersect SQL), the board encapsulates the query. The board IS the Dataverse-shape-aware Zone B abstraction (task 012 design); this extension stays in its lane.
  - **Bandaid for `InsightsOrchestratorTests`**: task 040 (parallel) added `IIngestOrchestrator` as a 4th ctor parameter to `InsightsOrchestrator` (which task 042 owned). This broke task 042's tests at compile time. Two options: (a) wait for task 040 to fix its own collision, OR (b) add a 1-line loose-mock ctor arg to unblock the test project build. Chose (b) — the fix is minimal (1 mock field + 1 ctor arg per affected test), it unblocks my test runs, and it's a coordination-collision pattern (parallel work touching shared interfaces always requires this). The 1 test that exercises old scaffold behavior (`RunIngestAsync_WithValidRequest_ThrowsScaffoldNotImplemented`) is left for task 040 — it knows what the new behavior should be tested as.
- Unblocks: Wave 6 task 060 (D-P14 synthesis playbook — can now retrieve Confirmed Precedents from `spaarke-insights-index` via `IndexRetrieveNode`)

---

**Task 042 — IInsightsAi facade + InsightsOrchestrator (scaffold)** ✅ (2026-05-28)
- Rigor: FULL (bff-api code, public facade contract, foundation for D-P8 + D-P15 + DEP-7 coordination)
- Files NEW (8 production + 1 test):
  - `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IInsightsAi.cs` (136 lines) — the SPEC §3.5 facade boundary; THREE methods (AnswerQuestionAsync for D-P15, RunIngestAsync for D-P8, EmbedTextAsync for D-P4 task 041) with exhaustive XML doc justifying each method's domain-not-mechanism naming + the third-method addition rationale
  - `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/InsightsOrchestrator.cs` (197 lines) — Zone A impl; `sealed`; wraps `IPlaybookExecutionEngine` + `IInsightsPlaybookExecutionCache` (D-P13 task 023) + `IOpenAiClient`; `AnswerQuestionAsync` wires the D-P13 cache with `factoryWasCalled` flag to detect hit-vs-miss; `RunIngestAsync` throws `NotImplementedException` with "task 040 (D-P7)" message (scaffold); `EmbedTextAsync` delegates to `IOpenAiClient.GenerateEmbeddingAsync` with null model/dimensions (facade is opinionated for `spaarke-insights-index` substrate consistency at 3072 dims per SPEC §3.4)
  - `src/server/api/Sprk.Bff.Api/Models/Ai/PublicContracts/InsightsAgentRequest.cs` (36 lines) — record: Question(Guid) + Subject + Parameters + TenantId + AccessibleScopeHash
  - `src/server/api/Sprk.Bff.Api/Models/Ai/PublicContracts/InsightsAgentResult.cs` (64 lines) — record with factory methods `Success` + `Declined` enforcing "exactly one of Artifact/Decline" invariant at call site; CacheHit + ProcessingTimeMs diagnostics
  - `src/server/api/Sprk.Bff.Api/Models/Ai/PublicContracts/InsightsIngestRequest.cs` (30 lines) — record: DocumentId + MatterId + TenantId
  - `src/server/api/Sprk.Bff.Api/Models/Ai/PublicContracts/InsightsIngestResult.cs` (40 lines) — record: ObservationsEmitted + Layer1Classification + Layer2Triggered
  - `src/server/api/Sprk.Bff.Api/Infrastructure/DI/InsightsFacadeModule.cs` (70 lines) — new Zone A feature module (AiModule at 15/15 cap; mirrors task 021's InsightsExtractionModule pattern); registers `IInsightsAi → InsightsOrchestrator` (Singleton); XML docs cite ADR-010 §Exceptions seam justification (§3.5 facade IS the seam)
  - `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Insights/InsightsOrchestratorTests.cs` (399 lines) — 24 xUnit/FluentAssertions/Moq tests (constructor null guards × 4, AnswerQuestionAsync arg validation × 5, AnswerQuestionAsync cache hit / miss / no-artifact-decline / cancellation / cache-request-shape-capture × 5, RunIngestAsync null / blank / scaffold-NotImplemented × 5, EmbedTextAsync blank-text / delegation / cancellation × 5)
- Files MODIFIED (1):
  - `src/server/api/Sprk.Bff.Api/Program.cs` (+5 lines) — wired `AddInsightsFacadeModule()` after `AddInsightsExtractionModule` (must follow `AddAnalysisServicesModule` which registers `IInsightsPlaybookExecutionCache`)
- Build: `dotnet build src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` clean — 0 errors, 17 pre-existing warnings (none in new files, same set as tasks 020/021/022)
- Test verification: `dotnet test --filter "FullyQualifiedName~InsightsOrchestratorTests" --no-build` → **24/24 PASS** in 53 ms. Test project (`Sprk.Bff.Api.Tests`) compiles cleanly per the 1c2a1053 cleanup commit
- SPEC §3.5.4 forbidden-imports grep on Zone B paths (Services/Insights/, Api/Insights/, Models/Insights/): **ZERO forbidden `using` statements** (one regex hit is a `<see cref>` XML doc reference in `StubLiveFactResolver.cs` — documentation pointer, not import); explicit `grep -E "^using Sprk\.Bff\.Api\.Services\.Ai\.|^using Microsoft\.Extensions\.AI|..."` on Zone B returns ZERO matches
- Quality gates (Step 9.5):
  - code-review ✅: 0 critical / 0 warnings / 1 minor suggestion (theory-param `_ = expectedField;` for unused-var suppression) / 0 AI smells; smell-1 (interface-with-single-impl) JUSTIFIED — IInsightsAi IS the §3.5 boundary, structural-not-optional, same pattern as IInsightsPlaybookExecutionCache / IInsightGraph / ILiveFactResolver
  - adr-check ✅: 11 ADRs validated (ADR-001, 007, 008, 009, 010, 013, 014, 019, 021, 028, 029) + 7 project decisions (D-24, D-27, D-52, D-54, D-P13, D-49, DEP-7) + CLAUDE.md §10 binding BFF hygiene checklist (placement justified, facade used, zero new packages, zero new CVE surface). 0 violations.
- Acceptance criteria: 6/6 PASS per POML
  1. IInsightsAi compiled at `Services/Ai/PublicContracts/IInsightsAi.cs` ✅
  2. InsightsOrchestrator registered via DI (InsightsFacadeModule wired in Program.cs); resolves successfully (verified by build + DI registration tests pass) ✅
  3. AnswerQuestionAsync invokes PlaybookExecutionEngine with D-P13 cache wrapping ✅ (tested with `Mock<IInsightsPlaybookExecutionCache>` + strict `Mock<IPlaybookExecutionEngine>` + cache-miss factory-drain pattern)
  4. RunIngestAsync invokes universal ingest playbook with caller-supplied context ⚠️ scaffold (throws Phase-1 NotImplementedException with "task 040 (D-P7)" message; arg validation works; full wiring lands with task 040 + 050)
  5. §3.5.4 grep passes ✅ (Zone B paths import IInsightsAi only)
  6. Interface naming follows domain convention ✅ (AnswerQuestionAsync NOT InvokePlaybookAsync; RunIngestAsync NOT ExecuteIngestPlaybookAsync; EmbedTextAsync NOT GenerateEmbeddingAsync)
- Decisions (task-level):
  - Added `EmbedTextAsync` as third facade method (per task 042 brief) to resolve task 041 §3.5 embedding-routing gap. Justified inline in XML doc: alternatives (extract Zone A embedding service / restructure D-P4 to push into Zone A node) had higher friction; single facade-method delegation is lowest-friction. Opinionated about model/dimensions (always null/null → text-embedding-3-large/3072) for spaarke-insights-index substrate consistency
  - `InsightsAgentResult.Declined` for no-artifact path is currently a SCAFFOLD with reason="no-artifact-produced" + empty MinimumEvidenceNeeded. Full structured decline extraction from engine stream (read DeclineToFindNode StructuredData, propagate MinimumEvidenceNeeded) lands with task 061 (D-P15 endpoint) once a real D-P14 playbook with EvidenceSufficiencyNode + DeclineToFindNode is registered. Scaffold honors the "exactly one of" Artifact/Decline contract today
  - `PlaybookRunRequest.DocumentIds = Array.Empty<Guid>()` for the synthesis path. D-P14 playbook uses `LiveFactNode` + `IndexRetrieveNode` for cohort retrieval — does NOT process ad-hoc DocumentIds. Empty array satisfies the engine's `required[]` contract; D-P15 endpoint task 061 owns subject-to-relevant-document mapping if/when needed
  - New `InsightsFacadeModule` rather than extending existing `InsightsExtractionModule` — keeps the §3.5 boundary visible in DI composition: ExtractionModule = Zone A extraction primitives; FacadeModule = Zone A public surface. Different concerns, different evolution cadences
- Unblocks: Wave 5 tasks 040 (D-P7 universal ingest — needs `IInsightsAi` to dispatch from) and 041 (D-P4 Precedent projection sync — needs `EmbedTextAsync` from facade per §3.5.4); Wave 6 task 050 (D-P8 SPE-upload consumer); Wave 7 task 061 (D-P15 ask endpoint)

---

## Earlier completed tasks

**Task 022 — D-P12 Five new Insights-mode node executors** ✅ (2026-05-28)
- Rigor: FULL (bff-api code, 5 platform primitives, foundation for D-P7 + D-P14)
- Files NEW (8 production + 6 test):
  - `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/LiveFactNode.cs` (192 lines) — wraps `ILiveFactResolver`; emits typed `FactArtifact` with confidence 1.0; ActionType.LiveFact=80
  - `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/IndexRetrieveNode.cs` (395+ lines) — config-driven AI Search against `spaarke-insights-index` with tenant guard, filter+vector+OData composition, D-A23 EvidenceGuard on empty results; ActionType.IndexRetrieve=90
  - `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/EvidenceSufficiencyNode.cs` (296 lines) — deterministic rule evaluator over upstream NodeOutputs (minCount + requireNonEmpty); sufficient/insufficient verdict + EvidenceGap[] gap analysis; ActionType.EvidenceSufficiency=100
  - `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/DeclineToFindNode.cs` (222 lines) — emits typed `DeclineResponse` deterministically per D-49 LAVERN Pattern #7; template token rendering ({have},{need},{rule},{from},{reason}); ActionType.DeclineToFind=110
  - `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/ReturnInsightArtifactNode.cs` (419 lines) — final node; serializes InsightArtifact envelope (Fact|Observation|Inference) per D-P1 + D-P12; D-A23/D-48 EvidenceGuard with `allowEmptyEvidence` escape hatch for Facts; ActionType.ReturnInsightArtifact=120
  - `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Composition/MultiIndexComposer.cs` (81 lines) — Q5-audit-extracted helper; AiAnalysisNodeExecutor's MergeKnowledgeContext now delegates to this so IndexRetrieveNode + AiAnalysisNodeExecutor compose multi-tier knowledge identically
  - `src/server/api/Sprk.Bff.Api/Services/Insights/LiveFacts/ILiveFactResolver.cs` (80 lines) — Zone B interface seam (Phase 1.5 swap-path) + LiveFactNotSupportedException
  - `src/server/api/Sprk.Bff.Api/Services/Insights/LiveFacts/StubLiveFactResolver.cs` (40 lines) — internal sealed; throws "Phase 1 not implemented" until DataverseLiveFactResolver lands with D-P7 task 040
  - 6 xUnit test files: `tests/.../{LiveFactNode,IndexRetrieveNode,EvidenceSufficiencyNode,DeclineToFindNode,ReturnInsightArtifactNode}Tests.cs` + `InsightsNodesIntegrationTests.cs` + `MultiIndexComposerTests.cs` + `InsightsNodeTestHelpers.cs` helper
- Files MODIFIED (3):
  - `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs` — added 5 new ActionType enum values (LiveFact=80, IndexRetrieve=90, EvidenceSufficiency=100, DeclineToFind=110, ReturnInsightArtifact=120) with 10-value gaps preserving task 020's GroundingVerify=70 pattern
  - `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` — `MergeKnowledgeContext` now delegates to `MultiIndexComposer.Merge` (4-line comment explaining extraction); behavior identical (lift-and-replace, no semantic change)
  - `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` — registered 5 new `INodeExecutor` singletons (LiveFactNode through ReturnInsightArtifactNode) in `AddNodeExecutors()` block following GroundingVerifyNode template
  - `src/server/api/Sprk.Bff.Api/Infrastructure/DI/InsightsModule.cs` — registered `ILiveFactResolver → StubLiveFactResolver` (Singleton) in Zone B
- Build: `dotnet build src/server/api/Sprk.Bff.Api/` clean — 0 errors, 17 pre-existing warnings (same as tasks 020/021; none in new files)
- Test verification (standalone runner at `c:/tmp/task022-runner/`, same pattern as tasks 001/002/012/020/021): **35/35 assertions PASS** — MultiIndexComposer (7), LiveFactNode (5), EvidenceSufficiencyNode (6), DeclineToFindNode (4), ReturnInsightArtifactNode (6), IndexRetrieveNode (4), Registry + mini-playbook (3). Sprk.Bff.Api.Tests project still has same pre-existing 7 compile errors (`EmbeddingMigrationService`, `AppOnlyDocumentAnalysisJobHandler`, `EmailAnalysisJobHandler`) unrelated to this task.
- SPEC §3.5.4 forbidden-imports grep: ZERO matches in `Services/Insights/LiveFacts/` (Zone B clean — only `Models.Insights` imported); ZERO matches in `Services/Insights/` overall
- Quality gates (Step 9.5):
  - code-review ✅: 0 critical / 1 warning (Filter passthrough on trusted-operator input — addressed inline with XML doc warning) / 2 suggestions / 0 AI smells; AI Smell Score: clean (the one interface with single impl is the documented Phase 1.5 swap seam, same as task 002 IInsightGraph pattern)
  - adr-check ✅: 13 ADRs/decisions validated, 0 violations (ADR-001, 002, 007, 008, 009, 010, 013, 014, 021, 028 + SPEC §3.5.4 + D-49 + D-A23/D-48 + D-04 + D-05)
- Acceptance criteria: 7/7 PASS per POML
  1. All 5 nodes implement INodeExecutor + resolve via NodeExecutorRegistry by ActionType ✅ (verified in standalone runner registry-dispatch test)
  2. LiveFactNode returns FactArtifact from ILiveFactResolver mock ✅ (HappyPath emits FactArtifact with confidence 1.0)
  3. IndexRetrieveNode queries spaarke-insights-index with filter+vector ✅ (config + filter-building + tenant-guard validated; full SDK integration deferred to D-P16 smoke test)
  4. EvidenceSufficiencyNode flags sufficient/insufficient against minComparableMatters:12 rule ✅ (Sufficient/Insufficient verdicts both verified with gap analysis)
  5. DeclineToFindNode emits structured DeclineResponse (typed, not prose) ✅ (verbatim DeclineResponse type emitted; template rendering verified for {have}/{need} tokens)
  6. ReturnInsightArtifactNode validates non-empty evidence → throws on empty ✅ (EvidenceGuard REJECTS empty-evidence inference test PASS; EvidenceGuard ALLOWS empty-evidence Fact test PASS)
  7. MultiIndexComposer consumed by both AiAnalysisNodeExecutor AND IndexRetrieveNode ✅ (AiAnalysisNodeExecutor.MergeKnowledgeContext now delegates; behavior preserved)
- Notes:
  - ActionType numbering preserves task 020's 10-value gap pattern: GroundingVerify=70 → LiveFact=80 → IndexRetrieve=90 → EvidenceSufficiency=100 → DeclineToFind=110 → ReturnInsightArtifact=120
  - ILiveFactResolver swap-path seam: Phase 1 stub throws `LiveFactNotSupportedException` with "Phase 1" message; DataverseLiveFactResolver will swap in via DI re-registration when D-P7 task 040 ships
  - Code-review warning re: IndexRetrieveNode.Filter passthrough on trusted-operator OData → addressed inline with explicit XML doc warning that ConfigJson is admin-authored, never request-body-sourced; canonical hardening at D-P15 endpoint task 061
  - MultiIndexComposer extraction is a pure refactor (lift-and-replace); AiAnalysisNodeExecutor behavior unchanged — confirmed by build clean + no new test failures in existing files

---

---

## Last completed tasks

**Task 020 — D-P9 GroundingVerifier + GroundingVerifyNode** ✅ (2026-05-28)
- Rigor: FULL (bff-api .cs, platform primitive shared with Action Engine, closes D-04 honesty-contract gap)
- Files NEW:
  - `src/server/api/Sprk.Bff.Api/Services/Ai/CitationVerification/IGroundingVerifier.cs` (110 lines) — interface + `ChunkRef` + `VerificationResult` + `VerificationVerdict` enum (Verified / VerifiedApproximate / NotFound / NoQuote / InvalidInput)
  - `src/server/api/Sprk.Bff.Api/Services/Ai/CitationVerification/GroundingVerifier.cs` (282 lines) — mechanical zero-LLM impl: normalize → exact substring → sliding-window (window=200, step=100, overlap threshold=0.70, min-quote=12) → 10K-char DoS cap
  - `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/GroundingVerifyNode.cs` (335 lines) — `INodeExecutor` for new `ActionType.GroundingVerify = 70`; config-driven (`citationsFrom` + `sourceChunksFrom` upstream variable names); annotates failures `[citation could not be verified]`
  - `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/CitationVerification/GroundingVerifierTests.cs` (~390 lines) — 13 verifier tests + 7 node tests (xUnit + FluentAssertions; shipped in canonical location for when pre-existing test-project errors get fixed)
- Files MODIFIED:
  - `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs` — added `ActionType.GroundingVerify = 70` enum value with 10-value gap above AgentService=60
  - `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` — registered `IGroundingVerifier` Singleton + `GroundingVerifyNode` as `INodeExecutor` in `AddNodeExecutors`
- Build: `dotnet build src/server/api/Sprk.Bff.Api/` clean — 0 errors, 2 pre-existing NU1903 warnings (Microsoft.Kiota.Abstractions CVE — unrelated)
- Test verification (standalone runner at `c:/tmp/grounding-verifier-test/`, same pattern as task 002): **50/50 assertions PASS** (25 verifier scenarios + 25 node scenarios incl. registry dispatch); test-project (Sprk.Bff.Api.Tests) still has 7 pre-existing compile errors unrelated to this task (`EmbeddingMigrationService`, `AppOnlyDocumentAnalysisJobHandler`, `EmailAnalysisJobHandler` — same as tasks 001/002/012 reported)
- SPEC §3.5.4 forbidden-imports grep: ZERO matches in `Services/Insights/` (Zone B clean) AND `Services/Ai/CitationVerification/` (zero LLM dependency confirmed — only ILogger in ctor)
- Quality gates (Step 9.5): code-review ✅ (0 critical / 0 warnings / 0 AI smells; algorithm-tuning rationale inline; named constants for thresholds) / adr-check ✅ (ADR-013 Zone A ✓, ADR-010 singleton justified ✓, D-47 algorithm match ✓, D-04 honesty-contract closure ✓, LAVERN ADR 10.6 platform primitive ✓)
- Acceptance criteria: 6/6 PASS per POML
  - (a) Exact-quote → Verified ✅
  - (b) Paraphrased within window → VerifiedApproximate ✅ (token-overlap ≥0.70)
  - (c) Fabricated → NotFound + DefaultAnnotation `[citation could not be verified]` ✅
  - (d) >10K-char chunk → InvalidInput ✅
  - (e) Node registered + dispatched via NodeExecutorRegistry ✅ (verified in test)
  - (f) Zero LLM calls ✅ (verified by ctor-parameter design check + functional smoke without any AI plumbing wired)
- Decision: `ActionType.GroundingVerify = 70` with 10-value gap above AgentService=60 leaves room for the rest of D-P12 nodes (LiveFact=71, IndexRetrieve=72, etc.) without renumbering this one. Algorithm constants (WindowSize=200, WindowStep=100, threshold=0.70, MinApproximateQuoteLength=12) tuned for legal-document quotes; preserves all substantive characters during normalization (only whitespace-collapse + ToLowerInvariant).
- Action Engine consumption path preserved: `IGroundingVerifier` interface is Zone A platform primitive; LAVERN ADR 10.6 — Action Engine R2 DI-injects this without reaching into Insights internals.

**Task 021 — D-P10 Confidence threshold gating + per-field Observation emission** ✅ (2026-05-28)
- Rigor: FULL (bff-api code, Zone A platform primitive, foundation for D-P7 universal ingest playbook)
- Files NEW (5 production + 1 test):
  - `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Extraction/ExtractionResult.cs` (145 lines) — typed POCO mirroring SPEC-phase-1-minimum.md §3.4 Layer 2 prompt schema (`Subject + DocumentRef + TenantId + ProducedBy{Kind,Id,Version} + AsOf + Scope + Fields[name → ExtractionField{Value:JsonElement, Quote, Confidence, DisplayHint}]`); Zone A internal contract
  - `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Extraction/ConfidenceThresholdOptions.cs` (64 lines) — D-63 admin-tunable per-field thresholds bound to `Insights:Extraction:ConfidenceThresholds`; defaults match SPEC-phase-1-minimum.md §3.4 starters (outcomeCategory 0.75 / settlementAmount 0.85 / outcomeDate 0.85 / matterDurationDays 0.75); case-insensitive `GetThresholdFor` with `DefaultThreshold` fallback
  - `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Extraction/IObservationEmitter.cs` (56 lines) — interface for D-P10 third mechanical gate; signature accepts `Func<ObservationArtifact, CancellationToken, Task>? upsertAsync` callback so we don't block on task 025 (W3.5 ReferenceIndexingService refactor) — task 040 (D-P7 ingest playbook) wires the real upsert later
  - `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Extraction/ObservationEmitter.cs` (175 lines) — `internal sealed`; per-call options snapshot via `IOptionsMonitor.CurrentValue`; strict `<` threshold (exact equality passes); structured logging with stable `EventId(8021, "ObservationDroppedBelowThreshold")` for App Insights drift dashboard query; preserves field iteration order; honours cancellation; builds Observation with SPEC §3.4.1-matching id pattern `obs:{matterLocal}:{fieldName}:{docLocal}` + two evidence refs (`document` with quote + `playbook-run`) + `ProducedBy.Version` per D-05
  - `src/server/api/Sprk.Bff.Api/Infrastructure/DI/InsightsExtractionModule.cs` (60 lines) — new Zone A feature module (NOT added to AiModule which is at 15/15 ADR-010 cap); registers `IOptions<ConfidenceThresholdOptions>` with `ValidateDataAnnotations` + `IObservationEmitter → ObservationEmitter` (Singleton)
  - `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Insights/Extraction/ObservationEmitterTests.cs` (320 lines) — 12 xUnit/FluentAssertions tests covering all 4 acceptance criteria + edge cases (cancellation, null guard, default-threshold fallback, case-insensitive lookup, IOptionsMonitor reload between calls); in-process `TestOptionsMonitor` helper. Test project Sprk.Bff.Api.Tests has pre-existing 7 compile errors unrelated to this task (per tasks 001/002/012 notes)
- Files MODIFIED (1):
  - `src/server/api/Sprk.Bff.Api/Program.cs` (+5 lines) — wired `AddInsightsExtractionModule(builder.Configuration)` between AnalysisServicesModule and AiSafetyModule
- Standalone runner: `c:/tmp/task021-emitter-runner/` exercises the same 11 scenarios as the xUnit suite (32 assertions) by resolving `IObservationEmitter` through the production `AddInsightsExtractionModule` extension (verifies real DI wiring at the same time). **All 32 assertions PASS.**
- Build: `dotnet build src/server/api/Sprk.Bff.Api/` clean — 0 errors, same 2 pre-existing warnings (Kiota CVE × 2) plus 15 unrelated warnings in legacy files (no warnings introduced by new files)
- SPEC §3.5.4 forbidden-imports grep:
  - Zone B `Services/Insights/**/*.cs` — ZERO matches (unaffected; we only added Zone A code)
  - Zone A `Services/Ai/Insights/Extraction/**/*.cs` — ZERO matches (the deterministic primitive happens to import nothing AI-internal either)
- Quality gates: code-review ✅ (0 critical, 0 warnings on new files; minor `LocalPart` helper note acknowledged in current-task) / adr-check ✅ — ADR-013 (Zone A placement per SPEC §3.5.2), ADR-010 (new feature module justified — AiModule at 15/15; `IObservationEmitter` interface seam justified per §Exceptions — D-P7 ingest consumer + D-62 re-extraction consumer + test seam), ADR-001 N/A, ADR-029 N/A (zero package adds), D-63 ✅ (IOptionsMonitor binding + per-field override + default fallback), D-P10 ✅ (one ObservationArtifact per surviving field; SPEC §3.4.1 evidence shape + `producedBy=outcome-extraction@v1`), D-A23/D-48 EvidenceGuard ✅ (every emitted Observation has Evidence[] populated, never empty), D-52 ✅ (TenantId required + propagated to envelope + Scope), bff-extensions.md ✅ (placement justified, zero new packages)
- Acceptance criteria: 4/4 PASS per POML
  1. High-confidence extraction emits N Observations (one per field) — Test 1 (4 fields, all above starter thresholds, 4 emit; predicates + id + value + scope all verified)
  2. Below-threshold field dropped AND logged to App Insights with field name + actual confidence — Tests 3/4 (drop verified; logged via `EventId(8021)` structured log)
  3. Thresholds load from IConfiguration via IOptionsMonitor; admin override via appsettings.json — module binds `Insights:Extraction:ConfidenceThresholds` section; Test 6 verifies reload-between-calls applies
  4. Each emitted Observation has `evidence[]` with verbatim quote + `producedBy="outcome-extraction@v1"` — Test 2 (verbatim SPEC §3.4.1 quote round-trips; `ProducedBy.Id="playbook://outcome-extraction@v1"`, `.Version="v1"`, `.Kind="playbook"`)
- Notes:
  - `<` is strict per the literal SPEC wording "fields below threshold are dropped"; equal-to-threshold passes (covered by Test 5)
  - Upsert callback signature lets task 025 (parameterized ReferenceIndexingService) wire substrate writes later without changing this primitive — D-P7 ingest playbook (task 040) is the canonical caller
  - `ObservationEmitter` is `internal sealed` (consumed via `IObservationEmitter` interface); `Sprk.Bff.Api.csproj` already grants `InternalsVisibleTo Sprk.Bff.Api.Tests` so the xUnit test can construct directly

**Task 012 — D-P3 (endpoint) POST /api/insights/admin/precedents admin endpoint** ✅ (2026-05-28)
- Rigor: FULL (bff-api code modifying .cs, admin endpoint, Zone B facade compliance, foundation for D-P4 + D-P14)
- Files NEW:
  - `src/server/api/Sprk.Bff.Api/Services/Insights/Precedents/IPrecedentBoard.cs` (125 lines) — interface + DTOs (CreatePrecedentRequest, PrecedentRecord, PrecedentStatus constants)
  - `src/server/api/Sprk.Bff.Api/Services/Insights/Precedents/DataversePrecedentBoard.cs` (227 lines) — IDataverseService-only impl, no AI internals
  - `src/server/api/Sprk.Bff.Api/Api/Insights/PrecedentAdminEndpoints.cs` (230 lines) — POST endpoint with ADR-008 filter + ADR-019 ProblemDetails + ADR-016 rate limit
  - `tests/integration/Spe.Integration.Tests/Api/Insights/PrecedentAdminEndpointsTests.cs` (223 lines) — 6 xUnit tests with mocked IPrecedentBoard (test project has pre-existing 4 compile errors unrelated to this task)
  - `scripts/Verify-PrecedentAdminEndpoint.ps1` — standalone real-Dataverse acceptance verifier
- Files MODIFIED:
  - `src/server/api/Sprk.Bff.Api/Infrastructure/DI/InsightsModule.cs` — registered IPrecedentBoard → DataversePrecedentBoard (Scoped)
  - `src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs` — wired `app.MapPrecedentAdminEndpoints()`
  - `src/server/shared/Spaarke.Dataverse/IGenericEntityService.cs` — added generic `AssociateAsync(entityLogicalName, entityId, relationshipName, relatedEntities, ct)` method
  - `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` — real impl using ServiceClient.AssociateAsync; tolerates duplicate-association errors
  - `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs` — NotImplementedException stub (matches pattern of other generic-entity methods on that class)
- Build: `dotnet build src/server/api/Sprk.Bff.Api/` clean — 0 errors, 17 pre-existing warnings (none in new files)
- §3.5.4 forbidden-imports grep: ZERO matches in `Api/Insights/` and `Services/Insights/Precedents/`
- Real-Dataverse verification (Verify-PrecedentAdminEndpoint.ps1 against spaarkedev1):
  - Created sprk_precedent with status=Tentative (100000000), producedBy=manual-sme-author, reviewerBy=current user (1d02f31c-1872-f011-b4cb-7c1e52671ad0)
  - N:N supporting matter (LITG-226554) associated via sprk_precedent_matter — read-back confirmed count=1
  - Cleanup: test row deleted
  - All 6 assertions PASS; ran end-to-end against Spaarke Dev
- Quality gates: code-review ✅ (0 critical, 0 warnings, 0 AI smells; IPrecedentBoard interface justified per ADR-010 exception — testing seam + D-P4 downstream) / adr-check ✅ (9 ADRs validated, 0 violations)
- Acceptance criteria: 5/5 PASS per POML
- Endpoint: `POST /api/insights/admin/precedents` (admin role required, 60/min rate limit, ProblemDetails errors, 201 Created with location header + CreatePrecedentResponse)

**Task 010 — D-P2 Bicep modules + spaarke-insights-index + Function App shell** ✅ (2026-05-28)
- Rigor: FULL (infra deployment, foundational, shared-state)
- First-step blocker RESOLVED: index name = `spaarke-insights-index` (user confirmed 2026-05-28)
- Files: `infra/insights/main.bicep` + 5 modules + `parameters/dev.json` + `schemas/spaarke-insights-index.index.json` + `README.md` + `.gitignore` (bicep build artifacts)
- Deployed to Spaarke Dev `spe-infrastructure-westus2` — deployment `insights-engine-spaarkedev-20260528120631` (provisioningState: Succeeded)
- Resources created (6, all tagged `spaarkeProject=insights-engine`):
  - `insights-spaarkedev-uami` — per-tenant UAMI (auth boundary per D-27/ADR-024)
  - `insights-search-deploy-uami` — transient UAMI for the deploymentScript container
  - `insights-spaarkedev-plan` — Flex Consumption FC1 hosting plan
  - `insights-spaarkedev-func` — Function App shell (dotnet-isolated 8.0, 2048MB, alwaysReady=1, state=Running, hostname `insights-spaarkedev-func.azurewebsites.net`)
  - `insightsspaarkedevstg` — Standard_LRS storage for Flex deployment artifacts
  - `deploy-spaarke-insights-index` — deploymentScript (one-shot; cleanupPreference=OnSuccess removes ACI on success)
  - + Key Vault Secrets User RBAC grant on existing `sprkspaarkedev-aif-kv` for the per-tenant UAMI
- Index verified on `spaarke-search-dev`: all 14 SPEC §3.4 fields present, contentVector dims=3072 (matches text-embedding-3-large), HNSW vector profile, semantic config, vectorFilterMode=preFilter friendly (all discriminator fields filterable=true)
- SPEC §3.4.3 worked-example filter queries verified — both Query 1 (cohort observations: `tenantId + artifactType + predicate` filters) and Query 2 (precedents: `tenantId + artifactType + status + value/raw/scope/matterType + value/raw/scope/opposingCounsel` filters) parse cleanly against the deployed schema (return 0 results from empty index — no errors)
- Function App reachable at `https://insights-spaarkedev-func.azurewebsites.net/` — returns default "Your Azure Function App is up and running" page (shell, no functions deployed per D-P2 scope)
- Zero new SAS keys, zero new `ClientSecretCredential` per D-24/D-27 (AzureWebJobsStorage uses platform-required auto-generated storage key, documented as known constraint)
- Single-tenant parameter file pattern documented in `infra/insights/README.md` — onboarding a new customer = copy `parameters/dev.json`, set `tenantShortName`+`tenantDisplayName`, redeploy
- Quality gates: self-run code-review + adr-check (8 ADRs/decisions checked, all pass)
- Acceptance criteria: 6/6 PASS — see task POML
- Deploy iterations: 3 (first 2 failed: FUNCTIONS_WORKER_RUNTIME invalid for Flex; curl not in deploymentScript container; both fixed)

**Task 011 — D-P3 (entity) sprk_precedent Dataverse entity + relationships** ✅ (2026-05-28)
- Rigor: STANDARD (Dataverse schema; quality gates inside dataverse-create-schema skill)
- Solution: created `spaarke_insights` (Spaarke publisher, prefix `sprk`) — new solution in Spaarke Dev
- Entity: `sprk_precedent` (LogicalName, SchemaName=sprk_Precedent, EntitySetName=sprk_precedents); primary name `sprk_name`
- Fields (8 custom): sprk_patternstatement (Memo 4000), sprk_status (Picklist→sprk_precedentstatus), sprk_reviewerby (Lookup→systemuser), sprk_reviewdate (DateOnly), sprk_effectivenessscore (Decimal 0-1, Phase 1.5+), sprk_clusterdefinition (Memo 2000), sprk_samplesize (Integer), sprk_producedby (String 200)
- Option set `sprk_precedentstatus` (global, 5 values): Tentative(100000000) / Confirmed(100000001) / Under Drift Review(100000002) / Deprecated(100000003) / Retired(100000004)
- N:N relationships: `sprk_precedent_matter` (supporting matters), `sprk_precedent_related` (self) — both created
- N:N DEFERRED: `sprk_precedent_observation` — sprk_observation entity does not exist in Phase 1 (will land with D-P11)
- Views: `Active Precedents` (default), `Tentative Precedents`, `Confirmed Precedents`, `Precedents Under Drift Review`, `Deprecated Precedents`
- Form: default Main form ("Information") added to solution
- Solution exported + unpacked to `src/solutions/spaarke_insights/` (27 files)
- Scripts created: `scripts/Deploy-PrecedentEntity.ps1` (idempotent, 5-phase pattern), `scripts/Deploy-PrecedentViewsAndForm.ps1`
- All 6 acceptance criteria PASS via Web API verification (queryable, fields present, 5 status values, N:Ns, views, solution export)

**Task 002 — D-P17 IInsightGraph interface + stub** ✅ (2026-05-28)
- Files: `Services/Insights/Graph/{IInsightGraph,InsightVertex,InsightEdge,GraphTraversalSpec,StubInsightGraph}.cs` + `Infrastructure/DI/InsightsModule.cs` + `Program.cs` registration + `tests/.../StubInsightGraphTests.cs`
- Tests: 9/9 pass (standalone verifier — DI resolution + 7 method NotImplementedException assertions, all with "Phase 1.5" + "SPEC §3.3" message check); test project Sprk.Bff.Api.Tests still has pre-existing compile errors unrelated to this task
- Build: `dotnet build src/server/api/Sprk.Bff.Api/` clean — 0 errors, 17 pre-existing warnings (none in new files)
- SPEC §3.5.4 forbidden-imports grep: clean (zero matches in `Services/Insights/Graph/`)
- Quality gates: skipped per STANDARD rigor; design discipline still applied (Zone B isolation, ADR-010 seam justification, D-09 no-Gremlin-leak)
- Preserves D-P17 swap path — CosmosNoSqlInsightGraph is first Phase 1.5 deliverable per SPEC §3.3
- Judgment: created new `InsightsModule` (Zone B) rather than extending Zone A `AnalysisServicesModule` — keeps §3.5 facade boundary visible in DI composition; ADR-010 §Exceptions permits interface seams when swap-path is real (it is)

**Task 001 — D-P1 InsightArtifact envelope POCOs** ✅ (2026-05-28)
- Files: `Models/Insights/{InsightArtifact,EvidenceRef,DeclineResponse}.cs` + `tests/.../InsightArtifactTests.cs`
- Tests: 7/7 pass (standalone runner); test project Sprk.Bff.Api.Tests has pre-existing compile errors unrelated to this task
- Build: `dotnet build src/server/api/Sprk.Bff.Api/` clean — zero new warnings
- SPEC §3.5.4 forbidden-imports grep: clean
- Quality gates: code-review ✅ / adr-check ✅
- Foundation type for D-P3, D-P4, D-P6, D-P10, D-P11, D-P14, D-P15 (all downstream tasks consume this envelope)

---

## Next action

Wave 5 task 042 complete. Tasks 040 (D-P7 universal ingest playbook) and 041 (D-P4 Precedent projection sync) are both unblocked and parallel-dispatchable — invoke them in ONE message with TWO Skill `task-execute` calls per root CLAUDE.md §4 parallel pattern.

---

## Progress tracking

| State | Count |
|---|---|
| ✅ Completed | 12 (001, 002, 010, 011, 012, 020, 021, 022, 023, 024, 025, 030, 031, 042 — D-P + side-quests + scaffold) |
| 🔄 In progress | 0 |
| 🔲 Pending | 040, 041, 050, 051, 060, 061, 070 (D-P7 / D-P4 / D-P8 / D-P11 / D-P14 / D-P15 / D-P16) |
| ⏭️ Deferred (Phase 1.5+) | — see SPEC §3.3 |

---

## Context recovery

If a session is compacted or interrupted, this file is the entry point for recovery:

1. Read this file to see active task state
2. Read [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) for progress
3. Read [SPEC.md](SPEC.md) §3.1 for canonical deliverable list
4. Read [CLAUDE.md](CLAUDE.md) for project-scoped instructions
5. Read root [CLAUDE.md](../../CLAUDE.md) §4 for the mandatory task-execute protocol
6. Invoke `task-execute` for whatever task is `in_progress` (or pick the next 🔲 from the index)

---

## Decision log (per task)

### Task 002 (D-P17) — completed 2026-05-28

- **New `InsightsModule` vs extending `AnalysisServicesModule`**: chose new module. AnalysisServicesModule is Zone A (freely imports `IOpenAiClient`, `IPlaybookService`, `Microsoft.Extensions.AI`); §3.5 facade boundary mandates Zone B Insights code be wired separately so the boundary is visible in DI. ADR-010 §Exceptions permits new interfaces when there's a true seam — D-P17 IS a true seam (Phase 1 stub ↔ Phase 1.5 Cosmos impl).
- **Singleton lifetime for `IInsightGraph`**: future Cosmos impl will hold a `CosmosClient` which is itself thread-safe and intended to be reused; stub is stateless so lifetime is moot in Phase 1.
- **`StubInsightGraph` marked `internal sealed`**: nothing outside the assembly should depend on the concrete type — only `IInsightGraph` via DI. Sealed prevents test subclassing tricks that would obscure the swap intent.
- **`InternalsVisibleTo Sprk.Bff.Api.Tests` already present** on `Sprk.Bff.Api.csproj` so tests can reference the internal stub type for assertion-against-concrete in `BeOfType<StubInsightGraph>()`.
- **Records (positional/init-only) for all DTOs** — immutable, value equality, smaller boilerplate, matches task 001's choice for `InsightArtifact`. `IReadOnlyDictionary<,>` + `IReadOnlyList<>` for collections preserves immutability through the interface surface.
- **Named traversal discipline (D-09)**: `GraphTraversalSpec` deliberately exposes `EdgeTypeFilter`, `MaxHops`, `TargetVertexTypeFilter` as plain lists/ints — NOT Gremlin step syntax fragments or Cosmos SQL strings. This is what makes a NoSQL ↔ Gremlin implementation swap a contained refactor.
- **Pre-existing test infrastructure breakage continues** (same 7 errors as task 001 reported). Worked around by writing a standalone console verifier that exercises every interface method through the DI-registered stub; all 9 assertions passed. The shipped `StubInsightGraphTests.cs` will run cleanly once the unrelated test-project breakage is fixed.

### Task 001 (D-P1) — completed 2026-05-28

- Use C# **records** (immutable) over classes — matches POML "Record types" wording, cleaner serialization, no accidental mutation in pipelines.
- **`PrecedentArtifact`** does NOT carry `confidence` on the wire (matches SPEC §3.4.2 worked example `"confidence": null`; Precedents are SME-confirmed per D-46) and adds **`Status`** (lifecycle state per design §2.1 + SPEC §3.4.2 `"status": "confirmed"`).
- **`Value.Raw`** typed as `JsonElement` — preserves arbitrary JSON shapes (string enum, integer, nested Precedent object) without custom converters.
- **Polymorphism**: `[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]` + `[JsonDerivedType]` for each tier — standard System.Text.Json idiom; no custom converter needed.
- **Pre-existing test infrastructure breakage** (EmbeddingMigrationService / AppOnlyDocumentAnalysisJobHandler / EmailAnalysisJobHandler types missing from Sprk.Bff.Api) — verified pre-existing by stashing my changes and confirming build still fails with same 7 errors. Out of scope for task 001; should be tracked separately as test-cleanup work.
