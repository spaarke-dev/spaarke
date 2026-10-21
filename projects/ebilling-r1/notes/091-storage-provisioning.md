# Task 091 — File store provisioning: inventory, tag-permission finding, escalation

> Project: ebilling-r1 (model `procedure-and-file-model.md` §4.2; owner re-plan Q5, 2026-10-21). Session 1 (2026-10-21) was authoring only: no deploy, no what-if, no role-assignment change, no Key Vault change. **The session stopped at step 2 on the POML's second escalation trigger (§3).** No bicep was written.

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

## 4. Planned bicep (not written — for the next session)

- `storage-account.bicep`: delete `uamiStorageBlobRbac` and the legacy `storageBlobRbac` (both account scope); add a parameter naming the containers the stamp identity is granted on (default empty), and a loop over the module's `blobContainers` creating one Storage Blob Data Contributor assignment **at container scope** per listed container (`guid(container id, principal, role)`, `principalType: ServicePrincipal`), copying `acs-communication.bicep` :98-124.
- `customer.bicep`: pass `['ai-chunks']` for the UAMI (and union it into `effectiveStorageContainers` so the container always exists); update the comments at :309-318 that say the UAMI holds the role on the account.
- Recompile `customer.json` (`az bicep build --file customer.bicep --outfile customer.json`; `CustomerStampKeylessTemplateTests` and `ArmTemplateInspectorTests` read it). The committed `customer.json` matched a fresh 0.46.1 build on 2026-10-21 (line endings aside), so the regenerated diff will be this change only.
- `modules/ebilling-file-store.bicep` (POML step 3) with `roleDefinitionId` per the owner's answer to §3.

## 5. Stale text to fix with the account-scope removal (not edited — out of this session's file scope)

- `src/server/api/Sprk.Bff.Api/appsettings.template.json:497` `_operator_prerequisites` tells the operator to grant Storage Blob Data Contributor "on that storage account" and says the module grants only via `appServicePrincipalId`.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/SessionFileBlobStore.cs:558-560` exception message: "needs the 'Storage Blob Data Contributor' role on the storage account … 'appServicePrincipalId' is passed to the module".
- `src/server/api/Sprk.Bff.Api/Api/Ai/ChatDocumentEndpoints.cs:100` doc comment (same assumption).

## 6. Hand-off to customer-provisioning-orchestration-r1 (to file after §3 is decided)

Not filed yet (step 7 depends on §3). Contents:

1. Wire `ebilling-file-store.bicep` into `customer.bicep` for the stamp UAMI, with the role chosen in §3 (custom role: create the definition per customer subscription before the module runs).
2. The per-environment malware-scanning setting (Defender on-upload scanning with a monthly cap, or the explicit "Scanning not enabled" setting).
3. The BFF config keys task 090 defines.
4. Network `defaultAction: 'Deny'` / private endpoint at prod hardening (K-3, accepted by the owner).
5. The container-scope-only rule for the stamp UAMI (§1), the `ai-chunks` grant, and the §2 inventory: a new blob consumer in the BFF needs its own container-scoped grant, or it fails with 403.
6. This file's §3 finding.

## 7. Acceptance criteria status

| Criterion | Status |
|---|---|
| Module builds clean, no secret | Not started (stopped at step 2) |
| Notes record inventory, tag finding, what-if, owner OK | Tag finding and code inventory recorded; live az inventory (step 1), what-if and owner OK pending |
| Containers + one container-scope assignment each on dev | Live step — needs owner OK |
| Negative: no account/RG/subscription grant; network, soft delete, KV unchanged | Live step |
| Defender matches owner's choice | Live step |
| Hand-off issue filed and in `defer-issues.md` | Pending §3 |
