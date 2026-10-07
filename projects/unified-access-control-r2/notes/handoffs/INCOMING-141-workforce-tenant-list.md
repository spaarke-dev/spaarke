# INCOMING to `customer-provisioning-orchestration-r1` — the customer workforce tenant list (UAC-r2 task 141)

> **From**: `unified-access-control-r2` task 141 (2026-10-01), owner decision round 3 **I1 = (b)**
> (`session27-owner-decisions-and-research.md`).
> **To**: `customer-provisioning-orchestration-r1` — provisioning WRITES this value. UAC-r2 did **not** edit any
> provisioning code, manifest or handler; everything below is a request.
> **BFF side (done on `task/uac-r2-141`)**: `WorkforceIdentityOptions` + `WorkforceIdentityOptionsValidator`
> (`src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/WorkforceIdentityOptions.cs`), registered with
> `ValidateOnStart` in `ExternalAccessModule`; the member test `WorkforceMembershipTest.Evaluate`.

## 1. The setting

| | |
|---|---|
| **Configuration key** | `WorkforceIdentity:CustomerTenantIds` |
| **App Service app-setting names** | `WorkforceIdentity__CustomerTenantIds__0`, `WorkforceIdentity__CustomerTenantIds__1`, … (one per tenant; indices contiguous from 0) |
| **Shape** | a string array of Microsoft Entra **tenant ids** (GUIDs) |
| **Value** | the Entra tenant id(s) of the CUSTOMER whose employees use this stamp (the `tid` in their workforce tokens) |
| **Secret?** | No. A public identifier — a per-env literal, not a Key Vault reference |
| **Where** | the BFF App Service of the stamp, **both slots** (same value; it is not slot-specific) |

## 2. Semantics

- A Type-2 caller (customer employee, no Power Apps licence) gets a first-sign-in **email bind or contact
  creation** only when their token is a user token, their `tid` is in this list, AND their `acct` claim is `0`
  (member).
- **Empty or absent = DENY** every first-sign-in email bind and every contact creation, for everyone. The caller
  gets `sdap.access.deny.workforce_tenant_list_empty`. People already bound by oid still resolve; licensed
  systemusers still authorize through Dataverse. Nothing fails at startup for an empty list — it fails closed at
  sign-in, which is why provisioning must write it.
- **Never** a fallback to `AzureAd:TenantId`. **Not** `TenantRouting:Tenants[]` (that map is not configured in
  `appsettings.template.json` and is a routing concern, not an identity one).
- **Startup validation** (`ValidateOnStart`, ADR-010): a non-GUID, the all-zero GUID, or the CIAM tenant id
  (`Ciam:TenantId`) **fails startup**. A stamp with a bad value does not come up — catch it in the handler's own
  validation before the App Service setting is written.

## 3. Which value, per model (D-13)

| Model | `AzureAd:TenantId` (the BFF registration's tenant) | `WorkforceIdentity:CustomerTenantIds` |
|---|---|---|
| **Model 2** (dedicated, registration in the customer's tenant) | the customer's tenant | the customer's tenant — the same GUID. **Write it anyway**; nothing is inferred. The H0.5 consent callback already captures this `tid`. |
| **Model 1** (registration in SPAARKE's tenant) | Spaarke's tenant | the **customer's** tenant — a DIFFERENT GUID. It is NOT the `-TenantId` run parameter. It must be collected as its own intake value. |

🔴 Model 1 is why this is its own setting: keyed on `AzureAd:TenantId`, the member test would refuse every Model-1
customer employee AND auto-bind Spaarke's own staff into the customer's environment.

A customer with more than one workforce tenant (e.g. after an acquisition) lists each; order is irrelevant.

## 4. Requested provisioning changes (for `customer-provisioning-orchestration-r1` to own)

1. **Intake**: a `customerWorkforceTenantIds` run parameter (array of GUIDs). Model 2 may default it from the
   H0.5 consent-captured `tid`; Model 1 requires it explicitly (I1 explicit-tenant invariant). Refuse the run when
   a value equals the CIAM tenant, is the all-zero GUID, or does not parse.
2. **Manifest**: a `per_env_settings` entry in `scripts/canonical-secret-catalog/manifest.yaml` (H4b
   bulk-app-settings) — key `WorkforceIdentity__CustomerTenantIds__0` (and further indices when more than one),
   `per_env_source: from-h0-parameter:customer_workforce_tenant_id` (or the H0.5 output for Model 2),
   `iOptionsModule: ExternalAccessModule (WorkforceIdentityOptions)`. Suggest `required: true` for any stamp that
   serves Teams/SPA users — "absent" is legal for the BFF but silently denies every Type-2 first sign-in.
3. **Validation (H13)**: assert the setting is present on both slots and parses; optionally assert it does not
   equal `AzureAd__TenantId` for a Model-1 stamp (that would mean Spaarke's tenant was written by mistake).
4. **Registry**: record the value(s) on the stamp's `sprk_dataverseenvironment` row if the registry carries
   per-customer identity facts (provisioning's call).

## 5. Two neighbours that ship with the same task (not this handoff's ask, listed so nothing is missed)

- **`acct` optional claim** on every per-customer BFF registration — `scripts/Register-EntraAppRegistrations.ps1`
  now adds it in Step 1 for a NEW registration (H3 runs that script); an existing one needs
  `-AcctClaimOnly -AcctClaimAppId <appId>`. Without it every Type-2 first sign-in is denied
  `workforce_acct_claim_missing`.
- **Dataverse schema** — `scripts/Set-ContactIdentityBindingSchema.ps1 -Apply` then `-Verify` must run BEFORE a BFF
  carrying task 141 is deployed (it selects the new columns; without them CIAM and Type-2 sign-ins fail closed with
  `binding_column_missing`). A natural home is after H6 (solution import) and before H9 (BFF deploy), or folding the
  components into the SpaarkeCore managed solution once they are exported. **Decided 2026-10-01 (UAC-r2 owner
  round 4 item 4, "B2") — safe to wire**: field-level security stays on `sprk_externalobjectid`, and the alternate
  key moves to a new unsecured mirror column `sprk_externalobjectidkey` (the script creates it, copies existing
  bindings into it, then creates the key). Run it in the stamp's own environment AND in every environment the stamp's
  BFF provisions users into (`DATAVERSE_URL` and each active `sprk_dataverseenvironment` row): the identity-link job
  reconciles all of them and reports any environment without the schema as failed.
- **Job switch** `IdentityLink__Reconciliation__WritesEnabled` — absent = report-only. It also gates the inline
  link a licensed user would get at first sign-in. A new stamp should run one report-only cycle, then set `true`.

Authoritative operator text: `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §6.5.2 / §6.5.3 / §7.3.
