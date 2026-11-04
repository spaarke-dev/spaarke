# Field Mapping → Server-Side Write Path — R1 — Design

> **Status**: REWRITTEN 2026-10-19 after a full re-verification against master (`49279def1`). All owner decisions are
> settled (§7). Next step: `/design-to-spec` → `/project-pipeline`.
> **History**: drafted 2026-09-25 · reviewed 2026-09-29 (Q1–Q5) · **rewritten 2026-10-19**. The goal and the
> ADR-002 framing are unchanged. The starting point, architecture and workstreams were rewritten because two weeks of
> merged work (word-add-in-r1 complete, UAC-r2 tasks 146/147/156, ontology-r1 D-66, ADR-002 amendment) built much of
> what the 09-29 draft planned to build.
> **Evidence**: [`notes/design-review-2026-10-19.md`](notes/design-review-2026-10-19.md) holds the file:line facts
> behind every current-state claim here. The spec cites it rather than re-deriving them.
> **Worktree**: `C:\code_files\spaarke-wt-field-mapping-server-write-path-r1`, branch `work/field-mapping-server-write-path-r1`.
> **Origin**: the ADR-002 review of 2026-09-25. Decision record: [`docs/adr/ADR-002-no-heavy-plugins.md`](../../docs/adr/ADR-002-no-heavy-plugins.md)
> (amended 2026-10-03). Architecture: [`docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md`](../../docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md).
> **Invariant-registry rows owned by this project**: **I-3** (creation-time field mapping) and **I-5** (search-index
> default). It also unifies the create path that **I-1, I-2, I-6, I-11, I-12, I-16** are applied through, and it
> publishes the per-table ownership standard that I-6 lacks.

---

## 0. Thesis

The goal is unchanged: **field mapping is applied by one engine, on the server, inside the create request, for
every product create path**, so mapped fields are present when the new record's page loads.

What changed is the starting point:

1. **The BFF create pipeline now exists, but twice, and neither copy maps fields for everyone.**
   - `OwnedChildWrite` (UAC-r2 task 147, behind `POST /api/v1/child-records/{table}`) has the strong caller-authorization
     model and serves 6 of 8 wizards, AI chat and decision follow-ons. **It applies no field mapping.**
   - `RecordCreationService` (word-add-in-r1) applies field mapping and the search-index default, but only for Office
     Matter/Project quick-create. Its authorization is weaker (#1614), and it gives a different owner for the same
     record.
   - Eight more server create paths each apply their own subset of the invariants.
2. **Three field-mapping engines still exist, and the client engine has no preview role.** All 9 client call sites run
   at create time, and the client engine **overwrites what the user entered**.
3. **Matter and Project wizards still create in the browser, as the user.** Those rows are **user-owned**, outside the
   ownership standard, and nothing reassigns them.

This project therefore:
- **unifies the two cores into one `RecordCreationService`**, built on `OwnedChildWrite`'s authorization;
- puts **field mapping (fill-only) and the search-index default** inside it;
- moves **every product creator** onto it, including the Matter/Project wizards;
- **deletes the client engine**, runs `/push` on the same engine (Copy only);
- **repairs records created outside the product** with a scheduled pass that runs the same code;
- publishes **a per-table ownership standard** with guards.

**This project is NOT:**
- a Dataverse plugin (ADR-002);
- a redesign of the field-mapping configuration model: the two tables stay; their forms stay; dormant columns are
  documented, not removed;
- a change to update-time semantics beyond running `/push` on the canonical Copy logic;
- a redesign of wizard UI;
- a migration of external-portal creates (external-access-r3 owns them; §4.6).

---

## 1. Background: the rules this implements

The ADR-002 review (2026-09-25) rejected plugins for Spaarke and adopted the **Server-Side Write-Path rule**, amended
2026-10-03:

| Rule | Meaning here |
|---|---|
| **WP-1** | Each invariant has one owner: a BFF server component, or a platform-native declarative mechanism applied on every write whoever writes (autonumber, alternate key) with a scripted `-Verify`. Plugins, flows, webhooks, business rules and **client code never own an invariant**. |
| **WP-2** | The client may preview; it is never the only enforcement. |
| **WP-3** | Tables that carry invariants are written **through the BFF**, not `Xrm.WebApi`. |
| **WP-4** | On-load UX invariants are applied **inline**. Field mapping is one: "users look for the field updates as soon as the page loads". |
| **WP-5** | Writes from outside the product are corrected asynchronously, idempotent, fill-only. |
| **WP-6** | Security fails closed. |
| **WP-7** | Bulk import goes through the BFF batch path or a raw load plus a server-side reconciliation pass. |
| **WP-8** | Customer forms and flows use BFF endpoints; customer plugins re-implementing invariants are unsupported. |

**Dataverse OOB relationship mappings were considered and are not the engine.**
- They pre-fill a model-driven form only when a child is created from the parent's form context.
- They do not run on Web API/SDK creates (every path in this design), so under WP-1 they cannot own I-3.
- They are Copy-only and same-type, and don't apply to records filed through the regarding pair.
- They live in solution metadata, not admin-editable data.

They are excluded (§3, option F). The one existing map (`businessunit` → `sprk_matter.sprk_ai_search_index`) is removed
because the server owns I-5.

---

## 2. Current state (verified 2026-10-19; details and file:line in the evidence notes)

### 2.1 Three engines, none of them a preview

| Engine | Where | Semantics | Used by |
|---|---|---|---|
| **A. Client TS** | `Spaarke.UI.Components/src/services/FieldMappingService.ts` | Copy/Default/Concat/Template. Runs inside each wizard's create function, **after** the payload is built, so mapping overwrites user input. **No preview anywhere.** | 8 wizards, 9 call sites |
| **B. BFF push** | `Api/FieldMappings/FieldMappingEndpoints.cs` `ApplyMappingRule` | Copy only (other types ignored). It is also a re-file writer (`ReparentAsync` + `SecureChildReconciler.AfterRefileAsync`). | Matter/Project ribbon "Update related" |
| **C. BFF create-time** | `Services/Office/CreateTimeFieldMapping.cs` | All four types; typed defaults; labels/ISO/Yes-No text rendering; null source skipped; protected-column guard. **No unit tests.** | Office quick-create Matter/Project only |

A and C disagree on null handling, option-set and date text, typed defaults, and Concat/Template rendering. No parity
test exists.

**Live consequence**: the Report Card wizard lets the user pick Law Firm 1 / Assigned External / Assigned Internal, the
exact columns the Matter→Report Card profile maps. The user's pick is silently replaced by the matter's value.

### 2.2 Two create cores and eight bespoke paths

| | `RecordCreationService` | `OwnedChildWrite` |
|---|---|---|
| Serves | Office quick-create Matter/Project | child-records route (11 tables incl. the Work Assignment root), AI chat, email draft, budget inquiry, decisions |
| Authorization | route filter: source Read + Create privilege; **no Append/AppendTo** on what it writes (#1614); no field-security check | as caller: Create/Append privilege, field-security refusal, AppendTo on every lookup and the regarding pair, uniform 404 |
| Field mapping, search-index default, autonumber read-back | yes | **no** |
| Owner | caller's BU team | parent's BU team first (record-first) |
| I-1 stamp, I-16, sharee mirror | n/a | in the route **after** create; **skipped by the chat tool** |
| Shape | sealed scoped class, typed request, per-entity methods | `internal static`, raw payload, mixes create + re-file + authz |

**Outside both cores**:
- Office Invoice quick-create, Office To Do, Office document save;
- `POST /api/v1/events`, `POST /api/ai/analysis/create`;
- invoice extraction (`InvoiceReviewService`);
- system creators: To Do generation, playbook create-task, Signal and Decision writers, inbound communication and
  document ingestion.

They use three different authorization models, and none maps fields.

### 2.3 Create-surface inventory (summary; full table in the evidence notes §5)

| Tier | Surfaces | Writer today |
|---|---|---|
| **1. Product, through the child-records core** | Wizards: Event, To Do, Invoice, Report Card, Work Assignment, Analysis, Document Upload; SmartTodo; Daily Briefing inline To Do; EventDetailSidePane To Do/memo; Notepad memo; secure-child ribbon "New" (budget/KPI/billing event); AI chat create; email draft; decision follow-ons | `OwnedChildWrite`; mapping client-side (wizards) or none |
| **2. Product, in the browser as the user** | **Create Matter wizard, Create Project wizard** | `Xrm.WebApi`, user-owned, client mapping |
| **3. Product, bespoke server** | Office quick-create (Matter/Project via `RecordCreationService`; Invoice inline), Office To Do, Office save, Summarize Files `POST /api/v1/documents`, Events and Analysis routes, invoice extraction, system creators | own code per path |
| **4. Native Dataverse UI** | "+ New"/form create on hosts without a ribbon override; quick-create forms (Event "Add Memo", "Add KPI"; solution XML still enables quick create on Project, Matter, Invoice Line Item; **verify live**); OOB **Assign** | platform, as user |
| **5. Outside the product** | Excel/Data Import, dataflows, migrations, customer Power Automate and integrations, Copilot / Dataverse MCP agents | platform |
| **Out of scope** | External portal (`ExternalDataService`): service requests, events, To Dos, documents | external-access-r3 |

### 2.4 Ownership

- **The standard exists, but only as a rule.** D-11 / I-6: the default Owner team of the record's own BU
  (record-first), else the acting user's BU, else refuse; a secure ancestor → the named Secure Record Owners team.
  There is **no per-table statement** of who owns what, and the census test covers only server child-table writers.
- **How a user-owned row is seen.** A row is placed in its owner's business unit, and readers reach it per their role's
  depth.
  - Spaarke Basic User and Core User hold **Read at Parent:Child (Deep)** on the main tables, so a row owned by user U
    is visible to everyone in U's BU and the BUs above it, not only to U.
  - The harm of user ownership is therefore:
    - the record sits in the creator's BU rather than its parent's, so the parent's readers can miss it;
    - on tables where a role has **User depth** (Basic User on `sprk_communication`, AI Analysis User on analysis),
      only the owner sees it;
    - when the user moves BU or leaves, their records move or strand.
- **Gaps.**
  - **Wizard Matter/Project rows are user-owned**: never assessed, never reassigned (the ownership backfill touches
    app-owned rows only).
  - **OOB Assign** ("Assign → Me / User or team"; Core User holds `prvAssign` Deep on Matter) changes `ownerid` and
    moves the record's BU. That splits record families across BUs and can pull a secure record out of isolation.
    Spaarke expresses "who works on this" with the Assigned To lookups, not `ownerid`, so Assign meets no business need.
- **Accepted exceptions**: notifications; private direct threads; unfiled communications (creator keeps; accepted
  2026-10-03). **Not yet assessed**: the notification outbox; one `DecisionRecordWriter` case.

### 2.5 Configuration

- **Tables and forms stay.** Profiles and rules are authored on native forms (`FieldMappingAdmin` PCF retired
  2026-10-15).
- **Seeded rules**: three Matter→child "attorney matrix" profiles (Event, Invoice, Report Card), all Copy
  lookup→lookup.
- **Drift and dead columns**:
  - the profile column is `sprk_capabilitymode` in SpaarkeMaster but `sprk_compatibilitymode` in the older export;
  - rule columns `sprk_syncmode` (One-time / Manual Refresh) and `sprk_mappingdirection` are read by nothing;
  - `IsRequired`, `CompatibilityMode` and `IsCascadingSource` are read but unused.
- The seeder's rule lookup fails with HTTP 400 (#1318).
- No rule validation stops a mapping from targeting the regarding pair (#1617).

### 2.6 `/push`

- **Real use**: the Matter/Project ribbon button that re-copies the attorney matrix onto existing events, invoices
  and report cards.
- **Open issues**: #2098 (a 500-child push can exceed the 230 s limit), #1437 (raw HTTP text in errors), #2056
  (invoice-family guard), #1617.

### 2.7 Records created outside the product

- **Repaired today**: the I-1 ancestor stamp (5-minute reconciliation job), and I-2 under secure parents only.
- **Never repaired**: I-3 mapping, I-5 search-index default, and I-6 owner on non-secure rows.
- **No Dataverse change-signal consumer exists.**
- **Users must keep the Create privilege** (the server checks it as the caller), so native creates can be discouraged
  but never fully blocked. A repair pass is required whatever else is done.

### 2.8 Dead code

- `bffDataServiceAdapter.ts`: no production importer; its routes don't exist.
- `solutions/LegalWorkspace/.../CreateMatter/matterService.ts`: no importer of the class.
- `FieldMappingHandler.ts`: legacy AssociationResolver integration, no consumer.

---

## 3. Options considered

| # | Option | Verdict | Why |
|---|---|---|---|
| A | Dataverse plugin applying mappings on Create | ❌ Rejected (ADR-002) | .NET Framework runtime, per-row import cost, ALM, creep. Owner 2026-09-25. |
| B | Async fix-up only | ❌ as primary | Page loads empty; races user edits. **Kept** as the W5 repair pass. |
| C | Server "compute" endpoint, client still writes | ❌ | Client stays the writer (WP-2/WP-3); non-wizard paths uncovered. |
| D | Status quo + align engines | ❌ | Client-only enforcement, three implementations. |
| E | Server-side write path, mapping inline in the create | ✅ Goal (unchanged) | WP-1…WP-4; every surface; fields present on load. |
| E1 | **…on one `RecordCreationService` built on `OwnedChildWrite`'s authorization** | ✅ **Chosen shape** (owner Q8) | Keeps the strongest, adversarially-tested authz (§11: extend); already serves roots and children, chat and decisions; one owner rule; one place per invariant. |
| E2 | …on `RecordCreationService` absorbing `OwnedChildWrite` | ❌ | Would import all of `OwnedChildWrite`'s authz anyway; per-entity hand-written shape; caller-only owner rule. |
| E3 | …on a new combined service | ❌ | Rebuilds proven authz code; highest migration risk. |
| F | Dataverse OOB relationship mappings | ❌ as engine | Form prefill only; not applied on API creates; can't own I-3 under WP-1; Copy-only; solution metadata. |
| G | Dataverse change-signal channel for non-product writes | ⏸ Deferred (owner Q5) | Needs the repair pass anyway for missed messages; new infra per environment. Add only if a named source needs seconds rather than minutes. |

---

## 4. Solution

### 4.1 Target architecture

```
Wizards · Office adapter · chat/decision tools · system creators · repair pass
   │  table + payload (caller-entered values only) + optional mapping source + surface + principal
   ▼
RecordCreationService  (one scoped service; Services/RecordCreation/)
   Create mode — one ordered pipeline, one place per rule (WP-1):
    1  identify principal: caller (WhoAmI, 403 if none) or system (app, RequestedBy optional)
    2  table rules lookup; refuse blocked columns in the CALLER's payload
    3  field mapping (I-3): resolve source (explicit, else the filed-under parent) → Read check on source AS CALLER
       → profile + source read → canonical engine, FILL-ONLY (caller-supplied values win; protected columns skipped)
    4  map the merged payload (DataverseWriteItemMapper)
    5  caller checks on the MERGED payload: Create/Append, field security, AppendTo on every lookup + regarding pair,
       uniform 404  ← mapped lookups get the same checks as typed ones (closes #1614, #1617)
    6  secure-root plan (inheriting roots)
    7  owner (I-6/I-2): ResolveOwnerAsync, record-first for roots and children alike
    8  search-index default (I-5) from the OWNING BU, fill-only
    9  creator person; "for" person / table defaults (e.g. Assigned To default) from table rules
   10  I-1 ancestor stamp + I-16 derived INTO the payload
   11  one app-only create (alternate-key retry for autonumber tables, I-11)
   12  post-create, inside the service, never throwing: number read-back, isolated-root completion,
       sharee mirror, assigned-access grants (I-12), membership event
   Repair mode — the same fill-only steps (3, 7, 8, 10) against an EXISTING row; idempotent
   ◄── id + applied values + warnings | typed refusal (kind, code) → one shared HTTP map
Re-file stays a separate operation (OwnedChildWrite.RefileAsync), unchanged in behaviour.
```

- **Per-table rules**: one declarative registry holds, per table: root/child, protected columns, autonumber column,
  "for"-person column, server defaults, and whether mapping applies.
  - It replaces `MatterProtectedAttributes`/`ProjectProtectedAttributes`, `ServerOwnedColumns`,
    `RootServerOwnedColumns` and `ForPersonColumns`.
- **Caller authorization**: `OwnedChildWrite`'s helpers (`CheckCallerMayCreateAsync`, AppendTo, pair resolution,
  WhoAmI) move unchanged to `Services/Dataverse/CallerWriteAuthorization.cs`.
- **System principal**: system creators (generation jobs, playbook nodes, ingestion, the repair pass) have no caller.
  They run steps 2–12 without the as-caller checks, with `RequestedBy` when known. Same rules, same owner; explicit and
  tested.
- **Adapters**:
  - **Office quick-create** maps `QuickCreateRequest` → service and keeps its Office error codes.
  - **The child-records route** becomes the browser entry point for every table, Matter and Project included.
  - **Chat and decision tools** call the service, which gives them the stamp/I-16/mirror steps they skip today.
- **DI**: one `AddScoped` registration replaces `OfficeModule.cs:73`, so the net registration count is unchanged
  (ADR-010). The mapping engine stays `internal static`.

### 4.2 Field-mapping semantics

- **One engine**: `CreateTimeFieldMapping` is promoted to `Services/Dataverse/FieldMapping/` and is the only
  implementation. The C# semantics are canonical:
  - typed Default conversion;
  - text rendering of option-set labels, ISO-8601 dates and Yes/No;
  - a null source is skipped;
  - the protected-column guard.

  The spec records each as a written decision with a test.
- **Fill-only at create** (owner Q6):
  - a mapped value is written only when the caller left the field **empty**. Absent or null counts as "not
    supplied"; an empty string is normalised to null.
  - To blank an inherited field, the user edits it after creation.
  - **Wizards send only values the user entered**, so client-side defaults must not block inheritance. Defaults that
    matter move into the server table rules.
- **Mapping source** (owner Q7): explicit when the caller names one; otherwise **inferred from the filed-under parent**
  (the same parent lookups the owner rule reads).
  - Every creator on the service therefore gets mapping without per-surface plumbing: wizards, chat, decisions, side
    panes, inline To Do, system creators.
  - With more than one candidate parent, the spec defines precedence (the regarding pair first, then the table's
    typed parent lookup).
- **Push = the deliberate overwrite** (owner Q3, revised): `/push` applies **Copy rules only**, using the canonical
  engine's Copy logic, and only rules with **`sprk_syncmode` = Manual Refresh**.
  - Existing Copy rules are migrated to Manual Refresh so today's push behaviour is preserved.
  - The story for makers: *at creation a child inherits what the user left empty; "Update related" re-copies the
    parent's values over existing children.*
- **Configuration**: the existing tables and native forms are the configuration surface (owner Q1a). Cleanup:
  - reconcile the profile mode column name;
  - document the dormant columns (direction, is-required, cascading-source, compatibility) as unused;
  - fix the seeder (#1318);
  - **refuse rules that target the regarding pair or a protected column**: skipped with a warning at run time, and
    flagged by an admin-guide check.
- **Analysis → email draft**: the Analysis wizard's analysis→communication mapping, which fills the email draft's
  subject and body, is applied **server-side in the send path, fill-only**. A user-typed subject and body win.
- **No preview** (Q4 superseded): the client engine is deleted. If a wizard later wants to show inherited values, that
  is a read-only display concern, not enforcement.

### 4.3 Authorization, identity and the ownership standard

- **Identity (Q2, unchanged)**: creates run app-only under the BFF managed identity. The caller's rights are checked
  explicitly before the create (§4.1 steps 3 and 5). `sprk_createdbyperson` records the acting user, which answers the
  09-29 audit question.
- **Owner rule (owner Q9)**: **record-first everywhere**, for roots and children:
  1. a secure ancestor → the named Secure Record Owners team;
  2. else the default Owner team of the filed-under record's BU;
  3. else the acting user's BU;
  4. else refuse. Never app-owned; never user-owned except the listed exceptions.

  This changes Office quick-create for a Project filed under a Matter (caller's BU → Matter's BU).
  `ResolveOwningTeamAsync` is retired for creates.
- **Per-table ownership standard (new)**: this project writes a per-table matrix next to I-6 in
  `DATAVERSE-WRITE-PATH-ARCHITECTURE.md`. For every invariant-bearing table it gives:
  - the owner rule;
  - the role depths that reach it (Basic User / Core User / others);
  - each exception, with its access rationale.

  The unassessed exceptions (notification outbox, `DecisionRecordWriter` communication-owner case) are assessed and
  recorded. **The census guard is extended** to root creates and to any client `Xrm.WebApi.createRecord` on a table in
  the matrix.
- **OOB Assign (owner decision)**:
  - **Hide** the Assign command on standard-owned tables.
  - The **repair pass restores** standard ownership if a row is reassigned anyway.
  - **Removing `prvAssign` from Spaarke roles** follows an audit of legitimate uses, coordinated with UAC-r2. It is a
    follow-up item, not silently done here.
  - Ownership changes happen only through BFF re-file.

### 4.4 Records created outside the product (W5; owner Q5 revised)

**A. Redirect the native UI**:
- Ribbon "New" on every mapping-target table opens the wizard (extending the secure-child ribbon override pattern).
- Quick create is turned off on those tables (Event "Add Memo" and "Add KPI" go through the BFF instead).
- The live quick-create flags are verified per table.

**B. Scheduled repair pass**:
- An `IScheduledJob` (ADR-036, `ScheduledJobHost` lease) runs every ~5 minutes over mapping-target and standard-owned
  tables.
- It reads rows created since its watermark whose **platform `createdby` ≠ the BFF application user**. The spec
  verifies this marker; `sprk_createdbyperson` is ruled out (evidence §5).
- It runs the service's **repair mode** (mapping, owner, search-index default, stamp), **fill-only and idempotent**.
- Admins can trigger it on demand for a table and time window after an import (the WP-7 reconciliation pass).
- Re-owning non-secure rows widens the current re-owners, so it is coordinated with UAC-r2.

**C. Change-signal channel**: not built. It is reconsidered only if a named source needs seconds rather than minutes.

### 4.5 Workstreams

| WS | Scope | Notes |
|---|---|---|
| **W1 Canonical engine and configuration** | Promote `CreateTimeFieldMapping` to `Services/Dataverse/FieldMapping/`; record the semantic decisions; fill-only applier; mapping-source resolver; unit tests; Copy-only push entry; configuration cleanup (column-name drift, dormant columns documented, #1318 seeder fix, `sprk_syncmode` migration to Manual Refresh for existing Copy rules, regarding-pair/protected-column refusal); remove the OOB `businessunit→sprk_matter` map | No dependencies; can start first |
| **W2 Unified `RecordCreationService`** | Build the service per §4.1 from `OwnedChildWrite`'s create path: per-table rules registry; `CallerWriteAuthorization` extraction; caller and system principals; record-first owner; search index from the owning BU; stamp/I-16 into the payload; post-create steps inside; typed refusal model + one HTTP map; repair mode. Re-point the child-records route, chat create tool, email draft, budget inquiry and decision cores at it. Office quick-create (Matter/Project/**Invoice**) becomes an adapter. | Coordinate with UAC-r2 (hot file) and ontology-r1 (chat/decision cores). Update the census, re-file and route-authorization guard tests. |
| **W3 Product creators onto the service** | (a) **Wizards**: add Matter and Project to the browser create route; all 10 wizards stop calling `applyFieldMappings` and send only user-entered values; client defaults move to table rules; Matter number vs autonumber (I-11) settled. (b) **Analysis email**: mapping applied in the send path, fill-only. (c) **Bespoke server paths** (owner Q10, all four groups): Office Invoice / To Do / document save (closes word gaps G1/G2: mapping, stamp, search index; association set in the create); `POST /api/v1/events` and `POST /api/ai/analysis/create`; invoice extraction; system creators (To Do generation, playbook create-task, Signal/Decision writers, inbound communication/document ingestion) through the system principal. (d) Delete `FieldMappingService.applyFieldMappings` and `FieldMappingHandler`. | Matter wizard first (reference: ownership + mapping + root), then Project, then the rest. One wizard at a time behind the existing `IDataService` seam. |
| **W4 `/push` on the canonical engine** | Copy-only via the canonical Copy logic; `sprk_syncmode` selects rules; keep re-file behaviour (`ReparentAsync`, `AfterRefileAsync`, root filing gate); fixed, actionable error text (#1437, ADR-019) | #2098 (timeout at 500 children) is filed as separate work; #2056 stays with its owner |
| **W5 Outside the product** | §4.4 A (ribbon redirect, quick create off, **hide OOB Assign**) + B (repair pass `IScheduledJob`, on-demand trigger) | Host per ADR-052 = BFF `IScheduledJob`; no new hand-rolled `BackgroundService` |
| **W6 Standard, guards, docs, retirement** | Per-table ownership matrix + exception assessment; registry rows I-3/I-5 → done (and I-6 updated); extend the census guard (roots, client creates, no client `applyFieldMappings`, no `Xrm.WebApi.createRecord` on matrix tables); rewrite `SPAARKE-FIELD-MAPPING-FRAMEWORK.md` and the admin guide (fill-only, push, sync mode, "use a quick view when the child only needs to *show* the parent's value", no OOB relationship mappings on rule-owned fields); root CLAUDE.md pointer rows; delete `bffDataServiceAdapter`, LegalWorkspace `CreateMatter/matterService.ts`; close/update #1614, #1617, #1318, #1437; file #2098 and the `prvAssign` audit as follow-ups | Includes the §11 evidence for the removed engines and services |

**Phasing**: W1 ∥ W2 → W3 (Matter first) ∥ W4 → W5 → W6. W6's doc and guard items land alongside the workstream they
describe.

### 4.6 Out of scope

- External-portal creates (`ExternalDataService`, owner Q11): this project exposes the service's mapping step and
  records how external creates adopt it; they are listed as an exception in the inventory and coordinated with
  external-access-r3.
- Push performance (#2098).
- Removing `prvAssign` from roles (follows the audit).
- A Dataverse change-signal channel.
- Secure isolation (I-2) logic itself (UAC-r2), beyond calling it.

---

## 5. ADR tensions (per CLAUDE.md §6.5)

| ADR | Tension | Resolution |
|---|---|---|
| ADR-002 (WP-1…WP-8, amended 2026-10-03) | None; this project is the WP-1…WP-5, WP-7 implementation for I-3/I-5 and the unified path for the others. OOB relationship mappings fail WP-1 (b). | Comply |
| ADR-010 (DI) | One scoped service | Replaces the Office registration; net zero. Engine stays static. |
| ADR-028 / ADR-008 | App-only creates | Comply (Q2): explicit caller checks; no impersonation for creates. |
| ADR-024 (regarding pair) | Mapping could write the pair | Refuse pair-targeting rules; mapped lookups pass the same AppendTo checks |
| ADR-019 (error text) | Push leaks raw HTTP text (#1437) | Fixed in W4 |
| ADR-052 / ADR-036 | W5 host | BFF `IScheduledJob`; no Function, no hand-rolled `BackgroundService` |
| ADR-038 | Tests | Engine unit tests, pipeline seam tests, per-surface integration tests, census guard extensions: all KEEP paths |
| ADR-012 | Client package shrinks | Comply; `FieldMappingService` loses `applyFieldMappings` |
| **Behaviour change (not an ADR)** | Office Project owner (caller BU → parent BU); create-time mapping becomes fill-only; push becomes Copy-only + Manual Refresh rules | Owner decisions Q6/Q3/Q9; called out in release notes |

---

## 6. Placement justification (BFF §10) and component justification (§11)

**Placement.** In the BFF:
- Creation applies several server-owned invariants in one request, under Spaarke auth.
- It is called by code pages, add-ins, AI tools, jobs and the repair pass.
- There is no other synchronous server runtime (no plugins; ADR-052 forbids user-facing endpoints in Functions).
- The repair pass is an `IScheduledJob` in the BFF: same code as the inline path, and the fewest moving parts.
- This is CRUD code with no AI dependency, so the ADR-013 facade rule is not triggered.

| Surface | Existing (evidence) | Extend instead? | Cost of doing nothing |
|---|---|---|---|
| Unified `RecordCreationService` | `OwnedChildWrite` (create half) + `RecordCreationService` (Office) | **Yes: merge two existing cores; no third** | Same record gets different owners and invariants by surface; mapping reaches 2 of ~30 create paths |
| `CallerWriteAuthorization` | `OwnedChildWrite` helpers | **Move unchanged** | Authz stays tangled with re-file and create in one 1.7k-line file |
| Per-table rules registry | Four separate column lists in two files | **Consolidate** | Protected/server-owned columns drift between paths |
| Canonical engine | `CreateTimeFieldMapping` (+ TS, push) | **Promote C; delete A; B delegates** | Same profile gives three answers |
| Repair pass job | `CoreAncestorStampReconciliationJob` pattern | **Same pattern, new invariants** | Imports, native forms and agent writes never get mapping or standard ownership |
| Browser create route for Matter/Project | child-records route | **Extend the table set** | Matter/Project stay user-owned and client-mapped |

**Publish size and CVE**: no new packages. Measure per BFF task against a fresh master build (root CLAUDE.md §10.4).

---

## 7. Owner decisions

| Q | Question | Decision | Date |
|---|---|---|---|
| Q1 | Sequencing with word-add-in-r1 | Separate project, own worktree; promote rather than fork word's code. **The gate is satisfied**: that code is on master (2026-09-30). | 2026-09-29 |
| Q1a | Configuration surface | Keep `sprk_fieldmappingprofile` / `sprk_fieldmappingrule` and their native forms; clean up (§4.2) | 2026-10-19 |
| Q2 | Identity | App-only create + explicit caller authorization + I-6 team owner | 2026-09-29 |
| Q3 | `/push` behaviour | **Revised**: Copy rules only, canonical Copy logic, selected by `sprk_syncmode` = Manual Refresh. *(Was: all four types; no configured use case, and Default on push would reset children.)* | 2026-10-19 |
| Q4 | Client preview | **Superseded**: no preview exists; the client engine is deleted | 2026-10-19 |
| Q5 | W5 | **Revised**: native UI redirect + scheduled fill-only repair pass (incl. owner); no change-signal channel unless a named source needs it | 2026-10-19 |
| Q6 | Precedence at create | **Fill-only**: caller-supplied values win; empty = not supplied; wizards send only user-entered values; push is the deliberate overwrite | 2026-10-19 |
| Q7 | Mapping source and reach | Inferred from the filed-under parent, explicit override; every creator on the service gets mapping (incl. chat, decisions, system creators) | 2026-10-19 |
| Q8 | Create-core architecture | One `RecordCreationService` built on `OwnedChildWrite`'s authorization; Office becomes an adapter; re-file separate | 2026-10-19 |
| Q9 | Owner rule for roots | Record-first everywhere | 2026-10-19 |
| Q10 | Bespoke server paths in scope | All four groups: Office Invoice/To Do/save; Events + Analysis routes; invoice extraction; system creators | 2026-10-19 |
| Q11 | External-portal creates | Out of scope; coordinate with external-access-r3 | 2026-10-19 |
| Q12 | OOB Assign | Hide on standard-owned tables + repair pass; `prvAssign` removal after an audit (UAC-r2) | 2026-10-19 |

---

## 8. Success criteria (measurable, for the spec)

1. **One engine.** `ApplyMappingRule` logic is gone; `FieldMappingService.applyFieldMappings` and `FieldMappingHandler`
   are deleted. A guard fails on any client-side mapping call.
2. **One create service.** Every in-scope create path in the inventory (§2.3 tiers 1–3) goes through
   `RecordCreationService`. A census test fails on a new server create of a matrix table that bypasses it, or on a
   client `Xrm.WebApi.createRecord` of one. Out-of-scope paths are listed exceptions.
3. **Mapped on first load.** For a record created from any wizard, Office, chat or a decision follow-on under a parent
   with a profile, mapped fields are present in the first retrieve after create. One integration test per surface
   family uses the same profile fixture.
4. **Fill-only.** A caller-supplied value for a mapped column survives create (the Report Card law-firm case is the
   regression test).
5. **Same owner everywhere.** The same Project filed under the same Matter gets the same owner from every surface.
   No Matter or Project is user-owned after create. Refusal when unresolved is tested.
6. **Mapped lookups are authorized.** A mapping that writes a lookup the caller can't AppendTo is refused or skipped
   per the spec rule, never silently written (#1614/#1617 regression tests).
7. **Push parity.** The same profile and source give identical Copy output from create (into an empty field) and from
   push. Push errors carry fixed, actionable text.
8. **Outside the product.** A row created natively or by import gets its mapping, standard owner, search-index default
   and stamp within one repair interval (target ≤ 10 min), fill-only (a value already present is never overwritten),
   and idempotently under re-runs.
9. **Ownership standard published.** A per-table matrix, every exception assessed; registry I-3/I-5 done; framework
   doc and admin guide rewritten.
10. **Dead code removed.** `bffDataServiceAdapter`, LegalWorkspace `CreateMatter/matterService.ts`, the OOB BU→Matter
    map.

---

## 9. Dependencies and coordination

| Project | Relationship |
|---|---|
| **spaarkeai-word-add-in-r1** | **Complete** (136/136). Source of `RecordCreationService`, `CreateTimeFieldMapping`, `RecordOwnershipResolver`, `QuickCreateSourceAccessFilter`. Its document-save gaps G1/G2 are closed by W3(c). |
| **unified-access-control-r2** | **Peer, still active** (148 tasks done; deferred and escalated items remain). Owns `OwnedChildWrite`, the reconcilers, D-11, the census guard and the role definitions. Coordinate on: the W2 refactor of its hot file, repair-pass re-owning (W5), the `prvAssign` audit, and census guard changes. |
| **spaarke-ontology-platform-r1** | **Peer.** Moved the write core to `Services/Dataverse` (D-66); owns the chat create tool, `DecisionRouteCores`, Signal and Decision writers. Coordinate on W2 re-pointing and W3 system creators. |
| **spaarke-SPA-external-access-platform-r3** | **Peer.** Owns external-portal creates (out of scope here); adopts the mapping step on its own schedule. |
| **set-regarding-and-field-mapping-resolver-r2** | **Predecessor.** Built the client engine; its "outside a wizard falls back to manual push" boundary is superseded. |
| **ci-cd-unit-test-remediation-r1** | Tier-1 carries the ADR-002 zero-plugin guard. No workflow edits expected. |

---

## 10. Directions for the spec (details the spec settles)

| Item | Direction |
|---|---|
| Route shape | Widen `POST /api/v1/child-records/{table}` to Matter/Project, or add `/api/v1/records/{table}` with child-records as an alias. Pick one; one browser entry point. |
| Request envelope | How the browser passes an explicit mapping source and the surface (body envelope vs header) |
| Multi-parent mapping source | Precedence rule when a payload binds several parents |
| "Empty" per type | Null/absent = not supplied; empty string → null; lookups = no bind; booleans/option sets: absent only |
| Refusal model | Refusal kinds → one HTTP map; Office error-code compatibility; mapped-lookup AppendTo failure = skip with a warning vs refuse the create |
| Source read | As the caller (field-security masking) for user creates; app-only for the system principal |
| Write method | SDK create vs upsert-with-new-id; idempotency for retries |
| I-11 | Matter wizard currently sends its own number; autonumber is a protected column. Decide: wizard stops sending it, or an explicit allow-list. |
| Client defaults | Inventory each wizard's defaults; move the ones that matter to table rules |
| Analysis email | Where in `CommunicationService.SendAsync` the analysis→communication mapping runs |
| Repair marker | Verify `createdby` ≠ BFF app user across all product paths (check impersonated creates); watermark storage |
| Native surfaces | Verify live quick-create flags; list each ribbon "New" to override |
| `sprk_syncmode` | Migrate existing Copy rules to Manual Refresh; default for new rules |
| Metrics | Per-step counters for the service and the repair pass |
| Guards | Exact census extensions and client-side arch rules |

---

## 11. Next steps

1. ~~Owner answers Q1–Q5.~~ Done 2026-09-29.
2. ~~Re-verify against master; settle Q3–Q12.~~ Done 2026-10-19.
3. `/design-to-spec` for this folder.
4. `/project-pipeline`: register in `projects/INDEX.md` with the hot-path declaration below, and correct the
   portfolio board status for issue #1164.

```xml
<hot-path-declaration>
  <bff>Y</bff>                   <!-- RecordCreationService, CallerWriteAuthorization, engine, routes, repair job -->
  <spaarkeai>N</spaarkeai>       <!-- wizards live in Spaarke.UI.Components / Spaarke.AI.Widgets; SpaarkeAi hosts them but its files are not edited -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>Y</root-claude-md>  <!-- W6 updates the Field Mapping Framework and Dataverse write path pointer rows -->
</hot-path-declaration>
```

---

## 12. W7 — Record family service and the Family tool (addendum, 2026-10-27; DRAFT for UAC-r2 and ontology review)

### 12.1 Why

- **Owner decisions (2026-10-27):**
  - build ONE shared, read-only "record family" service;
  - add a **Family** tool to the TrackingFieldTrio PCF. It opens a modal listing the record's family, with an action toolbar;
  - the first actions are **Push field updates** and **Update Assigned To** (owner 4a). More actions come later, each with its own review;
  - keep it in this project and follow the procedure.
- **Current state:** about eight separate enumerations each declare their own copy of the hierarchy, and nobody checks that they agree (code map, notes/decisions.md 2026-10-27):
  - push `BuildChildRecordQuery`;
  - `SecureRootInheritance` up and down walks;
  - `SecureChildLineage` / `SecureChildReconciler`;
  - `ChildAccessPermissionReconciler`;
  - `CoreAncestorResolver` / `CoreAncestorRestamper`;
  - `RecordOwnershipResolver.OwnershipParentEntities`;
  - `AccessibleRecordSetService`.
- **Gaps:** none of them answers "what in this record's family can THIS user see". Push is one level deep and has no preview.

### 12.2 The canonical family (UAC-r2, 2026-10-27; full text in notes/decisions.md)

- **Filing edges only:**
  - core → core: projects and work assignments under a matter or project (typed filing, intermediate filing, record-type pair);
  - core → child: the four regarding stamps plus each child table's direct filing lookups;
  - child → grandchild: the `SecureChildLineage` hops.
  - Depth is 4. The walk goes down from the record, plus up to its root.
- **Not family:** citations, people and organization lookups, N:N associations, grant rows, system rows.
- **Detail tables (UAC-r2 2026-10-28):** `RecordFamilyEdges.DetailTables` (event log, email review log, attachment artifact, email artifact, spend signal, spend snapshot, billing event, analysis output) stay registry rows but the default Family view and push skip them; hiding them hides no user record. KPI assessments are shown. An analysis is filed under its document (`sprk_analysis.sprk_documentid`, its ownership primary); a message is filed under its record thread (`sprk_communication.sprk_communicationthread`).
- **Service requests (UAC-r2 ruling 2026-10-28):** a service request is a core root outside the Secure family. Its `ServiceRequestRelations` lookups are Filing edges of its own family (ownership parent, re-file column, ordinary reconcile walk); the Secure lineage never walks them. `InSecureFamily = false` means "not walked by the Secure lineage". Secure-if-any does read the service request, but no writer puts one in the Secure Record business unit.
  - Expected behaviour, not a bug: a budget inquiry creates a `sprk_servicerequest` regarding the matter. Because the service request is its own root, the matter's Family view does not list the inquiries sent from it.
- **Ontology tables (ontology review, 2026-10-27; already in `SecureChildLineage`):**
  - `sprk_signal` is a child of its typed `sprk_matter`, of its subject (the 8 `sprk_regarding*` lookups; `sprk_regardingservicerequest` is a Filing edge of the service request's own family, not the Secure family — UAC-r2 2026-10-28), and of its Decision Record (`sprk_signal.sprk_decisionrecord`). It is never a child of a policy, a policy version or a core record type.
  - `sprk_decisionrecord` is a child of its typed core lookups (matter, project, work assignment) and of its subject's regarding lookups. It is never a child of `sprk_action` or a policy version.
  - `sprk_budgetrevision` is a child of `sprk_budget` and `sprk_matter`.
  - `sprk_policy` → `sprk_matter` and `sprk_policyversion` → `sprk_policy` (#2084): the EDGE always exists, but a policy is a MEMBER only when its `sprk_matter` is set (a matter-scoped rule). `sprk_policy.sprk_currentversion` is excluded (a loop).
- **Terms (ontology):**
  - "family", "member", "root" and "filing edge" are reuse, not invention ("Secure family", "a matter's family" #2084, "invoice family"). Ontology proposes a component-model §3 row.
  - Keep three neighbours distinct: "subject" (what a Signal or Decision Record is about; never use it for root), "citation" (never filing), "filing" (consistent with ontology's File and File+Act dispositions).
  - The UI word is "Family". Avoid "Related records", which reads as N:N associations.
- **To classify before seeding** (owner, with UAC and ebilling):
  - `sprk_procedureassignment` (one typed lookup per core record; configuration ABOUT the record, not filed content): in or out?
  - A record-scoped ruleset's record rule is its own `sprk_policy`. It is a member only if it carries `sprk_matter`; ebilling owns that answer.

### 12.3 Components

**A. `RecordFamilyEdges` — the canonical edge registry (data, `Services/Dataverse`, beside `CoreAncestorResolver`)**

- **One concept, with kinds (ontology, 2026-10-27; the term itself waits on the owner's component-model §3 row):**
  - A **family** is a set of records joined by typed edges of ONE kind. W7 builds one kind only: **Filing**, a tree with one root (the core ancestor).
  - A later peer kind for IP (for example **Priority**: continuation, divisional, foreign counterpart) is a graph with no root. It is labelled by its term of art ("Patent family"), and W7 builds nothing for it.
  - "Secure family" is NOT a kind. It is the Filing family of a Secure root (`InSecureFamily`), and stays a qualifier.
- **Rows:** `(FamilyKind, ChildTable, LookupColumn, ParentTable, EdgeKind = FilingCore | Stamp | LineageHop, InSecureFamily, RecordNumberColumn?, …)`. Every row is `FamilyKind = Filing` in W7.
- **Edge TYPES live in code; edge INSTANCES live in Dataverse (ontology).**
  - Filing edges are schema facts that UAC's isolation walks depend on. An editable table would let configuration change an access boundary, and the parity ratchet can only guard code.
  - A future IP kind keeps its instances (matter A claims priority from matter B) in a link table, and adds ONE registry row pointing at that table.
- **Seed:** from `SecureChildLineage`, `TypedFilingColumns` / `IntermediateFilingColumns` / pair, and `StampSourceColumns`, AFTER UAC task 194 merges. Never written from scratch.
- **Parity test:** each existing registry is a checked view of this one, with a ratchet baseline. A new disagreement fails the build; the known ones are listed and worked down by UAC. Migrating UAC's walks onto it is UAC's call.
- **Content ownership:** UAC-r2. Enforced by a content-pin test. Changing an edge fails the test, and its message requires a UAC review line in the PR (enforcement ladder: a test, not prose).
  - **The pin covers the access-relevant columns only (UAC C3):** FamilyKind, ChildTable, LookupColumn, ParentTable, EdgeKind and InSecureFamily.
  - `RecordNumberColumn` and other display fields change without a UAC review line.
- **Isolation stays Filing-only (UAC C2):** every view of the registry that UAC's access walks use filters `FamilyKind == Filing`. That covers `SecureChildLineage` parity and the `CoreAncestorResolver` children.
  - Enforced by a test that adds a synthetic non-Filing row and proves those views do not change.
  - This is the guard that a later Priority row can never enter an isolation walk or I-6.

**B. `RecordFamilyReader` — read-only, scoped**

- **Signature:** `ReadAsync(kind, table, id, direction, continuation)`. `kind` defaults to Filing.
- **One kind per walk (ontology 3a):** a walk never mixes kinds. A priority edge crosses separate matters, possibly Secure ones. A Filing walk that followed one would spread push and Assigned To across matters and through the isolation boundary.
- **Generic over edges:** the walk is driven by registry rows and keeps a visited set, even for Filing. This handles a bad data loop, so a later peer kind is a data change, not a rewrite.
- **Identity:** walks AS THE CALLER (OBO), level by level, with one batched query per (child table, lookup) per level (no N+1).
- **Unreadable records:** the walk never passes through one. It never returns an unreadable member, and never counts or hints at hidden ones.
  - **Up:** the walk stops at the first ancestor the caller cannot read. Nothing above it is returned, and there is no hint that it exists (UAC review F2).
- **Continuation (UAC review F1, C1):** opaque and integrity-protected, using ASP.NET Data Protection.
  - It is bound to the caller's `oid`, the anchor, the kind, the direction and the top it derived.
  - A down-walk continuation cannot be replayed as a full-family walk, and a Filing one cannot be replayed as another kind.
  - Every parent id it carries is re-checked as caller-readable before use, so a caller can never steer the walk through a record they cannot read.
  - A tampered continuation answers exactly like an invalid one.
- **Anchor probe:** uses the equalised gate call, so an unknown anchor and a denied anchor look the same. Kind validation (400) may run before the probe; it reveals nothing (UAC).
- **Caps:** depth 4; 500 members per view and a per-level cap. Past a cap it returns `truncated = true`, per-table counts of what IS shown, and a per-level continuation. It never truncates silently.
- **Truncation keeps the anchor in view (UAC C4):** a truncated full-family view still includes the anchor's ancestor chain and the anchor itself. It fills the anchor's own subtree before its siblings' subtrees.
  - Otherwise, opening a document under a large matter could truncate the document's own branch out of the view.
  - Covered by a K line in §12.11 and a test.
- **Field security:** because the walk is OBO, Dataverse applies field security. A field-secured record number or status comes back null. These columns are never read app-only (UAC).
- **One family per record (owner-approved via UAC-r2, 2026-10-27; sent to ontology for D-191):**
  - A record filed under several parents belongs only to its PRIMARY parent's family.
  - Up and down walks follow primary edges only.
  - The primary parent comes from `RecordOwnershipContext.InPrimaryOrder` / `OrderedParentsOf` (UAC 194b). Never a second ordering.
  - Isolation still uses secure-if-any over all filing parents.
  - Only the pair is the Matter → Project edge, never an N:N row.
- **De-duplication (ontology review):** members are unique by (table, id), and the shallowest edge is kept. Example: a Signal reached from a matter both directly and through its Decision Record appears once. Counts and the 500 cap count unique members.
- **Fields returned:** id, table, primary name, record number (null for a table without a number column), status, modified on, parent edge, depth (owner, 2026-10-27). The record-number column per table comes from the registry. No access metadata (grants, No Access, sharees, owner team).
- **Not an authorization input.** No cross-caller cache.

**C. `GET /api/v1/records/{table}/{id}/family`**

- Lives in the existing `/api/v1/records` group, with auth required, in its OWN file, `Api/Records/RecordFamilyEndpoints.cs`. `RecordNoAccessEndpoint.cs` stays one cohesive No Access responsibility (§11.5).
- It is registered in the route-authorization ledger (`RouteAuthorizationGuardTests.Ledger`) with its mechanism, and the governed-file census is updated (UAC review F2).
- Takes `direction` and `continuation`. The default is the FULL family (owner, 2026-10-27): up to the highest READABLE ancestor, then down from it. That ancestor is the root only when the root is readable (UAC T1). Siblings and cousins are included only when every record on the path down to them is readable. The walk's pruning rules apply to both directions. `direction=down` remains available for the push preview, which only acts below the source.
- Takes `kind` (default Filing; any other value is refused with 400 in W7).
- **Response shape (ontology 3c):**
  - The walk is described from an `anchor` (the record opened). `root` is optional, because a peer family has none.
  - Every member carries `kind` and `edgeKind`.
- An unknown or unreadable anchor gets one uniform 404.

**D. `/push` extensions (task 040; UAC 4a design)**

- `includeAssignedTo` defaults to false when absent.
- `preview: true` writes nothing. It returns, per child whose value would change: id, name, column, from → to, and the flag for each Assigned-To policy it will meet: Secure (suggestion only), Restricted (no contact grant) and Limited (no organization grant) (UAC review F2). It also returns a skipped-for-no-Write count and `truncated`.
- `childIds` is the confirmed list. The server re-derives the children as the caller and intersects them with the list; any other id is refused per id.
- With `includeAssignedTo`, the caller needs Write on the SOURCE (equalised probe).
- The profile's parent lookup must be a `RecordFamilyEdges` filing edge, or the push is refused.
- **Actions are per kind (ontology 3b):**
  - Push field updates and Update Assigned To are defined for the Filing kind only.
  - The server refuses them for any other kind; hiding them in the UI is not enough.
- Unchanged: filing columns are never pushed, invariant columns are never copied, `SecureRootFilingGate` and `AssignedAccessMaterializer` run per child.
- Sync mode becomes class-aware: Assigned-To targets 0, filing targets never, plain data 1. The values ship with 040 after the owner reviews the dry run.

**E. Family modal (client, `Spaarke.UI.Components`)**

- `RecordFamilyModal` is built on `SprkModal/presets/BrowseModal` (ADR-050 Layout 2, in-app).
- It shows the family grouped by level and table, with checkboxes, a truncation notice and "load more".
- **Action toolbar, v1:**
  - **Push field updates:** push preview → the person confirms rows → apply → result summary.
  - **Update Assigned To:** the same flow with `includeAssignedTo`.
- An action never acts on unconfirmed rows. The server re-checks everything.

**F. TrackingFieldTrio**

- A new toolbar icon, **Family**. The shared component gains an optional `onOpenFamily` callback, the same pattern as `onOpenGrantModal` / `onOpenEmailMembers`, so existing consumers see no change.
- The PCF (`index.ts`) wires it and renders the modal.
- Forms: Matter, Project, Work Assignment and Event. All four already host the PCF, so no form change is needed.
- The PCF bundle delta is measured.

### 12.4 Sequencing

1. **A**, after UAC 194 merges.
2. **B + C + E + F** as a read-only Family view. This does not depend on 017.
3. **D**, with 040. The modal's actions light up when 040 ships. 040 still depends on 019 → 017 → ebilling #1911.

### 12.5 Out of scope (later, each with its own review)

- Monitor or High Priority across the family; email family members (UAC: recipients from readable members' people lookups, field security respected, the existing send path).
- Any bulk share or grant (needs UAC review first).
- Any family table: neither a stored membership (UAC's unbuilt `sprk_securefamily`, D-191) nor a peer IP kind or its link table.
- External users (they use `AccessibleRecordSetService`).
- A Console entry point.

### 12.6 Placement (§10) and component justification (§11)

**Placement.** In the BFF: a caller-scoped Dataverse read used by PCF and code-page surfaces. CRUD only, with no AI dependency (ADR-013 is not triggered). No new packages.

| Surface | Existing (checked) | Extend instead? | Cost of doing nothing |
|---|---|---|---|
| `RecordFamilyEdges` | Seven registries in five files | **Consolidate**: a seeded union plus a parity ratchet; existing registries untouched | The hierarchy keeps drifting (UAC 194 just found `sprk_budgetbucket` missing); every new family feature adds an eighth copy |
| `RecordFamilyReader` | Access walks are app-only and enforce invariants; push is one level as the caller | **No**: those walks must stay app-only for isolation (UAC), and the push query is private, one level and profile-bound | The Family modal and the push preview each build their own caller walk |
| `GET …/family` | `/api/v1/records` group | **Extend the group** | The PCF has no way to list the family |
| `/push` `preview` / `includeAssignedTo` / `childIds` | `/push` | **Extend the existing endpoint** | Assigned-To re-push stays impossible, or becomes an unpreviewed mass access change |
| `RecordFamilyModal` | `BrowseModal`, `AccessGrantModal` pattern | **Compose `BrowseModal`** | No user surface for family actions |
| TrackingFieldTrio Family icon | Existing toolbar (grant, email) | **Extend the toolbar** | No entry point on the forms |

- **Publish size:** measured per BFF task against a fresh master build.
- **Hot path:** BFF Y (unchanged). SpaarkeAi N. The PCF and the shared UI library are not hot-path surfaces.

### 12.7 ADR tensions

| ADR | Point | Resolution |
|---|---|---|
| ADR-050 / MODAL-DECISION-CRITERIA | A collection browse with actions | Comply: `BrowseModal` (Layout 2), in-app from a React surface |
| ADR-021 / ADR-022 | Fluent v9 and React 16 in a PCF | Comply: the shared library is already consumed by this PCF (`AccessGrantModal`) |
| ADR-028 / ADR-008 | Read identity | Comply: OBO as the caller; the equalised root probe; auth required |
| ADR-010 | DI | One scoped reader plus static registry data |
| ADR-019 | Error text | Fixed, actionable texts; detail to logs |
| ADR-038 | Tests | Registry parity and content pin (unit or arch), reader seam tests (as the caller, unreadable pruning, no counts, caps), endpoint contract, push preview/ids tests (UAC's list), UI component tests |

None needs path A or B.

### 12.8 Success criteria (W7)

1. One edge registry. The parity test lists every disagreement with the existing registries, and a new one fails the build.
2. The family view shows only caller-readable members. A readable record below an unreadable one is absent, and nothing reveals hidden members (tested with a Secure family).
3. A truncated family says so and can load more. Nothing is dropped silently.
   - A tampered continuation naming an unreadable parent returns nothing under it, and answers the same as an invalid continuation.
   - With direction=up, a readable project under an unreadable Secure matter shows no ancestors.
4. **Push:**
   - preview writes nothing;
   - an absent `includeAssignedTo` leaves Assigned columns untouched;
   - a Read-only source with `includeAssignedTo` is refused (403);
   - an unticked child keeps its value and its grants;
   - a foreign id is refused;
   - a Secure child gets a suggestion, not a grant.
5. The Family tool works from the Matter, Project, Work Assignment and Event forms; existing TrackingFieldTrio consumers are unchanged.

### 12.9 Open questions for the owner

Owner answers, 2026-10-27:

1. **The Push Updates ribbon button:** keep it, and have it open the Family view (owner confirmed 2026-10-27).
2. **Columns per member:** name, **record number** (blank where a table has none), record type, status and modified on.
3. **Default view:** the full family, all the way up and down (§12.3 C).
4. **`sprk_procedureassignment`:** OUT of the Family view (owner, 2026-10-27; UAC-r2 agrees). It is billing configuration; its access lineage is unchanged.

The family as a data-model concept, including future patent and trademark families, has been raised with ontology-r1. See §12.10.

### 12.10 Reviews required before the spec amendment

- **UAC-r2:** the registry (A), read identity and limits (B, C), push extensions (D).
- **Ontology-r1:** the hierarchy vocabulary and the term "family" (terminology changes originate in its component model, root CLAUDE.md §1.1).

**UAC-r2 review (2026-10-27, at eec7704ec4):** accepted with four fix-now items, all applied above:
- F1: the continuation is integrity-protected and its parent ids are re-checked;
- F2: up-walk pruning;
- F2: the Limited flag in the preview;
- F2: own endpoint file, plus the route-authorization ledger and census.

A, B, D, E, F, §12.5 and §12.7 were accepted as written.

**Ontology-r1 review (2026-10-27, at eec7704ec4):** the terms and the hierarchy agree. Applied in §12.2 and §12.3 B:
- the ontology table edges;
- the policy membership rule;
- de-duplication by (table, id);
- the service-request note;
- the term guidance.

Still open: classify `sprk_procedureassignment` and record-scoped ruleset rules (§12.2).

**Ontology-r1, round 2 (2026-10-27, family as a data-model concept):**
- Nothing about a family component is recorded in ontology R1 or R2: no design, no D-number, no patent or trademark family model. Ontology is asking the owner directly.
- Recommendations applied in §12.3 A–D:
  - one concept with kinds (Filing now; a peer IP kind later; Secure is a qualifier, not a kind);
  - one kind per walk;
  - actions per kind, refused server-side;
  - anchor and optional root;
  - `kind` and `edgeKind` on every member;
  - a visited set;
  - edge types in code, edge instances in Dataverse.
- Term: **decided, D-191 (owner, 2026-10-27; ontology component model §3 "Family").**
  - There is ONE canonical Family concept: a set of records joined by typed edges of one kind.
  - Filing is the kind now, which is exactly W7. A peer IP kind (for example Priority) comes later and keeps the name "patent family".
  - "Secure family" is a qualifier, not a kind.
- **Materialised form, not foreclosed:** UAC-r2 rounds 94/100b decided an UNBUILT `sprk_securefamily` table: a single-membership lookup on every family table, an owner team per family, and nesting. UAC research note §8 leaves the owner the option to generalise it to a record family for every core root.
  - That table would be a stored form of this same Filing family.
  - W7's names and API must not foreclose it. If it is built, `RecordFamilyReader` may use the stored membership as an index instead of walking. The response contract does not change.
  - W7 creates NO family table (§12.5).

**UAC-r2, round 2 (2026-10-27, at 4d0bba20dd):**
- Accepted: the `FamilyKind` column and the `kind` parameter. The full-family default is covered by the existing pruning rule.
- Applied in §12.3 A–C and §12.11:
  - T1: down from the highest readable ancestor;
  - T2: the anchor in the 404 and in the probe;
  - C1: continuation bound to oid, anchor, kind, direction and top;
  - C2: isolation views filter Filing, with a synthetic-row test;
  - C3: the pin covers the access columns only;
  - C4: truncation keeps the anchor's chain and subtree;
  - OBO field security on display columns.
- **Seed: UAC 194 merged as PR #2349, master `de38296499` (2026-10-27).** Component A is seeded from that commit.
  - It adds `sprk_budgetbucket` → `sprk_budget` to `SecureChildLineage`.
  - Service requests are walked in ordinary mode through `ExternalDataService.ServiceRequestRelations`. That list is a service request's OWN family (it is its own root, §12.2), not part of a matter's.
  - `sprk_invoicelineitem` stays out until ebilling deploys it (#2348).
  - Grant rows do not follow an ordinary move (#2341). This does not affect a read-only view.
- **Matter → Project edge (#2352). Owner decided (a), the regarding pair only, 2026-10-27.**
  - The existing Project↔Matter N:N stays as an association (for example, related matters). It is not a filing edge and confers nothing.
  - The background below is kept for reference.
  - A project is filed under a matter by its regarding PAIR (`sprk_regardingrecordid` + `sprk_regardingrecordtype`), the edge UAC's inheritance already keys on (`SecureRootInheritance` task 158).
  - CreateProjectWizard writes only the N:N `sprk_Project_Matter_nn` and never the pair, so no dev project is filed under its matter today.
  - UAC-r2 recommends (a): the pair, one root per project, written by the wizard, with the 7 N:N-linked dev projects backfilled. UAC owns or reviews that fix. The alternative, (b), is a typed lookup kept in agreement with the pair. (c), promoting the N:N, is rejected: Filing is single-root.
  - **For W7:**
    - A models the pair as the Matter → Project filing edge (an EdgeKind for a pair).
    - B queries a pair edge by (id, record type).
    - D's parent-edge check (task 040) accepts a pair edge, not only an N:1 lookup. This replaces `ResolveParentLookup`'s lookup-only rule for push.

### 12.11 Known limits (one line each in the PR)

- **Query count:** the worst case is about (edges per level × 4 levels) batched reads per view, 100 or more for a big matter. Measure p95 on dev and record it. Narrow each level to the edges whose parent table is present only if it hurts in practice.
- **Truncation favours the anchor:** in a truncated full-family view, the anchor's ancestor chain and its own subtree are filled first. Siblings' branches are the ones cut, and "load more" reaches them (UAC C4; tested).
- **Depth cap counted from the top (owner decision 2026-10-29; exception to UAC C4):** the cap of 4 levels runs from the walk's top, the highest readable ancestor, not from the anchor. An anchor at depth 3 or 4 therefore shows one level or none of its own subtree, and "load more" cannot reach the rest. Opening that record from a shallower point, or on its own when it is the top, shows its subtree. (task 071 review B, finding 2)
- **Permissiveness:** the view is exactly as permissive as Dataverse read for the caller, never more. Gaps Dataverse itself has today (e.g. the flagged-external systemuser reading Restricted rows, UAC task 191) show here as they do in the MDA.

### 12.12 The family abstraction contract (UAC-r2 round 130, 2026-10-28, owner decision; #2362)

The structured family entity (`sprk_securefamily`) is **not built now**; it is on the roadmap. Until it exists, W7 is the family:

- **`RecordFamilyEdges` + `RecordFamilyReader` + `GET /api/v1/records/{table}/{id}/family` are the family abstraction.** Every caller reads a family through them (the reader in the BFF, the endpoint from clients), never through raw parent lookups. Push (task 040) finds a source's children through the edges and the primary-parent rule, not through `ResolveParentLookup` or a hand-written lookup.
- **The implementation can be replaced, the contract cannot.** If `sprk_securefamily` is built later, it replaces how the reader finds members; the reader's public surface, the FR-31 response and the callers stay the same. The reader therefore exposes no lookup names, edge internals or storage details (registry rows stay `internal` data the reader consumes).
- **Membership stays computed:** the primary-parent rule via `InPrimaryOrder` (UAC 194b), the Matter → Project pair edge only (UAC 195), no stored family key.
- **W7 adds no family lookup column and no family table.**
- **Binding now (round 130):**
  - every ownership write goes through `RecordOwnershipResolver` (W7 is read-only and writes none);
  - new code that tests isolation does so through UAC's one shared predicate keyed on the Secure Record business unit, never a hard-coded team id. W7 does not decide isolation (Dataverse read does, as the caller); 070's C2 guard and any test fixture follow the same rule.
  - The predicate (UAC-r2, 2026-10-28): `SecureRecordOwnerTeam.IsInSecureBusinessUnit(owningBusinessUnitId, secureBusinessUnitId)` → `bool?` (`Infrastructure/Dataverse/SecureRecordOwnerTeam.cs`). Select `owningbusinessunit` on the rows checked; `null` = cannot tell → fail closed. The Secure BU id comes from `SecureChildShareSynchronizer.ResolveSecureBusinessUnitAsync` → `(Id, Ambiguous)`: Ambiguous → refuse; no id → the environment has no Secure Record BU; a fault propagates. Resolve once per request or pass. UAC 194b moves the resolver to `SecureRecordOwnerTeam.ResolveSecureBusinessUnitAsync` (a one-line rename at merge). Never compare `ownerid` with the Secure Record Owners team id.

## 13. Customer configuration ownership and a server-owned search index (addendum 2026-10-28, #2399; DRAFT for UAC-r2 and provisioning review)

### 13.1 Why

- **Live defect #2399 (dev, 2026-10-28).** A child-BU user's Work Assignment create was refused with the uniform 404.
  - The wizard binds the user's BU index (`EntityCreationService.resolveUserBuDefaults`).
  - Step 5 asks AppendTo as the caller on that `sprk_aisearchindex` row.
  - The row is owned in the root BU, which Core User's Deep Read and AppendTo cannot reach.
- **Same defect, mapping configuration.** All `sprk_fieldmappingprofile` and `sprk_fieldmappingrule` rows are root-owned too. For child-BU users the client mapping engine maps nothing, and the Push Updates `hasSourceProfile` query hides the button.
- **Owner rule (UAC round 136).** The root BU holds Spaarke product data only. Customer configuration belongs in the customer's top BU.
- **UAC-r2 review.** Global AppendTo on the index would let any user route documents into any BU's index, so the owner chose re-ownership instead of widening the role.

### 13.2 Components

| # | Change | Owner of the code | Notes |
|---|---|---|---|
| a | The memberless team "Spaarke Customer Configuration Owners", plus the role "Customer Configuration Owner" (Read at Basic on `sprk_aisearchindex`, `sprk_fieldmappingprofile`, `sprk_fieldmappingrule`), with its script and guard | **UAC-r2** (194b R4b-3b) | Not this project. The script runs per customer BU and refuses the root and Secure Record. |
| b | Re-own dev's 11 index rows, 7 profiles and 40 rules to that team in Spaarke Business Unit 1 | this project (live write; owner's go) | No cascade from these tables (checked on dev). Read-back required. |
| c | **FR-38: the search index is server-owned** on every search-indexed table, on every server create path | this project | See §13.3. Amends FR-10's "an explicit value wins". |
| d | **FR-39: the mapping seeder owns its rows to the team** | **provisioning** (owns it; one PR with H7c's team creation, #2429; this project reviews) | H7c (new, after H7b; ported from UAC's Set-CustomerOwnerTeams.ps1, so one handler creates both owner teams, Restricted Content and Customer Configuration) creates the memberless team and records its id in `InterStepState`. H9 depends on H7c; H12b (which always runs after H9) binds `ownerid` to that id, and fails closed if it is missing. Today the seeder sets no owner: on ACME, 3 profiles and 20 rules are root-owned by the control-plane identity. Provisioning re-owns them after its owner's OK. |
| e | Wizards stop binding the index (`applyUserBuDefaults` / `bindLookup('sprk_aisearchindex', …)`) | this project, folded into W3 tasks 021–029 | After (c): the server applies the index itself. The same change considers removing Core User's `prvAppendTosprk_AISearchIndex` (after W3 it serves only native form edits; UAC-r2 review line) and removes the `sprk_aisearchindex` entry ("Business-unit default cascade") from `config/core-user-wizard-linking-role.json` (#2382, UAC-r2's guard), whose ratchet checks only that every bound set is declared, so a stale entry is not flagged. Dev's businessunit→sprk_matter map no longer carries the index (checked 2026-10-28: `businessunitid → sprk_securitybu` only). |

### 13.3 FR-38 — the server-owned search index (proposed)

- On a search-indexed table, a client-supplied `sprk_ai_search_index` is **not** a lookup the caller binds. The server **drops** it before step 5, so no AppendTo is asked on the catalog. Step 8 then applies the index of the record's **final owning BU** (FR-10; UAC-r2 condition 1).
  - "Final" means after the ownership step (194b's resolver): a Secure Record BU owner, the restricted-content team in the customer top BU, or a parent-inherited owner.
  - It is never the caller's BU and never the client's value.
- **The per-BU lookup stays** (owner's Q2 = (B), 2026-10-29). Customer environments have catalog rows but no BU defaults, so the lookup finds none and the column stays empty. It is the hook for a future per-BU index, such as Secure Records, at the cost of one lookup the server already does in step 8.
- **An owning BU with no index** (always the case for Secure Record today): the column stays **empty** and the server logs `search_index_unset_for_owner_bu`. It does not walk up to the root and does not use the caller's index.
  - Tests pin both cases: a secure record, and a BU with no index.
- **Every BFF write path, not only creates (UAC-r2 condition 2).** An update or PATCH through the BFF that carries `sprk_ai_search_index` gets the same drop, so a client cannot repoint an existing record. Native form edits stay out of scope.
- A dropped value that differs from the one the server applies is logged as `client_search_index_dropped`. This is the precedent of 018's `default_value_dropped`.
  - The log carries **ids only**: record, table, dropped index id, applied index id. No names or emails (UAC-r2 condition 4).
- Alternative (UAC-r2's other shape): accept a client value only when it equals the owning BU's index, and refuse otherwise (400). **Decided (owner, 2026-10-29): drop.** No product surface lets a user choose an index on purpose, and a refusal would break today's wizards until (e) ships.
- Not covered: native form edits of the lookup. Those are Dataverse's own role checks; out of scope, as for every native write (W5).

### 13.3b Business-unit moves (UAC-r2 condition 3)

**Answer: the index affects discoverability only, so a move is a known limit, not a recompute.**

- **Indexing:** `SearchIndexNameResolver` picks the index once, at indexing time: the document's index, then its parent's, then the parent's owning BU's, then the default.
- **Search:** the index searched never comes from the caller's BU. It is the tenant default, a session or deployment index, or a request-named allow-listed one.
- **Trimming:** the search paths trim hits by the caller's live Dataverse Read.
  - `/api/ai/rag/search`: `TrimToReadableDocumentsAsync`, one authorization check per hit.
  - Semantic search: per-row parent authorization (`AuthorizeRowsByParentAsync`).
  - `IndexRetrieveNode`: a per-row record-key check.
  - Every path also has a tenant filter.

So a moved record left in its old index:
- **is not disclosed** to someone who can no longer read it on those paths;
- **may be missed** by someone who now can, if they search a different index, until it is re-indexed.

Filed as a known limit (#2413). Not recomputed at a move.

**Open for UAC-r2 (not this project's change):** retrieval through `RagService.SearchAsync` alone (chat and playbook) filters by the caller's Entra `privilege_group_ids` and tenant. A per-row Dataverse Read check on those paths was not verified here.

### 13.3a FR-39 — the seeder's owner rules (UAC-r2 review, 2026-10-28)

- Resolve the team exactly as 194b's setup script does, and reuse 194b's team resolver if the seeder can reference it (no copy):
  - the BU is a **direct child of the root**, never the root or Secure Record;
  - the team is an **owner team** (teamtype 0), **not** a default team, with **exactly one** match.
- Refuse a team that has **members**. Membership and role drift are otherwise the setup script's `-Verify` job; no second verifier.
- **Set the owner on the create itself** (`ownerid@odata.bind` → `/teams(<id>)`). Never create-then-assign: a row is never owned by the provisioning identity.
- **Ordering.** The team and role setup runs **before H12b**. Without the role, Dataverse refuses the owner (0x80040299). The seeder then stops with a clear error and never retries under another owner.
- **Re-runs and upserts.** An existing row whose owner is not the team is **reassigned** to it, at least reported as drift; never left silently.
- Whatever creates `sprk_aisearchindex` rows in a customer environment (§13.5 question 2) follows the same rules.

### 13.4 Sequencing

(a) UAC sha → owner's go → (b) re-own on dev (this immediately fixes #2399) → (c) with task 017's route re-point (the child-records route then runs `RecordCreationService` step 8) → (e) in W3 → (d) with provisioning's next H12b change.

### 13.5 Open questions

1. ~~Owner: drop or refuse for a client index (§13.3).~~ **Decided by the owner 2026-10-29: drop.**
2. ~~Owner and UAC-r2: are `sprk_aisearchindex` rows wanted in customer environments?~~ **Decided by the owner 2026-10-29, revised the same day: (B) seed the canonical indexes, with no BU defaults.** (A) was first chosen and then withdrawn. The Semantic Search page's index picker (`aiSearchIndexService.listActiveSearchIndexes`, read as the user) needs the rows. With none it shows "No indexes configured for your role", and users can't pick among the 7 indexes.
   - Provisioning seeds one `sprk_aisearchindex` row per `CanonicalIndexCatalog` entry, owned by the configuration team (H7c): name, endpoint of the stamp's own service, target entity type, display name and order. `sprk_isdefault` is set on the row matching `KnowledgeIndexName`.
   - No business unit gets `sprk_ai_search_index`. FR-38 therefore leaves the record column empty, and indexing uses the settings default, as under (A).
   - `DataverseAllowedIndexesProvider` then reads the allow-list from the rows. The `AiSearch__AllowedIndexes` settings fix (H4b) stays as its fallback.
   - Dev keeps its rows (re-owned per §13.2 (b)). Provisioning's answer (2026-10-28): nothing creates them there. H2b creates the Azure AI Search index, not a Dataverse row, and ACME has 0 rows. The BFF's `DataverseAllowedIndexesProvider` falls back to appsettings.
   - Without rows, FR-38 leaves the column empty on every customer record, and indexing uses the appsettings fallback.
   - If rows are wanted per customer, provisioning seeds them in H12a, owned by the team.
   - Joint recommendation, provisioning + field-mapping (2026-10-28), **since withdrawn**: (A) no rows in customer environments. It missed the Semantic Search picker.
     - H2b creates the same 7 canonical indexes on each stamp's own AI Search service. Isolation comes from the service endpoint, so a row adds only a target for the BU lookup.
     - Rows (B) pay off only when one customer needs different indexes per BU. They would add a second source of truth and a dependency on the owner team.
     - Fix needed either way (provisioning owns it): stamps set no `AiSearch__AllowedIndexes`. The allow-list is therefore empty (`AiSearchOptions.cs:103`), and every path that names an index returns 400 `INDEX_NOT_ALLOWED` on customer stamps (ACME). Provisioning's H4b will write the allow-list from the same `CanonicalIndexCatalog` H2b provisions. Live stamps need their owner's OK.
     - Owner first chose (A), then (B) on 2026-10-29 once the picker dependency was found (above).
3. **Owner (#2410):** Basic User reads rules but not profiles.
4. **Dev arrangement (UAC-r2, corrected 2026-10-29):** Deep reaches the user's own BU and every BU below it. Root-homed users DO read team-owned rows in BU1. The gap is the reverse: BU1 users can't reach root-owned rows. The owner chose to move dev's people into BU1; UAC-r2 plans that, and the dry run goes to the owner before any write.

### 13.6 Component justification (§11)

- **FR-38.** *Existing:* FR-10's fill-only default at step 8 (`RecordCreationService.SearchIndexDefaultAsync`). *Extension:* yes. The same step owns the column, plus a drop before step 5; no new service. *Cost of doing nothing:* every child-BU user's create is refused whenever their BU names an index the caller cannot append to (#2399), and a client can route a record into another BU's index.
- **FR-39.** *Existing:* H12b's seeder. *Extension:* yes, an owner bind on its creates. *Cost of doing nothing:* every seeded mapping row lands owned by the provisioning identity's BU instead of the customer team, which is the #2399 defect for mapping configuration.

## 14. The search index follows the content type, never the user (addendum 2026-11-02, #2502; DRAFT for UAC-r2 and provisioning review)

> Draft for `design.md`. Binding owner decisions: `notes/decisions.md` "2026-11-01 — #2502 …" and "2026-11-02 — Owner accepts the three recommendations" item 1; `notes/defer-issues.md` DI-012. They are not re-opened here. Line numbers are on `work/field-mapping-server-write-path-r1` at 4fdb0a5c99 (master merged).

### 14.1 Why

- **A caller chooses where a file is indexed (DI-012).** `POST /api/ai/rag/index-file` carries the body's `searchIndexName` into the request (`RagEndpoints.cs:582`, `request with { TenantId }`) and on to `FileIndexingService` (`:140`, `:224`, `:272`) → `RagService.IndexDocumentsBatchAsync` (`RagService.cs:657-659`). Any allow-listed index is accepted. The route then stamps `sprk_searchindexname` with `GetDefaultIndexName()` (`RagEndpoints.cs:638-648`), so the record can name an index the file is not in.
- **Per-record and per-BU routing is a second chooser.** `SearchIndexNameResolver.ResolveAsync` (`:138-225`) walks the document → the parent → the parent's owning BU. FR-10/FR-38 step 8 copies the BU's index onto new records. The wizards copy the *user's* BU index onto records and into index-file bodies. On dev every BU that has an index names `spaarke-files-index`, which equals `KnowledgeIndexName`. Customer BUs have none, and each customer has its own search service with the fixed names. The routing changes nothing today. It costs two Dataverse reads per create and up to three per indexing job, and it is the source of #2399, #2206 and #2413.

**Owner rule (2026-11-01):** users never choose the index. The index is decided by what is being indexed.

### 14.2 The rule

1. The server picks the index for each content type from settings: SPE document chunks → `AiSearch:KnowledgeIndexName` (`spaarke-files-index`; discovery-tier chunks → `DiscoveryIndexName`), chat session files → `SessionFilesIndexName`, invoices → the invoice handler's index, and so on (`AI-SEARCH-INDEX-CATALOG.md` §4). No request field, record column or business-unit column changes it.
2. The only SPE-document seam that still takes an index argument is `FileIndexingService`. Every caller of it gets the files index. Session files (`RagIndexingPipeline.cs:314`), discovery (`:156`) and invoices already follow rule 1.
3. `sprk_document.sprk_searchindexname` stays the **stamp** of where the file landed. It is written only by the indexing pipeline, app-only, and always equals the index actually written. `VisualizationService.cs:108`, `RelocatedFileIndexing.cs:109-111` and the relocator (`DocumentContainerRelocator.cs:2692`) read it, so it must be true.
4. Record columns `sprk_ai_search_index` / `sprk_searchindexname` on the six search-indexed tables: **no server writer** (step 8 and Repair stop), **no client writer** (task 039's drop stays), **no reader on the indexing path**. Existing values stay; nothing is backfilled or cleared.

### 14.3 Current inventory: who picks or stamps an index

| Site | Today | Source |
|---|---|---|
| `RagEndpoints.IndexFile` `:582`, `:620` | body `searchIndexName` → write | caller |
| `RagEndpoints.IndexFile` `:638-648` | stamp = `GetDefaultIndexName()` | settings (**mismatch** with the write) |
| `RagEndpoints.SendToIndex` `:1311-1312`, `:1329`, `:1361` | resolver → write, stamp = resolved ?? default | record → parent → BU |
| `RagIndexingJobHandler` `:143-151`, `:184-191`, `:249` | payload, else resolver; INDEX_NOT_ALLOWED → retry with null; stamp = resolved ?? default | payload/record/BU (**mismatch** after the retry: the stamp names the rejected index) |
| `BulkRagIndexingJobHandler` `:291-306`, `:333-339` | resolver → write; retry with null | record → parent → BU |
| `IndexingWorkerHostedService` `:159-173`, `:196` | resolver(document only) → write | record |
| `PostUploadIndexingEnqueuer` `:194`, `:338`, `:414-416` | request value (all 8 producers pass `null`); OBO stamp = `AnalysisOptions.SharedIndexName` | settings, but the **deprecated** one (G-9 split if it differs from `KnowledgeIndexName`) |
| `RecordCreationService` step 8 `:749-760`, `:1365-1447` | copies the final owning BU's two columns | BU |
| `RecordCreationService.Repair` `:618-700` | fills unset columns from the owning BU | BU |
| `KnowledgeDeploymentService` `:124-131`, `:306` | `null` index → Shared deployment → `KnowledgeIndexName` | settings |
| Search: `RagEndpoints.Search` `:274`, `SemanticSearchService` `:144`, `:286`, `RecordSearchService` `:235-249` | request-named index, allow-listed; trimmed by live Read (§13.3b) | caller, search-time only |

### 14.4 Components changed (task 041; BFF only)

| # | File | Change | Reason |
|---|---|---|---|
| 1 | `Services/Ai/IFileIndexingService.cs` (`FileIndexRequest:162`, `ContentIndexRequest:262`), `Jobs/Handlers/RagIndexingJobHandler.cs:436` (payload), `Services/Ai/IPostUploadIndexingEnqueuer.cs:145` | **Remove** the `SearchIndexName` property/parameter | A type check enforces the rule (enforcement ladder: type): no producer can name an index. JSON bodies and queued payloads that still carry it bind fine; unknown members are ignored, and none of these DTOs sets `JsonUnmappedMemberHandling.Disallow`. |
| 2 | `Services/Ai/FileIndexingService.cs` `:140`, `:224`, `:272`, `:313`, `:384`, `:410` | Pass `searchIndexName: null` to `IndexDocumentsBatchAsync` / `DeleteChunksBeyondCountAsync`; drop the parameter from `IndexTextInternalAsync` | One seam, one index: the Shared deployment default (`KnowledgeIndexName`). |
| 3 | `Api/Ai/RagEndpoints.cs` IndexFile `:582-648`; SendToIndex `:1301-1371` | IndexFile: nothing to strip once (1) lands. SendToIndex: delete the resolver call. Both stamp `GetDefaultIndexName()` | Removes the caller chooser and the record/BU chooser; the stamp equals the write. |
| 4 | `RagIndexingJobHandler.cs:137-192`, `:249`; `BulkRagIndexingJobHandler.cs:286-339`; `Workers/Office/IndexingWorkerHostedService.cs:155-200` | Delete the resolver calls and the INDEX_NOT_ALLOWED retry branches; stamp `GetDefaultIndexName()` | The indexer stops reading record and BU columns (owner). Closes the retry stamp mismatch. |
| 5 | `Services/Ai/PostUploadIndexingEnqueuer.cs:414-416` | Stamp `ISearchIndexNameResolver.GetDefaultIndexName()`, not `AnalysisOptions.SharedIndexName` | The write uses `KnowledgeIndexName`; the stamp must too (G-9). The resolver is registered scoped and unconditionally (`AnalysisServicesModule.cs:65`); the enqueuer is scoped (`:110`). |
| 6 | 8 producers passing `SearchIndexName: null` (`AnalysisEndpoints.cs:997`, `ExternalProjectDataEndpoints.cs:2011`, `AnalysisResultPersistence.cs:274`, `RelocatedFileIndexing.cs:74`, `CommunicationEnrichmentService.cs:394`, `IncomingCommunicationProcessor.cs:1498`, `ComposeService.cs:2300`, `UploadFinalizationWorker.cs:1432`) | Delete that one argument line | Follows from (1). Hot-path files: run `/conflict-check`. |
| 7 | `Services/Ai/SearchIndexNameResolver.cs` | Delete `ResolveAsync` and its FetchXml helpers; keep `GetDefaultIndexName()` (the G-9 single point) | No caller is left. |
| 8 | `Services/RecordCreation/RecordCreationService.cs` step 8 `:749-760`, `:1331-1447`, `:295`, `:301`, `:787-789` | **Keep the step as a recorded no-op** (`NoOp`, "the index follows the content type (#2502); nothing written"). Delete `SearchIndexFill`, `BusinessUnitSearchDefaultsAsync` and the step-8 warning. Keep `TeamBusinessUnitAsync` (step 7 uses it, `:1262`) | Step numbers 9–12 are cited in the spec, the docs and the trace tests. A no-op keeps them stable and reads nothing. Saves two Dataverse reads per create (NFR-02). |
| 9 | `RecordCreationService.Repair.cs:132`, `:611-700`, reason codes `:950`, `:971-972`, part `:844` | Delete `RepairSearchIndexAsync` and its three reason codes. **Keep** `:342-343` (mapping may not write the columns in Repair) | Repair is internal and has no production caller yet (052 is 🔲), so the report shape is free to change. |
| 10 | `RecordTableRules.cs:169-175` (comments), `:504`, `:512-524`, `:620-657`, `:754-780`; `Api/DataverseDocumentsEndpoints.cs:151-167` | **Keep the 039 drop.** Move `LookupColumn` / `LegacyTextColumn` here from the resolver; fix the comments that call step 8 "the only writer"; `applied` is now always none, so every client value is logged as `client_search_index_dropped` (ids only) | The columns stay server-owned, so no client can bring routing back. This also removes a CRUD → AI-internal type reference (ADR-013). |
| 11 | `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md:135` (I-5), `docs/architecture/AI-SEARCH-INDEX-CATALOG.md` §4 | I-5 becomes "no server writer; client values dropped; the stamp is the pipeline's"; the catalog gets a routing paragraph (rule 14.2) | Code wins, and the docs are fixed with it. The runbook `MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md` goes to 062. |

**Out of 041, in W3 (021–029: the client stops sending; the server already ignores or drops the values):**
- `EntityCreationService.ts` (`:227-279` index-file `searchIndexName`; `:368-401` `applyDefaultSearchIndexName`; `:441-461` `resolveUserBuDefaults` select) is shared by all of the following.
- `CreateMatterWizard/matterService.ts:298-302` (bind) and `:403-412`.
- `CreateProjectWizard/projectService.ts:322-326` and `CreateProjectWizard.tsx:858-867`.
- `CreateEventWizard/eventService.ts:237-267`; `CreateInvoiceWizard/invoiceService.ts:214-217`; `CreateWorkAssignmentWizard/workAssignmentService.ts:415-416`.
- `document-upload/DocumentRecordService.ts:226-232`, `:379-386`; `DocumentUploadWizard/src/services/uploadOrchestrator.ts:347-386`, `:454`, `:499-607`; `DocumentUploadWizard/src/components/searchIndexResolver.ts:102-126` (029).
- `EmailComposer/createXrmEmailComposeHandlers.ts:281`, `:308-310`. **No W3 task names it.** Proposed: 028 (email).
- `Spaarke.SdapClient/src/types/index.ts:187` and `operations/IndexFileOperation.ts:51` are removed after the last wizard (035).

### 14.5 What stays

- The `sprk_aisearchindex` catalog. Provisioning seeds it (H12b; `CanonicalIndexCatalog`, 7 names, no BU default). `DataverseAllowedIndexesProvider` and the Semantic Search picker (`aiSearchIndexService.ts`, `SearchIndexSelector.tsx`) read it.
- Search-time index selection on the search routes: allow-listed, never from the caller's BU, trimmed by live Read (§13.3b). This is not indexing.
- Read-only consumers of the record columns:
  - the PCF `SearchIndexResolver.ts` → `SemanticSearchControl.tsx:543` → `NavigationService.ts:168-170`, which reads the host record's lookup to pick the index searched. Existing values equal the default; new records have none, so the default applies;
  - the Office profile display (`useDocumentProfile.ts:150`).
- Existing column values on every row; BU columns; the Core User role. Removing `prvAppendTosprk_AISearchIndex` stays §13.2 (e)'s question.
- The stamp columns and the relocator's cleanup of the old index plus the default index.
- The `ISearchIndexNameResolver` registration.

### 14.6 Compatibility and rollout

- **No client breaks.** Clients that still send `searchIndexName` to index-file get 200, with the chunks in the files index; the field is ignored at binding. A body naming a non-allow-listed index used to get 400 `INDEX_NOT_ALLOWED`; it now gets 200 (a relaxation). Record payloads keep being dropped (039).
- **No behaviour change on dev or customers today**, because every routed value already equals `KnowledgeIndexName`. The exception is documents whose stamp names another index; 041 step 1 counts them first.
- **Order:** 019 (W2 deploy and review, in progress) → **041** (spec amendment → UAC-r2 review → task → owner approval) → 021–029.
  - W3 cannot verify "the wizard sends no index" against a server that still copies the BU's value, so 041 goes first.
  - 041 also amends these acceptance lines, which now say "the owning-BU search index present at create": 029 `:23`, `:75`, `:82`, `:89`; 030 `:22`, `:110`; 036 `:23`, `:103`; 052 `:22`; 053 `:88`. The new reading is "no index column written by the server; the document stamp names the files index after indexing".
- **Queued jobs** enqueued before the deploy carry `SearchIndexName: null` (every producer passes null), so they are unaffected.

### 14.7 Test plan (ADR-038: update the tests that pin routing; delete only those whose subject is gone)

**Must-fire (new or inverted)**
1. index-file with an allow-listed `searchIndexName: "spaarke-invoices-index"` → `RagService` gets no index (default); stamp = `KnowledgeIndexName`.
2. index-file naming a non-allow-listed index → 200, not 400; the default index is written.
3. SendToIndex on a document whose lookup, text column, parent or BU names another index → write and stamp are the default (inverts `RagSendToIndexIndexNameContractTests.cs:105`).
4. `RagIndexingJobHandler`, bulk handler and Office worker: a document/parent/BU naming another index → no catalog-link fetch is issued; write and stamp are the default.
5. Unified create on each of the six search-indexed tables, with an owning BU that **has** an index → neither column is in the create body; the step-8 trace is `NoOp`; no `businessunit` retrieve (rewrites `SearchIndexDefaultTests.cs:115`, `:125`, `:135`, `:154`, `:197`, `:268`).
6. Repair of a row missing both columns, owner settled → no index key in the patch (rewrites `RecordRepairModeTests.cs:229`, `:264`).
7. OBO enqueuer with `SharedIndexName ≠ KnowledgeIndexName` → stamp = `KnowledgeIndexName` (rewrites `PostUploadIndexingEnqueuerTests.cs:158`).

**Must-not-fire (unchanged behaviour)**
1. index-file with no index, and the skipped (e-billing) file: same write, same stamp, and no stamp when skipped.
2. index-file authorization: tenant 403, row mismatch 409, knowledge-source 400 (existing contract tests pass unchanged).
3. A create or update never sends either column as `null`. Existing values are untouched (absent from the body, not cleared).
4. The 039 drop and its logs (`SearchIndexDefaultTests.cs:228-359` drop half, `RecordCreationServiceMappingSeamTests.Task039.cs`, `SearchIndexGenericUpdatePathTests.cs`, `SecureChildOwnershipAiToolTests.SearchIndex*.cs`) and `Spaarke.ArchTests/SearchIndexDropGuardTests.cs`.
5. Search-time selection (`SearchIndexNameEndpointContractTests.cs`, `RagEndpointsTests.cs:105-174`).
6. Relocation cleanup (`RelocatedFileIndexingTests.cs:71`, `:137`), session-files and discovery routing.

**Deleted (the subject is gone)**
- `SearchIndexNameResolverTests.cs:89-326` (9 `ResolveAsync` tests; the two `GetDefaultIndexName` tests stay).
- `RagIndexingJobHandlerTests.cs:490`, `:522`, `:558`.
- `FileIndexingServiceTests.cs:459`, `:506` (thread-through).
- `SearchIndexDefaultTests.cs:280` and `RecordRepairModeTests.cs:166`, `:210`, `:364` (BU-has-none and unsettled-owner cases: nothing is filled any more, so they fold into must-fire 5 and 6).

**Compile-only updates (mocks of `ResolveAsync`)**
- `BulkRagIndexingJobHandlerReplaceStaleChunksTests.cs`, `VersionSaveAiRefreshSeamTests.cs`, `RelocatedFileIndexingTests.cs`.
- Office quick-create contract tests and the pipeline/secure-root seam tests, where they assert index columns on the create.

### 14.8 ADR checks, placement and component justification

| ADR | Result |
|---|---|
| ADR-002 | **Comply.** No plugin. One owner per invariant: "the chunks' index and the stamp agree" is owned by `FileIndexingService` plus the stamp sites. The record columns hold no invariant any more, and the drop keeps clients out. |
| ADR-008 (path A, this project) | **Comply.** `CallerWriteAuthorization` and the index-file and send-to-index endpoint filters are unchanged. The caller controls one input fewer. No AppendTo on the catalog (already 039). |
| ADR-010 | **Comply.** No new registration; one constructor swaps `IOptions<AnalysisOptions>` for the registered resolver. Removes Dataverse reads. |
| ADR-013 | **Comply, improved.** AI routing stays in `Services/Ai`. `RecordCreation` stops referencing `SearchIndexNameResolver` (the constants move to `RecordTableRules`). |
| ADR-038 | **Comply.** §14.7 updates the tests that pin routing, adds tests for the new behaviour, and deletes only tests whose subject is gone. |
| BFF §10 | In the BFF, where the code already lives. Modifications and deletions only. Publish-size delta is expected at ≈ 0 and is measured against a fresh master build. No package change. |
| §11 | **No new surface.** No endpoint, service, interface, registration, column or package. The moved constants are a relocation. The three questions do not apply. |

### 14.9 Risks

- **Chunks left in a non-default index.** A document indexed under a per-record or caller-named index keeps those chunks there; a re-index writes the files index and trims only there. `privilege_group_ids` is only a pre-filter. The access boundary is `IRetrievalAccessTrim` / `TrimToReadableDocumentsAsync` in each consumer: `RagEndpoints` search, `SemanticSearchEndpoints`, `ReadableRagSearch`, `AnalysisRagProcessor`, `DocumentSearchHandler` and `PlaybookChatContextProvider`. `RecallSessionFileHandler` is scoped to the session (UAC-r2, 2026-11-02). Mitigation: 041 step 1 counts stamps ≠ `spaarke-files-index` on dev and records the count. **Owner 2026-11-02: leave the old chunks;** they are not cleaned.
- **The Dedicated deployment model** (`{tenant}-knowledge`, "do not configure", #1432) would make the `null` write differ from `GetDefaultIndexName()`. This is a known limit and is unchanged.
- **Two settings name the files index** (`KnowledgeIndexName`, `FilesIndexName`; `AiSearchOptions.cs:14`, `:42`). Drift is possible and pre-exists this change; not changed here.
- **Hot-path churn** (RagEndpoints, Compose, Communication, ExternalAccess): one-line deletions; run `/conflict-check` before the PR.
- **The hook is lost.** A future Secure Records index would need a new rule (§14.10 Q1), not a BU column.

### 14.10 Open questions

1. **Owner: Secure Records.** On 2026-10-27 the owner said "no per-BU search except Secure Records". Nothing implements a Secure Records index or plans one:
   - the Secure Record BU has no index (§13.3);
   - `CanonicalIndexCatalog` has 7 names and none is secure;
   - provisioning's H12b seeds rows with no BU default;
   - `infrastructure/ai-search/` has no secure index.

   **Recommendation:** treat the carve-out as withdrawn by the 2026-11-01 rule. If a separate index is ever wanted, the server chooses it from the record's secure state, with its own design and UAC-r2 review, never from BU columns. Withdrawn, or a future requirement?
2. **UAC-r2:** with nothing applied, `client_search_index_dropped` now fires for every client value until W3 lands (ids only). Keep it at Information as the W3 progress signal, or lower it to Debug?
3. **UAC-r2:** do #2206 (stamp not re-derived on owner change) and #2413 (BU move known limit) close with 041? Under rule 14.2 the record's owner no longer affects routing.
4. **Owner:** if the dev pre-check finds documents stamped into a non-default index, clean their old chunks, or leave them?

### 14.11 Proposed task 041 — Index routing follows the content type (#2502)

- **Deps:** 039 ✅; after 019 (do not disturb its review set). **Blocks:** 021 (W3 verification). It amends the acceptance of 029, 030, 036, 052 and 053 (§14.6).
- **Rigor:** FULL. Model sonnet / high. Tags: `bff`, `ai-indexing`, `testing`. One adversarial pass.
- **Scope:** §14.4 rows 1–11 only. No client file, no schema, no data write.

**Steps**
1. Dev read-only pre-check via the Dataverse MCP: `sprk_document` count with `sprk_searchindexname` set and ≠ `spaarke-files-index`; per table (6), rows whose `_sprk_ai_search_index_value` ≠ the `spaarke-files-index` catalog row; BUs with an index. Record the results in the notes.
2. Write must-fire 1–7 (failing first). Then make changes 1–11, the docs and the test updates in §14.7.
3. Build, `dotnet test`, publish-size delta against a fresh master build, `dotnet list package --vulnerable --include-transitive`.

**Acceptance criteria**
- AC1: index-file with any `searchIndexName` (allow-listed or not, or absent) writes to `KnowledgeIndexName` and stamps the same name; never 400 `INDEX_NOT_ALLOWED` for a body value.
- AC2: SendToIndex, the RAG job handler, the bulk handler and the Office worker issue no Dataverse read of `sprk_ai_search_index` / `sprk_searchindexname` on any record or BU, and write and stamp `KnowledgeIndexName`.
- AC3: `FileIndexRequest`, `ContentIndexRequest`, `RagIndexingJobPayload` and `PostUploadIndexingRequest` have no index field. `grep -rn "ResolveAsync" Services/Ai/SearchIndexNameResolver.cs` returns nothing.
- AC4: a unified create on each of the six search-indexed tables, with an owning BU that has an index, sends neither column; trace step 8 = `NoOp`; no `businessunit` retrieve.
- AC5: Repair writes neither column, on any row.
- AC6 (negative): no create, update or Repair body contains either column with `null`. Existing values are unchanged on read-back in the seam tests.
- AC7 (negative): the 039 drop tests, `SearchIndexDropGuardTests`, the index-file authorization contract tests, the search-time selection tests and the relocation cleanup tests pass **unchanged**.
- AC8 (negative): session-files, discovery and invoice index routing are unchanged (existing tests pass).
- AC9: the OBO enqueuer stamp equals `KnowledgeIndexName` when `SharedIndexName` differs.
- AC10: I-5 and the catalog doc are updated in the same PR. The publish-size delta and the CVE result are recorded. No new HIGH CVE.

### 14.12 Reviews (2026-11-02)

**UAC-r2: OK.** Index routing was never an access control; the per-caller trim on live Read is the boundary (see above). Conditions:
- **Log level.** `client_search_index_dropped` goes to **Information** (039 shipped it at Warning) and stays there as the W3 progress signal, ids only. When W3 lands it goes back to **Warning**. This is a W3 acceptance criterion: from then on, any firing is a client regression.
- **#2206 and #2413.** Both close on 041's merge, with a link to the PR. Close them only after 041 step 1's count is recorded. (Dev fact: the BU1 move ran on 2026-11-02.)
- **Search routes.** Keep a test showing that a request-named index outside the allow-list is still refused on the SEARCH routes.
- **Interaction with 042b (§15.5).** §15.5's step-8 bullet, "a secure event gets an empty index plus `search_index_unset_for_owner_bu`", is moot once step 8 is a no-op. Whichever of 041 and 042b lands second updates that bullet and its test.
- **Review before merge.** UAC-r2 does a scoped check of 041's diff before merge, confirming that only routing is removed and no trim call moves (RagEndpoints, Communication, ExternalAccess).
- **Accepted.** The 400 → 200 change on index-file is acceptable.

**Secure Records carve-out.** The owner deferred this to UAC-r2. UAC-r2's answer: a separate index is not needed, because secure isolation rests on Read and the trim enforces it. The carve-out is **withdrawn**. If a secure index is ever wanted, the server chooses it from the record's secure state, with its own design and a UAC-r2 review, never from BU columns.

**Provisioning: confirmed, no dependency.** H10, H2b, H4b and H13 never read a BU index default. H12b (not built) seeds one catalog row per verified index, with `sprk_isdefault` on `spaarke-files-index` and no BU default. 041 must not change what `sprk_isdefault` means on the catalog row.
- **Added to 041's scope:** retire or update `scripts/backfill-multi-container-multi-index/Audit-MultiContainerMultiIndex-Drift.ps1`, which reads `businessunits.sprk_searchindexname`. Once routing is retired, its "drift" means nothing.

**Owner (2026-11-02):** documents found in a non-default index are **left** as they are; nothing is cleaned.

## 15. The owner decision sees the derived stamp's root (addendum 2026-11-02, #2483 / DI-009; DRAFT for UAC-r2 review)

Binding, not re-opened: notes/decisions.md 2026-11-02 item 3; DI-009; #2483 and its two comments (UAC-r2 facts 2026-10-30; owner decision 2026-11-02). Line numbers are at `4fdb0a5c99` (master merged).

### 15.1 The defect

- **Where.** `RecordCreationService.DecideAsync` decides the owner at step 7 (`RecordCreationService.cs:730-747`) over the item: `OwnedChildWrite.LookupsOf(item)`, the project pair parent and a work assignment's intermediates (`ResolveOwnerOverAsync`, `:1030-1067`, through `OwnerQuestionOf` → `RecordOwnershipContext.ForRow`, `:1074-1082`).
- Step 10 (`:771-777`, `StampBeforeCreateAsync` `:806-911`) derives the I-1 stamp fill-only (`CoreAncestorRestamper.PlanMissingStampAsync`, `CoreAncestorRestamper.cs:525-569`, app-only) and binds it into `fields` only. The item, which steps 7 and 12 read, does not change (`:802-805`).
- **Why secure-if-any misses the root.** The resolver looks through only parents that are NOT team-owned: the primary look-through (`RecordOwnershipResolver.cs:3715-3718`) and the lineage walk in `SecureDecisionAsync` (`:3836-3843`). A team-owned intermediate is taken at its own business unit (`:3796-3798`).
- **Scenario.** Matter MAT-1 is made secure. It is now owned by the Secure Record Owners team in the Secure Record BU, with `sprk_issecure = true`. Invoice INV-1 (`sprk_matter = MAT-1`) is still owned by the Litigation BU's default team, because the Make Secure pass has not reached it yet or ended incomplete.
  1. A user who can AppendTo INV-1 posts `POST /api/v1/child-records/sprk_todo` with `{ sprk_regardinginvoice: INV-1 }` and no stamp.
  2. Step 5 asks AppendTo on INV-1: allowed.
  3. Step 7 resolves the owner over [INV-1]. INV-1 is team-owned, so nothing above it is read, and the answer is the **Litigation** team.
  4. Step 10 derives `sprk_regardingmatter = MAT-1` into the body.
  5. The to-do is created in Litigation. The Litigation team can read it, and also INV-1.
  6. Within about 2–4 minutes, `SecureChildReconciliationJob`'s recent-changes pass (`*/2 * * * *`, `SecureChildReconciliationJob.cs:131`; writes on unless the emergency stop is set, `:140-143`) asks the written row's question. That question includes the stamp (`RecordOwnershipContext.ForExistingRowAsync` → `OrderedParentsOf`, `RecordOwnershipResolver.cs:437-441`), so the reconciler re-owns the to-do to the Secure team.
- **Reach (UAC-r2, #2483).** Affected: the route and the chat Owned create for to-do, event, memo and analysis, a filed communication, and a decision's "Add To Do". Not affected: `email.draft` (§15.5), documents (#2169, no pre-create stamp, `:810-811`), work assignments (the #2475 intermediates) and the budget inquiry.
- The job's own remarks say "in-product creates no longer open that window" (`SecureChildReconciliationJob.cs:56-57`). DI-009 is an in-product create that does.

### 15.2 Change 1, stated precisely (accepted by the owner 2026-11-02)

Today's create does not do this. The fix introduces it at create. The reconciler's question already gives the same answer.

- The derived stamp joins step 7's question under its real column (`sprk_regarding{matter|project|workassignment|servicerequest}`). It is therefore an ordinary member of the table's primary order (`RecordOwnershipContext.InPrimaryOrder`, `RecordOwnershipResolver.cs:515-572`), not a Secure-only parent.
- On a row with no subject (no pair on a regarding-builder host, `:562`; any row of event, memo, analysis or communication), the lineage order decides the primary. Where that order ranks the root column above the intermediate's column, an **ordinary** row's owner moves from the intermediate's BU default team to the root's.
- Under today's orders, that happens only for a row filed under a **report card**. `sprk_regardingreportcard` ranks after matter and project on to-do, event and communication, and memo's `sprk_reportcard` ranks last (`SecureChildLineage.cs:124`, `:174-175`, `:213-215`, `:246-247`). Every other intermediate ranks above the roots, and the work-assignment root ranks last. Signal and Decision Record keep the core, then the subject (`RecordOwnershipResolver.cs:548-559`). A to-do with its pair keeps its subject first.
- This is the reconciler's answer for the written row. The reconciler moves an ordinary row only when `OrdinaryMovesWritesEnabled` is on (`SecureChildReconciliationJob.cs:103-112`).
- **Owner rationale:** in the target model every record is owned by the customer's main BU or by Secure Record, so the move changes nothing there.

### 15.3 The fix: new step order

| # | Step | Change |
|---|---|---|
| 1–5½ | principal … pre-write rules | none (step 5's AppendTo set is unchanged; the derived root is never asked as the caller) |
| 6 | secure-root plan | none (stamped tables never `Inherits`, so `isolatedTeam` is always null for them) |
| **6½** | **I-1 derivation** (moved from 10) | `PlanMissingStampAsync` over the item, app-only, exactly as step 10 runs it today (`:818-850`). Refusals: 400 `stamp_underivable` and 503 `stamp_unavailable`, the same codes, words and trace step (`StampAndInheritance`, begun here). A document is skipped, as today. Runs for every principal. |
| 7 | owner | `DecideOwnerAsync` takes the FULL plan (`PlanStampOfRowAsync`, what the step-12 restamp will write). `ResolveOwnerOverAsync` asks over the lookups **as the row ends** (`AsTheRowEnds`): each stamp column the plan names takes the planned root (a held value that disagrees is REPLACED, a planned clear removes it, a missing one is added); every other lookup, and a held stamp that agrees, is unchanged. Same `OwnerQuestionOf` / `ForRow`. **Amended by 042b fix round 1 (owner decision A', 2026-11-03, "replace, not add"):** the first version was fill-only here, so a caller-sent stamp that disagreed with the intermediate's root decided the owner and the restamp then moved the row from under it (the #2483 state again). |
| 8 | search index | none in code; it reads the FINAL owning team, which may now be Secure (§15.5) |
| 9 | creator / defaults | none |
| 10 | **bind + I-16** | binds the MISSING part of the 6½ plan (`MissingOf`: fill-only, unchanged by A') as the caller (`BindStampAsync`, `:859-885`; 500 `no_decision` / 503 unchanged, still here) and sets I-16 from the same row (`:889-908`). No second derivation. A system principal still binds after the create (DI-007). A held stamp that disagrees is created as sent and replaced by the step-12 restamp, as before 042b. |
| 11–12 | create, post-create | none |

- **Shape:** split `StampBeforeCreateAsync` into a plan half (6½) and a bind/I-16 half (10). The plan is passed through `DecideAsync`'s locals. No new type, enum member, interface, registration or package.
- **A' (fix round 1):** `CoreAncestorRestamper.PlanStampOfRowAsync` returns the full plan (SET `EntityReference` / CLEAR `DBNull`, the restamp's own `PlanStamp`); `PlanMissingStampAsync` is now `MissingOf(row, PlanStampOfRowAsync(...))`, so its callers are unchanged. This is the one file outside 042b's list (owner-approved; component justification in `notes/task-042b-notes.md`). The bind stays fill-only; the derived root is never probed as the caller; no refusal changes.
- **Repair** (`RecordCreationService.Repair.cs:119-133`) has the same order: the owner is decided at `:121` and the stamp is added at `:133`. 042b plans the stamp once, before `RepairOwnerAsync`, asks the owner over `AsTheRowEnds(row lookups, plan)` (A', security finding 6: a stale held stamp no longer decides the re-own) and hands the missing part (`MissingOf`) to `RepairStamp`, still fill-only. If planning fails, the owner is kept (Skipped, `record_repair.stamp_unavailable`) and never re-owned over the lookups alone (Q2).

### 15.4 Refusal parity (NFR-07)

A refusal raised by a single condition keeps its code, status and words. Only the ORDER changes when two conditions fail at once, plus the new refusals (rows 5–7). The maps are unchanged: `RecordCreationProblemMap.cs:33-41` (400 Invalid / 409 OwnerUnresolved / 500 Undecided / 503 Unavailable).

| # | Conditions | Today (step 7 first) | After 042b (6½ first) | Reach |
|---|---|---|---|---|
| 1 | Root underivable (two roots, or gone) **and** step 7 refuses | 409 owner code | 400 `stamp_underivable` | a caller already passed step 5 AppendTo on the intermediate, so this needs a compound data fault |
| 2 | Root read faults **and** step 7 refuses | 409 | 503 `stamp_unavailable` | compound |
| 3 | Root read faults **and** the owner decision faults | 503 `owner_unavailable` | 503 `stamp_unavailable` | the same status; the code differs |
| 4 | System principal, intermediate missing (no step 5) | 409 `ParentUnresolved` | 400 `stamp_underivable` | no system creator exists (DI-007, owner task 033) |
| 5 | **New:** the root is flagged secure but not isolated | 201, owned ordinary | 409 `SecureParentNotIsolated` (`RecordOwnershipResolver.cs:3876-3889`) | UAC change 3, fail closed (Q1) |
| 6 | **New:** the root cannot be read at step 7 after 6½ read it (a race) | 201 | 409 `ParentUnresolved` | a race only |
| 7 | **New (change 1):** a report-card child whose root's BU has no default team | 201 (report card's team) | 409 | rare; the reconciler asks the same question (its outcome is to be confirmed by the parity test) |
| 8 | **New (042b addendum A):** a Signal or Decision Record under a team-owned intermediate of a Secure root | 201, owned ordinary | 409 `NoOwnerSource` (the existing `OwnerDecided` / `SecureFilingRefused` words; `OwnerDecision.IsSecureOwner` true) | fail closed; UAC-r2 agreed 2026-11-03. These two are not reparentable children, so the "every stamped table" bullet below does not hold for them; their own writers own them into isolation |
| 9 | **New (042b addendum B):** a create carrying `sprk_communicationthread` whose thread cannot be read at step 7 | 201 (the thread was not read) | 503 `owner_unavailable` | a race or fault only: step 5 already asks AppendTo on the thread as the caller |
| 10 | **New (042b addendum C, Repair):** a stamp-planning fault on a filed row whose owner is kept for another reason (for example team-owned) | the stamp Skipped, the mapping ran | the stamp Skipped AND the mapping Skipped (`record_repair.mapping_filing_undecided`) | fail closed by design: without the stamp's root the filing's owner is undecided. Repair has no production caller yet (052, DI-008). A permanent planning fault is reported as `stamp_unavailable` (K2: no new enum member) |
| 11 | **K3 (042b fix round 2, Repair window):** a row Repair re-owned per the PLANNED root still holds the stale stamp until `CoreAncestorStampReconciliationJob` restamps it (`*/5`, `CoreAncestorStampReconciliationJob.cs:74`, registered `MetadataServiceExtensions.cs:48`); `SecureChildReconciliationJob` (`*/2`, `SecureChildReconciliationJob.cs:131`, registered `ExternalAccessModule.cs:734`) is an independent schedule with no ordering between the two, so a reconcile can run first and ask over the stale stamp | — (before A' Repair asked over the stale stamp itself) | owner per the planned root; the stored stamp is stale until the restamp | the recent-changes pass only moves a child INTO isolation, so a Secure re-own is not undone there; only the SWEEP (`SecureChild:Reconciliation:WritesEnabled`, absent = report-only) could move it to the stale stamp's owner, and the next restamp + reconcile moves it back. No job change here; Repair has no production caller yet (052, DI-008) |
| 12 | **K3 (UAC-r2 AC15 note 2, the create twin of row 11):** a create whose item SENDS a stamp that disagrees with the intermediate's root keeps the sent bind (fill-only, step 10) while its owner follows the PLANNED root (step 7, A'); if the step-12 restamp does not complete (`RecordCreationService.cs` ~:1802), the row holds the sent stamp until `CoreAncestorStampReconciliationJob` restamps it (`*/5`, `CoreAncestorStampReconciliationJob.cs:74`, registered `MetadataServiceExtensions.cs:48`) | 201, owned per the SENT root outright | 201, owned per the planned root; the stored stamp is the sent one until the restamp | narrower than before 042b. Within the window only the `SecureChildReconciliationJob` SWEEP (`*/2`, `SecureChildReconciliationJob.cs:131`, registered `ExternalAccessModule.cs:734`; `SecureChild:Reconciliation:WritesEnabled`, absent = report-only) could move the owner to the sent root's owner; the next restamp + reconcile moves it back. No code change; UAC-r2 accepted 2026-11-04 |

- Unchanged:
  - the bind refusals (they stay at step 10);
  - the step-6 refusals;
  - `ownership_exception` (an unfiled row has no source, so nothing is derived);
  - the secure-filing refusals in `OwnerDecided` (`:1106-1121`): every stamped table in the access taxonomy is a reparentable child, so a Secure owner is allowed (Signal and Decision Record are not: row 8).
- **Edges:**
  - route: `ChildRecordEndpoints.RefusalResult`;
  - chat: the tool codes (`ChatCreatedChildStampTests.cs:310-345`);
  - `email.draft`: `EmailDraftToolHandler.cs:1296-1306`;
  - Office quick-create: roots only, so it never reaches 6½.
- **No new leak (UAC):** every row above follows step 5's AppendTo on the intermediate. A 400 says no more than step 10's 400 says today.

### 15.5 Interactions

- **System principal (#2479, DI-007):**
  - it already derives at step 10 (`:859-862`) and binds after the create;
  - after 042b it derives at 6½ and step 7 uses the result, so the owner is right before the stamp is bound;
  - DI-007 itself is unchanged.
- **`email.draft` (a′):**
  - the handler checks AppendTo as the caller on the regarding, derives `DeriveForHostAsync` (`EmailDraftToolHandler.cs:360-408`) and keeps the stamp in the item (`:446-448`);
  - so step 5 asks AppendTo on it, step 7 already sees it, and 6½ finds nothing missing;
  - the owner and the checks do not change. Only 6½'s root read moves ahead of step 7, as row 1–3 ordering.
- **Office:**
  - the Office to-do is its own writer;
  - it stamps first and then asks `ForChild(todo)` with the stamps (`OfficeService.cs:3030-3060`, `:3105-3119`), which is the shape 042b copies;
  - Office quick-create uses the service only for a matter or project (`:2431`), which are not stamped;
  - Office codes are unchanged (NFR-07).
- **Repair:** §15.3. It has no production caller yet (052, DI-008).
- **Reconciler:**
  - **After the fix it still has work:**
    - the intermediate itself (INV-1 stays ordinary until the pass reaches it);
    - rows that existed before the pass;
    - writes made outside the product;
    - documents.
  - **Agreement by construction:**
    - the create asks `ForRow(table, item lookups ∪ derived stamp, pair)`;
    - the reconciler asks `ForExistingRowAsync(written row)`;
    - the written row's ownership parents are exactly the item lookups plus the bound stamp. The creator-person and index lookups are not ownership parents.
    - 042b pins this with a parity test.
  - **Known difference beyond the stamp (to be confirmed by that test):** the reconciler and Repair add a message's record-thread filing (`SecureChildReconciler.cs:3040-3051`, `RecordCreationService.cs:1183-1187`), and the create does not (Q3).
- **Step 8 index (updated by task 042b, 2026-11-03):**
  - task 041 (FR-40, #2502) merged first: step 8 is NoOp. The create writes neither search-index column for any owner, so an event made Secure at create gets no index value from step 8, as an ordinary one does;
  - there is therefore no step-8 index assertion in 042b's must-fire tests (AC13);
  - the text this bullet held before 041 (an empty index plus `search_index_unset_for_owner_bu` for the Secure Record BU) no longer describes the code.

### 15.6 Test plan (ADR-038; KEEP paths; the characterization lands first, alone)

**042a: characterization (tests only, no `src/` change). It pins today's wrong owner.**

- **A1:** `RouteCreatePreCreateStampTests.cs` (the route harness, the real service, a `StampWorld` and `.WithSecureRoot("sprk_matter", SecureMatter)`). Name: `RouteCreate_AnEventUnderATeamOwnedEventOfASecureMatter_IsOwnedByTheEventsOrdinaryTeam_DI009Window`.
  - Setup: the Event is team-owned by `ChildTeam` in `ChildBu` and filed under `SecureMatter`. The stamp world derives `sprk_regardingmatter = SecureMatter`.
  - Pins:
    - 201;
    - `ownerid@odata.bind = /teams(ChildTeam)`;
    - `sprk_RegardingMatter@odata.bind = /sprk_matters(SecureMatter)`;
    - no sharee-mirror row on the new event.
  - The comment names #2483 and says that 042b inverts this test.
- **A2 (same file): the reconciler disagrees.**
  - Build the written row from A1's create body and ask the same resolver `ForExistingRowAsync`.
  - Pins: `SecureNamedTeam`. This is the window's disagreement in one assertion.
- **A3:** `RecordCreationServicePipelineSeamTests.PreCreateStamp.cs`. The same world as the system principal. Pins:
  - an ordinary owner;
  - no stamp in the body;
  - the stamp is bound by the post-create re-derivation (as `:178`).
- **Ordinary control (existing):** `ChatCreatedChildStampTests.cs:106` (an event under an event under an ORDINARY matter, owned by `ChildTeam`).

**042b: the fix. A1–A3 are inverted in the same PR; nothing is deleted.**

- **Must fire:**
  - A1/A3: owned by `SecureNamedTeam`; the sharee mirror is written at create (as `ChatCreatedChildStampTests.cs:130`); the event's index is empty and the warning is logged.
  - A chat twin in `ChatCreatedChildStampTests`.
  - A2 now agrees.
  - **Parity theory** (`WriterBuilderOwnershipParityTests` style): for every table in `RecordTableRules` that `IsStampedChildEntity` (to-do, event, memo, analysis, communication, Signal, Decision Record), with a secure world and an ordinary one, the create's owner equals `ResolveOwnerAsync(ForExistingRowAsync(written row))`.
  - **Change 1:** a to-do with no pair under a team-owned report card in BU X, whose matter is in BU Y, is owned by Y's team.
  - **Change 3:** a matter flagged secure but not isolated gives 409 `SecureParentNotIsolated` and no create.
  - **Order:** a root read fault combined with an owner world that refuses gives 503 `stamp_unavailable` (pins row 2).
  - **Repair:** a user-owned to-do missing its stamp under a team-owned invoice of a Secure matter is re-owned to Secure in the one update. If planning faults, the owner is kept (`stamp_unavailable`) and the mapping is Skipped (`mapping_filing_undecided`).
  - **A' (fix round 1, F1): a sent stamp that disagrees is replaced in the owner question.**
    - (a) a sent ORDINARY stamp under a team-owned intermediate of a Secure root is owned by `SecureNamedTeam`, with the sharee mirror at create (route, and a chat twin); the body keeps the sent value (fill-only) and the step-12 restamp replaces it;
    - (b) a sent SECURE matter under an intermediate whose root is ordinary is owned by the ordinary owner, never the Secure team, and gets no mirror;
    - Repair: a user-owned to-do holding a stale stamp whose source's root is Secure is re-owned to Secure (same unit) or held by the escalation (another unit), never re-owned to the ordinary team; the held stamp is not overwritten.
  - **Parity theory, caller principal (fix round 1):** memo, analysis and communication created as the caller bind the stamp in the create body (step 10) and agree with the reconciler.
- **Must not fire:**
  - `ChatCreatedChildStampTests.cs:106` stays green. Its comment at `:108-110` ("step 10 adds it to the create BODY only, so the owner question is unchanged") is rewritten.
  - The same report card with the to-do's pair set is owned by X. (Does not hold on this code: a report card is not a server to-do regarding-builder target, so the pair names no subject; 042b pins parity with the reconciler instead. Filed as #2556 / DI-017.)
  - Step 5's AppendTo probes are identical. The derived root is never asked as the caller.
  - `EmailDraftRecordCreationTests`: the owner and the AppendTo calls are unchanged.
  - A browser-sent stamp that AGREES with the intermediate's root: the plan is empty, nothing is bound, and the owner and the question are unchanged (AC12 as amended: "a sent stamp that agrees changes nothing; one that disagrees is replaced in the owner question"). Step 6½ still reads the intermediate once when a stamp is sent (AC1 as amended).
  - A document (seam `:229`) and a work assignment (seam `:64`) derive nothing.
  - Single-condition refusals are unchanged: `RouteCreatePreCreateStampTests.cs:64,81`, seam `:84-148`, `ChildRecordCreateRefusalContractTests`, `CrossSurfaceOwnerTests.cs:78-144`, and the Office suites (`OfficeRecordOwnershipTests`, `SecureRootCreateRouteTests.Office*`).

### 15.7 ADR checks, placement (§10) and component justification (§11)

| ADR | Point | Resolution |
|---|---|---|
| ADR-008 path A | Caller checks as the user, create app-only | Comply. Step 5's set is unchanged. The derivation stays app-only, as `PlanMissingStampAsync` already is (`CoreAncestorRestamper.cs:41`), and its answers are never shown before the caller's AppendTo on the intermediate. |
| ADR-010 | DI | Comply. There is no new registration; `_restamper` is already injected. |
| ADR-002 | No plugins; one server-side owner per invariant | Comply. It strengthens the rule: the create and the reconciler ask ONE question, built by `ForRow`, and a parity test proves it. |
| ADR-038 | Tests | Comply. The characterization lands first, alone (042a), then the inversion with the fix (042b). Tests are at KEEP paths (`data-mutation`, `seam`). No `Mock<HttpMessageHandler>`. |

- **Placement:** a modification inside `RecordCreationService` (and `.Repair.cs`) in the BFF. There is no new endpoint, service, package or background work. The publish-size delta is measured per §10 bullet 4 (expected ≈ 0).
- **§11:** no new surface. The two private halves of an existing method and one extra argument to a private method are a pure modification.
- **Hot path:** BFF Y, unchanged.

### 15.8 Risks

- **The creator may lose sight of their own row.** A caller who is not a sharee of the Secure matter creates a to-do they then cannot read. This is the intended outcome; it is the same as filing directly under a Secure matter today.
- **Cost:** none added. The derivation's root read moves ahead of step 7; it is not repeated.
- **More 409s during Make Secure** if the flag is set before the move (row 5, Q1).
- **The parity theory may expose other differences between the create and the reconciler** (the thread filing, Q3). Each one is fixed in 042b or filed. None is suppressed.
- **The stale comment at `ChatCreatedChildStampTests.cs:108-110`** would mislead a later reader if it is not rewritten. It is listed under 042b's must-not-fire tests.

### 15.9 Questions for UAC-r2

1. **Make Secure order.** In a normal Make Secure pass, is there an interval where the root is flagged `sprk_issecure` but not yet owned in the Secure Record BU? If so, 042b turns that interval's ordinary creates into 409 `SecureParentNotIsolated` (row 5). Is that acceptable, or should a root that is mid-Make-Secure be named the way `UnsecuringRoot` is for the reverse transition?
2. **Repair.** Do you accept the same reorder in `RepairAsync`, with a planning failure keeping the current owner (`stamp_unavailable`, no re-own), rather than re-owning over the row's lookups alone?
3. **Thread filing.** The reconciler and Repair add a message's record-thread filing to its question; the create does not. If 042b's parity theory confirms that a create can carry `sprk_communicationthread` today, should it be fixed in 042b or filed as its own issue?

### 15.10 Tasks (scope path: this design → UAC-r2 review → spec amendment → task files → TASK-INDEX → owner approval)

**042a: characterization of the #2483 window** (tests only; sonnet / high; STANDARD, plus code-review and adr-check because it touches `tests/**`)

- **AC1:** A1, A2 and A3 (§15.6) are added and pass on the current `src/`. `git diff --stat` shows no `src/` change.
- **AC2:** A1 pins all four facts: 201, the `ChildTeam` owner, the Secure matter stamp in the body, and no mirror row. A2 pins `SecureNamedTeam`.
- **AC3 (negative):** A1 fails if `ResolveOwnerOverAsync` is given the derived stamp. This is verified once by a local scratch edit that is never committed, and recorded in the notes.
- **AC4:** the existing ordinary control `ChatCreatedChildStampTests.cs:106` is unchanged and green.

**042b: derive the stamp before step 7** (depends on 042a merged and on UAC-r2's answers to Q1–Q3; sonnet / xhigh; FULL; reviewed by UAC-r2)

- **AC1:** the new order in §15.3. Step 10 does not derive a second time. The bind, I-16 and the system-principal post-create bind are unchanged.
- **AC2:** A1–A3 are inverted, and every must-fire test in §15.6 passes.
- **AC3 (negative):** every must-not-fire test in §15.6 passes. Specifically:
  - no new AppendTo probe;
  - `email.draft` is unchanged;
  - single-condition refusal codes, statuses and words are unchanged;
  - Office codes are unchanged.
- **AC4:** the parity theory passes for every stamped table registered in `RecordTableRules`. Any other difference it finds is fixed here or filed with a DI entry (Q3).
- **AC5:** the refusal-order rows 1–7 of §15.4 are recorded as K lines in the PR. Rows 2 and 5 are covered by tests.
- **AC6:** the publish-size delta is measured against a fresh master build. The UAC-r2 review line is in the PR. DI-009 is closed, and #2483 is closed with a link to the PR.

### 15.11 UAC-r2 review (2026-11-02): OK with conditions

UAC-r2 confirms the design: step 7 asks the reconciler's question, built once by `ForRow`. There is no caller-side probe and no new surface, and the G5 split is kept. The derivation stays app-only and is never exposed before step 5's AppendTo on the intermediate. Rows 6 and 7 fail closed, which is correct.

- **Q1, the Make Secure order.**
  - **The interval exists.** Make Secure (`ProvisionProjectEndpoint.cs`, class doc) sets `sprk_issecure` first at 4.1 and assigns the Secure team only at step 5. A failed or compensated run (5.5) can leave that state in place for good.
  - **Row 5 is accepted** as 409 `SecureParentNotIsolated`. This is the existing C11 refusal, which direct children already get. There is **no** "SecuringRoot" exemption, because one would give a child an ordinary owner while its family goes secure.
  - **Condition (a):** two tests for row 5, one for the transient interval and one for the lasting compensated state.
  - **Condition (b):** keep the existing refusal words ("re-run Make Secure on it, then retry"). A friendlier transient message can only be a client-side mapping; no new server code.
- **Q2, Repair.**
  - **The same reorder is accepted.** A planning failure keeps the current owner (Skipped, `record_repair.stamp_unavailable`) and never re-owns over the row's lookups alone.
  - **Condition (c):** the skip is counted in the repair result and logged with the row id and reason, never silently.
- **Q3, thread filing.**
  - **Fix it in 042b** if the parity theory confirms that a create can carry `sprk_communicationthread`. The fix goes inside `OwnerQuestionOf` / `ForRow`, the way `RecordCreationService.cs:1183-1187` already handles Repair.
  - **File it** as its own issue, with a DI entry, only if it needs a different seam.
  - **Either way,** AC4's parity theory covers a communication that carries a thread.
- **Row 7:** the parity test confirms that the reconciler refuses the same report-card case. If the two disagree, fix it in 042b.
- **§15.8, creator visibility:** this is the intended outcome. The create response stays the normal 201 and reveals no secure-family id beyond what the caller sent. Add an assertion for this.
- **042b review:** UAC-r2 gets the diff before merge and does a full pass, since the change touches the owner decision on secure families.
