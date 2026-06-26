# Post-Deletion Summary (tasks 051, 052, 053a/b/c — collapsed to single 053)

> **Date**: 2026-06-26
> **Authority**: spec FR-B06 + ADR-038 + inventory finding from task CICD-020

## What was actually deleted

**11 files removed** from `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/`:

### HttpMessageHandler-mock antipattern (9 files)
- `Services/Ai/Handlers/WebSearchHandlerTests.cs`
- `Services/Ai/Nodes/CreateNotificationNodeExecutorTests.cs`
- `Services/Ai/NodeServiceTests.cs`
- `Services/Ai/PersonaResolutionTests.cs`
- `Services/Ai/PlaybookServiceTests.cs`
- `Services/Ai/Safety/GroundednessCheckServiceTests.cs`
- `Services/Ai/Safety/PromptShieldServiceTests.cs`
- `Services/Ai/ScopeResolverServiceTests.cs`
- `Services/Ai/Sessions/SessionRestoreServiceTests.cs`

### DI-registration antipattern (2 files)
- `Services/Ai/Handlers/AutoDiscoveryVerificationTests.cs`
- `Services/Ai/Handlers/HandlerContractTestTemplate.cs`

## Path-check (spec FR-B06)

✅ NONE of the 11 deleted files were under KEEP-protected paths (`tests/integration/{auth,regression,data-mutation,tenant,contract}/**` or `tests/unit/domain/**`). Same-PR replacement rule does not apply.

## Build verification

✅ `dotnet build tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj`: **Build succeeded** (0 errors, 18 warnings — all pre-existing and unrelated to the deletions). The Sprk.Bff.Api.Tests project uses implicit globbing, so deleted files dropped cleanly from the build without csproj edits.

## Tasks consolidated / cancelled

| Original task | Status | Reason |
|---|---|---|
| 051 delete-plugins-tests | cancelled-no-scope | Spaarke.Plugins.Tests has 0 DELETE files (2 KEEP) |
| 052 delete-scheduling-tests | cancelled-no-scope | Spaarke.Scheduling.Tests has 0 DELETE files (8 KEEP); top-5 flakes addressed separately |
| 053a delete-bff-mock-httpmessagehandler | complete-merged | 9 files removed as part of single 053 |
| 053b delete-bff-di-registration-and-null-checks | complete-merged | 2 files removed as part of single 053 |
| 053c delete-bff-remaining-by-directory | cancelled-no-scope | 0 remaining DELETE files |

**Critical path impact**: Phase 2 SERIAL-DEL chain collapses from `050 → 051 → 052 → 053a → 053b → 053c` (6 PRs) to `050-scaffold + 053-collapsed` (2 effective steps). Project critical path shortens by an estimated 4-6 elapsed days.

## Spec FR-B07 compliance (test-PR FULL rigor override)

✅ This 11-file deletion was performed under FULL rigor:
- Path-check: verified no KEEP-protected file removed
- Build verification: passed
- Antipattern targeting: each file matched a specific ban in `.claude/constraints/testing.md` (Mock<HttpMessageHandler> or DI-registration)
- No semantic changes to remaining tests

## SC-10 progress

Spec SC-10 ("Portfolio shape, not count") requires:
- ✅ DELETE category gone (0 remaining DELETE files in inventory after this PR)
- ✅ 6 KEEP path conventions exist (scaffolded in 050; bulk move pending)
- ⏳ Release+Debug full matrix restored and green (waits for cutover task 071 + 7-day soak)

## What's next

- Task 070 (pre-cutover snapshot) and task 071 (cutover) can proceed — Stream B Phase 2 is functionally complete
- Bulk path move (the deferred portion of 050) is a follow-up project — does NOT block cutover
- Long-tail tenant-isolation backfill (1 → many files) is part of the ≥6-month cultural change window
