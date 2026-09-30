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

## 4b. 🔔 ESCALATION 2 — the BFF creates only 6 of the owner's 19 entities

**Owner's list (2026-09-22), 19 entities**: `sprk_agreement`, `sprk_analysis`, `sprk_billingevent`,
`sprk_budget`, `sprk_budgetbucket`, `sprk_document`, `sprk_event`, `sprk_invoice`, `sprk_invoicelineitem`,
`sprk_kpiassessment`, `sprk_matter`, `sprk_memo`, `sprk_project`, `sprk_reportcard`, `sprk_servicerequest`,
`sprk_spendsignal`, `sprk_timekeeper`, `sprk_todo`, `sprk_workassignment`.

The owner also corrected the category-C recommendation: **`sprk_workassignment` IS a core record** and must be
team-owned. Verified live — its rows are user-owned in root (`ownerid` = `owninguser` = `1d02f31c…`,
`owningbusinessunit` = `06fbf21c…`), the same defect shape. Category C now keeps only the genuinely per-user
artifacts: notifications, workspace layouts, and the two thread-ownership sites.

**Swept for which of the 19 the BFF actually creates:**

| Created by the BFF (6) | Site |
|---|---|
| `sprk_document` | `DataverseServiceClientImpl:276` |
| `sprk_analysis` | `DataverseServiceClientImpl:422` |
| `sprk_workassignment` | `WorkAssignmentEndpoints:71` |
| `sprk_matter` | `RecordCreationService:249` (via `MatterEntity` const) |
| `sprk_project` | `RecordCreationService:355` (via `ProjectEntity` const) |
| `sprk_todo` | `OfficeService:2765` |

**NOT created by the BFF (13)**: `agreement`, `billingevent`, `budget`, `budgetbucket`, `event`, `invoice`,
`invoicelineitem`, `kpiassessment`, `memo`, `reportcard`, `servicerequest`, `spendsignal`, `timekeeper`.
These are created client-side (`Xrm.WebApi` wizards/PCF) or in the model-driven UI.

### 🔴 Therefore the BFF-side approach CANNOT satisfy the requirement

Patching create paths in the BFF fixes **6 of 19 entities, and only for rows the BFF creates**. The *same*
entity created from an MDA form, a wizard, or an import stays user-owned. Task 080 as originally scoped
("set the owner at create time in the BFF") is structurally incapable of delivering "all record entities are
owned by the acting user's BU default owner team."

### The options

| | Mechanism | Coverage | Cost |
|---|---|---|---|
| **A** | BFF create paths only (080 as scoped) | 6/19 entities, BFF-created rows only | Smallest; leaves the requirement unmet |
| **B** | **Dataverse pre-operation Create plugin** on all 19 | **19/19, every creation path** — BFF, wizards, MDA, imports, future code | New plugin assembly + registration + deploy |
| **C** | B, plus keep the BFF's explicit assignment where it already sets an owner | 19/19 with defence in depth | B + small |

**Recommendation: B (or C).** A plugin is the only layer that sees every create regardless of client. Two
supporting facts:
- **No ownership plugin exists today** — the only plugin assembly is `Spaarke.CustomApiProxy` (3 files); the
  `ownerid` hits under `src/dataverse/solutions/` are entity/form XML, not code. So this is net-new either way.
- **It fits ADR-002's thin-plugin envelope.** `IPluginExecutionContext` exposes **`BusinessUnitId`** (the
  initiating user's BU) directly, so the plugin needs **one** cacheable query — BU → default owner team
  (`isdefault = true AND teamtype = 0`) — and one attribute set. No HTTP, no external calls, far below 50 ms.

**This is a scope expansion beyond task 080's boundaries (CLAUDE.md §6) and an architectural addition, so it
is the owner's call, not a task-local one.** Awaiting the decision.

**Path-independent work that proceeds regardless**: the **backfill** fixes EXISTING rows whatever created
them, so it is needed under every option and is not blocked by this decision.

## 4c. TENANCY MODEL — REVISED 2026-09-28. Every customer gets a DEDICATED Dataverse.

> ⚠️ **This section was rewritten twice. Read only the current definition below.** The 2026-09-25 version of
> §4c asserted that Model 1 was a *shared* Dataverse with customers segregated by business unit. That is
> **superseded** — do not act on it if you find it quoted in an older commit, in `current-task.md` history, or
> in commit message `e454af78a`.

### The current definitions (owner, 2026-09-28)

Revised as a result of **UAC-r2 requirements**:

| Model | Dataverse environment + Azure resources | Tenant |
|---|---|---|
| **Model 1** | **Dedicated per customer** | **Spaarke's** tenant |
| **Model 2** | **Dedicated per customer** | **the customer's own** tenant |

**There is no shared-Dataverse deployment model any more.** The distinction between the two models is now
*only which tenant hosts the stamp* — it is no longer about sharing. This maps onto the interim labels used
earlier in this project as: new **Model 1 = old 2a**, new **Model 2 = old 2b**, and the old shared Model 1 is
**retired**.

### What this changes for task 080 — three things, and they all simplify it

**1. Cross-customer isolation is the ENVIRONMENT boundary, not business-unit placement.**
Two customers can never be in one Dataverse, so no ownership bug can leak data between customers. The BU
hierarchy now only ever describes structure *inside a single customer*. The `refuse` branch is therefore
**not** cross-customer-isolation-critical — its justification reverts to the original one inherited from
UAC-r2 task 076: **secure-record isolation within the customer** (see §4d).

**2. The bug is unchanged, and its cause is now entirely within our code to fix.**
Deep depth = own BU **+ descendants**, never the parent. Inside one customer environment:

| Reader | Sees a ROOT-owned record? |
|---|---|
| Customer user in a child BU (Deep) | ❌ **No** — root is their PARENT |
| A user sitting in that environment's root (Deep) | ✅ Yes — root + all descendants |

BFF-created records land on the **app user, in ROOT**. So any customer user who sits in a child BU cannot
read records the product created for them. That is the 063/064 symptom, and it is identical under the new
model. What changes is only the blast radius: over-restriction inside one customer, never a leak across two.

**3. Goal A is now satisfied by construction — 080 is no longer gated on a provisioning decision.**
A Dataverse application user exists per environment, so a dedicated environment per customer means the app
user is *necessarily* the right customer's. The old open question — *"does every customer get its own BFF app
registration?"* — **dissolves**. But note the consequence: placing the app user correctly now fixes
**nothing** on its own, because it was never in the wrong customer; it is in the wrong **BU** (root). So the
resolver (Goal B) is no longer one half of the fix — **it is the whole fix**, and the only remaining question
is which BU *within* the customer, which is exactly what record-first answers.

### Record-first is still the right order

The argument changes but the conclusion does not. Previously: Spaarke staff sit in shared root, so
creator-first would misfile a customer's document into root. Now: the **app user** sits in the customer's own
root, so creator-first would misfile into root just the same — invisible to every child-BU user. Record-first
takes the BU from the target record, which is authored inside the customer's structure and therefore correct.
Record-first also remains the only source when there is no human at all (inbound email).

### The per-customer BFF app registration idea — now moot as a fix, still worth knowing

Measured in dev: all **25** application users — including all three BFF ones (`mi-bff-api-dev` `8793f4b0…`,
`spaarke-bff-api-prod` `905a7d55…`, `SDAP-BFF-SPE-API` `bb5a90e5…`) — sit in **ROOT** `06fbf21c…`. Not one is
in a child BU.

Under the revised model this measurement no longer indicates a *customer* mismatch (there is one customer per
environment). It indicates the remaining real defect: **the app user is in root, so its creates are
root-owned.** Moving an app user into a child BU is still not a substitute for the resolver — it would give
**application-user** ownership rather than **team** ownership, and no within-customer BU scoping.

### Obligations for 080 — one REMOVED, one restated, one still open

1. ~~**A cross-customer negative test** (BU-A user must not read a BU-B record)~~ — **WITHDRAWN 2026-09-28.**
   Two customers are never in one Dataverse, so this test would assert a property the environment boundary
   already guarantees, using a scenario that cannot occur in production. Replaced by ⬇.
2. **A cross-BU negative test *within* one customer** — a user in one child BU MUST NOT read a record owned by
   a sibling BU's team. The security-relevant instance is the **Secure Project BU** (§4d): a record assigned
   there must be invisible to ordinary users unless the UAC adds them to it explicitly. The `refuse` branch is
   tested as part of this, since a fallback that resolved to the caller's general BU would defeat it.
3. **STILL OPEN for the owner, and now MORE important**: inside a customer environment, what is the BU
   structure — is a customer ever more than one BU (departments)? Under the retired shared model this decided
   whether "the customer's BU" and "the acting user's BU" could differ. Now that within-customer structure is
   the *only* structure, it decides the resolver's whole practical behaviour and what the backfill can infer
   for the 512 root-owned documents.

### ⚠️ Consequence the owner should route to UAC-r2 / customer-provisioning

The revision makes several **shared** documents wrong — none of them this project's to edit, and
`SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` currently has unmerged edits on `work/unified-access-control-r2`, so
editing it from here would be a hot-path collision. Handed off rather than fixed:

| Surface | What is now wrong |
|---|---|
| `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §3.2/§3.3 | Model 1 is described as a shared platform — *"**Shared** across all Model 1 tenants: App Service Plan, Azure OpenAI …, Azure AI Search"*. Nothing is shared per-customer under the revision. The 3-way split by tenant (Spaarke vs customer) is absent. |
| `infrastructure/bicep/stacks/model1-shared.bicep` | The **filename** encodes the retired concept. |
| `docs/adr/ADR-052-workload-placement.md` (UAC-r2, unmerged) | Tenancy rows: *"Model 1 — a **shared multi-tenant** Function app is acceptable"*, *"Model 1: the **shared** BFF UAMI"*, *"a **shared** hub for Model 1"*. |
| Deployment guide §8 invariants **I2/I3** | `tenantId` on every AI Search query, partition key on every Cosmos read — these were the *shared-resource* mechanisms. With dedicated Azure resources they become defence-in-depth. **Recommend keeping them mandatory anyway** (the code is shared even when the resources are not, and the cost is one predicate), but the rationale should be restated so nobody later removes them as obsolete. |
| CLAUDE.md §17 | Describes the guide as *"Model 1 (shared trial/SMB) + Model 2 (dedicated stamp)"*. |
| `/provision-environment` skill intake | `tenancyModel` semantics change. |

One thing the revision **resolves**: the absence of a Dataverse row-scoping invariant (there is no I6 for
Dataverse alongside I2–I5) is no longer a gap. It was always correct — Dataverse isolation comes from the
environment boundary. My 2026-09-25 reading of that absence as evidence of a doc/owner conflict was the
weaker half of that analysis; the docs were right about Dataverse and wrong only about sharing compute.

## 4d. Dataverse does NOT assign new records to the creator's team — and the secure-record rule

**Owner question 2026-09-25**: *"how/why is dataverse setup so that on record create ownership is assigned to
the user's team and not the user?"*

**Answer: it is not, and it cannot be configured to.** On create Dataverse sets `ownerid` to the **calling
identity** (user or application user), never to that identity's team. There is no OOB setting that redirects
it. Verified three ways in live dev:

| Entity | `ownerid` | `owninguser` | `owningteam` |
|---|---|---|---|
| `sprk_workassignment` | `1d02f31c…` | **same (a user)** | null |
| `sprk_document` (app-created) | `8793f4b0…` (app user) | **same** | **null** |
| `sprk_matter` | `cf15f587…` | null | **the team** |

A record becomes team-owned only by explicit `ownerid` = team on create, an explicit Assign afterwards, or a
plugin/flow/manual action. `sprk_matter` is team-owned because something assigned it — NOT `RecordCreationService`,
which assigns the caller.

**Consequence for the owner's plan**: placing each customer's BFF app registration in that customer's BU
delivers the correct **business unit** (Goal A) but leaves `ownerid` on the **application user**, not its team.
So Goal A is provisioning and Goal B is code; they compose, and neither substitutes for the other.

| Goal | Delivered by | Gets you |
|---|---|---|
| **A** — right customer BU | **Provisioning**: per-customer app registration + app user in the customer's BU | Correct BU on every app-only create; also makes the code's fallback safe |
| **B** — owned by a team | **Code**: this resolver | `ownerid` = team. No configuration can do this. |

Also established: **wizard/PCF creates via `Xrm.WebApi` run in USER context**, so Dataverse defaults `ownerid`
to the signed-in user and the BU is already correct. No client code sets `ownerid` (verified). So the earlier
claim that the 13 non-BFF entities need this convention was overstated **for BU placement** — they were never
broken there. They need it only for team-vs-user ownership.

### 🔒 SECURE RECORDS — the fallback was unsafe, now fail-closed

Owner: secure records are created in / assigned to the **Secure Project** business unit, a sibling of the
customer BU, so they are invisible unless the user is added via UAC. Verified: `Secure Project`
(`d9ec0b6f…`) is a child of root, sibling to the customer BUs.

`RecordContainerResolver` (task 076) already encodes this for containers, and its reasoning transfers verbatim:

> *"Ownership is a property of the record, so the container follows the record."* … *"per
> `notes/secure-project-workflow-review-2026-08-24.md` §A, users sit in the Operations subtree while secure
> records are owned in `Secure Projects`, so acting-user resolution writes a secure record's content into the
> general Operations container."* … *"An unknown answer read as not-secure is the same isolation failure with
> an extra step."*

Record-first already handles a secure target correctly **when the read succeeds**, because a secure record's
own `owningbusinessunit` IS the Secure Project BU. The danger was only the **fallback**: a named target whose
BU could not be read used to fall through to the acting user, which for a secure record would assign its child
to the caller's general business unit and defeat the isolation.

**Fixed**: a named-but-unresolvable target now **refuses** instead of falling back. Mirrors 076's
indeterminate-must-refuse rule. The acting-user fallback now applies ONLY when no target was named at all.

### Boundary note

The owner observed this discussion *"may be more appropriately in UAC-r2"*, and that is right for the
**policy** layer — UAC-r2 owns ADR-034 and the access model. Task 080's scope is the **BFF create-path
implementation**. The policy questions (which BU per tenancy model, secure-record handling, depth
configuration) belong with UAC-r2 and should not be settled here.

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
