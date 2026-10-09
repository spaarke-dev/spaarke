# Current Task State — `customer-provisioning-orchestration-r1`

> **Format**: CURRENT state only, REWRITTEN at each checkpoint (≤ 10 KB) — never prepend. Standing rules → project `CLAUDE.md` §2 "Binding rules", §3 "Owner directives", §6 "Gotchas" (one dated line each). Decisions + superseded rules → `notes/decisions.md`. Session narrative → checkpoint commit messages. History: git + `notes/handoff-history/` (do not load on recovery). Review limits: task-execute Step 9.5.

> **Last Updated**: 2026-10-09 SESSION 45 — parallel waves (owner: "get through this project"). Wave 1 merged: T253, T256, T206, T207, 204e, punch list 203b/204a/204f/204g; T208 + T209 done; T250 superseded. Wave 2 running: T258 + T255 (background agents).

## 🎯 Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **Wave 2** — agents running in isolated worktrees: **T258** (stamp BFF startup settings, 204e F5–F8; T186 blocker) and **T255** (workforce tenant list + H3 `acct`; INCOMING-141). Main session merges each as it reports. |
| **Step** | Wave 1 all merged + verified on the branch (solution build 0 errors / 0 warnings in touched projects; ControlPlane 2423/2424; ArchTests 888; LoadTests 5/5; recipe Pester 39/39; validate.ps1 OK; catalog -Verify OK). Applied every agent-proposed `.claude/` edit (provisioning.md: H7b + no-shell-Worker sections, handler count 21, DAG; bff-extensions §F.5; provision-environment skill: Step 0.5b `Invoke-PrereqPass`, NEW Step 1e-ter customer pass, H7b dry-run intake; manifest-driven-secret-catalog pattern). Follow-ups done here: PRQ-T-05/T-06 retired; bash-resolution bug (WSL stub in WindowsApps) fixed; H9 dead Deploy-Release.ps1 scan removed; CA2024 fixed. Filed: ISS-008 #1484 (run guard off), ISS-009 #1485 (E11 bind-by-email), ISS-010 #1486 (guests in root BU read secure records), ISS-011 #1487 (BFF gate asymmetries). |
| **Status** | in-progress — waiting for T258 + T255 agent reports (notifications arrive automatically; do NOT relaunch). |
| **Next Action** | For each report: `git merge <agent-branch>`; resolve (both touch manifest.yaml + generated/); apply proposed `.claude/` edits; build + ControlPlane + ArchTests (WITH build — `--no-build` ran a stale ArchTests dll twice) + LoadTests + `Invoke-CatalogGenerator.ps1 -Verify` + validate.ps1; mark POML/TASK-INDEX; commit + push. Then: PR to master (its CI runs the new router `prereqs` job). Owner-blocked: T240b re-test → T240c; T240d + T257 design notes (review); ISS-010 placement decision (customer BU — recommend H10); ISS-008 live step (run guard); T242c / T252 live. Then T186. |
| **Branch** | `work/customer-provisioning-orchestration-r1`, pushed; 0 behind master; carries the 218f close-out + checkpoints not yet on master (next PR). |
| **Main repo** | `C:\code_files\spaarke` master NOT fast-forwarded (still `b0a78f880`): another session's uncommitted researcher-memory edits (`.claude/agent-memory/researcher/MEMORY.md` + 2 files) block it. Left untouched — the owner decides. |
| **Order** | T250 → 213.7/207/208/209 → **T253** (G38) → **T255** (INCOMING-141; also H3's `acct` claim) → **T256** (waits on #1364; H7b also CREATES the Secure Record Owner role) → T240b (owner re-test, add-in 1.1.2) → **T240c (approved 2026-10-08)** → T240d → T257 → **T186** (first run: a NEW environment to execute/test/confirm E2E). |

### Files changed this session (all committed and pushed)
- Package: `src/dataverse/solutions/SpaarkeMaster/**` (1.2.0.0 export), `docs/data-model/package-scope.json`, `docs/data-model/spaarke-components-inventory.json`.
- Scripts: `scripts/solution-authoring/{SpaarkePackageScope.psm1, Assemble-…, Export-…, Import-SpaarkeMasterPackage.ps1 (new)}`, `scripts/Deploy-Release.ps1`, `scripts/Provision-Customer.ps1`, `scripts/Load-DemoSampleData.ps1`; `scripts/Deploy-DataverseSolutions.ps1` deleted; `config/environments.json` (`solutionPackageType`).
- CI: `.github/workflows/publish-dataverse-solutions-manifest.yml` (rewritten). Infra: `infrastructure/bicep/modules/controlplane-app-service.bicep` (+ JSON).
- Tests: `tests/scripts/SpaarkePackageScope.Tests.ps1` (39), ControlPlane `H11DefaultGuestRolePackagedTests`, parser fixture `Fixtures/spaarkemaster-manifest.sample.json`.
- Governance/docs: ADR-028 A6, ADR-027 MUSTs, skills `provision-environment` + `deploy-new-release`, constraints `provisioning.md`, CHANGELOG, many docs (T235 sweep, RAG guidance).

### Critical context
- **The store** (`sprkcpartifactsdev/provisioning-artifacts`): `dataverse-solutions-latest.json` → SpaarkeMaster 1.2.0.0 (managed + unmanaged, SHA-256); `latest.previous` = the hand-made 2026-08-21 manifest (rollback pointer). H6 and `Import-SpaarkeMasterPackage.ps1` now accept it.
- **Demo:** SpaarkeMaster 1.0.0.0 **unmanaged** + hand-installed dev-team solutions → `solutionPackageType` unmanaged (switch = rebuild, owner decision). Dev: `none` (authoring).

## Owner items

1. **Done 2026-10-08:** PR #1365 merged; first SpaarkeMaster publish; #1446 FIC deleted.
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

- Target customer (owner 2026-10-09): run 1 is a MOCK customer whose home tenant is Dewey Cheatham (`deweycheatham.onmicrosoft.com`, paid M365 Business — not a trial); its users are B2B guests from there. The stamp itself (subscription, Dataverse environment `spaarke-{customerId}`, BFF app registration, `sprk-{customerId}-users`) is in Spaarke's tenant; container type id still needed. A second customer tenant is created only for run 2 (cross-customer isolation: ISS-003 #1376, ISS-004 #1377, T255's per-stamp tenant list). Do NOT use `runs/trial1-intake.json`.
- H12a seeds 4 of 12 artifacts (playbooks + consumers pending — task 150).
- Live checks: (T228) H5 WhoAmI as the Worker; H1 RG listing under Owner; H6 sign-in right after H10. (T218b) async import + poll against a real environment; managed import over a fresh environment. (T227e/g/d) env-var definitions read; marker PATCHes; `businessunit.sprk_containerid` PATCH. (T251) W1 group Name vs DisplayName. (T230b) keyless proof all `proved`, E-2 measurement. (T240a) H3 client-access PATCH + adoption check; stamp BFF CORS literals.

## Notes for later tasks

- T250 likely superseded by master `bb8ba7251` (SPE Admin as the BFF MI) — raise with the owner when reached.
- T242c re-scope per D27 (demo = next env; `rg-spaarke-demo` has no AI Search; demo lacks `AiSafety__ContentSafety__Endpoint`).
- T241: shared tier's leftovers; owner decision first on `spaarke-model1-prod` (holds SpaarkeMaster 1.0.0.0 managed — never upgrade it; components dropped from the package would be deleted).
- Parked: T246 a–f, T247 (a)–(c) (pin refresh before ~2027-01-14), T244, G34, T252.
