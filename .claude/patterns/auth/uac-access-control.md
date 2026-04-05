# UAC Access Control Pattern

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

## When
Implementing authorization checks in endpoint filters or adding new operations to access policies.

## Read These Files
1. `src/server/api/Sprk.Bff.Api/Infrastructure/Authorization/ResourceAccessHandler.cs` — Policy-based authorization handler
2. `src/server/api/Sprk.Bff.Api/Api/Filters/DocumentAuthorizationFilter.cs` — Resource-level filter implementation
3. `src/server/shared/Spaarke.Dataverse/IAccessDataSource.cs` — Access data interface for user permission queries

## Constraints
- **ADR-008**: Use endpoint filters for resource auth — no global middleware
- **ADR-003**: Authorization must check resource ownership

## Key Rules
- Deny code format: `sdap.access.deny.{reason}` (e.g., `team_mismatch`, `role_insufficient`)
- Access data loaded once per request via RequestCache — not re-queried per check
- Check proceeds: authenticate → extract resource ID → query access → allow/deny
- Full architecture reference: `docs/architecture/uac-access-control.md`
