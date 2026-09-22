# Task 080 — record ownership: assign to the acting user's BU default owner team

> **Date**: 2026-09-22 · **Rigor**: FULL · opus @ xhigh · **Status**: in progress
> **Owner decision (2026-09-22)**: *"the records should be owned by the acting user's BU default owner team.
> So if testuser1 is in spaarke business unit 1 team/business unit, then the record they create is assigned to
> that team (not the user)."*

---

## 1. REPRODUCE-FIRST — verbatim (criterion 1)

**All 512 `sprk_document` rows are in the ROOT business unit.** `GROUP BY owningbusinessunit` returns exactly
one row — not "most", *all*:

```
doc_count: 512
owningbusinessunit: 06fbf21c-1872-f011-b4cb-7c1e52671ad0  ("Spaarke" — ROOT)
```

Per-row shape, identical across the 10 most recent (2026-09-03 → 2026-09-18):

```
ownerid            8793f4b0-01db-f011-8406-7c1e520aa4df   (= "# mi-bff-api-dev", the BFF application user)
owningbusinessunit 06fbf21c-1872-f011-b4cb-7c1e52671ad0   (root "Spaarke")
owninguser         8793f4b0-01db-f011-8406-7c1e520aa4df
owningteam         <null on every row>
```

**Why an ordinary user cannot read them**: Test User 1 sits in BU `cb15f587…` ("Spaarke Business Unit 1"),
a **child** of root. Dataverse `Deep` depth (4) grants own BU **plus descendants**. Root is the **parent** of
that BU, not a descendant. So no depth below `Global` (8) reaches these rows — and granting Global re-opens
findings F1 and F9.

**Honesty about the method**: this is reproduced at the DATA layer (live dev queries), not by holding a
session as Test User 1 and observing a 403. The MCP connection runs with my own privileges. The ownership
placement and the depth semantics are both verified facts; the 403 itself is the documented consequence of
them rather than something I observed directly. Stated rather than glossed.

## 2. The target shape is already live on `sprk_matter` (criterion 4's precedent)

Every `sprk_matter` row is **exactly** what the owner specified:

```
ownerid            cf15f587-baa0-f111-aaac-000d3a99d1d7   (= "Spaarke Business Unit 1" DEFAULT OWNER TEAM)
owningteam         cf15f587-baa0-f111-aaac-000d3a99d1d7   (team-owned; owninguser null)
owningbusinessunit cb15f587-baa0-f111-aaac-000d3a99d1d7   ("Spaarke Business Unit 1" — a CHILD BU)
```

So the destination state is proven achievable and already persisting in this environment.

**But the code that produced it is NOT the BFF.** See §3 item 2 — the BFF's own Matter-create path assigns
`ownerid` to the **caller**, not to a team. These live matter rows therefore came from some other path (the
model-driven UI, a plugin, or seed data). **The "mirror `sprk_matter`'s code" instruction in this task's
original framing cannot be followed literally, because no such BFF code exists.** What `sprk_matter` provides
is a verified target *shape*, not a code precedent.

## 3. Inventory — every owner assignment in the BFF (criterion 2)

12 assignment sites plus 2 unowned creates. Classified by whether the owner's convention should apply.

### A. Record entities — convention APPLIES, currently wrong

| # | Site | Entity | Today | Verdict |
|---|---|---|---|---|
| 1 | `DataverseServiceClientImpl.CreateDocumentAsync:273` | `sprk_document` | **no owner at all** | ❌ the 512-row defect |
| 2 | `RecordCreationService:295` | `sprk_matter` | `ownerid` = caller systemuser | ⚠️ user, should be team |
| 3 | `RecordCreationService:376` | `sprk_project` | `ownerid` = caller systemuser | ⚠️ user, should be team |
| 4 | `OfficeService:2631` | quick-create | `ownerid` = caller systemuser | ⚠️ user, should be team |
| 5 | `OfficeService:2859` | quick-create (2nd) | `ownerid` = caller systemuser | ⚠️ user, should be team |
| 6 | `sprk_todo` create path | `sprk_todo` | no owner (root-owned, verified earlier) | ❌ |
| 7 | `EmailUploadCaptureService` | `sprk_communication` | no owner | ❌ **cross-project — Communication owns it** |

### B. Already the target shape ✅

| # | Site | Note |
|---|---|---|
| 8 | `CommunicationEnrichmentService:712` | `ownerid` = `EntityReference("team", …)` — the only in-repo example of the shape being adopted |

### C. 🔴 Per-user artifacts — convention MUST NOT be applied blindly

**Applying team-ownership to these would WIDEN access, not narrow it. At least one is a privacy regression.**

| # | Site | Why team-ownership would be wrong |
|---|---|---|
| 9 | `NotificationService:81` | A notification is addressed **to one user**. Team-owning it exposes everyone's notifications to the whole BU. |
| 10 | `NotificationActionCore:192` | Same — `ownerid` = `recipientId`. |
| 11 | `WorkspaceLayoutService:437` | A user's **personal** workspace layout. Per-user config by definition. |
| 12 | `DirectThreadAccessService:86` | 🔴 **Ownership IS the access model here.** The file's own contract: participants = `{ownerid} ∪ {POA-shared systemuser principals}`. Team-owning a **private two-party thread** would silently expose it to an entire business unit. |
| 13 | `ThreadResolver:450` | Same thread-ownership model. |
| 14 | `WorkAssignmentEndpoints:73` | `ownerid` = `request.AssignedToUserId` — **assignment semantics**, deliberately the assignee, not a defect. |
| 15 | `TaskActionCore:147` | `ownerid` = explicitly caller-supplied `input.OwnerId`. |

## 4. 🔔 ESCALATION — scope of "all record entities"

**Trigger**: this task's escalation trigger 1 fires in its *inverse* form. The trigger anticipates an unplanned
**narrowing** of access; category C is an unplanned **widening**, which is worse — narrowing causes an outage
you notice, widening causes a disclosure you don't.

The owner's instruction was *"this needs to be the same pattern for all record entities."* Categories A and B
are unambiguously record entities. **Category C is not obviously in scope**, and blanket application would be
actively harmful in at least three places, one of them a privacy regression on private threads.

**Recommendation**: apply the convention to **category A only** — `sprk_document`, `sprk_todo`, `sprk_matter`,
`sprk_project` and the quick-create paths — plus the `sprk_communication` hand-off. Leave category C unchanged
and record why, so a later sweep does not "finish the job" and widen them.

**Awaiting owner confirmation before touching anything in category C.** Work on category A proceeds meanwhile,
since it does not depend on the answer.

## 5. Status

| Criterion | Status |
|---|---|
| 1 — reproduce-first | ✅ §1 (data-layer; method limitation stated) |
| 2 — full inventory | ✅ §3 |
| 3 — document + todo owner set | ⏳ in progress |
| 4 — convention stated with consequences | ✅ §2 + owner decision |
| 5 — reversible/dry-run backfill | ⏳ |
| 6 — ordinary user can read after change | ⏳ (needs a child-BU account — see the 92.5% trap) |
| 7 — 063 Run Index + 064 To Do succeed live | ⏳ |
| 8 — `sprk_communication` coordinated or handed off | ⏳ |
| 9 — suite / ArchTests / publish / CVE | ⏳ |
| 10 — test scope | ⏳ |
