# Task 016 — close-project cascade (contact **and** organization grants)

> **Finding**: A-12 · **Spec**: FR-15 · **Register**: D-2
> **Files**: `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ProjectClosureEndpoint.cs`,
> `tests/integration/auth/UnifiedAccessControl/ProjectClosureCascadeTests.cs` (new),
> `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalAccessEndpointTests.cs`
> **Also filed**: a new constraint on task **017** (§5 below)

---

## 1. The verified column name (POML step 1, escalation-gated)

`sprk_externalrecordaccess` live metadata, read via the Dataverse MCP `describe` tool on 2026-08-23:

| Lookup attribute | Related table | OData `_value` projection |
|---|---|---|
| `sprk_contact` | `contact` | **`_sprk_contact_value`** |
| `sprk_organization` | `sprk_organization` | `_sprk_organization_value` |
| `sprk_project` | `sprk_project` | `_sprk_project_value` |
| `sprk_matter` | `sprk_matter` | `_sprk_matter_value` |
| `sprk_workassignment` | `sprk_workassignment` | `_sprk_workassignment_value` |

**There is no `sprk_contactid` attribute on this table at all.** The escalation trigger — "if the contact FK
is neither `_sprk_contact_value` nor `_sprk_contactid_value`, STOP" — did not fire.

Three sources agreed against one:

- live metadata → `_sprk_contact_value` ✅
- `ExternalParticipationService.cs:57` runtime read path → `_sprk_contact_value` ✅
- `ExternalGrantKey.ToActiveRowsFilter` (task 010) → `_sprk_contact_value` ✅
- `src/solutions/.../sprk_externalrecordaccess/views-schema.md` → `sprk_contactid` ❌ **stale, do not trust**

The solution's schema doc is the outlier and is wrong. Noted because the next person to "fix" this will
find that doc first.

---

## 2. The two defects, and why either alone is sufficient

**A-12 half 1 — the query could not run.** `$select` named `_sprk_contactid_value`. Dataverse answers a
`$select` on a nonexistent column with 400. `QueryActiveAccessRecordsAsync` caught and rethrew;
`Handle` called it with no `try`. So **every** closure 500'd having deactivated nothing — and never
reached SPE removal or cache invalidation either. Loud, but functionally inert.

Task 070 had already fixed the *sibling* project lookup in this same file
(`_sprk_projectid_value` → `_sprk_project_value`) and left the contact one on the stale `*id_value` form.
Same typo class, same file, twice.

**A-12 half 2 — organization grants were filtered out.** The projection required
`_sprk_contactid_value.HasValue`. A row with **no contact** is precisely how this schema represents an
ORGANIZATION grant — the discriminator both `ExternalGrantKey` and `ExternalParticipationService` key on
(`_sprk_contact_value eq null`). So even with the column corrected, closing a project would have left
every organization grant active, and every member of those firms with access to the closed project.

---

## 3. What changed

| Change | Why |
|---|---|
| `$select` → `sprk_externalrecordaccessid,_sprk_contact_value,_sprk_organization_value`, hoisted to `internal const ActiveGrantSelect` | Fixes half 1. `internal` so a test can regression-guard the exact names — the failure mode is silent-adjacent (a 400 reads as "closure errored", never as "your column is wrong") |
| Dropped the null-contact `.Where` | Fixes half 2. The only row now discarded is one with no usable id, and that is counted as a **failure**, not skipped |
| `ExternalAccessRecord(Guid, Guid?, Guid?)` with `IsOrganizationGrant` | Both grant kinds flow to deactivation; the org kind is nameable in logs rather than anonymous |
| `ExternalAccessRow` / `ExternalAccessRecord` `private` → `internal` | The reason A-12 survived: no test could name `QueryAsync<ExternalAccessRow>`. ADR-038 §4 seam, ban B8 satisfied via `InternalsVisibleTo`, no reflection |
| Enumeration wrapped → typed `ClosureIncomplete` 500 + `reasonCode` | ADR-003 constraint. Was an unhandled exception — technically a 500, but untyped and indistinguishable from any other crash |
| `DeactivateAccessRecordsAsync` returns `(Revoked, Failed)`; a non-zero `Failed` returns `ClosureIncomplete` | **The defect I found while fixing A-12** — see §4 |
| SPE step guarded → `container_not_cleared` | Same class; **cannot fire today** — see §5 |
| Cache invalidation moved to run unconditionally | It only ever removes access, so it is worth doing even when an earlier step failed |

Three machine-readable reason codes (ADR-003): `sdap.closure.incomplete.enumeration_failed`,
`…partial_revocation`, `…container_not_cleared`. All carry `accessRecordsRevoked` — "we revoked none"
and "we revoked eleven of twelve" call for different operator responses and a bare 500 cannot tell them apart.

---

## 4. In-scope extension: partial deactivation was reported as success

Not in A-12, found while implementing it, same function, same ADR-003 rule.

`DeactivateAccessRecordsAsync` caught per-row exceptions, logged, continued — and returned **only the
success count**. `Handle` then answered `200 OK` with `AccessRecordsRevoked: 2`. If 3 of 5 rows failed,
the operator saw a success while three participants kept access.

That is the identical false-success shape the task's ADR-003 constraint forbids for the enumeration
failure, and the codebase already has the precedent one directory over — `ExternalGrantLifecycle.DeactivateAsync`
(task 010): *"Exceptions propagate: a partial sweep must surface as a failure, never as a success with
rows still active."*

Continue-on-error is **kept** — aborting at the first failure leaves strictly more access standing. What
changed is that the failures are counted and reported. Steps 3 and 4 still run first, because both only
ever remove access. Closure is idempotent (deactivating an inactive row is a no-op, and the filter carries
`statecode eq 0`), so "retry it" is sound advice.

---

## 5. 🔔 New finding — SPE container clearing cannot fail, and reports 0 either way

`SpeContainerMembershipService.ListExternalMembersAsync` catches **both** `ServiceException` and
`Exception` and returns `[]` in each. So `RemoveAllExternalMembersAsync` answers **"0 removed"** whether
the container was genuinely empty or Graph was unreachable, and its caller cannot distinguish them.

Consequence: close-project reports `SpeContainerMembersRemoved: 0` with a **200** while every external
user may still hold file permission on the container. That is FR-15's own acceptance ("no participant
retains access post-closure") failing on the SPE half.

**Not fixed here** — the defect is in `SpeContainerMembershipService.cs`, which is **task 017's** file, and
017 already carries an ADR-003 constraint of exactly this shape ("distinguish *confirmed absent* from
*match failed*"). Filed as a `task-016` constraint on 017 naming the concrete mechanism.

Task 016 built the receiving end: the closure endpoint guards the SPE call and answers
`container_not_cleared`. That guard is **unreachable until 017 lands** and is documented as untestable-today
in the test file rather than covered by a test that would have to fake an exception the service cannot throw.

The same swallow-and-count shape also exists inside `RemoveAllExternalMembersAsync`'s per-member loop.

---

## 6. Tests — `tests/integration/auth/UnifiedAccessControl/ProjectClosureCascadeTests.cs` (20)

ADR-038 KEEP path per the task-001 own-coverage constraint. **Deviation from the POML `<outputs>`**, which
named `tests/unit/Sprk.Bff.Api.Tests/AccessControl/ProjectClosureCascadeTests.cs`: the `task-001` constraint
is explicit that "tests go at `tests/integration/auth/**`", that path is deletion-protected and the unit path
is not, and every Phase 0 task so far has landed there. Constraint over `<outputs>`.

The fake table **interprets the `$select` and rejects unknown columns the way Dataverse does**. This is the
load-bearing design choice: a fake that returned canned rows regardless of projection would have gone green
on the exact code that shipped A-12.

**Perturbation results** — the fix was broken five ways and the tests were re-run:

| Perturbation | Tests failed |
|---|---|
| Revert `$select` to `_sprk_contactid_value` (half 1) | **14 of 20** |
| Restore the null-contact exclusion (half 2) | **6 of 20** |
| Rethrow instead of the typed enumeration response | **2 of 20** |
| Ignore `failedCount` (always 200) | **2 of 20** |
| Drop the unaddressable-row guard | **1 of 20** |

### The task-001 "flip"

Task 001 could **not** pin A-12 (its own constraint says so), so there was no characterization test to flip.
The nearest thing was `CloseProject_DataverseQueryThrows_PropagatesException` in the unit endpoint tests: it
asserted `Guid.Empty == Guid.Empty` and documented in its own comments why it could not test what its name
claimed — *"ExternalAccessRow … is a private sealed class … we cannot mock QueryAsync<ExternalAccessRow>
directly."* That inaccessibility is the reason A-12 reached production.

It is replaced by a pointer block to the real coverage. Its named contract also genuinely changed
(enumeration failure is no longer an unhandled exception). Not a KEEP path, so no FR-B06 replacement
obligation — though the replacement exists anyway.

---

## 7. Gates

| Gate | Result |
|---|---|
| All seven test projects | **11,357 passed / 0 failed** |
| Publish size | **43.69 MB** compressed incl. PDBs — **unchanged** vs the task-007 baseline (no packages added). Ceiling 60 |
| `dotnet list package --vulnerable --include-transitive` | clean |
| `dotnet build --warnaserror` | succeeds |
| ArchTests (ADR NetArchTest) | 36/36 |
| §11.5 complexity | 317 → 500 lines, 9 methods, one responsibility (the closure cascade). Growth is ~2/3 XML doc explaining the two defects. Cohesion unchanged — nothing new was bolted on, the same pipeline gained failure handling. No decomposition warranted |

## 8. Client-visible contract change

`POST /api/v1/external-access/close-project` can now answer **500 + RFC 7807** with
`reasonCode ∈ { sdap.closure.incomplete.enumeration_failed, …partial_revocation, …container_not_cleared }`
where it previously answered 200 (partial revocation) or an untyped 500 (enumeration). Callers that treat
any 2xx as "closed" now correctly see the failure. Retry is safe and is the intended response.

## 9. Not done here

- **Organization → member cache expansion.** An org grant names no contact, so its members are not eagerly
  invalidated and fall back to the ADR-009 60s TTL. Bounded and self-healing — the grant row is already
  inactive, so nothing re-populates the entry. Building the expansion would add a new query surface for a
  ≤60s window on an administrative path (CLAUDE.md §11). Revisit only if the TTL is raised.
- **Broader closure semantics** (register D-2) — out of scope by design; this fixes the broken cascade only.
- **The SPE defect in §5** — task 017.
