# Task 074 evidence — Per-turn agent prompt builder gathers visible state (D-C-29 + D-C-30)

**Pillar / Spec ref**: R6 Pillar 9 / D-C-29 (FR-58) + D-C-30 (FR-59) — refine the
workspace state block in `SprkChatAgentFactory.CreateAgentAsync` (added in task 053 with
a minimal text summary) to gather rich FR-57 per-tab visible state from each
Assistant-visible tab, with the binding privacy filter `tab.visibleToAssistant === true
AND widget has derivable visible state` (BOTH required).
**Wave**: C-G18 sequential after 053 + 073.
**Date**: 2026-06-18.

---

## Architectural decision — OPTION A (server-side derivation) chosen

The POML implied the frontend would push serialized visible state to BFF via the
workspace state model, but inspection of the current shape shows the C# model
(`WorkspaceTab.cs` + `WorkspaceTabWidgetData.cs`) does **not** carry a separate
`VisibleState` field — it carries the typed-discriminated `WidgetData` payload only.

Two paths were evaluated:

| Aspect | OPTION A (server-derive) | OPTION B (persist) |
|---|---|---|
| Schema impact | **ZERO** | New `VisibleState` field on `WorkspaceTab.cs` + migration |
| Frontend wiring | **ZERO** | Per-widget `getAgentVisibleState()` invoked at persist; payload extended |
| Source of truth | Server reads `WidgetData` (closed 4-variant union) | Frontend `getAgentVisibleState()` (task 073) |
| Duplication risk | Per-widget mapping logic in both client (task 073) and server | Single source |
| FR-57 enforcement | **Structural** — closed-union `switch` requires every variant | Trust-based — depends on client adherence |
| Risk surface | Small — typed pattern match on existing polymorphism | Larger — schema migration + back-compat for existing persisted tabs |

**Decision**: Option A. Decisive evidence — `WorkspaceTabWidgetData.cs` was authored at
Pillar 6a / task 053 (before task 074 ran) with XML doc comments on every subtype that
quote the **exact FR-57 shapes** the task 074 spec requires:

- `SummaryTabWidgetData` (line 44–48): *"Pillar 9 visible state: `{ widgetType, summary,
  tldr, hasUserEdits }`"*
- `DocumentViewerTabWidgetData` (line 66–69): *"Pillar 9 visible state: `{ widgetType,
  filename, mimeType, sizeBytes, hasSelection, selectionText? }`"*
- `DashboardTabWidgetData` (line 104–108): *"Pillar 9 visible state: `{ widgetType,
  dashboardName, lastViewedSection }` — deliberately NOT chart data (payload
  minimization, NFR-10)"*
- `TableTabWidgetData` (line 130–134): *"Pillar 9 visible state: `{ widgetType, rowCount,
  sortColumn, filteredColumns, selectedRows[] }` — NOT the raw rows (token economy)"*

The C# polymorphic models were **designed for server-side Pillar 9 derivation**.
Option A is the natural fit; Option B would introduce an unneeded persistence layer
that duplicates the typed contract.

The task brief noted "count, NOT row IDs — stricter than POML" for Table — Option A
implements this via a typed `int` projection (`SelectedRows: t.SelectedRows?.Count ?? 0`)
in the new `WorkspaceTabVisibleState.Table` record; the client (task 073) emits a
count as well, so the contract is uniform.

---

## Implementation overview

### NEW: `WorkspaceTabVisibleState` typed discriminated union

`src/server/api/Sprk.Bff.Api/Models/Workspace/WorkspaceTabVisibleState.cs` — 90 LOC.

Closed 4-variant union mirroring `WorkspaceTabWidgetData` but projecting ONLY the FR-57
deterministic fields:

```csharp
public abstract record WorkspaceTabVisibleState
{
    public abstract string WidgetType { get; }

    public sealed record Summary(string? Tldr, string? SummaryText, bool HasUserEdits);
    public sealed record DocumentViewer(string Filename, string MimeType, long SizeBytes,
                                        bool HasSelection, string? SelectionText);
    public sealed record Dashboard(string DashboardName, string? LastViewedSection);
    public sealed record Table(int RowCount, string? SortColumn,
                                IReadOnlyList<string> FilteredColumns, int SelectedRows);
}
```

(`SummaryText` named to avoid record-positional-parameter collision with the
`Summary` subtype name.)

### MODIFIED: `SprkChatAgentFactory.BuildWorkspaceStateBlock`

`src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` — three changes:

1. **Replaced** the legacy minimal text format (`"- Tab N (active): <WidgetType>"`) with a
   structured per-tab composition that emits the FR-57 fields under each tab header.

2. **Added** `internal static TryDeriveVisibleState(WorkspaceTab tab)` — closed-union
   `switch` on `WorkspaceTabWidgetData` polymorphism. Returns `null` when the widget has
   no derivable visible state (privacy default — Summary with empty body AND null tldr
   returns null).

3. **Added** `FormatVisibleStateFields(WorkspaceTabVisibleState state)` — typed switch
   formatting each shape into per-tab prompt fields. Summary body normalised + capped at
   600 chars; DocumentViewer selectionText capped at 200 chars; Table emits `selectedRows: <count>` (not row IDs).

4. **Widened** the fallback char ceiling from `WorkspaceStateBlockMaxChars` (500) to
   `WorkspaceStateBlockMaxCharsRich` (2000) to accommodate the richer per-tab shapes.
   The legacy 500-char const is retained (referenced by old tests that have been
   updated); the rich ceiling is the new fallback. Budget tracker (existing
   `TryReservePromptBudget` helper from task 068) remains the primary enforcement —
   the char ceiling is only a fail-safe when no tracker is wired.

5. **Filter logic** (FR-58 + FR-59 BINDING):
   ```csharp
   var visible = tabs
       .Where(t => t.VisibleToAssistant)
       .Select(t => (Tab: t, State: TryDeriveVisibleState(t)))
       .Where(p => p.State is not null)
       .ToList();
   ```
   BOTH conditions required (`visibleToAssistant === true` AND derivable state).

### Budget integration (NFR-10 / FR-46)

The existing `TryReservePromptBudget` helper (task 068) on the factory is unchanged and
continues to enforce the 8K shared budget at the `"workspace-state"` layer. The block is
emitted via the budget gate — when denied, the tracker emits truncation telemetry on
its `memory.prompt_budget_truncated` counter with the `workspace-state` layer tag (per
`PromptBudgetTracker.cs`).

No new telemetry surface is needed; the existing budget-tracker mechanism is the
canonical truncation telemetry per task 068's design.

### ADR-015 governance — what does + does not appear

| Field | Appears? | Rationale |
|---|---|---|
| `WidgetType` enum string | ✅ | Closed-union discriminator |
| `MatterContext.MatterName` | ✅ | Deterministic anchor (existing task 053 contract) |
| `IsPinned` boolean | ✅ | Deterministic boolean |
| Summary `Tldr` | ✅ | Agent-generated; FR-57 deterministic field |
| Summary `Body` (normalized + capped) | ✅ | Agent-generated; capped at 600 chars |
| DocumentViewer `Filename`, `MimeType`, `SizeBytes` | ✅ | Document metadata; FR-57 |
| DocumentViewer `HasSelection` | ✅ | Boolean state flag |
| DocumentViewer `SelectionText` | ✅ when visible | Content-bearing; capped at 200 chars per task 073 contract |
| Dashboard `DashboardName`, `LastViewedSection` | ✅ | Deterministic labels |
| Dashboard chart data | ❌ | Explicitly omitted (FR-57 spec) |
| Table `RowCount`, `SortColumn`, `FilteredColumns` | ✅ | Deterministic counts + column ids |
| Table `SelectedRows` row IDs | ❌ | **COUNT only** (stricter than POML's `selectedRows[]`) |

Summary `Body` participation requires nuance — the body IS agent-generated content the
user can quote in chat, so it must reach the LLM, but only via the normalized
600-char projection. The closed-union switch makes this structural.

---

## Tests

### Unit — extended `SprkChatAgentFactoryWorkspaceStateTests` (21 tests, all green)

```
tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/SprkChatAgentFactoryWorkspaceStateTests.cs
```

Legacy task 053 contract preserved (7 tests reflagged for the new format):
- Empty / all-hidden → empty
- Single visible → "Tab 1 (active): widgetType=Summary"
- Multiple visible → most-recent active
- Pinned/unpinned marker
- ADR-015 — no widget-data structural markers leak

Task 074 — FR-57 per-widget shape verification (5 tests):
- Summary widget → emits `tldr`, `summary`, `hasUserEdits` fields
- DocumentViewer widget → emits `filename`, `mimeType`, `sizeBytes`, `hasSelection`, `selectionText`
- DocumentViewer no selection → omits `selectionText` (privacy)
- DocumentViewer long selection → capped at 200 chars with `…` marker
- Dashboard widget → emits `dashboardName`, `lastViewedSection`; NO chart data
- Table widget → emits `rowCount`, `sortColumn`, `filteredColumns`, `selectedRows: <COUNT>`; NO row IDs

Task 074 — FR-58 + FR-59 privacy filter (4 tests):
- visible + has state → in prompt
- visible + NO state → NOT in prompt (privacy default)
- NOT visible → NOT in prompt (privacy default)
- 3-tab scenario → only (visible + has state) appears

Task 074 — Budget enforcement (3 tests):
- Over-budget tracker → denies; fragment must be omitted
- Within-budget tracker → grants
- Null tracker (legacy) → grants (pass-through)

Task 074 — Fallback ceiling (1 test):
- 50 rich-Summary tabs → block stays within `WorkspaceStateBlockMaxCharsRich + 200`

**Result**: 21 passed, 0 failed, 0 skipped.

### Integration — `Pillar9PrivacyFilterTests` (3 tests, all green)

```
tests/integration/Spe.Integration.Tests/Workspace/Pillar9PrivacyFilterTests.cs
```

Mirrors the `ConflictResolutionTests.cs` pattern (task 058):

1. **3-tab privacy filter end-to-end**: tab 1 (visible + has-state) appears; tab 2
   (visible + no-state, privacy default) excluded; tab 3 (not-visible, privacy default)
   excluded — verifies the BFF prompt-composition path enforces FR-58 + FR-59 BOTH-required
   semantics. Includes assertions that tab 3's `filename` AND `selectionText` never
   reach the prompt (ADR-015).

2. **All-3-widget-categories happy path** (DocumentViewer + Dashboard + Table all visible
   with state): verifies the structural fields appear; Table emits `selectedRows: <count>`
   and row IDs do NOT leak.

3. **All-excluded → empty result**: 2-tab scenario where both fail the filter; the entire
   block is omitted.

**Result**: 3 passed, 0 failed, 0 skipped.

### Regression — `Chat | Workspace` sweep

```bash
dotnet test tests/unit/Sprk.Bff.Api.Tests/ \
  --filter "FullyQualifiedName~Chat|FullyQualifiedName~Workspace" \
  --no-build --logger "console;verbosity=minimal"
```

**Result**: 1414 passed, 0 failed, 12 skipped (pre-existing skips — none introduced by
task 074). Verified that the factory hot path (task 053 wiring + task 058 conflict path +
task 063 telemetry + task 068 budget tracker) all continue to pass.

---

## Build + publish-size

```bash
dotnet build src/server/api/Sprk.Bff.Api/ -nologo -v q
```

**Result**: 0 errors, 16 warnings (all pre-existing; none introduced by task 074).

```bash
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/
tar -czf /tmp/bff-publish-074.tar.gz -C deploy api-publish
```

**Result**: compressed publish size **44.71 MB** — **identical to the prior baseline**.
Delta: **0.00 MB**. Pure code-only additions (1 new model file + ~150 LOC factory
extension + tests). No new packages, no new transitives. ADR-029 publish-size budget
preserved. NFR-02 cumulative R6 budget (≤+5 MB ceiling) unaffected.

---

## ADR compliance

| ADR | Status |
|---|---|
| ADR-010 (DI minimalism) | ✅ — ZERO new Program.cs lines. No DI changes. `WorkspaceTabVisibleState` is a typed model (no DI). |
| ADR-013 (AI architecture / facade boundary) | ✅ — Factory + new typed model live under `Services/Ai/Chat/` + `Models/Workspace/`. No new `PublicContracts` facade widening. |
| ADR-015 (data governance) | ✅ — Closed-union `switch` structurally enforces the FR-57 field set. No widget-data structural markers leak; selectionText capped at 200 chars; Table emits row COUNT only. |
| ADR-029 (BFF publish hygiene) | ✅ — 0.00 MB delta. |
| NFR-10 (8K budget) | ✅ — Existing `TryReservePromptBudget` (task 068) enforces; rich ceiling fallback only when tracker absent. |
| NFR-02 (R6 ≤+5 MB cumulative) | ✅ — 0.00 MB this task. |

---

## Files modified / created

**Created**:
- `src/server/api/Sprk.Bff.Api/Models/Workspace/WorkspaceTabVisibleState.cs` (90 LOC)
- `tests/integration/Spe.Integration.Tests/Workspace/Pillar9PrivacyFilterTests.cs` (180 LOC, 3 tests)

**Modified**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` — replaced legacy
  `BuildWorkspaceStateBlock` body (~40 LOC removed) with rich per-tab composition (~165
  LOC added); new `TryDeriveVisibleState` + `FormatVisibleStateFields` helpers; new
  `WorkspaceStateBlockMaxCharsRich` const; `SelectionTextMaxChars` const.
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/SprkChatAgentFactoryWorkspaceStateTests.cs`
  — rewrote with 21 tests covering legacy contract preservation + FR-57 per-widget
  shapes + FR-58/59 privacy filter + budget enforcement + fallback ceiling.

**Updated**:
- `projects/spaarke-ai-platform-unification-r6/tasks/TASK-INDEX.md` — 074 🔲 → ✅.
- `projects/spaarke-ai-platform-unification-r6/current-task.md` — Wave C-G18 closeout.

---

## Outstanding / follow-up

None. Task 074 closes Pillar 9 BFF surface. The Pillar 9 surface (072 registry +
073 per-widget impls + 074 BFF derivation) is complete.

Note for future consumers: the frontend per-widget `getAgentVisibleState()` (task 073)
is now an INFORMATIONAL contract — the BFF derives the SAME 4-variant FR-57 shapes
server-side. Both sides must stay in sync if either contract evolves; the FR-57 spec
in `spec.md` is the single source of truth.
