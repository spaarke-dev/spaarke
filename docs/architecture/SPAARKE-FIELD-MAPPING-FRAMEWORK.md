# Spaarke Field Mapping Framework Architecture

> **Last Updated**: 2026-07-09
> **Purpose**: Describes the shipped mechanism that auto-populates fields on a wizard-created child record (Event, Invoice, Report Card, Matter, Project, To Do, Work Assignment) from its host record (Matter/Project/etc.) at creation time, driven entirely by admin-authored Dataverse configuration.

---

## Overview

The Field Mapping Framework lets an admin declare, as data (not code), that creating an Event/Invoice/Report Card/etc. "under" a Matter or Project should carry specific field values forward — e.g. the Matter's assigned attorneys should also appear on the Event. Two Dataverse tables hold that configuration; a single BFF endpoint reads it; a context-agnostic client engine applies it to the in-memory create payload immediately before the wizard's `createRecord` call. No Dataverse plugin, form script, or new PCF control is involved anywhere in the mechanism — it is entirely client-side, wired into the existing `Create*Wizard` React components.

The framework shipped once already (February 2026, embedded in the now-retired `AssociationResolver` PCF) and was deleted as unrelated collateral damage during a later picker-consolidation effort. This project (`set-regarding-and-field-mapping-resolver-r2`) rebuilt the apply engine from scratch as a context-agnostic module (ADR-012) callable from any wizard service, extended the BFF's rule contract to carry every field the engine needs, and seeded the initial Matter→{Event, Invoice, Report Card} attorney-matrix configuration. The load-bearing design decision is the **creation-time / update-time split** (see Constraints): mappings apply automatically once, at the moment a new child record is created, because a brand-new record has no existing data to protect. Refreshing an **already-existing** child's fields remains a separate, manual, ribbon-triggered mechanism (`UpdateRelatedButton` → `POST /api/v1/field-mappings/push`) that this project did not change.

## Component Inventory (Code)

This is the complete build surface. Every row is a distinct, live component; paths are repo-relative. Layers run top-to-bottom from storage → server → client engine → wizard call-sites.

### Dataverse configuration (storage)

| Component | Path / location | Responsibility |
|-----------|------|-----------------|
| `sprk_fieldmappingprofile` (table) | Dataverse (`spaarkedev1`) · `infrastructure/dataverse/solutions/FieldMappingAdminSolution/Entities/sprk_FieldMappingProfile/` | One row per (source entity, target entity) pair: `sprk_sourcerecordtype`/`sprk_targetrecordtype` (**lookups to `sprk_recordtype_ref`**, not name strings), `sprk_compatibilitymode`, `sprk_description`, `sprk_name`, `statecode` (0=Active / 1=Inactive → `IsActive`) |
| `sprk_fieldmappingrule` (table) | Dataverse (`spaarkedev1`) · `.../Entities/sprk_FieldMappingRule/` | Child rows of a profile (relationship `sprk_fieldmappingrule_FieldMappingProfile_n1`, referencing attribute `sprk_FieldMappingProfile`): `sprk_sourcefield`/`sprk_targetfield` + their `sprk_sourcefieldtype`/`sprk_targetfieldtype`, `sprk_mapping_type` (Copy0/Default1/Concat2/Template3), `sprk_defaultvalue` (`NVARCHAR(100)`), **`sprk_expression`** (`Memo`, max 2000 — added this project), `sprk_compatibilitymode` (Strict0/Resolve1), `sprk_isrequired`, `sprk_executionorder`, `sprk_isactive` |
| `sprk_recordtype_ref` (table) | Dataverse (`spaarkedev1`) | The authoritative entity-catalog both profile lookups resolve through; `sprk_recordlogicalname` is the logical-name string the BFF maps ↔ GUID. A target entity **must** have a row here before a profile can reference it. Shared with the ADR-024 resolver ecosystem |

> **Nav-property fact (load-bearing):** the profile→rule `$expand` name the BFF uses is `sprk_fieldmappingrule_FieldMappingProfile_sprk_fieldmappingprofile`. This exact string is in `DataverseWebApiService.cs`; a wrong value here is one of the four latent bugs fixed during UAT (see Operational Notes).

### BFF API (server)

| Component | Path | Responsibility |
|-----------|------|-----------------|
| `FieldMappingEndpoints.cs` | `src/server/api/Sprk.Bff.Api/Api/FieldMappings/` | Minimal API group `/api/v1/field-mappings` (auth + `dataverse-query` rate-limit). Routes: `GET /profiles`, `POST /validate`, **`GET /profiles/{sourceEntity}/{targetEntity}`** (the creation-time engine's only read; **404 when no profile**), `POST /push` (update-time, ≤500 children). Also holds the int→string projections `MapMappingTypeToString`, `MapCompatibilityModeToString`, `MapFieldTypeToString`, `MapSyncModeToString` |
| `FieldMappingRuleDto.cs` / `FieldMappingProfileWithRulesDto.cs` / `FieldMappingProfileDto.cs` | `src/server/api/Sprk.Bff.Api/Models/FieldMapping/` | The additive rule/profile contract the client engine consumes (`MappingType`, `DefaultValue`, `Expression`, `IsRequired`, `CompatibilityMode` surfaced this project) |
| `PushFieldMappingsRequest.cs` / `PushFieldMappingsResponse.cs` | `.../Models/FieldMapping/Dtos/` | Request/response for the update-time `POST /push` path (unchanged logic; consumes the same additive DTO) |
| `IFieldMappingDataverseService.cs` | `src/server/shared/Spaarke.Dataverse/` | Service contract: `GetFieldMappingProfileWithRulesAsync`, `QueryFieldMappingProfilesAsync`, `GetFieldMappingRulesAsync`, `RetrieveRecordFieldsAsync`, `QueryChildRecordIdsAsync`, `UpdateRecordFieldsAsync` |
| `DataverseWebApiService.cs` | `src/server/shared/Spaarke.Dataverse/` | Implements the read: `LookupRecordTypeIdsAsync` (logical name → `sprk_recordtype_ref` GUID) then one profile query with `$expand` of active rules ordered by `sprk_executionorder`; `MapToFieldMappingProfileEntity`/`MapToFieldMappingRuleEntity` project the OData rows. `FieldMappingProfileEntity`/`FieldMappingRuleEntity` in `Models.cs` are the read-side shapes |

### Client engine + call-sites (browser)

| Component | Path | Responsibility |
|-----------|------|-----------------|
| `FieldMappingService.ts` | `src/client/shared/Spaarke.UI.Components/src/services/` | The client apply engine — `applyFieldMappings(...)`, one BFF call, one source read, dispatch over the four mapping types, never-throws |
| `FieldMappingTypes.ts` | `src/client/shared/Spaarke.UI.Components/src/types/` | `IFieldMappingProfile`/`IFieldMappingRule`/`IMappingResult` + the `FieldMappingType`/`FieldMappingFieldType`/`FieldMappingCompatibilityMode` unions — mirrors the BFF DTOs 1:1 |
| `PolymorphicResolverService.ts` (`discoverNavProps`/`findNavProp`) | `src/client/shared/Spaarke.UI.Components/src/services/` | Shared nav-property discovery, hoisted this project so the engine's lookup-Copy path and every wizard service use one implementation |
| 7 `Create*Wizard` services (`eventService.ts`, `matterService.ts`, `projectService.ts`, `todoService.ts`, `workAssignmentService.ts`, `invoiceService.ts`, `reportCardService.ts`) | `src/client/shared/Spaarke.UI.Components/src/components/Create*Wizard/` | Each calls `applyFieldMappings` on its create payload, after resolver-field application, before `createRecord`. Target entities: `sprk_event`/`sprk_matter`/`sprk_project`/`sprk_todo`/`sprk_workassignment`/`sprk_invoice`/`sprk_reportcard` |
| `FieldMappingService.test.ts` | `.../src/services/__tests__/` | 18 engine unit tests (all four types, never-throws, single-fetch, same-entity, lookup bind, silent empty-lookup skip) |

### Supporting / update-time surface

| Component | Path | Responsibility |
|-----------|------|-----------------|
| `UpdateRelatedButton` PCF (unchanged) | `src/client/pcf/` | Pre-existing **manual push** trigger for already-existing child records → `POST /push`; untouched by this project |
| `sprk_fieldmapping_push.js` (web resource) | `src/client/webresources/js/` | MDA ribbon script that calls `POST /push` from a parent form |
| `FieldMappingAdmin` PCF (`bundle.js`) | `.../FieldMappingAdminSolution/Controls/sprk_Spaarke.Controls.FieldMappingAdmin/` | Ships in the admin solution but is **not** the supported authoring path — profiles/rules are authored on the **native MDA forms** (see Design Decisions "Native Dataverse forms for admin authoring"). Present as legacy; the admin guide does not use it |

## PCF Hosts & the Set-Regarding Resolver (related controls — NOT part of the engine)

The field-mapping engine has **no PCF of its own** — it is a shared-library module (ADR-012). It rides inside two PCF controls that are deployed and versioned independently, and it is *fed* by a third. These are listed here so the "code and PCF" inventory is complete; the boundary is deliberate (per project `design.md` §8, the resolver is context this framework consumes, not a deliverable it owns).

| PCF control | Version | Deployed on | Data access | Relationship to field mapping |
|---|---|---|---|---|
| **VisualHost** (`Spaarke.Visuals.VisualHost`) | v1.4.34 | **Parent** forms (dashboards/cards) | Xrm.WebApi for charts; **BFF** for the wizard path | The **sole wizard host**: its "+" button uses `WizardRegistry.resolveWizard(...)` to lazy-mount the correct `Create*Wizard` in a Fluent `Dialog`, seeded with the host record as a locked `initialAssociation`. It bundles the shared-lib source, so shipping the wizards transitively bundles the engine — **the mapping cascade fires inside the wizard's service when the child is created.** VisualHost never calls `applyFieldMappings` directly |
| **RegardingResolver** (`Spaarke.Controls.RegardingResolver`) | v1.4.6 | **Child** forms (`sprk_todo`, `sprk_event`, `sprk_invoice`, `sprk_communication`, `sprk_kpiassessment`, …) | **Xrm.WebApi only** (no BFF) | The runtime "set-regarding" picker (ADR-024). A user picks a polymorphic parent — or the control auto-detects a subgrid-pre-populated `sprk_regarding{Entity}` lookup — and it writes the 5 denormalized regarding fields (`sprk_regardingrecordtype`/`id`/`name`/`url`/`number`) via `PolymorphicResolverService`. **This is what supplies the field-mapping engine's `sourceEntity`/`sourceId`** when a record is created with a regarding parent already set. It does *not* copy field values — that is the engine's job |
| **AssociationResolver** — **RETIRED (SRFR-045, 2026-07-05)** | — | — | — | The Feb-2026 framework originally lived inside this PCF. It was retired: its picker duty was 100% redundant with RegardingResolver, and its subgrid auto-detect + CREATE-mode helpers were folded into `RegardingResolverApp.tsx`. Its retirement is *why* the apply engine had to be rebuilt from scratch this project. **There is now one resolver control, not two.** Any doc or table still listing AssociationResolver is stale |

**How they interlock at runtime:** RegardingResolver (on the child form, or via the wizard's association block) establishes the *regarding parent* → the `Create*Wizard` service reads that parent as `sourceEntity`/`sourceId` → `applyFieldMappings` fetches the `{parent → child}` profile from the BFF and enriches the create payload → the child is born with inherited fields. VisualHost is just the shell that launches the wizard; RegardingResolver is the linkage; the engine is the copy.

## Configuration Enum Reference

These are the option-set integer values a maker sees as labels on the form, and that Claude Code / the Web API must send as integers when seeding programmatically (see the admin guide's seeding recipe).

| Field | Value → label |
|---|---|
| `sprk_mapping_type` | `0`=Copy · `1`=Default · `2`=Concat · `3`=Template |
| `sprk_sourcefieldtype` / `sprk_targetfieldtype` | `0`=Text · `1`=Lookup · `2`=OptionSet · `3`=Number · `4`=DateTime · `5`=Boolean · `6`=Memo |
| `sprk_compatibilitymode` | `0`=Strict · `1`=Resolve |
| `statecode` (profile active flag) | `0`=Active · `1`=Inactive |

## Data Flow

The primary path is **creation-time apply**, exercised by every `Create*Wizard`:

1. A user completes a wizard (e.g. "Create Event" from a Matter). The wizard's `onFinish`/service builds the create payload and — where a resolver/association block exists — calls `applyResolverFields` to write the regarding lookup(s) first.
2. Immediately after that, and **before** `createRecord`, the service calls `applyFieldMappings({ sourceEntity, sourceId, targetEntity, payload, dataService, authenticatedFetch, bffBaseUrl })`.
3. The engine issues **exactly one** BFF call: `GET /api/v1/field-mappings/profiles/{sourceEntity}/{targetEntity}`.
   - `404` (no profile configured for this pair) → graceful no-op, `{ profileFound: false, fieldsMapped: [], warnings: [] }`. No behavior change from today.
   - `200` → a `FieldMappingProfileWithRulesDto` with `rules` already `$expand`-ed (one Dataverse round-trip server-side, per the profile+rules query).
4. The engine batches every rule's source-field needs (Copy fields, plus every `{sprk_field}` placeholder referenced by any Concat/Template rule) into **one combined `$select`** and fetches the source record **once** via the injected `IDataService`.
5. Rules are applied in `sprk_executionorder` (priority) order, dispatching on `mappingType` — Copy / Default / Concat / Template (see Design Decisions). Each rule's apply is independently guarded: a failure appends a warning and the loop continues; the engine itself never throws.
6. The engine returns `{ profileFound, fieldsMapped, warnings }`. The calling wizard service appends `warnings` to its own existing warnings array (the same convention `applyResolverFields` already uses) and proceeds to `createRecord` with the now-enriched payload.

The **secondary path** — refreshing an already-existing child record — is unchanged: `UpdateRelatedButton` PCF → `POST /api/v1/field-mappings/push`, which re-queries the profile server-side and updates every related child in a batch (up to 500 records). This project extended the DTO the push path also uses (additively) but did not touch its logic.

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Depends on | `sprk_recordtype_ref` registry | `_sprk_sourcerecordtype_value`/`_sprk_targetrecordtype_value` lookups | Same authoritative entity-metadata table used across the ADR-024 resolver ecosystem; a target entity must have a row here before a profile can reference it as target (Report Card's missing row was created by this project — see Design Decisions) |
| Depends on | `PolymorphicResolverService.discoverNavProps`/`findNavProp` | Shared nav-prop discovery | Copy-lookup rules resolve the *target* entity's OData navigation property this way; the same utility now backs the field-mapping engine and every `Create*Wizard` service's own lookup binding |
| Depends on | `IDataService` (context-agnostic) | `retrieveRecord(entity, id, options)` | The engine's only Dataverse read; in production this is always the `Xrm.WebApi`-backed adapter (`createXrmDataService()`), never the BFF-record adapter — see Constraints |
| Consumed by | 7 `Create*Wizard` services | `applyFieldMappings(...)` | The engine's only public entry point; each service supplies its own `sourceEntity`/`sourceId`/`targetEntity` derived from the resolved host association |
| Consumed by | `UpdateRelatedButton` → `/push` (unchanged) | Server-side `ApplyMappingRule` in `FieldMappingEndpoints.cs` | A structurally separate, simpler apply (Copy + basic type coercion only — no Default/Concat/Template dispatch); this project's additive DTO fields do not alter its behavior |
| Sibling, not integrated with | `TodoRegardingUpdateBuilder`/`TODO_REGARDING_CATALOG` | — | Governs which entities a To Do's *regarding* lookup may point at; orthogonal to field-mapping inheritance. `todoService.ts` reads the catalog for `entityType` but the catalog itself is never mutated by this framework |

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| Creation-time cascade is automatic; update-time cascade stays manual | Apply mappings once, at child-record creation, with zero user action. Refreshing an existing child's fields remains the ribbon-triggered `UpdateRelatedButton` → `/push` flow | A brand-new record has no existing data to protect, so auto-apply carries none of the overwrite risk that motivated the original manual-only decision for already-existing records | amends predecessor project's non-goal (see project design.md §2) |
| Build all four mapping-type engines now | Copy, Default/Constant, Concat, Template | Owner directive to close the topic once rather than reopening the BFF contract incrementally per type | — |
| BFF DTO extended additively, no new endpoint | `FieldMappingRuleDto` gained `mappingType`, `defaultValue`, `expression`, `isRequired`, `compatibilityMode`; `sprk_mapping_type`/`sprk_expression` added to the `$select` | The existing `GET profiles/{source}/{target}` endpoint already returned the profile+rules shape the engine needs; the DTO simply hadn't surfaced fields the server already read (or, for `sprk_mapping_type`/`sprk_expression`, hadn't read at all) | §10 BFF Hygiene — additive-only |
| `sprk_expression` (Memo, 2000 chars) added to `sprk_fieldmappingrule` | New nullable column, separate from `sprk_defaultvalue` (`NVARCHAR(100)`) | `sprk_defaultvalue`'s 100-char limit and "literal" semantics are wrong for a Concat/Template format string; a new column keeps both mapping types genuinely usable rather than aspirational | — |
| Client engine is context-agnostic | `IDataService` + `AuthenticatedFetchFn` injected; no `ComponentFramework`/PCF types anywhere in `FieldMappingService.ts` | ADR-012 — the engine must run from any host (PCF wizard host today; any future Code Page/Office Add-in host tomorrow) without modification | ADR-012 |
| No `source === target` guard anywhere (engine, BFF, seed) | Same-entity pairs (e.g. matter→matter) are queried, fetched, and applied exactly like any other pair | Storage (recordtype-ref lookups) and the engine's signature are already pair-agnostic; creation-time application is a single hop per invocation, so same-entity carries no recursion risk (see Constraints) | — |
| No Dataverse plugin / form script, ever | The engine is called only from `Create*Wizard` service `onFinish`/create methods | Absolute owner constraint — creation-time mapping only exists for entities created through a React wizard; anything created outside a wizard falls back to the manual push, it is never a candidate for a plugin | project memory `no-dataverse-plugins` |
| Native Dataverse forms for admin authoring, no new PCF | `sprk_fieldmappingprofile`/`sprk_fieldmappingrule` are authored via the standard MDA form + subgrid | Two independent prior projects had already concluded native forms were sufficient for this authoring volume; a Feb-2026 build of 4 custom admin PCFs was the outlier, not the standard, and this project does not resurrect them | §11 Component Justification |

## Constraints

- **MUST NOT** invoke or reference `ComponentFramework`/PCF types anywhere in `FieldMappingService.ts` or `FieldMappingTypes.ts` (ADR-012). All I/O is injected.
- **MUST** make exactly one BFF call per `applyFieldMappings` invocation (`GET profiles/{source}/{target}`) and exactly one `IDataService.retrieveRecord` call per invocation (a single combined `$select` spanning every Copy rule's fields and every Concat/Template rule's `{sprk_field}` placeholders) — never one fetch per rule.
- **MUST NOT** throw. Every failure — no profile, a failed fetch, a missing source field, an unresolvable lookup annotation, an unresolved placeholder — is reported as a warning string and the affected rule is skipped; sibling rules still apply and the wizard's `createRecord` still proceeds.
- **MUST NOT** guard on `sourceEntity === targetEntity` or `sourceField === targetField` anywhere in the engine, the BFF query, or seed data. Same-entity creation-time mapping (e.g. `sprk_practicearea` self-mapping on a matter→matter pair) is a supported, tested scenario.
- **Known limitation — lookup Copy requires the Xrm.WebApi adapter.** Lookup-Copy resolution reads `_<field>_value` plus the `@Microsoft.Dynamics.CRM.lookuplogicalname` OData annotation from the source record. That annotation is present when `IDataService` is backed by `createXrmDataService()` (the confirmed, sole production wiring for all 7 wizards, per `VisualHostRoot.tsx`) but is **not** present through the BFF's `/api/dataverse/record/{entity}/{id}` adapter (`createBffDataService()`), which unwraps lookups into a plain `{id, logicalName, name}` shape with no annotation. A Lookup Copy rule run against the BFF adapter would gracefully warn-and-skip rather than throw, but would not bind. Not addressed in this project because no current caller uses that adapter for this engine.
- **Recursion boundary**: creation-time mapping fires exactly once per wizard invocation (a single hop, parent record → new child payload) — there is no re-firing on subsequent updates and no multi-hop chaining. This is what makes same-entity creation-time mapping safe without a guard. Update-time same-entity cascade (e.g. re-cascading from a saved parent matter down through a chain of child matters) is an explicit non-goal and would need its own recursion-depth guard if ever built — it is not built here.
- **MUST** treat `sprk_expression` as the single placeholder-resolution seam for both Concat and Template — one `resolveExpression` function serves both mapping types; they differ only by author intent (joined fields vs. fixed scaffold + fields), not by mechanism. An unresolved `{sprk_field}` token is replaced with an empty string (never left as the literal token) and reported as a warning.
- **MUST NOT** apply a Concat/Template rule to a `Lookup`-typed target field — a format string cannot produce an `@odata.bind` value; such rules warn and skip.
- **Per-wizard wiring is asymmetric, not uniform.** `WorkAssignmentService`/`InvoiceService` require `authenticatedFetch`/`bffBaseUrl` at construction; `EventService`/`ProjectService`/`TodoService`/`ReportCardService` accept them as optional (or, for `ReportCardService`, were made required as part of this project's wiring) — when either dependency is absent, the engine call is skipped as a graceful no-op, identical to the "no profile found" path. `Matter`/`Project` had no pre-create "regarding parent" parameter before this project; both gained an `association` parameter specifically so the engine has a source entity/id to call with.
- **Known scope gap (documented, not a defect)**: To Do records created via the cross-wizard "Add a To Do" follow-on (`createTodoRegardingChild`, invoked from `CreateInvoiceWizard`/`CreateReportCardWizard`) do not yet receive `authenticatedFetch`/`bffBaseUrl`, so field mapping is skipped (no-op) for that specific creation path. The primary standalone "Create New To Do" wizard is fully wired. A follow-up task would thread the two dependencies through `createTodoRegardingChild`'s two call sites.
- **MUST** verify target field names per entity pair before seeding configuration — target field names diverge across entities (e.g. Matter's `sprk_assignedattorney1` maps to Invoice's `sprk_assignedtoattorney1`; Invoice has no law-firm field at all). Seed data is authored per-pair against `describe`-verified schema, never assumed identical across targets.

## Operational Notes (UAT hardening)

The framework's first real consumer surfaced four **pre-existing latent bugs** in shared infrastructure during live UAT; all four are fixed on `master`. They are recorded here because three of them live in shared code (`DataverseWebApiService`) and affect every caller, not just field mapping:

1. **`DataverseWebApiService` `BaseAddress` was missing a trailing slash** — `…/api/data/v9.2` + a relative request URI drops `v9.2` per RFC-3986, producing a versionless URL that Dataverse answers with a 500. Shared-infra fix; affects **all** `DataverseWebApiService` HTTP calls, not only field mapping.
2. **Wrong `$expand` navigation property** — was `sprk_fieldmappingprofile_fieldmappingrule`; corrected to `sprk_fieldmappingrule_FieldMappingProfile_sprk_fieldmappingprofile` (the real relationship name).
3. **Unguarded `GetInt32()`/`GetBoolean()` on null `$expand` fields** — a Dataverse `$expand` includes null columns; the rule mapper now null-guards every scalar read in `MapToFieldMappingRuleEntity`.
4. **Noisy empty-lookup warnings (cosmetic)** — an unset source lookup now skips **silently**; a warning is emitted only for the genuine anomaly (a *populated* lookup whose `@odata.bind` annotation is missing).

**Debugging lesson recorded for maintainers:** hand-built repro queries with hardcoded GUIDs diverged from the BFF's actual code path (they skipped `LookupRecordTypeIdsAsync`). The container-log stack trace (Kudu VFS `…/api/vfs/LogFiles/<date>_containerStream.log`) was the turning point — pull server logs early rather than trusting a hand-built reproduction.

**Deploy note:** because bug 1 is in shared `Spaarke.Dataverse`, the BFF must be deployed for the fix to take effect (`spaarke-bff-dev`, verified 200 on `GET /api/v1/field-mappings/profiles/sprk_matter/sprk_event`). The engine changes ship inside **VisualHost** (currently v1.4.34) — the wizard host — since that PCF bundles the shared-lib source.

## Related

- [ADR-012](../../.claude/adr/ADR-012-shared-component-library.md) — context-agnostic shared library rule the engine complies with
- [ADR-024](../../.claude/adr/ADR-024-polymorphic-resolver-pattern.md) — the polymorphic regarding pattern the engine's `sourceEntity`/`sourceId` inputs come from, and the `sprk_recordtype_ref` registry this framework's profiles reference
- `.claude/constraints/bff-extensions.md` — the additive-DTO-extension checklist this project's BFF change followed
- `projects/set-regarding-and-field-mapping-resolver-r2/design.md` — full decision log (source of the creation-time-vs-update-time amendment and same-entity resolution)
- `docs/guides/FIELD-MAPPING-ADMIN-GUIDE.md` — maker-facing guide for authoring profiles/rules on the native MDA form, including how to write a `sprk_expression` template (companion doc, authored in parallel by task 041)
