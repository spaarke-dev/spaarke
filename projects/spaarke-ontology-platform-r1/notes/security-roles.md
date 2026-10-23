# Security roles for the five ontology tables

> **Status**: Setup instructions for the owner — 2026-10-02. Tables already created (see
> [`schema-draft.md`](schema-draft.md)); this is the privilege layer they still need.
> **Environment**: `spaarkedev1` · **6 business units** · 9 existing `Spaarke *` roles (each existing 6× — one
> record per BU, which is normal).

---

## 1. The answer to "one role or add to all of them": **separate roles. And the reason is not tidiness.**

**Dataverse privileges are additive across every role a user holds, and the effective depth is the MAXIMUM of
them.** There is no "deny" — you cannot subtract a privilege in one role that another role grants.

So **append-only on `sprk_decisionrecord` is a property of the union of all a user's roles, not of any one
role.** If *any* role a user holds grants Write, they can update Decision Records, and the guarantee is silently
gone everywhere.

That makes the choice concrete rather than stylistic:

| Approach | Consequence |
|---|---|
| Add the five tables to all 9 existing `Spaarke *` roles | The append-only guarantee becomes **unauditable**: you would have to re-verify **9 roles × 6 BUs = 54 role records** every time anyone edits any role, forever. One accidental Write on one role breaks it for every user who holds that role |
| **Three dedicated roles (recommended)** | The guarantee is **one place to check**. Users get a Console role *in addition to* their existing Spaarke role(s) — privileges union, so nothing is lost |

✅ **Good news, verified**: new custom tables default to **no privileges in existing roles**, so all five tables
are currently clean across the 9 `Spaarke *` roles. You are starting from a known-good state.

> ### ⚠️ One honest limit — Dataverse cannot make a table truly immutable with roles alone
>
> **System Administrator and System Customizer always hold every privilege**, and that cannot be removed. So
> append-only means *"no normal user and no application path can update it"* — which is the realistic bar — not
> *"no one can."* ADR-002 forbids plugins, so there is no pre-operation guard available either.
>
> If you want **detection** rather than only prevention, enable **auditing on `sprk_decisionrecord` and
> `sprk_policyversion`** (table-level, with Update + Delete tracked). That gives an admin edit a trail. Worth
> doing: these two tables are the entire defensibility story, and auditing them is cheap and narrow — unlike the
> org-wide auditing the design explicitly does **not** rely on.

---

## 2. The three roles

| Role | Who holds it | Why it is its own role |
|---|---|---|
| **`Spaarke Console User`** | Everyday users, **in addition to** `Spaarke Core User` / `Spaarke Basic User` | Resolves Work Items. Needs Write on Signals and Create on Decision Records — and nothing else |
| **`Spaarke Ontology Administrator`** | Legal-ops admin / policy author | The only role that can author Policies and Policy Versions |
| **`Spaarke Ontology Service`** | The **BFF's Dataverse Application User** (the managed identity), *not* a human | The evaluator is the only thing that **creates** Signals. Easy role to forget, and nothing works without it |

### Scope convention — use **Parent: Child Business Units** (depth "Deep")

Verified house convention, so the new tables match rather than invent. On `Spaarke Core User`, `sprk_matter`,
`sprk_communication` **and `sprk_spendsignal`** all carry `privilegedepthmask = 4` = **Parent: Child Business
Units**. `sprk_spendsignal` is `sprk_signal`'s predecessor, so that is a direct precedent for a signal table.

*(Depth values, for reference when reading the role editor: User = Basic ○, Business Unit = Local ◑,
Parent: Child Business Units = Deep ◕, Organization = Global ●.)*

---

## 3. The privilege matrix

Set these in the role editor's **Custom Entities** tab. Blank = **None**.

### `sprk_signal` — Work Items

| Privilege | Console User | Ontology Admin | Ontology Service |
|---|---|---|---|
| **Create** | 🔴 **None** | None | **Organization** |
| **Read** | **Parent: Child BU** | Parent: Child BU | **Organization** |
| **Write** | **Parent: Child BU** | Parent: Child BU | **Organization** |
| **Delete** | None | Parent: Child BU | None |
| **Append** | Parent: Child BU | Parent: Child BU | Organization |
| **Append To** | Parent: Child BU | Parent: Child BU | Organization |
| **Assign** | None | Parent: Child BU | Organization |
| **Share** | None | None | None |

> 🔴 **No Create for users, deliberately — and it enforces the architecture.** A Work Item can then exist **only
> because a rule produced it**, which is precisely the row contract's *"membership is computed by rule
> evaluation, not by a user's filter."* Users can resolve Signals; they cannot fabricate one. The privilege model
> does that enforcement for free, so keep it.
>
> Users **do** need **Write**, because resolving sets `sprk_signalstatus`, `sprk_resolutiontype`,
> `sprk_resolvedon`, `sprk_resolvedby` and `sprk_decisionrecord`.

### `sprk_decisionrecord` — append-only

| Privilege | Console User | Ontology Admin | Ontology Service |
|---|---|---|---|
| **Create** | **Parent: Child BU** | Parent: Child BU | **Organization** |
| **Read** | **Parent: Child BU** | Parent: Child BU | Organization |
| **Write** | 🔴 **None** | 🔴 **None** | 🔴 **None** |
| **Delete** | 🔴 **None** | 🔴 **None** | 🔴 **None** |
| **Append** | Parent: Child BU | Parent: Child BU | Organization |
| **Append To** | Parent: Child BU | Parent: Child BU | Organization |
| **Assign** | None | None | None |
| **Share** | None | None | None |

> **Write = None on all three roles is the append-only mechanism.** In Dataverse "Write" *is* Update; Create
> still lets the creator set every field at insert time. Note the consequence: with no Write and no Delete, rows
> also cannot be **deactivated** — which is correct for an append-only ledger, and worth knowing before someone
> reports it as a bug.

### `sprk_policyversion` — immutable after create

| Privilege | Console User | Ontology Admin | Ontology Service |
|---|---|---|---|
| **Create** | None | **Parent: Child BU** | None |
| **Read** | **Parent: Child BU** | Parent: Child BU | Organization |
| **Write** | 🔴 **None** | 🔴 **None** | 🔴 **None** |
| **Delete** | None | None *(see note)* | None |
| **Append To** | Parent: Child BU | Parent: Child BU | Organization |

> Every user needs **Read**, because a Signal cites the version that raised it and the row must stay explicable
> after the policy is retuned. **Delete = None even for the admin** is the stricter choice: deleting a version
> orphans the Signals and Decision Records that cite it. If you want a way to discard a mistyped draft before it
> fires, grant the admin Delete at Parent: Child BU and accept that it is a sharp tool.

### `sprk_policy` — authored by admins, read by everyone

| Privilege | Console User | Ontology Admin | Ontology Service |
|---|---|---|---|
| **Create** | None | **Parent: Child BU** | None |
| **Read** | **Parent: Child BU** | Parent: Child BU | **Organization** |
| **Write** | None | **Parent: Child BU** | None |
| **Delete** | None | Parent: Child BU | None |
| **Append To** | Parent: Child BU | Parent: Child BU | Organization |

### `sprk_budgetrevision`

| Privilege | Console User | Ontology Admin | Ontology Service |
|---|---|---|---|
| **Create** | None | **Parent: Child BU** | None |
| **Read** | **Parent: Child BU** | Parent: Child BU | **Organization** |
| **Write** | None | Parent: Child BU | None |
| **Delete** | None | Parent: Child BU | None |
| **Append To** | Parent: Child BU | Parent: Child BU | Organization |

> For MVP these rows are seeded by hand, so the admin role is the right home. Longer term, whoever owns budget
> changes should create them — and the Service role needs **Read** because Path B's second conjunct queries this
> table.

### The Service role also needs Read on existing tables

The evaluator cannot evaluate a predicate it cannot read. The BFF application user probably already has most of
these, but **`sprk_budgetrevision` is new**, so check: `sprk_matter` · `sprk_communication` ·
`sprk_spendsnapshot` · `sprk_budget` · **`sprk_budgetrevision`** · `sprk_triagecategory` · `sprk_event` ·
`sprk_todo` · `sprk_workassignment` (the last three for the Do-lane rules).

---

## 4. 🔴 The one thing to decide while setting this up: **who owns a Signal**

This is where the privilege model meets a question the design **deliberately deferred** —
`mvp-synopsis.md` §5 Out: *"Privilege inheritance on derived data — deferred by owner decision; separate ADR."*
With **6 business units** it is live, not academic.

**The problem.** A Signal is a **user-owned** record with its **own** record-level security. Dataverse secures it
by *the Signal's owner*, not by the matter it points at. But `sprk_signal.sprk_sentence` can contain matter
detail (*"a commitment was raised on Acme v. Northwind…"*). So if the evaluator creates every Signal owned by the
service account in one BU, then **Parent: Child BU read could expose a Signal about a matter the user cannot
open** — the matter lookup would render blank while the sentence tells them anyway.

**The cheap fix, and it is one line in the writer**: when the evaluator creates a Signal, **set its owner (or at
minimum its owning business unit) from the matter it is grouped under.** Then BU-scoped security on the Signal
lines up with BU-scoped security on the matter, and `Parent: Child BU` is *correct* rather than approximate.

Doing this at creation is free. Retrofitting it later means re-owning every existing row, and until then the
exposure is invisible — nothing errors, the sentence simply renders for someone who should not see it.

**What is still deferred** (and should be the ADR): true privilege *inheritance*, where a Signal's access is
computed from its subject rather than copied at creation. Copying at creation goes stale if the matter is
reassigned. For dev and MVP, copy-at-creation plus the sweep is sufficient; for external access or a
cross-BU customer it is not.

---

## 5. Setup order

1. Create the three roles in the **root business unit** (Dataverse replicates one copy per BU automatically).
2. Apply the matrices in §3 — **Custom Entities** tab.
3. Enable **auditing** on **all five** tables (Update + Delete) per §1's note.
   *(Corrected 2026-10-03: this step originally named only `sprk_decisionrecord` and `sprk_policyversion`.
   That was too narrow — see §7.2 for why `sprk_budgetrevision` in particular needs it.)*
4. Assign **`Spaarke Ontology Service`** to the BFF's Dataverse Application User.
5. Assign **`Spaarke Console User`** to yourself plus any test users, *alongside* their existing roles.
6. **Verify the append-only guarantee holds** rather than assuming it: as a non-admin test user, try to update a
   `sprk_decisionrecord` row and confirm it is refused. This is the one check worth doing by hand, because it is
   the property everything else rests on — and the `prvWrite` could be granted by any role the user happens to
   hold.

---

## 6. ✅ Applied and verified — 2026-10-02

All six setup steps done by the owner. Verified by query, not assumed:

**Roles exist**: `Spaarke Console User` · `Spaarke Ontology Administrator` · `Spaarke Ontology Service`
(6 copies each — one per business unit, normal). Auditing enabled on the two ledger tables.

**`Spaarke Console User` — present at depth 4 (Parent: Child BU)**: `prvReadsprk_Signal` ·
`prvWritesprk_Signal` · `prvCreatesprk_DecisionRecord` · `prvReadsprk_DecisionRecord`.

**Absent, as designed** — these four are the guarantees: `prvCreatesprk_Signal` ·
`prvWritesprk_DecisionRecord` · `prvDeletesprk_DecisionRecord` · `prvWritesprk_PolicyVersion`.

### The union check (§1's warning), run — and the answer is benign

Every role in the environment was checked for Write/Delete on `sprk_decisionrecord` and Write on
`sprk_policyversion`. **Four roles hold one or more:**

| Role | What it holds | Verdict |
|---|---|---|
| **System Administrator** | Write + Delete DR, Write PV | Unavoidable, already documented in §1 |
| **System Customizer** | Write + Delete DR, Write PV | Unavoidable, already documented in §1 |
| **`Service Writer`** | Write DR (depth 8), Write PV (depth 8) | ✅ **Benign — see below** |
| **`Service Deleter`** | Delete DR (depth 8) | ✅ **Benign — see below** |

**Why the last two are benign.** Their membership is **exclusively Microsoft first-party application
identities** — every holder has an `applicationid` and the `#` system-user prefix: AIBuilder (×2),
ApolloProdFirstParty, AppDeploymentOrchestration, CatalogServiceNam, DV-MetadataService, InsightsAppsPlatform,
MicrosoftCustomerEngagementPortalInfra, PowerPages Data Runtime PROD, three PPMI managed identities,
PpdfCDSClient, Power Apps Checker. **No human user and no Spaarke application identity holds either role.**
Dataverse auto-grants these to its own platform services on every new custom table.

🔴 **Do not strip privileges from `Service Writer` / `Service Deleter`** to "close the gap" — that would break
Power Pages, AI Builder and solution deployment. They are the same category as System Administrator:
platform-level, unavoidable, and not a path a person or the application can take.

**Net**: append-only holds against **every human user and against Spaarke's own application identity**, which is
the bar §1 set. The check was worth running; the finding is "documented, no action."

---

## 7. Second verification pass — 2026-10-03

Ran after the owner asked whether anything else was outstanding. Five things were checked that §6 had not
covered. **Nothing blocks `/design-to-spec`.** One gap was found and fixed; two items are open choices.

### 7.1 🟢 Org-level auditing is ON — checked because table auditing is inert without it

`organization.isauditenabled = True` on `spaarkedev1`. Worth stating explicitly because it is a classic silent
failure: per-table `IsAuditEnabled` can read `true` on every table and **still record nothing** when the
org-level flag is off. The §6 claim "auditing enabled" was only half-verified; it now rests on both halves.
(`auditretentionperiodv2` is null = platform default retention.)

### 7.2 🟡 FIXED — `sprk_budgetrevision` auditing was OFF; now ON

State before: `sprk_signal` / `sprk_decisionrecord` / `sprk_policy` / `sprk_policyversion` = **on**,
`sprk_budgetrevision` = **off**. That exactly matched §5 step 3, which named only two tables — so this was a
**gap in the instruction, not in the owner's execution**, who in fact enabled four. Step 3 is corrected above.

**Why this table specifically needs auditing — the reason is not symmetry.** The other four record *what we
did*. `sprk_budgetrevision` is the only one carrying a fact whose **absence** a Signal asserts: Path B's second
conjunct is a **NOT-EXISTS** — *no budget revision in this window*.

Absence is the one claim that cannot be re-verified later from current data:

- a revision **added** after the fact makes a **correct** Signal look wrong;
- a revision **deleted** after the fact makes an **incorrect** Signal look right.

Because `design.md` §8.2 deliberately **ruled out bitemporality**, there is no as-of reconstruction to fall
back on. The audit log on this one table is the only remaining way to establish what was true when the evaluator
ran — i.e. it is the cheap partial mitigation of the exact cost §8.2 accepted. Applied by
retrieve-modify-update `PUT EntityDefinitions(LogicalName='sprk_budgetrevision')` → HTTP 204; all five then
verified `audit=True`.

### 7.3 🟢 Per-BU role copies: privileges live ONLY on the root-BU record — normal, do not "fix"

Querying all six BU copies of each role shows privileges on the **root `Spaarke` BU copy only** (Console User
14, Ontology Administrator 25, Ontology Service 15) and **zero** on the copies in `Spaarke Dev 1`,
`Spaarke Test 1`, `Spaarke Demo`, `Spaarke Business Unit 1` and `Secure Record`.

**This looks alarming and is not.** The control settles it: **`Spaarke Core User`** — long-established and
demonstrably working in production — has the identical shape, **744 privileges on the root copy and 0 on all
five child copies**. Child-BU records are inherited shells; `roleprivileges_association` reports the root record.

All four holders sit in the root `Spaarke` BU (Chelsea Friez, Lori Witkin, Ralph Schroeder, and the
`SDAP-BFF-SPE-API` application user), so each holds the copy that carries the privileges. **No action.**

> **Method note worth keeping**: a `$top=1` read of `roles?$filter=name eq '...'` returns an *arbitrary* one of
> the six copies and will report `(none)`. Any future privilege audit must enumerate **all** copies, and should
> re-run the `Spaarke Core User` control before concluding that a zero is a defect.

### 7.4 🟢 The presence half of the privilege check — §6 had verified only absence

§6 confirmed the four guarantee-bearing privileges are **absent** from Console User. It never confirmed that
the privileges which make the system **work** are present. They are:

| Role | Signal | Decision Record | Policy | Policy Version | Budget Revision |
|---|---|---|---|---|---|
| **Ontology Service** (BFF) | **C** R W Ap ApTo **Asg** | **C** R Ap ApTo | R ApTo | R ApTo | R |
| **Ontology Administrator** | R W D Ap ApTo Asg | **C** R Ap ApTo | C R W D ApTo | C R D ApTo | C R W D Ap ApTo |

Three things this confirms:

1. **`prvCreatesprk_Signal` IS on Ontology Service.** Nothing works without it — no role could create a Signal.
2. **Neither role holds Write on `sprk_decisionrecord`.** Append-only holds on the *administrator* role too,
   which is the case that actually matters — an admin is the plausible accidental editor, not a service.
3. **Ontology Service holds `Assign` on `sprk_signal`.** This is what makes the carried-forward
   **Signal-ownership fix** (§4 — set the owner from the grouping matter at creation) executable **without a
   privilege change**. Worth knowing before the spec treats it as a prerequisite.

### 7.5 🟢 No column-level security on any of the 130 columns — and it must stay that way

The environment has six field security profiles (`Local Identity Credentials`, `Spaarke Identity Link
Readers`/`Writers`, `System Administrator`, `Integrated Search Provider Profile`,
`Standing Grant Administrators`). **None touches any column on the five tables** — zero attributes have
`IsSecured = true`.

That is the correct state, and it is a **constraint rather than only an observation**. Column-level security on
`sprk_sentence`, or on the denormalized `sprk_regarding*` trio, would make the trio diverge from the typed
lookup **per user** — precisely the inconsistency the trio exists to prevent. If column security is ever
proposed on these tables, resolve it against the resolver design first.

### 7.6 🟡 OPEN — none of the five tables is in any app module

`appmodulecomponents` holds **no `componenttype = 1` (entity) row** for any of the five, in any app. There is
therefore no sitemap entry and no way to open a Policy, Signal or Decision Record form by hand.

- **Not a blocker for the Console**, which reads through the BFF and never uses a sitemap.
- **It does bite §8.1 dev-data seeding and debugging** — authoring a `sprk_policy` row by hand, or opening a
  Signal to see why a predicate fired, currently has no UI at all.
- Mitigating: `IsValidForAdvancedFind = true` on all five, so rows are already reachable via Advanced Find.

**Recommendation: add all five to `Spaarke Platform`.** It holds **90 entities (87 `sprk_`)** — it is the
configuration/admin app. `Matter Management` and `Spaarke AI Setup` carry **0** entity components, so config
tables are not what those are for. Do **not** add these to an end-user app.

### 7.7 🟡 OPEN — `Spaarke Ontology Administrator` is assigned to nobody

Zero users and zero teams hold it. Leaving it empty is defensible, but the consequence is specific: Policy
authoring then happens as **System Administrator**, so **the role's 25 privileges stay unexercised** until a
customer environment hits them — the worst possible place to discover a missing `prvAppendTo`. Assigning it to
one person means the first Policy authored exercises the real role.

### 7.8 🟢 Other table settings, confirmed

All five are **`UserOwned`**, so the depth-4 scoping in §3 is meaningful — an organization-owned table would
make it inert. All five have **`IsValidForAdvancedFind = true`**. Both alternate keys remain **Active**.

---

## 8. Task 002 re-verification after the column adds (2026-10-03) — **ESCALATION**

Re-run after task 001 added `sprk_action` / `sprk_actioncode` to `sprk_decisionrecord` and
`sprk_direction` / `sprk_disposition` / `sprk_responseduedate` to `sprk_servicerequest`.
**72 Spaarke role records across 6 business units** enumerated; per-BU-copy shape is the normal
inherited-shell artifact recorded in section 7.3 (root-BU copy carries the privileges, the five child
copies report 0) and the `Spaarke Core User` control reproduces it exactly (744 on root, 0 on children).

### 8.1 The three ontology roles — as designed ✅

| Role | Ontology privileges on the root-BU copy |
|---|---|
| `Spaarke Console User` | 14 — Read on all five, `Write`/`Append`/`AppendTo` on `sprk_signal`, `Create` on `sprk_decisionrecord`. **No** Create on Signal, **no** Write or Delete on `sprk_decisionrecord`, **no** Write on `sprk_policyversion` |
| `Spaarke Ontology Administrator` | 25 — Create/Write/Delete across Policy, PolicyVersion, BudgetRevision; Delete + Assign on Signal. **No** Create on Signal, **no** Write or Delete on `sprk_decisionrecord`, **no** Write on `sprk_policyversion` |
| `Spaarke Ontology Service` | 15 — `prvCreatesprk_Signal` ✅, `prvCreatesprk_DecisionRecord` ✅, `prvAssignsprk_Signal` ✅ (the three task 030 needs), Read on all five, Write on Signal only |

So the **four guarantee-bearing privileges behave as designed at the role level**:
`prvCreatesprk_Signal` appears only on the Service role; `prvWritesprk_DecisionRecord`,
`prvDeletesprk_DecisionRecord` and `prvWritesprk_PolicyVersion` appear on **none** of the three.

`prvAssignsprk_Signal` is present on the Service role, confirming the **FR-14 owner-from-matter
requirement in task 030 needs no privilege change** — as section 7.4 predicted.

### 8.2 The union check — and the finding it surfaced 🔴

Every role in the environment holding any of the four:

| Privilege | Holders (all root BU) |
|---|---|
| `prvWritesprk_DecisionRecord` | `Service Writer`, `System Administrator`, `System Customizer` |
| `prvDeletesprk_DecisionRecord` | `Service Deleter`, `System Administrator`, `System Customizer` |
| `prvWritesprk_PolicyVersion` | `Service Writer`, `System Administrator`, `System Customizer` |
| `prvCreatesprk_Signal` | `Service Writer`, `System Administrator`, `System Customizer`, **`Spaarke Ontology Service`** |

No *human-facing Spaarke* role holds Write or Delete on either ledger. `Service Writer` (14 members) and
`Service Deleter` (8 members) are **exclusively Microsoft first-party application identities** —
Power Pages managed identities, AI Builder, `DV-MetadataService`, `AppDeploymentOrchestration`,
`PowerApps Checker` — verified by listing membership, and **not to be stripped** (section 7.5, and the
standing constraint: stripping them breaks Power Pages, AI Builder and solution deployment).

**But `System Administrator` is not only held by humans.** Its 17 members include **14 application
users**, among them **`SDAP-BFF-SPE-API`, `# mi-bff-api-dev` and `# spaarke-bff-api-prod`** — Spaarke's
own BFF identities. And:

> **`Spaarke Ontology Service` is assigned to exactly one principal: `SDAP-BFF-SPE-API` — which is
> itself a `System Administrator`.**

Dataverse privileges are **additive across roles with maximum depth and there is no deny**. So the
principal that will write Signals and Decision Records holds `prvWritesprk_DecisionRecord` and
`prvDeletesprk_DecisionRecord` **through its sysadmin membership**, and the Create-only shape of
`Spaarke Ontology Service` constrains nothing about it. The role is, for this principal, decorative.

**What is and is not true after this check:**

- ✅ **True**: no human using the Console or Administrator roles can update or delete a Decision Record.
- ✅ **True**: `sprk_policyversion` has no Write privilege on any Spaarke role.
- 🔴 **NOT true**: *"append-only is enforced by privilege"* for the **writer**. For `SDAP-BFF-SPE-API` the
  only thing preventing an update is that the code does not issue one. That is a code-discipline
  guarantee, not a platform guarantee — and ADR-002 forbids the plugin that would otherwise enforce it.

This matters because the defensibility claim rests on it: *"the Decision Record is append-only"* is a
statement about what the **system** cannot do, and §0.3 binds this project to testing what its message
claims. Owner decision required — see `notes/002-escalation-append-only-writer-principal.md`.

### 8.3 Criterion 4 (the negative test) — not yet executable *(✅ executed 2026-10-04 as the task 006 writer; see §9)*

The POML requires attempting an update as a non-admin holding only the Spaarke roles and confirming
refusal. Not executable today: `sprk_decisionrecord` has **0 rows**, and the only non-admin principals
hold `Spaarke Console User`, which has no Write privilege to exercise against a row that does not exist.
**Task 041 already owns this test** (*"Test as a real non-admin"*), after task 004/005 seed data. Recorded
here rather than filed as a new defer item, because an existing task covers it.


## 9. Task 006: the dedicated writer identity (2026-10-04): **escalation resolved, option A**

Owner chose option A on 2026-10-03 (§8). Plan and the facts verified before any change are in
`notes/006-writer-identity-plan.md`.

### What exists now

| Item | Value |
|---|---|
| User-assigned managed identity | `mi-ontology-writer-dev`, rg `rg-spaarke-dev`, `westus2`, tags `project`/`purpose` |
| **Client id (task 030 authenticates as this)** | **`69040982-612e-469e-a85f-26d5172367c5`** |
| Principal (object) id | `6cf6d7b6-8dc2-4cfb-a971-649df8605be6` |
| Attached to | `spaarke-bff-dev`, **alongside** `mi-bff-api-dev` (`5967251e-…`), which remains the identity every existing credential and Key Vault reference uses |
| Dataverse application user | `# mi-ontology-writer-dev`, systemuserid `3121bf1b-9fbf-f111-aaaf-0022482913fc`, `accessmode` 4 (non-interactive), root BU `Spaarke` |
| Directly assigned roles | **exactly one**: `Spaarke Ontology Service` (root copy `b1fb7ee0-bfbe-f111-aaaf-0022482913fc`) |
| Credentials | none created. The service principal is `ManagedIdentity` type with 0 password credentials (its one key credential is the Azure-managed MI certificate). No app registration, no Key Vault secret, no app setting added. `BFF-API-ClientSecret` / `bff-api-client-secret` do not exist in `spaarke-spekvcert` |

### Union check (task 002 step 4, re-run for the new principal)

Method: `systemusers(3121bf1b-…)/Microsoft.Dynamics.CRM.RetrieveUserPrivileges()`, the **effective** set,
which includes roles inherited through team membership, resolved to privilege names.

| Privilege | Effective | Required |
|---|---|---|
| `prvCreatesprk_Signal` | Global | ✅ required, present |
| `prvCreatesprk_DecisionRecord` | Global | ✅ required, present |
| `prvAssignsprk_Signal` | Global | ✅ required, present |
| `prvWritesprk_DecisionRecord` | **absent** | ✅ must be absent |
| `prvDeletesprk_DecisionRecord` | **absent** | ✅ must be absent |
| Write / Delete / Share / Assign on `sprk_policy`, `sprk_policyversion` | **all absent** | ✅ so `sprk_policyversion` is no-update for the writer too |

**Result: PASS.** The sentence "the writer cannot update or delete a Decision Record" is now true by
privilege, not only by code discipline. Task 002 criterion 3 is satisfied for the writer principal.

### 🟡 Finding: the effective set is wider than the one role, and that is load-bearing

Every user is automatically a member of its business unit's **default team** (this cannot be removed). The root
`Spaarke` default team holds **`Spaarke Console User`, `Spaarke Basic User`, `Spaarke Office Add In User`**, so
the writer's effective union is **722 privileges**, not the Ontology Service role's handful. None of the three
adds Write or Delete on the ledger (verified above).

The inheritance is also **what the writer runs on**. `Spaarke Ontology Service` is Create-only by design, so the
following come only from the default team:

- **`prvWritesprk_Signal` (Global)**: the evaluator needs it to stamp `sprk_lastevaluated` and to close Signals
  (`ConditionCleared` / `Superseded` / `PolicyRetired`, D-12).
- **Every Read**: `sprk_policy`, `sprk_policyversion`, `sprk_signal`, `sprk_decisionrecord`, and the predicate
  inputs.

Consequence: changing the root default team's roles silently changes what the writer can do. **Task 030 should
decide** whether to add `prvWritesprk_Signal` and the Global reads it depends on to `Spaarke Ontology Service`
itself, so the writer no longer depends on the team. That is a role edit, which is outward-facing, so it needs the
owner. Not changed here: task 006's scope was the identity, and the writer works today.

### 🔴 Hazard for tasks 021 / 030: NOT-EXISTS over a table the writer cannot fully read

Read depth for the writer, as the effective union stands:

| Global (whole org) | Basic (own rows only) |
|---|---|
| `sprk_matter`, `sprk_communication`, `sprk_budget`, `sprk_budgetrevision`, `sprk_invoice`, `sprk_project`, `sprk_event`, `sprk_memo`, `sprk_todo` | `sprk_spendsnapshot`, `sprk_spendsignal`, `sprk_document`, `account`, `contact`, `team`, `businessunit`, `sprk_gridconfiguration` |

The Path B predicate reads `sprk_communication` (EXISTS) and `sprk_budgetrevision` (NOT EXISTS): both
**Global**, so Path B is sound.

But a NOT-EXISTS clause over a **Basic**-depth table would see only the writer's own rows. That is effectively
always empty, so the clause would be **true for every matter** and the evaluator would assert something it never
checked. That is a §0.3 violation that produces Signals rather than an error. The predicate compiler (021) or
the writer (030) must **fail closed** when a rule's NOT-EXISTS target is a table the evaluating principal cannot
read at Global depth. An EXISTS clause over such a table fails the other way (silently false), which is also
wrong but quieter.

### Negative test on the wire (closes §8.3 / task 002 criterion 4)

Run 2026-10-04 against `sprk_decisionrecords` as the writer, via `MSCRMCallerID: 3121bf1b-…` (impersonation
makes Dataverse enforce **the writer's** privileges, not the caller's):

| Attempt | As | Result |
|---|---|---|
| Create a Decision Record | writer | **HTTP 204**; owner and createdby = the writer |
| Update `sprk_proposedaction` | writer | **HTTP 403 `0x80040220`** (privilege denied). Dataverse reports `roleCount=4, privilegeCount=722, accessMode='4 Non-interactive'`, which matches the union above |
| Delete | writer | **HTTP 403 `0x80040220`** |
| Update (positive control) | admin | HTTP 204, so the 403s are the privilege check, not a broken row |
| Delete (cleanup) | admin | HTTP 204 |

Two test rows were created across two runs (`9d9cf3b0-…`, `bc8b43bd-…`), both named
"ONTOLOGY DEV TEST 006 negative test (delete me)". **Both deleted; `sprk_decisionrecord` is back to 0 rows.**

The writer is a non-admin principal holding only Spaarke roles, so this is the test §8.3 said was not yet
executable. Task 041 still tests the guarantee for **human** users in the Console.

### Not verified here, and where it is verified

- **Token acquisition as the new identity from inside the app**: no code uses it yet, and the Kudu SCM container
  has no `IDENTITY_ENDPOINT` (ADR-028 E-2 note), so it cannot be exercised from outside. **Task 030** proves it on
  first write. If it fails, task 006's escalation trigger applies: **stop**, and do not fall back to the sysadmin
  identity.
- **Post-attach health**: `/healthz` 200 and `/healthz/dataverse` 200 after the attach. `/healthz/catalog` returns
  503, but that is **pre-existing** `ai-catalog-reconciliation` drift. App Insights logs the same message on
  2026-09-29, 09-30, 10-02 and 10-03 02:25Z, all before this change at 2026-10-04 02:49Z. Owned by the AI catalog,
  not this project.

### 9.1 Who owns a Signal: settled live (2026-10-04, task 030 independent review)

§4 said "set the owner (or at minimum the owning BU) from the grouping matter". Task 030 first implemented
**owner = the matter BU's default team**. The independent review predicted that would fail, and probes as the
writer (`MSCRMCallerID: 3121bf1b-…`) settled it. Every probe row was deleted; 0 remain.

| Probe (create `sprk_signal` as the writer) | Result |
|---|---|
| Baseline: no lookup, no owner | 204 |
| `sprk_Matter` → BU1 matter / → root matter | **204 / 204**. Nav property is `sprk_Matter`. ⚠️ **Not** because "AppendTo at Basic is no blocker": AppendTo is Basic on BOTH tables, yet the writer holds FULL rights (incl. Assign/Share) on all matters and only Read on communications (second review, `RetrievePrincipalAccess`). The grant behind matter access is **under investigation** (2026-10-04). A communication lookup fails (403 AppendTo, task 030 F26) |
| `ownerid` → **BU1 default team** | **403 `0x80040299`** "Read Privilege Check For Owner failed": that team holds no role with Read on `sprk_signal` |
| `ownerid` → root default team | 204, but useless: BU1 users read Signals at Parent:Child BU depth, and root is above them |
| **owner = writer, `owningbusinessunit` → BU1** | **204; stored owner = writer, owningbu = BU1** |

**Decision (no role change needed):** the writer stays the owner, and `owningbusinessunit` is set from the
grouping matter's BU on create. Console User's Parent:Child BU read then shows the Signal to exactly the matter's
BU (and the BUs below it). This works because spaarkedev1 has
**`EnableOwnershipAcrossBusinessUnits = true`**: an **environment dependency**. Provisioning must enable it for
every new environment, and the writer verifies the stored owning BU after each create and refuses on mismatch.

### 9.2 Correction: `RetrieveUserPrivileges` is NOT the effective set for team-inherited roles (2026-10-04)

§9 called `RetrieveUserPrivileges` "the effective set". **It is not, for roles a user inherits through a team.** The
three root-default-team roles have `isinherited = 1` ("Direct User (Basic) access level and Team privileges"). The
member's *own* copy of each privilege is reported at **Basic**, while the **team's** copy, which reaches the
member through team membership, can be **Deep**. That only shows in `teams(<id>)/RetrieveTeamPrivileges()` or in
`RetrievePrincipalAccess`. **Method from now on: effective = `RetrieveUserPrivileges` ∪ `RetrieveTeamPrivileges`
for every team, confirmed with `RetrievePrincipalAccess` on a real row.**

**Ledger re-verified with the corrected method:** user source and root-team source together hold **no Write or
Delete on `sprk_decisionrecord`, and no Write on `sprk_policy` / `sprk_policyversion`**. Append-only stands, and the
live 403 `0x80040220` on update and delete (§9) was already enforcement-level proof.

**What the corrected method reveals (investigation 2026-10-04, read-only):** the root default team `Spaarke` holds
**`Spaarke Office Add In User`**, which grants Read, Write, Append, AppendTo, Create, Share and Assign on
`sprk_matter` at **Deep** (no Delete). Deep from the root covers **every matter in every BU**, including
`Secure Record`. Every root-BU principal is in that team: about 150, mostly app users, including the writer,
`# mi-bff-api-dev`, `# spaarke-bff-api-prod` and the control-plane UAMI. The team also gives Deep Write on
`sprk_signal` and Deep Create on `sprk_decisionrecord`.
- **Known and accepted for dev:** `spaarkeai-word-add-in-r1/notes/role-grant-gap-2026-09-21.md` §8.1 flagged it;
  `unified-access-control-r2/notes/session27-owner-decisions-and-research.md` owner round 5 (2026-10-02) accepts
  root-BU membership as a dev artifact; for production, app users belong in a customer child BU (**issue #1094,
  open**).
- **Consequences for this project:**
  1. The writer is **not** least-privileged in practice. Through the team it can Write, Assign and Share any
     matter.
  2. Task 030's matter lookup works **only** because of this team grant (`Spaarke Ontology Service` has Read
     on matter, not AppendTo), and communication lookups fail (F26). The writer depends on an over-grant that is
     slated to change. *(F26 closed by §10: the writer now holds AppendTo on communication at Global in its own
     role.)*

---

## 10. Task 008: owner-approved role edits, applied and union re-verified (2026-10-07)

Owner decisions D-14, D-18, D-22, D-29 and D-33 (spec §9). Environment `spaarkedev1`. Applied by the dev operator
(`ralph.schroeder@spaarke.com`, systemuser `1d02f31c-…`, az CLI token) with `AddPrivilegesRole` on each role's
**root-BU copy** (the copy that carries privileges, §7.3). Each edit was read back from `roleprivilegescollection`
in the same script run.

### 10.0 uac-r2 coordination (spec §8.3)

`origin/master` @ `dbc58d139`, fetched 2026-10-07, the same commit task 079 read. Files and last commits:
`SecureChildLineage.cs`, `config/secure-record-owner-role.json`, `RecordOwnershipResolver.cs`,
`CoreAncestorResolver.cs`, `RecordRouteAccessAuthorizationFilter.cs`, `ExternalCallerContext.cs`,
`.claude/adr/ADR-034-*` all at `d254d7166`; `CallerRecordAccessProbe.cs` `428c5bfae`;
`RouteAuthorizationGuardTests.Ledger.cs` `dae5869d2`; `.claude/adr/ADR-003-*` `8c5c517a1`;
`scripts/Set-SecureRecordOwnerRolePrivileges.ps1` `9fee1e8e2`. **Nothing changed since 079's read, so the plan
stands.** Open uac-r2 PRs are #1353 (task 171) and #1342 (task 114); neither touches the owner-role config or the
lineage. #1355 (079's review request) is open with no reply yet; it gates task 039, not these role edits.

What uac-r2's mechanism relies on, checked before editing:
- **Secure Record Owner.** Its script **adds only and never removes** ("Outside the file … never removed by this
  script"). Its `-Verify` fails only on a *missing* table, so the two new Reads cannot turn it red.
- **Spaarke Basic User.** uac-r2's NFR-05 standing census (`SecureBuRoleDepthAssertion`) flags **Deep or Global**
  held by a human at an ancestor of the Secure BU. The D-33(b) grant is **Basic** (own/owning-team rows only), so it
  is outside what the census measures and cannot reach the Secure BU.

### 10.1 Before → after (every BU copy enumerated)

Snapshot of all privileges on every BU copy of Ontology Service, Ontology Administrator, Console User, Secure
Record Owner, Spaarke Basic User and the Spaarke Core User control, taken before any edit and again after all
edits. **Diff: exactly the 14 privileges below were added. Nothing was removed and no depth changed on any copy of
any of the six roles.**

| Role (root-BU copy) | Count before → after | Added (depth) | Applied (UTC) |
|---|---|---|---|
| Spaarke Ontology Service `b1fb7ee0-…` | 40 → 46 | D-29: `prvAppendTosprk_Communication`, `prvAppendTosprk_Event`, `prvAppendTosprk_Todo`, `prvAppendTosprk_WorkAssignment` (**Global** = Organization). D-18: `prvCreatesprk_BudgetRevision` (Global). D-33(c): `prvAssignsprk_DecisionRecord` (Global) | 15:06:21 (D-29, D-18), 15:07:14 (D-33c) |
| Spaarke Ontology Administrator `2f2b2137-…` | 34 → 38 | D-14: `prvWritesprk_PolicyVersion` (**Deep**). D-22: `prvCreatesprk_TriageCategory`, `prvWritesprk_TriageCategory`, `prvReadsprk_TriageCategory` (**Global**; the table is OrganizationOwned and its privileges allow Global only) | 15:06:40 |
| Secure Record Owner `e4ebabd9-…` (one copy, BU `Secure Record`) | 26 → 28 | D-33(a): `prvReadsprk_Signal`, `prvReadsprk_DecisionRecord` (**Basic**) | 15:07:05 |
| Spaarke Basic User `11f93c04-…` | 645 → 647 | D-33(b): `prvReadsprk_Signal`, `prvReadsprk_DecisionRecord` (**Basic**) | 15:07:10 |
| Spaarke Console User `51c924e2-…` | 23 → 23 | **none.** `prvWritesprk_Signal` (Deep) is **still present**; task 049 removes it (D-17) | — |

Child-BU copies of every role still report 0, and the `Spaarke Core User` control still reads 744 on root and 0 on
the five children. That is the inherited-shell artifact (§7.3), not a defect.

**Depths chosen where the decision did not name one.** D-14 Write on `sprk_policyversion` is **Deep**, matching the
administrator role's Create/Read/Delete on that table. D-18 Create on `sprk_budgetrevision` is **Global**, matching
the Service role's convention. D-29's AppendTo is **Organization**, as the POML requires (Deep would not reach
Secure-team-owned subjects in the sibling Secure Record BU once the writer moves to a customer child BU, #1094).

**Platform side effect, reverted.** On Secure Record Owner, `AddPrivilegesRole` also injected
`prvReadSharePointData`, `prvCreateSharePointData`, `prvReadSharePointDocument` and `prvWriteSharePointData`
at Global. These were absent before the call. This is the behaviour uac-r2's `SECURE-PROJECT-ENVIRONMENT-SETUP.md`
§5.4 documents, and its strip procedure (owner decision #1046) removes them. All four were removed with
`RemovePrivilegeRole` at 15:07:28–32Z, which restored the before state; the 26 → 28 count above is net of the
removal. No other role received injected privileges.

Then uac-r2's own `Set-SecureRecordOwnerRolePrivileges.ps1 -Verify` (from `origin/master`, with the `origin/master`
config) returned **exit 0**: 26 of 26 tables present, and 2 "Outside the file", namely the two new Reads.

### 10.2 Role assignment (D-22)

`Spaarke Ontology Administrator` is held by **Ralph Schroeder, `ralph.schroeder@spaarke.com`, systemuser
`1d02f31c-1872-f011-b4cb-7c1e52671ad0`**, the identity the az CLI and the Dataverse calls run as. The user sits in
the root `Spaarke` BU and holds the root copy, which carries the privileges. **The assignment already existed when
this task started**, so no assign call was made. §7.7 recorded 0 holders on 2026-10-03, so it was made between
then and now, presumably by the owner. It is the only holder, with no teams. Other "Ralph" accounts
(`…@spaarke.onmicrosoft.com`, the `#EXT#` hotmail guest, two disabled test users) do **not** hold it.

### 10.3 Union check (task 002 method, environment-wide)

Every role in the environment holding each privilege:

| Privilege | Holders |
|---|---|
| `prvWritesprk_DecisionRecord` | Service Writer, System Administrator, System Customizer |
| `prvDeletesprk_DecisionRecord` | Service Deleter, System Administrator, System Customizer |
| `prvWritesprk_PolicyVersion` | Service Writer, System Administrator, System Customizer, **Spaarke Ontology Administrator (Deep, D-14)** |
| `prvAssignsprk_DecisionRecord` | Service Writer, System Administrator, System Customizer, **Spaarke Ontology Service (Global, D-33c)** |
| `prvCreatesprk_Signal` | Service Writer, System Administrator, System Customizer, Spaarke Ontology Service |
| `prvWrite`/`prvDeletesprk_BudgetRevision` | platform/admin roles + Spaarke Ontology Administrator (pre-existing). **Not** Ontology Service |
| `prvWritesprk_Budget` | platform/admin roles + Spaarke Core User (pre-existing). **Not** Ontology Service |

Classification:
- **Platform**: Service Writer (14 members) and Service Deleter (8 members). **0 humans**, all application users
  (§8.2: Microsoft first-party). Not stripped.
- **Admin**: System Administrator (unchanged from §8.2, including the sysadmin BFF identities accepted under option
  A) and System Customizer, whose **2 members are both application users**: `# Microsoft Copilot Studio` and
  **`# github-actions-spe-infrastructure`**. The second is a Spaarke CI identity. §8 did not name it, but it holds
  the ledger privileges through an admin role, the same category the owner accepted for the sysadmin BFF
  identities. It was not caused by this task.
- **Human**: Spaarke Ontology Administrator → Ralph Schroeder only. It holds Write on `sprk_policyversion` (D-14),
  and per D-14 version-body immutability is now enforced by the publish service, not by privilege.
- **Spaarke identity**: no Spaarke role holds `prvWritesprk_DecisionRecord` or `prvDeletesprk_DecisionRecord`.

**Writer effective set** (`# mi-ontology-writer-dev`, `RetrieveUserPrivileges` ∪ `RetrieveTeamPrivileges` for
its only team, root `Spaarke`, per §9.2):
- **Absent**: Write and Delete on `sprk_decisionrecord`, `sprk_policyversion`, `sprk_budgetrevision`; Create, Write
  and Delete on `sprk_budget`.
- **Present at Global through its own role**: Assign on `sprk_decisionrecord`, Create on `sprk_budgetrevision`, and
  AppendTo on the four D-29 tables.

### 10.4 Live checks

**Append-only negative** (impersonation via `MSCRMCallerID`). Each principal created its own probe row, then:

| Principal | Update | Delete |
|---|---|---|
| Chelsea Friez (`c46d44ca-…`, human, non-admin: Core User + Console User + Office Add In User + default team) | **403 `0x80040220`** 15:12:19Z (`roleCount=4, privilegeCount=794, accessMode='0 Read-Write'`) | **403 `0x80040220`** |
| writer `# mi-ontology-writer-dev` | **403 `0x80040220`** 15:12:22Z (`accessMode='4 Non-interactive'`) | **403 `0x80040220`** |

Both rows were deleted as admin (204). 0 `zz-008` rows remain.

**Secure Record Owner positive probes** (guide §5.3 step 4). A create with `ownerid@odata.bind → /teams(Secure
Record Owners 6eabc7f9-…)` returned **204 on 3 of 3 polls** for both `sprk_signal` (15:12:40, 15:13:03,
15:13:25Z) and `sprk_decisionrecord` (15:12:42, 15:13:04, 15:13:26Z). The read-back `owningteam` was the team in
every case, and each probe was deleted. Control: the same create on `sprk_policy` (not in the role) was refused,
`0x80040299 … privilegeCount=28 … missing prvReadsprk_Policy`. So the reading was current. The **before** refusals
for both tables are task 079's (`notes/079-uac-coordination.md` §3, 14:58Z, before these edits).

**F26 closed.** `SignalWriterSeamTests` was run as the writer (`SIGNALS_LIVE_CALLER_ID=3121bf1b-…`) from a fresh
detached worktree at `c7dad4812`. Both tests passed, including
`WriteAsync_CommunicationSubject_DerivesTheRealGroupingMatter`, which failed with AppendToAccess on 2026-10-04. 0
`ONTOLOGY-DEV-TEST-030` rows remain.

### 10.5 Spaarke Platform (D-22)

All five tables were **already** entity components of `Spaarke Platform` (`sprk_SpaarkePlatform`, appmoduleid
`d908a85b-…`), added by the owner (app modified 2026-10-03). The app lists no explicit forms, so all forms are
available. Each table's only Main form ("Information") had its bound controls set to `disabled="true"` (2 per form:
`sprk_name`, `ownerid`). The forms had no unpublished edits pending. The change was made under
`MSCRM.SolutionUniqueName: OntologyPlatformSolution` and published with a **targeted** `PublishXml` (the 5 entities
and the app; no PublishAllXml) at 15:09:50Z. Form XML before the change is kept in the session scratchpad only.
This is a form setting, not a plugin (ADR-002). The Quick View forms are read-only by type.

⚠️ **The forms show only Name and Owner.** They are read-only, but they are not yet useful for inspecting a Signal
or a Policy Version. Adding the tables' columns to the forms is form design the decision did not specify, so it
was not done here (see 10.7).

### 10.6 Deploy order (D-33) — per environment

| Environment | Role edits applied | BFF carrying the new secure-child config (task 039) |
|---|---|---|
| `spaarkedev1` | **2026-10-07 15:06–15:07Z** | Not built. 039 waits on #1355, and the config on `origin/master` still has no `sprk_signal` / `sprk_decisionrecord` entry |
| any other | not applied | — |

Rule for every other environment: apply these role edits **before** deploying a BFF whose
`config/secure-record-owner-role.json` / `SecureChildLineage.cs` include the two tables. Task 039 re-runs
`-Verify` against the new config.

### 10.7 Deviations from the POML and open items

1. **No assign call (D-22).** The owner's user already held the role (10.2).
2. **The five tables were already in the app.** Only the read-only form setting was applied (10.5).
3. **Four platform-injected SharePoint privileges were removed from Secure Record Owner.** This restores the before
   state; it is not a new privilege change (10.1).
4. **Criterion "Ontology Service holds no privilege on `sprk_budget`" is not literally true.** The role has held
   `prvReadsprk_Budget` (Global) since setup (§3 lists it as an evaluator input). Nothing was added on
   `sprk_budget`, and Write/Delete on `sprk_budgetrevision` stay absent. The Read was not removed, because that
   would be an unapproved edit that breaks the evaluator. **The owner should confirm that reading.**
5. 🟡 **Interim hazard until task 039 lands.** uac-r2's runbook §5.4 says to strip anything the script lists as
   "Outside the file". The two new Secure Record Owner Reads are outside the file until 039 adds the config
   entries. If anyone runs that strip before then, it removes them, and a Secure-matter Signal create then fails
   with `0x80040299` once 039 deploys (039's `-Verify` would catch it first). This should be noted on #1355.
6. **Observations, no action taken.** `# github-actions-spe-infrastructure` holds System Customizer (10.3). Ontology
   Administrator holds `prvDeletesprk_PolicyVersion` (Deep), which predates this task (§7.4) and differs from §3's
   "Delete = None even for the admin".
7. **Not done here:** the read-only form was not checked in a browser (no Chrome session). It was verified from the
   published form XML.

## 2026-10-09 — D-108: writer role grants for revise-budget (task 044)

Owner-approved (2026-10-09) after the #1515 review. Applied by the main session to the root copy of **Spaarke Ontology Service** `b1fb7ee0-bfbe-f111-aaaf-0022482913fc` (BU `06fbf21c-…`) via `AddPrivilegesRole` at 2026-10-09T15:04:10Z (HTTP 204). Read back with `RetrieveRolePrivilegesRole` before and after: **46 → 50; added exactly** `prvAppendsprk_BudgetRevision`, `prvAppendTosprk_Budget`, `prvAppendToUser`, `prvAssignsprk_BudgetRevision`, all **Global** (matching the role's other 46, all Global); nothing removed, no depth changed. No other role touched. Repo `SpaarkeMaster/Roles/Spaarke Ontology Service.xml` updated to match.

- Why: the writer creates the revision with lookups to the budget (AppendTo budget, Append revision) and to the reviser (`sprk_revisedby` → systemuser; the writer's AppendToUser was Local via Basic User, so revisers in another BU failed), and must own a Secure-matter revision inside the wall (Assign revision).
- Supersedes task 008's "no privilege on `sprk_budget`" for **AppendTo only**: AppendTo links to a budget and cannot change its amount; Write on `sprk_budget` is still not granted (the amount is written as the signed-in user, D-18).
- Decision Record append-only is unaffected (no Write/Delete on `sprk_decisionrecord` added).

## 2026-10-09 — D-109: Read on `sprk_budgetrevision` for the owning teams (task 044, #1515 round-2 review)

Why: with the revision owned correctly (by the matter's BU owner team, or the Secure Record Owners team on a Secure matter), Dataverse requires the OWNER to hold Read on the table: `0x80040299` "Read Privilege Check For Owner failed". 59 of 62 dev matters are owned by the Business Unit 1 team, whose roles had no Read on revisions. Mirrors `sprk_budget` (Basic User Deep; Secure Record Owner Basic, uac-r2 G146-1).

1. **Spaarke Basic User** root `11f93c04-ddf6-f011-8406-7c1e520aa4df`: `AddPrivilegesRole` `prvReadsprk_BudgetRevision` at **Deep**, 2026-10-09T16:06:59Z (HTTP 204). Before/after `RetrieveRolePrivilegesRole`: 648 → 649, only that one added, nothing removed or changed. Repo `SpaarkeMaster/Roles/Spaarke Basic User.xml` matched.
2. **Secure Record Owner** `e4ebabd9-b4a0-f111-aaac-000d3a99d1d7` (BU Secure Record), via uac-r2's `scripts/Set-SecureRecordOwnerRolePrivileges.ps1` with #1515's `config/secure-record-owner-role.json` (adds the `sprk_budgetrevision` entry):
   - **Negative control** 16:09:21Z (before the grant): POST `sprk_budgetrevisions` owned by team `Secure Record Owners` `6eabc7f9-13be-f111-a05b-0022482913fc` → HTTP 403 `0x80040299` "Principal team … privilegeCount=28 … is missing prvReadsprk_BudgetRevision privilege (Id=f921db62-3fcf-4d02-9ba3-107d0452f484) on OTC=10999". Nothing written.
   - Dry run: MISSING only `prvReadsprk_BudgetRevision`. `-Apply`: added at Basic, 28 → 33 (platform re-injected the SharePoint four). **Strip (setup guide §5.4)**: removed ONLY `prvReadSharePointData`, `prvWriteSharePointData`, `prvCreateSharePointData`, `prvReadSharePointDocument` (`RemovePrivilegeRole`, 4× HTTP 204) → **29** (28 + the one). The guide's sample loop was NOT used: it strips everything outside the file, which would remove this project's D-33(a) `prvReadsprk_Signal` / `prvReadsprk_DecisionRecord`. `-Verify`: PASS (27 tables).
   - **Positive control** 16:12:11Z and 16:12:50Z: the same probe → HTTP 201 twice; both rows deleted (204); 0 `zz-044` probes left.

## 11. Task 041: append-only proven against a real non-admin, and the 1-to-N direction (2026-10-14)

Environment `spaarkedev1` only. Owner approval D-138 (2026-10-14) for the dev data writes. Replay scripts:
`notes/task-041-live-test.sh` (11.2, 11.5) and `notes/task-041-share-probe.sh` (11.4). Every row created carries
`zz-041-`, and both scripts delete what they create in an `EXIT` trap (11.7).

### 11.1 Who was tested, and how

**Chelsea Friez**, systemuser `c46d44ca-33bd-f111-aaaf-0022482913fc` (`chelsea.friez@demo.spaarke.com`, access mode
0 Read-Write, enabled, root BU `Spaarke`), the human non-admin task 008 used (section 10.4). Roles, read live before the run:

| Source | Roles |
|---|---|
| Direct | Spaarke Core User, Spaarke Console User, Spaarke Office Add In User |
| Default team `Spaarke` (`09fbf21c-...`) | Spaarke Console User, Spaarke Basic User, Spaarke Office Add In User |

No System Administrator, System Customizer, Service Writer, Service Deleter, Spaarke Ontology Administrator or Spaarke
Ontology Service. Dataverse itself reported `roleCount=4, privilegeCount=801`.

**Method.** The dev operator's `az` token with the `MSCRMCallerID` header set to Chelsea's id. Dataverse then runs the
request as Chelsea and enforces **her** privileges (the method of sections 9 and 10.4). It needs no secret, password,
new user, role or credential. It is not a Chelsea sign-in, but the privilege check is the platform's own and the error
text below names her as the principal. The positive control (11.2) shows the same row accepts an admin write, so the
refusals are the privilege check and not a broken row.

### 11.2 Update, delete and deactivate by Chelsea: all refused

Chelsea created a Decision Record herself (HTTP 204, so she is its **owner**: `ownerid` = Chelsea), then tried to change
it. Owning the row does not help, because no role she holds has Write or Delete.

| Attempt (run 2026-10-14T17:08:20Z) | Result |
|---|---|
| Update `sprk_proposedaction` | **HTTP 403 `0x80040220`** |
| Delete | **HTTP 403 `0x80040220`** |
| Deactivate (`statecode=1, statuscode=2`) | **HTTP 403 `0x80040220`**, refused on the same Write privilege |
| `RetrievePrincipalAccess` for Chelsea on her own row | `ReadAccess, AppendAccess, AppendToAccess, CreateAccess`. **No `WriteAccess`, no `DeleteAccess`** |
| Row re-read after the three attempts | unchanged (`sprk_proposedaction` original, `statecode` 0, `statuscode` 1) |
| Positive control: admin Update of the same row | HTTP 204 |

Captured errors, verbatim (update and deactivate return the same text):

```
Update:  {"error":{"code":"0x80040220","message":"Principal user (Id=c46d44ca-33bd-f111-aaaf-0022482913fc, type=8, roleCount=4, privilegeCount=801, accessMode='0 Read-Write', AADObjectId='3ebd4e17-fa8c-4b7d-a78c-13410d0238ce', MetadataCachePrivilegesCount=7350, businessUnitId=06fbf21c-1872-f011-b4cb-7c1e52671ad0), is missing prvWritesprk_DecisionRecord privilege (Id=192e7933-c198-463a-a21d-c5c8c7aea132) on OTC=11002 for entity 'sprk_decisionrecord' (LocalizedName='Decision Record'). context.Caller=c46d44ca-33bd-f111-aaaf-0022482913fc. Consider adding missed privilege to one of the principal (user/team) roles."}}
Delete:  {"error":{"code":"0x80040220","message":"Principal user (Id=c46d44ca-33bd-f111-aaaf-0022482913fc, type=8, roleCount=4, privilegeCount=801, accessMode='0 Read-Write', AADObjectId='3ebd4e17-fa8c-4b7d-a78c-13410d0238ce', MetadataCachePrivilegesCount=7350, businessUnitId=06fbf21c-1872-f011-b4cb-7c1e52671ad0), is missing prvDeletesprk_DecisionRecord privilege (Id=02615b9f-bca8-4603-869d-1278c2dd6076) on OTC=11002 for entity 'sprk_decisionrecord' (LocalizedName='Decision Record'). context.Caller=c46d44ca-33bd-f111-aaaf-0022482913fc. Consider adding missed privilege to one of the principal (user/team) roles."}}
```

An earlier run (17:07:55Z, before a navigation-property name in the Signal payload was corrected) gave the
same three refusals. A bound SetState call returned 404 on this Web API ("Resource not found for the segment"), so deactivating by `PATCH statecode` is the
path used (a state change is an Update to Dataverse, so it is checked as Write, as the error text shows).

### 11.3 Known limit: rows cannot be deactivated either

Deactivation is an Update (a Write), so with no Write privilege a Decision Record can **never** be deactivated, by a
human or by the writer. This is the **consequence of the no-Write privilege model**, recorded as a **known limit** and
not a defect: an append-only ledger has no inactive state, and a wrong decision is corrected by writing a **new**
Decision Record, not by hiding the old one. Nobody should file "cannot deactivate a Decision Record" as a bug, and no
view may rely on `statecode` to filter Decision Records (every row stays Active).

### 11.4 Sharing does not reopen Write (checks uac-r2's "mirrored share rights are inert")

uac-r2's `config/secure-record-owner-role.json` entry for `sprk_decisionrecord` says mirrored share rights are inert
because no human role holds Write or Delete. Tested: an admin-owned Decision Record was shared with Chelsea with
`Read, Write, Delete, Append, AppendTo` (`GrantAccess`, HTTP 204). `RetrievePrincipalAccess` then reported
`ReadAccess, WriteAccess, AppendAccess, AppendToAccess, CreateAccess, DeleteAccess` (the share is recorded). Chelsea:

| Attempt on the shared row (run 2026-10-14T17:09:45Z) | Result |
|---|---|
| Read | HTTP 200 (the share works) |
| Update | **HTTP 403 `0x80040220`**, missing `prvWritesprk_DecisionRecord` |
| Delete | **HTTP 403 `0x80040220`**, missing `prvDeletesprk_DecisionRecord` |
| Deactivate | **HTTP 403 `0x80040220`**, missing `prvWritesprk_DecisionRecord` |

A share carries a record's access *rights*, but Dataverse still requires the user to hold the *privilege*. So the
secure-child share mirroring cannot be used to edit a Decision Record. This holds only while no Spaarke role gains Write
or Delete on the table (11.6).

### 11.5 The relationship is 1-to-N with the foreign key on the Signal

**Metadata, read live (`EntityDefinitions`):**

| Check | Result |
|---|---|
| 1:N relationship with the Decision Record as parent and Signal as child | `sprk_signal_decisionrecord`: referenced `sprk_decisionrecord.sprk_decisionrecordid`, referencing **`sprk_signal.sprk_decisionrecord`**, not hierarchical, cascade Delete = RemoveLink, Assign/Share = NoCascade |
| Lookup `sprk_signal.sprk_decisionrecord` | Targets `[sprk_decisionrecord]`, RequiredLevel None (nullable for system closures) |
| **Negative:** N:1 relationships from `sprk_decisionrecord` to `sprk_signal` | **none** (0 of 28) |
| **Negative:** any lookup column on `sprk_decisionrecord` targeting `sprk_signal` | **none** (all 28 lookup columns listed; the `sprk_regarding*` ones target subject tables) |
| Many-to-many relationships on `sprk_decisionrecord` | none |

**Behaviour (run 2026-10-14T17:08:20Z).** One Decision Record and three Signals created with the lookup set to it
(`zz-041-sig-1..3`), plus a decoy Signal with no decision (`zz-041-sig-4-unlinked`). Read **as Chelsea**:

| Read | Rows |
|---|---|
| Navigation from the Decision Record, `sprk_decisionrecords(id)/sprk_signal_decisionrecord` | **3**: `zz-041-sig-1`, `-2`, `-3`, each with `_sprk_decisionrecord_value` = the decision. The decoy is absent |
| FetchXML on `sprk_signal` where `sprk_decisionrecord` eq id (what a "Signals closed by this decision" subgrid runs) | **3**, the same rows |
| `sprk_decisionrecords(id)` read | carries no Signal reference; nothing on the Decision Record lists its Signals |

So "Signals closed by this decision" renders from the Signal side. **Subgrid control:** none of the three
`sprk_decisionrecord` forms (types 2 Main, 6 Quick View, 11 Card, all named Information) has a subgrid today; they are
the read-only Name/Owner forms of section 10.5. The criterion is therefore proven at the query a subgrid executes, not
by opening a rendered form. Adding the control is form design (section 10.7 item 7) and belongs to a Console/UI task.

### 11.6 Union re-check (task 002 method), 2026-10-14T17:10Z

Roles holding the two ledger privileges now (`privileges(id)/roleprivileges_association`):

| Privilege | Held by |
|---|---|
| `prvWritesprk_DecisionRecord` | Service Writer, System Administrator, System Customizer |
| `prvDeletesprk_DecisionRecord` | Service Deleter, System Administrator, System Customizer |

Identical to section 10.3: **no Spaarke role** holds either, and the repo role XML
(`src/dataverse/solutions/SpaarkeMaster/Roles`) agrees: the table carries only Append, AppendTo, Create, Read (plus
Assign on Ontology Service). The accepted admin limit (section 1) stands: System Administrator and System Customizer
always hold everything, and auditing is the detection (`IsAuditEnabled` on `sprk_decisionrecord` = true).

### 11.7 Cleanup proof

The `EXIT` traps deleted every row: two runs of `task-041-live-test.sh` (the first 1 Decision Record + 1 Signal, the
second 1 Decision Record + 4 Signals; 17:07:55Z and 17:08:20Z) and two runs of the share probe (2 Decision Records, one from an aborted first
attempt). Query at 17:10:10Z with `contains(sprk_name,'zz-041')`: **`sprk_signals` 0 rows, `sprk_decisionrecords` 0
rows.** Both tables hold 0 rows in total after the run.

### 11.8 Notes for later tasks

- **Task 049 / D-62** removes Create on Decision Records from users. `task-041-share-probe.sh` creates its row as admin
  and keeps working. `task-041-live-test.sh` has Chelsea create the row and will then fail at step 1, which is a true
  statement about 049 and not a regression.
- Impersonation proves the privilege model; it does not drive the Console UI. Any Dataverse update on a Decision Record
  from the Console would see the same 403. The BFF commit route is the only writer (FR-18).

## 12. 2026-10-14 — Read on `sprk_policy` for Spaarke Basic User (D-110, DI-83 #1882)

- **Why**: the worklist FetchXML (task 050) outer-links `sprk_policy`; Dataverse refuses the whole query (0x80040220) when the
  caller lacks table Read on a linked table, so a Basic + Core user without Spaarke Console User got an empty worklist (task 038 live seam).
- **Before** (live query, spaarkedev1): Spaarke Basic User held `prvReadsprk_Signal` (Basic) but no `prvReadsprk_Policy`;
  Spaarke Console User holds `prvReadsprk_Policy` at Deep.
- **Change**: `AddPrivilegesRole` on the root-BU Spaarke Basic User (`11f93c04-ddf6-f011-8406-7c1e520aa4df`):
  `prvReadsprk_Policy` (`eda1df58-8428-4b7a-827c-d9c9ebf6140f`) at **Deep**, matching Console User and the house convention.
- **After**: read-back `privilegedepthmask = 4`. Repo role XML `Roles/Spaarke Basic User.xml` updated to match.
- **Still to do**: re-run `SignalReadRouteLiveTests` as the non-Console user after the 035 deploy, then close #1882.
  Other environments need the same grant when ontology is provisioned there.

## 13. 2026-10-15 — AppendTo on contact for Spaarke Ontology Service (owner-approved, 146 K-c)

- **Why**: on dev, every decision commit failed with `record_write_failed` (500): the writer lacked `AppendToAccess` on the
  person's `contact` when the Decision Record write set its contact lookups (146 live gate, K-c; seen for three different contacts).
- **Approval**: owner, 2026-10-15 (writer-role changes are outside D-110 and need the owner).
- **Change**: `AddPrivilegesRole` on the root copy of **Spaarke Ontology Service** `b1fb7ee0-bfbe-f111-aaaf-0022482913fc`
  (BU `06fbf21c-…`) at 2026-10-15T17:38:31Z (HTTP 204): `prvAppendToContact` (`158327b5-f4c1-448e-93d1-5f135126665b`) at
  **Global**, matching the role's other AppendTo privileges.
- **Read-back** (`RetrieveRolePrivilegesRole`, before and after): **50 → 51; added exactly** `prvAppendToContact|Global`;
  nothing removed, no depth changed. No other role touched. Repo `SpaarkeMaster/Roles/Spaarke Ontology Service.xml` updated.
- **Next**: re-run 146 items 1, 2, 3, 9 and 11 (commit-route Decision Records) on dev. Other environments need the same grant.

## 14. 2026-10-16 — Task 049 PREPARED, not applied (D-17, D-62, D-147; uac-r2 K-A)

Status: repo role XML and the live-apply script are in the task 049 PR; **nothing has been changed on spaarkedev1**. The main
session applies after uac-r2's check on the PR and appends the dated result below this section.

**The three removals (and nothing else; a fourth, Ontology Administrator Signal Write, was added by owner Q6 on 2026-10-17, see below):** Console User `prvWritesprk_Signal` (D-17), Console User
`prvCreatesprk_DecisionRecord` (D-62), Ontology Administrator `prvCreatesprk_DecisionRecord` (D-147), each on the **ROOT copy
of the role only: 3 `RemovePrivilegeRole` calls** (uac-r2 review F2/Q2, 2026-10-16). The five child-BU copies of each role are
inherited and read-only (Dataverse rejects an edit with `0x80044152` "Inherited roles cannot be modified",
`scripts/provisioning/Grant-ControlPlaneIdentity.ps1:451`), and `RetrieveRolePrivilegesRole` on a child reports the root's set.
Live `roleprivilegescollection` rows for `prvWritesprk_Signal` and `prvCreatesprk_DecisionRecord` exist only on root records
(2026-10-16). The script reads every inherited copy back and fails the run (`DIVERGENT`) if one differs from its root; it never
edits a copy. The two Create removals run before the Signal Write (uac-r2 K-A: Write on `sprk_signal` never goes without
Create on `sprk_decisionrecord` in the same change). K-A also covers a declined or failed call: Console User's Signal Write is
removed only if its Decision Record Create was removed in the same run or is already absent; otherwise the script skips it,
prints why and exits non-zero. Everything from the apply loop to the end of the run (removals, the AFTER read, member reads, the union check) sits in one `try/catch/finally`, so a run that fails there (for example a 503 after the removals) still writes its evidence file, listing the removals made before the failure, and exits 1; a failure in the BEFORE read happens before any write and leaves no file. The K-A check for the Signal Write is by read-back: it re-reads Console User's root and requires Create on `sprk_decisionrecord` to be absent (a 204 is not trusted). Repo: `Roles/Spaarke Console User.xml` (-2 lines) and
`Roles/Spaarke Ontology Administrator.xml` (-1 line). Source guard: `tests/Spaarke.ArchTests/SpaarkeRoleLedgerPrivilegeGuardTests.cs`.

**Client-write grep (POML constraint 2), 2026-10-16, branch `db1e7daf9` and `origin/master` `0ccc75b4c`:** no client write to
`sprk_signal` or `sprk_decisionrecord`. Searched `src/` (ts, tsx, js, jsx, html, ps1, bundles) for `sprk_signal(s)` and
`sprk_decisionrecord(s)`: every hit is a comment, a type or a display-name map (`WorklistWorkspaceWidget.tsx:10`,
`consoleStatus.ts:77,82`, `decisionDisplay.ts:161`, `Worklist/types.ts`, `worklistService.ts`, `WorklistWidget.tsx:7`,
`worklistWizardItem.ts:4`, `worklist.registration.ts:15`). No `Xrm.WebApi` update/create/delete, no `child-records` and no PATCH
reaches either table. The only Signal write route the Console calls is `POST /api/v1/signals/{id}/decision`
(`decisionWizardService.ts:7`, `DecisionCommitEndpoints.cs:31`). BFF side: every `sprk_signal` update is through the writer MI
(`OntologyWriterDataverseClient`: `DecisionCommitService.cs:2027`, `SignalSuppressionService.cs:1296,1305,1325`,
`SignalWriter.cs:650,907,1038,1079`); the only caller-identity access to `sprk_signals` is a read (`SignalCoreRecordAccess.cs:165`).
The commit route's caller preflight asks for the caller's Write on each Signal's **subject** and AppendTo on the core record
(`DecisionCommitService.cs:1462-1500`), never Write on the Signal. **No hit, so the escalation does not fire.**

**uac-r2 re-read (POML constraint 4), `origin/master` `0ccc75b4c` fetched 2026-10-16.** Last commit per file:
`SecureChildLineage.cs` `c1e16b784`; `config/secure-record-owner-role.json` `b2fc3fb78`; `RecordOwnershipResolver.cs` `ba9945149`;
`CoreAncestorResolver.cs` `6a00d1c47`; `RecordRouteAccessAuthorizationFilter.cs` `c5f71487f`; `CallerRecordAccessProbe.cs` `c5f71487f`;
`ExternalCallerContext.cs` `d254d7166`; `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` `720200e64`;
`.claude/adr/ADR-034-user-record-membership.md` `97d6e0be3`; `.claude/adr/ADR-003-authorization-seams.md` `8c5c517a1`;
`scripts/Set-SecureRecordOwnerRolePrivileges.ps1` `9fee1e8e2`. Against `7315b3e85` (the D-147 basis) the only change in any of
them is `720200e64`, ledger prose and line cites for the Do-lane "mine" read (D-141/D-145a). The owner-role config lists
**Reads only** for `sprk_signal` / `sprk_decisionrecord` (lines 233-245), so it names neither privilege being removed. No
`config/*.json` role config lists any of the three. Open uac-r2 PRs #2016 (unshare-owner) and #2017 (demo SPE grant marker)
touch neither. uac-r2's `current-task.md` lists "Task 049: confirm the Ontology Administrator change to ontology directly" as an
open owner item. **Nothing changed in a way that invalidates the plan.**

**Before (read-only, 2026-10-16, `-Verify`):** each of the 6 BU copies of Console User reports 23 privileges, of Ontology
Administrator 38, of Ontology Service 50, identical per role. `RetrieveRolePrivilegesRole` on an inherited copy reports the
root's set. What a child copy returns is call-dependent: `roleprivileges_association` on a child returns 0 (the rows live on the
root, which is the "0 on child copies" of §10.1 and §7.3), while `RetrieveRolePrivilegesRole` returns the root's set. The child
copies are inherited and cannot be edited, so the script plans and applies on the root only (3 calls, not 18) and checks the
other five copies of each role by read-back. That `DIVERGENT` check is therefore defence in depth, not independent evidence (the
call reads through to the root); the live union check (A1 to A5) is the real guard. Union:

| Privilege | Holders at preparation | After the removals (incl. Q6) |
|---|---|---|
| Write on `sprk_decisionrecord` | System Administrator, System Customizer, Service Writer | unchanged |
| Delete on `sprk_decisionrecord` | System Administrator, System Customizer, Service Deleter | unchanged |
| Create on `sprk_decisionrecord` | System Administrator, System Customizer, Service Writer, **Console User (Deep)**, **Ontology Administrator (Deep)**, Ontology Service | System Administrator, System Customizer, Service Writer, Ontology Service |
| Write on `sprk_signal` | System Administrator, System Customizer, Service Writer, **Console User (Deep)**, **Ontology Administrator (Deep)**, Ontology Service | System Administrator, System Customizer, Service Writer, Ontology Service |

**Ontology Administrator Signal Write: removed live 2026-10-17 (owner Q6), see §17.** The owner approved removing
`prvWritesprk_Signal` (Deep) from Spaarke Ontology Administrator; it was removed from the ROOT role by a one-off
`RemovePrivilegeRole` (privilege `8df17c8f-8796-4e7c-9d32-3c0d7c9cd082`), and the script now carries it as a fourth closed-list
removal (decision `Q6`, K-A gated on that role's Decision Record Create being read back absent). Union assertion A5 therefore has
no waiver (`$A5Waiver = $null`): only platform roles, admin roles (System Administrator, System Customizer) and Spaarke Ontology
Service may hold Write on `sprk_signal`, and any other holder fails the run.

**Evidence and commands.** `049-role-removal-evidence-whatif-*.md` (plan, projected union) and `-verify-*.md` (live before
picture) in this folder carry the full `RetrieveRolePrivilegesRole` listing per copy. Script:
`scripts/Remove-ConsoleSignalWriteAndDecisionRecordCreate.ps1` (`-WhatIf`, `-Verify`, `-Apply`).
**Rollback (not run; exact commands).** Each removal is restored with one `AddPrivilegesRole` on the ROOT role, at the original
depth (Deep, `privilegedepthmask` 4), taking the ids from the 2026-10-16 live reads. Root BU `06fbf21c-1872-f011-b4cb-7c1e52671ad0`.

| Removal | Root role id | Privilege | Privilege id | Depth |
|---|---|---|---|---|
| D-17 | Console User `51c924e2-bdbe-f111-a05b-3833c5e9614d` | `prvWritesprk_Signal` | `8df17c8f-8796-4e7c-9d32-3c0d7c9cd082` | Deep |
| D-62 | Console User `51c924e2-bdbe-f111-a05b-3833c5e9614d` | `prvCreatesprk_DecisionRecord` | `38366142-e133-4978-9a46-e6d1d598ce1f` | Deep |
| D-147 | Ontology Administrator `2f2b2137-bfbe-f111-aaaf-0022482913fc` | `prvCreatesprk_DecisionRecord` | `38366142-e133-4978-9a46-e6d1d598ce1f` | Deep |
| Q6 | Ontology Administrator `2f2b2137-bfbe-f111-aaaf-0022482913fc` | `prvWritesprk_Signal` | `8df17c8f-8796-4e7c-9d32-3c0d7c9cd082` | Deep |

```powershell
$base = 'https://spaarkedev1.crm.dynamics.com'
$t = az account get-access-token --resource $base --query accessToken -o tsv
$h = @{ Authorization = "Bearer $t"; Accept = 'application/json'; 'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0'; 'Content-Type' = 'application/json; charset=utf-8' }
$bu = '06fbf21c-1872-f011-b4cb-7c1e52671ad0'
function Restore($roleId, $privName, $privId) {
  $body = @{ Privileges = @(@{ '@odata.type' = 'Microsoft.Dynamics.CRM.RolePrivilege'; Depth = 'Deep'; PrivilegeId = $privId; PrivilegeName = $privName; BusinessUnitId = $bu }) } | ConvertTo-Json -Depth 5
  Invoke-RestMethod -Uri "$base/api/data/v9.2/roles($roleId)/Microsoft.Dynamics.CRM.AddPrivilegesRole" -Headers $h -Method Post -Body ([Text.Encoding]::UTF8.GetBytes($body)) | Out-Null
}
# Restore D-17 (Signal Write) before D-62 (DR Create): the reverse of removal (K-A).
Restore '51c924e2-bdbe-f111-a05b-3833c5e9614d' 'prvWritesprk_Signal'            '8df17c8f-8796-4e7c-9d32-3c0d7c9cd082'   # D-17
Restore '51c924e2-bdbe-f111-a05b-3833c5e9614d' 'prvCreatesprk_DecisionRecord'   '38366142-e133-4978-9a46-e6d1d598ce1f'   # D-62
Restore '2f2b2137-bfbe-f111-aaaf-0022482913fc' 'prvCreatesprk_DecisionRecord'   '38366142-e133-4978-9a46-e6d1d598ce1f'   # D-147
Restore '2f2b2137-bfbe-f111-aaaf-0022482913fc' 'prvWritesprk_Signal'            '8df17c8f-8796-4e7c-9d32-3c0d7c9cd082'   # Q6 (after D-147, so K-A holds)
```

After restoring:
1. `RetrieveRolePrivilegesRole` on both roots shows **23** (Console User) and **38** (Ontology Administrator) privileges, and the
   diff against the BEFORE listing in `049-role-removal-evidence-whatif-*.md` / `-verify-*.md` is empty (same names, same depths).
2. `pwsh -File scripts/Remove-ConsoleSignalWriteAndDecisionRecordCreate.ps1 -Verify` shows the inherited copies matching their
   roots again and the before-removal state (A2, A3, A5 fail).
3. Revert the repo role XML as well (`Roles/Spaarke Console User.xml`, `Roles/Spaarke Ontology Administrator.xml`), or the next
   SpaarkeMaster import removes the privileges again; the ArchTest `SpaarkeRoleLedgerPrivilegeGuardTests` then fails until the
   guard is changed with the decision.
4. `AddPrivilegesRole` may also add the four SharePoint privileges it is known to re-inject (setup guide §5.4; see the
   Secure Record Owner strip above); that is a no-op here because both roles already hold all four at Global (BEFORE evidence). The 23 / 38 counts in step 1 confirm it.

Still to do by the main session after uac-r2's check: `-Apply`, `-Verify`, the live negatives (non-admin update of a
`sprk_signal` row, and create of a `sprk_decisionrecord`, both refused: task 008 §10.4 method), and one decision recorded in
the deployed dev Console.

## 15. 2026-10-17 — AppendTo on the Signal/Decision Record lookup targets for Spaarke Ontology Service (owner-approved; ebilling 026 F1, DI-107 #2046)

- **Why**: the writer (`mi-ontology-writer-dev`, systemuser `3121bf1b-…`) sets lookups on `sprk_signal` / `sprk_decisionrecord` to tables
  its own role held no AppendTo on. `sprk_billingevent` (ebilling 026's invoice-line subject) had no route at all, so every line-subject
  Signal or Decision Record would fail 0x80040220. Matter, project, invoice, record-type catalog and the rest worked only through the root
  team "Spaarke" Basic User role (#2046).
- **Approval**: owner, 2026-10-16 ("yes grant 'append to'; also for other records").
- **Change**: `AddPrivilegesRole` on the root **Spaarke Ontology Service** `b1fb7ee0-bfbe-f111-aaaf-0022482913fc` (BU `06fbf21c-…`),
  all at **Global**: `prvAppendTosprk_AnalysisAction`, `prvAppendTosprk_BillingEvent`, `prvAppendTosprk_Document`,
  `prvAppendTosprk_Invoice`, `prvAppendTosprk_Matter`, `prvAppendTosprk_Project`, `prvAppendTosprk_RecordType_Ref`,
  `prvAppendTosprk_ServiceRequest`. The set is every `ReferencedEntity` of a ManyToOne on the two tables that the role lacked, apart from
  the ownership lookups (businessunit, owner, team).
- **Read-back**: 50 → 58, all 8 at Global, nothing unrequested, nothing removed. Child BU copy `0d5642b7-…` holds 58. The writer
  resolves `prvAppendTosprk_BillingEvent`, `_Matter`, `_RecordType_Ref` at Global via BU `06fbf21c-…`. uac-r2 notified before and after.
- **Repo**: `SpaarkeMaster/Roles/Spaarke Ontology Service.xml` + a guard test follow in an ontology PR. Other environments need the same grant.

## 16. 2026-10-17 — Append on `sprk_policy` / `sprk_policyversion` for Spaarke Ontology Administrator (owner-approved, D-150)

- **Why**: saving or publishing a rule links rule rows (Append on the referencing row). The role had AppendTo but not Append, so only System Administrators could author.
- **Change**: `AddPrivilegesRole` on the root `2f2b2137-bfbe-f111-aaaf-0022482913fc`: `prvAppendsprk_Policy`, `prvAppendsprk_PolicyVersion` at **Deep**.
- **Read-back**: 38 → 40 on the root and all 5 copies (`4f6a91ff`, `2c4541b2`, `675098f9`, `efca5b32`, `d785bc47`), both Deep; nothing unrequested. uac-r2 notified before and after. Repo XML already on master.

## 17. 2026-10-17 — Task 049 APPLIED (D-17, D-62, D-147, D-159)

- **03:17Z `-Apply`** (evidence `049-role-removal-evidence-apply-20261017T031742Z.md`): root-only `RemovePrivilegeRole` in K-A order — Ontology Administrator `prvCreatesprk_DecisionRecord` (40 → 39); Console User `prvCreatesprk_DecisionRecord`, then `prvWritesprk_Signal` (23 → 21). All 6 copies of each role read back with no divergence; Ontology Service unchanged (58). A1–A5 PASS.
- **Owner Q6, same window**: root-only `RemovePrivilegeRole` of `prvWritesprk_Signal` (Deep) on Ontology Administrator; all 6 copies 39 → 38, nothing else changed.
- **03:24Z `-Verify` with `$A5Waiver = $null`** (PR #2050 branch): PASS; no non-platform, non-admin, non-writer role holds Write on `sprk_signal`.
- **Still held — KEPT by owner decision D-160 (2026-10-17, "not a risk; we can remove later")**: Ontology Administrator keeps `prvDeletesprk_Signal` and `prvAssignsprk_Signal` (Deep). No shipped flow deletes or assigns a Signal as the caller (2026-10-17 search; test cleanups run as the operator or the writer). Console User holds neither. No Spaarke human role holds Write, Delete or Assign on `sprk_decisionrecord`.
- **Rollback**: §14.

## 18. 2026-10-23 — AppendTo on `sprk_eventtype_ref` for Spaarke Core User (D-110, task 159)

- **Why**: task 159's Assign Task creates a `sprk_event` with `sprk_eventtype_ref` = Task (`124f5fc9-…`) as the caller. Binding a lookup needs AppendTo on its target. `sprk_eventtype_ref` is user-owned, and no user role held `prvAppendTosprk_EventType_Ref`; only Service Writer (Global), Spaarke Office Add In User (Deep), System Administrator and System Customizer did (live read, 2026-10-23). A Core User's Assign Task would therefore be refused.
- **Change** (2026-10-23T00:32:20Z, D-110 standing approval for Core User): `AddPrivilegesRole` on the root Spaarke Core User `75e1b627-2ab6-f111-aaad-0022482913fc`: `prvAppendTosprk_EventType_Ref` at **Global**. Global because the reference rows are owned in the root BU and users sit in child and secure BUs; it matches Service Writer and Basic User's Global AppendTo on `sprk_recordtype_ref`.
- **Read-back** (`RetrieveRolePrivilegesRole` before and after): 784 → 785, added exactly that privilege at Global; nothing removed and nothing else added. Repo `Roles/Spaarke Core User.xml` is updated in task 159's PR #2289. Other environments need the same grant.

## 19. 2026-10-23 — Read on `sprk_policy` / `sprk_policyversion` dropped from Spaarke Basic User and Spaarke Console User: PREPARED in the repo, NOT applied live (D-185, #2285, task 163)

- **Why**: every internal user could read billing policies directly in Dataverse, bypassing the BFF's Procedure Read gate. The worklist no longer needs the Reads: it takes the policy short name, work type and severity from a BFF read of `sprk_policy` by id (task 163), and its configuration row has no `policy` link.
- **Repo change** (`Roles/*.xml`): Spaarke Basic User loses `prvReadsprk_Policy` (Deep); Spaarke Console User loses `prvReadsprk_Policy` and `prvReadsprk_PolicyVersion` (Deep). Spaarke Ontology Administrator and Spaarke Ontology Service keep theirs. Nothing else changes in the two role files (Basic User and Console User).
- **Live order, per environment**: (1) deploy the BFF with task 163, (2) apply `config/worklist.gridconfiguration.json`, (3) only then remove the three privileges from the live roles with `RemovePrivilegesRole`, and read back the role privilege counts. Before step 2 the old configuration still links `sprk_policy`, and a caller without Read gets an empty worklist (Dataverse 0x80040220, treated as no read on `sprk_signal`). This supersedes section 12's "Read on `sprk_policy` for Basic User".
- **Drift until the live change**: `check-dev-drift.py` and `Export-SpaarkeMasterSource.ps1` see dev still holding the Reads the repo has dropped, and a fresh export would put them back. Make the dev role change promptly after the config apply, and nobody exports the roles from dev before it.
- **Applied**: not yet. The main session records the live change and read-back here.
- **Found 2026-10-23 (not ours):** the live root-BU Spaarke Basic User (`11f93c04-ddf6-f011-8406-7c1e520aa4df`) already lacks `prvReadsprk_Policy` (granted Deep in §12) and `prvReadsprk_ruleset`. ebilling saw it in its task 120 `-WhatIf` (669 live vs 671 in source); confirmed by a `roleprivileges` query (only `prvReadsprk_Signal` of the four checked). Nobody in this session ran a removal, and `audit` holds no rows for the role. Asked ebilling whether its session removed them. Effect until the 163 BFF and the config row land on dev: a Basic + Core user without Console User gets an empty worklist (the old row links `sprk_policy` as the caller); Console User holders are unaffected. Not re-granted: the D-185 end state is removal and the BFF deploy is next. When applying this section, Basic User needs no change; Console User still does. **Pinned (uac-r2, 2026-10-23):** both Reads were present at 2026-10-22 01:11Z (uac-r2 T191a snapshot, 671 rows); the role's `modifiedon` is 2026-10-23 **00:01:07Z** (the MCP returns EDT; calibrated against ebilling's new role), by the owner's account, which every CLI session also uses. Ruled out by their own transcripts: ontology (this session and its agents), ebilling, R3 (it ran only `Deploy-BffApi.ps1` then), uac-r2; no `importjob` or SpaarkeMaster import. Asked field-mapping, customer-provisioning, word-add-in and the owner (maker-portal edit).

