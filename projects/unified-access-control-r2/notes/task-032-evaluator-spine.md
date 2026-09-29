# Task 032 — evaluator spine: live-data verification before implementing

> Started 2026-09-04. This file records the checks made **before** touching
> `AccessibleRecordSetService.cs`, because two of them changed the plan.

---

## 1. Escalation trigger #2 — evaluated against LIVE DATA, does NOT fire

Trigger: *"If the `sprk_externalrecordaccess` matter/WA rows turn out NOT to carry `sprk_accesslevel`
in live data (null level), STOP and escalate — do not invent a default level for a grant row."*

Source alone could not answer this: `GrantRowSelect` (`ExternalParticipationService.cs:51`) **does**
`$select` `sprk_accesslevel` for every row, but whether live rows *populate* it is a data question.
Queried dev Dataverse directly (Dataverse MCP, 50 rows):

**Every active grant row carries a non-null `sprk_accesslevel`, on all three root types.** Observed
across matters, projects and the work-assignment row: `100000000` ViewOnly, `100000001` Collaborate,
`100000002` FullAccess.

→ The level can be carried from the row it already came back on. **No default is invented, and no
escalation is needed.**

## 2. ⚠️ A trap the task description does not mention: the project path DROPS level-less rows

`ExternalParticipationService.cs:494` filters projects with
`.Where(r => r._sprk_project_value.HasValue && r.sprk_accesslevel.HasValue)` — a project grant row
with a null level **contributes nothing**. The matter/WA partitioning (`:501-508`) has no such filter;
it takes the id unconditionally.

So "just add `&& r.sprk_accesslevel.HasValue` to match projects" would be **a silent revocation**: any
matter/WA row with a null level that grants access *today* would stop granting it. §1 shows dev has no
such rows, but dev is not every tenant, and this path is the security boundary.

**Decision**: carry the level *without* adding the `HasValue` filter to the matter/WA id partitioning.
A row missing a level still contributes its id (behaviour preserved — this task's envelope is
"behaviour-preserving except level fidelity"), and contributes `AccessRights.None` rights, which the
max then cannot widen. Set membership is unchanged; only rights fidelity is added. Recorded because
the symmetrical-looking change is the wrong one, and a later reader will be tempted by it.

## 3. ⚠️ Live duplicate rows prove the highest-wins dedupe is REQUIRED for matters

Projects already dedupe by `GroupBy(ProjectId).Max(AccessLevel)` (`:534-537`) — matters and work
assignments do not, because a `HashSet<Guid>` makes duplicates invisible.

They are **not** hypothetical. In dev, contact **Sarah Chen** (`52bb55e7-…`) holds **five active grant
rows on the same matter** (`b68299c6-…` REAL-2026-123456.02). All five happen to be ViewOnly today, so
no level conflict is currently observable — but the shape is live, and the **org-grant union**
(`:515-529`) adds matter/WA rows from a *second* source, which is exactly how the project path acquires
differing levels for one id (its own comment says so: *"a contact may hold a direct project grant AND
inherit one via an org grant; the strongest level wins"*).

→ Without a max-dedupe, once levels are carried the answer for such an id would depend on **row
order**. The dedupe generalizes to all three root types.

## 4. §11 reuse check — the level→rights mapping ALREADY EXISTS; do not add a second

Step 2 of the POML says "add the `ExternalAccessLevel` → `AccessRights` mapping". **It is already
written**, as `ExternalCallerContext.GetEffectiveRights` (`ExternalCallerContext.cs:55-65`), with the
exact table the constraint specifies.

It is not reusable as-is: it is an *instance* method that resolves a level from `Participations`
(project-only) before mapping it. The mapping *expression* is what is needed elsewhere.

→ **Extract**, do not duplicate: lift the `switch` into one `internal static` function and have
`GetEffectiveRights` call it. That satisfies the constraint's "one internal static mapping function, no
duplicates" **and** root CLAUDE.md §11 (extend the existing rather than fork). Writing a second copy —
which the step's wording invites — would create precisely the drift this project exists to remove, in
the one function where a divergence silently changes rights.

## 5. Escalation trigger #1 — consumer surface (checked)

Trigger fires only if `RecordIds` cannot stay a derived view without changing consumers beyond the
named files. Plan keeps `RecordIds` + `Contains` as derived views over the new `Rights` map, so
`Tier2ScopeFilterInjector` and the module predicates are untouched. Not fired.

## 6. 🔴 THE TRAP THAT WOULD HAVE SHIPPED: the Redis cache drops matter/WA levels

`CacheGrantSetAsync` (`ExternalParticipationService.cs:659-666`) persists:

```csharp
Projects        = ... new CachedParticipation { ProjectId = p.ProjectId, AccessLevel = (int)p.AccessLevel }
Matters         = grantSet.Matters.ToList()          // List<Guid> — IDS ONLY, NO LEVEL
WorkAssignments = grantSet.WorkAssignments.ToList()  // List<Guid> — IDS ONLY, NO LEVEL
```

Projects persist their level. **Matters and work assignments persist ids only.**

So fixing only the *query* path produces a defect that is nearly invisible in testing: on a **cache
miss** matter rights are correct; on a **cache hit** (60-second TTL — i.e. the common case) the level
is gone and rights degrade to `AccessRights.None`. Unit tests bypass the cache entirely, so the whole
suite would be green while production intermittently under-grants.

Under-granting fails closed, so this is a correctness/availability bug rather than a disclosure — but
it is *silent and intermittent*, which is the worst shape to debug.

**Required with the change**: extend `CachedGrantSet` to persist matter/WA levels (mirroring
`CachedParticipation`) **and bump `CacheVersion`** — otherwise entries cached under the old shape
deserialize into the new one with levels missing, reproducing the same bug for one TTL after deploy.

## 7. The shape decision (constrained by "do not touch `CallerPrincipalResolver.cs`")

`ExternalGrantSet.Matters` / `.WorkAssignments` cannot simply *become* level-carrying collections:
`CallerPrincipalResolver.cs:339-340` assigns them straight into `AccessibleMatterIds` /
`AccessibleWorkAssignmentIds`, and that file is **task 033's**, explicitly out of bounds here.

**Plan**: add level-carrying `MatterGrants` / `WorkAssignmentGrants` as the source of truth, and keep
`Matters` / `WorkAssignments` as **derived views** over them so the two cannot drift — the same
technique used for `RecordIds` over the new `Rights` map. Additive; no consumer outside this task's
named files changes.

---

## STATUS — ✅ COMPLETE (2026-09-04)

All five findings were carried into the implementation.

### What shipped

| File | Change |
|---|---|
| `ExternalCallerContext.cs` | `ExternalAccessLevels.ToAccessRights` **extracted** (§4) — `GetEffectiveRights` now calls it. New `ExternalRootGrant` (id + **nullable** level, §2). `ExternalGrantSet` gains `MatterGrants`/`WorkAssignmentGrants` as source of truth, with `Matters`/`WorkAssignments` as **derived views** (§7) |
| `ExternalParticipationService.cs` | Matter/WA partitioning keeps the level; org-grant union keeps it too; `DedupeByHighestLevel` generalizes the project rule (§3); `CachedGrantSet` persists levels + **`CacheVersion` 3 → 4** (§6) |
| `AccessibleRecordSetService.cs` | `AccessibleRecordSet.Rights` + `RightsFor`; `RecordIds`/`Contains`/`Count` derived from it; `GrantedIdsFor` → `GrantedRightsFor`; both compose paths restructured into explicit terms merged by `AccumulateTerm` (highest-wins); `ApplyVetoPipeline` seam wired as an ordered no-op |
| tests | New `AccessibleRecordSetTestFactory`; 5 test files migrated off the now-derived setters; **10 new rights-fidelity tests** |

### Verification

- Clean `dotnet build Spaarke.sln --no-incremental` ✅
- BFF unit **12,062 passed / 0 failed** / 58 skipped
- ArchTests **191/191**
- Publish **44.15 MB compressed** incl. PDBs (ceiling 60; flat vs this branch's 44.14)
- **Perturbation-verified**: making `ViewOnly` grant `Write` turned
  `ComposeAsync_ContactWithViewOnlyProjectGrant_YieldsReadOnly_NotCollaborate` RED with
  `Actual: Read | Write`. The rights assertions are not vacuous.

### Two decisions worth re-reading before extending this

1. **`AccumulateTerm` may only add or widen.** Vetoes are a separate, later step that REMOVES keys.
   Keeping the operations distinct is what stops "No Access" being smuggled in as a low value that
   `max()` would silently discard.
2. **`ApplyVetoPipeline` is an ordered no-op on purpose.** Task 032 fixes the SHAPE and the ORDER
   (pre-max Secure suppression → deny list → Restricted); 037/038/039 fill the slots. The seam exists
   so filling it is an additive change at a named point rather than a re-derivation of where vetoes go.

### Also fixed while in the file

`ExternalGrantSet`'s class doc still claimed matters/WAs "are id sets … not level-differentiated for
those types yet" — false the moment this task landed. Corrected in place; leaving it would have been a
textbook FAILURE-MODES **AP-12** (a stale comment becoming the constraint the next reader honours).
