# Provisioning Constraints

> **Last Reviewed**: 2026-10-06 (T244 — keyless stamp resources rule added; T246 — Content Safety joins the keyless set)
> **Reviewed By**: customer-provisioning-orchestration-r1 task 203a per punch list row A08
> **Load when**: task tags include `provisioning`, `provisioning-run`, `l2-controlplane`, `provisioning-handler`, `customer-provisioning`
> **Wired into**: `.claude/skills/task-execute/SKILL.md` Step 4a tag map
> **Sibling constraints**: `.claude/constraints/{api,pcf,auth,data,ai,jobs,testing,config,bff-extensions}.md`

## Purpose

Cross-cutting constraints for customer-provisioning code — the L2 control-plane (`src/server/services/Sprk.Provisioning.ControlPlane.*/**`), Bicep infra (`infrastructure/bicep/**`), operator scripts (`scripts/provisioning*/`), and the `/provision-environment` L3 skill. Applies whether the task touches a handler, a template, a preflight check, or the L3 skill wiring.

**Load this alongside**: `.claude/constraints/bff-extensions.md` (for any BFF-side work surfaced during provisioning), `.claude/constraints/auth.md` (for H3/H4/H10), `.claude/constraints/data.md` (for H5/H6/H7 Dataverse ops), and the pattern files at `.claude/patterns/provisioning/*.md`.

## Tenant-isolation invariants (I1–I5) — BINDING per design.md §4D

Any code path in the L2 control-plane, handlers, BFF-provisioning surface, or operator scripts that touches customer resources MUST enforce these:

- **I1 — Explicit tenantId** (FR-28): the operator's `tenantId` is passed explicitly on every operation. **NEVER** hardcode a "default tenant" in provisioning scripts. Handlers reject requests missing `tenantId`. Operator's own AAD identity per NFR-11 — bootstrap under a service principal is a HARD violation.
- **I2 — AI Search unconditional `tenantId` filter** (FR-29): every AI Search query MUST include `tenantId eq '{tenantId}'` in the filter clause. The `spaarke-session-files` index uses tenantId + sessionId dual-filter (ADR-014 strengthens). A query missing this filter is a silent cross-tenant leak.
- **I3 — Cosmos partition-key predicate** (FR-30): every Cosmos read/write MUST include the partition-key `/customerId` predicate. Cross-partition queries are audit-flagged and treated as bugs.
- **I4 — SPE container ID resolution** (FR-31): SPE container IDs derived from tenant context via `ITenantContainerResolver`. NEVER hardcode a container ID or resolve via lookup that isn't tenant-scoped.
- **I5 — Graph token per-tenant** (FR-32): every Graph token acquisition uses tenant `{tenantId}` — never the operator's home tenant, never a shared "app-tenant" for cross-tenant ops. `ITokenAcquisition.GetAccessTokenForAppAsync(tenant)` is the discipline.

Violations of I1-I5 surface via ArchTests (planned in task 204e) + are Critical findings in code-review Step 6.

## KV credential lifecycle — BINDING per ADR-028 Amendment A4 + E-3 closure (§6.5 resolution 2026-08-25; supersedes the r3-handoff never-delete list)

> **History**: the r3-handoff blanket rule "NEVER delete `Dataverse-ClientSecret` / `BFF-API-ClientSecret`" was
> superseded on 2026-08-24 when auth-v4 task 033 closed ADR-028 Exception E-3 — exactly the supersession
> pre-authorized by this project's spec.md FR-39. E-3 closure facts: app settings `API_CLIENT_SECRET` /
> `AzureAd__ClientSecret` / `Dataverse__ClientSecret` / `AgentToken__ClientSecret` removed 2026-08-24 16:50:25Z;
> KV `BFF-API-ClientSecret` + `bff-api-client-secret` deleted 17:14:40Z (**soft-deleted, recoverable to
> 2026-11-22 — not purged**). `Dataverse-ClientSecret` was deliberately NOT deleted. Resolution record:
> `projects/customer-provisioning-orchestration-r1/notes/decisions/adr-028-a4-integration-conflict-resolution.md`
> (owner-approved 2026-08-25; Q7 owner narrowing recorded therein).

**1. NEVER create, seed, restore, or re-introduce `BFF-API-ClientSecret` (either casing — `bff-api-client-secret`
included) in any secret-free environment.** Secret-free = `spaarke-bff-dev` (flipped 2026-08-24) and EVERY
newly-provisioned environment on the secret-free contract (`Graph__Credentials__Order__0=ManagedIdentityFederated`
as the ONLY entry + `Graph__Credentials__RequireSecretFreeIdentity=true`). H4 **omits** the secret entirely —
**no sentinel value** (the ordered selector cannot distinguish a sentinel from a real secret and fails opaquely
with `AADSTS7000215`; positive migration markers go in a provisioning-state field or KV tag, never the credential
slot — auth-v4 §9.1). A `.WithClientSecret(...)` site on the BFF identity is a plain ADR-028 A4 violation — E-3
is closed; there is no exception to cite. The FR-39 credential-type seam in H3/H4 stays in code (pluggability),
but the secret path may only be selected for a prong-3 unmigrated environment — never for new provisioning.
**Since task 225b (2026-10-02, G21) secret-free is the H4 default** (`KvSecretsPopulationOptions.RequireSecretFreeIdentity`
= `true`, also set explicitly by the Worker Bicep) and H4 omits **both** `BFF-API-ClientSecret` and
`Dataverse-ClientSecret` on every new stamp. Rule 2's hold protects the EXISTING live `Dataverse-ClientSecret`
copy from deletion — it never required H4 to write a new one.

**2. NEVER purge or delete the rollback copies before 2026-11-23** (Path A, time-boxed): do not purge the
soft-deleted `BFF-API-ClientSecret` / `bff-api-client-secret` KV entries, and do not delete the still-live
`Dataverse-ClientSecret` KV secret. Its old rationale is stale (the shared-lib consumer is migrated on master),
but it is auth-v4's live rollback copy during the soak window (obligation 051-E; rollback proven config-only,
decisions/031 §5.6 — NOTE: proven on a slot pair already carrying `keyVaultReferenceIdentity`; a fresh slot
needs that site property re-asserted first). Retirement belongs to auth-v4's runbook — never a provisioning
sweep, test cleanup, or "temporary" removal. **Sunset 2026-11-23**: auth-v4 retires it or the owner re-reviews;
do not silently extend.

**3. Unmigrated environments — the original rule survives unchanged**: for any environment whose LIVE credential
order still contains `ClientSecret`, the original never-delete for BOTH secrets + the FR-35 pre-check gate
remain fully in force until auth-v4's retirement runbook executes there. **Q7 owner narrowing (2026-08-25)**:
prong 3 applies to `spaarkedev1` and any as-yet-unprovisioned Model 2 stamp; H4 executor MUST NOT provision
new Model 2 stamps under this prong until A36-A42 land per Q6 (per-customer stamps under prong 3 are barred
during the transition window).

**4. E-1 secrets are OUT OF SCOPE and stay protected indefinitely**: per-customer SpeAdmin container-type secrets
(ADR-028 E-1 — open, architectural, unaffected by A4/E-3) authenticate OTHER applications, not the BFF identity.
`sprk_specontainertypeconfig` rows + their KV secret names keep `never_delete: true` with no sunset.

Additionally, any secret with `never_delete: true` in `scripts/canonical-secret-catalog/manifest.yaml` MUST NOT
be deleted regardless of context. The manifest's two BFF-identity entries are re-annotated per this resolution;
until re-annotation lands, read their `never_delete: true` as prongs 1-3 above, not the retired blanket rule.

`§7.9 pre-check gate` — BINDING per spec.md FR-35, UNCHANGED: BEFORE any secret rename/delete, verify LIVE
App Service + KV + Dataverse-persisted config for references. Skipping the pre-check is a HARD violation.

## Publish-size ≤60 MB — BINDING per CLAUDE.md §10 NFR-01

Every BFF-touching task (including provisioning tasks that modify BFF DI, register new services, or update BFF app-settings via H4b) MUST measure and report BFF publish size.

- Command: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`
- Measure: compressed size (`du -sh deploy/api-publish/`) + delta vs prior baseline (currently ~44.96 MB incl. PDBs, dotnet-10 framework-dependent linux-x64).
- Thresholds: `≥+5 MB single-task delta` → explicit justification required; `≥55 MB cumulative` → architecture review; `≥60 MB` → HARD STOP.

Report absolute + delta in task notes / PR description. See `.claude/constraints/azure-deployment.md` "BFF Publish-Size Per-Task Verification Rule" for full mechanic.

## Handler registration completeness — BINDING per ADR-032 + `.claude/patterns/provisioning/handler-registration-completeness.md`

- Every new `IProvisioningHandler` touches five places: `HandlerIds.cs` (const + `Dispatchable`), the handler class, its concrete + dependency registrations, the keyed forwarder in `HandlerDispatchRegistrationModule.cs`, and `DagAdvancer.HandlerDependencies`. Missing the forwarder → the Worker dead-letters the message as `NoHandler`; a missing dependency → `HandlerResolutionFailed`; a missing DAG entry → the reconciler never dispatches it.
- `HandlerRegistrationCompletenessTests` and the HANDLER-12 parity test in `DagAdvancerTests` MUST pass on every PR (currently 20 dispatchable ids — T226 retired H4-shared on 2026-09-30; adding a handler → 21).
- Handler contract: `IProvisioningHandler.HandleAsync(HandlerEnvelope, CancellationToken)` returns `HandlerResult` — closed: `Success(IdempotencyKey)` or `Failure(FailureClass, RejectionCode, Diagnostic)`; `FailureClass` = Resumable | RetryableWithCleanup | QuarantineRequired | SuccessfulButDrifted (design §4C).
- Feature-gated handlers follow ADR-032 P1/P2/P3 — null-impl UNCONDITIONAL outside the gate; real-impl CONDITIONAL inside. Last-write-wins for the same key resolves correctly at runtime.

## ADR-032 F.1 asymmetric-registration — BINDING per `.claude/constraints/bff-extensions.md` § F.1

Cross-references the BFF-side constraint verbatim; applies analogously to L2 control-plane modules. Every conditionally-registered service (inside `if (options.EnableX)` block in `*Module.cs`) MUST have a null-object counterpart registered UNCONDITIONALLY. No unconditional consumer may inject a conditionally-registered service — inject the INTERFACE + let the kill-switch resolve at runtime.

Static-scan recipe + IActionSeam case study: see `.claude/patterns/provisioning/null-object-kill-switch-anti-pattern.md`.

## Fixture-Config-FIRST + Empirical-Reproduction-FIRST (F.2 / F.3)

When a provisioning-adjacent test suspects a DI issue, FIRST inspect fixture config for non-contract values (per `docs/procedures/test-fixture-contracts.md`) before assuming DI code is wrong (F.2).

Before applying a ledger entry's recommended fix, hand-trace + reproduce empirically. File a path-b decision record if root cause differs from the ledger's (F.3).

## Class-A / Class-B / Class-C routing — BINDING per owner 2026-08-24

Provisioning-surfaced bugs default-route to the project that OWNS the fix location:

- **Class-A** — fix in `src/server/services/Sprk.Provisioning.ControlPlane.*/**` → land in provisioning project.
- **Class-B** — fix in `src/server/api/Sprk.Bff.Api/**` or `src/server/shared/Spaarke.*/**` → file in BFF-owning worktree (per project CLAUDE.md coordination table). SESSION 7 amendment: if target BFF worktree is CLOSED and row is E2E-blocking, absorb into current provisioning project as a task-204 sub-phase.
- **Class-C** — touches both surfaces → document split in punch list; coordinate merge across worktrees.

Every Class-B routing MUST include an accompanying ArchTest (or equivalent forcing function) that prevents the class-of-bug at build time. Fix without prevention = fix that will recur.

Full mechanic: `.claude/patterns/provisioning/bff-vs-provisioning-boundary.md`.

## Run-context contract — every handler input has ONE producer (BINDING, task 245a / G25)

- `run.Parameters.NonSecret` holds **intake values only** — the closed set in `Models/IntakeParameterCatalog.cs`; `POST /api/runs` rejects any other key. **NEVER read a value another handler produces from `NonSecret`** — nothing writes it there.
- An intake value a handler has rules for is validated at `POST /api/runs` with **the same rules** — shared code where the rule is non-trivial (T245c: `UserProvisioningIntake` is called by both H11 and the endpoint), and the handler's own rejection code where it has one (H14a / H14b; an API-owned code otherwise, e.g. the mailbox shape). There is no add-parameter endpoint, so a value the handler would refuse must be refused before the run guard / registry / Cosmos / enqueue — not after H0–H10 have built the stamp. The handler keeps its guard as defence in depth.
- A value one handler produces for another goes in a typed `InterStepState` property carrying `[ProducedBy(HandlerIds.X)]` (or `[NoProducer(reason)]`), written only by X.
- A value L2 owns (its own principal, the SPE owning app per container type) is a validated Worker option (`AddOptions().Bind().Validate().ValidateOnStart()`), never a run parameter; an idempotency version is computed from the artifact the handler applies (`Handlers/ArtifactVersion.cs`), never supplied (T245b).
- Declare every handler input in `Reconciler/HandlerRunInputs.cs` (Intake / Output / Gap). A REQUIRED Output must come from a strict DAG ancestor of the reader (`DagAdvancer.HandlerDependencies`) — add the DAG edge, don't reorder reads.
- H4b `per_env_settings` sources are a closed set (`Handlers/BulkAppSettings/PerEnvSourceCatalog.cs`, mirrored in the generator); an unknown source fails the manifest read and `-Verify`.
- **`run.Parameters.Secrets` has no writer.** No handler may write it; only H4 may read it, and only for the manifest entries pinned as gaps.
- Every H4 manifest secret needs a source H4 can reach before H3 runs — a `from-bicep-output` label is only true if `customer.bicep`'s `kvSecretValues` writes it.
- `RunContextContractTests` enforces all of this with a Roslyn source scan (declared inputs = reads, both ways), a DAG check and the manifest check. A failure means the data flow is wrong — fix the flow, never add a Gap to pass. Handler unit tests seeding a value by hand prove nothing about who writes it (that is how ~20 inputs shipped with no producer).

Full mechanic: `.claude/patterns/provisioning/run-context-contract.md`; evidence: `projects/customer-provisioning-orchestration-r1/notes/run-context-dataflow-gap.md`.

## SPE owning app — MI-FIC, nothing stored (BINDING, task 248 / owner D16)

- L2 acts as an SPE container type's **owning app** only through `SpeConfidentialClientGraphFactory`, whose credential is `WorkerDataverseCredentialFactory.CreateManagedIdentityFederatedCredential`: the Worker UAMI's token for `api://AzureADTokenExchange` is the client assertion for the owning app's federated identity credential (ADR-028 A4's default). **NEVER** add a certificate, a client secret or a Key Vault read for an owning-app credential, and never add a client secret to an owning app — the SharePoint admin center asks for one when a container type is created; decline it.
- If Entra, Graph or SPE rejects the FIC-acquired token: **STOP and ask the owner.** A Key Vault certificate is A4's sanctioned alternative and is an owner decision, never an implementation choice; a secret is never the fallback.
- Worker configuration per container type is `{ContainerTypeId, OwnerAppId}` (`SpeContainerOptions:ContainerTypeOwners`). H0's `SpeOwnerCredential` check signs in as the owner and GETs the Graph v1.0 registration: `spe-owner-not-configured` / `spe-owner-token-failed` / `spe-container-type-not-registered` (all Resumable; no time gate). The registration `PUT` is create-or-replace — any later grant change keeps the owning-app grant.
- The BFF's `SpeAdminGraphService` still signs in to owning apps with E-1 Key Vault client secrets until T250 — until then, no secret-based `sprk_specontainertypeconfig` row for a Model 1 container type.
- Never delete `rg-spaarke-shared-prod` or the `Microsoft.Syntex/accounts` billing account in it (`dc4749c2-ca04-4b38-b6c2-e38dc3eec72b`): a standard container type's billing binding is permanent.

## Exchange mailbox access — RBAC for Applications, sidecar holds no credential (BINDING, task 251 / owner D24–D26)

- H14a grants the stamp's **managed identity only** the Exchange application roles for the Graph mailbox permissions (`IGraphAppRolesRegistry.GetExchangeScoped()` → `Application Mail.Read`, `Mail.ReadWrite`, `Mail.Send`, `MailboxSettings.Read`), **scoped to the customer's mail-enabled security group** (intake `exchangePolicyScopeGroupId`). Never ApplicationAccessPolicy (Microsoft: legacy; capped per tenant). Never grant the BFF app registration — RBAC for Applications *grants* access, so adding an identity widens what it reaches.
- Exchange adds role assignments to Entra app permissions. **NEVER** grant a mailbox role (`Mail.*`, `MailboxSettings.*`, `Calendars.*`, `Contacts.*`) to a stamp identity in Entra — H10 grants only `GetEntraGranted()`, and H13 T3 fails when a mailbox role is present in Entra. A new mailbox permission goes into `ExchangeScopedValues`, not into H10.
- The sidecar holds **no credential** and reads no Key Vault: the Worker signs in as `Spaarke Exchange Admin` through its UAMI's federated credential (`ExchangeAdminTokenSource` → `WorkerDataverseCredentialFactory.CreateManagedIdentityFederatedCredential`) and sends the Exchange Online token in `X-Exchange-Access-Token`; the sidecar runs `Connect-ExchangeOnline -AccessToken`. Never give that app a secret or a certificate. Never give it an Entra directory role (Exchange Administrator made its Exchange writes fail, 2026-10-04). Its Exchange permission is the narrowed role `Spaarke App RBAC Admin` plus `-Delegating` assignments for exactly the four application roles. Widening it is an owner decision.
- The sidecar connects with `-Organization` = the tenant's **initial domain** (`contoso.onmicrosoft.com`), which the Worker reads from Graph `GET /organization` and sends as `organization`. **NEVER** the tenant GUID: Exchange then connects and reads, but every write fails with "doesn't have write permission to target DC" (2026-10-04). The sidecar refuses a request without a domain-shaped `organization`.
- A group-scoped assignment reads back as `RecipientWriteScope = Group` with the group's **Name** in `CustomResourceScope` — there is no `RecipientGroupScope` property. Match scope on those two fields (`Test-AssignmentInScope`).
- A `sitecontainers` environment variable's `value` is the **name of a Worker app setting**, never a literal (Microsoft's contract — a literal arrives empty; G30). The sidecar binds its port even when a setting is missing, so it can never hold the Worker at 503.
- One-time per tenant: deployment guide §4.2.1 / prerequisite `PRQ-E-15`.

## Stamp Redis — Azure Managed Redis, Microsoft Entra only (BINDING, task 242 / owner D12–D13)

- **MUST** deploy every stamp's Redis from `modules/redis.bicep`: `Microsoft.Cache/redisEnterprise@2025-07-01`, `Balanced_B0`, high availability on (D12), database `default` with `accessKeysAuthentication: Disabled`, OSSCluster, AllKeysLRU, port 10000, and a `databases/accessPolicyAssignments` entry for the **stamp UAMI only**.
- **MUST NOT** add `listKeys()`, a Redis key/connection-string output, a `Redis-ConnectionString` catalog secret, or a `Redis__ConnectionString` / `ConnectionStrings__Redis` setting for a stamp. The endpoint is the plain setting `Redis__Endpoint` (`host:10000`) — set by `customer.bicep` and by H4b (`from-h2a-output:redis_endpoint`).
- The BFF (`CacheModule`) and the L2 Worker (`DispatchModule`) authenticate with their user-assigned identity (`ManagedIdentity__ClientId`) over RESP3 whenever `Redis__Endpoint` is set, and **refuse to start** on a connection string without it outside Development/Testing — so a deployed BFF/Worker MUST carry `Redis__Endpoint` before it runs a T242+ build (per-env cut-over: task 242b, endpoint added first, connection string removed only after master carries T242).
- H1 registers `Microsoft.Cache` by default. Managed Redis has no scale-down and HA is fixed at create — size up only on a measured memory metric.

## SPE: container type per model, container per customer — app-only isolation is in code (BINDING, owner D28 / T227b)

- One container type per model (`Spaarke Model 1`), one container per customer (per Dataverse environment). H8 grants each stamp's UAMI application `full` and its BFF app registration delegated `full` on the registration, as the owning app.
- An app-only token reaches **every** container of the type (Microsoft, no per-container app scoping). So the BFF **MUST** target only its own stamp's container(s) on app-only SPE calls — enforced by code and tests (T227d). **MUST NOT** add an app-only SPE call whose container or drive id comes from a caller-supplied value without that check.
- **How (T227d)**: app-only SPE clients come only from `SpeContainerOwnershipGuard` (`ForOwnedContainerAsync(id)`; `ForTypeWideOperation()` only in the SPE facade, whose list/search results are filtered with `IsOwnedAsync` and whose creates are marked with `MarkOwnedAsync`). Own = an id in `EmailProcessing__DefaultContainerId` / `Communication__ArchiveContainerId` / `SharePointEmbedded__OwnedContainerIds` (operator-only, pre-marker environments) **or** custom property `spaarkeCustomerId` = `Customer__Id`. **MUST NOT** call `IGraphClientFactory.ForApp()` for SPE work anywhere else — `tests/Spaarke.ArchTests/TenantIsolation/SpeAppOnlyContainerGuardTests.cs` fails the build. Refusal = 404 `spe_container_not_owned` (indistinguishable from a missing container). SPE Admin on a stamp is confined the same way (owner D29): container lists/searches are filtered; Container-TYPE operations (settings, permissions, create a type) touch no customer's container data and are not filtered: Graph refuses them app-only, because a stamp's identity holds only `FileStorageContainer.Selected` (no `FileStorageContainerType.*`). The marker stops a misrouted/forged id, not another stamp's code.
- Do **not** propose a container type per customer as the fix: the owner rejected it (25-per-tenant cap, 5 used, standard types undeletable).

## Stamp resources are keyless (BINDING, task 244 / owner D13)

- **MUST** keep local/key auth disabled on the stamp's AI Search, Azure OpenAI, Document Intelligence, Content Safety (T246), Service Bus, Cosmos DB and SignalR (`disableLocalAuth: true`; AI Search with no `authOptions`), Storage (`allowSharedKeyAccess: false` — the module default since T244) and Redis (rule above). A new keyed resource ships the same way (Content Safety did, T246: its endpoint is a plain app setting, `AiSafety__ContentSafety__Endpoint`, and the BFF refuses to start outside Development/Testing without it — never a fallback to a shared or dev account). Documented exclusions (in the test file): App Insights ingestion, and ACS (setting unverified on its pinned API).
- **MUST NOT** add `listKeys()` / `listAdminKeys()` / `listQueryKeys()`, a SAS authorization rule, an `AccountKey=` connection string, or a key / connection-string output to any stamp module or to `customer.bicep`. A caller that needs data-plane access gets a **role** (BFF UAMI: `bff-runtime-rbac.bicep` + module grants; L2 UAMI: `customer-l2-bff-rbac.bicep` — Website Contributor, Search Service Contributor, Search Index Data Reader — L2 writes no documents).
- Event Grid dead-letters to stamp Storage with `deadLetterWithResourceIdentity` (system-topic identity, Storage Blob Data Contributor on the container) — plain `deadLetterDestination` has no identity.
- Forcing function: `tests/Spaarke.ArchTests/CustomerStampKeylessTemplateTests.cs` reads the compiled `customer.json` — recompile it (`az bicep build --file customer.bicep --outfile customer.json`) after any module change. T230 makes one real managed-identity call per service at runtime (and measures ADR-028 E-2 on the stamp's `kind: OpenAI` account).

## Handler idempotency + drift-detection

- Every handler MUST be idempotent. Second run of the same handler against the same customer resources produces the same end state (assuming no external drift).
- Drift-detection handlers (H10 for setup registry) MUST audit-log the drift + rotate/repair automatically OR escalate per §6.5 (if drift indicates an ADR conflict). (H4-shared, the former drift handler for `from-shared-service` secrets, was retired by T226 on 2026-09-30 — customer stamps never read a shared service's credential.)
- Idempotency is verified in each handler's own test class (`src/server/services/Sprk.Provisioning.ControlPlane.Tests/Handlers/*Tests.cs` — re-run / already-completed cases). Missing coverage → PR blocker. (There is no shared `HandlerIdempotencyTests` class; an earlier version of this line named one.)

## Progressive fail-fast recovery — BFF startup completeness

- New BFF `AddOptions<T>().ValidateOnStart()` module → MUST add corresponding entry to `per_env_settings` list in `scripts/canonical-secret-catalog/manifest.yaml`.
- Deploy discipline: H4b bulk-set applies ALL settings in ONE batch → ONE App Service restart cycle. NO manual `az webapp config appsettings set` single-setting fixes in production.
- Nightly `IOptions-inventory-drift` ArchTest (planned task 203-followup) catches drift between BFF DI + manifest.

Full mechanic: `.claude/patterns/provisioning/progressive-fail-fast-recovery.md`.

## Reserved-suffix registry for global-namespace resources

Global-namespace resources (Service Bus, Storage, Cognitive Services, ACR, Front Door) MUST run `checkNameAvailability` preflight at Step 2.5 (per `.claude/patterns/provisioning/resource-name-availability-precheck.md`) + consult `scripts/provisioning-prereqs/reserved-suffixes.yaml` (task 203-followup authors).

- Service Bus reserves `-sb` suffix (F10 discovery, 2026-08-22).
- Adding a new global-namespace resource type → add its `checkNameAvailability` API to preflight + add any known reserved suffixes to the registry.

## Prerequisites — `docs/guides/PROVISIONING-PREREQUISITES.md`

Every provisioning task MUST honor the prereq registry. `/provision-environment` Step 0.5 verifies via `scripts/provisioning-prereqs/prereqs.yaml`. Adding a new manual prereq → add to both files with `scope`, `owner`, `check_recipe`, `consequence_of_absence`, `remediation` (the fields `validate.ps1` checks); state in `name` / `remediation` when it applies to one tenancy model only (there is no `tenancyModel` field — corrected 2026-10-01, T245c).

Task 202 established the registry with 27 prereqs across 4 scopes (once_per_tenant, once_per_subscription, once_per_env, once_per_customer). Do NOT bypass Step 0.5.

## Auth v2 (ADR-028) — 21 MUSTs

Provisioning tasks touching auth (H3, H4, H10, `/provision-environment` skill Step 0-6) MUST honor the ADR-028 21 MUSTs. Key ones:

- UAMI-outbound preference (not System-Assigned) for shared platform → source services.
- `keyVaultReferenceIdentity` on App Service MUST be set to the UAMI resource ID when `identity.type='UserAssigned'`.
- Client-cred rotation cadence: **retired for the BFF identity** (secret-free per ADR-028 A4 + E-3 CLOSED 2026-08-24; §6.5 resolution 2026-08-25 — no client secret to rotate on that identity). **Retained for E-1** (per-customer SpeAdmin container-type secrets): no more than once per 90 days SCHEDULED (drift-recovery is a different failure mode, escalate per §6.5).
- Operator's own AAD identity per NFR-11 (never a service principal).

Full pattern: `.claude/patterns/provisioning/keyvault-reference-identity-invariant.md` (T1) + `.claude/patterns/provisioning/operator-rbac-bootstrap.md` (F15/F18).

## Sub-Agent Write Boundary — BINDING per root CLAUDE.md §3

Tasks touching `.claude/**` (skills, patterns, constraints, catalogs, agents, settings) MUST run in the MAIN SESSION. Sub-agents cannot Write/Edit these paths and will fail with "Edit denied". `task-create` auto-marks these tasks `parallel-safe: false`.

If a parallel agent is accidentally dispatched to a `.claude/**` task, it will fail cleanly — main session picks up sequentially. Do NOT attempt workarounds.

## Test update obligation — analogous to bff-extensions.md § F

PRs modifying `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/**` MUST add/update tests in `src/server/services/Sprk.Provisioning.ControlPlane.Tests/Handlers/**`. Handler-registration-completeness ArchTest is the forcing function; skipping the actual behavior tests is a Critical finding.

PRs modifying `scripts/canonical-secret-catalog/manifest.yaml` MUST prove generator determinism via `Invoke-CatalogGenerator.ps1 -Verify` → exit 0.

## E2E acceptance ceremony — task 186 gate

Task 186 (E2E live-fire against sub `cd95fcec-6b89-49ea-8339-c2b579b12587`) is BLOCKED by the pre-check trigger: `pre-live-fire-punch-list-gate` per task 202 (BINDING per owner 2026-08-24). Cannot fire until Class-A + Class-B `blocks_e2e=yes` rows are all `applied|already-applied`. Verify via `notes/task-202-punch-list.md` status column before invoking 186.

## Cross-refs

- Sibling constraints: `.claude/constraints/{api,auth,data,jobs,testing,config,bff-extensions,azure-deployment}.md`
- Related patterns: `.claude/patterns/provisioning/*.md` (9 files)
- Related ADRs: ADR-004 (job contract), ADR-013 (AI architecture), ADR-028 (Spaarke auth v2), ADR-032 (Null-Object kill-switch), ADR-036 (background job infrastructure), ADR-044 (Dataverse GUID canonicalization)
- Related project docs: `projects/customer-provisioning-orchestration-r1/{spec.md,design.md,CLAUDE.md,notes/task-202-punch-list.md}`
- Related prereq: `docs/guides/PROVISIONING-PREREQUISITES.md` + `scripts/provisioning-prereqs/prereqs.yaml`
