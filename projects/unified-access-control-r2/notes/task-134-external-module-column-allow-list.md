# Task 134 — external module column allow-list (defect C6, GitHub #1057)

> **Status**: code complete on `task/uac-r2-134`; **MANUAL live gate pending** (see §7).
> **Closes**: session-26 defect C6 · investigation gap G-10 (design-register D-3 annotated).

## 1. What changed

The external module read seam (`POST /api/v1/external/api/dataverse/fetch`,
`GET …/record/{entity}/{id}`) had **row** scope only. Both routes execute the caller's own
FetchXML / `$select` **app-only**, which bypasses field-level security. So a contact granted on one
project could read `sprk_graphdriveid`, `sprk_graphitemid` and `sprk_filepath` on every in-scope
document, and any internal column of an in-scope row.

| Piece | Where |
|---|---|
| `ExternalModuleDescriptor.ReadableColumns` (required; one new property) | `Infrastructure/ExternalAccess/ExternalModuleRegistry.cs` |
| `Register` refuses: null/empty list, blank entry, missing scope attribute, missing primary id (`{entity}id`), any `PointerColumns` entry | same file |
| Seven live-derived allow-lists | `Infrastructure/DI/ExternalAccessModule.cs` |
| Guard signal **(3) column scope**, ordered after entity identity and link-entity, inside the single `EvaluateFetchXmlGuard` | `Api/ExternalAccess/ExternalModuleDataEndpoints.cs` |
| New error codes `DV_FETCHXML_COLUMN_NOT_PERMITTED` (fetch) and `DV_RECORD_COLUMN_NOT_PERMITTED` (record `$select`) | same file |
| Post-execution strip (after `ScopeRows`; keeps `@logicalName`; filters `@formattedValues`) on both routes | same file |
| File header rewritten: the broker-only promise now names the mechanism that keeps it | same file |

`FetchService` and `RecordService` are **untouched**. They are shared with the internal `/api/dataverse`
surface; all enforcement lives in the external endpoint (constraint honoured).

### What the column signal refuses
In a join-free document (signals 1–2 already ran), the signal refuses:
- `all-attributes`;
- `<attribute name=X>` with X not allow-listed;
- any `alias=` on an attribute;
- any aggregate marker (`aggregate`, `groupby`, `dategrouping`, `rowaggregate`, `distinct`) on an attribute, and `<fetch aggregate='true'>`;
- `<condition attribute=X>` or `valueof=X` with X not allow-listed;
- `<condition entityname=…>`;
- `<order attribute=X>` with X not allow-listed, and `<order alias|entityname>`;
- any element other than `fetch / entity / attribute / filter / condition / value / order`.

Elements are matched by local name, case-insensitively, as join detection already does.

## 2. Allow-list derivation (live dev data, read-only, 2026-09-30)

**Sources read:** the six `sprk_gridconfiguration` records, every active main view (`savedquery`,
`querytype=0`) of the seven module entities, `EntityDefinitions` primary id/name, and `IsSecured`
attributes. **No escalation trigger fired.** No live grid or view references a pointer column, an alias
or an aggregate. No config or view was unreadable. No module entity has any FLS-secured attribute
(`IsSecured eq true` returned none, and `fieldpermission` has no rows for these entities).

**Rule:** the allow-list for each module is the union of:
- (a) the grid config's attribute, condition and order columns;
- (b) the columns of every sibling main view the grid's **ViewSelector** offers that **can render rows today**, meaning views that project a scope-dimension attribute. A view that projects none returns 0 rows after `ScopeRows`, so it adds no column;
- (c) the scope-dimension attributes;
- (d) the `/record` default projection (primary id + primary name).

| Module / entity | Grid config (record id) → columns | Renderable sibling views → extra columns | Scope | Primary id / name |
|---|---|---|---|---|
| collaboration / `sprk_project` | `61711823-1092-f111-b8dc-7ced8ddc4a05` → sprk_projectid, sprk_projectname, sprk_projectnumber, statuscode, modifiedon | Active Projects `195ab203` (+ createdon, statecode, ownerid, sprk_practicearea, sprk_projecttype_ref); My Projects `0e36d0a4` | sprk_projectid | sprk_projectid / **sprk_projectnumber** |
| documents / `sprk_document` | `3af4102c-1092-f111-b8dc-7ced8ddc4a05` → sprk_documentid, sprk_documentname, sprk_documenttype, createdon, sprk_project, sprk_matter, sprk_workassignment | none (All Documents `a2a31a92`, My Documents `1ab4a862`, Attachments `dc1f574d`, Invoice Review Queue `83643108` project no scope lookup → 0 rows today) | sprk_project, sprk_matter, sprk_workassignment | sprk_documentid / sprk_documentname |
| invoices / `sprk_invoice` | `3ff4102c-1092-f111-b8dc-7ced8ddc4a05` → sprk_invoiceid, sprk_invoicenumber, sprk_invoicedate, sprk_invoicestatus, sprk_totalamount, sprk_project, sprk_matter | Invoice - Matter Context `b9f6d045` (+ sprk_name, sprk_visibilitystate, modifiedon, statecode). Excluded (no scope lookup): Active/Inactive/All Invoices, All Invoices subgrid, Invoice Review Queue Projects | sprk_matter, sprk_project | sprk_invoiceid / sprk_name |
| work-assignments / `sprk_workassignment` | `42f4102c-1092-f111-b8dc-7ced8ddc4a05` → sprk_workassignmentid, sprk_workassignmentnumber, sprk_priority, sprk_responseduedate, statuscode, sprk_regardingproject | Active WAs `c8391ddf`, Inactive WAs `d73b2239`, My Work to Assign `b7cf5593` (+ sprk_name, createdon, statecode, ownerid, sprk_assignedto) | sprk_workassignmentid | sprk_workassignmentid / sprk_name |
| matters / `sprk_matter` | `583a2a33-1092-f111-b8dc-7ced8ddc4a05` → sprk_matterid, sprk_mattername, sprk_matternumber, statuscode | Active Matters `3ba2301f` (+ createdon, statecode, sprk_mattertype, sprk_practicearea); My Matters `6c3c5d88`; All Matters `694cd4b7` | sprk_matterid | sprk_matterid / **sprk_matternumber** |
| service-requests / `sprk_servicerequest` | `403e5d37-cb94-f111-b8db-00224835447a` → sprk_servicerequestid, sprk_servicerequestnumber, sprk_name, statuscode, createdon, sprk_requestedby | none (Inactive Service Requests `43f8bd8d` lacks sprk_requestedby → 0 rows) | sprk_requestedby | sprk_servicerequestid / sprk_name |
| grid-configuration / `sprk_gridconfiguration` | (no grid) — shared `DataGrid.tsx` `fetchConfigRecord` → `retrieveRecord(…, ['sprk_configjson'])` | n/a | sprk_gridconfigurationid | sprk_gridconfigurationid / sprk_name |

**Pointer columns excluded everywhere** (`ExternalModuleRegistry.PointerColumns`, enforced at startup):
sprk_graphdriveid, sprk_graphitemid, sprk_filepath, sprk_containerid, sprk_specontainerid,
sprk_containername, sprk_driveitemid, sprk_parentgraphitemid, sprk_parentfolderid, spk_fileviewerid.

## 3. Premise corrections (the POML's anchors vs. current source/data)

1. **`sprk_gridconfiguration` has no `sprk_fetchxml` or `sprk_layoutxml` columns.** The FetchXML and
   LayoutXML live inside `sprk_configjson` (`source.fetchXml` / `source.layoutXml`, `source.type="inline"`
   on all six records).
2. **`ConfigurationService.ts:68/:149` and `ViewService.ts:314` are not the external path.** They read
   through `xrm.WebApi` (MDA). The external grid-config read is `DataGrid.tsx:657`
   (`fetchConfigRecord` → `BffDataverseClient.retrieveRecord('sprk_gridconfiguration', id, ['sprk_configjson'])`).
3. **The external grids render a ViewSelector.** `GridWidgetBody` mounts `<DataGrid>` with the default
   `showViewSelector = true`, so each grid offers the entity's internal MDA main views from
   `/savedqueries/{entity}`. That is why rule (b) above exists.
4. All other anchors (route lines, guard, `ScopeRows`, `FetchService` / `RecordService` being shared,
   seven registrations) re-verified. Task 056 has **not** landed (seven modules, not ten).
5. Only `src/client/external-spa/src/services/gridDataverseClient.ts` calls this seam, so escalation
   trigger 3 does not fire.

## 4. Decisions and deviations

- **D1: sibling-view rule (b).** Including the columns of views that render 0 rows today would have
  admitted AI-triage columns (classification, invoice hints, review status) that no external caller
  can currently see. Excluding them changes those views from an empty grid to a grid error state.
  Neither showed data before, and none is the grid's default view.
  🔔 **Owner follow-up (not done here, client change):** the external grids expose internal MDA views
  through the ViewSelector at all. Passing `showViewSelector={false}` in
  `src/client/external-spa/src/widgets/GridWidgetBody.tsx` would let every allow-list shrink to its grid
  config plus scope plus default. That removes, for example, `ownerid`, `sprk_assignedto` and
  `sprk_visibilitystate` from external reach.
- **D2 (SUPERSEDED by the criterion-8 follow-up, §8): the primary-name check lived in a test, not in
  `Register`.** The original reasoning: the constraint allowed one new descriptor property and the
  primary name is metadata the descriptor cannot derive, so `Register` enforced only the primary id and a
  live-metadata test pinned each production list's primary name. The verifier ruled criterion 8 only
  PARTIALLY met on exactly this point; the follow-up adds a second descriptor property
  (`PrimaryNameAttribute`) and moves the check into `Register`. The live-metadata pin test is kept and now
  pins the DECLARATION.
- **D3: `/record` strips the synthetic `id` key.** `RecordService` echoes `entity.Id` as `"id"`. The
  criterion says "only allow-listed keys", and the primary-id column carries the same value, so the
  strip removes `"id"`. The only external `/record` caller reads `sprk_configjson`.
- **D4: the `/record` `$select` check runs before the Tier-2 gate.** This mirrors `/fetch`
  (guard before scoping). It is a pure input check that reads nothing.
- **D5: one error-code family, two codes.** `DV_FETCHXML_COLUMN_NOT_PERMITTED` and
  `DV_RECORD_COLUMN_NOT_PERMITTED`.
- **D6: test seam.** The two handlers became thin DI shells over `…CoreAsync(…)` methods that take the
  read as a delegate (`FetchService.ExecuteAsync` / `RecordService.GetRecordAsync`). The tests drive
  the real pipeline with a counting double. That is how "Dataverse not queried" and the
  "extra attribute + extra `@formattedValues` entry" strip are asserted. No new type, service, filter
  or route.
- **D7: money columns.** Dataverse may return `transactioncurrencyid` next to `sprk_totalamount`.
  It is not allow-listed and is stripped (logged at Debug). The DataGrid renderers do not read it
  (checked in Spaarke.UI.Components DataGrid).
- **⚠️ Maintenance consequence:** adding a column to an external grid configuration or main view now
  requires adding it to the module's `ReadableColumns`. Otherwise that grid gets a 400. The
  registration comment in `ExternalAccessModule.cs` says so.
- **Task 056** (three new child modules) must declare `ReadableColumns` (compile-time `required`, and
  startup validation). The main session should add that line to 056's POML.

## 5. Placement justification (CLAUDE.md §10 / `bff-extensions.md`)

The change belongs **in the BFF, extending the existing seam**. The external module read seam is a
BFF route group, and the column scope must be enforced where the app-only read happens. There is no
new endpoint, service, DI registration, package or background work: one property on an existing
descriptor, one ordered signal in the existing single-authority guard, and a strip in the existing
handlers. **NuGet: no change.** Publish-size measurement is skipped per run instructions (the main
session measures once after merging the batch).

## 6. Verification

- Tests (KEEP path `tests/integration/auth/UnifiedAccessControl/ExternalModuleColumnAllowListTests.cs`,
  63 cases) cover:
  - every FetchXML position: attribute, condition, `valueof`, order, aliased, aggregate, `groupby`,
    and `fetch aggregate`;
  - `all-attributes` and casing/namespace variants;
  - unknown elements and unprovable columns;
  - a missing list;
  - guard order (cross-entity → `EntityMismatch`, self-join → `LinkEntityNotPermitted`);
  - the fetch pipeline: refusal without a query, regression on scoped rows, strip plus `@logicalName`,
    and the empty-set negative in both directions;
  - the record pipeline: `$select` refusal without a read, a readable `$select`, the default
    projection, and an out-of-set 403;
  - registration refusals (5 shapes plus all 10 pointer columns);
  - production parity (live primary names, no pointers, each of the six live grid FetchXMLs admitted,
    `sprk_configjson` readable).
- HTTP wiring in `ExternalModuleDataContractTests` (+3): a pointer fetch returns 400 with the column
  code; a bad record `$select` returns 400 with the column code; a readable `$select` passes to the
  read (404 from the double).
- Updated for the new signature: `FetchXmlGuardSelfJoinTests` (`all-attributes` moved out of
  "allowed"), `ExternalScopeCharacterizationTests`, and `ExternalModuleRegistryTests` (descriptors
  declare lists).
- **Perturbations, all reverted (files byte-compared to backups):**

  | Seeded violation | Tests turned red |
  |---|---|
  | P1: column signal removed | 25 |
  | P2: post-execution strip removed (both routes) | 3 |
  | P3: alias check removed | 1 (`…WhenAReadableColumnIsAliased_RefusesOnTheAliasRuleAlone`) |
  | P4: `Register` validation removed | 15 |

- Internal `/api/dataverse` routes: `FetchService`, `RecordService` and their tests are unmodified.
- **Runs (2026-09-30):**

  | Run | Result |
  |---|---|
  | Affected classes (`ExternalModuleColumnAllowListTests`, `FetchXmlGuardSelfJoinTests`, `ExternalScopeCharacterizationTests`, `ExternalModuleRegistryTests`, `ExternalModuleDataContractTests`) | 121/121 passed |
  | Full `tests/unit/Sprk.Bff.Api.Tests` | 13,113 passed, 0 failed, 56 skipped (13,169 total) |
  | `tests/Spaarke.ArchTests` | 337/337 passed |
  | BFF build | 0 warnings |
  | `dotnet list package --vulnerable --include-transitive` | no vulnerable packages |

  No NuGet change.

## 6a. Step 9.5 quality gates

**code-review: no Critical findings.** Notes, all accepted or documented:
- (Info, security) The 400 `detail` echoes the caller's own column names and violation position.
  This is reflected input in JSON ProblemDetails, carries no record data, and is the same pattern as
  the existing entity-mismatch detail.
- (Info, observability) Stripped keys are logged at Debug, not Warning, so routine
  `transactioncurrencyid` noise does not flood the logs. The guard is the control; the strip is the
  backstop.
- (Info, behaviour) The D1 ViewSelector exposure and the D2 primary-name pinning are covered above.
- (Info, complexity) `ExternalModuleDataEndpoints.cs` grew by about 200 lines. The additions are
  cohesive: one guard, one strip, and two pipeline cores split out for testability. No decomposition
  is warranted (CLAUDE.md §11.5).

**adr-check: no violations.**

| ADR | Result |
|---|---|
| ADR-001 Minimal API | ✅ |
| ADR-003 | ✅ fail-closed on a missing list, an unparseable or unmodelled element, and the default arm |
| ADR-008 | ✅ no new filter; the inherited group filter is unchanged |
| ADR-010 | ✅ no new DI registration, interface or type |
| ADR-019 ProblemDetails | ✅ |
| ADR-028 A1–A3 broker-only | ✅ strengthened: pointer columns are unreachable |
| ADR-038 | ✅ KEEP path `tests/integration/auth/**`; no `Mock<HttpMessageHandler>`, no DI-registration or ctor-null tests; perturbation-proven |

No §6.5 challenge path is needed.

## 7. MANUAL live gate — PENDING (no live writes or deploys in this run)

This needs a dev deploy of the branch. Then, as an existing CIAM test contact in the correct BU and as
an existing non-admin workforce test user:
1. Open each of the six outside-counsel grids (Projects, Documents, Invoices, Work Assignments,
   Matters, Service Requests [workforce only]) and confirm the same columns and rows as before.
2. Hand-craft `POST /api/v1/external/api/dataverse/fetch` for `sprk_document` selecting
   `sprk_graphitemid` and confirm a **400 `DV_FETCHXML_COLUMN_NOT_PERMITTED`**.
3. Record screenshots / response captures here.

Expected visible change: picking an internal sibling view in a ViewSelector that already showed no
rows (e.g. Documents → "All Documents") now shows an error state instead of an empty grid (D1).

## 8. Criterion-8 follow-up (verifier: PARTIALLY met) — 2026-09-30

**Finding.** `Register` (`ValidateReadableColumns`) enforced the primary id (`{RecordEntity}id`) but not the
primary NAME, the other column of the no-`$select` `/record` default projection. A future descriptor whose
list omitted its primary name would start cleanly and strip every default `/record` read to an id-only
record; only the production pin test would notice, and only for the seven entities in its table.

**Fix.**
- `ExternalModuleDescriptor.PrimaryNameAttribute` — a second `required` property (supersedes D2's
  one-property reading of the CLAUDE.md §11 constraint; directed by the verifier, still no new type,
  service, registry or filter).
- `Register` now refuses at startup: an undeclared (null / blank) `PrimaryNameAttribute`, and a
  `ReadableColumns` that lacks the declared name — in addition to the existing refusals.
- All seven production descriptors declare it. **Live re-verification (READ-ONLY, `EntityDefinitions`
  `$select=LogicalName,PrimaryIdAttribute,PrimaryNameAttribute`, spaarkedev1, 2026-09-30):**

  | Entity | Primary id | Primary name |
  |---|---|---|
  | sprk_project | sprk_projectid | sprk_projectnumber |
  | sprk_document | sprk_documentid | sprk_documentname |
  | sprk_invoice | sprk_invoiceid | sprk_name |
  | sprk_workassignment | sprk_workassignmentid | sprk_name |
  | sprk_matter | sprk_matterid | sprk_matternumber |
  | sprk_servicerequest | sprk_servicerequestid | sprk_name |
  | sprk_gridconfiguration | sprk_gridconfigurationid | sprk_name |

  Identical to §2's original derivation; no allow-list content changed.
- The live-metadata pin test is kept and now also asserts each DECLARATION equals the live value (a
  wrong declaration would make the `Register` check vacuous). A new fact fails if any registered module
  is missing from the pin table, so a new module (e.g. task 056's three) cannot skip the live check.

**New tests** (`ExternalModuleColumnAllowListTests`): seeded descriptor missing the name column throws at
`Register` (theory row `missing primary name` + an exact-message fact naming `'sprk_documentname'`);
undeclared name `null` / `""` / `"  "` throws; a case-differing declaration is accepted;
`ProductionRegistry_EveryRegisteredModule_HasItsPrimaryNamePinnedToLiveMetadata`. Existing test
descriptors (`ExternalModuleRegistryTests`, this file) declare the name and carry it on their lists.

**Perturbations, all reverted (`git diff` re-checked):**

| Seeded violation | Tests turned red |
|---|---|
| P5: primary-name membership check disabled | 2 (`missing primary name` row, `…LacksTheDeclaredPrimaryName…`) |
| P6: undeclared-name check disabled | 3 (`…PrimaryNameIsNotDeclared…` ×3) |
| P7: production `sprk_project` declared as `sprk_projectname` (on the list, wrong per metadata) | 1 (pin test, `sprk_project`) |
| P8: `sprk_projectnumber` removed from the production project list | every production-registry test — the app refuses to start (`Register` throws in the fixture) |

**Runs (2026-09-30, after all perturbations reverted):**

| Run | Result |
|---|---|
| Affected classes (`ExternalModuleColumnAllowListTests`, `ExternalModuleRegistryTests`, `FetchXmlGuardSelfJoinTests`, `ExternalScopeCharacterizationTests`) | 117/117 passed |
| Full `tests/unit/Sprk.Bff.Api.Tests` | 13,120 passed, 0 failed, 56 skipped (13,176 total; +7 new cases) |
| `tests/Spaarke.ArchTests` | 337/337 passed |

Publish size skipped per run instructions (main session measures after merge). No NuGet change.

**Owner question (open, no client change): D1 / ViewSelector.** Recorded in the POML notes — whether
external grids should set `showViewSelector={false}`, which would let every allow-list shrink.
