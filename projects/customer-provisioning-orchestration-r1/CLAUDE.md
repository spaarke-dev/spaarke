# customer-provisioning-orchestration-r1 — project operating manual

> Read with `current-task.md` (current state). Repo-wide rules are in root `CLAUDE.md`; this file holds only what is
> specific to this project. Template: `.claude/skills/project-setup/references/claudemd-template.md`. Rationale and
> superseded rules: `notes/decisions.md`. Previous version: `notes/handoff-history/CLAUDE-archive-2026-10-07.md`.

## 1. Scope and status

The customer-provisioning platform. An operator runs `/provision-environment {customerId}` under their own identity.
The skill drives the L2 control plane (REST API + Worker, `src/server/services/Sprk.Provisioning.ControlPlane.*`). L2
runs handlers H0–H14 over Service Bus + Cosmos and finishes with one customer's stamp at `Setup Status = Ready`.

Deployment model (UAC-r2 D-12/D-13, 2026-09-28; owner 2026-09-30): every customer gets a **dedicated stamp** with its
own subscription, resource group, Dataverse environment, BFF app registration and Redis. Model 1 stamps live in
Spaarke's tenant, and customer users are B2B guests. **Model 2 is out of scope** (plan D3): add no Model 2 work and
break no existing Model 2 path.

Out of scope:
- per-customer staging/dev (D6);
- BI;
- VNet (off for MVP);
- a provisioning web UI (follow-on: `notes/follow-on-customer-deployment-webapp-proposal.md`);
- data migration (`spaarke-data` CLI);
- registry-aware decommission and fleet management (r2).

Status: `tasks/TASK-INDEX.md` and `current-task.md`. The work now in progress is the Model 1 remediation in
`notes/model1-dedicated-remediation-plan.md`: §2 owner decisions D1–D29, §4 gaps, §7 task list (tasks without a POML
are created from it). Also `spec.md` · `design.md` · `plan.md`.

## 2. Binding rules for this project

Full list: `spec.md` "MUST Rules". These come up on most tasks.

**Architecture**
- Handlers implement the L2-local `IProvisioningHandler` and register in the L2 control plane, never the BFF. They take no compile reference to the BFF's `IJobHandler` (spec §5.2).
- `POST /api/runs` enqueues on Service Bus and returns 202; no handler runs in the HTTP path (FR-22). Dispatch uses `ServiceBusSessionProcessor`, `SessionId = CustomerId`, `MaxConcurrentCallsPerSession = 1` (FR-22b).
- Every handler input has one producer. The intake endpoint and the handler validate with the same shared code (`IntakeParameterCatalog`, `HandlerRunInputs`, the `*Intake` validators — `provisioning.md` "Run-context contract").
- The L2 main site never shells out to pwsh/az/pac; the only PowerShell path is the H14a Exchange sidecar.
- H9 deploys the BFF from the CI-published artifact, never `dotnet publish` at provision time (FR-12).
- One BFF app registration per customer, both models (D-13, binding). No per-customer Entra tenant.
- Tenant isolation I1–I5 (FR-28–FR-32): `provisioning.md` "Tenant-isolation invariants". Every app-only SPE call gets its Graph client from `SpeContainerOwnershipGuard`, never `.ForApp()` (T227d; `SpeAppOnlyContainerGuardTests`). This includes code that arrives in a master merge.
- Stamps are keyless (D13). Stamp Redis is Azure Managed Redis, Microsoft Entra only (D12). See `provisioning.md` "Stamp resources are keyless" and "Stamp Redis".
- SPE: one container type per model and one root container per customer. Never propose a container type per customer (D28). SPE Admin on a stamp reaches only that stamp's containers (D29). Container-type creation is delegated-only, an operator one-time step (topology doc §R5).
- Model 1 users are B2B guests in the environment security group `sprk-{customerId}-users`. Spaarke pays pay-as-you-go on the customer's stamp subscription; guest access must be on (D2, owner 2026-10-07; `provisioning.md` "Model 1 users").
- The customer's subscription and Dataverse environment are operator prerequisites; H5 adopts, never creates (D4, T228). Cost: one envelope per dedicated stamp (T229).

**Credentials and Key Vault**
- Read the current text of `provisioning.md` "KV credential lifecycle" before touching any secret. In short:
  - never create, seed or restore `BFF-API-ClientSecret` (either casing) or `Dataverse-ClientSecret` in secret-free environments;
  - before 2026-11-23, never delete `Dataverse-ClientSecret` and never purge soft-deleted rollback copies;
  - E-1 secrets are protected indefinitely.
- No secret or certificate on the Model 1 owning app `bfac7f6e`: L2 signs in with MI-FIC (D16). No secret, certificate or Entra directory role on `Spaarke Exchange Admin` (D24/D25).
- PATCH App Service `keyVaultReferenceIdentity` to the UAMI on both slots (trap T1).
- Use canonical KV and resource naming; the vault name is a Bicep parameter.
- Pre-check the live App Service, KV and Dataverse before removing any alias (FR-35).
- The operator authenticates with their own AAD identity, never a service principal (NFR-11).

**BFF-touching tasks**
- `.claude/rules/bff-hygiene.md` applies.
- Report the publish size and delta from fresh short-path worktrees of both sides (NFR-01).
- Run `/conflict-check` before every BFF PR.

**Process**
- Deferred work and newly found issues go through `/project-defer-issue-tracking`: `notes/defer-issues.md` AND a GitHub issue.

**ADR tensions approved here** (root §6.5; detail in `spec.md` "ADR Tensions", `design.md` §17):
- ADR-004 A — L2 orchestration is a custom state machine over Cosmos.
- ADR-027/028 A — one L2 identity holds Owner on every customer subscription (T228, owner 2026-10-06).
- ADR-027 management groups — A, deferred (G36, awaiting the owner).
- ADR-020 A — the `POST /api/runs` intake map rejects or requires keys (T245a/b/c, T225b).
- ADR-020 A — the stamp deployment `gpt-4o-mini` runs gpt-4.1-mini (T247).
- ADR-007 A — L2 calls Graph SPE APIs directly (T248).
- ADR-038 A — L2 test project location.
- ADR-028 — CLOSED 2026-10-08 (T235): ADR-028 Amendment A6 records D13 (keyless customer stamps); the dev/demo Document Intelligence key is A6's named dev/demo window, not a project exception.
- T254, owner-approved 2026-10-07:
  - `Set-AiSpendLimit.ps1` single-setting writes;
  - no `ValidateOnStart` for the AiSpendLimit options;
  - the 17th `SystemCacheKeys` entry `AiSpendMonth`.
- Withdrawn exceptions are listed in `notes/decisions.md` §2.

## 3. Owner directives and standing decisions

Rationale: `notes/decisions.md`. The owner's D1–D29 are in plan §2.

**How to work**
- Build the process, not the environment (2026-08-23). Never `pac admin copy` from another environment as a shortcut; every gap found is fixed here.
- End state: provisioning runs E2E with no human interaction (2026-09-01). L2 has no web UI in r1.
- T186 (first live E2E) goes through `/provision-environment`, never direct L2 REST calls (2026-08-30). Its purpose: build a brand-new environment to execute, test and confirm the end-to-end process (owner 2026-10-08).
- T218 defines the complete solution package and is a hard blocker for T186 (2026-09-28). All solutions ship to every customer (2026-09-01).
- Every environment, Spaarke's own included, imports only the canonical SpaarkeMaster built by CI from git — never solutions the dev team creates or manages by hand (owner 2026-10-08, #1401 → T218f).
- Every defect found is fixed in scope, or filed and reported. Review limits cap ceremony, never fixing (2026-10-06; task-execute Step 9.5).
- Fix what can be fixed now, including drift and broken CI elsewhere. Implement the owner's design and absorb the follow-on work.
- Decide a doubtful mechanism by necessity: needed → build it, otherwise remove it. Give one recommendation, not a menu of variants (2026-10-06).
- No Microsoft support case: find the root cause (2026-10-04).
- The Word/Outlook add-ins and the Teams tab are used only by Dataverse-licensed users, internal or B2B guest; external contacts never use them (owner 2026-10-07, T240). Coordinate add-in and Teams client changes with spaarkeai-word-add-in-r1 directly, or give the owner a message to relay.
- Production client sites are Spaarke-named custom domains on shared Static Web Apps in `rg-spaarke-shared-prod`: `addins.spaarke.com` (live) and `external.spaarke.com` (External Access SPA and Teams tab). Customer BFFs serve CIAM external contacts (T240d); Teams is for internal and workforce users only — external-contact Teams access is not a requirement, contacts use the browser SPA (owner 2026-10-07); customer staff (B2B guests) DO use the Word/Outlook add-ins, installed in their HOME tenant by the customer's IT (Integrated apps) as a standard onboarding step (owner 2026-10-07); the Teams tab drops its Teams-SSO fallback (owner 2026-10-07).

**Live actions**
- Every live Azure/Entra/Dataverse/Exchange change and every deploy needs the owner's OK, per action. Read-only checks are fine. Record each action in the task POML notes.

**Keep — never delete or alter without the owner**
- `rg-spaarke-shared-prod` (subscription "Spaarke Shared Production" `cd95fcec…`) and the `Spaarke Model 1` SPE billing account `Microsoft.Syntex/accounts` `dc4749c2-…` in it (D23). The billing binding is permanent and cannot be re-attached.
  - Keep the RG's Bicep tags as they are.
  - Put no lock on the RG: it would block T241's deletion of the 13 shared-tier resources inside it.
  - At T241's close, propose a resource-level `CanNotDelete` lock on the Syntex account only. It is a live action, so ask first (owner 2026-10-07: do what is technically required or helpful).
- `sprk-prod-kv`: not before 2026-11-23 (T241; KV lifecycle).
- Registry rows (`sprk_dataverseenvironment`): never delete, deactivate instead (T237).
- `Spaarke Exchange Admin` `46670ee2-ac0c-44b0-9ac2-d40ae4dcbdd7`, `Spaarke SPE Model 1 Owner` `bfac7f6e-9fa0-4664-8492-c7a1dfe73d5e`, and Graph Explorer's grant on `Spaarke Model 1` (2026-10-03).
- Exchange group `sprk-t251-spike-scope` (Entra `c709af95-0332-4ea2-a9d4-6925b1666bad`, member testuser1@), the test group for `Verify-Sidecar-Live.ps1 -InTenant` (2026-10-04). `Enable-OrganizationCustomization` has been run (irreversible).
- The dev BFF MI `mi-bff-api-dev` holds application `full` on the `Spaarke Model 1` container type `fb3817a8` (option A, 2026-10-06). Only the ownership guard and `SharePointEmbedded__OwnedContainerIds` on spaarke-bff-dev confine it.
- A spike/test cleanup never deletes Key Vault secrets; the `Exchange-Connect-Cert` sentinel stays. Planned deletions follow the KV lifecycle rule.

**Settled — do not re-litigate**
- D-13: a per-customer BFF app registration.
- D28: no container type per customer.
- No BI in MVP: a per-customer Power BI F-SKU comes later; do not procure it (2026-09-28).
- The M365 Copilot agent is per customer (2026-09-28).
- VNet stays off for MVP (2026-09-01).
- `SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md` is authoritative; never merge an SPE owning app with a BFF app registration (2026-08-30).
- Demo is the next environment for testing the orchestration (D27).
- The prod L2 control plane goes into `rg-spaarke-shared-prod` from its first deploy (D23).

## 4. Coordination

- **unified-access-control-r2.** Shares the BFF SPE/access code (`SpeContainerMembershipService`, `DriveItemOperations`, `UploadSessionManager`, `DemoProvisioningService`) and H8.
  - It owns the INCOMING doc §8 items (Secure Record rename, Redis subject keys); leave them alone.
  - Owed to us: the `sprk_noaccessentry` hand-off; T256 and T218 depend on it.
  - Owed by us: an acknowledgement of INCOMING-141/145 (accepted as T255/T256), and the §13.9(b) do-not-mint note (`notes/coordination/2026-10-06-uac-r2-task165-13-9b-mint-secret.md`). Ask the owner how to deliver each.
- **sdap-SPE-admin-app-r2.** SPE Admin as the BFF identity (master `bb8ba7251`) probably supersedes T250; raise it with the owner when T250 is reached.
- **spaarke-auth-v4-dataverse-MI** (archived). Owns `Dataverse-ClientSecret` retirement after 2026-11-23.
- **spaarkeai-compose-r8** task 063. The Blob keyless proof stays `not-in-use` until that task lands.
- **`.github/workflows/**`.** ci-cd-unit-test-remediation-r1 is closed (owner 2026-10-02); this project changes its own provisioning workflows. Check `projects/INDEX.md` for an active CI-governance project first.
- **spaarkeai-word-add-in-r1.** Owns the Office add-ins and `@spaarke/auth`'s OfficeNaaStrategy use. Agreed 2026-10-07: a Spaarke directory endpoint (T240c) instead of client-side `/me/memberOf`; a dev diagnostics build for T240b. Messages: `notes/coordination/2026-10-07-*word-add-in-r1*`.
- **spaarke-SPA-external-access-platform-r3.** Owns the External Access SPA, which is also the Teams tab (`src/client/external-spa/appPackage`). Answered 2026-10-07 (`notes/coordination/2026-10-07-from-external-access-r3.md`); our reply `…-to-external-access-r3-2.md`. It added the requirements (`external.spaarke.com`, Teams via NAA with its own client app, no SSO fallback, Spaarke-tenant workforce authority, per-customer CIAM) to its design.md and starts execution 2026-10-07. T240d is designed with it.
- **Hot paths:** BFF, skill directives, CI workflows. Registry: `projects/INDEX.md`.

## 5. Environment and live actions

- Dev BFF `spaarke-bff-dev`: deploy only from master at or after `c8b93b294`. Dev Redis, its alerts and App Insights `spe-insights-dev-67e2xz` are in `spe-infrastructure-westus2`, not `rg-spaarke-dev`.
- The L2 control plane deploys with `scripts/provisioning/Deploy-ControlPlane.ps1`; see the gotcha below. Current blockers: `current-task.md` owner items.
- Operator documents:
  - prerequisites: `docs/guides/PROVISIONING-PREREQUISITES.md` + `scripts/provisioning-prereqs/prereqs.yaml` (`validate.ps1`);
  - operator guide: `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`;
  - resources per environment: `docs/architecture/SPAARKE-ENVIRONMENT-RESOURCE-INVENTORY.md`.
- Azure OpenAI model pins need a refresh before about 2027-01-14 (`PinnedModelCatalog`, T247).
- `runs/trial1-intake.json` (gitignored) predates D-12; do not use it. T186's target customer is not yet defined.

## 6. Gotchas — do not re-learn

**Naming and process**
- Three "D" decision series exist: design.md D1–D20, plan §2 D1–D29 (owner) and UAC-r2's hyphenated D-12/D-13/D-14. Always name the series (2026-10-07).
- After every master merge, grep the BFF for `.ForApp(` and run `SpeAppOnlyContainerGuardTests` (2026-10-07). A clean auto-merge can lose the ownership guard: master's task 171 added app-only twins and shared `*CoreAsync` bodies, and the merge spliced our guard line into a shared core, where it shadowed the OBO client.
- `sdap-ci.yml` is not a required check and its jobs are `continue-on-error`. A gate that must block goes in `ci-tier1-blocking.yml` (Router) (2026-10-06).
- Before declaring a per-run H13 check fixed, ask where its inputs live at runtime. The Worker publish has no `scripts/` or `infrastructure/`, and the host has no pwsh or pac (2026-10-06).
- A prereq recipe's tokens must resolve at the step that runs its scope; `validate.ps1` checks documentation only (2026-10-06).
- Check Microsoft's per-feature region table before defaulting a regional AI resource to the stamp location. Content Safety defaults to westus (2026-10-06).

**Shell, az, pac**
- Never `az account set`; pass `--subscription`. To target another subscription without touching the shared context, use a private `AZURE_CONFIG_DIR` copy and delete it afterwards (2026-10-04).
- Never run destructive az commands as a "clean slate": `az account clear` wiped the credential cache (2026-08-23).
- pac: never run a command with flags to "check its usage". On 2026-08-23 that executed `pac admin create-service-principal`. Use a no-argument call or Microsoft's docs; az `--help` is safe. Traps: `pac admin create` silently appends a digit to a taken domain, and `create-environment` is not the command.
- Windows `az.cmd` breaks on `)` in an argument (e.g. `RetrieveCurrentOrganization(...)`). Use `az account get-access-token` + `Invoke-RestMethod` (2026-10-07).
- Git Bash rewrites a leading `/subscriptions/...` argument; `export MSYS_NO_PATHCONV=1` first (2026-10-06).
- Another tenant (the external test org Dewey Cheatham `deweycheatham.onmicrosoft.com`): sign in with a fresh private `AZURE_CONFIG_DIR` and `az login --tenant <domain> --allow-no-subscriptions --use-device-code`; the interactive browser login may never surface, and `| Select-Object -Last N` hides the device code until the command ends (2026-10-07).
- "Create tenant" in the Entra admin center offers Workforce or External: an External ID tenant cannot run Microsoft 365 ("B2C and External ID users are not supported in Microsoft 365 Admin center"). An external ORGANIZATION for tests is a Workforce tenant; the Microsoft 365 trial sign-up creates one (2026-10-07).
- `az ad app permission admin-consent` fails ("Consent validation failed"). Consent Graph app roles with `POST /servicePrincipals/{graph}/appRoleAssignedTo` (2026-10-02).
- Granting an MI on an SPE container-type registration: Graph v1.0 `PUT /storage/fileStorage/containerTypeRegistrations/{ct}/applicationPermissionGrants/{appId}` via `Connect-MgGraph -Scopes FileStorageContainerTypeReg.Manage.All`. `Set-SPOApplicationPermission` fails for an MI (2026-10-06).
- `Deploy-ControlPlane.ps1` via `pwsh -File` stops at the ConfirmImpact=High prompt. Run it in-process with `-Confirm:$false`; `-SkipBuild` reuses a build (2026-10-04).

- The Dataverse MCP `read_query` returns at most 20 rows unless you pass `top`. Two wrong conclusions on 2026-10-07 ("the Console is in no Spaarke solution", "no Spaarke Basic User role") came from capped results. Pass `top`, or use the solution-authoring scripts (2026-10-07).
- The session's primary working directory can switch to the main repo (`C:\code_files\spaarke`) mid-session. Use absolute worktree paths in every command (2026-10-07).

**Dataverse packaging**
- Only roles of the ROOT business unit can be packaged ("Subcomponent … cannot be added … root component Role is missing"). A role contained in a child unit by design (Secure Record Owner) is created per environment by provisioning (H7b), never shipped (2026-10-08).
- `RemoveSolutionComponent`'s `SolutionComponent` reference takes the COMPONENT's id (objectid), not the solutioncomponent row id (that fails 0x8004f021) (2026-10-08).
- `Export-SpaarkeMasterSource.ps1` needs an output folder ≤ 140 characters: past the Windows path limit the unpack is silently incomplete (2026-10-08).
- An attribute PUT does not change a LOCAL option set's description, and a metadata GET right after a PUT can return the cached old value — re-read with header `Consistency: Strong` (2026-10-08).
- The authoring copy of SpaarkeMaster had read 1.0.0.0 although 1.1.0.0 was shipped (2026-08-21): set versions with `Assemble-SpaarkeMasterSolution.ps1 -Version` (must be higher), never trust the dev number alone (2026-10-08).
- `AddSolutionComponent` with `AddRequiredComponents = true` drags whole Microsoft tables into SpaarkeMaster (five on 2026-08-23). Add with subcomponents and no dependencies; the F12 leak is caught at export (`Export-SpaarkeMasterSource.ps1` fails on a missing dependency on `solution="Active"`) (2026-10-07).
- Solution import from code: use `ImportSolutionAsync` / `StageAndUpgradeAsync` and poll `asyncoperations`. The synchronous actions hold the request for the whole import and outlive HttpClient's 100 s default (2026-10-07).

**Code and tests**
- Pester 3.4 under pwsh: a bare `Should Throw` (no message) always fails — always pass the expected message (2026-10-08).
- A script with `SupportsShouldProcess` passes `-WhatIf` to `New-Item`/`Remove-Item` too: create local scratch folders with `-WhatIf:$false`, or the dry run itself fails (2026-10-08).
- The .NET configuration binder appends to an initialised list. Apply a list default after binding (e.g. `EffectiveGuestSecurityRoleNames`) (2026-10-07).
- To separate a timeout from caller cancellation, catch with `ex is not OperationCanceledException || !ct.IsCancellationRequested` (2026-10-07).
- A process-wide `Meter` leaks between tests running in parallel. Select your own measurement by a tag unique to the fixture (2026-10-07).
- `string.Create(IFormatProvider, …)` rejects `$"" + $""`; format with `ToString("F2", CultureInfo.InvariantCulture)` (2026-10-06).
- A Bicep `@description('…')` string must not contain an apostrophe (2026-10-06).
- Pester for `tests/scripts/*.Tests.ps1`: `Import-Module Pester -RequiredVersion 3.4.0`. 6.x rejects `-Script` / `Should Be`, so `Auth-V4-Operator-Script-Gates.Tests.ps1` fails 27/27 under 6.2; it is not in CI (2026-10-06).

**Git and editing**
- Before merging this branch to master, list master's push-triggered workflows for the changed paths: `deploy-spaarke-ai` deploys the Console to DEV on any master push touching shared UI paths; `publish-provisioning-arm-artifacts` uploads to `sprkcpartifactsdev` (2026-10-08).
- `Router` is the only required check: merge only when EVERY PR check is terminal and green, not when Router passes (2026-10-08).
- Write edit scripts with the Write tool; bash heredocs fail on quoting. In Python use `'''` when the text holds `"` before `"""` or C# raw strings, and restrict line-prefix replacements to the intended line (2026-10-06).
- Parallel sub-agents share the git index (2026-08-19):
  - commit with `git commit --only <paths>`, never `git add -A` / `git add .`;
  - dispatch prompts must say "commit AND push on success";
  - index-race recovery: `git reset --soft`, then a tagged `git stash push --keep-index -m <tag>`.
- Never bare `git stash`, never stage `.husky/_/*`, never `--no-verify`. A fresh worktree needs a root `npm install` before the pre-commit hook works.
- The pre-commit hook skips lint-staged on a merge commit. After resolving conflicts by hand, run `dotnet format <csproj> --include <files>` on those files. Its LF→CRLF "WHITESPACE/ENDOFLINE" reports are line endings only; check with `git diff --ignore-cr-at-eol` (2026-10-07).

## 7. Key documents

- `spec.md` · `design.md` (§4D tenant isolation, §17 placement + tensions) · `plan.md` · `notes/decisions.md` · `notes/model1-dedicated-remediation-plan.md` · `INCOMING-D12-D13-REMEDIATION.md` (reference) · `COMPONENT-INVENTORY.md`
- Rules: `.claude/constraints/provisioning.md` · `.claude/patterns/provisioning/INDEX.md` · `.claude/skills/provision-environment/SKILL.md`
- Architecture: `docs/architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md` · `docs/architecture/SPAARKE-ENVIRONMENT-RESOURCE-INVENTORY.md`
- ADRs: 004, 007, 009, 010, 013, 020, 027, 028, 032, 036, 038, 039, 044, 052 (`.claude/adr/`)
- Related projects: unified-access-control-r2 · sdap-SPE-admin-app-r2 · spaarke-auth-v4-dataverse-MI (archived) · code-quality-and-assurance-r3 (r3 hand-off: `notes/r3-handoff.md`) · superseded `spaarke-environment-factory-r1`
