# Current Task State — `customer-provisioning-orchestration-r1`

> **Format**: CURRENT state only, REWRITTEN at each checkpoint (≤ 10 KB) — never prepend. Standing rules → project `CLAUDE.md` §2 "Binding rules", §3 "Owner directives", §6 "Gotchas" (one dated line each). Decisions + superseded rules → `notes/decisions.md`. Session narrative → checkpoint commit messages. History: git + `notes/handoff-history/` (do not load on recovery). Review limits: task-execute Step 9.5.

> **Last Updated**: 2026-10-08 SESSION 45 — **PR #1365 MERGED** (`1ef1b0949`, 22:05Z) after fixing 2 CI test failures; master verified. Waiting on owner items 2 and 3.

## 🎯 Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **218f**'s last acceptance check (AC2), after the first SpaarkeMaster publish (owner item 2). |
| **Step** | SESSION 45: CI on `93c805fbd` failed 2 tests, both fixed in `2c5cac232` (merged at `73e83d50f`) — DataverseRecordShareWireTests (master's uac-r2 test used the pre-guard SpeContainerMembershipService constructor) and LoadTests EnqueueLatencyScenario (NativeAccount Model1 intake, refused since T232). Master merged again (9 commits, clean); solution build 0 errors; ArchTests 841/841; UAC tests 231/231; LoadTests 5/5; `.ForApp(` 31. CI on `73e83d50f`: 33 pass, 7 skip, 0 fail. Trial merge with 13 newer master commits clean, no new `.ForApp(`. **Merged `1ef1b0949`.** Master verified: build 0 errors, ArchTests 841/841, `.ForApp(` 31, guard present. Push-triggered runs on master (Deploy SpaarkeAi → DEV, Publish Provisioning ARM Artifacts, CI, Bicep, prereqs) were queued at 22:06Z. |
| **Status** | blocked on the owner (items 2, 3). |
| **Next Action** | (1) Owner OK → run `publish-dataverse-solutions-manifest.yml` on master with `publish: true`; watch it to green (manifest read-back). (2) 218f AC2 (below), close 218f, close #1401 (ISS-005 → Resolved), sync the board. (3) Owner OK → delete the `gh-pull_request` FIC on `8c85a481-…` (#1446), close ISS-007. (4) Check the master push runs listed above finished green. (5) Then T250. |
| **Branch** | `work/customer-provisioning-orchestration-r1`, merged into master at `1ef1b0949`; 0 behind. |
| **Main repo** | `C:\code_files\spaarke` master NOT fast-forwarded (still `b0a78f880`): another session's uncommitted researcher-memory edits (`.claude/agent-memory/researcher/MEMORY.md` + 2 files) block it. Left untouched — the owner decides. |
| **Order after the merge** | first SpaarkeMaster publish (owner OK) → 218f close → T250 → 213.7/207/208/209 → **T253** (G38) → **T255** (INCOMING-141; also H3's `acct` claim) → **T256** (waits on #1364; H7b also CREATES the Secure Record Owner role) → T240b (owner re-test, add-in 1.1.2) → **T240c (approved 2026-10-08)** → T240d → T257 → **T186** (first run: a NEW environment to execute/test/confirm E2E). |

### Files changed this session (all committed and pushed)
- Package: `src/dataverse/solutions/SpaarkeMaster/**` (1.2.0.0 export), `docs/data-model/package-scope.json`, `docs/data-model/spaarke-components-inventory.json`.
- Scripts: `scripts/solution-authoring/{SpaarkePackageScope.psm1, Assemble-…, Export-…, Import-SpaarkeMasterPackage.ps1 (new)}`, `scripts/Deploy-Release.ps1`, `scripts/Provision-Customer.ps1`, `scripts/Load-DemoSampleData.ps1`; `scripts/Deploy-DataverseSolutions.ps1` deleted; `config/environments.json` (`solutionPackageType`).
- CI: `.github/workflows/publish-dataverse-solutions-manifest.yml` (rewritten). Infra: `infrastructure/bicep/modules/controlplane-app-service.bicep` (+ JSON).
- Tests: `tests/scripts/SpaarkePackageScope.Tests.ps1` (39), ControlPlane `H11DefaultGuestRolePackagedTests`, parser fixture `Fixtures/spaarkemaster-manifest.sample.json`.
- Governance/docs: ADR-028 A6, ADR-027 MUSTs, skills `provision-environment` + `deploy-new-release`, constraints `provisioning.md`, CHANGELOG, many docs (T235 sweep, RAG guidance).

### Critical context
- **218f AC2 (the only open check):** after the first publish, run `./scripts/solution-authoring/Import-SpaarkeMasterPackage.ps1 -EnvironmentUrl https://spaarke-demo.crm.dynamics.com -PackageType unmanaged -WhatIf` → must print "Update SpaarkeMaster 1.0.0.0 -> 1.2.0.0 (unmanaged …)" and the pac command. Then mark 218f ✅ (POML + TASK-INDEX + drift check) and close #1401 (move ISS-005 to Resolved).
- **The store today** (`sprkcpartifactsdev/provisioning-artifacts`): only the hand-made 2026-08-21 `dataverse-solutions-latest.json` (SpaarkeMaster 1.1.0.0, old shape) + `dataverse-solutions/2026.08.21-h3-manual-1/SpaarkeMaster.zip`. H6 and the import script both refuse it until the CI publish.
- **Demo:** SpaarkeMaster 1.0.0.0 **unmanaged** + hand-installed dev-team solutions → `solutionPackageType` unmanaged (switch = rebuild, owner decision). Dev: `none` (authoring).

## Owner items

1. **PR #1365 — MERGED** 2026-10-08 (`1ef1b0949`). Its push runs deploy the Console to DEV and publish ARM artifacts to `sprkcpartifactsdev`; production only by manual dispatch.
2. **First real SpaarkeMaster publish** (after the merge): `publish-dataverse-solutions-manifest.yml` on master with `publish: true` — uploads 1.2.0.0 + manifest, keeps the hand-made manifest as `latest.previous`. Needed by T186 and 218f AC2. **Ask for OK.**
3. **#1446 (ISS-007)**: delete the unused `gh-pull_request` federated credential on `github-actions-spe-infrastructure` (`8c85a481-…`) — verified 2026-10-08 that no workflow signs in on pull requests. Entra write. **Ask for OK.**
4. **T240b live re-test** with add-in package 1.1.2 (owner, test tenant Dewey Cheatham `deweycheatham.onmicrosoft.com`; guest `ralph@deweycheatham.onmicrosoft.com`; Diagnostics view should show `acct` 1).
5. **G36** (ADR-027 management group), **G31** (H10 tenant-wide Directory/User write roles) — awaiting decisions; do NOT act.
6. Board Status "Active" vs Status Reason "On hold" on Issue #438.
7. Relay handed to the owner 2026-10-08: `notes/coordination/2026-10-08-to-uac-r2-2.md` (Secure Record Owner stays contained; H7b creates it — UAC-r2's design is right).

## Cross-project deliveries (track until delivered)

- **UAC-r2**: answers owed on #1364 (`sprk_noaccessentry` — now ships in SpaarkeMaster 1.2.0.0; re-export if its schema changes) and the 2026-10-08 Secure Record Owner note; #1363 fix.
- **word-add-in-r1**: first production deploy to `addins.spaarke.com` waits for our T240c (their task 114).
- **external-access-r3**: owes its Teams client app id and the T240d CIAM design session.

## Live state

- Dev control plane (`rg-spaarke-platform-dev`, sub `484bc857…`): Bicep `s44b` Succeeded; Worker + Api deployed from `35c20287c`, healthy; Api staging slot now carries the same settings as production (fixed `21b96bf1e`).
- Prod client sites (`rg-spaarke-shared-prod`): `addins.spaarke.com`, `external.spaarke.com` — Ready, empty.
- Dev BFF `spaarke-bff-dev` runs branch build `0911515d7`; future dev BFF deploys only from master. Dev BFF MI holds application `full` on the Model 1 container type (owner option A).
- Filed: #1376 (ISS-003), #1377 (ISS-004), #1401 (ISS-005 → closes with 218f), #1432 (ISS-006 RAG `Dedicated` dead mode — BFF), #1446 (ISS-007 PR OIDC subject).

## Open items (no task yet)

- `RoutingConsumerTypeHealthCheck FAILED: AI catalog drift` on the dev BFF — reported, no outcome recorded.
- BFF MI lacks `SecurityEvents.Read.All` (§6B Security tab). No alert on `failed_open_auth` (Prompt Shield).
- Dev Redis memory trend — recheck `performanceCounters | where name has 'Private Bytes'` on `spe-insights-dev-67e2xz`.
- Latent: the BFF does not boot without `AzureOpenAI:Endpoint` + `ChatModelName`; stamps always set both.
- `sprk_tenancymodel` local option-set description still has pre-D-12 text (Web API cannot update it; cosmetic).

## T186 (first live E2E) — open questions + live checks

- Target customer undefined after D-12/T228: needs its own subscription, Dataverse environment `spaarke-{customerId}`, container type id. Do NOT use `runs/trial1-intake.json`.
- H12a seeds 4 of 12 artifacts (playbooks + consumers pending — task 150).
- Live checks: (T228) H5 WhoAmI as the Worker; H1 RG listing under Owner; H6 sign-in right after H10. (T218b) async import + poll against a real environment; managed import over a fresh environment. (T227e/g/d) env-var definitions read; marker PATCHes; `businessunit.sprk_containerid` PATCH. (T251) W1 group Name vs DisplayName. (T230b) keyless proof all `proved`, E-2 measurement. (T240a) H3 client-access PATCH + adoption check; stamp BFF CORS literals.

## Notes for later tasks

- T250 likely superseded by master `bb8ba7251` (SPE Admin as the BFF MI) — raise with the owner when reached.
- T242c re-scope per D27 (demo = next env; `rg-spaarke-demo` has no AI Search; demo lacks `AiSafety__ContentSafety__Endpoint`).
- T241: shared tier's leftovers; owner decision first on `spaarke-model1-prod` (holds SpaarkeMaster 1.0.0.0 managed — never upgrade it; components dropped from the package would be deleted).
- Parked: T246 a–f, T247 (a)–(c) (pin refresh before ~2027-01-14), T244, G34, T252.
