# `ActionType` → `ExecutorType` Rename Strategy — Wave 2 Execution Plan

> **Task**: R7-021 (Wave 2 planning, FR-10 preparatory)
> **Date**: 2026-06-28
> **Status**: Plan complete — operational input for task 022 (mechanical rename)
> **Consumes**: `notes/spikes/actiontype-audit.md` (464 refs in 82 files: 224 BFF + 240 tests)
> **Branch**: `work/spaarke-ai-platform-unification-r7`

---

## 1. Decisions at a Glance

| Decision | Choice | Rationale |
|---|---|---|
| **PR structure** | **Single PR, single commit** | 464 refs is small; mechanical rename; coherent diff is auditable; partial rename leaves the codebase non-compiling. |
| **Rename mechanism** | **Hybrid (C) — Roslyn rename for symbols + manual Edit for excluded files** | Roslyn-aware refactor handles type/enum/cref/parameter atomically with zero false positives. Edge cases (§3) get hand-verified. |
| **Sequencing within PR** | **One atomic Roslyn rename + manual exclusion verification** | Avoids interim non-compile states. Big-bang per spec NFR-06 (no shim, no alias). |
| **CI gate** | `dotnet build` + `dotnet test --filter` baseline (pre) and full suite (post) | Per ADR-029 §3 + spec NFR-02. |
| **Rollback** | `git revert` of merge commit | Standard; trigger = post-merge BFF smoke fails OR sibling worktree blocked >24h. |
| **Estimated diff size** | **~700-900 line touches** (~464 word-boundary hits + multi-hit lines + XML doc + cref) | See §6. |

---

## 2. Rename Mechanism — Hybrid Roslyn + Manual

**Primary**: Visual Studio / Rider **Roslyn Rename refactor** (`F2` on the `ActionType` enum symbol in `INodeExecutor.cs:97`).

Why Roslyn over sed/regex:
- **Cref handling**: `<see cref="ActionType"/>` + `<see cref="ActionType.AiCompletion"/>` are correctly renamed (sed would miss the `.AiCompletion` suffix retention).
- **Parameter rename cascading**: when the enum is renamed, IDE prompts to rename camelCase parameter `actionType` → `executorType` (FR-10 surface — see §3c).
- **Zero compile-error window**: Roslyn atomically updates all references in a single workspace operation.
- **Test file coverage**: Roslyn workspace includes both `src/` and `tests/` projects loaded in the solution.

**Fallback** (if IDE unavailable in target environment): `dotnet tool install --global dotnet-format` + `dotnet format analyzers --severity info --diagnostic-id IDE0270 ...` is NOT reliable for cross-project rename. Use the `RenameSymbol` API via a one-shot Roslyn console tool. **Last resort**: `git grep -l '\bActionType\b' src/server/api/Sprk.Bff.Api tests | xargs sed -i 's/\bActionType\b/ExecutorType/g'` followed by manual exclusion fixup (§3) — high error rate, do NOT use without strict diff review.

**Recommendation: use IDE-driven Roslyn rename.** Executor for task 022 opens the solution in Rider/VS, F2-renames the `ActionType` enum symbol, accepts the rename preview, applies, then proceeds to §3 manual fixup.

---

## 3. Exclusion List (hand-verify after Roslyn pass)

Roslyn rename is symbol-aware and will NOT touch the following — but task 022 MUST verify each is untouched via post-rename `git diff` inspection.

### 3a. `InlineActionInfo.ActionType` (string discriminator) — DO NOT RENAME
- **File**: `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/AnalysisChatContextResponse.cs`
- **Why excluded**: `string ActionType` is an unrelated `record` property name; symbol-wise it is `InlineActionInfo.ActionType`, NOT the C# enum. Roslyn-rename of the enum WILL NOT touch this.
- **Verification**: `git diff src/server/api/Sprk.Bff.Api/Models/Ai/Chat/AnalysisChatContextResponse.cs` MUST be empty.

### 3b. Lowercase `"actionType"` JSON literals (32 hits) — DEFER to Wave 6
- **Files**: 5 playbook JSON under `Services/Ai/Insights/Playbooks/` + `Services/Ai/Chat/Playbooks/` + SSE wire schema (`SseEventSchemaValidator.cs:165` + `R2SseEventEmitter.cs:143` XML doc).
- **Why excluded**: storage + wire contract; breaking-client-contract change owned by Wave 6 task 062 (Dataverse `sprk_actiontype` choice column rename).
- **Verification**: `grep -rn '"actionType"' src/server/api/Sprk.Bff.Api/` should return ≥32 matches **unchanged** post-rename.

### 3c. Parameter name `actionType` (camelCase) — RENAME
- **Files**: `INodeExecutorRegistry.cs:24,37` `GetExecutor(ActionType actionType)` and similar.
- **Why included**: Roslyn rename cascades parameter name when enum type is renamed (or prompts for it). Updating `actionType` → `executorType` keeps the C# convention pair `ExecutorType executorType`.
- **Verification**: no orphan `actionType` C# parameter names remain in BFF source.

### 3d. `SupportedActionTypes` property (97 refs) — DEFER to task 023
- **Files**: 22 prod + 75 test files declaring or asserting on `INodeExecutor.SupportedActionTypes`.
- **Why excluded**: property rename is a SEPARATE task (023) per `plan.md` Wave 2 WBS. Roslyn rename of the enum does NOT rename the property automatically (property name is a different symbol).
- **Verification**: post-rename, `grep -rn '\bSupportedActionTypes\b' src/server/api/Sprk.Bff.Api tests` should still return ~97 hits.

### 3e. Cross-project residue (`projects/`, `docs/`, `scripts/`, `src/client/`) — DO NOT RENAME
- **Why excluded**: per spec FR-10 + audit §6, this rename is BFF-internal only. Doc + script + client updates are owned by Waves 6/8.
- **Verification**: `git diff projects/ docs/ scripts/ src/client/` MUST be empty.

---

## 4. Execution Sequence (task 022 step-by-step)

1. **Pre-rename baseline**: `dotnet build src/server/api/Sprk.Bff.Api/` → 0 errors expected. Capture warning count. Then `dotnet test --filter "FullyQualifiedName~Sprk.Bff.Api"` → record pass/fail/skip counts (per Wave 1 task 010: 7504 pass / 2 pre-existing fail; same baseline expected).
2. **Open solution** in Rider/VS. Navigate to `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs:97` (enum declaration).
3. **F2-rename** `ActionType` → `ExecutorType`. Preview ALL references (expect ~464 in 82 files). Accept rename.
4. **Save All**.
5. **Manual exclusion verification** per §3a-3e — `git diff` review.
6. **Post-rename build**: `dotnet build src/server/api/Sprk.Bff.Api/` → 0 errors required. Warning count delta MUST be 0.
7. **Post-rename test**: same filter as step 1 → same pass/fail/skip counts required.
8. **Stage + single commit**: `git add -A` (BFF + tests only — `git status` MUST show zero changes outside `src/server/api/Sprk.Bff.Api/` + `tests/`). Conventional commit per `plan.md` Wave 2 message.
9. **Push** to remote on `work/spaarke-ai-platform-unification-r7`.
10. **Open PR** to master with full audit + plan citation in PR body; reviewer applies §10 BFF Hygiene checklist (publish-size = 0 delta expected per IL-neutral rename).

---

## 5. Risk Register

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| Roslyn rename misses an XML cref `<see cref="ActionType.X"/>` due to malformed doc | Low | Low | Post-rename `dotnet build /warnaserror:CS1574` would surface broken crefs; manually grep `<see cref="ActionType` post-rename for residue. |
| Sibling worktree merges BFF change during rename window | Medium | Medium | Per `projects/INDEX.md` 2026-06-26 sweep: 15 active BFF-touching projects. R4 + Action Engine R1 HOLD. Coordinate task 022 dispatch via `/conflict-check` immediately before commit; expect ≤24h conflict window. |
| Reflection-based `nameof(ActionType)` lookup breaks | None | — | Audit §5 confirmed ZERO `nameof(ActionType)` usages in BFF source. Rename is reflection-safe. |
| JSON serialization of enum changes wire format | None | — | Audit §5 confirmed enum serializes to INTEGER (default `System.Text.Json`); type name is not on the wire. Rename does NOT break wire format. Lowercase `"actionType"` JSON property name is preserved (§3b). |
| Test file (`tests/`) data fixtures reference string literal `"ActionType"` | Low | Low | Audit §3 confirmed only 2 string-literal `"ActionType"` hits BFF-wide, both in source not data fixtures. Verify post-rename `grep '"ActionType"' tests/` returns 0 actionable hits. |
| `[JsonConverter]` reflection-based attribute coupling breaks | None | — | Audit §5 confirmed ZERO `[JsonConverter]` references to `ActionType`. Safe. |
| Build-warning regression masks broken rename | Low | Medium | Step 6 of §4 requires warning count delta = 0. Any new warning blocks PR. |

---

## 6. PR Sizing Estimate

| Component | Count |
|---|---|
| Word-boundary `\bActionType\b` hits (audit §1) | 464 |
| Multi-hit lines (e.g., `GetExecutor(ActionType x) where ActionType : Enum` — 2 hits same line) | ~30 (estimate from top-10 cluster review) |
| XML doc `<see cref="ActionType"/>` cref + `<see cref="ActionType.X"/>` (17 hits, all single-line) | 17 |
| Parameter name cascade `actionType` → `executorType` (5-8 files, mostly `INodeExecutorRegistry.cs`) | ~15 |
| Enum declaration line (`public enum ActionType` → `public enum ExecutorType`) | 1 |
| **Total estimated line touches** | **~500-530 lines modified** |
| **Estimated `git diff --stat`** | **~500 insertions + ~500 deletions across 82 files** |
| **Estimated patch bytes** | **~50-80 KB** |

**Reviewability**: mechanical rename diffs are review-cheap (visual pattern: `ActionType` → `ExecutorType` everywhere). A senior reviewer can audit a 500-line mechanical rename in <15 min. **Single PR is the correct size.** Splitting would create non-compiling intermediate states.

---

## 7. CI Gate Strategy

**Pre-rename baseline** (record in PR body):
```
dotnet build src/server/api/Sprk.Bff.Api/        # 0 errors required
dotnet test --filter "FullyQualifiedName~Sprk.Bff.Api"   # baseline pass/fail/skip
```

**Post-rename gate** (PR auto-runs via `.github/workflows/sdap-ci.yml`):
- BFF build: 0 errors, 0 new warnings.
- Test suite: same pass/fail/skip counts as baseline (any delta = rename regression).
- Publish-size: expected 0.00 MB delta vs Wave 1 baseline (46.71 MB). IL-neutral rename. If non-zero, investigate.
- CVE scan: 0 new HIGH-severity (no new package refs added).

**Failure handling**: any gate failure = `git reset --hard HEAD~1` on branch + diagnose. Do NOT push partial fix; the rename is atomic by design.

---

## 8. Rollback Plan

**Trigger criteria** (any one):
1. Post-merge BFF smoke fails within 24h (deployment to spaarkedev1 returns errors).
2. Sibling worktree (R6, chat-routing-redesign-r1, redis-cache-r2) reports a blocked rebase that cannot be resolved within 24h.
3. Hidden consumer (e.g., dataverse plugin via `Spaarke.Core`) breaks at runtime (deemed extremely unlikely per audit §5 — `src/server/shared/` returned 0 hits).

**Rollback steps**:
1. `git revert <merge-commit-sha>` on master.
2. Push revert commit + open follow-up PR explaining rollback.
3. Re-open task 022 with risk-register addendum naming the root cause.
4. Sibling worktrees: rebase off post-revert master.

**Rollback complexity**: LOW. Mechanical rename revert is itself mechanical. The forward fix (re-doing task 022 after addressing root cause) is the same effort as original.

---

## 9. Conflict-Risk Window Coordination

Per `projects/INDEX.md` 2026-06-26 sweep, **15 active BFF-touching worktrees**. Hot-conflict candidates for the rename:

| Worktree | BFF touch | Conflict probability |
|---|---|---|
| `spaarke-redis-cache-remediation-r2` | `Program.cs` + cache infrastructure | Low — different files |
| `spaarke-ai-platform-unification-r6` | handler registry (8 typed handlers, persona scope) | **HIGH** — may touch `ActionType` enum usages in handlers |
| `spaarke-ai-platform-chat-routing-redesign-r1` | `PlaybookDispatcher`/`CapabilityRouter` | **HIGH** — likely touches dispatch path |
| `ai-spaarke-action-engine-r1` | action handler registry | HOLDS at Phase 0 per spec — no current commits |
| `spaarke-daily-update-service-r4` | NotificationService, playbook membership | HOLDS until R7 ships — no current commits |
| `ai-spaarke-insights-engine-widgets-r1` | widget endpoints | Low |
| Others (8) | varied | Low |

**Coordination action for task 022**:
1. Invoke `/conflict-check` immediately before step 3 of §4 to surface in-flight PRs touching `ActionType` symbol.
2. If any HIGH-probability sibling has an open PR, post a comment requesting 24h merge pause until rename lands.
3. After rename merges, post on those worktree READMEs: "rebase required; mechanical s/ActionType/ExecutorType/g in BFF code paths."

---

## 10. Sign-off

**Plan status**: ✅ Complete.
**Source modifications**: ZERO (planning task, doc deliverable only).
**Files touched**: this plan + `current-task.md` + `TASK-INDEX.md` (state updates only).
**Hand-off**: task 022 follows §4 step-by-step. Manual exclusion verification (§3) is the load-bearing safety net; do NOT skip.
**Open risks for task 022**: (1) sibling-worktree conflict per §9 — coordinated via `/conflict-check`. All other risks are mitigated per §5.
