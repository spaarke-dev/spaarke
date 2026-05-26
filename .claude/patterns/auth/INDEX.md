# Auth Patterns Index

> **Last Reviewed**: 2026-05-19 (post-auth-v2 drift audit)
> **Reviewed By**: doc-drift-audit (auth v2 close-out)
> **Status**: Verified
> **Canonical architecture**: [ADR-028 — Spaarke Auth Architecture](../../adr/ADR-028-spaarke-auth-architecture.md)

> Pointer-based patterns for OAuth, OBO, MSAL, and access control. Spaarke Auth v2 (ADR-028) is the canonical client-side architecture; this index lists what remains canonical and what has been retired.

## Authentication
| Pattern | When to Load | Last Reviewed | Status |
|---------|-------------|---------------|--------|
| [spaarke-sso-binding.md](spaarke-sso-binding.md) | **CANONICAL** — MSAL binding invariants (INV-1..INV-8) + v2 token acquisition model | 2026-05-19 | Verified (v2) |
| [oauth-scopes.md](oauth-scopes.md) | Configuring OAuth scopes | 2026-04-05 | Verified |
| [obo-flow.md](obo-flow.md) | Graph API OBO token exchange | 2026-04-05 | Verified |
| [dataverse-obo.md](dataverse-obo.md) | Dataverse OBO token exchange | 2026-04-05 | Verified |
| [service-principal.md](service-principal.md) | App-only auth. Note: v2 migrated most app-only flows to `DefaultAzureCredential` (managed identity) — see ADR-028. | 2026-05-19 | Verified (v2) |
| [token-caching.md](token-caching.md) | Token caching — Redis OBO (server) canonical; client-side cascade RETIRED in v2 | 2026-05-19 | Verified (server portion only) |
| [xrm-webapi-vs-bff-auth.md](xrm-webapi-vs-bff-auth.md) | Decision: Xrm.WebApi vs BFF auth | 2026-04-05 | Verified |
| [bff-url-normalization.md](bff-url-normalization.md) | **CRITICAL**: BFF URL construction via `buildBffApiUrl()` — unchanged in v2 | 2026-05-19 | Verified (v2) |

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
