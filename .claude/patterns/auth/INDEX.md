# Auth Patterns Index

> Pointer-based patterns for OAuth, OBO, MSAL, and access control.

## Authentication
| Pattern | When to Load |
|---------|-------------|
| [oauth-scopes.md](oauth-scopes.md) | Configuring OAuth scopes |
| [obo-flow.md](obo-flow.md) | Graph API OBO token exchange |
| [dataverse-obo.md](dataverse-obo.md) | Dataverse OBO token exchange |
| [service-principal.md](service-principal.md) | App-only auth (no user context) |
| [token-caching.md](token-caching.md) | Token caching (Redis/session) |
| [msal-client.md](msal-client.md) | Legacy PCF MSAL singleton |
| [spaarke-auth-initialization.md](spaarke-auth-initialization.md) | Code Page auth bootstrap |
| [xrm-webapi-vs-bff-auth.md](xrm-webapi-vs-bff-auth.md) | Decision: Xrm.WebApi vs BFF auth |
| [bff-url-normalization.md](bff-url-normalization.md) | **CRITICAL**: BFF URL must be HOST ONLY (no /api). Load when constructing ANY BFF fetch URL. |

## Graph API
| Pattern | When to Load |
|---------|-------------|
| [graph-sdk-v5.md](graph-sdk-v5.md) | Graph client setup and auth modes |
| [graph-endpoints-catalog.md](graph-endpoints-catalog.md) | Existing Graph operations inventory |
| [graph-webhooks.md](graph-webhooks.md) | Change notification subscriptions |

## Authorization
| [uac-access-control.md](uac-access-control.md) | Endpoint filters and access policies |
