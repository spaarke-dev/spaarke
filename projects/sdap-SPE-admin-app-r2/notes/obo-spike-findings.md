# OBO spike — findings

> **Task 010** (spec FR-B01) · 2026-08-21 · **VERDICT: UNWORKABLE as specified**
> **Method**: code inspection plus live Entra / Microsoft Graph calls against the Spaarke Dev tenant
> (`a221a95e-…`). Read-only throughout — **no container type was created, modified, or deleted.**
> **No secret value, token, or assertion appears in this file.** App IDs and tenant IDs are public
> identifiers, not credentials.

---

## Verdict

**The per-customer owning-app OBO shape cannot obtain a Graph-audienced delegated token, and cannot be
made to without a change that this task is explicitly forbidden to make.**

Both defects are confirmed, and both are **worse than the spec described**. Underneath them sits a
premise error: **there is no per-customer owning app in this environment.** The app the config names
as the owning app is the SPA client the code page already authenticates *as*.

🔔 **Escalation triggers 1 and 2 both fire.** See [`BLOCKED.md`](../BLOCKED.md). The §6.5 gate must be
re-run with this evidence.

---

## The registrations (live, via Entra)

| Role | Display name | App ID | `identifierUris` | Exposed scopes |
|---|---|---|---|---|
| BFF (resource) | `SDAP-BFF-SPE-API` | `1e40baad-…` | `api://1e40baad-…` (+2) | `user_impersonation` etc. |
| SPA client | `SDAP-PCF-CLIENT` | `170c98e1-…` | **`[]` — none** | **`[]` — none** |

`sprk_specontainertypeconfig.sprk_owningappid` = **`170c98e1-…`** — i.e. the config's "owning app" **is
`SDAP-PCF-CLIENT`**, the client the SPE Admin code page signs in as. It is not a separate per-customer
owning application.

---

## Defect 1 — CONFIRMED, and fatal at the resource level

`SpeAdminTokenProvider.cs:142`

```csharp
var scopes = new[] { $"api://{config.OwningAppId}/.default" };
```

…and the resulting token is handed to a client pointed at Graph (`SpeAdminGraphService.cs:4212`,
base URL `https://graph.microsoft.com/beta`).

The spec called this an audience mismatch. Live, it is worse — the resource does not exist:

```
AADSTS500011: The resource principal named api://170c98e1-d486-4355-bcbe-170454e0207c
was not found in the tenant named a221a95e-….
```

`SDAP-PCF-CLIENT` has **no `identifierUris` and no exposed OAuth2 scopes**, so `api://{OwningAppId}` can
never resolve. This scope has never been satisfiable — not "returns the wrong token", but **cannot
return a token at all**. Independent corroboration:
`projects/spaarke-auth-v4-dataverse-MI/notes/CREDENTIAL-INVENTORY.md:22`.

## Defect 2 — CONFIRMED, and structurally unfixable in this shape

`SpeAdminTokenProvider.cs:306-310`

```csharp
ConfidentialClientApplicationBuilder
    .Create(config.OwningAppId)     // 170c98e1 — SDAP-PCF-CLIENT
    .WithClientSecret(clientSecret)
```

**The OAuth 2.0 On-Behalf-Of flow requires the confidential client performing the exchange to be the
audience of the incoming assertion.** The SPE Admin code page signs in as `SDAP-PCF-CLIENT` and requests
the BFF's scope, so the assertion the BFF receives carries `aud = 1e40baad-…` (the BFF).

`170c98e1 ≠ 1e40baad`. The exchange cannot succeed — and it cannot be repaired by configuration,
because `SDAP-PCF-CLIENT` exposes no identifier URI and no scopes, so **it can never be the audience of
anything**. Making it one is an app-registration change plus a client-side change to request a second
token — which is escalation trigger 2.

> Together these are conclusive: **the SPE-084 multi-app OBO path has never executed successfully.**
> Consistent with spec §1 — no test makes a real Graph call, so nothing ever exercised it.

---

## Step 3 (the crux) — which registration can perform the exchange?

**Only the app the assertion is audienced to: the BFF, `SDAP-BFF-SPE-API` (`1e40baad-…`).**

This is a protocol constraint, not a policy one. No amount of credential configuration on
`SDAP-PCF-CLIENT` changes it.

---

## Step 4 — delegated permission state (trigger 3 does NOT fire)

Both service principals already carry the needed grant, admin-consented for all principals:

| Service principal | Delegated Graph scopes (consented, `AllPrincipals`) |
|---|---|
| `SDAP-PCF-CLIENT` | `Files.ReadWrite.All`, `Files.ReadWrite.AppFolder`, `FileStorageContainer.Manage.All`, `FileStorageContainer.Selected`, **`FileStorageContainerType.Manage.All`**, `openid`, `profile`, `offline_access` |
| `SDAP-BFF-SPE-API` | `Directory.ReadWrite.All`, `Files.Read.All`, `Files.ReadWrite.All`, `FileStorageContainer.Manage.All`, `FileStorageContainer.Selected`, **`FileStorageContainerType.Manage.All`**, `FileStorageContainerTypeReg.Manage.All`, `Sites.FullControl.All`, … |

**`FileStorageContainerType.Manage.All` is granted and consented on both.** Escalation trigger 3 —
"lacks the required delegated permission or consent" — **does not fire**. No operator grant is needed.

**The permissions were never the problem. The exchange mechanics are.**

---

## Steps 5–6 — app-only confirms the spec's core claim

Owning app, `client_credentials`, corrected scope `https://graph.microsoft.com/.default`:

- token issued, `aud = https://graph.microsoft.com`
- roles: `FileStorageContainer.Selected`, `FileStorageContainerTypeReg.Selected`, `Files.ReadWrite.All`,
  `Files.SelectedOperations.Selected`, `Files.ReadWrite.AppFolder`

```
GET /v1.0/storage/fileStorage/containerTypes    → 403 accessDenied
GET /beta/storage/fileStorage/containerTypes    → 403 accessDenied
  "Caller does not have required permissions for this API"
```

**Spec §3.1 is confirmed empirically**: Container Types cannot work under the app-only posture, on
either API version. A delegated token is genuinely required — the question is only which app can get one.

---

## Step 7 — NOT RESOLVED, and here is why

The task asks whether container-type **list** works for a non-admin owning-tenant user, and whether
**create** truly needs no admin role (the Graph reference page and the conceptual doc disagree).

**I could not answer this empirically.** Both questions need a *delegated* token, and there are only
three ways to get one here:

1. via the OBO path — which is the very thing proven broken above;
2. via an interactive or device-code sign-in as `SDAP-PCF-CLIENT` — requires a human at a browser;
3. by testing **create** — forbidden by this task's read-only tenant-safety constraint.

Recording it as unresolved rather than inferring it from documentation, since the whole point of the
question is that the documentation contradicts itself. **It carries forward to task 011**, which will
hold a working delegated token and can settle it in one call. It does not change this verdict.

---

## The shape that WOULD work — and why I did not adopt it

`SDAP-BFF-SPE-API` is the assertion's audience **and** already holds delegated
`FileStorageContainerType.Manage.All`, admin-consented. So a BFF-performed OBO exchange would very
likely work on the first try.

**I did not take it, and task 011 must not take it either without a human decision.** Escalation
trigger 1 forbids exactly this fallback, because it:

- puts the BFF identity on a confidential-client credential path → **ADR-028 Amendment A4** territory
  (MI-FIC or Key Vault certificate — never a client secret);
- trips **E-3's "does not license expansion"** clause, since it is a NEW site rather than one of the
  enumerated transitional ones;
- **contradicts `spaarke-auth-v4-dataverse-MI`**, whose `design.md:149` explicitly scopes
  `SpeAdminTokenProvider` / `SpeAdminGraphService` *out* of its MI-FIC migration on the stated grounds
  that they "authenticate per-customer *owning applications*, not the BFF identity (ADR-028 E-1)".

That last point is the one that matters most: **the §6.5 gate was resolved as path C on a premise that
turns out to be false.** E-1 covers "per-customer owning apps, which are other applications'
identities." There is no such app here. The gate's reasoning does not survive the evidence, so the gate
itself must be re-run — not quietly reinterpreted.

---

## What task 011 must NOT assume

1. **Do not assume Search is blocked on this.** Task 004 proved Search's failure was a wrong Graph
   entity type, fully fixed, nothing to do with auth.
2. **Do not "repair" `SpeAdminTokenProvider` by swapping the scope string.** Defect 1 is not a typo;
   the resource does not exist. Fixing the scope alone still leaves defect 2.
3. **Do not switch `Create(OwningAppId)` → `Create(BffAppId)` as an obvious fix.** It is the one move
   trigger 1 names, and it needs a human path A/B decision plus auth-v4 coordination.

---

## Evidence ledger

| # | Check | Result |
|---|---|---|
| 1 | `api://{owningAppId}/.default` token request | `AADSTS500011` resource principal not found |
| 2 | `SDAP-PCF-CLIENT` `identifierUris` / exposed scopes | `[]` / `[]` |
| 3 | `sprk_owningappid` in Dataverse | `170c98e1-…` = `SDAP-PCF-CLIENT` (the SPA client) |
| 4 | BFF app id / identifier URIs | `1e40baad-…` / `api://1e40baad-…` |
| 5 | `FileStorageContainerType.Manage.All` delegated, both SPs | granted, `AllPrincipals` consent |
| 6 | app-only `GET …/containerTypes` v1.0 and beta | `403 accessDenied` both |
| 7 | app-only Graph token audience | `https://graph.microsoft.com` (so app-only itself is healthy) |
| 8 | Container types created / modified / deleted | **none** |

## Secret handling

The owning-app secret was read from Key Vault into a shell variable for token acquisition and **never
printed, logged, or written to any file**. No secret value, token, or assertion appears in this
document or anywhere in the repo.
