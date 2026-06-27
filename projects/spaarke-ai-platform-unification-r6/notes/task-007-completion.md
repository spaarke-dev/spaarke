# Task 007 (D-A-07) — Completion Note

> **Status**: ✅ Complete
> **Wave**: A-G3 (Pillar 2 tool registry extensions)
> **Date**: 2026-06-07
> **Rigor**: FULL (Dataverse production entity schema change + DTO mod + downstream blocker for 008/010/011)
> **Predecessor**: 006 ✅ (IToolHandler rename)
> **Sibling (deferred)**: 008 (JsonSchema field — same DTO; serial per parallel-safe=false)

---

## What was added

### C# DTO surface

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/IScopeResolverService.cs`

- New `ToolAvailabilityContext` enum (plain, NOT `[Flags]`):
  - `Playbook = 100000000` (default — backward-compat for all pre-R6 rows)
  - `Chat = 100000001`
  - `Both = 100000002`
- New `AnalysisTool.AvailableInContexts` nullable property (FR-07 discriminator).

Plain-enum choice rationale: Dataverse single-select picklist semantics require an explicit `Both` value rather than bit composition. Spec FR-07 enumerates three discrete values, not flag combinations.

### Service mapper

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisToolService.cs`

- `ToolEntity.AvailableInContexts` JSON property added (nullable int? for backward-compat with rows whose column isn't yet populated).
- Added `sprk_availableincontexts` to OData `$select` in `ListToolsAsync`.
- New `MapAvailableInContexts(int?)` helper — known values map to enum; null + unknown → null (callers apply Playbook fallback at resolve time per FR-07).
- Wired the mapper into 3 `AnalysisTool` construction sites: `GetToolAsync`, `ListToolsAsync`, `CreateToolAsync`.

### Dataverse deployment script

**File**: `scripts/Add-AnalysisToolAvailableInContexts.ps1` (NEW)

- Adds option-set column `sprk_availableincontexts` to existing `sprk_analysistool` entity.
- `DefaultFormValue = 100000000` (Playbook) → all pre-R6 rows continue to behave as playbook tools (FR-07 backward-compat invariant).
- Idempotent: `Test-AttributeExists` guard. Safe to re-run.
- `-DryRun` mode for preview.
- Publishes customizations after column add.
- Modeled on `Create-AiPersonaEntity.ps1` (canonical exemplar from task 001).

### Tests

**File**: `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisToolDtoTests.cs` (NEW)

18 tests across 6 concerns:
1. Enum value contract (3 tests) — each value matches its canonical Spaarke option-set number
2. Enum cardinality (1 test) — exactly 3 values; adding a 4th amends FR-07
3. DTO default state (1 test) — `AvailableInContexts` defaults to `null` for pre-R6 rows
4. Round-trip JSON serialization (4 tests — `[Theory]` × 3 enum values + 1 null) — wire contract preserved
5. Mapper coverage (8 tests — null + 3 known values + 4 unknown values) — backward-compat + forward-compat
6. Record `with` expression preserves the field (1 test) — record member, not orphaned field

---

## Deployment evidence

### Spaarke Dev (https://spaarkedev1.crm.dynamics.com)

```
Step 1: Verifying sprk_analysistool entity exists...
  sprk_analysistool found
Step 2: Adding/verifying sprk_availableincontexts column...
  Adding attribute: sprk_availableincontexts...
    Added: sprk_availableincontexts
Step 3: Publishing customizations...
  Customizations published
```

Post-deploy idempotency check (re-ran `-DryRun`):

```
Step 2: Adding/verifying sprk_availableincontexts column...
  sprk_availableincontexts already exists, skipping
```

---

## Quality gates

| Gate | Result |
|------|--------|
| Build (Debug) | 0 errors / 0 warnings |
| Build (Release publish) | OK |
| BFF unit tests | 6141 pass / 0 fail / 109 skipped (no regression) |
| AnalysisToolDtoTests (new) | 18 pass / 0 fail |
| code-review (inline FULL) | PASS — naming, nullability, magic-number elimination, XML docs cite FR-07/D-A-07, no DI changes, no PublicContracts changes, idempotent script |
| adr-check | PASS — ADR-010 (zero new DI), ADR-013 (no PublicContracts modifications), ADR-027 (sprk_ prefix + unmanaged + idempotent), ADR-029 (size verified) |

---

## BFF publish-size verification (NFR-02 / ADR-029)

| Metric | Value |
|--------|-------|
| Baseline (compressed) | 45.5 MB |
| Post task 007 (compressed) | 45.5 MB |
| Delta | **0.00 MB** |
| R6 budget remaining | ≤+5 MB |
| Hard ceiling | 60 MB |

Zero delta — DTO + service mapper changes are LOC-only; no new packages or dependencies.

---

## FR-07 backward-compat invariant — verified

> "Existing playbook tool rows default to `Playbook`. New chat tools declare `Chat` or `Both`."

Honored at three layers:
1. **Dataverse**: `DefaultFormValue = 100000000` on the column.
2. **DTO**: `AvailableInContexts` is nullable → unpopulated rows deserialize as null.
3. **Mapper + downstream resolve**: null in mapper → null in DTO → callers (task 011 chat resolver) treat null as Playbook (NOT exposed to chat agent).

Every existing `sprk_analysistool` row continues to behave exactly as it did before R6 task 007.

---

## Decisions logged

| ID | Decision | Reason |
|----|----------|--------|
| D-007-01 | Plain enum, not `[Flags]` | Dataverse single-select picklist + spec FR-07 enumerates 3 discrete values. `Both` is explicit, not `Playbook \| Chat` bitwise. |
| D-007-02 | DTO field nullable, not non-nullable with default | Migration safety — distinguishes "not yet populated" (null → fall back to Playbook at resolve time) from "explicitly Playbook" (enum value). Equivalent at runtime today but preserves observability if FR-07 evolves. |
| D-007-03 | Enum + DTO co-located in `IScopeResolverService.cs` | The record is already authoritative there. Task 008 (JsonSchema) extends same record without forcing a partial-class split. Avoids premature file refactor. |
| D-007-04 | Mapper returns null for unknown raw values (not throw) | Forward-compat — if an admin adds a new picklist option that BFF doesn't yet know about, we degrade gracefully (treat as Playbook), not 500 on every tool list call. |

---

## What's unblocked

- **Task 008** (`JsonSchema` field on same DTO + entity) — DTO surface ready; script pattern proven; entity confirmed targetable. Same wave.
- **Task 010** (`ToolHandlerToAIFunctionAdapter`) — depends on 007+008+009; can begin DTO contract design once 008 lands.
- **Task 011** (`ResolveTools()` reads `sprk_analysistool` rows) — discriminator field now queryable.
- **Task 012** (Q9 BIG-BANG batch migration) — new chat tool rows can now set `AvailableInContexts = Chat`.

---

## Files modified

| Path | Change |
|------|--------|
| `src/server/api/Sprk.Bff.Api/Services/Ai/IScopeResolverService.cs` | +50 LOC (enum + property + XML docs) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisToolService.cs` | +35 LOC (ToolEntity field, mapper, $select, 3 construction sites) |
| `scripts/Add-AnalysisToolAvailableInContexts.ps1` | NEW (~280 LOC, idempotent + dry-run) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisToolDtoTests.cs` | NEW (~180 LOC, 18 tests) |
| `projects/spaarke-ai-platform-unification-r6/tasks/TASK-INDEX.md` | 007 row 🔲 → ✅ |
| `projects/spaarke-ai-platform-unification-r6/tasks/007-add-availableincontexts-enum.poml` | status → completed; completion-notes |
| `projects/spaarke-ai-platform-unification-r6/notes/task-007-completion.md` | NEW (this file) |
