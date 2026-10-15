# Issue #1410: the Teams/SPA read-time No Access veto honours a secure parent's list

Fix for GitHub #1410 (found by task 064; not caused by it). Branch `fix/uac-r2-1410-secure-parent-veto`, from `origin/master` `11dc9b0da`.

## The defect (class a, fail-open, narrow)

Round 61 item 1 (binding): a No Access entry on a secure matter or project reaches every secure record filed below it, at any depth. The share-time guard (`SecureShareNoAccessGuard.CheckRecordAndSecureParentsAsync`) and the enforcer (`NoAccessShareEnforcer.EnforceForRecordAsync` / `EnforceOnFiledRecordsAsync`) applied it. The read-time veto `AccessibleRecordSetService.ResolveSystemUserDenyVetoAsync` did not. It built each candidate from the record's own id and its own referenced organizations only.

Failure: a user walled off a secure matter, who still reached a secure work assignment filed under it through a team (owner N2), saw that work assignment in Teams/SPA. The enforcer reported that access as "not enforceable, hidden on Teams/SPA", but it was not hidden.

## The fix: one rule, batched

- **The walk is still the one walk.** `SecureRootInheritance.ReadSecureParentsAsync` (one record) and the new `ReadSecureParentsOfManyAsync` (many records of one table) both run ONE climb, `ClimbAsync`. It works level by level, with a visited set per record, and is bounded by `MaxFilingDepth`. A chain past the bound, an unreadable row, filing, pair type or parent flag, or an EMPTY parent flag makes the answer Unverifiable. That is the same decision as before.
- **What changed is how the reads are made:**
  - The one-record shape (`batched: false`) reads every row and every parent with its own query, exactly as before. The guard, the enforcer, inheritance and the sharee rule keep their per-row fault granularity (secure-if-any).
  - The many-record shape reads each level's rows per table in chunks of `IdsPerQuery` (200), and the flags of the records they are filed under likewise. A chunk that faults leaves every record that needed it Unverifiable.
- **`DecideParentsCoreAsync` is split into two pieces:**
  - `NameParentsAsync`: typed lookups, then the pair, which reads its type, cached.
  - `DecideNamedParents`: the flag decision.
  
  The one-record decision is these two pieces with point reads between them; the batched decision is the same two pieces with chunked reads. The behaviour is identical.
- **The veto asks the walk about every work assignment and project.** Owner round 82 (2026-10-08, binding) is "the parent permissions control": a record filed under a secure parent is governed by that parent's No Access entries whatever its own `sprk_issecure` says. That includes the time while inheritance is pending, and a record inheritance left unsecured because it ended Refused or Failed. Verifier pass 1 F1 found that the first cut walked secure candidates only.
  - For a work-assignment or project composition, `ReadSecureParentsOfManyAsync(..., maxDepth: MaxFilingDepth)` runs ONCE over EVERY candidate, before the "nothing could match" return. That return now fires only when no candidate is secure, none has a secure ancestor, none is undecidable and there is no contact axis. A matter (or non-root) composition reads nothing and returns exactly as before.
  - Each distinct secure ancestor becomes a deny-list candidate carrying its OWN referenced organizations, the way the guard asks a parent's list (`CheckForSecuringAsync`).
  - The ancestors join the SECURE deny-list query, which uses the FULL subject set (`ResolveSubjectsAsync`: the systemuser, its linked and oid-bound contacts, their organizations). The subjects are resolved when any candidate is secure OR has a secure ancestor.
  - A match on any ancestor removes the candidate WHOLE, secure or not.
  - A non-secure candidate's OWN list is unchanged (Q4 / N3): a systemuser entry on it removes nothing, and a contact or organization entry removes only the contact's contribution.
- **Fail closed** (matches the veto's existing fault handling, ADR-003). The candidate is removed, secure or not, when:
  - its filing is Unverifiable (an unreadable record, pair type or parent flag, an EMPTY parent flag, or a chain past the bound);
  - an ancestor's referenced organizations are unreadable;
  - the subjects are unreadable (every secure candidate and every candidate with a secure ancestor);
  - the secure query faults (the same set).
  
  A throw reaches the veto's existing catch, which removes every candidate.
- **New dependency:** `AccessibleRecordSetService` takes `IGenericEntityService`, the same app-only reader the guard and the enforcer use. It is a singleton, registered unconditionally in GraphModule.

## Cost (NFR-02)

- **Matter (or non-root) composition:** exactly the reads it made before #1410.
- **Work-assignment or project composition:** always the parent walk. Per level of filing:
  - one row read per 200 rows per table;
  - one parent-flag read per 200 parents per table;
  - a pair-type read per distinct type (cached);
  
  then one referenced-organization read per **50** ancestors per table (`ExternalParticipationService` chunks at `FlagQueryChunkSize` = 50).
- **When it stops at the walk:** no candidate secure, no secure ancestor, no contact axis. Then there is no link read and no deny-list query (pinned by `ManyNonSecureCandidates_UnderNonSecureMatters_CostOnlyTheBatchedWalk`: 250 work assignments take 2 row reads and 2 flag reads, no link read, no deny-list query).
- **Otherwise:**
  - the link reads (one systemuser read, plus one oid read when the user has an oid);
  - the secure deny-list query over the secure candidates plus the distinct ancestors. The reader runs ⌈(candidates + ancestors) / 50⌉ record-id chunks per subject chunk (`NoAccessListReader.ObjectIdChunkSize` = 50), plus the organization-object chunks;
  - with a contact axis and non-secure candidates, the non-secure query.
- Never a read per candidate (`ManyCandidates_AreWalkedInBatches`: 250 secure work assignments take 2 + 2 reads).

## Reuse of #1411 `NoAccessShareEnforcer.ReadCoverageAsync`

Not used, and not a better fit. It answers which entries (any subject) cover ONE record and its parents. That is a per-record read, followed by reading each entry's subject. The veto needs subject-specific matching batched across every candidate, which is `INoAccessListReader`'s job. Reusing it would make the veto N+1.

Both use the same parent walk.

## Tests

`SecureParentReadVetoTests` drives the REAL service, the REAL walk over the in-memory world, and the REAL deny-list matching.

**The rule:**
- A matter's entry hides a secure work assignment filed under it; a sibling under another matter stays.
- The grandchild and the project between are hidden.
- An entry on an organization the PARENT references hides the child.
- A NON-secure parent's entry does not reach the child.
- The climb goes through a non-secure project.
- A matter composition reads nothing.
- In a project composition where an ancestor is also a candidate, both are removed.

**Round 82 (not-yet-secure children):**
- A systemuser wall on the secure parent removes the child.
- A contact-subject or an organization-subject wall on the parent removes it whole.
- It is removed through a non-secure project.
- A non-secure matter's wall leaves it.
- A systemuser entry on the non-secure child itself removes nothing (Q4).
- A parent read fault removes it, while an unfiled sibling stays.
- A mixed secure and non-secure batch is removed in ONE walk.

**Fail closed:**
- A parent flag fault removes the child.
- A fault reading the candidate's own filing removes it.
- A batched-chunk fault removes the chunk, and one chunk's fault leaves the other chunk decided.
- An EMPTY parent flag removes the child.
- Unreadable ancestor organizations remove the child.
- A chain past the bound removes the candidate.
- A cycle ends the climb and the wall still applies.

**Cost:** 250 secure candidates; and 250 non-secure under non-secure matters (2 + 2 reads, no link read, no deny-list query).

`SecureParentReadVetoInheritanceTests` runs the REAL `SecureIfFiledUnderSecureAsync` on the provisioning fixture:
- **Refused** (the creator is walled off the matter) leaves the record unsecured, and the composition still hides it.
- **Failed** (the flag write faults) leaves it unsecured, and a colleague walled off the matter does not see it.

The other construction sites (12) pass `AccessibleRecordSetTestFactory.NoFilingEntities()`, in which nothing is filed under anything.

## Filed separately (not in this PR)

- #1425: the contact plane ignores a secure parent's list.
- #1426: the synchronizer checks only direct parents.

## P1: the enforcer, the same ruling (pre-existing, fixed here)

`NoAccessShareEnforcer.EnforceOnFiledRecordsAsync` reported a CONFIRMED record filed under a walled secure matter or project as `NotSecure` when its own flag was not set, and kept the walled user's direct share. Its comment, "the next enforcement reaches it", is false once inheritance ends Refused or Failed. `EnforceForRecordAsync` inherited the gap through `EnforceEntryAsync`.

**Fix:** the `FlaggedSecure` gate is dropped for confirmed filed records (owner round 82). N5 (the author holds Write on the filed record) and S5 (never the last reader) still apply. An unconfirmed filing is still left alone and reported `children-incomplete`. The entry's OWN object record still answers `NotSecure` when it is not secure (Q4).

**Tests (`NoAccessShareEnforcerTests`):**
- the old "not the wall's" test is inverted: the share is removed;
- N5 negative and S5 negative on a not-yet-secure filed record;
- Update Access (`EnforceForRecordAsync`) on the not-yet-secure record removes the share.

**Seeding proof:** with the gate restored, all 4 tests fail (55 others pass).

## F2 (verifier pass 2): the Assigned-To materializer, the same ruling

The problem, with P1 in place: a not-yet-secure (or Refused or Failed) work assignment sits under a walled secure matter.
1. The materializer shared the assignee without asking the matter's list (it asked only when `flags.IsSecure`).
2. The enforcer then removed that share.
3. The materializer recorded the removal as an operator's (`Declined` / `RemovedOutOfBand`), so the share was never restored when the wall lifted.

`AssignedAccessMaterializer` now asks `SecureShareNoAccessGuard.CheckRecordAndSecureParentsAsync(..., AsFlagged, ...)` WHATEVER the record's flag, at three places. The guard answers NotSecure for a record with no secure parent.
- **The share-gone decision:** Walled → `RemovedByNoAccess`, which is restored once the wall lifts; Unverifiable → the existing fault path.
- **`FreshShareAsync`:** Walled → `NoAccess` / `RemovedByNoAccess`; Unverifiable → the fault path. The A3 "suggest, don't share" branch is kept for secure records only.
- **Grant → share conversion:** a walled user is NOT converted, and the grant is left as it is; Unverifiable reports a fault and converts nothing this pass.

**Tests (`AssignedAccessParentWallTests`, 5):**
- a walled assignee is never shared, and is shared once the wall lifts;
- a share the enforcer removed is `RemovedByNoAccess` and is Restored;
- with no secure parent, a removed share is still Declined (control);
- an unreadable parent shares nothing and fails the run;
- a grant is not converted for a walled user, and converts once the wall lifts.

**Seeding proof:** with the pre-F2 materializer, 4 of the 5 fail; the control passes.

**Cost:** the guard's parent walk now runs on each non-secure assignee share decision. That adds a few point reads per root per sync, never per candidate.
