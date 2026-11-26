# ISS-010 (#3184) part C: the commit's access memo — design for uac-r2 approval (2026-11-25)

> **Status:** design only. No code has been written. Nothing is built until uac-r2 approves.
> **Basis:** uac-r2's binding design ("uac-r2's design for C" in `iss-010-commit-latency.md`, points (a)–(e)).
> **Counts:** the part 1 harness profile (PR #3193), the four-action decision. In that profile, the caller-access and `sprk_recordtype_refs` counts match the live trace.
> **Line numbers:** branch `perf/decision-commit-latency` at `b67488d777`. File paths are relative to `src/server/api/Sprk.Bff.Api/`.

## 1. Call sites on the commit path, with counts

| Class | Site (file:line) | Asks |
|---|---|---|
| (c) probe `RetrievePrincipalAccess` | Choke point: `DecisionExec.CallerHoldsAsync` `Services/Signals/Actions/DecisionActionExecutors.cs:253`. It is reached from: | **17** |
| | commit check: `DecisionCommitService.cs:1301` → `:1894` → `DecisionCallerRights.cs:61` (AppendTo, matter) | 1 |
| | `ChildCreateExecutorBase.PreflightAsync` core AppendTo `DecisionActionExecutors.cs:1270` (via `:282`), preflight ×4 + execute ×4 | 8 |
| | the same method, assignee contact AppendTo `:1274` (task, to-do, work; ×2) | 6 |
| | `AssignTaskExecutor.PreflightAsync` Task-type row AppendTo `:1481` (×2) | 2 |
| (c) route core `RetrievePrincipalAccess` | `CallerWriteAuthorization.RightsOnAsync` `Services/Dataverse/CallerWriteAuthorization.cs:781`, via `CallerMayAppendToAsync` `:466`. Per step: budget 2, task 4, to-do 3, work 6 (two step-5 passes). | **15** |
| (a) probe privilege set | `DecisionExec.CallerHoldsPrivilegeAsync` `DecisionActionExecutors.cs:268`, from `:1266` (Create privilege, preflight ×4 + execute ×4) | **8** |
| (a) route core privilege set | `CallerMayCreateInTableAsync` `CallerWriteAuthorization.cs:116` (5); `CallerMayAppendToOrganizationOwnedAsync` `:522` (5) | **10** |
| (b) `sprk_recordtype_refs` | `PairTypeTableAsync` `CallerWriteAuthorization.cs:313` (4); `CallerMayAppendToOrganizationOwnedAsync` row read `:530` (4) | **8** |

Totals: 32 `RetrievePrincipalAccess`, 18 privilege sets, 8 `sprk_recordtype_refs`. Two facts about how these asks are ordered:
- **All of a step's caller-access asks come before that step's one write.** In `RecordCreationService.CreateAsync`, steps 1–10 make the checks (step 5 at `RecordCreationService.cs:736`, step 6 at `:816`). Step 11 is the single app-only create (`:230`). Step 12, post-create (`:268`), asks no caller-access question: `CoreAncestorRestamper`, `SecureChildShareSynchronizer`, `AssignedAccessMaterializer`, `SecureRootFilingGate` and `SecureRootInheritance` reference none of `IDataverseUserClient`, `CallerRecordAccessProbe` or `CallerWriteAuthorization`.
- **Five of the 15 route-core asks cannot be saved.** They target organisation-owned rows (`sprk_recordtype_ref` ×4, `transactioncurrency` ×1). Dataverse refuses `RetrievePrincipalAccess` on those tables (live 400, see the `CallerWriteAuthorization.cs:469` comment), so under (d) they are never cached.

## 2. How the memo is carried (recommendation)

**Recommended: one object, `DecisionAccessWindow`** (new, `internal sealed`, in `Services/Signals/`).
- **Who creates it:** `DecisionCommitService` only, once per commit.
- **How it is carried:** in an `HttpContext.Items` slot under a private key. Readers can only call `Of(http)`, which returns null when no commit is open.
- **Precedent:** the `CallerWhoAmIMemo` / `CallerMetadataReadMemo.RequestScope` pattern (lock the request, then `Items`).
- **Why not pass it down as a parameter:** the route core is reached only through `HttpContext`, via `DecisionRouteCores.CreateChildAsync(http, …)` → `ChildRecordEndpoints.CreateAsync`. Passing the object down would change the signatures of a shared REST handler.

What the window holds:

| Store | Key | Kept | Cleared |
|---|---|---|---|
| (c) probe rights | (caller oid, entity set, record) → `AccessRights` | A 2xx mask, or a 403 (`None`, Dataverse's own denial) | After every executor step |
| (c) route-core `RetrievePrincipalAccess` GETs | path (it names the caller's systemuserid and the target) | Success responses only | After every executor step |
| (a) probe privileges | (caller oid, privilege) → Held / NotHeld | Never `Faulted` | When the window closes |
| (a)+(b) route-core GETs | path: `RetrieveUserSetOfPrivilegesByNames(`, `sprk_recordtype_refs(` | Success only | When the window closes |

**Readers, and what does not change:** `CallerRecordAccessProbe`, `CallerWriteAuthorization` and `RecordCreationService` are not edited.
- `DecisionExec.CallerHoldsAsync` (`:253`), when a window is open, asks `probe.GetCallerRightsForRecordsOrUnknownAsync(token, [(set, id)])`.
  - This is the same OBO, `WhoAmI` and `RetrievePrincipalAccess` path with the same not-found schedule. The only difference is that it keeps "unknown" (null) apart from a 403.
  - Only a non-null answer is stored; null still denies.
  - With no window, it calls `GetCallerRightsAsync` exactly as today.
- `DecisionExec.CallerHoldsPrivilegeAsync` (`:268`), when a window is open, asks `CallerPrivilegeAnswerAsync`.
  - `CallerHoldsPrivilegeAsync` is already defined as `== Held` on that method (`CallerRecordAccessProbe.cs:544`), so the answer is the same.
  - With no window, nothing changes.
- **Route core:** `ChildRecordEndpoints.cs:322` passes `DecisionAccessWindow.Of(httpContext)?.CallerClient(user) ?? user` to `RecordCreationPrincipal.ForCaller`.
  - `CallerClient` is an `IDataverseUserClient` decorator. It answers the four GET shapes above from the window and passes everything else through.
  - It returns ONE wrapper per inner client, so part 1's metadata memo (keyed per client instance) still shares across the four creates.
  - As a local belt-and-braces measure, it also forgets its (c) answers on any write that goes through it.
- **Callers with no window ask exactly as today.** No window means: REST routes, the chat tool, jobs, the billing decisions (`DecisionCallerRights.cs:89`, token-based, no `HttpContext`), the Do-lane replay (`DecisionCommitService.cs:960`) and everything after the write loop.

**Key: one mask per (caller, table, record), not one entry per right. This needs uac-r2's confirmation.** `RetrievePrincipalAccess` returns the full mask, and each caller derives its right from it. Keying by right would make a second wire ask for the same answer. Every (c) ask in this profile is AppendTo, so the saving is the same either way.

## 3. Where the orchestrator clears it

All of these are in `DecisionCommitService.CommitCoreAsync`:
- **Open:** just before the FR-51 check, `:1301`. The commit check, all preflights (`:1356–1381`, write-free by the `IDecisionActionExecutor.PreflightAsync` contract, `DecisionActionExecutors.cs:153`) and step 1's execute recheck share window 0.
- **Close on the replay branch:** at the top of the `finishing` branch (`:1304`), before `CloseAllAsync` writes.
- **Each step:** around `executor.ExecuteAsync` (`:1458–1468`), call `BeginStep(executor)`, then `EndStep()` in a `finally`.
  - `EndStep` clears both (c) stores after **every** executor step: Done, Refused, Failed or thrown, with no read-only exemption.
  - So "did the step write?" is never asked. Every writer is covered: app-only creates, restamps, shares, materializer grants, caller PATCHes.
- **Close after the loop:** before step (5), `:1485`. The Decision Record write, `CloseAllAsync`, `AfterRecordedAsync` and the email all run with no window.
- **Dispose** removes the slot on every return path.
- **Inside a step:** a step's own asks may reuse earlier answers only when all of that step's asks come before its write. That is true only of `ChildCreateExecutorBase` (one `CreateChildAsync`, `:1318`, whose route core checks before step 11).
  - `BeginStep` suspends the window (`Of` returns null) for any other executor's `ExecuteAsync`: revise-budget, approve-variance, the messages, the inquiry.
  - Making `ChildCreateExecutorBase.WriteAsync` (`:1306`) `sealed override` lets the compiler stop a subclass from adding a second write.

## 4. Why (a) and (b) are safe for the whole commit

The commit path writes only:
- child rows (`RecordCreationService.cs:230`);
- stamp columns (`CoreAncestorRestamper.cs:799`);
- record shares (`SecureChildShareSynchronizer.cs:770/1135/1141`, `AssignedAccessMaterializer.cs:1938/1941/1992`);
- the Decision Record, the Signal close and service requests (the ontology writer).

None of them touches `systemuserroles`, `teammembership`, `teamroles` or `sprk_recordtype_ref`.
- **Roles and team membership:** the only BFF code that associates a role is `ExternalCollaboratorPlacement.cs:46` (external plane), and nothing in `Services/Signals` or `Services/RecordCreation` references it. `SecureRootInheritance.Cascade.cs:1680` only *reads* role membership.
- **Record shares:** they change record access, which is class (c) and is cleared per step. They never change privileges: `RetrieveUserSetOfPrivilegesByNames` reflects roles, including roles held through owner-team membership.
- **`sprk_recordtype_ref`** is an organisation-owned catalog that the commit only reads. The catalog row is the same for every step.
- **What (a)/(b) may reuse for the whole window:** a NotHeld privilege answer. Faults and non-success responses are never kept.

## 5. Expected saving (part 1 harness, the four-action decision)

| | `RetrievePrincipalAccess` | Privilege sets | `sprk_recordtype_refs` | Calls saved |
|---|---|---|---|---|
| Probe | 17 → 10 (window 0 asks 10 over 3 targets) | 8 → 4 | — | 11 |
| Route core | 15 → 13 (work's second step-5 pass: matter, contact) | 10 → 6 | 8 → 2 | 12 |
| **C as recommended** | 32 → 23 | 18 → 10 | 8 → 2 | **23** |
| C2: probe and route core share one (c) store | 32 → 15 | 18 → 10 | 8 → 2 | **31** |

C2 needs `RightsOnAsync` (`CallerWriteAuthorization.cs:777`) to read the window, which changes access code, so I offer it to uac-r2 as an option and do not recommend it.

| Modelled calls | After part 1 | After C | After C2 |
|---|---|---|---|
| Calls | 141 | 118 | 110 |
| 20 ms per call (serial) | 2,820 ms | 2,360 ms | 2,200 ms |
| 100 ms per call (serial) | 14.1 s | 11.8 s | 11.0 s |
| Live, at ~88 ms per call | — | ≈ 2.0 s saved | ≈ 2.7 s saved |

## 6. Tests (per (e), on the part 1 rig, `tests/integration/data-mutation/**`)

**Must-fire**
- An earlier step's write removes access, and the later step is refused. Step 1's app-only create is followed by a restamp to Secure or Restricted, after which the matter's AppendTo is gone. Step 2 re-asks matter AppendTo and is refused (NotAuthorized, one write landed). Two variants:
  - on the probe wire;
  - on the route-core wire, where the probe still allows.
- A 403 denial is not reused across a step: denied in window 0, then granted before step 2 — step 2 re-asks.

**Must-not-fire**
- Window 0: matter AppendTo goes on the probe wire once across the commit check, the four preflights and step 1's execute.
- The work assignment's two step-5 passes make one route-core ask per target.
- Each privilege set and each `sprk_recordtype_refs` path is asked once per commit.

**Fault path:** each of these is re-asked, and no fault becomes an allow:
- a probe RPA 5xx, or an exhausted 404 (null);
- a route-core RPA 503;
- a `Faulted` privilege answer;
- a failed `sprk_recordtype_refs` GET.

**Scope**
- No window (a REST create, the chat tool's create, a billing decision) gives the same call counts and results as today.
- The window is closed for step (5) and the follow-ups.
- Non-child executors ask the wire on every call during their execute.
- The window can be opened only by `DecisionCommitService`: an internal `Open`, plus an ArchTest source scan with a must-fire control.
- An ArchTest checks that the step-12 services stay free of the caller-access types (the §1 fact), so a later change cannot silently read a stale answer after a write.

**Unchanged:** the seven part 1 equivalence tests (same writes, order, payloads, responses and refusals), the Signals contract baselines and the child-record REST responses byte for byte. The ISS-010 profile test asserts the new counts (23 / 10 / 2).

## 7. Risks and scope

**Risks**
1. **An ask that comes after a write inside a step** would get a stale answer. This is limited to the child-create family by suspension, `sealed override` and the step-12 ArchTest.
2. **Parallel asks** (`CheckCallerMayAppendToAsync` settles lookups concurrently) may both reach the wire. There is no single-flight, so this costs a call but never correctness.
3. **The probe's commit path calls `GetCallerRightsForRecordsOrUnknownAsync`.** This is the same wire path, but tests that mock `GetCallerRightsAsync` do not cover it. The part 1 rig's `ProfiledProbe` answers at the wire, so it does.
4. **Five organisation-owned asks stay uncached** (the live 400 is treated as a fault).

**Out of scope**
- Caching the deterministic "table not supported" 400 (a D item for uac-r2).
- C2, unless uac-r2 chooses it.
- The D items: the business-unit and team lookups, assign-work's re-map, and the harness gap.
- The Assign Task email (an owner decision).
- The OBO exchange and `WhoAmI`, which are already memoised.

**Files to change**
- New `Services/Signals/DecisionAccessWindow.cs`, with the §11 answers:
  - **Existing:** `CallerMetadataReadMemo.RequestScope` (metadata, request-long, never cleared by writes).
  - **Extension:** it cannot hold access answers, which must clear per step.
  - **Cost of doing nothing:** 23 calls, about 2 s live.
- `DecisionCommitService.cs`: open, per-step clear, close.
- `DecisionActionExecutors.cs`: `:253`, `:268`, `:1306`.
- `ChildRecordEndpoints.cs:322`.
- Tests.

No package or DI registration is added.
