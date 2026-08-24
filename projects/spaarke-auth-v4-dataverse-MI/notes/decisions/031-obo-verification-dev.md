# Task 031 — OBO verification on the slot

> ## 🟡 STATUS: IN PROGRESS — THE CENTRAL CLAIM IS PROVEN; THE CHECKLIST IS PARTIAL.
>
> 2026-08-23 pre-flight (§2) · **2026-08-24 live verification (§5)**. FR-C1.
>
> **✅ The project's central claim is now demonstrated on live infrastructure: OBO succeeds under a
> Managed-Identity federated credential, with no client secret involved.** See §5.1 — that is the thing
> three prior audits concluded was impossible.
>
> **Task 031 is NOT complete** — several checklist surfaces remain unexercised (§5.5), including the
> secret-first rollback re-verification that 032 depends on. But nothing is unexplained: the one
> anomalous result (§5.3) was chased to root cause, and row-level authorization is verified (§5.4).

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

### 5.3 ✅ RESOLVED: the `/api/containers` 403 is a pre-existing route/policy mismatch

| | ralph (admin) | testuser1 (read-only) | no token | malformed |
|---|---|---|---|---|
| `GET /api/containers` | 403 | 403 | 401 | 401 |

**Root cause, from the slot log:**

```
warn: Sprk.Bff.Api.Infrastructure.Authorization.ResourceAccessHandler[0]
      Authorization failed: No resource ID found in route for user c74ac1af-...
      Authorization failed. Fail() was explicitly called.
Request finished ... 403 0 - 2.6780ms
```

`GET /api/containers` is a **collection** route carrying `.RequireAuthorization("canmanagecontainers")`
→ `ResourceAccessRequirement("create_container")`. But `ResourceAccessHandler` is a **per-resource**
handler: it calls `ExtractResourceId(httpContext)` and, finding no resource id in the route, calls
`context.Fail()` (`ResourceAccessHandler.cs:51-57`). A collection route has no resource id, so the
policy **can never be satisfied on this endpoint, for any caller.**

**Not a credential problem, and provably so**: it fails in **2.68 ms**, before any Dataverse call or
token use, and §5.1 shows the OBO exchange succeeding in the same session. The identical 403 for an
admin and a read-only user — which is what made it look suspicious — is explained: the handler never
reaches the access check for *either* of them.

**Not caused by this project.** `git diff origin/master...HEAD` shows neither
`ResourceAccessHandler.cs` nor `DocumentsEndpoints.cs` is touched on this branch; the handler's last
commit is a project-wide rename.

**Is it a required capability that is broken? No — checked, not assumed.** Container create and list
both work via the SpeAdmin group at **`/api/spe/containers`**
(`Api/SpeAdmin/ContainerEndpoints.cs:67`), and provisioning creates containers directly through
`SpeFileStore.CreateContainerAsync` (`ExternalAccess/ProvisionProjectEndpoint.cs:390`). A repo-wide
grep finds **no client code calling `/api/containers` at all** — only docs, API baselines, and one
CORS `OPTIONS` test. These two are **superseded duplicates**, not a missing capability.

**Exactly two endpoints are affected**, and the reason is a near-miss worth recording:

| Route | Resource id present? | Result |
|---|---|---|
| `POST /api/containers` (`:16`, policy `:61`) | none | **always 403** |
| `GET /api/containers?containerTypeId=` (`:64`, policy `:106`) | query is `containerTypeId`; the fallback matches `containerId` | **always 403** |
| `GET /api/containers/{containerId}/drive` (`:110`) | ✅ | works |
| `GET /api/drives/{driveId}/…` ×3 (`:152`, `:195`, `:248`) | ✅ | works |

`ExtractResourceId` reads route values `containerId`/`driveId`/`documentId`/`resourceId`/`id`, then
falls back to query `containerId`/`driveId`/`documentId`/`resourceId`
(`ResourceAccessHandler.cs:139-159`). One character class away from matching.

**The real damage was documentation, and it is fixed here rather than deferred.** Three docs pointed
at the dead path, and one of them was dangerous:

- `docs/guides/INCIDENT-RESPONSE.md` told operators to **health-probe `/api/containers` during an SPE
  incident**. It always returns 403, so the runbook would have sent someone chasing an auth fault that
  does not exist — the same failure mode as the stale "0 slots exist" note that cost 40 minutes on
  2026-08-23. Now points at `/healthz` and `/api/spe/containers`, with the reason stated.
- `docs/architecture/sdap-overview.md` advertised it as the container-CRUD path.
- `docs/standards/INTEGRATION-CONTRACTS.md` listed it as the container-management contract.

**What is left for the owner of the documents surface** (booked as 090 obligation `031-A`, as a
decision, not a silent deferral): delete the two dead endpoints, or re-guard them with an
app-role/admin policy rather than a per-resource one. That choice is not a credential-migration
project's to make.

### 5.4 ✅ Row-level authorization verified — a computed denial, not a failing lookup

The task names `PermissionsEndpoints` as the row-level authorization surface. Tested against a real
`sprk_document` (`d9ad4750-829d-f111-b8de-70a8a590c51c`, found via Dataverse):

| Identity | `GET /api/documents/{id}/permissions` | Body |
|---|---|---|
| ralph (admin) | **200** | every flag `false`, `userId` hash `d12L59F…` |
| testuser1 (read-only) | **200** | every flag `false`, `userId` hash `QO9TTMN…` |
| no token | **401** | — |
| malformed token | **401** | — |

All-false for an admin looked like the §5.3 ambiguity again — a broken lookup failing closed is
indistinguishable from a real denial by response alone. It is not:

```
Retrieving permissions for user d12L59F... on document d9ad4750-...
Sprk.Bff.Api.Infrastructure.Caching.CachedAccessDataSource[0]
Permissions retrieved for document d9ad4750-...: AccessRights=None, CanPreview=False, CanDownload=False
Request finished ... 200 ... 11.8368ms
```

**`AccessRights=None` is a computed result, not an error.** The lookup ran through
`CachedAccessDataSource`, completed without exception, and returned an explicit "no rights" answer
which the endpoint rendered as false flags. So the pipeline resolves identity per-user (the two hashes
differ), evaluates access, and denies — under a credential chain rooted in MI-FIC.

Whether ralph *ought* to hold rights on that particular document is a Dataverse data question, not an
auth-plumbing one, and does not bear on this criterion.

Incidentally confirmed in the same capture: outbound Dataverse traffic targets
`https://spaarkedev1.crm.dynamics.com/api/data/v9.2/` — the expected dev environment.

### 5.5 ✅ SPE over OBO

| Route | ralph | Reading |
|---|---|---|
| `GET /api/obo/containers/{id}/children` | **200** `{"items":[],"nextLink":null}` | **the OBO→Graph→SPE call completed.** A permission failure would surface as an error, not an empty success |
| `GET /api/containers/{containerId}/drive` | 403 | consistent with §5.4 — this route DOES carry a resource id, so `ResourceAccessHandler` ran the real check and got `AccessRights=None`. Correct denial, same data reason as the document permissions result |

Container used: `b!21yLRdWEOAdkaWXskuRfByIRiz1S9kb_...` (the one `sprk_container` row in dev).

### 5.6 ✅ ROLLBACK RE-VERIFIED AT CREDENTIAL LEVEL — the evidence 032 depends on

032 may only delete the `staging` slot if config-only rollback is proven, because deleting the slot
retires "swap back" and leaves "reorder credentials" as the sole rollback. Proven here.

**Method.** Set `Graph__Credentials__Order__0=ClientSecret`,
`Graph__Credentials__Order__1=ManagedIdentityFederated` on the **slot only**, then restart to force the
confidential-client cache to rebuild, then exercise OBO.

**The app flagged its own deviation** — logged at `fail` level, unprompted:

```
fail: OrderedCredentialClientProvider[0]
  ADR-028 A4 DEVIATION: credential order ClientSecret > ManagedIdentityFederated places the client
  secret ABOVE a secret-free credential. This is valid only as a deliberate, temporary rollback.
  Restore the secret-free credential to the top once the incident is resolved.
info: Ordered credential selection active: ClientSecret > ManagedIdentityFederated.
```

That is the task-021/062 safety mechanism working: a rollback is possible, and it is impossible to
perform quietly.

**The proof, at credential level:**

```
13:30:16  Confidential client for 1e40baad-... built with credential ClientSecret.
13:30:16  OBO token exchange successful
```

against the MI-FIC run for comparison:

```
12:53:30  Confidential client for 1e40baad-... built with credential ManagedIdentityFederated.
12:53:30  OBO token exchange successful
```

**Why the HTTP 200s were not sufficient, and why this took an extra restart.** The abbreviated pass
returned 200 on `/api/me`, `/api/obo/containers/{id}/children` and `/api/workspace/layouts` under
secret-first ordering — but **a 200 does not prove the secret was used.** The whole point of an ordered
provider is that a failing first credential falls through to the next; had `ClientSecret` failed, the
provider would have silently used `ManagedIdentityFederated` and returned exactly the same 200. Only
the `built with credential ...` line distinguishes them, and it is emitted on cache miss only — hence
the deliberate restart. Accepting the 200s would have produced a confident and possibly false claim
about the one property 032 rests on.

**Restored, and verified restored:**

```
13:31:54  Ordered credential selection active: ManagedIdentityFederated > ClientSecret.
```

The `A4 DEVIATION` warning is **absent** after the revert — the positive signal, not merely the
absence of an error. `Graph__Credentials__Order__*` deleted; slot back to **213 app settings**;
`/healthz` 200.

**Conclusion for 032:** rollback is a config-only reorder requiring no redeploy, demonstrated on this
build, on this slot, at credential level. The 032 escalation trigger *"031's secret-first
re-verification did not pass — STOP before the slot deletion step"* does **not** fire.

### 5.7 Not yet exercised

SPE upload/download/preview · `dataverse.*` chat tool calls over SSE · Office add-in save flows ·
`/api/agent` · row-level authorization on `PermissionsEndpoints` · send-as-user email (authorized by the
owner, recipient = owner only) · long-running OBO · the Ciam and RagApiKey inbound schemes · **the
secret-first rollback re-verification, which 032 depends on**.
