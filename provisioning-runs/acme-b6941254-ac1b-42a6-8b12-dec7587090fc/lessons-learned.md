# Lessons Learned — acme-b6941254-ac1b-42a6-8b12-dec7587090fc

> **Created by**: `customer-provisioning-orchestration-r1` task 203a per punch list row A06 (template).
> **MANDATORY** — Written by `/provision-environment` skill Step 7 BEFORE run is marked complete or folder is committed.
> Source of truth for every lesson below: `projects/customer-provisioning-orchestration-r1/notes/t186-acme-run-log.md` (§4 lessons table, §5 timeline). Drafted by Claude for the operator from that log and the handoff report `runs/b6941254-ac1b-42a6-8b12-dec7587090fc.md`.

- **Outcome**: Completed 2026-10-20 16:24:26 UTC (started 2026-10-15 15:50:24 UTC; 5 d 0 h 34 min wall clock — the first live end-to-end run, so most of the time went on finding and fixing defects)
- **Operator**: `ralph.schroeder@spaarke.com`
- **Customer / model**: acme (ACME Corporation), Model 1 dedicated stamp, DEMO subscription, tier `smb`

## What went right

- **H13 acceptance and registry**: all 8 H13 gates verified 2026-10-20 16:24:26 UTC (`h13-extended-validation`, `-stamp-keyless`, `-secure-isolation`, `-customer-mailbox`, `-trap-catalog` T1–T7, `-invariant-catalog`, `-cost-envelope`, `-registry-ready`). The Step 6a read-back found the registry row already `Ready` (`sprk_provisionedon` 2026-10-20T16:24:26Z, `sprk_currentrunid` released), so nothing was reconciled. Cost $101.06/mo vs $400 expected (advisory drift −74.7 %, under the envelope).
- **H9 BFF deploy (first BFF on a Model 1 stamp)**: Succeeded 2026-10-16 04:10:49 UTC after the staging probe, slot swap and production probe passed; NFR-01 publish size 34.93 MB (−7.95 MB vs baseline). Every earlier H9 failure failed closed with production untouched.
- **Run-control and concurrency guards proven live**: ISS-008 second `POST /api/runs` refused with 409 `customer-run-in-flight` (2026-10-15 15:50:27 UTC); cancel, clear-quarantine, resume and gate-advance each proven live after the L18/L37 fixes; H2a (21:03:53 UTC 10-15), H4 (21:42:27) and H3 (10-16 00:17:32) re-ran idempotently after their quarantines/failures with nothing orphaned.
- **Secure-by-design handlers held**: H4 left a secret-free stamp with the ADR-028 leak guard clean (10-15 21:42:27 UTC); H7b applied 29 privileges and locked 4 tables (10-16 01:47:45); H8 T6 verified app-only (10-16 01:55:37); H14m created the shared mailbox and joined it to `Spaarke-AppAccess-acme` only after the PRQ-E-16 probe of both writes (10-20 16:23:45 UTC).
- **Step 0.5 prerequisites outcome (run-log §2, timeline 2026-10-14)**: the first pass was 23 pass / 12 fail; 9 of the 12 were recipe defects and the rest real gaps (L13). After fixes and four owner approvals (Graph roles via `Grant-ControlPlaneIdentity.ps1`, EventGrid/Communication registered, registry placeholder row, tier `smb` $450/mo) the second pass was 32/32 active checks passing (T-01/T-02 SKIP on 403, as designed). Met by Spaarke: PRQ-S-00, S-03, S-04, S-06, C-10 (group created), C-12 (guest access on), E-13 (placeholder row, later retired as a prerequisite). Met by the owner: PRQ-C-09 (environment `spaarke-acme`), C-11 (PAYG policy `SpaarkeAcmePayG`), C-08 (Exchange group), C-13 (Workforce tenant), C-14 (outbound B2B attested). The L2 vCPU preflight (L17) later failed H0 and was removed. No prerequisite was skipped; none failed at the time of the run.

## What went wrong

### Lesson L01 — Pre-existing environment cannot be adopted

- **Symptom**: the old demo environment held SpaarkeMaster unmanaged, so H6 would refuse the managed import (D34).
- **Root cause**: a pre-existing environment that holds SpaarkeMaster unmanaged cannot be adopted for a managed stamp; every stamp starts from a NEW environment.
- **Fix applied**: no-deferred — open in run log; next step: add "new environment, never one that held Spaarke solutions" to the guide's environment-creation step.
- **Landing spot**: `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` (environment creation step).
- **Blocks future runs**: conditional-on an operator reusing an existing environment (inferred).
- **Punch-list class**: A (inferred)

### Lesson L02 — Legacy resource groups mix products

- **Symptom**: `rg-spaarke-demo` also hosts the website's functions, so deleting the group would have broken them.
- **Root cause**: no inventory of consumers (app settings, code references, metrics) was taken before deleting the old demo.
- **Fix applied**: no-deferred — open in run log; next step: write the inventory-first, delete-resource-by-resource rule into the guide. The old demo was cleaned up resource by resource (run-log §3).
- **Landing spot**: deployment guide (decommission / environment-retirement note).
- **Blocks future runs**: conditional-on a decommission being performed (inferred).
- **Punch-list class**: A (inferred)

### Lesson L03 — Website demo-request flow is independent of customer stamps

- **Symptom**: the website demo flow (website → `spaarke-bff-dev` → `sprk_registrationrequest` in spaarkedev1 → "Approve Demo Access") adds users to group `745bfdf6`, not to a stamp.
- **Root cause**: the flow ignores the registry row's `sprk_securitygroupid`; retargeting it to a stamp needs a code change and a cross-stamp identity decision.
- **Fix applied**: no-deferred — open in run log; owner decision pending.
- **Landing spot**: deployment guide § demo; owner decision.
- **Blocks future runs**: no (inferred; the row records it as an observation).
- **Punch-list class**: C

### Lesson L04 — Microsoft.SignalRService registration not required

- **Symptom**: `Microsoft.SignalRService` was registered on the DEMO subscription although not obviously needed.
- **Root cause**: SignalR is conditional (`signalrEnabled`, default false) and ARM registers it on demand; registering early is harmless.
- **Fix applied**: n/a — log status `not needed` (no change; recorded for completeness).
- **Landing spot**: none.
- **Blocks future runs**: no
- **Punch-list class**: A (inferred)

### Lesson L05 — Read-only agent printed secret values

- **Symptom**: a read-only agent asked to list app settings printed secret values into its transcript.
- **Root cause**: the prompt did not say "names only; never print values" and did not filter with `--query "[].name"`.
- **Fix applied**: no-deferred — open in run log; next step: add the agent-prompt rule.
- **Landing spot**: `.claude/constraints/agent-cost.md` or `provisioning.md` agent-prompt note.
- **Blocks future runs**: conditional-on agents being asked to read settings (inferred).
- **Punch-list class**: A (inferred)

### Lesson L06 — Retiring an environment strands its SPE root container

- **Symptom**: the old demo's SharePoint Embedded root container remains after retirement.
- **Root cause**: a container cannot be removed once its owning app is gone; here it sits under the type billed by Syntex `1f3cfdfb`, which the owner keeps, so nothing more was done.
- **Fix applied**: no-deferred — open in run log; next step: decommission must list each SPE container with type and owning app and decide per container BEFORE deleting any app registration.
- **Landing spot**: deployment guide (decommission note, with L02).
- **Blocks future runs**: conditional-on a decommission being performed (inferred).
- **Punch-list class**: C (inferred)

### Lesson L07 — Repo references to a retired environment are scattered

- **Symptom**: references to the old demo remain across config, ~20 scripts, a web resource and ~10 docs.
- **Root cause**: retiring an environment had no repo-sweep step.
- **Fix applied**: no-deferred — the sweep itself was done for the old demo (`chore/retire-old-demo-refs`, merged 2026-10-14); the guide note is open in the run log. Remaining old-demo references are on the current-task "Remaining" list.
- **Landing spot**: deployment guide (decommission note).
- **Blocks future runs**: no (inferred)
- **Punch-list class**: A (inferred)

### Lesson L08 — Environment created from prose got the wrong URL and group

- **Symptom**: the environment was created as `spaarkedemo` with an existing security group; both fail the run's checks and the group would admit unrelated users.
- **Root cause**: creation from a prose description; the run enforces the values (POST /api/runs `dataverse-env-url-invalid`; H11 `userprov-security-group-rejected`) but the operator had no exact value card (also: a taken domain silently gets a digit).
- **Fix applied**: no-deferred — open in run log; next step: guide value card and skill Step 1 printing it with the customerId filled in. The owner corrected the environment for this run.
- **Landing spot**: deployment guide (environment-creation step); `.claude/skills/provision-environment/SKILL.md` Step 1.
- **Blocks future runs**: no — the checks refuse wrong values; the card removes the rework (inferred).
- **Punch-list class**: A

### Lesson L09 — Dataverse property name typed in lower case

- **Symptom**: H11 and the PRQ-C-12 recipe read `organization.restrictguestuseraccess`; the live GET is a 400, so H11 would have failed every run.
- **Root cause**: the logical name is `restrictGuestUserAccess` and Web API names are case-sensitive; unit-test fakes used the same wrong name, so they proved nothing.
- **Fix applied**: yes-here-in-this-run — `DataverseWebApiGuestUserWriter.cs`, `H11GuestAccessSeamsTests.cs`, `prereqs.yaml`, `Prereqs-Recipes.Tests.ps1`, docs (negative test rejects the lower-case key); dev control plane redeployed 2026-10-14. Rule: copy property names from metadata or a live response.
- **Landing spot**: handler code and `prereqs.yaml` (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L10 — PRQ-C-11 recipe used a resource group that does not yet exist

- **Symptom**: the recipe put the billing policy in "the stamp resource group", which H2a creates during the run.
- **Root cause**: recipe not run against a real target; the policy needs a group that exists before the run.
- **Fix applied**: yes-here-in-this-run — `prereqs.yaml` PRQ-C-11 remediation now uses `rg-ppbilling-{customerId}` and the current admin-center path (the owner used an existing group for ACME; it works).
- **Landing spot**: `scripts/provisioning-prereqs/prereqs.yaml` (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L11 — No recipe for creating a test Workforce tenant

- **Symptom**: creating an ACME-style test org (the customer's home directory) was an operator step with no procedure.
- **Root cause**: the demo/test customer path was never written up (Entra path, domain, test users, tenant id for PRQ-C-13, outbound B2B default for PRQ-C-14).
- **Fix applied**: no-deferred — open in run log; next step: write the test or demo customer appendix.
- **Landing spot**: deployment guide, test or demo customer appendix.
- **Blocks future runs**: conditional-on another demo/test customer being onboarded (inferred).
- **Punch-list class**: A

### Lesson L12 — PRQ-C-11 check used pac commands that do not exist

- **Symptom**: the skill's Step 1e-bis hard stop and the recipe ran `pac licensing get-environment-billing-policy`, absent in pac 1.46.1; the run would have stopped at intake.
- **Root cause**: preview commands assumed available.
- **Fix applied**: yes-here-in-this-run — `prereqs.yaml`, skill, `Prereqs-Recipes.Tests.ps1`: both now use the GA Power Platform API `licensing/billingPolicies` (2024-10-01); interactive prompt removed; 4 new Pester cases; verified live (ACME passes, spaarkedev1 fails). Policy name now `Spaarke{CustomerId}PayG`.
- **Landing spot**: `prereqs.yaml` and the skill (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L13 — First prerequisite pass: 12 failures, 9 were recipe defects

- **Symptom**: Step 0.5 returned 23 pass / 12 fail on the first real pass.
- **Root cause**: none of the recipes had run against a real customer. Recipe defects in PRQ-S-00, S-01, S-03, T-02, T-03/T-04, E-05/E-13, C-06, C-08, C-10; real gaps: the L2 Worker identity lacked 3 of its T261 Graph roles (`Grant-ControlPlaneIdentity.ps1` never re-run on dev) and H1's provider list lacked four namespaces `customer.bicep` can deploy.
- **Fix applied**: yes-here-in-this-run — recipes, Pester cases and `SubscriptionReadinessOptions.cs` fixed; Graph roles granted (T3 parity 7/7); providers registered; second pass 32/32 active checks. Log status still reads `in progress` (see the New-prereqs table for the remaining operator step and the guide/prereqs carry-forward).
- **Landing spot**: `prereqs.yaml`, `Prereqs-Recipes.Tests.ps1`, `docs/guides/PROVISIONING-PREREQUISITES.md`, `SubscriptionReadinessOptions.cs`.
- **Blocks future runs**: conditional-on `ControlPlaneGraphAppRoles.cs` changing without a re-run of `Grant-ControlPlaneIdentity.ps1`.
- **Punch-list class**: A

### Lesson L14 — Demo expiry redirected to the first customer registry row

- **Symptom**: `DemoExpirationService` on `spaarke-bff-dev` picks the environment for expired demo users by `SelectDefault`; with no row flagged default, "ACME Corporation" sorts first, so team removal and SPE revoke throw on its empty fields and the user is still marked expired.
- **Root cause**: fallback to a default or first-by-name row instead of the request's own target environment. Nothing expired on 2026-10-14.
- **Fix applied**: yes-here-in-this-run — expiry uses each request's own target environment (`DemoExpirationService.cs` + tests); PR #1885 merged (master `ed2b703f3`) and the fix is live on dev (timeline 04:10 UTC). Log status column still reads `in progress`.
- **Landing spot**: BFF `DemoExpirationService.cs` (done); the row's wording is not clear that a separate deploy of `spaarke-bff-dev` remained.
- **Blocks future runs**: no
- **Punch-list class**: B

### Lesson L15 — Registry `sprk_environmenttype` mapped from control-plane tier

- **Symptom**: the skill maps `dev` → Development, which describes Spaarke's control plane, not the customer's environment; the ACME row was written as Demo (1) by hand.
- **Root cause**: skill Step 1f derives the type from the tier instead of the intake.
- **Fix applied**: no-deferred — open in run log; next step: take the type from the intake, defaulting to Production for a customer stamp (listed in current-task "Remaining": fix L15).
- **Landing spot**: `.claude/skills/provision-environment/SKILL.md` Step 1f.
- **Blocks future runs**: no (inferred; the operator can correct the row)
- **Punch-list class**: A

### Lesson L16 — First POST /api/runs returned HTTP 500

- **Symptom**: the first run request failed before a run was created (`sprk_currentrunid` stayed null).
- **Root cause**: `CustomerRunGuard` built If-Match with `new EntityTagHeaderValue(etag, isWeak: false)`, which throws on Dataverse's weak ETag (`W/"…"`); tests used an in-memory store so the real header code never ran.
- **Fix applied**: yes-here-in-this-run — `DataverseRegistryConcurrencyStore.cs` now uses `EntityTagHeaderValue.Parse` + 5 tests; control plane redeployed and the POST re-sent (202), proven live. Rule: every external adapter gets a test that builds its real request.
- **Landing spot**: `DataverseRegistryConcurrencyStore.cs` (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L17 — H0 vCPU preflight failed on a fresh subscription

- **Symptom**: H0 failed in 7 s on `quota-subscription-vcpu` ("NOT REPORTED") because Microsoft.Compute was not registered.
- **Root cause**: after registration the DEMO westus2 `standardDv5Family` limit is 0, so the check would refuse every fresh subscription; App Service plans draw on Microsoft.Web quota, and no correct replacement check exists (`Microsoft.Web/locations/{loc}/usages` is not implemented).
- **Fix applied**: yes-here-in-this-run — resolved by removal 2026-10-15 (`ArmComputeVCpuProbe.cs` check removed from H0); a real quota shortfall now surfaces at H2a with Azure's own error. Microsoft.Compute stays registered on DEMO.
- **Landing spot**: `ArmComputeVCpuProbe.cs`, `prereqs.yaml`, `SubscriptionReadinessOptions.cs` (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L18 — Resume, cancel and gate-advance endpoints did nothing

- **Symptom**: `POST /resume` on the failed run returned 202 and changed nothing; the queue's dead-letter count went to 1.
- **Root cause**: the endpoints enqueued envelopes whose `HandlerId` was a control tag ("Resume", "Cancel", "GateAdvance", "ClearQuarantine") that no Worker handler is registered for, so they were dead-lettered `NoHandler`; tests asserted the tag and so proved the dead path.
- **Fix applied**: yes-here-in-this-run — `RunsEndpoints.cs`; transitions moved to a Core `RunControlService`, regression test asserts every enqueued envelope names a dispatchable handler; preflight on a terminal run now 409; guide §L2 API table and skill EXEC-05 updated. Proven live (resume 14:17 UTC, cancel 15:50:05 UTC).
- **Landing spot**: `RunsEndpoints.cs`, `RunControlService`, guide, skill (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L19 — H0 dispatched H0.5 on every run, failing the run

- **Symptom**: after H0 passed, the run went Failed @ H0.5 one second later: `consent-malformed-payload` (empty `ConsentCapturePayload`).
- **Root cause**: leftover "wave-C4 temporary bridge" — H0 enqueued H0.5 with `ParametersJson = "{}"`. H1 → H2a and H12c → H14 dispatched duplicate copies, and H12b → H12c started H12c before its dependency H12a finished.
- **Fix applied**: yes-here-in-this-run — the four bridges removed from `H0PreflightHandler.cs`, `H1SubscriptionReadinessHandler.cs`, `H12bAppConfigSeedHandler.cs`, `H12cRuntimeReferencesHandler.cs` (reconciler owns chaining per `DagAdvancer.HandlerDependencies`); guard test: only H0.5 may take an `IHandlerEnqueuer`.
- **Landing spot**: handler code (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L20 — H2a's ARM deployment had no location

- **Symptom**: H2a created an empty `rg-spaarke-acme-prod`, then ARM refused the deployment: `400 InvalidDeployment — The 'location' property must be specified`; H2a quarantined.
- **Root cause**: `ArmDeploymentRunner` built `ArmDeploymentContent` without `Location` (required at subscription scope); `ArmWhatIfDriftDetector` had the same omission; unit tests used a fake ARM accepting any body.
- **Fix applied**: yes-here-in-this-run — `ArmDeploymentRunner.cs`, `ArmWhatIfDriftDetector.cs` set `Location = request.Location`; tests assert the PUT/what-if body carries `location`. Cleared and resumed 17:43 UTC.
- **Landing spot**: `src/server/services/**` handler code (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L21 — Every fresh stamp quarantined at H2a on the T1 trap

- **Symptom**: after a successful ~10 min stamp deploy, T1 failed: `trap-T1-keyvault-reference-identity-mismatch — prod='SystemAssigned', staging='SystemAssigned'`.
- **Root cause**: `app-service.bicep` / `app-service-slot.bicep` left `keyVaultReferenceIdentity` to H4's patcher, which runs after H2a; inspector rule R3 rejected only an explicit `'SystemAssigned'`, not an absent property.
- **Fix applied**: yes-here-in-this-run — both modules set it to the UAMI at creation (`customer.json` rebuilt); `ArmTemplateInspectorTests.RealCustomerTemplate_EverySiteAndSlot_SetsKeyVaultReferenceIdentityToTheUami`; PR #1901 merged, ARM build `2026.10.15-15` published; T1 passed 21:03:53 UTC.
- **Landing spot**: `modules/app-service.bicep`, `modules/app-service-slot.bicep`, `customer.json` (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L22 — ARM template artifact publishes only from master

- **Symptom**: `publish-provisioning-arm-artifacts.yml` dispatched on a work branch failed at `azure/login` (`AADSTS700213`).
- **Root cause**: the federated credential subject is `ref:refs/heads/master`; a Bicep fix found mid-run reaches L2 only by merging its PR. The row calls this the right trust boundary — do not add a branch credential.
- **Fix applied**: no-deferred — open in run log; next step: carry into the guide's H2a quarantine recovery at T186 close.
- **Landing spot**: deployment guide (H2a recovery).
- **Blocks future runs**: conditional-on a Bicep fix being needed mid-run (inferred).
- **Punch-list class**: A

### Lesson L23 — H2a treated its own resources as name collisions on re-run

- **Symptom**: the resume failed in under a second: `[resource-name-taken] StorageAccount name 'sprkacmeprodsa' is unavailable`; that account was the stamp's own.
- **Root cause**: `checkNameAvailability` reports any existing name as taken and the check was skipped only for upgrade runs, so any resume or retry of a fresh run after a deploy failed.
- **Fix applied**: yes-here-in-this-run — `ArmResourceNameAvailabilityProbe.cs`, `IResourceNameAvailabilityProbe.cs`, `H2aBicepInfraDeployHandler.cs`: an unavailable name is read at its expected ID in the stamp RG (200 = ours, 404 = real collision); 3 tests fail without it. Proven live 21:03:53 UTC.
- **Landing spot**: handler code (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L24 — H4 wrote secrets before its own Key Vault role grant propagated

- **Symptom**: 10 of 13 Key Vault entries got `403 ForbiddenByRbac`; run quarantined (`kvsecrets-kv-write-partial-failure`).
- **Root cause**: RBAC takes minutes to reach the Key Vault data plane; H4 started `SetSecret` immediately after granting the role.
- **Fix applied**: yes-here-in-this-run — `SecretClientKvWriter.cs`, `KvSecretsPopulationOptions.cs`: 403 retried every 15 s for up to 10 min across the whole write; 2 new tests. Proven live 21:30 UTC.
- **Landing spot**: handler code (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L25 — H4's secret-leak guard rejected the Redis endpoint

- **Symptom**: after the L24 fix H4 quarantined on `kvsecrets-cleartext-secret-leak` for `RedisEndpoint`.
- **Root cause**: Azure Managed Redis endpoints are `host:port`; the guard exempted bare hosts and http(s) URLs but a `:port` suffix fell through to the 40-character token rule; the test list predated T242/T246.
- **Fix applied**: yes-here-in-this-run — `H4KvSecretsPopulationHandler.cs`: DNS host plus numeric port is exempt (a token in the port position still trips); AC28b/AC28d include Redis and Content Safety endpoints, AC28c has 2 new must-trip cases. Proven live 21:42:32 UTC.
- **Landing spot**: handler code (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L26 — H3 used its new Entra app before Graph replicated it

- **Symptom**: nine seconds after creating `spaarke-bff-api-acme`, H3 failed `Graph ODataError 404: Request_ResourceNotFound` on that object.
- **Root cause**: `CreateAppAsync` PATCHed `identifierUris`, read `optionalClaims` and created the service principal with no replication retry.
- **Fix applied**: yes-here-in-this-run — `GraphAppRegistrationProvisioner.cs`: those calls go through `RetryWhileReplicatingAsync` (404 / the 400 message, 6 × 10 s); `GraphReplicationRetryTests`. Known limit: no unit test proves the call sites use the helper; the live re-run did (22:04 UTC).
- **Landing spot**: handler code (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L27 — Second federated credential write refused while Graph replicated the first

- **Symptom**: H3's second POST (`spaarke-l2-worker`) got `400 Request contains a property with duplicate values` although no such credential existed.
- **Root cause**: Graph refuses a credential write on an app it has not finished replicating.
- **Fix applied**: yes-here-in-this-run — `GraphAppRegistrationProvisioner.cs`: the read → plan → delete/create pass runs through `RetryWhileReplicatingAsync` with `IsFicWriteNotYetConsistent` (404, or 400 "duplicate values"); each attempt re-reads. Proven live 22:04:39 UTC.
- **Landing spot**: handler code (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L28 — H4b would poll /healthz on a stamp that has no BFF code (caught before it ran)

- **Symptom**: found by static audit: on a fresh stamp `/healthz` returns 404 (placeholder page), so H4b would time out on every fresh run.
- **Root cause**: H4b waited for `/healthz` 200 after the settings write, but the code arrives only at H9, which depends on H4b.
- **Fix applied**: yes-here-in-this-run — `H4bBulkAppSettingsHandler.cs`, `HandlerRunInputs.cs`: poll only on an upgrade run (`provisionedOn` present); test `L28_FreshStamp_WritesSettingsAndCompletes_WithoutPollingHealthz`.
- **Landing spot**: handler code (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L29 — H3's Key Vault commit had one budget and no retry (caught before it ran)

- **Symptom**: any transient error on the post-consent write of `BFF-API-ClientId` / `BFF-API-Audience` would quarantine the run, and a timeout escaped as an uncaught cancellation.
- **Root cause**: one 15 s budget for every write, no retry, own timeout not converted to a diagnostic.
- **Fix applied**: yes-here-in-this-run — `GraphAppRegistrationProvisioner.cs`, `EntraAppRegOptions.cs`: `CommitWithRetryAsync` with a per-write timeout and retries on 0/403/408/429/5xx (8 × 15 s); tests in `GraphReplicationRetryTests`. The deferred commit ran at 00:17 UTC.
- **Landing spot**: handler code (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L30 — A handler's failure gates stay Pending after it later succeeds

- **Symptom**: the run record still lists `h2a-bicep-deploy-failed`, `h2a-resource-name-taken`, `h4-kvsecrets-…`, `h3-appreg-provisioning-failed` as `Pending` after those handlers passed.
- **Root cause**: nothing resolves a handler's own failure gates on success; nothing aggregates gate states, so the run is not blocked, but an operator sees false open gates.
- **Fix applied**: no-deferred — open in run log: mark a handler's own failure gates resolved when it completes, after T186 (current-task "Remaining": fix L30).
- **Landing spot**: `src/server/services/**` — the handlers' `FailAsync` gate writes.
- **Blocks future runs**: no (the row states the run is not blocked).
- **Punch-list class**: A

### Lesson L31 — Worker Dataverse/Graph REST clients sent every request once (caught before it ran)

- **Symptom**: a Dataverse 429 or a gateway 502/503/504 failed the handler; several handlers map any writer error to QuarantineRequired.
- **Root cause**: no transient-HTTP retry on the Worker's REST clients.
- **Fix applied**: yes-here-in-this-run — `TransientHttpRetryHandler` (Core `Infrastructure/Http`) on the 15 Dataverse/Graph clients: 429 on any method, 408/502/503/504 and transport failures on GET/HEAD only, Retry-After honoured; `TransientHttpRetryHandlerTests`. Bounds revised under L36.
- **Landing spot**: Worker `Program.cs` HttpClient registrations (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L32 — H8 treated SPE replication lag as fatal (caught before it ran)

- **Symptom**: activation after create, the business-unit stamp PATCH and its read-back (404, or a stale 200) took the failed-bind path that deletes the new container and quarantines; the app-only verifier quarantined on any non-404 error.
- **Root cause**: no bounded retry for SharePoint Embedded replication or transient errors.
- **Fix applied**: yes-here-in-this-run — `GraphContainerProvisioner.cs`, `GraphAppOnlyContainerVerifier.cs`, `SpeGraphRetry.cs`: bounded retries (6 × 10 s); a different non-empty stamp is still a conflict; verifier transients become Resumable `spe-container-verification-transient`; `H8GraphReplicationRetryTests` (22 cases). H8 passed 10-16 00:30 UTC.
- **Landing spot**: handler code (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L33 — H10 judged propagation as mismatch and quarantined on transients (caught before it ran)

- **Symptom**: the role-copy lookup ran right after the business unit was created; the app-user and app-role parity verifiers read once and mapped 429/5xx/timeouts to Quarantine.
- **Root cause**: no retry for not-yet-visible answers or transients.
- **Fix applied**: yes-here-in-this-run — `DataverseWebApiAppUserCreator.cs`, `DataverseWebApiAppUserVerifier.cs`, `GraphRestAppRoleParityVerifier.cs`, `H10Propagation.cs`: 6 attempts, then a new `Unverified` result → Resumable; H13's T2/T3 probes map it to InfraFault; `H10PropagationRetryTests` (24 cases). H10 passed 10-16 00:17:45 UTC.
- **Landing spot**: handler code (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L34 — No propagation wait for the first FIC sign-in or first app-user Dataverse call (caught before it ran)

- **Symptom**: a FIC written minutes earlier answers AADSTS70025/700211/700212/700213 (or 700016) until Entra replicates it; a new Dataverse app user answers 401/403 until effective.
- **Root cause**: H6/H7/H7b made the first call with no wait.
- **Fix applied**: yes-here-in-this-run — `Handlers/Credentials/PropagationWait.cs`, H6 importer and org-settings applier, H7 writer: those AADSTS codes waited out (10 min at 15 s); first Dataverse call retries 401/403 under one budget; `PropagationWaitTests` and others.
- **Landing spot**: handler code (done). Known limit: a permanent AADSTS error now surfaces after the 10-minute wait (L36).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L35 — H11 used Graph objects it had just created (caught before it ran)

- **Symptom**: `assignLicense` ran right after the user POST; the guest's group add and the Dataverse system-user read ran right after the invite.
- **Root cause**: no replication retry on freshly created Graph objects.
- **Fix applied**: yes-here-in-this-run — `GraphRestUserProvisioner.cs`, `GraphRestEnvironmentSecurityGroupClient.cs`, `DataverseWebApiGuestUserWriter.cs`, `H11ReplicationRetry.cs`: retry on 404 and the 400 "does not exist" family; genuine licence errors are not retried; `H11ReplicationRetryTests`.
- **Landing spot**: handler code (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L36 — Review findings on the L28–L35 fixes

- **Symptom**: one adversarial pass found (a) the new retry handler could sleep past the 30 s HttpClient timeout, (b) H6's propagation waits (up to 20 min) plus a 60-min import exceeded the 65-min lock, (c) H8's cleanup DELETE counted a 404 as "gone" and could orphan a container on resume.
- **Root cause**: retry/wait budgets not reconciled with HttpClient timeout and `MaxHandlerDuration`; (c) pre-existing, made likelier by L32.
- **Fix applied**: yes-here-in-this-run — `TransientHttpRetryHandler.cs` (4 attempts, 1 s base, 5 s cap), `DispatcherOptions.cs` (`MaxHandlerDuration` 90 min, `DispatcherOptionsBudgetTests`), `GraphContainerProvisioner.cs` (DELETE retries 404; persistent 404 keeps the container on the record); all mutation-checked.
- **Landing spot**: handler code (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L37 — A sibling handler's success overwrote WaitingOnGate and stalled the run

- **Symptom**: H3 parked the run at the admin-consent gate (22:04:39 UTC 10-15); H5 then succeeded and wrote `Running`/`H5`; gate-advance was refused 409 `run-not-waiting-on-gate` and the reconciler's re-dispatch was dropped as a duplicate MessageId.
- **Root cause**: every handler's success set `Status = Running` unconditionally (the same overwrite could erase a sibling's Failed or Quarantined).
- **Fix applied**: yes-here-in-this-run — `ProvisioningRun.MarkPhaseSucceeded` (19 handlers) + source-scan test; `RunControlService.AdvanceGateAsync` accepts a Running run with a Pending gate. Recovery proven live 10-16 00:16 UTC (advance → 202).
- **Landing spot**: handler code and `RunControlService` (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L38 — H8, H7's writer and Provision-Customer.ps1 expanded a non-existent navigation property

- **Symptom**: H8 failed `spe-recorded-container-unreadable` (400 "Could not find a property named 'environmentvariablevalues'"); H7 would have failed the same way.
- **Root cause**: `environmentvariablevalues` is the entity-SET name; the navigation property is `environmentvariabledefinition_environmentvariablevalue`. Test fakes never checked the query.
- **Fix applied**: yes-here-in-this-run — `DataverseRootBusinessUnitReader.cs`, `DataverseWebApiEnvVarValuesWriter.cs`, `scripts/Provision-Customer.ps1`: one constant `ValuesNavigationProperty`; tests pin the expand name. H8 passed 10-16 00:30 UTC.
- **Landing spot**: handler code and script (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L39 — Published SpaarkeMaster could not be installed managed in a new environment

- **Symptom**: H6's import of 1.2.3.0 failed (statuscode 31): "Option Label … description is null or empty … language: 1033"; 22 options had empty English labels.
- **Root cause**: `Set-ContactIdentityBindingSchema.ps1` and `Set-AssignedAccessLedgerSchema.ps1` built labels as `$Options[$value]` from an `[ordered]` dictionary with int keys, which indexes by position and returns `$null`; every earlier import was unmanaged over existing options, which tolerates it.
- **Fix applied**: yes-here-in-this-run — PR #1995 (master `4c7be32b9`): labels in source, SpaarkeMaster 1.2.3.1, same labels written in spaarkedev1, both scripts read by key, CI guard `SpaarkeMasterOptionLabelsPackagedTests`; 1.2.3.1 published 10-16 01:10 UTC; H6 passed 01:47:13 UTC.
- **Landing spot**: `scripts/Set-ContactIdentityBindingSchema.ps1`, `scripts/Set-AssignedAccessLedgerSchema.ps1`, package source (done).
- **Blocks future runs**: no
- **Punch-list class**: C (inferred — solution package and schema scripts sit outside the control plane)

### Lesson L40 — H12 field-mapping seeder bound navigation properties with the wrong casing (found before it ran)

- **Symptom**: `sprk_sourcerecordtype@odata.bind` / `sprk_targetrecordtype@odata.bind` would have failed every profile POST on a new stamp.
- **Root cause**: `$metadata` names them `sprk_SourceRecordType` / `sprk_TargetRecordType`; bind names are case-sensitive. Found by validating the control plane's Dataverse names against live metadata; the same check cleared every other name and flagged only false alarms.
- **Fix applied**: yes-here-in-this-run — `DataverseWebApiFieldMappingSeeder.cs` constants with metadata casing (test forbids lower case); `docs/guides/FIELD-MAPPING-ADMIN-GUIDE.md` recipe corrected.
- **Landing spot**: handler code and guide (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L41 — A second handler failure hid the first; the run showed Running while stalled

- **Symptom**: after H6 succeeded (01:47 UTC) the run was Running but H8 (and behind it H4b, H9, H7) never ran; the reconciler re-sent H8's processed attempt every 5 s.
- **Root cause**: H6's failure replaced H8's as the only recorded failure (`currentPhase`/`errorDetail`); resume re-dispatches only `currentPhase`, and the stale attempt's MessageId was already processed.
- **Fix applied**: yes-here-in-this-run — live recovery 01:55:23 UTC via `POST …/gates/h8-spe-recorded-container-unreadable/advance` (the L37 path), then fixed in code: `ProvisioningRun.FailedHandlers` / `MarkPhaseFailed` in `HandlerOutcomeApplier`, `RunControlService.ResumeAsync`, `DagAdvancer` and five handlers; resume re-dispatches every failed handler; source-scan guard.
- **Landing spot**: `src/server/services/**` (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L42 — H9's BFF artifact had never been published

- **Symptom**: H9 failed `artifact-manifest-rejected`: no `latest.json` in `provisioning-artifacts`, no `bff-api-*.zip`.
- **Root cause**: the zip + manifest upload lived in the Build job of `deploy-bff-api.yml`, which deploys the platform BFF and has been dispatch-only since 2026-08-11.
- **Fix applied**: yes-here-in-this-run — PR #2000 (`879a0a1ea`): new `publish-bff-artifact.yml` (build, r3 gates, zip, manifest, rollback pointer; deploys nothing); upload steps removed from `deploy-bff-api.yml`; WORKFLOWS.md corrected. Published `bff-api-2026.10.16-2.zip` 02:11:53 UTC.
- **Landing spot**: `.github/workflows/publish-bff-artifact.yml` (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L43 — H9's scheduled-jobs slot guard could never succeed on a new customer site

- **Symptom**: H9 failed `scheduled-jobs-slot-guard-infra-fault` (`ArgumentNullException: resourceId`) before any zip-deploy.
- **Root cause**: for a site with no sticky setting ARM answers `slotConfigNames` with `"id": null`, and the deserializer in Azure.ResourceManager.AppService 1.5.0 throws; the unit test's fake ARM answered with a real `id`.
- **Fix applied**: yes-here-in-this-run — `ArmSlotStickyAppSettingWriter`: slotConfigNames GET/PUT as raw JSON over the ArmClient pipeline; tests include the verbatim live answer. Proven live 02:26:08 UTC.
- **Landing spot**: handler code (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L44 — A RetryableWithCleanup failure re-enqueued itself forever

- **Symptom**: H9 ran ~370 times in 12 minutes (02:13:57–02:26:05), each time downloading the 36 MB artifact; `handlerRetryAttempts.H9` reached 494 (498 final).
- **Root cause**: `HandlerOutcomeApplier` re-enqueued every such failure with no limit and no delay.
- **Fix applied**: yes-here-in-this-run — `HandlerOutcomeApplier`, `ProvisioningRun`, `RunControlService`: `RollbackTransitions.MaxAutoRetries = 3`, then the failure is Resumable with "Automatic retries exhausted … resume"; success, resume or gate advance restores the budget.
- **Landing spot**: handler/applier code (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L45 — Kudu zip-deploy cut off after 100 s

- **Symptom**: H9's zip-deploy failed 02:27:49 with "timed out after 00:15:00" after 100 s.
- **Root cause**: the typed `HttpClient` kept .NET's default 100 s `Timeout`, so the 15-minute ceiling never applied (and its message misreported it); a synchronous zip-deploy is also ended by the App Service front end at ~230 s.
- **Fix applied**: yes-here-in-this-run — `KuduZipDeployer`, Worker `Program.cs`: `POST /api/zipdeploy?isAsync=true` then poll the deployment record every 10 s under one `KuduZipDeployTimeout` ceiling; 5 new tests. Proven live 02:43 UTC.
- **Landing spot**: handler code (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L46 — Staging slot lacked settings production had

- **Symptom**: staging `/health` was 503 ×24; the container aborted with `Redis is disabled and in-memory fallback not opted in` (staging 61 settings, production 67).
- **Root cause**: `customer.bicep` writes its inline BFF settings to the production site only, and non-sticky settings travel with a swap (a swap would have stripped them from production).
- **Fix applied**: yes-here-in-this-run — `ArmSlotStickyAppSettingWriter` (H9 step 8) makes the slot hold production's non-sticky settings + the slot's sticky ones + the guard (`BuildStagingSettings`); tests rewritten. Proven live 02:58 UTC.
- **Landing spot**: handler code (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L47 — Stamp never created the queues the BFF's Office workers hard-code

- **Symptom**: staging stdout showed `UploadFinalizationWorker` / `ProfileSummaryWorker` looping on "messaging entity … could not be found" (`office-upload-finalization`, `office-profile`, `office-indexing`).
- **Root cause**: `customer.bicep`'s `serviceBusQueues` default listed only four queues and H2a passes no override.
- **Fix applied**: yes-here-in-this-run — three queues added to the default (`customer.json` rebuilt), created live on `spaarke-acme-prod-sbus`; guard `T186_L47_EveryQueueTheBffHardCodes_IsAQueueTheStampTemplateCreates` (must-fire + must-not-fire controls).
- **Landing spot**: `customer.bicep` `serviceBusQueues` (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L48 — BFF cannot start within App Service's default 230 s on the S1 plan

- **Symptom**: container start 02:59:22, Kestrel listening 03:03:02 (~220 s), platform startup probe failed at 03:03:13; H9's probe (24 × 5 s) gave up even sooner.
- **Root cause**: the stamp plan is S1 (1 core; dev runs P1v3); the BFF's cold start is ~220 s with a 76 s silent gap after `ComposeIdentityKeyHealthCheck` DEGRADED.
- **Fix applied**: yes-here-in-this-run — `WEBSITES_CONTAINER_START_TIME_LIMIT: '600'` in `customer.bicep` (and live on `sprk-acme-prod-api`); `BffDeployOptions.HealthProbeMaxRetries` 24 → 120; guard `T186_L48_H9HealthProbeWindow_CoversTheStampContainerStartLimit`. Open owner decision (cost): whether the stamp plan should be larger than S1 — not changed; the cold start itself is filed for follow-up.
- **Landing spot**: `customer.bicep`, `BffDeployOptions` (done); S1 plan size is an owner decision.
- **Blocks future runs**: no
- **Punch-list class**: A (plan size: C)

### Lesson L49 — ComposeIdentityKeyHealthCheck DEGRADED on the ACME BFF

- **Symptom**: `ComposeIdentityKeyHealthCheck` reports DEGRADED (`sprk_graphitemid_uk` alternate key not readable); the BFF starts anyway.
- **Root cause**: not established — whether the managed SpaarkeMaster import leaves the key unbuilt or the check reads it wrongly.
- **Fix applied**: no-deferred — open in run log: investigate after H9 (H13 acceptance still passed); current-task lists it under "Report to the owner". Remedy script `scripts/Repair-ComposeIdentityKey.ps1` exists.
- **Landing spot**: ACME Dataverse (SpaarkeMaster 1.2.3.1 import) / BFF health check.
- **Blocks future runs**: no (inferred; BFF runs degraded, not unhealthy)
- **Punch-list class**: B (inferred)

### Lesson L50 — Two BFF settings only appsettings.template.json supplies never reached the stamp

- **Symptom**: the staging BFF answered HTTP 500 to everything: `IDW10106: The 'Instance' option must be provided` and `CosmosPersistence:DatabaseName is not configured`.
- **Root cause**: the BFF artifact ships no `appsettings.json` (`appsettings.template.json` is `CopyToPublishDirectory=Never`); a code-mapper sweep found these two as the only blocking keys absent on the stamp.
- **Fix applied**: yes-here-in-this-run — `scripts/canonical-secret-catalog/manifest.yaml` (H4b `per_env_settings`): `AzureAd__Instance` and `CosmosPersistence__DatabaseName` added as literals, catalog regenerated; guard `T186_L50_…`; set live on `sprk-acme-prod-api`. Feature-only gaps (CIAM, AgentToken, ExternalAccess portal URL) are listed for the operator docs.
- **Landing spot**: canonical secret catalog manifest (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L51 — Dataverse never added the B2B guests: no licence

- **Symptom**: `GET systemusers(azureactivedirectoryobjectid=…)` stayed 404 (`0x80060891`) after both guests redeemed; H11 failed `userprov-dataverse-user-failed` (13:10:39 and 13:13:07 UTC).
- **Root cause**: the design left guests unlicensed and paid by pay-as-you-go, but policy `SpaarkeAcmePayG` had `AppPass` (Power Apps) off and the PRQ-C-11 recipe named the meter without checking it; H11's Dataverse read waited only 60 s.
- **Fix applied**: yes-here-in-this-run — owner decision 2026-10-16: guests get Microsoft Power Apps Plan 2 Trial (`POWERAPPS_VIRAL`). `H11UserProvisioningHandler`, `GraphRestUserProvisioner`, `DataverseWebApiGuestUserWriter`, `H11UserProvisioningOptions`: H11 sets usage location and assigns the SKU (`GuestLicenseSkuId`), Dataverse read waits 30 × 10 s; guide §H11 and PRQ-C-11 rewritten. Proven live 13:17:50 UTC.
- **Landing spot**: handler code, guide §H11, PRQ-C-11 (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L52 — B2B invitation email may never arrive on a demo tenant

- **Symptom**: neither guest received an invitation email (redeemed 13:00 / 13:05 UTC by signing in).
- **Root cause**: H11 sends via Microsoft's invitation service to each guest's own address; `*.onmicrosoft.com` users without an Exchange Online mailbox receive nothing. Redeeming needs no email: sign in at `https://myapps.microsoft.com/?tenantid=<Spaarke tenant>`, accept, then advance the gate.
- **Fix applied**: yes-here-in-this-run — documented 2026-10-16 in guide §H11 step 3.
- **Landing spot**: `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §H11 step 3 (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L53 — H12a's seed writer could never seed a new stamp

- **Symptom**: `GET /sprk_analysisactiontype?…` → 404; H12a quarantined `seed-manifest-invocation-failed` with 0 rows written.
- **Root cause**: two porting errors from `Deploy-*.ps1`: `type-lookups.json` is keyed by table logical name but the C# port used it as the URL segment (and the test encoded the same wrong names); `knowledge.json` / `skills.json` bind type GUIDs of the SOURCE environment.
- **Fix applied**: yes-here-in-this-run — `DataverseWebApiSeedWriter`: logical → entity-set map; knowledge/skills resolve each type id by name in the target environment; test `T186_L53_KnowledgeAndSkillBinds_…`. H12a Succeeded 13:41:00 UTC.
- **Landing spot**: handler code (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L54 — No step created the record-type registry rows

- **Symptom**: H12b's field-mapping seeder failed `no sprk_recordtype_ref row exists for logical name 'sprk_matter'` (`appconfig-seeder-failed`).
- **Root cause**: `sprk_recordtype_ref` is platform reference data but only the table ships in SpaarkeMaster; in dev its 14 rows were added by hand; ACME's table had 0 rows.
- **Fix applied**: yes-here-in-this-run — `AppConfigSeedModule` (H12b): new first scope `record-type` (`DataverseWebApiRecordTypeSeeder`) creates the 14 platform record types if missing, never rewriting existing rows; tests incl. registration order. Proven live 14:48 UTC.
- **Landing spot**: handler code (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L55 — A handler dispatched alongside a quarantining one was silently lost

- **Symptom**: H11 dispatched H12a and H12b together at 13:17:50; H12a quarantined the run, H12b's envelope was completed-without-invoke, and after clear-quarantine + resume the run sat Running for 37 minutes until H12b ran at 14:17:52.
- **Root cause**: resume re-dispatched only the current phase; the reconciler re-sent H12b's ORIGINAL attempt (884 sends, same MessageId), which the queue's 1-hour duplicate detection dropped. Same mechanism as L18 and L41.
- **Fix applied**: yes-here-in-this-run — `RunControlService.ResumeAsync`: the first resume after clear-quarantine also re-dispatches every DAG-ready handler with a fresh attempt (`Quarantine.ResumedAt`); later and plain-Failed resumes do not; tests incl. 2 caught mutants.
- **Landing spot**: `RunControlService` (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L56 — H12a reports success but seeds only 4 of 12 AI artifacts

- **Symptom**: ACME has no AI actions, tools, playbooks or JSON schemas (dev: 34 playbooks / 93 nodes / 80 actions / 51 tools / 40 consumers); `input-schemas`, `output-schemas`, `tools-r7`, `actions-r7`, `action-outputschema-patches`, `playbooks-mvp`, `playbook-consumers` stay PENDING.
- **Root cause**: the writer header lists them as a "follow-on" tracked nowhere, and the repo is not a usable source (17 of 80 actions, 6 of 34 playbooks, 0 of 93 nodes); the seed must also preserve dev primary keys.
- **Fix applied**: no-deferred — open in run log; owner decision pending (does not block the run). Recommended: export the shipped set from dev with ids preserved and port one PK-preserving writer into H12a, ordered tools → actions → playbooks → N:N → nodes (after H12c) → node N:N → canvas → consumers.
- **Landing spot**: `DataverseWebApiSeedWriter` (H12a) and `seed-manifest`.
- **Blocks future runs**: conditional-on the stamp needing Spaarke AI analysis features (the row: without these they cannot run).
- **Punch-list class**: C (owner decision), then A

### Lesson L57 — Field-mapping rule writer could not seed a new stamp

- **Symptom**: the rule lookup was refused 400 `Could not find a property named 'sprk_fieldmappingprofile'`; the seeder also wrote `sprk_targetfieldtype` = 1, which Dataverse refuses.
- **Root cause**: a lookup is filtered by `_sprk_fieldmappingprofile_value`; `sprk_targetfieldtype` is a different option set from `sprk_sourcefieldtype` (Lookup = `100000001`) and the test asserted `1`.
- **Fix applied**: yes-here-in-this-run — `DataverseWebApiFieldMappingSeeder`: filter `_sprk_fieldmappingprofile_value eq`, `TargetFieldTypeLookup = 100000001`; a sweep found no other lookup filtered by logical name. Proven live: H12b Succeeded 14:58 UTC.
- **Landing spot**: handler code (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L58 — Worker Exchange sidecar ran an image older than the code it serves

- **Symptom**: H14m failed `h14m-mailbox-ensure-failed`: sidecar answered `Unknown route: POST /ensure-customer-mailbox` (404).
- **Root cause**: `platform-controlplane-dev.bicepparam` pinned `provisioning-sidecar:latest`, but CI pushes only date and SHA tags, so `latest` stayed the 2026-10-04 manual first push.
- **Fix applied**: yes-here-in-this-run — Worker sitecontainer set to `:2026.10.15-31` via ARM (the `az webapp sitecontainers update` crashes — an az-cli defect), `Verify-Sidecar-Live.ps1` PASS; the bicepparam pins a CI tag and guard `SidecarImagePinTests` fails any non-CI tag. The pin was later rolled to `2026.10.20-40`.
- **Landing spot**: `platform-controlplane-dev.bicepparam` (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L59 — H14m refused ACME's scope group because Exchange appended a timestamp to its Name

- **Symptom**: H14m quarantined (`h14m-mailbox-drift`, nothing created): group is named `Spaarke-AppAccess-acme20261014194847`, expected `Spaarke-AppAccess-acme`.
- **Root cause**: Exchange built the Name from the alias plus the creation time; the sidecar compared Name exactly.
- **Fix applied**: yes-here-in-this-run — sidecar `SidecarCore.psm1`: `Test-ScopeGroupName` accepts the exact name or the name + exactly 14 digits (34 Pester cases); PR #2026 (`19d068ece`), image `2026.10.16-34`. Proven live 20:42:27 UTC. Guide §H14m listed as a landing spot in the row.
- **Landing spot**: sidecar `SidecarCore.psm1` (`Get-CustomerMailboxFacts`); guide §H14m.
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L60 — H14m PRQ-E-16 probe refused every ensure

- **Symptom**: H14m failed "Spaarke Exchange Admin cannot run System.Object[] — prerequisite PRQ-E-16 (owner decision D32) is not applied", even with PRQ-E-16 applied.
- **Root cause**: `Test-CustomerMailboxWritePermission` ended `return , $missing`, the caller wrapped it with `@(...)`, so the count was always 1; every H14m test mocked the probe.
- **Fix applied**: yes-here-in-this-run — sidecar `SidecarCore.psm1`: the probe writes missing names to the pipeline; H14m tests no longer mock it; new must-fire tests (a `return , @()` mutant fails 7 tests). PR #2041 (`c54511cb8`), image `2026.10.16-36`. Proven live 21:19:14 UTC.
- **Landing spot**: sidecar `SidecarCore.psm1` (done). Local gotcha: use Pester 5.7.1 on this Windows machine (Pester 6.2.0 fails at import).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L61 — Exchange failure in the sidecar named neither the cmdlet nor the cause

- **Symptom**: H14m failed twice (21:19:14, 21:20:13 UTC) with Exchange's generic "A server side error has occurred … try again after some time", repeatable within a second.
- **Root cause**: every sidecar route's catch returned only `$_.Exception.Message`.
- **Fix applied**: yes-here-in-this-run — sidecar `Listener.ps1`, `SidecarCore.psm1` (`Format-ExchangeError`): every route reports `<cmdlet>: <message> | id | category | at: <call chain> | inner …` in one bounded line; 39 Pester cases; PR #2042 (`292c33f46`), image `2026.10.16-38`. Proven live 22:14:18 UTC (named `New-Mailbox`).
- **Landing spot**: sidecar code (done).
- **Blocks future runs**: no
- **Punch-list class**: A

### Lesson L62 — Exchange refused H14m's two recipient writes (root cause: role entries dropped common parameters)

- **Symptom**: `New-Mailbox -Shared` and `Add-DistributionGroupMember` were refused for `Spaarke Exchange Admin` with `CmdletAccessDeniedException` (403; EXO 3.5.1 shows "server side error"); a role group (option 1, 2026-10-19) and the `OverrideEnforceExoAppRbacPermissions` override (option 2, 2026-10-20) changed nothing.
- **Root cause**: the first reading ("a Microsoft enforcement change") was wrong. The D32 role entries (`Set-ManagementRoleEntry … -Parameters <5 names>`) REPLACE the list and drop the PowerShell common parameters, which Exchange checks like any parameter; the sidecar has called `New-Mailbox … -ErrorAction Stop` since T263, whereas the 10-13 M4 check omitted `-ErrorAction`. `-WhatIf` probes were refused for the same reason. The join's parent role (Security Group Creation and Membership) cannot take the common parameters.
- **Fix applied**: yes-here-in-this-run — owner added common parameters to `Spaarke Shared Mailbox Create\New-Mailbox`; sidecar fix `4c724b807`, PR #2136 (`05bcdeee6`, image `2026.10.20-40`: the join runs without common parameters and reads the error stream; PRQ-E-16 now also runs both writes on `sprk-authz-probe@authz-probe.invalid`); role group and override undone. Applied: guide §4.2.1 step 5, T263 note §5 (M4 corrected, M8 added), PRQ-E-16, `.claude/FAILURE-MODES.md` G-23, project CLAUDE.md §6, skill Troubleshooting. H14m Succeeded 16:23:45 UTC.
- **Landing spot**: guide §4.2.1 step 5; `prereqs.yaml` PRQ-E-16; `FAILURE-MODES.md` G-23; sidecar (all done). The role-entry change took ~30 min–2 h to propagate.
- **Blocks future runs**: no
- **Punch-list class**: A (Exchange tenant configuration is owner-run: C)

## New prereqs to codify

Only items the run log itself proposes, or that open lessons clearly imply:

| proposed_id | name | scope | tenancyModel | check_recipe | consequence-if-absent |
|---|---|---|---|---|---|
| PRQ-E-16 (existing; amended 2026-10-20) | Narrowed D32 Exchange role entries carry the common parameters where the parent role allows, and both recipient writes are authorized | Spaarke Exchange tenant (platform) | models using a customer mailbox (inferred) | Already updated per the log: run both writes as the app on `sprk-authz-probe@authz-probe.invalid` with an unaccepted domain; 400/404 = authorized, 403 = denied (L62) | H14m fails with Exchange's generic "server side error" |
| — (to be assigned) | Re-run `Grant-ControlPlaneIdentity.ps1` after any change to `ControlPlaneGraphAppRoles.cs` | Spaarke control plane (dev/prod) | all | L2 Worker identity Graph role parity (T3 parity 7/7 on this run) | L2 Worker lacks Graph roles; Step 0.5 fails or handlers fail late (L13) |
| — (to be assigned) | Target Dataverse environment is new and never held Spaarke solutions | customer environment | Model 1 and any managed stamp (inferred) | Inferred: list solutions in the environment and expect no `sprk_*` / SpaarkeMaster | H6 managed import refused (L01) |

## New patterns to add

- `replication-retry.md` — one bounded retry (404 / "does not reference a valid …" / transient 429/5xx, then Resumable `Unverified`, never Quarantine) for any call on an object another service created seconds earlier (Graph, Key Vault RBAC, SPE, Dataverse sync); covers L24, L26, L27, L29, L31–L35.
- `live-shape-tests.md` — names and request shapes for external systems (property and bind names, option values, `$expand`, ARM bodies) are copied from live metadata and pinned by a test that fails on the wrong form, never fake-accepts-anything; covers L09, L16, L20, L38, L40, L43, L53, L57, L60.
- `live-probe-unacceptable-value.md` — probe a narrowed permission with the real parameter set and a value that fails validation (an unaccepted domain), never `-WhatIf`; 400/404 means authorized, 403 means denied, and a raw call is the diagnostic when a module hides the 403 (L62).

## Recommendations for next run

- Carry lessons L1–L62 into `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` and `PROVISIONING-PREREQUISITES.md` (current-task "After T186 completes"); the open doc items are L01, L02, L06, L07, L08, L11 and L22 (guide) and L05 (agent-prompt note).
- Fix L15 (skill Step 1f: environment type from the intake, default Production) and L30 (resolve a handler's own failure gates on success).
- Owner decision L56: export the shipped AI set from dev with ids preserved and port a PK-preserving writer into H12a, then re-seed ACME (does not block a run, but a stamp has no AI analysis features until then).
- Investigate L49 (`ComposeIdentityKeyHealthCheck` DEGRADED; `scripts/Repair-ComposeIdentityKey.ps1`) and report the L48 S1-plan cost question and the ~220 s cold start to the owner.
- Owner OK needed for #2027 org auditing on spaarkedev1 and ACME (H6 ran under the old contract); tell uac-r2 when dev is set.
- When the work branch merges to master the PR body must say `Closes #2034`, `Closes #2040`, `Closes #2027`; per current-task the #2034 fix (`246f6f166`) was on the branch only, so redeploy the control plane before the next customer run (re-check that it is still pending).
- Next-step items for this stamp: Step 6e secure-record verification before notifying the customer admin, Step 6f per-customer Copilot agent, 24 h App Insights monitoring, and the T255 identity-link write enablement after one report-only cycle (deployment guide §6.5.2).
- Remove the old broad L2 Graph roles; close the open live proofs listed in current-task (H14m route-level M4, H13 census T260, PRQ-C-14).

## Cross-run pattern (if this lesson recurs)

This is the first live end-to-end run, so every lesson above is first observed in run `acme-b6941254-ac1b-42a6-8b12-dec7587090fc` with an occurrence count of 1 (to be updated by `/audit-provisioning-lessons`). Several lessons already share a shape within this run (replication-retry L24/L26/L27/L32/L33/L35; fake-accepts-anything tests L09/L16/L20/L38/L53/L57; duplicate-MessageId stalls L18/L37/L41/L55), which makes them the first promotion candidates if they recur.

- **First observed in run**: `acme-b6941254-ac1b-42a6-8b12-dec7587090fc` (this run)
- **Occurrence count across runs**: 1 for each lesson
- **Recommended promotion**: if count ≥ 3 → promote to `PROVISIONING-PREREQUISITES.md` OR `.claude/patterns/provisioning/` (owner: platform-ops)

## Sign-off

- Author: `ralph.schroeder@spaarke.com` @ `2026-10-20` (drafted by Claude for the operator)
- Reviewer (if separate): pending
- Committed to git: set at commit (post-Step-7)
- INDEX.md `lessons-count` field updated to reflect this file's entry count: 62 (`### Lesson` headings) — to be applied by the main session
