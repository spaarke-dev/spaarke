# Task 080 — record ownership: assign to the acting user's BU default owner team

> **Date**: 2026-09-22 → 2026-09-30 · **Rigor**: FULL · opus @ xhigh · **Status**: ⚠️ completed with escalation (`5d870b898`) — §6
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

## 5. Status — superseded by §6.7 (kept for the trail)

The 2026-09-25 status table lived here. §6.7 is current.

---

## 6. Resumed 2026-09-30 after UAC-r2 #1029 merged — implementation

UAC-r2's #1029 merged as `2682e8225` and came into this branch as `26acc00e2`. Their task 043 is now in our base:
`MembershipResolverService` confers access through `owningteam`, so a team-owned record no longer resolves to nobody
in the membership resolver. That was the reason 080 was paused. #1011 itself is still open, but 043 routes around it.

### 6.1 What every Office writer now does

Each writer asks `IRecordOwnershipResolver` and writes `ownerid = team`. If no team comes back, the writer
**refuses** and writes nothing. None of them falls back to app-user ownership.

| Entity | Writer | Where the team comes from (record-first) | Refusal |
|---|---|---|---|
| `sprk_document` | `POST /api/office/save` (`OfficeService.SaveAsync`) | `TargetEntity`, else the caller's `oid` | **403 `OFFICE_022`**, returned **before** the email capture, the job row, the upload and the row |
| `sprk_document` | `UploadFinalizationWorker`: fallback create + email **attachment children** | the team the save resolved, carried on `UploadFinalizationPayload.OwningTeamId`, so children always match their parent. A pre-080 message without it resolves from `AssociationType`/`AssociationId` + `UserId` | fallback create throws (the message fails); attachment children are **not created**, with an error log, because attachment failures never fail the job |
| `sprk_document` | `POST /api/v1/documents` (`DataverseDocumentsEndpoints`) | the caller (the body names no record) | 403 `record_owner_unresolved` |
| `sprk_todo` | `POST /api/office/todo` (`CreateTodoAsync`) | regarding record, then document carrier, then communication carrier, then the caller. Only the first named target is passed | **403 `OFFICE_022`** via `SdapProblemException`, with a new endpoint catch that keeps it distinct from `OFFICE_010` |
| `sprk_invoice` | `POST /api/office/quickcreate/invoice` | the caller (a new invoice is filed against nothing) | **403 `OFFICE_022`**. It used to be best-effort and left the invoice app-owned in root |
| `sprk_matter`, `sprk_project` | `RecordCreationService` (quick-create) | the caller's `systemuserid`. The field-mapping **source** is an input, not a parent | `owner_team_unresolved` (403) |

A **version save** asks nothing: it creates no row, and the row keeps its owner. A test pins this by making every
answer a refusal and showing the version save still succeeds.

### 6.2 🔒 A security defect the inventory found, and its fix

**`POST /api/v1/documents` let the CLIENT choose the owning team and the primary key.** The endpoint binds the request
body straight into `Spaarke.Dataverse.CreateDocumentRequest`. Earlier 080 work added `OwningTeamId` and task 014
added `Id` as server-side inputs, but neither was excluded from binding. Any authenticated caller could therefore
create a document owned by any team, and so in any business unit, including a secure one. They could also choose
its primary key.

- **Fix:** `[JsonIgnore]` on both properties, and the endpoint now resolves the owner itself.
- **Pinned by:** `DocumentCreateOwnershipContractTests`. With `[JsonIgnore]` removed from `Id`, the test goes red.
- **Honest scope:** the endpoint's only client (`SummarizeFilesWizard`) sends `{Name, ContainerId}`. No evidence of
  abuse exists. ⚠️ **CORRECTED 2026-09-30 — this line originally said the properties "have not shipped to
  master". Wrong:** both reached master with **#960** (2026-09-29; `OwningTeamId` in `db046e534`, `Id` in task
  014), and the hole is live on master and on `spaarke-bff-dev` (`2682e8225`). Filed as **#1043**; this branch is
  the fix. The UAC-r2 session caught the wrong claim; the commit message of `5d870b898` repeats it and cannot be
  amended (pushed).

### 6.3 Resolver changes

- **TOP 2 with a refusal on ambiguity**, for both the user lookup and the team lookup. `RecordContainerResolver`
  already keeps this contract for the same fact, so one save can no longer get two answers about its business unit.
  (UAC-r2 finding (c).)
- **Target aliases** go through `DocumentAssociationMap.ToLogicalName`, which is now that class's single alias table.
  `TryApply` switches on logical names only. A queued `AssociationType` of `"matter"` therefore reads `sprk_matter`
  instead of refusing a legitimate save.
- **Reads through `IGenericEntityService`** (the narrowest seam), not all of `IDataverseService`.
- **Fixed the stale comment** saying an unreadable target "falls back to the acting user". It refuses.

### 6.4 Deviations from the POML / handoff, and why

| Instruction | What was done | Why |
|---|---|---|
| "use `IDataverseUserClient` to read the acting user's BU as the user" (POML UPDATE) | Kept the app-only read | Background writers (the worker, inbound mail) have no user token, and one fact should have one reader. Business unit and default team are ownership metadata, not user content. |
| "append the resolver ctor param AFTER `IDataverseUserClient userClient`" | Added as a **required** parameter after `RecordCreationService` | `userClient` did not survive the merge: our `OfficeService.cs` won. Required rather than optional, because the resolver is registered unconditionally and a missing one must fail at startup, not quietly re-open app ownership (ADR-032). |
| "wire the remaining create paths (worker ×2, matter, project, workassignment, analysis)" | Worker ×2, matter and project done; Invoice added. **Work assignment and analysis NOT done** | See §6.6. |

### 6.5 Full inventory (criterion 2) — 78 create sites + 4 upserts

Swept 2026-09-30 across `Sprk.Bff.Api`, `Spaarke.Dataverse` and `Spaarke.Core`. It supersedes the 12-site §3 table,
which missed Invoice quick-create, TaskActionCore's `sprk_event`, and every Communication/AI/Finance path.

- **Only two creates run as the user** (`EmailDraftToolHandler`, `DataverseCreateRecordHandler`, both through
  `DataverseUserClient`). **Every other create is app-only.**
- **No create anywhere impersonates the caller.** So a create with no `ownerid` is owned by the application user, in
  root.

| Disposition | Entities / sites |
|---|---|
| **Changed in 080** | the §6.1 rows |
| **Per-user by design — must NOT be team-owned** (§3 category C) | `appnotification` (×2), `sprk_notificationoutbox`, `sprk_workspacelayout`, `sprk_communicationthread` direct + record threads (owner = caller) |
| **Record entity, app-only, NO owner — hand off to the owning project** | `sprk_document`: Communication archive/attachments (`CommunicationService` ×3, `IncomingCommunicationProcessor` ×2), external portal (`ExternalDataService`), Compose upsert (`ComposeCreateOnSavePromoter` → `UpsertRequest`). `sprk_communication` (×5 app-only paths incl. **Office email-save capture**). `sprk_event` (`TaskActionCore` when no owner is passed, `/api/v1/events`, external portal). `sprk_todo` (`TodoGenerationService` daily job, external portal). `sprk_invoice` (`InvoiceReviewService` PATCH upsert). `sprk_analysis` (×6, AI). `sprk_spendsignal` / `sprk_spendsnapshot` (Finance job) |
| **Not a user-facing record entity** (system/audit/config; ownership has no reader impact or is org-level) | `sprk_processingjob`, `sprk_emailartifact`, `sprk_attachmentartifact`, `sprk_emailreviewlog` ×12, `sprk_communicationattachment`, `-participant`, `-channelref`, `sprk_affinity`, `sprk_eventlog`, `sprk_aichatsummary`/`-message`, `sprk_userentityassociation`, `sprk_speauditlog`, `sprk_specontainertypeconfig`, `sprk_speenvironment`, `sprk_registrationrequest`, `systemuser`, `sprk_fileversion`, `sprk_playbooknode`, `sprk_precedent`, `sprk_externalrecordaccess` (UAC-r2 #1010 conforms separately), `contact` |
| **Dead code** | `EmailAttachmentProcessor.ProcessAttachmentsAsync`, `MessageAttachmentMaterializer`, `AnalysisAction/Skill/Knowledge/ToolService` creates |

**Filed:** the hand-off row → **ISS-007 [#1034](https://github.com/spaarke-dev/spaarke/issues/1034)**; work assignment →
**ISS-008 [#1035](https://github.com/spaarke-dev/spaarke/issues/1035)**; playbooks → **ISS-009
[#1036](https://github.com/spaarke-dev/spaarke/issues/1036)**; the picker's Read-vs-AppendTo gap (UAC-r2 finding e) →
**ISS-010 [#1037](https://github.com/spaarke-dev/spaarke/issues/1037)**.

Three findings from the sweep belong to other owners (items 2 and 3 are folded into ISS-007):

1. **`PlaybookService.CreatePlaybookAsync` / `ClonePlaybookAsync` set no owner**, even though they are passed
   `userId`, while the ownership checks filter on `_ownerid_value eq userId`. A "private" clone is owned by the app
   user.
2. **`ThreadResolver` per-user MASTER thread** (`:600`) is keyed on a user but has no owner.
3. **`TodoGenerationService.CreateTodoAsync`** has an owner parameter that no caller passes.

### 6.6 Why work assignment and analysis are NOT in this change

- **`sprk_workassignment`: NOT changed. Escalated.**
  - `WorkAssignmentEndpoints` sets `ownerid = AssignedToUserId`, and that is the **only** record of the assignee. The
    entity's `sprk_assignedto*` lookups point at **contact**, not `systemuser`, so team ownership would lose who the
    work is assigned to.
  - Separately, a live schema check shows the endpoint writes **two columns that do not exist**: `sprk_matterid`
    (real: `sprk_regardingmatter`) and `sprk_duedate` (real: `sprk_responseduedate`). Any create that supplies a
    matter or a due date faults.
  - Owner decision needed: where does the assignee live once the record is team-owned?
- **`sprk_analysis`: handed off, with the recipe.**
  - Six call sites sit in the AI zone (`AnalysisEndpoints` ×3, `AnalysisResultPersistence`, `AppOnlyAnalysisService`
    ×2), plus `DataverseObservationMirror`. `AppOnlyAnalysisService` already has 14 constructor parameters.
  - **Recipe:** add an optional `Guid? owningTeamId` to `IAnalysisDataverseService.CreateAnalysisAsync` (before `ct`;
    every caller passes `ct:` by name, so only the 13 Moq setups in `AnalysisFork`/`PromoteEndpointContractTests`
    change). Each caller resolves record-first from `documentId`, else the `regarding` target.
  - `AppOnlyAnalysisService` treats analysis creation as best-effort, so a refusal must stay a skipped row there,
    never a failed profile.
- **`sprk_communication` (066): handed off to the Communication project**, per this task's constraint *"do not reach
  into their create path"*.
  - The team is **already resolved** in `OfficeService.SaveAsync` before `EmailUploadCaptureService.CaptureAsync`
    runs, so their change is one parameter: accept `Guid? owningTeamId` and set it in `BuildCommunicationEntity`.
  - Their inbound and send paths need the same resolver call, record-first on the association.

### 6.7 Status against the acceptance criteria

| # | Criterion | Status |
|---|---|---|
| 1 | reproduce-first | ✅ §1 (data layer; method limitation stated) |
| 2 | full inventory | ✅ §6.5 — 78 + 4, by disposition |
| 3 | `sprk_document` + `sprk_todo` → BU default Owner team, record-first, both refusals | ✅ §6.1; tests §6.8 |
| 4 | convention stated with consequences; `sprk_matter` not cited as code precedent | ✅ §2, §4d |
| 5 | backfill: dry-runnable, reversible, sample-verified before wholesale, row counts | ⚠️ **script + live dry run done** (§6.9). The **sample `-Apply` is the owner's**: *"if we need to back fill something manually, let me know and i'll do it"* |
| 6 | ordinary child-BU user reads a document they saved — **live** | ⏳ **needs a deploy.** The owner deferred redeploys, and the shared `spaarke-bff-dev` must be deployed from master. Verify as **Test User 1** (the only account in a child BU); a root account cannot tell a working fix from a no-op |
| 7 | 063 Run Index + 064 document-source To Do succeed live | ⏳ same deploy |
| 8 | `sprk_communication` coordinated or handed off with a named owner | ✅ handed off, §6.6 → Communication project (`email-communication-intelligence-r2` successor) |
| 9 | full suite / ArchTests / publish / CVE | see §6.10 |
| 10 | cross-BU negative (secure BU) + both refuse branches + backfill dry-run | ✅ `RecordOwnershipResolverTests` (secure target → secure team, never the caller's; named-unreadable → refuse without consulting the caller; nothing-to-resolve → refuse) + the dry run. That a sibling BU **cannot read** the row is Dataverse depth semantics — a live check, with 6/7 |

### 6.8 Tests (scope per criterion 10)

| File | Pins |
|---|---|
| `tests/unit/domain/Dataverse/RecordOwnershipResolverTests.cs` (12) | record-first order; secure target → secure team; alias → logical entity; **both refuse branches** (missing / no-BU target never consults the caller; nothing to resolve); a **faulting** target read propagates (never refuses, never falls back); oid path; ambiguous user; no default team; **both team predicates** (decoys first) |
| `tests/unit/domain/Office/UploadFinalizationOwnerTeamTests.cs` (3) | worker: the carried team wins without asking; a legacy payload resolves from the association + user; nothing → create nothing |
| `tests/integration/regression/Issue1038_OfficeSaveSecureRecordContainerTests.cs` (1) | F1 / #1038: a save filed to a secure project by its friendly name lands in the project's OWN container |
| `tests/integration/data-mutation/RecordOwnership/OfficeRecordOwnershipTests.cs` (9) | the save (filed / unfiled / refused-writes-nothing / version save never asks); team handed to the worker; To Do record-first (record over document, document alone, refused); Invoice (team, refused) |
| `tests/integration/contract/Api/Documents/DocumentCreateOwnershipContractTests.cs` (2) | body cannot set owner or id; refused |
| quick-create contract tests (Matter, Project) | owner assertions moved from the caller to the caller's team; new team-refusal test |

**Seeded negative controls. Each went red and was then restored:**

1. Dropped `isdefault` → the both-predicates test failed.
2. Let an unreadable target fall through to the caller → 2 refuse tests failed.
3. Stopped the save passing the team → 2 save tests failed.
4. Made `Id` bindable again → the contract test failed.

### 6.9 Backfill — `scripts/Backfill-RecordOwnership.ps1`

The script is dry-run by default. Before each write it records the row's previous owner in a **write-ahead reversal
manifest**; `-RevertManifest <csv> -Apply` undoes a run. Every assignment is **read back**. `-MaxWritesPerRun` caps
the **sample** run. Candidates are rows owned by an **application user** only.

**Live dry run, dev, 2026-09-30 (zero writes; 159 application users):**

| Entity | App-owned | Would re-own | Unfiled (not written) | Unresolvable |
|---|---|---|---|---|
| `sprk_document` | 407 | **31** → `Spaarke Business Unit 1` team (all via `sprk_matter`) | **376** | 0 |
| `sprk_todo` | 10 | **10** → 3 BU1 team (matter), 7 root team (invoice — invoices are root-owned) | 0 | 0 |

**The 376 unfiled documents are deliberately not written.** They were created app-only, so the data does not record
who saved them, and the owner's 065 rule is *"we can't 'guess'"*. They stay in root.

**Owner's commands:**

- **Sample:** `.\scripts\Backfill-RecordOwnership.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply -MaxWritesPerRun 5`.
  Then open one of the re-owned documents as Test User 1.
- **Undo:** `-RevertManifest <path printed> -Apply`.
- **Order:** run `sprk_todo` again after `sprk_document`, so To Dos follow their documents.

**✅ APPLIED 2026-10-02** (owner: *"for 080 backfill - you are able to run this"*), after the deploy of master
`5e39f2bea` and the #1081 root-team role:

| Run | Entity | Written | Failed | Reversal manifest (copied to `notes/080-backfill-2026-10-02/`) |
|---|---|---|---|---|
| Sample (`-MaxWritesPerRun 5`) | `sprk_document` | 5 | 0 | `record-ownership-manifest-20261002-170848.csv` |
| Full | `sprk_document` | 26 | 0 | `record-ownership-manifest-20261002-170933.csv` |
| Full | `sprk_todo` | 10 (3 → BU1 team, 7 → root team) | 0 | `record-ownership-manifest-20261002-170949.csv` |
| Dry run after | both | **0 left to write** | — | 379 unfiled documents untouched by design |

**Verified as a user, not just by owner field**: `RetrievePrincipalAccess` for **Test User 1** (Spaarke Business Unit 1)
on the 5 sample documents → Read, Write, Append, AppendTo, Create, Delete, Assign; on a control document still
app-owned in root and filed to a matter → **None**. The 7 root-team To Dos were writable only because of #1081's role.
Unfiled documents grew 376 → 379 since 09-30: three new app-owned creates by writers outside the resolver (ISS-007).
**Undo**: `.\scripts\Backfill-RecordOwnership.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
-RevertManifest <manifest> -Apply` (restores only rows still owned by the team it set).

### 6.10 Gates (commit `5d870b898`)

| Gate | Result |
|---|---|
| Full BFF suite (single process) | **13,047 pass / 1 fail / 56 skip**. The one failure is `SseStreamingIntegrationTests.Cancellation_NoLingeringBackgroundTask_AfterClientAbort`: pre-existing, unrelated, uses a wall-clock `Task.Delay(50)`, passes 3/3 alone, and passed on the previous full run (13,042 / 0 / 56). Not in `tests/.reliability-registry.json` |
| ArchTests | 337 / 337 |
| Publish size (§10) | master `2682e8225` **45.48 MB** vs branch `5d870b898` **45.49 MB** → **+0.01 MB**. Fresh worktrees at short paths, `Compress-Archive` Optimal, PDBs included, **212 files each** |
| CVE | no vulnerable packages; no package changes |
| office-addins | tsc 68 (baseline, no new errors); `errorMessages` jest 14/14; eslint clean |
| Seeded negative controls | 5, all went red then restored: dropped `isdefault`; unreadable target falls through to the caller; save stops passing the team; `Id` bindable again; the #1038 friendly name passed through |

### 6.11 Step 9.5 review — triage (code-review + adr-check, 22 findings)

| # | Finding | Disposition |
|---|---|---|
| **F1** | 🔴 **Pre-existing, outside the diff, security**: the Office save handed the FRIENDLY type (`"project"`) to `RecordContainerResolver`, whose securable registry is keyed on LOGICAL names, so a save filed to a **secure** project skipped the secure branch and landed in the shared default container. SPE permissions are additive-only, so that cannot be retracted. **Every** filed save also missed its record's business-unit container (task 076/085 intent). | ✅ **FIXED** in `OfficeService.ResolveContainerAsync` (our code), through the same `DocumentAssociationMap.ToLogicalName` table. **GitHub [#1038](https://github.com/spaarke-dev/spaarke/issues/1038)**; regression test `Issue1038_OfficeSaveSecureRecordContainerTests`, which goes red with the pre-fix code (lands in `b!test-office-save-drive`). ⚠️ Office content saved to a secure record since task 085 shipped may already sit in the shared container. **UAC-r2 checked dev: 0 documents are linked to any secure record, so nothing leaked there.** Check production-shaped environments. ⚠️ `spaarke-bff-dev` runs master `2682e8225`, which **still has the bug** until this branch merges. The other five container-resolver callers are safe or fail closed (`uac-r2-findings` §7). |
| F2 | Every resolver exception became "no team", so a throttle or timeout surfaced as a permanent 403 `OFFICE_022` telling the user to check the record | ✅ fixed: "no row / no BU / ambiguous" is an answer (refuse); a Dataverse **fault propagates** as the caller's 5xx. Tests split: missing target → refuse; faulting target → throws; neither case consults the caller |
| F3 | One condition had three codes (`OFFICE_022`, `owner_team_unresolved`, `record_owner_unresolved`), none known to the add-in (DEFAULT_ERROR: recoverable) | ✅ unified on **`OFFICE_022`** everywhere, and added to `errorMessages.ts` as `recoverable: false` |
| F4 | The persistence layer still created an ownerless row whenever it got no team; the invariant rested on eight writers remembering | ✅ `OfficeDocumentPersistence` now **throws** before its first write when no team is passed. The shared-library comment is corrected (other writers still pass null — #1034) |
| F5 | Record-first copies root placement from root-owned parents (invoices, communications, work assignments), so those children see no improvement | 📝 By design; improves as the parents' writers adopt I-6 (#1034). Live check for 063/064: use a **matter**-filed document as Test User 1 |
| F6 | Backfill omitted `sprk_relatedevent` / `-todo` / `-contact`, which the save writes | ✅ added. Dry run re-run: counts unchanged, so no dev app-owned doc uses them |
| F7 | The membership event still says `ownerid` = the caller, for a now team-owned row | ✅ comments corrected (`OfficeService`, `OfficeEndpoints`, `DataverseDocumentsEndpoints`). **Event semantics are ADR-034's**: flagged to UAC-r2 (the publisher is off by default) |
| F8 | ADR-038: a new test outside the KEEP paths | ✅ Path C: `RecordOwnershipResolverTests` moved to `tests/unit/domain/Dataverse/` |
| F9 | Gaps: worker, Project team-refusal, documents refusal body | ✅ `UploadFinalizationOwnerTeamTests` (domain: carried team wins; legacy payload resolves; null → create nothing), Project refusal test, `OFFICE_022` body assertion. Not added: a full attachment-pipeline test (no harness exists; the carried-team handoff is pinned from the save side) and the email-capture ordering (claim (a) was confirmed by reading) |
| F10 | §10 gates | see §6.10 / commit |
| F11, F18 | Stale text: "Invoice best-effort", "null → app-owned", "ownerid = caller", a Swagger description, "the save sends logical names", "contact/sprk_todo unmapped" | ✅ swept |
| F12 | "children can never land in different business units" is false for pre-080 in-flight messages | ✅ doc softened (a deploy-window-only exception) |
| F13 | The body-`OwningTeamId` half of the contract test is vacuous (the endpoint overwrites it) | ✅ renamed; the note in the test explains that `Id` is the half that pins `[JsonIgnore]` |
| F14 | The predicate test catches a dropped predicate only through TOP 2 | ✅ decoys inserted before the default team |
| F15 | Duplicate business-unit reads (container + ownership) per save | ⏭️ **not changed**: cost is 2–3 small reads, and merging needs a shared business-unit reader across a UAC-r2 component. Noted for the write-path consolidation |
| F16 | `OfficeService` constructor 21, worker 11 (ADR-010 threshold) | ⏭️ noted, not in scope. Moving the Invoice leg into `RecordCreationService` is the natural next cut (task 059 extracts `OfficeService` clusters) |
| F17 | New early return skipped disposing attachment streams | ✅ fixed (the two older early returns have the same leak; left as they were) |
| F19 | Backfill revert could clobber a later reassignment; the token was fetched once | ✅ revert restores only rows still owned by the team it set; token refreshes after 45 minutes |
| F20 | `Trait("status","repaired")` on new files | ✅ `new` |
| F21 | Team ownership makes an unfiled personal quick-save visible to the whole business unit at Local depth | 🔔 Owner-decided convention. Confirm that the roles' `sprk_document` Read depth is intended |
| F22 | The worker trusts `payload.OwningTeamId` | 📝 same Service Bus trust boundary as `DocumentId` / `ContainerId` |

**ADR check.**

- ADR-038: resolved by the move (F8).
- **§6.5 Path A for the acting-user fallback (G5):** now recorded in `spec.md` ADR Tensions. The resolver had declared it; the spec had not.
- **ADR-002 WP-3/WP-5:** the I-6 registry row now states the interim for client- and form-created rows. They are user-owned, in the right business unit.
- ADR-019: resolved (F3).
- ADR-034 (F7): UAC-r2's call.
- ADR-010: `IRecordOwnershipResolver` remains the justified test seam (`ADR010_DITests` ceiling entry).

### 6.12 After the PR was prepared: three findings from UAC-r2 (2026-09-30)

1. 🔴 **`POST /api/v1/documents` IS live on master.** §6.2 wrongly said it had never shipped.
   - `OwningTeamId` (`db046e534`) and `Id` (task 014) both reached master in **#960**, with no `[JsonIgnore]`.
   - On master, and on `spaarke-bff-dev` (`2682e8225`), any caller can create a document owned by any team,
     including Secure Record, with a GUID they choose.
   - Filed as **[#1043](https://github.com/spaarke-dev/spaarke/issues/1043)**. **This branch is the fix, so merging
     it is a SECURITY merge** (#1038 and #1043).
2. 🔴 **Regression introduced by 080: team-owned To Dos vanish from the Daily Briefing.**
   - `DailyBriefingCollector`'s to-do channels filter `owninguser = caller` (`:1027`, and `ScopeToOwner` at
     `:430-432`). A team-owned row has no `owninguser`.
   - `sprk_todo` has no other user-typed "for whom" column, so no data-only guard exists.
   - Filed as **[#1044](https://github.com/spaarke-dev/spaarke/issues/1044)** with three options: To Dos stay
     caller-owned / add a "for" user column / accept the regression until UAC-r2's "who is notified" design lands.
   - **Owner decision before merge.** Also noted: default-team ownership widens membership-driven briefing items
     (matters, projects, events) to the whole business unit. UAC-r2 is taking that design.
3. ⚠️ **Saves filed to a SECURE project will likely FAIL until UAC-r2's C10 lands.**
   - The "Secure Record Owner" role has Read only on `sprk_project`, `sprk_matter` and `sprk_workassignment`, per
     `SECURE-PROJECT-ENVIRONMENT-SETUP.md:161-168`. Dataverse refuses to assign a row to a team whose role lacks
     Read on that table.
   - So a document or To Do that record-first assigns to the Secure team will be refused by Dataverse.
   - This is inferred from the guide and **untested live**. It **fails closed**: an error, and the bytes stay in the
     secure project's own container. Before this branch, such a save went to the SHARED container (#1038).
   - UAC-r2's C10 adds the child-table privileges.

   > 🔴 **CORRECTED 2026-09-30, after this item was written.** Two claims above were wrong.
   > 1. **The owner is not UAC-r2.** C10 is the Secure team's identity (a named, non-default team) plus re-owning
   >    documents at provisioning. It is not these privileges, and UAC-r2's own note said the reverse ("fails
   >    closed until UAC C10 lands"), so nobody owned it. **This project owns it: task 082, ISS-013,
   >    [#1046](https://github.com/spaarke-dev/spaarke/issues/1046)**, by owner instruction.
   > 2. **The guide is not the live role.** A read-only query on dev returned 36 privileges.
   >    - `sprk_document` has all 8, so **secure-target document saves work in dev**.
   >    - `sprk_todo` has **none**, so a **To Do** filed against a secure record is refused.
   >    - Communication, event and memo have none either.
   >
   >    Environments built from the guide have no child privileges, so documents fail there too. The guide's §5.4
   >    strip script would remove dev's document privilege if re-run. Detail: the 082 POML.
