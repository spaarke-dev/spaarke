# Live verification — the missing owning-app credential

> 2026-08-24 · **RESOLVED.** App-only live verification is now available to this project.
> **No secret value appears in this file.** App ids, vault names, and secret *names* are public
> identifiers, not credentials.

---

## 1. What was blocking live verification

Tasks 021–024 and 030 were all recorded as "not live-verified", on the stated grounds that no Azure
login was available. **That was wrong, and it was my error**: a single failed `az account show` early
in the session was carried forward as a standing fact across four task notes without ever being
retried. Precisely the defect this project exists to remove — one observation cached as truth.

On re-checking, `az` was logged in. The real blocker was two layers deeper.

| Layer | Finding |
|---|---|
| `az` CLI token | Works, but returns **403 accessDenied** on `/storage/fileStorage/*`. The az CLI's own app registration carries no SPE permissions. Not a login problem. |
| App-only as the owning app | The documented route (task 010). Requires the client secret for `170c98e1`. |
| 🔴 **The secret** | `sprk_specontainertypeconfig.sprk_keyvaultsecretname` = `spe-owning-app-secret`; `SpeAdmin__KeyVaultUri` = `https://sprk-prod-kv.vault.azure.net/`. **The secret did not exist in that vault.** |

Verified as genuinely absent rather than a permissions artifact: the vault holds exactly six secrets,
none soft-deleted, and read access was confirmed by reading a different secret's attributes.

### Why it was missing

The app **did** hold a valid credential — *"SPE Dev 2 Functions Secret"*, expiring 2027-09-22. But
Azure displays a client secret **once, at creation**. That one was minted for a Functions app, and
**no Function App exists in any of the five subscriptions**, nor any local `*.local.json`. The value
was unrecoverable.

**Not caused by this project's earlier credential cleanup.** Task 013 removed two credentials from this
same registration on 2026-08-22, but both were already expired and the 2027 one was untouched.

### Blast radius while it was missing

`FetchKeyVaultSecretAsync` feeds `GetClientForConfigAsync`, which serves **every** `…ForConfigAsync`
method — containers, recycle bin, search, security, audit. With the secret absent, none of them could
build a Graph client at all. This is plausibly a significant part of why the app "has never been fully
functional".

---

## 2. Resolution

1. **Minted a new secret on `170c98e1` with `--append`.** The flag is load-bearing: without it,
   `az ad app credential reset` **deletes every existing credential**. Confirmed afterwards that the
   2027 credential survived alongside the new one.
2. **Piped the value straight into Key Vault** as `spe-owning-app-secret` via `--file`, so the value
   never appeared in a command line, log, or terminal. Local scratch file shredded immediately.
3. **Verified metadata only** — secret id and `enabled`, never the value.

| Item | Value |
|---|---|
| App registration | `170c98e1-d486-4355-bcbe-170454e0207c` (**SDAP-PCF-CLIENT**, doubles as the SPE owning app in this tenant — see [`app-registration-topology.md`](app-registration-topology.md)) |
| Credential display name | `spe-owning-app-secret (SPE Admin R2)` |
| **Expires** | **2028-08-24** |
| Vault | `sprk-prod-kv` (matches `SpeAdmin__KeyVaultUri`) |
| Secret name | `spe-owning-app-secret` (matches the Dataverse config) |

**Fully reversible**: delete the credential by its display name and delete the vault secret.

⏰ **Set a reminder for 2028-08-24.** When this expires, every app-only SPE path stops working the same
silent way it just did.

---

## 3. What the credential unlocked, immediately

The app-only token carries `FileStorageContainer.Selected`, `FileStorageContainerTypeReg.Selected`,
`Files.ReadWrite.All` — enough for containers, the recycle bin, search, security, and audit.

| Task | Verified live |
|---|---|
| **024** | 🔴 **5 of 5 containers report storage; 861 MB total** while the Dashboard rendered `0 B`. Every value **fits in int32** — a `long`-only match would have dropped all five. See [`storage-consumption-spike.md`](storage-consumption-spike.md) §6. |
| **024** | GET omits `storageUsedInBytes` even with an explicit `$select` — task 020's LIST-only finding confirmed. |
| **022** | `deletedContainers` returns **200, no OData error** — the POML's claimed 400 does not occur. |
| **010** | Container types remain **403 app-only**, on beta. Delegated-only, re-confirmed. |

---

## 4. 🔔 Stage 2 — delegated access is still required

App-only cannot reach container **types**, so these stay unverified: task **023**'s settings
write→read-back (its AC-2 and escalation trigger), and task **030**'s owning-app / trial-expiry fields.

The operator account already holds **SharePoint Embedded Administrator** and **Global Administrator**,
so the *identity* is sufficient. What is missing is an **app registration that requests the delegated
SPE permission** and can complete an interactive sign-in.

**Recommended setup — one registration, about five minutes:**

1. Create an app registration, e.g. `SPAARKE-SPE-Admin-CLI`.
2. Authentication → **Allow public client flows: Yes** (enables device-code sign-in; no secret needed).
3. API permissions → Microsoft Graph → **Delegated** → `FileStorageContainerType.Manage.All`, plus
   `FileStorageContainer.Selected`.
4. **Grant admin consent.**
5. Record its client id here.

Then `az login --allow-no-subscriptions --tenant a221a95e-… --client-id <new-app-id>`, or a device-code
flow, yields a delegated token that can read and PATCH container types.

**Do not** bolt SPE permissions onto the Microsoft first-party az CLI registration to avoid this — that
is a tenant-wide change to a shared identity in order to fix a local gap.
