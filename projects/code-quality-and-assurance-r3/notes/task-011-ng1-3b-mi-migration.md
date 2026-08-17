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

**Root cause — ENVIRONMENTAL, not code.** The App Service managed identity returns **HTTP 400
(`managed_identity_request_failed`)** when requesting a token for the **Dataverse** resource
(`https://spaarkedev1.crm.dynamics.com`), while it successfully mints **Graph** tokens (the MSAL cache showed
multiple cached tokens under UAMI partition `5967251e…_managed_identity_AppTokenCache`). The failing request uses
the **identical scope + UAMI-pinned `DefaultAzureCredential`** as the app's canonical `DataverseHttpServiceBase`
(scope `{dataverseUrl}/.default`, credential from `ManagedIdentityCredentialFactory`) — so no code change fixes it.
There is **no proven working MI→Dataverse path anywhere in this App Service** (the "Phase C already migrated
Services/Ai Dataverse to MI" assumption is unverified/likely never exercised at runtime — that camp injects the
same credential and would hit the same 400). MI correlation ID for Azure support:
**`f8b7a8ce-4606-4f38-8ba4-f025c1c3392c`**.

Dev was restored to the ClientSecret build (redeploy of master `08cd5b5b7`); `/healthz` 200 and Dataverse
`ServiceClient` paths work again (`/api/dataverse/metadata/account` → 200). Secrets untouched.

### #3b is BLOCKED on an Azure/MI investigation (not code). To unblock:

Determine **why the App Service MI cannot obtain a Dataverse token (HTTP 400)** while Graph works. Candidate lines
of investigation (operator / Azure support):
1. **MSI endpoint variant** — is the App Service on the legacy `MSI_ENDPOINT` vs IMDS `IDENTITY_ENDPOINT`? Some
   legacy App Service MSI endpoints mishandle certain resource strings. Check the resource actually sent
   (`https://spaarkedev1.crm.dynamics.com` — try with/without trailing slash).
2. **The 400 response body** — capture it (Azure support with the correlation ID above) for the real reason
   (invalid_resource / unauthorized_client / identity-not-found).
3. **UAMI token-request eligibility for the Dataverse resource** in this tenant/region.
4. Compare against a **known-good MI→Dataverse App Service** elsewhere (if any) for config deltas.

**Once the token 400 is resolved**, the code is ready: apply `3b-mi-migration.patch` (commit `d40f6c24a`) — it is
the correct startup-safe implementation — deploy, verify the connect log + a Dataverse read, then (separately,
operator-gated) remove `API_CLIENT_SECRET` / `Dataverse-ClientSecret`.

**Bottom line:** the code side of #3b is solved (startup-safe lazy connect + sync token, preserved). The remaining
blocker is a **live Azure managed-identity configuration issue** (MI can't mint a Dataverse token) that must be
diagnosed at the platform level before the migration can go live. This is very likely why #3b sat un-done: it's
not a code task, it's an MI-enablement task for Dataverse in the App Service.
