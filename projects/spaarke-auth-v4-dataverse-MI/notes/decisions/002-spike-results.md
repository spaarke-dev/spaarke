# Decision record — 002: OBO under a Managed-Identity client assertion

> **Task**: `tasks/002-spike-obo-under-mi-fic.poml` · **Executed**: 2026-08-20 · **Rigor**: FULL · opus/xhigh
>
> ## ✅ VERDICT: **OBO WORKS UNDER MI-FIC. THE PROJECT PROCEEDS AS SPECIFIED.**
>
> No pivot to a Key Vault certificate (Option B). No escalation trigger fired.

---

## 1. Why this task existed

Three prior audits concluded the BFF's client secret could never be removed, because OBO —
delegated user auth — was believed to require a client *secret*. ADR-028 **A4** corrected the
premise on paper (OAuth requires a confidential **credential**; a secret is one of three ways to
satisfy it). But Microsoft's canonical OBO page still lists only secret and certificate, and no
first-party sample covers our exact chain: **OBO + federated identity credential + managed
identity**, called through direct MSAL rather than `Microsoft.Identity.Web`'s declarative
`ClientCredentials` (ruled out by E4′).

Documentation could not settle it. This spike did.

---

## 2. What was built

A throwaway harness on branch **`spike/002-obo-mi-fic`** (commit `397a5f306`, **never merged**),
deployed to the **`staging` slot only** — never to the production slot, never swapped.

- Added `Microsoft.Identity.Web.Certificateless` 4.14.2 to the BFF.
- Added `GET /api/spike/obo` (`RequireAuthorization`), which takes the caller's real bearer token
  as the OBO user assertion and runs six tests.
- Per ADR-015 the endpoint returns **MSAL error codes and non-sensitive JWT claims only** — never
  a token, an assertion, or a secret.

The credential is built exactly as ADR-028 A4 prescribes, reusing the assertion instance so it
caches until expiry:

```csharp
var mia = new ManagedIdentityClientAssertion(uamiClientId);   // 5967251e-…  (the UAMI)
var cca = ConfidentialClientApplicationBuilder
    .Create(appRegistrationId)                                // 1e40baad-…  (the app reg)
    .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
    .WithClientAssertion(opts => mia.GetSignedAssertionAsync(opts))
    .Build();
var result = await cca.AcquireTokenOnBehalfOf(scopes, new UserAssertion(userToken)).ExecuteAsync();
```

### Getting a real user token — the part that could have blocked the spike

OBO needs a genuine delegated token whose audience is the BFF API. The BFF app registration
**pre-authorizes the Azure CLI** (`04b07795-8ddb-461a-bbee-02f9e1bf7b46`) for both `SDAP.Access`
and `user_impersonation`, so:

```bash
az account get-access-token --resource "api://1e40baad-e065-4aea-a8d4-4b7ab273458c"
```

yields a real user token (`upn=ralph.schroeder@spaarke.com`, `scp=SDAP.Access user_impersonation`).
**Record this — it is the reusable recipe for tasks 031 and 041's verification checklists.**

---

## 3. Results

| Test | Result | Evidence |
|---|---|---|
| **T0** assertion introspection | ✅ | `sub=9fd47efb-…` (UAMI **principalId**), `iss=…/a221a95e-…/v2.0`, `aud=fb60f99c-7a34-4190-8149-302f77469936` |
| **T1** OBO → **Graph / SPE** | ✅ | `aud=https://graph.microsoft.com`, `appid=1e40baad-…`, source `IdentityProvider` |
| **T2** OBO → **Dataverse** `user_impersonation` | ✅ | `aud=https://spaarkedev1.crm.dynamics.com`, `scp=user_impersonation`, `upn=ralph.schroeder@spaarke.com` |
| **T3** **long-running** OBO | ✅ | init `IdentityProvider` → retrieval from **`Cache`**; session key present |
| **T4** CONTROL — secret path | ✅ | identical scopes; proves the harness itself is sound |
| **T5** NEGATIVE — wrong identity | ✅ **fails, as required** | `managed_identity_request_failed` / HTTP 400 |
| **Local dev**, no MI present | ✅ | falls through to secret, real OBO succeeds |

### T0 — the assertion is minted by the right identity

`sub` is `9fd47efb-7962-492b-ac44-e5ccd0268ebb` — the UAMI's **principalId**, exactly matching the
subject of the FIC created on 2026-08-19, and `iss` matches the FIC issuer. This is direct
confirmation that the principalId-not-clientId rule (the commonest silent error) was applied
correctly when the FIC was created.

`aud` is `fb60f99c-7a34-4190-8149-302f77469936`, the application ID behind
`api://AzureADTokenExchange`. Worth recording: the audience appears in **GUID form** in the minted
assertion, not as the `api://` string. Anyone eyeballing a decoded assertion expecting the literal
URI will think it is wrong. It is not.

### T1 — OBO to Graph/SPE, the core question

Granted scopes include the full SPE surface — `FileStorageContainer.Manage.All`,
`Files.ReadWrite.All`, `Files.Read.All`, `Container.Selected`, `Directory.ReadWrite.All`. Token
source `IdentityProvider` (a genuine network exchange, not a cache hit). `appid` is the **app
registration**, confirming the BFF still acts as itself while the *assertion* was signed by the
UAMI. That separation is the whole mechanism working as designed.

### T2 — OBO to Dataverse, and why it is the most important row

The Dataverse token carries `scp=user_impersonation` **and** `upn=ralph.schroeder@spaarke.com` /
`oid=c74ac1af-…`. The user's identity survives the exchange. Had the credential change silently
degraded to an app-only token, the `upn` would be absent and Dataverse row-level authorization
would evaluate with application privileges instead of the user's — an error-**open** failure. It
did not.

### T3 — long-running OBO

`InitiateLongRunningProcessInWebApi` succeeded and the follow-up `AcquireTokenInLongRunningProcess`
returned a token from **`Cache`**. The session-key mechanism the AI/agent paths depend on across
chat turns is intact under the assertion.

### T5 — the negative control has teeth

Minting the assertion for the **app registration's** clientId instead of the UAMI's fails with:

> `[Managed Identity] Error Message: No User Assigned or Delegated Managed Identity found for
> specified ClientId/ResourceId/PrincipalId.`

Loud, specific, and at *assertion-minting* time — before any token exchange. The FR-B4 conflation
hazard is real, and it is **detectable**, which is what task 023's guard needs.

---

## 4. Local development — works, but the checked-in local secret is STALE

On a workstation there is no IMDS, so the MI assertion fails with
`managed_identity_unreachable_network` (`169.254.169.254:80` unreachable) — a clean, catchable
`MsalServiceException`. The ordered selector then falls through to the client secret. That is
exactly the shape task 021 must build, and it is proven end-to-end: **the fallback selected the
secret and completed a real OBO exchange.**

**But the first run failed** — `AADSTS7000215: Invalid client secret provided`. The
`API_CLIENT_SECRET` in this workstation's user-secrets store
(`cbc576fa-6ea6-485a-bb2a-d96130f21f20`) does **not** match the app registration's current secret
(`Dataverse-Checkout-20251218`). Re-running with the current value succeeded immediately.

Two things follow, and both matter:

1. **Local dev OBO is currently broken for anyone holding the stale secret** — and it fails in a
   way that looks like a code problem rather than a config one. It is neither caused by nor fixed
   by this project; task 024 (local-dev config story) should carry it.
2. **This independently confirms the answer already given to `customer-provisioning-orchestration-r1`**
   in [`PROVISIONING-CHANGE-REQUEST.md`](../PROVISIONING-CHANGE-REQUEST.md) §9.1: a *wrong* secret
   produces the opaque `AADSTS7000215`, whereas an *absent* secret produces a clean, catchable
   fall-through. That is precisely why the recommendation was to **omit the secret entirely rather
   than write a sentinel value**. We now have the failure mode on record rather than as an argument.

---

## 5. Acceptance criteria

| # | Criterion | Result |
|---|---|---|
| 1 | OBO → Graph/SPE under a MI-issued assertion, with evidence | ✅ T1 |
| 2 | OBO → Dataverse `user_impersonation`, with evidence | ✅ T2 |
| 3 | Long-running OBO succeeds | ✅ T3 |
| 4 | Local dev degrades to the secret path with no MI | ✅ + stale-secret finding (§4) |
| 5 | Negative: assertion for the wrong identity fails | ✅ T5 |
| 6 | Negative: authorization still fails **CLOSED** | ⚠️ **partially evidenced — see below** |
| 7 | Production slot never deployed to, never swapped | ✅ production returned 200 throughout |

### On criterion 6, stated honestly

The spike changed **no authorization code**, and T2 shows the positive half of what matters: the
Dataverse token carries the real user's `upn`/`oid`, so row-level authorization evaluates as that
user rather than as the application. A live `/api/containers` call also returned **403** (denied)
rather than erroring open. What was **not** exercised is a user who genuinely lacks rights
returning `AccessRights.None` — that requires a second test principal, and it is specified as part
of task **031**'s §6.1 checklist. It is recorded here as evidenced-in-part rather than claimed.

---

## 6. Per-task obligations (root CLAUDE.md §10)

| Obligation | Result |
|---|---|
| Publish size (spike build, incl. Certificateless) | **43.68 MB** compressed incl. PDBs |
| Delta vs the 43.67 MB clean build | **+0.01 MB** — `Microsoft.Identity.Web.Certificateless` is essentially free |
| vs the 44.96 MB net10 baseline / 60 MB ceiling | ✅ comfortably under |
| CVE scan | ✅ no vulnerable packages |
| Build | ✅ 0 errors, 7 pre-existing obsolete-API warnings |
| Placement justification | N/A for the spike (unmerged). Task 020 owns it for the real seam |

**The publish-size answer matters for task 020**: adding the Certificateless package costs
approximately nothing, so NFR-01 is not a constraint on the chosen design.

---

## 7. What this hands to Phase 2

- **Task 020** — the seam can be built with confidence; the mechanism is proven and the package is
  free. Reuse the `ManagedIdentityClientAssertion` instance (it caches until expiry).
- **Task 021** — the ordered fallback shape is validated: catch `MsalServiceException`
  (`managed_identity_unreachable_network` on a workstation, `managed_identity_request_failed` for a
  wrong identity) and fall through. Both are clean, catchable, and distinguishable.
- **Task 023** — T5 is the reproduction the conflation guard should assert against.
- **Task 024** — the stale-local-secret finding belongs to the local-dev config story.
- **Tasks 031 / 041** — reuse the `az account get-access-token --resource api://…` recipe in §2 to
  obtain a real user token for verification.

## 7a. ADR-028 A4 note — the T4 control adds a `.WithClientSecret` call site

Stated explicitly rather than left silent, because A4 is the rule this project enforces:
**T4 constructs a new `.WithClientSecret(...)` client.** A4 forbids new secret-bearing BFF-identity
sites and E-3 does not license expansion.

It is defensible here and is **not** claimed as an exception: the call exists only on the unmerged
throwaway branch `spike/002-obo-mi-fic`, purely as the control that makes a T1/T2 failure
attributable to the mechanism rather than to the harness. It never reaches the work branch, never
reaches master, and the slot has since been redeployed without it. The forcing function built in
task **060** would correctly flag this code if anyone tried to merge it — which is the desired
behaviour, and a useful confirmation that the ban will bite.

## 8. Cleanup performed

The slot was **redeployed with the clean (non-spike) build** and verified: `/api/spike/obo` now
returns **404**, `/healthz` returns 200, production unchanged at 200. The spike survives only as
branch `spike/002-obo-mi-fic`, unmerged, as reproducible evidence.
