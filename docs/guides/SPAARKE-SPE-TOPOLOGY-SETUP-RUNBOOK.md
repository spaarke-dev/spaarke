# Spaarke SPE Topology Setup Runbook

> **Authored**: 2026-08-30 by customer-provisioning-orchestration-r1 task 213.5
> **Rewritten**: 2026-10-03 by the same project, task 248 — the owning app's credential is now a **managed-identity
> federated identity credential (MI-FIC)**, ADR-028 A4's default for confidential clients (owner decision D16,
> 2026-10-02; no ADR amendment). The owning app has **no certificate and no client secret**. Every earlier
> instruction to create, upload or import an owning-app certificate is withdrawn.
> **Authority**: [SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md](../architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md) (owner-attested authoritative 2026-08-30)
> **Audience**: operator standing up a new Spaarke SPE container type (Model 1 / Model 2) for the first time
> **Runs**: ONCE per container type — NOT per customer

---

## What this runbook does

Executes the one-time setup that must be in place BEFORE the first customer that uses a given container type can be
provisioned. Per topology doc §3, a container type serves many customers through containers inside it; it is
permanent (topology doc R1–R3), so it is created once, by an operator, through a delegated flow (R5).

This runbook creates, for one container type:
- the **owning app registration** (Entra, single-tenant for Model 1) — no secret, no certificate;
- the **federated identity credential** on that owning app that lets the L2 control plane's Worker act as it;
- the **container type** itself, with its billing;
- the **Worker configuration entry** that tells L2 which owning app owns which container type.

It does **not** create a BFF app registration. Since D-13 every customer has its own BFF app registration, created by
handler **H3** during that customer's provisioning run (Step 9 below explains the BFF's container-type grant).

**How L2 acts as the owning app (no stored credential).** The L2 Worker runs as the user-assigned managed identity
`sprk-controlplane-{env}-uami`. It requests a managed-identity token for the audience `api://AzureADTokenExchange` and
presents that token as the client assertion
(`client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer`) in a client-credentials request for
the owning app. Entra accepts it because the owning app carries a federated identity credential whose subject is that
UAMI. In code: `SpeConfidentialClientGraphFactory` over `WorkerDataverseCredentialFactory.CreateManagedIdentityFederatedCredential`
(Azure.Identity `ClientAssertionCredential`). There is no certificate path and no fallback. MI-as-FIC requires a
**user-assigned** managed identity in the **same tenant** as the app.

> **Worked example — Spaarke Model 1 (2026-10-03)**
>
> | Item | Value |
> |---|---|
> | Owning app | `Spaarke SPE Model 1 Owner` — appId `bfac7f6e-9fa0-4664-8492-c7a1dfe73d5e`, object id `b0f01a91-7836-4949-be96-4fcab69419c2`, single-tenant, **no secret, no certificate** |
> | Graph application permissions | `FileStorageContainer.Selected`, `FileStorageContainerTypeReg.Selected` — admin-consented via `appRoleAssignedTo` |
> | Federated identity credential | `sprk-controlplane-dev-uami-assertion` — issuer `https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2/v2.0`, subject `38f7693f-e6e2-4a3e-9acf-7f9e29dd4044`, audience `api://AzureADTokenExchange` |
> | Trusted identity | dev Worker UAMI `sprk-controlplane-dev-uami` — principal (object) id `38f7693f-e6e2-4a3e-9acf-7f9e29dd4044`, client id `965a4a01-01e1-442b-97a6-6a98308018b3` |
> | Container type | `Spaarke Model 1` — id `fb3817a8-5a55-42ba-8cc9-12cf055168b8`, `standard`, created by the owner in the SharePoint admin center |
> | Billing | `Microsoft.Syntex/accounts` `dc4749c2-ca04-4b38-b6c2-e38dc3eec72b` in `rg-spaarke-shared-prod` (subscription "Spaarke Shared Production", eastus). **Permanent — never delete that resource group or account** |
> | Registration in Spaarke's tenant | registered at creation (`registeredDateTime` 2026-10-03T22:54:58Z), `billingClassification` `standard`, `billingStatus` `valid` |
> | Grants on the registration | the owning app (delegated `full` / application `full`); **known extra grant**: Microsoft Graph Explorer `de8bc8b5-d9f9-48b1-a8ad-b748da725064` (delegated `full` / application `none`) — added during creation, not by L2; owner decision 2026-10-03: **keep** |
> | Worker setting | `speContainerTypeOwners = [{ containerTypeId: 'fb3817a8-…', ownerAppId: 'bfac7f6e-…' }]` in `platform-controlplane-dev.bicepparam`, deployed to the dev control plane 2026-10-03 |
> | Live proof | from a throwaway Azure Container Instance carrying the dev Worker UAMI (deleted afterwards): the FIC exchange returned an owning-app Graph token with `appidacr` = `2` (client-assertion class, the same class as a certificate), `idtyp` = `app`, roles `[FileStorageContainerTypeReg.Selected, FileStorageContainer.Selected]`; the registration GET returned 200; listing containers of the type returned 200 with an empty list |

---

## Prerequisites BEFORE running

- Operator can register Entra app registrations and grant tenant-wide admin consent in the Spaarke tenant
  (Application Administrator / Cloud Application Administrator for the app; Privileged Role Administrator or Global
  Administrator for the app-role consent in Step 2).
- Operator can create container types in the SharePoint admin center (SharePoint Embedded Administrator or SharePoint
  Administrator) — container-type create is delegated-only (topology doc R5).
- An Azure subscription + resource group for the `standard` billing account, with owner/contributor rights. For Model 1
  this is `rg-spaarke-shared-prod` (owner D23).
- The L2 control plane for the target environment is deployed, so its Worker UAMI `sprk-controlplane-{env}-uami`
  exists. You need its **principal (object) id** for Step 3:
  ```powershell
  az identity show -g rg-spaarke-platform-{env} -n sprk-controlplane-{env}-uami --query principalId -o tsv
  ```
- `az` CLI logged in as the operator's OWN AAD identity (NEVER a service principal — per NFR-11).

---

## The setup

### Step 1 — Register the owning app (Entra, single-tenant, no secret, no certificate)

**What**: create the owning app for the container type. The binding is permanent and 1:1 (topology doc R1).

**Command** (`-TenantId` is mandatory per FR-28 / I1):
```powershell
./scripts/Register-EntraAppRegistrations.ps1 `
  -TenantId $env:AZURE_TENANT_ID `
  -CreateOwningApp Model1
```

The script is idempotent (an existing app with the same display name is skipped with a warning). It creates the app
secret-free and adds Graph `FileStorageContainer.Selected` (Application) to its required permissions. It refuses
`-AllowClientSecretMint` together with `-CreateOwningApp`.

**Manual fallback** (only if the script fails for a tenant-specific reason):
1. Portal → Entra ID → App registrations → New registration.
2. Name: `Spaarke SPE Model 1 Owner`.
3. Supported account types: **Accounts in this organizational directory only (Single tenant — AzureADMyOrg)**.
4. Redirect URI: leave blank.
5. Save. Record the **Application (client) ID** and the **Object ID**.
6. Make sure a service principal exists: `az ad sp create --id <owningAppId>` (harmless if it already exists).

**Do not** add a client secret or a certificate to this app — not now and not later. L2 authenticates through the
federated credential in Step 3.

**Model 2 note (topology doc §3A row 3)**: `Spaarke SPE Model 2 Owner` — and only that owning app — is
**multi-tenant** (`AzureADMultipleOrgs`), because Model 2 customers consent to it in their own tenant. Model 2 is out
of scope for customer-provisioning-orchestration-r1 (D3).

**Output**: `owningAppId` (client id) and the app's object id.

---

### Step 2 — Grant the two Graph application permissions and admin-consent them

**What**: the owning app needs two Microsoft Graph **Application** permissions:

| Permission | Why |
|---|---|
| `FileStorageContainer.Selected` | create, activate and list containers of the type (H8, H13 T6) |
| `FileStorageContainerTypeReg.Selected` | read and write the container type's **registration** (H0 check, Step 5, per-app grants) |

**Why not `az ad app permission admin-consent`**: for this app it failed with *"Consent validation failed"*. Consent
was granted instead by creating the app-role assignments directly on the Microsoft Graph service principal, which is
what admin consent does for application permissions.

**Commands**:
```powershell
$owningAppId = '<owningAppId from Step 1>'
$graphAppId  = '00000003-0000-0000-c000-000000000000'   # Microsoft Graph

# Object ids of the two service principals
$owningSpId = az ad sp show --id $owningAppId --query id -o tsv
$graphSpId  = az ad sp show --id $graphAppId  --query id -o tsv

# App-role ids — look them up; do not hard-code
$roleContainer = az ad sp show --id $graphAppId --query "appRoles[?value=='FileStorageContainer.Selected'].id | [0]" -o tsv
$roleTypeReg   = az ad sp show --id $graphAppId --query "appRoles[?value=='FileStorageContainerTypeReg.Selected'].id | [0]" -o tsv

# Declare both on the app (so the portal shows them), then grant consent per role
az ad app permission add --id $owningAppId --api $graphAppId --api-permissions "$roleContainer=Role" "$roleTypeReg=Role"

foreach ($role in @($roleContainer, $roleTypeReg)) {
  $body = @{ principalId = $owningSpId; resourceId = $graphSpId; appRoleId = $role } | ConvertTo-Json -Compress
  az rest --method POST `
    --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$graphSpId/appRoleAssignedTo" `
    --headers "Content-Type=application/json" --body $body
}
```

A `Permission being assigned already exists` error on re-run means that role is already granted.

**Verify**: Portal → Entra ID → App registrations → the owning app → API permissions — both rows show
"Granted for <tenant>". Or:
```powershell
az rest --method GET --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$owningSpId/appRoleAssignments" `
  --query "value[].appRoleId"
```

---

### Step 3 — Add the federated identity credential that trusts the L2 Worker UAMI

**What**: a federated identity credential (FIC) on the owning app whose subject is the L2 Worker's user-assigned
managed identity. This is the owning app's **only** credential.

| Field | Value |
|---|---|
| name | `sprk-controlplane-{env}-uami-assertion` |
| issuer | `https://login.microsoftonline.com/{spaarkeTenantId}/v2.0` |
| subject | the Worker UAMI's **principal (object) id** — never its client id |
| audiences | `["api://AzureADTokenExchange"]` |

**Command**:
```powershell
$fic = @{
  name      = 'sprk-controlplane-dev-uami-assertion'
  issuer    = "https://login.microsoftonline.com/$env:AZURE_TENANT_ID/v2.0"
  subject   = '<Worker UAMI principalId>'
  audiences = @('api://AzureADTokenExchange')
} | ConvertTo-Json -Compress
az ad app federated-credential create --id $owningAppId --parameters $fic
```

`./scripts/Register-EntraAppRegistrations.ps1 -FicOnly -FederatedCredentialAppId <owningAppId> -UamiResourceId <UAMI resource id> -FederatedCredentialName sprk-controlplane-{env}-uami-assertion`
does the same idempotently and then tries to verify it by a real token exchange; from a workstation that verification
needs `-AssertionToken` (a UAMI token can only be minted inside Azure) or `-AllowUnverified`.

**Rules**:
- One FIC per Worker UAMI that must act as this owner (for example, one per environment's control plane). An app
  accepts at most **20** federated credentials.
- The UAMI must be **user-assigned** and in the **same tenant** as the app.
- The FIC name is a label; Entra matches on issuer + subject + audience.

---

### Step 4 — Create the container type in the SharePoint admin center

**What**: create the container type through a delegated flow (topology doc R5 — app-only returns 403).

```
https://<tenant>-admin.sharepoint.com → Advanced → Containers → Container types → New
  → Name: Spaarke Model 1
  → Owning app: Spaarke SPE Model 1 Owner (appId from Step 1)
  → Billing: standard — select the Azure subscription and resource group for the billing account
```

- **Standard billing** attaches a `Microsoft.Syntex/accounts` billing account in the subscription and resource group
  you choose. That binding is **permanent**: never delete the resource group or the account (for Model 1:
  `rg-spaarke-shared-prod`, owner D23). If billing setup fails with `SubscriptionNotRegistered`, the `Microsoft.Syntex`
  resource provider registration is still propagating — wait a few minutes and retry.
- **The admin center asks for a client secret on the owning app. Do not add one** — skip that prompt. The owning app
  stays secret-free; L2 uses the federated credential.
- The SPE Admin app (`SPAARKE-SPE-Admin-CLI`, `68cf5a14-1efb-4254-80bf-2761ffc89373`) and the SharePoint Embedded
  VS Code extension are alternative delegated paths. Do **not** use `scripts/Create-NewContainerType.ps1` — it is
  deprecated (task 213.3: app-only, 403 by design) and throws on invocation.
- `directToCustomer` (Model 2) attaches no Spaarke billing; the customer activates pay-as-you-go in their M365 admin
  center (topology doc §4).

**Verify** in the admin center (Container types → the new type): name, owning app, billing classification and billing
status. App-only `GET /storage/fileStorage/containerTypes` (and `/containerTypes/{id}`) is documented as 403 for
app-only callers, and `GET /containerTypes/{id}` also returned 403 to the Azure CLI's delegated identity on
2026-10-03 — use the admin center, or Graph Explorer signed in as an SPE administrator.

**Output**: `containerTypeId`.

---

### Step 5 — Check the registration in Spaarke's tenant; register only if missing

**What**: the container type must be **registered** in the consuming tenant (for Model 1, Spaarke's own tenant). The
admin-center flow registered `Spaarke Model 1` at creation, so this step is normally a check.

**Check** — as the owning app:
```http
GET https://graph.microsoft.com/v1.0/storage/fileStorage/containerTypeRegistrations/{containerTypeId}
Authorization: Bearer <owning-app token>
```
- `200` — registered. Confirm `owningAppId`, `billingClassification`, `billingStatus` and the grants list.
- `404` — not registered; register it (below).

**Getting an owning-app token.** The owning app has no secret or certificate, so the only way to sign in as it is from
Azure compute carrying the Worker UAMI: request a managed-identity token for the resource `api://AzureADTokenExchange`
(as the UAMI's client id), then post it to `https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token` with
`grant_type=client_credentials`, `client_id={owningAppId}`, `scope=https://graph.microsoft.com/.default`,
`client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer` and `client_assertion={MI token}`.
On 2026-10-03 this was done from a throwaway Azure Container Instance carrying the dev Worker UAMI, deleted
afterwards. In normal operation you do not need to: **L2's H0 performs this exact check on every run** (Step 8).

**Register** (only on 404):
```http
PUT https://graph.microsoft.com/v1.0/storage/fileStorage/containerTypeRegistrations/{containerTypeId}
Authorization: Bearer <owning-app token>
Content-Type: application/json

{
  "applicationPermissionGrants": [
    { "appId": "{owningAppId}", "delegatedPermissions": ["full"], "applicationPermissions": ["full"] }
  ]
}
```
- Permission: application `FileStorageContainerTypeReg.Selected`. With that permission Graph limits changes to
  registrations **owned by the calling app**, so the call must be made **as the owning app**.
- This PUT is **create-or-replace**. A later PUT of the whole registration replaces the grants list — it must keep the
  owning-app grant (and any other grant that should survive). Prefer the per-app grant API (Step 9) for adding grants.
- The legacy SharePoint REST registration (`/_api/v2.1/storageContainerTypes/.../applicationPermissions`) returns
  `apiNotFound` in this tenant. Do not use it.

---

### Step 6 — Add the owner entry to the control-plane configuration and deploy

**What**: tell the L2 Worker which owning app owns the container type. The Worker reads exactly two settings per
entry: `SpeContainerOptions__ContainerTypeOwners__{i}__ContainerTypeId` and `__OwnerAppId`. There are no `OwnerCert*`
settings.

**Edit** `infrastructure/bicep/parameters/platform-controlplane-{env}.bicepparam`:
```bicep
param speContainerTypeOwners = [
  {
    containerTypeId: 'fb3817a8-5a55-42ba-8cc9-12cf055168b8'
    ownerAppId: 'bfac7f6e-9fa0-4664-8492-c7a1dfe73d5e'
  }
]
```
(`modules/controlplane-worker-app-service.bicep` flattens the array into the indexed app settings.)

**Deploy — control-plane Bicep FIRST, then Worker code**:
```powershell
az deployment sub create `
  --location westus2 `
  --template-file infrastructure/bicep/platform-controlplane.bicep `
  --parameters infrastructure/bicep/parameters/platform-controlplane-{env}.bicepparam

./scripts/provisioning/Deploy-ControlPlane.ps1 -Target Worker   # code only
```
Deploy the Bicep first and the matching Worker code straight after: a Worker validates its configuration at startup
(e.g. `ControlPlaneIdentity__PrincipalObjectId` since T249), so an older Worker may not start against newer settings
and vice versa. The Worker is down between the two steps; queued Service Bus messages wait.

An empty `speContainerTypeOwners` is valid at boot — H0 then rejects every run with `spe-owner-not-configured`.

---

### Step 7 — Set `containerTypeId` in `spaarke-constants.yaml`

**Edit** `scripts/provisioning-prereqs/spaarke-constants.yaml`:
```yaml
per_env_constants:
  dev:
    containerTypeId: fb3817a8-5a55-42ba-8cc9-12cf055168b8
```
It must match the bicepparam entry from Step 6. `bffApiAppId` in the same block predates D-13 (per-customer BFF app
registrations); restructuring this file per model is T227 (G2) — do not populate it with a shared BFF app.

---

### Step 8 — Verify

**End-to-end proof — a provisioning run's H0.** Dispatch a run that uses this container type (`/provision-environment`).
H0's `SpeOwnerCredential` check signs in as the owning app through the Worker UAMI's federated credential and GETs the
container type's registration. A pass proves Steps 1–7 together. The three ways it can fail (all **Resumable** — fix
the cause, then resume the run):

| Rejection code | Meaning | Fix |
|---|---|---|
| `spe-owner-not-configured` | The run has no `containerTypeId`, or the Worker has no `SpeContainerOptions` owner entry for it | Step 6 / Step 7, and the intake's `containerTypeId` |
| `spe-owner-token-failed` | The token exchange through the FIC failed, or Graph refused the owning-app token with 401/403 (for example missing consent) | Step 3 (issuer / subject / audience) and Step 2 (consent) |
| `spe-container-type-not-registered` | The registration GET returned 404 | Step 5 |

There is no 24 h age gate; the old `SpeCertBootstrap` check and its `spe-cert-bootstrap-missing` code no longer exist.

**Without dispatching a run**: confirm the container type, its owning app and billing in the SharePoint admin center
(Step 4), the consent in Entra (Step 2) and the federated credential with
`az ad app federated-credential list --id <owningAppId>` (Step 3).

---

### Step 9 — BFF app registrations and their container-type grants (per customer — not done here)

**What**: a customer's BFF reaches its containers through a **grant on the container-type registration**, not by
owning anything (topology doc §3A "How a BFF gets container access without owning anything").

- The **BFF app registration is per customer** (D-13) and is created by **H3** during that customer's provisioning
  run: single-tenant, **no client secret**, with its own federated credential `spaarke-uami-trust` trusting the
  customer's BFF UAMI. This runbook does not create one. The former shared `Spaarke BFF - Trial 1` /
  `Spaarke BFF - Model 1` registrations are retired (inventory doc, "Retired" section).
- The **grant** for each customer's BFF app on the registration is per customer. **No handler owns it yet — T227 (G9).**
  When it is done (by a handler or manually), use the v1.0 per-app grant API, as the owning app:
  ```http
  PUT https://graph.microsoft.com/v1.0/storage/fileStorage/containerTypeRegistrations/{containerTypeId}/applicationPermissionGrants/{bffAppId}
  Authorization: Bearer <owning-app token>
  Content-Type: application/json

  {
    "delegatedPermissions": ["full"],
    "applicationPermissions": ["full"]
  }
  ```
  `201 Created` on success. Do not put `appId` in the body (it is in the URL). `PATCH` on the same URL updates an
  existing grant; `DELETE` removes it; `GET .../applicationPermissionGrants` lists them. Grants can take up to one
  hour to propagate. Unlike the whole-registration PUT in Step 5, this touches only that app's grant. Choose the
  permission set the BFF actually needs; `full` mirrors the owning app. Source:
  [Learn — Create fileStorageContainerTypeAppPermissionGrant](https://learn.microsoft.com/en-us/graph/api/filestoragecontainertyperegistration-post-applicationpermissiongrants?view=graph-rest-1.0).
- The BFF's own SPE admin surface (`SpeAdminGraphService`) still signs in as owning apps with Key Vault client secrets
  (ADR-028 exception E-1); moving it to MI-FIC is **T250**. Until T250 there is no secret-based
  `sprk_specontainertypeconfig` row for `Spaarke Model 1`.

---

## What happens next per customer (NOT part of this runbook)

Per topology doc §6: each customer gets its own **container** inside the shared container type, created by handler
**H8** during `/provision-environment {customerId}`.

**H8 acts as the container type's owning app** — app-only, through the Worker UAMI's federated credential — to create
the container, activate it and verify it. It does **not** use the customer's BFF app or the BFF's UAMI. **H13**'s T6
trap then lists `GET /storage/fileStorage/containers?$filter=containerTypeId eq {id}` as the owning app and passes only
if the run's container (H8's output) is in the list.

---

## Anti-patterns to avoid

- ❌ **Do NOT add a client secret or certificate to the owning app** — not when the SharePoint admin center asks, not
  for scripts. The owning app's only credential is the federated credential (Step 3).
- ❌ **Do NOT create a container type per customer.** Every `standard` container type permanently consumes 1 of the
  25-cap (topology doc R2 + R3).
- ❌ **Do NOT merge the owning app with a BFF app registration** (topology doc §3A). `170c98e1… = SDAP-PCF-CLIENT` is
  the merged shape to unwind, not the pattern.
- ❌ **Do NOT use `scripts/Create-NewContainerType.ps1`** — deprecated (task 213.3), app-only 403.
- ❌ **Do NOT delete the billing resource group or the `Microsoft.Syntex` account** of a `standard` container type.
- ❌ **Do NOT re-PUT the whole registration to add one grant** — it replaces the grants list. Use the per-app grant API.
- ❌ **Do NOT re-run this runbook per customer.** It runs once per container type.

---

## Escalation triggers

- Container-type creation in the admin center fails for any reason other than a `SubscriptionNotRegistered` retry.
- Billing setup fails with `SubscriptionNotRegistered` for more than 30 minutes — check the `Microsoft.Syntex`
  provider registration on the subscription.
- H0 keeps returning `spe-owner-token-failed` after Steps 2 and 3 are confirmed — compare the FIC subject with the
  Worker UAMI's **principal** id and the issuer's tenant id before changing anything else.
- `spe-container-type-not-registered` persists after the admin center shows the container type.

---

## Related documents

- **[SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md](../architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md)** — authoritative topology (R1–R5, §3, §3A, §7)
- **[SPAARKE-ENVIRONMENT-RESOURCE-INVENTORY.md](../architecture/SPAARKE-ENVIRONMENT-RESOURCE-INVENTORY.md)** — every resource per environment, including the SPE rows
- **[ADR-028-spaarke-auth-architecture.md](../../.claude/adr/ADR-028-spaarke-auth-architecture.md)** — A4 (MI-FIC default for confidential clients), E-1
- **[SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md](./SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md)** — customer-provisioning operator guide (references THIS runbook as a prereq)
- **[projects/customer-provisioning-orchestration-r1/tasks/248-spe-owning-app-mi-fic.poml](../../projects/customer-provisioning-orchestration-r1/tasks/248-spe-owning-app-mi-fic.poml)** — task authority for the MI-FIC rewrite
- **[projects/customer-provisioning-orchestration-r1/tasks/213-spe-topology-reconciliation-plus-h8-rework.poml](../../projects/customer-provisioning-orchestration-r1/tasks/213-spe-topology-reconciliation-plus-h8-rework.poml)** — original task authority for this runbook
