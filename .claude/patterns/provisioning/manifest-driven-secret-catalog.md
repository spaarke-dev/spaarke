# Manifest-Driven Secret Catalog Pattern

> **Last Reviewed**: 2026-08-24
> **Reviewed By**: customer-provisioning-orchestration-r1 task 202 (SKELETON — task 203 fills)
> **Status**: Skeleton

## When
Adding a new provisioning handler that seeds KV secrets, or adding a new KV secret consumed by BFF.

## Read These Files (task 203 fills)
1. `scripts/canonical-secret-catalog/manifest.yaml` — single source of truth (task 084 / FR-36).
2. `scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1` — the generator (produces Bicep + PS + docs from manifest).
3. `src/server/api/Sprk.Provisioning.ControlPlane.Core/Handlers/SharedKvSecrets/H4SharedKvSecretsPopulationHandler.cs` — reference impl consuming the manifest.
4. `src/server/api/Sprk.Provisioning.ControlPlane.Core/Handlers/BulkAppSettings/H4bBulkAppSettingsHandler.cs` — reference impl for `per_env_settings` list.

## Constraints
- **BINDING**: never delete `Dataverse-ClientSecret` / `BFF-API-ClientSecret` (root CLAUDE.md §10 + spec.md MUST).
- New secret → 1 manifest entry + 0 handler changes (per-tenant H4 / shared H4-shared / bulk-app-settings H4b resolve automatically via source field).
- Every entry MUST have `source: {kv-ref | per-env-input | literal | from-bicep-output | from-shared-service}` field.
- BINDING pre-check gate (§7.9): before any secret rename/delete, verify LIVE App Service + KV + Dataverse-persisted config.

## Key Rules (task 203 fills detail)
1. Extend manifest; run `Invoke-CatalogGenerator.ps1 -Verify` to prove determinism (byte-identical regen).
2. `never_delete: true` entries stay in every deploy path — no exceptions.
3. Handler filter: per-tenant H4 filters `FromSharedService` entries; H4-shared filters non-shared; H4b filters `per_env_settings`. Cross-cutting entries (e.g., `AzureAd__TenantId`) may appear in both `secrets` + `per_env_settings` — last-write-wins is deterministic.
