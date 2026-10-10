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
