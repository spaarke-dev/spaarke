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
3. Enable **auditing** on `sprk_decisionrecord` and `sprk_policyversion` (Update + Delete) per §1's note.
4. Assign **`Spaarke Ontology Service`** to the BFF's Dataverse Application User.
5. Assign **`Spaarke Console User`** to yourself plus any test users, *alongside* their existing roles.
6. **Verify the append-only guarantee holds** rather than assuming it: as a non-admin test user, try to update a
   `sprk_decisionrecord` row and confirm it is refused. This is the one check worth doing by hand, because it is
   the property everything else rests on — and the `prvWrite` could be granted by any role the user happens to
   hold.
