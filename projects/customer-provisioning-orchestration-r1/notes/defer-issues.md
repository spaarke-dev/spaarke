# Defer / Issue Tracking — customer-provisioning-orchestration-r1

> **Source of truth** for deferred work + newly-discovered issues in this project.
> Each entry has a paired GitHub Issue. See `/project-defer-issue-tracking` skill for the protocol.
>
> **Rollup view**: `gh issue list --label customer-provisioning-orchestration-r1` (visible to whole team via portfolio board)
> **CLAUDE.md §11 rule**: every entry MUST name a concrete behavior or contract that fails without it.

---

## Open (in priority order)

### ISS-001 — Hand-off owed by UAC-r2: how a new environment gets `sprk_noaccessentry`

| Field | Value |
|---|---|
| **Status** | Open — waiting on unified-access-control-r2 |
| **Urgency** | now (blocks T256 / T218 / T186) |
| **Filed** | 2026-10-07 |
| **Source** | 2026-10-06 conversion review; owner 2026-10-07 asked for an issue + message to UAC-r2 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1364 |

**Description**

An environment without `sprk_noaccessentry` denies every read, so a freshly provisioned environment fails closed and
T186 cannot pass. UAC-r2 documents the table only in its own notes; nothing tells provisioning which solution carries
it, when it must exist, what to seed, or how H13 verifies it.

**Entry-points**

- `projects/unified-access-control-r2/` notes `DEPLOY-CHECKLIST.md`, `batch4-integration-steps.md`; `docs/data-model/INDEX.md`
- This project: T256 (INCOMING-145), T218 (solution package), H13 checks

**Suggested fix**

UAC-r2 answers the four questions in the issue (packaging, ordering, verification, upgrade); provisioning turns the
answer into T256 handler work and T218 package content.

**Update 2026-10-08 (T256).** What provisioning needs is now answered by the repository, and built:
1. *Packaging*: the table ships in SpaarkeMaster 1.2.0.0 with every column the BFF reads (`src/dataverse/solutions/SpaarkeMaster/Entities/sprk_noaccessentry`).
2. *Ordering*: H7b probes `sprk_noaccessentries` with the BFF reader's own `$select` (`NoAccessListReader.RowSelect`,
   pinned by `SecureRecordOwnerRoleSetParityTests`) and refuses Resumable `secure_setup.noaccessentry_missing`; H9 waits
   for H7b, so no BFF is deployed to an environment without it. Nothing is seeded: an empty deny list denies nothing.
3. *Verification*: that probe, on every run.
4. *Upgrades*: SpaarkeMaster upgrades carry it.
Left open only for UAC-r2's confirmation on #1364 (and to hear if the schema changes — we re-export, never hand-edit).

### ISS-003 — A second environment for the same customer overwrites the first one's BFF app registration

| Field | Value |
|---|---|
| **Status** | Open — not needed while owner D6 holds (one environment per customer) |
| **Urgency** | later (before per-customer staging/dev) |
| **Filed** | 2026-10-07 |
| **Source** | T240a review, verifier pass 2 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1376 |

**Description**

The registration is per customer (D-13) but H3 sets its SPA redirect, FIC (`spaarke-uami-trust`) and pre-authorizations
per run, so a second environment for the same customer breaks the first's code-page sign-in and BFF credential.

**Suggested fix**

A per-stamp registration (`spaarke-bff-api-{customerId}-{env}`), or intake refusing a second environment until then.

### ISS-004 — Any Spaarke-tenant user can get any customer BFF token (no stamp-level token gate)

| Field | Value |
|---|---|
| **Status** | Open — investigate (known limit; no cross-customer data path found) |
| **Urgency** | before the first external customer |
| **Filed** | 2026-10-07 |
| **Source** | T240a review, verifier pass 2 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1377 |

**Description**

All Model 1 BFF registrations are in Spaarke's tenant; any member or guest can get a token for any customer's BFF. Record
access is still bounded by the stamp's Dataverse/SPE authorization, but record-free endpoints (AI chat) may run on that
customer's OpenAI quota.

**Suggested fix**

`appRoleAssignmentRequired` per BFF service principal with `sprk-{customerId}-users` assigned — after checking the Type-2
external workforce plane (UAC-r2 task 141) and the External Access SPA (T240d).

### ISS-002 — Demo self-registration SPE grant: marker keyed to the demo Dataverse; expiry leaves it behind (UAC-r2 code)

| Field | Value |
|---|---|
| **Status** | Open — owned by unified-access-control-r2 (task 171 code) |
| **Urgency** | next-round (latent: "Demo 1" has no `sprk_specontainerid` today) |
| **Filed** | 2026-10-07 |
| **Source** | Adversarial verifier finding F8 on the 2026-10-07 master merge (`f442915e6`) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1363 |

**Description**

Step 8 records a standing-writer marker keyed by a systemuserid from the demo environment's Dataverse, which the
membership sync cannot resolve. Demo expiry deletes the grant directly (first page only, no `Prefer` header) and
leaves the marker. When the BFF's Dataverse and the demo's are the same, the standing pass sees a stale marker and can
re-grant writer to an expired demo user whose systemuser is still enabled.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Registration/DemoProvisioningService.cs:146`, `:169-185`, `:257-261`
- `src/server/api/Sprk.Bff.Api/Services/Registration/DemoExpirationService.cs:226`, `:308-350`
- `src/server/api/Sprk.Bff.Api/Services/Access/SpeContainerMembershipSync.cs:95-210`

**Suggested fix**

UAC-r2 decides the marker identity (or a demo prefix the standing pass ignores) and expires via
`RemoveMarkedGrantAsync`. Provisioning side (ours): configuring a demo container also needs it owned by the hosting
BFF (`SharePointEmbedded__OwnedContainerIds` or the `spaarkeCustomerId` marker), or Step 8 is skipped (T227d).

### ISS-006 — RAG `DefaultRagModel = Dedicated` reads an index nothing creates

| Field | Value |
|---|---|
| **Status** | Open — BFF code (not this project's surface) |
| **Urgency** | before anyone sets `Analysis__DefaultRagModel` on a stamp |
| **Filed** | 2026-10-08 (found in T235) |
| **Source** | T235 doc sweep — the BYOK guide told operators to set `Dedicated` |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1432 |

**Description**

`Shared` (the default) reads `AiSearch:KnowledgeIndexName` (`spaarke-files-index`, created by H2b in the stamp's own
AI Search). `Dedicated` reads `{tenantId}-knowledge`, which nothing creates, so setting it breaks every RAG search that
does not name its index. No stamp sets it today. The two docs that advised it were corrected in T235.

**Suggested fix**

Remove `Dedicated` (needed → build, else remove) or have H2b create its index; optionally rename `Shared`.
`AnalysisOptions.cs` (enum), `KnowledgeDeploymentService.cs:288-320`.

---

### ISS-008 — L2 CustomerRunGuard (I5 / FR-32) is off in every environment

| Field | Value |
|---|---|
| **Status** | Open — before T186; needs an owner-approved live step |
| **Urgency** | before T186 |
| **Filed** | 2026-10-09 (punch-list re-verification, 203b row A27) |
| **Source** | `infrastructure/bicep/platform-controlplane.bicep` `customerRunGuardEnabled` default false; no `.bicepparam` sets it; live dev Api `CustomerRunGuard__Enabled = False` |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1484 |

**Description**

Two runs for the same customer can overlap: the guard that serializes them (spec §4D I5, FR-32 — required ON in
production) is disabled. It authenticates as the L2 UAMI against the admin Dataverse environment and fails fast at host
start when enabled without that access, which is why the default is off.

**Suggested fix**

Owner OK → ensure the UAMI is an Application User on the admin environment (`Grant-ControlPlaneIdentity.ps1`); set
`customerRunGuardEnabled = true` in `platform-controlplane-dev.bicepparam` (and the prod parameter file when created);
redeploy; prove a second concurrent run for one customer is refused.

---

### ISS-009 — External-contact bind-by-email (E11) binds contacts this stamp never invited

| Field | Value |
|---|---|
| **Status** | Open — UAC-r2 code; before any stamp accepts CIAM tokens (T240d step 2) |
| **Urgency** | before external contacts are enabled on a stamp |
| **Filed** | 2026-10-09 (T240d design) |
| **Source** | `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ContactBindingDecision.cs` `// E11` (~line 659) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1485 |

**Description**

A CIAM sign-in whose verified email matches an unbound contact is bound to it whether or not this stamp invited the
person. With one shared CIAM tenant, an account created for customer X can be bound to customer Y's same-email contact.

**Suggested fix**

Pending-invite marker written by this stamp's invitation; bind by email only when it exists; consume on bind. Also fixes
"second customer invites a person who already has a CIAM account" (fails today). Design: `notes/t240d-ciam-external-contacts-design.md` §5–§6.

---

### ISS-011 — BFF asymmetric registrations: services that cannot be constructed when a feature gate is off (204e F1–F4)

| Field | Value |
|---|---|
| **Status** | Open — BFF code (ADR-032 decision per service); latent for customer stamps |
| **Urgency** | before any environment runs with a gate off (DocIntel / AI Search) |
| **Filed** | 2026-10-09 (task 204e) |
| **Source** | `tests/Spaarke.ArchTests/Adr032/*` ledgers; `notes/task-202-punch-list.md` rows 204e-F1..F4 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1487 |

**Description**

Finance job handlers (F1), the app-only analysis job handlers + `EmbeddingMigrationService` (F2 — the BFF cannot be
constructed with `DocumentIntelligence:Enabled=false`, the code default), AI Search / chat-client consumers registered
unconditionally (F3), and ~35 services when DocIntel is on without an AI Search endpoint (F4). Stamps set DocIntel on and
the AI Search endpoint, so none of this hits T186.

**Suggested fix**

Per service: register on the dependency's gate, or give the dependency a Null-object peer (ADR-032); delete the ledger
row as each is fixed.

---

### ISS-012 — `Graph:Scopes` is required at BFF startup but nothing reads it

| Field | Value |
|---|---|
| **Status** | Open — not blocking (T258 supplies the value on every stamp) |
| **Urgency** | later (maintainability) |
| **Filed** | 2026-10-09 (T258) |
| **Source** | T258 — reading the validators behind 204e-F7 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1499 |

**Description**

`GraphOptions.Scopes` carries `[Required]` + `[MinLength(1)]` with an empty default, so a BFF without
`Graph__Scopes__0` does not start in any environment. No code reads `GraphOptions.Scopes`: every Graph client builds its
own scopes. T258 writes the literal `https://graph.microsoft.com/.default` through the manifest so stamps start. This is
the same latent-blocker class auth-v4 task 024 removed for `Graph:ClientSecret` (a rule that only prevents a boot).

**Suggested fix**

Owner decision (it changes a validator): drop the property and its two attributes, then the manifest entry
`Graph__Scopes__0` and the `IOptionsDriftTests` census row. Until then the manifest literal is correct and harmless.

### ISS-013 — The H0.5 consent callback cannot work as built (Model 2, out of scope)

| Field | Value |
|---|---|
| **Status** | Open — dormant: T258 gates the endpoint off (`Onboarding:Enabled`, default false) |
| **Urgency** | when Model 2 returns (plan D3) |
| **Filed** | 2026-10-09 (T258) |
| **Source** | T258 — deciding whether a stamp needs the Onboarding endpoint (204e-F5) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1500 |

**Description**

`POST /api/onboarding/consent-callback` (BFF, task 042) cannot carry a Model 2 consent into L2 on any current host:
(1) it enqueues to `sprk-provisioning-jobs` through the host BFF's own Service Bus client — on a customer stamp that is
the stamp's namespace, which has no such queue and which L2 does not drain; (2) Microsoft's admin-consent redirect is a
browser GET carrying `admin_consent`, `tenant` and `state` in the query, not an HMAC-signed POST, so something else
would have to sign and forward it; (3) no host was ever given an `Onboarding:HmacSigningKey` (spaarke-bff-dev ran with
`Onboarding__EnableDevBypass=true`, so the route answered 401 to every call). T258 maps the route and registers its
services only when `Onboarding:Enabled=true`; no stamp channel sets it, and `IOptionsDriftTests` fails if one does
without supplying the key.

**Suggested fix**

When Model 2 is reopened, decide the consent-capture host (L2 itself is the natural one — it owns the queue and the
run) and either rebuild the callback there or remove the BFF endpoint, `HmacSignatureVerifier`,
`ServiceBusProvisioningEnqueuer` and their tests. Needed → build, else remove.

---

### ISS-017 — `AgentToken:*` / `AgentTokenService` are registered but on no request path; a comment claims token validation that nothing does

| Field | Value |
|---|---|
| **Status** | Open (latent; nothing breaks today) |
| **Urgency** | low. Decide before the first per-customer Copilot agent live test (T257), so nobody configures `AgentToken__*` on a stamp believing it gates the agent |
| **Filed** | 2026-10-09 (T257) |
| **Source** | T257 step 2, the alignment check (design note §5.2) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1523 |

**Description**

`AgentModule` registers `AgentTokenService` (a singleton OBO client for "M365 Copilot → Graph/Dataverse") and binds
`AgentTokenOptions`. No endpoint or handler resolves either of them:
- `SpaarkeAgentHandler` has `// TODO: Inject AgentTokenService when MCI-014 is implemented`;
- the `/api/agent/*` endpoints and every endpoint the Copilot agent's OpenAPI calls use the normal OBO path.

`AgentTokenOptions.AgentAppId` is documented as "used to validate that incoming tokens were issued to the expected
agent", but no code validates `azp`/`appid` against it. It is only logged. `AgentTokenOptionsValidator` requires
`TenantId`, `ClientId`, `AgentAppId` and `DataverseEnvironmentUrl`. There is no `ValidateOnStart` and no resolver, so
the validator never runs. The canonical secret catalog still emits `AgentToken__ClientId` / `AgentToken__TenantId` app
settings.

Concrete risk: an operator or a later task reads the comment, sets `AgentToken__AgentAppId` to the "Spaarke Copilot
Agent" client, and believes the BFF now only accepts that client. It does not. Per-customer isolation comes from the
token audience and the stamp's Dataverse roles (T257 design §3), not from this setting.

**Suggested fix**

Needed → build, else remove. T257 does not need an agent-specific token path, so remove these:
- `AgentTokenService`;
- `AgentTokenOptions` and its validator;
- the `AgentToken__*` catalog app settings;
- the stale `SpaarkeAgentHandler` TODO.
If MCI-014 is ever revived, it re-adds them with a real consumer. This is BFF work (§10 hygiene, publish-size delta) for
the BFF's owning project.

---

### ISS-018 — Graph identity grant gaps found by T261: the L2 Worker lacks the roles H3/H10 call with; H3's delegated catalog has a wrong id; app-only SPE search

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | F3 and F6 before T186 (F3 blocks H3 and H10 outright); F2/F4/F5 low |
| **Filed** | 2026-10-09 (T261) |
| **Source** | T261 inventory — `notes/t261-stamp-graph-least-privilege.md` §8 (call sites, Learn citations, live reads) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1543 |

**Description**

- **F3 (blocks T186).** The L2 Worker identity (`sprk-controlplane-dev-uami`, read live 2026-10-09) holds neither
  `AppRoleAssignment.ReadWrite.All` nor `Application.ReadWrite.OwnedBy`. H10 (`POST/DELETE
  /servicePrincipals/{stamp}/appRoleAssignments`) and H3 (`POST /applications`, FICs, `appRoleAssignedTo`) need them;
  no prerequisite granted them (`Grant-ControlPlaneIdentity.ps1` granted the BFF stamp catalog). T261 gave the Worker
  its own catalog (`Handlers/ControlPlaneGraphAppRoles.cs`, PRQ-E-07 manifest 11). Owed: run
  `Grant-ControlPlaneIdentity.ps1` for `sprk-controlplane-dev-uami` (live, owner OK); then, optionally, remove the
  Worker roles the note lists as no longer needed (`Directory.ReadWrite.All`, `User.ReadWrite.All`, `Files.*`,
  `Sites.*`, `FileStorageContainer.Selected`, `Group.Read.All`, `User.Read.All`, `Mail.ReadWrite`, `Mail.Send`,
  `MailboxSettings.Read`).
- **F6 (risk for T186, delegated).** `EntraAppReg/EntraAppRegPermissionCatalog.cs` requests delegated
  `Files.ReadWrite.All` with id `75359482-…` — the APPLICATION role id; the delegated scope is
  `863451e7-0667-486c-a5d6-d135439485f0` (read live). It also lacks delegated `FileStorageContainer.Selected`
  (`085ca537-6565-41c2-aca7-db852babc212`), which OBO SPE calls need and the dev BFF registration carries. A stamp's OBO
  SharePoint Embedded calls may fail. Fix in H3's catalog + `scripts/Register-EntraAppRegistrations.ps1:288` (same id).
- **F2.** `POST /api/spe/search/items` calls `/search/query` app-only (`SpeAdminGraphService.cs:7432`). Microsoft
  documents SharePoint Embedded search as delegated-only, so it cannot return container content; the old catalog's
  `Files.Read.All` would have let it search all of Spaarke's SharePoint instead. Needs OBO or `$filter` enumeration
  (SPE admin owner — sdap-SPE-admin-app-r2).
- **F4 (dev).** `mi-bff-api-dev` lacks `Mail.ReadWrite` → inbound mark-as-read 403 (3/3, 2026-10-06); security pages
  `GET /security/alerts_v2` 403 7/8 (Learn least: `SecurityAlert.Read.All`; dev holds `SecurityEvents.Read.All`).
- **F5 (dev).** `createLink` 403 app-only and delegated — a sharing setting, not a Graph role; not investigated.

**Concrete failure without a fix:** H3 and H10 403 on the first live run (F3); OBO SPE calls on a new stamp may 403
(F6).

---

## Resolved

<!-- Resolved entries move here with the resolution date and commit/PR. -->

### ISS-019 — H14b and H14c wire webhooks to routes the BFF does not serve (a run would stop at H14b)

| Field | Value |
|---|---|
| **Status** | Resolved 2026-10-09 — H14b and H14c removed from L2 (owner directive "needed -> build, else remove"); H14a kept |
| **Urgency** | before the first run reaches H14 |
| **Filed** | 2026-10-09 (incoming note from auth-system-of-record-r1 via external-access-r3, `notes/coordination/2026-10-09-from-auth-system-of-record-r1-webhooks.md` §1) |
| **Source** | auth-system-of-record-r1 `x09a-mail-and-webhooks.md` Q2-Q3 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1560 |

**Description**

H14b registered Graph subscriptions at `{stamp}/api/webhooks/graph/{module}` and H14c a Dataverse `serviceendpoint` at
`{stamp}/api/webhooks/dataverse/communication`. Verified in code on this branch (every claim re-checked, none taken from the note):

| Claim | Result | Evidence |
|---|---|---|
| H14b notificationUrl is `{base}/api/webhooks/graph/{module}`, `clientState` = the `Communication-Webhook-SigningKey` secret | True | `H14bGraphWebhookSubHandler.cs:55,159-163,196-197` (before this change) |
| H14c endpoint URL is `{base}/api/webhooks/dataverse/communication` | True | `H14IntegrationWiringHandler.cs:290`, `H14cDataverseWebhookSubHandler.cs:45,135-139` (before this change) |
| The BFF maps neither route | True | the only webhook routes in `src/server/api/Sprk.Bff.Api` are `POST /api/communications/incoming-webhook` (`Api/CommunicationEndpoints.cs:470`) and `POST /api/compose/webhooks/spe-doc-changed` (`Api/ComposeSyncEndpoints.cs:43`); no `/api/webhooks/*` route exists |
| H14c registers no `sdkmessageprocessingstep` | True | the registrar wrote only the `serviceendpoints` row (`DataverseWebApiServiceEndpointWebhookRegistrar.cs`, before this change); `sdkmessageprocessingstep` occurs in L2 only as a privilege-name string in `Tests/Handlers/SecureRecordSetup/FakeSecureRecordSetupDataverse.cs:25-26` |
| The BFF creates/renews its own per-mailbox subscriptions with `customer.bicep`'s URL | True | `Services/Communication/GraphSubscriptionManager.cs:58,431-434` use `CommunicationOptions.WebhookNotificationUrl`; `infrastructure/bicep/customer.bicep:793` sets `Communication-WebhookUrl` = `{bffApi appServiceUrl}/api/communications/incoming-webhook` |

A Graph create handshake to a 404 route fails, H14b turns any failure into `HandlerResult.Failure`, so a run stopped at H14b.
Mail was never at risk: the BFF subscribes itself and polls every 5 minutes.

**Resolution (2026-10-09)**

Removed, not rewired (nothing needs a provisioning-time subscription; the BFF owns subscribe/renew/self-heal):
- Deleted `H14bGraphWebhookSubHandler`, `H14cDataverseWebhookSubHandler`, `IGraphSubscriptionCreator` + `GraphRestSubscriptionCreator`,
  `IServiceEndpointWebhookRegistrar` + `DataverseWebApiServiceEndpointWebhookRegistrar`, their tests and `H14SecretRedactionTests`.
- `H14IntegrationWiringHandler` now drives H14a only (`ExpectedSubStepCount` 3 -> 1); it no longer requires `BffApiUrl`, the customer
  `keyVaultName`, `dataverseEnvUrl` or `subscriptionId`. Removed `HandlerIds.H14b/H14c`, the options only they used
  (`GraphRequestTimeout`, `GraphSubscriptionExpirationMinutes`, `DataverseRequestTimeout`, `ServiceEndpoint*`), the rejection codes
  (`H14bRejections`, `H14cRejections`, four H14 parent codes) and the gates `GraphWebhooksWired` / `DataverseWebhookWired`.
- DAG: `H14 <- H12c` (the `H9` edge existed only for the webhook base URL; H9 still precedes H14 transitively via H11 <- H7 <- H9).
- Intake: removed `communicationGraphResource` / `emailGraphResource` (catalog, `HandlerRunInputs`, POST /api/runs rule
  `h14b-no-webhook-targets-configured`, `intake.schema.json`, `provisioning-runs/_templates/intake.md`, load-test payloads).
- The Worker's `Mail.Read` Graph role existed only for H14b: removed from `ControlPlaneGraphAppRoles.cs` and the T261 note. **Live revocation
  of `Mail.Read` from the Worker identity is an operator step (not done here).**
- Handler count: the dispatchable count stays **21** (H14b/H14c were never in `HandlerIds.Dispatchable`); the in-process H14 sub-steps
  went from 3 (H14a/b/c) to 1 (H14a); handler ids in the catalog 24 -> 22. No Bicep or app setting existed only for H14b/H14c.
- Signing key: a stamp still needs one - the BFF's `Communication:WebhookSigningKey` is `[Required]` and its mapped receiver verifies
  `X-Hub-Signature-256` with it. The BFF reads config key `Communication:WebhookSigningKey` (app setting `Communication__WebhookSigningKey`),
  not a Key Vault name; on a stamp that setting is a Key Vault reference to `Communication-Webhook-SigningKey`, which is what
  `scripts/canonical-secret-catalog/manifest.yaml`, `customer.bicep` and H4 already use, so the manifest needed no change. The other
  two spellings are outside provisioning: `Communication-WebhookSigningKey` (dev platform, `config/spaarke-resources.yaml:349`) and
  the BFF's own error-message text (`communication-webhook-signing-key`, `CommunicationOptions.cs:65`).
- Not fixed here (BFF owner / other issues): #1561 (the mapped receiver 401s without `X-Hub-Signature-256`, which Graph never sends),
  #1562 (nothing creates the stamp's `sprk_communicationaccount` rows).

**Entry-points**

`src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/IntegrationWiring/H14IntegrationWiringHandler.cs`, `.../Reconciler/DagAdvancer.cs`,
`docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §5, §7.9.

### ISS-014 — H13 cannot run the BFF's secure-record isolation census (no identity can call it)

| Field | Value |
|---|---|
| **Status** | Resolved 2026-10-09 — task 260 (owner approved the recommended fix 2026-10-09): H13 requires the stamp BFF's census to answer `isolated` |
| **Urgency** | before T186 Ready (the census is run by hand until then) |
| **Filed** | 2026-10-09 (T259) |
| **Source** | T259 step 5 (ISS-010's H13 half) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1527 |

**Description**

The census exists only as a BFF scheduled job: `SecureRecordIsolationCensusJob` (`secure-record-isolation-census`, every
15 min, read-only; its run's `ResultJson.status` is `isolated` | `findings` | `inert` | `error`). It is reachable only
through the generic admin routes `POST /api/admin/jobs/{jobId}/trigger` (202, async) and `GET /api/admin/jobs/{jobId}/status`
(`recentRuns[].resultJson`), both behind `RequireAuthorization("SystemAdmin")` — an `Admin` or `SystemAdmin` app role
(`AuthorizationModule`). A stamp's BFF app registration (H3) defines exactly one app role, `Provisioning.KeylessProof`,
assigned to the L2 Worker identity only; it defines no `Admin`/`SystemAdmin` role. So H13, signing in as the L2 Worker,
gets 403, and no run can prove isolation before Ready — the owner's 2026-10-09 goal ("a run cannot reach Ready unless the
census says isolated") is unmet. Granting L2 an `Admin` role would be far wider than needed: the same policy guards job
enable/disable and trigger of every job, RAG index writes/deletes, membership admin and record-matching admin.

**Suggested fix (one recommendation)**

A read-only, synchronous BFF route beside the keyless proof: `POST /api/platform/secure-record-isolation-census`, behind
the existing `KeylessProofAuthorizationFilter` (app-only token of this tenant for this API holding the L2-only
`Provisioning.KeylessProof` role — no new role, no H3 change), returning `{status, verdict, findings}` from the SAME code
the job runs (move the job's census read + `SecureBuRoleDepthAssertion.Evaluate` into one shared method; the job keeps its
schedule). L2: H13 calls it with the keyless proof's token acquisition and fails unless `status == "isolated"` (new code,
e.g. `h13-secure-isolation-not-isolated`, carrying the findings). §11: existing = the job + admin routes (async, admin-only;
widening them to L2 grants every job's controls); cost of doing nothing = H13 cannot gate Ready on isolation. Needs the
owner's OK (BFF surface + authorization, CLAUDE.md §6/§10) and a BFF publish-size check.

**Resolution (task 260, 2026-10-09)**

Built as recommended — no new app role, no H3 change, no new package.
- BFF: `POST /api/platform/secure-record-isolation-census` in `Api/Platform/KeylessProofEndpoints.cs`, behind
  `RequireAuthorization` + the existing `AddKeylessProofAuthorizationFilter` (app-only token of this tenant, this API's
  audience, `Provisioning.KeylessProof`) + the `job-submission` rate limit. Synchronous, read-only, bounded at 60 s; a read
  failure or timeout answers 200 `status=error` (no exception text). Body = the job's result: `{status, verdict,
  findings[{verdict, message}]}`.
- One census: the job's read + grading moved into `Services/ExternalAccess/SecureRecordIsolationCensus.cs` (static —
  no DI registration); the job calls it and keeps its schedule, CRITICAL lines, heartbeat, throw-on-read-failure and
  `ResultJson`. A test pins that the job and the route agree (status, verdict, findings) on isolated / findings / inert.
- Contract: route + the four statuses in `KeylessProofContract.SecureRecordIsolationCensus` (source-linked into both).
- L2: `IE2EValidationRunner.RunSecureIsolationCensusAsync` — the keyless proof's call path extracted into
  `PostAsL2IdentityAsync` and shared (same credential, `api://{BffAppRegId}/.default`, https only, role-less-token
  diagnostic, one transient retry, 401/403/500 fail, 404 inconclusive). H13 step 7b calls it after the existing checks:
  `isolated` → gate `h13-secure-isolation` Verified; `findings` → QuarantineRequired `h13-secure-isolation-not-isolated`
  (findings in the diagnostic, ≤ 20, sanitised); `inert` → QuarantineRequired `h13-secure-isolation-inert`; a failed
  call or an answer this build cannot read → QuarantineRequired `h13-secure-isolation-census-failed`; `error` /
  transport / timeout / 404 / a throw → Resumable `h13-secure-isolation-inconclusive`.
- `inert` on a fresh stamp: the census grades the security TOPOLOGY (role depth into the Secure Record unit, users in it,
  the owner team and role), not records, so a new environment with no secure record yet is graded in full and can be
  `isolated`. `inert` means the BFF finds no Secure Record unit at all; H7b creates it before H13 (H13 ← H7b), so inert
  proves nothing and is a broken stamp (or a BFF `SecureRecord:BusinessUnitName` that differs from H7b's) — it fails.
- Tests: BFF route contract (401 / 403 ×3 / 200 shape / error without exception text / reads only), job↔route parity;
  L2 runner (13 census cases) and H13 (AC-28..35). The deployment guide's "run the census by hand" interim text is
  replaced by the H13 rule (§7.10, §7.11).

**Rollout:** deploy the BFF build carrying task 260 before (or with) the Worker build: a Worker that calls an older BFF
gets 404 → Resumable `h13-secure-isolation-inconclusive` (redeploy the BFF via H9, then resume). Live proof owed at T186
(the first H13 run against a real stamp).

---

### ISS-015 — H6, H7 and H7b cannot sign in as the customer BFF app registration under either Worker credential chain

| Field | Value |
|---|---|
| **Status** | Resolved 2026-10-09 — H3 keeps a second FIC `spaarke-l2-worker` (subject = L2 Worker UAMI principalId) on every Spaarke-tenant customer BFF registration; branch `worktree-agent-ae886473c4b5b7444` (merged to the work branch by the main session) |
| **Urgency** | now — blocks T186 at H6 (first live run) |
| **Filed** | 2026-10-09 (T252; ISS-014 left free for a parallel agent) |
| **Source** | T252 step 1 inventory of the dev control plane's Worker credential chain |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1524 |

**Description**

H6 (solution import), H7 (environment-variable values) and H7b (Secure Record setup) sign in to the customer's
Dataverse environment as `InterStepState.BffAppRegId` — the per-customer BFF app registration H3 creates (D-13). The
Worker builds that credential from the FR-39 chain (`WorkerDataverseCredentialFactory.Create`):

- **Secret-free chain `[ManagedIdentityFederated]`** (T252 makes it the default and dev's setting): the Worker presents
  its own UAMI (`sprk-controlplane-{env}-uami`) as a federated assertion for that app. But H3 gives the app exactly one
  federated credential, whose subject is the **stamp BFF's** UAMI (`GraphAppRegistrationProvisioner`, FIC recipe:
  `Subject = request.UamiPrincipalId`). Nothing trusts the L2 Worker UAMI, so Entra refuses the assertion
  (AADSTS70021 / 700213: no matching federated identity record) at H6's first token request.
- **Legacy chain `[ClientSecret]`** (dev until T252): the secret slot holds `BFF-API-ClientSecret` from the platform
  vault — on dev the sentinel `pending-oob-population`, and in any case the secret of the old shared BFF app
  (`1e40baad`), not of the per-customer app. It cannot authenticate as the per-customer app (AADSTS7000215), and the
  binding rule forbids creating a real one.

So no configured chain can complete H6/H7/H7b on a Model 1 run today. Unit tests do not catch it: they stop at
credential construction, and the boot tests deliberately do not exchange tokens (`WorkerSecretFreeBootTests` header).
H3's adoption check also refuses an existing app carrying a federated credential it did not create (`foreignFics`), so a
hand-added Worker FIC would block re-runs.

**Suggested fix**

H3 adds a second federated credential on every per-customer BFF app registration, subject = the L2 Worker UAMI
(`ControlPlaneIdentityOptions.PrincipalObjectId`, already a validated Worker option), issuer = Spaarke's tenant (Model 1),
audience `api://AzureADTokenExchange`; triple-idempotent like the first; the adoption check accepts exactly these two
names. Then H6's first live call (T186) is the exchange proof. Owner check before building: confirm the per-customer
app should trust L2's identity (it already holds Dataverse System Administrator through H10's app user, and L2 already
owns the app it created), versus a different sign-in identity for H6/H7/H7b. Needed → build, else remove.

**Entry-points**

- `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/EntraAppReg/GraphAppRegistrationProvisioner.cs` (FIC recipe ~L1100–1215; adoption check ~L410–440)
- `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/Credentials/WorkerDataverseCredentialFactory.cs`
- `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/{SolutionImport/H6SolutionImportHandler.cs, EnvVarValues/H7DataverseEnvVarValuesHandler.cs, SecureRecordSetup/H7bSecureRecordSetupHandler.cs}`

**Resolution (2026-10-09)**

Built as suggested — a gap against D-13, not a design change. `GraphAppRegistrationProvisioner`:
- `BuildRequiredFicSpecs` (pure) plans two credentials before any Graph write: `spaarke-uami-trust` (stamp BFF UAMI,
  unchanged) and `EntraAppRegOptions:WorkerFicName` = `spaarke-l2-worker` — issuer
  `https://login.microsoftonline.com/{SpaarkeTenantId}/v2.0`, subject = `ControlPlaneIdentityOptions.PrincipalObjectId`
  canonicalised (the Worker UAMI's **object id**, the value `WorkerDataverseCredentialFactory`'s MI assertion carries as
  `sub`; never its clientId), audience `api://AzureADTokenExchange`. Refused before any write
  (`appreg-worker-fic-identity-missing`) when the Worker principal id is blank / not a GUID / empty, equals the stamp UAMI,
  or the two names are blank or equal. `customer-owned-model2`: stamp credential only (cross-tenant; Model 2 out of scope).
- `PlanFederatedCredentials` (pure) reconciles both by triple (SF-7) — create, no-op re-run, delete-then-recreate a
  drifted or misnamed one (a stamp triple under the worker name is moved, never dropped) — and the re-GET verification
  runs the same planner. The adoption check accepts exactly the planned names.
- No new setting: `ControlPlaneIdentity__PrincipalObjectId` (Worker, ValidateOnStart; `platform-controlplane.bicep`
  passes `uami.outputs.principalId` — the same UAMI whose clientId is the Worker's `ManagedIdentity__ClientId`).
- Tests: `WorkerFicTrustTests` (24 cases) + `AppRegistrationAdoptionTests` (+3).

**Rollout:** deploy the Worker; then, for a stamp whose H3 already ran (e.g. the T186 run), resume H3 (it adds the
credential to the adopted registration) before H6. The exchange proof is H6's first token request (T186); a fresh FIC
flaps with AADSTS70025 for ~2 minutes (auth.md). Both FICs stay for the registration's lifetime (2 of Entra's 20).

---

### ISS-010 — Production business-unit topology: the customer's own unit, the BFF application users and guests in it (INCOMING-145 §6 T1/T3/T5)

| Field | Value |
|---|---|
| **Status** | Resolved 2026-10-09 — task 259 (owner decision 2026-10-09): T1/T3/T5 built and H7b checks T1/T3; the H13 census step is split out as **ISS-014** (H13 cannot call the census with the stamp's identity) |
| **Urgency** | before T186 (server-side creates of secure children; #1081) |
| **Filed** | 2026-10-08 (T256) |
| **Source** | unified-access-control-r2 INCOMING-145 §6 (owner 2026-10-02, binding) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1486 |

**Description**

§6 makes the production topology binding: users and the BFF's application users sit in the customer's NAMED child
business unit, never in the root; the Secure Record unit is a sibling of it under the root. T256's H7b enforces the two
parts it owns — **T2** (it creates the Secure Record unit under the root and refuses, quarantined, one under any other
parent) and **T4** (it refuses, quarantined, a root default team holding Deep/Global Read on a codified table). Not
built, because each needs a decision or an input that does not exist:
- **T1** the customer unit — its name "comes from the run", but no intake key carries a customer display name
  (`IntakeParameterCatalog` has none; the schema's `displayName` never reaches the run).
- **T3** `DataverseWebApiAppUserCreator` (H10) creates both application users in the ROOT unit. Server-side creates then
  fall back to the root default team, which holds no privileges, and Dataverse refuses it as owner (#1081). H10 runs
  before H6/H7b and its users must keep System Administrator (H6/H7/H7b sign in as the BFF app user), so moving them later
  (a business-unit change removes a user's roles) is not a safe repair.
- **T5** H11 makes each guest a Dataverse user — today in the ROOT unit, with `Spaarke Basic User`
  (`H11UserProvisioningOptions.DefaultGuestSecurityRoleName`), which ships holding `prvReadsprk_Project`,
  `prvReadsprk_Matter` and `prvReadsprk_WorkAssignment` at **Deep** (`SpaarkeMaster/Roles/Spaarke Basic User.xml`).
  Deep at the root reaches every child unit, the Secure Record unit included: **on a provisioned environment every
  guest reads every secure project, matter and work assignment by depth** (NFR-05 clause 1 — exactly §6's "why it
  matters"). Nothing in the pipeline catches it: H7b runs before H11 (T4 checks only the root default team), and H13
  does not run the BFF's isolation census (INCOMING-145 §3 leaves that to "H13 or the operator"). A real-path
  cross-record exposure (F1) on the first live run (T186) unless the operator census (`secure-record-isolation-census`)
  is run and acted on.

**Suggested fix (one recommendation)**

Add intake `customerDisplayName` (required, validated at POST /api/runs); **H10** creates the customer unit (T1) under the
root before it creates the two application users IN it with System Administrator (T3), and records the unit id
(`InterStepState.CustomerBusinessUnitId`, `[ProducedBy(H10)]`); **H11** creates guests in that unit (T5); H7b then also
checks T1/T3 (unit present, application users' `businessunitid`); and **H13** triggers the BFF's read-only
`secure-record-isolation-census` and requires `isolated` (INCOMING-145 §3), so no run reaches `Ready` with isolation
void. Needs the owner's OK on the placement and the new key. Until then, T186's runbook must run the census by hand
after H11 and treat any human reaching the Secure Record unit as a stop.

**Resolution (task 259, 2026-10-09)**

Owner decision 2026-10-09 (binding): "for secure records, only users (systemusers, guest systemusers or contact users)
explicitly granted access should have access; guest users are added to the root customer business unit NOT added to the
secure business unit (no users are added to the secure business unit)." The "root customer business unit" is the
customer's own unit directly under the Dataverse root (not the root itself — Deep at the root reaches Secure Record).
- Intake: the existing `displayName` (T237) is carried into the run — required at POST /api/runs
  (`CustomerBusinessUnitIntake`: 1–160 chars, no control character, no leading/trailing whitespace, never `Secure Record`;
  `h10-customer-display-name-required` / `-invalid`). No new key (coordinator correction, §11 reuse).
- H10 (T1/T3): finds or creates the customer unit directly under the root (`h10-customer-bu-wrong-parent` Quarantine,
  `h10-customer-bu-ambiguous`), records `InterStepState.CustomerBusinessUnitId` (`[ProducedBy(H10)]`), creates both App
  Users IN it with the unit's System Administrator copy; an App User elsewhere → Quarantine
  `h10-app-user-in-foreign-business-unit`, never moved.
- H11 (T5): resolves the guest roles in the customer unit; moves a guest Dataverse added to the ROOT into the customer
  unit (PATCH + read-back) before any role; a guest in any other unit → Quarantine `userprov-guest-in-foreign-business-unit`.
- H7b: `secure_setup.customer_bu_missing` / `secure_setup.customer_bu_wrong_parent` / `secure_setup.app_user_outside_customer_bu`
  (all Quarantine); S2 already refuses any user in the Secure Record unit. Procedure version 3.
- H7 (found in review): links the customer unit to H8's container beside the root — records are owned in the customer
  unit and the BFF resolves a non-secure record's container from its owning unit only.
- H11 also refuses (Quarantine) a guest holding a role of another unit (`userprov-guest-holds-role-outside-customer-unit`).
- Not done here: H13 requiring the census → ISS-014, resolved by task 260 (H13 now refuses Ready unless the census is
  `isolated`; no hand-run census).

---

### ISS-005 — Deploy-Release Phase 3 imports a 9-solution list that does not exist

| Field | Value |
|---|---|
| **Status** | Resolved 2026-10-08 — task 218f: Deploy-Release Phase 3 imports the CI-published SpaarkeMaster (Import-SpaarkeMasterPackage.ps1, H6 rules, typed by config/environments.json); Deploy-DataverseSolutions.ps1 deleted; first publish 1.2.0.0 (run 37864623352); PR #1365 |
| **Urgency** | before the next release to demo |
| **Filed** | 2026-10-07 (found in T218b) |
| **Source** | T218b — H6 left `Deploy-DataverseSolutions.ps1`; `Deploy-Release.ps1` still calls it |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1401 |

**Description**

`Deploy-Release.ps1` Phase 3 (the `deploy-new-release` skill, Spaarke's own environments) runs
`Deploy-DataverseSolutions.ps1`, whose list names 9 solutions — 6 exist nowhere — and whose zip filter
(`:443`) accepts every zip. A release to demo imports nothing useful or fails at the first missing solution. The legacy
`Provision-Customer.ps1` (step 7, `:915`) calls it too; `Load-DemoSampleData.ps1` and `scripts/README.md` point operators
to it.

**Suggested fix**

Package type per Spaarke environment (`config/environments.json`), then point the script at SpaarkeMaster with an
explicit type and fix the filter — or retire Phase 3 in favour of the CI-published SpaarkeMaster zips (T218d).

### ISS-007 — CI identity trusts the `pull_request` OIDC subject

| Field | Value |
|---|---|
| **Status** | Resolved 2026-10-08 — owner OK; credential deleted (4 remaining: master, environment dev/staging/production); OIDC guide no longer creates it |
| **Urgency** | soon (security exposure; nothing uses the subject) |
| **Filed** | 2026-10-08 (T218d review) |
| **Source** | T218d — the new publish workflow uses the same CI identity |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1446 |

**Description**

`github-actions-spe-infrastructure` (`8c85a481-…`) has federated credential `gh-pull_request`
(`repo:spaarke-dev/spaarke:pull_request`). Unused since task 249. A same-repo pull request that edits a workflow can sign
in to Azure as the app, with all its roles, before review.

**Suggested fix**

Delete the credential; drop `pull_request` from the OIDC guide's setup loop (guide row already corrected).
