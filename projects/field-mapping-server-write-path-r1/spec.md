# Field Mapping → Server-Side Write Path (R1) — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-10-19
> **Source**: [`design.md`](design.md) (rewritten 2026-10-19). Evidence: [`notes/design-review-2026-10-19.md`](notes/design-review-2026-10-19.md)
> **Baseline**: `origin/master` @ `49279def1`. Line numbers in the evidence notes drift; re-grep before use.

## Executive Summary

Field mapping (parent → child value inheritance configured in `sprk_fieldmappingprofile` / `sprk_fieldmappingrule`)
today runs in three engines. The enforcing one runs **in the browser**, in 8 wizards only, and it overwrites what the
user entered. This project:

- builds **one `RecordCreationService`** by unifying the two existing BFF create cores, on `OwnedChildWrite`'s
  caller-authorization model;
- applies **field mapping (fill-only) and the search-index default inline** in every product create;
- moves **every in-scope creator** onto it, including the Matter/Project wizards that still create user-owned rows
  through `Xrm.WebApi`;
- deletes the client engine and runs `/push` on the same engine (Copy only);
- adds a **scheduled repair pass** for records created outside the product;
- publishes a **per-table ownership standard** with guards.

It implements ADR-002 WP-1…WP-5 and WP-7 for invariants I-3 and I-5. It also makes I-1, I-2, I-6, I-11, I-12 and I-16
apply identically on every create path.

## Scope

### In Scope

- **W1 Canonical engine and configuration**:
  - promote `CreateTimeFieldMapping`; record its semantics in writing;
  - add a fill-only applier and a mapping-source resolver;
  - clean up the configuration model; remove the OOB `businessunit → sprk_matter` relationship map.
- **W2 Unified `RecordCreationService`**:
  - Create and Repair modes; caller and system principals; per-table rules registry;
  - `CallerWriteAuthorization` extraction; record-first owner; typed refusals;
  - re-point every existing core caller; Office quick-create becomes an adapter.
- **W3 Product creators onto the service**:
  - all 10 wizards (Matter and Project included) and the Analysis email draft;
  - Office Invoice, To Do and document save;
  - the `POST /api/v1/events` and `POST /api/ai/analysis/create` routes;
  - invoice extraction;
  - system creators;
  - delete the client engine.
- **W4 `/push`**: Copy-only on the canonical Copy logic; rules selected by `sprk_syncmode`; fixed error text.
- **W5 Records created outside the product**:
  - redirect the native "New" commands; turn quick create off; hide OOB Assign on standard-owned tables;
  - a forward-only scheduled repair pass with an on-demand trigger.
- **W6 Standard, guards, docs, retirement**:
  - the per-table ownership matrix; census and client guards; registry and doc updates;
  - dead-code removal; issue hygiene.

### Out of Scope

- External-portal creates (`ExternalDataService`, owned by external-access-r3): listed as an inventory exception;
  external-access-r3 adopts the mapping step itself.
- **Historic data backfill.** No ownership backfill and no mapping backfill of rows created before go-live (owner
  2026-10-19). The repair pass is forward-only.
- `/push` performance (#2098, 500-child timeout): filed as separate work.
- Removing `prvAssign` from security roles: follows an audit and is filed to UAC-r2.
- A Dataverse change-signal channel (service endpoint → Service Bus).
- I-2 secure-isolation logic itself: called, not changed.
- Changes to the field-mapping table schema, beyond reconciling the profile mode-column name drift.
- Wizard UI redesign.
- A runtime kill switch or feature flag for the migration.

### Affected Areas

**BFF** (`src/server/api/Sprk.Bff.Api/`):

| Path | Change |
|---|---|
| `Services/Dataverse/OwnedChildWrite.cs` | Create half moves into the service; caller-check helpers move to `CallerWriteAuthorization`; `RefileAsync` stays |
| `Services/Office/RecordCreationService.cs` | Matter/Project rules fold into the service; becomes an adapter, then is deleted |
| `Services/Office/CreateTimeFieldMapping.cs` | Moves to `Services/Dataverse/FieldMapping/` |
| `Api/ChildRecordEndpoints.cs` | Becomes the browser create route for all tables, incl. Matter and Project |
| `Services/Office/OfficeService.cs` | Quick-create (Matter/Project/Invoice), To Do |
| `Services/Office/OfficeDocumentPersistence.cs`, `Workers/Office/UploadFinalizationWorker.cs` | Document save |
| `Api/Events/EventEndpoints.cs`, `Api/Ai/AnalysisEndpoints.cs` | Typed create routes |
| `Services/Finance/InvoiceReviewService.cs` | Invoice extraction |
| `Services/Ai/Handlers/DataverseCreateRecordHandler.cs`, `EmailDraftToolHandler.cs`, `Services/Signals/Actions/BudgetInquiryExecutor.cs`, decision route cores | Re-point at the service |
| System creators: `Services/Workspace/TodoGenerationService.cs`, `Services/Ai/Nodes/ActionCore/TaskActionCore.cs`, `Services/Signals/SignalWriter.cs`, `DecisionRecordWriter.cs`, inbound communication/document ingestion | Go through the system principal |
| `Services/Communication/CommunicationService.cs` | Analysis → email mapping applied at send |
| `Api/FieldMappings/FieldMappingEndpoints.cs` | Push |
| `Infrastructure/DI/OfficeModule.cs` | The registration moves |
| New `Services/RecordCreation/` | The unified service |
| New repair `IScheduledJob` | W5 |

**Shared Dataverse** (`src/server/shared/Spaarke.Dataverse/`): field-mapping read path (`DataverseWebApiService.cs`,
`Models.cs`).

**Client** (`src/client/shared/`):
- **`Spaarke.UI.Components`**:
  - the 7 `Create*Wizard` services;
  - `utils/adapters/bffChildWriteAdapter.ts`;
  - `services/FieldMappingService.ts` and `FieldMappingHandler.ts` (deleted);
  - `utils/adapters/bffDataServiceAdapter.ts` (deleted);
  - the document-upload creators.
- **`Spaarke.AI.Widgets`**: `CreateAnalysisWizardWidget.tsx`.
- **`src/solutions/`**: CreateMatterWizard / CreateProjectWizard hosts, DocumentUploadWizard, SummarizeFilesWizard.
- **`src/solutions/LegalWorkspace/src/components/CreateMatter/matterService.ts`**: deleted.

**Dataverse metadata**:
- `src/dataverse/solutions/SpaarkeMaster/`: ribbon diffs for mapping-target tables; quick-create flags;
  `Other/EntityMaps.xml`.
- `infrastructure/dataverse/ribbon/**`.
- Web resources `sprk_fieldmapping_push.js`, `sprk_event_ribbon_commands.js`, `sprk_kpi_ribbon_actions.js`,
  `sprk_secure_child_ribbon.js`.

**Provisioning**: `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/AppConfigSeed/DataverseWebApiFieldMappingSeeder.cs`.

**Tests** (ADR-038 KEEP paths): `tests/unit/Sprk.Bff.Api.Tests/`, `tests/integration/**` (seam, data-mutation,
contract), `tests/Spaarke.ArchTests/` (`RecordOwnerAssignmentCensusTests`, `RefileWriterAfterRefileGuardTests`,
`RouteAuthorizationGuardTests`).

**Docs**:
- `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` (I-3/I-5/I-6 rows + ownership matrix);
- `docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md`;
- `docs/guides/FIELD-MAPPING-ADMIN-GUIDE.md`;
- root `CLAUDE.md` pointer rows.

## Requirements

### Functional Requirements

**W1 — Canonical engine and configuration**

1. **FR-01 One canonical engine.** Move `CreateTimeFieldMapping` to `Services/Dataverse/FieldMapping/`, still
   `internal static`. Its current semantics are canonical:
   - typed Default conversion to the target type;
   - Copy into Text/Memo renders option-set labels, ISO-8601 dates and Yes/No;
   - a null source is skipped;
   - Concat/Template use one `{field}` resolver with the same text rendering;
   - protected columns are skipped with a warning;
   - rules apply in `sprk_executionorder` order.

   Acceptance: unit tests (KEEP path) cover each mapping type × each target field type, and each semantic rule above.
   A written semantics table lives in `SPAARKE-FIELD-MAPPING-FRAMEWORK.md`.
2. **FR-02 Fill-only application at create.** A mapped value is written only when the caller did not supply the
   column. "Not supplied" means:
   - **scalars**: absent, null, or an empty/whitespace string (normalised to null);
   - **lookups**: no bind;
   - **booleans and option sets**: absent only.

   Acceptance: a caller-supplied value for a mapped column survives create. The Report Card Law Firm 1 / Assigned
   External / Assigned Internal case is a regression test. An unsupplied column receives the mapped value.
3. **FR-03 Mapping-source resolution.**
   - The source is the explicit `mappingSource` (entity + id) when the caller provides one; otherwise it is inferred
     from the record's filed-under parent.
   - With several candidate parents, precedence is the ADR-024 regarding pair first, then the table's single typed
     parent lookup. If that is ambiguous, no inferred source, with a warning.
   - No source, no profile, or a profile with no active rules is a silent no-op (no profile) or a warning (profile
     without rules).

   Acceptance: tests for explicit, inferred-from-pair, inferred-from-typed-lookup, ambiguous and none.
4. **FR-04 Source read authorization.** For the caller principal, the source record is checked for **Read as the
   caller** before it is read, using the probe `QuickCreateSourceAccessFilter` uses. A caller who can't read the
   source gets no mapping and a warning, never a refusal and never a leak of whether the source exists. System
   principals read app-only.

   Acceptance: a Read-denied source yields a create with no mapped values and a warning.
5. **FR-05 Configuration cleanup.**
   - (a) Reconcile the profile mode column (`sprk_capabilitymode` vs `sprk_compatibilitymode`) to the deployed name
     in code and solution.
   - (b) Document `sprk_mappingdirection`, `sprk_isrequired`, `sprk_iscascadingsource` and `sprk_compatibilitymode`
     as unused.
   - (c) Fix the seeder rule filter to `_sprk_fieldmappingprofile_value` (#1318).
   - (d) Read `sprk_syncmode` on rules; migrate existing Copy rules to Manual Refresh (1) with a scripted,
     idempotent, `-Verify`-able step; new rules default to One-time (0).
   - (e) At run time, refuse (skip + warn) any rule whose target is the regarding pair (`sprk_regardingrecordid`,
     `sprk_regardingrecordtype`, resolver fields) or a protected column for that table.
   - (f) Remove the OOB `businessunit → sprk_matter` `sprk_ai_search_index` map from `EntityMaps.xml`.

   Acceptance:
   - the seeder seeds rules without 400s;
   - the migration's `-Verify` reports every existing Copy rule = Manual Refresh;
   - a pair-targeting rule never writes;
   - `EntityMaps.xml` has no `sprk_matter` map.

**W2 — Unified `RecordCreationService`**

6. **FR-06 Single create service.** One sealed, scoped `RecordCreationService` (`Services/RecordCreation/`), registered
   once in place of `OfficeModule.cs:73`. **Create mode** runs, in this order:
   1. resolve the principal;
   2. table rules; refuse blocked columns in the **caller's** payload;
   3. field mapping (FR-02…FR-04);
   4. map the merged payload;
   5. caller checks on the merged payload (FR-08);
   6. secure-root plan;
   7. owner (FR-09);
   8. search-index default (FR-10);
   9. creator person, "for" person and table defaults;
   10. I-1 stamp + I-16 derived into the payload;
   11. one app-only create with the alternate-key retry for autonumber tables;
   12. post-create steps inside the service, never throwing: number read-back, isolated-root completion, sharee
       mirror, assigned-access grants, membership event.

   Acceptance: a seam test asserts the step order and that one create call is made. Every step is covered for one
   root (Matter) and one child (Event).
7. **FR-07 Principals.**
   - **Caller**: WhoAmI under the caller's token; unresolved → 403.
   - **System**: for jobs, playbook nodes, ingestion and the repair pass. Skips the as-caller checks (steps 2 and 5),
     records `RequestedBy` when known, and applies every other step identically.

   Acceptance: tests for both principals. A system create yields the same owner, stamp and mapping as a caller create
   under the same parent.
8. **FR-08 Caller authorization on the merged payload.** `OwnedChildWrite`'s caller-check helpers move unchanged to
   `Services/Dataverse/CallerWriteAuthorization.cs`: `CheckCallerMayCreateAsync`, AppendTo, pair resolution, rights,
   WhoAmI. They run on the merged payload:
   - **Caller-supplied values** that fail Create/Append, field security or AppendTo → refuse (unchanged behaviour,
     uniform 404/403).
   - **Mapped values** that fail AppendTo or field security → **dropped with a warning**; the create proceeds (owner
     2026-10-19).
   - **Exception: filing lookups (amended 2026-10-20, owner, at UAC-r2's request).** A mapped lookup that is a filing
     lookup for the target table (`SecureChildLineage` / `RecordReparent.FilingParentOf`) and fails AppendTo
     **refuses**, exactly like a caller-supplied one. Only reference (non-filing) lookups drop with a warning.
     Otherwise a child copied from a Secure parent would be created unfiled, outside isolation. Pinned by a
     must-fire test on a Secure parent (task 014).

   Acceptance: closes #1614 and #1617. A mapped lookup the caller can't AppendTo is not written and a warning is
   returned. A typed lookup the caller can't AppendTo still refuses. The existing `SecureChildOwnershipAiToolTests*`
   suite passes unchanged.
9. **FR-09 Owner rule: record-first everywhere.** Owner = `ResolveOwnerAsync(ForParents(...), RequestedBy)` for roots
   and children: secure ancestor → the named Secure Record Owners team, else the filed-under record's BU default Owner
   team, else the acting user's BU team, else refuse. `ResolveOwningTeamAsync` is no longer used for creates.

   Acceptance: the same Project filed under the same Matter gets the same owner via Office, the browser route, chat
   and a decision follow-on. Unresolved → typed refusal. No create yields a user- or app-owned row except a listed
   exception.
10. **FR-10 Search-index default (I-5).** Fill-only, from the **owning** BU's `sprk_searchindexname` /
    `sprk_ai_search_index`, on every table the rules registry marks as search-indexed.

    Acceptance: an explicit value wins; an unset value takes the owning BU's default; tested for Matter, Project and
    Document.
11. **FR-11 Per-table rules registry.** One declarative registry holds, per table: root/child; protected columns;
    autonumber column; "for"-person column; server defaults; search-indexed Y/N; mapping-applies Y/N. It replaces
    `MatterProtectedAttributes`/`ProjectProtectedAttributes`, `ServerOwnedColumns`, `RootServerOwnedColumns` and
    `ForPersonColumns`.

    Acceptance: the old lists are deleted; an arch test fails if a create table lacks a registry entry.
12. **FR-12 Refusal model.** The service returns typed refusals `(Kind, Code, Detail)`. One shared map turns kinds
    into HTTP ProblemDetails with a stable `errorCode` (ADR-019). The Office adapter keeps its existing Office error
    codes.

    Acceptance: the route, Office and chat callers use the shared map; the Office quick-create contract tests pass
    with unchanged codes.
13. **FR-13 Re-point existing callers.** These call the service:
    - the child-records route create;
    - the chat `dataverse.create_record`, `email.draft` and budget inquiry;
    - the decision route cores.

    The I-1 stamp, I-16 and sharee mirror then run for chat creates, which today skip them. Office quick-create
    (Matter, Project **and Invoice**) becomes a thin adapter over the service. `RecordCreationService` (Office) is
    deleted once the adapter lands.

    Acceptance: a chat-created child carries its I-1 stamp at create; Office quick-create contract tests pass.
14. **FR-14 Repair mode.** The service exposes a fill-only, idempotent Repair mode against an existing row:
    mapping (FR-02 semantics against the row's current values), owner (only when the row is user- or app-owned and
    not a listed exception), search-index default, I-1 stamp.

    Acceptance: two consecutive repairs produce no second write; a present value is never overwritten.

**W3 — Product creators onto the service**

15. **FR-15 Browser create route covers every wizard table.**
    - Extend the existing `POST /api/v1/child-records/{table}` create table set, and the client
      `BFF_CHILD_CREATE_TABLES`, with `sprk_matter` and `sprk_project`.
    - Accept an optional `mappingSource` in the request (FR-03).
    - Wizard secure-Project creation goes through the service's secure-root plan (create into isolation), and the
      remaining provisioning steps stay as today. The task confirms the split against `provisionSecureProject`.

    Acceptance: Matter and Project created from their wizards are team-owned per FR-09 and mapped per FR-02.
16. **FR-16 Wizard migration (10 wizards).** Matter, Project, Event, To Do, Invoice, Work Assignment, Report Card,
    Analysis, Document Upload and Summarize Files each create through the service:
    - Each stops calling `applyFieldMappings`.
    - Each sends **only user-entered values**. Client-side defaults that matter move to the server table rules (FR-11).
    - The Matter wizard stops generating `{typeCode}-{random6}` numbers: numbers come from the platform autonumber
      (I-11, owner 2026-10-02 task 076).
    - Migration is **one wizard per task, Matter first**, each verified live on dev before the next. Rollback is
      redeploying the previous bundle.

    Acceptance: per wizard, an integration test plus a live dev check that mapped fields are present on first load,
    the user's own values are kept, and the owner follows FR-09.
17. **FR-17 Analysis email mapping at send.** The Analysis wizard's analysis → communication mapping (email draft
    subject/body) is applied server-side in the send path, fill-only. A user-typed subject/body wins.

    Acceptance: a user-typed subject survives; an empty subject is filled from the mapping.
18. **FR-18 Office paths.** Office Invoice quick-create, `/api/office/todo` and Office document save
    (`OfficeDocumentPersistence` / `UploadFinalizationWorker`) create through the service. For document save the
    association is set **in the create**, not a follow-up update (word gaps G1/G2).

    Acceptance: an Office-created document under a Matter carries mapping (if profiled), I-1 stamp, owner and search
    index at create; no separate association update is issued.
19. **FR-19 Typed routes.** `POST /api/v1/events` and `POST /api/ai/analysis/create` create through the service. Their
    route-level filters remain for route-level authorization (ADR-008).

    Acceptance: existing route contract tests pass; created rows carry the service's invariants.
20. **FR-20 Invoice extraction.** `InvoiceReviewService`'s invoice create goes through the service (caller
    principal: the reviewing user).

    Acceptance: the extracted invoice carries mapping from its Matter.
21. **FR-21 System creators.** These create through the system principal:
    - To Do generation;
    - playbook create-task (`TaskActionCore`);
    - Signal and Decision Record writers;
    - inbound communication and document ingestion (`MessagingIngestor`, `IncomingCommunicationProcessor`,
      `EmailAttachmentProcessor`, `EmailUploadCaptureService`).

    Existing accepted ownership exceptions (unfiled communications keep the creator; direct threads; notifications)
    are preserved as registry rules.

    Acceptance: the census test lists each as routed; each exception is a named registry rule with its rationale.
22. **FR-22 Delete the client engine.** Delete `FieldMappingService.applyFieldMappings` (keep only what other code
    still imports, if anything) and `FieldMappingHandler.ts`.

    Acceptance: no `applyFieldMappings` symbol in `src/`; a client arch rule fails if it reappears.

**W4 — `/push`**

23. **FR-23 Push on the canonical engine.**
    - `/push` applies **Copy rules only**, using the canonical engine's Copy logic, and only rules with
      `sprk_syncmode` = Manual Refresh. Push overwrites (it is the deliberate re-sync).
    - Re-file behaviour is kept: `ReparentAsync`, the root filing gate, restamp, `AfterRefileAsync`.
    - Per-child failures return fixed, actionable text; detail goes to logs only (#1437).
    - `ApplyMappingRule`'s own Copy logic is removed.

    Acceptance: create-time Copy into an empty field and push Copy give identical values for the same profile and
    source (parity test); #1437 regression test; existing push contract and re-file guard tests pass.

**W5 — Records created outside the product**

24. **FR-24 Native UI.**
    - Ribbon "New" on every mapping-target table opens its wizard (extending the `sprk_secure_child_ribbon.js` override
      pattern).
    - Quick create is turned off on those tables; Event "Add Memo" and "Add KPI" create through the BFF instead of
      quick-create forms.
    - The OOB **Assign** command is hidden on standard-owned tables.
    - Live quick-create flags are verified per table.

    Acceptance: a scripted per-environment check (`-Verify`) reports quick create off and the Assign command hidden on
    each listed table; ribbon "New" opens the wizard.
25. **FR-25 Repair pass.** An `IScheduledJob` (ADR-036 A1-1…A1-7), registered with `AddScheduledJob`, runs every
    5 minutes over mapping-target and standard-owned tables.
    - It selects rows with `createdon` ≥ max(go-live, watermark) whose platform `createdby` ≠ the BFF application
      user, and runs Repair mode (FR-14).
    - **Forward-only**: rows created before go-live are never touched, including rows reassigned via OOB Assign.
    - An authorized admin can trigger it on demand for a table and time window (the WP-7 import reconciliation pass).
    - Each unit is claimed atomically with a completion marker (A1-3), and each attempt emits a heartbeat (A1-5).

    Acceptance: a natively created or imported row gets its mapping, standard owner, search-index default and stamp
    within ≤ 10 minutes; re-runs are no-ops; a pre-go-live row is never written.

**W6 — Standard, guards, docs, retirement**

26. **FR-26 Per-table ownership standard.** A per-table matrix sits next to I-6 in
    `DATAVERSE-WRITE-PATH-ARCHITECTURE.md`. For every invariant-bearing table it gives the owner rule, the role depths
    that reach it (Spaarke Basic User, Core User, Office Add In User, AI Analysis User) and each exception with its
    access rationale. The notification outbox and the `DecisionRecordWriter` communication-owner case are assessed
    and recorded.

    Acceptance: every table in the rules registry has a matrix row; no exception lacks a rationale.
27. **FR-27 Guards.** Extend the census guards:
    - every server create of a registry table goes through `RecordCreationService` or is a named exception;
    - no client `Xrm.WebApi.createRecord` / `dataService.createRecord` of a registry table outside the BFF adapter;
    - no client field-mapping application.

    Acceptance: each guard has a negative control that fails when the rule is broken.
28. **FR-28 Docs and retirement.**
    - Registry rows I-3/I-5 → done; I-6 updated.
    - Rewrite `SPAARKE-FIELD-MAPPING-FRAMEWORK.md` and `FIELD-MAPPING-ADMIN-GUIDE.md`: fill-only; push and sync mode;
      "use a quick view when the child only needs to show the parent's value"; never define OOB relationship
      mappings on rule-owned fields.
    - Update root `CLAUDE.md` pointer rows (with a `.claude/CHANGELOG.md` entry).
    - Delete `bffDataServiceAdapter.ts` and the LegalWorkspace `CreateMatter/matterService.ts`.
    - Close or update #1614, #1617, #1318 and #1437; file #2098 follow-up and the `prvAssign` audit (UAC-r2).

    Acceptance: docs merged; issues updated; dead files removed.

**W7 — Record family service and the Family tool** (amendment, 2026-10-27; owner-approved scope; detail and review
record in `design.md` §12; reviewed by UAC-r2 in two rounds and by ontology-r1 in two rounds; the term "Family" is
ontology D-191)

29. **FR-29 Family edge registry.** `RecordFamilyEdges` in `Services/Dataverse`.
    - Each row is (FamilyKind, ChildTable, LookupColumn, ParentTable, EdgeKind, InSecureFamily, RecordNumberColumn).
      Every row is `FamilyKind = Filing`. EdgeKind covers a typed lookup, a stamp, a lineage hop and the regarding pair.
    - Seeded from UAC 194 (master `de38296499`): `SecureChildLineage`, typed and intermediate filing columns, the pair,
      and the stamp columns.
    - The Matter → Project edge is the regarding pair (owner, #2352). Its data comes with UAC task 195.
    - Excluded: N:N associations, citations, people and organization lookups, `sprk_procedureassignment` (owner) and
      `sprk_policy.sprk_currentversion`.
    - Edge types live in code. No family table is created.

    Acceptance:
    - a parity test lists every disagreement with the existing registries, and a new disagreement fails the build;
    - a content pin covers the six access columns, and changing one requires a UAC review line;
    - adding a synthetic non-Filing row leaves every isolation view unchanged (UAC C2).
30. **FR-30 Family reader.** `RecordFamilyReader.ReadAsync(kind, table, id, direction, continuation)` is read-only.
    - It walks as the caller (OBO), one batched query per (table, lookup) per level, with a visited set.
    - It never passes through an unreadable record, and gives no counts or hints of hidden ones.
    - Going up, it stops at the first unreadable ancestor. The full view then walks down from the highest readable
      ancestor.
    - Each walk covers one kind only.
    - **One family per record (owner-approved, UAC-r2, 2026-10-27).** A record filed under several parents belongs to
      exactly one family, its PRIMARY parent's. Its other filing edges are secondary.
      - The walk goes up only through the primary parent, and going down it lists a child only under its primary
        parent.
      - The primary parent comes from `RecordOwnershipResolver`'s order (`InPrimaryOrder` / `OrderedParentsOf`, UAC
        194b). There is no second ordering.
      - Isolation is unchanged: secure-if-any still applies over ALL filing parents. Family membership is not the
        security boundary.
    - Members are unique by (table, id), and the shallowest edge is kept.
    - Caps: depth 4 and 500 members. A truncated view keeps the anchor's chain and its own subtree, and returns
      `truncated` plus a continuation.
    - The continuation is protected with Data Protection and bound to oid + anchor + kind + direction + top. Every
      parent it carries is re-checked.

    Acceptance:
    - a readable record below an unreadable one is absent, with nothing revealing it (tested with a Secure family);
    - a truncated view says so and loads more, and nothing is dropped silently;
    - a replayed or tampered continuation is refused like an invalid one;
    - field-secured display columns come back null.
31. **FR-31 Family endpoint.** `GET /api/v1/records/{table}/{id}/family` lives in its own file,
    `Api/Records/RecordFamilyEndpoints.cs`, with auth required and registered in the route-authorization ledger and
    the census.
    - `kind` defaults to Filing; any other value is 400.
    - `direction` defaults to the full family; `down` is also accepted.
    - The response carries `anchor` and an optional `root`. Each member has id, table, name, record number, record
      type, status, modified on, kind, edgeKind, parent and depth.
    - An unknown anchor and an unreadable anchor both get one uniform 404 (equalised probe).

    Acceptance: endpoint contract tests, including the uniform 404 and the 400 on kind.
32. **FR-32 Family modal.** `RecordFamilyModal` in `Spaarke.UI.Components`, built on `SprkModal/presets/BrowseModal`
    (ADR-050 Layout 2).
    - Columns: name, record number (blank when the table has none), record type, status, modified on.
    - It shows the family grouped by level and table, with selection, a truncation notice and "load more".
    - Action toolbar: Push field updates and Update Assigned To (FR-34). Each action shows a preview, the person
      confirms rows, then a result summary.

    Acceptance: component tests; no action acts on unconfirmed rows.
33. **FR-33 Family tool on TrackingFieldTrio.**
    - The shared component gains an optional `onOpenFamily` callback, so existing consumers are unchanged.
    - The PCF wires it to the modal.
    - It is available on the Matter, Project, Work Assignment and Event main forms.
    - The PCF bundle delta is measured.

    Acceptance: the icon opens the modal on all four forms; existing consumers' snapshots are unchanged.
34. **FR-34 Push extensions** (with FR-23, task 040; UAC 4a design).
    - `preview: true` writes nothing. It returns per-child changes and flags (Secure, Restricted, Limited).
    - `includeAssignedTo` is false when absent, and requires Write on the source.
    - `childIds` is the confirmed list: the server re-derives the children as the caller and intersects them.
    - Children are found through a `RecordFamilyEdges` Filing edge (a typed lookup or the regarding pair), which
      replaces the lookup-only `ResolveParentLookup` (#2352).
    - The actions are Filing-only and are refused server-side for any other kind.
    - Sync mode is class-aware: Assigned-To targets 0, filing targets never, plain data 1. The values ship after the
      owner reviews a dry run.

    Acceptance: UAC's list in `design.md` §12.8 item 4, plus a Matter → Project push that succeeds through the pair.
35. **FR-35 Push Updates ribbon opens the Family view** (owner). The Matter and Project ribbon button opens the Family
    modal instead of pushing directly.

    Acceptance: the button opens the modal, and no push happens without the person confirming in the modal.

**Work Assignment create defaults** (amendment, 2026-10-27; owner, from the dev check)

36. **FR-36 Work Assignment Assigned To default (#2354).**
    - When a `sprk_workassignment` create does not supply `sprk_assignedto`, the server sets it to the new record's own
      `sprk_assignedtointernal` after mapping (fill-only, so a supplied value wins).
    - It applies on every server create path for work assignments (the child-records route and
      `RecordCreationService`). It is declared once in `RecordTableRules`.
    - `sprk_assignedto` confers access, so UAC-r2 reviews the diff before merge.

    Acceptance:
    - a work assignment created from a matter whose Assigned To Internal is X gets Assigned To = X;
    - a supplied Assigned To is kept;
    - no Assigned To Internal leaves Assigned To blank, with a structured warning.

37. **FR-37 Assigned To on the Create Work Assignment wizard** (owner, 2026-10-27, approving the task list).
    - The wizard's Enter Info step gains an optional "Assigned To" contact picker. It reuses the contact search
      and nav-prop binding that the follow-on event step already uses.
    - Left blank, the payload omits `sprk_assignedto` (never a null bind), so FR-36's server default applies.
      The client does not pre-fill or compute the default, because the rule is declared once, on the server.
    - When chosen, the contact is sent as a supplied value and takes the server's supplied-value path (Assigned-To
      policy and materialiser).

    Acceptance:
    - choosing contact Y creates the work assignment with Assigned To = Y;
    - leaving it blank sends no `sprk_assignedto` key, so the record gets FR-36's default;
    - the field is optional and the step's validity rules are unchanged.

    (The Work Assignment Number, #2353, is a Dataverse autonumber `WRK-{SEQNUM:6}`, applied on dev 2026-10-27 with the
    owner's OK; PR #2358. No code.)

### Non-Functional Requirements

- **NFR-01 Security fails closed** (WP-6): every caller check refuses on error. Uniform 404 for "doesn't exist" and
  "can't see". Mapping never becomes an existence oracle or an AppendTo bypass. Security-sensitive tasks (FR-06…FR-09,
  FR-25) carry FULL rigor and an adversarial review.
- **NFR-02 Latency**: the Matter wizard create p95 (wizard click → record id) is measured before and after migration
  on dev. The added cost of server mapping ≤ 1 profile read + 1 source read per create, and none when no profile
  exists. Report the delta; escalate if p95 grows by > 500 ms.
- **NFR-03 Idempotency**: Repair mode and the repair pass are idempotent per unit (ADR-036 A1-3). The create path is
  not retried blindly after an ambiguous failure.
- **NFR-04 Observability**: per-step structured logs plus counters for create (mapped / skipped-unauthorized / refused
  by kind) and the repair pass (scanned / repaired / no-op / failed). Errors carry a correlation id (ADR-019).
- **NFR-05 Publish size and CVE**: no new packages. Each BFF task measures publish size against a fresh master build
  per root CLAUDE.md §10.4 (Compress-Archive, short path, file-count check) and runs
  `dotnet list package --vulnerable --include-transitive`.
- **NFR-06 Tests** (ADR-038): engine unit tests, pipeline seam tests (`tests/integration/seam/**`), per-surface
  integration tests, data-mutation ownership tests and arch guards, all at KEEP paths. No `Mock<HttpMessageHandler>`
  and no DI-registration tests.
- **NFR-07 Backward compatibility**: Office quick-create error codes are unchanged. Push response shape is unchanged
  apart from error text. The child-records route contract is unchanged except for the added tables and the optional
  `mappingSource`.
- **NFR-08 Coordination**: changes to `OwnedChildWrite`, the census tests and role definitions are sequenced with
  UAC-r2. Chat and decision re-pointing is sequenced with ontology-r1. Run `/conflict-check` before each W2 task.

## Technical Constraints

### Applicable ADRs

- **ADR-002** (no plugins; server-side write path WP-1…WP-8, amended 2026-10-03): one owner per invariant; client
  never enforces; invariant tables written via BFF; fill-only async fix-up; reconciliation for imports.
- **ADR-001 / ADR-008**: Minimal API; endpoint filters for route-level resource authorization; `.RequireAuthorization()`.
- **ADR-010**: DI minimalism. One scoped service replaces one registration; the engine stays static.
- **ADR-019**: ProblemDetails with a stable `errorCode` and correlation id; no raw upstream text.
- **ADR-024**: polymorphic regarding pair. Five resolver fields; `PolymorphicResolverService` client-side; server-side
  pair resolution.
- **ADR-028**: auth. App-only creates under the managed identity; the caller identity is resolved via WhoAmI on the
  caller's token.
- **ADR-036 / ADR-052**: the repair pass is a BFF `IScheduledJob` registered with `AddScheduledJob`, leased, with a
  heartbeat; no new hand-rolled `BackgroundService`; no Function.
- **ADR-038**: integration-heavy tests at KEEP paths; coverage is observed, never a gate.
- **ADR-012**: shared client components. The client package loses `applyFieldMappings`; the package boundary is
  unchanged.

### MUST Rules

- ✅ MUST apply field mapping server-side, inline, in the create request (WP-1, WP-4).
- ✅ MUST run caller checks on the **merged** payload, so mapped lookups are AppendTo/field-security checked.
- ✅ MUST resolve owner record-first via `ResolveOwnerAsync` for every create; never user- or app-owned except named
  exceptions.
- ✅ MUST keep re-file behaviour on push (`ReparentAsync`, `AfterRefileAsync`, root filing gate).
- ✅ MUST make the repair pass fill-only, idempotent, leased and heartbeated (ADR-036).
- ✅ MUST return ProblemDetails with stable codes; Office codes preserved.
- ❌ MUST NOT add a Dataverse plugin, business rule, flow or OOB relationship mapping as an invariant owner.
- ❌ MUST NOT leave any client-side field-mapping application or client `createRecord` of a registry table.
- ❌ MUST NOT write rows created before go-live (forward-only).
- ❌ MUST NOT add a hand-rolled timer `BackgroundService` or a new NuGet package.

### Existing Patterns to Follow

- Caller authorization core: `Services/Dataverse/OwnedChildWrite.cs` (`CheckCallerMayCreateAsync`, AppendTo, pair
  resolution).
- Owner resolution: `Services/Dataverse/RecordOwnershipResolver.cs` (`ResolveOwnerAsync`, `ForParents`).
- Source Read probe: `Api/Filters/QuickCreateSourceAccessFilter.cs`.
- Engine: `Services/Office/CreateTimeFieldMapping.cs`; mapping I/O: `RecordCreationService.ApplyFieldMappingsAsync`.
- Scheduled reconciliation: `Services/Dataverse/CoreAncestorStampReconciliationJob.cs`,
  `Services/Access/SecureChildReconciliationJob.cs`.
- Ribbon "New" override: `src/client/webresources/js/sprk_secure_child_ribbon.js` +
  `infrastructure/dataverse/ribbon/SecureChildRibbons/`.
- Census guard: `tests/Spaarke.ArchTests/RecordOwnerAssignmentCensusTests.cs`.
- Scripted `-Verify` + reversible apply: `scripts/Backfill-RecordOwnership.ps1` (pattern only; no backfill in scope).

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration

```xml
<hot-path-declaration>
  <bff>Y</bff>
  <spaarkeai>N</spaarkeai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>Y</root-claude-md>
</hot-path-declaration>
```

**Placement justification** (`.claude/constraints/bff-extensions.md`):
- **`RecordCreationService` (BFF)**: the synchronous write path that owns the invariants; it needs BFF identity,
  request scope and the shared resolvers; no other synchronous server runtime exists.
- **Repair pass (BFF `IScheduledJob`, ADR-052)**: the same code as the inline path. Signals: short, schedule-driven,
  Dataverse-only I/O, an existing lease host. The tie-breaker is fewer moving parts.
- **CRUD only**: no AI dependency, so the ADR-013 facade rule is not triggered.
- **Size**: the ≤ 60 MB publish ceiling applies per task.

### New Components (§11 three-question gate)

| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing |
|---|---|---|---|
| `RecordCreationService` (unified, `Services/RecordCreation/`) | `Services/Dataverse/OwnedChildWrite.cs` create half; `Services/Office/RecordCreationService.cs` | **Yes**: merges the two existing cores; net one service, not three | The same Project gets different owners by surface; mapping reaches 2 of ~30 create paths; chat creates skip I-1/I-16 |
| `CallerWriteAuthorization` | `OwnedChildWrite.cs` helpers | **Yes**: moved unchanged | Authz stays inside a 1.7k-line file mixing create, re-file and pair rules, and can't be shared by the unified service without a circular dependency |
| Per-table rules registry | `MatterProtectedAttributes`, `ProjectProtectedAttributes`, `ServerOwnedColumns`, `RootServerOwnedColumns`, `ForPersonColumns` | **Yes**: consolidates five lists | Protected/server-owned columns drift between paths (today they already differ per core) |
| Mapping-source resolver + fill-only applier | `RecordCreationService.ApplyFieldMappingsAsync` (explicit source only, overwrite) | **Yes**: extends it | Non-wizard creators get no mapping; user values keep being overwritten (Report Card bug) |
| Repair `IScheduledJob` | `CoreAncestorStampReconciliationJob` (I-1 only), `SecureChildReconciliationJob` (secure only) | No: those jobs own other invariants; same pattern, separate job | Native, imported and agent-written rows never get mapping, search index or standard owner |
| `mappingSource` request field on the child-records route | none | Extends the existing route | Wizards can't name a source when the filed-under parent differs from the intended mapping source |
| Native-surface `-Verify` script | `scripts/*-Verify` patterns | New script, existing pattern | No proof quick create is off / Assign hidden per environment |

## ADR Tensions (per CLAUDE.md §6.5)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| ADR-008 | "MUST use endpoint filters for resource authorization" | The create checks depend on the **request body** (every lookup, the pair, mapped values) and are shared with non-HTTP callers (chat tools, decision cores, system creators, repair pass). An endpoint filter can't see mapped values and can't serve non-HTTP callers. | **A** (project-scoped exception, continuing the precedent `OwnedChildWrite` / UAC-r2 task 147 already set) | Route-level authorization stays in filters (`RequireAuthorization`, caller identity, route ledger). Payload-dependent checks live in `CallerWriteAuthorization`, called by the service. Cited in the W2 PR description. |

> No other tensions. The behaviour changes (Office Project owner, fill-only create mapping, Copy-only push) are owner
> decisions, not ADR conflicts; they are called out in release notes.

## Success Criteria

1. [ ] One engine: no `ApplyMappingRule` Copy logic, no `applyFieldMappings`, no `FieldMappingHandler`. Verify: grep +
   client arch guard.
2. [ ] One create service: every in-scope create path uses `RecordCreationService` or is a named exception. Verify:
   census guard with a negative control.
3. [ ] Mapped on first load from every surface family (wizard, Office, chat, decision, system). Verify: per-family
   integration tests with a shared profile fixture + live dev check on Matter→Event.
4. [ ] Fill-only: caller values survive. Verify: Report Card regression test.
5. [ ] Same owner everywhere; no user-owned Matter/Project after go-live. Verify: cross-surface owner test + live dev
   check.
6. [ ] Mapped lookups authorized: unauthorized mapped lookups are dropped with a warning; typed ones refuse. Verify:
   #1614/#1617 regression tests.
7. [ ] Push parity and fixed error text. Verify: parity test + #1437 test.
8. [ ] Outside-product rows repaired within ≤ 10 min, fill-only, idempotent, forward-only. Verify: data-mutation test +
   live import of 5 rows on dev.
9. [ ] Ownership standard published with all exceptions assessed; registry I-3/I-5 done. Verify: doc review.
10. [ ] Dead code removed; OOB BU→Matter map removed; native-surface `-Verify` passes on dev. Verify: grep + script.

## Dependencies

### Prerequisites

- word-add-in-r1 code on master: **satisfied** (2026-09-30).
- UAC-r2 sign-off on the W2 refactor of `OwnedChildWrite` and the census guard changes; timing agreed before W2
  starts.
- ontology-r1 awareness for the chat tool, decision cores and Signal/Decision writer re-pointing.
- W7 prerequisites:
  - FR-29 seeds from UAC 194 (merged, `de38296499`);
  - FR-30's primary-parent order needs UAC 194b merged;
  - the Matter → Project pair edge needs UAC task 195 merged (the server writes the pair; dev backfill);
  - FR-34/FR-35 ship with task 040 (needs 019 → 017 → ebilling #1911);
  - FR-36 needs a UAC-r2 diff review.
  - FR-37 ships after FR-36 (task 037), so a blank field gets the server default.

### External Dependencies

- Dev Dataverse (spaarkedev1) for live checks; platform autonumber configured (task 076 `-Verify`).
- Ribbon/solution deployment via `dataverse-deploy` / `ribbon-edit`; BFF via `bff-deploy`; wizards via
  `code-page-deploy`.

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Q1 Sequencing | Relationship to word-add-in-r1 | Separate project; promote, don't fork; gate satisfied | W2 starts from master code |
| Q1a Config surface | Reuse profile/rule tables? | Yes; native forms; clean up | FR-05; no new schema |
| Q2 Identity | Create identity | App-only + explicit caller checks + I-6 team owner | FR-07…FR-09 |
| Q3 Push | Mapping types on push | Copy only, by `sprk_syncmode` = Manual Refresh | FR-05(d), FR-23 |
| Q4 Preview | Keep client preview? | Superseded: none exists; delete the client engine | FR-22 |
| Q5 W5 | Outside-product shape | Native UI redirect + scheduled repair pass; no change-signal channel | FR-24, FR-25 |
| Q6 Precedence | Caller value vs mapping | Fill-only, caller wins | FR-02 |
| Q7 Source | Where the source comes from | Inferred from the filed-under parent, explicit override; all creators | FR-03 |
| Q8 Architecture | Reconcile the two cores | One service on `OwnedChildWrite`'s authorization | FR-06…FR-13 |
| Q9 Owner rule | Roots | Record-first everywhere | FR-09 |
| Q10 Path scope | Bespoke paths | All four groups | FR-18…FR-21 |
| Q11 External | Portal creates | Out; coordinate | Out of Scope |
| Q12 Assign | OOB Assign | Hide + repair; privilege removal after an audit | FR-24, FR-28 |
| Mapped authz | Mapped value fails AppendTo / field security | Skip that value + warn; the create proceeds. A mapped **filing** lookup that fails AppendTo refuses (amended 2026-10-20) | FR-08 |
| Backfill | Fix pre-go-live rows? | **Neither**: forward-only | FR-25 forward-only; Out of Scope |
| Rollout | Reversibility | Per-wizard cutover, revert by redeploy; no runtime flag | FR-16 |
| Matter number | Wizard `{typeCode}-{random6}` vs autonumber | Autonumber (owner decision 2026-10-02, task 076) | FR-16 |
| W7 scope (2026-10-27) | Shared family service + Family tool? | Yes: one shared read-only service, a Family tool on TrackingFieldTrio, kept in this project | FR-29…FR-35 |
| Family term (2026-10-27) | One concept or several? | One Family concept with kinds; Filing now; patent family later (ontology D-191) | FR-29…FR-31 |
| Family view (2026-10-27) | Default view and columns | The full family, up and down; name, record number, record type, status, modified on | FR-31, FR-32 |
| Ribbon (2026-10-27) | Push Updates button | Keep it; it opens the Family view | FR-35 |
| Procedure assignments (2026-10-27) | In the Family list? | Out | FR-29 |
| Project → Matter (2026-10-27, #2352) | Filing edge | The regarding pair only; the N:N stays an association | FR-29, FR-34 |
| WA Assigned To (2026-10-27, #2354) | Default | The record's own Assigned To Internal | FR-36 |
| WA wizard Assigned To (2026-10-27) | Field on the wizard? | Yes, it can be added: an optional picker on Enter Info; blank uses the FR-36 default | FR-37 |
| Push Updates timing (2026-10-28) | When does the button switch to the Family view? | Ship 075 with 040: Push Updates keeps pushing as today until the Family view's own Push action exists | FR-35 |
| WA Number (2026-10-27, #2353) | Generation | Autonumber `WRK-{SEQNUM:6}` (dev applied, PR #2358) | — |

## Assumptions

- **Route**: widen the existing `POST /api/v1/child-records/{table}`; no new route name (§11: extend). Affects FR-15.
- **"Not supplied"**: as defined in FR-02. Affects every wizard's payload builder.
- **Multi-parent precedence**: regarding pair, then the single typed parent lookup; ambiguous → no source + warning.
  Affects FR-03.
- **Repair interval**: 5-minute schedule, ≤ 10-minute target. Affects FR-25.
- **Repair marker**: platform `createdby` ≠ the BFF application user identifies rows created outside the BFF. To be
  verified in the first W5 task; if impersonated product creates exist, they are excluded by an explicit list.
- **OOB Assign after go-live**: a BFF-created row later reassigned via Assign is not caught by a `createdby`-based
  scan. The repair pass also selects rows **created after go-live** whose owner is a systemuser on a standard-owned
  table (not a listed exception). Pre-go-live rows are never touched.
- **Secure Project wizard**: the server creates into isolation via the existing secure-root plan; the FR-15 task
  confirms which `provisionSecureProject` steps remain.
- **Profile caching**: none initially. Add only if NFR-02 escalates.
- **Latency budget**: escalation threshold +500 ms p95 (NFR-02).

## Unresolved Questions

- [ ] **Repair marker verification**: confirm no product path creates under impersonation (`MSCRMCallerID` on the
  Communication routes). Blocks: final FR-25 selection query.
- [ ] **Live quick-create flags** for Project, Matter and Invoice Line Item (the solution XML says on; the
  architecture doc says off). Blocks: the FR-24 table list.
- [ ] **Exception assessment** for the notification outbox and the `DecisionRecordWriter` communication-owner case.
  Blocks: the FR-26 matrix rows (may add exceptions to FR-21).
- [ ] **UAC-r2 timing** for the `OwnedChildWrite` refactor and census changes. Blocks: W2 start date.

---

*AI-optimized specification. Original design: `design.md` (2026-10-19). Evidence: `notes/design-review-2026-10-19.md`.*
