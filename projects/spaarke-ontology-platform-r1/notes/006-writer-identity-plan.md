# Task 006: dedicated writer identity, the exact plan (step 1)

> **Date**: 2026-10-03 · **Owner approval**: 2026-10-03, "Proceed as proposed" (task 002 escalation, option A)
> **Status**: approved; this note records the plan and the pre-change facts it rests on.

## The plan

| # | Change | Exact values |
|---|---|---|
| 2a | Create user-assigned managed identity | `mi-ontology-writer-dev`, resource group `rg-spaarke-dev`, region `westus2` |
| 2b | Attach it to the BFF App Service | `spaarke-bff-dev` (rg `rg-spaarke-dev`). **Additive**: `mi-bff-api-dev` stays attached and stays the identity every existing credential uses |
| 3a | Register a Dataverse application user | `spaarkedev1`, `applicationid` = the new UAMI's **client id**, business unit = root `Spaarke` (`06fbf21c-1872-f011-b4cb-7c1e52671ad0`) |
| 3b | Assign exactly one role | `Spaarke Ontology Service`, root-BU copy `b1fb7ee0-bfbe-f111-aaaf-0022482913fc` |
| 4 | Re-run the task 002 union check for the new principal | Create on `sprk_signal` + `sprk_decisionrecord`, Assign on `sprk_signal`, **no** Write/Delete on `sprk_decisionrecord` |

**Not done, by constraint**: no client secret, no app registration, no Key Vault secret; `BFF-API-ClientSecret`
stays deleted; System Administrator is **not** removed from the existing BFF identities.

## How the evaluator gets a Dataverse token as this identity (for task 030)

App-only, managed identity, **pinned by client id** (ADR-028 A4 "App-only" row: `TokenCredential` pinned to the
UAMI). In the BFF:

```csharp
new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(<writer client id>))
    .GetTokenAsync(new TokenRequestContext(new[] { "https://spaarkedev1.crm.dynamics.com/.default" }), ct);
```

The client id is read from a **new, separate** config key (task 030 names it; suggested
`Ontology:Writer:ManagedIdentityClientId`). It must **not** reuse `ManagedIdentity:ClientId` /
`Graph:ManagedIdentity:ClientId`; those are the sysadmin identity's keys. **Fail closed**: if the key is empty,
the writer refuses to write rather than falling back to the shared Dataverse client (which is System
Administrator). That fallback is exactly what this task exists to prevent.

No OBO, no confidential client and no FIC are involved: this is a plain app-only managed-identity token, which
Dataverse accepts for an application user registered against the managed identity's client id.

## Facts verified before any change (2026-10-03, read-only)

| Fact | Evidence | Why it matters |
|---|---|---|
| `spaarke-bff-dev` has **one** identity: user-assigned `mi-bff-api-dev` (client `5967251e-…`), no system-assigned | `az webapp identity show` | With a second UAMI attached, an **unpinned** credential could no longer resolve an identity |
| Every BFF credential site pins its client id from `ManagedIdentity:ClientId` or `Graph:ManagedIdentity:ClientId` | `ManagedIdentityCredentialFactory`, `ManagedIdentityAssertionProvider`, `GraphClientFactory`, `DataverseAccessDataSource`, `DataverseWebApiService`, `DataverseWebApiClient`, `DataverseServiceClientImpl` | So existing token acquisition does not change when a second identity is attached |
| Both keys are set on the app to `5967251e-…`; `AZURE_CLIENT_ID` is not set | `az webapp config appsettings list` | The pinning above is live, not just in code |
| Key Vault references resolve as `mi-bff-api-dev` | `keyVaultReferenceIdentity` on the app | KV-referenced settings are unaffected by a second identity |
| No deployment slots | `az webapp deployment slot list` | One app to attach and verify |
| `mi-bff-api-dev` lives in `spe-infrastructure-westus2`, not `rg-spaarke-dev` | `az webapp identity show` | The approved plan places the new identity next to its **App Service** (`rg-spaarke-dev`). Both RGs are `westus2`. Recorded so a reader does not look for it beside `mi-bff-api-dev` |

**Expected side effect**: changing an App Service's identities restarts the app; the dev BFF is briefly
unavailable. Verified after the attach by `/healthz` and by the existing identity still being attached.
