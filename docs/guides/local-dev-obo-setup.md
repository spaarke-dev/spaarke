# Running OBO locally (developer workstation)

> **Created**: 2026-08-25 by `spaarke-auth-v4-dataverse-MI` task 090, closing success criterion #9.
> **Why this exists**: that project removed `BFF-API-ClientSecret` from app settings **and** Key Vault. The
> Key Vault copy was the only **readable** copy of the value developers used locally, so a developer cloning
> the repo today has no working credential for OBO. This guide defines the replacement.

---

## The constraint (read this first — it explains why there is no trivial answer)

A developer workstation **has no route to IMDS**, so a managed identity cannot be minted there and MI-FIC —
the mechanism every deployed environment now uses — **cannot work locally**. `IdentityConfigurationValidator`
exempts `Development` from rule 6 (`RequireSecretFreeIdentity`) for exactly this reason
([`IdentityConfigurationValidator.cs:305`](../../src/server/api/Sprk.Bff.Api/Configuration/IdentityConfigurationValidator.cs#L305)).

OBO also cannot be satisfied by `az login` or `DefaultAzureCredential`. Those produce **app-only** tokens; an
On-Behalf-Of exchange requires the application to authenticate **as itself** with a confidential credential
while presenting the user's token. There is no user-delegated substitute.

And the credential must belong to the app registration **whose audience the incoming user token was issued
for**. You cannot mix: a token minted for app A cannot be exchanged by app B.

Config keys read, in precedence order
([`OrderedCredentialClientProvider.cs:514`](../../src/server/api/Sprk.Bff.Api/Infrastructure/Auth/OrderedCredentialClientProvider.cs#L514)):

```
AzureAd:ClientSecret  →  API_CLIENT_SECRET  →  AZURE_CLIENT_SECRET
```

`Graph:ClientSecret` and `Dataverse:ClientSecret` are **not** read by anything — a retired setup doc told
people to set them, which produced local environments that silently could not authenticate. Do not use them.

---

## The options, honestly

| Option | Verdict |
|---|---|
| **A — Put a secret back on the shared dev app registration** (`SDAP-BFF-SPE-API`) | ❌ **No.** This undoes the project on `spaarkedev1`: the app registration would again hold a secret-shaped credential, and the FR-F1/FR-F2 guards exist to stop precisely that drift. |
| **B — Each developer creates their own app registration** | ✅ Correct and fully isolated, but expensive: the *client* side (PCF / code page / add-in MSAL config) must also target that app registration, because the user token's audience must match. Realistically a half-day per developer. |
| **C — Don't run OBO locally** — run the local BFF for non-OBO paths and point OBO-dependent flows at deployed dev | ✅ Zero setup, and honest about what local can do. Insufficient if you are actively changing OBO code. |
| **D — One shared *local-dev-only* app registration**, separate from every deployed identity, secret held in Key Vault where developers can read it | ✅ **Recommended.** Preserves the property that matters — **no deployed identity holds a secret** — while giving developers a working path. Blast radius is a dev-only app with no production access and no Dataverse application-user rights beyond a dev environment. |

**Recommended: D, with C as the everyday default.** Most local work does not touch OBO; reach for D only when
you are changing the OBO paths themselves.

> Option D is a **deliberate, scoped exception** to the project's zero-secret objective, not a regression of
> it. The objective was that **no deployed BFF identity** authenticates with a secret. A workstation is not a
> deployed identity, cannot use the deployed mechanism, and the app registration involved is not the one any
> environment runs as.

---

## Setting up option D (one-time, per environment)

Not yet provisioned — this records the exact procedure so it is a task, not a research problem.

```bash
SUB=484bc857-3802-427f-9ea5-ca47b43db0f0
TENANT=a221a95e-6abc-4434-aecc-e48338a1b2f2

# 1. Create the local-dev-only app registration
az ad app create --display-name "SDAP-BFF-LocalDev" --sign-in-audience AzureADMyOrg

# 2. Add a client secret (short expiry — it is a convenience credential, not an identity)
az ad app credential reset --id <appId> --display-name "localdev-$(date +%Y%m)" --years 1

# 3. Expose the API scope the local client will request a token for
#    and grant the same downstream API permissions as SDAP-BFF-SPE-API (Graph + Dataverse user_impersonation)

# 4. Register it as a Dataverse application user in the DEV environment ONLY
```

Then, on each workstation — **never in a file that can be committed**:

```bash
cd src/server/api/Sprk.Bff.Api
dotnet user-secrets set "AzureAd:ClientId"     "<localdev-appId>"
dotnet user-secrets set "AzureAd:ClientSecret" "<the secret>"
```

`dotnet user-secrets` stores outside the repo tree, which is why it is the sanctioned mechanism here. **Do
not** put the value in `appsettings.Development.json` — that file is inside the repo and one `git add -A`
away from a leak.

Point the local client (PCF / code page / add-in MSAL config) at `<localdev-appId>` so the user token's
audience matches.

### Verify

```bash
dotnet run --project src/server/api/Sprk.Bff.Api
# Expect at startup: no rule-6 failure (Development is exempt), and a credential resolved from
# AzureAd:ClientSecret. Then exercise any OBO endpoint with a user token from the local client.
```

---

## What NOT to do

- ❌ **Do not recover `BFF-API-ClientSecret` from Key Vault** to get a value. It is soft-deleted until
  2026-11-22 **as a rollback mechanism for the deployed environment**, not as a developer convenience.
  Recovering it for local use makes a production rollback artifact part of daily development.
- ❌ **Do not add a secret to `SDAP-BFF-SPE-API`.** That is the identity `spaarke-bff-dev` runs as.
- ❌ **Do not set `Graph:ClientSecret` / `Dataverse:ClientSecret`.** Nothing reads them; you will get a
  local environment that looks configured and silently cannot authenticate.

---

## Related

- [`ADR-028`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — canonical auth architecture (A4 + E-3)
- [`auth-deployment-setup.md`](auth-deployment-setup.md) — the deployed-environment runbook
- [`projects/spaarke-auth-v4-dataverse-MI/notes/lessons-learned.md`](../../projects/spaarke-auth-v4-dataverse-MI/notes/lessons-learned.md) §7.2 — how this gap arose
