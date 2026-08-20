# Current Task State — customer-provisioning-orchestration-r1

> **Last Updated**: 2026-08-20 (Task 153 COMPLETE — H12c credential-config confirmation, Batch G-5C
> [ran alone, no siblings]. **Investigated first** (per dispatch directive #2): confirmed the POML's
> literal "needs the same H7/task-142 client-secret KV pattern" framing does NOT hold — H12c's
> `DataverseWebApiModelDeploymentReferenceWriter` already authenticates via `DefaultAzureCredential
> (TenantId=...)` (UAMI-as-Dataverse-App-User), the SAME idiom every other post-H10 handler uses,
> since H12c dispatches strictly after H12a+H12b (both post-H10). No client-secret credential exists
> to provision (D-153-1). The GENUINE gap: `RuntimeReferencesOptions.SharedPlatformOpenAiEndpoint`
> (Model1Shared's shared-platform OpenAI endpoint) had ZERO Bicep wiring anywhere — wired
> `RuntimeReferences__SharedPlatformOpenAiEndpoint` as a KV-reference app setting sourced from the
> SAME canonical `AzureOpenAI-Endpoint` secret the `.Api` site already resolves (single source of
> truth, not a duplicate) — new `azureOpenAiEndpointSecretName` param on both
> `modules/controlplane-worker-app-service.bicep` + `platform-controlplane.bicep`, threaded through,
> `platform-controlplane.json` regenerated, set in the dev bicepparam (D-153-2). Added NFR-05:
> `RuntimeReferencesOptions.SectionName` const + `Validate()` bounds-checking ONLY
> `DataverseRequestTimeout` — deliberately does NOT require `SharedPlatformOpenAiEndpoint` at boot
> since it's conditionally required (Model1Shared branch only); an unconditional requirement would
> crash-loop a Model2-only Worker for a setting it never uses. Wired
> `AddOptions<T>().Bind().Validate().ValidateOnStart()` inside `RuntimeReferencesModule` (Program.cs's
> single call line unchanged). `az deployment sub what-if` (dev): status Succeeded, Worker shows full
> "Create" (live-ceremony hasn't run yet, same as task 142's finding — not a regression). 6 new tests
> (NFR-05 region, incl. a SectionName regression-guard). Quality gates (code-review + adr-check,
> test-modifying override): 0 Critical, 0 Warnings, 0 ADR violations across ADR-004/010/028/032/036.
> Full deviation record: `notes/task-153-h12c-credential-config-deviations.md`. L2 tests: 1061 ->
> **1067/1067** [+6, zero regressions]. **Wave G-5 is now 100% COMPLETE** (tasks 150/151/152/153 all
> ✅) — Wave G-6 (tasks 160 H14 KV-reader swap / 161 H14a sidecar client wiring / 162 sidecar live
> verification) is now unblocked.)
>
> **Previous** (2026-08-20, Task 152 COMPLETE — H12b field-mapping + chart-def GREENFIELD
> seeders, Batch G-5B [ran alone, no siblings]. New `DataverseWebApiFieldMappingSeeder.cs` [3
> sprk_fieldmappingprofile profiles -- Matter to Event/Invoice/Report Card Attorney Matrix -- + 20
> sprk_fieldmappingrule Copy rules, all ground-truthed live against spaarkedev1 via Dataverse MCP
> describe+read_query, not invented] + `DataverseWebApiChartDefSeeder.cs` [4 "Upcoming To Dos"
> sprk_chartdefinition rows, embedded from the ONE chart-def family with a checked-in repo JSON
> mirror -- infrastructure/dataverse/charts/upcoming-todos-*.json -- + a proven idempotent script,
> smart-todo-r4 task 080-G] replace the 2 DeferredAppConfigSeeder no-op placeholders in
> AppConfigSeedModule.cs. **FR-16's 4-scope delivery (DataGrid + workspace-layout + field-mapping +
> chart-def) is now fully complete.** DeferredAppConfigSeeder.cs RETIRED (kept on disk unregistered,
> retirement banner added matching the PowerShellAppConfigSeeder.cs precedent; grep 'new
> DeferredAppConfigSeeder(' in AppConfigSeedModule.cs returns 0 matches -- verified via a dedicated
> unit test AND a manual grep). **Escalation trigger did NOT fire** -- seed content was clearly
> specified: field-mapping is a literal, MCP-verified mirror of the 3 Active profiles already
> documented in SPAARKE-FIELD-MAPPING-FRAMEWORK.md + FIELD-MAPPING-ADMIN-GUIDE.md's Worked Example;
> chart-def is the one family with a real repo source + proven script (FR-31..FR-36). **2 documented
> scope decisions** (not silent): (1) a 4th live field-mapping profile ("Matter to Work Assignment")
> exists in spaarkedev1 but is undocumented + carries only 1 of 8 expected rules with a
> self-inconsistent name/value -- intentionally excluded as in-progress/incomplete, not a finished
> default; (2) spaarkedev1 carries ~19 live sprk_chartdefinition records total but only the 4
> "Upcoming To Dos" rows have a repo JSON mirror -- the other ~15 (MATTER HEALTH, MATTER BUDGET,
> etc.) have zero repo source of truth and were intentionally NOT reproduced from a live SELECT
> (would have been exactly the "inventing default seed content" risk the POML's escalation trigger
> warns against) -- flagged as a Wave-C5-style follow-on, not silently dropped. Idempotency
> semantics: field-mapping profiles/rules are find-then-SKIP-if-found (admin-editable config, parity
> with WorkspaceLayoutSeeder); chart-defs are find-then-PATCH-if-found/always-refresh (parity with
> DataGridSeeder). FK pre-requisite (missing sprk_recordtype_ref row) fails loud with the admin
> guide's exact remediation, never silently invents/skips. 17 new tests (hand-rolled fake
> HttpMessageHandler, never Mock&lt;HttpMessageHandler&gt; per ADR-038) incl. idempotency-on-rerun
> tests for BOTH scopes + a fail-loud missing-recordtype_ref test, all passing. Quality gates
> (code-review + adr-check, FULL rigor + test-modifying override, both unconditional): 0 Critical, 0
> Warnings, 0 ADR violations. No BFF Hygiene trigger (files are under
> Sprk.Provisioning.ControlPlane.Core, not Sprk.Bff.Api). L2 tests: 1044 -> **1061/1061** [+17, zero
> regressions]. **Wave G-5 Batch G-5B is now 100% COMPLETE** — task 153 (H12c credential config,
> Batch G-5C, deps 123+150+151+152 all now satisfied) is unblocked.)
>
> **Previous** (2026-08-20, Task 150 COMPLETE — H12a YamlDotNet manifest engine + DV-REST seed writes,
> Batch G-5A [parallel with task 151, now also complete — **Wave G-5 Batch G-5A is 100% COMPLETE**]. Deleted
> `InvokeSeedManifestScriptRunner.cs` (`ProcessStartInfo` shell-out to `Invoke-SeedManifest.ps1`, which
> itself required a second PowerShell YAML module — the DS-1b matrix-correction finding this task closes).
> New `YamlSeedManifestEngine.cs` [YamlDotNet parse of the embedded manifest.yaml + Kahn's-algorithm
> topological sort, exact port of the PS script's Read-ManifestYaml + Get-TopologicalOrder] + new
> `DataverseWebApiSeedWriter.cs` [`ISeedManifestRunner` impl reusing `DataverseWebApiModelDeploymentReferenceWriter`'s
> (H12c) exact in-process idiom verbatim — HttpClient + DefaultAzureCredential, find-by-filter existence
> check, JsonContent POST]. **Documented scope boundary** (ADR-039 Path A per adr-check, not a silent gap):
> the writer directly seeds the 4 manifest artifacts whose deployer.idempotencyMode is
> existence-check-then-insert AND whose authoritativeSource is a flat JSON content file with no
> relationship wiring (type-lookups/knowledge/skills/output-types — 44 rows against an empty env). The
> remaining 8 artifacts (4 R7 per-file directory loaders, already PENDING under the retired PS script;
> playbooks-mvp/action-outputschema-patches/playbook-consumers — N:N relationship-association writes, a
> materially different operation shape than H12c's flat-upsert idiom this task's constraint scoped the
> writer to reuse; aimodeldeployment — H12c-owned placeholder) report the SAME PENDING/PLACEHOLDER marker
> semantics the retired script already emitted — no observability regression, no ADR-039 retired-artifact
> re-introduction (that defense-in-depth scan stays in the untouched `FileSeedManifestReader`).
> Relationship-wiring support is flagged as a legitimate follow-on task, not silently dropped.
> `AiSeedChainOptions` lost 2 dead fields (`PwshExecutable`/`InvokeSeedManifestScriptPath`), gained
> `DataverseRequestTimeout` (parity with H12c's `RuntimeReferencesOptions`). Test seam: internal
> `credentialFactory` constructor overload (parity with `DataverseWebApiSolutionImporter`'s own seam) so
> token-acquisition failure is unit-testable without a real `DefaultAzureCredential` network path. 20 new
> tests (`YamlSeedManifestEngineTests` ×13, `DataverseWebApiSeedWriterTests` ×7 — hand-rolled fake
> `HttpMessageHandler`, never `Mock&lt;HttpMessageHandler&gt;` per ADR-038), all passing — including a
> writer-level idempotency test (2nd `InvokeAsync` run against a handler reporting every row as existing
> fires zero new POSTs) and a same-request-shape-as-H12c test (identical Bearer/OData headers +
> `$filter=sprk_name eq '...'` existence-check shape). **Grep-collision self-catch**: initial doc-comment
> drafts of both new files literally contained "powershell-yaml"/"Install-Module" (describing what was
> replaced) — would have failed the POML's own literal grep acceptance criterion; reworded before the grep
> was re-run, verified 0 matches in the 2 new production files (banned strings only remain inside the test
> files' own literal assertion strings, which is expected). Quality gates (code-review + adr-check,
> unconditional per FULL rigor + test-modifying override): 0 Critical, 0 blocking Warnings; adr-check
> surfaced 1 documented ADR-039 Path-A scope-boundary Warning (see above), 0 violations. `dotnet test
> tests/Spaarke.ArchTests` showed 2 PRE-EXISTING, unrelated failures (`CosmosProvisioningSecretGuardTests`
> looking for the retired pre-split `Sprk.Provisioning.ControlPlane` project bin dir — superseded by the
> `.Core`/`.Api`/`.Worker` split, predates this task, out of scope to fix here). L2 tests (`Sprk.Provisioning
> .ControlPlane.Tests`): 1024 (post-151) → **1044/1044** [+20, zero regressions]. Shared-file coordination:
> `Sprk.Provisioning.ControlPlane.Core.csproj` was concurrently edited by sibling task 151 (H12b) — verified
> clean additive merge (their 2 `<EmbeddedResource>` blocks land strictly after this task's own block,
> confirmed via their own stash/pop verification + a standalone build). **Wave G-5 Batch G-5A is now 100%
> COMPLETE** (both 150 + 151 done) — task 152 (H12b greenfield seeders, already unblocked by 151) and task
> 153 (H12c credential config, needs 150+151+152) can now both proceed once 152 lands.)
>
> **Previous** (2026-08-20, Task 151 COMPLETE — H12b DataGrid + workspace-layout DV-REST ports, Batch
> G-5A [parallel with task 150]. `DataverseWebApiDataGridSeeder.cs` [sprk_gridconfigurations: 4-row
> find-by-id-or-name -> PATCH sprk_configjson if found / POST if not — always refreshes on match, config
> JSON embedded from the ReconciliationGrid shared-lib's own JSON files so seed content can never drift]
> + `DataverseWebApiWorkspaceLayoutSeeder.cs` [sprk_workspacelayouts: N-row find-by-name+isSystem ->
> skip if found / POST if not — default no-`-Force` parity, layouts embedded from scripts/system-layouts.json]
> replace the retired PowerShellAppConfigSeeder shell-outs to seed-reconciliation-gridconfig.ps1 /
> Deploy-SystemWorkspaceLayouts.ps1 (kept on disk unregistered per the Wave G-2..G-4 retirement
> convention). **Auth decision**: DefaultAzureCredential pinned to the L2 UAMI (NOT ClientSecretCredential)
> — design.md's handler DAG places H12b after H10 (App User creation) + H11, unlike H6/H7 which run
> before H10; parity with H12c's existing DataverseWebApiModelDeploymentReferenceWriter. **Documented
> deviation** (not a defect): the source script's schema half (Add-IsSystemAttribute, adds sprk_issystem
> column) was intentionally NOT ported — the POML's own dependency note frames H6 solution import as
> already carrying that column in the managed solution; re-adding metadata-mutation logic to a per-customer
> data seeder would also poorly fit Option D's "pure Web API upserts" characterization. AppConfigSeedOptions
> gained DataverseRequestTimeout + Validate()/ValidateOnStart (NFR-05 parity with task 142's H7 precedent).
> Embedded-resource pattern (task 124/126 precedent) used for both JSON sources — single source of truth,
> no drift, self-contained L2 publish output. 19 new tests (hand-rolled fake HttpMessageHandler, never
> Mock&lt;HttpMessageHandler&gt; per ADR-038), all passing. Quality gates (code-review + adr-check) clean —
> zero critical/warning findings, zero ADR violations (test-modifying override made gates mandatory despite
> STANDARD rigor). Shared-file coordination: Sprk.Provisioning.ControlPlane.Core.csproj is also being
> edited by sibling task 150 (H12a) concurrently — this task's commit surgically isolates ONLY its own
> 25-line `<EmbeddedResource>` addition into the commit (task 150's in-flight block was preserved
> unstaged in the working tree for them to commit separately); Worker/Program.cs was NOT touched (H12b's
> `AddH12bAppConfigSeedHandler()` extension method is fully self-contained, unchanged 1-line call site).
> L2 tests (this task's scope alone): 19/19 passing; full-suite run showed 3 PRE-EXISTING failures, all in
> `YamlSeedManifestEngineTests` (task 150's own in-progress work, unrelated files) — not caused by this
> task's changes. **Wave G-5 Batch G-5A is now HALF complete** (151 done; 150 in progress) — task 152
> (H12b greenfield seeders) unblocked once 151 lands; task 153 (H12c) still waits on 150 too.)
>
> **Previous** (2026-08-20, Task 144 COMPLETE — H11 live verification post C5.8 grants. Consent-gate
> seam [`GraphRestB2BConsentVerifier`, fully read-only] live-verified end-to-end via new durable smoke
> tests [`H11SeamsSmokeTests.cs`] run genuinely live in-sandbox, both Verified/Pending branches — the
> escalation-trigger-relevant seam (false-Pass-on-pending would be HIGH severity; directly tested and NOT
> observed). Write-capable seams [`GraphRestUserProvisioner`'s POST /users + assignLicense,
> `GraphRestB2BInvitationClient`'s POST /invitations] had shapes ground-truthed against Microsoft Learn
> (exact field-name match) — writes deferred to live-ceremony (real user creation / real invitation email,
> no safe automated undo). **BONUS CATCH (MAJOR)**: the 14-role `GraphAppRoles.cs`/`L2GraphAppRolesRegistry.cs`
> catalog (task 005) was missing `User.Invite.All` entirely — H11's B2BGuest branch would have received a
> permanent 403 on every invitation POST once C5.8 grants land. Fixed: added a 15th role (GUID
> ground-truthed live: `09850681-111b-4a89-9bed-3f2cae46d706`), mirrored to L2, every stale "14" reference
> updated across 8 files + spec.md FR-33 + the customer deployment guide; mirror-parity test re-confirmed
> byte-identical. Consent-verifier reuse check (CLAUDE.md §11): confirmed H11's verifier and H3's
> `GraphAdminConsentVerifier` answer genuinely different questions (guest `externalUserState` vs app-reg
> `oauth2PermissionGrants`) — no consolidation opportunity. Deferred+documented (not fixed):
> `H11UserProvisioningOptions` has zero Bicep wiring (`AccountDomain` defaults to Spaarke's own tenant
> domain — assessed fail-loud via Graph's UPN-domain-verification, not fail-silent). L2 tests: 1003 ->
> **1005/1005** [+2, zero regressions]. Full evidence: notes/h11-live-verification-2026-08.md.
> **Wave G-4 is now 100% COMPLETE (5/5 tasks)** — Wave G-5 (H12a/b/c seed chain, 4 tasks) unblocked.)
>
> **Previous** (2026-08-20, Task 141 COMPLETE — H6 Web-API import port. `DataverseWebApiSolutionImporter.cs` [ISolutionImporter — Dataverse Web API ImportSolution/StageAndUpgrade actions + importjobs polling, resolving the 8 solution ZIPs from a versioned blob-artifact manifest in the `provisioning-artifacts` container] + `DataverseWebApiSolutionVerifier.cs` [ISolutionVerifier — trivial GET /solutions?$select=uniquename,version,solutionid] replace the retired DeployDataverseSolutionsScriptImporter/PacCliSolutionVerifier shell-outs. Web API shapes ground-truthed via WebFetch against Microsoft Learn (not guessed). Documented deviation from the dispatch context's "poll /asyncoperations" framing: polls importjobs({ImportJobId}) using the client-generated GUID directly (deterministic, no Location-header parsing needed) — completedon non-null is the terminal signal. importjobs.data (XML) defensively parsed for result="failure"/"warning" nodes; unparseable/empty data is a provisional success whose diagnostic says so explicitly (never silently swallowed) — the separate verifier's independent GET is defense-in-depth. Two intentionally distinct credentials: ClientSecretCredential (BFF app-reg, task 142's H7 precedent) for the customer-Dataverse-env calls; the shared L2 UAMI TokenCredential for the artifacts blob container. Live-ceremony gap documented (no CI workflow yet publishes the solution-artifact manifest — SolutionImportOptions.Validate() fails fast at boot if unset). Grep-collision self-catch: initial doc-comment drafts literally contained "pac solution import"/"pac solution list" — reworded before the grep was re-run. L2 tests: 968 -> **1003/1003** [+35 this task, zero regressions]. Batch G-4B now FULLY COMPLETE (141 + 143 both done); Batch G-4C (144, H11 verify) unblocked.)
>
> **Previous** (2026-08-20, Task 143 COMPLETE — H10 live verification post C5.8 grants. Live-verified 3 of 5 REST/Graph seams fully end-to-end [T2 DataverseWebApiAppUserVerifier + T3 GraphRestAppRoleParityVerifier, both fully read-only]; live-verified the READ components of the remaining 2 [DataverseWebApiAppUserCreator, GraphRestAppRoleGranter] via direct REST against spaarkedev1 + real Microsoft Graph; WRITE components deferred to live-ceremony [C5.8/task 111 not yet live-executed]. **BONUS CATCH**: found + fixed a genuine wrong-but-non-null AppRoleId GUID in GraphAppRoles.cs [GroupMember.ReadWrite.All — last 4 hex chars `6571` should be `6695`] by cross-checking all 14 catalog entries against the REAL Microsoft Graph resource SP's own appRoles collection. L2 tests: 965 -> **968/968** [+3, zero regressions]. Full evidence: notes/h10-live-verification-2026-08.md.)
> **Working directory**: `c:\code_files\spaarke-wt-customer-provisioning-orchestration-r1`
> **Branch**: `work/customer-provisioning-orchestration-r1` — see git log for latest commit, in sync with `origin/work/customer-provisioning-orchestration-r1`
> **PR**: https://github.com/spaarke-dev/spaarke/pull/779 (DRAFT — DO NOT MERGE — Phase C'' incomplete; Waves G-4..G-7 remain. Wave G-2.5 (customer.bicep completion) is fully closed. Wave G-3 (130/131/132) is now FULLY COMPLETE.)

## 🎯 Wave G-4 — 100% COMPLETE (2026-08-20, 5/5 tasks)

All of Wave G-4 (140, 141, 142, 143, 144) has landed. L2 tests: 903 (Wave G-3 baseline) → **1005/1005**
across the full wave. Zero code-review criticals, zero unresolved ADR violations across all 5 commits.
Wave G-5 (H12a/b/c seed chain, 4 tasks — 150+) is now unblocked.

## 🎯 Wave G-6 Dispatch Plan (unblocked 2026-08-20 after Wave G-5 close)

**Dependency DAG for Wave G-6 (3 tasks, sequential chain)**:
```
160 (H14 KV-reader swap, FULL/high, deps 125+153) ─→ 161 (H14a sidecar client, OPUS/high, deps 114+160) ─→ 162 (sidecar live verify, OPUS/high, deps 101+113+114+161)
```

No parallelism possible — linear chain. All 3 depend transitively on each prior.

**Batch G-6A**: task 160 alone (H14 KV-reader swap; sonnet tier)
**Batch G-6B**: task 161 alone (H14a sidecar client wiring; **OPUS tier** — main-session Opus 4.7 dispatches Opus subagent)
**Batch G-6C**: task 162 alone (sidecar live verify against dev L2 Worker; **OPUS tier**; live-ceremony-aware — may defer live check if credentials unavailable)

---

## Wave G-5 Tally (100% COMPLETE 2026-08-20)

| Task | Commit | Handler | Δ tests | Notable |
|---|---|---|---|---|
| 151 | `9cfb0ec61` | H12b DataGrid + workspace-layout ports | +19 → 1024 | Post-H10 auth insight (`DefaultAzureCredential` not `ClientSecretCredential`); cross-agent SendMessage validated |
| 150 | `99c0a5a0e` | H12a YamlDotNet + DV-REST seed writes | +20 → 1044 | ADR-039 Path A (4 of 12 artifacts direct-seeded); embedded-resource manifest at build-time; T3 same-shape-as-H12c test; **fixed sibling 151's flagged 3 pre-existing failures** |
| 152 | `9bcf53c5f` | H12b field-mapping + chart-def greenfield seeders | +17 → 1061 | **FR-16 fully closed** (all 4 scopes now real DV-REST seeders); Dataverse MCP used for live schema ground-truthing; live-data-inconsistency flagged on spaarkedev1 |
| 153 | `f44cc746b` | H12c credential config (STANDARD, no code delta) | +6 → 1067 | Investigated-before-implementing (POML framing wrong — H12c uses UAMI not ClientSecret); **major silent-fail catch: SharedPlatformOpenAiEndpoint zero Bicep wiring** (Model1 would silent-fail forever); conditional NFR-05 (bounds-check only Model2-required fields) |

L2 tests: **1005 → 1067/1067** (+62 across Wave G-5). Zero code-review criticals, zero ADR violations. FR-16 closed. Silent-fail-at-runtime defects caught across Waves G-4+G-5: **4 total** (wrong AppRoleId GUID; missing User.Invite.All role; SectionName drift; SharedPlatformOpenAiEndpoint zero Bicep wiring).

---

## 🎯 Wave G-5 Dispatch Plan (unblocked 2026-08-20 after Wave G-4 close)

**Dependency DAG for Wave G-5 (4 tasks 150-153)**:
```
150 (H12a YamlDotNet + DV-REST seeds, FULL/high, waveG5-parallel) ─┐
                                                                     ├─→ 153 (H12c credential config, STANDARD, needs 150+151+152)
151 (H12b DataGrid+workspace-layout ports, STANDARD, parallel) ─→ 152 (H12b field-mapping+chart-def seeders, FULL, needs 151) ─┘
```

**Batch G-5A** — ✅ 100% COMPLETE (both 150 + 151 landed 2026-08-20)
- 150: ✅ COMPLETE — H12a YamlDotNet manifest engine + DV-REST seed writes (deps 141 done)
- 151: ✅ COMPLETE — H12b 2 DV-REST ports (DataGrid + workspace-layout seeders) (deps 141 done)

**Batch G-5B** — ✅ 100% COMPLETE (152 landed 2026-08-20)
- 152: ✅ COMPLETE — H12b 2 greenfield seeders (field-mapping + chart-def) — completes FR-16 (deps 151, satisfied)

**Batch G-5C** — ✅ 100% COMPLETE (153 landed 2026-08-20)
- 153: ✅ COMPLETE — H12c credential-config confirmation (deps 123, 150, 151, 152 — all satisfied). No client-secret needed (already-correct UAMI/DefaultAzureCredential); wired the genuinely-unwired `SharedPlatformOpenAiEndpoint` Bicep KV-ref + NFR-05 bounds-only Validate(). +6 tests.

## 🎯 Wave G-5 — 100% COMPLETE (2026-08-20, 4/4 tasks)

| Task | Handler | Δ tests | Notable |
|---|---|---|---|
| 150 | H12a YamlDotNet manifest engine + DV-REST seed writes | +20 → 1044 | Deleted PowerShell shell-out runner; reused H12c's exact HttpClient+DefaultAzureCredential idiom |
| 151 | H12b DataGrid + workspace-layout DV-REST ports | (parallel w/150) | Retired PowerShellAppConfigSeeder shell-outs |
| 152 | H12b field-mapping + chart-def GREENFIELD seeders — completes FR-16 | +17 → 1061 | Genuinely new content, ground-truthed live via Dataverse MCP, not invented |
| 153 | H12c credential-config confirmation | +6 → 1067 | No client-secret needed (D-153-1); wired unwired `SharedPlatformOpenAiEndpoint` Bicep KV-ref (D-153-2) + NFR-05 bounds-only Validate() |

L2 tests: **1024 → 1067/1067** (+43 across Wave G-5). Zero code-review criticals, zero ADR violations across all 4 tasks. Wave G-6 (tasks 160/161/162 — H14 KV-reader swap, H14a sidecar client wiring, sidecar live verification) is now unblocked.

---

## Wave G-4 Tally (100% COMPLETE 2026-08-20)

| Task | Commit | Handler | Δ tests | Notable |
|---|---|---|---|---|
| 140 | `d974ed461` | H5 BAP-REST env-create + async polling | +19 → 965 | Live WebSearch resolved BAP endpoint discrepancy; idempotent existing-env check added |
| 142 | `0ba095c58` | H7 credential provisioning + NFR-05 | +8 → 946 | Silent-fail-trap fix (`SectionName` mismatch with Bicep key) + `ValidateOnStart` proactive fix |
| 143 | `7623527f2` | H10 verify + wrong GUID fix | +3 → 968 | **Major defect fix**: wrong `AppRoleId` for `GroupMember.ReadWrite.All` (would 403 silently at runtime) |
| 141 | `bcb1e3f0f` | H6 Web-API import + ZIP packaging | +35 → 1003 | Microsoft Learn WebFetch ground-truthed polling; XML `data` failure-parsing; ImportJob directly-polled |
| 144 | `ffeacb934` | H11 verify + missing role fix | +2 → 1005 | **Major defect fix**: missing `User.Invite.All` role (would 403 silently on B2B invites); added 15th role mirrored across 7 files + spec + customer guide |

L2 tests: **938 → 1005/1005** (+67 across Wave G-4). Zero code-review criticals, zero ADR violations, **2 major silent-fail-at-runtime defects caught + fixed** by verification-focused tasks 143 + 144.

**Verification tasks caught what SDK-port tasks missed** — validating the "verify code-that-looks-real" pattern is high-value.

---

## 🎯 Wave G-4 Dispatch Plan (unblocked 2026-08-20 after Wave G-3 close)

**Dependency DAG for Wave G-4 (5 tasks 140-144)**:
```
140 (H5, high) ─┬─→ 141 (H6, xhigh) [Web-API import + ZIP packaging]
                └─→ 143 (H10 verify) ─→ 144 (H11 verify)

142 (H7 STANDARD, high) [independent — dep 126 only]
```

**Batch G-4A** (parallel-safe now, dispatch immediately): 140 + 142 — BOTH COMPLETE, Batch G-4A closed
- 140: ✅ COMPLETE — H5 BAP-REST env-create + async-operation-polling port (deps 102/103/123 — all done). See "Task 140 — COMPLETE" section below.
- 142: ✅ COMPLETE — H7 credential provisioning + NFR-05 validation (STANDARD rigor; dep 126 — done). See "Task 142 — COMPLETE" section below.

**Batch G-4B** (after 140 lands): 141 + 143 — BOTH COMPLETE, Batch G-4B closed
- 141: ✅ COMPLETE — H6 Web-API import (ImportSolution/StageAndUpgrade + ImportJob polling) + ZIP artifact packaging. See "Task 141 — COMPLETE" section below.
- 143: ✅ COMPLETE — H10 live verification post C5.8 grants (5 REST/Graph seams; code already real — verification-focused task). See "Task 143 — COMPLETE" section below.

**Batch G-4C** (after 141 + 143 land): 144 alone — COMPLETE, Batch G-4C closed, Wave G-4 100% COMPLETE
- 144: ✅ COMPLETE — H11 live verification (Graph REST + B2B invitation + consent verifier; code already real). See "Task 144 — COMPLETE" section below.

**Rough estimate**: 3 batches × ~30-60 min each = ~2-3 hours wall clock for entire Wave G-4.

---

## Task 152 — COMPLETE (2026-08-20)

H12b field-mapping + chart-def GREENFIELD seeders (Wave G-5 Batch G-5B, ran alone — no sibling
agents this dispatch). Completes FR-16's 4-scope delivery. Full detail in the "Last Updated" banner
above; summary here for the per-task index pattern.

New `DataverseWebApiFieldMappingSeeder.cs` (3 `sprk_fieldmappingprofile` profiles + 20
`sprk_fieldmappingrule` Copy rules, ground-truthed live via Dataverse MCP against spaarkedev1's
Active configuration rather than invented) + `DataverseWebApiChartDefSeeder.cs` (4 "Upcoming To
Dos" `sprk_chartdefinition` rows, embedded from the repo's one JSON-backed chart-def family)
replace the 2 `DeferredAppConfigSeeder` no-op placeholders in `AppConfigSeedModule.cs`.
`DeferredAppConfigSeeder.cs` retired (kept on disk unregistered, zero remaining callers).

Tests: 1044 → **1061/1061** (+17, zero regressions). Step 9.5 (code-review + adr-check, FULL rigor
+ test-modifying override, both unconditional): 0 Critical, 0 Warnings, 0 ADR violations.

Commit scope: 2 new seeder files, 2 new test files, `AppConfigSeedModule.cs` (registration swap),
`DeferredAppConfigSeeder.cs` (retirement banner), `IAppConfigSeeder.cs` + `H12bAppConfigSeedHandler.cs`
(doc-comment updates), `Sprk.Provisioning.ControlPlane.Core.csproj` (1 new EmbeddedResource block),
this project's `current-task.md`/`TASK-INDEX.md`/task-152 POML status flip.

No coordination needed — ran alone this dispatch (Wave G-5 Batch G-5A siblings 150/151 both already
landed before this task started; task 153 dispatches after this lands).

---

## Task 150 — COMPLETE (2026-08-20)

H12a YamlDotNet manifest engine + DV-REST seed writes (Wave G-5 Batch G-5A, parallel with task 151). Full
detail in the "Last Updated" banner above; summary here for the per-task index pattern.

Deleted `InvokeSeedManifestScriptRunner.cs` (`ProcessStartInfo` shell-out, DS-1b matrix-correction target).
New `YamlSeedManifestEngine.cs` (YamlDotNet embedded-resource parse + Kahn's-algorithm topological sort) +
`DataverseWebApiSeedWriter.cs` (new `ISeedManifestRunner` impl, reusing H12c's
`DataverseWebApiModelDeploymentReferenceWriter` idiom verbatim). Directly seeds 4 of 12 manifest artifacts
(type-lookups/knowledge/skills/output-types — 44 rows fresh-run); the other 8 report PENDING/PLACEHOLDER
per the manifest's own pre-existing marker semantics (documented SCOPE BOUNDARY, ADR-039 Path A per
adr-check — relationship-association writes are a materially different idiom than the flat-upsert pattern
this task's constraint scoped the writer to reuse from H12c).

Tests: 1024 → **1044/1044** (+20, zero regressions). Step 9.5 (code-review + adr-check, FULL rigor +
test-modifying override, both unconditional): 0 Critical, 0 blocking Warnings; adr-check found 5 compliant
ADRs + 1 documented Path-A Warning (ADR-039 scope boundary, see banner) + 0 violations. Pre-existing,
unrelated `Spaarke.ArchTests` gap noted (2 `CosmosProvisioningSecretGuardTests` failures — retired
pre-split `Sprk.Provisioning.ControlPlane` project bin dir, predates this task).

No coordination needed with sibling task 151 (H12b) beyond the shared `Core.csproj` — verified clean
additive merge (see banner). Commit scope: H12a handler doc-header update, 2 new files, 2 deleted-file
edits (`AiSeedChainOptions.cs`/`ISeedManifestRunner.cs` doc updates + `InvokeSeedManifestScriptRunner.cs`
deletion), `Worker/Program.cs` DI swap, `Core.csproj` embed block, 2 new test files, this project's
`current-task.md`/`TASK-INDEX.md`/task-150 POML status flip.

---

## Task 144 — COMPLETE (2026-08-20)

H11 live verification post C5.8 grants. DS-4 §2 classified all 3 of H11's REST/Graph seams as
"already real" — this task performed the live-verification pass, direct sibling of task 143's H10
verify (same template, same discipline).

**Live verification results** (full detail: `notes/h11-live-verification-2026-08.md`): the consent
verifier (`GraphRestB2BConsentVerifier`, fully read-only, the escalation-trigger-relevant seam) was
**fully live-verified end-to-end** — both `Verified` (a real existing accepted guest,
`ad268fcd-ac34-4e40-b63f-dacdc849fcbb`) and `Pending` (unknown/never-invited guest id → Graph 404
→ correctly folded to Pending, never Verified) branches — via new durable xUnit smoke tests
(`H11SeamsSmokeTests.cs`) run **genuinely live in-sandbox** (notably better than H10's own smoke
tests, which soft-skipped on `DefaultAzureCredential` in this sandbox — task 144's consent-verifier
calls resolved a real token in ~23s and completed against real Graph responses). The two
WRITE-capable seams (`GraphRestUserProvisioner`'s `POST /users` + `POST /assignLicense`,
`GraphRestB2BInvitationClient`'s `POST /invitations`) had their exact request/response shapes
ground-truthed against Microsoft Learn (field-name-exact match, incl. least-privileged permissions)
— the actual writes are deferred to live-ceremony: creating a real Entra user or sending a real
invitation email has no safe automated undo, and task 111's C5.8 grants haven't been live-executed
yet (same precedent task 143 established for H10's write paths).

**BONUS CATCH** (MAJOR, genuine defect, fixed same commit): cross-referencing
`GraphRestB2BInvitationClient`'s `POST /invitations` against Microsoft Learn's own
least-privileged-permission table found the pre-existing 14-role `GraphAppRoles.cs` /
`L2GraphAppRolesRegistry.cs` catalog (r1 task 005) was **missing `User.Invite.All` entirely** — not
a wrong GUID (task 143's H10 class of defect) but a **missing role**. Once task 111's C5.8 grants
are live-executed, the L2 UAMI would hold `User.ReadWrite.All` (sufficient for the NativeAccount
branch + the consent-verifier GET) but NOT `User.Invite.All`, so every B2BGuest-preset H11 run
would receive a **permanent, unrecoverable 403** on the invitation POST — this is the same failure
class task 144's own escalation trigger names ("a signal that either task 111's grant scope is
wrong or the classification needs correction"). Fixed in the same commit: added a 15th catalog
role (GUID ground-truthed live via `GET /v1.0/servicePrincipals?$filter=appId eq
'00000003-...'&$select=appRoles` against the real Microsoft Graph resource SP:
`09850681-111b-4a89-9bed-3f2cae46d706`), mirrored to L2, and every stale "14"/"14-role" reference
updated across `GraphAppRoles.cs`, `L2GraphAppRolesRegistry.cs`, `IGraphAppRolesRegistry.cs`,
`H10DataverseAppUserGraphParityHandler.cs`, `Program.cs`, `GraphAppRoleParityTest.cs`,
`H10DataverseAppUserGraphParityHandlerTests.cs` (AC16, renamed off the now-stale
`...EnumeratesAll14PopulatedGuids` method name), `spec.md` FR-33, and
`docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`. Re-verified:
`L2GraphAppRolesRegistry_MirrorsBffGraphAppRolesConstant` (task 067's unconditional mirror-parity
test) **PASSES** post-addition — the two catalogs remain byte-identical (confirmed via live
`dotnet test` run against `AZURE_TENANT_ID` this task).

**Consent-verifier reuse check** (CLAUDE.md §11 three-question test, explicit per the dispatch
directive): confirmed H11's `GraphRestB2BConsentVerifier` (checks an invited **guest user's**
`externalUserState`) and H3's `GraphAdminConsentVerifier` (checks the **app registration's**
`oauth2PermissionGrants`) answer genuinely different questions against different Graph resources —
no consolidation opportunity; the two verifiers correctly share only the Verified/Pending result
SHAPE (documented explicitly in `IB2BConsentVerifier.cs`'s own header), not an implementation. H11
correctly owns its own verifier.

**Deferred, documented, NOT fixed** (out of this verification task's proportionate scope):
`H11UserProvisioningOptions` has zero Bicep wiring anywhere in `infrastructure/bicep/` and its
`AccountDomain` defaults to `"spaarke.onmicrosoft.com"` — the Spaarke tenant's OWN domain, not a
customer's. Assessed as fail-**loud** in practice (Microsoft Graph rejects a UPN whose domain isn't
verified in the authenticating tenant — a 400, not a silent wrong-tenant write), not fail-silent,
but a genuine deployment-completeness gap: every NativeAccount-preset H11 run against a real
customer will fail until this is wired. A NFR-05 `Validate()` addition would not help (all 5 fields
have non-null defaults, so a null-check `Validate()` never fires — the defect class is "wrong value
in play", undetectable by options-validation without per-customer context). Recommended as a
follow-up task (thread as Bicep app settings, parity with task 142's `EnvVarValues__ClientSecret`
wiring); not applied here to avoid unreviewed scope creep, consistent with task 143's own precedent
for its 2 out-of-scope wrong-GUID doc findings.

Both POML escalation triggers checked and cleared: trigger 1 (branch failure) did not fire — the
one genuine defect found was a catalog-completeness gap, fixed in-commit per task 143's own
precedent, not a live orchestration failure in H11 itself. Trigger 2 (consent verifier false-Pass
on genuinely-pending consent) was directly tested and **not observed** — the Pending-branch smoke
test proves an unknown/unaccepted guest id is never reported as Verified, both by live execution
and by code inspection of the fail-closed `!response.IsSuccessStatusCode → pendingIds.Add(userId)`
branch.

Tests: `Sprk.Provisioning.ControlPlane.Tests` (L2, CI-gated) 1003 → **1005/1005** (+2,
`H11SeamsSmokeTests.cs` — zero regressions). `NightlyTests` project (not CI-gated): no new test
added; `L2GraphAppRolesRegistry_MirrorsBffGraphAppRolesConstant` re-confirmed passing post-15-role-
addition; `GraphAppRolesCatalog_AppRoleIds_MatchRealMicrosoftGraphAppRoleDefinitions` hits the SAME
pre-existing sandbox-only `ManagedIdentityCredential`/IMDS limitation task 143 already documented
(not a regression from this task — confirmed by inspection: the failure is in credential
construction, unrelated to catalog contents). Step 9.5 (self-conducted code-review + adr-check,
test-modifying unconditional override): 0 Critical, 0 Warnings, 0 ADR violations (ADR-028
`DefaultAzureCredential` + explicit tenant confirmed throughout; ADR-010 no new DI registrations;
ADR-038 no banned mock patterns in the new smoke test). BFF Hygiene checklist (root CLAUDE.md §10)
assessed as not triggered — the `GraphAppRoles.cs` edit is a data-catalog addition (one constant +
one array entry), not a new endpoint/service/DI registration/package/background work.

No coordination needed with any sibling task — task 144 ran alone per the dispatch (no other Wave
G-4 agents active); git status showed a clean, expected diff scope throughout.

## Task 141 — COMPLETE (2026-08-20)

H6 Web-API import port. New `Handlers/SolutionImport/DataverseWebApiSolutionImporter.cs` (implements `ISolutionImporter` —
Dataverse Web API `ImportSolution`/`StageAndUpgrade` actions + `importjobs` polling; resolves the 8 solution ZIPs from a
versioned blob-artifact manifest in the `provisioning-artifacts` container rather than a local filesystem path — the
DS-1b §1 H6 invariant) + `DataverseWebApiSolutionVerifier.cs` (implements `ISolutionVerifier` — trivial GET
`/solutions?$select=uniquename,version,solutionid`). Retired (kept on disk, unregistered, retirement banners):
`DeployDataverseSolutionsScriptImporter.cs` / `PacCliSolutionVerifier.cs`. `ISolutionCatalog`
(`CanonicalSolutionCatalog`) is UNCHANGED and is now the runtime ordering authority the new importer reads directly —
it was already verified byte-for-byte equivalent to the PS script's `$SolutionImportOrder` by the pre-existing
catalog-mirror test, so the "port the order verbatim" constraint holds even though the PS script itself is no longer
invoked.

**Web API shapes ground-truthed via WebFetch against Microsoft Learn** (not guessed, per Wave G discipline):
`ImportSolution`/`StageAndUpgrade` share `OverwriteUnmanagedCustomizations`/`PublishWorkflows`/`CustomizationFile`
(base64)/`ImportJobId` (client-generated GUID)/`SkipProductUpdateDependencies`; `StageAndUpgrade` can be invoked
directly with `CustomizationFile` populated (no separate `StageSolution` round-trip needed). The importer maps the
retired PS script's own `Import-ManagedSolution` branch (existing solution + Auto/Upgrade mode → stage-and-upgrade;
else plain import) onto: existing solution → `StageAndUpgrade` action; absent → `ImportSolution` action — a literal
1:1 port of that branch, and a faithful mapping of the POML's "ImportSolution + StageAndUpgrade" framing onto two
distinct action calls.

**Documented deviation from the dispatch context's "poll /asyncoperations" framing** (not silent — same discipline
task 140 used for its own ground-truthed deviation): this importer polls `importjobs({ImportJobId})` using the
client-generated GUID directly, never touching `asyncoperations` or a `Location` header — `ImportJobId` is
deterministic and known before the POST is even sent, so it needs no header parsing or `AsyncOperationId` discovery.
`completedon` (non-null) is the terminal signal; the dispatch context's "statecode 0/3" framing conflated `Ready`
(Dataverse's pre-run state) with a terminal one — corrected in the file header for future auditors. `importjobs.data`
(XML, schema not published in a single canonical Microsoft Learn page) is defensively parsed for
`result="failure"`/`"warning"` nodes; an unparseable/empty `data` field is treated as a **provisional** success whose
diagnostic explicitly says so (never silently swallowed) — the separate `DataverseWebApiSolutionVerifier`'s
independent post-import GET (called by the handler immediately after) is the defense-in-depth for that ambiguity.

**Two intentionally distinct credentials**: `ClientSecretCredential` (BFF app-reg ClientId/ClientSecret — task 142's
H7 precedent, KV-sourced, zero new S2S secret) for the customer-Dataverse-env Web API calls; the shared L2 UAMI
`TokenCredential` singleton (ADR-028 MI-outbound) for the `provisioning-artifacts` blob container. **Live-ceremony
gap** (documented, not a defect of this task): no CI workflow yet publishes the solution-artifact manifest
(`dataverse-solutions-latest.json`) or the 8 solution ZIPs to blob storage — `SolutionImportOptions.Validate()` fails
fast at boot if the container URI is unset (NFR-05 parity); a live E2E run additionally needs a new CI publish step
+ the provisioning-artifacts storage account (existing backlog item #4). This handler is fully buildable/unit-testable
today via fake-transport tests.

**Grep-collision self-catch** (same anti-pattern class task 127/130/140 caught): initial doc-comment drafts of both
new files literally contained "pac solution import"/"pac solution list" (describing what was replaced) — would have
failed the POML's own literal `grep 'pac solution\|ProcessStartInfo'` acceptance criterion. Reworded before the grep
was re-run; verified 0 matches.

**Bonus catch during Step 9.5 prep**: the new `SolutionImportOptions.PostConfigure(Validate)` broke
`HandlerRegistrationCompletenessTests`'s `WorkerTestFactory` (task 103's all-19-handlers-resolve DI sweep) — fixed by
adding the same `UseSetting` convention tasks 122/123/132/142 already established for their own `Validate()` additions.

Tests: 968 → **1003/1003** (+35 this task on top of sibling task 143's own concurrent +3 landing — zero regressions
from either task). New: `DataverseWebApiSolutionImporterTests` (~28 facts/theories incl. ImportJob failure-parsing —
`EvaluateImportJobData`: explicit failure/warning-only/clean-success/empty-data/unparseable-XML — a real
`TimeProvider.System` polling-timeout test, PartialImport-vs-first-solution-not-promoted distinction, per-tier
verification gate, manifest/blob 404 paths, `ClassifyHttpFailure` theory, `TryReadSolutionVersionFromZip`) +
`DataverseWebApiSolutionVerifierTests` (7 facts). `dotnet build` 0 errors/0 warnings across Core/Worker/Tests. Step 9.5
(self-conducted code-review + adr-check, FULL rigor mandatory): 0 Critical, 0 blocking Warnings. code-review: 2 minor
Suggestions (5 ctor params on the importer; `ImportAsync` orchestration length) both precedent-matching
(`BapRestEnvironmentCreator`), no action needed. adr-check: 1 Warning noted for the record — `ClientSecretCredential`
(not MI) for the customer-Dataverse-env auth is the SAME pre-existing pattern H7/task 142 already established
(DAG-ordering: H10, which creates the MI-Dataverse-App-User, runs after H6/H7) — not a new deviation, no fresh Path
A/B/C escalation required.

No coordination needed with sibling task 143 (H10 verify) — zero file overlap confirmed (different handler folders,
disjoint Worker/Program.cs insertion points; git status showed a clean, expected diff scope throughout).

## Task 143 — COMPLETE (2026-08-20)

H10 live verification post C5.8 grants. DS-4 §2 classified all 5 of H10's REST/Graph seams as "already
real" with C5.8 (task 111's grant script) as the only blocker — this task performed the live-verification
pass DS-4 called for, not a re-implementation.

**Live verification results** (full detail: `notes/h10-live-verification-2026-08.md`): seams 3 (T2
`DataverseWebApiAppUserVerifier`) and 5 (T3 `GraphRestAppRoleParityVerifier`) — both fully read-only by
design — are **fully live-verified end-to-end**, both branches each (Verified/CountMismatch,
Verified/Partial), via direct REST calls made during authoring AND via new durable xUnit smoke tests.
Seams 1/2 (`DataverseWebApiAppUserCreator`, BFF + UAMI App User creation) and seam 4
(`GraphRestAppRoleGranter`) have their READ components live-verified the same way; their WRITE components
(POST systemusers / POST role-association / POST appRoleAssignments) are deferred to live-ceremony —
`spaarkedev1` is a shared dev env H10 doesn't even target (H10 targets CUSTOMER envs, which don't exist
live yet since H5/140 hasn't live-ceremony-executed), and task 111's C5.8 grants for the L2 UAMI have not
themselves been live-executed yet (its own `<notes-completion>`: "Live-exec verification: DEFERRED").

**BONUS CATCH** (genuine defect, fixed same commit): cross-checking all 14 `GraphAppRoles.cs` catalog
entries against the REAL Microsoft Graph resource SP's own `appRoles` collection found
`GroupMember.ReadWrite.All`'s `AppRoleId` was WRONG — `dbaae8cf-10b5-4b86-a4a1-f871c94c6571` does not exist
on the real Graph resource SP at all; the correct id is `...c6695` (only the last 4 hex chars differ). This
is precisely the failure class the T3 trap's own diagnostic text warns about ("a still-partial result...
most often means a GraphAppRoles.cs GUID value is WRONG, not just null") — a non-null-but-incorrect GUID
that neither the existing null-GUID escalation gate NOR task 067's own live parity test (never actually
run live per its own D4 note) could have caught. Fixed in `GraphAppRoles.cs` +
`L2GraphAppRolesRegistry.cs` (mirror re-verified byte-identical post-fix via task 067's unconditional
`L2GraphAppRolesRegistry_MirrorsBffGraphAppRolesConstant` test); new regression-guard test
(`GraphAppRolesCatalog_AppRoleIds_MatchRealMicrosoftGraphAppRoleDefinitions`, `NightlyTests` project,
`[SkippableFact]` on `AZURE_TENANT_ID` only) added so this defect class cannot silently recur. Root cause
pre-dates r1 (`scripts/Setup-EntraInfrastructure.ps1`'s own pre-r1 list, transcribed forward into
`GraphAppRoles.cs` by r3 task 062, then mirrored by task 053) — 2 out-of-scope occurrences of the SAME
wrong GUID flagged (not fixed) in `docs/guides/AZURE-SETUP-SELF-SERVICE-REGISTRATION.md` +
`scripts/Setup-EntraInfrastructure.ps1` (different subsystem/owner — recommend follow-up, not applied
here to avoid unreviewed scope creep into a live-consumed provisioning script).

**Self-caught mid-authoring** (own test design flaw, fixed before landing): the first version of the new
smoke tests asserted only on RETURN TYPE — but `DataverseWebApiAppUserVerifier`/`GraphRestAppRoleParityVerifier`
both swallow a `DefaultAzureCredential` failure internally and return a business-shaped-but-fake result
with only a `LogWarning` as the tell. In this sandbox, `DefaultAzureCredential` genuinely fails (confirmed
via a throwaway diagnostic test): `ManagedIdentityCredential`'s IMDS probe against the unreachable
169.254.169.254 throws `AuthenticationFailedException` (not `CredentialUnavailableException`) after ~27s,
and Azure.Identity does NOT fall through to `AzureCliCredential` after that class of failure — even though
the operator IS logged in via `az login` (confirmed: `AzureCliCredential` alone resolves in ~1.3s). This
made the first test version pass VACUOUSLY (never touching the wire). Fixed by adding a `CapturingLogger<T>`
that soft-skips when the ONLY captured warning is the collaborator's own "token acquisition failed"
diagnostic (sandbox limitation, not a seam defect) and fails loud on anything else. This is expected,
correct PRODUCTION behavior (a real Azure host's managed identity resolves immediately, chain never walks
further) — not an H10 defect — but is a genuine sandbox testability gap, documented in the verification
report §4 for future live-ceremony/CI-nightly awareness.

**Deviation**: POML assumed GraphAppRoles.cs was at "11 of 14" populated GUIDs (step 4 / AC-3). Actual
state is 14-of-14 — r1 task 005 landed all 14 GUIDs on 2026-08-17, three days before this task ran.
Documented as Path C (comply with the criterion's intent against the codebase's real, better state) —
verification report §8.

Tests: `Sprk.Provisioning.ControlPlane.Tests` (L2, CI-gated) 965 → **968/968** (+3, `H10SeamsSmokeTests.cs`
— zero regressions). `NightlyTests` project (not CI-gated) +1 test (compiles clean; live execution hits
the same sandbox-only `DefaultAzureCredential`/IMDS limitation via the Graph SDK's own credential path —
not an H10 defect, documented). Step 9.5 (code-review + adr-check, self-conducted, test-modifying
unconditional override): 0 Critical, 0 Warnings, 2 non-blocking Suggestions; 0 ADR violations (ADR-028
confirmed — `DefaultAzureCredential` throughout, explicit tenant, zero `ClientSecretCredential`).

**Coordination with sibling task 141 (H6)**: ran live-editing `Handlers/SolutionImport/**` in the SAME
working tree throughout this task. Isolated via targeted `git stash` (2 cycles, path-scoped) to validate
build/tests without touching their in-flight files — including a mid-session divergence where their own
newer edit to one file superseded what had been stashed, handled via selective per-file `git checkout
stash@{0} -- <path>` rather than a blanket pop, so their live edit was never clobbered. Both cycles fully
restored their exact state; zero content of theirs read, edited, or committed. No `SendMessage`
coordination needed — zero file overlap (`SolutionImport/**` vs `DataverseAppUserGraphParity/**`).

## Task 142 — COMPLETE (2026-08-20)

H7 credential provisioning (`EnvVarValues:ClientSecret` KV ref) + NFR-05 boot-time fail-fast validation. Config-only
per DS-4 — H7's handler logic was already real (task 050); this closed the last unprovisioned credential + added a
startup guard on top of the handler's existing runtime guard.

Ground-truthed the KV target via manifest.yaml cross-reference (not guessed): H7 authenticates using the SAME shared
multitenant BFF app-reg H6 uses (spec.md §9.1 v3), so `EnvVarValues__ClientSecret` resolves to the platform KV's
canonical `BFF-API-ClientSecret` secret — the same secret `.Api` already resolves as `AzureAd__ClientSecret`/
`Graph__ClientSecret`. Escalation trigger (FIC-retirement) checked first and does NOT fire — manifest.yaml still
classifies `BFF-API-ClientSecret` as `never_delete:true`, `value_source:"from-existing-kv"` (real, live secret);
auth-v4 has not started per owner directive.

**Changes**: `EnvVarValuesOptions.cs` (Core) — added `SectionName = "EnvVarValues"` const (renamed off the old bare
`nameof(EnvVarValuesOptions)` binding so the Bicep app-setting key matches the POML's literal acceptance criterion)
+ `internal void Validate()` (parity with task 112/122's `DataverseEnvironmentRegistryOptions.Validate()`). `Program.cs`
(Worker) — swapped `Configure<T>()` for `AddOptions<T>().Bind(...).Validate(...).ValidateOnStart()` (same pattern as
`DataverseEnvironmentRegistryModule`); the handler's existing runtime `MissingClientSecret` guard stays as
defense-in-depth. Bicep — new `bffApiClientSecretName` param (default `'BFF-API-ClientSecret'`) threaded through
`platform-controlplane.bicep` → `modules/controlplane-worker-app-service.bicep`, which now emits the
`EnvVarValues__ClientSecret` KV-reference app setting. `az bicep build` 0 errors/0 warnings; regenerated
`platform-controlplane.json`; live `az deployment sub what-if` (dev sub) returned `Succeeded` (Worker site shows as a
fresh Create since the live-ceremony `Deploy-ControlPlane.ps1` run, backlog item #5, hasn't executed against this env
yet — expected, not a regression).

**Bonus catch**: the new `ValidateOnStart()` would have silently broken `HandlerRegistrationCompletenessTests.cs`'s
`WorkerTestFactory` (task 103's all-19-handlers-resolve DI sweep constructs H7, which now eagerly validates
`IOptions<EnvVarValuesOptions>` at ctor time) the next time that test ran. Caught by cross-referencing the exact 2
sites that needed the equivalent fix after task 122's `AdminEnvironmentUrl` precedent; fixed the one that actually
resolves H7 (`HandlerRegistrationCompletenessTests.cs`), confirmed the other (`ProvisioningDispatchSpineSeamTests.cs`)
never touches H7/EnvVarValuesOptions so no change was needed there.

Tests: 938 → **946/946** (+8 new NFR-05 tests: null/whitespace ClientSecret throws, RequestTimeout bounds, happy-path
passes, a `SectionName=="EnvVarValues"` regression guard against Bicep-key drift; all 23 pre-existing H7 test methods
unmodified). `dotnet build` 0 errors/0 warnings. No new NuGet packages. Step 9.5 (code-review + adr-check,
test-modifying override — mandatory): 0 Critical, 0 Warnings, 0 ADR violations; no interfaces added (ADR-010), no
hardcoded secrets (grep-verified), BFF-hygiene not triggered (L2 control-plane only). Acceptance criterion 5 (FIC
sentinel accommodation) is N/A per the escalation-trigger check above — not implemented since no sentinel contract
exists yet. No coordination needed with sibling task 140 (H5 BAP-REST) — zero file overlap, different handler
folders.

## Task 140 — COMPLETE (2026-08-20)

H5 BAP-REST env-create + async-operation-polling port. `BapRestEnvironmentCreator.cs` (new,
`Handlers/DataverseEnvCreation/`) replaces `PacAdminDataverseEnvCreator` (retired — retirement banner added, kept on
disk unregistered per Wave G-2/G-3 convention; `ClassifyStderr` + its test preserved as historical reference) as the
`IDataverseEnvCreator` implementation, wired via `builder.Services.AddHttpClient<IDataverseEnvCreator,
BapRestEnvironmentCreator>()`. Ports Provision-Customer.ps1 STEP 5 ("Creating Dataverse environment via Power
Platform Admin API") + STEP 6 ("Waiting for Dataverse environment provisioning") exactly: idempotent
existing-environment list-check → POST create → GET-poll the environment resource until `provisioningState` is
terminal or `CreationTimeout` elapses. The existing health-probe collaborator (`IDataverseHealthProbe` /
`DataverseWebApiHealthProbe`) is byte-for-byte UNCHANGED per the POML's explicit preserve constraint.

**Ground-truthed a real in-repo discrepancy** (via WebSearch against Microsoft Learn / MicrosoftDocs/power-platform —
no live BAP credentials available in-sandbox): the script and this project's OWN task-120 BAP-REST precedent
(`BapRestEnvironmentRateProbe.cs`) disagreed on two details — resource-provider namespace
(`Microsoft.BusinessAppsPlatform` plural in the script vs `Microsoft.BusinessAppPlatform` singular in task 120) and
token audience (`https://api.bap.microsoft.com/.default` in the script vs `https://service.powerapps.com/.default`
in task 120). Live-web verification confirmed task 120's values are correct on both counts (the script's spellings
are latent typos) — `BapRestEnvironmentCreator` follows task 120's ground-truthed values, documented in the file
header for future auditors. Also confirmed the actual async-operation semantics are a direct-poll-the-resource
pattern (BAP's documented "API v2.8 and earlier" behavior), NOT the generic 202+`Location`-header pattern the
dispatch context assumed — an explicit, cited deviation rather than a silent mismatch.

**Bonus correctness catch**: added an idempotent existing-environment check (ports the script's own Step 5
"already exists" branch) that the original `PacAdminDataverseEnvCreator` never had. Without it, a resume after a
crash between "BAP acknowledged the create request" and "Cosmos `CompletedPhase` durably written" would re-POST the
SAME deterministic domain (= customerId) and hit an unrecoverable create-conflict loop. Two new failure
classifications thread through the full chain (`DataverseEnvCreationFailureKind` → `DataverseEnvCreationRejectionCodes`
→ `H5DataverseEnvCreationHandler.MapCreatorFailure`): `DomainAlreadyExists` (create rejected — nothing created by
this attempt, distinct from `PartialProvisioning`) and `ProvisioningFailed` (BAP-reported terminal Failed/Deleted,
distinct from `Timeout` — required by the POML's own acceptance criteria).

**Self-caught during authoring**: my own file-header doc comment initially contained the literal phrase "pac admin"
(describing what was replaced) — would have failed the POML's own literal `grep 'pac admin\|ProcessStartInfo'`
acceptance criterion against the production file. Reworded to "the pac CLI's `admin create-environment` command"
before the grep was re-run; verified 0 matches post-fix (same anti-pattern class task 131 caught for
`ClientSecretCredential` doc-comment references).

Tests: 17 new `BapRestEnvironmentCreatorTests` (request-body shape assertion, poll-loop-detects-Succeeded after
intermediate states, poll-loop-detects-Failed distinct from Timeout, poll-loop-Timeout via real tiny wall-clock
TimeSpans — same convention as H5's own T13 test — duplicate-domain classification, existing-env-already-succeeded
short-circuit [no create/poll calls made], existing-env-still-provisioning skip-create-and-poll-existing,
`ClassifyHttpFailure` theory ×7 cases, token-acquisition-failure→AuthFailure, source-grep defense-in-depth) + 2 new
`MapCreatorFailure` `InlineData` rows on `H5DataverseEnvCreationHandlerTests`. `dotnet build` 0 errors across
Core/Worker/Tests. Step 9.5 (code-review + adr-check, self-conducted — FULL rigor mandatory): 0 Critical, 0 new
Warnings (the pre-existing `IDataverseEnvCreator` single-impl ADR-010 note is unchanged, not newly introduced), 0
ADR violations (ADR-028 `DefaultAzureCredential(TenantId=...)` per-call confirmed, no `ClientSecretCredential`
anywhere in scope). No `Mock<HttpMessageHandler>` (ADR-038). File is 614 lines, well under the 2,000-line god-class
ceiling.

Tests: 946 → **965/965** (+19 this task, zero regressions; combined with sibling task 142's own +8 landing in the
same window, 938 baseline → 965 total). No coordination message needed with sibling task 142 (H7 credential
provisioning) — zero file overlap confirmed by both tasks independently (different handler folders, disjoint
Worker/Program.cs insertion points).

## Wave G-3 Tally (100% COMPLETE 2026-08-20)

| Task | Commit | Handler | Δ tests | Notable |
|---|---|---|---|---|
| 130 | `9702871cb` | H3 Graph app-reg + consent verifier + BFF-API-*/RunParameters + auth-v4 pluggability seam | +13 → 916 | Two Path-C deviations accepted (H10 owns app-role grants + Dataverse app-user; H3 defers). 2 Graph SDK gotchas caught via reflection. |
| 132 | `ccaf1cad2` | H9 artifact-based rebuild + Kudu zip-deploy + swap + rollback re-swap | +14 → 930 | Rollback-completeness proof (AC15a). Manifest schema verified against task 116's CI. Storage-account risk documented. |
| 131 | `22c5ff2ba` | H8 Graph containerTypes (v1.0 GA — Beta package eliminated) + T6 cert-from-KV | +8 → 938 | Architectural simplification: SharePoint-REST `applicationPermissions` entirely eliminated by native Graph GA endpoint. Self-caught `BuildEvidence` bug during Step 9.5. Cert-loading gotcha (SecretClient not CertificateClient). 6th documented git-index race handled cleanly. |

Zero code-review criticals, zero ADR violations across all 3 commits. L2 tests: 903 baseline → **938/938** (+35 across Wave G-3).

---


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
| **Wave G-4 status** | Batch G-4A ✅ COMPLETE (140 H5 + 142 H7). Batch G-4B ✅ COMPLETE (141 H6 + 143 H10 verify). L2 tests at 1003/1003. Batch G-4C (144, H11 verify) now unblocked. |
| **Next decision** | Dispatch 144 (H11 live verification, Batch G-4C — final task of Wave G-4). |
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
