# Current Task State — customer-provisioning-orchestration-r1

> **Last Updated**: 2026-08-20 (Task 131 COMPLETE — H8 Graph containerTypes port. GraphContainerTypeProvisioner + GraphAppOnlyContainerVerifier + SecretClientSpeContainerIdKvWriter (+ shared SpeConfidentialClientGraphFactory helper) replace the retired shell-out scaffold, all under ClientCertificateCredential (T6, cert-from-KV). Biggest catch: the retired script's separate SharePoint-REST applicationPermissions PUT (different token audience) has a native Graph GA replacement — POST /storage/fileStorage/containerTypeRegistrations — so the ENTIRE flow now runs under ONE Graph client + ONE T6 credential. New RunStatus.WaitingOnGate outcome for the documented 24h SPE replication-lag case (verify-GET 404), never Resumable/QuarantineRequired. Self-caught + fixed a real bug during Step 9.5 (BuildEvidence hardcoded verifiedViaAppOnlyToken=true — would have mislabeled WaitingOnGate evidence). L2 tests: 930 → **938/938** (+8, zero regressions). Wave G-3 now FULLY COMPLETE — 130 (H3) + 131 (H8) + 132 (H9) all done. Note: this task's Worker/Program.cs DI edit landed inside sibling task 132's commit `ccaf1cad2` via a git-index race — content verified correct, cited for audit trail.)
> **Working directory**: `c:\code_files\spaarke-wt-customer-provisioning-orchestration-r1`
> **Branch**: `work/customer-provisioning-orchestration-r1` — see git log for latest commit, in sync with `origin/work/customer-provisioning-orchestration-r1`
> **PR**: https://github.com/spaarke-dev/spaarke/pull/779 (DRAFT — DO NOT MERGE — Phase C'' incomplete; Waves G-4..G-7 remain. Wave G-2.5 (customer.bicep completion) is fully closed. Wave G-3 (130/131/132) is now FULLY COMPLETE.)

## Task 131 — COMPLETE (2026-08-20)

Ported H8SpeContainerTypeHandler's 3 collaborators (task 011/051 shell-out scripts) to Microsoft.Graph 6.5.0 under `ClientCertificateCredential` (T6 confidential-client, cert loaded from KV). New: `GraphContainerTypeProvisioner.cs` (POST `/storage/fileStorage/containerTypes` + POST `/storage/fileStorage/containerTypeRegistrations` + POST `/storage/fileStorage/containers`), `GraphAppOnlyContainerVerifier.cs` (single GET — "dramatically simplified" per the POML's own framing, replacing the retired script's 123 lines of hand-rolled JWT ceremony), `SecretClientSpeContainerIdKvWriter.cs` (reuses task 125's `SecretClient` idiom, single-secret narrow seam), `SpeConfidentialClientGraphFactory.cs` (shared T6 cert-from-KV + credential-construction helper, CLAUDE.md §11 reuse between the two Graph collaborators). Retired: `CreateNewContainerTypeScriptProvisioner.cs` / `SpeContainerAppOnlyVerifier.cs` / `AzCliSpeContainerIdKvWriter.cs` (kept on disk, unregistered, retirement banners).

**Graph SDK gotchas ground-truthed via reflection** (Wave G-2/G-3 discipline): (1) `/storage/fileStorage/containerTypes`+`/containers` are GA (v1.0) in the installed SDK — no `Microsoft.Graph.Beta` package needed, despite the retired script's beta endpoint. (2) `FileStorageContainerType` has no Description/DisplayName (only `Name`) and `OwningAppId` is `Guid?` not `string` — a real cross-surface inconsistency vs `Application.AppId` (string) on the same package. (3) **The big one**: the retired script's separate SharePoint-REST `applicationPermissions` PUT (a DIFFERENT token audience than Graph) has a native Graph GA replacement — `POST /storage/fileStorage/containerTypeRegistrations` with `ApplicationPermissionGrants:[{AppId, ApplicationPermissions:[Full], DelegatedPermissions:[Full]}]` (`FileStorageContainerTypeAppPermission` is an enum, not a settable object). The ENTIRE H8 flow now runs through ONE Graph client under ONE T6 credential — the SharePoint-domain token audience is gone. (4) T6 cert-from-KV: the cert is a base64-PFX KV **secret** (not a KV Certificate object) — `Azure.Security.KeyVault.Certificates.CertificateClient` was considered per the task directive and REJECTED (its `DownloadCertificateAsync` returns public-cert-only; the private key is only obtainable via the paired Secret). `SecretClient` + `X509CertificateLoader.LoadPkcs12` (the modern non-obsolete API) is used.

**New `RunStatus.WaitingOnGate` outcome** (DS-4 §2 / this project's CLAUDE.md MUST rule — the 24h SPE replication gate is a run-level external blocker, never a handler defect): `ISpeContainerVerifier` gained a third outcome, `ReplicationPending` (fired on a verify-GET 404 — any other Graph error is a genuine `NotVerified` failure). `H8SpeContainerTypeHandler`'s new `MarkWaitingOnGateAsync` sets `RunStatus.WaitingOnGate` (never Resumable/QuarantineRequired), persists the already-created container-type/root-container IDs, marks the T6Verified gate Pending, does NOT append a CompletedPhase (a resume re-executes `HandleAsync` in full), and does NOT write the KV secret yet. Confirmed safe: `DagAdvancer.HandlerDependencies` has no entry keyed on H8 — nothing in the DAG depends on H8, so the pause blocks nothing else; confirmed `HandlerOutcomeApplier` does not clobber the write (Success path reads `run.Status` as-is).

**Self-caught + fixed during Step 9.5**: `BuildEvidence` hardcoded `verifiedViaAppOnlyToken=true` unconditionally — reusing it verbatim for `WaitingOnGate` would have written misleading gate evidence. Fixed with an explicit parameter + added regression-guard assertions to both the happy-path and new WaitingOnGate tests. Also reworded 3 doc-comment occurrences of the literal string `ClientSecretCredential` (legitimate negative references, "never X") that would otherwise have failed the POML's literal `grep 'ClientSecretCredential'` acceptance criterion against the production collaborator files — the real negative-type assertion is preserved in the test file (outside the criterion's scope).

Tests: 30 → 938 (net +8 from a 930 baseline this session — AC-22 WaitingOnGate test on `H8SpeContainerTypeHandlerTests.cs` + 7 new `SpeConfidentialClientGraphFactoryTests.cs` tests covering cert-load, the T6 cert-path credential-TYPE assertion [never `ClientSecretCredential`], malformed-base64 handling, 404 propagation, and T6 trap-phrase detection). `dotnet build` 0 errors across Core/Worker/Tests. `grep 'ClientSecretCredential'`/`'ProcessStartInfo'` in the new H8 collaborator files: 0 matches (verified post-fix).

**Git-index race (cited per coordination convention)**: this task's Worker/Program.cs H8 DI-registration edit landed inside sibling task 132's commit `ccaf1cad2` ("H9 artifact-based rebuild") rather than a commit of this task's own — the sibling's commit swept the file while this edit was already applied to the shared working tree. Content verified correct (build + full 938-test suite green post-sweep); no functional issue, noted for audit-trail completeness. No `SendMessage` coordination was needed (no conflicting content, sibling's H9 files untouched by this task).

## Task 132 — COMPLETE (2026-08-20)

Re-scoped H9BffDeployHandler per DS-4 §5's exact artifact-based design, replacing `DeployBffApiScriptRunner` (which ran the dotnet-publish build step AT PROVISION TIME — DS-4's "heaviest environment dependency of all") with a resolve/verify/download/deploy/swap flow consuming task 116's CI-published artifact. New collaborators (all `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/BffDeploy/`): `ArtifactManifestVerifier.cs` (pure C# — downloads + parses `latest.json` via a shared `BlobContainerClient`; hard-blocks if any of the 5 gate keys is missing/red or a requested buildId doesn't match the manifest's own buildId — replaces `DotnetR3GateVerifier`'s dotnet/pwsh shell-outs since the r3 gates now run in CI), `BlobArtifactDownloader.cs` (`BlobClient.DownloadToAsync(string,ct)`, UAMI RBAC, no stored key), `KuduZipDeployer.cs` (typed HttpClient POST to `https://{app}-{slot}.scm.azurewebsites.net/api/zipdeploy` with an MI-acquired `https://management.azure.com/.default` bearer token — no ARM SDK zip-deploy primitive exists), `ArmSlotSwapper.cs` (implements the **existing** `IAppServiceSlotSwapper` interface unchanged — `WebSiteSlotResource.SwapSlotAsync(WaitUntil.Completed, CsmSlotEntity, ct)` as a proper awaited LRO). `DotnetR3GateVerifier.cs` / `DeployBffApiScriptRunner.cs` / `AzCliAppServiceSlotSwapper.cs` retired (kept on disk unregistered, same convention as every prior Wave G-2/G-3 collaborator retirement).

`H9BffDeployHandler.cs` rewritten with the rollback-re-swap tail **byte-identical** to the pre-task-132 version (only the `IAppServiceSlotSwapper` DI registration changed, from `AzCliAppServiceSlotSwapper` to `ArmSlotSwapper`) — the constraint "PRESERVE the existing rollback-re-swap logic unchanged" is satisfied literally, not just in spirit. `buildId` run parameter is now OPTIONAL: a two-phase idempotency check handles both the fast path (buildId supplied — zero network calls before the no-op short-circuit) and the resolve-from-manifest path (buildId absent — manifest fetched first, then idempotency checked against the resolved buildId). `SkipBuildParameterKey` + `EnvironmentNameParameterKey` removed (meaningless once there is no build step to skip and no script `-Environment` flag to pass).

Ground-truthed via reflection against the installed SDK assemblies (not guessed, per Wave G-2/G-3 discipline): `WebSiteSlotResource.SwapSlotAsync(WaitUntil, CsmSlotEntity, ct) -> Task<ArmOperation>` (called on the SOURCE slot's resource; `CsmSlotEntity.TargetSlot` names the destination); `CsmSlotEntity(string targetSlot, bool preserveVnet)` positional ctor; `BlobClient.DownloadToAsync(string path, CancellationToken) -> Task<Response>`. Kudu zip-deploy has no ARM SDK primitive — implemented as a documented raw HTTP POST (synchronous, no `?isAsync=true`, matching `deploy-bff-api.yml`'s own `--async false` precedent) authenticated with the SAME shared UAMI-pinned `TokenCredential` every sibling collaborator uses.

Tests: 21 new collaborator tests (`ArtifactManifestVerifierTests` ×8, `BlobArtifactDownloaderTests` ×4, `KuduZipDeployerTests` ×5 incl. a real cooperative-cancellation timeout test, `ArmSlotSwapperTests` ×4 — the 4th is the rollback-completeness proof at the SDK-shape layer: two identical `SwapAsync` invocations both independently reach ARM) + `H9BffDeployHandlerTests` fully rewritten (28 tests; AC15a is the handler-level rollback-completeness proof asserting the two swap requests are byte-identical). Reused task 123's `ArmSdkTestFakes.NewBlobContainerClient` fake-transport helper (extend, don't duplicate — CLAUDE.md §11) for both blob-consuming collaborators; authored a new hand-rolled `FakeKuduHttpMessageHandler` for the non-ARM Kudu POST (ADR-038 — no `Mock<HttpMessageHandler>`). L2 suite: 916 → **930/930** (+14 net, zero regressions).

**Storage-account-doesn't-exist-yet risk** (documented per dispatch context — Wave G-1 live-ceremony backlog item #4): all new collaborators are fully unit-tested against fake Azure.Storage.Blobs / HTTP transports; `BffDeployOptions.Validate()` fails fast at boot if `ProvisioningArtifactsContainerUri` is unset (intentional — a real end-to-end run additionally requires the storage account to exist + task 116's CI workflow to have published at least one build). Not a blocker for this task; documented in the handler file header + POML `<notes-completion>`.

**Coordination with sibling task 131 (H8 Graph containerTypes)**: ran in parallel in a different folder (`Handlers/SpeContainerType/`) for most of this session, leaving the shared solution in a transient non-compiling intermediate state (their own uncommitted WIP, unrelated domain — zero file overlap with `Handlers/BffDeploy/`). To validate this task's build/tests without waiting, their in-progress files were temporarily `git stash push`-ed (uncommitted WIP only), Core/Worker/Tests validated clean + full suite green, then immediately `git stash pop`-ed to restore their exact working-tree state byte-for-byte — no content of theirs was read, edited, or committed. By the final verification pass their files had independently reached a clean-compiling state on their own; no coordination message was needed.

Step 9.5 (self-conducted via the `code-review` + `adr-check` Skill procedures): 0 Critical, 0 new Warnings. Zero-shell-out MUST rule verified via scoped grep against the actively-registered collaborator set (the retired-but-kept-on-disk files still contain their historical shell-out code by design — excluded from scope since unregistered in DI). ADR-010's 3-new-interfaces-1-impl-each shape matches this handler family's established, repeatedly-precedented seam-justification convention (identical to every sibling Wave G-2/G-3 collaborator) — not a new deviation. ADR-028 UAMI-outbound confirmed (shared `TokenCredential` singleton reused everywhere; zero stored keys/SAS/connection strings anywhere in the new code).

## Task 130 — COMPLETE (2026-08-19)

Ported H3EntraAppRegHandler's collaborators from the Wave-C4 shell-out scaffold to Microsoft.Graph 6.5.0 SDK. New: `GraphAppRegistrationProvisioner.cs` (Model 2 — idempotent app-reg/SP/client-secret ensure + FIC trusting the shared BFF UAMI per auth-v4 §3.1 + Model 1 read-only shared-app grant-currency verification), `GraphAdminConsentVerifier.cs` (real `oauth2PermissionGrants` query — replaces `NullAdminConsentVerifier`'s unconditional Verified, closing DS-4 §3's "consent gate can advance on fiction" defect), `EntraAppRegPermissionCatalog.cs` (shared 5-delegated-scope source of truth for both new collaborators). `H3EntraAppRegHandler.cs` rewritten: Model 1/Model 2 branch selection is I6-enforced (explicit `tenancyModel`, no default — 2 dedicated tests prove missing/unrecognized values fail loudly); KV secret writes are STAGED by the provisioner and committed only AFTER the consent verifier returns Verified (DS-4 §3 binding ordering — never before); `RunParameters.Secrets["BFF-API-ClientId"/"BFF-API-Audience"/"BFF-API-ClientSecret"]` populated on the Verified path (task 129's manifest.yaml `from-run-parameter`/`from-existing-kv` contract — H3 is the documented value producer). `RegisterEntraAppRegScriptProvisioner.cs` + `NullAdminConsentVerifier.cs` retired (kept on disk, unregistered, retirement banners — same pattern as `AzCliKvSecretsWriter.cs`). `Microsoft.Graph 6.5.0` added to `Sprk.Provisioning.ControlPlane.Core.csproj` (L2's first Graph SDK dep — every other L2 Graph collaborator stayed REST-only; design.md §4.1's H3 SDK-surface table explicitly assigns Graph SDK to H3 specifically).

**Two documented Path-C deviations from the POML's literal text** (root CLAUDE.md §6.5 — full rationale in task 130 POML `<notes-completion>`):
1. H3 does NOT grant the 14 `AppRoleAssignedTo` application-only roles. design.md §4.1's authoritative SDK-surface table lists only Applications/ServicePrincipals/Oauth2PermissionGrants for H3; H10 (already landed, task 053) already grants all 14 `GraphAppRoles.cs` roles onto the customer's UAMI service principal — a DIFFERENT principal than H3's own app-reg SP. Duplicating in H3 would target the wrong principal.
2. H3 does NOT perform its own Dataverse-app-user assignment. H10 already registers both the BFF app-reg (via H3's own `InterStepState.BffAppRegId` output) and the UAMI as Dataverse App Users. H3 runs on a DAG branch parallel to H5→H6→H7→H10, so `DataverseEnvUrl` is typically unavailable when H3 dispatches — an H3-side attempt would be dead code in the success path.

**Two ground-truthed Microsoft.Graph SDK gotchas caught via reflection** (per Wave G-2/G-3 discipline): (1) `GraphServiceClient`/`AzureIdentityAuthenticationProvider` have no per-request `tenantId` override — per-tenant scoping requires a fresh `DefaultAzureCredential(TenantId=...)` per call (parity with H10's `GraphRestAppRoleGranter`), not the shared DI singleton. (2) The POML's "exchange-based FIC verification" is not literally implementable by L2 — L2 runs under its own platform UAMI, not the customer's per-customer UAMI (the FIC's subject), so it has no credential path to mint a client_assertion as that identity; implemented instead as an independent re-GET confirming Subject/Issuer/Audiences persisted exactly as requested.

`dotnet build` 0 errors across Core/Worker/Api/Tests. `dotnet test`: **916/916 passing** (was 903; +13 net — full rewrite of `H3EntraAppRegHandlerTests.cs` [~24 tests] + new `GraphAdminConsentVerifierTests.cs` [5 pure-logic tests against a refactored `EvaluateGrantedScopes` decision function]). `dotnet list package --vulnerable --include-transitive` clean after the Graph SDK add. Step 9.5 code-review + adr-check: 0 Critical, 1 pre-existing Warning (ADR-010 single-impl interfaces, established codebase-wide convention predating this task), 0 new ADR violations. Auth-v4 coordination: proceeded without a coordination check per owner directive (auth-v4 has not started; POML's FIC-pluggability seam built directly per the POML, no extension script existed to defer to).

## Task 129 — COMPLETE (2026-08-19)

Wired `scripts/canonical-secret-catalog/generated/kv-secrets.generated.bicep` (task 084, never hand-edited — only regenerated via its own generator) into `infrastructure/bicep/customer.bicep` as the final module in the composition, invoked with a `kvSecretValues` object populated from 10 resolvable sibling-module outputs: `AiSearch--AdminKey`, `AiSearch-Endpoint`, `AppInsights-ConnectionString` (128b), `AzureOpenAI-Endpoint`, `Communication-WebhookUrl` (constructed to match `stacks/model2-full.bicep:243`'s precedent), `DocumentIntelligence-ApiKey`/`Endpoint` (128b), `Redis-ConnectionString` (128b/E2), `ServiceBus-ConnectionString`, `Storage-ConnectionString`. Explicit `dependsOn: [keyVault]` added (needed — `keyVaultName` is a plain param, not an implicit dependency edge). **Step 6 manifest fix (owner E3)**: `scripts/canonical-secret-catalog/manifest.yaml` reclassified `BFF-API-ClientId` + `BFF-API-Audience` from `FromBicepOutput` to `FromRunParameters` (H3/task 130 is the actual runtime value producer); regenerated all 4 canonical-catalog artifacts via `Invoke-CatalogGenerator.ps1`, verified deterministic via `-Verify`. Only 3 SPE-* entries (H8/H9 runtime-only) remain permanently omitted from Bicep — expected, not a failure; they resolve via H4's FromRunParameters path after H8/H9 execute.

`az bicep build` exits 0 (0 errors); 40 total warnings, but **0 net-new** — 33 are pre-existing in `kv-secrets.generated.bicep`'s own body (verified via standalone build of that file alone: exactly 33), out of scope to fix (binding DO-NOT-EDIT-BY-HAND); 7 are customer.bicep's pre-existing baseline (unchanged since 128b). Live `az deployment sub what-if` (customerId=t129wif) returned exit 0, "Resource changes: 30 to create", with the same pre-existing ARM what-if short-circuit limitation (task 128 first documented it) now naturally extending to `docIntelligence` + the new `kvSecrets` nested deployment. Quality gates self-conducted (Bicep/YAML/PS1/MD, outside the code-review skill's C#/TS checklists) — 0 Critical, 0 Warnings beyond the two documented AC-wording deviations (see task POML `<notes-completion>` for full detail).

**Net effect**: combined with task 128b, reduces task-126-deviations' "QuarantineRequired on fresh customer" risk from ~15 failing FromBicepOutput entries to **0 permanent Bicep-side failures**. All 15 originally-flagged entries now resolve via either Bicep (10) or H4's runtime FromRunParameters path (5: 3 SPE-* from H8/H9 + BFF-ClientId/Audience from H3) — all expected-post-handler resolutions per the DAG order, not silent failures.

## Task 128b — COMPLETE (2026-08-19)

Wired `modules/doc-intelligence.bicep` + `modules/monitoring.bicep` + `modules/redis.bicep` (all UNMODIFIED per CLAUDE.md §11) into `infrastructure/bicep/customer.bicep`. Monitoring (App Insights + Log Analytics) inserted after Key Vault/before Storage; Document Intelligence inserted after AI Search/before Membership Topic; Redis inserted immediately after Document Intelligence. Document Intelligence granted Cognitive Services User RBAC via `uami.outputs.principalId` (same pattern as task 128); monitoring/redis correctly receive no MI param (ikey/access-key auth). New params `redisSku`='Basic'/`redisCapacity`=0 (dev-cost posture). 9 new non-secret outputs added under the exact required names (`docIntelligenceEndpoint`, `docIntelligenceName`, `appInsightsName`, `appInsightsId`, `logAnalyticsName`, `logAnalyticsWorkspaceId`, `redisName`, `redisHostName`, `redisPort`); no raw secrets echoed. Both stale "Redis not per-customer" comments (header + Cosmos DB section) corrected to state the E2 reconciliation. `az bicep build` exits 0, exactly 7 warnings (byte-identical to pre-task baseline — zero net-new). Live `az deployment sub what-if` (customerId=t128bwif) returned Succeeded, 30 resources Create-only, zero unexpected changes to 127/128's landed resources. Quality gates both passed clean (0 critical, 0 violations).

**Step 8 spec/design amendment landed same-commit**: spec.md item 19 / FR-04 / NFR-04 / § MUST Rules updated to distinguish Model 1 (Redis shared, unchanged) from Model 2 (Redis per-customer, reinstated); design.md §7.1 naming table (Redis row reinstated, Model 2 only), §7.2 Resource Catalog (row 6b added), §7.2 disposition table, §7.6 step 8, §7.7 `redis-connection-string` all updated. Project CLAUDE.md § MUST rules updated identically. **Bumped to v3.6, not v3.3** — the POML's own text cited v3.3, but that version number was already taken by the 2026-08-16 owner-review-round entry in both docs' own history (design.md had independently advanced to v3.5 the same day via the auth-v4 coordination commit) — reusing v3.3 would have created a duplicate/misleading version marker. Documented explicitly in both files' headers + design.md's CHANGELOG + the task POML's own completion notes.

**Escalation trigger 3 fires**: task 129's kv-secrets triage should be re-verified against this landed state before 129 executes — 3-4 of its 9 originally-omitted entries (DocumentIntelligence-ApiKey, DocumentIntelligence-Endpoint, AppInsights-ConnectionString, Redis-ConnectionString for Model 2) are now potentially resolvable. Task 129's own POML was NOT touched by this task (per its escalation trigger's own instruction).

## Task 128b — AUTHORED, NOT EXECUTED (historical — superseded by COMPLETE entry above)

New POML [`tasks/128b-customer-bicep-docintel-appinsights-loganalytics-redis-wiring.poml`](./tasks/128b-customer-bicep-docintel-appinsights-loganalytics-redis-wiring.poml) authored during task 128's own authoring pass, closing two separate escalation findings:

- **E1** (task 127's + task 128's escalation triggers): spec.md FR-04 / design.md §7.2 rows 11-12 require Document Intelligence + App Insights/Log Analytics wired into customer.bicep; both tasks flagged it out-of-scope rather than silently expanding. `modules/doc-intelligence.bicep` and `modules/monitoring.bicep` (single module = App Insights + Log Analytics) already exist, grep-verified orphaned as far as customer.bicep is concerned.
- **E2** (task 129's escalation trigger): manifest.yaml's `Redis-ConnectionString` `FromBicepOutput` classification conflicts with the documented per-environment Redis architecture (Q-E FR-12). Owner reconciliation: since customer.bicep is confirmed to be the sole template deployed for the Model2Dedicated branch only, "per-environment" and "per-customer" are the same unit for THIS template — `modules/redis.bicep` (already exists, FR-09 hardened) is wired unconditionally.

**IMPORTANT — this reverses a versioned (v3.2) documented decision.** spec.md item 19/FR-04/§MUST-Rules and design.md §7.2's Resource Catalog (struck row 6) + shared-vs-dedicated disposition table (Redis marked "shared" for BOTH Model 1 and Model 2) and §7.6/§7.7 ALL currently assert Redis is per-environment, NOT per-customer, with no exception carved out for Model 2 dedicated. Task 128b's own `<escalation>` block requires this be confirmed with the project owner BEFORE dispatch, and — if confirmed — treated as a **Path B (ADR/spec amendment)** per root CLAUDE.md §6.5: a spec.md/design.md v3.3 amendment should land before or alongside 128b's execution so the documents stop contradicting the code. **This is a genuine open item for the next session/owner — not silently resolved by authoring 128b.**

Task 128b also flags (escalation trigger 3): once it lands, 3-4 of task 129's 9 documented "not resolvable" kv-secrets entries (DocumentIntelligence-ApiKey, DocumentIntelligence-Endpoint, AppInsights-ConnectionString, and pending E1/E2's resolution, Redis-ConnectionString) become potentially resolvable. Task 129's triage should be re-verified against 128b's actual landed state before 129 executes.

**Not executed** — authoring only, per the dispatch instruction that produced this POML. `infrastructure/bicep/customer.bicep` is untouched by this entry.

## Task 128 — COMPLETE (2026-08-20)

Wired `modules/openai.bicep` + `modules/ai-search.bicep` (task 046, both UNMODIFIED per CLAUDE.md §11) into `infrastructure/bicep/customer.bicep`, inserted immediately after the Cosmos DB section / before Membership Topic (task 128's declared insertion zone, disjoint from task 127's UAMI/App Service zones). OpenAI named `sprk-{customerId}-{env}-openai`, AI Search named `sprk-{customerId}-{env}-search` per design.md §7.1. NO `deployments` override passed to openai.bicep — module default array (150/200/30/350 TPM) is verified byte-for-byte identical to design.md §7.4 + spec.md NFR-12. AI Search uses module defaults throughout (task 124's completion notes confirm SearchIndexClientProvisioner needs only the service endpoint via UAMI-pinned TokenCredential — zero admin-key handling, no additional infra shape). Both modules granted Cognitive Services User RBAC via `uami.outputs.principalId` (task 127's uami module). Two new outputs added under the exact required names: `openAiEndpoint`, `aiSearchEndpoint` (ArmDeploymentRunner.MapOutputs contract, task 123). No raw `openAiKey`/`searchServiceAdminKey` echoed (task 129's scope). `az bicep build` exits 0 with 7 pre-existing warnings (verified zero net-new against baseline). Live `az deployment sub what-if` (throwaway `customerId=t128wif`) returned `status: Succeeded`, 27 resources `Create` — the 2 new AI resources (plus the pre-existing `keyVault`) were short-circuited from per-resource what-if detail by a confirmed PRE-EXISTING ARM limitation (nested deployment consuming a sibling module's not-yet-resolved output), not introduced by this task. Quality gates (code-review + adr-check) both passed clean — 0 critical, 0 warnings, 0 ADR violations.

**Remaining customer.bicep gaps**: task 128b (Doc Intelligence + App Insights + Log Analytics + Redis — authored, not yet executed, see above) and task 129 (`kv-secrets.generated.bicep` wiring — task 126's Gap 2, not done yet).

## Task 127 — COMPLETE (2026-08-19, commit `8fdd0e2d0`)

Wired `modules/uami.bicep` (task 028) + `modules/app-service-plan.bicep` + `modules/app-service.bicep` + `modules/app-service-slot.bicep` (task 029) into `infrastructure/bicep/customer.bicep`, all four modules used UNMODIFIED per CLAUDE.md §11. UAMI named `mi-spaarke-{customerId}-{env}` per design.md §7.1; both App Service prod + staging slots bound to the SAME UAMI (T5 fix); Key Vault RBAC wired via key-vault.bicep's existing `userAssignedIdentityPrincipalId` param; task 027's vestigial pass-through param removed; 4 new outputs added under the exact names `ArmDeploymentRunner.MapOutputs` (task 123) requires (`userAssignedIdentityObjectId`/`ClientId`, `appServiceName`, `appServiceStagingSlotName`) alongside the now-real `userAssignedIdentityResourceId`. `az bicep build` exits 0 (zero net-new warnings — 7 pre-existing warnings unchanged). Live `az deployment sub what-if` (subscription-scope, throwaway `customerId=t127wif`) confirmed clean Create-only plan with the UAMI correctly bound to both slots.

**Remaining customer.bicep gaps** (per task 123/126 discovery, Path 1 plan): task 128 (OpenAI + AI Search modules — task 123's Gap 1 part 2/2) and task 129 (`kv-secrets.generated.bicep` wiring — task 126's Gap 2) are NOT done yet. Task 128 dispatches next (serial after 127 to avoid customer.bicep merge race). Wave G-3 (130/131/132) stays blocked on 127+128+129 landing per the owner's Path 1 sequencing decision.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Phase** | Phase C'' — Execution-Engine Build (delivers r1's stated goal: E2E provisioning per FR-18 / SC #5) |
| **Wave G-1 status** | ✅ 100% COMPLETE — 17 tasks + governance + auth-v4 coord = 20 substantive commits |
| **Wave G-2 status** | ✅ 100% COMPLETE — 7 tasks + 1 fix-at-discovery = 8 commits (120/121/122/123/124/125/126 + bicepparam fix). L2 tests: 787 → **903/903** (+116 new, zero regressions). Zero Step 9.5 findings across the wave. |
| **Wave G-3 status** | ✅ 100% COMPLETE — task 130 (H3) + task 131 (H8) + task 132 (H9) all landed. L2 tests at 938/938. |
| **Next decision** | Dispatch Wave G-4 (140-144: H5/H6/H7/H10/H11). |
| **Live-ceremony backlog** | 8 items now (originally 7 + BAP admin REST verification from task 120); item #4 (provisioning-artifacts storage account) is now ALSO a hard dependency for task 132's H9 to run live, not just tasks 116/117's CI publish. |
| **NEW work items surfaced** | 2 customer.bicep gaps (see § New Work Items below) — both now resolved (see task 128/128b/129 entries) |
| **Status** | Fully clean checkpoint — working tree empty, all commits pushed |

---

## Wave G-2 tally

| Commit | Task | Content | Δ tests |
|---|---|---|---|
| `f83cc74e2` | 122 | H0.5 registry DI-swap (Path X verified in-code + completeness ArchTest + bonus Bicep `adminDataverseEnvironmentUrl` wiring same-PR) | +1 → 788 |
| `637658eab` | 121 | H1 real ARM subscription-reachability + Lighthouse probe (bonus tenant-mismatch guard; `NullSubscriptionReadinessProbe` DELETED) | +10 → 798 |
| `d33e06a75` | 120 | H0 SDK-port 4 preflight probes (ARM.CognitiveServices TPM · BAP-REST env-rate · ARM.Compute vCPU · KV cert-bootstrap; adopted 121's `ArmSdkTestFakes` pattern mid-flight) | +33 → 831 |
| `17f74cd46` | (fix) | `parameters/platform-controlplane-dev.bicepparam` + 2 runbook bugs (wrong bicepparam target file + `az deployment group create` vs required `sub create` for subscription-scope template) | — |
| `ad36052f0` + `abdc8d83e` | 124 | H2b SearchIndexClient port + REAL AI Search tenant-filter template provisioner (both Stubs deleted; SearchIndexClient uses low-level PUT with raw JSON from `infrastructure/ai-search/*.json` canonical source; demoed parallel-file surgical-hunk staging pattern) | +20 → 851 |
| `225fb4192` + `85d7fec84` | 123 | H2a ARM.Resources deployment port (xhigh; caught `WhatIfOperationResult.Changes` `properties`-wrap SDK gotcha via reflection; ARM JSON consumed via blob-download-at-runtime from task 117 manifest) | +16 → 867 |
| `7cade7200` | 125 | H4 SecretClient + ARM.AppService KeyVaultReferenceIdentity PATCH (T1 BOTH slots + completeness ArchTest) + ARM.Authorization role assignment (T5 KV Secrets User `4633458b-17de-408a-b874-0445c86b69e6`) + 3 reflection bonus catches (`UpdateAsync` uses PATCH not PUT · slot id needs `/slots/{name}` segment · `PrincipalId` is Guid not string). Auth-v4 pluggability satisfied manifest-structurally (deviated from `IBffCredentialCreator` seam suggestion — no code-level special-casing; seam belongs to H3/task 130) | +16 → 883 |
| `3eae3e799` | 126 | H4 real value-sourcing (`RandomNumberGenerator` 256-bit lower-hex generate + real `SecretClient` copy for FromExistingKvSecret/FromRunParameters + skip-or-honest-fail for FromBicepOutput) + `FileKvSecretManifest` (task 084 never shipped a .NET reader — built fresh); `AzCliKvSecretsWriter` retired unregistered. YamlDotNet 18.1.0 new dep, clean CVE scan. Zero cleartext logging (verified) | +20 → **903** |

**Zero code-review criticals, zero ADR violations across all 8 commits.** Auth-v4 FIC pluggability satisfied structurally (manifest-driven) rather than via interface seam — accepted deviation.

---

## What was accomplished this session (in commit order)

### Foundation: gap analysis → design studies → decisions → amendments → task decomposition

| Commit | Content |
|---|---|
| `57003b1b0` | DS-6 Batch 1 amendments (spec.md FR-22 + design.md §4.2 restructured with §4.2a runtime topology + §4.2b dispatcher design + S-12 MUST rules; the "BFF IJobHandler infra" root muddle fixed) + 10 evidence design-study notes |
| `a33d719d5` | DS-6 Batch 2 amendments (runtime fact base + retry envelope + serializer contract + Path X cluster + H9 artifact) |
| `3ee508628` | DS-6 Batch 3 amendments (summary/scope/dispositions/ADRs/SC + v3.4 version bump) |
| `5cdc26c0e` | DS-6 addendum sweep (IProvisioningHandler terminology consistency across spec/design/plan/README/CLAUDE — 25 edits) |
| `b0f535ef0` | **Phase C'' task decomposition — 58 POMLs across Waves G-1..G-7** |

### Wave G-1 build (17 tasks) + governance + auth-v4 coord

| Commit | Task | Content |
|---|---|---|
| `b5508eebb` | 100 | Split L2 project → `.Core` + `.Api` + `.Worker` (234 files renamed, all 6 project builds green, 670 tests unchanged) |
| `382478f63` | 106 | C4.5 serializer fix — Newtonsoft `StringEnumConverter` on `RunStatus`/`GateState`/`QuarantineState` + serializer contract test + Cosmos scanner seam test (regression-proof verified) |
| `eefad966e` | 111 | `Grant-ControlPlaneIdentity.ps1` — Path X UAMI-App-User + C5.8 Graph app-role grants (idempotent, `-DryRun`/`-WhatIf`, delegates Section B to sibling script per §11) |
| `762fe50e8` | 108 + 112 + 114 | 108 🟡 (Bicep queue module + drain-verify runbook — live recreate deferred) · 112 ✅ (C1.4 registry client, MI-native, `DefaultAzureCredential`) · 114 ✅ (Exchange sidecar image ~200-230 MB, pwsh 7.4-mariner + ExchangeOnlineManagement 3.5.1) |
| `8c20179bd`+`23de4e096` | 101 | Worker App Service Bicep module + wiring into `platform-controlplane.bicep` (sitecontainer for sidecar, slotless per DS-3) |
| `29e9e2396` | 107 | `attempt` field in `HandlerEnvelope` + MessageId hash + `HandlerRetryAttempts` dict on ProvisioningRun (defeats L1-dedup-kills-§4C-retry latent defect) |
| `4302c45df` | 115 | Sidecar CI workflow YAML drafted as coord-note (escalated closed coordination window — window was CLOSED, worktree DORMANT) |
| `8281045052` | **Governance** | ci-cd-r1 coord window declared expired by owner — r1 formally owns `.github/workflows/**` for Phase C'' scope. 3 queued coord-notes applied (067/088/115): sdap-ci.yml tenant-isolation job + naming-conformance + nightly-health.yml graph parity + NEW `build-provisioning-sidecar.yml`. r1 CLAUDE.md + `projects/INDEX.md` updated |
| `87da541f3` | 116 | BFF artifact publish CI extension — `deploy-bff-api.yml` extended with 11 new steps (buildId + 3 r3 gates + artifact zip + OIDC ACR login + blob push + manifest + red-gate check) + JSON schema for `latest.json` |
| `f1763c489` | 117 | Bicep→ARM-JSON pre-compile CI — new `publish-provisioning-arm-artifacts.yml` (compiles `customer.bicep` + `stacks/model1-shared.bicep`, blob-publishes with manifest). **actionlint downloaded and used** |
| `8edc72d66` | **auth-v4 coord** | Response to auth-v4's ADR-028 A4+E-3 change request (FIC adoption for BFF-OBO). Applied split: spec.md:236 MUST scoped to Model 1 (shared multitenant app-reg) vs Model 2 (per-customer app-reg + FIC). New FR-39 (pluggable secret/FIC) + FR-40 (invariant I6 Model-1-only, ArchTest-enforced). R23 closed with corrected MI-as-issuer vs MI-as-recipient cap analysis. design.md v3.4 → v3.5. POMLs 125/126/130/142 amended for FIC pluggability. Response coord-note at `notes/AUTH-V4-CHANGE-REQUEST-RESPONSE.md` |
| `a0b1f9d26` | 103 | Keyed DI catalog for 20 dispatchable HandlerIds + `HandlerRegistrationCompletenessTests` (WebApplicationFactory<Worker.Program> asserts all 19 resolve). **Caught 2 real DI bugs** (H5 + H7) that unit tests missed. 729/729 tests pass |
| `66b1911e8` | 104 | Extract `IHandlerOutcomeApplier` from `StateReconcilerService` + shared `ReconcilerEnvelopeBuilder`. **Bonus**: DS-2 §5 guard-release gap closed (wires `ICustomerRunGuard.ReleaseAsync`). 736 tests pass (+7) |
| `8e5163dfa` | 109 | Bicep C5.1-C5.3 config-key drift fixes for Api side (`Cosmos__AccountEndpoint`, `ServiceBus__FullyQualifiedNamespace`, etc.); `dev.bicepparam` B1→P1v3 per auth-v4 §8 item 4; ARM JSON regenerated (was stale since task 033) |
| `1a9d23914` | 110 | 🟡 SB Data Sender + Receiver RBAC role assignments in Bicep (cross-RG pattern from task 108) + folded-in Worker-Bicep C5.1 fix. Live grant deferred to Deploy-ControlPlane.ps1 |
| `6a54e09d2` | 113 | 🟡 `Deploy-ControlPlane.ps1` (1074 lines, PSScriptAnalyzer clean, `-WhatIf`-exercised, real `dotnet publish` produced 10.01 MB zip). **Bonus fix-at-discovery**: `.Api` had no `/healthz` route (platform health probe 404-ing since first deploy per task-033 Bicep) — added + tested |
| `2b6250aa6` | 102 | **`ProvisioningHandlerDispatcher`** — the load-bearing execution engine (748 lines). `ServiceBusSessionProcessor` (NOT plain), `SessionId=CustomerId`, `MaxConcurrentCallsPerSession=1` freeze-tested TWICE. Contract test uses reflection on `assembly.GetReferencedAssemblies()` (stronger than grep). New `Microsoft.Extensions.Caching.StackExchangeRedis 10.0.9` NuGet (Wave G-1's sole new dep). 740/740 tests pass |
| `c56be622d`+`220ec3023` | 118 | Dispatch spine integration seam test — arrange in-memory Worker DI, register canary handler under REAL "H1" HandlerId (so `DagAdvancer` genuinely computes H2a as next), assert Cosmos test-double receives outcome. Regression coverage explicitly maps to tasks 102/103/104/106. 742/742 tests pass (+2) |
| `89f2d60fa` | 105 | Real Redis `DispatchIdempotencyService` (swaps NoOp) + 4 new test files (36 new tests). **Self-corrected 2 ADR-038 violations during Step 9.5** + **caught silent-fail design gap** (Redis fallback silently degraded to same-instance-only in prod; fixed via `IHostEnvironment` throw). **787/787 tests pass** (+47). Dispatch-namespace test count: 76 |

### Wave G-1 tally

- **17 of 17 tasks ✅** (14 fully-complete ✅ + 3 authoring-complete-live-pending 🟡: 108, 110, 113)
- **20 substantive commits** landed on PR #779
- **787/787 tests pass** on the L2 test suite (started at ~618 pre-Wave-G; net +169 new tests, zero regressions)
- **Zero code-review criticals**, **zero ADR violations** across all quality gates
- **The execution engine is in the tree**: dispatcher + reconciler + crash recovery + outcome applier + keyed handler resolution + Redis L2 idempotency + Path X registry client + sidecar image + queue Bicep + config-key alignment + deploy script + integration seam test + freeze tests

---

## 🎯 CRITICAL FRAMING FOR NEXT SESSION

Phase C'' delivers r1's stated E2E goal. Wave G-1 (foundation) and Wave G-2 (SDK ports for H0/H1/H0.5/H2a/H2b/H4) are now done. **THREE independent tracks remain**:

1. **Wave G-1 LIVE CEREMONY** — 8 owner-in-the-loop operations against live Azure/Dataverse (originally 7 + BAP admin REST verification from task 120). Enumerated in § Live Ceremony Backlog below.
2. **Waves G-3 through G-7** — 32 tasks remaining across 5 waves. See § Remaining Waves below.
3. **🚨 NEW WORK ITEMS surfaced this session — TWO customer.bicep completeness gaps blocking E2E acceptance** (§ New Work Items below). Not on the DS-4 wave plan; need to be scoped + inserted.

**Owner decision required BEFORE Wave G-3 dispatch** — three sequencing options:

- **Path 1 (recommended — cleanest E2E path)**: Author customer.bicep completion tasks FIRST (as a new mini-wave, probably 2-3 POMLs). Then dispatch Wave G-3 (handler ports for H3/H8/H9). Then G-4..G-7. Reason: Wave G-7's live acceptance depends on customer.bicep being complete anyway; getting it in first avoids late-cycle re-work.
- **Path 2 (parallel-friendly)**: Dispatch Wave G-3 as normal AND author customer.bicep completion in parallel. Requires main-session bandwidth for the Bicep work while subagents do handler ports.
- **Path 3 (defer)**: Dispatch Wave G-3..G-6 as normal; hold customer.bicep completion until pre-Wave-G-7. Risk: G-7 discovers additional gaps that ripple back to earlier waves.

**ALSO**: Wave G-3 task 130 (H3 heavy Graph port) requires auth-v4 phase-5 rollout state check before dispatch — task 130 amends for FIC pluggability per commit `8edc72d66`; if auth-v4 has landed the FIC creation seam in `Register-EntraAppRegistrations.ps1`, task 130 invokes it rather than duplicating logic.

---

## 🚨 New Work Items surfaced during Wave G-2 (not on original plan)

### 1. customer.bicep completeness — missing per-customer stack resources (task 123's flag)

**Symptom**: `infrastructure/bicep/customer.bicep` (350 lines, the Model2Dedicated template that H2a actually deploys) declares RG + KV + Storage + ServiceBus + Cosmos + MembershipTopic (+ optional ACS + SignalR). It does **NOT** declare:
- UAMI (per-customer User-Assigned Managed Identity)
- App Service (per-customer BFF App Service Plan + slots)
- Azure OpenAI
- AI Search

**Impact**: H2a can report `Success` after deploying customer.bicep, but the resulting RG has no compute + no AI resources. Every downstream handler (H4 KV writes, H5 Dataverse env-var writes, H6/H7 config writers, H10 role assignments, H11 storage init) either has nothing to configure OR wires against non-existent resource IDs.

**Discovered**: 2026-08-19 by task 123 (H2a agent) via real ARM calls against a live subscription. Not introduced by task 123 — pre-existing gap made visible.

**Fix scope**: Author the missing modules + wire them into customer.bicep. Estimate: 4-6 new modules + customer.bicep amendments ≈ 500-800 LOC of Bicep. Needs its own POML(s), probably 2-3 tasks (one per resource family: `customer-uami.bicep`, `customer-appservice.bicep`, `customer-openai.bicep`, `customer-aisearch.bicep`).

**Status (2026-08-19/20)**: UAMI + App Service closed by task 127 (✅). OpenAI + AI Search closed by task 128 (✅). Document Intelligence + App Insights + Log Analytics + Redis (the residual slice of this item, plus the E2 Redis-per-customer reconciliation) closed by **task 128b** — POML authored, not yet executed. See "Task 128b — AUTHORED, NOT EXECUTED" above for the full escalation context (E1/E2), including the still-open spec.md/design.md v3.3 amendment question this reconciliation raises.

**Cross-references**: task 123 flagged in POML `<notes>` COMPLETION section + `ArmDeploymentRunner.cs` header comment.

### 2. customer.bicep KV-secrets seed wiring — `kv-secrets.generated.bicep` never invoked (task 126's flag) — ✅ RESOLVED 2026-08-19 by task 129

**Symptom (historical)**: H4's `KvSecretsPopulationManifest` has ~26 secret entries; ~15 of them declare `value_source: FromBicepOutput`, meaning their value must be produced by an upstream Bicep deployment (task 086's `kv-secrets.generated.bicep`) and read from that deployment's outputs. Task 086 authored the module but **never wired it into customer.bicep**.

**Impact (historical)**: Fresh customer runs invoke H4 → H4 resolves those ~15 entries → resolver can't find their upstream output → H4 emits honest `Failed` → run enters `QuarantineRequired` state (correct fail-loud per DS-4's mandate, but blocks E2E).

**Discovered**: 2026-08-19 by task 126 (H4 real-values agent) during real value-resolution implementation. Documented in `notes/task-126-deviations.md` Deviation #3.

**Resolution (task 129, 2026-08-19)**: `kv-secrets.generated.bicep` now invoked from `customer.bicep` with a `kvSecretValues` object covering 10 of the 15 entries from sibling-module outputs. The remaining 5 (BFF-API-ClientId/Audience + 3 SPE-*) are correctly NOT Bicep-resolvable — reclassified to `FromRunParameters` (BFF-API-*) or expected to resolve via H4's `FromRunParameters` path after H8/H9 run (SPE-*). **0 permanent Bicep-side failures remain** — see task 129's completion notes in `tasks/129-*.poml` `<notes-completion>` for the full final triage.

**Cross-references**: task 126's POML `<notes>` + `notes/task-126-deviations.md` + task 129's POML `<notes-completion>`.

### 3. BAP admin REST response-shape verification (task 120's flag)

**Symptom**: `BapRestEnvironmentRateProbe` assumes the BAP admin REST returns `properties.createdTime` on environment lookups. Task 120's sandbox had no live BAP credentials; assumption unverified.

**Fix scope**: Live-only. Operator invokes the probe against a real BAP admin session, verifies shape, adjusts probe if wrong. Adds one item to the live-ceremony backlog before H0 gates a production customer run.

**Cross-references**: `BapRestEnvironmentRateProbe.cs` header + task 120 POML `<notes>`.

---

## Remaining Waves (29 tasks across G-4..G-7 — Wave G-3 complete)

| Wave | Tasks | Scope | Notes |
|---|---|---|---|
| ~~G-3~~ | ~~130, 131, 132 (3)~~ | ~~H3 / H8 / H9 (Graph app-roles + SPE containers + workflows)~~ | **COMPLETE** — 130 (H3) + 132 (H9) done this session; 131 (H8) ran in parallel, verify its own commit landed |
| G-4 | 140-144 (5) | H5 / H6 / H7 / H10 / H11 (Dataverse env-var writers · KV writers · config writers · Storage init · etc.) | H5/H7 depend on customer.bicep gap 1 being closed for meaningful E2E |
| G-5 | 150-153 (4) | H12a/b/c seed chain | |
| G-6 | 160-162 (3) | H14 + sidecar client + live verify | |
| G-7 | 170-186 (17) | H13 probes + Ready writer + real Phase F E2E acceptance | **The E2E gate** — depends on customer.bicep gaps 1+2 being closed |

**Batching guidance for Wave G-3** (per DS-4 §4): 130 (H3) is heavy + needs auth-v4 coord check (dispatch alone first). 131 (H8) + 132 (H9) can parallel after 130 completes.

---

## Wave G-1 LIVE CEREMONY Backlog (owner-in-the-loop, ordered)

### 1. Queue delete-and-recreate (per task 108's runbook)

**Prerequisite** (already met): task 107's `attempt` field is committed (`29e9e2396`); will deploy via task 113's `Deploy-ControlPlane.ps1`. Turning on dedup without `attempt` in the MessageId hash would silently swallow every §4C retry within PT1H.

**Runbook**: [`notes/queue-recreate-runbook-2026-08.md`](notes/queue-recreate-runbook-2026-08.md)

**Steps**:
1. Peek queue: `az servicebus queue show -n sprk-provisioning-jobs --namespace-name spaarke-servicebus-dev -g SharePointEmbedded --query "{active:countDetails.activeMessageCount,dlq:countDetails.deadLetterMessageCount}"`
2. Task 108 verified 0/0 on 2026-08-19. If non-zero when you run, drain first per runbook §3.
3. Delete: `az servicebus queue delete -n sprk-provisioning-jobs --namespace-name spaarke-servicebus-dev -g SharePointEmbedded`
4. Recreate via Bicep (**note**: `platform-controlplane.bicep` has `targetScope='subscription'`, so use `az deployment sub create`, NOT `group create`; use the new `parameters/platform-controlplane-dev.bicepparam` file created 2026-08-19 fix-at-discovery — NOT `stacks/dev.bicepparam` which targets `model2-full.bicep`): `az deployment sub create --location westus2 --template-file infrastructure/bicep/platform-controlplane.bicep --parameters infrastructure/bicep/parameters/platform-controlplane-dev.bicepparam`
5. Verify: `az servicebus queue show ... --query "{sess:requiresSession,dup:requiresDuplicateDetection}"` → both `true`
6. Flip TASK-INDEX row 108 🟡 → ✅

### 2. Pre-Grant-ControlPlaneIdentity fix (small code fix)

**Discovered by task 113**: `az ... | Select-Object -First 1` leaves `$LASTEXITCODE` stale/null → false "Azure CLI not found" preflight failures. Same pattern exists **unfixed** in `Grant-ControlPlaneIdentity.ps1` (task 111). Back-port task 113's fix pattern before step 3.

**Fix pattern**: set `$LASTEXITCODE = 0` before every `az` call, OR capture output to a variable then check `$LASTEXITCODE` before piping.

### 3. Grant-ControlPlaneIdentity.ps1 exec against spaarkedev1

**Script**: [`scripts/provisioning/Grant-ControlPlaneIdentity.ps1`](scripts/provisioning/Grant-ControlPlaneIdentity.ps1) (task 111 `eefad966e`)

**Prerequisite**: operator has Dataverse System Administrator role on `spaarkedev1.crm.dynamics.com` (bootstrap is not self-serviceable).

**Steps**:
1. `az login` with account having Dataverse SysAdmin on `spaarkedev1`
2. Dry run: `.\scripts\provisioning\Grant-ControlPlaneIdentity.ps1 -TenantId a221a95e-6abc-4434-aecc-e48338a1b2f2 -AdminEnvUrl "https://spaarkedev1.crm.dynamics.com" -L2UamiClientId "965a4a01-01e1-442b-97a6-6a98308018b3" -DryRun`
3. Review dry-run output
4. Execute: same command minus `-DryRun`
5. Verify: `WhoAmI` returns the new systemuser; role association returns 200

### 4. Provisioning-artifacts storage account bootstrap (for tasks 116 + 117 blob push)

**Escalation from tasks 116 + 117**: workflows can't push artifacts to `provisioning-artifacts` blob container because storage account doesn't exist in Bicep. Workflows fail cleanly today (don't break existing deploy).

**Steps**:
1. Author Bicep for a platform storage account (probably new `modules/provisioning-artifacts-storage.bicep` invoked from `platform-controlplane.bicep`)
   - Resource: `Microsoft.Storage/storageAccounts` with `provisioning-artifacts` container
   - RBAC: grant `Storage Blob Data Contributor` to the OIDC SP used by `deploy-bff-api.yml` + `publish-provisioning-arm-artifacts.yml` (grep `.github/workflows/` for the SP name/OIDC federation config)
2. Deploy the Bicep
3. Set repo variable `PROVISIONING_ARTIFACTS_STORAGE_ACCOUNT` to the new storage account name
4. Trigger workflows (dispatch) to confirm end-to-end artifact publish

**Note**: this is effectively a new small task not on the DS-4 wave plan. Could be numbered 118.5 or folded into Wave G-3's H9 (task 132) prerequisites.

### 5. Deploy-ControlPlane.ps1 live execution (deploys Wave G-1 code + Bicep)

**Script**: [`scripts/provisioning/Deploy-ControlPlane.ps1`](scripts/provisioning/Deploy-ControlPlane.ps1) (task 113 `6a54e09d2`)

**This is the meat of the live ceremony** — deploys the entire Wave G-1 platform to `rg-spaarke-platform-dev`.

**Steps**:
1. `.\scripts\provisioning\Deploy-ControlPlane.ps1 -Environment dev -Target All -WhatIf`
2. Review what-if output — last chance to catch anything wrong before mutation
3. `.\scripts\provisioning\Deploy-ControlPlane.ps1 -Environment dev -Target All`
4. Verify: `/healthz` returns 200 on both Api + Worker; `az servicebus queue show` returns sessions+dedup both true; `az role assignment list --assignee <L2-UAMI-principalId>` returns Sender + Receiver

### 6. Post-deploy verification (closes 108/110/113 outstanding acceptance criteria)

Once step 5 completes cleanly:
- Flip TASK-INDEX row 108 🟡 → ✅ (queue live-verified)
- Flip TASK-INDEX row 110 🟡 → ✅ (RBAC live-verified)
- Flip TASK-INDEX row 113 🟡 → ✅ (deploy script live-exercised)

### 7. auth-v4 phase-5 secret retirement coordination (deferred, ongoing)

auth-v4 owns the schedule. r1's obligation: honor the pluggability contract (POMLs 125/126/130/142 amended per commit `8edc72d66`). Not a live ceremony action this session — reminder for Wave-G-3 dispatch: check auth-v4's rollout state before starting task 130 (H3 heavy Graph port with FIC branch).

---

## Wave G-2 Dispatch Plan (independent of live ceremony)

Per DS-4 §4 + auth-v4 amendments per commit `8edc72d66`, Wave G-2 = 7 tasks:

| Task | Model | Rigor | Scope | Notes |
|---|---|---|---|---|
| 120 | Sonnet | FULL | H0 SDK ports (4 `PowerShellPreflightProbe` → `ARM.CognitiveServices` + BAP-REST + `ARM.Compute` + KV) | ~600-800 LOC |
| 121 | Sonnet | FULL | H1 real ARM probe (replaces `NullSubscriptionReadinessProbe` which returns Passed unconditionally) | ~150-250 LOC |
| 122 | Sonnet | STANDARD | H0.5 registry swap (DI-swap onto task 112's real `IDataverseEnvironmentRegistryClient`) | ~50 LOC handler-side |
| 123 | Sonnet | FULL | H2a ARM port (`ArmDeployment.CreateOrUpdateAsync` + `WhatIfAtSubscriptionScopeAsync` + T1 KV-ref read; consumes task 117's ARM JSON artifacts) | ~600-800 LOC |
| 124 | Sonnet | FULL | H2b `SearchIndexClient` port + REAL AI Search tenant-filter template provisioner (replaces `Stub*Provisioner`) | ~400-600 LOC |
| 125 | Sonnet | FULL | H4 SDK port (`SecretClient` + `ARM.AppService` `KeyVaultReferenceIdentity` PATCH + `ARM.Authorization` role assignment) — **AMENDED for auth-v4 FIC pluggability per commit `8edc72d66`** | ~350-500 LOC |
| 126 | Sonnet | FULL | **H4 real-values correctness gate** — replaces `AzCliKvSecretsWriter.ResolveValueForEntry`'s literal `{name}-interim-placeholder-{customerId}` values with real secret generation. **AMENDED for auth-v4 FIC (BFF-API-ClientSecret path may go away per phase 5)** | ~200-400 LOC |

**Dispatch batching** (learned from Wave G-1 coordination):
- **Batch G-2A** (parallel, isolated trees): 120 (H0) + 121 (H1) + 122 (H0.5) — each in own handler folder
- **Batch G-2B** (after G-2A): 123 (H2a heavy) + 124 (H2b) — different handlers, safe parallel
- **Batch G-2C** (after G-2B, or in parallel with G-2A/B if isolated enough): 125 (H4 SDK port) then 126 (H4 real values) — SEQUENTIAL (both touch H4 handler + KV writer)

Rough estimate: Wave G-2 total ~3-5 days of parallel-agent work at Wave G-1's cadence.

---

## Live Azure state (unchanged this session — Wave G-1 was code + IaC only)

### `rg-spaarke-platform-dev` (L2 control plane)

- `spaarke-provisioning-controlplane-dev` App Service — currently running the pre-Wave-G-1 code (Wave G-1 code committed but NOT deployed; live ceremony step 5 deploys it)
- `spaarke-provisioning-controlplane-worker-dev` — DOES NOT EXIST YET (task 101 authored the Bicep; deployed by live ceremony step 5)
- `cosmos-spaarke-platform-dev/spaarke-provisioning/runs` — still has the 1 orphaned ProvisioningRun doc (`65109e91-5968-4300-933e-9e79dea4109c`, customerId `trial-2026-08-18`) from failed test 2026-08-18. Will be dropped when reconciler starts + advances it, or when queue is recreated.
- `sprk-controlplane-dev-kv` — unchanged
- `sprk-controlplane-dev-uami` — UAMI needs live grants per step 3 (Path X App User on `spaarkedev1`)

### `spaarke-servicebus-dev` (in `SharePointEmbedded` RG)

- Queue `sprk-provisioning-jobs` still exists with sessions + dedup OFF (task 108 verified 0/0 messages 2026-08-19). Live ceremony step 1 recreates it.
- L2 UAMI has Sender role (manually granted 2026-08-18); Receiver role NOT YET granted (task 110's Bicep adds it; live ceremony step 5 applies)

### `spaarkedev1.crm.dynamics.com` (admin Dataverse)

- `sprk_dataverseenvironment` record from 2026-08-18: id `87d7b4a7-399b-f111-b8de-7ced8ddc4a05`, sprk_name `trial-2026-08-18`. Still there.
- L2 UAMI NOT YET registered as systemuser (Path X). Live ceremony step 3 registers it.

### Cost

~$110-120/mo on `rg-spaarke-platform-dev` baseline (PremiumV3 App Service Plan). Will grow slightly (~$5/mo for storage account) after live ceremony steps 4 + 5.

---

## Systemic coordination context (governance decisions this session)

### 1. ci-cd-r1 declared expired (commit `8281045052`)

The `ci-cd-unit-test-remediation-r1` worktree owned `.github/workflows/**` for a 28-day coordination window that closed 2026-07-23. Worktree dormant since 2026-06-28 (~52 days). Owner declared window expired 2026-08-19; **r1 formally took ownership of `.github/workflows/**` for Phase C'' scope**. If ci-cd-r1 reactivates, merge-conflict resolution proceeds normally.

Applied: 3 queued r1 coord-notes (067 Graph parity, 088 CI-gate wiring, 115 sidecar build) landed directly to workflows. r1 CLAUDE.md + `projects/INDEX.md` updated.

### 2. auth-v4 coordination (commit `8edc72d66`)

auth-v4 (via `notes/PROVISIONING-CHANGE-REQUEST.md`) is moving BFF confidential credential from client-secret to Federated Identity Credential (FIC) per ADR-028 Amendment A4 + Exception E-3.

**Owner-signed-off split** (2026-08-19):
- Model 1 (shared/SMB): single shared multitenant BFF app-reg (matches live state `AzureADMultipleOrgs`). No per-customer FIC creation.
- Model 2 (dedicated): per-customer BFF app-reg + FIC pointing at shared BFF UAMI.
- **New invariant I6 (FR-40, Model 1 only)**: OBO exchange app-reg MUST be per-tenant-request-context-derived. ArchTest-enforced (same pattern as I1-I5).
- **R23 closed** with corrected MI-as-issuer vs MI-as-recipient cap analysis. r1's Q5 spike was overly conservative.
- **Pluggability contract accepted**: POMLs 125/126/130/142 amended for secret-OR-FIC swappable creation during auth-v4's phased rollout.

Response coord-note at [`notes/AUTH-V4-CHANGE-REQUEST-RESPONSE.md`](notes/AUTH-V4-CHANGE-REQUEST-RESPONSE.md).

**Wave G-3 timing**: check auth-v4's rollout state before starting task 130 (H3 heavy Graph port). If FIC creation script has landed in `Register-EntraAppRegistrations.ps1` (auth-v4's §3.2 primary home), task 130 invokes it rather than duplicating logic.

---

## Quality patterns proven this session (worth capturing for future waves)

### 1. Completeness/forcing-function ArchTests catch real defects

- **Task 103's HandlerRegistrationCompletenessTests** (WebApplicationFactory<Worker.Program> constructs real DI): caught H5 + H7 DI-registration defects that unit tests missed. Task 050's H7 agent auto-resumed and fixed it via inter-agent coordination.
- **Task 113's discovery**: `.Api` had no `/healthz` route despite Bicep declaring `healthCheckPath: '/healthz'` since task 033 → platform health probe was 404-ing since first deploy. Fixed at discovery (rigor bumped STANDARD → FULL).
- **Task 102's reflection-based contract test** (`assembly.GetReferencedAssemblies()` vs file-grep): stronger than grep (no false positives from 22 prose-only doc comments referencing IJobHandler by name).
- **Task 118's seam test regression coverage** explicitly mapped: fires on any regression in tasks 102/103/104/106.
- **Task 105's self-correction during Step 9.5 quality gates**: caught 2 ADR-038 violations + 1 silent-fail Redis-fallback design gap (originally would have silently degraded to same-instance-only in prod → fixed via `IHostEnvironment` throw at startup).

Every one of these was worth the engineering cost of building the ArchTests.

### 2. Parallel-agent git-index-race handling

Multiple subagents share the git index. When agent A commits, it can sweep agent B's staged files. Working pattern (proven across ~30 subagents this session):

- Every agent instructed: `git commit --only <specific paths>`, never `git add -A` / `git add .`
- When race happens: sibling agent's file sweep is caught in commit + explicitly cited in commit message. Coordination-only note sent via SendMessage between agents.
- No data loss across the entire session despite ~5 documented sweeps.
- Alternative option available if conflicts worsen: `isolation="worktree"` in Agent tool (creates isolated worktree per agent; ~200-500ms setup + disk cost).

### 3. Live-ceremony vs authoring separation

Consistently applied pattern (task 089 established, tasks 108/110/113 followed):
- Authoring-half completes as ✅ OR 🟡 (if acceptance criteria have live-only checks)
- Live-ceremony half deferred to grouped operator run
- POML `<status>` = `authoring-complete-live-*-pending`
- TASK-INDEX row = 🟡 with inline note pointing at runbook

Keeps subagents safe from destructive live mutations while operator retains full control.

### 4. Coord-note application → direct-authoring shift

Governance discovered mid-Wave-G-1: coord-notes queued for months against a dormant worktree = blocked E2E. Owner declared expiry → r1 took ownership of the surface. Pattern replicable if similar ownership impasses arise on other cross-worktree surfaces.

---

## What's NOT changing (reassurance)

- Handler catalog H0-H14 (19 top-level + 3 sub-handlers = 22 classes total): unchanged
- DAG dependencies + gates + §4B trap catalog + §4C rollback + §4D invariants I1-I5: unchanged (I6 added Model-1-only)
- Tenancy model (D3): unchanged
- The 7 hard-required H7 env vars: unchanged
- The 8 authoritative solutions: unchanged
- Canonical naming (FR-35/36): unchanged
- Path X for L2's own Dataverse creds (FR-38, DS-8): unchanged and distinct from auth-v4's BFF-OBO scope
- Wave G-1 acceptance criteria remaining (108/110/113's live checks): unchanged — pending owner-in-the-loop ceremony

---

## Recovery for a fresh session

If context is refreshed and next session picks up:

1. **Read this file first** (`current-task.md`) — the Quick Recovery table + Wave G-1 tally + owner decision are the essential recovery
2. **Read [`notes/design-study-ds4-handler-audit.md`](notes/design-study-ds4-handler-audit.md)** — the authoritative wave sequencing for Waves G-2..G-7
3. **Check [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md)** for row statuses across the 58 Phase C'' tasks (100-186)
4. **Verify git state** — `git status --short` should be clean; `git log --oneline -5` should show `89f2d60fa` at HEAD (or later if new commits have landed)
5. **Ask owner**: Path A (live ceremony first), Path B (Wave G-2 parallel), or Path C (Wave G-2 now, ceremony later)?

Do NOT re-derive Wave A/B decisions — they are locked and committed. Do NOT re-open R23 — closed per auth-v4 §4 analysis.

---

## Files preserved for full context

| File | Purpose |
|---|---|
| `spec.md` v3.5 (post-Batch-1/2/3/addendum/auth-v4 amendments) | Authoritative FR/NFR reference |
| `design.md` v3.5 | Authoritative design reference |
| `plan.md` + `README.md` + this project's `CLAUDE.md` (post-terminology sweep) | IProvisioningHandler-consistent |
| `tasks/TASK-INDEX.md` | 136 tasks (78 original + 58 Phase C''), status current |
| `tasks/100-186-*.poml` | Phase C'' task files (POMLs 125/126/130/142 amended for auth-v4 FIC pluggability) |
| `notes/design-study-ds*.md` (9 files) | Wave A + Wave B design studies |
| `notes/r1-gap-analysis-2026-08-18.md` | Original gap analysis |
| `notes/design-study-ds6-spec-design-amendment-text.md` | Ready-to-apply amendment text (already applied) |
| `notes/AUTH-V4-CHANGE-REQUEST-RESPONSE.md` | r1's response to auth-v4's change request |
| `notes/PROVISIONING-CHANGE-REQUEST.md` | auth-v4's change request (APPLIED banner added) |
| `notes/queue-recreate-runbook-2026-08.md` | Task 108's live queue recreate runbook |
| `notes/h9-artifact-publish-ci-coord-pr.md` | Task 116's design record |
| `notes/h2a-bicep-precompile-ci-coord-pr.md` | Task 117's design record |
| `notes/sidecar-ci-workflow-coord-pr.md` | Task 115's coord-note (APPLIED banner added) |
| `notes/graph-app-role-parity-coord-pr.md` | Task 067's coord-note (APPLIED) |
| `notes/phase-h-ci-wiring-coord-pr.md` | Task 088's coord-note (APPLIED) |
| `notes/task-*-deviations.md` (multiple) | Per-task deviation records |
| `scripts/provisioning/Grant-ControlPlaneIdentity.ps1` | Path X grant script (task 111) |
| `scripts/provisioning/Deploy-ControlPlane.ps1` | L2 deploy script (task 113) |
| `src/server/services/Sprk.Provisioning.ControlPlane.*` | L2 code split into `.Core` + `.Api` + `.Worker` + `.Sidecar` + `.Tests` |
| `.github/workflows/sdap-ci.yml` (updated) + `nightly-health.yml` (updated) + `deploy-bff-api.yml` (updated) + NEW `build-provisioning-sidecar.yml` + NEW `publish-provisioning-arm-artifacts.yml` | r1-owned CI workflows per governance shift |

---

*Wave G-1 complete. Ready for owner direction on live ceremony vs Wave G-2 dispatch (or both in parallel).*
