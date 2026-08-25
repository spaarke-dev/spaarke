# Key Vault Reference Identity Invariant Pattern

> **Last Reviewed**: 2026-08-25
> **Reviewed By**: customer-provisioning-orchestration-r1 task 203a per punch list row A07
> **Status**: Content filled (task 203a). Was skeleton from task 202.

## When

Load this pattern when:
- App Service has `@Microsoft.KeyVault(SecretUri=...)` refs silently unresolvable at runtime — settings resolve to `null` even though secrets exist and RBAC is granted (T1 trap per design.md §4B).
- Authoring or modifying `app-service.bicep` to add UAMI + KV RBAC.
- Debugging BFF SIGABRT that references an IOptions module even though the setting IS configured in App Service and the KV secret IS present.
- Reviewing a PR that changes the App Service `identity.type` or adds `keyVaultReferenceIdentity`.
- Diagnosing a slot-parity issue (staging works, prod doesn't, or vice-versa).

## Read These Files (canonical source)

1. `projects/customer-provisioning-orchestration-r1/notes/lessons-learned-model1-prod-standup-2026-08-22.md` § F16 / F16b / F16.5 — three sub-facets of the T1 trap.
2. `projects/customer-provisioning-orchestration-r1/design.md` § 4B trap catalog T1 — the design-level statement of the invariant.
3. `docs/guides/PROVISIONING-PREREQUISITES.md` PRQ-E-05 (Website Contributor on target BFF App Service), PRQ-E-10 (KV Secrets User on shared KV).
4. `infrastructure/bicep/modules/app-service.bicep` — post-Phase C UAMI refactor. Must consume `uami.outputs.principalId` for role assignments and `uami.outputs.resourceId` for `keyVaultReferenceIdentity`.
5. `infrastructure/bicep/modules/app-service-slot.bicep` — slot-level parity; same UAMI + same `keyVaultReferenceIdentity` for both prod and staging slots.

## Constraints

- App Service with `identity.type='UserAssigned'` MUST set `keyVaultReferenceIdentity` to the UAMI's resource ID (NEVER the string literal `'SystemAssigned'`).
- Emitting `keyVaultReferenceIdentity='SystemAssigned'` with only UAMI attached → binding points to a non-existent identity → SILENT null resolution at runtime. Setting value is `null`, no error is raised, BFF SIGABRTs on the downstream ValidateOnStart.
- Every UAMI attached to an App Service that references KV secrets needs `Key Vault Secrets User` (data-plane) role on that KV.
- CLI bug F16.5: `az webapp update --set keyVaultReferenceIdentity=<uami>` returns `Bad Request`. Use `az rest --method patch` on the site resource instead.
- Slot parity: prod + staging must have SAME UAMI + SAME `keyVaultReferenceIdentity`. Asymmetric config → slot swap flips into a broken state.

## Key Rules (walk this for every App Service + KV wiring)

1. **Bicep contract**: assert `identity.type='UserAssigned'` → `keyVaultReferenceIdentity = <uamiResourceId>` (not `'SystemAssigned'`, not omitted). Add a Bicep guard or ArchTest that catches drift.
2. **Bicep emits role assignment**: attached UAMI → `Key Vault Secrets User` on EVERY referenced KV (per-tenant + shared). Missing role → 403 at KV data-plane → null resolution.
3. **T1 runtime verification** (H4 or a diagnostic script): read `az webapp show --query keyVaultReferenceIdentity` + `az webapp show --query identity.userAssignedIdentities`. Cross-reference: the `keyVaultReferenceIdentity` string must equal a key in `identity.userAssignedIdentities`. Mismatch → F16b symptom.
4. **Slot parity check**: `az webapp show -g {rg} -n {app}` vs `az webapp show -g {rg} -n {app} --slot staging` — same `identity.userAssignedIdentities` map, same `keyVaultReferenceIdentity`. Phase C UAMI migration structural fix (single UAMI spans both slots) supersedes the interim per-slot approach.
5. **Recovery script** uses `az rest --method patch` if `az webapp update --set` returns Bad Request:
   ```
   az rest --method patch \
     --url "https://management.azure.com/subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.Web/sites/{app}?api-version=2022-03-01" \
     --body '{"properties": {"keyVaultReferenceIdentity": "{uami-resource-id}"}}'
   ```

## Anti-patterns this catches

- ❌ App Service with `identity.type='UserAssigned'` but `keyVaultReferenceIdentity` omitted or set to `'SystemAssigned'` → silent null resolution.
- ❌ Granting UAMI `Key Vault Secrets Officer` on the KV as a "just in case" over-privilege → violates least-privilege; `Key Vault Secrets User` is the correct minimum.
- ❌ Configuring prod slot with UAMI-A + staging slot with UAMI-B (or one without UAMI at all) → slot swap breaks BFF resolution.
- ❌ Using `az webapp update --set keyVaultReferenceIdentity=...` in a runbook or script → F16.5 CLI bug returns Bad Request; must use `az rest --method patch`.
- ❌ Adding a new KV reference to BFF app-settings without adding UAMI Key Vault Secrets User grant on the new KV → 403 at data-plane → null resolution → SIGABRT on downstream ValidateOnStart.

## Recovery recipes

- **BFF SIGABRT with correct app-settings + correct KV secrets present**: check `keyVaultReferenceIdentity` first. `az webapp show --name {app} --query "keyVaultReferenceIdentity"` — must be the UAMI resource ID, not `SystemAssigned`.
- **`az webapp update --set keyVaultReferenceIdentity=...` returns Bad Request**: F16.5 CLI bug. Use `az rest --method patch` on the site resource.
- **Slot swap flips BFF into broken state**: slot parity broken. Check both slots' `identity.userAssignedIdentities` + `keyVaultReferenceIdentity`; realign to same UAMI + same reference identity.
- **KV secret exists + RBAC granted + `keyVaultReferenceIdentity` correct + still null**: check the SecretUri format (must be `https://{kv}.vault.azure.net/secrets/{name}` — no version suffix required, but version-pinned URIs work too). Also check KV firewall / private endpoint isn't blocking App Service outbound.

## Worked example — Bicep contract + F16.5 recovery

Correct Bicep pattern (`app-service.bicep` post-Phase-C):

```bicep
resource uami 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: uamiName
  scope: resourceGroup(uamiRgName)
}

resource kv 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
  scope: resourceGroup(keyVaultRgName)
}

resource app 'Microsoft.Web/sites@2022-03-01' = {
  name: appServiceName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${uami.id}': {}
    }
  }
  properties: {
    // CRITICAL: must be UAMI resource ID, NEVER 'SystemAssigned'
    keyVaultReferenceIdentity: uami.id
    serverFarmId: appServicePlan.id
    siteConfig: {
      appSettings: [
        // @Microsoft.KeyVault(SecretUri=...) refs use the UAMI declared above
        { name: 'Dataverse__ClientSecret', value: '@Microsoft.KeyVault(SecretUri=${kv.properties.vaultUri}secrets/Dataverse-ClientSecret/)' }
      ]
    }
  }
}

// Grant UAMI Key Vault Secrets User on the KV
resource kvRbac 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(kv.id, uami.id, 'Key Vault Secrets User')
  scope: kv
  properties: {
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')  // Key Vault Secrets User
    principalId: uami.properties.principalId
    principalType: 'ServicePrincipal'
  }
}
```

F16.5 recovery — when `az webapp update --set` returns Bad Request:

```bash
sub="cd95fcec-6b89-49ea-8339-c2b579b12587"
rg="rg-spaarke-shared-prod"
app="sprksharedprod-api"
uamiId="/subscriptions/$sub/resourceGroups/rg-spaarke-shared-uami/providers/Microsoft.ManagedIdentity/userAssignedIdentities/sprk-shared-uami"

# WORKS
az rest --method patch \
  --url "https://management.azure.com/subscriptions/$sub/resourceGroups/$rg/providers/Microsoft.Web/sites/$app?api-version=2022-03-01" \
  --body "{\"properties\":{\"keyVaultReferenceIdentity\":\"$uamiId\"}}"

# Verify
az webapp show -g "$rg" -n "$app" --query "keyVaultReferenceIdentity" -o tsv
# Should print the UAMI resource ID, not 'SystemAssigned'

# Restart to pick up the change
az webapp restart -g "$rg" -n "$app"

# Poll /healthz
for delay in 30 60 90 120 180; do
  sleep "$delay"
  status=$(curl -s -o /dev/null -w '%{http_code}' "https://$app.azurewebsites.net/healthz")
  [ "$status" = "200" ] && { echo "GREEN"; break; }
done
```

## Cross-refs

- Related pattern: [operator-rbac-bootstrap.md](operator-rbac-bootstrap.md) (bootstrap the RBAC first, then this pattern ensures the App Service can actually use it)
- Related pattern: [manifest-driven-secret-catalog.md](manifest-driven-secret-catalog.md) (`@Microsoft.KeyVault(SecretUri=...)` references come from the manifest)
- Related pattern: [progressive-fail-fast-recovery.md](progressive-fail-fast-recovery.md) (silent null resolution → BFF SIGABRT cascade)
- Related design doc: `projects/customer-provisioning-orchestration-r1/design.md` § 4B T1 (canonical trap description)
