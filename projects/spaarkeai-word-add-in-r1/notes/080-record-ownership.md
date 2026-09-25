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

## 4c. 🔴 MODEL 1 IS A SHARED DATAVERSE — this is tenant isolation, not org tidiness

**Owner correction 2026-09-25**: *"in model 1 each customer DOES NOT get their own dataverse
environment — that's the whole point. customers are segregated by business unit (i.e., each customer would
start at a child business unit / team below the root 'Spaarke')."*

### ⚠️ The authoritative deployment guide says the OPPOSITE

[`docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](../../../docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md)
§3.2, Model 1 composition, verbatim: *"**Dedicated per-customer**: Dataverse env, SPE container-type + root
container, Key Vault, Storage, UAMI, Entra app config."* That directly contradicts the owner's stated model,
and CLAUDE.md §17 calls that file *"the single authoritative operator guide."* **It drives provisioning**, so
the contradiction is not cosmetic — a provisioning run following the doc would stand up a dedicated
environment per customer. Needs an owner decision on which is correct, then a doc fix. Filed here rather than
silently resolved, because I do not know which side is stale.

### Verified BU structure — consistent with the owner's model

Root **Spaarke** (`06fbf21c…`, `parentbusinessunitid` null) with **five flat children**, all directly under
root: `Secure Project`, `Spaarke Business Unit 1`, `Spaarke Demo`, `Spaarke Dev 1`, `Spaarke Test 1`.
One level, exactly the shape "each customer starts at a child BU below root" implies.

### What this changes: the stakes, and the direction of the failure

Deep depth = own BU **+ descendants**, never the parent. So with every record pooled in ROOT today:

| Reader | Sees root-owned records? |
|---|---|
| Customer A user in BU-A (Deep) | ❌ **No** — root is their PARENT |
| Customer B user in BU-B (Deep) | ❌ No |
| Spaarke staff in root (Deep) | ✅ Yes — root + all descendants |

So there is **no cross-customer leak today**, but something worse for a shared-tenancy product: **every
customer's data pools in root where only Spaarke staff can see it, and no customer can see their own.**
After the fix, records land in the customer's BU, that customer's users can read them, siblings cannot, and
root staff retain the support-wide view.

**So the ownership fix is the Model 1 customer-data-visibility mechanism, not an org-structure nicety.**
Its `refuse` branch becomes isolation-critical rather than merely tidy.

### Record-first is MORE right under Model 1, not less

Spaarke staff sit in **root**. A staff member filing a document to customer A's matter would, under
creator-first, land it in ROOT — invisible to customer A. Record-first lands it in A's BU, from the matter.
The correction made on 2026-09-25 is therefore load-bearing for Model 1 specifically.

### The per-customer BFF app registration idea — assessed, and NOT configured today

Owner's hypothesis: each client may have its own BFF app registration, so its Dataverse application user
would sit in that customer's BU, making the Dataverse default land records correctly with no code.

**Verified: it is not configured that way.** All **25** application users in dev — including all three BFF
ones (`mi-bff-api-dev` `8793f4b0…`, `spaarke-bff-api-prod` `905a7d55…`, `SDAP-BFF-SPE-API` `bb5a90e5…`) —
sit in **ROOT** `06fbf21c…`. Not one is in a child BU.

Assessment if it were configured:
- ✅ Would give customer-level correctness for app-only creates with zero code.
- ❌ Gives **application-user** ownership, not **team** ownership — not what the owner asked for.
- ❌ Customer-level granularity only; no within-customer BU/department scoping.
- ❌ Breaks if one BFF app registration serves several Model 1 customers — an app user can sit in only ONE BU.
  The owner flagged this themselves (*"need to check if always does"*).
- ❌ Correct placement is an operational provisioning step, enforced by nothing in code.

**Conclusion**: worth doing as defence-in-depth for the no-target/no-user case, but it is not a substitute for
explicit assignment and does not deliver team ownership.

### New obligations this creates for 080

1. **A cross-customer negative test** — a BU-A user MUST NOT read a BU-B record. Previously absent from the
   criteria because ownership read as org structure; under Model 1 it is the isolation proof.
2. **The refuse branch is isolation-critical** — a fallback that ever resolved to ROOT would make a customer's
   record invisible to them. Already fail-closed; now it must be tested as such.
3. **OPEN QUESTION for the owner**: in Model 1, is a customer ever more than one business unit (departments
   beneath the customer's BU)? That decides whether "the customer's BU" and "the acting user's BU" can differ,
   and what read scope Deep is meant to give.

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
