# Task 063 — internal system-user share endpoints (FR-29 server half)

> **Status**: implementation committed `d47b586eb` (2026-09-15, session 13). Step 9.5 and the final
> verification numbers are recorded in §7 below.
> **Consumed by**: task 065 (the Manage Access "+ User" picker). §5 is the contract it codes against.
> **Sibling**: task 064 owns the FR-30 read/provenance surface. This task deliberately builds no part of it.

---

## 1. What shipped

Three routes on the existing `/api/v1/external-access` management group:

| Route | Body / query | Answer |
|---|---|---|
| `POST /share-user` | `{recordType, recordId, systemUserId, accessLevel}` | `{systemUserId, accessLevel, accessRightsMask, outcome}` |
| `POST /unshare-user` | `{recordType, recordId, systemUserId}` | `{systemUserId, removed}` |
| `GET /user-shares` | `?recordType=&recordId=` | `{shares: [{systemUserId, fullName, accessRightsMask, accessLevel, modifiedOn}]}` |

`recordType` is `project` | `matter` | `workassignment`. A share is a Dataverse
`principalobjectaccess` (POA) row written app-only through the one share seam
(`IDataverseRecordShareService`, task 060). New code: `Api/ExternalAccess/InternalShareEndpoints.cs`,
`Api/ExternalAccess/Dtos/InternalUserShareDtos.cs`, `Services/Access/RecordShareLevels.cs`, and two
primitives on the shared `DataverseWebApiService` (§3).

## 2. The route-placement decision (the POML asked for it explicitly)

**On the existing `/api/v1/external-access` group, not a sibling group.** That group already carries
`AddDelegationRuleFilter()` — Write on the record, evaluated as the CALLER over OBO (owner decision
B-14, task 008) — and the filter denies any request whose record it cannot identify, so these routes
were gated from their first request. A sibling group would need its own copy of that wiring, and a copy
is a second place for the gate to be forgotten. design.md §6 names the gate the blocking prerequisite
for exactly this surface: without it the "+ User" button is a one-click path from read-only to Full
Access on a confidential matter.

Two consequences worth stating:

- **"External" in the path names the surface (Manage Access), not the principal.** Nothing in this task
  reads or writes `sprk_externalrecordaccess`. A future rename of the group would be a separate,
  contract-breaking change for every client.
- **The group is no longer mutation-only.** `GET /user-shares` discloses who can reach a record, which
  is why it is gated the same way rather than placed on a read surface. `DelegationRuleFilter`'s remarks
  were corrected accordingly. The filter resolves the GET's target from its `[AsParameters]`-bound query
  object, pinned by a test — if the filter ever stopped seeing that object, the route would deny
  everyone rather than admit them.

**Placement justification (root CLAUDE.md §10 / `.claude/constraints/bff-extensions.md`)**: the work
belongs in the BFF because it is an authorization-bearing write that must be evaluated as the caller
(OBO) and executed app-only — the split the BFF exists to hold. No new package, no new background work,
no new DI registration (the share seam was already registered unconditionally by task 060/043; §F.1's
asymmetric-registration rule therefore does not fire). Publish-size delta in §7.

**Component justification (§11)**: `RecordShareLevels` is new because no level→rights table existed
(premise error 17 below); `InternalShareEndpoints` is new because the existing grant/revoke endpoints
write a different store (`sprk_externalrecordaccess` rows, with expiry and reminders) for a different
principal kind (Contacts), and overloading them would fork their contract. Both reuse the existing
group, filter, seam and root-resolution helper rather than adding parallel ones.

## 3. Why the write path looks the way it does

Microsoft Learn documents **`ModifyAccess` as REPLACING** a principal's access mask. It does **not**
document three things this surface depends on, and each undocumented case is handled on the unsafe
assumption rather than the convenient one:

| Undocumented case | Assumption taken | Consequence in the code |
|---|---|---|
| `GrantAccess` on a principal who already holds a share | may be ADDITIVE (the reported behaviour) | Grant is used ONLY when there is no direct share; an existing share is changed with `ModifyAccess`. A downgrade through Grant could otherwise keep Write and Delete while the UI showed View Only. |
| `RevokeAccess` for a principal holding no share | may NOT be a no-op | Unshare reads first and answers `removed = false` without calling Revoke. Idempotency is decided here, not assumed of Dataverse. |
| Sharing to a disabled / application user | may succeed | Refused before the write, with a distinct reason code per cause. |

Two new primitives on `DataverseWebApiService` (the canonical POA client — the ArchTest guard was
extended so a second client cannot be born through `ModifyAccess` either):

- **`ModifyAccessAsync`** — same key and payload shape as `GrantAccessAsync`; both now build that body
  through one shared helper, so the two actions cannot address a record or a principal differently.
- **`GetPrincipalAccessOrThrowAsync`** — the complete list of a record's shares, or an exception.
  The existing `GetPrincipalAccessAsync` answers an **empty list** when the read fails, which is safe
  for a caller that only displays shares and unsafe for one that decides a write: "no share" would pick
  Grant for a user who already holds Full Access, and would report "nothing to remove" for a share that
  exists. Incomplete counts as failed — an unreadable table code, an unreadable row, or a response that
  continues on another page (`@odata.nextLink`), whose unread rows could include the very principal
  being changed.

**Every write is confirmed.** After Grant/Modify/Revoke the stored mask is read back and must equal the
level's mask exactly (or be gone, for an unshare). Anything else answers `500` with
`sdap.access.user_share.write_not_confirmed` and a message that claims no outcome it cannot prove. A
write that threw — or was cancelled — is treated as "may have applied": the cache is cleared either way.

**A zero mask is not a share.** A POA row whose `accessrightsmask` is 0 carries no direct rights
(Dataverse keeps such a row for access inherited from a related record). It reads as "no share", so a
new share there is a Grant, the list excludes it, and an unshare reports `removed = false` — this
surface neither created nor can remove inherited access.

## 4. The levels (owner decision 2026-09-15, "No re-share at any level")

| Level | Value | Rights sent | Stored mask |
|---|---|---|---|
| View Only | 100000000 | `ReadAccess` | 1 |
| Collaborate | 100000001 | `ReadAccess,WriteAccess,AppendAccess,AppendToAccess` | 23 |
| Full Access | 100000002 | Collaborate + `DeleteAccess` | 65559 |

`Share` and `Assign` are at **no** level: a person given access through the picker cannot pass it on or
take the record over. `RecordShareLevels` is the single table, and
`ProvisionProjectEndpoint.CollaboratorAccessRights` / `CreatorAccessRights` now derive from it (the
creator keeps its own `ShareAccess`, unchanged from task 061).

⚠️ **The masks are Dataverse's own `AccessRights` values** (Read 1, Write 2, Append 4, AppendTo 16,
Create 32, Delete 65536, Share 262144, Assign 524288), read by reflection from
`Microsoft.Crm.Sdk.Proxy` 1.2.26 — **not** Spaarke's `AccessRights` enum, whose Delete is 4 and whose
Append is 16. Mixing the two vocabularies would make every read-back confirmation fail, or — worse —
pass one for the wrong rights. This is the third place in this project where two same-named rights
vocabularies had to be kept apart, and it is why `RecordShareLevels` holds the numbers and the tests
assert them as literals rather than deriving them from the table under test.

## 5. The contract task 065 codes against

- **Authorization**: all three routes require **Write on the record**, evaluated as the caller. A caller
  without it gets `403` + `sdap.access.deny.delegation_write_required`. No credential at all is `401`.
  A request that names no resolvable record is `403` + `…delegation_target_unresolved` (not 400 — the
  filter refuses to disclose that the record does not exist).
- **`accessLevel` is the same number a contact grant carries** (100000000 / 1 / 2), so one picker can
  speak to both surfaces.
- **`outcome`** is `created`, `updated` or `unchanged`. `unchanged` means the user already held exactly
  that level and **nothing was written** — safe to call repeatedly.
- **`removed = false`** means the user held no share; nothing was written.
- **`accessLevel` in the list is `null`** when a share's rights match no level — for example the
  provisioning creator's Collaborate + Share. Setting a level on such a share REPLACES those rights, so a
  picker that offers a level change on a `null`-level row should say so.
- **`fullName` may be `null`** (the name read failed); the share is still listed. Never treat a missing
  name as a missing share.
- **Refusals** are ProblemDetails with `reasonCode` + `traceId`. Stable codes:
  `sdap.access.user_share.record_unresolved` (400), `.user_required` (400), `.level_invalid` (400),
  `.user_not_found` (404), `.user_disabled` (422), `.user_not_a_person` (422), `.user_not_internal` (422),
  `.read_failed` (500 — nothing was written), `.write_not_confirmed` (500 — a write was sent and its
  result could not be confirmed; reload before retrying).
- **Who can receive a share**: an existing, enabled person (access mode Read-Write, Administrative or
  Read; no application id) whose `sprk_isexternal` flag confirms them internal. **Unsharing checks only
  that the user exists** — a share must stay removable after its holder is disabled or reclassified.

## 6. Cache invalidation

After every write attempt the affected user's `ImpersonatedRootSetSource` entry for that record type is
removed (resource `impersonated-root-set`, id `{systemUserId:D}:{logicalName}`). That source answers
"which records can this user read?" with an impersonated query, which sees POA shares natively, so an
answer cached before the change is stale. The key shape now lives in one place
(`CacheId` + `CacheTenantFor`), and a test populates the entry through `GetAsync` and then invalidates
it — so the removal is proven to hit the entry the read path wrote, rather than succeeding silently
against a key nobody uses. Task 036 turns that source on in the evaluator; until then the invalidation
is forward-looking and harmless.

## 7. Verification

_Recorded when the runs completed — see the session record in `current-task.md` for the same figures._

- Build: BFF `0 warnings`, `0 errors`. No vulnerable packages (`dotnet list package --vulnerable
  --include-transitive`).
- New and affected suites: **152 / 152** (handler, wire, delegation, cache, provisioning, contract).
- ArchTests: **15 / 15** on the two guards this task moves — route census `118 → 119` and the POA-client
  guard extended to `ModifyAccess` (with its own negative control, P10 below).
- **Perturbations: 10 / 10 caught.** Each broke one property and was restored from `HEAD`:
  P1 always Grant (never Modify) · P2 decide the write from the soft read · P3 trust the write instead
  of confirming the stored mask · P4 drop the `/share-user` case from the delegation filter ·
  P5 skip the cache invalidation · P6 unshare always calls Revoke · P7 Full Access also carries
  `ShareAccess` · P8 share with a disabled user · P9 copy the share eligibility onto unshare ·
  P10 a second file builds a `ModifyAccess` payload.
- Conflict check (root CLAUDE.md §10 hot-path rule, re-run at close): master is **0 commits ahead** of
  this branch, and no file is changed on both sides. The only open PR touching `Spaarke.Dataverse` is
  **#875** (dependabot: Azure.Core / Azure.Identity), which edits the `.csproj` and none of this task's
  files.
- Full suite, publish size, and Step 9.5 (code-review + adr-check): §7.1 below.

### 7.1 Final numbers

_(filled in at task close)_

### 7.2 Step 9.5 — adr-check

**0 violations, 8 warnings**, across 15 ADRs and constraint sections. It independently confirmed the two
claims most worth confirming: every service these handlers inject is registered **unconditionally** — so
`bff-extensions.md` §F.1's asymmetric-registration rule does not fire and no Null-Object peer is needed —
and premise error 17, that reusing `ExternalAccessLevels.ToAccessRights` would have granted the WRONG
rights, because its bit layout is Spaarke's and a POA row stores Dataverse's.

No code change was required to clear compliance. Dispositions:

| # | Warning | Disposition |
|---|---|---|
| W1 | ADR-019 names the extension key `errorCode`; this surface emits `reasonCode` | **Keep `reasonCode`.** Every route on `Api/ExternalAccess/**` uses it, and so does the delegation gate that answers BEFORE these handlers — one surface, one key. The honest fix is a path-B amendment to ADR-019's wording; not taken here. |
| W2 | No rate-limiting policy on the routes | **Decision recorded, not changed.** No route on this admin surface carries one, and adding a policy at the group would change 9 existing routes in a task that tests none of them. The shape is worth naming: `GET /user-shares` is an enumeration surface, and share/unshare drive repeated `systemusers` reads plus POA writes. A group-level policy is the right place if the owner wants it. |
| W3 | Handler tests call `internal` members (ADR-038 ban B8) | **Accept.** B8 is unenforced in ADR-038's own annex (blocked on a production refactor). The public HTTP surface is covered separately at the contract KEEP path plus the delegation gate; these tests add depth over the undocumented-Dataverse matrix rather than substituting for it. |
| W4 | `Mock<DataverseWebApiClient>` in the systemuser double | **Accept.** It asserts `$filter` / `$select` semantics and rejects unknown columns or clause shapes; the real wire shape is pinned separately by a hand-written `HttpMessageHandler` (ban B1's intent). Same pattern as the pre-existing `DelegationRuleTestFixture`. |
| W5 | A Write-holder can confer Delete, including on themselves | **Open owner decision** — §9 item 1. It must appear in the PR description, not only in these notes. |
| W6 | Publish-size delta not yet recorded | §7.1. |
| W7 | `InternalShareEndpoints.cs` is 616 lines; §11.5 asks that the complexity evaluation be STATED | **Cohesion holds, and here is the statement:** one reason to change (the FR-29 share surface), three handlers over one refusal shape, one strict-read helper, one eligibility predicate — and roughly half the file is the documentation of the undocumented-Dataverse decisions. No LOC gate exists (the ratchet was retired 2026-08-20). If task 064 grows this file, the natural seam is extracting `SystemUserEligibility` plus the systemuser reads. |
| W8 | The handler tests do not mirror the source path | **Accept.** `tests/integration/auth/UnifiedAccessControl/` is this project's existing auth cluster (six sibling files); the contract test mirrors the source path. |

## 8. Premises the task file got wrong (16 and 17 for this project, found at Step 0)

16. It located the POA seam at `Services/Communication/Access/`. Task 060 moved it to
    `Services/Access/IDataverseRecordShareService.cs`.
17. It said to "reuse the 060 seam's single mapping" from level to `AccessMask`. **No such mapping
    exists.** What existed: literal rights strings in `ProvisionProjectEndpoint` and
    `PlaybookSharingService`, and the evaluator's `ExternalAccessLevels.ToAccessRights`, whose `Create`
    is meaningless on a share of an existing record. Obeying the POML literally would have meant either
    inventing a table and calling it reuse, or reusing the evaluator's table and granting the wrong
    rights. The mapping was created in one place instead, and the provisioning constants point at it.

The POML's escalation trigger — "if the delegation filter cannot express Write on an arbitrary entity
type" — did **not** fire: `DelegationTarget(EntitySet, RecordId)` is generic, and the three root types
resolve through the same helper `/set-record-share-expiry` uses.

## 9. Open items for the owner

1. **A caller with Write can confer Delete — including on themselves.** B-14 makes Write the delegation
   right, and Full Access includes Delete, so a user who can write a record can grant Full Access to a
   colleague, or to themselves, and thereby gain a right they did not have. Dataverse's own sharing model
   would not allow that (a sharer can only pass on rights they hold), but the write here is app-only, so
   Dataverse does not enforce it. This is a direct consequence of two decisions already made, so it is
   surfaced rather than changed unilaterally. Closing it would mean either intersecting the requested
   level with the caller's own rights, or refusing self-shares (the narrow half). **Not implemented;
   awaiting a decision.**
2. **Sharing with an external licensed system user is refused** (`user_not_internal`). An external person
   is expected to arrive through a contact grant, which carries an expiry and reminders. If the product
   intends to allow a licensed external system user to be shared with directly, this rule needs to change.
3. **Before merge: confirm the stored masks against real Dataverse.** Every read-back comparison assumes
   Dataverse stores exactly the mask that was sent (1 / 23 / 65559). No live test can run in CI (owner
   directive), and the offline tests pin our end of the wire only. If Dataverse normalises a mask, every
   share would answer "not confirmed" — loud, not silent, but 100% broken. One create + read against a
   real environment settles it.

## 10. Registered findings

- **ISS-017 / `PlaybookSharingService.MapFromDataverseAccessRights` reads bit 524288 as Share.** That bit
  is **Assign**; Share is 262144 (SDK values, verified by reflection). Effect: a playbook shared with
  Assign is displayed as re-shareable, and a genuine Share right is not displayed at all. AI-owned code,
  not on this project's path → handed off. Registered in `notes/defer-issues.md`.
- **ISS-018 / `UnsecureProjectEndpoint.RevokeAllSharesAsync` cannot see a failed share read.** It calls
  the soft read inside a `try/catch`, but that read answers an EMPTY LIST on an HTTP failure rather than
  throwing — so a failed read reports "0 shares revoked" as success and the `catch`'s warning never fires.
  The one-line fix is the strict read this task added; it is not applied here because it would change
  task 061's endpoint behaviour on the >5000-row and malformed-row paths, which deserves its own review.
  Registered in `notes/defer-issues.md`.
