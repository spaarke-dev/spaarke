# unified-access-control-r2 — project operating manual

> Read with `current-task.md` (current state). Repo-wide rules are in root `CLAUDE.md`; this file holds only what is specific to this project. Guidance: `.claude/skills/project-setup/references/claudemd-template.md`. Restructured 2026-10-07; the previous file is archived verbatim at `notes/handoff-history/CLAUDE-archive-2026-10-07.md`, and superseded items are in [`notes/decisions.md`](notes/decisions.md).

## 1. Scope and status

Spaarke had two disjoint authorization systems sharing a data resolver and nothing else. This project unifies them into ONE evaluator returning `(recordId → rights)`, makes every BFF route authorize the record it acts on, and builds secure records, Restricted records, the No Access list, Assigned-To grants and broker-only document access (SPE) end to end. The parent→child access cascade falls out of the model.

**Out of scope:**
- AI-search trimming for contacts (finding A-21 → AI/indexing owner);
- field-level visibility, break-glass, the organization-hierarchy cascade, GDPR erasure of grant rows;
- the BU restructure itself (UAT/environment work);
- D-12/D-13 code remediation and the `tenantId` rename (cpo-r1's, 2026-09-28).

Status: see `tasks/TASK-INDEX.md` and `current-task.md`. Spec: `spec.md`. Design: `design.md`. Plan: `plan.md`.

## 2. Binding rules

**The facts every decision rests on** (design §4):
1. **Wherever a read goes through the BFF, the BFF filter is the ENTIRE security boundary** (app-only reads; Dataverse row security is inert). Ask "does this read go through the BFF?", never "is this the MDA?": embedded PCFs reading via the BFF are as exposed as the SPA (2026-08-25 disclosure, `notes/decisions.md`).
2. **A lookup reference grants ZERO access in Dataverse.** Access comes only from ownership, role privilege, team membership, a share (POA) or the user hierarchy.
3. **Dataverse has no per-record deny.** Isolate by scoping the baseline and granting additively; never "restrict a row".
4. **A `contact` is not a security principal** (no share, no impersonation), so the contact plane computes access.
5. **"No Access" is a VETO, never a level** (`max()` would ignore it).

**Evaluator** (FR-20, FR-22, FR-24; design §5):
- Additive terms union with highest-wins: `max(dataverse-answer, explicit-grant, derived-member, org-expansion, inherited)`.
- Then the vetoes apply, in order:
  1. the deny list (ethical wall + per-child revocation) → None;
  2. Restricted (`sprk_accesspermission` = 100000002) → None for every contact, and for system users flagged external (round 67);
  3. Secure (`sprk_issecure`) suppresses derived and org terms BEFORE the max, for every principal kind.
- A licensed system user gets Dataverse's own answer (impersonated read) ∪ contact grants; an unlicensed customer employee or an external contact gets `sprk_assigned*` ∪ org, with no business unit.

**Records:**
- *Core* records (project, matter, work assignment, service request) need direct grants.
- *Child* records (invoice, communication, document, event, to-do, analysis) inherit **1 hop** through a denormalized core-ancestor stamp. Matter does not inherit from project.
- "Core" ≠ "externally grantable": a service request is core but **never** grantable to a contact. Do not add a service-request root set (task 028 note).

**Hard gates — do not merge without these:**
- **NFR-04:** the negative canary. An impersonated low-privilege read returns a strict subset AND strictly fewer rows than app-only; equality fails the build. Task 034 is the blocking merge gate for 036.
- **NFR-05:** no security role reaches the `Secure Record` BU.
- **NFR-01:** fail closed everywhere (ADR-003). An unknown id and a denied id get the same answer.
- **NFR-03:** caps are visible ("Only 5,000 records displayed"); never silently under-grant.
- **FR-07 → FR-29:** delegation ships before any "+ User" grant button.
- **NFR-06:** BFF publish size ≤ 60 MB, measured per BFF-touching task against a FRESH master build (root `CLAUDE.md` §10 → `.claude/rules/bff-hygiene.md`).
- **Every BFF route declares its authorization** (round 9 item 3; `RouteAuthorizationGuardTests`):
  - a record check, an admin policy, or a reasoned waiver;
  - a sign-in-only route fails the build;
  - a route with no caller in the repo and in no published API description is DELETED, not fixed (round 10 item 1).
- **Run the integration suites in full before a PR:** `tests/integration/Sprk.Bff.Api.IntegrationTests` AND `Spe.Integration.Tests`. Router does not run them.

**How work is done here:**
- Authorization-path tasks are `<rigor>FULL</rigor>`; executors, fixers and verifiers are pinned to Opus (memory `agent-model-selection`).
- **Findings:** task-execute Step 9.5 "Finding triage and review scope" (the owner's rule, rounds 56/59/74). Every defect found is fixed in scope or filed AND reported, whatever its origin; limits cut ceremony, never fixing.
- **Reuse, don't fork:**
  - impersonated reads: `Spaarke.Dataverse/DataverseImpersonation.cs` + `RetrieveMultipleImpersonatedAsync` (refuses `Guid.Empty`);
  - the gate: `Infrastructure/ExternalAccess/AccessibleRecordSetService.cs`;
  - POA: consolidate on `IDataverseAccessGrantService` (no third client);
  - child scoping: `ExternalModuleRegistry` `ScopeDimension` + `Tier2ScopeFilterInjector`;
  - the eligibility rule: `InternalShareEndpoints.ClassifyEligibility` / `IsBarredOnRestricted`, the ONE rule for every share writer;
  - rights checked AS THE USER: `CallerRecordAccessProbe`;
  - document filing: `sprk_related{type}` lookups (legacy direct lookups retire); readers follow BOTH families through `DocumentLinkFields` (2026-09-04).
- Parallel safety and the per-PR BFF obligations: §4.

**ADR tensions approved for this project** (root §6.5):
- Path B, applied: ADR-003 (task 030); ADR-028 A2, Dataverse's answer replaces the ADR-034 derivation (031); ADR-034 per-surface allow-list (040) and A4, Assigned-To as removable Collaborate grants (round 11).
- Path B, **approved, not yet merged:** 036's ADR-034 amendment (system-user ACCESS on SPA/Teams = Dataverse's answer; round 75). The ADR text merges with or before 036's PR.
- Path B: the AI tools' "User-OBO ONLY" rule yields to the G5 pattern and the inline re-stamp (rounds 7, 8, 13).
- Path A: 143 reuses `IScheduledJobLease` as the per-record mutex (ADR-036 A1-7 / ADR-052 §5; round 10 item 6).
- The 1-hop cap needs no exception (the ancestor stamp makes every chain one hop).

## 3. Owner directives and standing decisions

The full log is in `notes/session27-owner-decisions-and-research.md` (numbered rounds; earlier decisions in `notes/design-register.md` and `notes/decisions/`). Index and superseded items: [`notes/decisions.md`](notes/decisions.md).

**Working rules:**
- **2026-10-10 (round 90, BINDING): wrap-up mode.** Fix only findings that directly impact uac-r2's objectives or are significant breaking bugs on a real path (security and data loss in our code count). Everything else becomes a GitHub issue with evidence and is reported, not a new task. A task closes when merged, deployed and its server-side live gate passes; owner browser checks stay on the checklist and don't hold it open.
- 2026-10-01/03: continue autonomously; stop only for a genuine owner decision (one batched question round, recommended first). On a partial-option escalation the main session decides the complete fix and records it as a round.
- 2026-10-06 (round 75): ADR decisions inside 036's scope that are clearly consistent with this project's objectives are approved by the main session and recorded. Anything not clear-cut goes to the owner.
- 2026-10-06: terminology: say "save the email as documents" (the archive route), never "Save to SharePoint".
- 2026-09-07: don't absorb other surfaces' work (Compose, CI gates) or hand work to a mid-execution project; the 27 unbuilt solutions are fixed per surface, never by a CI workflow.
- 2026-09-10: no Dataverse test in CI (live assertions are manual gates); a failed revoke gives the user a message, never a bare 500.

**Access model:**
- **D1/C9 (2026-09-30):**
  - a system user with access in Dataverse has it in Teams/SPA;
  - a contact gets exactly the records granted, at the granted level;
  - "Created By" decides who a record is FOR (briefing, notifications), never who can OPEN it.
- **Grant Access (C4, rounds 2-3b):**
  - Write → may share;
  - Collaborate/Full carry grant-access; View does not;
  - a manual grant is capped at the grantor's own level;
  - a contact grants only to its own organization's contacts, at or below its level;
  - **Assigned-To auto-grants are always Collaborate and uncapped** (ADR-034 A4).
- **Restricted** = internal use only: no contact-based access, no system user flagged external (rounds 2, 67). **Secure** = its own named owner team and container; for contacts only named direct grants count.
- **`sprk_isexternal`** (on `systemuser` only):
  - only a stored true is external; blank is internal, everywhere (round 67);
  - on a Restricted record a flagged user is refused a share, and existing shares are removed; a secure-only record may be shared with them, e.g. licensed outside counsel (round 78);
  - **Restricted wins over the last-reader rule:** the removal reports `no-internal-reader` for an admin (round 76);
  - B2B guests are flagged by `scripts/Set-ExternalFlagForB2BGuests.ps1`, run BEFORE the BFF deploy.
- **Accepted gap (round 77):** a flagged user can still read a NON-secure Restricted record in their own business unit through role depth.
- **`sprk_issecure`** is field-locked and changed only through the endpoints (round 2):
  - securing needs Write;
  - unsecuring needs Full Access or being the creator (F3, round 3b), and only on a record with no parent (round 84);
  - a work assignment or project filed under a secure parent is itself secure (round 6);
  - ~~unsecuring a parent leaves its secure children secure~~ REPLACED by round 84: unsecuring a parent unsecures its filed children.
- **A secure record always has at least one user who can see it** (S5): inbound mail with an unknown secure parent is held, and unshare and No Access can't remove the last internal reader. Exception: round 76 (Restricted wins).
- **Ownership is team ownership, never user ownership** (D-11, round 5):
  - record-first, so the parent's BU default team owns it;
  - otherwise the creator's BU team;
  - a secure parent's records go to the named "Secure Record Owners" team.
  - App-created rows record the person in `sprk_createdbyperson`.
  - **G5 pattern** (rounds 3b/7/9): check the caller's rights AS THE USER, then write AS THE APP.
- **Access changes take effect in minutes:** immediately on save, plus the "Update Access" ribbon, with background jobs ≤5 min as the safety net (R3/R4). The MDA Share/Unshare mirror to children is ≤2 min (round 11).
- **Child inheritance:**
  - a child under two secure roots gets the INTERSECTION of their sharees (round 11);
  - invoices follow their matter (round 10 item 11);
  - communications inherit the parent's access permission (round 2 Q6).
  - To Do, Event, Communication and Document show their parent's `sprk_accesspermission`, written once in the shared stamp path and kept in step by the reconcile; a parentless child keeps its own value, recorded only, no new enforcement (round 81; owner may revisit).
- **A child's access always follows its parent, both ways, locked while it has a parent** (round 84; replaces round 6 item 4's "no unsecure cascade"): a filed work assignment / project takes the parent's `sprk_issecure` and `sprk_accesspermission`; To Do / Event / Communication / Document take `sprk_accesspermission`; most restrictive across parents; enforcement uses the parent-derived value (computed from the filing chain, fail closed) until the stored flag catches up; a parentless record keeps its own. **Refined by round 87 (2026-10-09): the parent sets a FLOOR, not an equality.** A child inherits the parent's values; a user may set a child STRICTER by hand (same permissions as today; removing the extra strictness needs F3) but never looser than the parent; when the parent loosens, only INHERITED values follow it down, and values set on the child stay; a re-file never loosens; the lock covers Secure + Access Permission only (people grants and shares stay).
- **No Access** applies to internal users on secure records (Q4); a record filed under a secure parent counts as secure for this, whatever its own flag says ("the parent permissions control", round 82). It is enforced only when the entry's author has Write (N5), and the record is hidden in Teams/SPA (N2).
- **Notifications** target Created By and Assigned To; no fan-out to a team (round 2 item 9).

**Documents (SPE):**
- **Broker-only for every container** (round 69): the BFF checks Dataverse, then reads and writes bytes app-only, for system users and contacts alike. Contacts never get an SPE permission.
- **Office edit** (round 70):
  - internal users (not flagged external) are standing WRITERS on the environment/BU container, kept in sync;
  - secure containers have no standing members, only JIT writer grants for users with Write on the record, removed when Write goes away.
  - Accepted: non-secure documents are reachable through Office/SharePoint by any internal user.
- **Round 72:**
  - `sprk_graphitemidbound` (field-locked copy) must equal `sprk_graphitemid`, or the pointer is refused;
  - share-links are refused on secure and Restricted records;
  - "Modified by" = the BFF app for app-only writes is accepted.
- **Upload binding:** an app-uploaded file attaches only for the user the BFF recorded as its uploader (171 hotfix, PR #1353).

**Reconciliation:** `ExternalAccessReconciliationJob` writes on dev since round 79 (2026-10-08, after a 0-change report); it was report-only until then (round 7). R4 deactivates grants whose record is gone (round 71). In any other environment, explain plainly and confirm before setting `ExternalAccess:Reconciliation:WritesEnabled` or `Communication__OwnershipHoldAlertUserIds__0`.

## 4. Coordination

- **customer-provisioning-orchestration-r1 (cpo-r1):**
  - hand-offs INCOMING-141 (workforce tenant list) and INCOMING-145 (H7b Secure Record setup) were delivered on #1094 (2026-10-06); track until acknowledged;
  - production invariants (users and the BFF app user in the customer's child BU; Secure Record BU a sibling of it) are theirs (round 5);
  - 🔴 do NOT run `Repair-SpeConfigSecretName.ps1 -MintClientSecret` on `bfac7f6e`: cpo-r1 D16 uses MI-FIC and ADR-028 A4 forbids new secrets; #1313 owns the secret-less config.
- **spaarkeai-word-add-in-r1/r2:**
  - shares Office routes, `sprk_document`/`sprk_todo` team ownership (D-11) and #1081;
  - #1011 is fixed here (task 172) with no Office access change;
  - answer them through GitHub issues/comments, because peer sessions hold incoming messages for their owner.
- **SPA-external-access-platform r1/r2/r3 and teams-app-r1:** share the BFF access surface; run `/conflict-check`.
- **Running agents:**
  - Agent-tool agents may be resumed with SendMessage;
  - a running *workflow* agent is answered by appending to `NOTE-FROM-MAIN.md` in its worktree (never committed); see memory `workflow-agent-messaging`;
  - agents sharing one worktree never edit TASK-INDEX/current-task, never run git, never run solution-wide `dotnet`.
- **Issues filed for others under the defect rule:** see `notes/defer-issues.md` and `current-task.md`.
- **Workflow resume:** re-invoke the SAME script with `resumeFromRunId`. Caching is prefix-ordered, so when a pooled/DAG script's call order varies between runs, write a continuation script that embeds the done results.
- **Parallel safety:**
  - `parallel-safe:false` for `Infrastructure/ExternalAccess/**`, `Api/ExternalAccess/**`, `Spaarke.Core/Auth/**` and `Spaarke.Dataverse/DataverseWebApiService.cs`;
  - `.claude/**` edits are main-session-only;
  - tasks touching `AccessGrantModal.tsx` serialize;
  - 036 runs alone (opus/xhigh), never in a wave.
- **Every BFF-touching PR:** state the Placement Justification (`.claude/constraints/bff-extensions.md`), run the CVE check, and run `/conflict-check` (the surface is shared with SPA-external-access r1/r2/r3 and teams-app-r1).

## 5. Environment and live actions

- **Dev only:**
  - Dataverse `https://spaarkedev1.crm.dynamics.com`;
  - BFF `spaarke-bff-dev` in `rg-spaarke-dev`;
  - BFF app ids `5967251e-171c-46fe-a6c2-ef843c90309d`, `1e40baad-e065-4aea-a8d4-4b7ab273458c`.
  - Its records are test records.
- **Live actions need the owner's OK.** Gate approvals are recorded per round (e.g. rounds 4, 11, 69/70/72). Agents never write Entra or Key Vault, never change app settings, and print setting NAMES only. Never change SPE container-type registration.
- **No prod Key Vault for dev work (owner, round 85, 2026-10-08):** agents never read secrets from `sprk-prod-kv` (or any non-dev vault) for dev checks or gates. Use dev credentials and the existing dev automation; if a dev gate needs a credential that only lives in prod, stop and ask.
- **An app-setting change restarts the BFF:** it's an owner decision, unless it's part of an approved rollout (e.g. 171's `DocumentPointer__ItemIdBoundBackfillComplete`).
- **Deploy:**
  - **the BFF:** `pwsh -File scripts/Deploy-BffApi.ps1 -Environment dev -AppServiceName spaarke-bff-dev -ResourceGroupName rg-spaarke-dev`, from a FRESH short-path worktree of `origin/master`;
  - **web resources:** `scripts/Deploy-WebResourceInline.ps1`, then read back and compare the hash;
  - use `pac.cmd` (the bash `pac` shim does nothing) and run PowerShell scripts from inside `pwsh`.
- **Branches:**
  - task and fix work happens in fresh short-path worktrees from `origin/master`;
  - `work/unified-access-control-r2` holds notes and tasks and is merged from master periodically (last 2026-10-07);
  - PRs squash-merge on Router green; delete a branch only with no check pending.
- **Test identities:**
  - testuser1@spaarke.com: non-admin, Spaarke Business Unit 1, systemuser `8d7bad7a-e39e-f011-bbd3-7c1e5217cd7c`, token via `AZURE_CONFIG_DIR=C:/tmp/az-uac-child`;
  - `uac.child.user@demo.spaarke.com`: impersonated reads only;
  - Secure Record Owners team `6eabc7f9-13be-f111-a05b-0022482913fc`.
  - Test passwords are never stored in the repo.
  - Don't relocate users to fix BU reach. Root-BU reach is an accepted dev artifact (rounds 4/5).
- **Never touch:**
  - secure project `65a3fab2…` (except a gate that says so);
  - the `Dataverse-ClientSecret` / `BFF-API-ClientSecret` secrets (never delete);
  - `C:\wtD\scripts\logs\` (batch-4 deploy backups).
- **Held by the owner:** the Power BI workspace id stays unset (reporting answers 503); `PowerBi__ClientSecret` stays a plain setting.

## 6. Gotchas — do not re-learn

**Background jobs (2026-10-09):** run ONE polling/merge job at a time. Four in parallel exhausted the page file (Win32 1455), and bash could not fork. A job from a previous session can SURVIVE a restart and still merge or deploy, so check `ps -ef | grep merge` before launching. Never start a job with a bare `&`; use run_in_background. Re-check a PR with `git merge-tree` before merging: master moves fast, and a conflicting PR gets no CI.
**Shared machine (2026-10-08):** many sessions and agents run tests on this machine at once. NEVER kill processes machine-wide (`taskkill /F /IM testhost.exe`, `dotnet build-server shutdown`, killing `dotnet`/`node` by name): it interrupts other sessions' runs. Kill only a PID you started, or wait for the lock to clear. A Dataverse form `GET` returns the PUBLISHED XML: publish before reading back a form write (task 153's script, PR #1473).

**BFF base URL in form scripts (2026-10-09, #1488):** `sprk_BffApiBaseUrl` on dev ends in `/api`. A web resource MUST normalize it with `replace(/\/+$/, "").replace(/\/api$/i, "")` before appending `/api/...` or passing it to `Spaarke.BffAuth` (which appends `/api/config/client`); otherwise every call is `/api/api/...` → 401. Our banner, Assigned-To and No Access scripts shipped without it and failed silently on dev for days, because the live gates called BFF routes directly. **A live gate for a form script must exercise the form path, or at least the script's URL resolver against the real env var value.** Also: `Deploy-WebResourceInline.ps1` puts a NEW web resource only in Default/Active, not SpaarkeMaster. Follow it with `AddSolutionComponent` (type 61, `SpaarkeMaster`) and read the membership back (#1492).

**Dataverse Web API:**
- A lookup is `_x_value` in `$filter` AND `$select` (G-13); a test double matching on query text copies the code's mistake.
- `RetrievePrincipalAccess` is refused on organization-owned tables (0x80040800); ask the table privilege.
- Revoking the current owner's own share needs `MSCRMCallerID` = owner (0x80040223).
- An unshared user's impersonated RetrievePrincipalAccess answers 403 **0x80048306** = no access (2026-10-07).
- `/$count?$filter` and `$skip` are refused; use `$count=true` + `$top` and `@odata.nextLink`.
- PATCH by id must send `If-Match: *`, or a bad id CREATES a row. A keyed PATCH with `If-None-Match: *` answers 404.
- `sprk_related*` casing is not uniform, and `@odata.bind` is case-sensitive; verify every schema name with a query that succeeds (2026-09-04).
- `mcp__dataverse__create_table` has no solution parameter: create via the Web API + `MSCRM.SolutionUniqueName`, then assert the prefix (AP-13).
- The privilege cache lags role edits: re-probe ≥3 times.
- A grant's "today" is the UTC date, which rolls over in the local evening.
- An `az` CLI Graph token 403s on SPE container permissions; use the BFF's identity. `roleprivilegescollection` cannot `$expand=roleid`; filter per role.

**SPE:** app-only uploads make Graph `createdBy` the BFF app, so anything comparing it to a person must use the server-side upload binding (2026-10-07).

**Builds and tests:**
- `Sprk.Bff.Api.Tests` silently vanishes from a root `dotnet test` when it fails to build; for interface changes, build `Spaarke.sln`.
- `tests/integration/{auth,seam}/**` are globbed into the UNIT csproj; prove a filter with `--list-tests`.
- Never pipe a command whose exit code you need, and never `--no-build` after an unguarded build. Chain a push AFTER a build/test only with `&&` (2026-10-07: a failed build was pushed).
- Concurrent suites give timing flakes; launch long suites with `run_in_background`, never `&`.
- After a master merge, rebuild the merged result: a clean textual merge can still break the build (114 × 171, 2026-10-07).
- **Client:** build `@spaarke/sdap-client` and `@spaarke/auth` before typechecking or running ui-components jest; run jest from inside the package. `ApiError` has `statusCode`, not `status`; an injected `authenticatedFetch` either throws or returns the raw response, depending on the host.
- A perturbation that doesn't compile is INVALID. Commit before perturbing.
- A build that breaks after a clean needs `dotnet restore` + a plain rebuild; no MSBuild property overrides.
- Re-read a task's `parallel-safe` against its CURRENT scope: source-disjoint packages can still share a build (`dist/`, barrel).
- Verify a POML's or comment's premises against code and live metadata; never trust `<dependency status>` attributes.

**Git and tooling:**
- The pre-commit hook stashes unstaged changes and reformats files: never commit where a build or perturbation is in flight, and re-run suites after the hook.
- Don't push to a PR branch while its CI run is in flight (cancel-in-progress kills the verdict). Merge before `/context-handoff`, whose commit re-triggers the gate.
- If agents' `current-task.md` copies conflict on a merge, keep the orchestrator's.
- The LFS-locks pre-push failure is transient: retry (never `--no-verify`, never disable `locksverify`).
- Repo-wide rules not repeated here: no bare `git stash` / `git add -A` (environment rules); publish from SHORT paths (`.claude/rules/bff-hygiene.md`); FAILURE-MODES G-13, G-16, G-17.
- In a fresh worktree run root `npm install --ignore-scripts` first, or lint-staged kills `dotnet format`; `src/client/pcf` needs `node_modules` for the ESLint hook.
- Remove a `node_modules` junction with `cmd /c rmdir` before `git worktree remove`.
- Python needs `PYTHONIOENCODING=utf-8`; scratchpad PowerShell stays ASCII under `pwsh`.
- Count TASK-INDEX by its ASCII tokens (`[open]` `[wip]` `[done]` `[cancelled]` `[deferred]`); every row carries one.
- Azure.Identity needs `AZURE_TOKEN_CREDENTIALS=dev`; the 034 canary needs `AzureCliCredential` + `SPAARKE_TESTS_ALLOW_OUTBOUND=1`.

## 7. Key documents

- `spec.md` (FRs/NFRs) · `design.md` (the model) · `plan.md` · [`notes/decisions.md`](notes/decisions.md) · `notes/defer-issues.md`.
- `notes/session27-owner-decisions-and-research.md`: the numbered decision rounds.
- `notes/design-register.md`: findings, decisions and prerequisites (§A–I).
- `notes/investigation/10-finding-confirmations.md`: per-finding evidence.
- `notes/investigation/08-option-b-feasibility.md`: the impersonation mechanism.
- `notes/task-NNN-*.md`: each task's full record.
- `notes/batch4-live-gates-2026-10-06.md`, `notes/batch5-live-gates-2026-10-07.md`: the live-gate records.
- `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md`: the invariant registry.
- `SECURE-DOCUMENTS-BUILD-PLAN.md`.
- Applicable ADRs: 002, 003, 008, 010, 028 (A2, A4, A5), 034 (A1–A4), 036, 038, 049, 052.
- Related projects: customer-provisioning-orchestration-r1, spaarkeai-word-add-in-r1/r2, SPA-external-access-platform-r1/r2/r3, teams-app-r1.
