# Spaarke SPE Topology Setup Runbook

> **Authored**: 2026-08-30 by customer-provisioning-orchestration-r1 task 213.5
> **Authority**: [SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md](../architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md) (owner-attested authoritative 2026-08-30)
> **Audience**: operator standing up a new Spaarke SPE tier (Trial / Model 1 / Model 2) for the first time
> **Runs**: ONCE per (env × model) — NOT per customer

---

## What this runbook does

Executes the one-time setup that must be in place BEFORE the first customer of a given tier can be provisioned. Per topology doc §3, Spaarke's SPE topology allocates **4 container-types total** across the org (PAYGO 1 dev / Trial 1 / Model 1 / Model 2), each serving unlimited customers via containers inside. This runbook creates ONE of those container-types + its owning app-reg + the tier's shared BFF app-reg.

**Concretely, for the first trial dispatch (`customerId=trial1`)** this runbook creates:
- **`Spaarke SPE Trial 1 Owner`** app-reg (Entra, single-tenant `AzureADMyOrg` — the container-type owner)
- **`Spaarke Trial 1`** SPE container-type (classification `standard`, owned by ↑, containers live in Spaarke tenant)
- **`Spaarke BFF — Trial 1`** app-reg (Entra, single-tenant — the API-caller identity per topology doc §3A row 4)

After this runbook completes, ALL future trial prospects (trial2, trial3, ...) reuse the same container-type + BFF app-reg. Only their individual `container` inside the container-type is created per-customer (via H8 handler at dispatch time).

---

## Prerequisites BEFORE running

- Operator has **owning-tenant admin** OR the ability to register Entra app-regs + create SPE container-types in the Spaarke tenant.
- Operator has one of the following delegated flow options available (topology doc §R5 — container-type CREATE is delegated-only, app-only receives 403):
  - **SPE Admin app** — `SPAARKE-SPE-Admin-CLI` (appId `68cf5a14-1efb-4254-80bf-2761ffc89373`) already registered in the tenant. This app is designed to perform the delegated exchange.
  - **SharePoint Embedded VS Code extension** — Microsoft-published, uses your Entra login for delegated flow.
  - **SharePoint admin center** — `https://<tenant>-admin.sharepoint.com` → Advanced → Containers.
- Azure subscription + resource group in the owning tenant, with owner/contributor rights (for `standard` billing profile attachment at step 4).
- `az` CLI logged in as the operator's OWN AAD identity (NEVER a service principal — per NFR-11).

---

## The 8-step setup

### Step 1 — Register the owning app-reg (Entra, single-tenant)

**What**: Create the immutable-1:1 owning app-reg per topology doc §R1 (permanent binding to the container-type you create at step 3).

**Command** (script landed 2026-08-30 per task 213.4 — includes `-TenantId` requirement per FR-28 / I1):
```powershell
# From repo root — TenantId is mandatory (never a hardcoded default)
./scripts/Register-EntraAppRegistrations.ps1 `
  -TenantId $env:AZURE_TENANT_ID `
  -CreateOwningApp Trial1
```

The script is idempotent (safe to re-run): existing app-regs detected by display name are skipped with a warning; only new ones are created. It auto-adds Graph `FileStorageContainer.Selected` (Application). It emits an operator-actionable warning for `FileStorageContainerTypeReg.Selected` (which lives on a non-Graph API surface in some tenants and requires the manual portal step below).

**Manual fallback** (only if the script fails for a tenant-specific reason):
1. Portal → Entra ID → App registrations → New registration
2. Name: `Spaarke SPE Trial 1 Owner`
3. Supported account types: **Accounts in this organizational directory only (Single tenant — AzureADMyOrg)**
4. Redirect URI: (leave blank)
5. Save. Capture the **Application (client) ID** GUID.
6. API permissions → Add:
   - Microsoft Graph → **Application permissions** → `FileStorageContainer.Selected`
   - Microsoft Graph → **Application permissions** → `FileStorageContainerTypeReg.Selected`
7. Grant admin consent for the tenant (Portal Blue button).

**Note (topology doc §3A row 3)**: for `Spaarke SPE Model 2 Owner` — and ONLY for that owning app — supported account types is **`AzureADMultipleOrgs`** (multi-tenant). This is because Model 2 customers must grant admin consent to the owning app in THEIR tenant. Trial 1 + Model 1 owning apps are single-tenant because their container-types host containers in the Spaarke tenant only.

**Output**: `owningAppId` GUID — record it for step 3.

---

### Step 2 — Grant admin consent on the owning app-reg (Portal, one click)

**What**: Grant tenant-wide admin consent for the two API permissions from step 1.

**Command**:
Portal navigation:
```
Portal → Entra ID → App registrations → Spaarke SPE Trial 1 Owner
  → API permissions
  → "Grant admin consent for <tenant>" (blue button)
```

**Verify** — status column MUST show green ✅ "Granted for <tenant>" on both permissions.

**Time**: ~1 minute.

---

### Step 3 — Create the container-type via DELEGATED flow

**What**: `POST /storage/fileStorage/containerTypes` — the actual container-type creation. Per topology doc §R5 this MUST be delegated; app-only returns 403 by design.

**Recommended path — SPE Admin app**:
Use the `SPAARKE-SPE-Admin-CLI` app to perform the delegated exchange. It's already registered in the tenant.

**Alternative — SharePoint Embedded VS Code extension**:
1. Install the "SharePoint Embedded" extension in VS Code.
2. Sign in with your Entra identity when prompted.
3. Choose "Create container type" → enter:
   - Name: `Spaarke Trial 1`
   - Owning app ID: (paste from step 1)
   - Billing classification: `standard`

**Alternative — SharePoint admin center**:
```
https://<tenant>-admin.sharepoint.com → Advanced → Containers → Container types → New
  → Name: Spaarke Trial 1
  → Owning app: Spaarke SPE Trial 1 Owner (from step 1)
  → Billing classification: standard
```

**Raw Graph call** (only if you're building your own delegated tool — the three above are the operator-supported paths):
```http
POST https://graph.microsoft.com/beta/storage/fileStorage/containerTypes
Authorization: Bearer <DELEGATED user token, NOT client_credentials>
Content-Type: application/json

{
  "name": "Spaarke Trial 1",
  "owningAppId": "<owningAppId from step 1>",
  "billingClassification": "standard"
}
```

**⚠️ DO NOT** attempt this via `scripts/Create-NewContainerType.ps1` — that script is DEPRECATED per task 213.3 (uses app-only, returns 403 by design). Runtime `throw` will block invocation.

**Output**: `containerTypeId` GUID — record it for step 6.

---

### Step 4 — Attach the Azure billing profile (`standard` classification only)

**What**: Wire the container-type's billing to a Spaarke-owned Azure subscription so pay-as-you-go metering resolves.

**Steps**:
1. In SharePoint admin center → Container types → `Spaarke Trial 1` → Billing.
2. Choose "Attach billing profile".
3. Select the Spaarke Azure subscription (e.g., dev sub).
4. Select the resource group where you want the billing meter to accrue.

**Common failure**: `SubscriptionNotRegistered` — the `Microsoft.Syntex` resource provider registration is slow to propagate. Wait a few minutes + retry.

**Verify**: billing profile status shows "Active" in the container-type detail page.

**Time**: 2-5 minutes.

**Note (`directToCustomer` classification only — Model 2 tier)**: skip step 4 entirely. The CUSTOMER activates pay-as-you-go in THEIR M365 admin center after step 5 registration → **Setup → Billing and licenses → Activate pay-as-you-go services → Apps → SharePoint Embedded** (topology doc §5.4). Don't attempt to attach Spaarke-side billing to a `directToCustomer` container-type.

---

### Step 5 — Wait for replication

**What**: The newly-created container-type must replicate across Microsoft's SPE infrastructure before it's queryable via Graph.

**Microsoft's stated SLO**: up to 24 hours.

**Empirical practice** (per operator memory `feedback_spe_container_timing`, 2026-08-22 Model 1 Prod stand-up): container-type replication completes in ~2 minutes.

**Verify readiness** (poll every 30-60s until non-404):
```bash
# Acquire delegated token first
token=$(az account get-access-token --resource https://graph.microsoft.com --query accessToken -o tsv)

# GET the container-type until 200
curl -sI -H "Authorization: Bearer $token" \
  "https://graph.microsoft.com/v1.0/storage/fileStorage/containerTypes/<containerTypeId>"
# Expect: HTTP/2 200 (initially: HTTP/2 404 during replication window)
```

If still 404 after 30 minutes, escalate — something is wrong with the container-type creation.

---

### Step 6 — Register the BFF app-reg for this tier (Entra, single-tenant)

**What**: Create the separate BFF app-reg per topology doc §3A row 4 (`Spaarke BFF — Trial 1`). **MUST BE SEPARATE** from the owning app (§3A "The BFF app registration MUST be separate from the owning app" + owner directive 2026-08-30 Q4 "create NEW Spaarke BFF — Trial 1; do not reuse dev").

**Command** (script landed 2026-08-30 per task 213.4):
```powershell
# Trial 1 or Model 1 (shared BFF app-reg per tier):
./scripts/Register-EntraAppRegistrations.ps1 `
  -TenantId $env:AZURE_TENANT_ID `
  -CreateBffApp Trial1

# Model 2 per-customer BFF app-reg (topology doc §3A row 6):
./scripts/Register-EntraAppRegistrations.ps1 `
  -TenantId $env:AZURE_TENANT_ID `
  -CreateBffApp Model2 `
  -CustomerName Acme
```

The script creates the BFF app-reg with:
- `signInAudience=AzureADMyOrg` (single-tenant, per project CLAUDE.md § MUST rule — BFF apps are ALWAYS single-tenant)
- Graph delegated permissions: `Files.ReadWrite.All`, `Sites.ReadWrite.All`, `User.Read`, `Mail.Send` (mirrors the reference `spaarke-bff-api-prod` app-reg)
- Dynamics CRM delegated: `user_impersonation`
- Application ID URI: `api://{appId}` + exposed `user_impersonation` scope
- Service principal
- **NO client secret** (topology BFF apps are secret-free per ADR-028 A4 + KV credential-lifecycle rule 1). FIC is added separately in Step 8 below.
- **NO Key Vault writes** (that is the per-customer H4 handler's job at provisioning time).
- **NO redirect URIs** (BFF is a confidential-client using FIC/MI to mint tokens; no OAuth code flow needed).

**Manual fallback** (only if the script fails for a tenant-specific reason):
1. Portal → Entra ID → App registrations → New registration
2. Name: `Spaarke BFF - Trial 1` (or per-tier equivalent; use ASCII hyphen to match script's display name convention)
3. Supported account types: **Accounts in this organizational directory only (Single tenant)** — per topology doc §3A rows 4-6 (BFFs are always single-tenant; only Model 2 OWNING app is multi-tenant, row 3).
4. Redirect URI: (leave blank — BFF is secret-free confidential-client, no OAuth code flow)
5. Save. Capture the **Application (client) ID** GUID.
6. Configure API permissions per the reference BFF app-reg (`SDAP-BFF-SPE-API` = `1e40baad-e065-4aea-a8d4-4b7ab273458c`).

**Output**: `bffApiAppId` GUID — record it for step 6.

**Register the BFF app on the container-type registration** (topology doc §3A "How a BFF gets container access without owning anything"):

```http
POST https://graph.microsoft.com/beta/storage/fileStorage/containerTypeRegistrations/<containerTypeId>/applicationPermissionGrants
Authorization: Bearer <DELEGATED or app-only Graph token>
Content-Type: application/json

{
  "appId": "<bffApiAppId from step 6>",
  "applicationPermissions": ["Full"],
  "delegatedPermissions": ["Full"]
}
```

This gives the BFF app-reg full access to create + read + write containers of type `Spaarke Trial 1` — WITHOUT making it the owner (topology doc §3A "How a BFF gets container access without owning anything — VERIFIED").

---

### Step 7 — Populate `spaarke-constants.yaml` with real GUIDs

**What**: Fill in the two `null` values in `scripts/provisioning-prereqs/spaarke-constants.yaml per_env_constants.dev.*` with the GUIDs from steps 3 + 6.

**Edit**:
```yaml
per_env_constants:
  dev:
    containerTypeId: "<GUID from step 3>"      # was: null
    bffApiAppId: "<GUID from step 6>"          # was: null
    bffProdBase: https://spaarke-bff-trial1.azurewebsites.net   # or agreed alternative
```

**Commit**:
```bash
git add scripts/provisioning-prereqs/spaarke-constants.yaml
git commit -m "task 213.7: populate SPE topology constants for Trial 1 tier"
```

---

### Step 8 — Smoke-verify via delegated Graph probe

**What**: Confirm the container-type actually exists + is queryable + the response body matches what we recorded in constants.

```bash
token=$(az account get-access-token --resource https://graph.microsoft.com --query accessToken -o tsv)
containerTypeId="<GUID from step 3>"

# 1. Container-type exists + is queryable
result=$(curl -sf -H "Authorization: Bearer $token" \
  "https://graph.microsoft.com/v1.0/storage/fileStorage/containerTypes/$containerTypeId")
echo "$result" | jq -e ".id == \"$containerTypeId\"" > /dev/null \
  || { echo "❌ Container-type verify failed"; exit 1; }
echo "✅ Container-type $containerTypeId exists"

# 2. Container-type registration in owning tenant exists
curl -sf -H "Authorization: Bearer $token" \
  "https://graph.microsoft.com/beta/storage/fileStorage/containerTypes/$containerTypeId/registrations" \
  | jq -e ".value | length >= 1" > /dev/null \
  || { echo "❌ No registrations found for container-type"; exit 1; }
echo "✅ Container-type has at least one registration"

# 3. BFF app-reg is granted on the registration
bffApiAppId="<GUID from step 6>"
curl -sf -H "Authorization: Bearer $token" \
  "https://graph.microsoft.com/beta/storage/fileStorage/containerTypes/$containerTypeId/registrations" \
  | jq -e ".value[].applicationPermissionGrants[] | select(.appId == \"$bffApiAppId\")" > /dev/null \
  || { echo "❌ BFF app-reg $bffApiAppId not granted on the container-type registration"; exit 1; }
echo "✅ BFF app-reg $bffApiAppId is granted on the container-type"
```

**If all three checks PASS**: the topology is set up correctly. `/provision-environment trial1 --batch runs/trial1-intake.json` should now progress past SKILL Step 0.5c topology-verify (added by task 213.6).

---

## What happens next per customer (NOT part of this runbook)

Per topology doc §6 + task 213.2 (H8 rework): each trial customer (trial1, trial2, ...) gets its own **container** created inside the shared `Spaarke Trial 1` container-type. The container is created by the L2 handler H8 during `/provision-environment {customerId}` dispatch — NOT by this runbook.

**Container creation is app-only-capable** (unlike container-type creation which is delegated-only). H8 uses the BFF app-reg's UAMI to make the `POST /storage/fileStorage/containers` + activate calls.

---

## Anti-patterns to avoid

- ❌ **Do NOT create a container-type per customer.** Every `standard` container-type permanently consumes 1 of the 25-cap (topology doc §R2 + §R3). 25 customers = wall + no way to reclaim.
- ❌ **Do NOT merge the owning app-reg with the BFF app-reg.** Topology doc §3A ("The BFF app registration MUST be separate from the owning app"). The existing `170c98e1... = SDAP-PCF-CLIENT` merged shape is the anti-pattern to unwind, not the pattern.
- ❌ **Do NOT use `scripts/Create-NewContainerType.ps1`.** DEPRECATED (task 213.3) — app-only 403. Use the delegated flows in step 3.
- ❌ **Do NOT re-run this runbook for trial2, trial3, ...**. It's ONCE per tier. Reuse the same container-type + BFF app-reg for all customers of the tier.
- ❌ **Do NOT skip step 5's replication wait.** A subsequent H8 container-creation against a not-yet-replicated container-type returns 404 or worse — flaky failure mode.

---

## Escalation triggers

- Container-type creation via SPE Admin app fails with anything OTHER than the expected 403-from-app-only (which we're deliberately not doing). Escalate rather than force.
- Billing profile attachment (step 4) fails with `SubscriptionNotRegistered` for >30 minutes. Check `Microsoft.Syntex` provider registration state on the subscription.
- Container-type verification (step 8) fails after 30-minute replication wait. The container-type is either lost or in a bad state — escalate.

---

## Related documents

- **[SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md](../architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md)** — authoritative topology (§R1-R5, §3, §3A, §7)
- **[ADR-028-spaarke-auth-architecture.md](../../.claude/adr/ADR-028-spaarke-auth-architecture.md)** — auth-v2 (line 239 RESOLVED note references this topology)
- **[SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md](./SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md)** — customer-provisioning operator runbook (references THIS runbook as a prereq)
- **[projects/customer-provisioning-orchestration-r1/tasks/213-spe-topology-reconciliation-plus-h8-rework.poml](../../projects/customer-provisioning-orchestration-r1/tasks/213-spe-topology-reconciliation-plus-h8-rework.poml)** — task authority for this runbook
