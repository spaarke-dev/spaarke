# SESSION 18 handoff — MED#10 Cosmos-first refactor (for fresh session)

> **Date filed**: 2026-08-28 (SESSION 18)
> **Branch**: `work/customer-provisioning-orchestration-r1`
> **Last commit**: `f5ef16231` (Bucket B MED/LOW cluster)
> **Purpose**: Fresh-session pickup for the ONE remaining Bucket B item — MED#10 H13 Cosmos-first refactor.

---

## Session 18 final state — 4 commits landed

| Commit | Scope |
|---|---|
| `6baf1fbfd` | ISH-12 controlPlaneEnv rename |
| `97e18c227` | Bucket A (5 pre-dispatch blockers) |
| `f5438373a` | Bucket B credential cluster (5 HIGH) |
| `00341e7c2` | SESSION 18 first handoff |
| `1c8b02fbc` | Bucket B final 5 HIGHs (I5/H0/silent-fail) |
| `f5ef16231` | Bucket B MED/LOW cluster (10 MED + 4 LOW) |

**Verification (last known green)**: 1922 L2 tests pass / 0 failed / 1 skipped. Build 0 warnings / 0 errors.

**Findings state**: 29 of 30 workflow findings CLOSED (13 HIGH + 10 MED + 4 LOW + 2 opportunistic). **1 REMAINING: MED#10.**

---

## MED#10 — full Cosmos-first refactor scope

### The problem

Current H13 `HandleAsync` sequence (`src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/E2EAcceptance/H13E2EAcceptanceGateHandler.cs`):

1. Line 550-590: `UpdateColumnsAsync` — registry PATCH #1 (promoted columns)
2. Line 592-622: `TransitionToReadyAsync` → `UpdateSetupStatusAsync` — registry PATCH #2 (sprk_setupstatus=Ready)
3. Line 634: `MarkCompleteAsync` → `_repository.ReplaceRunAsync` (Cosmos write — writes RunStatus.Completed)

**Split-brain window**: if the Cosmos write at step (3) returns Conflict, the registry has already been PATCHed to Ready but Cosmos remains at RunStatus.Running (from the concurrent winner). Post-Bucket-B-HIGH#7 the guard stays HELD (no lockout leak), but registry-shows-Ready-vs-Cosmos-shows-Running for the concurrent winner's applier duration.

### The correct fix — Cosmos-first

**Principle**: write to your OWN state (Cosmos, ETag-protected, your table) BEFORE mutating someone else's (Dataverse registry). If your write fails, no external state was touched.

**Target sequence**:
1. Prepare run state in-memory (Status=Completed, timestamps, gates, ErrorDetail-if-advisory — currently at line 823-868 of `MarkCompleteAsync`)
2. `_repository.ReplaceRunAsync` — Cosmos write FIRST
3. On Conflict → return Failure Resumable (existing behavior — no registry mutation happened)
4. On Success → do the promoted-columns PATCH (`UpdateColumnsAsync`)
5. On promoted-columns failure → **NEW SEMANTIC** — return HandlerResult.Success with a stale-registry warning; the run IS complete, but registry needs the operator-side SKILL Step 6a to finish the PATCH. Do NOT re-flip Cosmos back to Failed (state churn).
6. Do the setupstatus PATCH (`TransitionToReadyAsync`)
7. On setupstatus PATCH failure → same as step (5) semantic
8. Return HandlerResult.Success

### Refactor mechanics

**Extract 2 helpers from `MarkCompleteAsync`**:
- `PrepareRunStateForCompletion(run, envelope, ...)` → mutates run in-memory, returns nothing. Lines 823-868.
- `WriteCompletionToCosmosAsync(run, etag)` → calls `_repository.ReplaceRunAsync`, handles Conflict/NotFound. Lines 870-891.

**Then in `HandleAsync` at line 550**:
```csharp
// Step 1: prepare + write Cosmos-Completed FIRST (MED#10 Cosmos-first ordering).
PrepareRunStateForCompletion(run, envelope, costReport);
var cosmosResult = await WriteCompletionToCosmosAsync(run, etag, cancellationToken);
if (cosmosResult is HandlerResult.Failure) return cosmosResult;  // Conflict → resume; NO registry writes

// Step 2: registry PATCHes AFTER Cosmos-Completed lands.
try
{
    var promotedColumns = BuildPromotedColumnsForReady(run, DateTimeOffset.UtcNow);
    if (promotedColumns.Count > 0)
    {
        var colOutcome = await _registryClient.UpdateColumnsAsync(...);
        if (colOutcome is not RegistryUpdateOutcome.Success)
        {
            // Log warning; run IS complete; operator SKILL Step 6a picks up the residual.
            LogRegistryStaleWarning(...);
            return new HandlerResult.Success(idempotencyKey);
        }
    }

    var updateOutcome = await _registryUpdater.TransitionToReadyAsync(...);
    if (updateOutcome is not RegistrySetupStatusUpdateOutcome.Success)
    {
        LogRegistryStaleWarning(...);
        return new HandlerResult.Success(idempotencyKey);
    }
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    LogRegistryStaleWarning(...);
    return new HandlerResult.Success(idempotencyKey);
}

return new HandlerResult.Success(idempotencyKey);
```

### Test surface to update

Existing tests in `src/server/services/Sprk.Provisioning.ControlPlane.Tests/Handlers/H13E2EAcceptanceGateHandlerTests.cs`:

Any test that:
1. Sets up a Conflict on `_repository.ReplaceRunAsync` and asserts the registry was NOT PATCHed → new behavior is CORRECT (this is what the refactor guarantees)
2. Sets up registry failure and expects `HandlerResult.Failure(Resumable, RegistryUpdateFailed)` → **must invert** to expect `HandlerResult.Success` with warning-log verification (the run IS complete post-Cosmos-write; registry PATCH failure is now log-and-tolerate, not fail-Cosmos-back)
3. Asserts on the specific order of registry-vs-Cosmos writes → **must invert** the ordering assertion

Add NEW tests:
- `H13_CosmosConflict_DoesNotMutateRegistry_BucketB_MED10` — asserts UpdateColumnsAsync + TransitionToReadyAsync fake mocks were NEVER called when Cosmos returns Conflict.
- `H13_CosmosSuccess_ThenRegistryFailure_ReturnsSuccessWithLog_BucketB_MED10` — asserts Success is returned + a warning log fired + Cosmos-Completed persisted.

### Documentation surfaces to update

1. **My existing SESSION 18 comment block at H13E2EAcceptanceGateHandler.cs ~line 871** (the "DOCUMENTED SPLIT-BRAIN WINDOW" block) — DELETE it; replace with an "AS OF MED#10 SESSION-19: Cosmos-first ordering eliminates the split-brain window" note.

2. **SKILL.md Step 6a registry-stale artifact** (my HIGH#10 fix at line ~1445) — currently says "Do NOT touch sprk_setupstatus (already set by L2 H13)". With MED#10 landed, H13 may NOT have set sprk_setupstatus if the registry PATCH failed. Update the runbook to reflect that possibility + include sprk_setupstatus in the recovery recipe.

3. **DataverseRegistrySetupStatusUpdater.cs** file header — reference the SESSION-19 MED#10 change as the "Cosmos-first ordering" that makes this updater callable AFTER Cosmos-Completed.

### Estimated effort

- H13 HandleAsync + helper extraction: ~45 min
- Test surface updates: ~60 min (existing tests + 2 new tests)
- SKILL.md Step 6a runbook update: ~15 min
- Documentation cross-references: ~15 min
- Build + full L2 test verification: ~20 min

**Total: ~2.5-3h.** Do it in a fresh session with clean context.

### Verification checklist for the fresh session

- [ ] `dotnet build src/server/services/Sprk.Provisioning.ControlPlane.Core/` clean
- [ ] All existing H13 tests pass (with inversions applied)
- [ ] New MED#10 tests pass
- [ ] Full L2 suite: 1924+ passed (up from 1922 with +2 new tests)
- [ ] Grep for "MED#10 documented split-brain window fired" — should be REMOVED from H13
- [ ] SKILL.md Step 6a registry-stale artifact recipe includes sprk_setupstatus recovery

### Post-MED#10 → dispatch task 186

Once MED#10 lands, all 30 workflow findings are closed. Task 186 dispatch via `/provision-environment trial1 --batch runs/trial1-intake.json` is unblocked. Per root CLAUDE.md §4 mandatory task-execute protocol — invoke the skill; never bypass.

---

## Standing binding rules (unchanged from prior handoffs)

- Never CREATE / seed / restore `BFF-API-ClientSecret` (either casing) — auth-v4 task 033 DELETED it 2026-08-24
- Never DELETE `Dataverse-ClientSecret` before 2026-11-23 (auth-v4 owns retirement)
- Sub-Agent Write Boundary: `.claude/**` = main-session only
- Operator uses OWN AAD identity (NEVER service principal) per NFR-11
- Never touch claude.ai Gmail/Calendar/Drive MCPs
