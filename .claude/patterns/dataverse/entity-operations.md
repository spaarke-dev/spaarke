# Entity Operations Pattern

## When
Performing CRUD operations on Dataverse entities (create, retrieve, update, query).

## Read These Files
1. `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` — ServiceClient CRUD implementation
2. `src/server/shared/Spaarke.Dataverse/Models.cs` — DTOs, request models, entity mappings

## Constraints
- **ADR-002**: Late-bound entities only — no early-bound code generation
- **ADR-007**: Map Entity to DTO at service boundary — don't leak SDK types

## Key Rules
- Late-bound: `new Entity("sprk_document")` with string-based `entity["sprk_field"]` access
- Sparse updates: only set changed fields — `new Entity("sprk_document", existingGuid)`
- Field types: `OptionSetValue` for choices, `EntityReference` for lookups, string/bool/int for scalars
- Use `GetAttributeValue<T>()` for safe reads — null-safe, no cast exceptions
- Map to strongly-typed DTOs at service boundary (private `MapToXxxEntity()` methods)
