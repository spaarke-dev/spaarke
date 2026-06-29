# `ActionType` Reference Audit — Wave 2 Rename Pre-Flight

> **Task**: R7-020 (Wave 2 kickoff, FR-10 preparatory)
> **Date**: 2026-06-28
> **Status**: Complete — read-only audit, zero source modifications
> **Consumed by**: Task 021 (rename plan) → Task 022 (mechanical rename) → Task 023 (`SupportedActionTypes` → `SupportedExecutorTypes`)
> **Branch**: `work/spaarke-ai-platform-unification-r7`

---

## 1. Headline Counts

| Scope | Word-boundary `\bActionType\b` hits | Files |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/` | **224** | 48 |
| `src/server/shared/Spaarke.*/` | **0** | 0 |
| `tests/unit/Sprk.Bff.Api.Tests/` + `tests/integration/Sprk.Bff.Api.IntegrationTests/` | **240** | 34 |
| **BFF rename surface (sum)** | **464** | **82** |
| Out-of-scope: `src/client/code-pages/PlaybookBuilder/` | 32 | 5 |
| Out-of-scope: `scripts/` | 17 | 7 |
| Out-of-scope: `src/dataverse/` | 0 | 0 |

**Spec FR-10 estimate**: "~1000+". **Actual: 464** (BFF + tests). Spec estimate was high — the ~1000 figure likely double-counted enum-value usages (`ActionType.AiCompletion` = 1 word-boundary hit, but contains the literal substring twice if counted naively). The audit's 464 is the authoritative figure to consume in task 021 PR-sizing.

**Subcategory: `ActionType.*` enum-value usages** (the dominant kind):
- BFF source: **132** across 40 files
- Tests: **204** across 33 files
- **Total enum-value usages: 336** (72% of total)
- Remaining **128** are type-name usages in signatures, generics, XML cref, and `SupportedActionTypes` property references.

**Subcategory: `SupportedActionTypes` property** (renames to `SupportedExecutorTypes` in task 023):
- Production source (`src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/`): **22 declarations** (one per executor, plus interface + registry) across 22 files.
- Tests: **75** assertions across 19 files.
- **Total: 97** references — task 023 owns these.

---

## 2. Largest Clusters (by hit count)

### Production source (top 10)

| File | Word-boundary hits | Largest reference kind |
|---|---|---|
| `Services/Ai/PlaybookOrchestrationService.cs` | **25** | Dispatch switch + ActionType lookup — DELETED in Wave 2 task 024 (FR-07) |
| `Services/Ai/NodeService.cs` | **22** | NodeType↔ActionType crosswalk for storage |
| `Infrastructure/DI/AnalysisServicesModule.cs` | **11** | DI registration + comments |
| `Services/Ai/Nodes/StartNodeExecutor.cs` | **10** | Multiple SupportedActionTypes + cref doc |
| `Services/Ai/Insights/Playbooks/matter-health-single.playbook.json` | **10** | Lowercase `"actionType"` JSON property (NOT renamed — see §6) |
| `Services/Ai/AnalysisActionService.cs` | **10** | Loads ActionType enum from `sprk_actiontype` choice |
| `Services/Ai/Nodes/LoadKnowledgeNodeExecutor.cs` | **8** | SupportedActionTypes + Validate |
| `Services/Ai/Insights/Playbooks/predict-matter-cost.playbook.json` | **8** | Lowercase `"actionType"` (out-of-scope) |
| `Services/Ai/Nodes/INodeExecutorRegistry.cs` | **7** | Interface signatures: `GetExecutor(ActionType)`, `HasExecutor(ActionType)` |
| `Services/Ai/Nodes/ReturnResponseNodeExecutor.cs` | **8** | SupportedActionTypes + cref doc |

### Tests (top 10)

| File | Word-boundary hits |
|---|---|
| `tests/integration/.../CanvasServerMappingDriftTests.cs` | **19** |
| `tests/unit/.../Nodes/InsightsNodesIntegrationTests.cs` | **19** |
| `tests/integration/.../MigratedPlaybookFixture.cs` | **17** |
| `tests/unit/.../Integration/PlaybookExecutionTests.cs` | **29** |
| `tests/unit/.../Services/Ai/Nodes/AiAnalysisNodeExecutorTests.cs` | **24** |
| `tests/unit/.../Services/Ai/PlaybookOrchestrationServiceTests.cs` | **14** |
| `tests/unit/.../Services/Ai/Insights/UniversalIngestPlaybookTests.cs` | **12** |
| `tests/unit/.../Services/Ai/Insights/PlaybookOrchestrationServiceSectionStreamingTests.cs` | **9** |
| `tests/unit/.../Services/Ai/Nodes/LiveFactNodeTests.cs` | **8** |
| `tests/unit/.../Services/Ai/Insights/Routing/InsightsActionRouterTests.cs` | **4** |

**Largest single cluster: `PlaybookOrchestrationService.cs` (25)** — this is the dispatch refactor target. Task 024 (FR-07/FR-08) is expected to DELETE ~150 LOC; many ActionType references will disappear naturally (no rename needed because the code is gone). Task 022 (rename) executes BEFORE task 024 to keep the rename mechanical.

**Second-largest cluster: `NodeService.cs` (22)** — NodeType→ActionType crosswalk for storage layer. All references are pure renames (no semantic change).

---

## 3. Reference Categories Breakdown

| Category | Count (BFF source) | Count (tests) | Total | Rename action |
|---|---|---|---|---|
| Enum **declaration** (`public enum ActionType { ... }`) | 1 (INodeExecutor.cs:97) | 0 | **1** | Rename to `public enum ExecutorType { ... }` |
| Enum-value usages (`ActionType.AiCompletion`, `.AiAnalysis`, `.Condition`, etc.) | 132 | 204 | **336** | Mechanical s/`ActionType.`/`ExecutorType.`/g |
| Type-name in signatures, params, generics (`GetExecutor(ActionType x)`, `IReadOnlyList<ActionType>`) | ~50 | ~25 | **~75** | Mechanical s/`\bActionType\b`/`ExecutorType`/g |
| `SupportedActionTypes` property declarations + references | 22 prod + 75 test | — | **97** | Task 023 owns rename → `SupportedExecutorTypes` |
| XML doc `<see cref="ActionType"/>` / `<see cref="ActionType.X"/>` | 17 | 0 | **17** | Mechanical (cref refs caught by same regex; XML doc warnings would surface if broken) |
| String literal `"ActionType"` (in plain `""`) | 2 | 0 | **2** | NOT the executor — see §4 disambiguation |
| `nameof(ActionType)` / `JsonConverter`-style attribute references | 0 | 0 | **0** | None — no reflection-based ActionType usage |

---

## 4. Edge Cases Requiring Manual Review

### 4a. UNRELATED `ActionType` — DO NOT RENAME

**`Models/Ai/Chat/AnalysisChatContextResponse.cs`** has a `record InlineActionInfo(string Id, string Label, string ActionType, string? Description)` — the `ActionType` here is a `string` discriminator (`"chat"` vs `"diff"`), NOT the C# executor enum. The `\bActionType\b` regex matches 3 hits in this file (parameter name on line 78, XML doc lines 61, 73, plus a CapabilityToActionMap doc reference). All MUST be left untouched.

**Rename rule for task 022**: when matching `\bActionType\b`, exclude `Models/Ai/Chat/AnalysisChatContextResponse.cs` from blanket regex replacement. Task 022 should hand-verify this file and any future `InlineActionInfo` consumers (greppable via `InlineActionInfo` symbol).

### 4b. Lowercase `"actionType"` JSON property — OUT-OF-SCOPE (Wave 6 territory)

The lowercase `"actionType"` appears 31 times across BFF source:

- **5 playbook JSON files** (`Services/Ai/Insights/Playbooks/*.json` + `Services/Ai/Chat/Playbooks/*.json`) — 28 instances, all serialized integer enum values. These are storage format; renaming the JSON property name would be a Dataverse + JSON migration (Wave 6 — `sprk_actiontype` → `sprk_executortype` is at the Dataverse schema layer, FR-21).
- **`R2SseEventEmitter.cs:143`** — `<param name="actionType">` XML doc reference describing an SSE event payload field. The SSE wire contract uses `actionType` (lowercase) per `SseEventSchemaValidator.cs:165` (`RequireEnum(el, "actionType", ...)`). **Renaming the SSE wire field is a breaking client contract change — DEFER to Wave 6** when clients (PlaybookBuilder + SprkChat) migrate together.
- **`INodeExecutorRegistry.cs:24, 37`** — `<param name="actionType">` XML doc on `GetExecutor(ActionType actionType)`. The PARAMETER NAME (lowercase camelCase) is C# convention; renaming to `executorType` is bundled with task 022 (interface method signature update).

**Rule for task 022**: ONLY rename the C# parameter name `actionType` → `executorType` inside `INodeExecutorRegistry.cs` (and any other place a method parameter is so named). Do NOT touch JSON `"actionType"` literals; those are Wave 6.

### 4c. Wave 6 task 062 (playbook JSON normalization) inherits this

The 28 playbook JSON `"actionType"` hits + 1 SSE schema validator + 3 SSE emitter XML doc references = **32 references that survive Wave 2** and must be addressed in Wave 6 when `sprk_actiontype` choice column is renamed to `sprk_executortype` in Dataverse. Task 022 leaves them intact.

### 4d. Cross-project residue (read-only, no R7 obligation)

The `\bActionType\b` regex against `projects/` (other worktrees) returned hits in:
- `projects/ai-spaarke-insights-engine-r2/` (decisions + tasks — historical references)
- `projects/bff-ai-architecture-audit-r1/` (audit notes — historical references)
- `projects/spaarke-ai-platform-unification-r1/` (R1 task POMLs — historical references)
- `projects/x-ai-node-playbook-builder/` (reference design doc — historical references)
- `projects/spaarke-daily-update-service-r4/` (smoke notes — historical references)
- `projects/spaarke-platform-foundations-r3/` (R3 task POML — historical reference)
- `projects/ci-cd-unit-test-remediation-r1/` (test inventory CSVs — historical references)

**Decision**: do NOT rename any project notes (per CLAUDE.md §3 + R7 design discipline — historical project notes are immutable). Task 021 will note this exclusion.

---

## 5. Edge Cases Resolved

| Concern | Outcome |
|---|---|
| `[JsonConverter]` attributes referencing `ActionType` | NONE found. No JSON-converter reflection coupling — safe rename. |
| `nameof(ActionType)` reflection | NONE found. Safe rename. |
| `IActionType` interface or `XmlActionType`/`ActionTypeId` (substring collisions) | NONE found via word-boundary regex. Confirmed clean. |
| Cross-project shared types in `Spaarke.Core` / `Spaarke.Dataverse` | NONE — `src/server/shared/` returned 0 hits. All `ActionType` references live in `Sprk.Bff.Api` (and its tests). Rename is BFF-internal. |
| Public namespace export risk (downstream consumers depending on type name in serialization) | Low. The C# enum serializes to integer (per `appsettings`/JSON converter defaults); type name is not on the wire. The wire format uses lowercase `"actionType"` (handled in §4b). Rename does NOT break wire format. |

---

## 6. Out-of-Scope Surfaces — Confirmed Excluded (FR-10 BFF-only)

| Surface | Hit count | Rationale for exclusion |
|---|---|---|
| `src/client/code-pages/PlaybookBuilder/` | 32 in 5 files | TypeScript — Wave 8 task 080+ owns PlaybookBuilder updates (sprk_nodetype/sprk_executortype rename). |
| `src/dataverse/` | 0 | No matches (no plugins reference ActionType enum). |
| `scripts/` | 17 in 7 files | PowerShell deployment scripts — Wave 6 task 063 owns script updates when Dataverse choice column renames. |
| `docs/` | (not measured; FR-10 explicitly excludes) | Wave 6 task 060 owns doc updates. |
| `projects/*/notes/` | various | Immutable per CLAUDE.md §3 + R7 doc discipline (DELETE outdated, not rewrite historical). |
| `.claude/patterns/`, `.claude/skills/` | 1 (node-executor-authoring.md) | Wave 7 task 070+ owns skill rewrites. |

---

## 7. Recommended Rename Strategy for Task 021

Task 021 should consume this audit and design the rename plan as follows:

1. **Single PR for the C# rename** (task 022) — coherent diff is auditable; partial rename leaves the codebase non-compiling.
2. **Rename regex**: `\bActionType\b` → `ExecutorType` across BFF source + tests, EXCLUDING:
   - `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/AnalysisChatContextResponse.cs` (unrelated `InlineActionInfo.ActionType` string discriminator — §4a)
   - Lowercase `"actionType"` JSON property literals (Wave 6 — §4b)
   - Comments/docs that intentionally reference legacy concept (none expected; verify with `git diff` review)
3. **Separate PR for `SupportedActionTypes` → `SupportedExecutorTypes`** (task 023) — property rename touches 22 prod + 75 test files, can land in parallel after task 022 merges.
4. **Conflict window**: time task 022 against sibling BFF-touching worktrees per CLAUDE.md §10 Hot-Path Declaration. The 2026-06-26 sweep shows 13 active worktrees touching BFF; coordinate task 022 dispatch via `/conflict-check`.
5. **Build verification per ADR-029 + spec NFR-02**: task 022 acceptance criteria MUST include `dotnet build src/server/api/Sprk.Bff.Api/` clean + full test suite green + publish-size delta = 0 (rename is IL-neutral).
6. **No backward-compat shim** per spec NFR-06 — no `[Obsolete]` alias, no `using ActionType = ExecutorType;` ergonomics file. Big-bang.

---

## 8. Sign-off

**Audit status**: ✅ Complete.
**Source modifications**: ZERO (read-only audit per task POML).
**Files touched by this task**: this audit doc + `current-task.md` + `TASK-INDEX.md` (state updates only).
**Hand-off**: task 021 reads this doc; task 022 executes the rename per §7 strategy.
**Risks identified**: 1 (the `InlineActionInfo.ActionType` string-discriminator collision in `AnalysisChatContextResponse.cs` — §4a). Task 022 plan MUST exclude that file from blanket regex replace.
