# Integration Fixture Sibling Discovery — 2026-05-31

> **Source**: Wave 2.4 MERGED task 062+063 execution (`P23.I3` / `P23.I4`)
> **Author**: AI agent (sdap-bff.api-test-suite-repair)
> **Status**: Discovery handoff — requires new Phase 2+3 follow-up task to fully clear Cluster B
> **Cross-references**: `integration-test-triage.md` (task 024), `baseline/post-062-2026-05-31.trx`, both task POMLs 062 + 063

---

## Summary

Task 024's triage assumed `IntegrationTestFixture.cs` was the single host-build entry point for all 422 `Spe.Integration.Tests` tests. Wave 2.4 execution discovered this assumption is wrong: **8 sibling fixtures** exist that inherit `WebApplicationFactory<Program>` directly (not `IntegrationTestFixture`), so they do NOT receive the `IntegrationTestFixture` config overlay. This explains why the post-062 TRX still contains 98 active `SpeAdmin:KeyVaultUri` errors despite `IntegrationTestFixture.cs` already supplying that key at line 74.

The Cosmos config fix in task 062 cleared Cluster A (97 failures) and Cluster C (3 reporting tests). Cluster B's 98 failures cannot be cleared by editing `IntegrationTestFixture.cs` — each sibling fixture needs its own config dictionary updated.

---

## Confirmed Wave 2.4 outcome

| Metric | Pre-062 | Post-062 | Delta |
|---|---:|---:|---:|
| **Total** | 422 | 422 | 0 |
| **Passed** | 88 | 262 | +174 |
| **Failed** | 198 | 108 | -90 |
| **Skipped** | 136 | 52 | -84 |

**Cluster A (Cosmos)**: CLEARED (0 active `CosmosPersistence:Endpoint is not configured` messages in post-062 TRX)
**Cluster B (SpeAdmin)**: PARTIAL — 98 active SpeAdmin errors remain, all from sibling fixtures
**Cluster C (Reporting Skip)**: CLEARED — all 3 `GetStatus_ReturnsCorrectPrivilegeLevel_PerRole` rows now Pass via `IntegrationTestFixture.CreateReportingClient`

---

## Sibling-fixture inventory (8 fixtures)

| Fixture class | Inherits | File path | Likely failing classes | Cluster B share |
|---|---|---|---|---:|
| `SemanticSearchTestFixture` | `WebApplicationFactory<Program>` | `SemanticSearch/SemanticSearchIntegrationTests.cs:555` | `SemanticSearchIntegrationTests` | 22 |
| `SemanticSearchAuthorizationTestFixture` | `WebApplicationFactory<Program>` | `SemanticSearch/SemanticSearchAuthorizationTests.cs:370` | `SemanticSearchAuthorizationTests` | 14 |
| `RecordSearchTestFixture` | `WebApplicationFactory<Program>` | `SemanticSearch/RecordSearchIntegrationTests.cs:346` | `RecordSearchIntegrationTests` | 13 |
| `KnowledgeBaseTestFixture` | `WebApplicationFactory<Program>` | `Api/Ai/KnowledgeBaseEndpointsTests.cs:325` | `KnowledgeBaseEndpointsTests` | 13 |
| `AnalysisTestFixture` | `WebApplicationFactory<Program>` | `AnalysisEndpointsIntegrationTests.cs:526` | `AnalysisEndpointsIntegrationTests` | 12 |
| `ChatEndpointsTestFixture` | `WebApplicationFactory<Program>` | `Api/Ai/ChatEndpointsTests.cs:285` | `ChatEndpointsTests` | 11 |
| `ReAnalysisFlowTestFixture` | `WebApplicationFactory<Program>` | `Api/Ai/ReAnalysisFlowTests.cs:325` | `ReAnalysisFlowTests` | 8 |
| `AuthorizationTestFixture` | `WebApplicationFactory<Program>` | `AuthorizationIntegrationTests.cs:219` | `AuthorizationIntegrationTests` | 5 |
| **Total sibling failures** | | | | **98** |

| Fixture class (already inherits IntegrationTestFixture — no change needed) | | | | |
|---|---|---|---|---|
| `PrecedentAdminTestFixture` | `IntegrationTestFixture` | `Api/Insights/PrecedentAdminEndpointsTests.cs:193` | `PrecedentAdminEndpointsTests` | 1 unrelated Moq failure |
| `UploadTestFixture` | `IntegrationTestFixture` | `Api/Ai/UploadIntegrationTests.cs:487` | `UploadIntegrationTests` | 9 (different root cause) |

The `Upload` 9 failures are NOT cluster B — `UploadTestFixture` inherits `IntegrationTestFixture` and gets the Cosmos + SpeAdmin keys via inheritance. The 9 upload failures are a separate runtime category that surfaced once the host could boot. Triage them separately.

---

## Recommended follow-up task scope

Generate a new Phase 2+3 task `P23.I5` (or similar) with the following scope:

- **Files touched**: 8 sibling fixture `.cs` files (one config dict edit each — same pattern as the IntegrationTestFixture edit)
- **Net change per file**: +1 dict entry (`CosmosPersistence:Endpoint`) + verify SpeAdmin:KeyVaultUri is present; add if not
- **NFR-02 compliance**: trivially compliant (≤5 LOC per file)
- **NFR-07 anti-parallelism**: each file owns a different fixture serving disjoint test classes — fully parallel-safe across the 8 files
- **Expected outcome**: 98 Cluster B failures → 0; remaining 9 Upload failures + 1 Precedent failure surface as the next layer to triage
- **Acceptance**: post-edit TRX shows 0 `SpeAdmin:KeyVaultUri is not configured` and 0 `CosmosPersistence:Endpoint is not configured` messages

### Alternative scope (recommended): refactor to inheritance

Have all 8 sibling fixtures inherit `IntegrationTestFixture` instead of `WebApplicationFactory<Program>` directly. This eliminates the entire class of config-drift errors going forward. Each sibling fixture keeps its specific overrides (test services, additional config keys) while picking up the canonical host config from the parent.

**Risk consideration (NFR-02)**: changing the base class is structurally larger than +1 dict-entry per file but mechanically cleaner. Each sibling's diff would be: change `WebApplicationFactory<Program>` → `IntegrationTestFixture` on the class declaration line + change method overrides from `ConfigureWebHost` to `ConfigureWebHost` (still applies via `base.ConfigureWebHost(builder)` chain). ≤10 LOC change per file. NFR-02 compliant.

Decision should be made by the orchestrator / owner when the follow-up task is generated.

---

## Why task 024's triage didn't surface this

Task 024 was a TRX-based pattern-classification task that couldn't distinguish between fixtures (the TRX records test names and failure messages, not which `WebApplicationFactory` builds the failing host). The assumption that `IntegrationTestFixture.cs` was the universal entry point was the natural reading of the project's file structure — the discovery here is that test authors created per-feature fixtures to add feature-specific mocks (e.g., `KnowledgeBaseTestFixture` registers Azure AI Search mocks). Each sibling re-implemented its own `ConfigureHostConfiguration` without inheriting `IntegrationTestFixture`'s base config block.

This is a project-discoverable fact that becomes visible only once the host boots — exactly the post-fix re-run scenario task 024 anticipated in §"§6.2 end-state projections" by reserving the second-pass triage to a follow-up doc.

---

## Compliance ledger

| NFR | Compliance |
|---|---|
| **NFR-01** (no `src/`/`power-platform/`/`infra/`/`scripts/` changes) | ✅ |
| **NFR-02** (≤50% line replacement) | ✅ — 7 lines added to one file (1.6% delta) |
| **NFR-03** (no new DI registrations in tests) | ✅ |
| **NFR-09** (`repair-not-rewrite: true` in POML) | ✅ |
| **NFR-11** (-warnaserror clean) | ✅ — integration project compiles with 0 errors |
| **§4.5** (no `CustomWebAppFactory.cs` rewrite) | ✅ — different file; the §4.5 anti-parallelism guard applies to `IntegrationTestFixture.cs` as the integration analog and was honored (single agent owned the file) |
| **§6.2** (per-test trait tagging) | DEFERRED to follow-up sibling-fixture task (matches task 024's "tagging would commit prematurely" guidance) |
| **§6.3** (cite §3 measured numbers) | ✅ — cited post-062 TRX directly |

---

*End of handoff document.*
