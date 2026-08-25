# Spaarke Auth v4 — Zero-Secret BFF Confidential Credential — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-08-19
> **Source**: [`design.md`](design.md)
> **Epic**: AUTH & SSO (#426) · **Risk**: HIGH (OBO = all delegated user auth; fails closed)
> **Supporting evidence**: [`notes/PHASE-0-LIVE-VERIFICATION.md`](notes/PHASE-0-LIVE-VERIFICATION.md) ·
> [`notes/CREDENTIAL-INVENTORY.md`](notes/CREDENTIAL-INVENTORY.md) ·
> [`notes/RESEARCH-FINDINGS.md`](notes/RESEARCH-FINDINGS.md) ·
> [`notes/TENANCY-AND-CREDENTIALS.md`](notes/TENANCY-AND-CREDENTIALS.md)
> **Cross-project**: [`notes/PROVISIONING-CHANGE-REQUEST.md`](notes/PROVISIONING-CHANGE-REQUEST.md) ·
> [`notes/COORDINATION-DATAVERSE-ACCESS-UNIFICATION.md`](notes/COORDINATION-DATAVERSE-ACCESS-UNIFICATION.md)

---

## Executive Summary

Replace the BFF's client secret with a **Managed-Identity-issued federated credential (MI-FIC)** across every
BFF-identity confidential client, including the OBO (delegated user auth) paths that prior projects concluded
could never be secret-free. Eliminate `BFF-API-ClientSecret` from all six of its paths, migrate Power BI to a
managed-identity principal, retire the Azure first-party API keys that have an MI alternative, and leave
**CI-enforced forcing functions** behind so a secret-bearing confidential client cannot be reintroduced silently.

ADR-028 **Amendment A4** + transitional exception **E-3** were applied 2026-08-17. The dev MI-FIC
(`mi-bff-api-dev-assertion`) was created 2026-08-19; all platform prerequisites are verified live.

---

## Scope

### In Scope

- The **6 BFF-identity confidential clients** that authenticate as app registration `SDAP-BFF-SPE-API`
  (`1e40baad-…`): `GraphClientFactory` (OBO), `DataverseAccessDataSource` (OBO + app-only), `DataverseUserClient`
  (OBO), `AgentTokenService` (OBO), plus the residual `ClientSecretCredential` fallbacks in
  `DataverseServiceClientImpl` and `DataverseWebApiService` / `DataverseWebApiClient`.
- A single **client-assertion provider** seam, with ordered credential selection (MI-FIC → KV certificate → dev
  secret) and rollback by configuration.
- Two **pre-existing defects** that block the migration and are independently correct: the MI-flag gating defect
  and the DI-lifetime hazard.
- **Power BI** — migrated to a **user-assigned managed identity as the Power BI principal** (owner decision;
  Microsoft's documented model), retiring `PowerBi:ClientSecret`.
- **Group 2 non-Entra credentials** — Azure first-party services running on API keys where Entra/MI auth is
  available (§ FR-E).
- **Forcing functions** — ArchTest ban, credential census, startup assertion (§ FR-F).
- Removal of `BFF-API-ClientSecret` from app settings and Key Vault, **including the lowercase
  `bff-api-client-secret` alias**, plus reconciliation of the 11 scripts and ~25 documents that reference it.
- Config validator relaxation where the secret is currently `[Required]`.

### Out of Scope

- **Environments other than dev.** `spaarke-bff-prod` is **Stopped**; prod/demo are decommissioned per the r3
  handoff. Artifacts stay environment-parameterised, but only dev is executed. *(Owner decision.)*
- **Per-customer SpeAdmin credentials** (ADR-028 **E-1**) — `SpeAdminTokenProvider`, `SpeAdminGraphService`.
  These authenticate *other applications* (per-customer SPE container-type owning apps), not the BFF identity.
- **`CiamGraphClientFactory`** — already secret-free via a Key Vault certificate; the in-repo precedent.
- **Inbound auth** — JWT validation, `AddMicrosoftIdentityWebApi`, the CIAM scheme, the `RagApiKey` scheme,
  webhook HMAC. In scope only as a **regression surface to prove unaffected** (NFR-05).
- **Group 1 third-party API keys** — Bing Search, LlamaParse. A key is the only mechanism these vendors offer.
  One hygiene fix only (FR-E7).
- **Group 3 inbound HMAC / clientState keys** — `Communication:WebhookSigningKey`, `WebhookClientState`,
  `EmailProcessing:WebhookSigningKey`, tracking-footer signing key. Inbound validation, no MI equivalent,
  correctly designed today. **Explicitly closed, not deferred** — recorded so a future audit does not re-open them.
- **Plaintext secrets in Dataverse columns** (`BaseProxyPlugin.cs:121-124`, `SimpleAuthHelper.cs:19-26`). Same
  defect class — `AuthType=2 (ManagedIdentity)` is declared in the option set and **throws "not supported"** — but
  a different plane (Dataverse plugins). Filed as a **sibling issue**, not a generic follow-up.
- **Certificate provisioning automation.** No Spaarke deployment shape requires one. **Dropped, not deferred.**

### Affected Areas

| Path | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Infrastructure/Auth/` | New `ManagedIdentityAssertionProvider`; extends the existing `ManagedIdentityCredentialFactory` seam |
| `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` | OBO CCA credential; the `AZURE_CLIENT_ID ?? API_APP_ID` hazard |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/Dataverse/DataverseUserClient.cs` | OBO CCA credential |
| `src/server/api/Sprk.Bff.Api/Api/Agent/AgentTokenService.cs` | OBO CCA credential + DI lifetime |
| `src/server/api/Sprk.Bff.Api/Api/Reporting/` | Power BI → UAMI principal (both services) |
| `src/server/shared/Spaarke.Dataverse/` | `IClientAssertionProvider` contract; `DataverseAccessDataSource`, `DataverseServiceClientImpl`, `DataverseWebApiService`, `DataverseWebApiClient` |
| `src/server/api/Sprk.Bff.Api/Configuration/` | `DataverseOptions`, `GraphOptionsValidator`, `AgentTokenOptions`, `PowerBiOptions` |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/` | `SpaarkeCore.cs`, `AgentModule.cs`, `AiModule.cs`, `AiSafetyModule.cs` |
| `tests/Spaarke.ArchTests/`, `tests/integration/seam/` | Forcing functions + credential-seam coverage |
| `scripts/` (11 files), `docs/` (~25 files) | Operational estate reconciliation |

---

## Requirements

### Workstream A — Prerequisites (independently correct; land first)

**FR-A1** — Fix the MI-flag gating defect.
`DataverseAccessDataSource.cs:53` and `DataverseWebApiClient.cs:42` never read `Graph:ManagedIdentity:Enabled`;
secret *presence* alone selects the secret path. On dev — where `API_CLIENT_SECRET` is set because OBO needs it —
**both run on the client secret today despite MI being enabled**.
*Acceptance*: both sites read the flag; with `Graph:ManagedIdentity:Enabled=true` and a secret present, both
resolve the MI credential; with the flag false and a secret present, both resolve the secret; with the flag true
and no secret, both resolve MI. Covered by a seam test asserting the selected credential type per matrix.

**FR-A2** — Fix the DI-lifetime hazard.
`DataverseAccessDataSource` is a **transient** typed HttpClient (`SpaarkeCore.cs:39`) and `AgentTokenService` is
**scoped** (`AgentModule.cs:24`), so each builds a fresh MSAL confidential client per resolution and discards its
token cache. Client assertions require shared/cached clients.
*Acceptance*: both use a process-wide static CCA cache keyed `(tenant|client)`, copying
`DataverseUserClient.cs:55-56,91`. Resolving each type twice from DI yields the same underlying CCA instance.
No behaviour change beyond token-cache reuse; existing tests stay green.

⚠️ **ADR-009 interaction — state the decision, don't inherit it** (`/adr-check` 2026-08-19). MSAL's token cache
inside a singleton CCA is **in-process and per-instance**, while `Services/GraphTokenCache.cs:21` already
implements a Redis (`IDistributedCache`) OBO token cache per ADR-009's Redis-first rule. On a multi-instance App
Service the in-process cache is not shared, so OBO tokens are re-acquired per instance — the exact cost the Redis
cache exists to avoid. Dev is single-instance, so this does not bite in scope, but the choice must be explicit.
*Acceptance*: the task records whether MSAL's cache is serialized into `IDistributedCache` or whether
`GraphTokenCache` remains the sole cross-request cache, with the reason. Silence is not an acceptable outcome.

### Workstream B — The credential seam

**FR-B1** — Introduce `IClientAssertionProvider` and its managed-identity implementation.
The contract is **declared in `Spaarke.Dataverse`**; the implementation and the
`Microsoft.Identity.Web.Certificateless` package live **in the BFF only**. This mirrors the existing nullable
`TokenCredential? credential = null` parameter at `DataverseAccessDataSource.cs:32`, supplied by
`Program.cs:46-48`.
*Acceptance*: one-method interface; singleton implementation registered in the BFF; assertion cached until
expiry and reused (ADR-028 A4 line 172 — *"reuse the instance"*); shared-lib constructors take
`IClientAssertionProvider? assertion = null` with a null default. `Spaarke.Dataverse.csproj` gains **no**
ProjectReference and **no** new package. `tests/Spaarke.ArchTests/LayerDependencyTests.cs` FR-14 still passes
unmodified.

⚠️ **Two ADR-010 obligations, both discovered by `/adr-check` 2026-08-19 — do not let these surface as CI failures:**

1. **Raise the 1:1-interface ceiling in the same PR.** `IClientAssertionProvider` → `ManagedIdentityAssertionProvider`
   is a new 1:1 interface→implementation mapping. `tests/Spaarke.ArchTests/ADR010_DITests.cs:164` asserts
   `knownOneToOneCeiling = 153`; this makes 154 and **fails the build**. The seam is genuinely justified —
   cross-assembly dependency inversion where `Spaarke.Dataverse` structurally cannot reference the implementation
   (FR-14) — which is exactly the exception ADR-010 carves out. Raise 153 → 154 **with a comment citing this
   project and the FR-14 rationale**, per the maintenance procedure documented at `ADR010_DITests.cs:144-146`.
   *Acceptance*: `dotnet test tests/Spaarke.ArchTests/` passes; the ceiling comment names the justification.
2. **Register via a feature module, not inline in `Program.cs`.** ADR-010 MUST NOT: *"inline registrations (use
   feature modules)"*. The `TokenCredential` precedent at `Program.cs:46-48` is itself inline and should not be
   mirrored. *Acceptance*: the registration lives in an existing DI feature module; `Program.cs` gains no new
   inline `AddSingleton`.

**FR-B2** — Build ordered credential selection into the provider.
Order is **MI-FIC → Key Vault certificate → dev secret**, config-driven. **This must be built, not inherited** —
see the E4′ correction under *Assumptions*. It is the mechanism the entire rollback story depends on.
*Acceptance*: reordering the configured credential list changes the selected credential with no code change and
no redeploy beyond an app-settings update; each ordering is covered by a seam test; a missing/failing higher-
priority credential falls through to the next with a logged warning rather than throwing.

**FR-B3** — Migrate the BFF-identity confidential clients onto the provider.
Sites: `GraphClientFactory.cs:83-90` (OBO exchange `:225-228`), `DataverseAccessDataSource.cs:59-63` (OBO
`:118-121`), `DataverseUserClient.cs:91-96` (OBO `:178-182`), `AgentTokenService.cs:49-53` (OBO `:92-95` Graph,
`:162-165` Dataverse), plus the residual `ClientSecretCredential` fallbacks at `DataverseServiceClientImpl.cs:114-118`
and `DataverseWebApiService.cs:83`. **Power BI is excluded — see FR-D.**
*Acceptance*: every listed site obtains its credential from the provider; the secret remains configured and
selectable as the lowest-priority fallback until FR-C3; no call site constructs a credential inline.

**FR-B4** — Never conflate the UAMI clientId with the app-registration clientId.
`GraphClientFactory.cs:54` resolves `_clientId = AZURE_CLIENT_ID ?? API_APP_ID`, and in Azure `AZURE_CLIENT_ID`
is deliberately set to the **UAMI's** clientId (`docs/guides/auth-deployment-setup.md:156-163`). MI-FIC requires
holding both simultaneously — the UAMI's to mint the assertion, the app registration's to build the CCA. The dev
subscription holds **five** UAMIs, one (`spaarke-bff-identity`) named as though it were the BFF's but not
attached to it.
*Acceptance*: the provider takes the UAMI clientId and the app-registration clientId as distinct, separately-named
inputs; a test asserts the assertion is minted for the UAMI while the CCA is built for the app registration;
identities are resolved by resource ID, never by name. **This failure mode is silent** — a wrong value creates
successfully and fails only at token exchange.

**FR-B5** — Relax the config validators that make the secret mandatory.
`DataverseOptions.cs:32` (`[Required]` + ValidateOnStart via `ConfigurationModule.cs:30-34` — the startup-crash
dependency), `GraphOptionsValidator.cs:20-23`, `AgentTokenOptions.cs:38`.
*Acceptance*: the BFF starts with no secret configured when a higher-priority credential is available; it still
fails fast with an actionable message when **no** credential of any kind is configured.

### Workstream C — Rollout and removal (dev only)

**FR-C1** — Create a dev deployment slot and prove OBO end-to-end under MI-FIC.
`spaarke-dev-plan` is **P1v3** so slots are supported; **zero exist today**.
*Acceptance* — the full §6.1 checklist passes on the slot: SPE document upload / download / preview ·
`dataverse.*` AI tool calls via `/api/ai/chat` SSE · Office add-ins (Outlook + Word) · M365 Copilot agent
(`/api/agent`) · **Dataverse row-level authorization** (`PermissionsEndpoints.cs:56,116` and the AI authorization
filters) · send-as-user email · long-running OBO · the built ordered fallback (FR-B2) · **and inbound token
validation demonstrably unaffected** (NFR-05).

**FR-C2** — Flip dev to MI-FIC-first via slot swap, then soak.
*Acceptance*: MI-FIC is first in the credential order on dev; the full FR-C1 checklist re-passes post-swap; a
soak period elapses with no auth-related errors before FR-C3 begins. **No in-session flips** — `#3b` attempt 1
took dev down (SIGABRT from an eager connect under `ValidateOnBuild`).

**FR-C3** — Remove the secret and reconcile the operational estate.
Remove from app settings, then Key Vault, **including the duplicate lowercase `bff-api-client-secret` alias** used
by the Office add-in deploy. Reconcile the **11 PowerShell scripts** that reference `ClientSecret`
(`Configure-ProductionAppSettings.ps1`, `Register-EntraAppRegistrations.ps1`, `Rotate-Secrets.ps1`,
`Seed-ProductionKeyVault.ps1`, `Provision-Customer.ps1`, `Reconcile-DemoEnvironment.ps1`, `Deploy-Release.ps1`,
`Deploy-DataverseSolutions.ps1`, `Test-EntraAppRegistrations.ps1`, `Test-SharePointToken.ps1`,
`naming-conformance-check.ps1`) and the ~25 referencing documents.
*Acceptance*: no BFF-identity path resolves a secret; the Office add-in deploy still succeeds; every listed script
either no longer references the secret or documents why it still does; `.claude/constraints/auth.md`,
`Sprk.Bff.Api/CLAUDE.md:110,221` and the deployment guides reflect the end state.

**FR-C4** — Extend `scripts/Register-EntraAppRegistrations.ps1` with federated-credential creation.
**Added 2026-08-19 in response to [`AUTH-V4-CHANGE-REQUEST-RESPONSE.md`](notes/AUTH-V4-CHANGE-REQUEST-RESPONSE.md).**
`customer-provisioning-orchestration-r1` accepted the change request and assigned this script as auth-v4's to own;
their **task 130** (H3 heavy port, Wave G-3) will *invoke* it rather than duplicate FIC-creation logic. **If it is
not landed before their Wave G-3 dispatches, task 130 builds its own implementation** from the §3.1 recipe, which
then has to be reconciled.

Note this is the one piece of scope that is **not** dev-only: it is shared provisioning automation. It is included
because a sibling project is now soft-blocked on it, and because the dev FIC was created by hand (2026-08-19) —
nothing in the repo can currently create one.
*Acceptance*: the script creates a FIC idempotently given (tenant, app registration, UAMI principalId); issuer is
the hosting tenant's `/v2.0` OIDC endpoint, subject is the UAMI **principalId** (not clientId), audience is exactly
`api://AzureADTokenExchange`; **retries on `AADSTS70021`** (propagation delay); **verifies by performing a token
exchange**, not by checking that creation returned success — misconfiguration creates cleanly and fails only at
exchange. Re-running against an existing FIC is a no-op.

### Workstream D — Power BI (UAMI-as-principal) — ⏭️ **DEFERRED OUT OF THIS PROJECT (owner, 2026-08-19)**

> **DEFERRED.** *"we can ignore Power BI if it is not readily available and defined — we are not yet using
> Power BI (it will be in the future but we can address the MI at that time)."* — owner, 2026-08-19.
>
> **Tasks 040, 041, 042 are parked ⏭️ and `PowerBi:ClientSecret` REMAINS in place.** The requirements below are
> retained verbatim as the re-open specification; nothing about them was found wrong.
>
> **Why deferring is safe**: `PowerBi:ClientSecret` (`PowerBiOptions.cs:44-45`) is a *genuinely separate*
> credential from `BFF-API-ClientSecret`. No OBO path reads it, so Workstream D does not gate FR-C3 (task 033)
> and does not touch the fail-closed surface. Task 033 already asserts it is left untouched.
>
> **What the deferral still obligates**, so this is not silent debt: FR-F1 (task 060) allowlists the Power BI
> sites **with this reason recorded**; FR-F2 (task 061) keeps them in the census as **secret-backed** entries so
> the count stays honest; success criterion 10 is **waived with reason**, not dropped. Re-open at FR-D2's
> gating question — it is still unanswered.

*Original owner decision (still the intended approach when re-opened): adopt Microsoft's documented model rather
than prototyping MI-FIC on the existing service principal. Power BI therefore does **not** consume the FR-B1
provider.*

**FR-D1** — Enable and grant the managed identity as a Power BI principal.
Tenant setting permitting service principals / managed identities to use the Power BI APIs; grant the UAMI
access to the relevant workspaces.
*Acceptance*: the UAMI can list and embed the workspaces the BFF serves today.

**FR-D2** — Rework the reporting services off CCA + secret.
`ReportingEmbedService.cs:77-81` (`:604`) and `ReportingProfileManager.cs:74-78` (`:247`).
*Acceptance*: both authenticate as the UAMI; embed tokens still issue; existing reporting endpoints return
unchanged payloads. **Gated on unresolved question #1** — see below.

**FR-D3** — Remove `PowerBi:ClientSecret` (`PowerBiOptions.cs:44-45`), a genuinely separate secret from
`BFF-API-ClientSecret`.
*Acceptance*: removed from config and Key Vault; the validator no longer requires it.

### Workstream E — Group 2 non-Entra credentials (parallel workstream, own PRs)

*Independent of the OBO migration — different services, no shared code. Gates wrap-up; does not block FR-C.*

**FR-E1** — Content Safety → MI. `ContentSafetyAuthHandler.cs:41,72` currently prefers the API key.
**The MI path already exists and is simply not selected** (`ContentSafetyTokenProvider.cs:55`).
*Acceptance*: clearing the key selects bearer auth via the existing provider; the 100 ms Prompt-Shield deadline
is still met.

**FR-E2** — Service Bus → namespace + MI. `ConnectionStrings:ServiceBus` is a SAS credential
(`ServiceBusOptions.cs:15`). **The MI pattern already exists** at `MembershipJunctionUpdaterHost.cs:120`.
*Acceptance*: job processing runs on `ServiceBusClient(namespace, credential)`; the SAS connection string is
removed. **Note**: a live SAS key was found in a local `appsettings.Development.json` — rotate it regardless
(design §10 item 6).

**FR-E3** — Azure OpenAI (ADR-028 **E-2**) → MI. **Check `spaarke-openai-dev` for a custom subdomain first** —
Microsoft documents a missing custom subdomain as the root cause of exactly the MI-401 that caused E-2. If absent,
this may be a one-config-change elimination independent of everything else.
*Acceptance*: either the key is cleared and `AiModule.cs:122-128` resolves the DI `TokenCredential` successfully,
or the failure is re-diagnosed and E-2 is re-affirmed with current evidence.

**FR-E4** — Azure AI Search ×2 → Entra/MI: `InternalIndexProvider.cs:80-88`, `AiSearchOptions.cs:6`.

**FR-E5** — Document Intelligence ×3 → Entra/MI: `DocumentIntelligenceOptions.cs:42,152,303`.

**FR-E6** — `Analysis:PromptFlowKey` (`appsettings.json:118`) — **verify whether still in use** before touching.
*Acceptance*: either migrated, or removed as dead configuration, or documented as still required.

**FR-E7** — Group 1 hygiene carve-out. `BingSearch:ApiKey` is read directly from configuration
(`WebSearchHandler.cs:283,504`); make it Key-Vault-by-name as `LlamaParseClient.cs:117-126` already is.
*Acceptance*: the key value is never bound into configuration. LlamaParse itself needs no change.

### Workstream F — Forcing functions (anti-recurrence)

*The predecessor audit did not miss the code — it inventoried all nine consumers correctly and then concluded
"NEVER-REMOVE" on a false premise. Text alone is what failed last time.*

**FR-F1** — ArchTest credential ban. No type under `src/server/**` may call `.WithClientSecret(` or construct
`ClientSecretCredential` outside a named allowlist (ADR-028 **E-1** SpeAdmin sites; **E-3** until FR-C3 completes).
Same shape as the existing `GodClassGuardTests`.
*Acceptance*: adding a new `.WithClientSecret` site outside the allowlist **fails the build**; the allowlist
entries each carry a written reason; the test's negative control proves the detector fires.

**FR-F2** — Credential census test. Assert that the number of confidential-client construction sites equals a
checked-in census with a per-site reason.
**MUST be implemented as source / assembly analysis, NOT as a DI-resolution test.** ADR-038 ban **B3** prohibits
`Assert.NotNull(services.GetRequiredService<X>())`; a census that resolves services from a container is a banned
DI-registration test. Analyse the source tree or the compiled assembly for construction sites.
*Acceptance*: adding a ninth CCA site fails until the census is updated. *This is what would have caught
`SpeAdminTokenProvider` and `SpeAdminGraphService`, both absent from the origin seed's inventory.*

**FR-F0** — Declare the forcing functions MAINTAIN-class before wrap-up.
FR-F1 and FR-F2 live in `tests/Spaarke.ArchTests/`, which is **not one of the 7 KEEP paths** in
[`tests/CLAUDE.md`](../../tests/CLAUDE.md). That file states tests outside those paths are *"anti-pattern by
construction"*, and does not carve out the ArchTests project — despite it being pre-existing, sanctioned, and home
to `GodClassGuardTests` and `LayerDependencyTests`. **Risk**: `/test-diet` at the `090-wrapup-*` task (a mandatory
gate per root CLAUDE.md §7) classifies them as scaffolding and proposes deletion — destroying the anti-recurrence
mechanism that is this project's entire purpose, and invalidating success criterion 12.
*Acceptance*: the `/test-diet` report classifies FR-F1 and FR-F2 as **MAINTAIN**, citing that structural fitness
functions are a distinct category from build-class scaffolding. If `/test-diet` cannot express that, escalate to a
`tests/CLAUDE.md` amendment adding `tests/Spaarke.ArchTests/**` as an eighth KEEP path rather than deleting them.

**FR-F3** — Startup assertion. Outside `Development`, fail fast if any BFF-identity credential resolves to a
secret once FR-C3 has completed, rather than silently degrading.
*Acceptance*: a deliberately misconfigured non-Development startup fails with an actionable message; Development
is unaffected.

### Non-Functional Requirements

- **NFR-01** — Publish size ≤ **60 MB** compressed (binding ceiling). Report absolute + delta against the
  **44.96 MB incl. PDBs** net10 baseline (2026-08-13) on every BFF-touching task.
  `Microsoft.Identity.Web.Certificateless` is a **new** package reference; expected impact small but must be
  measured, not assumed.
- **NFR-02** — No new HIGH-severity CVE from `dotnet list package --vulnerable --include-transitive`.
- **NFR-03** — OBO fails **closed**. Breakage locks every user out immediately and totally across SPE documents,
  chat tool calls, Office add-ins, the Copilot agent, send-as-user email, and row-level authorization on every
  document and AI endpoint. Staged rollout with an explicit rollback is mandatory at every phase.
- **NFR-04** — All **46 test fixtures** that seed dummy secrets must keep compiling and passing. The provider
  parameter is nullable with a null default; **adding a required constructor argument breaks all 46**.
- **NFR-05** — **Inbound token validation provably unaffected.** `AddMicrosoftIdentityWebApi`
  (`AuthorizationModule.cs:36`) binds the same `AzureAd` configuration section that carries `AzureAd:ClientSecret`
  (read for OBO at `DataverseUserClient.cs:85`). Inbound and outbound share one config section. Verified after
  **every** config change, not assumed.
- **NFR-06** — Rollback at every phase is a configuration change (credential reorder) or a slot swap back. The
  secret is not deleted until FR-C3, after a soak.

---

## Technical Constraints

### Applicable ADRs

| ADR | Relevance |
|---|---|
| **ADR-028** | Canonical auth architecture. **Amendment A4** (secret-free confidential credential; MI-FIC preferred, KV certificate the sanctioned alternative, never a secret) + transitional exception **E-3** (the retained secret, time-boxed to this project). Applied 2026-08-17 |
| **ADR-003** | Server seams + OBO |
| **ADR-008** | Endpoint filters for authorization — the row-level auth surface this must not regress |
| **ADR-009** | Redis OBO token cache — interacts with the FR-A2 CCA-cache change |
| **ADR-010** | DI minimalism (≤15 non-framework registrations). The provider adds registrations |
| **ADR-027** | Subscription isolation / tenancy — the Model 2 shape |
| **ADR-032** | Null-Object kill-switch — applies if any credential path remains feature-gated |
| **ADR-038** | Testing strategy. Coverage goes to `tests/integration/seam/**`; **DI-registration tests and ctor null-check tests are banned** |

### MUST Rules

- ✅ **MUST** use a secret-free confidential credential (MI-FIC preferred) for BFF-identity confidential clients — ADR-028 A4.
- ✅ **MUST** keep the assertion-minting identity (UAMI) and the authenticating identity (app registration) as
  distinct, separately-named inputs — FR-B4.
- ✅ **MUST** resolve managed identities by resource ID, never by name (five UAMIs exist; one is a decoy).
- ✅ **MUST** verify inbound token validation after every configuration change — NFR-05.
- ✅ **MUST** report publish size + delta and run the CVE scan on every BFF-touching task — CLAUDE.md §10.4–.5.
- ❌ **MUST NOT** add a `ProjectReference` from `Spaarke.Dataverse` to any other Spaarke project — CI-enforced by
  `tests/Spaarke.ArchTests/LayerDependencyTests.cs` FR-14.
- ❌ **MUST NOT** introduce a required constructor parameter on the shared-lib types — NFR-04.
- ❌ **MUST NOT** flip a live environment in-session; use the slot — NFR-03.
- ❌ **MUST NOT** delete the secret before the soak completes — NFR-06.
- ❌ **MUST NOT** fall back to a client secret in any new code path. If MI-FIC is unavailable for a future shape,
  fall back to a **Key Vault certificate** — ADR-028 A4.

### Existing Patterns to Follow

| Pattern | Reference |
|---|---|
| Nullable credential injected from the BFF into the shared lib | `DataverseAccessDataSource.cs:32` ← `Program.cs:46-48` |
| Process-wide static CCA cache keyed `(tenant|client)` | `DataverseUserClient.cs:55-56,91` |
| Extending the shared credential seam | `ContentSafetyTokenProvider.cs:15-22` (documents the pattern explicitly) |
| Secret-free confidential client, already in production | `CiamGraphClientFactory.cs:129-133,154-170` (certificate) |
| App-only MI already on a namespace credential | `MembershipJunctionUpdaterHost.cs:120` |
| ArchTest forcing function | `tests/Spaarke.ArchTests/LayerDependencyTests.cs`; `GodClassGuardTests` |

---

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration

```xml
<hot-path-declaration>
  <bff>Y</bff>                            <!-- 6 confidential-client sites + DI + config validators -->
  <spaarkeai>N</spaarkeai>
  <ci-workflows>N</ci-workflows>          <!-- note: GitHub Actions OIDC FIC exists on the same app registration -->
  <skill-directives>Y</skill-directives>  <!-- .claude/adr/ADR-028, .claude/constraints/auth.md, .claude/patterns/auth/* -->
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

### Placement Justification (CLAUDE.md §10)

All changes belong **in the BFF and `Spaarke.Dataverse`** because they modify how existing BFF-owned code
authenticates. No new endpoint, background worker, or client surface is added. The one new component extends the
existing `Infrastructure/Auth/` seam and is registered as a singleton in the existing DI module. Per
`.claude/constraints/bff-extensions.md`, this is a modification of existing BFF surface, not an addition of new
capability, and does not meet any AI-extraction criterion.

### New Components (§11 three-question gate)

| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| `IClientAssertionProvider` (contract, `Spaarke.Dataverse`) | None — no assertion abstraction exists anywhere; zero `WithClientAssertion` / `ClientAssertionCredential` in `src/**` | **No.** `Spaarke.Dataverse` cannot reference `Sprk.Bff.Api` (dependency direction) and cannot reference `Spaarke.Core` (**circular** — `Spaarke.Core.csproj:32` already references Dataverse; blocked by `LayerDependencyTests.cs:43` FR-14). Dependency inversion is the only legal seam | Without it, `DataverseAccessDataSource` and the other shared-lib sites **cannot obtain a client assertion at all**, so the OBO paths — including row-level authorization for every document and AI endpoint — must keep using the client secret. The project's primary goal fails |
| `ManagedIdentityAssertionProvider` (impl, `Sprk.Bff.Api/Infrastructure/Auth/`) | `ManagedIdentityCredentialFactory.cs:26-41` provides a UAMI-pinned `TokenCredential` for `Azure.Identity` app-only calls | **Yes — and that is the plan.** This sits alongside it in the same namespace and reuses its UAMI-resolution logic. It is not a parallel abstraction; it adds the assertion-callback + CCA-cache capability the existing factory does not provide | Without it, each of the 6 call sites needs independent assertion plumbing and independent lifetime fixes; the two per-request construction sites rebuild an MSAL client per request and discard the token cache; and the credential type could not be switched by configuration, which is the entire rollback mechanism (NFR-06) |

*Alternative rejected*: a dedicated `Spaarke.Auth.Credentials` project below `Spaarke.Dataverse`. It fails §11
question 2 — the existing seam can be extended — and would drag `Microsoft.Identity.Web.Certificateless` into the
base shared layer, widening publish size and CVE surface for every downstream consumer.

---

## ADR Tensions (per CLAUDE.md §6.5)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| **ADR-028** | Line 24: *"MUST use `DefaultAzureCredential` … NOT `ClientSecretCredential`"* | The OBO paths used `ClientSecretCredential` / `.WithClientSecret` and were **not** in the exception registry — an undocumented deviation. The rule was also **unsatisfiable literally**: `DefaultAzureCredential` cannot perform an OBO exchange | **B — amendment. ✅ APPLIED 2026-08-17** | Amendment **A4** now requires a secret-free confidential credential and names the client-assertion mechanism; exception **E-3** covers the retained secret, time-boxed to this project. The `adr-check` rule that flagged every OBO site with no sanctioned alternative was fixed at source. **Recorded as resolved, not open** |
| **ADR-010** | ≤15 non-framework DI registrations | FR-B1 adds a singleton registration | **Verify at task time, then declare A or C** | Likely absorbed within budget. Count the current non-framework registrations before implementing; if the budget is exceeded, declare a scoped exception (A) with the reason, or consolidate (C). **Do not discover this at code review** |
| **ADR-038** | Bans DI-registration tests and ctor null-check tests | The forcing functions (FR-F) are structural assertions that could be mistaken for banned DI tests | **C — comply** | FR-F1/F2 are **ArchTests** (assembly/source analysis), not DI-container resolution tests; FR-F3 is a runtime startup assertion. Credential-seam behaviour goes to `tests/integration/seam/**`, the category ADR-038 §7 added for dispatch-spine-style changes |
| *Layer FR-14* | `Spaarke.Dataverse` references no other Spaarke project | Not a tension — recorded because it **eliminated** the obvious design | *n/a* | The `Spaarke.Core` placement is circular and CI-blocked. FR-B1's dependency-inversion seam respects the constraint without modifying the fitness function |
| **ADR-009** | *"MUST use `IDistributedCache` for cross-request caching"* / *"MUST document an ADR-009 exception for any in-process cache"* | FR-A2's fix makes the MSAL confidential client process-lifetime, so its **in-process** OBO token cache becomes a cross-request cache. Distributing it (`AddDistributedTokenCache`) is available and was **declined** | **A — project-scoped exception. ✅ RESOLVED 2026-08-20 (task 011)** | Full resolution, accepted consequences, revisit triggers and preferred remedy: [`notes/decisions/011-adr009-token-cache-decision.md`](notes/decisions/011-adr009-token-cache-decision.md) §4. Short form: a serialized MSAL cache carries **refresh-token-bearing** material, so distributing it is a security-posture change, not a caching change — and it must not ride along with a DI-lifetime bugfix. The two high-volume OBO paths (`GraphClientFactory`, `AgentTokenService`) **already** Redis-cache their results at the application layer, which is where ADR-009's requirement earns its keep. Cite this row in the task 011 PR |
| **ADR-028 A4** | Line 207: *"MUST obtain the confidential credential from the **single shared credential provider** … rather than constructing credentials per call site"* | Task 011 leaves **three** per-class static client caches (`DataverseUserClient`, `DataverseAccessDataSource`, `AgentTokenService`) — one process can hold three clients for the same `(tenant\|client)` | **A — exception, TIME-BOXED and EXPIRING AT TASK 022** | Consolidating at 011 would pre-empt task 020's provider design and then be reworked by 022. Booked as a **constraint + acceptance criterion on both 020 and 022** and as a row in `tasks/TASK-INDEX.md` — deliberately not as prose in a notes file, which is how it was first written and what `adr-check` finding **W2** caught. If 022 does not consolidate, this becomes a standing A4 violation with no owner |

---

## Success Criteria

> **WRAP-UP WALK (task 090, 2026-08-24).** Every criterion below carries its evidence. Per 090's negative-case
> rule, **nothing is checked without it** — two criteria (#9, #15) are marked NOT MET rather than quietly ticked,
> and #10's waiver was re-verified rather than assumed. Live proof that MI is in actual use (Dataverse `createdby`
> + Entra sign-in logs, independent of the no-fallback argument) is in
> [`notes/decisions/mi-proof-dataverse-side.md`](notes/decisions/mi-proof-dataverse-side.md).

1. [x] **VERIFIED** Phase 0 spike proves OBO under MI-FIC — task 031 proved it live; **Office add-in UAT PASSED
       2026-08-24** (Outlook save + the created `.eml` opens; Word record + file profile). Evidence:
       `notes/uat-findings-2026-08-24.md`.
2. [x] **VERIFIED** MI-flag gating defect fixed (FR-A1) — tasks 020–024; `IdentityConflationSeamTests` +
       `CredentialSelectionSeamTests` under `tests/integration/seam/Auth/`.
3. [x] **VERIFIED** DI lifetimes fixed (FR-A2) — static CCA cache reused across resolutions; task 021.
4. [x] **VERIFIED** Every BFF-identity confidential client uses the provider — `CredentialCensusTests` (FR-F2)
       enumerates every construction site with its credential source; **census fails if the count drifts**.
5. [x] **VERIFIED** `Spaarke.Dataverse` gains no ProjectReference and no package — `LayerDependencyTests` FR-14
       passes unmodified; **ArchTests 56/56 green** as of 2026-08-24.
6. [x] **VERIFIED** Rollback is configuration-only — exercised in 031 §5.6 (credential reorder → restart →
       selected credential changed, no code change).
7. [x] **VERIFIED** `BFF-API-ClientSecret` removed from app settings **and** Key Vault, all six paths incl. the
       lowercase alias — task 033; soft-deleted (recoverable to 2026-11-22), **not purged**.
8. [x] **VERIFIED** Config validators relaxed consistently (FR-B5) — BFF boots with no secret configured; still
       fails fast with no credential at all. `CredentialOrderingSeamTests` holds both halves.
9. [~] ⚠️ **DOCUMENTED, NOT YET PROVISIONED — no longer an undefined gap.**
       **Closed at task 090 (2026-08-25)** with [`docs/guides/local-dev-obo-setup.md`](../../docs/guides/local-dev-obo-setup.md):
       the constraint is stated (a workstation has no route to IMDS, so MI-FIC cannot work locally, and neither
       `az login` nor `DefaultAzureCredential` can perform an OBO exchange), the exact config keys are named
       (`AzureAd:ClientSecret` → `API_CLIENT_SECRET` → `AZURE_CLIENT_SECRET` — **not** the `Graph:ClientSecret` /
       `Dataverse:ClientSecret` a retired doc told people to set, which have zero consumers), four options are
       compared, and **option D is recommended**: one *local-dev-only* app registration, separate from every
       deployed identity, with its secret in `dotnet user-secrets`.
       **Residual**: the option-D app registration is not yet created — a one-time Azure action, with the exact
       commands in the guide. The property that mattered is preserved either way: **no deployed identity holds a
       secret**; a workstation is not a deployed identity.
10. [⏭️] ~~Power BI runs as a managed-identity principal; `PowerBi:ClientSecret` removed~~ — **WAIVED 2026-08-19
        (owner): Power BI is not yet in use at Spaarke; Workstream D deferred.** Instead verify at wrap-up that
        the deferral is *visible, not silent*: the Power BI sites are named in the FR-F1 allowlist with the
        deferral reason AND appear in the FR-F2 census as still-secret-backed. Re-open with FR-D when Power BI
        is adopted.
        ✅ **Wrap-up re-verification DONE (2026-08-24) — checked, not assumed.** The Power BI sites are present
        in **both** guards with the deferral reason inline: `CredentialGuardTests.cs:96–106` (FR-F1 allowlist)
        and `CredentialCensusTests.cs:113–123` (FR-F2 census), the latter recording
        `CredentialSource: "PowerBi:ClientSecret — STILL SECRET-BEARING"`. The deferral is therefore **visible
        in a failing-by-default test surface**, not silent: if someone deletes the allowlist entry without
        migrating Power BI, FR-F1 fails.
11. [x] **VERIFIED** Group 2 credentials migrated or documented — tasks 051 (Service Bus → MI) and 053 (AI Search
        → MI) cut over live, each **proven by removing the fallback**; the retained KV rollback secrets were
        deleted at wrap-up (2026-08-25, soft-delete to 2026-11-23). Per-credential acceptance in FR-E1..E7.
12. [x] ✅ **VERIFIED — EXERCISED, not asserted.** A deliberate ninth secret-bearing confidential client was
        seeded on a scratch branch; **FR-F1 and FR-F2 both fired**, naming the exact `file:line` and telling the
        reader what to do instead. Scratch branch deleted; 56/56 green after. Evidence + exact failure output:
        `notes/lessons-learned.md` §3. *This is the criterion that distinguishes auth-v4 from its predecessors.*
        Precise claim: **`dotnet build` succeeds; the ArchTests fail** — the gate is CI, not the compiler.
13. [x] **VERIFIED** Inbound token validation unaffected (NFR-05) — `AddMicrosoftIdentityWebApi` untouched
        throughout; re-confirmed by the add-in **authenticating successfully** during UAT.
14. [x] **VERIFIED** Operational estate reconciled — task 033 §4/§5. Note the count was wrong in this spec:
        **15 scripts and 33 docs**, not "11 scripts and ~25 docs". Re-derived, not inherited (see §4 of
        lessons-learned on systematic under-counting).
15. [ ] ❌ **NOT MET — open OWNER decision, not a project deliverable.**
        `notes/PROVISIONING-CHANGE-REQUEST.md` §5.1 asks which app registration the shared Model 1 BFF acts as
        (one shared multitenant app vs one per customer). The document states plainly: *"This is yours to make."*
        **MI-FIC works either way**, so it does not block this project — but it decides whether customer
        onboarding gains a per-customer FIC step. §5.2 (a `design.md:1006` doc fix) is likewise for the
        provisioning owner. Hand-off: `customer-provisioning-orchestration-r1` (#779).
16. [x] **VERIFIED** `/test-diet` run at wrap-up — `notes/test-diet-report.md` (73 methods added, **0
        SCAFFOLDING**, 8 path-violation-protected, 1 ambiguous). Publish size **45.04 MB incl. PDBs** vs the
        **44.96 MB** baseline = **+0.08 MB**; ceiling 60 MB (NFR-01). PDB convention stated. No new HIGH CVE.

### Walk result

**14 of 16 verified with evidence · 1 waived (Power BI, deferral re-verified as visible) · 2 NOT MET and carried
forward with owners.** Neither open item blocks the secret-removal objective: #9 is a local developer-experience
gap created by the removal, and #15 is an identity-design decision that MI-FIC satisfies either way.

---

## Dependencies

### Prerequisites

- ✅ **ADR-028 A4 + E-3** — applied 2026-08-17.
- ✅ **Dev MI-FIC** (`mi-bff-api-dev-assertion`, `66bac39a-…`) — created 2026-08-19. Reversible in one command.
- ✅ **Platform prerequisites verified live** — app registration + UAMI same tenant; `spaarke-bff-dev` runs
  user-assigned MI only (`mi-bff-api-dev`, principalId `9fd47efb-…`); plan is P1v3.
- ⬜ **Dev deployment slot** — supported but **zero exist**; created in FR-C1.
- ⏭️ ~~**Power BI tenant setting + workspace grants**~~ — FR-D1. **Not needed: Workstream D deferred 2026-08-19.**
- `#3b` (app-only Dataverse → MI) — done, live on dev.

### External Dependencies

- **Azure AD admin** — authenticated in-session; owner runs the project. No external scheduling dependency remains.
- ⏭️ ~~**Power BI admin**~~ — for FR-D1. **No longer required: Workstream D deferred 2026-08-19.** This removes the project's last external-approval dependency.
- **`customer-provisioning-orchestration-r1`** — PR #779, ~68% executed. Not blocking; needs the §5.1 decision.
- **`dataverse-access-unification-r1`** — **not a prerequisite in either direction.** Parallel execution expected;
  four-file interlock in `notes/COORDINATION-DATAVERSE-ACCESS-UNIFICATION.md` §4.
- **Open PR #293** (`Azure.Identity` 1.17.1 → 1.21.0) — relevant to `ClientAssertionCredential` /
  `ManagedIdentityCredential` behaviour. Coordinate rather than conflict.

---

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Rollout envs | Only `spaarke-bff-dev` is Running; `spaarke-bff-prod` is Stopped. Which environments? | **Dev only** | One environment. §6.1 verification runs once. Artifacts stay environment-parameterised but prod is not executed. Prod adoption becomes a follow-on if it is revived |
| Power BI | MI-FIC on the existing SP (undocumented) vs Microsoft's recommended UAMI-as-principal? | **Adopt UAMI-as-principal** | Power BI **leaves the shared provider seam** and becomes Workstream D: replace the SP with a managed identity, touch Power BI tenant/workspace admin config, rework both reporting services. More work than the credential swap, but the blessed path |
| Group 2 sequencing | Parallel, sequential, or front-loaded? | **Parallel workstream, own PRs** | Runs alongside the OBO migration; gates wrap-up but does not block FR-C. Two items (Content Safety, Service Bus) are near-immediate — the MI path already exists in-repo |
| Provider seam | `Spaarke.Dataverse` can't reference the BFF. How do the shared-lib sites get the assertion? | **Option A — named interface injected into the shared lib** | `IClientAssertionProvider` declared in `Spaarke.Dataverse`; implementation + Certificateless package in the BFF only. `Spaarke.Core` placement was **eliminated** as circular and CI-blocked, not chosen against |
| Scope — non-Entra keys | Defer all API keys, or address them? | **Group 2 in; Group 1 out (one hygiene fix); Group 3 explicitly closed** | Adds Workstream E. Group 3 being *closed* rather than *deferred* is deliberate — it stops a future audit re-opening settled ground |
| Anti-recurrence | *"we addressed Auth in previous projects only to find this remained latent"* | **Forcing functions are a graduation criterion, not a follow-up** | Adds Workstream F. Success criterion 12 requires proving the build fails on a deliberately-introduced violation |

---

## Assumptions

- **E4′ — the declarative adoption path does not exist here.** `Microsoft.Identity.Web`'s ordered
  `ClientCredentials` list is the documented mechanism, but this codebase has **zero** occurrences of
  `EnableTokenAcquisition` / `ITokenAcquisition` / `IDownstreamApi` / `ClientCredentials` in any `.cs` file. All
  eight confidential clients hand-roll `ConfidentialClientApplicationBuilder`; `AddMicrosoftIdentityWebApi` is
  inbound validation only; `Spaarke.Dataverse` has no Identity.Web reference at all. **Assuming
  `.WithClientAssertion(Func<AssertionRequestOptions,Task<string>>)` + `ManagedIdentityClientAssertion` from
  `Microsoft.Identity.Web.Certificateless`.** Affects FR-B1/B2 and invalidates the original ~350–550 LOC estimate.
- **`AADSTS70021` immediately after FIC creation is propagation delay, not incompatibility.** The dev FIC was
  created 2026-08-19, so propagation is long settled; retry logic is still required for any provisioning flow.
- **Assuming the retained secret stays configurable through FR-C2** so rollback remains available. FR-C3 is the
  only irreversible step, and it is gated on a soak.
- **Assuming ADR-028 E-1 (SpeAdmin per-customer secrets) remains valid** and is unaffected. Those authenticate
  other applications, not the BFF identity.

---

## Unresolved Questions

- [⏭️] **Power BI service-principal *profiles* under a managed identity.** `ReportingProfileManager` uses Power BI
      SP profiles; whether profiles are supported when the principal is a managed identity is **unverified**.
      **DEFERRED WITH WORKSTREAM D (2026-08-19)** — it no longer blocks anything in this project, but it does
      **not** become answered by being deferred. It travels with task 040 and must be settled *before* 041/042
      are ever attempted. *If unsupported*: fall back to MI-FIC-on-existing-SP, or retain the Power BI secret
      under a documented ADR-028 exception.
- [x] ~~**ADR-010 DI-registration headroom.**~~ **RESOLVED 2026-08-19 by `/adr-check`.** Not a blocker: ADR-010
      itself records **265 registrations** at the 2026-05-26 baseline against the "≤15 non-framework lines"
      principle, explicitly *"a known violation accepted by the project"*, with reduction out of scope. FR-B1 adds
      one to an already-accepted overage. The live obligations are instead the two under FR-B1 — raise the
      `ADR010_DITests` 1:1 ceiling 153 → 154, and register via a feature module rather than inline.
- [ ] **`Analysis:PromptFlowKey` — still in use?** *Blocks*: FR-E6 disposition (migrate / delete / retain).
- [x] ~~**Which app registration the shared Model 1 BFF authenticates as**~~ — **ANSWERED 2026-08-19 by
      provisioning** ([`notes/AUTH-V4-CHANGE-REQUEST-RESPONSE.md`](notes/AUTH-V4-CHANGE-REQUEST-RESPONSE.md)).
      The answer is a **split, not a single reading**: **Model 1** = Reading 1, one shared multitenant app
      registration (matches the live `AzureADMultipleOrgs`), per-customer trust via the existing D18
      consent-callback, **no per-customer FIC**. **Model 2** = Reading 2, per-customer app registration with its
      own FIC, created by their H3. Their spec.md FR-39; R23 closed.
- [x] ~~**Invariant I6**~~ — **ADOPTED 2026-08-19**, verbatim, scoped to **Model 1 only** (structurally true by
      construction under Model 2's per-customer app registration). ArchTest
      `Spaarke.ArchTests.TenantIsolation.I6_ObApp*`; enforcement carried by their task 130. Their spec.md FR-40.
- [ ] ⚠️ **Raised back to provisioning — Model 2 same-tenant check.** Their reply states Model 2 uses a
      per-customer app registration *"+ a FIC trusting **the shared BFF UAMI**"*. For **Model 2 in the Spaarke
      tenant** that is intra-tenant and fine. For **Model 2 in a customer's tenant** — where
      [`TENANCY-AND-CREDENTIALS.md`](notes/TENANCY-AND-CREDENTIALS.md) §3 has the app registration *and* a stamp
      UAMI both customer-side — trusting a Spaarke-tenant shared UAMI would be **cross-tenant, which MI-FIC does
      not support** (ADR-028 A4 line 179 and the Entra same-tenant prerequisite). Either the customer-tenant shape
      uses its **own stamp UAMI** as the FIC issuer, or that shape cannot use MI-FIC at all and needs the KV
      certificate. *Blocks*: nothing in dev-only scope. **Must be settled before their Wave G-3 task 130 executes.**

---

*AI-optimized specification. Original design: [`design.md`](design.md).*
