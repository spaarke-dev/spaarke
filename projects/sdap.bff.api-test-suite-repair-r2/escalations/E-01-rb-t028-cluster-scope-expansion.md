# E-01 — RB-T028 Cluster: Scope Expansion Beyond Task Boundary (HUMAN INPUT REQUIRED)

> **Project**: sdap.bff.api-test-suite-repair-r2
> **Task**: 011 (Phase 1 P1-S2 / HIGH cluster fix)
> **RB IDs**: RB-T028-03, RB-T028-04, RB-T028-05, RB-T028-06
> **Filed**: 2026-06-01
> **Filed by**: task-execute (Claude Code, Opus 4.7)
> **Status**: Open — awaiting owner direction
> **Severity**: BLOCKING for task 011

---

## 🔔 Human Input Required

The RB-T028 cluster as filed in the r1 ledger captures only a **subset** of the actual production bug. Investigation during task 011 execution surfaced a much wider asymmetric registration/mapping pattern across the BFF that cannot be resolved within the task's `<estimated-effort>5-8 hours</estimated-effort>` scope or NFR-01 / NFR-02 constraints.

**Decision required**: which of the 4 paths below should task 011 take?

---

## Situation

The r1 ledger entry for RB-T028-03 (line 762-770) recommended:

> Two viable approaches:
> 1. **Conditional endpoint mapping** (preferred): wrap the KB endpoint registration in `Program.cs` with `if (analysisEnabled)` so the endpoints are not mapped when AI is disabled.
> 2. **Conditional service registration** (alternative): register a no-op `INotificationService` (e.g., `NullNotificationService`) when AI is disabled.
>
> Approach (1) is preferred because it matches the documented intent.

During task 011 implementation of **Approach (1)** (D-09 draft, reverted), the following layered failure cascade was discovered:

### Layer 1 — Original ledger-captured issue (4 endpoint families)

`NotificationService` (registered conditionally inside `if (Analysis:Enabled && DocumentIntelligence:Enabled)` in `AnalysisServicesModule.AddPlaybookServices`) is required by:
- `Api/Ai/AnalysisEndpoints.cs` (already inside the `if` block at line 122 of `EndpointMappingExtensions` — fine)
- `Api/WorkAssignmentEndpoints.cs` (mapped UNCONDITIONALLY at line 116 — bug)
- `Api/UploadEndpoints.cs` (mapped UNCONDITIONALLY at line 109 — bug)

This is fixed by moving `services.AddSingleton<NotificationService>()` to be unconditional. `NotificationService` has zero AI dependencies (just `IGenericEntityService` + `ILogger`); it's just a Dataverse appnotification creator. **This is a clear bug — `NotificationService` was misregistered**.

### Layer 2 — Additional unconditionally-mapped endpoints with AI service deps

After fixing Layer 1, the next startup failure surfaces:
- `Api/Workspace/WorkspaceMatterEndpoints.HandleAiSummary` takes `IBriefingAi? briefingAi = null` — minimal-API param-inference fails because `IBriefingAi` is not registered when AI is off, and the nullable-default doesn't suppress the UNKNOWN classification.

Fixable with `[FromServices] IBriefingAi? briefingAi = null` annotation — but pattern proliferates.

### Layer 3 — More endpoints with AI service deps

After fixing Layer 2:
- `Api/Finance/FinanceEndpoints.SearchInvoices` requires `IInvoiceSearchService` (registered conditionally) — fails param-inference.

Same pattern (`[FromServices] IInvoiceSearchService? + null-503`). Continues.

### Layer 4 — Concrete-class conditional dependencies

After fixing Layer 3:
- `Api/Ai/ChatEndpoints.SendMessageAsync` requires `PendingPlanManager` (concrete class, registered conditionally in `AiModule` line 274). `[FromServices]` works, but `ChatEndpoints` has ~5 mapped handlers, each taking multiple such conditional services.

### Layer 5+ — Wider surface

A systematic Grep of all endpoints mapped unconditionally vs services registered conditionally would likely surface 10–20 more endpoint handlers across:
- `ChatEndpoints` (multiple handlers)
- `KnowledgeBaseEndpoints` (multiple handlers — though already in scope)
- `DailyBriefingEndpoints` (already has the nullable-default pattern; may still fail param-inference)
- Various Workspace + Office endpoints

---

## Why this matters

The r1 ledger said "37 tests flip Skip → Pass on success" with a single production-code change of 5-8 hours. **The actual production change required is materially larger**:

- **Approach 1 (conditional endpoint mapping)** affects ~10-15 `Map*Endpoints` call sites across `EndpointMappingExtensions.cs` PLUS requires test fixtures to expect 404 when AI is off OR to flip `Analysis:Enabled=true` and provide real stubs (NFR-01 violation if tests changed).

- **Approach 2 (NullObject services)** requires registering Null-Object implementations for 10-20+ conditional services. Some are simple (`NullNotificationService`), but most depend on AI-specific types (`IRagService` → `IOpenAiClient`; `IBriefingAi` → `IOpenAiClient`; `IInvoiceSearchService` → `IOpenAiClient` + `SearchIndexClient`). This is a substantial architectural addition (~10+ new classes, ~200+ LOC).

- **Approach 3 (FromServices + nullable pattern)** can be applied surgically per endpoint but requires editing 10-20+ handler signatures + adding null-check branches. NFR-02 (<50% line replacement per file) would apply per file but the cumulative scope is large.

- **Approach 4 (test fixture extension)** — extend the test fixtures with more Loose mocks for the missing services. NFR-01 forbids test changes outside Skip→Pass transitions; would need explicit owner approval.

The r1 ledger only captured the FIRST symptom (`notificationService UNKNOWN` from KnowledgeBaseEndpoints). The actual production debt is layered.

---

## Findings as evidence

| Symptom | Detected by | Root cause |
|---|---|---|
| `notificationService UNKNOWN` | KnowledgeBaseTests | `NotificationService` misregistered (Layer 1) |
| `briefingAi UNKNOWN` | KnowledgeBaseTests after Layer 1 fix | `IBriefingAi` nullable-default insufficient for param-inference (Layer 2) |
| `searchService UNKNOWN` | KnowledgeBaseTests after Layer 2 fix | `IInvoiceSearchService` non-nullable + conditional (Layer 3) |
| `pendingPlanManager UNKNOWN` | KnowledgeBaseTests + AuthTests after Layer 3 fix | `PendingPlanManager` non-nullable + conditional (Layer 4) |
| (potentially more) | Would surface after Layer 4 fix | Unknown until tried |

Each layer was discovered serially during the implementation. The "shared root cause" framing in r1 was technically correct (asymmetric registration vs mapping) but **the surface is much wider than 4 endpoint families** — it's the entire BFF.

---

## Options for owner decision

### Option A — Defer task 011, file new ledger entries
- Mark RB-T028-03/04/05/06 as `needs-investigation` rather than `repaired`.
- File new ledger entries RB-T028-09 through RB-T028-N covering the layered findings.
- Re-scope task 011 (or replace with new task) with proper effort estimate (~20-40h) and decision on which approach.
- Tests remain Skipped at status `real-bug-pending-fix`.

### Option B — Adopt Approach 2 (NullObject) fully
- Allocate ~20-30h to design + implement Null-Object impls for all conditional AI services.
- Single PR with cluster commit.
- All 37 tests pass without test changes (NFR-01 compliant).
- ADR impact: requires ADR-018 amendment or new ADR documenting the Null-Object kill-switch pattern.

### Option C — Adopt Approach 1 + owner-approved fixture change
- Set `Analysis:Enabled=true` in the 4 test fixtures (NFR-01 EXCEPTION required).
- Move all AI endpoint mapping inside the `if (Analysis:Enabled && DocumentIntelligence:Enabled)` block (~10-15 endpoint families).
- Fixtures continue to stub AI services via Loose mocks (already do this).
- Production behavior: AI endpoints disappear when kill switch is off (404).
- Effort: ~10-15h. Requires owner sign-off on NFR-01 exception.

### Option D — Apply Approach 3 (FromServices + nullable) surgically
- Add `[FromServices] T? + null-503` pattern to every endpoint handler with a conditional service dependency.
- Iteratively: fix, test, find next, fix, test...
- Effort: hard to estimate (~15-25h). Each fix uncovers the next layer.
- Risk: scope creep; may exceed NFR-02 (<50% line replacement per file) in some files.

---

## Recommendation from task-execute

**Option A** (file new ledger entries + replace task 011 with properly-scoped work).

Reasoning:
1. r2 already has rigorous ADR governance + test governance discipline. Slamming through a "cluster fix" that's actually a multi-layered architectural change violates that discipline.
2. The original r1 task 028 closeout filed RB-T028-03/04/05/06 with `Fix-by date: 2026-07-31` (60-day target). Refiling with broader scope keeps the 2026-07-31 deadline observable.
3. NFR-01 (no test changes) is a load-bearing constraint of r2. Option C requires explicit NFR-01 exception — owner judgment call, not task-execute discretion.
4. The "cluster exception" D-02 was scoped for clusters with truly shared root cause + single production change. The actual surface is broader; D-02 doesn't cleanly apply.
5. Per D-03 + CLAUDE.md §6 Human Escalation Triggers: "Scope expansion beyond task boundaries" + "Breaking changes" → MUST escalate before continuing.

---

## What was changed during investigation (now REVERTED)

All production code changes were REVERTED to baseline before this escalation:
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` — REVERTED
- `src/server/api/Sprk.Bff.Api/Api/Finance/FinanceEndpoints.cs` — REVERTED
- `src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceMatterEndpoints.cs` — REVERTED
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs` — REVERTED (initially edited then reverted)

All test Skip→Pass transitions on 4 test files were REVERTED to baseline:
- `tests/integration/Spe.Integration.Tests/Api/Ai/KnowledgeBaseEndpointsTests.cs` — REVERTED
- `tests/integration/Spe.Integration.Tests/Api/Ai/ChatEndpointsTests.cs` — REVERTED
- `tests/integration/Spe.Integration.Tests/Api/Ai/ReAnalysisFlowTests.cs` — REVERTED
- `tests/integration/Spe.Integration.Tests/AuthorizationIntegrationTests.cs` — REVERTED

`git status` confirms zero modified files outside `projects/sdap.bff.api-test-suite-repair-r2/escalations/`.

The draft `decisions/D-09-rb-t028-cluster-fix-approach.md` was deleted (was based on incomplete diagnostic — Option B alone is insufficient).

---

## Next steps awaiting owner

1. Owner reviews this escalation and selects A/B/C/D (or proposes E).
2. If Option A: file new ledger entries documenting Layer 2-5 findings; replace task 011 with properly-scoped successor.
3. If Option B/C/D: re-scope task 011 estimated-effort; possibly file new design.md addendum or ADR amendment.
4. Task 011 status remains `not-started` in TASK-INDEX.md until owner direction received.
5. Tests remain Skipped at `real-bug-pending-fix` status — no impact on baseline test counts.

---

*Recorded by task-execute under FULL rigor protocol per CLAUDE.md §6 Human Escalation Triggers ("Scope expansion beyond task boundaries").*
