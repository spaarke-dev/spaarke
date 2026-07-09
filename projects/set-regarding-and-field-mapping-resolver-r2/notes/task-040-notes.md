# Task 040 — Field Mapping Framework architecture doc — Notes

**Completed**: 2026-07-09 · Rigor: MINIMAL (docs task) · Model: sonnet@high

## What was done

Authored `docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md` (docs-architecture skill
conventions: Overview → Component Structure → Data Flow → Integration Points → Design
Decisions → Constraints → Related). Added an entry row to `docs/architecture/INDEX.md`
under "Feature Architectures" (alongside `event-to-do-architecture.md`).

Grounded against the merged code, not the design's aspirations:
- `src/client/shared/Spaarke.UI.Components/src/services/FieldMappingService.ts` (read in full — 618 lines)
- `src/client/shared/Spaarke.UI.Components/src/types/FieldMappingTypes.ts` (read in full)
- `src/server/api/Sprk.Bff.Api/Api/FieldMappings/FieldMappingEndpoints.cs` (read in full)
- `src/server/api/Sprk.Bff.Api/Models/FieldMapping/FieldMappingRuleDto.cs`
- `src/server/shared/Spaarke.Dataverse/Models.cs` (`FieldMappingProfileEntity`/`FieldMappingRuleEntity`)
- `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs` (`$select`/`$expand` confirming
  the physical profile field is `sprk_capabilitymode`, not `sprk_syncmode` as the DTO's `SyncMode`
  property name might suggest)
- design.md (decision log §0, §2 core tension, §10 same-entity, §11 doc deliverables)
- Task notes 001, 002, 010, 012, 013, 014, 015, 020, 021, 022, 030 (all read in full)

## Doc coverage (against the task's 10-point checklist)

1. Two tables — covered in Component Structure (exact field lists incl. `sprk_capabilitymode`,
   `sprk_expression` new-this-project).
2. BFF contract — covered in Component Structure + Data Flow step 3 (single GET, additive DTO
   fields named explicitly).
3. Client engine — covered in Data Flow + Component Structure (`applyFieldMappings` signature,
   context-agnostic, never-throw, one BFF call + one batched source fetch).
4. Four mapping types — covered in Data Flow step 5 + Constraints (Copy incl. lookup bind
   mechanism; Default; Concat/Template shared `resolveExpression`).
5. `sprk_expression` extensibility — covered in Design Decisions row + Constraints (single seam
   for both Concat/Template, unresolved-token handling).
6. Creation-time vs update-time boundary — covered in Overview + Design Decisions (top row) +
   Data Flow secondary path (`UpdateRelatedButton`/push, unchanged).
7. Same-entity + recursion note — covered in Design Decisions + Constraints ("Recursion boundary"
   bullet — single-hop creation-time is safe by construction; update-time cascade is the deferred
   case).
8. Wiring (all 7 services) — covered in Component Structure (file list) + Constraints
   ("Per-wizard wiring is asymmetric" bullet, incl. the Matter/Project new `association` param
   nuance and the To Do follow-on gap).
9. Seeded config — covered in Constraints (per-pair schema-verification bullet) — the seed
   matrix itself is data, not architecture, so full seed detail is left to task 030's notes /
   the admin guide (task 041), per docs-architecture's "no environment-specific values" rule.
10. Invariants (no plugin, no form script, no new PCF) — covered in Overview + Design Decisions
    (native-forms-for-authoring row + no-plugin row, citing project memory `no-dataverse-plugins`).

## Divergences from design.md noted explicitly in the doc

- **`sprk_capabilitymode`** is the actual physical field name read by the BFF's `$select` for
  the profile's mode concept — the task's own framing text said "capabilitymode" (matching the
  code), which the doc uses verbatim; the C# entity property is named `SyncMode`/`MappingDirection`
  internally but the doc describes the Dataverse field name, not the internal property name, to
  stay code-grounded at the schema layer.
- **ReportCardService constructor** — design.md's Wave-B wiring assumption ("dependencies already
  injected everywhere") did not hold for `ReportCardService` (no BFF deps pre-task) or `TodoService`
  (optional-only) or `Matter`/`Project` (no pre-create association param at all). The doc's
  Constraints section states this asymmetry explicitly rather than presenting a uniform wiring
  story that doesn't match what shipped.
- **To Do follow-on gap** — `createTodoRegardingChild` (invoked from Invoice/Report Card wizards'
  "Add a To Do" follow-on) is NOT wired; documented as a known scope gap, not silently omitted.
- **Lookup Copy / BFF-adapter limitation** — documented as a known limitation (task 012's finding):
  the annotation-based lookup resolution only works through the Xrm.WebApi-backed `IDataService`
  adapter, not the BFF-record adapter. Confirmed still true by reading the shipped engine code.

## §17 pointer row (for main session to add to root CLAUDE.md)

```
| **Field Mapping Framework (architecture)** | [`docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md`](docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md) — two Dataverse tables, additive BFF contract, context-agnostic client engine (`FieldMappingService.ts`), four mapping types (Copy/Default/Concat/Template), `sprk_expression` extensibility seam, creation-time-vs-update-time boundary, same-entity/recursion note. Wired into all 7 `Create*Wizard` services. |
```

## Boundaries respected

- Did NOT edit `TASK-INDEX.md`, `current-task.md`, or root `CLAUDE.md` (main-session-only).
- Did NOT touch `docs/guides/FIELD-MAPPING-ADMIN-GUIDE.md` (task 041's exclusive file, parallel wave).
- Set `<status>completed</status>` in `tasks/040-architecture-doc.poml` (permitted).

**Unblocks**: 090 (wrap-up), pending 041 (admin guide, parallel) + main-session CLAUDE.md §17 insertion.
