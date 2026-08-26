# DS-8 — UAMI-as-Dataverse-App-User Maturity Study (L2 registry-write credential model)

> **Produced**: 2026-08-18 by design-study sub-agent (research + design only; no source edits).
> **Question**: For L2 control-plane privileged writes to the ADMIN Dataverse env (`spaarkedev1.crm.dynamics.com`) — registry `sprk_dataverseenvironment` create/read, `sprk_setupstatus` PATCH (H13 Ready writer), `sprk_currentrunid` run-guard upsert (I5) — is **Path X (L2 UAMI registered as a Dataverse Application User, tokens via managed identity)** mature enough to build on today, or is **Path Y (client secret via KV)** still safer?
> **Answer up front**: **Path X. It is mature, first-party-documented, already half-implemented in this repo (H10), pre-planned in the L2 code's own file headers, and the only ADR-028-compliant option.** Path Y would create a NEW documented ADR-028 violation, an operational rotation burden, and a muddled audit identity. Detail + evidence below.
> **Companion studies**: DS-5 (C5.3/C5.6/C5.7/C5.8 are decision-gated on exactly this), DS-1/DS-2 (dispatcher + runtime env), r1-gap-analysis C1.4.

---

## 1. Current implementation state — who authenticates to Dataverse today, and how

Grep-verified across the worktree (all paths absolute under `src/server/`):

| Consumer | Auth today | Path | Evidence |
|---|---|---|---|
| `shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` (SDK `ServiceClient`, BFF's primary stack) | Connection string with **ClientSecret** (BFF app-reg) | **Y** | `:53` "Initializing Dataverse ServiceClient with ClientSecret"; `:65` `new ServiceClient(connectionString)` |
| `shared/Spaarke.Dataverse/DataverseWebApiService.cs` (parallel Web-API stack) | **`ClientSecretCredential`** | **Y** | `:66` |
| `shared/Spaarke.Dataverse/DataverseWebApiClient.cs` | **Dual**: `ClientSecretCredential` when configured, else `DefaultAzureCredential` (optional `ManagedIdentityClientId` pin) | X+Y capable | `:44–53` |
| `shared/Spaarke.Dataverse/DataverseAccessDataSource.cs` | **Dual**: secret when configured, else DI-injected UAMI-pinned `TokenCredential` | X+Y capable | `:49–76` |
| L2 `Handlers/DataverseAppUserGraphParity/*` (H10 — creates App Users on **customer** envs) | **`DefaultAzureCredential`** (ADR-028 MI-outbound; §4D I5 explicit tenant) | **X** | `DataverseWebApiAppUserCreator.cs:7` header; `H11`/`GraphRest*` same idiom |
| L2 `Handlers/DataverseEnvCreation/IDataverseHealthProbe` (H5 WhoAmI probe) | `DefaultAzureCredential` | **X** | file header `:7` |
| L2 H7 env-var writer / H6 solution import options | **`ClientSecretCredential`** (BFF app-reg), KV wiring deferred "Wave C5" — **unprovisioned** | Y (inert) | `Program.cs:440–443, 464, 483–486` |
| L2 `Concurrency/DataverseRegistryConcurrencyStore` (I5 guard → ADMIN env) | **`ClientSecretCredential`** (BFF app-reg) — `Enabled=false` default, config never provisioned, **dummy KV secret** seeded live (bug #18) | Y (inert) | `CustomerRunGuardOptions.cs:11–19` header |
| L2 registry read client + H13 Ready writer | **Placeholders** (`NullDataverseEnvironmentRegistryClient`, no-op `DataverseRegistrySetupStatusUpdater`) — C1.4 greenfield | none yet | gap analysis A15/A16 |

**Dominant pattern**: everything L2 built new in r1 is `DefaultAzureCredential` (Cosmos, Service Bus, Graph, Dataverse health probe, H10/H11 Dataverse Web API). The only secret-shaped L2 code is the guard + H6/H7 option classes — and **none of that secret config was ever provisioned**; it is dead-on-arrival Path Y scaffolding. Crucially, `CustomerRunGuardOptions.cs:21–27` carries an explicit **"FUTURE MIGRATION"** block: *"When the L2 App Service's UAMI is granted a systemuser record on the admin env (paired with an app-user creation script similar to H10's pattern for customer envs), swap the ClientSecretCredential for DefaultAzureCredential and delete the ClientId/ClientSecret fields."* Path X is the code's own declared endgame.

## 2. Shipped vs aspirational (r3 reality check)

- **r3 task 060 dropped the *separate vestigial* Dataverse S2S app-reg only** — grep-clean confirmed: `scripts/Register-EntraAppRegistrations.ps1:867–872` and `scripts/Test-EntraAppRegistrations.ps1:10–11, 262–264` carry removal tombstones ("zero code consumers; Dataverse S2S access consolidated onto the BFF app registration"); no live consumer references `spaarke-dataverse-s2s-*` or `Dataverse-S2S-*` secrets.
- **What replaced it: nothing MI-shaped for the BFF.** The BFF's Dataverse access still runs as the **BFF app-reg with a client secret** (`Dataverse-ClientSecret`), registered as the *single* Dataverse Application User with System Administrator (`docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md:347`). The **#3b `ClientSecret`→MI migration is explicitly deferred** to the NG1/task-011 track (Idea #742) — r3 handoff §5 and `projects/code-quality-and-assurance-r3/notes/red-item-analyses/RED-4-ng1-dataverse-stack-unification.md:23–24`, which states flatly: *"the BFF's own Dataverse path is still secret-based (ADR-028 §24 mandates MI — the secret paths are violations)"*.
- **So "r3's UAMI-App-User pattern" is a direction, not a shipped BFF mechanism.** What IS shipped and production-shaped is **L2's own H10**: `DataverseWebApiAppUserCreator` implements the full idempotent UAMI-App-User registration (find `systemusers?$filter=applicationid eq {id}` → POST `/systemusers` with `applicationid` + root BU → associate security role via `systemuserroles_association/$ref`), authenticated via `DefaultAzureCredential`. The repo already contains the exact code Path X needs — pointed at customer envs; the admin env is the same operation with a different URL.
- **`Spaarke.Dataverse` shared lib**: supports both paths (`DataverseWebApiClient` / `DataverseAccessDataSource` fall back to `DefaultAzureCredential`); the SDK `ServiceClient` stack is secret-only as wired. **L2 does not use the shared lib at all** — its Dataverse touches are raw `HttpClient` + bearer, so the shared-lib limitation (see §3 SDK note) does not constrain L2.
- **Known issues**: none found for the MI→Dataverse data path in-repo. The one documented MI failure in this codebase is ADR-028 exception **E-2 (Azure OpenAI data plane 401)** — a Cognitive Services RBAC issue, not a Dataverse one; Dataverse authorization is security-role-based (no RBAC data-actions layer), so that failure class does not transfer.

## 3. Microsoft platform current-state (online research, 2026)

1. **MI-backed Application Users are first-party supported and documented.** Microsoft Learn "Manage application users in the Power Platform admin center" (ms.date **2026-04-03**) states verbatim: *"In addition to entering the Application Name or Application ID, you can also enter an **Azure Managed Identity Application ID**. For Managed Identity, do not enter the Managed Identity Application Name, use the Managed Identity Application ID instead."* Also: one application user per Entra application per environment; roles editable; app users bypass security-group gating.
   → https://learn.microsoft.com/en-us/power-platform/admin/manage-application-users
2. **Scriptable via PAC CLI**: `pac admin assign-user --environment <url> --user <applicationId> --role <role> --application-user [--business-unit <id>]` creates the app user + assigns the role (default role is System Administrator — override it; see §4). → https://learn.microsoft.com/en-us/power-platform/developer/cli/reference/admin
3. **Token acquisition**: a managed identity requests a token with scope `https://{org}.crm.dynamics.com/.default`; Dataverse accepts it once the systemuser row exists. This is the pattern community-documented since 2021 and unchanged: → https://dreamingincrm.com/2021/11/16/connecting-to-dataverse-from-function-app-using-managed-identity/ · https://blog.yannickreekmans.be/secretless-applications-use-azure-identity-sdk-to-access-data-with-a-managed-identity/
4. **SDK support**: `Microsoft.PowerPlatform.Dataverse.Client.ServiceClient` still does **not** accept `TokenCredential` natively; the sanctioned bridge is the **`tokenProviderFunction` constructor overload** (`new ServiceClient(instanceUrl, async uri => (await cred.GetTokenAsync(new TokenRequestContext(new[]{ uri + "/.default" }))).Token, true)`). → https://learn.microsoft.com/en-us/dotnet/api/microsoft.powerplatform.dataverse.client.serviceclient.-ctor?view=dataverse-sdk-latest — Irrelevant to L2 (raw HttpClient); load-bearing for NG1 #3b later.
5. **Disambiguation — "Power Platform managed identity" (plug-ins) is a DIFFERENT feature**: `managedidentities` table + FIC (`credentialsource`, `subjectscope`, v2 DN-hash subject identifiers) lets Dataverse **plug-ins** call out to Azure secretlessly. It is *outbound from Dataverse*, not inbound; do not confuse its `pac managed-identity` verbs (which don't support UAMI) with app-user creation. → https://learn.microsoft.com/en-us/power-platform/admin/set-up-managed-identity
6. **No 2025–2026 breaking changes found** to the inbound MI-App-User story; the 2026-04 doc refresh *strengthened* it (explicit MI Application ID guidance in PPAC). The adjacent NEW capability (see §7 Path Z) is **managed identities as Federated Identity Credentials on Entra apps — now GA**: → https://devblogs.microsoft.com/identity/access-cloud-resources-across-tenants-without-secrets-ga/ · https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-config-app-trust-managed-identity
7. **Rate limits / throttling**: Dataverse service-protection limits are evaluated **per user (per systemuser), per web server** — the credential type (MI token vs secret token) is invisible to them. No documented throttling difference. A *separate* systemuser for L2 (vs piggybacking the BFF app-reg's systemuser) actually gives L2 its own service-protection budget instead of sharing the BFF's. → https://learn.microsoft.com/en-us/power-apps/developer/data-platform/api-limits

## 4. Setup mechanics (admin env, one-time per environment)

Exact sequence to register the L2 UAMI (`mi` of `platform-controlplane.bicep`, client id already emitted as `ManagedIdentity:ClientId`/`AZURE_CLIENT_ID`):

1. **Security role (once)**: create a scoped role, e.g. `Spaarke Provisioning Registry`, in the admin env — org-level Read/Write/Create/Append on `sprk_dataverseenvironment` + prvReadUser etc. minimum basics. **Do NOT default to System Administrator** (pac's default): the registry writes touch exactly one custom table; least privilege is cheap here and this is the control plane's identity. Ship the role in a small managed solution (repeatable per env) or a one-shot metadata script.
2. **App User**: either
   - `pac admin assign-user --environment https://spaarkedev1.crm.dynamics.com --user <uamiClientId> --role "Spaarke Provisioning Registry" --application-user`, or
   - Web API (the H10 idiom, reusable verbatim): `GET /api/data/v9.2/systemusers?$filter=applicationid eq <uamiClientId>` → if absent `POST /systemusers { "applicationid": "<uamiClientId>", "businessunitid@odata.bind": "/businessunits(<rootBU>)" }` → `POST /systemusers(<id>)/systemuserroles_association/$ref`.
3. **Idempotent**: yes — find-by-`applicationid` first (exactly what `DataverseWebApiAppUserCreator.cs:89–91` does); role association is check-then-add (`:241–248`). Safe to re-run every provisioning cycle.
4. **Admin consent**: **not required.** Dataverse authorizes via security roles, not Entra permission grants; creating the systemuser row IS the authorization act. No Entra app-permission or consent ceremony exists for this path (a UAMI has no consentable app permissions against Dataverse). The only privilege needed is that the *caller creating* the app user is a Dataverse admin (operator identity or an existing SysAdmin app user) — a bootstrap fact, same as Path Y's secret-seeding would be.
5. **Bicep?** No — app-user creation is Dataverse **data plane**, not ARM. It lands in the `Grant-ControlPlaneIdentity.ps1` script DS-5 already scoped for C5.8 (which needs the same script for Graph app-role grants — also not ARM-expressible). One script, two grants, runbook-invoked; optionally wrapped in a Bicep `deploymentScript` later, but a plain script step is the honest v1.

## 5. Failure modes

| Failure | Behavior | Mitigation |
|---|---|---|
| UAMI deleted/disabled | New token acquisitions fail loudly (`CredentialUnavailableException`); **already-issued tokens can remain valid up to ~24h** (Azure MI token caching at IMDS + Azure.Identity in-process cache — https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/managed-identities-faq) | UAMI is Bicep-owned in the L2 stamp — deletion is a stamp-teardown event, not drift. Accept the ≤24h tail; registry writes fail-closed (handlers report InfraFault, §4C Resumable) |
| SystemUser row deleted/deactivated in admin env | Immediate 401/403 on next call — loud, unambiguous | Idempotent `Grant-ControlPlaneIdentity.ps1` re-run restores in seconds; add the systemuser existence check to H13's T2-style live probes for the control plane itself |
| Security role unassigned | 403 `prvCreate...` privilege error — loud | Same re-run; role association is part of the idempotent script |
| "Rotation" | **None exists** — the platform rotates the MI's underlying credential automatically; no operator action, no expiry cliff, no KV secret to age out | This is the point |
| Token refresh | `DefaultAzureCredential`/Azure.Identity handles caching + proactive refresh; no code | — |
| Cross-tenant | **MI tokens are home-tenant-only.** L2 UAMI (Spaarke tenant) → `spaarkedev1` (Spaarke tenant): same-tenant, works. L2 writing a **customer-owned-tenant** Dataverse directly with its UAMI: impossible — and correctly so; registry writes are admin-env-only, and customer-env writes belong to the handler credential story (H5–H7 target spaarke-hosted envs in the Spaarke tenant; the customer-owned Model-2 case needs the multitenant app-reg — see Path Z) | The same-tenant constraint is an *enforcement* of the isolation design, not a limitation for this use case |

## 6. Blast radius of each path

**Path X (recommended)** — net code DELTA is small and mostly deletion:
- `Concurrency/CustomerRunGuardOptions.cs` — delete `ClientId`/`ClientSecret` + their `Validate()` clauses (the header already instructs this).
- `Concurrency/DataverseRegistryConcurrencyStore.cs` — swap `ClientSecretCredential` → the module-shared UAMI-pinned `DefaultAzureCredential` (same factory idiom as `CosmosModule.cs:126–131`).
- **C1.4 registry client (greenfield either way)** — build MI-native from day one; H13's `DataverseRegistrySetupStatusUpdater` + `NullDataverseEnvironmentRegistryClient` swaps ride on it.
- Bicep: `platform-controlplane.bicep:104–105` + `controlplane-app-service.bicep:137–141` — **delete** the `dataverseClientSecretName` param + KV-ref emission (resolves C5.3 by removal; kills dummy-secret bug #18 at source). C5.6 guard config shrinks to `TargetDataverseUrl` + `Enabled`.
- New: `Grant-ControlPlaneIdentity.ps1` (S–M; shares C5.8's script) + scoped security role.
- ≈ 5–6 existing files touched, one new script, **net config removed**.

**Path Y** — seed a REAL BFF app-reg secret into the L2 KV binding; provision `CustomerRunGuard__ClientSecret` + (later) `SolutionImport__ClientSecret`/`EnvVarValues__ClientSecret` KV refs; author + own a 90-day rotation runbook forever; H4 keeps the rung. Zero L2 code change — but it **(a)** creates a NEW ADR-028 §MUST violation ("MUST use DefaultAzureCredential... NOT ClientSecretCredential") requiring a §6.5 documented-exception PR against the ADR's MI-exceptions list, **(b)** attributes every L2 registry write to the **BFF's** systemuser (audit says the BFF wrote the registry — false), and **(c)** widens the blast radius of a BFF secret leak/rotation-miss to the control plane.

**Two scope corrections to the question's framing** (both matter):
1. *"the current dummy `Dataverse-ClientSecret` KV binding is deleted"* — delete the **L2 stamp's binding/param** (Bicep), but the **KV secret itself MUST stay**: the BINDING never-delete rule (r3 handoff §4a) protects `Dataverse-ClientSecret` because the **BFF** shared-lib path still consumes it until NG1 #3b. Path X removes L2's *dependency* on it, not the secret.
2. *"H4 skips the Dataverse-secret rung"* — only for **L2's own credential**. H4 seeds secrets for the **customer** stamp, whose BFF is still secret-based until #3b (explicitly NOT r1's migration, r3 handoff §5). H4's customer-side `Dataverse-ClientSecret` seeding stays until NG1 retires it.

**Rotation ownership**: Path X = nothing to own. Path Y = a scheduled rotation + KV update + App Service KV-ref refresh, per environment, forever — precisely the class of drift the auto-memory note ("fix drift at discovery; later means lost + perpetuated") warns about.

## 7. Best-practice framing

- **Microsoft's recommendation today** is unambiguous: managed identities over secrets wherever the compute is Azure-hosted and the target is same-tenant (Secure Future Initiative; Azure Identity guidance; the 2026-04 PPAC doc explicitly accommodating MI Application IDs for app users). ADR-028 already codifies this repo-side as a MUST.
- **Reference implementations** (Azure Functions/App Service → Dataverse via MI App User) have been the canonical community pattern since 2021 and are what Microsoft's own ecosystem writers use; the secret pattern survives mainly in cross-tenant and legacy scenarios.
- **Path Z exists but is for a different problem**: **managed identity as a Federated Identity Credential on an Entra app** (GA — https://devblogs.microsoft.com/identity/access-cloud-resources-across-tenants-without-secrets-ga/) lets a workload exchange its UAMI token for an **app-reg** token — including a *multitenant* app-reg consented into customer tenants (worked Dataverse example: https://dreamingincrm.com/2025/02/06/secretless-cross-tenant-access-logic-apps-dataverse/). This is NOT better than Path X for admin-registry writes (extra hop, attribution collapses onto the app-reg, MI must be same-tenant as the app anyway). It IS the right future answer for (a) the **customer-owned-tenant Model-2** handler credential problem and (b) making NG1 #3b secretless without per-env MI App Users for the BFF. Recommend: note it in the C1.4 design as the sanctioned cross-tenant escape hatch; do not build it in r1.

## 8. Recommendation

**Path X — register the L2 UAMI as a Dataverse Application User on the admin environment with a scoped custom security role; all L2 registry reads/writes via `DefaultAzureCredential` pinned to the L2 UAMI. Build C1.4 MI-native from day one; never provision the Path Y secrets.**

It is best-practice for this system because: it's the only ADR-028-compliant option (Path Y requires filing a new documented violation for a scenario Microsoft fully supports secretless — an indefensible exception); the platform support is mature and first-party-documented as of 2026-04; the repo already ships the exact registration code (H10) and the L2 code headers pre-declare this migration; it gives L2 a **distinct, auditable Dataverse identity** with its own service-protection budget and least-privilege role instead of impersonating the BFF as System Administrator; and it deletes operational surface (no rotation, less config, dummy-secret bug #18 dies at source) rather than adding it.

**Residual risks**: (1) ≤24h MI token-cache tail after a UAMI disable — acceptable; registry writes fail closed. (2) Bootstrap dependency — an admin-privileged caller must run the one-time grant script per environment; identical bootstrap exists for Path Y's secret seeding. (3) The scoped-role privilege list needs one careful authoring pass (a missing privilege surfaces as a loud 403, not silence). None material.

**The one thing that would flip it**: if the L2 registry client were required to authenticate to a **customer-owned-tenant** Dataverse with the same credential path. Plain UAMI cannot cross tenants — that requirement would force the multitenant-app-reg route (and then the answer is **Path Z (FIC), still not Path Y**). Per the design (§4D; registry lives only in the admin env), this requirement does not exist.

**Rollout plan** (sequenced with DS-5's ordering; all S–M):
1. Author the scoped security role + `Grant-ControlPlaneIdentity.ps1` (idempotent: role-ensure → app-user-ensure via the H10 Web-API idiom or `pac admin assign-user --application-user` → role-associate → `WhoAmI` verify). Fold in C5.8's Graph app-role grants — one identity script for the control plane.
2. Run it against `spaarkedev1`; verify with `scripts/debug/check-app-user-roles.ps1`-style query + a canary registry-row upsert attributed to the UAMI's systemuser.
3. Build C1.4 registry client MI-native (raw HttpClient + `DefaultAzureCredential(ManagedIdentityClientId)`, `{adminEnvUrl}/.default` scope — same idiom as `DataverseWebApiHealthProbe`); swap the two H13/H0.5 placeholders onto it.
4. Refactor guard: delete `ClientId`/`ClientSecret` from `CustomerRunGuardOptions` + store; update `Validate()`; flip `Enabled=true` in config once the dispatcher can drive a 409-provoking run (DS-5 C5.6 sequencing).
5. Bicep: remove `dataverseClientSecretName` + its KV-ref emission from the L2 stamp (with C5.1/C5.2 in the same template pass, per DS-5's redeploy-safety ordering). Leave the `Dataverse-ClientSecret` KV **secret** untouched (BINDING never-delete; BFF consumer).
6. Add "L2 systemuser exists + role assigned" to the H13 live-probe set and the operator runbook's verification steps.

---

### Source URLs (all fetched/verified 2026-08-18)

- https://learn.microsoft.com/en-us/power-platform/admin/manage-application-users (ms.date 2026-04-03 — MI Application ID accepted for app users)
- https://learn.microsoft.com/en-us/power-platform/developer/cli/reference/admin (`pac admin assign-user --application-user`)
- https://learn.microsoft.com/en-us/power-platform/admin/set-up-managed-identity (plug-in FIC feature — disambiguation)
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.powerplatform.dataverse.client.serviceclient.-ctor?view=dataverse-sdk-latest (no native TokenCredential; tokenProviderFunction overload)
- https://learn.microsoft.com/en-us/power-apps/developer/data-platform/api-limits (service-protection limits are per-user)
- https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/managed-identities-faq (MI token caching ≤24h)
- https://devblogs.microsoft.com/identity/access-cloud-resources-across-tenants-without-secrets-ga/ (MI-as-FIC GA — Path Z)
- https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-config-app-trust-managed-identity (Path Z mechanics)
- https://dreamingincrm.com/2021/11/16/connecting-to-dataverse-from-function-app-using-managed-identity/ (canonical community pattern, 2021)
- https://dreamingincrm.com/2025/02/06/secretless-cross-tenant-access-logic-apps-dataverse/ (Path Z worked Dataverse example)
- https://blog.yannickreekmans.be/secretless-applications-use-azure-identity-sdk-to-access-data-with-a-managed-identity/ (secretless Dataverse pattern)

*Design study only. No code, config, Azure state, or `.claude/**` files modified.*
