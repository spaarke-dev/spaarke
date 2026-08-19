# Design — Spaarke Auth v4: Zero-Secret BFF Confidential Credential

> **Status**: DESIGN DRAFT (research complete; ready for `/design-to-spec`) · **Date**: 2026-08-17
> **Epic**: Auth / Code Quality (#427) · **Risk**: HIGH (OBO = all delegated user auth)
> **Origin**: `code-quality-and-assurance-r3` task 011 / #3b (app-only Dataverse → MI, live on dev)
> **Evidence base**: [`notes/RESEARCH-FINDINGS.md`](notes/RESEARCH-FINDINGS.md) · [`notes/CREDENTIAL-INVENTORY.md`](notes/CREDENTIAL-INVENTORY.md) · [`notes/ASSESSMENT.md`](notes/ASSESSMENT.md) (origin seed)
> **Live state + Phase 0 prerequisites**: [`notes/PHASE-0-LIVE-VERIFICATION.md`](notes/PHASE-0-LIVE-VERIFICATION.md) (2026-08-19 — **prerequisites resolved; dev MI-FIC created**)
> **Cross-project**: [`notes/PROVISIONING-CHANGE-REQUEST.md`](notes/PROVISIONING-CHANGE-REQUEST.md) · [`notes/COORDINATION-DATAVERSE-ACCESS-UNIFICATION.md`](notes/COORDINATION-DATAVERSE-ACCESS-UNIFICATION.md)

---

## 1. Problem

The BFF authenticates to Graph, Dataverse, Power BI and Azure OpenAI through **eight distinct confidential-client
sites**. Seven of the eight use a **client secret**; only `CiamGraphClientFactory` is secret-free (certificate).

A single secret — `BFF-API-ClientSecret` — is fanned out across **five config keys** plus a **sixth lowercase Key
Vault alias** used by the Office add-in deploy, and is consumed by nine code paths. `#3b` moved the two *app-only*
Dataverse implementations to Managed Identity, but the secret cannot be removed because the **OBO (delegated) paths
still require a confidential-client credential** — and the codebase, the constraints file, and every prior audit
assumed that credential must be a *secret*.

**That assumption is wrong, and it is the root of the problem.** OAuth requires a confidential **credential**;
a secret is one of three ways to satisfy it. Microsoft's current guidance ranks the alternatives explicitly, and
places client secrets last: *"Development and testing only."*

Three consequences follow:

- **Security posture.** We run production delegated auth on the credential type Microsoft designates for dev/test.
- **Operational cost.** `customer-provisioning-orchestration-r1` must create, store and rotate a secret **per
  customer, forever** (24-month expiry, dedicated rotation handler).
- **Portability risk.** Entra app-management policies (GA) let any customer tenant block secret creation or cap
  secret lifetime on a service principal. For a multi-tenant ISV this is a deployment blocker, not just hygiene.

### 1.1 A finding that changes the option set

**ADR-028 does not document the OBO secret as an exception.** Its exception registry contains only E-1 (SpeAdmin)
and E-2 (Azure OpenAI), and its own preamble states that adding an exception *"requires a PR that updates this
list."* The OBO retention is asserted only in `.claude/constraints/auth.md:108`, `Sprk.Bff.Api/CLAUDE.md:110,221`,
and downstream guides.

Strictly read, **the OBO secret is currently an undocumented exception to ADR-028's own MUST**. There is therefore
no zero-cost "do nothing" option: every outcome requires ADR text work. This is a §6.5 ADR-conflict case and is
surfaced formally in §7.

### 1.2 Why prior auth projects left this latent — the root cause was a premise, not a coverage gap

This has been addressed before and kept resurfacing, so the failure mode itself is in scope.

The predecessor audit **did not miss the code**. [`bff-auth-surface-map.md`](../code-quality-and-assurance-r3/notes/bff-auth-surface-map.md)
mapped all nine secret consumers at `file:line`, traced the five config keys, and caught the
`auth-azure-resources.md` app-registration contradiction. Its inventory was excellent. It then concluded, at
`:199`: **"Verdict: NEVER-REMOVE."**

The stop was a **false premise treated as a platform constraint** — one sentence at
`.claude/constraints/auth.md:108`: *"OBO flow (OAuth spec requires confidential client + secret)."* That clause
converted a complete inventory into a closed question, then propagated: into ADR-028's silence (no exception was
registered, because there was assumed to be nothing to except), into `Sprk.Bff.Api/CLAUDE.md:110,221`, into six
deployment guides, and into an `adr-check` rule that flagged every OBO site with **no sanctioned alternative to
move to** — so each auth-touching task re-litigated the same finding and moved on.

**Consequence for this project's design**: the text was corrected on 2026-08-17 (§7.1), but text alone is what
failed last time. Recurrence is prevented by **forcing functions in code and CI (§5.5)**, not by another audit.
That is Goal 7, and it is a graduation criterion (§12).

## 2. Goals / non-goals

**Goals**
1. Decide — on evidence, via a prototype — the correct confidential-client credential for the BFF: **MI-FIC**,
   **certificate**, or **retained secret**.
2. Amend ADR-028 to state that decision explicitly, replacing today's undocumented gap.
3. If a secret-free credential is chosen: migrate all BFF-identity confidential clients to it, remove
   `BFF-API-ClientSecret` (and its six paths) from every environment, and relax the config validators that make it
   mandatory.
4. Leave `customer-provisioning-orchestration-r1` with a credential model that works for **both** Model 1 and
   Model 2, including the customer-owned-subscription case.
5. Keep local development working (`dotnet run`), including local OBO.
6. **Eliminate the Azure-first-party API keys that have a Managed Identity alternative** (§5.3.1, "Group 2"). These
   are the same latent-defect class as the OBO secret — a credential retained because a prior pass concluded it was
   required — and two of them already have a working MI path in-repo that simply isn't selected.
7. **Leave forcing functions behind** (§5.5) so a ninth secret-bearing confidential client cannot be introduced
   silently. This is the graduation criterion that distinguishes auth-v4 from its predecessors (§1.2).

**Non-goals**
- The **external / collaboration / module-host planes** (ADR-028 A1–A3). They are broker-only by design and
  exchange no downstream token. Amendment text must not weaken their "no OBO" invariants.
- The **inbound** side of auth (JWT validation, `AddMicrosoftIdentityWebApi`, webhook HMAC, API-key schemes).
- **Per-customer SpeAdmin secrets** (ADR-028 E-1) — those authenticate *other* applications, not the BFF identity.
- **Genuinely third-party API keys** — Bing Search, LlamaParse ("Group 1", §5.3.1). A key is the only mechanism
  these vendors offer. Scope is limited to one hygiene fix: `BingSearch:ApiKey` is read straight from config
  (`WebSearchHandler.cs:283`) rather than resolved from Key Vault by name the way LlamaParse already is.
- **Inbound HMAC / clientState / webhook signing keys** ("Group 3", §5.3.1) — `Communication:WebhookSigningKey`,
  `Communication:WebhookClientState`, `EmailProcessing:WebhookSigningKey`, and the tracking-footer signing key.
  These validate what *arrives*; they are not outbound credentials and have no MI equivalent. The tracking-footer
  signer is already correctly designed (KV-by-name, never bound into config). **Explicitly closed, not deferred** —
  stated here so the next audit does not re-open them.
- The **plaintext secrets in Dataverse columns** used by `BaseProxyPlugin`. Filed as a separate issue (§10).

## 3. Evidence summary

Full detail in [`notes/RESEARCH-FINDINGS.md`](notes/RESEARCH-FINDINGS.md). The load-bearing facts:

| # | Fact | Source |
|---|---|---|
| E1 | MI-as-FIC is **GA since 2025-05-08** (not preview) | Entra GA blog + how-to, no preview banner |
| E2 | **OBO works with a FIC-authenticated client** — the assertion is a standard `client_assertion`; Microsoft documents the OBO+FIC+MI wire protocol | Entra Agent ID OBO doc (updated 2026-08-10) |
| E3 | Microsoft ranks credentials: **certificateless (MI-FIC) = Highest**, certificate = fallback, **secret = "Development and testing only"**; *"Don't use password credentials."* | Identity.Web credentials overview (2026-04-19); Entra app-security best practices |
| E4 | ~~Adoption is **declarative** — `ClientCredentials: [{SourceType: "SignedAssertionFromManagedIdentity"}]` with ordered fallback~~ **CORRECTED 2026-08-19 — see E4′** | Microsoft.Identity.Web docs |
| **E4′** | **The declarative path is unavailable in this codebase.** Zero occurrences of `EnableTokenAcquisition` / `ITokenAcquisition` / `IDownstreamApi` / `ClientCredentials` in any `.cs` file — all 8 confidential clients hand-roll `ConfidentialClientApplicationBuilder`, and `AddMicrosoftIdentityWebApi` (`AuthorizationModule.cs:36`) is **inbound validation only**. `Spaarke.Dataverse.csproj:17` references only `Microsoft.Identity.Client` — no Identity.Web at all — so two sites cannot bind config even in principle. **The mechanism is `.WithClientAssertion(Func<AssertionRequestOptions,Task<string>>)` + `ManagedIdentityClientAssertion` (`Microsoft.Identity.Web.Certificateless`, not currently referenced).** Ordered fallback must be **built** into the provider seam, not inherited | Repo grep, 2026-08-19 |
| E5 | **The BFF app registration already has a working FIC** (`github-actions-deploy-staging`, audience `api://AzureADTokenExchange`); 1 of 20 used | Live `az` verification, this session |
| E6 | **The App Service runs user-assigned MI only** (`mi-bff-api-dev`) — MI-FIC's hard prerequisite, already satisfied | Live `az` verification |
| E7 | **No downstream service constrains the credential type** — Dataverse, Graph/SPE, Power BI, Azure OpenAI all validate only the resulting token. SPE's historical cert-only pocket is gone (registration moved to Graph v1.0) | Learn docs, 2026-07/08 |
| E8 | **Same-tenant rule**: the UAMI and app registration must share a tenant. Cross-tenant *resource* access works via a multitenant app consented into the customer tenant. Cross-cloud unsupported. 20-FIC cap | Entra how-to + considerations |
| E9 | **Dev App Service Plan is P1v3**, not the `B1` the IaC declares → **slots are available** for staged rollout | Live `az` verification |
| E10 | **Certificate is already proven in-repo** — `CiamGraphClientFactory` (KV PFX, key ephemeral in process) | `CiamGraphClientFactory.cs:129-133,154-170` |
| E11 | **No FIC automation exists** anywhere in the repo — exhaustively verified incl. untracked paths. No script and no Bicep resource creates a FIC for the BFF identity; even the existing GitHub Actions OIDC FIC was **hand-run** (`.github/D-11:61`, Application Administrator required). **FIC provisioning automation is the one new cost line** — it replaces secret creation *and* the rotation ceremony, so provisioning nets simpler | Repo audit |
| E12 | **Every Spaarke deployment shape is intra-tenant** (Model 1, Model 2-Spaarke, Model 2-customer-tenant) → MI-FIC covers all of them; the one shape that would need a certificate is **ruled out** (owner decision 2026-08-18) | [`notes/TENANCY-AND-CREDENTIALS.md`](notes/TENANCY-AND-CREDENTIALS.md) §3, §3.1 |

## 4. Options

| | **A. MI-FIC** | **B. Certificate (KV)** | **C. Retain secret** |
|---|---|---|---|
| MS ranking | Highest | Fallback | Dev/test only |
| Rotation | **None** (Azure-managed) | Per-cert, KV-automatable | Per-customer, forever |
| Prereqs in our tenant | ✅ already met (E5, E6) | ✅ precedent exists (E10) | ✅ current state |
| Provisioning | H4 loses secret creation + rotation ceremony; H3 gains a per-customer FIC step (**to build**) | H3/H4 provision + rotate per-customer certs (**to build**) | unchanged; permanent per-customer cost |
| Tenancy (see [`notes/TENANCY-AND-CREDENTIALS.md`](notes/TENANCY-AND-CREDENTIALS.md)) | ✅ **Covers every deployment shape** — Model 1, Model 2-in-Spaarke-tenant and Model 2-in-customer-tenant are all intra-tenant | **Not required by any shape.** Retained as ADR-028 A4's sanctioned alternative (policy) and proven in-repo, but nothing to build | unchanged; **a hardened customer tenant can block secrets outright** via app-management policy |
| Local dev | ordered-fallback entry required | ordered-fallback entry required | works today |
| ADR work | amend line 24 + amendment A4 | amend line 24 | **still required** — add exception E-3 |

**Recommendation to test (not to assume): Option A, with Option B as the designed fallback.** Option A wins on
every documented axis and both of its usual blockers are already resolved in our tenant. The spike exists because
documentation cannot confirm the one thing that matters: that **our** OBO chain works under a FIC-authenticated
client. If Phase 0 fails or reveals a Model-2 blocker, Option B is a drop-in with in-repo proof.

The architecture is deliberately **credential-agnostic**: a single provider seam (§5) makes A and B differ by
configuration, not by call-site code — so the Phase 0 outcome does not invalidate the implementation work.

## 5. Proposed architecture

### 5.1 One confidential-credential provider

Today, seven call sites each roll their own credential handling. The design introduces **one** singleton provider
that supplies the confidential credential (and cached MSAL clients) to every BFF-identity confidential client.

- **Extends** `Infrastructure/Auth/ManagedIdentityCredentialFactory.cs` + the DI singleton `TokenCredential`
  (`Program.cs:44-47`) — the existing seam, already documented as the extension point by
  `ContentSafetyTokenProvider.cs:15-22`.
- **Reuses** the process-wide static CCA cache pattern already proven in `DataverseUserClient.cs:55-56,91`.
- Selection is **config-driven and ordered** (MI-FIC → KV certificate → dev secret), per E4 — the same mechanism
  serves production, the fallback option, and local dev.

**Component justification (root CLAUDE.md §11)**

1. **Existing** — `ManagedIdentityCredentialFactory` provides a UAMI-pinned `TokenCredential` for
   `Azure.Identity`-based app-only calls. It does **not** provide MSAL confidential-client credentials or client
   assertions, and is not used by any of the 8 CCA sites (verified by grep; see `CREDENTIAL-INVENTORY.md` §2).
2. **Extension** — Yes, and that is the plan: extend it rather than introduce a parallel abstraction. The new
   surface is the assertion-callback + CCA-cache capability added to that existing seam.
3. **Cost of doing nothing** — concrete, not hypothetical: seven call sites would each need independent assertion
   plumbing and independent lifetime fixes; the two per-request construction sites (§5.2) would each rebuild an
   MSAL client per request, discarding its token cache; and every future credential change would be a seven-site
   edit. The credential type could not be switched (A↔B) by configuration.

### 5.2 Prerequisite fixes (independently correct; land first)

- **DI lifetimes** — `DataverseAccessDataSource` is a **transient** typed HttpClient (`SpaarkeCore.cs:39`) and
  `AgentTokenService` is **scoped** (`AgentModule.cs:24`), so each builds a fresh MSAL client per request.
  Client assertions require shared/cached clients. Fix to singleton-cached clients using the `DataverseUserClient`
  pattern.
- **MI-flag gating defect** — `DataverseAccessDataSource.cs:53` and `DataverseWebApiClient.cs:42` **never read**
  `Graph:ManagedIdentity:Enabled`; secret *presence* alone selects the secret path, so they run on the secret today
  despite MI being enabled on dev. Correct the gating.

Both are pre-existing defects worth fixing regardless of the credential decision, and both de-risk the migration.

### 5.3 Migration surface

Seven BFF-identity confidential clients move to the provider: `GraphClientFactory` (OBO),
`DataverseAccessDataSource` (OBO + app-only), `DataverseUserClient` (OBO), `AgentTokenService` (OBO),
`ReportingEmbedService` + `ReportingProfileManager` (Power BI app-only), and the residual
`ClientSecretCredential` fallbacks in `DataverseServiceClientImpl` / `DataverseWebApiService`.

`SpeAdminTokenProvider` and `SpeAdminGraphService` are **out of scope** — they authenticate per-customer *owning
applications*, not the BFF identity (ADR-028 E-1). `CiamGraphClientFactory` already meets the bar.

Config validators to relax: `DataverseOptions.cs:32` (`[Required]` + ValidateOnStart — the startup-crash
dependency), `GraphOptionsValidator.cs:20-23`, `AgentTokenOptions.cs:38`.

**Design constraints on the provider** (both are silent-failure generators; each needs a test):

- **Never conflate the UAMI clientId with the app-registration clientId.** MI-FIC requires holding both
  simultaneously — the UAMI's to mint the assertion, the app-reg's to build the CCA.
  `GraphClientFactory.cs:54` already resolves `_clientId = AZURE_CLIENT_ID ?? API_APP_ID`, and in Azure
  `AZURE_CLIENT_ID` is deliberately set to the **UAMI's** clientId. The dev subscription holds **five** UAMIs, one
  named `spaarke-bff-identity` that is *not* the BFF's. Resolve identities by resource ID, never by name.
- **`AddMicrosoftIdentityWebApi` binds the same `AzureAd` section** that carries `AzureAd:ClientSecret`
  (`DataverseUserClient.cs:85`). Inbound validation and outbound OBO share one config section. Almost certainly
  benign — but "almost certainly" is how the premise in §1.2 got established, so **inbound validation is verified
  after every config change** (§6.1), not assumed.

Estimated ~350–550 LOC across ~15 files — **understated**; that figure assumed the declarative adoption ruled out
by E4′. Re-estimate at spec time to include the ordered-credential selector, the assertion cache, and the
injection path into a shared library with no Identity.Web dependency.

### 5.3.1 Non-Entra credential groups

Twelve non-Entra credentials exist. They split three ways, and only Group 2 is in scope:

| Group | Credentials | Disposition |
|---|---|---|
| **1 — third-party** | `BingSearch:ApiKey` (`WebSearchHandler.cs:283`) · LlamaParse (`LlamaParseClient.cs:117-126`) | **Out.** A key is the only mechanism. One hygiene fix: Bing reads config directly; make it KV-by-name like LlamaParse already is |
| **2 — Azure first-party running on a key while MI is available** | `AiSearch:ReferencesApiKey` (`InternalIndexProvider.cs:80`) · `AiSearch:ApiKeySecretName` (`AiSearchOptions.cs:6`) · `AzureOpenAI:ApiKey` (`AiModule.cs:115,122`, ADR-028 **E-2**) · `AiSafety:ContentSafety:ApiKey` (`ContentSafetyAuthHandler.cs:41,72`) · DocIntel ×3 (`DocumentIntelligenceOptions.cs:42,152,303`) · ServiceBus SAS (`ServiceBusOptions.cs:15`) · `Analysis:PromptFlowKey` (`appsettings.json:118`, verify) | **In.** Same latent-defect class as the OBO secret. **Two already have a working MI path in-repo that isn't selected** — `ContentSafetyTokenProvider.cs:55` and `MembershipJunctionUpdaterHost.cs:120` (namespace + MI). E-2 may be a one-config-change fix: check `spaarke-openai-dev` for a **custom subdomain**, the documented root cause of the MI-401 |
| **3 — inbound HMAC / clientState** | `Communication:WebhookSigningKey` + `WebhookClientState` (`CommunicationOptions.cs:47,65`) · `EmailProcessing:WebhookSigningKey` (`:192`) · tracking-footer key (`TrackingTokenSigner.cs:122-176`) | **Explicitly closed.** Inbound validation, no MI equivalent, correctly designed today |

### 5.4b Operational estate (larger than the code surface)

**11 PowerShell scripts** reference `ClientSecret`, not the 2 the inventory cites: `Configure-ProductionAppSettings.ps1`,
`Register-EntraAppRegistrations.ps1`, `Rotate-Secrets.ps1`, `Seed-ProductionKeyVault.ps1`, `Provision-Customer.ps1`,
`Reconcile-DemoEnvironment.ps1`, `Deploy-Release.ps1`, `Deploy-DataverseSolutions.ps1`, `Test-EntraAppRegistrations.ps1`,
`Test-SharePointToken.ps1`, `naming-conformance-check.ps1` — plus ~25 documents. **Phase 5 gates on reconciling
these**, not on deleting a Key Vault secret. Three of them (`Register-EntraAppRegistrations.ps1`,
`Rotate-Secrets.ps1`, `Seed-ProductionKeyVault.ps1`) are where FIC automation belongs, and they are already
idempotent and tenant-aware — so the automation is a swap, not greenfield.

### 5.5 Forcing functions (the anti-recurrence requirement)

The predecessor audit did **not** miss the code — [`bff-auth-surface-map.md:199`](../code-quality-and-assurance-r3/notes/bff-auth-surface-map.md)
inventoried all nine consumers correctly and then concluded **"Verdict: NEVER-REMOVE"** on a false premise (§1.2).
More auditing does not prevent recurrence; a build that fails does. Three mechanisms, all cheap:

1. **ArchTest ban** — no `src/server/**` type may call `.WithClientSecret(` or construct `ClientSecretCredential`
   outside a named allowlist (ADR-028 **E-1** SpeAdmin per-customer apps; **E-3** until Phase 5). New site → red
   build, not a review comment. Same pattern as the existing `GodClassGuardTests`.
2. **Credential census test** — assert the count of confidential-client construction sites equals a checked-in
   number with a per-site reason. Adding a ninth fails until the census is updated. *This is what would have caught
   `SpeAdminTokenProvider` and `SpeAdminGraphService`, both absent from the origin seed.*
3. **Startup assertion** — outside Development, fail fast if any BFF-identity credential resolves to a secret after
   Phase 5, rather than silently degrading.

Per ADR-038 these are `tests/integration/seam/**` + ArchTest, not DI-registration tests.

### 5.4 Test strategy

**46 test files** seed dummy secrets to satisfy ValidateOnStart and the `GraphClientFactory` constructor. Relaxing
`[Required]` is backward-compatible, but **adding a required constructor argument breaks all 46**. The provider must
therefore have a test-friendly default. Per ADR-038, coverage goes to `tests/integration/seam/**` (the credential
seam), not to DI-registration tests.

## 6. Rollout and rollback

Staged, per environment, **slot-based** — dev is P1v3 so slots are available (E9), though none exist yet and must
be created.

| Phase | Content | Gate |
|---|---|---|
| **0. Spike** | ⚙️ **Prerequisites RESOLVED 2026-08-19** — [`notes/PHASE-0-LIVE-VERIFICATION.md`](notes/PHASE-0-LIVE-VERIFICATION.md). The dev MI-FIC exists (`mi-bff-api-dev-assertion`); app-reg/UAMI same-tenant, UAMI-only identity and P1v3 all verified live. **Remaining**: create a dev slot, deploy the assertion spike, prove OBO → Graph/SPE and → Dataverse `user_impersonation`, long-running OBO, the built ordered fallback (per E4′), Power BI, and the **Model 2 cross-tenant resource** shape | Empirical proof, or pivot to Option B. **Decision recorded here.** **Not a scheduling gate** — no external admin dependency remains |
| **1. ADR** | ✅ **DONE 2026-08-17** — ADR-028 **A4** + exception **E-3**, `.claude/constraints/auth.md` corrected, `adr-check`/`adr-aware`/`patterns` enforcement fixed (§7.1) | Owner-directed; applied |
| **2. Prereqs** | DI lifetime fixes + MI-flag gating fix (§5.2) | Tests green; no behavior change |
| **3. Provider** | Build the credential provider; migrate call sites **with the secret still present** as the ordered fallback | Both credentials work; fallback proven |
| **4. Flip** | Promote MI-FIC to first position per environment, via slot swap | Full OBO verification (§6.1) per env |
| **5. Removal** | Remove the secret from app settings, then Key Vault — **including the lowercase `bff-api-client-secret` alias** (Office add-in). Relax validators | Soak period; operator sign-off |

**Rollback** at every phase is reordering the `ClientCredentials` list (config-only) or a slot swap back. The secret
is not deleted until Phase 5, after a soak. **No in-session flips** — #3b attempt 1 took dev down (SIGABRT from an
eager connect under `ValidateOnBuild`).

### 6.1 OBO verification checklist (run per environment, per flip)

SPE document upload/download/preview · chat + `dataverse.*` AI tool calls · Office add-ins (Outlook/Word) ·
M365 Copilot agent (`/api/agent`) · **Dataverse row-level authorization** (`PermissionsEndpoints`, the AI
authorization filters) · send-as-user email. Note the failure mode is **fail-closed** — broken OBO locks users out
rather than exposing data, which is the safe direction but means outages are immediate and total.

## 7. ADR Tensions (root CLAUDE.md §6.5) — ✅ RESOLVED 2026-08-17

> **Outcome: path B (amendment), owner-directed and applied.** ADR-028 now carries **Amendment A4** (secret-free
> confidential credential for OBO and BFF-identity clients) and transitional exception **E-3** (the retained
> secret, time-boxed to this project). The enforcement surfaces that caused the recurring CI churn were corrected
> in the same pass — see §7.1. The original conflict statement is retained below as the decision record.
>
> **Consequence for phasing**: Phase 1 (§6) is **complete before implementation starts**. The ADR no longer blocks;
> it now *specifies* the target. Remaining ADR-adjacent work is documentation follow-through (§10).

### 7.1 What was changed (applied 2026-08-17)

| File | Change |
|---|---|
| `.claude/adr/ADR-028-spaarke-auth-architecture.md` | Split the line-24 MUST into **app-only** (`DefaultAzureCredential`) vs **confidential client** (MI-FIC / KV cert, never a secret); added **Amendment A4**; added exception **E-3**; replaced the Key Patterns C# sample (which taught `ClientSecretCredential` as the fallback) with the app-only + client-assertion pair |
| `.claude/constraints/auth.md` | Corrected the false *"OAuth spec requires confidential client + secret"* clause — **the single sentence that foreclosed this question in every prior audit** — and added the A4 MUST/MUST NOTs |
| `.claude/skills/adr-check/references/adr-validation-rules.md` | **Fixed the CI churn at its source**: the exclusion filter was `$_.Path -notmatch 'OBO\|onBehalfOf'` — a **path** filter that never matched, since OBO appears in file *content*. Every OBO site therefore tripped the rule on every run with no sanctioned alternative to move to. Replaced with an E-3/E-1 allowlist, plus a new rule that flags **new** `.WithClientSecret` sites and per-request CCA construction |
| `.claude/skills/adr-check/SKILL.md`, `.claude/skills/adr-aware/SKILL.md` | Added the A4 row; corrected "for Graph" → "for app-only" |
| `.claude/patterns/auth/service-principal.md` | Updated to A4; corrected the stale claim that the Dataverse SDK uses `ClientSecretCredential` (migrated to MI by #3b) |

**Why this mattered beyond tidiness**: the pre-A4 rule was *unsatisfiable* for OBO — `DefaultAzureCredential`
cannot perform an OBO exchange — so the OBO paths violated it permanently and every auth-touching task
re-litigated the same finding. That is the "stepping on this repeatedly" problem; it is now fixed at the rule,
not worked around per task.

### 7.2 Original conflict statement (decision record)

🔔 **ADR Conflict — Resolution Required**

- **ADR in question**: ADR-028 — Spaarke Auth Architecture
- **Specific rule** (`.claude/adr/ADR-028-spaarke-auth-architecture.md:24`): *"**MUST** use `DefaultAzureCredential`
  (managed identity) for all server outbound … NOT `ClientSecretCredential`. Documented exceptions: (1) Per-tenant
  SpeAdmin container-type ops; (2) Azure OpenAI / AI Services data plane."* The exception preamble (line 133) adds:
  *"Adding an exception requires a PR that updates this list."*
- **Conflict**: The OBO paths use `ClientSecretCredential` / `.WithClientSecret` and are **not** in the exception
  list — an undocumented deviation. The rule as written also cannot be satisfied literally, because
  `DefaultAzureCredential` cannot perform an OBO exchange; the correct secret-free mechanism is a **client
  assertion** derived from the managed identity, which the ADR does not contemplate.
- **Proposed path**: **B — ADR amendment.** Amend line 24 to require a *secret-free confidential credential*
  (MI-FIC preferred, KV certificate as the sanctioned alternative) for BFF-identity confidential clients, and add
  amendment **A4** recording the decision, the evidence, and the retained E-1 carve-out.
- **Rationale**: Microsoft's current guidance ranks secrets last and explicitly advises against them (E3); the
  platform prerequisites are already satisfied in our tenant (E5, E6); and the ADR's own registry requirement means
  the present state is non-compliant either way.
- **Impact if accepted**: ADR-028 line 24 + Key Patterns sample; `.claude/constraints/auth.md:108`;
  `Sprk.Bff.Api/CLAUDE.md:110,221`; `docs/guides/auth-deployment-setup.md:390`;
  `docs/guides/SECRET-ROTATION-PROCEDURES.md`; the surface map's NEVER-REMOVE verdict; provisioning's
  `spec.md:242` / `design.md:783` never-delete MUST; four `.claude/patterns/auth/*.md` files.
- **Alternative considered and rejected**: **Path A (project-scoped exception)** — write the OBO secret in as
  exception E-3 and stop. Rejected as the *primary* path because it documents a state Microsoft designates as
  dev/test-only and leaves per-customer rotation as a permanent cost. **It remains the correct fallback if Phase 0
  fails**, and in that case it is mandatory, not optional — the current undocumented state cannot simply persist.

## 8. Cross-project coordination

- **`customer-provisioning-orchestration-r1` — act now, not later.** It is **executing** (PR #779, ~68%), not
  design-phase as the seed stated. H3 (per-customer app registration), H4 (KV secret + rotation handler), H9
  (blue-green slot deploy) and H10 (Dataverse app user) are already implemented on that branch, and it has already
  logged MI-FIC as risk **R23** with the 20-FIC cap. **Minimum ask**: keep the "configure BFF confidential
  credential" step pluggable, and contribute Model-1/Model-2 constraints as first-class Phase 0 input. Auth-v4's
  outcome will land as a change request against shipped handlers.
- **`dataverse-access-unification-r1` (RED-4 C)** — **not a prerequisite; expect parallel execution.** The earlier
  "let it land first" framing is **retracted** (rationale in [`notes/COORDINATION-DATAVERSE-ACCESS-UNIFICATION.md`](notes/COORDINATION-DATAVERSE-ACCESS-UNIFICATION.md) §2).
  It does not touch `GraphClientFactory`, and the two files it deletes (`DataverseWebApiService`,
  `DataverseWebApiClient`) are **app-only** — none of auth-v4's OBO risk depends on it. The overlap is **four
  files** with an explicit contract (§4 of that note); `DataverseServiceClientImpl.cs` is the only one needing real
  sequencing, since their decomposition relocates the credential block we edit in place. Merging the projects is
  considered and rejected in §3 of that note — it would couple a fail-closed credential migration to a ~5,600-LOC
  security-semantics refactor under one rollback boundary.
- **Open PR #293** (`Azure.Identity` 1.17.1→1.21.0) is directly relevant — newer `ClientAssertionCredential` /
  `ManagedIdentityCredential` behavior. Coordinate rather than conflict.
- **`speadmin-decomposition-r1`** decomposes `SpeAdminGraphService` — out of auth-v4's scope but adjacent; confirm
  no overlap at task-creation time.

## 9. Hot-path declaration

<hot-path-declaration>
  <bff>Y</bff>                    <!-- 7 confidential-client sites + DI + config validators -->
  <spaarke-ai>N</spaarke-ai>
  <ci-workflows>N</ci-workflows>  <!-- but note: GitHub Actions OIDC FIC already exists on the same app reg -->
  <skill-directives>Y</skill-directives>  <!-- .claude/adr/ADR-028, .claude/constraints/auth.md, .claude/patterns/auth/* -->
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>

### Placement justification (root CLAUDE.md §10)

All changes belong **in the BFF and `Spaarke.Dataverse`** because they modify how existing BFF-owned code
authenticates. No new endpoint, background worker, or client surface is added. The one new component (§5.1) is an
extension of the existing `Infrastructure/Auth/` seam, registered as a singleton in the existing DI module.
**Publish-size impact is expected to be ~0** — `Microsoft.Identity.Web.Certificateless` may be added (small; it is
part of the Identity.Web family already referenced). Baseline to report against: **~44.96 MB incl. PDBs**
(net10, 2026-08-13); ceiling 60 MB. Every BFF task reports measured size + delta and runs the CVE scan per §10.4–.5.

## 10. Follow-ups to file (independent of the decision)

1. Correct `.claude/constraints/auth.md:108` — *"OAuth spec requires confidential client + secret"* → *"requires a
   confidential credential"*. **This single clause foreclosed the question in every prior audit.**
2. Fix the MI-flag gating defect (`DataverseAccessDataSource`, `DataverseWebApiClient`) — §5.2.
3. Update the stale researcher memory recording MI-as-FIC as "preview" (GA since 2025-05-08).
4. Reconcile `stacks/dev.bicepparam:12` (`B1`) with live (`P1v3`) — IaC drift; also note master IaC creates only a
   system-assigned identity while live uses a UAMI (the UAMI Bicep lives on the provisioning branch).
5. Refresh `docs/architecture/auth-azure-resources.md` — it claims system-assigned MI and contradicts itself on
   which app registration owns `BFF-API-ClientSecret`. Portal-confirm before automating any removal.
6. Rotate the live Service Bus SAS key found in a local `appsettings.Development.json`.
7. Re-test ADR-028 **E-2** (Azure OpenAI MI 401): **check for a custom subdomain first** — Microsoft documents that
   as the root cause of exactly this failure. Potentially a one-config-change secret elimination, independent of OBO.
8. File the plaintext-secrets-in-Dataverse-columns issue (`BaseProxyPlugin.cs:121-124`, `SimpleAuthHelper.cs:19-26`).
9. Clean up the duplicate lowercase KV alias `bff-api-client-secret` and the orphaned `Graph-API-ClientSecret`.

## 11. Open questions for `/design-to-spec`

1. ~~**Scope boundary**~~ — **CLOSED 2026-08-19 (owner).** Power BI is **in**. Non-Entra keys split three ways per
   §5.3.1: **Group 2 in** (Azure first-party with an MI alternative), Group 1 out (genuine third-party; one Bing
   hygiene fix), Group 3 explicitly closed (inbound HMAC).
2. ~~**Phase 0 owner and environment**~~ — **CLOSED 2026-08-19.** Owner runs the project; AAD admin authenticated
   in-session. The dev MI-FIC was created 2026-08-19, removing the external-admin dependency permanently. A dev
   slot must still be created — routine, and needed for Phase 4 regardless. Detail:
   [`notes/PHASE-0-LIVE-VERIFICATION.md`](notes/PHASE-0-LIVE-VERIFICATION.md).
3. ~~**Provisioning coordination mechanism**~~ — **CLOSED 2026-08-19.** Standalone change-request document:
   [`notes/PROVISIONING-CHANGE-REQUEST.md`](notes/PROVISIONING-CHANGE-REQUEST.md). Sibling coordination note for
   the parallel Dataverse project: [`notes/COORDINATION-DATAVERSE-ACCESS-UNIFICATION.md`](notes/COORDINATION-DATAVERSE-ACCESS-UNIFICATION.md)
   (which also **retracts** the earlier "let it land first" framing — it is not a prerequisite; §2 of that note).
4. **Tenancy + credential per deployment shape** — **SETTLED**, documented in
   [`notes/TENANCY-AND-CREDENTIALS.md`](notes/TENANCY-AND-CREDENTIALS.md): **every deployment shape is intra-tenant,
   so MI-FIC covers all of them**; the Spaarke-owned-app-reg-with-customer-tenant-compute shape is explicitly ruled
   out (§3.1, owner decision 2026-08-18); **no certificate provisioning is needed**; the 20-FIC cap is closed out as
   a non-factor. The one remaining *decision* is that file's **§4: which app registration the shared Model 1 BFF
   authenticates as** (one shared multitenant app vs one per customer) — it decides whether onboarding creates a FIC
   per customer or none, and does **not** affect feasibility. Provisioning's call.
5. ~~**Certificate fallback readiness**~~ — **CLOSED 2026-08-18.** No deployment shape requires a certificate
   (E12), so no certificate provisioning is built. Option B remains the ADR-028 A4 sanctioned alternative as
   policy — and the in-code fallback if the Phase 0 spike fails — but carries no provisioning work.

## 12. Graduation criteria

- [ ] Phase 0 spike answers §11 and the §5 must-prototype list; credential decision recorded with evidence.
- [ ] ADR-028 amended (A4, or exception E-3 if Option C) and merged; `.claude/constraints/auth.md:108` corrected.
- [ ] DI lifetime + MI-flag gating defects fixed and verified.
- [ ] Every BFF-identity confidential client uses the provider; OBO verified per §6.1 in each live environment.
- [ ] `BFF-API-ClientSecret` removed from app settings and Key Vault — **all six paths incl. the lowercase alias** —
      or an explicit documented decision to retain it.
- [ ] `DataverseOptions.ClientSecret` `[Required]` and the two other validators relaxed consistently.
- [ ] Local `dotnet run` works, including OBO, with the documented fallback.
- [ ] **Group 2 non-Entra credentials (§5.3.1) migrated to MI** — or, per credential, a documented reason not to.
- [ ] **The three forcing functions (§5.5) are merged and failing correctly** on a deliberately-introduced ninth
      secret-bearing confidential client. *This is the criterion that distinguishes auth-v4 from its predecessors.*
- [ ] **Operational estate reconciled (§5.4b)** — 11 scripts + the docs, including the lowercase KV alias.
- [ ] Inbound token validation verified unaffected after every config change (§6.1).
- [ ] Provisioning coordination closed: its credential step matches the chosen model for Model 1 **and** Model 2.
- [ ] `/test-diet` run at wrap-up; publish size reported against the 44.96 MB baseline (note: `Microsoft.Identity.Web.Certificateless` is a new reference).
