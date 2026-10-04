# Task 033 — FR-19 consumer propagation, and the deletion of the blanket Collaborate stamp

> **Completed** 2026-09-04 · `opus` @ high · rigor FULL · `parallel-safe: false`
> **Depends on** 032 (the evaluator spine). **Register** A-8. **Spec** FR-19.
> Suite **12,074 passed / 0 failed** · ArchTests **191/191** · publish delta **0.00 MB**

---

## What this task actually turned out to be

The POML framed 033 as "add rights enforcement to the mutating routes". **That enforcement was already
there.** All four mutating routes under `/api/v1/external` already required a right before writing:

| Route | Verb | Gate | Present since |
|---|---|---|---|
| `/projects/{id}/todos` | POST | `Create` | before this task |
| `/projects/{id}/documents` | POST | `Create` | before this task |
| `/projects/{id}/events` | POST | `Create` | before this task |
| `/todos/{id}` | PATCH | `Write` (project roots only) | FR-08 / task 009 |

The gates could not fire. `WorkforcePrincipalStrategy` stamped `Collaborate` over **every** project in
a workforce caller's accessible set, so `GetEffectiveRights` returned `Read|Create|Write` no matter what
the grant row said. **The enforcement was real and the input was fabricated**, which is worse than a
missing check: the code reads as guarded, and a reviewer confirming "yes, it requires Write" is correct
and still wrong about the outcome.

So the deliverable was not new gates. It was **making the existing gates true**.

---

## The three changes

### 1. Rights are stored; levels are derived (not the reverse)

`CallerProjectAccess` used to hold an `ExternalAccessLevel` and map it to rights on demand. That
direction cannot carry the evaluator's answer: rights compose by highest-wins union across terms, and a
union need not land on one of the three level constants. Storing a level and re-deriving rights would
round-trip the evaluator's answer through a coarser type — **the exact flattening FR-19 removes**.

Inverted: `Rights` is stored, `AccessLevel` is a derived **display** projection
(`ExternalAccessLevels.ToDisplayLevel`) for the one consumer whose contract is a level string
(`/api/v1/external/me`). The projection degrades **downward** — `Read|Write` without `Create` reports
`ViewOnly`, never `Collaborate` — so it can only ever under-state a grant, never overstate one.

Matters and work assignments gained `MatterAccess` / `WorkAssignmentAccess` as `(recordId → rights)`,
with `AccessibleMatterIds` / `AccessibleWorkAssignmentIds` becoming **derived views** over them — the
same shape task 032 used for `AccessibleRecordSet.RecordIds`. Ids and rights cannot disagree because
there is only one collection.

### 2. The stamp is deleted, and nothing replaced it

`WorkforceProjectAccessLevel` is gone; `grep` returns **0** across `src/` and `tests/`. No per-plane
default replaced it. A plane-wide level now only legitimately exists as an evaluator **term**
(`AccessibleRecordSetService.MembershipTermRights`), where it composes under `max()` instead of
overwriting every other term.

### 3. The matter/WA asymmetry is gone

`UpdateTodo` carried a long comment explaining that matter and work-assignment access were "bare id sets
with no level anywhere in the pipeline", so for those root types **membership implied write** — a caller
who would have been ViewOnly on a project could edit matter-parented to-dos.

That comment was an accurate description of the code, and it was **load-bearing in the wrong direction**:
it read as a settled design decision, so the next reader honoured it rather than fixing it
([`FAILURE-MODES.md` AP-12](../../../.claude/FAILURE-MODES.md)). Tasks 032/033 removed its premise — grant
rows always carried `sprk_accesslevel` for all three root types; the level was simply dropped at
partitioning. All three roots are now gated identically.

---

## Two defects caught while building this

### 🔴 `HasFlag(AccessRights.None)` is always true — a fail-open reachable by caller error

`IsOperationPermittedAsync(principal, entityType, recordId, requiredRights, ct)` is the new rights-aware
check on `IAccessibleRecordSetService`. The obvious implementation is
`set.RightsFor(recordId).HasFlag(requiredRights)`.

`AccessRights` is a `[Flags]` enum, so **zero is a subset of every set** and `anything.HasFlag(None)`
returns `true` — including `AccessRights.None.HasFlag(None)`. A caller that computed its requirement
dynamically and landed on `None` (an unmapped operation, a defaulted field, a mis-parsed config value)
would be granted permission on **any** record, *including one it cannot see at all*. A fail-OPEN, on the
one method whose entire job is to deny, reachable purely by caller mistake.

Guarded explicitly, logged as a caller bug, and pinned by
`IsOperationPermittedAsync_RequiredRightsNone_DeniesInsteadOfPermittingEverything`, which asserts **both**
the in-set and out-of-set case — both return `true` without the guard.

### The two guards became one, which is stronger

`UpdateTodo` had a scope check followed by a rights check. Because every rights accessor returns `None`
for an unreachable record, "out of scope" and "insufficient rights" are now **one expression**. A future
root type cannot be added to the scope branch and forgotten in the rights branch.

`ExternalTodoScopeTests` contained an assertion pinning *which* guard denied, written precisely because
deleting the scope check alone would otherwise leave every test green. Its reason changed, so its comment
was rewritten rather than left to rot — it now pins that out-of-scope and under-privileged callers get the
**same** response, so neither can infer which they are.

**Verified by perturbation, not asserted**: removing the single guard fails **11 of 19**
`ExternalTodoScopeTests`. Restored, 19/19 green.

---

## Escalation triggers — both checked, neither fired

| Trigger | Finding |
|---|---|
| A mutating route with no resolvable scoped root | **Does not fire.** 12 `Map{Post,Patch,Put,Delete}` sites exist under `Api/ExternalAccess/`. 4 are the scoped routes above. 7 are on `adminGroup` (`/api/v1/external-access`) behind `DelegationRuleFilter` — FR-07's surface, which the POML explicitly says not to duplicate. The 12th (`/api/dataverse/fetch`) is a **read** that uses POST for a FetchXML body. |
| Stamp deletion reduces rights for a legitimate flow | **Does not fire.** `MembershipTermRights` is `Read\|Write\|Create` — byte-identical to `Collaborate`. Membership-derived records keep exactly what the stamp gave them. Only a caller whose *sole* source is a ViewOnly grant loses Write, which is the intended demotion. Pinned by `GetEffectiveRights_WorkforceMembershipProject_IsCollaborateEquivalent`. |

---

## ⚠️ User-visible behavior change (deploy note)

Three previously-permitted operations now 403:

1. A **workforce** caller holding only a ViewOnly grant on a project can no longer create to-dos,
   upload documents, create events, or update to-dos on it.
2. A caller with a **ViewOnly matter** grant can no longer update matter-parented to-dos.
3. Same for a **ViewOnly work-assignment** grant.

All three are correct per FR-19 and register A-8 — a View Only grant must not permit a write on any
route — and all three were reachable before. Read behavior is **unchanged**: the id sets read-scoping
consumes have the same members as before (derived from the same keys), including for a grant row whose
level is null, which keeps its id and contributes `AccessRights.None`.

---

## Quality gates

**`adr-check`** — 0 violations. Verified clean on ADR-001 (no controllers), ADR-007 (no `Microsoft.Graph`
leakage), ADR-008 (no global auth middleware — gates stay in handlers/filters), ADR-009 (no
`IMemoryCache`), ADR-013 (no CRUD→AI internals), ADR-028 A4 (no `.WithClientSecret`), ADR-003 (no cached
*decisions*; deny codes present on all 4 mutating gates). ADR-003 A1's evaluator MUSTs — `(recordId →
rights)`, additive max, vetoes after the max, fail-closed — all hold.

**`code-review`** — 0 critical.

- ⚠️ `ExternalProjectDataEndpoints.cs` is **787 lines**, past the checklist's 500 "critical" line. Per
  [CLAUDE.md §11.5](../../../CLAUDE.md) and [`COMPONENT-COMPLEXITY.md`](../../../docs/standards/COMPONENT-COMPLEXITY.md)
  this is judged on **cohesion, not LOC** — it is one route group's mappings plus their handlers, a single
  reason-to-change, and the LOC ratchet was retired 2026-08-20. This task's delta is **+22 lines** (four
  deny-code extensions and comment rewrites). Accepted, recorded here rather than silently passed over.
- ℹ️ The `??=` memoisation of the derived id sets is not thread-safe. Worst case is computing an identical
  `HashSet` twice; the principal is per-request. Identical to the pattern already in
  `AccessibleRecordSet.RecordIds` and `ExternalGrantSet.Matters`.
- ℹ️ `GetEffectiveRights` is `O(n)` over a `List`. Pre-existing and unchanged — and `UpdateTodo` now does
  **one** scan where it previously did two.
- AI smells: 0 new interfaces, 0 new DI registrations, 0 catch-log-rethrow added (the 3 in the module are
  pre-existing `OperationCanceledException` rethrows in files this task did not touch).

**Placement Justification (CLAUDE.md §10)** — no new component. One **method** added to the existing
`IAccessibleRecordSetService`, which is already the single evaluator; the alternative (a second service)
would have split the authorization decision across two places. No new endpoints, no new DI registrations,
no package changes — `git diff origin/master...HEAD` touches neither `Sprk.Bff.Api.csproj`, `Program.cs`,
nor `Infrastructure/DI/`.

**Publish size (NFR-01)** — measured against a **fresh build of `origin/master`** per §10, not the
recorded baseline:

| | |
|---|---|
| master `eb71df826`, fresh publish | **45.46 MB** |
| branch `work/unified-access-control-r2` | **45.46 MB** |
| **delta** | **0.00 MB** (ceiling 60 — 14.54 MB headroom) |
| zip tool | PowerShell `Compress-Archive -CompressionLevel Optimal` (matches `scripts/Deploy-BffApi.ps1`) |

Worth recording: master measured **45.42 MB on 2026-09-02** and **45.46 MB today**. It grew 0.04 MB in
two days with no involvement from this branch — the baseline-ageing hazard §10 warns about, observed
again. Re-measuring master is the measurement.

**CVE** — `dotnet list package --vulnerable --include-transitive`: no vulnerable packages.

---

## Files changed

| File | Change |
|---|---|
| `Infrastructure/ExternalAccess/CallerPrincipalResolver.cs` | Stamp deleted; `CallerProjectAccess` stores rights; matter/WA rights maps + derived id views; `GetMatterRights`/`GetWorkAssignmentRights`; `RightsFromGrants` |
| `Infrastructure/ExternalAccess/ExternalCallerContext.cs` | `ExternalAccessLevels.ToDisplayLevel` — the lossy-downward reverse projection |
| `Infrastructure/ExternalAccess/AccessibleRecordSetService.cs` | `IsOperationPermittedAsync` + the `None` fail-open guard |
| `Api/ExternalAccess/ExternalProjectDataEndpoints.cs` | PATCH gates all three root kinds; deny codes on all 4 mutating gates; class doc corrected |
| `Api/ExternalAccess/ExternalUserContextEndpoint.cs` | `/me` handles the now-nullable display level |
| `tests/…/CallerPrincipalTests.cs` | Migrated + 5 new (mixed-rights, matter/WA rights, derived-view, display-level) |
| `tests/…/AccessibleRecordSetServiceTests.cs` | 5 new `IsOperationPermittedAsync` tests |
| `tests/integration/auth/…/ExternalTodoScopeTests.cs` | Migrated + 4 new (ViewOnly matter/WA denial, Collaborate-matter positive control, deny code) |
| `tests/…/ExternalModuleRegistryTests.cs`, `ExternalAccessEndpointTests.cs` | Migrated to the new shape |

---

## For the next task

**037/038/039** fill `ApplyVetoPipeline`. The consumer side is now ready for them: a veto that removes a
key from `AccessibleRecordSet.Rights` propagates automatically to `CallerPrincipal`, to the derived id
sets, and to all four mutating gates — no consumer change needed. **A veto must REMOVE the key**, never
write a low rights value: under `max()` a low value is ignored, and now that `None` is explicitly guarded
in `IsOperationPermittedAsync`, writing `None` would *also* be silently rejected as a caller bug rather
than honoured as a denial. Removal is the only representation of no access.
