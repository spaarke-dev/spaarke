# Task 010 — idempotent `/grant`, sweeping `/revoke`: the logical key and the races

> **Date**: 2026-08-22 · **Spec**: FR-09 · **Finding**: A-11 (High, ranked **#1 of 13**)
> "Silent privilege retention after revoke."

---

## 1. What was wrong

Two halves of one defect:

| Endpoint | Behaviour | Evidence |
|---|---|---|
| `/grant` | CREATEd unconditionally — no pre-existence check anywhere on the path | `GrantExternalAccessEndpoint.CreateGrantAsync:130-147` |
| `/revoke` | Deactivated exactly ONE row, by `AccessRecordId` — root- and grantee-agnostic, never queried siblings | `RevokeExternalAccessEndpoint.cs:96-97` |

So granting twice produced two active rows, and revoking once left one standing. **Access survived
revocation.**

And it was invisible. `ExternalParticipationService.QueryGrantSetAsync:469-472` collapses duplicates with
`GroupBy(ProjectId).Max(level)`; the matter/work-assignment sets are `HashSet`s (`:439`, `:443`); and
access-record ids are never surfaced. No effective-access view in the product could show that N active
rows backed one logical grant — so the operator who clicked "revoke" saw success, and the participation
list looked correct, while the grantee kept access.

## 2. The logical grant key

Everything in this task follows from one definition:

```
key = (root record) × (exactly one grantee: Contact XOR Organization)
```

| Grant kind | Identity | OData filter |
|---|---|---|
| Person | the Contact | `{rootCol} eq {rootId} and _sprk_contact_value eq {contactId} and statecode eq 0` |
| Organization | the Organization, **and the absence of a Contact** | `{rootCol} eq {rootId} and _sprk_organization_value eq {orgId} and _sprk_contact_value eq null and statecode eq 0` |

`{rootCol}` ∈ `_sprk_project_value` / `_sprk_matter_value` / `_sprk_workassignment_value`.

### Two details that are load-bearing, not cosmetic

**A row may carry BOTH a contact and an organization.** `BuildGrantPayload` binds `sprk_Organization`
whenever `OrganizationId` is supplied, including on a per-contact grant, where it records the contact's
*firm*. That is **association metadata, not grantee identity**. If the key treated it as identity, a
person grant and an org grant on the same root would collide and could revoke each other. Hence:
contact wins when present.

**`_sprk_contact_value eq null` is what makes an org grant an org grant.** Drop that clause and the org
filter also matches every person grant whose contact belongs to that organization — so revoking one
firm's grant would sweep each of its members' personal grants. This mirrors the read side term for term
(`ExternalParticipationService.cs:511`), which is the point: when the write side and the read side
disagree about what "the same grant" means, revocation stops matching what the participation surface
displays. That disagreement *is* A-11.

Pinned by `GrantKey_ForOrganization_RequiresContactToBeNull` and both isolation tests; removing the
clause fails 3 tests.

## 3. `/grant` — upsert

```
query active rows for the key
 ├── none        → CREATE (as before)
 ├── exact level → no-op, return the existing id
 └── other level → UPDATE sprk_accesslevel in place on the survivor
then: collapse any surplus rows onto the survivor
```

The pre-existence query's failure **propagates**. Falling back to a blind create would reintroduce the
duplicate this task exists to remove.

### The concurrent-grant race

Two callers granting the same key simultaneously both see zero rows and both create. After creating,
each re-queries and collapses.

The subtlety: **both racers must elect the same survivor**, or they deactivate each other's row and the
grant vanishes entirely — a worse bug than the one being fixed. The election is `OrderBy(id).First()` —
stable and clock-independent, unlike `createdon`, which can tie at Dataverse's precision. Whichever
racer observes the duplicate pair collapses it; if only one sees both rows, only it acts; if both see
both, they perform the identical deactivation, which is idempotent.

The post-create collapse is deliberately **non-fatal**: the caller's grant succeeded, and any surviving
duplicate is swept by the next grant *or revoke* on that key — both of which now operate by key.

## 4. `/revoke` — sweep by key, not by id

```
retrieve the target row  → 404 if absent
derive its logical key   → FAIL LOUDLY if underivable (see below)
query ALL active rows on that key
deactivate every one     → report DeactivatedCount
```

Three consequences worth stating:

- **An already-inactive target still sweeps live siblings.** "The row you named is already off" is not
  the same question as "does this grant still confer access?" — answering the first when the caller
  asked the second is precisely how A-11 hid.
- **Any failure returns an error.** Per the ADR-003 constraint verbatim: *"/revoke must never report
  success while any matching active row remains unqueried."* A success response with rows still active
  is the worst outcome available, because the caller stops looking.
- **`DeactivatedCount` makes the outcome explicit** rather than inferable: `0` = safe no-op, `1` =
  normal, `>1` = duplicates existed and were collapsed — the exact condition that used to leave access
  standing after a "successful" revoke.

### The underivable-key decision

A row with no root lookup (or no grantee) has no queryable siblings. The POML flags this as an
escalation — *"fail the revoke loudly vs best-effort sweep is a security judgment call"*.

**The task's own ADR-003 constraint answers it**: siblings that cannot be queried cannot be guaranteed
absent, so reporting success is forbidden. The revoke fails with a 500 naming the record, and
**deactivates nothing**. Partial revocation while reporting success is the A-11 shape; refusing is
fail-closed. Recorded here rather than escalated because the constraint is explicit, not because the
judgment was skipped.

In practice such rows should not exist: `ResolveGrantRoot` fails closed, so every row this endpoint
writes has exactly one root. The guard is for legacy or hand-created data.

## 5. Escalation triggers — all three evaluated

| Trigger | Fired? | Why |
|---|---|---|
| Pre-existing duplicates → is a data-cleanup script needed? | **No** | No migration written. Duplicates are collapsed opportunistically on the key being *touched* — at grant time and at revoke time — which is what the POML's step 2 asks for. Untouched historical duplicates are swept the first time anyone grants or revokes that grant |
| Underivable logical key | **No** — resolved by constraint | See §4. The ADR-003 constraint is explicit; fail loudly |
| Contract change breaking `AccessGrantModal` / SPA | **No** | `RevokeAccessResponse` gains `DeactivatedCount` with a default. Additive and optional; request shapes are untouched. Existing callers that ignore the field are unaffected |

## 6. Placement Justification (CLAUDE.md §10)

| Component | Placement | Why |
|---|---|---|
| `ExternalGrantKey` + `ExternalGrantLifecycle` (new) | `Infrastructure/ExternalAccess/` | Beside `ExternalGrantRoot`, the existing write-side root abstraction they extend |
| `ExternalGrantRoot.ValueColumnFor` | extends the existing type | The filter-column counterpart to the existing `BindFor` |
| Endpoint changes | in place | No new endpoints, DI registrations, packages, or background work |

**§11 three questions for the new type.** *Existing*: nothing — grant and revoke each spoke to Dataverse
directly and shared no notion of "the same grant", which is the defect itself. *Extension*:
`ExternalGrantRoot` is extended rather than duplicated; the key/query/sweep is genuinely new behaviour
needed by two callers. *Cost of doing nothing*: the upsert-match filter and the revoke-sweep filter would
be built independently, and one divergent predicate — the `_sprk_contact_value eq null` clause is the
obvious candidate — would silently sweep the wrong grantee's access. Concrete, silent, privilege-changing.

## 7. Deliberately NOT changed

- **The read-side `GroupBy`** in `ExternalParticipationService` — the POML scopes it out (Phase 1 rebuilds
  the evaluator). This task stops duplicates being *created* and stops them *surviving revocation*; it
  does not make them visible. **They remain invisible to the participation surface until Phase 1.**
- **The SPE UPN matcher** (`RemoveContactFromSpeContainerAsync`) — that is task 017, same file. The diff
  is scoped to row lifecycle.

## 8. Test coverage

`tests/integration/auth/UnifiedAccessControl/GrantLifecycleCharacterizationTests.cs` — 22 tests, the
ADR-038 §2 security-auth KEEP path. Three task-001 characterizations flipped; the rest new.

The seam is `DataverseWebApiClient` (methods `virtual` — the ADR-038 §4 module boundary). The revoke
handler was made `internal` so the **production** handler is exercised, per the `InternalsVisibleTo`
convention already used across `Sprk.Bff.Api` — no reflection (ban B8), no transport mock (ban B1).

### The fake table interprets the real filter — on purpose

`FakeGrantTable` answers `QueryAsync` by *parsing the production `$filter`* rather than returning canned
rows. A canned fake would pass even if the filter were wrong — including the org/person case, which is
the one that silently changes whose access is revoked. Interpreting the emitted filter means a wrong
predicate fails the test.

### Verified empirically, not argued

| Perturbation | Result |
|---|---|
| Drop `and _sprk_contact_value eq null` from the org filter | **3 tests fail** |
| Revoke deactivates only the named row (the old behaviour) | **2 tests fail** |
| Both restored | 22/22 |

### A real defect the full suite caught

The first full run failed one pre-existing test:
`ExternalAccessContractTests.InviteAndGrant_WhenGranting_…`, with *"Did not expect accessRecordId to be
empty"*.

Its `StubDataverseWebApiClient` answers **every** `QueryAsync` with the same canned payload, so my new
pre-existence query deserialized a contact row into an `ExternalGrantRow` with all-default fields —
`Id == Guid.Empty`. The upsert then adopted that unusable row as "the existing grant", issued an
`UpdateAsync` against `Guid.Empty`, and returned an empty id.

The stub is unrealistic, but **the defect it exposed was real and mine**: an unaddressable row must
never be adopted as an existing grant. Doing so makes the grant a silent no-op that still reports
success — the exact failure class this project exists to remove. Fixed in production
(`QueryActiveRowsAsync` discards rows with no usable id) rather than by adjusting the stub, and pinned
by `CreateGrantAsync_WhenQueryReturnsRowsWithNoUsableId_StillCreatesARealGrant`.

Worth noting how it surfaced: the TRX-capture technique recorded during task 005 (`--logger trx`, parse
`outcome="Failed"`) named the failing test immediately. Under `-v q` it would have been an anonymous
"Failed: 1" indistinguishable from the pre-existing flake.

### One honest note on the headline test

`GrantTwiceThenRevokeOnce_LeavesZeroActiveGrants` states FR-09's acceptance verbatim, but with the upsert
in place only one row ever exists — so it would also pass with a single-row revoke. It verifies the
*combination*, not the sweep. The test that isolates the sweep is
`Revoke_WithPreExistingDuplicateRows_DeactivatesEveryOne` (three seeded duplicates, all three
deactivated), and that is the one perturbation 2 fails. Both are kept: one matches the spec's wording,
the other has the teeth.

## 9. Follow-on obligations

| # | Obligation | Owner |
|---|---|---|
| 1 | **Duplicates remain invisible** to the participation surface until the `GroupBy` collapse is replaced. The evaluator must surface per-row provenance, or the next duplicate class hides the same way | **task 032** / Phase 1 |
| 2 | Task 017 edits `RevokeExternalAccessEndpoint.cs` next (SPE UPN matcher). It MUST NOT reduce the sweep back to the target row, and the SPE removal should arguably follow the same key rather than the single `request.ContactId` | **task 017** |
| 3 | Historical duplicate rows are collapsed only when someone next grants or revokes that specific key. If a proactive audit is wanted, it is a separate authorized piece of work — not done here (escalation trigger 1) | owner decision |
| 4 | `/invite-and-grant` (task 029) shares `CreateGrantAsync`, so it inherits idempotency for free — worth a smoke check when that path is next touched | future |
