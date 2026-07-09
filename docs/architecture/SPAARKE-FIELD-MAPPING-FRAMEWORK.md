# Spaarke Field Mapping Framework Architecture

> **Last Updated**: 2026-07-09
> **Purpose**: Describes the shipped mechanism that auto-populates fields on a wizard-created child record (Event, Invoice, Report Card, Matter, Project, To Do, Work Assignment) from its host record (Matter/Project/etc.) at creation time, driven entirely by admin-authored Dataverse configuration.

---

## Overview

The Field Mapping Framework lets an admin declare, as data (not code), that creating an Event/Invoice/Report Card/etc. "under" a Matter or Project should carry specific field values forward — e.g. the Matter's assigned attorneys should also appear on the Event. Two Dataverse tables hold that configuration; a single BFF endpoint reads it; a context-agnostic client engine applies it to the in-memory create payload immediately before the wizard's `createRecord` call. No Dataverse plugin, form script, or new PCF control is involved anywhere in the mechanism — it is entirely client-side, wired into the existing `Create*Wizard` React components.

The framework shipped once already (February 2026, embedded in the now-retired `AssociationResolver` PCF) and was deleted as unrelated collateral damage during a later picker-consolidation effort. This project (`set-regarding-and-field-mapping-resolver-r2`) rebuilt the apply engine from scratch as a context-agnostic module (ADR-012) callable from any wizard service, extended the BFF's rule contract to carry every field the engine needs, and seeded the initial Matter→{Event, Invoice, Report Card} attorney-matrix configuration. The load-bearing design decision is the **creation-time / update-time split** (see Constraints): mappings apply automatically once, at the moment a new child record is created, because a brand-new record has no existing data to protect. Refreshing an **already-existing** child's fields remains a separate, manual, ribbon-triggered mechanism (`UpdateRelatedButton` → `POST /api/v1/field-mappings/push`) that this project did not change.

## Component Structure

| Component | Path | Responsibility |
|-----------|------|-----------------|
| `sprk_fieldmappingprofile` (table) | Dataverse (spaarkedev1) | One row per (source entity, target entity) pair: `sprk_sourcerecordtype`/`sprk_targetrecordtype` (lookups to `sprk_recordtype_ref`), `sprk_capabilitymode`, `sprk_description`, `sprk_name`, `statecode` (active/inactive) |
| `sprk_fieldmappingrule` (table) | Dataverse (spaarkedev1) | Child rows of a profile: `sprk_sourcefield`/`sprk_targetfield` + their `sprk_sourcefieldtype`/`sprk_targetfieldtype`, `sprk_mapping_type` (Copy0/Default1/Concat2/Template3), `sprk_defaultvalue` (`NVARCHAR(100)`), **`sprk_expression`** (`Memo`, max 2000 — added this project), `sprk_compatibilitymode`, `sprk_isrequired`, `sprk_executionorder`, `sprk_isactive` |
| `FieldMappingEndpoints.cs` | `src/server/api/Sprk.Bff.Api/Api/FieldMappings/` | Minimal API group `/api/v1/field-mappings`; this framework's read path is `GET profiles/{sourceEntity}/{targetEntity}` |
| `FieldMappingRuleDto.cs` / `FieldMappingProfileWithRulesDto.cs` | `src/server/api/Sprk.Bff.Api/Models/FieldMapping/` | The additive rule/profile contract the client engine consumes |
| `DataverseWebApiService.cs` | `src/server/shared/Spaarke.Dataverse/` | Reads `sprk_fieldmappingprofile`/`sprk_fieldmappingrule` via OData `$select`/`$expand`; `FieldMappingProfileEntity`/`FieldMappingRuleEntity` in `Models.cs` are the read-side entity shapes |
| `FieldMappingService.ts` | `src/client/shared/Spaarke.UI.Components/src/services/` | The client apply engine — `applyFieldMappings(...)`, one BFF call, dispatch over the four mapping types |
| `FieldMappingTypes.ts` | `src/client/shared/Spaarke.UI.Components/src/types/` | `IFieldMappingProfile`/`IFieldMappingRule`/`IMappingResult` — mirrors the BFF DTOs 1:1 |
| `PolymorphicResolverService.ts` (`discoverNavProps`/`findNavProp`) | `src/client/shared/Spaarke.UI.Components/src/services/` | Shared nav-property discovery, hoisted this project so the engine's lookup-Copy path and every wizard service use one implementation |
| 7 `Create*Wizard` services (`eventService.ts`, `matterService.ts`, `projectService.ts`, `todoService.ts`, `workAssignmentService.ts`, `invoiceService.ts`, `reportCardService.ts`) | `src/client/shared/Spaarke.UI.Components/src/components/Create*Wizard/` | Each calls `applyFieldMappings` on its create payload, after resolver-field application, before `createRecord` |
| `UpdateRelatedButton` PCF (unchanged) | `src/client/pcf/` | The pre-existing manual push mechanism for already-existing child records — untouched by this project |

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

## Related

- [ADR-012](../../.claude/adr/ADR-012-shared-component-library.md) — context-agnostic shared library rule the engine complies with
- [ADR-024](../../.claude/adr/ADR-024-polymorphic-resolver-pattern.md) — the polymorphic regarding pattern the engine's `sourceEntity`/`sourceId` inputs come from, and the `sprk_recordtype_ref` registry this framework's profiles reference
- `.claude/constraints/bff-extensions.md` — the additive-DTO-extension checklist this project's BFF change followed
- `projects/set-regarding-and-field-mapping-resolver-r2/design.md` — full decision log (source of the creation-time-vs-update-time amendment and same-entity resolution)
- `docs/guides/FIELD-MAPPING-ADMIN-GUIDE.md` — maker-facing guide for authoring profiles/rules on the native MDA form, including how to write a `sprk_expression` template (companion doc, authored in parallel by task 041)
