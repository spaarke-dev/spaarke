# Polymorphic Resolver Pattern

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

## When
Associating a child record (event, document, work assignment, communication) with multiple parent entity types.

## Read These Files
1. `src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts` — Client-side resolver (applyResolverFields, resolveRecordType, findNavProp)
2. `src/server/api/Sprk.Bff.Api/Services/Communication/IncomingAssociationResolver.cs` — Server-side resolver for communications
3. `src/server/shared/Spaarke.Dataverse/IDataverseService.cs` — QueryRecordTypeRefAsync for server-side lookups

## Constraints
- **ADR-024**: Dual-field strategy — entity-specific lookup + 4 resolver fields
- MUST populate ALL 4 resolver fields when setting any entity-specific lookup
- MUST populate only ONE entity-specific lookup at a time (mutually exclusive)
- MUST use shared `PolymorphicResolverService` — no duplicating resolver logic in services

## Key Rules
- Entities using pattern: `sprk_event`, `sprk_document`, `sprk_workassignment`, `sprk_communication`, `sprk_memo`
- 4 resolver fields: `sprk_regardingrecordtype` (lookup), `sprk_regardingrecordid`, `sprk_regardingrecordname`, `sprk_regardingrecordurl`
- Client: call `applyResolverFields()` — sets entity-specific lookup + all 4 resolver fields in one call
- Server: priority order for "regarding": matter > project > invoice > workassignment > budget > analysis > organization > person
- New parent types: add seed data to `sprk_recordtype_ref` — no schema changes needed
