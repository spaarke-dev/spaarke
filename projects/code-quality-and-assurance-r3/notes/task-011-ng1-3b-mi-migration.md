# #3b — Dataverse `ClientSecret` → Managed Identity migration (routed to task 011 / NG1 / Idea #742)

> **Source**: r3 RED-4 Fable-verified assessment (`notes/red-item-analyses/RED-4-dataverse-two-stack-ASSESSMENT.md`)
> **Owner track**: task 011 / NG1 (Idea #742) — the assess-then-decide credential-migration slice.

## Scope (verified, separable, constructor-scoped)

ADR-028 §24 mandates Managed Identity for server-outbound Dataverse. **Both** Dataverse impls still use a
client secret and are in unremediated violation:
- `DataverseServiceClientImpl` — `AuthType=ClientSecret` connection string from `API_CLIENT_SECRET`
  (`DataverseServiceClientImpl.cs:42-65`). Migrate the connection to `DefaultAzureCredential` / MI token provider.
- `DataverseWebApiService` — `ClientSecretCredential` from `Dataverse:ClientSecret`
  (`DataverseWebApiService.cs:40,56`). Swap the `TokenCredential` to `DefaultAzureCredential`.

**Plan of record**: a third Dataverse camp (Services/Ai raw-HTTP) was already migrated to MI in **AUTHV2-042
Phase C** (`appsettings.template.json:80`), which explicitly gates full secret removal on this #3b slice and
names the MI (`mi-bff-api-{env}`) + the operator step (register the UAMI as a Dataverse **Application User**).

## Binding operator prerequisites (per env — dev only for now; demo/prod decommissioned)

1. Register `mi-bff-api-{env}` as a Dataverse **Application User** with the required security role.
2. Grant **`prvActOnBehalfOfAnotherUser`** to the MI's app-user — REQUIRED for the impersonated WRITE path
   (`CommunicationModule.cs:288`); impersonation regression tests belong in this task.
3. **Do NOT remove** `Dataverse-ClientSecret` / `API_CLIENT_SECRET` until MI attribution is proven LIVE
   (never-remove until then). Keep the secret path as fallback during cutover.

## Relationship to the other RED-4 pieces

- **Independent of** the interim hardening (`dataverse-access-hardening`) — MI migration is constructor-scoped.
- **Feeds** the `dataverse-access-unification-r1` project — the single-impl target is MI-only.

---

## Attempt 1 (2026-08-17) — IN-SESSION FLIP CRASHED DEV STARTUP. Reverted. Redesign required.

**Operator prereqs re-verified live (all GREEN, dev/spaarkedev1):** UAMI `mi-bff-api-dev`
(clientId `5967251e-171c-46fe-a6c2-ef843c90309d`) is a registered Dataverse **Application User**, enabled, role
**System Administrator** (covers `prvActOnBehalfOfAnotherUser` + `prvReadEntity`); App Service has the UAMI
attached and `ManagedIdentity__ClientId` + `Graph__ManagedIdentity__ClientId` set to it;
`Graph__ManagedIdentity__Enabled=true`. **These prereqs are done — not wasted.**

**What was tried (saved as a patch): [`3b-mi-migration.patch`](3b-mi-migration.patch)** — both impls flag-gated on
`Graph:ManagedIdentity:Enabled`: MI-preferred (`DefaultAzureCredential` w/ `ManagedIdentityClientId`, matching
`GraphClientFactory.cs:117-132`), ClientSecret fallback, **secrets retained**. For `DataverseServiceClientImpl`
the `ServiceClient` was built via the async **token-provider constructor** (`new ServiceClient(Uri, Func<string,Task<string>>, useUniqueInstance:true)`). Build clean; full BFF suite 10,427/0/97.

**Failure:** deployed to dev → container **exited with code 134 (SIGABRT) at ~13 s during startup, twice**; the
deploy health check failed (24 attempts) → dev returned 503. Restored to the pre-MI ClientSecret build (redeploy
of master `08cd5b5b7`); dev healthy again. (The crashed build did intermittently recover on a 3rd cold-start
attempt — a 2-of-3 crash rate, unacceptable.)

**Root cause (specific + verified):** net10 H2 DI validation (`ValidateOnBuild`/`ValidateOnStart`) constructs the
singletons at boot. **`DataverseServiceClientImpl`'s ctor eagerly CONNECTS** the `ServiceClient` (checks
`IsReady`) — so a Dataverse connection happens on the startup thread. The MI **token-provider path acquires the
token sync-over-async during that synchronous singleton construction**, which intermittently aborts the process
(SIGABRT). The pre-MI **ClientSecret connection-string** path connects eagerly too but is reliable (no
sync-over-async token bridge).

**KEY ISOLATION — the two impls are NOT equally risky:**
- **`DataverseWebApiService` MI swap is LOW-RISK** — its ctor only constructs the `TokenCredential` and sets
  `BaseAddress`; it does **not** connect or acquire a token at construction (token is fetched lazily per request
  in `GetAccessTokenAsync`). This is the same shape as the working `GraphClientFactory` MI path. It almost
  certainly was **not** the crasher and can be migrated on its own.
- **`DataverseServiceClientImpl` MI swap is the HARD part** — the eager `ServiceClient` connect at ctor +
  `ValidateOnBuild` is what aborts.

### Startup-safe redesign for task 011 (do these; do NOT just re-apply the patch)

1. **Split the migration.** Migrate `DataverseWebApiService` (TokenCredential → `DefaultAzureCredential`) FIRST as
   its own low-risk change; verify live; then tackle the SDK impl separately.
2. **Make the SDK `ServiceClient` connection startup-safe** — pick one:
   - **Lazy connect**: don't connect in the ctor; build/connect the `ServiceClient` on first use (`Lazy<ServiceClient>`
     or an async init gate). Removes the eager MI connect from `ValidateOnBuild`. (Reconcile with the net10 H2
     intent — the DI graph still validates; only the network connect is deferred.)
   - **Synchronous, pre-acquired token**: acquire the MI token ONCE synchronously (`credential.GetToken(...)`, not
     `GetTokenAsync().Result`) before constructing `ServiceClient`, and feed a token provider that returns the
     cached token / refreshes on a background timer — avoid the sync-over-async bridge during construction.
3. **Deploy via staging slot + swap** (`Deploy-BffApi.ps1 -UseSlotDeploy`) so a bad boot is caught on the slot and
   never takes dev down. In-place dev deploy is what made this a live outage.
4. **Add a boot smoke** to the pipeline: after deploy, hit a Dataverse-backed path (or watch for the ctor
   "using Managed Identity … connected successfully" log) before declaring success.

**Lesson (attempt 1):** #3b is genuinely a careful **task-011 project**, not an in-session flip — the eager
ServiceClient-connect-at-`ValidateOnBuild` interaction with MI token acquisition is the crux and needs the
startup-safe redesign above. Secrets must stay until this is proven. The patch + this analysis are the starting point.

---

## Attempt 2 (2026-08-17) — startup-safe design WORKED, but UNMASKED the real blocker: MI→Dataverse token = HTTP 400

**Code (preserved in commit `d40f6c24a` + `3b-mi-migration.patch`):** applied the startup-safe redesign —
`DataverseServiceClientImpl` now defers the connect via `Lazy<ServiceClient>` (ctor validates config + builds the
connect factory only; no network at construction), the MI token is acquired **synchronously**
(`credential.GetToken` → `Task.FromResult` inside the ServiceClient token provider — no async bridge), and
`Dispose` guards on `IsValueCreated`. `DataverseWebApiService` MI swap unchanged (low-risk). Build clean; full BFF
suite 10,427/0/97.

**Result:** deployed in-place to dev → **boot succeeded, `/healthz` 200, no crash** — the lazy connect fixed the
attempt-1 SIGABRT (startup no longer touches Dataverse). ✅ **The startup-safe design is correct and is the right
code for #3b.** BUT the first Dataverse request (privilege check → `TryUnwrapServiceClient` →
`OrganizationService` → the lazy MI `ServiceClient` connect) failed:

```
DataverseConnectionException: Failed to connect to Dataverse
 ---> ExternalTokenManagement Authentication Requested but not configured correctly. 003
 ---> Azure.Identity.AuthenticationFailedException: ManagedIdentityCredential authentication failed (AppService source)
 ---> MSAL MsalServiceException  ErrorCode: managed_identity_request_failed  StatusCode: 400
```

**Initial (WRONG) hypothesis:** "environmental — the MI can't mint a Dataverse token." That was disproven — see
the Resolution below. The 400 was a **scope-derivation bug in the ServiceClient token provider**, not an Azure/MI
issue.

Dev was restored to the ClientSecret build while investigating; `/healthz` 200 and Dataverse `ServiceClient` paths
worked again. Secrets untouched throughout.

---

## ✅ RESOLVED (2026-08-17) — the 400 was a scope bug; MI→Dataverse now works end-to-end

**Diagnosis (instrument-and-observe, temporary anonymous probe endpoints, since removed):**
1. `credential.GetTokenAsync`/`GetToken` for `https://spaarkedev1.crm.dynamics.com/.default` → **`ok:true`, valid
   token** (both sync AND async). So the UAMI CAN mint a Dataverse token — the "environmental" hypothesis was wrong.
2. A probe that built a **real `ServiceClient` with a capturing token provider** revealed the culprit: the Dataverse
   `ServiceClient` invokes the token-provider function with the **full SOAP endpoint URL** as its `resourceUri`:
   ```
   https://spaarkedev1.crm.dynamics.com/XRMServices/2011/Organization.svc/web?SDKClientVersion=9.2.49.17970
   ```
   The attempt-1/attempt-2 provider derived the scope as `resourceUri.TrimEnd('/') + "/.default"` → a **garbage
   AAD resource** (path+query included) → the MI endpoint correctly returned **HTTP 400
   `managed_identity_request_failed`**. Not the MI, not the grant, not sync-vs-async, not Dataverse+MI — a one-line
   scope bug.

**Fix (`DataverseServiceClientImpl`):** ignore the `resourceUri` the provider is handed; request the token for the
Dataverse **environment ROOT authority**:
```csharp
var dataverseScope = new Uri(dataverseUrl).GetLeftPart(UriPartial.Authority).TrimEnd('/') + "/.default";
// → https://spaarkedev1.crm.dynamics.com/.default   (proven to mint a token)
```
Kept the startup-safe **lazy connect** (out of `ValidateOnBuild`). `DataverseWebApiService` already derived its
scope from the authority (`{_apiUrl minus /api/data/v9.2}/.default`), so it had no bug — only the SDK
ServiceClient provider did (because ONLY it is handed the SOAP URL).

**Proven live on dev (2026-08-17):** deployed the fixed MI build (flag `Graph:ManagedIdentity:Enabled=true`):
`/healthz` 200; `/api/dataverse/metadata/account` + `/sprk_matter` → **200** (SDK `ServiceClient` → MI);
`/api/v1/field-mappings/profiles` → **200** (WebApi impl → MI). Both Dataverse camps now authenticate to dev
Dataverse via the UAMI `mi-bff-api-dev`. Build + full BFF suite 10,427/0/97. Temporary diagnostic endpoints
removed (verified 404). The updated `3b-mi-migration.patch` is the final working implementation.

**Remaining (operator-gated, separate step):** now that MI attribution is proven live, `API_CLIENT_SECRET` /
`Dataverse-ClientSecret` MAY be removed from Key Vault — but that is a deliberate, separately-gated action
(keep the ClientSecret fallback until the operator chooses to remove it). `BFF-API-ClientSecret` stays regardless
(OBO). Per-env repeat when demo/prod are re-provisioned (register the UAMI as a Dataverse App User there too).

**Lesson:** the "needs Azure investigation" framing was premature — instrumenting the actual token request
(what resource is really being asked for) found a trivial code bug in minutes. Always capture the real
`resourceUri`/scope before concluding a managed-identity issue is environmental.
