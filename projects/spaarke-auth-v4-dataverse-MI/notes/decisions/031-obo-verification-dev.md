# Task 031 — OBO verification on the slot

> ## 🟡 STATUS: IN PROGRESS — THE CENTRAL CLAIM IS PROVEN; THE CHECKLIST IS PARTIAL.
>
> 2026-08-23 pre-flight (§2) · **2026-08-24 live verification (§5)**. FR-C1.
>
> **✅ The project's central claim is now demonstrated on live infrastructure: OBO succeeds under a
> Managed-Identity federated credential, with no client secret involved.** See §5.1 — that is the thing
> three prior audits concluded was impossible.
>
> **Task 031 is NOT complete.** Several checklist surfaces are unexercised and one result (§5.3) is
> explicitly unexplained. Do not mark it complete on the strength of this document.

---

## 1. Why this task looked unrunnable — and what actually unblocked it

> **Superseded in part on 2026-08-24 (see §5).** Two of the three blockers below were removed: a real
> delegated token proved mintable from the operator's own session, and the owner created
> `testuser1@spaarke.com` for the negative case. What remains genuinely blocked is the three surfaces
> needing a human in a real client (Outlook, Word, Copilot). The original analysis is kept because it
> is why the task was scoped as owner-present in the first place.

031 verifies eight OBO surfaces. OBO is *delegated* auth — every one of them requires **a real signed-in
user's token**, which cannot be minted from an automation context. Three of the eight additionally
need a human driving a real client:

| Step | Blocker |
|---|---|
| SPE upload / download / preview | real delegated user token |
| `dataverse.*` tool calls over `/api/ai/chat` SSE | real delegated user token |
| **Office add-ins: Outlook + Word save flows** | a human in Outlook and Word |
| **M365 Copilot agent `/api/agent`** | a human in Copilot |
| Dataverse row-level authorization | real delegated user token |
| Send-as-user email `/api/communications` | real delegated user token |
| Long-running OBO | real delegated user token |
| Inbound validation regression (3 schemes) | workforce JWT + Ciam + RagApiKey |
| **Negative case: unauthorised user is denied on every surface** | **a SECOND test principal, which does not exist** |

The second principal is the sharpest blocker and it is not a technicality: the fail-closed criterion is
what proves the migration did not turn an authorization boundary into an error-open one. It cannot be
demonstrated with a single identity. The owner directive for this project is *"if something must exist
for dev to work E2E, create it in this project rather than deferring it"* — so creating it is in scope,
but creating an Entra principal is a tenant-level change that needs explicit authorization.

## 2. Pre-checks completed (read-only, 2026-08-23)

### 2.1 The SIGABRT trap is NOT armed ✅

Task 001 Finding A: `keyVaultReferenceIdentity` is a **site property**, and `--configuration-source`
does not copy it. When it defaulted to `SystemAssigned` (which the slot does not have), every Key Vault
reference failed and startup aborted with exit code 134 — a failure that *looks exactly like a
credential failure*, which is the worst possible misdiagnosis while testing a credential change.

Verified on both slots of `spaarke-bff-dev`:

| | default slot | `staging` slot |
|---|---|---|
| `keyVaultReferenceIdentity` | `…/spe-infrastructure-westus2/…/mi-bff-api-dev` | **same** ✅ |
| identity type | `UserAssigned` | `UserAssigned` ✅ |
| attached UAMI | `mi-bff-api-dev` | `mi-bff-api-dev` ✅ |

Resolved by **resource ID**, so this is not the `spaarke-bff-identity` decoy.

### 2.2 Both slots are healthy ✅

`https://spaarke-bff-dev.azurewebsites.net/healthz` → **200**
`https://spaarke-bff-dev-staging.azurewebsites.net/healthz` → **200**

⚠️ Note for the checklist: after task 051, `/healthz` also carries the new
`servicebus-job-processing` check. **A 200 is no longer a weak signal for job processing specifically**
— but it remains a weak signal for OBO, which has no health check. Do not substitute `/healthz` for the
checklist.

### 2.3 `AZURE_CLIENT_ID` — the booked obligation resolves to "record why not" ✅

031 carries a constraint from task 023: *"CLEAR AZURE_CLIENT_ID ON BOTH SLOTS, or record why not."*

Live state — **identical on both slots**:

```
AZURE_CLIENT_ID = 5967251e-171c-46fe-a6c2-ef843c90309d   ← the UAMI's clientId
API_APP_ID      = 1e40baad-e065-4aea-a8d4-4b7ab273458c   ← the app registration
```

**Recording why not: the trap is already defused in code.** Task 022 deleted the
`AZURE_CLIENT_ID ?? API_APP_ID` fallback from `GraphClientFactory`. Verified by grep — the *only*
remaining reader anywhere in `src/` is `IdentityConfigurationValidator.cs:65`, which reads it in order
to **detect and log the conflation**, not to resolve an identity from it.

So clearing the setting is now **hygiene, not a fix**: it removes a misleading value that reads like
configuration but drives nothing. The original danger — *"set `Graph:ManagedIdentity:Enabled=false`
during an incident and the BFF builds a `ClientSecretCredential` from a managed identity"* — **no longer
exists**, because the consuming branch is gone.

`Graph__ManagedIdentity__Enabled = true` on both slots.
`Graph__Credentials__RequireSecretFreeIdentity` is **absent on both** — correct for now; task 062 made it
off-by-default deliberately so it could not block 031/032 while the secret is still the fallback. **Task
033 must set it in the same change that drops the secret**, or the FR-F3 forcing function silently never
engages.

**Deliberately not changed here.** Clearing an app setting restarts the app. Doing that as a fragment of
a task that cannot be completed buys nothing and costs a restart on both slots. It belongs in the
owner-present session.

### 2.4 Finding: the `staging` slot is a live competing consumer on `sdap-jobs`

Both slots carry **all 213 app settings** — identical — including the Service Bus connection string and
queue name, and both are **Running**. Service Bus queues are competing-consumer, so this is not
duplicate processing; but it does mean **the "staging" slot has been silently doing real work against
dev data**, not sitting idle. That is how it came to be logging `InvalidSignature` during the
2026-08-23 outage in the first place.

This strengthens the 032 rescope (see [`032-slot-strategy.md`](032-slot-strategy.md)): the slot is not a
dormant spare. It is a second live instance, invisible in telemetry because it reports the same
`cloud_RoleName`.

## 3. What the owner-present session needs

1. **A delegated user token** for a real dev user (the recipe is in `current-task.md`).
2. **A second test principal** — decision needed: create it (per the standing directive) or descope the
   fail-closed negative criterion with a written reason.
3. **A human in Outlook, Word and Copilot** for three of the eight surfaces.
4. Roughly two minutes of tolerance for **AADSTS70025 flapping** if any FIC is touched — 8 failures
   interleaved with successes over ~130s (measured, task 030). A single green check inside that window
   proves nothing; sample across it.

## 4. Not done as of the 2026-08-23 pre-flight

> **This section describes the state on 2026-08-23 only. §5 supersedes it.** By 2026-08-24 the branch
> build HAD been deployed to the slot and OBO surfaces HAD been exercised. Read §5 for current state.

- Nothing deployed to the slot. *(no longer true — see §5.0)*
- No app setting changed on either slot. *(still true of the default slot; filesystem logging was
  enabled on the staging slot only — see §5.0)*
- No OBO surface exercised. *(no longer true — see §5.1/§5.2)*
- **No FIC created or modified.** *(still true, and deliberately so — no AADSTS70025 convergence window
  was opened, which is why the §5 results need no flap-tolerance caveat)*
- The secret-first rollback re-verification — **which 032 now depends on**, since it is the evidence
  that config-only rollback works before the slot is deleted. *(still outstanding — see §5.4)*


---

## 5. Live verification, 2026-08-24 (staging slot)

### 5.0 What was done to the environment

| Action | Scope | Reversible |
|---|---|---|
| Published the branch build and **zip-deployed it to the `staging` slot** | slot only — default slot never touched (verified 200 throughout) | yes (redeploy, or 032 deletes the slot) |
| Enabled filesystem application logging on the slot | slot only | yes |
| Acquired a delegated token for `testuser1@spaarke.com` by **device-code flow** | no change to the CLI session; token never transited chat | expires ~1h |

The slot previously ran the **task-001 build (2026-08-20)**, which predates task 022's migration — so
verifying MI-FIC required deploying first.

**Credential order was deliberately NOT configured.** `Graph:Credentials:Order` is absent on the slot,
so `AuthorizationModule` applies the canonical default `[ManagedIdentityFederated, ClientSecret]`. That
is exactly what the default slot inherits after a swap, so testing the default is more faithful than
pinning an explicit order.

### 5.1 ✅ MI-FIC OBO WORKS — the finding this project exists for

Slot boot: **`/healthz` 200 Healthy, no exit-134 SIGABRT.** Then, verbatim from the slot's own log
stream (slot-scoped, so free of the shared-`cloud_RoleName` ambiguity):

```
Sprk.Bff.Api.Infrastructure.Auth.OrderedCredentialClientProvider[0]
  Confidential client for 1e40baad-e065-4aea-a8d4-4b7ab273458c
  built with credential ManagedIdentityFederated.
  OBO Token Exchange - CCA configured with ClientId from API_APP_ID
  Cache miss, performing OBO token exchange
  OBO token exchange successful
  OBO token scopes: ... Container.Selected, FileStorageContainer.Manage.All,
    Files.ReadWrite.All, Mail.Send, Sites.FullControl.All, User.ReadWrite.All ...
```

Three things are proven together here, and each was a separate open question:

1. **The confidential client was built from `ManagedIdentityFederated`** — not the secret. The ordered
   provider selected the MI-FIC credential first and it succeeded.
2. **The OBO exchange completed**, returning a downstream Graph token with the full delegated scope set.
   OBO under a secret-free confidential credential is exactly what `.claude/constraints/auth.md:108`
   asserted was impossible and what ADR-028 A4 corrected.
3. **`API_APP_ID` is the ClientId in play** — confirming task 022's deletion of the
   `AZURE_CLIENT_ID ?? API_APP_ID` fallback holds at runtime, not just in source. The identity
   conflation trap (FR-B4) does not fire.

Also captured, app-only on the same path:

```
Spaarke.Dataverse.DataverseAccessDataSource[0]
  app-only auth: Managed Identity (ADR-028; DI-injected TokenCredential, clientId 5967251e-...)
  delegated auth: OBO available via the ordered credential provider
```

### 5.2 ✅ Inbound validation — including the criterion about WHERE rejection happens

| Identity | `/api/me` | `/api/config/client` |
|---|---|---|
| `ralph.schroeder@spaarke.com` | **200** | 200 |
| `testuser1@spaarke.com` | **200** | 200 |
| no token | **401** | 200 (anonymous by design) |
| malformed token | **401** | 200 |

The acceptance criterion is not merely "rejected" but *"rejected at the inbound layer, not at the OBO
exchange"*. The log proves the distinction:

```
Microsoft.IdentityModel...IdentityLoggerAdapter[0]
  IDX10504: Unable to validate signature, token does not have a signature
JwtBearerHandler[1] Failed to validate the token.
```

— signature validation, before any OBO code runs. And for the absent-token case:
`DenyAnonymousAuthorizationRequirement: Requires an authenticated user.` **✅ criterion met.**

### 5.3 🟡 UNEXPLAINED: `/api/containers` returns 403 to BOTH identities

| | ralph (admin) | testuser1 (read-only) | no token | malformed |
|---|---|---|---|---|
| `GET /api/containers` | **403** | **403** | 401 | 401 |
| `GET /api/containers?containerTypeId=<guid>` | **403** | — | — | — |

The endpoint requires policy `canmanagecontainers` → `ResourceAccessRequirement("create_container")`,
resolved through Dataverse **over OBO**.

**This is NOT evidence of an OBO failure** — §5.1 shows the exchange succeeding in the same run. But
**neither is it yet proven to be a correct denial**, and the distinction matters more here than
anywhere else in the task: *this path fails closed, so a broken authorization lookup and a legitimate
"no permission" produce the identical 403.* The status code cannot separate them, and I did not capture
the specific denial — the log-tail window closed before these probes landed.

**Open. Must be resolved before 031 can be called green.** The discriminator is a slot-scoped log
capture spanning the `/api/containers` call, showing either a `ResourceAccessRequirement` denial
(correct) or an access-lookup error (a defect). Identical 403s for an admin and a read-only user is at
least mildly suspicious and deserves the check rather than an assumption.

### 5.4 Not yet exercised

SPE upload/download/preview · `dataverse.*` chat tool calls over SSE · Office add-in save flows ·
`/api/agent` · row-level authorization on `PermissionsEndpoints` · send-as-user email (authorized by the
owner, recipient = owner only) · long-running OBO · the Ciam and RagApiKey inbound schemes · **the
secret-first rollback re-verification, which 032 depends on**.
