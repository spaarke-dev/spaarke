# How to confirm Dataverse is actually using Managed Identity — live evidence

> Captured 2026-08-24/25 in answer to: *"on the Dataverse side how do we confirm that it is using MI —
> and not just the fallback?"* This is **positive evidence of actual use**, independent of the
> no-fallback-exists argument.

---

## 0. The subtlety that has to come first

There are **two different identities** and **two different mechanisms**, and only one of them is visible
from Dataverse:

| Identity | GUID | Used by | Dataverse sees it? |
|---|---|---|---|
| **UAMI** `mi-bff-api-dev` | clientId `5967251e-…`, principalId `9fd47efb-…` | app-only Dataverse writes — the MI **is** the principal | ✅ **Yes** — as application user `# mi-bff-api-dev` |
| **App registration** `SDAP-BFF-SPE-API` | appId `1e40baad-…`, SP objectId `d93c832e-…` | OBO (delegated), where the MI is only the **credential** via MI-FIC | ❌ **No** — the token's `appid` is the app registration whether the credential is a secret or a federated assertion |

**This matters.** For MI-FIC the application identity is unchanged by design — only the *credential*
changes. So Dataverse alone can never prove the OBO half. That half is proven in Entra (§2).

---

## 1. Dataverse-side proof — the `createdby` audit field

Dataverse stamps `createdby` with the identity that actually performed the write. Join `systemuser` to
read it:

```sql
SELECT TOP 8 d.sprk_documentid, d.createdon, u.fullname, u.applicationid
FROM sprk_document d JOIN systemuser u ON d.createdby = u.systemuserid
ORDER BY d.createdon DESC
```

### The two application users

```
fullname            applicationid                          azureactivedirectoryobjectid          isdisabled
SDAP-BFF-SPE-API    1e40baad-e065-4aea-a8d4-4b7ab273458c   d93c832e-9b1d-4ccc-a2a8-9419fbf3fc18  false
# mi-bff-api-dev    5967251e-171c-46fe-a6c2-ef843c90309d   9fd47efb-7962-492b-ac44-e5ccd0268ebb  false
```

`# mi-bff-api-dev`'s `azureactivedirectoryobjectid` is **`9fd47efb-…` — byte-identical to the UAMI's
principalId** from `az webapp identity show`. That is what ties the Dataverse row to the Azure resource;
the `#` prefix is Dataverse's own convention for a managed-identity application user.

### The cutover is visible in the data

`sprk_communication`, newest first:

| createdon | createdby | applicationid |
|---|---|---|
| 2026-08-24 21:32 | `# mi-bff-api-dev` | `5967251e-…` ← **Outlook UAT** |
| 2026-08-24 21:22 | `# mi-bff-api-dev` | `5967251e-…` ← **Outlook UAT** |
| 2026-08-24 09:53 | `# mi-bff-api-dev` | `5967251e-…` |
| 2026-08-19 16:03 | `# mi-bff-api-dev` | `5967251e-…` |
| 2026-08-18 15:53 | `# mi-bff-api-dev` | `5967251e-…` |
| 2026-08-17 08:36 | *Ralph Schroeder* | `null` ← a human (OBO impersonation) |
| **2026-08-13 15:03** | **`SDAP-BFF-SPE-API`** | `1e40baad-…` ← **last app-registration write** |
| 2026-08-13 09:58 | `SDAP-BFF-SPE-API` | `1e40baad-…` |

`sprk_document`, newest first — **every row created during today's UAT**:

```
2026-08-24T21:32:27   # mi-bff-api-dev   ← the Word document
2026-08-24T21:27:49   # mi-bff-api-dev
2026-08-24T21:25:06   # mi-bff-api-dev
2026-08-24T20:51:37   # mi-bff-api-dev
```

**This is not configuration that would select MI. It is Dataverse's own audit field on rows written during
the UAT, naming the managed identity as the actor.** `SDAP-BFF-SPE-API` has not written since 2026-08-13.

---

## 2. Entra-side proof — which *credential* was used

`createdby` proves the app-only principal. It cannot prove the OBO credential (§0). Entra sign-in logs can.

### 2a. The MI is live against Dataverse

```
az rest --method get --url "https://graph.microsoft.com/beta/auditLogs/signIns
  ?$filter=signInEventTypes/any(t: t eq 'managedIdentity')
    and servicePrincipalId eq '9fd47efb-7962-492b-ac44-e5ccd0268ebb'"
```

```
createdDateTime        resource                              servicePrincipalName   errorCode
2026-08-25T01:26:45Z   Dataverse                             mi-bff-api-dev         0
2026-08-25T01:22:19Z   Azure Cognitive Search                mi-bff-api-dev         0
2026-08-25T01:22:16Z   AAD Token Exchange Endpoint: Public   mi-bff-api-dev         0
2026-08-25T01:08:36Z   Microsoft Graph                       mi-bff-api-dev         0
2026-08-25T01:08:28Z   Azure Key Vault                       mi-bff-api-dev         0
2026-08-25T01:08:27Z   Microsoft.ServiceBus                  mi-bff-api-dev         0
2026-08-25T01:08:27Z   Dataverse                             mi-bff-api-dev         0
2026-08-25T01:08:26Z   Azure Cosmos DB                       mi-bff-api-dev         0
```

**`AAD Token Exchange Endpoint: Public` is the MI-FIC assertion exchange** — the MI acquiring a token for
`api://AzureADTokenExchange` to present as the confidential client's `client_assertion`. Its presence is
direct evidence that the **federated** path is in live use, not merely configured. `Dataverse` alongside
it, with `errorCode 0`, is the app-only path succeeding.

### 2b. The app registration's secret-based sign-ins STOPPED

```
az rest ... "$filter=signInEventTypes/any(t: t eq 'servicePrincipal')
    and appId eq '1e40baad-e065-4aea-a8d4-4b7ab273458c'
  &$select=createdDateTime,resourceDisplayName,federatedCredentialId,status"
```

```
createdDateTime        resource     federatedCredentialId   errorCode
2026-08-24T14:51:11Z   Dataverse    ""                      0     <- NEWEST. Nothing after this.
2026-08-24T14:27:51Z   Dataverse    ""                      0
2026-08-24T13:27:51Z   Dataverse    ""                      0
2026-08-24T12:39:57Z   Dataverse    ""                      0
```

Two things at once:

1. **`federatedCredentialId` is EMPTY** on all of them — these were **secret**-based sign-ins. The field is
   populated precisely when a federated credential is used, so it is the discriminator.
2. **The newest is 14:51Z on 2026-08-24** — the cutover window. There have been **no** app-only
   service-principal sign-ins from the app registration since, across ~10 hours of live traffic that
   includes the entire UAT.

The old credential path did not merely stop being *preferred*. It stopped being *exercised*.

---

## 3. The third leg — no fallback exists (stated last, deliberately)

Weakest of the three on its own, and the one the operator explicitly asked not to lean on. Included for
completeness:

```
Graph__Credentials__Order__0                  = ManagedIdentityFederated   <- the ONLY entry
Graph__Credentials__RequireSecretFreeIdentity = true                       <- refuses to boot otherwise
```

plus the secret is absent from app settings **and** Key Vault (soft-deleted, recoverable to 2026-11-22).

---

## Summary — independent layers

| Layer | Question it answers | Verdict |
|---|---|---|
| Dataverse `createdby` | *Which identity performed the write?* | `# mi-bff-api-dev` on every UAT row |
| Entra MI sign-ins | *Is the MI actually authenticating — and federating?* | Dataverse + `AAD Token Exchange Endpoint`, errorCode 0, minutes ago |
| Entra app-reg SP sign-ins | *Is the old secret path still used?* | `federatedCredentialId=""` and **none since the cutover** |
| Config | *Could it fall back?* | No entry to fall back to; boot refuses |

Layers 1 and 2 are **positive evidence of actual use**. They do not depend on the absence-of-fallback
argument, and they would still hold if the fallback were restored tomorrow.

---

## Re-running these checks later

Both are read-only and safe to repeat:

1. **Dataverse** — the `createdby` join in §1 (Dataverse MCP `read_query`, or any SQL-4-CDS surface).
2. **Entra** — the two `az rest` calls in §2. Needs `AuditLog.Read.All`; sign-in logs retain 30 days on
   the current licence, so **capture, don't rely on re-querying** for anything older.
