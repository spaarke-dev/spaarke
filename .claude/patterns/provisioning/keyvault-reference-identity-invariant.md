# Key Vault Reference Identity Invariant Pattern

> **Last Reviewed**: 2026-08-24
> **Reviewed By**: customer-provisioning-orchestration-r1 task 202 (SKELETON — task 203 fills)
> **Status**: Skeleton

## When
App Service has `@Microsoft.KeyVault(...)` refs silently unresolvable at runtime (settings resolve to `null` even though secrets exist). T1 trap (design.md §4B).

## Read These Files (task 203 fills)
1. `projects/customer-provisioning-orchestration-r1/notes/lessons-learned-model1-prod-standup-2026-08-22.md` § F16 / F16b / F16.5 — three sub-facets of T1.
2. `projects/customer-provisioning-orchestration-r1/design.md` § 4B trap catalog T1.
3. `docs/guides/PROVISIONING-PREREQUISITES.md` PRQ-E-05 (Website Contributor on target BFF), PRQ-E-10 (KV Secrets User on shared KV).
4. `infrastructure/bicep/modules/app-service.bicep` — post-Phase C UAMI refactor (removed appServicePrincipalId; must consume `uami.outputs.principalId`).

## Constraints
- App Service with `identity.type='UserAssigned'` MUST set `keyVaultReferenceIdentity` to the UAMI's resource ID (NEVER string literal `'SystemAssigned'`).
- Emitting `keyVaultReferenceIdentity='SystemAssigned'` with only UAMI attached → binding points to non-existent identity → SILENT null resolution at runtime.
- Every UAMI attached to an App Service that references KV secrets needs Key Vault Secrets User (data-plane) role on that KV.
- CLI bug F16.5: `az webapp update --set keyVaultReferenceIdentity=<uami>` returns Bad Request. Use `az rest --method patch` on site resource instead.

## Key Rules (task 203 fills detail)
1. Bicep validation: assert `identity.type='UserAssigned'` → `keyVaultReferenceIdentity=<uamiResourceId>` (never 'SystemAssigned').
2. Bicep emits role assignment: attached UAMI → KV Secrets User on every referenced KV.
3. T1 handler (H4) runtime verification: read `az webapp show --query keyVaultReferenceIdentity` + `az webapp show --query identity.userAssignedIdentities`; cross-reference; warn on mismatch (F16b).
4. Slot parity: both prod + staging slots MUST have same UAMI + same kvRefIdentity. Phase C UAMI migration structural fix (single UAMI spans both slots) supersedes interim H4 RBAC-both-slots.
5. Recovery script uses `az rest --method patch` if `az webapp update --set` returns Bad Request.
