# Task 091 — File store provisioning: inventory, tag-permission finding, escalation

> Project: ebilling-r1 (model `procedure-and-file-model.md` §4.2; owner re-plan Q5, 2026-10-21). Session 1 (2026-10-21) was authoring only: no deploy, no what-if, no role-assignment change, no Key Vault change. **The session stopped at step 2 on the POML's second escalation trigger (§3).** No bicep was written.
>
> **Session 3 (2026-10-21): the §3 stop is lifted — owner chose option A; uac-r2 accepted it on four conditions.** `modules/ebilling-file-store.bicep` is authored with the custom role; tests, the operator verification scripts and the exact dev deploy commands are in §8. Still authoring only: no deploy, no what-if, no live role or assignment, no Key Vault change.

## 1. Owner decision this task carries

2026-10-21 (re-plan Q5): remove the stamp identity's **account-scope** Storage Blob Data Contributor assignment (`infrastructure/bicep/modules/storage-account.bicep` `uamiStorageBlobRbac`, :171-179; the legacy `storageBlobRbac` for `appServicePrincipalId`, :156-165, is also account scope but has no caller). Provisioned customer environments get **container-scoped assignments only**, in the bicep customer provisioning uses, with a container-scoped grant for every container the identity needs. Removing the grant on dev is low priority and needs the owner's OK when it runs.

This settles the POML's first escalation trigger (account-scope role already held) in advance: the owner chose option 3, remove the account-scope grant.

## 2. Container inventory — what the stamp identity reads or writes today

Method: grep of `src/**` for `BlobServiceClient`, `BlobContainerClient`, `BlobClient`, `GetBlobContainerClient`, `Azure.Storage*` package references, the container names provisioned by `customer.bicep` / `storage-account.bicep`, and `blob.core.windows.net`; plus every bicep module that assigns a Storage Blob role (`ba92f5b4-…`).

Only two projects reference `Azure.Storage.*`: `Sprk.Bff.Api` (stamp UAMI) and `Sprk.Provisioning.ControlPlane.Core` (L2 control-plane UAMI, on its own `provisioning-artifacts` account — not the stamp identity, out of scope).

| Container (stamp account `sprk{customer}{env}sa`) | Provisioned by | Who touches it | Stamp UAMI needs a grant? |
|---|---|---|---|
| `ai-chunks` (`SessionFileStore:ContainerName`, default `SessionFileBlobStore.DefaultContainerName`) | `customer.bicep` `storageContainers` default | BFF `SessionFileBlobStore` (upload, download, list, delete — all container-level operations; no account-level call such as `GetUserDelegationKey` or `GetBlobContainers`), BFF `AiKeylessProbe` (list, H13 keyless proof). Off until `SessionFileStore:BlobEndpoint` is set. | **Yes — Storage Blob Data Contributor at container scope.** |
| `temp-files` | `customer.bicep` default | Nobody. `UploadFinalizationWorker.CleanupTempFileAsync` is a stub (#231); no blob call. | No |
| `document-processing` | `customer.bicep` default | Nobody (the Service Bus queue of the same name is unrelated). | No |
| `acs-eventgrid-deadletter` (when `deployAcsMessaging`) | `customer.bicep` | Event Grid system-topic identity writes dead-letters; it already holds a **container-scope** grant (`acs-communication.bicep` :98-124 — the pattern to copy). The BFF only puts the name in `AcsProvisioningPlan`; it reads no blob. | No |
| `test-documents` | module default only; `scripts/Setup-TestDocumentStorage.ps1` (operator, `--auth-mode login`) | Operator sign-in, not the stamp identity. Not created by `customer.bicep`. | No |
| `procedures`, `invoice-files`, `billing-exports` (new, model §4.2) | this task's module (not written yet) | Task 090's file store (not on this branch yet): write-once upload, download, delete, **and read the Defender scan-result blob index tag**. | **Yes — container scope; role blocked by §3.** |

**Live check still owed before the dev removal:** confirm on `spaarke-bff-dev` (both slots) whether `SessionFileStore:ContainerName` is set to something other than `ai-chunks` (compare without printing the value) and whether `SessionFileStore:BlobEndpoint` is set at all. A container-scoped grant on the wrong container would turn the H13 Blob probe from `not-in-use`/`proved` into an auth failure.

## 3. Step 2 finding — the role cannot read the scan tag (ESCALATION, security)

`az role definition list --name "Storage Blob Data Contributor"` (read-only, 2026-10-21):

- actions: `containers/delete`, `containers/read`, `containers/write`, `generateUserDelegationKey/action`
- dataActions: `containers/blobs/delete`, `/read`, `/write`, `/move/action`, `/add/action`
- **No `Microsoft.Storage/storageAccounts/blobServices/containers/blobs/tags/read`.**

Get Blob Tags (and List Blobs with `include=tags`) needs `blobs/tags/read`. Model §4.2 makes the scan tag the only source of the scan result and says **a missing tag refuses**, so with Contributor alone every eBilling download would be refused (the read would 403, which the file store must treat as "no tag").

Built-in roles that include `blobs/tags/read` (`az role definition list`, BuiltInRole):

| Role | Also grants | Problem |
|---|---|---|
| Storage Blob Data Owner | `containers/*`, `blobs/*` | Includes `blobs/tags/write`: the BFF could set or overwrite the scan-result tag (a forged "No threats found"). |
| Defender for Storage Data Scanner | blob read, tags read **and write**, blob delete, file-share read | Meant for the scanner; tags write; no blob write, so the BFF could not upload. |
| Defender Storage Malware Data Scanner | blob read, tags read **and write**, Event Grid send | Same; no blob write. |
| Storage Actions Blob Data Operator | blob read/write/delete, tags read **and write**, `runAsSuperUser`, immutable-storage super-user, deployments | Far too broad. |

Every built-in that can read the tag can also write it. Only a custom role keeps the BFF unable to forge the scan result. The POML forbids granting Owner or a custom role without the owner's and uac-r2's agreement, so the task stops here.

### 🔔 Human Input Required — role for the three eBilling containers

**Situation:** the file store must read the Defender scan tag; Storage Blob Data Contributor cannot; every built-in that can also writes tags.

| Option | What | For | Against |
|---|---|---|---|
| A (recommended) | Custom role "Spaarke Blob Data Contributor + Tag Reader": Contributor's actions and dataActions plus `blobs/tags/read` only, assigned at each eBilling container's scope. | Least privilege; the BFF cannot forge or clear the scan tag; container scope keeps K-3's mitigation. | A role definition is a subscription-level object: customer provisioning must create it per customer subscription (L2 holds Owner there, so it can) and the module must take its id. Model 2 stamps sit in the customer's tenant. |
| B | Storage Blob Data Owner at each eBilling container's scope (it is a superset of Contributor, so one assignment). | Built-in; no custom role to provision. | BFF can write tags, so a BFF bug or compromise can mark a malicious file clean; also `containers/*` (set ACLs, delete the container). |
| C | Keep Contributor and get the scan result another way (Defender's Event Grid result event or Log Analytics). | No tag permission at all. | Contradicts model §4.2 ("read on demand; nothing is queued; no Event Grid receiver in R1"); a design change for 090. |

**Recommendation:** A, reviewed by uac-r2 (security). Whichever is chosen, the module takes `roleDefinitionId` as a parameter (POML step 3), so the module shape does not change.

Not affected by this question, and ready to author once the stop is lifted: removing the account-scope grant from `storage-account.bicep` and adding the container-scoped Contributor grant on `ai-chunks` (§2) — `ai-chunks` needs no tag read.

## 4. Account-scope removal — done in session 2 (2026-10-21), on the coordinator's instruction

The §3 stop stays: no eBilling container, no `ebilling-file-store.bicep`, no eBilling role. What was authored (no deploy, no what-if, no Key Vault):

- `storage-account.bicep`: both account-scope grants deleted (`uamiStorageBlobRbac`, and the legacy `storageBlobRbac` with its unused `appServicePrincipalId` parameter). New parameter `userAssignedIdentityContainerNames` (default empty) and a loop `uamiContainerBlobRbac` over the module's own `blobContainers`: one Storage Blob Data Contributor assignment per listed container, `scope` = that container, name `guid(container id, principal, role)`. A listed name not in `containers` gets no grant.
- `customer.bicep`: `var bffBlobContainerNames = ['ai-chunks']`, passed as `userAssignedIdentityContainerNames`, and unioned into the created containers (`baseStorageContainers`) so the granted container always exists.
- `customer.json` recompiled (Bicep 0.46.1). Diff = the variables above, the module's parameters and the role-assignment copy loop only.
- Operator texts: `appsettings.template.json` `SessionFileStore._operator_prerequisites`, the `SessionFileBlobStore` 403 message, the `ChatDocumentEndpoints` `DurableStoreFailedErrorCode` comment now say container scope.
- Guard: `CustomerStampKeylessTemplateTests` — every Storage Blob Data Contributor/Owner/Reader assignment in `customer.json` is at container scope (plus negative control), and `bffBlobContainerNames` contains `SessionFileBlobStore.DefaultContainerName`. Both fail on the previous `customer.json`.

**Existing stamps keep the old grant.** ARM incremental deployment does not delete a role assignment that left the template, so on every already-provisioned stamp (and dev) the account-scope assignment `guid(account id, UAMI principal, ba92f5b4-…)` stays until it is deleted explicitly — a live step needing the owner's OK. Delete it only after the container-scope grant on `ai-chunks` exists, or the session-file store (if enabled) and the H13 Blob probe start failing 403.

### Original plan (session 1)

- `storage-account.bicep`: delete `uamiStorageBlobRbac` and the legacy `storageBlobRbac` (both account scope); add a parameter naming the containers the stamp identity is granted on (default empty), and a loop over the module's `blobContainers` creating one Storage Blob Data Contributor assignment **at container scope** per listed container (`guid(container id, principal, role)`, `principalType: ServicePrincipal`), copying `acs-communication.bicep` :98-124.
- `customer.bicep`: pass `['ai-chunks']` for the UAMI (and union it into `effectiveStorageContainers` so the container always exists); update the comments at :309-318 that say the UAMI holds the role on the account.
- Recompile `customer.json` (`az bicep build --file customer.bicep --outfile customer.json`; `CustomerStampKeylessTemplateTests` and `ArmTemplateInspectorTests` read it). The committed `customer.json` matched a fresh 0.46.1 build on 2026-10-21 (line endings aside), so the regenerated diff will be this change only.
- `modules/ebilling-file-store.bicep` (POML step 3) with `roleDefinitionId` per the owner's answer to §3.

## 5. Stale text to fix with the account-scope removal (fixed in session 2, §4)

- `src/server/api/Sprk.Bff.Api/appsettings.template.json:497` `_operator_prerequisites` tells the operator to grant Storage Blob Data Contributor "on that storage account" and says the module grants only via `appServicePrincipalId`.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/SessionFileBlobStore.cs:558-560` exception message: "needs the 'Storage Blob Data Contributor' role on the storage account … 'appServicePrincipalId' is passed to the module".
- `src/server/api/Sprk.Bff.Api/Api/Ai/ChatDocumentEndpoints.cs:100` doc comment (same assumption).

## 6. Hand-off to customer-provisioning-orchestration-r1 (§3 decided: option A)

Filed 2026-10-21 as #2232 (DEF-034). Contents:

1. Wire `modules/ebilling-file-store.bicep` into `customer.bicep` for the stamp UAMI (`principalId: uami.outputs.principalId`, `storageAccountName: storage.outputs.storageAccountName`, `scope: <stamp RG>`). The module itself creates the custom role definition at the stamp resource group (`assignableScopes` = that RG only; name `guid(resourceGroup().id, 'spaarke-ebilling-file-store-blob-role')`; role name made unique per RG with `uniqueString(resourceGroup().id)`, because custom role names are tenant-unique). The identity that deploys `customer.bicep` therefore needs `Microsoft.Authorization/roleDefinitions/write` and `roleAssignments/write` on the stamp RG (customer-provisioning note (e): the control-plane Worker can). Then recompile `customer.json` from the merged `customer.bicep` (never hand-merge). `CustomerStampKeylessTemplateTests.StampBlobDataRoles_AreContainerScoped` already recognises a custom role with blob data actions, so the wired grants are checked for container scope automatically; the exactness test (`FileStoreRole_IsExactlyContributorPlusTagRead`) reads `modules/ebilling-file-store.json`, which must be recompiled with any module edit.
2. The per-environment malware-scanning setting: `enableMalwareScanning` has no default (on-upload scanning with `malwareScanningCapGBPerMonth`, or `false` = the explicit "Scanning not enabled" setting, which the BFF must also receive as task 090's `MalwareScanningEnabled=false`).
3. The BFF config keys task 090 defines.
4. Network `defaultAction: 'Deny'` / private endpoint at prod hardening (K-3, accepted by the owner).
5. The container-scope-only rule for the stamp UAMI (§1), the `ai-chunks` grant (built-in Contributor, container scope — it needs no tag read), and the §2 inventory: a new blob consumer in the BFF needs its own container-scoped grant, or it fails with 403.
6. Per-stamp verification: `scripts/ebilling/Test-EbillingFileStoreRole.ps1` (role exact against live built-in Contributor + tags/read; assignments per container only) and the data-plane tag-write proof `scripts/ebilling/test-ebilling-file-store-tag-write.sh` (§8.5 step 6); candidates for H13.
7. Never remove the account-scope grant on ACME or any existing stamp from this project (customer-provisioning note (d)); that stays customer-provisioning's step with its owner.

## 7. Acceptance criteria status

| Criterion | Status |
|---|---|
| Module builds clean, no secret | **Met (authored)**: `az bicep build` of `ebilling-file-store.bicep` exits 0 with no warning; no key, connection string or SAS in any output, parameter or property (`FileStoreTemplate_ContainsNoKeyMaterial`). |
| Notes record inventory, tag finding, what-if, owner OK | Tag finding, code inventory and a read-only dev identity/account inventory (§8.4) recorded; owner chose option A (2026-10-21). What-if, scanning choice and the owner's OK on the what-if: pending (§8.5 commands). |
| Containers + one container-scope assignment each on dev | Live step — commands in §8.5; the dev storage account is an open owner choice (§8.4). |
| Negative: no account/RG/subscription grant; network, soft delete, KV unchanged | Template: enforced by `FileStoreRole_IsAssignedPerContainerOnly`. Live: before/after reads in §8.5. |
| Defender matches owner's choice | Live step. |
| Hand-off issue filed and in `defer-issues.md` | Contents ready (§6); not filed. |

## 8. Option A — the file-store custom role (session 3, 2026-10-21)

Owner decision 2026-10-21: option A. unified-access-control-r2 accepted it on four binding conditions (POML notes, SCAN-TAG ROLE). This section is the authored result. **Nothing here ran against Azure except the read-only queries in §8.4.**

### 8.1 The role (as compiled in `modules/ebilling-file-store.json`; `description` omitted)

```json
{
  "type": "Microsoft.Authorization/roleDefinitions",
  "name": "[guid(resourceGroup().id, 'spaarke-ebilling-file-store-blob-role')]",
  "properties": {
    "roleName": "[format('Spaarke eBilling File Store Blob Data ({0})', uniqueString(resourceGroup().id))]",
    "type": "customRole",
    "permissions": [ {
      "actions": [
        "Microsoft.Storage/storageAccounts/blobServices/containers/delete",
        "Microsoft.Storage/storageAccounts/blobServices/containers/read",
        "Microsoft.Storage/storageAccounts/blobServices/containers/write",
        "Microsoft.Storage/storageAccounts/blobServices/generateUserDelegationKey/action"
      ],
      "notActions": [],
      "dataActions": [
        "Microsoft.Storage/storageAccounts/blobServices/containers/blobs/delete",
        "Microsoft.Storage/storageAccounts/blobServices/containers/blobs/read",
        "Microsoft.Storage/storageAccounts/blobServices/containers/blobs/write",
        "Microsoft.Storage/storageAccounts/blobServices/containers/blobs/move/action",
        "Microsoft.Storage/storageAccounts/blobServices/containers/blobs/add/action",
        "Microsoft.Storage/storageAccounts/blobServices/containers/blobs/tags/read"
      ],
      "notDataActions": []
    } ],
    "assignableScopes": [ "[resourceGroup().id]" ]
  }
}
```

The four actions and the first five dataActions are built-in Storage Blob Data Contributor's, verbatim (`az role definition list --name "Storage Blob Data Contributor"`, re-read 2026-10-21, same as §3). No `tags/write`, no wildcard, no condition.

**Where it is assigned:** `fileStoreGrants`, one `Microsoft.Authorization/roleAssignments` per entry of `containerNames` (default `procedures`, `invoice-files`, `billing-exports`), `scope` = that container, name `guid(container id, principalId, role id)`, `principalType: ServicePrincipal`, principal = the module's `principalId` (the BFF identity: the stamp UAMI on a stamp, `mi-bff-api-dev` on dev). Nothing is assigned at the account, resource group or subscription. `ai-chunks` keeps its built-in Contributor container grant from `storage-account.bicep` (§4) — it reads no tags.

**Module:** `infrastructure/bicep/modules/ebilling-file-store.bicep` (POML step 3): existing account + `default` blob service; the containers (`publicAccess: None`, no immutability); the role; the grants; Defender for Storage on-upload scanning (`Microsoft.Security/defenderForStorageSettings` `current`, `overrideSubscriptionLevelSettings: true`, sensitive data discovery off) only when `enableMalwareScanning`; outputs `containerNames` and `blobEndpoint` only. No network, soft-delete, key or Key Vault change.

**Deviations from the POML's step 3 text (recorded, not silent):**
- No `roleDefinitionId` parameter. Option A fixes the role; a parameter defaulting to built-in Contributor would make every download refuse (no tag read), and one that could be set to Owner would reintroduce tag write. The module creates and assigns its own role.
- `enableMalwareScanning` has no default, so every environment states its choice (model 4.2 "explicit per-environment setting").
- The compiled `modules/ebilling-file-store.json` is checked in (precedent `modules/bff-runtime-rbac.json`) because the ArchTest reads it; recompile with `az bicep build --file ebilling-file-store.bicep --outfile ebilling-file-store.json` after every edit.
- `customer.bicep` / `storage-account.bicep` are not changed by this session (POML constraint; customer wiring is the §6 hand-off). After merging `origin/work/ebilling-r1` the committed `customer.json` equals a fresh Bicep 0.46.1 compile of `customer.bicep` (no diff apart from line endings and the template hash), so no recompile was needed.

### 8.2 uac-r2 conditions — where each is met

| # | Condition | Met by | State |
|---|---|---|---|
| 1 | The role is exact; must-fire on tags/write or any `*`; must-not-fire on the exact set | Template: `CustomerStampKeylessTemplateTests.FileStoreRole_IsExactlyContributorPlusTagRead` (the real compiled definition — the must-not-fire control on the real role), `FileStoreRole_ExactSet_IsAccepted` (exact set, any order), `FileStoreRole_Deviation_IsFlagged` (14 must-fire cases: tags/write, tags/*, blobs/*, an action wildcard, `*`, missing tags/read, missing action, extra action, notActions, notDataActions, a second block, a condition, subscription- and account-assignable). Live: `scripts/ebilling/Test-EbillingFileStoreRole.ps1` step 1 diffs the DEPLOYED role against the LIVE built-in Contributor + tags/read (Pester `tests/scripts/Test-EbillingFileStoreRole.Tests.ps1`). | Met live 2026-10-21: §8.6 block `== 4. Test-EbillingFileStoreRole`, step 1 [PASS], exit 0. |
| 2 | Live proof: under the role, Set Blob Tags and Put Blob with `x-ms-tags` are both 403 | `scripts/ebilling/test-ebilling-file-store-tag-write.sh`, run **as the identity** in the App Service SSH console (§8.5 step 6). Controls: Put Blob without tags 201, Get Blob Tags 200. | **Met live 2026-10-22**: §8.7, all 3 containers PASS. |
| 3 | Downloads need a clean scan time no earlier than the blob's Last-Modified | Task 090's code. 090's POML notes already state it: "The download serves only when the tag 'Malware Scanning scan result' = 'No threats found' AND the scan time is no earlier than the blob's Last-Modified. A missing tag, an unreadable tag or an older scan refuses. Pinned by a test: tagged clean, then Last-Modified later, then refused. Keep the time check even if dev shows an overwrite clears tags." Restated here as binding on 091's hand-off: the role keeps blob write, so the time check is what stops an overwritten blob inheriting an earlier clean verdict. | Stated in both; implementation is 090's. |
| 4 | Per-container assignments only | Template: `FileStoreRole_IsAssignedPerContainerOnly` on the module, and the existing `StampBlobDataRoles_AreContainerScoped` on `customer.json` — `BlobDataRoleScopeViolations` now also counts a custom role defined in the same template with any blob data action, so an account-, RG- or deployment-scope grant of it fails (`CustomBlobRoleScope_NegativeControl`). Live: `Test-EbillingFileStoreRole.ps1` step 2 (every assignment of the role in the subscription is at one of the expected containers; the principal holds it on each). | Met live 2026-10-21: §8.6 block `== 4. Test-EbillingFileStoreRole`, step 2 [PASS], exit 0. |

**If step 3 of the data-plane proof returns 201** (Put Blob with `x-ms-tags` accepted under blob write alone), stop: an uploader could then write a forged "No threats found" tag and scan time at upload, which condition 3's time check does not catch. Report to uac-r2 before enabling downloads.

### 8.3 Seeding proofs (2026-10-21)

- Module edited to add `.../blobs/tags/write`, recompiled: `FileStoreRole_IsExactlyContributorPlusTagRead` fails ("dataActions has unexpected '…/tags/write'; tag write '…'"). Restored.
- Module grant `scope` changed to the storage account, recompiled: `FileStoreRole_IsAssignedPerContainerOnly` fails. `scope` line removed (deployment = resource-group scope), recompiled: fails. Restored.
- `BlobDataRoleScopeViolations` with custom-role recognition switched off (the session-2 rule): `FileStoreRole_IsAssignedPerContainerOnly` and `CustomBlobRoleScope_NegativeControl` fail. Restored.
- `Test-EbillingFileStoreRole.ps1` with the dataActions comparison, the tag-write check and the scope check switched off: 7 Pester must-fire tests fail. Restored.

### 8.4 Dev read-only inventory (2026-10-21, operator sign-in, nothing changed)

- `spaarke-bff-dev` identity: user-assigned only — `mi-bff-api-dev` (in `spe-infrastructure-westus2`, principal `9fd47efb-7962-492b-ac44-e5ccd0268ebb`) and `mi-ontology-writer-dev`. The app settings `ManagedIdentity__ClientId` and `UAMI_CLIENT_ID` equal `mi-bff-api-dev`'s client id (compared in the query; values not printed). **The file store's principal on dev is `mi-bff-api-dev`.**
- Neither identity holds any Storage role at any scope (`az role assignment list --assignee … --all`). There is no account-scope blob role to remove on dev; POML escalation trigger 1 does not fire on dev.
- No `SessionFileStore__*` or `FileStore*` app setting on `spaarke-bff-dev`.
- **`rg-spaarke-dev` has no storage account.** Accounts in the subscription: `insightsspaarkedevstg`, `sprkspaarkedevaifsa` (`spe-infrastructure-westus2`), `sprkcpartifactsdev` (`rg-spaarke-platform-dev`, control plane), `sharepointembeddedabc0`, `stspaarkewebsite` — all serve other workloads.

**Open owner choice (POML escalation trigger 3, before any live step):** which account dev's file store uses. (a) A new dedicated keyless account in `rg-spaarke-dev` (§8.5 step 0) — recommended: nothing else shares it, and the role is assignable only in that RG. (b) An existing account — every candidate is shared with another workload, which the POML says needs the owner's explicit OK.

### 8.5 Dev deploy and verify — the operator runs these

PowerShell 7, `az login` as an identity with Owner or User Access Administrator on the target RG (the deployment writes a role definition and role assignments).

```powershell
$sub       = '484bc857-3802-427f-9ea5-ca47b43db0f0'      # Spaarke Development Environment
$rg        = 'rg-spaarke-dev'                             # the account's RG (owner choice, §8.4)
$account   = 'sprkspaarkedevebillsa'                      # owner choice, §8.4 (the name shown is option (a))
$principal = '9fd47efb-7962-492b-ac44-e5ccd0268ebb'      # mi-bff-api-dev
$scan      = 'false'                                      # owner's scanning choice: 'true' = scan with cap, 'false' = "Scanning not enabled"
$cap       = 5000
$module    = 'infrastructure/bicep/modules/ebilling-file-store.bicep'
$params    = @("storageAccountName=$account", "principalId=$principal", "enableMalwareScanning=$scan", "malwareScanningCapGBPerMonth=$cap")

# 0. Only for option (a): create the dedicated keyless account (owner OK first)
az storage account create -n $account -g $rg --subscription $sub -l westus2 --sku Standard_LRS --kind StorageV2 `
  --min-tls-version TLS1_2 --allow-blob-public-access false --allow-shared-key-access false --https-only true

# 1. BEFORE reads (paste the output into this file)
$accountId = az storage account show -n $account -g $rg --subscription $sub --query id -o tsv
az storage account show --ids $accountId --query "{network:networkRuleSet.defaultAction,sharedKey:allowSharedKeyAccess,publicBlob:allowBlobPublicAccess}" -o json
az storage account blob-service-properties show --account-name $account -g $rg --subscription $sub --query "{blobSoftDelete:deleteRetentionPolicy,containerSoftDelete:containerDeleteRetentionPolicy}" -o json
az rest --method get --url "https://management.azure.com$accountId/providers/Microsoft.Security/defenderForStorageSettings/current?api-version=2022-12-01-preview" --query properties -o json
az role assignment list --assignee $principal --all --query "[].{role:roleDefinitionName,scope:scope}" -o table

# 2. Build, then what-if (save the output here; show the owner before step 3)
az bicep build --file $module --stdout | Out-Null
az deployment group what-if -g $rg --subscription $sub --name ebilling-file-store-dev --template-file $module --parameters @params

# 3. Apply - only after the owner's OK on the what-if
az deployment group create -g $rg --subscription $sub --name ebilling-file-store-dev --template-file $module --parameters @params

# 4. Verify the role and the assignments (uac-r2 conditions 1 and 4); exit 0 expected
./scripts/ebilling/Test-EbillingFileStoreRole.ps1 -SubscriptionId $sub -ResourceGroup $rg -StorageAccountName $account -PrincipalId $principal

# 5. AFTER reads: containers private; repeat every step-1 read and compare (network, shared key, soft delete unchanged;
#    Defender = the owner's choice; the principal's only storage roles are the three container-scope grants)
az storage container-rm list --storage-account $account -g $rg --subscription $sub --query "[].{name:name,publicAccess:publicAccess}" -o table

# 6. Data-plane proof AS THE IDENTITY (uac-r2 condition 2). Wait about 10 minutes after step 3 for RBAC to propagate.
az webapp ssh -g rg-spaarke-dev -n spaarke-bff-dev --subscription $sub
#   In the SSH shell: create /tmp/t.sh with the content of scripts/ebilling/test-ebilling-file-store-tag-write.sh
#   (for example: cat > /tmp/t.sh, paste, Ctrl-D), then run it once per container:
#     bash /tmp/t.sh sprkspaarkedevebillsa invoice-files
#     bash /tmp/t.sh sprkspaarkedevebillsa procedures
#     bash /tmp/t.sh sprkspaarkedevebillsa billing-exports
#   Expected, each run:
#     [PASS] 1 Put Blob (no tags) - control: HTTP 201
#     [PASS] 2 Set Blob Tags - must be refused: HTTP 403 (AuthorizationPermissionMismatch)
#     [PASS] 3 Put Blob with x-ms-tags - must be refused: HTTP 403 (AuthorizationPermissionMismatch)
#     [PASS] 4 Get Blob Tags - control (tags/read granted): HTTP 200
#     [PASS] 5 Delete probe a: HTTP 202
#     RESULT: PASS ...
#   Step 1 at 403 = RBAC not propagated yet (retry later), or the wrong identity or container. Step 3 at 201 = STOP (§8.2).
```

No Key Vault command and no account key, SAS or connection string anywhere above.

### 8.6 Dev run (2026-10-21)

Owner decisions 2026-10-21:
- Create a dedicated account in rg-spaarke-dev (option (a), `sprkspaarkedevebillsa`).
- Apply after the what-if.
- Dev scanning: **"Scanning not enabled"** (`enableMalwareScanning=false`; files are served as "Not scanned" on dev).

Steps 0–5 were run by the main session as ralph.schroeder@spaarke.com. Step 6, the data-plane proof as the identity, ran 2026-10-22 (§8.7).

```
== 0. create account
{
  "name": "sprkspaarkedevebillsa",
  "publicBlob": false,
  "sharedKey": false,
  "state": "Succeeded",
  "tls": "TLS1_2"
}
== 1. before reads
{
  "network": "Allow",
  "publicBlob": false,
  "sharedKey": false
}
{
  "blobSoftDelete": {
    "allowPermanentDelete": false,
    "days": null,
    "enabled": false
  },
  "containerSoftDelete": null
}
{
  "isEnabled": false,
  "malwareScanning": {
    "onUpload": {
      "capGBPerMonth": -1,
      "isEnabled": false
    }
  },
  "overrideSubscriptionLevelSettings": false,
  "sensitiveDataDiscovery": {
    "isEnabled": false
  }
}
Role                                             Scope
-----------------------------------------------  --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
Cognitive Services OpenAI User                   /subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/spe-infrastructure-westus2/providers/Microsoft.CognitiveServices/accounts/spaarke-openai-dev
Cognitive Services User                          /subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/spe-infrastructure-westus2/providers/Microsoft.CognitiveServices/accounts/spaarke-openai-dev
Key Vault Secrets User                           /subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/SharePointEmbedded/providers/Microsoft.KeyVault/vaults/spaarke-spekvcert
Azure Service Bus Data Sender                    /subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourcegroups/SharePointEmbedded/providers/Microsoft.ServiceBus/namespaces/spaarke-servicebus-dev/topics/sprk-membership-changes
Azure Service Bus Data Receiver                  /subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourcegroups/SharePointEmbedded/providers/Microsoft.ServiceBus/namespaces/spaarke-servicebus-dev/topics/sprk-membership-changes/subscriptions/recon-junction-updater
Search Index Data Contributor                    /subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/spe-infrastructure-westus2/providers/Microsoft.Search/searchServices/spaarke-search-dev
Communication and Email Service Owner            /subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/rg-spaarke-dev/providers/Microsoft.Communication/CommunicationServices/spaarke-acs-dev
Azure Service Bus Data Sender                    /subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/SharePointEmbedded/providers/Microsoft.ServiceBus/namespaces/spaarke-servicebus-dev
Azure Service Bus Data Receiver                  /subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/SharePointEmbedded/providers/Microsoft.ServiceBus/namespaces/spaarke-servicebus-dev
Cognitive Services Content Understanding Reader  /subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/rg-spaarke-dev/providers/Microsoft.CognitiveServices/accounts/spaarke-docintel-invoices-dev
== 2. what-if (scan=false)
Change
--------
Create
Ignore
Ignore
Ignore
Ignore
Ignore
Ignore
Ignore
Ignore
Ignore
Create
Create
Create
Create
Create
Create
Ignore
Ignore
Ignore
Create   Microsoft.Authorization/roleDefinitions/fa22633d-1c13-50ff-b07d-e23d4d9eb6e5
Create   Microsoft.Storage/storageAccounts/sprkspaarkedevebillsa/blobServices/default/containers/billing-exports
Create   Microsoft.Storage/storageAccounts/sprkspaarkedevebillsa/blobServices/default/containers/billing-exports/providers/Microsoft.Authorization/roleAssignments/4853f12b-9ff0-5d19-9d13-ad46b9840096
Create   Microsoft.Storage/storageAccounts/sprkspaarkedevebillsa/blobServices/default/containers/invoice-files
Create   Microsoft.Storage/storageAccounts/sprkspaarkedevebillsa/blobServices/default/containers/invoice-files/providers/Microsoft.Authorization/roleAssignments/1c3eb40b-f168-59f5-8337-26dd79562761
Create   Microsoft.Storage/storageAccounts/sprkspaarkedevebillsa/blobServices/default/containers/procedures
Create   Microsoft.Storage/storageAccounts/sprkspaarkedevebillsa/blobServices/default/containers/procedures/providers/Microsoft.Authorization/roleAssignments/1784d197-94c2-5eec-9dd5-a9269d66d27f
ignored: 12 existing resources in the RG not in the template
== 3. apply (owner OK 2026-10-21; scan=false)
{
  "outputs": {
    "blobEndpoint": {
      "type": "String",
      "value": "https://sprkspaarkedevebillsa.blob.core.windows.net/"
    },
    "containerNames": {
      "type": "Array",
      "value": [
        "procedures",
        "invoice-files",
        "billing-exports"
      ]
    }
  },
  "state": "Succeeded"
}
== 4. Test-EbillingFileStoreRole

>> 1. Role definition is exact (uac-r2 condition 1)
   [INFO] role 'Spaarke eBilling File Store Blob Data (2bit77dli45c2)' id fa22633d-1c13-50ff-b07d-e23d4d9eb6e5
   [PASS] exactly Storage Blob Data Contributor (live) + blobs/tags/read; no wildcard, no tags/write

>> 2. Assigned per container only (uac-r2 condition 4)
   [INFO] 3 assignment(s) of the role in the subscription
   [PASS] one assignment per container (procedures, invoice-files, billing-exports), none above container scope

>> 3. Other blob data roles the principal holds on the account (context)
   [PASS] no built-in Storage Blob Data role on the account or above

>> 4. Data-plane 403 proof (uac-r2 condition 2)
   [INFO] not run here: run scripts/ebilling/test-ebilling-file-store-tag-write.sh in the App Service SSH console (as the identity)
exit=0
== 5. after reads
{
  "network": "Allow",
  "publicBlob": false,
  "sharedKey": false
}
{
  "blobSoftDelete": false,
  "containerSoftDelete": null
}
{
  "enabled": false,
  "onUpload": false
}
Name             PublicAccess
---------------  --------------
billing-exports  None
invoice-files    None
procedures       None
C:\code_files\spaarke-wt-ebilling-r1>  "C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\\..\python.exe" -IBm azure.cli role assignment list --assignee 9fd47efb-7962-492b-ac44-e5ccd0268ebb --all --query [?contains(scope,'Storage')].{role:roleDefinitionName,scope:scope} -o table
].{role:roleDefinitionName was unexpected at this time.
Spaarke eBilling File Store Blob Data (2bit77dli45c2) | sprkspaarkedevebillsa/blobServices/default/containers/billing-exports
Spaarke eBilling File Store Blob Data (2bit77dli45c2) | sprkspaarkedevebillsa/blobServices/default/containers/invoice-files
Spaarke eBilling File Store Blob Data (2bit77dli45c2) | sprkspaarkedevebillsa/blobServices/default/containers/procedures
```

**State:**
- uac-r2 conditions 1 and 4 are met live (Test-EbillingFileStoreRole.ps1 exit 0).
- The account is unchanged apart from the module's resources: network Allow (as created), shared key off, public blob off, no soft delete, Defender off.
- The identity's only storage roles are the three container-scope grants.
- Condition 2 (step 6) is still open. 090 downloads stay off on every environment until it passes.

### 8.7 uac-r2 condition 2 (c): tag-write proof as the identity (2026-10-22)

The operator ran `scripts/ebilling/test-ebilling-file-store-tag-write.sh` in the spaarke-bff-dev App Service SSH console, which means it ran as mi-bff-api-dev with a token from the App Service managed-identity endpoint. It ran once per container against `sprkspaarkedevebillsa`. Every container got the same result:

| Step | Expected | invoice-files | procedures | billing-exports |
|---|---|---|---|---|
| 1 Put Blob, no tags (control) | 201 | 201 | 201 | 201 |
| 2 Set Blob Tags | 403 | 403 AuthorizationPermissionMismatch | 403 AuthorizationPermissionMismatch | 403 AuthorizationPermissionMismatch |
| 3 Put Blob with x-ms-tags | 403 | 403 AuthorizationPermissionMismatch | 403 AuthorizationPermissionMismatch | 403 AuthorizationPermissionMismatch |
| 4 Get Blob Tags (control) | 200 | 200 | 200 | 200 |
| 5 Delete probe a | 202 | 202 | 202 | 202 |

All three ended `RESULT: PASS` and `exit 0`. Probe b was never created, because step 3 was refused. The identity can write blobs and read tags, but cannot write tags, so it cannot forge the malware-scan verdict. All three uac-r2 conditions are met.

The first paste attempt did not run. Bracketed-paste markers broke the first word, and the base64 decode carried CRLF line endings. The paste line now starts with `true; ` and passes the decode through `tr -d '
'`.


### 8.8 Blob soft delete, 30 days, every environment (DEF-038, #2255; owner decision 2026-10-22)

Owner decision 2026-10-22: turn on blob soft delete with 30-day retention for the file store in every environment, including dev. The module change is on `task/ebill-2255-softdelete`. **Applied on dev 2026-10-22 by the main session (owner OK: all environments including dev, after a what-if).** The record is at the end of this section.

**Change** (`modules/ebilling-file-store.bicep` and its recompile `.json`):
- `blobServices/default` was a reference (`existing`); it is now declared, with `properties.deleteRetentionPolicy = { enabled: true, days: blobSoftDeleteRetentionDays }` and nothing else. The containers now depend on it.
- New parameter `blobSoftDeleteRetentionDays`: default 30, range 1..365. customer-provisioning (#2232) passes it per environment; an environment that does not override gets 30.
- Not set, on purpose: versioning, change feed, container soft delete, restore policy, CORS, `allowPermanentDelete`. Role, assignments, network and the Defender setting are untouched.
- Tests (`CustomerStampKeylessTemplateTests`): must-fire `FileStoreBlobService_EnablesSoftDeleteWithParameterDays` (enabled, days = the parameter, default 30, 1..365), `FileStoreContainers_DependOnBlobService`, `FileStoreBlobService_Deviation_IsFlagged` (9 cases: disabled, literal days, missing policy, versioning, change feed, container soft delete, CORS, restore policy, permanent delete); must-not-fire `FileStoreBlobService_ExactShape_IsAccepted`. The `FileStoreRole_*` tests are unchanged and still pass.

**Why the PUT must be read before the apply.** The §8.6 "== 1. before reads" block read only `deleteRetentionPolicy` (enabled false, days null, allowPermanentDelete false) and `containerDeleteRetentionPolicy` (null). Versioning, change feed, CORS, restore policy, last-access-time tracking and the default service version were not read. The template writes none of them; the expectation is that an ARM PUT leaves unstated blob-service properties alone, but that is not verified here. So capture the full blob-service properties before and after, and diff them: only `deleteRetentionPolicy` may change.

Run from the worktree root, PowerShell 7, signed in as before (variables as in §8.5, `$params` extended):

```powershell
$sub       = '484bc857-3802-427f-9ea5-ca47b43db0f0'
$rg        = 'rg-spaarke-dev'
$account   = 'sprkspaarkedevebillsa'
$principal = '9fd47efb-7962-492b-ac44-e5ccd0268ebb'      # mi-bff-api-dev
$scan      = 'false'                                      # unchanged dev choice (§8.6)
$cap       = 5000
$days      = 30
$module    = 'infrastructure/bicep/modules/ebilling-file-store.bicep'
$params    = @("storageAccountName=$account", "principalId=$principal", "enableMalwareScanning=$scan", "malwareScanningCapGBPerMonth=$cap", "blobSoftDeleteRetentionDays=$days")

# 1. BEFORE read: the whole blob service, not just soft delete (save the output here)
az storage account blob-service-properties show --account-name $account -g $rg --subscription $sub -o json

# 2. Build, then what-if (show the owner before step 3)
az bicep build --file $module --stdout | Out-Null
az deployment group what-if -g $rg --subscription $sub --name ebilling-file-store-dev --template-file $module --parameters @params

# 3. Apply - only after the owner's OK on the what-if
az deployment group create -g $rg --subscription $sub --name ebilling-file-store-dev --template-file $module --parameters @params

# 4. AFTER read: diff against step 1; only deleteRetentionPolicy (enabled true, days 30) may differ
az storage account blob-service-properties show --account-name $account -g $rg --subscription $sub -o json
./scripts/ebilling/Test-EbillingFileStoreRole.ps1 -SubscriptionId $sub -ResourceGroup $rg -StorageAccountName $account -PrincipalId $principal
```

**What the what-if should show:** `Modify` on `.../blobServices/default` with `deleteRetentionPolicy.enabled: false -> true` and `days: null -> 30`; the role definition, the three assignments and the three containers unchanged (or `Ignore`/no-change); no Defender resource (`scan=false`). Anything else, stop and tell the owner.

**Side effect:** blobs deleted from now on stay recoverable for 30 days and still count toward storage. The §8.7 tag-write probe's "delete probe a" now leaves a soft-deleted blob in each container for 30 days; harmless.

**Dev run, 2026-10-22** (main session, as ralph.schroeder@spaarke.com; the steps above):
- **Before read:** saved the whole blob service. `deleteRetentionPolicy` was `{allowPermanentDelete:false, days:null, enabled:false}`.
- **What-if:** `blobServices/default` showed Modify: `deleteRetentionPolicy.enabled` false -> true and `days` null -> 30. The what-if also listed three items that changed nothing, as the next two reads prove:
  - `allowPermanentDelete` false -> null. It is still false after the apply.
  - The role definition `type` "CustomRole" -> "customRole", a case-only difference.
  - On each container, `defaultEncryptionScope` "$account-encryption-key" -> null and `denyEncryptionScopeOverride` false -> null. An encryption scope can't change after the container is created.
  The 3 assignments were NoChange. There was no Defender resource.
- **Apply:** `Succeeded`.
- **After read, diffed with the before read:** the only difference is `deleteRetentionPolicy` `{allowPermanentDelete:false, days:30, enabled:true}`.
- **Containers, read before and after:** identical. Each has scope `$account-encryption-key`, deny false, publicAccess None, and no immutability policy or legal hold.
- **`Test-EbillingFileStoreRole.ps1`:** exit 0, with conditions 1 and 4 still [PASS].

