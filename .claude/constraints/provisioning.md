# Provisioning Constraints

> **Last Reviewed**: 2026-10-08 (T235 — keyless section cites ADR-028 A6); 2026-10-07 (T232 — Model 1 guest access rule added; 2026-10-06 T244 keyless stamp resources; T246 Content Safety)
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
- **I3 — Cosmos partition-key predicate** (FR-30): every Cosmos read/write MUST include the container's partition-key predicate — `/tenantId` on the stamp's tenant-scoped containers (or the key `cosmos-db.bicep` declares: `/partitionKey`, `/subjectId`), `/customerId` on L2's ProvisioningRun container. Cross-partition queries are audit-flagged and treated as bugs. H13's I3 probe checks the stamp's account holds exactly the template's containers with their declared keys (T230a).
- **I4 — SPE container ID resolution** (FR-31): SPE container IDs come from the record being served or the stamp's own settings, and every app-only SPE call passes `SpeContainerOwnershipGuard` — the BFF's one definition of this stamp's containers (T227d; the unused `ITenantContainerResolver` was retired by T227f). NEVER hardcode a container ID.
- **I5 — Graph token per-tenant** (FR-32): every Graph token acquisition uses tenant `{tenantId}` — never the operator's home tenant, never a shared "app-tenant" for cross-tenant ops. `ITokenAcquisition.GetAccessTokenForAppAsync(tenant)` is the discipline.

Violations of I1-I5 surface via ArchTests + are Critical findings in code-review Step 6. H13 samples I2–I5 on the deployed stamp; I1 (no hardcoded tenant in scripts) is a build-time property — the I1 ArchTest is its one enforcement point, since the L2 Worker ships and runs no script (T230a / T253).

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
**Since task 252 (2026-10-09) the L2 control plane is secret-free by default too**: `platform-controlplane.bicep`
`requireSecretFreeIdentity` defaults to `true` (Worker FR-39 chain `[ManagedIdentityFederated]`, no Key Vault reference to
`BFF-API-ClientSecret`; `false` is a prong-3 opt-in only), and `Seed-PlatformKeyVault.ps1` never seeds
`BFF-API-ClientSecret` or `Dataverse-ClientSecret` (no sentinel, no real value). Copies already in a platform vault stay
untouched — never delete them.

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

- How: `.claude/rules/bff-hygiene.md` item 4 — delta against a fresh master build (both from short-path worktrees, zipped with the same tool, `Compress-Archive`), never a recorded baseline or an uncompressed `du -sh`. Procedure: `.claude/constraints/azure-deployment.md` "BFF Publish-Size Per-Task Verification Rule".
- Thresholds: `≥+5 MB single-task delta` → explicit justification required; `≥55 MB cumulative` → architecture review; `≥60 MB` → HARD STOP.

Report absolute + delta in task notes / PR description. See `.claude/constraints/azure-deployment.md` "BFF Publish-Size Per-Task Verification Rule" for full mechanic.

## Handler registration completeness — BINDING per ADR-032 + `.claude/patterns/provisioning/handler-registration-completeness.md`

- Every new `IProvisioningHandler` touches five places: `HandlerIds.cs` (const + `Dispatchable`), the handler class, its concrete + dependency registrations, the keyed forwarder in `HandlerDispatchRegistrationModule.cs`, and `DagAdvancer.HandlerDependencies`. Missing the forwarder → the Worker dead-letters the message as `NoHandler`; a missing dependency → `HandlerResolutionFailed`; a missing DAG entry → the reconciler never dispatches it.
- `HandlerRegistrationCompletenessTests` and the HANDLER-12 parity test in `DagAdvancerTests` MUST pass on every PR (currently 21 dispatchable ids — T226 retired H4-shared on 2026-09-30; T256 added H7b on 2026-10-08; adding a handler → 22).
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
- H4b `per_env_settings` sources are a closed set (`Handlers/BulkAppSettings/PerEnvSourceCatalog.cs`, mirrored in the generator); an unknown source fails the manifest read and `-Verify`. A LIST source (T255) feeds only an `indexed: true` entry, which is always required and owned whole (stale indices removed).
- **`run.Parameters.Secrets` has no writer.** No handler may write it; only H4 may read it, and only for the manifest entries pinned as gaps.
- Every H4 manifest secret needs a source H4 can reach before H3 runs — a `from-bicep-output` label is only true if `customer.bicep`'s `kvSecretValues` writes it.
- `RunContextContractTests` enforces all of this with a Roslyn source scan (declared inputs = reads, both ways), a DAG check and the manifest check. A failure means the data flow is wrong — fix the flow, never add a Gap to pass. Handler unit tests seeding a value by hand prove nothing about who writes it (that is how ~20 inputs shipped with no producer).

Full mechanic: `.claude/patterns/provisioning/run-context-contract.md`; evidence: `projects/customer-provisioning-orchestration-r1/notes/run-context-dataflow-gap.md`.

## The L2 Worker runs no shell tool (BINDING, task 253 / G38)

- The L2 Worker host is App Service `DOTNETCORE|10.0`: no pwsh, pac or az, and its publish carries no `scripts/` or `infrastructure/` folder. **MUST NOT** start a process (`Process.Start`, `ProcessStartInfo`) in any `Sprk.Provisioning.ControlPlane.*` assembly — `tests/Spaarke.ArchTests/ControlPlaneNoProcessStartGuardTests.cs` fails the build. Port to an SDK / REST client (ARM SDK, Dataverse Web API, Graph, Key Vault SDK); a PowerShell-only Exchange operation belongs in the H14a sidecar.
- **MUST NOT** read a repo file at run time (`AppContext.BaseDirectory/scripts/...`): ship it as an `<EmbeddedResource>` (as the secret-catalog manifest, the seed manifest and the index schemas are).
- H4b writes the BFF app settings through `IAppServiceSettingsWriter` (ARM SDK, production + `staging`, merge never replace). They equal `Configure-AppServiceSettings.generated.ps1`'s (parity test in `H4bBulkAppSettingsHandlerTests`) — regenerate the script with every manifest change.
- H6 installs no Power Platform application: SpaarkeMaster may depend only on platform applications (`SpaarkeMasterApplicationDependencyTests`). Remove a new dependency at source; never add an installer.

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

## Subscription + Dataverse environment are operator prerequisites (BINDING — owner D4 / Q1; T228)

- The operator creates the customer's **own Azure subscription** and **Dataverse environment** before the run; L2 creates **neither** and **MUST NOT default** either — no shared subscription, no `az account show`, for any tenancy model (ADR-027). POST /api/runs requires `subscriptionId` (GUID), `containerTypeId` (GUID, G19) and `dataverseEnvUrl` for every model.
- **The environment must be named for the customer**: domain `spaarke-{customerId}` or `spaarke-{customerId}-{environmentName}` — `DataverseEnvironmentUrlRule`, applied at POST /api/runs and again by H5. It is the guard against adopting another customer's environment (all Model 1 environments share Spaarke's tenant). Reject, never repair.
- **H1 refuses a subscription that is not the customer's alone**: outside the run's tenant, or holding another `rg-spaarke-{otherId}-*` group (`SubscriptionDedication`) — before it writes anything.
- **H5 adopts, never creates**: URL rule → `GET /WhoAmI` as the L2 Worker identity (401/403 → Resumable `worker-not-app-user`; the operator adds that identity as a System Administrator application user, PRQ-C-09).
- **DAG**: H10 ← H3, H5 and H6 ← H10 — H6/H7/H7b sign in as the BFF app registration, an application user only once H10 has registered it. H11 ← H10, H7. H7b ← H6; H9 ← H3, H4b, H6, H7b; H13 ← H14, H7b (T256).
- **The L2 identity holds Owner on each customer subscription** (owner decision 2026-10-06 — customer.bicep writes role assignments): granted by the operator with `infrastructure/bicep/modules/controlplane-subscription-rbac.bicep` at that subscription (PRQ-S-04). It is never deployed on the platform subscription, and L2 never grants itself access to a subscription.

## Model 1 users — B2B guests, environment security group, pay-as-you-go (BINDING — owner D2 + 2026-10-07; T232)

- A Model 1 run takes only `B2BGuest` (`UserProvisioningIntake`). Every B2BGuest run names the environment's security
  group `sprk-{customerId}-users` (`environmentSecurityGroupId`, PRQ-C-10). **The group is the isolation boundary between
  Model 1 environments** (all in Spaarke's tenant): H11 refuses a group with another name or not security-enabled before
  inviting anyone, and adds guests to that group only. That the group is the one **set on the environment** is visible
  only to a Power Platform admin — the skill checks it (Step 1e-bis); L2 checks the name. **MUST NOT** add a guest to any
  other group or make it a user of another environment.
- Guests get **no licence**: Spaarke pays pay-as-you-go on the customer's stamp subscription (PRQ-C-11 — an operator
  check; L2 cannot see billing). **MUST NOT** add per-user licence assignment for B2BGuest. NativeAccount refuses a run
  with no SKU configured.
- A guest's Dataverse user is made through the `azureactivedirectoryobjectid` alternate key (on-demand add of a group
  member) as the L2 Worker identity. **MUST NOT** grant L2 a Power Platform admin role or register it as a management
  app for this (force sync) — ask the owner first if a live run shows the alternate key is not enough.
  Then (T259) H11 moves the user from the root into the customer's business unit (`PATCH systemusers({id})` `businessunitid@odata.bind`, `If-Match: *`, read back) BEFORE any role, and assigns only that unit's role copies.
- An existing guest is reused — **never re-invite** (it re-sends the email). `restrictguestuseraccess` must be off
  (PRQ-C-12); H11 checks it before inviting. Decisions + known limits: `projects/customer-provisioning-orchestration-r1/notes/t232-guest-access-decisions.md`.

## Stamp BFF clients — CORS + app-registration client access (BINDING, task 240a)

- **CORS:** a stamp BFF does not start outside Development with an empty `Cors:AllowedOrigins`. The manifest's literal
  `Cors__AllowedOrigins__0/1` are the shared client sites `https://addins.spaarke.com` and `https://external.spaarke.com`
  (exact https origins, same on every stamp). Never add a customer's Dataverse origin (`CorsModule` admits
  `*.dynamics.com` / `*.powerapps.com` by suffix), never a wildcard, never a `*.azurestaticapps.net` host.
- **H3 sets exactly** the customer BFF registration's `spa.redirectUris` (the customer's Dataverse origin from intake
  `dataverseEnvUrl`, via `DataverseEnvironmentUrlRule`) and `api.preAuthorizedApplications` (Worker setting
  `EntraAppRegOptions__PreAuthorizedClientAppIds__N` on `user_impersonation`). It PATCHes only that registration — never a
  shared client app. A shared client's own sign-in redirects live on that client app, once, never per customer.
- **`EntraAppRegOptions__SpaarkeTenantId`** (Worker Bicep, the deployment's tenant) is the FIC issuer for Model 1; without
  it every Model 1 run fails at H3.
- **H3 keeps two federated credentials on every Spaarke-tenant customer BFF registration (ISS-015):** `spaarke-uami-trust`
  (subject = the stamp BFF UAMI) and `spaarke-l2-worker` (subject = `ControlPlaneIdentity__PrincipalObjectId`, the L2 Worker
  UAMI's **principalId**, issuer Spaarke's tenant, audience `api://AzureADTokenExchange`). H6/H7/H7b sign in as the
  registration through the second (D-13, secret-free). Never remove it, never replace it with a secret, never set its
  subject to the UAMI's clientId (AADSTS700213). A blank/invalid Worker principal refuses H3 before any write
  (`appreg-worker-fic-identity-missing`). `customer-owned-model2`: stamp credential only (MI-FIC cannot cross tenants).
- **H3 adopts an existing `spaarke-bff-api-{customerId}` only if nobody but the control plane can act as it** (one match,
  no secret or certificate, no FIC other than `spaarke-uami-trust` / `spaarke-l2-worker`, no owner but the control plane); otherwise `appreg-adoption-refused`, nothing
  written. Never relax this to "adopt by name".
- **One environment per customer (D6).** The registration is per customer (D-13) while H3 sets its redirect, FIC and
  pre-authorizations per run, so a second environment for the same customer would overwrite the first's. Per-customer
  staging/dev needs a per-stamp registration first (`notes/defer-issues.md`).

## Customer workforce tenants + `acct` (BINDING, T255 / unified-access-control-r2 INCOMING-141)

- Intake `customerWorkforceTenantIds` (JSON array, **required for every model**) is the CUSTOMER's Entra tenant(s) — Model 1: the customer's HOME tenant, never Spaarke's and never the run's `tenantId`. ONE rule, `Core/Models/CustomerWorkforceTenantsRule`, at POST /api/runs, in H4b and in H13; it refuses the CIAM tenant and Spaarke's tenant from the L2-owned `ReservedTenantsOptions` (`ReservedTenants__SpaarkeTenantId` / `__CiamTenantIds__N`, both hosts, ValidateOnStart). **NEVER** fall back to `AzureAd__TenantId` or `TenantRouting:Tenants[]`.
- H4b writes `WorkforceIdentity__CustomerTenantIds__N` on both slots from the manifest's `indexed: true` entry and REMOVES every other `WorkforceIdentity__CustomerTenantIds__*` there; H13 T7 fails a stamp whose slots differ from the run's list. Change a stamp's list with a new run, never by hand.
- H3 puts the `acct` optional claim on every per-customer BFF registration's access tokens (create + reconcile, read back).
- Provisioning does **not** write `IdentityLink__Reconciliation__WritesEnabled` (H4b merges — a written `false` would undo an operator's `true`).
- The contact identity-binding schema ships in SpaarkeMaster; OOB-table alternate keys are in the package rule (T255). Its field-security profile memberships are per environment — H7b maintains them (see Secure Record setup — H7b).

## Dataverse package — one SpaarkeMaster, managed by default (BINDING, ADR-027 §3–§4 amended 2026-10-07; T218)

- **One solution, `SpaarkeMaster`**, is the whole Dataverse package (`SolutionImport/SpaarkePackage.cs`); no per-feature
  solution list exists. Its scope is a rule (every `sprk` component in spaarkedev1 + OOB-table columns − committed
  exclusions) — runbook `docs/procedures/SPAARKE-SOLUTION-RELEASE-PROCESS.md`.
- **Managed by default; unmanaged only on explicit instruction** — intake `solutionPackageType` (`managed` | `unmanaged`,
  exact case; absent → `managed` stored at CreateRun). Never add an environment- or server-level default that flips it.
- H6 **refuses** (Resumable, nothing imported) a managed↔unmanaged switch (`package-type-mismatch`) and a downgrade
  (`downgrade-refused`); a failed read of the installed solution is a failure, never "assume a fresh install". It never
  falls back to the other package type's blob.
- The manifest H6 reads: `{"solutions":{"SpaarkeMaster":{"version","managedBlobName","unmanagedBlobName"}}}`.
- H9 waits for H6 (the BFF never starts against an older schema). H13 records `sprk_solutionversion` =
  `SpaarkeMaster {version} ({managed|unmanaged})` — the package type on the registry row.
- Environment-variable **values** never ship in the package (H7 writes them).

## Secure Record setup — H7b (BINDING, T256 / unified-access-control-r2 INCOMING-145)

- H7b creates per environment what no package can carry: the `Secure Record` unit (direct child of the root, no users), the memberless Owner team `Secure Record Owners`, and the `Secure Record Owner` role IN that unit with exactly `config/secure-record-owner-role.json` (Read at Basic; linked into L2, never copied). Never put the role in SpaarkeMaster or the root unit (`secure_setup.role_is_replica`).
- Read-then-write: every refusal precedes the first write; the §5.4 strip follows every grant; a second run writes nothing. QuarantineRequired only for owner decisions (users in the unit, team members, wrong parent, root default team at Deep/Global, a stray field writer or sprk_issecure writer).
- Field-security profiles, the `sprk_issecure` lock, the contact identity-binding lock (`contact.sprk_externalobjectid`, `systemuser.sprk_primarycontact`) and `sprk_noaccessentry` ship in SpaarkeMaster; H7b verifies them and adds only memberships: every business unit's default team → `Spaarke BFF-Managed Field Readers` and `Spaarke Identity Link Readers`; H10's two BFF application users → `Spaarke BFF-Managed Field Writers` and `Spaarke Identity Link Writers`, nobody else (any other member or team → QuarantineRequired `secure_setup.field_writer_has_other_member` / `secure_setup.identity_link_writer_has_other_member`; another profile — the reader profiles included — that may write a locked column → `…_lock_other_writer`). Never remove a member. H9 waits for H7b: no BFF reaches an environment without `sprk_noaccessentry`.
- Names: unit and role from the file, team = the BFF's compiled `SecureRecord:OwnerTeamName` default. A `SecureRecord__` manifest key needs ONE run parameter feeding H4b and H7b (`SecureRecordOwnerRoleSetParityTests` fails until then).
- Dry run: intake `secureRecordSetupDryRun` = `true` | `false` (exact); it stops the run at H7b with `secure_setup.dry_run` and the plan in gate `h7b-secure-setup-plan`.
- Customer business unit (T259 / ISS-010, owner 2026-10-09 — INCOMING-145 §6 T1/T3/T5): **no user of any kind is ever placed in the Secure Record unit.** H10 finds or creates the customer's own unit (intake `displayName`, `CustomerBusinessUnitIntake`) as a DIRECT child of the root and creates both BFF application users IN it (`InterStepState.CustomerBusinessUnitId`, `[ProducedBy(H10)]`); H7 links it to H8's container beside the root; H11 places every guest there (moved from the root before any role; roles = the unit's copies). **MUST NOT** move an application user or re-parent a unit (QuarantineRequired: `h10-customer-bu-wrong-parent`, `h10-app-user-in-foreign-business-unit`, `userprov-guest-in-foreign-business-unit`, `userprov-guest-holds-role-outside-customer-unit`); H7b refuses `secure_setup.customer_bu_missing` / `customer_bu_wrong_parent` / `app_user_outside_customer_bu`. Never place a user in the root: Deep there reaches Secure Record.
- Open (ISS-014, before T186 Ready): H13 cannot run the BFF's isolation census (admin route, `SystemAdmin` policy) — run `secure-record-isolation-census` by hand after H11 and treat any human reaching the Secure Record unit as a stop.

## Cost model — one dedicated stamp per run, both models (BINDING, task 229)

- Every run deploys ONE dedicated stamp (`customer.bicep`) into the customer's own subscription — Model 1 paid by Spaarke, Model 2 by the customer. No cost concept may assume a shared platform: no `shared-trial` tier, no "marginal" or "shared floor" envelope, no per-model cost branch.
- `tier` (smb | enterprise | dedicated = the keys of `H0Options.DefaultCeilingsUsd`) and `estimatedMonthlyUsd` are **required for every model**, validated by ONE rule set (`Handlers/Preflight/CostEnvelopeIntake`) at POST /api/runs and again by H0; the intake schema's `tier` enum equals that table (parity test).
- H0 refuses an estimate above the tier ceiling (`quota-cost-overrun`, Resumable) with **no waiver** — `costEnvelopePolicy` / `warnAndProceed` are retired and refused as unknown keys. `H0Options.CostEnvelopeAbortsPreflight=false` is the only switch.
- H13 compares the subscription's cost with ONE `H13AcceptanceOptions.DedicatedStampEnvelopeUsd` ($400; $337.04 fixed at 2026-10-06 list prices). Re-derive it (and the guide §3.2 breakdown) whenever `customer.bicep`'s SKUs change.

## SPE: container type per model, container per customer — app-only isolation is in code (BINDING, owner D28 / T227b)

- One container type per model (`Spaarke Model 1`), one ROOT container per customer (per Dataverse environment) — H8's; secure-record containers (one per secure project / matter / work assignment — `ProvisionProjectEndpoint`) and any further business-unit container (SPE admin plane) are created by the BFF at runtime, bound to their business unit and marked — never by provisioning (T227g). H8 grants each stamp's UAMI application `full` and its BFF app registration delegated `full` on the registration, as the owning app.
- An app-only token reaches **every** container of the type (Microsoft, no per-container app scoping). So the BFF **MUST** target only its own stamp's container(s) on app-only SPE calls — enforced by code and tests (T227d). **MUST NOT** add an app-only SPE call whose container or drive id comes from a caller-supplied value without that check.
- **How (T227d)**: app-only SPE clients come only from `SpeContainerOwnershipGuard` (`ForOwnedContainerAsync(id)`; `ForTypeWideOperation()` only in the SPE facade, whose list/search results are filtered with `IsOwnedAsync` and whose creates are marked with `MarkOwnedAsync`). Own = an id in `EmailProcessing__DefaultContainerId` / `Communication__ArchiveContainerId` / `SharePointEmbedded__OwnedContainerIds` (operator-only, pre-marker environments) **or** custom property `spaarkeCustomerId` = `Customer__Id`. **MUST NOT** call `IGraphClientFactory.ForApp()` for SPE work anywhere else — `tests/Spaarke.ArchTests/TenantIsolation/SpeAppOnlyContainerGuardTests.cs` fails the build. Refusal = 404 `spe_container_not_owned` (indistinguishable from a missing container). SPE Admin on a stamp is confined the same way (owner D29): container lists/searches are filtered; Container-TYPE operations (settings, permissions, create a type) touch no customer's container data and are not filtered: Graph refuses them app-only, because a stamp's identity holds only `FileStorageContainer.Selected` (no `FileStorageContainerType.*`). The marker stops a misrouted/forged id, not another stamp's code.
- **One root container per customer, ever (T227e, on unified-access-control-r2 task 165)**: a re-entry of the run resumes with the container in its typed creation record (`InterStepState.SpeContainerCreation`); a LATER run reuses the container the environment records (`sprk_SharePointEmbeddedContainerId`, written by H7 on the first run) as `adopted`. The record and the environment naming different containers → Resumable `spe-duplicate-customer-containers` naming both; an unreadable value → Resumable, nothing created. **Never pick one, never create without an answer, never list the shared type to adopt a container.** A reused container is **never removed** by a failed bind (`SpeContainerBindRequest.RemoveIfNotBound = false`; adoption is kept on the record — `AdoptedRootContainerId`), and is **read before anything is written to it**: missing, another container type, another customer's marker or another business unit's stamp → Resumable, nothing written. All checks run before the container-type grants. **Operator rule:** resume a run that stopped after H8 and before H7 — never start a new one (it would create a second container). H8 writes the `spaarkeCustomerId` marker after the bind; its name is ONE source-linked constant (`src/server/shared/Contracts/SpeContainerCustomerMarker.cs`, pinned by ArchTest `SpeContainerMarkerParityTests`).
- **The root business unit names the root container (T227g)**: H7 sets `businessunit.sprk_containerid` on the environment's root unit to H8's container — unified-access-control-r2 task 076 resolves a record that is not secure to its owning business unit's `sprk_containerid`, so without it a fresh stamp stores no non-secure file. Equal → no write; empty → PATCH + read back; **another container → Resumable `root-business-unit-container-conflict` naming both, never overwritten** (it would move where the customer's files go). **Every container a script creates** is stamped AND marked (`Invoke-SpeContainerBindOrRemove -CustomerId <the BFF's Customer__Id>`; ArchTest `SpeContainerMarkerParityTests`). The `Secure Record` business unit has **no** container (each secure record has its own).
- Do **not** propose a container type per customer as the fix: the owner rejected it (25-per-tenant cap, 5 used, standard types undeletable).

## Stamp resources are keyless (BINDING, task 244 / owner D13 — ADR-028 Amendment A6, 2026-10-08)

- **MUST** keep local/key auth disabled on the stamp's AI Search, Azure OpenAI, Document Intelligence, Content Safety (T246), Service Bus, Cosmos DB and SignalR (`disableLocalAuth: true`; AI Search with no `authOptions`), Storage (`allowSharedKeyAccess: false` — the module default since T244) and Redis (rule above). A new keyed resource ships the same way (Content Safety did, T246: its endpoint is a plain app setting, `AiSafety__ContentSafety__Endpoint`, and the BFF refuses to start outside Development/Testing without it — never a fallback to a shared or dev account). Documented exclusions (in the test file): App Insights ingestion, and ACS (setting unverified on its pinned API).
- **MUST NOT** add `listKeys()` / `listAdminKeys()` / `listQueryKeys()`, a SAS authorization rule, an `AccountKey=` connection string, or a key / connection-string output to any stamp module or to `customer.bicep`. A caller that needs data-plane access gets a **role** (BFF UAMI: `bff-runtime-rbac.bicep` + module grants; L2 UAMI: `customer-l2-bff-rbac.bicep` — Website Contributor, Search Service Contributor, Search Index Data Reader — L2 writes no documents).
- Event Grid dead-letters to stamp Storage with `deadLetterWithResourceIdentity` (system-topic identity, Storage Blob Data Contributor on the container) — plain `deadLetterDestination` has no identity.
- Forcing function: `tests/Spaarke.ArchTests/CustomerStampKeylessTemplateTests.cs` reads the compiled `customer.json` — recompile it (`az bicep build --file customer.bicep --outfile customer.json`) after any module change.
- **Proved per run (T230b)**: H13 calls the stamp BFF's `POST /api/platform/keyless-proof` as the L2 Worker identity (token for `api://{BffAppRegId}`; H3 assigns that identity the application role `Provisioning.KeylessProof` — one source-linked contract, `src/server/shared/Contracts/KeylessProofContract.cs`). The BFF makes one read-only managed-identity call per stamp service; anything but `proved` fails H13 (`unreachable` is Resumable; `not-in-use` passes only for the services in `KeylessProofContract.Services.MayBeUnused` — Blob, while the session-file store is off by design) — **an auth failure is never a skip**. `ArmStampKeylessVerifier` checks key auth off on every keyed resource and no key setting on any slot (`StampKeySettingCatalog`). The `openai-chat` result is the ADR-028 E-2 measurement on the stamp's `kind: OpenAI` account.
- **MUST NOT** add a key credential (`AzureKeyCredential`, `ApiKeyCredential`, a key header, a key-bearing connection string) to server code without its entry in `tests/Spaarke.ArchTests/KeyCredentialCensusTests.cs` — and for a stamp resource, its setting in L2's `StampKeySettingCatalog` and in the BFF keyless-proof probe (the census asserts both). No key fallback is added for stamps.

## Handler idempotency + drift-detection

- Every handler MUST be idempotent. Second run of the same handler against the same customer resources produces the same end state (assuming no external drift).
- Drift-detection handlers (H10 for setup registry) MUST audit-log the drift + rotate/repair automatically OR escalate per §6.5 (if drift indicates an ADR conflict). (H4-shared, the former drift handler for `from-shared-service` secrets, was retired by T226 on 2026-09-30 — customer stamps never read a shared service's credential.)
- Idempotency is verified in each handler's own test class (`src/server/services/Sprk.Provisioning.ControlPlane.Tests/Handlers/*Tests.cs` — re-run / already-completed cases). Missing coverage → PR blocker. (There is no shared `HandlerIdempotencyTests` class; an earlier version of this line named one.)

## Progressive fail-fast recovery — BFF startup completeness

- New BFF `AddOptions<T>().ValidateOnStart()` module → MUST add corresponding entry to `per_env_settings` list in `scripts/canonical-secret-catalog/manifest.yaml`.
- Deploy discipline: H4b bulk-set applies ALL settings in ONE batch → ONE App Service restart cycle. NO manual `az webapp config appsettings set` single-setting fixes in production.
- `tests/Spaarke.ArchTests/IOptionsDriftTests.cs` (task 204e) fails the PR when a `ValidateOnStart` options type demands a key no stamp channel writes. Per-PR, not nightly. Checklist: `bff-extensions.md` §F.5.

Full mechanic: `.claude/patterns/provisioning/progressive-fail-fast-recovery.md`.
- An `Exempt` census row names its `Gate`; `IOptionsDriftTests` fails when a stamp channel writes that gate without the demanded key (T258). The BFF's H0.5 consent callback is such a gate (`Onboarding:Enabled`, off on every stamp).

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
