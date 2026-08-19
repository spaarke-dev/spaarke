# Spaarke Auth v4 — Zero-Secret BFF Confidential Credential — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-08-19
> **Source**: [`design.md`](design.md)
> **Epic**: Auth / Code Quality (#427) · **Risk**: HIGH (OBO = all delegated user auth; fails closed)
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

### Workstream B — The credential seam

**FR-B1** — Introduce `IClientAssertionProvider` and its managed-identity implementation.
The contract is **declared in `Spaarke.Dataverse`**; the implementation and the
`Microsoft.Identity.Web.Certificateless` package live **in the BFF only**. This mirrors the existing nullable
`TokenCredential? credential = null` parameter at `DataverseAccessDataSource.cs:32`, supplied by
`Program.cs:46-48`.
*Acceptance*: one-method interface; singleton implementation registered in the BFF; assertion cached until
expiry and reused; shared-lib constructors take `IClientAssertionProvider? assertion = null` with a null default.
`Spaarke.Dataverse.csproj` gains **no** ProjectReference and **no** new package.
`tests/Spaarke.ArchTests/LayerDependencyTests.cs` FR-14 still passes unmodified.

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

### Workstream D — Power BI (UAMI-as-principal)

*Owner decision: adopt Microsoft's documented model rather than prototyping MI-FIC on the existing service
principal. Power BI therefore does **not** consume the FR-B1 provider.*

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
*Acceptance*: adding a ninth CCA site fails until the census is updated. *This is what would have caught
`SpeAdminTokenProvider` and `SpeAdminGraphService`, both absent from the origin seed's inventory.*

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

---

## Success Criteria

1. [ ] Phase 0 spike proves OBO under MI-FIC — Verify: FR-C1 checklist green on the dev slot.
2. [ ] MI-flag gating defect fixed — Verify: seam test matrix (FR-A1).
3. [ ] DI lifetimes fixed — Verify: same CCA instance across two DI resolutions (FR-A2).
4. [ ] Every BFF-identity confidential client uses the provider — Verify: credential census (FR-F2) lists each
       site with its credential source; no inline credential construction remains.
5. [ ] `Spaarke.Dataverse` gains no ProjectReference and no new package — Verify: `LayerDependencyTests` FR-14
       passes unmodified; `git diff Spaarke.Dataverse.csproj` shows no `ProjectReference` and no `PackageReference` add.
6. [ ] Rollback is configuration-only — Verify: reorder the credential list, restart, confirm the selected
       credential changed, with no code change (FR-B2).
7. [ ] `BFF-API-ClientSecret` removed from app settings and Key Vault, **all six paths incl. the lowercase alias** —
       Verify: `az keyvault secret list`; Office add-in deploy succeeds.
8. [ ] Config validators relaxed consistently — Verify: BFF starts with no secret configured; still fails fast
       with no credential at all (FR-B5).
9. [ ] Local `dotnet run` works, including OBO, via the documented fallback.
10. [ ] Power BI runs as a managed-identity principal; `PowerBi:ClientSecret` removed — Verify: embed tokens issue;
        reporting endpoints return unchanged payloads (FR-D).
11. [ ] Group 2 credentials migrated, or each documented with a reason not to — Verify: per-credential acceptance
        in FR-E1..E7.
12. [ ] **Forcing functions merged and failing correctly** — Verify: introduce a deliberate ninth secret-bearing
        confidential client on a scratch branch; **the build fails** (FR-F1 + FR-F2). *This is the criterion that
        distinguishes auth-v4 from its predecessors.*
13. [ ] Inbound token validation unaffected — Verify: NFR-05 check after each config change.
14. [ ] Operational estate reconciled — Verify: the 11 scripts and referencing docs (FR-C3).
15. [ ] Provisioning coordination closed — Verify: `notes/PROVISIONING-CHANGE-REQUEST.md` §5.1–5.3 answered.
16. [ ] `/test-diet` run at wrap-up; publish size reported against the 44.96 MB baseline.

---

## Dependencies

### Prerequisites

- ✅ **ADR-028 A4 + E-3** — applied 2026-08-17.
- ✅ **Dev MI-FIC** (`mi-bff-api-dev-assertion`, `66bac39a-…`) — created 2026-08-19. Reversible in one command.
- ✅ **Platform prerequisites verified live** — app registration + UAMI same tenant; `spaarke-bff-dev` runs
  user-assigned MI only (`mi-bff-api-dev`, principalId `9fd47efb-…`); plan is P1v3.
- ⬜ **Dev deployment slot** — supported but **zero exist**; created in FR-C1.
- ⬜ **Power BI tenant setting + workspace grants** — FR-D1, requires Power BI admin.
- `#3b` (app-only Dataverse → MI) — done, live on dev.

### External Dependencies

- **Azure AD admin** — authenticated in-session; owner runs the project. No external scheduling dependency remains.
- **Power BI admin** — for FR-D1.
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

- [ ] **Power BI service-principal *profiles* under a managed identity.** `ReportingProfileManager` uses Power BI
      SP profiles; whether profiles are supported when the principal is a managed identity is **unverified**.
      *Blocks*: sizing FR-D2. *If unsupported*: fall back to MI-FIC-on-existing-SP, or retain the Power BI secret
      under a documented ADR-028 exception. **Verify before committing Workstream D's task set.**
- [ ] **ADR-010 DI-registration headroom.** Count non-framework registrations before FR-B1; declare path A or C.
      *Blocks*: nothing, but must not surface first at code review.
- [ ] **`Analysis:PromptFlowKey` — still in use?** *Blocks*: FR-E6 disposition (migrate / delete / retain).
- [ ] **Which app registration the shared Model 1 BFF authenticates as** — one shared multitenant app (Reading 1,
      the working assumption, supported by the live `AzureADMultipleOrgs` value) vs one per customer (Reading 2).
      **Provisioning's call**, `notes/PROVISIONING-CHANGE-REQUEST.md` §5.1. *Blocks*: nothing in dev-only scope;
      determines whether customer onboarding gains a per-customer FIC step.
- [ ] **Invariant I6** (Model 1 only) — under MI-FIC the shared BFF UAMI can mint an assertion for any app
      registration that trusts it, moving part of the isolation boundary from resource-level to code-level.
      Raised with provisioning; not adopted unilaterally.

---

*AI-optimized specification. Original design: [`design.md`](design.md).*
