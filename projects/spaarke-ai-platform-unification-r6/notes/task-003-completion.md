# Task 003 Completion Note — D-A-03 Persona Resolver Methods (Pillar 1)

**Date**: 2026-06-07
**Status**: ✅ Completed
**Rigor**: FULL
**Wave**: A-G1 (parallel with 002 + 004)

---

## What landed

Two new methods on `IScopeResolverService` implementing Q1 most-specific-wins persona resolution (playbook-attached > tenant CUST- > global SYS-):

- `Task<AnalysisPersona> ResolvePersonaForChatAsync(string tenantId, Guid? playbookId, CancellationToken ct)`
  Throws `InvalidOperationException` when no SYS- default exists (catastrophic seed-data failure — task 004 owns seeding per FR-04).
- `Task<AnalysisPersona?> GetEffectivePersonaAsync(string tenantId, Guid? playbookId, CancellationToken ct)`
  Returns null when no candidate matches at any precedence layer. Used when callers need explicit "no persona configured" handling.

Implementation lives in `AnalysisPersonaService.cs` (extended from task 002's skeleton), delegated via `ScopeResolverService.cs` facade. Three precedence layers each issue an OData query with `sprk_scopetype eq {value}` plus a NFR-14 SYS-/CUST- prefix filter as defense-in-depth.

---

## Files

### Modified
- `src/server/api/Sprk.Bff.Api/Services/Ai/IScopeResolverService.cs`
  +62 lines: two method signatures + comprehensive XML docs citing FR-03 / FR-04 / NFR-01 / NFR-14 / ADR-013 / ADR-014.
- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisPersonaService.cs`
  +140 lines (extended task 002's 158-line skeleton): added `ResolvePersonaForChatAsync`, `GetEffectivePersonaAsync`, and private `QueryFirstByScopeTypeAsync` helper. Region-organized.
- `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs`
  Replaced task 002's parallel-coordination `NotImplementedException` stubs (lines 390-404) with delegating calls to `_personaService.ResolvePersonaForChatAsync(...)` / `_personaService.GetEffectivePersonaAsync(...)`. Net −4 LoC.

### New
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/PersonaResolutionTests.cs` (409 lines, 13 tests)

### Not modified (already done by task 002)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` — `services.AddHttpClient<AnalysisPersonaService>()` already registered (line 364)
- `AnalysisPersona` record + `PersonaScopeType` enum (in `IScopeResolverService.cs` lines 685-770) — added by task 002
- `ListPersonasAsync` interface method + implementation — owned by task 002

---

## Parallel coordination outcome

Worktree state at task-execute start showed task 002 had partially executed in parallel:
- ✅ Added `AnalysisPersona` DTO + `PersonaScopeType` enum
- ✅ Added `ListPersonasAsync` interface method
- ✅ Created `AnalysisPersonaService.cs` skeleton with `ListPersonasAsync` impl
- ✅ Wired `AnalysisPersonaService` into `ScopeResolverService` constructor + DI module
- ✅ Stubbed `ResolvePersonaForChatAsync` / `GetEffectivePersonaAsync` with `NotImplementedException` marked "owned by task 003"

Task 003 (this task) consumed those stubs and replaced them with real implementations. **Zero method-signature conflict** as parent's coordination plan specified. Build was already green from task 002's stubs; remained green after task 003's replacements.

---

## Test evidence

```
$ dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~PersonaResolutionTests" -c Release --no-build
Passed!  - Failed: 0, Passed: 13, Skipped: 0, Total: 13, Duration: 29 ms
```

Coverage matrix:
| Spec link | Test |
|---|---|
| FR-04 (SYS- default fallback) | `GetEffectivePersonaAsync_NoOverrides_ReturnsSysDefault` |
| FR-03 (tenant CUST- > SYS-) | `GetEffectivePersonaAsync_TenantOverride_ReturnsTenantCustOverSys` |
| FR-03 (playbook > tenant > SYS-) | `GetEffectivePersonaAsync_PlaybookOverride_WinsOverTenantAndSys` |
| Q1 fall-through | `GetEffectivePersonaAsync_PlaybookBoundButMissing_FallsThroughToTenant` |
| NFR-14 CUST- filter | `GetEffectivePersonaAsync_TenantQuery_FiltersOnCustPrefix` |
| NFR-14 SYS- filter | `GetEffectivePersonaAsync_SysQuery_FiltersOnSysPrefix` |
| Performance (no wasted RT) | `GetEffectivePersonaAsync_NoPlaybookId_SkipsPlaybookQuery` |
| Total miss → null | `GetEffectivePersonaAsync_NoCandidateAtAnyLayer_ReturnsNull` |
| FR-04 throw semantics | `ResolvePersonaForChatAsync_NoSysDefault_ThrowsInvalidOperationException` |
| Happy path | `ResolvePersonaForChatAsync_SysDefaultPresent_ReturnsIt` |
| Argument validation | `ResolvePersonaForChatAsync_BlankTenantId_Throws` (Theory ×2) + `_NullTenantId_Throws` |

---

## Build + size

| Metric | Value |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ -c Release` | 0 errors, 16 warnings (matches baseline) |
| Compressed publish size | 45.50 MB |
| CLAUDE.md baseline (2026-05-26) | 45.65 MB |
| **Delta** | **−0.15 MB** (under R6 +5 MB total budget per NFR-02) |
| NFR-01 ceiling (60 MB) | unaffected |

---

## Quality gates (Step 9.5)

| Gate | Result |
|---|---|
| `code-review` | ✅ PASS — 0 critical / 0 warning / 0 suggestion / 0 AI smells across 4 files |
| `adr-check` | ✅ PASS — 17 ADRs/NFRs verified compliant; 0 warnings, 0 violations |
| ADR-010 (DI Minimalism) | ✅ No new interface; concrete `AnalysisPersonaService` injected; module-scoped registration |
| ADR-013 (AI facade boundary — refined) | ✅ Resolver is AI-internal; XML docs explicitly say "NOT exposed via PublicContracts"; no `IOpenAiClient` / `IPlaybookService` injected |
| ADR-014 (AI caching) | ✅ `tenantId` is first parameter on all resolver methods so future caching honors tenant isolation in key composition |
| NFR-01 (Conversational primacy) | ✅ XML doc explicit: "persona augments the conversational system prompt but never replaces conversational ability" |
| NFR-14 (SYS-/CUST- boundary) | ✅ OData filters include explicit `startswith(sprk_name, 'CUST-')` / `'SYS-'`; tests verify URL-level |
| BFF §10 pre-merge checklist | ✅ Placement justified, ADRs cited, publish-size verified, no new CRUD→AI dep, feature-module DI |

---

## Downstream

- **Task 005** (SprkChatAgentFactory wiring) can now call `scopeResolver.ResolvePersonaForChatAsync(tenantId, playbookId, ct)` to replace the hardcoded `BuildDefaultSystemPrompt()`.
- **Task 004** (SYS- seed row) is the operational prerequisite for `ResolvePersonaForChatAsync` to succeed in any environment — until it lands, the method will throw `InvalidOperationException` (which is the explicit FR-04 contract for missing seed data).
- **Task 002** (`GET /api/ai/scopes/personas` endpoint) is independent and parallel; main session reconciles Wave A-G1 after all three close.

---

## Decisions

1. **No caching introduced in this task** — kept scope minimal. ADR-014 framing is in place (tenantId as first parameter) so caching can layer on without breaking the public surface.
2. **`InvalidOperationException` for missing SYS- default** rather than returning a synthetic fallback persona. Rationale: this is a catastrophic seed-data failure per FR-04; a synthetic fallback would mask deployment errors and violate the FR-04 contract that today's `BuildDefaultSystemPrompt()` text is preserved verbatim via seed row.
3. **Defense-in-depth name-prefix filters** in addition to `sprk_scopetype` filter. A mis-tagged row (e.g., CUST- name but Tenant scopetype) cannot leak across SYS-/CUST- boundary. NFR-14 is enforced API-side; this is the resolver layer's contribution.
4. **`MapEntity` shared between list + resolve paths** — single mapping function avoids drift between the LIST endpoint output and resolver output.
