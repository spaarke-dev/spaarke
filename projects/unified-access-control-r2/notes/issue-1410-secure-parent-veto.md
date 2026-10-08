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
- **The veto now asks the walk for secure candidates:**
  - It runs only for SECURE candidates (flags secure or unreadable), only on a table that files under something (work assignment, project), and only once the subjects are readable. It asks `ReadSecureParentsOfManyAsync(..., maxDepth: MaxFilingDepth)`.
  - Each distinct secure ancestor becomes a deny-list candidate carrying its OWN referenced organizations, the way the guard asks a parent's list (`CheckForSecuringAsync`). Those organizations are read once per ancestor table.
  - The ancestors join the SAME deny-list query as the candidates.
  - A candidate is removed whole when it, or any of its ancestors, is denied.
- **Fail closed** (matches the veto's existing fault handling):
  - An Unverifiable filing removes the candidate.
  - An ancestor whose referenced organizations are unreadable removes its children.
  - A reader fault removes the asked candidates.
  - A throw reaches the veto's existing catch, which removes every candidate.
- **Unchanged:**
  - Matter compositions and non-root tables make no reads.
  - Non-secure candidates are not walked (Q4 scope).
  - Secure candidates with no ancestors send the same deny-list query as before.
- **New dependency:** `AccessibleRecordSetService` takes `IGenericEntityService`, the same app-only reader the guard and the enforcer use. It is a singleton, registered unconditionally in GraphModule.

## Cost (NFR-02)

The parent walk adds, per level of filing:
- one row read per table per 200 rows;
- one parent-flag read per table per 200 parents;
- a pair-type read per distinct type (cached);
- one referenced-organization read per ancestor table.

It never makes a read per candidate. The test `ManyCandidates_AreWalkedInBatches` pins this: 250 work assignments under 250 matters take 2 row reads and 2 flag reads.

## Reuse of #1411 `NoAccessShareEnforcer.ReadCoverageAsync`

Not used, and not a better fit. It answers which entries (any subject) cover ONE record and its parents. That is a per-record read, followed by reading each entry's subject. The veto needs subject-specific matching batched across every candidate, which is `INoAccessListReader`'s job. Reusing it would make the veto N+1.

Both use the same parent walk. There is no file overlap with #1411 or #1406.

## Tests

`tests/integration/data-mutation/ExternalAccess/SecureParentReadVetoTests.cs` drives the REAL service, the REAL walk over the in-memory world, and the REAL deny-list matching.

**The rule:**
- a matter's entry hides a secure work assignment filed under it, and a sibling under another matter stays;
- a matter's entry hides the grandchild and the project between them;
- an entry on an organization the PARENT references hides the child;
- a NON-secure parent's entry does not reach the child;
- the climb goes through a non-secure project;
- a non-secure candidate is not walked;
- a matter composition reads nothing.

**Fail closed:**
- a parent flag read fault removes the child, while an unfiled secure sibling stays;
- a fault reading the candidate's own filing removes it;
- a batched-chunk fault removes the whole chunk;
- an EMPTY parent flag removes the child;
- unreadable ancestor organizations remove the child;
- a chain past the bound removes the candidate;
- a cycle ends the climb and the wall still applies.

**Cost:** the batching test above.

The other construction sites (12) pass `AccessibleRecordSetTestFactory.NoFilingEntities()`, in which nothing is filed under anything.

## Known limit (not changed, reported)

A work assignment or project filed under a secure parent but not yet flagged secure (the ≤5-minute window before the inheritance job secures it) is a non-secure candidate, so it is not walked. The share-time guard does check parents for such a record. Q4 scopes the internal wall to secure records, so this follows the rule as written. It is flagged for the main session to decide.
