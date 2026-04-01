# Dataverse Queries Pattern

## When
Querying Dataverse data from PCF controls via WebAPI or context.

## Read These Files
1. `src/client/pcf/UniversalQuickCreate/control/services/MetadataService.ts` — Metadata queries via Xrm.WebApi
2. `src/client/pcf/SemanticSearchControl/SemanticSearchControl/services/NavigationService.ts` — Entity navigation queries
3. `src/client/pcf/RelatedDocumentCount/RelatedDocumentCount/RelatedDocumentCount.tsx` — FetchXML aggregate query

## Constraints
- **ADR-006**: Use `context.webAPI` or `Xrm.WebApi` — never direct HTTP to Dataverse
- **ADR-009**: Cache repeated queries in component state or service-level cache

## Key Rules
- Use `context.webAPI.retrieveMultipleRecords()` with OData filter for simple queries
- Use FetchXML for aggregates, complex joins, or subqueries
- Lookup fields: read via `_fieldname_value` (logical name, underscore prefix)
- Lookup fields: write via `SchemaName@odata.bind` (CASE-SENSITIVE — see relationship-navigation pattern)
- Environment variables: `Xrm.Utility.getGlobalContext().getCurrentAppProperties()` for app-level config
