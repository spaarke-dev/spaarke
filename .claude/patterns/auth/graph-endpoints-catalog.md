# BFF Graph Endpoints Catalog

## When
Adding new Graph API features — check this catalog first to avoid duplicating existing functionality.

## Read These Files
1. `src/server/api/Sprk.Bff.Api/Api/OBOEndpoints.cs` — All user-facing OBO endpoints (SPE containers, files, permissions)
2. `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` — ForUserAsync (OBO) and ForApp (app-only) modes

## Constraints
- **ADR-007**: Graph SDK types must not leak above SpeFileStore facade
- MUST check existing endpoints before adding new Graph operations

## Key Rules
- User-facing operations: via `ForUserAsync(ctx, ct)` — OBO flow
- App-only operations: via `ForApp()` — background jobs, system operations
- All Graph operations are in SpeFileStore or dedicated service classes — not in endpoints directly
- SPE operations: containers, drive items, permissions, sharing, thumbnails
