# Endpoint Definition Pattern

## When
Creating or modifying Minimal API endpoints in the BFF API.

## Read These Files
1. `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` — AI streaming endpoints (exemplar)
2. `src/server/api/Sprk.Bff.Api/Api/DocumentsEndpoints.cs` — Container CRUD endpoints
3. `src/server/api/Sprk.Bff.Api/Api/UploadEndpoints.cs` — File upload endpoints
4. `src/server/api/Sprk.Bff.Api/Program.cs` — Endpoint registration (MapGroup, MapXxxEndpoints calls)

## Constraints
- **ADR-001**: Minimal API + BackgroundService — no Azure Functions
- **ADR-008**: Use endpoint filters for auth — no global middleware

## Key Rules
- One static class per feature area with `Map{Feature}Endpoints` extension method returning `IEndpointRouteBuilder`
- Use `MapGroup` for shared config (RequireAuthorization, WithTags)
- Handler methods are `private static async Task<IResult>` — inject services via parameters
- Return `TypedResults.Ok/Created/NoContent/NotFound` or `ProblemDetailsHelper` for errors
