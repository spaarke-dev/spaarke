# Task 043 — org-expansion membership term + the registry filter on the systemuser path

> **Status: IN PROGRESS (2026-09-17).** FR-24 / FR-25 / FR-22, design §4.5 term 4. Deps 037 ✅, 041 ✅,
> 042 ✅. Rigor FULL. Production code builds clean (0 warnings / 0 errors); verification pending.

---

## 1. What shipped

| Change | Where |
|---|---|
| `MembershipResolveOptions.OrganizationIds` — org ids to bind into the contact-plane identity | `IMembershipResolverService.cs` |
| `ResolveByContactAsync` binds them, so registry-listed **org-typed** descriptors emit conditions | `MembershipResolverService.cs` |
| 🚨 `HashOptions` now covers `AccessConferringOnly` **and** `OrganizationIds` | `MembershipResolverService.cs` |
| `ActiveOrgMemberships` outcome + `ReadActiveOrgMembershipsAsync` — ONE junction read per composition | `AccessibleRecordSetService.cs` |
| Org-expansion term: one walk per **distinct** org baseline, Secure-suppressed | `AccessibleRecordSetService.cs` |
| `ComposeForSystemUserAsync` requests `AccessConferringOnly: true` | `AccessibleRecordSetService.cs` |
| `AccessibleRecordSetSources.OrgExpansionMembership` provenance flag | `AccessibleRecordSetService.cs` |

---

## 2. 🚨 The finding that changed the task: the cache key was narrower than the query

`HashOptions` hashed Roles, IdentityTypes, IncludeRelated, Limit, ContinuationToken — **not
`AccessConferringOnly`**. Task 041 added that flag and deliberately left it unset at every call site,
so the omission was harmless and invisible.

Task 043's step 3 sets it `true` in `ComposeForSystemUserAsync`. From that moment:

- the **authorization** caller (filtered, `true`) and
- the **scoping** caller (`Api/Membership/MembershipEndpoints`, unfiltered, `false`)

share one cache id — same user, same entity, same Limit ⇒ same
`{systemUserId}:{entityType}:{optionsHash}` — for the 5-minute TTL. Whichever call arrives first
decides what the other sees, and the dangerous order is the likely one: a scoping call landing first
hands the authorization gate the **unfiltered descriptor set**. That is register A-8's over-inclusion
— the exact defect FR-24 exists to close — reintroduced through Redis rather than through code.

**So the hash fix is a prerequisite of step 3, not an adjacent tidy-up.** `OrganizationIds` carries the
same hazard in the new option: the evaluator issues one walk per distinct baseline, so a single contact
legitimately makes several contact-plane calls differing *only* by which orgs are bound; omitting it
would serve one baseline's row set as another's.

Pinned by two tests that fail on a regression:
`ResolveAsync_AccessConferringOnly_DoesNotShareACacheEntryWithTheUnfilteredCall` (runs the calls in the
dangerous order) and `ResolveByContactAsync_DifferentOrganizationIds_DoNotShareACacheEntry`.

**General rule now stated on the method**: an option that can change the returned rows MUST appear in
the hash. On an authorization path a too-narrow cache key is not a performance detail — it is a way to
answer the wrong question quietly.

---

## 3. Design decisions (mine, not escalations)

### 3.1 The evaluator resolves the org ids; the resolver receives them

The POML's step 1 says the resolver should "populate the identity's OrganizationIds from the contact's
active junction rows". Taken literally that makes `Services/Ai/Membership` depend on
`Infrastructure/ExternalAccess` (ADR-034 M1 keeps the binding *inside* the canonical resolver, but the
junction read is not the resolver's data), and it adds a **second, separately-cached** junction read
that could disagree with the first mid-composition.

The evaluator already performs that read for the FR-23 deny veto. Hoisting it above the membership walk
costs **zero** extra reads and makes the additive term and the ethical wall provably use the same
snapshot. `<steps mode="directional">` permits the adaptation; recorded here as the deviation.

**The pre-existing `IIdentityOrganizationResolver` seam is not a substitute** — its implementation
resolves via `MembershipOptions.OrganizationLookup.UserLookupField`
(`sprk_organization` → `systemuser`, default empty), a different mechanism that returns nothing for a
contact.

### 3.2 One outcome, two opposite fail directions

One junction read feeds two consumers whose safe failure directions are **inverted**:

| Consumer | Over-inclusion is | So a failed read must |
|---|---|---|
| additive org-expansion term | an over-**grant** | contribute nothing |
| FR-23 deny-veto **subject** | a stricter wall | deny every queried candidate |

`ResolveDenyVetoAsync` has always denied everything on this fault. Hoisting the read and passing a bare
empty list would have **silently converted that fail-closed into a fail-open**, because an empty
subject-org list is indistinguishable from "belongs to no organization" — the wall would simply stop
matching. Hence `ActiveOrgMemberships(OrganizationIds, Unreadable)` rather than `IReadOnlyList<Guid>`.

### 3.3 One walk per DISTINCT baseline

The term must credit each record at **its own** organization's baseline. Two alternatives, both wrong:

- **One walk per organization** — N queries for information that varies only by baseline, and FR-25
  defines exactly three baselines, so N collapses to ≤3.
- **One walk binding every organization** — can credit only a single level, so a record reachable only
  through a View Only firm silently inherits an unrelated Full Access firm's rights. **Undetectable
  downstream**: the record ids are identical either way and only the level differs.

Rejected a third option — reporting matched-org provenance on `MembershipResponse` — because a new
response field changes the wire shape and would trip the POML's own escalation trigger 2 (M10
byte-compatibility for existing membership API consumers).

Pinned by `ComposeAsync_TwoOrgsAtDifferentBaselines_CreditsEachRecordAtItsOwnOrgsLevel`.

### 3.4 An org-only walk must narrow identity types

`ResolveByContactAsync` binds the `ContactId` **unconditionally**. A walk that bound org ids without
narrowing would return contact-derived records too — and the org term credits everything it receives at
the organization's baseline, so the contact's own assignment would inherit its firm's level. The walk
therefore passes `IdentityTypes: ["Organization"]`; `FilterDescriptors` already supports it, so no new
machinery. Pinned at the FetchXml level by
`ResolveByContactAsync_OrganizationIdentityTypeOnly_ExcludesTheAlwaysBoundContactCondition`.

### 3.5 Org expansion is a contact-plane term

Design §5 composes a systemuser as ADR-034 membership ∪ the caller's own contact grants. The org-derived
access a Type 1 user reaches through their linked contact is the org-**inherited grant**, which term 2
already suppresses on a secure record via `DirectAccessLevel` (task 037). Adding a second org path on
that plane would invent access design §5 does not grant. Recorded as two `Times.Never` assertions in
`ComposeAsync_SecureRecord_Type1SystemUserGetsNoOrgInheritedAccessAndNoOrgExpansionWalk` rather than as a
comment.

### 3.6 Standing-gated, provisionally

Register B-1 says derived access is "default-on" with Secure as the veto, but **no FR assigns a level to
a non-standing derived contribution**. The POML says to encode, not decide (escalation trigger 1), so an
organization with the flag unset, no baseline, or an unreadable row contributes nothing —
`StandingGrantState.Rights` is `None` in all three cases, which is also task 042's fail-closed value.

**Escalation trigger 1 did NOT fire**: no non-standing derived term exists today, because the
contact-plane walk only runs when `standingRights != None`.

---

## 4. 🔔 The two task-020 constraints — decided, as instructed, either way

### 4.1 `sprk_enddate` on the read path — NOT bounded, and this is now a recorded decision

The junction carries `sprk_enddate`, but `QueryActiveOrgIdsAsync` filters `statecode eq 0` alone, so **a
membership ended by date but never deactivated still confers inherited access**. Task 020 filed the
asymmetry onto this task with the explicit instruction not to inherit the revoke's shape by default,
because the revoke's fail direction is inverted.

**Decision: leave the query shape unchanged, and record why — one shape cannot be right for both
callers.** `QueryActiveOrgIdsAsync` is shared by:

- the **additive** org-grant and org-expansion terms, where over-inclusion is an over-grant, so a date
  bound is a *tightening*; and
- the **deny-veto subject**, where over-inclusion is a stricter wall, so the same date bound makes the
  ethical wall match **fewer** subjects — a fail-OPEN change to a veto.

A blanket date bound would therefore fix one caller and quietly weaken the other. Doing it properly
means a date-bounded variant for the additive callers only, which also **changes who has access today**
(narrowing live org-grant inheritance) — beyond this task's scope and requiring the owner's word.

→ **Filed as an owner item and a register entry; not fixed here.** The cheap hedge in the meantime is
that the additive term is standing-gated, and **0 organizations hold a standing grant today** (task 042
§3), so the org-expansion term's exposure to this is currently nil.

### 4.2 Per-member cache invalidation — NOT added, deliberately

Task 020 declined it because its member list existed only when a `ContainerId` was supplied, making
invalidation fire on some org revokes and not others. That objection genuinely does not apply here — this
task derives the member list unconditionally.

**Decision: still do not add it**, for a different reason. The staleness window that matters is not the
junction read (uncached, live per composition) but the **membership resolver's own 5-minute response
cache** plus the 60-second participation cache. Invalidating one junction row would leave the resolver's
per-contact entries untouched, so the observable revocation lag would be unchanged while the code gained
a second invalidation contract to keep honest. ADR-034's Phase 2 pub/sub invalidator
(`Membership:CacheInvalidator:Enabled`, default off) is the mechanism that actually closes this, and it
is the right place for it.

→ Recorded so the decision is deliberate rather than forgotten.

---

## 5. POML premise errors — #18 and #19

| # | Claim | Reality |
|---|---|---|
| 18 | `<file role="canonical-reference">ContactStandingGrantReader.cs</file>` | Task 042 **renamed** it `SubjectStandingGrantReader.cs`; the cited file does not exist |
| 19 | Step 3: wire the flag into "the **flag-off branch of 036**" | **036 is `🔲 open`** (deps 032/**034**/035/**104**, and 034 is 🟡 blocked). `ComposeForSystemUserAsync` has no flag and no branches; its single `_membership.ResolveAsync` call **is** the path. 036 will add the impersonated answer alongside it |

Also stale: all three `<dependency status="pending">` attributes (037/041/042 are ✅), and the §10
publish-baseline note still quotes the retired ~44.96 MB convention.

**Running total: 19.** The code won again.

## 5.1 Docs-vs-reality drift fixed in passing

`IMembershipResolverService.ResolveByContactAsync`'s own XML doc still described the **retired
`sprk_assigned*` naming convention plus an exclusion list** as the allowlist mechanism. Task 041 deleted
that prefix check outright — it is not layered under the registry. A reader trusting the doc would have
concluded that naming a new column `sprk_assigned*` still confers access. Corrected in place, with the
correction dated and attributed so it reads as a repair rather than as fresh prose.

**Docs-vs-reality instances: twelve.**

---

## 6. Read accounting (NFR-02)

Per contact-plane composition:

| Read | Count | Note |
|---|---|---|
| grant set | 1 | cached 60 s, unchanged |
| `sprk_contactorganization` junction | **1** | was 1 (inside the veto); now hoisted and shared |
| org standing grant | 1 per active org | one per *subject*; the reader's contract is per-subject. 0 orgs today |
| membership walk — contact standing term | 1+ pages | unchanged, gated on the contact's own grant |
| membership walk — org expansion | 1+ pages per **distinct baseline** (≤3) | new; 0 today |
| root record flags | 1 | batched over every candidate incl. org-derived ids |

Net new reads when no organization holds a standing grant: **zero** beyond one baseline read per org.

---

## 7. What offline tests cannot cover

- **The `statecode eq 0` filter** (criterion 3's "inactive junction row confers nothing") lives inside
  `QueryActiveOrgIdsAsync`, which the test double replaces. Asserting it at evaluator level would assert
  the fake. Pinned where it lives; deliberately not restated.
- **`sprk_enddate`'s type/format** on the junction is unverified — the Dataverse MCP server failed to
  connect this session (`CONNECTION_CLOSED`). Relevant only if §4.1 is ever actioned.
- **No live check of the org-typed lookup metadata** for the same reason; the registry seed's org columns
  come from task 041's live pass (2026-09-04).
