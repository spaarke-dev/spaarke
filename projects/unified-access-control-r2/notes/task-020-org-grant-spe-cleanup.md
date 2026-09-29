# Task 020 — an organization-grant revoke cleans up every active member's SPE container permission

> **Finding**: FR-16b (spec FR-16 extension) — filed by task 017 §6, scoped in by owner decision 2026-08-24
> **Files**: `Api/ExternalAccess/RevokeExternalAccessEndpoint.cs`,
> `Api/ExternalAccess/Dtos/RevokeAccessResponse.cs`,
> `tests/integration/auth/UnifiedAccessControl/SpeRevokeMatcherTests.cs` (+14)

---

## 1. The escalation triggers, answered first

The POML arms two. **Neither fires**, and both were checked against live Dataverse rather than reasoned about.

**Trigger 1 — "is membership derivable from `sprk_contactorganization` alone?"**

| Check | Result |
|---|---|
| Junction empty while org grants exist? | **No.** 2 active membership rows across 2 organizations; active org grants exist for `Morrison Foerster LLP`, which has an active member |
| Membership also implied by `contact.parentcustomerid`? | **No.** `parentcustomerid` is the OOB customer lookup (→ `account`/`contact`); firms are the custom `sprk_organization` table. `ExternalDataService` reads `_parentcustomerid_value` for **display only**, never for access. `GrantAccessRequest`/`InviteExternalUserRequest` both document "a `sprk_organization` id (NOT the OOB `account`)" |

**Trigger 2 — "can an organization have more members than one revoke can process (>200)?"**
The largest organization in the environment has **1** active member. The 200 bound shipped anyway as a guard rail, not as a live limit — see §4.

## 2. Schema, live-verified (not re-derived, and not trusted)

The constraint requires re-verification even though the POML records the answer, because three Phase 0 tasks (070, 016, 017) turned on a stale column name and *a wrong column in a revocation query reads as "nothing to revoke" — silently*.

Dataverse MCP `describe('tables/sprk_contactorganization')`, 2026-08-26:

| Element | Verified value |
|---|---|
| Collection | `sprk_contactorganizations` |
| Contact lookup | `sprk_contact` → `contact` ⇒ `_sprk_contact_value` |
| Organization lookup | `sprk_organization` → `sprk_organization` ⇒ `_sprk_organization_value` |
| State | `statecode` — Active (0) / Inactive (1) |
| Also present | `sprk_enddate` (DATE ONLY), `sprk_isprimary`, `sprk_role` |

This **confirms** the assumption standing as a caveat comment in
`ExternalParticipationService.QueryActiveOrgIdsAsync`. Deleting that caveat is a one-line follow-up left
undone — see §8.

## 3. What changed

An org revoke passes `ContactId = Guid.Empty` (task 073 #7): no single grantee → no email → nothing to
match, so `RemoveSpeContainerPermissionAsync` returned `NotAttempted`. Honest, but it meant the Dataverse
sweep revoked the grant for every member while every member's container ACL stayed exactly where it was.

Three changes:

1. **Dispatch on the ROW, not the request.** The derived `ExternalGrantKey` is hoisted out of Step 1's
   `try` so the SPE step can see whether the grant names an organization. Keying the SPE cleanup off
   anything other than what the Dataverse sweep acted on would let the two halves of one revoke disagree
   about who was revoked — finding A-11's shape, one layer down.
2. **`ExternalOrganizationMembership`** — organization → active member contacts, mirroring
   `QueryActiveOrgIdsAsync`'s query shape inverted, on the `DataverseWebApiClient` seam.
3. **Per-member sweep + member-granular reporting** via `SpeOrgMemberCleanupSummary`.

### Why the outcome enum did NOT gain a value

Step 4 left the choice open. The existing four states already carry the verdict, and adding a fifth
(`PartiallyRemoved`) would have created a state whose *name* sounds acceptable — the precise failure this
project exists to remove. Instead the counts carry the arithmetic and the existing enum carries the
verdict, mapped by a total function:

```
MembersEnumerated is null  → Failed             (we never established who the members are)
Failed > 0                 → Failed             (some members retain access)
PermissionsRemoved > 0     → PermissionRemoved
otherwise                  → NoPermissionFound  (enumerated; nobody held one — the broker-only norm)
```

`SpeContainerMembershipRevoked` stays `outcome == PermissionRemoved`, so "some members retain access" is
**not reportable as success** under either field.

**`NotAttempted` is now unreachable for an org revoke that supplied a `ContainerId`** — deliberately. Had
the zero-member case kept reporting `NotAttempted`, a test asserting it could not tell "fixed" from "not
fixed". Zero members reports `NoPermissionFound`: the list was established, and nobody held anything.

### `MembersEnumerated` is nullable, and that is load-bearing

`(0,0,0,0)` cannot distinguish "the organization has no members" from "we could not ask". `null` says the
second. It is pinned by a serialization test: a `DefaultIgnoreCondition = WhenWritingNull` added to the
app's HTTP JSON options would **omit** the field, and a JS client's `=== null` check would silently stop
detecting the case. The BFF configures no `ConfigureHttpJsonOptions`, so `JsonSerializerDefaults.Web` is
what actually runs and the explicit `null` survives.

## 4. The bound, and why it is not decoration

`DataverseWebApiClient.QueryAsync` reads **one page and discards `@odata.nextLink`**. An unbounded
membership query on a large organization therefore returns a truncated list that looks exactly like a
complete one. The reader asks for `MaxMembersPerSweep + 1` (201) so *"there are too many"* becomes
detectable rather than silent, checks the bound on **raw rows before `Distinct()`** (so duplicate junction
rows cannot collapse an over-bound org back under the limit), and on overflow removes **nothing** and
reports `Failed` with `MembersEnumerated: null`. A partial sweep would produce counts that read like a
complete answer.

## 5. `sprk_enddate` — the read-side asymmetry, recorded not fixed

The junction carries `sprk_enddate`, and `QueryActiveOrgIdsAsync` **ignores it**, keying on `statecode`
alone. So a membership that has ended by date but was never deactivated **still confers inherited access
on the read side today**.

The revoke sweeps by `statecode` alone too, deliberately. Over-including on a revoke removes more access
(fail-closed); under-including does not. A revoke that skipped date-ended-but-active memberships would
leave a live inheritance standing.

> **Finding for task 043 (FR-24/FR-25 org expansion)**: decide whether `sprk_enddate` should bound
> inherited access on the READ path. If it should, `ExternalParticipationService.QueryActiveOrgIdsAsync`
> needs the predicate and this revoke path should follow. Changing who has access on the read side was out
> of scope here — it is a behaviour change affecting live users, not a revocation fix.

## 6. Tests — 14 added (17 → 31), and a hole in my own double

At `tests/integration/auth/UnifiedAccessControl/SpeRevokeMatcherTests.cs` (ADR-038 KEEP path). The
existing 17 are untouched except `Revoke_OfAnOrganizationGrant_ReportsNotAttemptedRatherThanSuccess`,
which pinned the gap and is **flipped in place** to
`Revoke_OfAnOrganizationGrant_NoLongerReportsNotAttempted`.

### ⚠️ The double had a collision hole — the 4th instance of this class in this project

`EmailFor` originally derived the address as `$"member-{memberId.ToString()[..8]}@…"`. The three member
GUIDs **share their first eight characters**, so all three "distinct" members had the *same* email. The
all-members-removed test passed on one address matched three times, and per-member routing could not be
expressed at all. Only the test that must fail **exactly one** member surfaced it.

Fixed by stating the identities in a map rather than deriving them. *A double that derives its identities
can collide them; one that states them cannot.*

### The doubles THROW rather than defaulting permissive

Moq's loose default for `Task<List<T>>` is an **empty list** — which would have let the junction query
silently "succeed" with zero members in every test that never modelled it. So:

- `DataverseFor` (the per-contact double) **throws** if the junction is queried at all — the contact path
  must never touch that table.
- The org double **throws** on: a wrong entity set, a `$filter` missing the organization id or
  `statecode eq 0`, a `$select` missing `_sprk_contact_value`, a **null `$top`**, an unmodelled contact
  lookup, or an unmodelled container.
- The grant-sweep setup **interprets** the production filter rather than returning a fixed set — without
  which the task-010 isolation assertion would be vacuous.

### Perturbation results — each guard perturbed individually

| # | Perturbation | Tests failed |
|---|---|---|
| P1 | Skip enumeration (restore `NotAttempted`) | **9** |
| P2 | Ignore per-member failures in the aggregate | **3** |
| P3 | Report unknown membership as genuinely-absent | **1** |
| P4 | Match members on GUID instead of email (A-13's shape) | **3** |
| P5 | Drop the `$top` bound (unbounded page read) | **6** |
| P6 | Drop the over-bound detection (silent truncation) | **1** |
| P7 | Abort the loop on the first per-member failure | **2** |
| P8 | Sweep INACTIVE memberships too (drop `statecode`) | **6** |
| P9 | Stale junction column name | **6** |

None fails zero. P3 and P6 fail exactly one each — by design: they are single-purpose guards, and each has
exactly one test whose job is to hold it.

## 7. Gates

| Gate | Result |
|---|---|
| `dotnet build` (solution) | **green**, 0 errors (5 pre-existing `CA2024` warnings in `Spe.Integration.Tests`, untouched) |
| `dotnet build --warnaserror` (BFF) | **succeeds**, 0 warnings |
| `Sprk.Bff.Api.Tests` | **11,168 passed / 0 failed** (`SpeRevokeMatcherTests` 31/31) |
| Other 9 .NET test projects | all green — Core 45, RecordSyncJob 12, Scheduling 46, ControlPlane 1555, BFF Integration 103, SPE Integration 372, LoadTests 5, NightlyTests 1 |
| `Spaarke.ArchTests` | 102 passed / **6 failed — PRE-EXISTING**, verified identical on the stashed clean baseline (FR-27 ×2, ADR-010 concrete-services, ServiceBusClient guard, FR-F1/FR-F2 credential census). None mention any type this task added |
| Publish size | **43.78 MB** compressed incl. PDBs (42.88 excl.). Baseline 43.69 (task 017) → **+0.09 MB**; ceiling 60 |
| `--vulnerable --include-transitive` | clean — no packages added |
| §10 Placement Justification | see §9 |
| §11 justification | see §9 |
| §11.5 complexity | endpoint 206 → 370 **code** lines (+138 comment). See §8 — a real decomposition finding |

## 8. Known limitations and follow-ups

### ⚠️ What the tests CANNOT falsify

Everything stops at the `SpeContainerMembershipService` seam, so nothing here says anything about real
Graph behaviour. Three ways the org cleanup can report the healthy-looking `NoPermissionFound` for a member
who **still has access**:

1. **Paging (owned by task 024).** `RevokeMembershipAsync` reads the container's permissions with a single
   `GetAsync` and does not follow `@odata.nextLink`. On a multi-page container a member whose entry sits
   beyond page 1 is reported absent. This is the same false-assurance class as `container_not_cleared`'s
   M1/M2. This task **inherits** it and cannot detect it from the callee's result; fixing it here would
   mean forking the matcher, which is exactly what task 017 deleted.
2. **Eventual consistency.** SPE permission lists are eventually consistent; a removal reported as
   succeeded may remain observable for a window.
3. **Address mismatch.** The match is case-insensitive but not alias/proxy-address aware. A member invited
   under a different address than `contact.emailaddress1` reports `NoPermissionFound`.

### N+1 on the Graph side (for task 024)

`RevokeMembershipAsync` re-reads the container's **entire** permission list on every call, so an N-member
sweep performs N full list reads. Negligible at today's ~1 member/org, real at 200. **This pairs naturally
with task 024's paging work**: one *paged* read + local email match + delete-by-permission-id would fix the
N+1 **and** the paging false assurance in a single change. `RemoveAllExternalMembersAsync` is not reusable
here — it removes *every* external member, not just this organization's.

### Per-member cache invalidation — considered and deliberately NOT done

Now that the member list is enumerated, invalidating each member's participation cache became *possible*.
Rejected: the member list only exists when a `ContainerId` was supplied, so invalidation would fire on some
org revokes and not others. An inconsistent invalidation contract is worse than the uniform, documented
60 s TTL. If it is wanted, it belongs where the member list is derived unconditionally — task 043.

### Decomposition finding (§11.5) — `ExternalOrganizationMembership` should be hoisted

The reader is a Dataverse membership reader with its own reason to change; it does not belong in an
endpoint file. It sits there **only** because this task's wave-scoped modify-set was three files. It is
namespace-independent and hoisting it to
`Infrastructure/ExternalAccess/ExternalOrganizationMembership.cs` is a pure file move with zero call-site
churn. **Task 043 should hoist rather than duplicate it.**

### One-line follow-up not taken

`ExternalParticipationService.QueryActiveOrgIdsAsync` still carries the now-resolved caveat
*"confirm against the created junction schema"* (§2 verified it). `ExternalParticipationService.cs` was
outside this task's modify-set, so the deletion is left to whoever next touches that file.

## 9. Justifications

**§10 Placement Justification.** The change lives in the BFF because it *is* the BFF's existing
`/api/v1/external-access/revoke` handler completing work it already starts. No new endpoint, no new DI
registration, no new package, no new background work — the `/revoke` route and
`services.AddScoped<SpeContainerMembershipService>()` are both unconditional and pre-existing, so §F.1
asymmetric registration does not apply. The alternative placement (a separate service) fails the decision
criteria: the cleanup is a step *within* one request's revocation transaction, not an independently
schedulable unit.

**§11 three questions** (for `ExternalOrganizationMembership`):

- **Existing** — `ExternalParticipationService.QueryActiveOrgIdsAsync`
  (`Infrastructure/ExternalAccess/ExternalParticipationService.cs:614`) reads the same junction in the
  inverse direction (contact → organizations).
- **Extension** — **No, not callable.** It is `private` and built on a raw `HttpClient` with its own
  app-only token flow; the revoke path holds a `DataverseWebApiClient`. Reaching it would drag the
  participation service's token flow into the revoke path. The **query shape** is mirrored, not the code.
- **Cost of doing nothing** — an organization-grant revoke deactivates the grant for every member and
  reports success while every one of those members keeps their SPE container permission, and therefore
  continued access to the project's files. Legacy and admin-created ACLs are the only population affected,
  and also the only population no other code path will ever clean up (nothing in Spaarke *adds* a
  container permission — `GrantMembershipAsync` has zero callers, and this task did not change that).

## 10. Client-visible contract change

`POST /api/v1/external-access/revoke`:

- **`speOrgMemberCleanup` ADDED** — `null` for a per-contact revoke; for an org revoke, an object with
  `membersEnumerated` (**nullable**), `permissionsRemoved`, `permissionsNotFound`, `failed`.
- **`speContainerOutcome` semantics NARROWED** — `NotAttempted` no longer means "org grant". An org revoke
  with a `ContainerId` now returns `PermissionRemoved` / `NoPermissionFound` / `Failed`.

Impact on shipped clients is nil: `AccessGrantModal` awaits the call and ignores the body. The e2e
`revocation.spec.ts` interface is additive-safe.
