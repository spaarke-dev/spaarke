# Operator RBAC Bootstrap Pattern

> **Last Reviewed**: 2026-08-25
> **Reviewed By**: customer-provisioning-orchestration-r1 task 203a per punch list row A07
> **Status**: Content filled (task 203a). Was skeleton from task 202.

## When

Load this pattern when:
- Bootstrapping a fresh Azure subscription + fresh RBAC-enabled Key Vault; operator has subscription Owner but data-plane operations return `ForbiddenByRbac`.
- Debugging `az role assignment create` returning `MissingSubscription` (F15b — CLI routing bug specific to KV data-plane).
- Adding a new RBAC-enabled resource to the platform stack and needing operator + UAMI grants.
- Reviewing a PR that touches `Grant-ControlPlaneIdentity.ps1` or the F15/F18-related bootstrap scripts.

## Read These Files (canonical source)

1. `projects/customer-provisioning-orchestration-r1/notes/lessons-learned-model1-prod-standup-2026-08-22.md` § F15 / F15b / F18 — original discovery + `az rest` fallback recipe.
2. `docs/guides/PROVISIONING-PREREQUISITES.md` PRQ-S-05 (operator's own AAD identity — never a service principal, per NFR-11) + PRQ-E-10 (L2 UAMI Key Vault Secrets User).
3. `scripts/provisioning/Grant-ControlPlaneIdentity.ps1` — the Graph + Dataverse + KV bootstrap script (task 203c hardens this per punch list rows A15/A16).
4. `.claude/adr/ADR-028-spaarke-auth-architecture.md` — the 21 auth MUSTs. Operator AAD identity + UAMI outbound preference.
5. `infrastructure/bicep/modules/model1-shared-l2-rbac.bicep` — the Bicep module that grants L2 UAMI RBAC on source Azure services (task 203b landed 4 of 6 grants; the L2 UAMI Bicep grants are the durable form of what this pattern's script does interactively).

## Constraints

- Azure treats KV data-plane (secret read/write) and control-plane (vault CRUD) as SEPARATE RBAC surfaces. Subscription Owner grants control-plane only. No built-in role spans both.
- `az role assignment create` has a CLI routing bug for KV data-plane on operator's OWN OID (returns `MissingSubscription`). Use `az rest --method put` on the Authorization endpoint instead. UAMI grants via `az role assignment create` work correctly (only operator's own identity hits the bug).
- Every RBAC-enabled KV (per-tenant + shared) carries the same gap. Bootstrap MUST enumerate ALL KVs in the target RG + grant Secrets Officer to operator on each.
- Operator's own AAD identity per NFR-11 — the bootstrap script must reject invocation under a service principal (verify `az account show --query user.type -o tsv` == `user`).
- IDEMPOTENT: bootstrap script MUST check existing assignment before creating (avoids PUT-409 + spurious audit noise).

## Key Rules (walk this for every fresh-sub / fresh-KV bootstrap)

1. **Verify operator identity**: `az account show --query user.type -o tsv` — MUST be `user`, not `servicePrincipal`. Bootstrap-as-SP is a hard NFR-11 violation.
2. **Detect operator OID**: `az ad signed-in-user show --query id -o tsv` → the operator's Entra OID (needed as the `principalId` in the role assignment).
3. **Enumerate KVs in target RG**: `az keyvault list -g {rg} --query "[].{name:name, id:id}" -o json` — capture the array. Each entry needs a separate grant.
4. **Grant KV Secrets Officer** to operator on each KV via `az rest --method put`:
   ```
   az rest --method put \
     --url "https://management.azure.com/subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.KeyVault/vaults/{kv}/providers/Microsoft.Authorization/roleAssignments/{guid}?api-version=2022-04-01" \
     --body '{"properties": {"roleDefinitionId": "/subscriptions/{sub}/providers/Microsoft.Authorization/roleDefinitions/b86a8fe4-44ce-4948-aee5-eccb2c155cd7", "principalId": "{operator-oid}", "principalType": "User"}}'
   ```
   (`b86a8fe4-44ce-4948-aee5-eccb2c155cd7` is the built-in role ID for `Key Vault Secrets Officer`.)
5. **Poll until data-plane works**: `az keyvault secret list --vault-name {kv} --query "[0]"` — retry every 5s up to 60s max, exit on non-403.
6. **Grant L2 UAMI Key Vault Secrets User** on each KV via `az role assignment create` (works for UAMI, only operator's own OID hits F15b):
   ```
   az role assignment create --assignee {uami-principal-id} --scope {kv-id} --role "Key Vault Secrets User"
   ```
7. **Idempotency check first**: for every grant, `az role assignment list --assignee {oid} --scope {kv-id} --query "[?roleDefinitionName=='{role}']"` — if present, skip. Prevents PUT-409 noise.

## Anti-patterns this catches

- ❌ Bootstrapping under a service principal identity → violates NFR-11 + operator-audit trail lost; RBAC grants land against SP OID that isn't the human operator's, so audit is misattributed.
- ❌ Assuming subscription Owner is enough for KV secret ops → data-plane is separate; F18 discovery cost hours of debugging.
- ❌ Using `az role assignment create` for operator's own KV Secrets Officer grant → hits F15b (`MissingSubscription`) with confusing error message.
- ❌ Skipping the idempotency check + creating the assignment blindly → PUT-409 clutters audit + slows re-runs.
- ❌ Enumerating only one KV in the RG and assuming that's all → shared-tier stamps have per-tenant + shared KVs; per-customer stamps may have multiple KVs for shared vs runtime secrets.

## Recovery recipes

- **`az role assignment create` returns `MissingSubscription`**: switch to `az rest --method put` on the Authorization endpoint for that specific KV. Diagnostic: this bug applies to operator's own OID, not UAMIs.
- **`az keyvault secret list` returns 403 after PUT**: RBAC propagation delay; wait 30-60s and retry. If still 403 after 5 min, verify role assignment landed via `az role assignment list --assignee {oid} --scope {kv-id}`.
- **Second bootstrap run hits PUT-409**: the assignment already exists; idempotency check should have skipped it. Grep for the idempotency-check code path; add if missing.
- **Fresh KV never resolves data-plane after all grants**: verify KV is RBAC-enabled (`az keyvault show --name {kv} --query "properties.enableRbacAuthorization"`); if `false`, it's using legacy access-policy model and needs different treatment.

## Worked example — SESSION 2 recovery narrative

SESSION 2 (2026-08-22) discovered F15 + F18 while attempting to seed `sprk-prod-kv` for the Model 1 Prod BFF. Verbatim recovery:

1. **Attempt 1** — `az role assignment create --assignee {ralph-oid} --scope {kv-id} --role "Key Vault Secrets Officer"` returned:
   ```
   ERROR: (MissingSubscription) The request did not have a subscription or a valid tenant level resource provider.
   ```
   Cause: CLI routing bug for KV data-plane on operator's own OID (F15b).

2. **Fallback** — construct the role assignment via `az rest`:
   ```powershell
   $sub = "cd95fcec-6b89-49ea-8339-c2b579b12587"
   $rg = "rg-spaarke-shared-prod"
   $kv = "sprk-prod-kv"
   $oid = az ad signed-in-user show --query id -o tsv
   $roleDefId = "b86a8fe4-44ce-4948-aee5-eccb2c155cd7"  # Key Vault Secrets Officer
   $assignmentGuid = [guid]::NewGuid().ToString()
   $scope = "/subscriptions/$sub/resourceGroups/$rg/providers/Microsoft.KeyVault/vaults/$kv"

   az rest --method put `
     --url "https://management.azure.com$scope/providers/Microsoft.Authorization/roleAssignments/${assignmentGuid}?api-version=2022-04-01" `
     --body (@{
       properties = @{
         roleDefinitionId = "/subscriptions/$sub/providers/Microsoft.Authorization/roleDefinitions/$roleDefId"
         principalId      = $oid
         principalType    = "User"
       }
     } | ConvertTo-Json -Depth 5 -Compress)
   ```

3. **Poll** — `az keyvault secret list --vault-name sprk-prod-kv --query "[0]"` — returned 403 for ~15 seconds then unblocked.

4. **F18 discovery** — while at the terminal, discovered the same gap on the SHARED KV (initially thought only per-tenant KVs had it). Same fix, different scope.

5. **L2 UAMI grant** — did NOT hit F15b (only operator's own OID is affected). Standard command:
   ```
   az role assignment create --assignee $l2UamiPrincipalId --scope $kvId --role "Key Vault Secrets User"
   ```

6. **Idempotency retrofit** — the actual script that landed (`Grant-ControlPlaneIdentity.ps1`) added an existence check before the PUT to avoid PUT-409 on re-runs:
   ```powershell
   $existing = az role assignment list --assignee $oid --scope $scope --query "[?roleDefinitionName=='Key Vault Secrets Officer']" -o json | ConvertFrom-Json
   if ($existing.Count -eq 0) { <PUT call here> } else { Write-Host "Assignment already exists; skipping." }
   ```

## Cross-refs

- Related pattern: [keyvault-reference-identity-invariant.md](keyvault-reference-identity-invariant.md) (T1 — after RBAC is bootstrapped, App Service needs `keyVaultReferenceIdentity` correctly bound)
- Related pattern: [manifest-driven-secret-catalog.md](manifest-driven-secret-catalog.md) (once data-plane works, secrets flow through the manifest)
- Related ADR: ADR-028 (Spaarke auth v2) — NFR-11 operator identity + UAMI outbound preference
- Related script: `scripts/provisioning/Grant-ControlPlaneIdentity.ps1` (task 203c authors + hardens)
