# Operator RBAC Bootstrap Pattern

> **Last Reviewed**: 2026-08-24
> **Reviewed By**: customer-provisioning-orchestration-r1 task 202 (SKELETON — task 203 fills)
> **Status**: Skeleton

## When
Fresh Azure subscription + fresh RBAC-enabled Key Vault: operator has subscription Owner but data-plane operations return `ForbiddenByRbac` (F15 / F18). Or: `az role assignment create` returns `MissingSubscription` (F15b — CLI routing bug for KV data-plane).

## Read These Files (task 203 fills)
1. `projects/customer-provisioning-orchestration-r1/notes/lessons-learned-model1-prod-standup-2026-08-22.md` § F15 / F15b / F18 — original discovery + `az rest` fallback.
2. `docs/guides/PROVISIONING-PREREQUISITES.md` PRQ-S-05 (operator RBAC) + PRQ-E-10 (L2 UAMI KV Secrets User).
3. `scripts/provisioning/Grant-ControlPlaneIdentity.ps1` (task 203 authors) — the Graph+Dataverse+KV bootstrap script.

## Constraints
- Azure treats KV data-plane and control-plane as SEPARATE RBAC surfaces. Subscription Owner grants control-plane only. No built-in role spans both.
- `az role assignment create` has a CLI routing bug for KV data-plane (returns `MissingSubscription`). Use `az rest --method put` on Authorization endpoint instead.
- Every RBAC-enabled KV (per-tenant + shared) carries the same gap. Handler MUST enumerate ALL KVs in target RG + grant Secrets Officer to operator on each.
- Operator's own AAD identity (per NFR-11) — never a service principal.

## Key Rules (task 203 fills detail)
1. Detect operator OID via `az account show --query user.type -o tsv` (must be `user`, not `servicePrincipal`).
2. Grant KV Secrets Officer scoped to specific KV via `az rest --method put --url "https://management.azure.com/subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.KeyVault/vaults/{kv}/providers/Microsoft.Authorization/roleAssignments/{guid}?api-version=2022-04-01" --body …`.
3. Poll until `az keyvault secret list --vault-name {kv} --query "[0]"` returns non-403 (60s max, 5s intervals).
4. IDEMPOTENT: check existing assignment first (`az role assignment list --assignee {oid} --scope {kvId} --query "[?roleDefinitionName=='Key Vault Secrets Officer']"`). If present, skip.
5. Same shape for L2 UAMI (PRQ-E-10): `az role assignment create --assignee {uamiPrincipalId} --scope {kvId} --role "Key Vault Secrets User"` works for UAMI (only operator's OWN OID hits the CLI bug F15b).
