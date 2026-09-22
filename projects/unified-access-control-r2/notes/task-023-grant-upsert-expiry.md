# Task 023 — the grant upsert writes the expiry (H1)

> 2026-09-08, session 4. **One escalation is open — see §4.**

## 1. The defect, confirmed in the code

`GrantExternalAccessEndpoint.CreateGrantAsync`'s match path branched only on the access level:
identical level → logged "no-op (idempotent)" and wrote nothing; different level → wrote
`sprk_accesslevel` **only**. `ExternalGrantLifecycle.RowSelect` did not even select
`sprk_expiresdate`, so the code could not see the row's current expiry.

With task 007's read filter enforcing expiry server-side, three requests silently did nothing while
returning **200 + a record id**:

| Request | Before | Now |
|---|---|---|
| Re-grant to ADD an expiry (unbounded → bounded) | no-op; **access stays unbounded** | expiry written |
| Re-grant to EXTEND an expiry | ignored | later date written |
| Re-grant over an EXPIRED row, no new expiry | 200 + id; grantee still has nothing | **409** + id + `sdap.grant.expired_not_restored` |

The first is the worst: an operator asks to bound access, is told it worked, and the grant stays
unbounded. That is **A-5's shape resurrected on the WRITE path by the two tasks that closed it on the
read path**.

## 2. What changed

| File | Change |
|---|---|
| `ExternalGrantLifecycle.cs` | `sprk_expiresdate` added to `RowSelect` + `ExternalGrantRow.ExpiresDate` (`DateOnly?`, matching the DATE ONLY column) |
| `GrantExternalAccessEndpoint.cs` | Match path writes the expiry; level and expiry are now **independent** reasons to write. Expired-row-with-no-new-expiry returns a warning instead of a bare success. `CreateGrantAsync` returns `GrantUpsertOutcome(AccessRecordId, Warning)` rather than a bare `Guid`. `sprk_granteddate` now written DATE ONLY (was a full round-trip timestamp — the recorded LOW finding) |
| `InviteAndGrantExternalUserEndpoint.cs` | Consumes the outcome; **logs** the warning rather than failing, because the Contact was already onboarded and failing there would strand a real onboarding over a grant the operator can fix by re-granting |
| `spec.md` FR-09 | Acceptance criteria now name expiry on the upsert path |

**Why the return type changed.** ADR-003 and acceptance criterion 4 require that a grant which cannot
be applied as requested not return 200 with a record id. A bare `Guid` cannot carry that, so the one
case where the caller most needs to be told something was indistinguishable from success.

## 3. Perturbation results

| Perturbation | Tests failed |
|---|---|
| Drop the expiry write entirely | **4** |
| Drop it only on the extend path (`survivor.ExpiresDate is null` guard) | **2** — exactly the two extend-path tests |

The second is the one that matters: it proves the tests discriminate between "writes an expiry at all"
and "writes an expiry on the path that already had one", which a single happy-path test would not.
Before this task, `ExpiryDate` appeared **once** in the whole grant-lifecycle suite, as `null`.

## 4. 🔔 ESCALATION — clearing an expiry is a contract decision (OPEN)

The POML's trigger: *"If clearing an expiry (date → null) turns out to be ambiguous in the API
contract — omitted field versus explicit null — STOP and escalate; that is a contract decision, not an
implementation detail."* **It fires.** Verified, not assumed:

- `GrantAccessRequest.ExpiryDate` is `DateOnly?`. Under System.Text.Json an omitted `expiryDate` and an
  explicit `"expiryDate": null` **both** deserialise to `null`. They cannot be told apart.
- There is **no** tri-state/optional convention anywhere in `Api/` to borrow (grepped for
  `JsonElement`-wrapped optionals, `Optional<T>`, `IsSet` — none).

Both readings are defensible and both have a security consequence:

| Reading | Consequence |
|---|---|
| `null` = **clear** | Any re-grant that does not restate the date silently **unbounds** a bounded grant — e.g. a level change from a UI that does not round-trip the expiry. This is H1's own defect in the other direction. |
| `null` = **leave unchanged** (shipped) | Safe, but clearing an expiry is impossible through `/grant`; an operator who wants a grant to become permanent must revoke and re-grant. |

**Shipped: `null` = leave unchanged**, because it is the only reading that cannot silently widen
access, and it is pinned by `Upsert_WithNoExpiryInTheRequest_LeavesAnExistingExpiryIntact`. This is a
safe default, **not** an answer to the escalation.

**The decision needed**: should clearing be expressible at all, and if so how — an explicit
`ClearExpiry: bool` on the request, a dedicated route, or accept "revoke and re-grant" as the way to
make a grant permanent? Recorded in spec.md FR-09 as an open owner decision.

## 5. Verification

Targeted per the operator's verification-economy direction: grant-lifecycle **29/29** (23 existing +
6 new), plus `GrantExpiryCharacterizationTests` — **40/40** together. Build 0 errors / 0 warnings.
Both perturbations run and restored, residue-checked clean.
