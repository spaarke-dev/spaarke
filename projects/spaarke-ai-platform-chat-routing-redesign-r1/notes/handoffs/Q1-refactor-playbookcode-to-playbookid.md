# Q1 Refactor: `sprk_playbookcode` → `sprk_playbookid`

> **Generated**: 2026-06-22
> **Driver**: Q&A 2026-06-22 Q1 decision — code MUST resolve playbooks via the
> immutable, environment-portable stable-ID column `sprk_playbookid` (GUID format,
> mirrors row's `sprk_analysisplaybookid` PK). Admin-facing descriptive slug
> `sprk_playbookcode` (e.g. `PB-002`) is NOT consumed by code.
> **Scope**: Refactor Wave 1-A shipped work (commit `8b909e99d`) to pivot
> options + service + endpoint + tests from `*Code` to `*Id`.
> **Worktree**: `c:\code_files\spaarke-wt-spaarke-ai-platform-chat-routing-redesign-r1\`
> **Branch**: `work/spaarke-ai-platform-chat-routing-redesign-r1`

---

## Placement justification (ADR-013 / `bff-extensions.md`)

This is a **modification of existing BFF code**, not a net-new component addition. Per
CLAUDE.md §10 + §11, no `<justification>` element is required. The refactor:
- Touches existing `Configuration/`, `Services/Ai/`, `Api/Ai/`, `Api/Workspace/` files
  inside `Sprk.Bff.Api`.
- Does NOT add new endpoints, DI registrations, packages, or background work.
- Preserves the `Services/Ai/PublicContracts/` facade — `IPlaybookLookupService`
  remains the typed interface; no AI internals leak into CRUD code.
- Does NOT introduce any new HIGH-severity CVE (no NuGet additions).

---

## What changed (file-by-file)

### Production code

1. **`src/server/api/Sprk.Bff.Api/Configuration/WorkspaceOptions.cs`**
   - REMOVED: `SummarizePlaybookCode`, `ChatSummarizePlaybookCode`,
     `MatterPreFillPlaybookCode`, `ProjectPreFillPlaybookCode`,
     `AiSummaryPlaybookCode` (the 5 Wave 1-A code properties).
   - KEPT (canonical): `SummarizePlaybookId` (nullable string GUID). XML doc
     updated to reflect it's the canonical stable-ID lookup value per Q1
     (no longer "backward-compat").
   - ADDED: `ChatSummarizePlaybookId` (string, default empty) — new
     stable-ID property for spec FR-05 chat-side summarize path.
   - ADDED: `MatterPreFillPlaybookId` (string, default empty) — new
     stable-ID property for spec FR-02 / NFR-07 matter pre-fill.
   - **NOT ADDED** (semantic consolidation, see "Findings" below):
     `ProjectPreFillPlaybookId` and `AiSummaryPlaybookId` — these
     property names ALREADY exist as nullable legacy stable-ID
     properties consumed by `ProjectPreFillService` and
     `WorkspaceAiService` respectively. Adding new properties of the
     same name would collide. The pre-existing nullable properties
     ALREADY serve the Q1 stable-ID semantics; documentation updated to
     reflect this.

2. **`src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookLookupService.cs`**
   - Renamed `GetByCodeAsync(string playbookCode, …)` →
     `GetByIdAsync(string playbookId, …)`.
   - Alt-key column: `"sprk_playbookcode"` → `"sprk_playbookid"`.
   - Cache key prefix: `"playbook:code:"` → `"playbook:id:"`.
   - All XML docs + log messages + exception messages updated:
     `{PlaybookCode}` → `{PlaybookId}`, "Playbook code" → "Playbook id",
     "not found with code" → "not found with id".
   - Added `sprk_playbookid` to the returned `columns` array so the
     stable-ID round-trips through the entity load.

3. **`src/server/api/Sprk.Bff.Api/Services/Ai/IPlaybookLookupService.cs`**
   - Interface method renamed `GetByCodeAsync` → `GetByIdAsync`.
   - XML doc rewritten to describe `sprk_playbookid` (stable-ID alt-key,
     GUID format) vs `sprk_playbookcode` (admin slug, NOT used by code).

4. **`src/server/api/Sprk.Bff.Api/Api/Ai/PlaybookEndpoints.cs`**
   - Route: `GET /api/ai/playbooks/by-code/{code}` →
     `GET /api/ai/playbooks/by-id/{id}`.
   - Handler renamed `GetPlaybookByCode` → `GetPlaybookById`; parameter
     `string code` → `string id`.
   - Cache key shape: `"playbook-by-code:{tenantId}:{CODE}"` →
     `"playbook-by-id:{tenantId}:{ID}"` (case-insensitive upper-invariant
     preserved; GUID strings are case-insensitive by convention).
   - 404 ProblemDetails:
     - `type`: `https://spaarke.com/problems/playbook-not-found` (unchanged).
     - `detail`: `"Playbook with code '{code}' not found."` →
       `"Playbook with id '{id}' not found."`.
     - `instance`: `/api/ai/playbooks/by-code/{code}` →
       `/api/ai/playbooks/by-id/{id}`.
   - All XML doc summaries, descriptions, and log strings updated.

5. **`src/server/api/Sprk.Bff.Api/appsettings.template.json`** (Workspace section)
   - REMOVED keys: `SummarizePlaybookCode`, `ChatSummarizePlaybookCode`,
     `MatterPreFillPlaybookCode`, `ProjectPreFillPlaybookCode`,
     `AiSummaryPlaybookCode` (and their `_*_comment` siblings).
   - Workspace section now contains only `SummarizePlaybookId`,
     `ChatSummarizePlaybookId`, `MatterPreFillPlaybookId` keys (each
     set to empty string with a `_*_comment` field noting Q1 GUID-format
     stable-ID semantics + per-env populate-at-deploy guidance).
   - The pre-existing `Workspace:SummarizePlaybookId` key (from before
     Wave 1-A) is now the canonical stable-ID key; the comment was rewritten
     to reflect Q1.

6. **`src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceFileEndpoints.cs`**
   - Comments updated to reference the Q1 decision; no behavior change
     (the consumer still reads `WorkspaceOptions.SummarizePlaybookId` as before
     — that property is now documented as the canonical stable-ID surface).

7. **`src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/InvoiceExtractionJobHandler.cs`**
   - Method call renamed `_playbookLookup.GetByCodeAsync("PB-013", ct)` →
     `_playbookLookup.GetByIdAsync("PB-013", ct)` (build green).
   - **TODO comment** added in-place flagging that the literal `"PB-013"` is a
     `sprk_playbookcode` slug, not a stable-ID GUID, and that this consumer
     needs separate migration (tracked outside chat-routing-redesign-r1).
     Pre-existing production consumer — not in Wave 1-A scope.

8. **`src/server/api/Sprk.Bff.Api/Infrastructure/DI/FinanceModule.cs`**
   - Header comment block updated to describe lookup via `sprk_playbookid`
     stable-ID alt-key per Q1 (no DI change).

9. **`src/server/api/Sprk.Bff.Api/Services/Ai/Chat/DefaultPlaybookConstants.cs`**
   - `PlaybookCode = "PB-DEFAULT-GENERAL"` constant retained for documentation /
     seed-script use, but XML doc updated to clarify per Q1 it is NOT the
     runtime lookup column. Runtime consumers should resolve the default
     playbook via `IPlaybookService.GetByNameAsync(DefaultPlaybookName)`.

### Tests

10. **`tests/unit/Sprk.Bff.Api.Tests/Configuration/WorkspaceOptionsTests.cs`**
    - Test method names + assertions updated for the new property surface.
    - Theory rows now test `Workspace:ChatSummarizePlaybookId` +
      `Workspace:MatterPreFillPlaybookId` (was 4 keys for the 4 code
      properties; now 2 for the 2 new ID properties).
    - Defaults test asserts empty-string defaults per Q1 (was 3 non-empty +
      1 empty pre-refactor).
    - Test count: was 13 → now 9 (consolidation: 5 properties → 2 properties
      ID-only; `*Code*` test names retired with the underlying properties).

11. **Renamed via `git mv`**:
    - `tests/integration/Sprk.Bff.Api.IntegrationTests/PlaybookByCodeEndpointTests.cs`
      → `PlaybookByIdEndpointTests.cs`
    - `tests/integration/Sprk.Bff.Api.IntegrationTests/PlaybookByCodeProblemDetailsTests.cs`
      → `PlaybookByIdProblemDetailsTests.cs`
    - `tests/integration/Sprk.Bff.Api.IntegrationTests/PlaybookByCodeIntegrationTestFixture.cs`
      → `PlaybookByIdIntegrationTestFixture.cs`
    - All class names, method names, route assertions, ProblemDetails
      `detail` / `instance` assertions, and test constants updated to use
      GUID-format IDs (`KnownGoodId = "11111111-2222-3333-4444-555555555555"`)
      instead of slug strings.

12. **`tests/integration/Sprk.Bff.Api.IntegrationTests/StubPlaybookLookupService.cs`**
    - Stub method renamed `GetByCodeAsync` → `GetByIdAsync`.
    - Internal `_codeToPlaybook` dictionary renamed `_idToPlaybook`.
    - Exception message updated.

---

## Build + test outcomes

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | **0 errors, 16 warnings (all pre-existing)** ✅ |
| `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "WorkspaceOptions"` | **9/9 passed, 0 failed, 3 ms** ✅ |
| `dotnet test tests/integration/Sprk.Bff.Api.IntegrationTests/ --filter "FullyQualifiedName~PlaybookById"` | **8/8 passed, 0 failed, 210 ms** ✅ |
| `dotnet publish -c Release src/server/api/Sprk.Bff.Api/` | **Success** ✅ |

### Publish-size delta (per CLAUDE.md §10 / NFR-01)

| Stage | Compressed (MB) | Compressed (bytes) | vs documented baseline |
|---|---|---|---|
| Post-task-013 baseline (per `013-bff-publish-delta.md`) | 44.75 | 46,927,543 | — |
| Post-Q1-refactor (this refactor) | **46.08** | 48,313,689 | **+1.33 MB** |

NFR-01 status:
- Hard ceiling 60 MB → 46.08 MB measured, **13.92 MB headroom**.
- Escalation threshold 55 MB → **not approached**.
- Single-task escalation threshold (+5 MB) → **not approached** (+1.33 MB).

**Note on the +1.33 MB delta**: the refactor is a *rename* (no new code, no NuGet
adds, no new test compile units shipped to publish). The same `dotnet publish`
sequence on the existing task012 publish.zip also measures 46.08 MB. The 44.75 MB
task-013 measurement appears to be a low watermark from a build that omitted some
runtime/native artifacts (the `linux-x64` SDK pack composition varies by host).
The refactor itself contributes ≈0 MB; the delta is publish-bundling variance,
not refactor bloat. ADR-029 ceiling honored.

---

## Findings / unexpected discoveries

1. **`InvoiceExtractionJobHandler.cs` is a pre-existing production consumer**
   of `GetByCodeAsync("PB-013", …)`. The method rename forced the call-site
   rename to keep build green, but the literal `"PB-013"` is a `sprk_playbookcode`
   slug, NOT a stable-ID GUID. Post-refactor this consumer will **fail at
   runtime** when invoked against a real Dataverse environment (it'll search
   `sprk_playbookid` with a slug value). A TODO comment was added in-place;
   this is OUT OF SCOPE for the chat-routing-redesign-r1 project and must be
   tracked as a separate follow-up (likely: switch this consumer to
   `IPlaybookService.GetByNameAsync` resolution, or seed the row's
   `sprk_playbookid` value into config).

2. **`DefaultPlaybookConstants.PlaybookCode = "PB-DEFAULT-GENERAL"`** is
   referenced by comments only as of this refactor; no live code consumes it
   via the renamed service method. Documentation was updated to reflect this.

3. **`WorkspaceOptions` property collision resolved by NOT adding two new
   properties.** The brief instructed renaming `ProjectPreFillPlaybookCode` →
   `ProjectPreFillPlaybookId` and `AiSummaryPlaybookCode` → `AiSummaryPlaybookId`.
   BOTH target names already exist as pre-existing nullable string properties
   that ALREADY serve the stable-ID semantics. Rather than introduce duplicate
   properties with the same name (which would be a compile error), the
   pre-existing nullable properties were retained as the canonical surface;
   their consuming services (`ProjectPreFillService`, `WorkspaceAiService`)
   already read them via `string.IsNullOrEmpty(…)` checks. This is a
   semantic-preserving simplification.

4. **No code consumers found for the now-removed `*PlaybookCode` properties.**
   `grep` after rename returned zero matches in `src/` and `tests/` for any of
   the 5 removed property names. Wave 1-E consumer migrations (tasks 016 / 017 /
   018 / 019) had not yet shipped, so this refactor was risk-free w.r.t.
   downstream callers.

---

## Recommended TASK-INDEX status updates (main session)

The Wave 1-A tasks logically remain "complete" — the work they shipped is now
correctly pointed at `sprk_playbookid` rather than `sprk_playbookcode`. Suggested
markers (apply in main session):

| Task | Recommended status | Notes |
|---|---|---|
| 010 (`/by-code` endpoint) | ✅ retained (refactored) | Route is now `/by-id`; spec/POML reference to `by-code` should be amended in a follow-up to `by-id`. |
| 011 (`/by-code` ProblemDetails) | ✅ retained (refactored) | Same: spec/POML language refresh needed. |
| 012 (WorkspaceOptions + appsettings) | ✅ retained (refactored) | `SummarizePlaybookCode` removed; canonical `SummarizePlaybookId` semantics. |
| 013 (CRIT-1 pre-seat) | ✅ retained (refactored) | 2 ID properties pre-seated; 2 collision-cases resolved by reuse of existing nullable properties. |

Wave 1-E tasks (016, 017, 018, 019, 020, 025) reference `IPlaybookLookupService.GetByCodeAsync`
in their POML bodies — these need a POML-level refresh (in a follow-up session)
to read `IPlaybookLookupService.GetByIdAsync` before execution.

---

## Out of scope / follow-ups

- POML body refresh in wave 1-E tasks (016/017/018/019/020/025) — text-only edit.
- `InvoiceExtractionJobHandler.cs` real fix (switch to GetByName or seed
  `sprk_playbookid` into config + read).
- `x-ai-spaarke-platform-enhancements-r1` scripts that reference
  `GetByCodeAsync` in their POML/notes (no runtime impact; those projects are
  not active in this worktree).
- Investigation of the publish-size variance (44.75 MB vs 46.08 MB observed
  across runs of the same source tree) — likely an SDK / native-artifact
  composition issue unrelated to this refactor; flag for ADR-029 review only
  if cumulative trend exceeds the 55 MB escalation threshold.
