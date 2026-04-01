# OAuth Scopes Pattern

## When
Configuring OAuth scopes for BFF API, Graph API, or Dataverse access.

## Read These Files
1. `src/client/shared/Spaarke.Auth/src/config.ts` — Code Page MSAL scope configuration
2. `src/client/pcf/UniversalQuickCreate/control/services/auth/msalConfig.ts` — PCF scope config
3. `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` — Server-side OBO scope usage

## Constraints
- **ADR-004**: Scopes must follow least-privilege principle
- **ADR-008**: Resource-specific consent — request only needed scopes per endpoint

## Key Rules
- BFF API scope: `api://{clientId}/access_as_user` (single scope for all BFF endpoints)
- Graph OBO: `https://graph.microsoft.com/.default` (server requests all configured permissions)
- Dataverse OBO: `https://{org}.crm.dynamics.com/.default`
- Client requests BFF scope; server exchanges for Graph/Dataverse scopes via OBO
