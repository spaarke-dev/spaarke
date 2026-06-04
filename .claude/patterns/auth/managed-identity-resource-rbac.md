# Pattern — Managed Identity ↔ Azure Resource RBAC

> **Last Reviewed**: 2026-05-28
> **Status**: Verified (post auth-chain bring-up incident)
> **Loads**: when wiring the BFF MI to a new Azure resource type, OR when diagnosing MI 401/403 to an Azure dependency, OR when provisioning a new env.

## Canonical sources

- [ADR-028 §"Documented MI exceptions" E-1, E-2](../../adr/ADR-028-spaarke-auth-architecture.md)
- [docs/guides/auth-deployment-setup.md §3.5 "Azure dependency RBAC + KV reference identity"](../../../docs/guides/auth-deployment-setup.md)
- [`Infrastructure/Auth/ManagedIdentityCredentialFactory.cs`](../../../src/server/api/Sprk.Bff.Api/Infrastructure/Auth/ManagedIdentityCredentialFactory.cs) — UAMI-pinned credential
- [`Infrastructure/DI/AiModule.cs`](../../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiModule.cs) — AzureOpenAI path with MI / ApiKey switch

## What this pattern covers

The four requirements that must ALL be in place for BFF MI auth to work against any Azure dependency. The 2026-05-28 incident hit each one in turn over ~3 hours; this pattern exists so the next env-provisioning hits zero of them.

| # | Requirement | Failure mode if missed | Where to read |
|---|---|---|---|
| 1 | App Service `keyVaultReferenceIdentity` set to UAMI resource ID (NOT default `SystemAssigned`) | KV refs silently fail; literal `@Microsoft.KeyVault(...)` string passed to BFF; "invalid subscription key" downstream | auth-deployment-setup.md §3.5 (a) |
| 2 | UAMI has `Key Vault Secrets User` on every KV referenced | KV ref returns 403; falls into (1) failure mode | auth-deployment-setup.md §3.5 (b) |
| 3 | UAMI has `Cognitive Services User` (wildcard) on every AI Services / OpenAI account — OR `AzureOpenAI__ApiKey` set per ADR-028 E-2 | OpenAI 401 PermissionDenied. The narrower OpenAI User role observed insufficient for `kind=AIServices`. | auth-deployment-setup.md §3.5 (c) |
| 4 | UAMI has `Cosmos DB Built-in Data Contributor` on every Cosmos account | Cosmos 403 (Forbidden Substatus 5301) on session persistence | auth-deployment-setup.md §3.5 "Cosmos DB persistence" + provisioning sequence |

## Failure-mode quick lookup

- "401 PermissionDenied" from Azure OpenAI → req #3
- "403 Forbidden, Substatus 5301" from Cosmos → req #4
- "invalid subscription key" from OpenAI BUT BFF is configured for MI → almost certainly req #1 (KV ref didn't resolve, so the api-key header is the literal `@Microsoft.KeyVault(...)` string) — check #2 next
- "Bearer token is invalid" from any KV-backed setting → req #1 or #2

## How to apply

When provisioning a new env, work through the pre-flight checklist in auth-deployment-setup.md §3.5 "Pre-flight checklist for any new env" before declaring auth-complete. Missing any one of the four produces a different failure mode at runtime.
