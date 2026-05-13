# Auth Patterns Index

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

> Pointer-based patterns for OAuth, OBO, MSAL, and access control.

## Authentication
| Pattern | When to Load | Last Reviewed | Status |
|---------|-------------|---------------|--------|
| [spaarke-sso-binding.md](spaarke-sso-binding.md) | **CANONICAL** — binding requirements, 6-strategy chain, bundling reality | 2026-05-13 | Verified |
| [oauth-scopes.md](oauth-scopes.md) | Configuring OAuth scopes | 2026-04-05 | Verified |
| [obo-flow.md](obo-flow.md) | Graph API OBO token exchange | 2026-04-05 | Verified |
| [dataverse-obo.md](dataverse-obo.md) | Dataverse OBO token exchange | 2026-04-05 | Verified |
| [service-principal.md](service-principal.md) | App-only auth (no user context) | 2026-04-05 | Current |
| [token-caching.md](token-caching.md) | Token caching (Redis server + localStorage client) | 2026-05-13 | Verified |
| [msal-client.md](msal-client.md) | Legacy PCF MSAL singleton (superseded by `@spaarke/auth`) | 2026-04-05 | Legacy |
| [spaarke-auth-initialization.md](spaarke-auth-initialization.md) | Code Page auth bootstrap | 2026-04-05 | Current |
| [xrm-webapi-vs-bff-auth.md](xrm-webapi-vs-bff-auth.md) | Decision: Xrm.WebApi vs BFF auth | 2026-04-05 | Verified |
| [bff-url-normalization.md](bff-url-normalization.md) | **CRITICAL**: BFF URL construction via `buildBffApiUrl()` | 2026-04-05 | Verified |

## Graph API
| Pattern | When to Load | Last Reviewed | Status |
|---------|-------------|---------------|--------|
| [graph-sdk-v5.md](graph-sdk-v5.md) | Graph client setup and auth modes | 2026-04-05 | Verified |
| [graph-endpoints-catalog.md](graph-endpoints-catalog.md) | Existing Graph operations inventory | 2026-04-05 | Verified |
| [graph-webhooks.md](graph-webhooks.md) | Change notification subscriptions | 2026-04-05 | Verified |

## Authorization
| Pattern | When to Load | Last Reviewed | Status |
|---------|-------------|---------------|--------|
| [uac-access-control.md](uac-access-control.md) | Endpoint filters and access policies | 2026-04-05 | Verified |
