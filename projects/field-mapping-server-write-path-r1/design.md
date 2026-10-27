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
- **Service requests:** a service request is a core record with its own family, outside the Secure family.
  - Expected behaviour, not a bug: a budget inquiry creates a `sprk_servicerequest` regarding the matter. Because the service request is its own root, the matter's Family view does not list the inquiries sent from it.
- **Ontology tables (ontology review, 2026-10-27; already in `SecureChildLineage`):**
  - `sprk_signal` is a child of its typed `sprk_matter`, of its subject (the 8 `sprk_regarding*` lookups, excluding `sprk_regardingservicerequest`), and of its Decision Record (`sprk_signal.sprk_decisionrecord`). It is never a child of a policy, a policy version or a core record type.
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

1. **The Push Updates ribbon button:** keep it, and point it at the Family view. This is my reading of "yes push updates ribbon button"; the owner can correct it.
2. **Columns per member:** name, **record number** (blank where a table has none), record type, status and modified on.
3. **Default view:** the full family, all the way up and down (§12.3 C).
4. **`sprk_procedureassignment`:** open with the owner. The recommendation is to keep it OUT of the view (billing configuration), and UAC-r2 agrees. Its access lineage is unchanged either way.

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

### 12.11 Known limits (one line each in the PR)

- **Query count:** the worst case is about (edges per level × 4 levels) batched reads per view, 100 or more for a big matter. Measure p95 on dev and record it. Narrow each level to the edges whose parent table is present only if it hurts in practice.
- **Truncation favours the anchor:** in a truncated full-family view, the anchor's ancestor chain and its own subtree are filled first. Siblings' branches are the ones cut, and "load more" reaches them (UAC C4; tested).
- **Permissiveness:** the view is exactly as permissive as Dataverse read for the caller, never more. Gaps Dataverse itself has today (e.g. the flagged-external systemuser reading Restricted rows, UAC task 191) show here as they do in the MDA.
