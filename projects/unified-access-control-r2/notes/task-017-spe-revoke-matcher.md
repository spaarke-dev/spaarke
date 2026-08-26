# Task 017 — SPE revoke matcher, the H-8b relic, and honest SPE reporting

> **Findings**: A-13 (FR-16) · register **H-8b** · discharges the **task-016** constraint
> **Files**: `Api/ExternalAccess/RevokeExternalAccessEndpoint.cs`,
> `Api/ExternalAccess/Dtos/RevokeAccessResponse.cs`,
> `Infrastructure/ExternalAccess/SpeContainerMembershipService.cs`,
> `Api/ExternalAccess/ProjectClosureEndpoint.cs`,
> `tests/integration/auth/UnifiedAccessControl/SpeRevokeMatcherTests.cs` (new, 17),
> `ProjectClosureCascadeTests.cs` (+3), `GrantLifecycleCharacterizationTests.cs`,
> `ExternalAccessEndpointTests.cs`, `tests/e2e/.../revocation.spec.ts`,
> `AccessGrantModal.test.tsx`

---

## 1. The escalation question, answered first

The POML arms an escalation: *"If removing the best-effort SPE path would break a legacy/invite flow that
DOES add container members, STOP and escalate."*

**It does not fire. Nothing in this codebase adds an SPE container permission:**

| Candidate writer | Verdict |
|---|---|
| `SpeContainerMembershipService.GrantMembershipAsync` | **zero callers** repo-wide (excluding its own definition) |
| `GrantExternalAccessEndpoint` | returns `SpeContainerMembershipGranted: false`, with the comment *"Broker-only: no synthetic SPE container membership is granted on the external path"* |
| `InviteExternalUserEndpoint` | no SPE reference at all |
| `InviteAndGrantExternalUserEndpoint` | no SPE reference at all |

So the SPE removal path is **a cleanup path for ACLs this product did not create** — legacy rows from a
pre-broker version, or entries an admin added outside Spaarke. That reframing is what makes
`NoPermissionFound` the *ordinary, healthy* answer rather than a problem, and it is why the path is worth
keeping rather than deleting: the ACLs it cleans up are exactly the ones nothing else will.

---

## 2. A-13, and why the fix is deletion rather than repair

`RevokeExternalAccessEndpoint` carried its **own private copy** of the SPE revoke logic:

```csharp
var contactIdStr = contactId.ToString();                       // a GUID
… upn?.ToString()?.Contains(contactIdStr, …) == true            // searched inside the UPN
```

Membership is written with `userPrincipalName` = the contact's **email**
(`GrantMembershipAsync`). An email never contains a GUID, so the predicate matched **nothing, ever**. It
then did the damaging part:

```csharp
if (contactPermission?.Id == null)
    return true;   // "Not a failure — the permission may have already been removed"
```

Never matching + reporting success on no-match = `/revoke` claimed the SPE permission was gone every
single time, while the ACL entry sat untouched.

**The discovery that shaped the fix**: `SpeContainerMembershipService.RevokeMembershipAsync` already did
this correctly — `FindPermissionByEmail` does an exact, case-insensitive match on
`userPrincipalName` (falling back to `email`) — and had **zero callers**. The endpoint had forked a
working implementation and broken it.

So the fix is not "patch the matcher" but "delete the fork" (CLAUDE.md §11 — default to reuse). What
stays endpoint-side is the endpoint's actual job: turn a contact id into the email key
(`contacts({id})?$select=emailaddress1`, via the already-injected `DataverseWebApiClient` — no new
service), and report the outcome.

`IGraphClientFactory` left the handler's signature with it; this endpoint no longer talks to Graph at all.
Five now-dead usings went too.

---

## 3. Why a bool could not carry the answer

The old `SpeContainerMembershipRevoked` conflated four states and answered `true` for two of them. Per the
ADR-003 constraint — *"distinguish 'confirmed absent' from 'match failed'"* — the response now carries
`SpeContainerOutcome`:

| Outcome | Meaning | Operator action |
|---|---|---|
| `NotAttempted` | No `ContainerId`, or an org-grant revoke (no single grantee → no key) | none |
| `PermissionRemoved` | Matched and deleted — a legacy/admin ACL was cleaned up | none |
| `NoPermissionFound` | Container read; this contact holds none. **Expected under broker-only** | none |
| `Failed` | Could not read, match, or delete. **They may still have file access** | retry |

`SpeContainerMembershipRevoked` is retained and is now *honest* — true only for `PermissionRemoved` — so
existing readers get a correct value rather than a constant. The e2e spec asserts the two can never
disagree.

Note the deliberate choice on **"contact has no email"** → `Failed`, not `NoPermissionFound`. Without the
key, a permission that exists is unfindable; that is an unknown state, not an absence. Reporting absence
there would be A-13's mistake in a new spot.

---

## 4. H-8b — the relic, and what was NOT deleted

**`WebRoleRemoved` is gone** from `RevokeAccessResponse`. It was hard-coded `false` at every call site
because Spaarke does not manage Power Pages web roles — a field describing a subsystem that isn't there,
which can only mislead.

**`GrantMembershipAsync` was NOT deleted**, and that is a judgement call worth stating. The H-8b
constraint says "do not leave dead branches that imply container members are added on grant." It is dead
(zero callers) and it does imply that. But it is also (a) the definition of the identity key the revoke
matcher must match, and (b) the documented counterpart of a method that genuinely matters. Deleting a
public service method is a wider change than this task's scope, so instead it now carries an explicit
⚠️ header stating it has no callers by design, that Spaarke is broker-only, and that wiring it up requires
revisiting that decision. **Flagged for the owner** as a deletion candidate.

---

## 5. Discharging the task-016 constraint

Task 016 found that `ListExternalMembersAsync` caught both `ServiceException` and `Exception` and returned
`[]`, so `RemoveAllExternalMembersAsync` answered "0 removed" whether the container was empty **or Graph
was unreachable** — and close-project reported `200 OK` while external users kept file access. It built
the receiving guard (`container_not_cleared`) but could not reach it.

Two changes make both failure modes observable:

- **`ListExternalMembersAsync` propagates.** An empty list now means exactly one thing: the container has
  no external members.
- **`RemoveAllExternalMembersAsync` returns `SpeBulkRemovalResult(Removed, Failed)`** instead of a bare
  `int`. Per-member failures still do not abort the loop (stopping early leaves *more* access in place) —
  they are counted. `IsComplete => Failed == 0`.

`ProjectClosureEndpoint` consumes it and its guard is now **reachable and tested**: a listing failure and
a *partial* clear both return `container_not_cleared`. The partial case is the subtler one — the call
completes and returns a number, so under the old `int` contract it looked like success.

Both methods became `virtual` (ADR-038 §4 seam, `DataverseWebApiClient` precedent) so this is testable
without mocking Graph transport (ban B1).

---

## 6. Known gap, deliberately filed: org-grant SPE cleanup

Task 010's constraint asked this task to *"assess and either fix or file"* the fact that SPE removal keys
on a single `request.ContactId` while the Dataverse sweep keys on the logical grant. For an
**organization** grant those are different sets.

**Assessed; filed, not fixed.** An org revoke passes `ContactId = Guid.Empty` (task 073 #7), so there is
no single grantee and no single email. Cleaning up members' ACLs would need an organization → members →
emails expansion that does not exist on this path — the same expansion declined in task 016 for cache
invalidation. The endpoint reports `NotAttempted`, which is honest.

**What bounds it**: under broker-only, no member ACLs are created in the first place, so in practice there
is nothing to clean. The exposure is legacy org-era ACLs only. Fixing it properly belongs with whoever
introduces org→member expansion (a candidate for the Phase 1 evaluator work, which needs the same
expansion for `FR-24`/`FR-25` org terms).

---

## 7. Tests — 17 new + 3 added to the cascade suite

At `tests/integration/auth/UnifiedAccessControl/SpeRevokeMatcherTests.cs` (ADR-038 KEEP path per the
task-001 constraint — **deviation from the POML `<outputs>`** unit path, same reasoning as tasks 007/016:
the constraint is explicit and the KEEP path is deletion-protected).

The load-bearing assertion is `CapturedEmail` — A-13 was entirely about *which key* the revoke matched on,
so the tests assert the key actually passed to the service, not merely that a call happened.

### Perturbation results

| Perturbation | Tests failed |
|---|---|
| Match on the contact GUID again (A-13 root cause) | **2** |
| Restore false success on no-match (A-13's cover-up) | **3** |
| Report a Graph error as genuinely-absent | **2** |
| Re-swallow listing failures (the task-016 finding) | **2** |
| Ignore per-member removal failures (`IsComplete => true`) | **1** |

### ⚠️ The perturbation run caught a hole in my own tests

Re-swallowing listing failures **initially passed everything**. The closure tests substitute
`RemoveAllExternalMembersAsync` at its seam, so they never exercised `ListExternalMembersAsync`'s error
path at all — the actual fix the task-016 constraint asked for was **untested**, and only the perturbation
revealed it. Four service-level tests were added
(`ListExternalMembersAsync_WhenGraphFails_ThrowsRatherThanReturningEmpty`,
`RemoveAllExternalMembersAsync_WhenTheListingFails_Propagates`, and two on `SpeBulkRemovalResult`), after
which the perturbation fails 2. Worth recording: mocking at a seam proves the *caller* handles a failure,
never that the *callee* reports one.

### Deletions

Three `RevokeAccessResponse` DTO tests in `ExternalAccessEndpointTests.cs` were removed — positional-record
property round-trips (ban B16) that asserted nothing about behaviour and centred on the deleted
`WebRoleRemoved`. Not a KEEP path, so no FR-B06 replacement obligation; the replacement exists anyway.

### The e2e spec was pinning the bug

`revocation.spec.ts` asserted `speContainerMembershipRevoked === true` after a revoke — which **passed for
the wrong reason**, because the broken matcher returned `true` unconditionally. Flipped to assert an honest
outcome (`PermissionRemoved` or `NoPermissionFound`, never `Failed`) and that the boolean agrees with it.

---

## 8. Gates

| Gate | Result |
|---|---|
| All seven .NET projects | **11,372 passed / 0 failed** |
| Frontend (`@spaarke/ui-components`, AccessGrantModal) | **26 passed** — required `npm install --legacy-peer-deps` first; `node_modules` is absent in a fresh worktree |
| Publish size | **43.69 MB** compressed incl. PDBs — **unchanged** (no packages added). Ceiling 60 |
| `--vulnerable --include-transitive` | clean |
| `dotnet build --warnaserror` | succeeds |
| ArchTests | 36/36 |
| §10 F.1 asymmetric registration | `services.AddScoped<SpeContainerMembershipService>()` is **unconditional** at module scope; `/revoke` maps unconditionally — no asymmetry |
| §11 justification | **No new component.** A fork was deleted in favour of an existing service; contact email read via the already-injected client rather than a new service |
| §11.5 complexity | `RevokeExternalAccessEndpoint` 325→355, `SpeContainerMembershipService` 376→440 — mostly XML doc. The endpoint got **simpler** in substance: a ~68-line Graph method was replaced by delegation. Cohesion unchanged |

## 9. Client-visible contract change

`POST /api/v1/external-access/revoke`:

- **`webRoleRemoved` REMOVED** (Power Pages relic, always `false`).
- **`speContainerOutcome` ADDED** — `NotAttempted` / `PermissionRemoved` / `NoPermissionFound` / `Failed`.
- **`speContainerMembershipRevoked` semantics CHANGED** — was effectively a constant `true` whenever a
  `containerId` was supplied; now true only when a permission was actually deleted.

Impact on shipped clients is nil: `AccessGrantModal` awaits the call and ignores the body entirely, and
its test stub used field names (`speRevoked`, `webRoleRemoved`) that never matched the DTO. Updated to the
real shape so the next reader doesn't infer the wrong contract from it.
