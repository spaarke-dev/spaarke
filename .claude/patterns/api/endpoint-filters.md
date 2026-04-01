# Endpoint Filters Pattern

## When
Adding resource-level authorization to API endpoints.

## Read These Files
1. `src/server/api/Sprk.Bff.Api/Api/Filters/DocumentAuthorizationFilter.cs` — IEndpointFilter implementation (exemplar)
2. `src/server/api/Sprk.Bff.Api/Infrastructure/Authorization/ResourceAccessHandler.cs` — Policy-based authorization handler
3. `src/server/api/Sprk.Bff.Api/Program.cs` — Policy definitions (AddAuthorization section)

## Constraints
- **ADR-008**: No global auth middleware — use per-endpoint filters or policies
- **ADR-003**: Authorization must check resource ownership

## Key Rules
- Use endpoint filter when resource ID is in route and custom extraction needed
- Use policy + handler for standard claims-based auth reusable across endpoints
- Deny code format: `sdap.access.deny.{reason}` (e.g., `team_mismatch`, `role_insufficient`)
- Extract resource ID from route values: `documentId`, `containerId`, `driveId`, `itemId`
