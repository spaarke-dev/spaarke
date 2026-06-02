# D-12 — RB-T070-03 `AnalysisChatContextResolver` dead-path: Path 1 (test-seam stub) selected

> **Date**: 2026-06-01
> **Project**: `sdap.bff.api-test-suite-repair-r2`
> **Task**: [`023-fix-rb-t070-03.poml`](../tasks/023-fix-rb-t070-03.poml)
> **Ledger entry closed**: RB-T070-03 (Phase 2 P2-W1; MEDIUM severity)
> **Owner**: `ralph.schroeder@hotmail.com` (selected Path 1 on 2026-06-01)
> **Sibling/security contact**: `dev@spaarke.com` (security review NOT required — MEDIUM per D-03)

---

## 1. Context

`AnalysisChatContextResolver` (in `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/`) is the AnalysisWorkspace SprkChat context resolver. R1 shipped it as a stub returning a non-null default `AnalysisChatContextResponse` for any string `analysisId`. R2-020 replaced the stub with a real Dataverse-bound implementation that:

1. Parses `analysisId` as a GUID via `Guid.TryParse` (returns null → 404 on failure)
2. Retrieves `sprk_analysisoutput` from Dataverse
3. Retrieves related `sprk_analysisplaybook`, scopes, entity record
4. Assembles `AnalysisChatContextResponse`

The R1 unit tests in `AnalysisChatContextEndpointsTests` used string IDs like `"analysis-stub-001"`, `"analysis-stub-deserialize"`, etc. With the R2-020 implementation in the in-process `CustomWebAppFactory`, all 7 tests fail at step 1 (`Guid.TryParse` returns false → resolver returns null → endpoint returns 404). The tests were Skip'd as RB-T070-03 (90-day fix-by, MEDIUM).

## 2. Three paths surfaced by the ledger

| Path | Description | Effort | Notes |
|---|---|---|---|
| **1 (chosen)** | Restore a test-seam stub via a config-key gate in `AnalysisChatContextResolver`. When the seam config-key is set AND the `analysisId` is not a GUID, return a canned response. | ~2-4h | Production unaffected — production never sets the seam key. Preserves the 7 tests with minimal change. |
| 2 | Wire a real Dataverse mock into `CustomWebAppFactory` returning synthetic `sprk_analysisoutput` / `sprk_analysisplaybook` entities. Removes stub dependency entirely. | ~6-8h | Requires §4.5 owner approval; higher factory complexity; closer to production behaviour. |
| 3 (archive) | Rename the 7 tests' file to `*.archived-YYYY-MM-DD` per predecessor NFR-06. Production coverage continues via AnalysisWorkspace Code Page integration tests + the 3 still-passing endpoint-mapping tests. | ~30min | Lowest-cost; reduces unit-test surface area. |

## 3. Owner decision

**Selected: Path 1 (test-seam stub)** on **2026-06-01** by **`ralph.schroeder@hotmail.com`**.

**Rationale**:
- Preserves unit-test surface area (10 of 10 tests in the class active).
- Lowest production-code risk: the seam is dormant in production (config key never set).
- Aligns with ADR-010 (DI minimalism): uses `IConfiguration` which is already in DI as a framework service — no new DI registrations added.
- Aligns with ADR-013 (AI architecture): seam stays in-process; no extraction.
- Clearly distinct from an ADR-018 kill switch — documented in code comments.

## 4. Implementation summary

### 4.1 Production change: `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/AnalysisChatContextResolver.cs`

**Net additive** (no replacement of existing code paths). Three additions:

1. **`internal const string TestStubConfigKey = "Analysis:UseStubResolver";`** — documented seam key.
2. **Constructor parameter** `IConfiguration? configuration = null` (optional with null-default for backward compatibility with pre-existing direct-construction unit tests; preserves NFR-01).
   - Sets `_testStubEnabled` from `configuration.GetValue<bool>(TestStubConfigKey, defaultValue: false)`.
3. **`ResolveAsync` entry guard**: if `_testStubEnabled && !Guid.TryParse(analysisId, out _)` → return `BuildCannedTestStubResponse(analysisId)`. Production GUID-bearing requests are never intercepted.
4. **`BuildCannedTestStubResponse`** private static helper — returns the same shape the original R1 stub produced:
   - `DefaultPlaybookName = "Default Analysis Playbook"`
   - All 7 `InlineActions` from `CapabilityToActionMap` (including `selection_revise` with `ActionType="diff"`)
   - `AnalysisContext.AnalysisId` echoes the route param

**Line replacement %**: NFR-02 compliant — the file gained ~70 lines (using-statement + XML-doc + ctor param + entry guard + helper method) on a 615-line file (~11% net addition). No existing lines replaced — the seam is purely additive.

### 4.2 Test-infrastructure change: `tests/unit/Sprk.Bff.Api.Tests/CustomWebAppFactory.cs`

One added config dictionary entry:

```csharp
["Analysis:UseStubResolver"] = "true",
```

No new DI registrations, no new mocks, no new replacements — just one config key.

### 4.3 Test transitions: `tests/unit/Sprk.Bff.Api.Tests/Api/Ai/AnalysisChatContextEndpointsTests.cs`

- Class-level comment updated: notes that ALL 10 tests are now active and RB-T070-03 is closed via Path 1 / D-12.
- 7 affected tests: `[Fact(Skip = "RB-T070-03: …")]` → `[Fact]`; `[Trait("status", "real-bug-pending-fix")]` → `[Trait("status", "repaired")]`.

### 4.4 What was NOT changed (path discipline)

- No new DI registrations (ADR-010 NFR-03 compliant).
- No `IConfiguration` change to the `CustomWebAppFactory.ConfigureTestServices` block.
- No production line replacement — purely additive.
- 3 existing pre-RB-T070-03 tests (`MapAnalysisChatContextEndpoints_MethodExists_AndIsStatic`, `GetAnalysisChatContext_WithoutAuth_ReturnsUnauthorized`, `GetAnalysisChatContext_WithAuth_DoesNotReturn404`) untouched and still green.
- The `WhenAnalysisNotFound_Returns404` test remains Skipped (its skip reason is task-021, NOT RB-T070-03 — orthogonal).

## 5. ADR analysis (Step 9.5 inputs)

| ADR | Compliance |
|---|---|
| **ADR-010 (DI minimalism)** | ✅ COMPLIANT. `IConfiguration` is a framework-provided service already in the container. No new `services.Add*()` line. No new interface created. |
| **ADR-013 (AI architecture)** | ✅ COMPLIANT. `AnalysisChatContextResolver` stays in `Services/Ai/Chat/`; no extraction; in-process latency-coupled design preserved. |
| **ADR-018 (kill switches)** | ✅ COMPLIANT (with explicit distinction). The seam is **not a kill switch** — it does NOT disable the feature and does NOT return `503`. Production GUIDs always exercise the Dataverse path; the seam only intercepts non-GUID IDs when the dev/test config key is set. Documented in code comments (line ~140-155 of the resolver) so the distinction is durable. Different from `Analysis:Enabled` which IS an ADR-018 kill switch. |
| **NFR-01 (production in scope; tests Skip→Pass only)** | ✅ COMPLIANT. Test changes limited to (a) one config key in factory, (b) Skip→Pass + trait flip on the 7 affected tests. No new test logic added. Pre-existing direct-construction tests (`AnalysisChatContextResolverTests`, `ContextResolverIntegrationTests`) NOT modified — the optional constructor parameter preserved their call sites byte-for-byte. |
| **NFR-02 (<50% per file line replacement)** | ✅ COMPLIANT. ~11% net addition to the resolver; ~0.4% addition to the factory; ~3-4% changes to the test file (Skip removal + trait swap). |
| **NFR-04 (commit cites RB-T070-03 + mode)** | Pending — commit performed by the main session after this task hands off. Recommended message: `fix(ai/chat): restore unit-test seam in AnalysisChatContextResolver (RB-T070-03; repaired; D-12)` |

## 6. Verification

- BFF build: `dotnet build src/server/api/Sprk.Bff.Api/ --nologo` → **0 errors** (17 pre-existing warnings).
- Tests build: `dotnet build tests/unit/Sprk.Bff.Api.Tests/ --nologo` → **0 errors** (17 pre-existing warnings).
- Targeted test run: `dotnet test … --filter "FullyQualifiedName~AnalysisChatContextEndpoints|FullyQualifiedName~AnalysisChatContextResolver" --no-build`
  - **40 Passed, 0 Failed, 1 Skipped** (the 1 Skip is `WhenAnalysisNotFound_Returns404`, task-021 — NOT RB-T070-03).
  - The 7 affected tests transitioned Skip → **Pass**.

## 7. Ledger transition

`projects/sdap-bff.api-test-suite-repair/ledgers/real-bug-ledger.md` RB-T070-03 entry:
- Status: `open` → **`repaired`**
- Notes: Cite this commit (TBD by main session), Path 1, D-12.

## 8. Files modified

| File | Change | NFR-02 line-replacement % |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/AnalysisChatContextResolver.cs` | + 1 using; + 1 const + XML doc; + 1 field; + 1 optional ctor param + setter logic; + entry guard in `ResolveAsync`; + private static helper `BuildCannedTestStubResponse` | ~11% net addition; 0% replacement |
| `tests/unit/Sprk.Bff.Api.Tests/CustomWebAppFactory.cs` | + 1 config-dict entry (`Analysis:UseStubResolver`) + comment | ~0.4% addition |
| `tests/unit/Sprk.Bff.Api.Tests/Api/Ai/AnalysisChatContextEndpointsTests.cs` | Class-level comment updated; 7× `[Fact(Skip=…)]` → `[Fact]`; 7× `Trait("real-bug-pending-fix")` → `Trait("repaired")` | ~3-4% replacement |

Total files modified: **3** (1 production, 2 test infrastructure).

## 9. Open items / handoff to main session

1. Commit (NFR-04): the main session commits with a message citing `RB-T070-03` + `repaired` + `D-12`.
2. Ledger transition: main session edits `real-bug-ledger.md` RB-T070-03 entry → `repaired` and links this commit.
3. TASK-INDEX.md: flip 023 row from 🔲 → ✅.
4. Task POML `<metadata><status>` → `completed`.

---

*This decision is binding for RB-T070-03 closure. If a future task requires deeper Dataverse-mock infrastructure (Path 2 territory) for OTHER resolver tests, file a new ledger entry — do NOT extend this seam, since seam scope is intentionally narrow.*
