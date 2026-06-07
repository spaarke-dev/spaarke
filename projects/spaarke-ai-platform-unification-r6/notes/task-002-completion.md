# Task 002 Completion Notes — D-A-02 Add `GET /api/ai/scopes/personas` BFF Endpoint

> **Project**: spaarke-ai-platform-unification-r6
> **Task**: 002 (Phase A, Wave A-G1, Pillar 1)
> **Status**: ✅ Completed
> **Completed**: 2026-06-07
> **Rigor Level**: FULL

---

## Summary

Added the `GET /api/ai/scopes/personas` BFF endpoint, the CRUD-side LIST surface for the new `sprk_aipersona` Dataverse entity (created in task 001). The endpoint mirrors the 4 canonical scope endpoints (`/skills`, `/knowledge`, `/tools`, `/actions`) in registration, pagination/filter/sort contract, and authorization. Registration is symmetric with the consuming endpoint — both inside the compound `Analysis:Enabled && DocumentIntelligence:Enabled` gate.

---

## Files Modified

### New files (3)

- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisPersonaService.cs` — Focused service for `sprk_aipersona` LIST operations + the task-003 `Resolve*Persona` methods (added by task 003 in-flight, in the same file).
- `tests/unit/Sprk.Bff.Api.Tests/Api/Ai/ScopePersonasEndpointTests.cs` — 12 endpoint contract tests (registration, auth, pagination, filter, sort, parity with sibling endpoints).
- `projects/spaarke-ai-platform-unification-r6/notes/task-002-completion.md` — this file.

### Modified files (4)

- `src/server/api/Sprk.Bff.Api/Services/Ai/IScopeResolverService.cs` — Added `AnalysisPersona` record (thin DTO mirroring the entity fields) + `PersonaScopeType` enum (Global/Tenant/PlaybookAttached) + `ListPersonasAsync` method on the interface. Task 003 added the `Resolve*Persona` interface methods in parallel.
- `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs` — Added `AnalysisPersonaService` ctor injection + `_personaService` field + `ListPersonasAsync` delegation + throw-stubs for task 003's `Resolve*Persona` methods (clearly marked as task-003-owned; build-green-only stubs).
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` — Registered `AnalysisPersonaService` as `AddHttpClient<AnalysisPersonaService>()` sibling to `AnalysisActionService`/`AnalysisSkillService` (inside compound AI gate; symmetric with the endpoint mapping).
- `src/server/api/Sprk.Bff.Api/Api/Ai/ScopeEndpoints.cs` — Added `GET /personas` route + `ListPersonas` handler mirroring `ListActions` exactly.
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/ScopeResolverServiceTests.cs` — Updated 3 `ScopeResolverService` ctor call sites to pass the new `AnalysisPersonaService` arg (per BFF test-update obligation, `bff-extensions.md` §F).

---

## Quality Gates (Step 9.5)

### Build
- `dotnet build src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj`: **0 errors, 16 warnings** (all pre-existing in unrelated files).
- `dotnet build tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj`: **0 errors**.

### Tests
- New `ScopePersonasEndpointTests`: **12/12 PASS** (3 s).
- Existing `ScopeResolverServiceTests`: **19/19 PASS** (139 ms) — no regression from ctor signature change.

### ADR Compliance (manual `adr-check` walk-through)

| ADR | Verdict | Evidence |
|-----|---------|----------|
| **ADR-008** (endpoint filters) | ✅ PASS | Endpoint inherits `.RequireAuthorization()` from the `MapScopeEndpoints` group — same filter as the 4 sibling endpoints. Symmetric. |
| **ADR-010** (DI minimalism) | ✅ PASS | `AnalysisPersonaService` registered inside existing `AnalysisServicesModule.AddAnalysisOrchestrationServices`. ZERO new top-level `Program.cs` lines. Endpoint mapped via existing `MapScopeEndpoints` in `EndpointMappingExtensions.cs`. |
| **ADR-013** (AI architecture facade boundary) | ✅ PASS | `AnalysisPersonaService` extends `DataverseHttpServiceBase` (HTTP-only). Endpoint injects only `IScopeResolverService` (existing CRUD-side surface). NO injection of `IOpenAiClient` / `IPlaybookService` / `IPlaybookOrchestrationService` / other AI internals. |
| **ADR-016** (AI rate limits) | ⚠️ **DEVIATION** | The 4 canonical scope endpoints (`/skills`, `/knowledge`, `/tools`, `/actions`) do **NOT** apply `RequireRateLimiting("ai-context")`. My new endpoint follows that parity per the task POML `<pattern>` directive ("Clone the registration for one of the existing 4 scope types"). Task POML `<constraint source="ADR-016">` conflicts with the sibling-parity directive. **Decision**: parity wins. If ADR-016 should apply, it must apply to all 5 scope endpoints atomically (separate task). |
| **ADR-018** (feature flags) | ✅ PASS | NO new feature flag. Endpoint maps unconditionally within the existing compound AI gate. Service registration inherits the same gate. |
| **ADR-029** (BFF publish hygiene) | ✅ PASS | Compressed publish size: **45.50 MB** (delta vs baseline 45.49 MB: **+0.010 MB / +10 KB**). Well under R6 single-task threshold (+1 MB) and cumulative budget (+5 MB). Far below the 60 MB hard ceiling. |
| **bff-extensions.md §A** (placement) | ✅ PASS | Endpoint is the canonical CRUD-side LIST surface for a scope catalog entity — by definition belongs in BFF alongside the 4 sibling scope endpoints. |
| **bff-extensions.md §F** (test obligation) | ✅ PASS | New test class added (12 tests); existing `ScopeResolverServiceTests` updated for ctor signature change. |
| **bff-extensions.md §F.1** (asymmetric-registration) | ✅ PASS | Registration of `AnalysisPersonaService` (inside compound gate) is symmetric with the endpoint mapping (`MapScopeEndpoints` is mapped inside the same compound gate in `EndpointMappingExtensions.cs`). No new unconditional consumer of `AnalysisPersonaService` exists. |

### Spec compliance

- **FR-02 (D-A-02)**: ✅ Endpoint exists, returns `ScopeListResult<AnalysisPersona>`, mirrors sibling pagination/filter/sort.
- **NFR-02 (publish-size)**: ✅ +10 KB delta — well within budget.
- **NFR-03 (ADR compliance)**: ✅ NO new ADRs introduced; one explicit deviation flagged on ADR-016 (parity-with-siblings).

---

## BFF Publish-Size Evidence (ADR-029)

| Stage | Path | Compressed Size |
|-------|------|-----------------|
| **Baseline** (pre-task) | `deploy/api-publish-baseline-002.zip` | **45.49 MB** |
| **Post-task 002** | `deploy/api-publish-task-002.zip` | **45.50 MB** |
| **Delta** | — | **+0.010 MB (+10 KB)** |

Method: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o <out>` → `Compress-Archive` (Optimal) → measure `Length / 1MB`.

Compliance:
- ✅ Single-task delta ≤ +1 MB: **+0.010 MB** (1000× margin)
- ✅ Cumulative R6 budget ≤ +5 MB: **+0.010 MB**
- ✅ Hard ceiling ≤ 60 MB: **45.50 MB** (14.5 MB headroom)

---

## Decisions Made

1. **2026-06-07 — Persona service shape**: Created a focused `AnalysisPersonaService` (sibling to `AnalysisActionService` / `AnalysisSkillService` / etc.) rather than putting LIST logic directly in `ScopeResolverService`. Reason: matches the post-PPI-054 god-class decomposition pattern. Smaller diff to `ScopeResolverService` (delegation only).

2. **2026-06-07 — IScopeResolverService surface coordination**: Added ONLY `ListPersonasAsync` to the interface (per task POML coordination). Task 003 added `ResolvePersonaForChatAsync` + `GetEffectivePersonaAsync` in parallel. I added throw-stub implementations in `ScopeResolverService.cs` for the task-003 methods to keep the build green during the parallel wave window. Task 003 owns the real bodies (and has already implemented them on `AnalysisPersonaService` in the same file).

3. **2026-06-07 — ADR-016 rate-limit deviation**: The 4 canonical scope endpoints do NOT apply `RequireRateLimiting("ai-context")`. I followed sibling parity per task POML `<pattern>` directive. Task POML `<constraint source="ADR-016">` conflicts with this. **Recommended follow-up**: file a separate task to apply `ai-context` rate limit to all 5 scope endpoints atomically — single-endpoint application would be inconsistent and confusing for operators.

4. **2026-06-07 — Persona DTO ownership properties**: Mirrored the 4-scope pattern (`OwnerType`, `IsImmutable`, `ParentScopeId`, `BasedOnId` minus `BasedOnId` since persona has no Save-As yet). Used `PersonaScopeType` enum with the exact Dataverse CHOICE values (100000000/100000001/100000002) verified via task 001's MCP describe_table.

5. **2026-06-07 — SYS-/CUST- prefix enforcement**: API-side only via `EnsureCustomerPrefix` / name-prefix check in `MapEntity`. Matches the 4-scope pattern; Dataverse has no enforcement. Per task POML "from task 001 finding".

---

## Coordination with Task 003 (Parallel Wave)

- Task 003 added two interface methods (`ResolvePersonaForChatAsync`, `GetEffectivePersonaAsync`) to `IScopeResolverService` in parallel.
- Task 003 also implemented those methods inside `AnalysisPersonaService.cs` (the file I created).
- My `ScopeResolverService.cs` has throw-stub delegation for these methods, clearly marked as task-003-owned. **Task 003 must update `ScopeResolverService.cs` to delegate `Resolve*Persona` calls to `_personaService`** before any caller (task 005) exercises the resolution path.
- No interface conflicts — additive changes only.
- No file-overlap conflicts — task 003 wrote into the persona service body (which I created); I did NOT touch `Resolve*Persona` logic.

---

## Bookkeeping Updates (per coordination instructions — parallel wave pattern)

- ✅ NOT touching `current-task.md` — main session reconciles after wave.
- ✅ Updated own row only in `TASK-INDEX.md`: 002 🔲 → ✅.
- ✅ Wrote this completion summary at `projects/spaarke-ai-platform-unification-r6/notes/task-002-completion.md`.
- ✅ Updated POML status: `<status>not-started</status>` → `<status>completed</status>` + added `<completed>` + `<completion-notes>`.

---

## Open Items / Follow-Ups

1. **Task 003 coordination**: must update `ScopeResolverService.cs` to replace my throw-stubs with delegation to `_personaService.ResolvePersonaForChatAsync` / `_personaService.GetEffectivePersonaAsync`. Build is currently green because no caller invokes these stubs yet (task 005 is the first caller).

2. **ADR-016 rate-limit gap**: optional follow-up task to apply `RequireRateLimiting("ai-context")` atomically to all 5 scope LIST endpoints. Current state is consistent (none apply it); changing one would be inconsistent.

3. **Integration smoke test**: deferred to Phase A integration test (task 028) per the task POML "if test infrastructure permits" clause. The 12 unit tests cover the contract-layer; the integration test will exercise the full Dataverse round-trip once a real seed row exists (task 004).
