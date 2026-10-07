# Task 104: the Dataverse impersonation helper fails closed (ISS-013 / #990)

> **Status**: implemented 2026-09-15 (session 12). Commits: `6be320e82` (implementation), `134bdb73e` (Step 9.5 fixes), `43ac00a18` (ADR-028 A5 correction).
> #990 closes when PR #950 merges. Prerequisite **P3** of ADR-052 §6.
> **Runs before task 036**, which turns impersonated root sets on in the evaluator.

## 1. The problem

`DataverseImpersonation.Apply(request, Guid?)` added **no header** for a null **or empty** id and returned. The
only refusal lived in `DataverseWebApiService.RetrieveMultipleImpersonatedAsync`. A call site that used the
helper, or the request builder, without going through that method therefore sent an **app-only** request.
Dataverse answered it with the application identity's org-wide rights and HTTP 200: no error, no log line.

## 2. What changed

| Where | Change |
|---|---|
| `Spaarke.Dataverse/DataverseImpersonation.cs` | `ApplyAsSystemUser(request, Guid)` sets `MSCRMCallerID`, and `ApplyAsEntraUser(request, oid, tokenTenant, orgTenant)` sets `CallerObjectId`. Together they replace `Apply(request, Guid?)`. Both throw on `Guid.Empty`; the oid path also throws on a tenant mismatch. Each removes the other header first, so a request carries exactly one. There is no "impersonate if I have an id" overload: a caller that does not impersonate does not call the helper |
| `Spaarke.Dataverse/DataverseWebApiService.cs` | `CreateAuthenticatedRequestAsync` calls the helper only when an id is supplied, and **before** acquiring a token; it disposes the request on failure. It is the **single enforcement point** for every impersonated request. A new **protected** constructor takes a `TokenCredential`, used only by tests |
| `IFieldMappingDataverseService`, `IActionSeam`, `UpdateRecordActionCore` | Docs: `null` = app-only; `Guid.Empty` is refused before the write is sent |
| `.claude/adr/ADR-028` A5 | Factual correction ("the helper refuses too"); no MUST / MUST NOT changed |

**Microsoft Learn**, checked 2026-09-15 by the researcher agent:
- `CallerObjectId` with the Entra oid is **preferred**; `MSCRMCallerID` with the systemuserid is **legacy**.
- **Not documented**: what happens when a request carries both headers, and what happens for an unknown, disabled, unlicensed or foreign-tenant user.
- The Delegate privilege `prvActOnBehalfOfAnotherUser` is required and cannot come through a team.
- Effective rights are the intersection of the two users' rights.

## 3. Escalation triggers and premises

| Trigger / premise | Outcome |
|---|---|
| 1: a caller relies on "empty means app-only" on an access-scoped read | **Did not fire.** Reads reach the helper only through `RetrieveMultipleImpersonatedAsync`, which already refused an empty id. The only impersonating writers, `CommunicationProposalApplyService` and `CommunicationCreateTaskApplyService` (5 sites), reject an empty caller id with `CALLER_NOT_RESOLVED` before passing it on |
| 2: a tenant or environment check needs data the helper cannot get | **Did not fire.** The tenant ids arrive as parameters. The environment is not knowable in the helper: `DataverseWebApiService` sends relative URIs on an `HttpClient` whose base address is the configured org. Documented in the XML doc, not invented |
| The POML's line numbers for the refusal (`:953-989`) | Stale; the refusal is at the top of `RetrieveMultipleImpersonatedAsync` |
| Conflict check (`Spaarke.Dataverse` is a hot path) | Soft pass: no master drift. The only open PR on the library is dependabot #875 (csproj only). The smart-todo-r4 worktree is a stale, completed project |

## 4. Design decisions

1. **Split the intents at the helper, not by reinterpreting null.** The ambiguous `Apply(Guid?)` is gone. A caller either impersonates, with a non-nullable id and a throw on empty, or does not call the helper at all.
2. **One enforcement point.** The code review (F1) found that an early `Guid.Empty` guard in `UpdateRecordFieldsAsync` masked the builder: weakening the builder's check left every test green. The early guard was removed, so the builder is now tested directly. The cost is that the app-only `EntitySetName` metadata lookup, which names no user, may run before the refusal.
3. **A protected test constructor, not a public optional parameter.** The BFF container holds a singleton `TokenCredential` (`Program.cs:48`). Dependency injection fills optional public constructor parameters from the container. A protected constructor can never be selected, however the type is registered. The sibling `DataverseWebApiClient` does have that exposure today: ISS-016 / #993, handed off.
4. **Tenant check by parameter.** `ApplyAsEntraUser` refuses when the token's `tid` differs from the org's tenant. The check is only as strong as the caller's sources, and the XML doc says so. Recorded on #989.

## 5. Residuals, recorded deliberately (also on #990)

- **`DataverseWebApiService` still uses one parameter for both intents.** `impersonateSystemUserId` is `Guid?`, so a caller that means to impersonate but passes `null` runs app-only. Every current impersonating writer guards against this. Impersonated reads use a `Guid` that cannot be null. An explicit impersonated-write overload is recommended if a new impersonating writer appears.
- **`ApplyAsEntraUser` has no production caller.** The first Function caller (ADR-052 §6) must take the token tenant from the validated `tid` and the org tenant from configuration (#989).
- **The live canary Test 4 (helper path) runs only under task 034's conditions:** an operator run with the canary provisioned. There is still no Dataverse credential in CI (owner directive).
- **Dataverse's behaviour for unknown, disabled or foreign-tenant ids is undocumented.** It needs a dev-org check before a Function relies on it.

## 6. Tests

| File (auth KEEP path) | What it pins |
|---|---|
| `DataverseImpersonationHelperTests` | Empty ids throw and stamp nothing; the tenant mismatch throws; exactly one header, including when switching between identity types; the literal `MSCRMCallerID` / `CallerObjectId` wire values. Replaces `tests/unit/.../DataverseImpersonationTests.cs`, which pinned the old "empty adds no header" behaviour |
| `DataverseWebApiServiceImpersonationTests` | What the production builder SENDS, captured by a recording handler (hand-written, not `Mock<HttpMessageHandler>`): the exact app-only header set for both a GET and a PATCH; exactly one `MSCRMCallerID` on impersonated reads and writes; an empty id is refused by the builder and no PATCH is sent |
| `ImpersonationNegativeCanaryTests` Test 4 | The live NFR-04 comparison through the helper directly (operator run) |
| `ImpersonationFailClosedTests` | Unchanged behaviour; stale line reference fixed |

## 7. Verification

- Targeted tests: 52/52, run after the orphaned-testhost fix. Covers the helper, the service request shape, task 001, the canary's always-run layers, and the Job B/C apply services.
- Perturbations: **6/6 caught** on the final code (`134bdb73e`); every build succeeded (one file-lock retry). Restore check clean; baseline 34/34 afterwards.
  - P1: remove the systemuserid empty-id refusal → 2 failing tests.
  - P2: let both headers coexist → 3.
  - P3: remove the tenant check → 1.
  - P4: the builder calls the helper when not impersonating → 4.
  - P5: the builder quietly skips an empty id (review F1's exact weakening) → 1.
  - P6: remove the oid empty-id refusal → 1.
- ArchTests: **323/323**.
- Full suite (`Spaarke.sln`): **14,590 passed / 0 failed / 86 skipped** (session-12 baseline was 14,578 / 0 / 86: +12 tests).
- Publish size: master `e0a6f87c4` **45.35 MB / 214 files**, branch `6be320e82` **45.43 MB / 214 files**. Both were built in fresh short-path worktrees (`C:\wt104m`, `C:\wt104b`) and zipped with Compress-Archive Optimal. The +0.08 MB is the branch's cumulative delta: the previous branch head measured the same, so task 104 itself adds ≈0. Well under the 60 MB ceiling.
- CVE: `dotnet list … --vulnerable --include-transitive` → **no vulnerable packages** in `Sprk.Bff.Api`.

## 8. Step 9.5 quality gates

- **adr-check**: 0 violations and 7 warnings. W1 (stale `IActionSeam` / `UpdateRecordActionCore` docs), W2 (unpinned literal), W5 (injectable credential) and W7 (the dropped pairing note) are fixed. W3 is this file. W4 is on #989. W6 (the canary returns when unprovisioned) is accepted as the existing precedent.
- **code-review**: approved with changes; 0 High. Fixed: F1 (the builder masked by a method guard), F2, F3, F5 (doc), F6, F7, F9 and F12. F4 and F5 are recorded as residuals (§5). F8 (the hand-written handler is intended) and F11 (a failed call keeps any earlier identity, harmless) are accepted. F10 is moot: with the early guard removed, the log clause is live again.
- **Lessons**:
  - A method-level guard in front of the real enforcement point makes the enforcement point untestable, so keep one.
  - Stopping a background `dotnet test` with TaskStop kills the shell, not the `testhost`. The orphan holds the test binaries, so the next build's copy step fails and a follow-up test run silently executes the OLD DLLs (a false "52 passed"). Check the build output before trusting a test count.
  - Two perturbation builds failed on compiler file locks. Those are not results, so they were re-run.
