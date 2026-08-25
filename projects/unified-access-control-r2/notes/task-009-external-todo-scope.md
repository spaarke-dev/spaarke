# Task 009 — scope-check `PATCH /api/v1/external/todos/{id}` (FR-08 / A-7, + H-8a)

> Closed 2026-08-24. Rigor FULL. `sonnet` @ `high`, prescriptive steps.

---

## What was wrong

The handler applied the PATCH with **no record-scope check at all**, and said so:

```csharp
// Note: for update, we can't easily check project membership without looking up the to-do.
// The ExternalCallerAuthorizationFilter already validates the caller is authenticated.
// A stricter implementation would look up the to-do's project (via sprk_regardingproject)
// and verify access — acceptable for now given the app's low blast radius (only the
// authenticated user's linked data).
await dataService.UpdateTodoAsync(id, request, ct);
```

**The stated justification was false.** "Only the authenticated user's linked data" would be true if the route derived the to-do from the caller. It does not — `PATCH /todos/{id}` takes an arbitrary to-do GUID. Any caller who resolved to a `CallerPrincipal` could rename, re-prioritise, re-date, or close **any to-do in the tenant**, including on matters and projects they have no relationship to.

The two sibling handlers on the same route group (`GetTodos`, `CreateTodo`) both check `HasProjectAccess(id)` — they take a *project* id, so the check is direct. This handler takes a *to-do* id, which is the entire reason it was skipped.

## The fix

**Resolve the child's root, then apply the existing gate.** Two additions:

**1. `ExternalDataService.GetTodoRootAsync(Guid todoId)` → `(TodoRootKind Kind, Guid? RootId, string? TodoName)`.** Deliberately mirrors `GetDocumentProjectAndNameAsync` (task 027), which solves the identical child-record-to-root authorization problem for documents — same `public virtual` seam, same absent-or-unreadable = deny contract. Not a new pattern; the second instance of an existing one. Projects all three A-9 root lookups (`sprk_regardingproject`, `sprk_regardingmatter`, `sprk_regardingworkassignment`) per the owner decision below.

**2. The handler gate**, ordered so nothing writes before every check passes:

| Condition | Response | Reason |
|---|---|---|
| To-do absent *or* unreadable | 404 | ADR-003 fail closed |
| Parent is one of the 10 non-scopeable regarding types | 403 | no accessible set exists to check against |
| More than one root lookup populated (`Ambiguous`) | 403 | ADR-003 fail closed — see below |
| Root outside the matching accessible set | 403 | FR-08 — the A-7 fix proper |
| Project root, caller lacks `Write` | 403 | mirrors `CreateTodo`'s `Create` gate |
| Matter / WA root, caller is a member | **allowed** | owner decision; no level exists to gate on |

Plus H-8a: three stale comments naming the deleted `ExternalCallerAuthorizationFilter` / `ExternalCallerContext` corrected to `CallerPrincipalAuthorizationFilter` / `CallerPrincipal`.

## Live-metadata findings (verified via Dataverse MCP `describe`, 2026-08-24)

Applying this project's carried-forward lesson — **verify every column you add** — before writing the `$select`. Two corrections to what the code believed:

1. **`sprk_todo` has 13 regarding-parent lookups, not 11.** The code comment said 11 twice. Actual: `analysis`, `budget`, `communication`, `contact`, `document`, `event`, `invoice`, `matter`, `organization`, `project`, `reportcard`, `servicerequest`, `workassignment`. Comment corrected in place.
2. **`sprk_regardingrecordtype` is a LOOKUP to `sprk_recordtype_ref`, not a string or choice.** Anyone reaching for it as a type discriminator gets a GUID, not a type name. Worth knowing before the next task tries to branch on it.

Columns used in the new `$select` (`sprk_todoid`, `sprk_name`, `_sprk_regardingproject_value`, `_sprk_regardingmatter_value`, `_sprk_regardingworkassignment_value`) all verified present. `sprk_name` is `NOT NULL`, which is what makes a non-null name a reliable existence signal.

## Escalation — RESOLVED by owner 2026-08-24

The POML anticipated this: *"If the To Do's root cannot be resolved through an existing accessible-set entity, STOP and escalate: scoping to 'todo regarding project only' vs denying todos with other parents is a product decision, not an implementation detail."*

**It fired, and the owner ruled: matter and work assignment get the same functionality as project.**

### The question, stated precisely

> For a to-do whose regarding-parent is a **matter** or **work assignment** rather than a project, should `PATCH /api/v1/external/todos/{id}` be allowed when the caller has that matter/WA in their accessible set?

`CallerPrincipal` exposes exactly three accessible sets — `ProjectAccess`, `AccessibleMatterIds`, `AccessibleWorkAssignmentIds` (the A-9 root sets). Of `sprk_todo`'s 13 regarding-parent lookups, those 3 are scopeable and **10 are not**. The initial ship was project-only; the owner widened it to all three.

### ⚠️ The asymmetry the answer runs into — and what was done about it

"Same functionality" cannot be **literally** identical, because the data model does not carry the same information for the three roots:

| Root | Shape on `CallerPrincipal` | Carries an access level? |
|---|---|---|
| Project | `IReadOnlyList<CallerProjectAccess>` — id **+ `ExternalAccessLevel`** | **Yes** (`ViewOnly` / `Collaborate` / `FullAccess`) |
| Matter | `IReadOnlySet<Guid>` | No |
| Work assignment | `IReadOnlySet<Guid>` | No |

Traced to source: `grantSet.Projects` is a collection of `(ProjectId, AccessLevel)`; `grantSet.Matters` and `grantSet.WorkAssignments` are bare id sets. Matter/WA levels do not exist anywhere in the pipeline — they are populated purely for read-scoping via `ScopeDimension`.

So the only implementable reading of "same as project" is **membership implies write** for matter and work assignment, while project-parented to-dos continue to honour the level and require `Write`.

**Consequence, stated plainly**: a matter collaborator whose project-equivalent level would have been `ViewOnly` **can edit matter-parented to-dos**. Matter/WA are therefore slightly *more* permissive than project. Closing that gap requires a per-matter access level in the grant model, which does not exist — it is not a code change in this task's reach.

### Ambiguity is denied (defensive, not requested)

ADR-024 says a to-do has ONE parent, but the 13 lookups are physically independent columns and nothing in Dataverse enforces that. If more than one root lookup is populated, `GetTodoRootAsync` returns `Ambiguous` and the handler denies. Honouring whichever root the caller happens to hold would let them write a record that is also parented somewhere they do not. Covered by a test.

### ⚠️ Read/write asymmetry this creates — recommend a follow-on task

The read and create paths are still **project-only**: `GetTodosAsync` filters on `_sprk_regardingproject_value`, and `CreateTodoAsync` writes only `sprk_regardingproject`. After this widening, a caller can **PATCH** a matter-parented to-do that the same plane will not **list** for them.

That inverts the property the original project-only ship was protecting (write surface never wider than read). The owner's decision is recorded and implemented as asked — but full parity wants `GetTodosAsync` / `CreateTodoAsync` extended to matter and work assignment, which is new endpoint behaviour and belongs in its own task. **Flagged, not silently absorbed.**

## Perturbation results (mandatory per the `review-2026-08-24` constraint)

| Perturbation | Tests failed |
|---|---|
| Remove `HasProjectAccess` (restore A-7) | **1** |
| Remove the `Write`-rights check | **1** |
| Apply the write BEFORE the checks | **8** |
| Drop the 404 guard | **1** |
| *(widening)* Allow any matter — membership check bypassed | **2** |
| *(widening)* Check matter against the PROJECT set (cross-wire) | **1** |
| *(widening)* Treat `Ambiguous` as in-scope | **2** |
| *(widening)* Allow any work assignment — check bypassed | **1** *(was **0** — see below)* |
| *(widening)* Check WA against the MATTER set (cross-wire) | **1** |

**The first perturbation initially failed ZERO tests** — the constraint's exact warning, caught by running it rather than trusting the green suite.

Cause: for a project outside `ProjectAccess`, `GetEffectiveRights` returns `AccessRights.None`, so the **rights** check already denies an out-of-scope to-do. The two guards are redundant for that case, and the test asserted only `403` — which both produce. Deleting the A-7 fix outright left every test green.

Fix (per the constraint — *fix the test, do not drop the perturbation*): the out-of-scope test now asserts **which** guard denied, by matching the response detail. Record-scope denial says "You do not have access to this to-do"; rights denial says "Your access level does not permit…". Removing the scope check now flips the message and fails the test.

**Generalisable**: when two guards deny the same case, a status-code assertion cannot tell them apart, and deleting either one is invisible. Assert the distinguishing observable — or accept that one guard is untested.

**It happened a SECOND time on the widening.** Bypassing the work-assignment membership check entirely failed **zero** tests, because the new WA coverage was a *positive* test only (an accessible WA succeeds) with no negative counterpart. A check with only a happy-path test is not tested — deleting it is free. Fixed by adding `PatchExternalTodo_WhenTodoRootIsAWorkAssignmentOutsideTheAccessibleSet_IsDeniedAndDoesNotWrite`; P8 then fails 1. **Every membership check needs a negative test, and the positive one does not substitute.** Two independent instances of the same blind spot in one task is worth remembering.

Note also that a *first* attempt at that perturbation didn't compile: removing the null guard broke nullable flow analysis. The compiler enforces part of the contract, which is worth knowing but is not test coverage — the perturbation was reshaped to compile before being counted.

## Coverage authored

`tests/integration/auth/UnifiedAccessControl/ExternalTodoScopeTests.cs` — 9 tests (ADR-038 §2 security-auth KEEP path). Task 001 could not pin A-7, so this task owns its coverage.

**The load-bearing assertion is `UpdateCallCount`, not the status code.** A 403 alone would pass even if the PATCH had already been issued. Every deny test asserts the write never happened — which is what makes the P3 perturbation (write-before-check) fail 8 of 9.

### Fixture note — A-7 is NOT untestable offline

`ExternalCollaborationTestFixture`'s docstring claimed handler-level record-scope behaviour on this group "stays out of reach offline" because principal resolution "needs real Dataverse participation data". **That is not true**: the filter resolves through `ICallerPrincipalResolver`, an interface registered `AddScoped` (`ExternalAccessModule.cs:141`), substitutable in `ConfigureTestServices` with no Dataverse at all. The docstring is corrected and the class un-sealed so `ExternalTodoScopeTestFixture` inherits its policy fix rather than duplicating it (CLAUDE.md §11 — extend, don't fork).

This likely unblocks other findings task 001 recorded as offline-unreachable for the same stated reason. Worth a re-check pass.

## §11 Component Justification — the one new component

`ExternalDataService.GetTodoProjectAsync` is the only new production surface (no new interface, no new DI registration, no new package).

1. **Existing** — `GetDocumentProjectAndNameAsync` does exactly this for documents. Verified by grep that no single-to-do read exists anywhere: no `GetTodoAsync`, no `GetTodoByIdAsync`, and the only `sprk_todos({id})` reference in the codebase was the PATCH itself.
2. **Extension** — cannot reuse the document method (different entity set, different lookup). This *is* the extension of an established pattern to a second entity, deliberately keeping the same signature shape, the same `virtual` seam, and the same `(null, null)` = deny contract so the two read alike.
3. **Cost of doing nothing** — the handler cannot resolve the to-do's root, so A-7 stays open: any caller who resolves to a `CallerPrincipal` renames, re-dates, re-prioritises, or closes any to-do in the tenant by GUID.

## Quality gates

| Gate | Result |
|---|---|
| All 7 test projects | **11,383 passed / 0 failed** (+9 = this task's tests) |
| `dotnet build --warnaserror` | clean |
| Publish size (§10 / NFR-01) | **43.70 MB** compressed incl. PDBs — ceiling 60, baseline 43.69; delta +0.01 MB (noise; no packages added) |
| `--vulnerable --include-transitive` | none |
| ADR-001 / 003 / 010 / 024 / 034 | compliant |
| ADR-008 | the scope check is **inline in the handler**, not an endpoint filter. Compliant by the POML's own constraint ("inline service-call acceptable only if the sibling scoped routes do the same — match the file's live convention"): `GetTodos` and `CreateTodo` both check inline. The group-level `CallerPrincipalAuthorizationFilter` remains the filter-based half. |
| ADR-038 | tests on the `tests/integration/auth/**` KEEP path; module-boundary doubles only; no `Mock<HttpMessageHandler>`; no reflection into privates; `{Method}_{Scenario}_{ExpectedResult}` naming |

**Performance note**: the PATCH now costs one additional Dataverse GET (a 3-column single-record read) before the write. That is the same shape the document download path already pays for the same reason — an authorization precheck cannot be free.

**Honest limit on the errored-lookup test**: `PatchExternalTodo_WhenTodoLookupFails_FailsClosedAndDoesNotWrite` stubs a *throw*. In production `GetSingleAsync` catches everything and returns null, so a real Dataverse fault surfaces as the 404 path, not as a throw. The test therefore proves the **ordering** guarantee (nothing is written before the check completes) rather than a realistic production fault mode. Both paths deny; neither writes.

## Decisions

| Decision | Rationale |
|---|---|
| **403 for out-of-scope, 404 for absent** | Matches the sibling handlers' established convention in this file (403 via `HasProjectAccess`) and the POML's acceptance criteria. ⚠️ It does distinguish existence — a caller learns a GUID is real. Accepted because the project-level routes already disclose project existence the same way, so this adds no new class of leak; revisit if the external plane ever adopts uniform 404s. |
| **Made `UpdateTodoAsync` `virtual`** | ADR-038 §4 substitution seam. Without it the "write never happened" assertion is impossible and the scope check is only verifiable by status code — which perturbation P1 proved is insufficient. |
| **Identical deny message for no-root and out-of-scope** | Both are "you don't get this record"; distinguishing them would tell a caller *why*, which leaks whether the to-do is project-parented. |
| **Did not touch `ExternalScopeCharacterizationTests.cs`** | Binding POML constraint — that file is A-17 / pending task 011 on a `parallel-safe:false` surface. |
| **Left H-8a relics in OTHER files** | The POML scoped H-8a to this file. Several remaining mentions elsewhere are legitimate *historical* references ("replaces the old X", "reproduces the old X byte-for-byte"); only claims about *current* behaviour are stale. `ExternalUserContextEndpoint.cs` has two that look genuinely stale — not in scope, flagged here. |
