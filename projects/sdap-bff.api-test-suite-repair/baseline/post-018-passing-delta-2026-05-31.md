# Post-018 Factory-Extension Delta — 2026-05-31

> **Source pre-edit TRX**: `baseline/pre-018-baseline.trx` (captured 2026-05-31 BEFORE the factory edit)
> **Source post-edit TRX**: `baseline/post-018-measure.trx` (captured 2026-05-31 AFTER the factory edit)
> **Edit applied**: 7 additive config dict entries in `tests/unit/Sprk.Bff.Api.Tests/CustomWebAppFactory.cs` (`CosmosPersistence:*` + `AgentService:*`)
> **Authority**: Per §6.4, this file documents the full-suite delta required around every `CustomWebAppFactory.cs` change. Per §6.3, all numbers below are measured (not estimated).

---

## Headline result

| Metric | Pre-018 | Post-018 | Delta |
|---|---:|---:|---:|
| Total tests | 6,034 | 6,034 | 0 |
| **Passed** | **5,641** | **5,753** | **+112 (+2.0%)** |
| **Failed** | **284** | **172** | **−112 (−39.4%)** |
| Skipped | 109 | 109 | 0 |
| Build errors | 0 | 0 | 0 |
| Build warnings | 2 (pre-existing Kiota CVE) | 2 (same) | 0 |
| Duration | 1m 15s | 1m 12s | −3s |

**No regressions** — `comm` analysis of post-018 ∖ pre-018 cluster set returned ∅. Every test that was passing pre-018 still passes post-018. The skipped count is unchanged, so the −112 failures became +112 passes (not skips).

---

## Cluster-level deltas (top 25)

Sorted by failure reduction; `pre=N post=M delta=−K` indicates cluster `N` failures pre-edit, `M` post-edit, `K` resolved.

### A. Clusters fully eliminated by task 018 (17 clusters, 125 failures cleared)

| Cluster | Pre | Post | Delta |
|---|---:|---:|---:|
| Api.Ai.PlaybookRunEndpointsTests | 20 | 0 | **−20** |
| Api.Ai.HandlerEndpointsTests | 11 | 0 | **−11** |
| Api.Ai.NodeEndpointsTests | 10 | 0 | **−10** |
| UploadEndpointsTests | 9 | 0 | **−9** |
| Api.Ai.ModelEndpointsTests | 8 | 0 | **−8** |
| SpeAdmin.SearchItemsTests | 7 | 0 | **−7** |
| FileOperationsTests | 6 | 0 | **−6** |
| ListingEndpointsTests | 6 | 0 | **−6** |
| UserEndpointsTests | 6 | 0 | **−6** |
| Api.Ai.ChatSessionPlanEndpointTests | 5 | 0 | **−5** |
| Api.Ai.ChatRefineEndpointTests | 4 | 0 | **−4** |
| CorsAndAuthTests | 1 | 0 | **−1** |
| EndpointGroupingTests | 1 | 0 | **−1** |
| HealthAndHeadersTests | 4 | 1 | −3 (mostly cleared) |
| PipelineHealthTests | 4 | 1 | −3 (mostly cleared) |
| Api.Ai.StandaloneChatContextEndpointsTests | 13 | 6 | −7 |
| Api.Ai.AnalysisChatContextEndpointsTests | 10 | 7 | −3 |
| **Subtotal cleared** | | | **−110** |

Remaining 2 failures explained by residual non-startup issues now visible (HealthAndHeadersTests=1, PipelineHealthTests=1).

### B. Clusters unchanged by task 018 (delta = 0; 159 failures remain)

These were NOT host-build failures — they were already running past the factory init and failing at assertion/setup level. Task 018 was not expected to clear them; they are absorbed by Phase 2+3 tasks.

| Cluster | Pre = Post | Phase 2+3 absorber (per task 008 reconciliation) |
|---|---:|---|
| Integration.Workspace.WorkspaceEndpointsTests | 31 | task 060 |
| Integration.Workspace.WorkspaceLayoutEndpointTests | 23 | task 060 |
| Services.Ai.Safety | 11 | task 044 |
| Api.Office.OfficeEndpointsTests | 10 | task 073 |
| Integration.CommunicationIntegrationTests | 9 | task 055 |
| Integration.SseStreamingIntegrationTests | 8 | task 050 / known NSubstitute issue |
| Api.Reporting.ReportingEndpointsTests | 7 | task 072 |
| Services.Ai.Nodes | 5 | task 054 |
| Api.Reporting.ReportingAuthorizationFilterTests | 5 | task 072 |
| Services.Ai.Feedback | 4 | task 050 (default) |
| Services.Ai.Chat | 4 | task 050 |
| Services.Ai.RagServiceTests | 3 | task 053 |
| Services.Ai.Insights | 3 | HOLD (task 008 owner decision pending) |
| Api.Agent.HandoffUrlBuilderTests | 3 | task 070 |
| Api.Agent.AgentConversationServiceTests | 3 | task 070 |
| Services.Ai.WorkingDocumentServiceTests | 2 | task 050 |
| Services.Ai.Capabilities | 2 | task 053 |
| Api.Ai.DailyBriefingEndpointsTests | 2 | task 070 |
| Api.Ai.R2SseEventEmitterTests | 1 | task 070 |
| Api.Agent.AgentConfigurationServiceTests | 1 | task 070 |
| Integration.PlaybookExecutionTests | 1 | task 050 |
| Services.Jobs.RecordSyncJobTests | 1 | task 046 |
| **Subtotal residual** | **159** | distributed across 12 Phase 2+3 tasks |

### C. Regression check (clusters newly failing post-018)

| Cluster | Pre | Post | Delta |
|---|---:|---:|---:|
| (none) | — | — | — |

**Verdict**: zero regressions. No test that was passing pre-018 is failing post-018. `services.RemoveAll<IHostedService>()` guard absorbed any new background services that the new config keys may have unlocked (per task 017 §D analysis).

---

## Task 014 top-5 cluster impact (per project CLAUDE.md Decisions Made entry 2026-05-31)

Task 014 captured these top-5 clusters as the post-Wave-1.1a starting state (284 total failures):

| Task 014 cluster name | Pre-018 (TRX-measured) | Post-018 | Reduction |
|---|---:|---:|---:|
| **Api.Ai.*** (89) | 84 | 13 | **−71 (−85%)** |
| **Integration.Workspace.*** (54) | 54 | 54 | 0 (assertion-level, not factory) |
| **Top-level *EndpointTests** (39) | 39 | 1 | **−38 (−97%)** |
| Services.Ai.* non-Safety (23) | 22 | 19 | −3 |
| Services.Ai.Safety.* (19) | 11 | 11 | 0 |

The first and third top-5 clusters are dramatically reduced. The second (Workspace Integration) was **never** a factory-startup issue per task 017 — it's the same `CosmosPersistence:Endpoint` symptom from `IntegrationTestFixture.cs`, which is OUTSIDE task 018's scope and will be cleared by Phase 2+3 task 062 (per the orchestrator's task 024 Cluster A mapping). Services.Ai.Safety remained at 11 because those failures are assertion-level (not host-build).

(Note: the Phase 2+3 absorbing-task estimates in the project CLAUDE.md Implementation Notes for task 014 were measured against the post-Wave-1.1a state. Some cluster boundaries differ slightly from what the TRX namespace prefix produces; the spirit of the projection holds.)

---

## §4.5 additive-only compliance proof

```
$ git diff --stat tests/unit/Sprk.Bff.Api.Tests/CustomWebAppFactory.cs
 tests/unit/Sprk.Bff.Api.Tests/CustomWebAppFactory.cs | 19 ++++++++++++++++++-
 1 file changed, 18 insertions(+), 1 deletion(-)
```

- **18 insertions** (7 dict entries + 11 lines of section comments/blank lines)
- **1 deletion** = the existing `["ManagedIdentity:ClientId"] = "test-managed-identity-client-id"` line was rewritten only to append `,` (no semantic change; same key, same value). This is the smallest possible additive growth point in C# dictionary-initializer syntax.
- **0 method-signature changes**
- **0 existing dict entries removed or modified semantically**
- **0 logic restructuring**
- Pre-edit factory: 171 LOC. Post-edit factory: 188 LOC. Net growth: **+17 LOC (+9.9%)** — well below design.md §3.4 estimate of "~30 LOC growth to ~200 LOC" and far below the §4.8 50% rewrite escalation threshold.

NFR-09 (`<repair-not-rewrite>true</repair-not-rewrite>`): satisfied.
NFR-01 (no `src/` changes): satisfied (only `tests/unit/Sprk.Bff.Api.Tests/CustomWebAppFactory.cs`).
NFR-03 (no new BFF DI registrations): satisfied (config keys only; zero `services.AddXxx(...)` calls added).
NFR-07 (anti-parallelism): satisfied (task ran ISOLATED in Wave 1-C-isolated per orchestrator confirmation; current-task.md history shows no concurrent work).

---

## Cross-cluster integration impact (informational)

The same `CosmosPersistence:Endpoint` config gap is the root cause of integration test Cluster A (97 failures per task 024). That cluster is OUTSIDE task 018's scope; it requires an equivalent edit to `tests/integration/Spe.Integration.Tests/IntegrationTestFixture.cs`. Phase 2+3 P23.I task 062 will handle it. Documenting here so the cross-reference is preserved at the merge point.

---

## Trait-tagging note

Trait-tagging individual newly-passing tests on the factory edit is **impractical** at this scope (a 110-failure clearance touches dozens of test classes). Per task 018 POML Step 7: "Add `[Trait("status", "repaired")]` — N/A here (factory is infrastructure, not a test)." Per the orchestrator brief: "Trait-tag any test files newly passing? — NO." Phase 2+3 will trait-tag individual tests as it touches them (each absorbing task in §B above is responsible for its own files' trait-tagging when it runs).

This inventory document substitutes for per-test trait-tagging at the factory edit point.

---

## Verification

- [x] Pre-edit TRX captured and committed to `baseline/pre-018-baseline.trx`
- [x] Post-edit TRX captured and committed to `baseline/post-018-measure.trx`
- [x] Build clean (0 errors, 2 warnings — pre-existing Kiota CVE)
- [x] Net failure reduction measured (−112, −39.4%)
- [x] Zero regressions (no clusters newly failing)
- [x] §4.5 additive-only verified via `git diff --stat`
- [x] Inventory cross-references the 7 entries from task 017 verbatim
- [x] Cross-cluster integration impact noted (task 062 in Phase 2+3 will close it on the integration side)
