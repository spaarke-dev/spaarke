# Procedure-Surface Changelog

> **Forward-only from 2026-05-14.** No back-fill from history.

This file tracks changes to the agent-procedure surface — `.claude/skills/`, `.claude/agents/`, `.claude/settings.json`, `.claude/patterns/`, `.claude/constraints/`, `.claude/FAILURE-MODES.md`, and the root `CLAUDE.md`. Git history covers everything; this file is the **curated** view that a human (or future agent) can scan to answer "when did skill X change?" or "when did hooks last get fixed?" without bisecting commits.

Format follows [Keep a Changelog](https://keepachangelog.com/) conventions.

---
###### 2026-10-09 — ADR-027 management groups implemented (T262)

`customer-provisioning-orchestration-r1` T262 (G36).

- **ADR-027 concise**: implementation note on "MUST use Azure Management Groups" (`spaarke-environments` →
  `spaarke-customers`, Audit/DoNotEnforce built-in policy, PRQ-S-06).
- **`/provision-environment`** Step 0.5b: new `{customerManagementGroupId}` token (must resolve); Step 1e-ter notes
  PRQ-S-06's read on the management group.

###### 2026-10-09 — provisioning: H3 keeps an L2 Worker FIC on each customer BFF registration (ISS-015)

`customer-provisioning-orchestration-r1` ISS-015 (#1524).

- **`.claude/constraints/provisioning.md`** §Stamp BFF clients: H3 keeps two FICs — `spaarke-uami-trust` (stamp BFF UAMI)
  and `spaarke-l2-worker` (L2 Worker UAMI principalId) — so H6/H7/H7b sign in as the registration secret-free; adoption
  accepts exactly those two names.
- **`.claude/adr/ADR-028-spaarke-auth-architecture.md`** FIC cap note: two FICs per Spaarke-tenant customer BFF
  registration, not one. No secret created, changed or deleted.

###### 2026-10-09 — provisioning: control plane secret-free by default (T252)

`customer-provisioning-orchestration-r1` T252.

- **`.claude/constraints/provisioning.md`** §KV credential lifecycle rule 1: the L2 control plane defaults to the
  secret-free Worker chain (`requireSecretFreeIdentity=true`); `Seed-PlatformKeyVault.ps1` no longer seeds the
  `BFF-API-ClientSecret` / `Dataverse-ClientSecret` sentinels. No secret created, changed or deleted.

###### 2026-10-09 — Agent cost controls: Sonnet sub-agents, concurrency caps, earlier compaction (agent-cost-controls-r1)

Owner direction 2026-10-09 after estimated spend rose to $1–2.5k a day. The number of calls had grown about 20×, 65–92% of them from sub-agents (mostly Opus, inherited from `"model": "opus"`). Windows ran out of memory from the parallelism.

- **`.claude/settings.json`:**
  - `env` gains `CLAUDE_CODE_SUBAGENT_MODEL=sonnet`, `CLAUDE_CODE_MAX_CONCURRENT_SUBAGENTS=4` and `CLAUDE_CODE_WORKFLOW_MAX_CONCURRENT_AGENTS=4`.
  - New top-level `workflowSizeGuideline: "small"` and `autoCompactWindow: 400000`.
  - The main-session `model` is unchanged. A per-call `model` still overrides the sub-agent default, for planning and reviews.
- **`.claude/constraints/agent-cost.md`** (new, binding), with the evidence table and the settings reference (docs confirmed 2026-10-09). Seven rules:
  1. sub-agents default to Sonnet;
  2. one top-tier review per change set;
  3. small, scoped agents;
  4. don't resume an idle agent;
  5. concurrency, including one or two heavy projects per machine;
  6. earlier compaction;
  7. research fans out once.
  Indexed in `.claude/constraints/INDEX.md`.
- **Root `CLAUDE.md`:**
  - §8.5 gains an "Agent cost" bullet.
  - §16 names the new settings.
- **`.claude/FAILURE-MODES.md`:** G-19.

---
###### 2026-10-08 — ADR-028 Amendment A6: keyless customer stamps; Secure Record Owner not packaged (T235, T218e)

`customer-provisioning-orchestration-r1` T235 (owner D13) and T218e.

- **`.claude/adr/ADR-028-spaarke-auth-architecture.md`**: Amendment **A6** — customer stamps disable key/local auth on
  every data-plane resource and reach them as the stamp UAMI; a stamp vault holds no credential with a managed-identity
  alternative; dev/demo keep "key if configured" until the D13 follow-on (named case: the dev Document Intelligence key).
  The A4 pattern note no longer points at the closed E-3. `INDEX.md` row updated.
- **`.claude/constraints/provisioning.md`**: the keyless section cites A6.
- **`.claude/skills/provision-environment/SKILL.md`**: the tenant-id line no longer calls Spaarke's tenant "shared".
- **`.claude/patterns/provisioning/bff-vs-provisioning-boundary.md`**: Decision 3 (shared-BFF Dataverse routing) marked
  superseded by D-12.
- **`.claude/constraints/provisioning.md`** (T218e, earlier the same day — recorded here): the package rule takes
  roles from the ROOT business unit only; "Secure Record Owner" stays contained in the Secure Record unit and is
  created per environment by H7b (T256), not packaged.
- Root `CLAUDE.md` unchanged (its provisioning and auth pointer rows were already correct).

---
###### 2026-10-07 — Stamp BFF clients: CORS + H3 client access (T240a)

`customer-provisioning-orchestration-r1` T240a (owner 2026-10-07: `addins.spaarke.com`, `external.spaarke.com`).

- **`.claude/constraints/provisioning.md`**: new BINDING section "Stamp BFF clients — CORS + app-registration client
  access": the manifest's literal CORS origins are the two shared client sites; H3 sets exactly the SPA redirect and the
  pre-authorized clients on the customer's own registration; `EntraAppRegOptions__SpaarkeTenantId` is required.
- **`.claude/skills/provision-environment/SKILL.md`**: the handler list's H3 line said "KV secret bootstrap"; it is the
  per-customer BFF app registration (now with client access). H4b notes the CORS origins.

---
###### 2026-10-07 — Tenancy wording follows D-12 everywhere (T233)

`customer-provisioning-orchestration-r1` T233 (plan G8).

- **`.claude/skills/provision-environment/SKILL.md`**: the opening line no longer offers a "Model 1 shared trial/SMB" stamp; both models are dedicated stamps.
- **`.claude/skills/azure-deploy/SKILL.md`**: the retired Model 1 stack row no longer says H2a refuses Model 1 runs (it deploys them with `customer.bicep` since task 228).
- **`.claude/patterns/provisioning/operator-rbac-bootstrap.md`**: the multiple-vault anti-pattern no longer cites the retired shared tier.

---
###### 2026-10-07 — Model 1 guests: environment security group + pay-as-you-go (T232)

`customer-provisioning-orchestration-r1` T232 (owner D2; owner 2026-10-07: Spaarke pays guest access pay-as-you-go).

- **`.claude/skills/provision-environment/SKILL.md`** Step 1e-bis: a Model1 run's preset is fixed to `B2BGuest`; new
  intake `environmentSecurityGroupId` (GUID of `sprk-{customerId}-users`); the step checks PRQ-C-10 (group) and
  PRQ-C-12 (guest access) as the operator — hard stop — and shows PRQ-C-11 (`pac licensing
  get-environment-billing-policy`) for confirmation; Step 4.0 sends the group id.
- **`.claude/constraints/provisioning.md`**: new binding section "Model 1 users — B2B guests, environment security group,
  pay-as-you-go".

---
###### 2026-10-07 — ADR-050 amended: `WizardShell` is the wizard preset, `WizardModal` retired, in-app launch rule (spaarke-ontology-platform-r1 task 110, D-26)

`.claude/adr/ADR-050-canonical-modal-shell.md` (concise; **no full ADR-050 exists under `docs/adr/`**, checked
2026-10-07): the preset list is now `ConfirmModal`, `ChoiceModal`, `FormModal`, `PreviewModal`, `BrowseModal`,
**`WizardShell`** — the engine-bearing wizard preset that renders inside `SprkModal`, with an `embedded` mode for tabs,
full pages and pages under platform chrome. **`WizardModal` is retired** (zero consumers). New MUSTs: every multi-step
flow uses `WizardShell` (no second wizard engine); **launch in-app** from Spaarke React surfaces and keep
`navigateTo(target 2)` for hostless ribbon scripts, because its title bar is light-only platform chrome that cannot be
themed; `WizardShell` embedded `hideTitle` under platform chrome; wizards default to `dismiss="explicit"` + `uiScale`.
New MUST NOT: inject CSS or DOM into platform dialog chrome. Unchanged: Layout 1 (85% × 85%), the `fullCover`
escalation, the per-entity-size MUST NOT. `RecordNavigationModalShell` is recorded as the dirty-check protocol behind
`BrowseModal.onBeforeNavigate` (zero envelope consumers). Companion fixes: `.claude/patterns/ui/record-modal-selection.md`
and `.claude/patterns/ui/modal-shell.md` (no longer "compose `RecordNavigationModalShell`"),
`.claude/adr/ADR-026-full-page-custom-page-standard.md` (Wizard/dialog row cross-reference), plus
`docs/standards/MODAL-DECISION-CRITERIA.md`, `docs/standards/MODAL-DESIGN-SYSTEM.md` §7 and
`docs/architecture/ui-dialog-shell-architecture.md`. Path **B** per root `CLAUDE.md` §6.5 (owner decision **D-26**,
2026-10-07); alternatives A (project exception) and C (build on `WizardModal`, a second engine) rejected. Record:
`projects/spaarke-ontology-platform-r1/notes/modal-wizard-canonical-approach.md` §6.

---
###### 2026-10-08 — Enforcement ladder: rules enforced by the build, not prose (enforcement-ladder-r1); root CLAUDE.md about +1.1 KB vs this branch's base (about 17.8 KB injected, under 190 lines)

The common root cause behind the 2026-10 findings (dead `res.ok` branches after `authenticatedFetch`, detached tests, #975's 3-of-47 fix, module CLAUDE.md drift, the shared-stash incident): a rule in prose drifts and competes for attention, while a rule the build enforces reaches the agent at the line it just wrote. This makes that the default.

- **Root §16 "Enforcement ladder"**: for any new rule, use the strongest mechanism that works — type → lint rule (`post-edit-lint.sh` now returns findings to the agent as `additionalContext` after each Edit/Write; before this its stdout reached only the debug log) → ArchTest/guard (`Stop` hook, CI) → hook/permission → prose with a reason. Record "Enforced by: …". New guards carry must-fire/must-not-fire controls and a ratchet baseline. Full checklist: `ai-procedure-maintenance` **Checklist G**; Checklist A (new ADR) gains item 12, Checklist F runs the path check.
- **`task-execute` Step 9.5 rule 6 "Fix the class, not the instance"**: a pattern defect is searched repo-wide and fixed, guarded or listed.
- **`code-review` Step 6.55 "Enforcement check"**: flags a new rule with no mechanism, a guard with no must-fire control, and a pattern fix with no class search.
- **New check `scripts/quality/Test-InstructionPaths.ps1`** (CI Tier 2, advisory; also in `doc-drift-audit`): every backticked path under src/, tests/, docs/, scripts/, .claude/, .github/, infrastructure/ or config/ in every instruction file (and FAILURE-MODES) must be tracked by git — case-sensitive, so it answers the same on Windows and Linux CI. Ratcheted — `scripts/quality/instruction-paths.baseline.txt` records the 39 dead paths found today (deleted PCFs, moved files); a new one fails. Gitignored build output, placeholders and package-relative `src/…` are skipped. Its own must-fire test caught a bug in it (every `src/` path was being skipped).
- **`post-edit-lint.sh` delivers its findings**: a `PostToolUse` hook's plain stdout goes only to the debug log, so the lint hook never reached the agent. It now emits `hookSpecificOutput.additionalContext` with only real findings (filtered per tool, capped at 3,000 characters), stays silent on a clean file, and its matcher covers `Edit|Write`.
- **`permissions.ask` on `git stash pop/apply/drop/clear`** (Bash + PowerShell, including `git -C <path> stash …`): the stash stack is shared by every worktree; a pop applied another session's stash on 2026-10-08. New **FAILURE-MODES G-18**.

---
###### 2026-10-07 — Procedure calibration: guardrails, not caps (procedure-calibration-r1)

The owner's rule: *"we want guardrails but not such strong constraints that we cause problems. A hard limit like 'maximum 2 fixes' or 'maximum 20 KB' limits the judgement that there may be legitimate situations that require the added resources."* An independent review sorted every numeric limit and absolute in the instruction set into platform fact / owner hard stop / trigger-written-as-cap / unbounded / fine. This entry fixes the caps and the open loops; safety rules (secrets and Key Vault, endpoint auth, the no-client-secret guard, tenant isolation, fail-closed, no plugins, "never drop a real defect") stay absolute.

**Caps that blocked legitimate work → signals or triggers:**
- `code-review`: metric thresholds (> 500 lines, > 20 public methods, > 3 interfaces) were "Critical = must fix before merge", contradicting root §11.5. They are now look-closer signals reported without severity; Smell 5 is Suggestion/Warning, never Critical on a count; the size-only smells are cohesion prompts. Findings are classified F/K per task-execute Step 9.5, and every F-class finding is fixed.
- `constraints/pcf.md`: "MUST achieve 90%+ coverage on shared components" removed (ADR-038 bans coverage targets).
- "≤15 DI lines" is the readability target from ADR-010's rationale, not a hard count — in `constraints/api.md`, the ADR-010 and ADR-001 concise files, `.claude/adr/INDEX.md`, `adr-aware`, `adr-check`'s validation rules, `code-review`'s checklist and `mcp-tool-handler`. ADR-012 concise loses its 90% coverage MUST too.
- Context-percentage stops (60/70/85%) in `task-execute`, `context-handoff`, `project-continue`, `project-pipeline` → event triggers (user/harness reports context high, compaction notice, after a deploy, before a large load). Claude cannot measure its own context (root §5).
- `task-execute` Step 8.0: "MUST delegate 4+ files" → a judgment call for independent, substantial work, recorded in one line.
- Verifier passes (root §8.5, task-execute Step 9.5): one by default (two for auth/security/tenant), more when a fix changes the approach, with a one-line reason. Never stops a fix.
- `adr-check`: check the ADRs that apply to the change — adr-aware Rule 1, code-review's always-check set, and one pass over `.claude/adr/INDEX.md` as the backstop for ADRs the mapping does not reach — listing what was considered; grep-checking every ADR is for full scans and 090 wrap-up. Its index is now `.claude/adr/INDEX.md`.
- `code-review` metrics: every file over a look-closer value gets a one-line cohesion verdict in the review, so a signal is never silently ignored.
- `project-pipeline`: the 500-word spec minimum is a prompt to check substance, not a stop; `doc-drift-audit`'s "auto-fix ≥ 50%" target dropped; `task-create`'s /goal turn cap is a raisable default.

**Open loops → stopping conditions:** failed wave tasks are retried only after the cause is named and addressed; an unexplained failure is escalated.

**ADR-038 Amendment A3 (owner-ratified 2026-10-07): orphaned and detached tests.** An orphaned test (its subject was *deleted*, not moved) may be deleted without a same-PR replacement when the PR carries evidence: the deletion named; the behaviour not continuing elsewhere; a retirement test for a removed route or security path; invariants still in force re-targeted; no dependent tests; verified at code-review. A detached test (re-creates the logic in the test file, calls no production code) is rewritten against production code or deleted. Retirement tests and ArchTests are never orphans. Applied in ADR-038 §2/§6, `constraints/testing.md`, `tests/CLAUDE.md`, `task-execute` Step 9.5, `TEST-ARCHITECTURE.md` and `test-diet` (new ORPHAN / DETACHED classes and checks 13–14).

**Hard stops given a reason or an escalation path:** HIGH CVE with no upstream fix (`.claude/rules/bff-hygiene.md` item 5: advisory ID, reachability, follow-up, owner sign-off — the finding stays open until the sign-off exists); Plan Mode in a non-interactive session (Steps 0–1.7 read-only, then stop with a report); the 6-agent cap (API-overload guard); ≥ 60 MB publish size (roll back, extract, or ADR-029 amendment); provisioning Step 0.5 failures (record, remediate, resume).

**One rule, one place:** the publish-size rule was stated six ways across seven files with two stale baselines (49.63 / 44.96 MB) and an uncompressed `du -sh`; `task-execute`, `code-review`, `bff-extensions.md`, `provisioning.md`, `azure-deployment.md` now point to `bff-hygiene.md` item 4. Also fixed: `task-execute` Step 9.5 protected only four of the eight KEEP paths from deletion; `pac pcf push` / "4 version locations" in `task-create` and `task-execute`; Code Pages' location and React version in `constraints/pcf.md`; the skip rule (a reason in the `Skip` string plus `[Trait("status", "real-bug-pending-fix" | "flaky-quarantined")]`; the old `skip-reason` trait was used nowhere); the context-recovery procedure's percentage triggers; ADR-029's 49.63 MB baseline marked historical; remaining "4 locations" / `pac pcf push` release steps; `ThrowIfNull` guidance aligned with code-review; `project-setup`'s obsolete `MAX_THINKING_TOKENS`; `ai-procedure-maintenance` numbering and its "CLAUDE.md ADR table" step.

---
###### 2026-10-07 — Module CLAUDE.md files corrected against the code: 89,166 → 45,733 bytes (−43,433) across 8 files (module-claude-md-cleanup-r1)

A module `CLAUDE.md` loads whenever an agent reads a file in its folder, and agents treat it as ground truth. Checked line by line against the code, the older files were giving wrong instructions, not just long ones. Originals are archived verbatim at `.claude/archive/2026-10-07/modules/`. Each file now carries maintainer notes in a stripped HTML comment, with a **size target, not a cap**.

**Wrong instructions removed** (each verified against the code; two independent audits):
- `Sprk.Bff.Api`: described `BFF-API-ClientSecret` as both removed (E-3 closed) and the live OBO fallback; called the managed identity system-assigned (it is user-assigned); 7 wrong paths; two dead links; a retired runbook; a unit-test sample contradicting ADR-038. Mailbox Graph now cites Exchange RBAC for Applications, not the legacy ApplicationAccessPolicy.
- `pcf`: manifest sample used React 18.2.0 (platform is 16.14.0, ADR-022); deploy workflow still said `npm run build`; 4 version locations (pcf-deploy: 5); the auth sample's BFF scope `SDAP.Access` (PCFs use `user_impersonation`) and a nonexistent helper. Adds: BFF base URL is host only (`getApiBaseUrl()`); shared-library imports are deep `dist/` paths per ADR-012.
- `server/shared`: `Guard`, `Result<T>`, `QueryExtensions`, `EntityExtensions`, `DataverseService` do not exist; csproj sample showed the dependency direction reversed; test sample used `Mock<IServiceClient>` (banned B2).
- `client/shared`: `StatusBadge`, `usePagination`, `formatters.ts` do not exist; `workspace:*` (actual: `file:`); covered 1 of 15 packages. Adds ADR-012's new-package rule.
- `tests`: `dotnet test tests/integration/contract/` cannot work (KEEP folders compile into `tests/unit/Sprk.Bff.Api.Tests`); `appsettings.Test.json` and `Shared/Builders/` do not exist; KEEP count said 6/7 (ADR-038: 8). Adds: scope is .NET xUnit only; `Spaarke.ArchTests` is not in `Spaarke.sln`; never add a second KEEP glob (NETSDK1022). B6–B17 examples → pointer to ADR-038 §7, which holds equivalent or richer pairs (compared ban by ban).

**Moved, not deleted:** version-bump list, Custom Page republish, hard refresh → `pcf-deploy` + `PCF-DEPLOYMENT-GUIDE.md`; settings → `appsettings.template.json`; Kiota history → csproj comment; endpoint/error samples → `.claude/patterns/api/`; auth status → ADR-028 (pointer, not paraphrase). Headings cited elsewhere are unchanged ("Expect to Defend at Project Close", the integration template, "Package Management", "Scrollable Lists").

**Adjacent fixes:** `TEST-ARCHITECTURE.md` §3 listed six KEEP categories and called anything outside them a DELETE candidate — that made every seam test and fitness function a delete target; now eight, illustrative examples labelled. KEEP count also corrected in ADR-038 §2/§3, both ADR indexes, `docs/INDEX.md`, `constraints/testing.md`. `CODE-REVIEW-BY-MODULE.md` stated the Core/Dataverse dependency backwards. `provisioning-runs/_templates/CLAUDE.md`: Key Vault paraphrase → pointer to the live "KV credential lifecycle" rule; root §6.5 escalation fields. `office-addins`: header history trimmed.

**Drift found by the audits and fixed here:**
- Exchange mailbox access: control-plane stamps use Exchange RBAC for Applications (H14a, owner D26); Application Access Policies are legacy. Legacy-mechanism notes added to `COMMUNICATION-DEPLOYMENT-GUIDE.md`, `MI-CONFIGURATION-PATTERNS.md`, `SPAARKE-SELF-SERVICE-USER-REGISTRATION.md`; `bff-deploy` / `spe-integration` / `azure-deploy` skills, `sdap-auth-patterns.md` (also: MI is user-assigned), `sdap-overview.md`, `docs/architecture/INDEX.md`, `DATAVERSE-AUTHENTICATION-GUIDE.md`, `docs/guides/INDEX.md` stop pointing at the retired `auth-deployment-setup.md` stub; `GraphAppRoles.cs` comment.
- PCF shared-library imports: `pcf-safe.ts` header, `.claude/constraints/react-versioning.md`, `universal-dataset-grid-architecture.md` no longer say `src/pcf-safe` (ADR-012/022: compiled `dist/` paths). The nine PCFs importing the bare barrel are documented as working only through their per-control webpack stubs (task 092).
- Stale KEEP counts and cites in test comments/READMEs (`tests/integration/auth/README.md`, `tests/eval/*`, `contract/README.md`, `LayerDependencyTests`, `ComposeEndpointsContractTests`, `FetchXmlGuardSelfJoinTests`, `AnalysisOrchestrationServiceTests`) — comments only.
- Left to the owning project (its branch is editing these files): `AZURE-SETUP-SELF-SERVICE-REGISTRATION.md`, `PROVISIONING-PREREQUISITES.md`, ControlPlane comments naming ApplicationAccessPolicy, and five ControlPlane test comments citing "7 KEEP paths".

---
###### 2026-10-07 — Root CLAUDE.md cleanup: 66,657 → 18,569 bytes (−48,088), 499 → 215 lines; 16.6 KB / 187 lines as injected (claude-md-cleanup-r1)

The root file had regrown from 18 KB (May rewrite) to 66 KB. §17 pointer rows alone were 30 KB; incident write-ups sat inside rules; nothing limited growth. It now holds only binding every-turn rules, safety guards and one-line triggers, per Anthropic's guidance (< 200 lines per CLAUDE.md). Section numbers are unchanged. The previous file is archived verbatim at `.claude/archive/2026-10-07/CLAUDE.md`.

**Relocated, not deleted** (each verified present at its destination):
- §17's 58 rows → new `docs/INDEX.md`, verbatim.
- The Calendar two-variant detail → `src/client/shared/Spaarke.Events.Components/README.md`.
- §10 → new path-scoped rule `.claude/rules/bff-hygiene.md` (loads when editing the BFF / `Spaarke.Core` / `Spaarke.Dataverse`; items 1–6 keep their numbering). §10's publish-size text, including hazards 3 and 4 that existed only in root → `.claude/constraints/azure-deployment.md`. That file's own rule also changes: it now says to measure the delta against a fresh master build, not "the prior measured baseline".
- §6.5's enforcement points, limits and anti-patterns → `adr-check` Step 5.5.
- §11's anti-pattern examples → `task-create` Step 3.5.6.
- §7 and §8 → pointers to `task-execute`, which already held them.

**Changed:**
- §5: event-based checkpoints, replacing context-percentage triggers. Claude cannot see its own context percentage; the docs say only the user can.
- §9: the Key Vault rule is no longer paraphrased. It now points at the current text, which is time-boxed and environment-specific; issue #1348 tracks the stale E-1 statements in ADR-028 / `provisioning.md`.
- §16: describes the real hooks.
- Maintainer notes moved into a block-level HTML comment, which is stripped before injection.
- Paths are backticked rather than linked, and wording is plain (no emphasis inflation).

**New:**
- `.claude/rules/credentials.md`: path-scoped pointer to the Key Vault lifecycle rule and ADR-028, for secret-handling code.
- `.claude/hooks/reinject-project-state.ps1`, plus a `SessionStart`/`compact` hook in `.claude/settings.json`. It re-injects the project's `current-task.md` and its standing directives after compaction. PreCompact cannot inject context; SessionStart can.
- `permissions.ask` rules for `az keyvault secret delete|purge|recover`, `set *ClientSecret*`, and the PowerShell equivalents. The human confirms each time; `ask` takes precedence over the blanket Bash allow. These are not `deny` rules, so the time-boxed lifecycle (auth-v4's retirement runbook) is never blocked.

**Skills:**
- `ai-procedure-maintenance` no longer sends additions to root tables that don't exist; it states the budget and placement rules, and adds `.claude/rules/` as a home.
- `doc-drift-audit` reports instruction-file size and dead paths.
- `project-setup`: the project `CLAUDE.md` template is rewritten to a 7-section operating manual that does not copy repo-wide rules, with guidance on size and upkeep.

Three docs had anchor links to the old §6.5 / §10 headings and were repointed. Run `/doctor prompt-audit` (Claude Code ≥ 2.1.283) after merge.

**Fixes from the independent audit (same PR):**
- §11 now states that the rule covers new surface added inside an existing file; the earlier wording read as a loophole.
- §10 keeps the publish-size thresholds and the SpaarkeAi half of the hot-path rule.
- §5 defers to `task-execute` Step 8.5 for checkpoint cadence.
- A §17 trigger for new code pages restores the Navigator registrar step.
- The auth trigger points at the live constraint and pattern, not the retired setup stub.
- The hook finds the project by branch **or** `spaarke-wt-<project>` folder, and re-injects the template's binding-rule, directive and gotcha sections as well as the legacy heading.
- The `ask` rules also cover lowercase `*client-secret*`, `restore` and `set-attributes`.
- `credentials.md` also covers `scripts/provisioning/**` and the provision, decommission and app-registration scripts.
- `azure-deployment.md` MUST 1 now requires fresh short-path worktrees for both sides, and MUST 4 makes the CVE check unconditional, matching root.

###### 2026-10-06 — SPE byte identity: broker-only replaces writer-identity matching (unified-access-control-r2 task 171)

Three files change: `.claude/patterns/auth/spe-writer-identity-matching.md` (marked SUPERSEDED, decision-matrix row marked historical), `.claude/constraints/auth.md` (the SPE File Access section is rewritten: app-only behind a Dataverse decision plus the pointer check; no new `*AsUserAsync` callers; container roles only through `GrantMarkedWriterAsync`) and `.claude/constraints/bff-extensions.md` §D (background SPE reads are app-only after the pointer check). This follows owner rounds 69/70.

---
###### 2026-10-06 — Optional per-customer OpenAI spend limit at intake (T254)

`customer-provisioning-orchestration-r1` T254 (owner G37: no cap by default, a per-customer limit if desired).

- **`.claude/skills/provision-environment/SKILL.md`**: new Step 1b-quater — OPTIONAL `openAiMonthlyLimitUsd` (empty = no
  limit; plain number in (0, 1,000,000]); Step 4.0 sends it only when set; on an upgrade run leave it out or send the
  current value (a re-run re-applies it). Later changes: `scripts/Set-AiSpendLimit.ps1` (guide §3.2b).

---
###### 2026-10-06 — Keyless proof: H13 proves every stamp service with the BFF's managed identity (T230b)

`customer-provisioning-orchestration-r1` T230b (owner D13).

- **`.claude/constraints/provisioning.md`** ("Stamp resources are keyless"): the per-run proof — H13 calls the stamp BFF's
  `POST /api/platform/keyless-proof` as the L2 Worker identity (app role `Provisioning.KeylessProof`, assigned by H3); an
  auth failure is never a skip; ARM keyless check (`ArmStampKeylessVerifier`); new MUST NOT: a key credential in server
  code needs its `KeyCredentialCensusTests` entry and, for a stamp resource, its `StampKeySettingCatalog` + probe entries.
- **`.claude/adr/ADR-028-spaarke-auth-architecture.md`** E-2: informational note — the stamp measurement mechanism exists;
  the measurement is pending T186.
- **`.claude/skills/provision-environment/SKILL.md`**: H13 line names the keyless gate.
- H13's four user-workflow "sample" checks (agent message, search count, layouts, field mappings) are removed: an app-only
  token could never pass them and their auth failures were skipped, so they never ran.

---
###### 2026-10-06 — H13 checks the deployed stamp; naming conformance + I1 are build gates (T230a)

`customer-provisioning-orchestration-r1` T230a (G5).

- **`.claude/constraints/provisioning.md`**: I3 text corrected to spec FR-30 (`/tenantId` on the stamp's containers or the
  key `cosmos-db.bicep` declares; `/customerId` only on L2's ProvisioningRun); H13 samples I2–I5 on the stamp, I1 is
  enforced by its ArchTest only (the L2 Worker ships and runs no script — T253).
- **`.claude/skills/provision-environment/SKILL.md`**: invariants row — I2–I5 at H13, I1 a build gate.
- Naming conformance (`scripts/naming-conformance-check.ps1`, vault rule now also `sprk-{customerId}-{env}-kv`) runs as a
  merge-blocking job in `ci-tier1-blocking.yml`; H13's per-run copy (which linted repository files absent from the
  Worker host) is removed.

###### 2026-10-06 — Cost model for dedicated stamps: one rule set, every model, no waiver (T229)

`customer-provisioning-orchestration-r1` T229 (G4, G13; D-12 — every customer gets a dedicated stamp).

- **`.claude/constraints/provisioning.md`**: new binding section "Cost model — one dedicated stamp per run, both models":
  no `shared-trial` tier / marginal / shared-floor envelope; `tier` + `estimatedMonthlyUsd` required for every model and
  validated by `CostEnvelopeIntake` at POST /api/runs and in H0; no `costEnvelopePolicy` / `warnAndProceed`; H13 has one
  `DedicatedStampEnvelopeUsd` ($400; $337.04 fixed at 2026-10-06 list prices), re-derived when `customer.bicep` SKUs change.
- **`.claude/skills/provision-environment/SKILL.md`**: new Step 1b-ter (tier + estimate, with the empty-stamp floor as
  guidance); batch loader drops `costEnvelopePolicy` and its Model 2 check; Step 2 BAT-10 overrun is a hard stop in both
  modes (no interactive "Proceed anyway?"); Step 4.0 requires and sends both values, no `costEnvelopePolicy`.

###### 2026-10-06 — The customer's subscription and Dataverse environment are operator prerequisites (T228)

`customer-provisioning-orchestration-r1` T228 (owner D4 / Q1; L2 identity = Owner per customer subscription, owner
decision 2026-10-06).

- **`.claude/constraints/provisioning.md`**: new binding section — the operator creates the customer's subscription and
  Dataverse environment; intake requires `subscriptionId`, `containerTypeId` and `dataverseEnvUrl` for every model (no
  shared or default subscription); the environment's domain must be `spaarke-{customerId}[-{environmentName}]`
  (`DataverseEnvironmentUrlRule`); H1 refuses a subscription holding another customer's stamp; H5 adopts and never
  creates; H10 runs before H6; Owner granted per customer subscription by the operator (PRQ-S-04).
- **`.claude/skills/provision-environment/SKILL.md`**: Step 1b-bis (subscriptionId + dataverseEnvUrl), the Step 1e
  Model 1 hard stop removed, Step 1f records the real environment URL, Step 4.0 sends `dataverseEnvUrl` and never falls
  back to `az account show`.

###### 2026-10-06 — One ROOT container per customer; H7 links the root business unit; secure-record setup is a runbook phase (T227g)

`customer-provisioning-orchestration-r1` T227g (owner question: how do the Secure Record containers fit?).

- **`.claude/constraints/provisioning.md`** (SPE section): "one container per customer" corrected to one ROOT container —
  secure-record containers (one per secure project / matter / work assignment) and further business-unit containers are
  the BFF's, at runtime, bound and marked. New binding bullet: H7 sets the root business unit's `sprk_containerid` to H8's
  container (unified-access-control-r2 task 076's non-secure default), never overwriting another container (Resumable
  `root-business-unit-container-conflict`); every script-created container carries the `spaarkeCustomerId` marker.
- **`.claude/skills/provision-environment/SKILL.md`**: Step 6d — the secure-record environment setup
  (`SECURE-PROJECT-ENVIRONMENT-SETUP.md`, gated by its §7 checklist) before the customer is told the environment is ready.

###### 2026-10-06 — One definition of the stamp's containers; the I4 resolver diagnostic retired (T227f)

`customer-provisioning-orchestration-r1` T227f.

- **`.claude/constraints/provisioning.md`** I4: container ids come from the record or the stamp's settings and every
  app-only SPE call passes `SpeContainerOwnershipGuard`; the unused `ITenantContainerResolver` (and its diagnostic route)
  and `SharePointEmbedded:StagingContainerId` were removed.
- **`.claude/skills/provision-environment/SKILL.md`**: the I4 checklist line names what H13 actually checks.

###### 2026-10-06 — H8 reuses the customer's container; unread SPE-ContainerTypeId retired (T227e)

`customer-provisioning-orchestration-r1` T227e.

- **`.claude/constraints/provisioning.md`** (SPE section): one container per customer, ever — on top of
  unified-access-control-r2 task 165's per-run creation record, a LATER run reuses the container the environment records
  (`sprk_SharePointEmbeddedContainerId`, written by H7); the record and the environment disagreeing stops the run naming
  both; a reused container is never removed by a failed bind; H8 writes the `spaarkeCustomerId` marker after the bind.
  The marker name is one source-linked constant (`src/server/shared/Contracts/SpeContainerCustomerMarker.cs`).
- **`.claude/patterns/provisioning/manifest-driven-secret-catalog.md`**: `from-topology-constants` retired with its only
  entry (`SPE-ContainerTypeId`, unread); the reader now refuses it.
- **`.claude/skills/provision-environment/SKILL.md`**: the `containerTypeId` comment names its real readers (H4b setting, H8).

###### 2026-10-06 — App-only SPE calls go through the ownership guard (T227d, owner D28/D29)

`customer-provisioning-orchestration-r1` T227d.

- **`.claude/constraints/provisioning.md`** (SPE section): app-only SPE Graph clients come only from
  `SpeContainerOwnershipGuard`; "own" = a configured stamp container or the `spaarkeCustomerId` marker; SPE Admin on a
  stamp is confined to own containers (owner D29); enforced by ArchTest `SpeAppOnlyContainerGuardTests`.

###### 2026-10-06 — Non-secret values from later handlers are settings, not vault secrets (T227c, plan G18)

`customer-provisioning-orchestration-r1` T227c.

- **`.claude/patterns/provisioning/manifest-driven-secret-catalog.md`** rule 4: a non-secret value a later handler
  produces goes in `per_env_settings` with a `from-{handler}-output` source and an H4b ← handler DAG edge (the SPE
  container id moved there), not in the vault.

###### 2026-10-06 — SPE app-only isolation is in code (T227b, owner D28)

`customer-provisioning-orchestration-r1` T227b.

- **`.claude/constraints/provisioning.md`**: new BINDING section — one container type per model, one container per
  customer; an app-only grant reaches every container of the type, so app-only SPE calls must target only the stamp's
  own container(s), enforced in code (T227d). A container type per customer was rejected by the owner.

###### 2026-10-06 — No shared BFF app registration in the provisioning skill (T227a, plan G2)

`customer-provisioning-orchestration-r1` T227a.

- **`.claude/skills/provision-environment/SKILL.md`**: Step 0.5b no longer derives `{bffAppServiceId}` / `{bffAppId}`;
  Step 0.5c no longer hard-stops on a null `bffApiAppId` or checks a shared BFF app and its grant (each customer's
  BFF app is created by H3; H8 grants it — T227b); Step 5a (Model 2) no longer reads removed constants.

###### 2026-10-06 — One OpenAI deployment set for stamps; no recompose (T247, plan G27)

`customer-provisioning-orchestration-r1` T247.

- **`.claude/patterns/provisioning/openai-quota-region-composition.md`**: rewritten. The stamp set is fixed (the BFF calls
  deployments by name), mirrored by `PinnedModelCatalog.cs` and pinned by a forcing test; DataZoneStandard; OpenAI in
  `openAiLocation`; no support case. The old gpt-5 tiers, support-ticket quota bumps and "MVP fallback" are gone.
- **`.claude/skills/provision-environment/SKILL.md`**: Step 2.5 F5 no longer auto-recomposes the deployment set (a
  shortfall HALTs at H0); F8/F9 support-ticket steps marked not used; `sharedOpenAiLocation` → `openAiLocation`.

###### 2026-10-06 — Customer stamps get their own keyless Content Safety (T246, plan G26)

`customer-provisioning-orchestration-r1` T246.

- **`.claude/constraints/provisioning.md`**: the keyless-stamp rule now lists Content Safety, and states that its
  endpoint is a plain app setting the BFF requires outside Development/Testing — never a fallback to a shared or dev
  account (the BFF used to default to a non-existent dev endpoint and fail open).

###### 2026-10-06 — Customer stamps are keyless (T244, owner D13)

`customer-provisioning-orchestration-r1` T244 (plan G16).

- **`.claude/constraints/provisioning.md`**: new BINDING section "Stamp resources are keyless" — local auth disabled on
  AI Search (no `authOptions`), OpenAI, Document Intelligence and Service Bus; Storage shared key off; no key-listing
  call, SAS rule or key/connection-string output in any stamp module; callers get roles (L2 now holds Search Service
  Contributor for H2b + Search Index Data Reader for the H13 probe); Event Grid dead-letters with the system topic's identity.
  Forcing function: `tests/Spaarke.ArchTests/CustomerStampKeylessTemplateTests.cs` over the compiled `customer.json`.
- **`.claude/adr/ADR-028-spaarke-auth-architecture.md`**: informational note under E-2 (no rule change) — E-2 covers the
  shared dev `AIServices` account only; stamps are `kind: OpenAI` with local auth disabled, so the key fallback cannot
  apply there; a 401 at T230 would be an E-2 scope extension needing an owner decision. Cosmos DB and SignalR are in
  the keyless rule too (owner added Cosmos 2026-10-06).
- **`.claude/constraints/azure-deployment.md`**: the Service Bus row listed a Key Vault-referenced
  `ConnectionStrings__ServiceBus`; the supported path is `ServiceBus__FullyQualifiedNamespace` + managed identity
  (`ServiceBusClientFactory`), and the startup-failure line now says so.

---
###### 2026-10-06 — FAILURE-MODES G-17: cache-version pins (unified-access-control-r2 task 172)

`.claude/FAILURE-MODES.md` G-17: a test pinning a cache-version constant to an exact value fails every later legitimate bump. Pin the floor and seed the pre-bump version.

###### 2026-10-06 — Correction (owner): round limits cap review ceremony, never fixing; every found defect is fixed or surfaced

The throughput entry below capped Step 9.5 at "2 fix rounds … never start round 3". In practice that left a confirmed F1 (task 171 in `unified-access-control-r2`) waiting on a round count. The owner corrected the intent the same day: the limits exist to stop unnecessary checks and pseudo-fixes, not to leave known-broken code unfixed.

- `task-execute` Step 9.5:
  - The section is renamed "Finding triage and review scope".
  - There is no longer a round cap on fixing. The scope limits stay: re-checks cover the fix diff only, with one full verifier pass (two for auth/security).
  - New binding rule: **every defect found is fixed in scope, or filed and reported to the operator.** That holds whether the current work caused it directly or indirectly, or only uncovered it.
  - A K class may not hold a confirmed real-path defect.
  - Escalation is triggered by non-convergence, not by a count.
- Root `CLAUDE.md` §8.5 "Coverage-first review" bullet updated to match.
- `code-review`: also report defects in code the change did not write, marked "found in passing".

###### 2026-10-06 — Throughput fixes: `current-task.md` is state, not history; finding triage + round limits; seeding-proof and task-size scope (procedure-throughput-fixes-r1)

Investigation into why projects went from 20–50 tasks/day (Jan–Mar) to 1–4/day found three procedure causes. Build and test cost is real, but secondary.

- **`current-task.md` accumulated history.** `context-handoff` said "don't overwrite history" and its template ended in `[... rest of current-task.md content ...]`. The file therefore grew 5 KB → 483 KB in `unified-access-control-r2`, and was read at Step 0 and Step 2 of every task.
  - `context-handoff` gains "State, not history (BINDING)": rewrite at each checkpoint, ≤10 KB target / 20 KB trigger, and a destination table for durable items (project `CLAUDE.md` "Standing directives & gotchas", notes, commit messages, `notes/handoff-history/`).
  - `task-execute` Step 0 gains a size guard. Steps 8.5 and 11 now rewrite instead of accumulating.
  - `current-task.template.md` updated.
- **Review/verify loops had no stopping rule.** `code-review` hands filtering to task-execute Step 9.5, but Step 9.5 only said "fix → re-run".
  - Step 9.5 gains "Finding triage and round limits": classes F1–F4 fix-now and K1–K4 known-limit; 2 fix rounds re-verifying the fix diff plus its direct callers and callees (affected suites re-run); 1 adversarial-verifier pass (2 for auth/security/tenant-isolation); escalate any F1 still open.
  - This generalizes the owner's own `unified-access-control-r2` rule (rounds 56/59).
  - `code-review` suggests a class per finding and scopes re-reviews to the fix diff plus its direct callers and callees.
  - Root `CLAUDE.md` §8.5 "Coverage-first review" bullet extended by one sentence so it binds session-written workflow scripts too.
- **`task-create`:** seeding proofs only for security, isolation, fail-open and data-loss guards and pure regression guards (one per guard). Task size target ≤15 KB; fix-round history goes in notes, not the POML.

Unchanged by decision: BFF publish-size per-task measurement (§10.4) stays per-task. Moving it to CI would let growth accumulate unseen before it reached CI.

###### 2026-10-06 — Blocking Tier 1 Xrm Capability Guard + router docs-only fix; ci-cd skill updated (ontology task 081 rounds 7–8, PR #1309)

`.claude/skills/ci-cd/SKILL.md`: the Tier 1 row adds `Xrm Capability Guard (getXrm AST scan)`, a BLOCKING job in
`ci-tier1-blocking.yml` that runs on `src/client/**`, `src/solutions/**` or workflow changes, and the advisory
DataGrid External-Host Gate, which the row was missing. The troubleshooting table gains its row. The job is an
owner-approved (2026-10-06) CLAUDE.md §6.5 path-A exception to the workflow's "do not extend without spec
amendment" (ci-cd-unit-test-remediation-r1 FR-A02), scoped to this one job. Extended 2026-10-06 (owner-approved,
same §6.5 path-A exception, PR #1309 round 8): `ci-router.yml` now classifies a diff as docs-only, and skips Tier 1,
only when every changed file matches the existing documentation globs (a `dorny/paths-filter`
`predicate-quantifier: 'every'` step), closing the hole where client code plus any `*.md` or `projects/**` file
skipped all of Tier 1 including the Xrm capability guard.

###### 2026-10-06 — FAILURE-MODES G-13 extended to `$filter` (unified-access-control-r2 dev live gates)

`.claude/FAILURE-MODES.md` G-13: a lookup in a `$filter` must be `_<name>_value`. The section now records the No Access reader defect (every deny-list read was a 400 on dev and failed closed, which blocked secure provisioning) and the provisioning seeder case (#1318). It also records the lesson: a test double that matches on query text can't catch a wrong query, because it copies the same mistake.

---
###### 2026-10-05 — bff-deploy route verification and smoke check; FAILURE-MODES AP-15 (unified-access-control-r2 tasks 140, 166, 167)

`.claude/skills/bff-deploy/SKILL.md`: §9c's smoke check moves from the retired anonymous `/healthz/dataverse/doc/{id}` to
`/healthz/dataverse` (task 166). Route registration is now proved with a SIGNED-IN request — since task 167's authorization
FallbackPolicy an anonymous request answers 401 whether or not the route exists — in Step 3, Manual Quick Deploy step 3 and the
troubleshooting table, which also gains a row for 429 from `/healthz` / `/ping` (the "health-probe" rate limit).
`.claude/FAILURE-MODES.md` gains AP-15: TimeZoneIndependent DateOnly columns arrive from the Web API as timestamps (task 140).

###### 2026-10-04 — Route-sweep integration: knowledge indexing pointer removed; communications filter pointer (unified-access-control-r2 tasks 161, 163)

`.claude/skills/add-reference-to-index/SKILL.md` "Related" no longer points at `ReferenceIndexingService.cs`: task 163
deleted it together with `/api/admin/knowledge/*`, so the scripts the skill lists are the only indexing path.
`.claude/patterns/api/endpoint-filters.md` lists `CommunicationRecordAuthorizationFilter` (task 161) as a second
per-record filter to read: one `CommunicationRecordRoute` value per route fixes the id source, the right and the deny
answer.

###### 2026-10-04 — ADR-034 Amendment A4: Assigned-To access is materialized as removable grants (unified-access-control-r2 task 142)

`.claude/adr/ADR-034-user-record-membership.md` gains the A4 call-out, four MUST bullets and two MUST NOT bullets.
The access-conferring registry gets a second, **write-time** consumer. Every registry-listed Contact- or
Organization-typed "Assigned *" column on a project, matter or work assignment gives the named subject
**Collaborate** as an explicit, removable grant, or a POA share for a linked internal user. ONE invariant owner
does this, `AssignedAccessMaterializer`, with three triggers: L1 inline, the sync route and an L4 job. Provenance
lives in the `sprk_assignedaccess` ledger. It never lowers existing access, and an operator's removal sticks
(`Declined`). The record's Restricted / Secure / Limited policy and the No Access list apply before any write, and
nothing is written when an input cannot be read (ADR-003). The read-time standing-grant and organization-expansion
terms are kept (owner A2 reversed).

Line 41's claim that the Q4 `sprk_assigned*` fields have no BFF writers is corrected (142's writer census). The
owner accepted the amendment under §6.5 path B in round 11, 2026-10-03. The spec's FR-32 and MUST NOT list and
design §7 now name the one exception. Full rules: `docs/adr/ADR-034-user-record-membership.md` § "Amendment A4".


###### 2026-10-05 — Redis key-rotation tooling removed (T242b, owner)

`customer-provisioning-orchestration-r1` T242b.

- **`.claude/skills/ci-cd/SKILL.md`**: the `redis-key-rotation.yml` row is removed. The workflow,
  `scripts/Rotate-RedisKey.ps1` and the missed-rotation alert are deleted: every Spaarke Redis is Azure Managed
  Redis with access keys disabled (ADR-009 as amended by T242), so there is nothing to rotate; the tooling
  wrote a Redis key to Key Vault, which ADR-009 forbids; and every scheduled run had failed since 2026-07-01.

---
###### 2026-10-05 — ADR-009: the latency alert is the average BFF-observed call latency (T242b, owner §6.5 Path B)

`customer-provisioning-orchestration-r1` T242b Step 9.5 review.

- **`.claude/adr/ADR-009-redis-caching.md`** (+ full `docs/adr/ADR-009-caching-redis-first.md`): operational alert (b) was
  "P95 >100ms / 5min". It is now "average BFF-observed cache call latency per operation
  (`cache.redis_call_duration_ms`) >100ms / 5min". The histogram reaches App Insights pre-aggregated
  (sum/count/min/max), so a true P95 cannot be computed there, and the old alert queried `cache.redis_p95_ms`, which
  nothing emits, so it never fired. `infrastructure/bicep/alerts.bicep` implements the new rule.

---
###### 2026-10-05 — Redis key rotation: dev retired, quarterly schedule removed (T242b)

`customer-provisioning-orchestration-r1` T242b (owner decision 2026-10-05).

- **`.claude/skills/ci-cd/SKILL.md`**: the `redis-key-rotation.yml` row now reads "manual only". The workflow's
  quarterly crons were removed because every scheduled run had failed (no staging/prod cache, service principal
  or secrets exist), and dev has no key since the dev cache became Azure Managed Redis, Entra only.

---
###### 2026-10-04 — PCF deploy procedures verify the REAL build result; `pcf-scripts` exits 0 on a failed build

**What was wrong.** `pcf-scripts build` (and so `npm run build:prod` in every PCF) **exits 0 when the webpack
build fails**: its `taskRunner.js` logs `[build] Failed:` and `[pcf-1033] [Error] An error occurred compiling or
bundling the control.` and returns without rethrowing. Deploy procedures that ran the build and went on to "copy
`bundle.js`, pack, import" could ship the PREVIOUS bundle still in `out/`. Found 2026-10-04 by
`spaarke-ontology-platform-r1` (tasks 092 / 093b): the new nightly CI workflow reported 17 of 18 PCFs passing when
9 had failed.

**What changed** (one rule, judged from the OUTPUT: fail on a non-zero exit, `[build] Failed`, `compiled with N
error(s)` or `[pcf-1033]`, after stripping ANSI colour; pass only on `[build] Succeeded`; anything else is a failure.
Same rule as `.github/workflows/pcf-build-prod-nightly.yml`, PR #1285):
- New `scripts/PcfBuildResult.psm1` (the rule) and `scripts/Invoke-PcfBuildProd.ps1` (build one PCF, exit 1 on a
  failed build). Proven against a real failing build (VisualHost: npm exit 0, script exit 1) and a real passing one,
  plus fake builds in Windows PowerShell 5.1 and pwsh 7.
- `pcf-deploy` and `dataverse-deploy` SKILL.md: every build step runs the script **from the PCF folder**
  (`pwsh -File ../../../../scripts/Invoke-PcfBuildProd.ps1 -PcfPath .`) and stops on a non-zero exit; MUST rules
  say why. `pcf-deploy` Step 3's copy paths now match its working folder (they were `../out/...`), and its
  Manual Quick Deploy no longer uses dev-mode `npm run build`.
- `.claude/commands/dataverse-deploy.md`: its "Quick Dev Deploy" was `npm run build:prod` + `pac pcf push`, which
  contradicts `pcf-deploy`'s NEVER rule (push rebuilds in development mode). Now the verified build + pack + import.
- `task-execute` and `project-pipeline` SKILL.md: wave build verification and the PCF checklist judge PCF builds
  with the script; task-execute's checklist no longer suggests `pac pcf push`.
- `script-aware`, `project-pipeline`, `task-execute`: the example PCF script was `Deploy-PCFWebResources.ps1`,
  deleted below (`script-aware` had also documented a `-ControlName` parameter it never had).
- `docs/guides/PCF-DEPLOYMENT-GUIDE.md` said **"NEVER use `npm run build:prod` — pcf-scripts only has `build`"**,
  the AP-1 error `pcf-deploy` was corrected for in May. Corrected, plus a troubleshooting row for "build succeeded
  but the deployed control is unchanged".
- `src/client/pcf/{MatterHeader,RecordHeader}/Solution/pack.ps1` (which rebuild before packing) judge the build with
  the module.
- `scripts/Build-AllClientComponents.ps1` Step 4 (the release build's PCF step, run by `Deploy-Release.ps1` Phase 1)
  now builds **each PCF on its own in production mode**, mirroring the nightly workflow: every git-tracked
  `src/client/pcf/<name>/package.json` with a `build:prod` script gets `npm install` + `npm run build:prod`, is judged
  by the module, and is its own summary row (`PCF/<name>`); zero PCFs discovered is a FAILED row. It used to run ONE
  aggregate dev-mode `npm run build` at `src/client/pcf`, which never worked from a clean checkout (TS5083, then out of
  memory) and whose `out/` nothing consumed. Step 1 now builds `Spaarke.Events.Components` and
  `Spaarke.SmartTodo.Components` AFTER `Spaarke.UI.Components` (both depend on it; before, a clean checkout failed
  Step 1 and never reached Step 4). `ThemeEnforcer` gained the `build:prod` script every other PCF has. The script is
  now ASCII-only, so Windows PowerShell 5.1 parses it too (12 non-ASCII dashes/arrows in a BOM-less file gave 10 parse
  errors). Verified with #1123 merged: 14/14 shared libs + 19/19 PCFs pass.
- `master-deploy` SKILL.md: F-2's "until diagnosed" follow-up was stale (F-2 itself records the 2026-06-11 fix); now
  points at this PR's clean-checkout fixes as well, and notes that a full run takes over an hour.
- `scripts/Deploy-PCFWebResources.ps1` **deleted** and dropped from `Deploy-AllWebResources.ps1`: it only ever pushed
  `UniversalQuickCreate`, deleted 2026-06-22 by `pcf-orphan-cleanup-r1`, from a hard-coded path that no longer exists.

---
###### 2026-10-04 — Redis: Azure Managed Redis, Microsoft Entra only (T242, owner D12/D13); ADR-009 amended

`customer-provisioning-orchestration-r1` T242 (§6.5 Path B — owner decisions D12/D13 amend ADR-009 §2/§3).

- **`.claude/adr/ADR-009-redis-caching.md`** (+ full `docs/adr/ADR-009-caching-redis-first.md`): product = Azure
  Managed Redis (`Microsoft.Cache/redisEnterprise`); SKU table B0 (dev/demo non-HA, staging + customer stamps HA);
  authentication = the app's user-assigned managed identity only (access keys disabled, access-policy assignment,
  `Redis__Endpoint` plain setting, RESP3). Replaces the Basic/Standard/Premium SKU table and the "connection string in
  Key Vault" MUST; the 2026-09-28 "Standard per customer" wording is marked superseded.
- **`.claude/constraints/provisioning.md`**: new BINDING section "Stamp Redis — Azure Managed Redis, Microsoft Entra
  only" (module shape, no key/secret/connection-string setting, BFF + Worker refuse a connection string outside
  Development/Testing, H1 registers `Microsoft.Cache`).
- **`.claude/constraints/azure-deployment.md`**: the `ConnectionStrings__Redis` KV-reference row → `Redis__Endpoint`
  (plain host:10000).
- **`.claude/patterns/caching/distributed-cache.md` + `INDEX.md`**: `CacheModule` mode selection; keys disabled; the
  INDEX line that still said `AbortOnConnectFail = false` corrected to `true`.
- **`.claude/patterns/provisioning/manifest-driven-secret-catalog.md`**: `Redis-ConnectionString` no longer an example
  of a from-bicep-output secret.

---
###### 2026-10-04 — provisioning: Exchange sidecar holds no credential; H14a on RBAC for Applications (T251, D24–D26)

`customer-provisioning-orchestration-r1` T251 (gap G30, owner decisions D24, D25, D26).

- **`.claude/constraints/provisioning.md`**: new BINDING section "Exchange mailbox access — RBAC for Applications,
  sidecar holds no credential" — H14a grants only the stamp's managed identity the four `Application Mail.*` /
  `MailboxSettings.Read` roles scoped to the customer's group (never ApplicationAccessPolicy, never the BFF app); never a
  mailbox role in Entra (H10 grants `GetEntraGranted()`, T3 fails otherwise); the Worker signs in as
  `Spaarke Exchange Admin` via its UAMI's federated credential and passes the token to the sidecar (no secret,
  certificate or Entra directory role on that app; narrowed Exchange role); a `sitecontainers` variable names an app
  setting, never a literal. Two more bullets added after the live spike: the sidecar connects with the tenant's
  **initial domain**, never the tenant GUID (with a GUID, every Exchange write fails "doesn't have write permission to
  target DC"); and a group-scoped assignment reads back as `RecipientWriteScope = Group` + `CustomResourceScope =
  <group Name>`, so scope is matched on those fields.
- **`.claude/agent-memory/researcher/`**: `exo-apponly-dc-write-error-2026-10-04.md` (new, confirmed live) and the
  RBAC-for-apps note's open question answered.
- **`.claude/skills/provision-environment/SKILL.md`**: `exchangePolicyScopeGroupId` row (direct members only), the H14
  plan line and the T3/T4 report lines reworded for RBAC for Applications.
- **`.claude/agent-memory/researcher/`**: two findings files (EXO identity, RBAC-for-Applications design) moved from
  the project folder the agent wrote them to, and indexed.

###### 2026-10-03 — provisioning: L2 signs in as the SPE owning app with MI-FIC, not a certificate (T248, D16)

`customer-provisioning-orchestration-r1` T248 (gap G28, owner decision D16).

- **`.claude/constraints/provisioning.md`**: new BINDING section "SPE owning app — MI-FIC, nothing stored" — L2 acts
  as a container type's owning app only through the Worker UAMI's federated identity credential; never a certificate,
  secret or Key Vault read for it; a rejected FIC token is an owner decision (A4's Key Vault certificate), never a
  secret; Worker config `{ContainerTypeId, OwnerAppId}`; H0's three `spe-*` codes; the registration PUT is
  create-or-replace; no secret-based `sprk_specontainertypeconfig` for Model 1 until T250; never delete
  `rg-spaarke-shared-prod` or its Syntex billing account.
- **`.claude/skills/provision-environment/SKILL.md`**: Step 0.5b note names the owner entry and H0's three codes
  (was: certificate vault/secret + `SpeCertBootstrap`); Step 0.5c treats the container-type GET's 403 to the operator's
  Azure CLI token as expected (not consented for container-type reads — observed 2026-10-03) and defers to H0 instead
  of hard-stopping; check (5) flagged stale until T227 (per-customer BFF grants, D-13); T6 report line reworded.
- **`.claude/adr/ADR-028-spaarke-auth-architecture.md`** E-1: informational note — a managed identity can act as a
  same-tenant owning app via MI-FIC (verified by T248); L2 needs no E-1 secret; the BFF side is T250. No rule change.
- The mechanism: `SpeConfidentialClientGraphFactory` (now an instance class, credential cached per tenant + owner) gets owning-app tokens from
  `WorkerDataverseCredentialFactory.CreateManagedIdentityFederatedCredential` — the one place the Worker mints the UAMI
  assertion; `KeyVaultCertBootstrapProbe` and its 24 h gate are replaced by `SpeOwnerCredentialProbe`; H13's T6 lists
  the run's container app-only as the owner (the old app-only `containerTypes` GET is documented 403). Verified live
  from the dev Worker identity (FIC token `appidacr` 2; registration + containers GET 200).

---
###### 2026-10-04 — portfolio board hygiene: Type backfill, 156 missing projects registered, new `/project-spend-update` skill

Triggered by investigating why `unified-access-control-r2` spent ~$4,490 over 3 days via 27
self-authored Workflow-tool runs — a Claude Code judgment call, not a per-run user request. That
investigation surfaced the portfolio board (Project #2) was itself unreliable: 39 items with no `Type`
set (invisible to Type-filtered views), and 156 of 205 local `projects/` folders (32% of the then-active
registry) with no board presence at all.

- **`project-defer-issue-tracking`**: fixed the root cause — `gh issue create --project` added items to
  the board but never set `Type`. Skill now sets it immediately after creation (`defer→Idea`,
  `issue→Bug`). Backfilled the 39 existing untyped items by hand.
- **156 missing projects registered** as `[Project]:` board issues (existence + `Type`/`Status` only —
  not a full `/devops-project-register` pass; Epic linkage, Task Count, and Start Date were deliberately
  left for later enrichment). Board now has 207 `Type=Project` items, 1:1 with local folders.
- **Local README portfolio pointers backfilled** for all 156 (+1 manual test case) — `devops-project-sync`
  Step 0 requires that pointer block to find a project's Issue #, so without this, any future task work
  on those projects would have silently failed the sync precondition. 35 projects had no `README.md` at
  all (created minimal ones); 119 got the pointer inserted after the title; 3 had a stale `Portfolio: TBD`
  placeholder replaced.
- **New `/project-spend-update` skill** + `scripts/ai-cost/{get-project-cost,update-board-spend}.py`:
  manually-triggered (NOT wired into `task-execute` or `devops-project-sync`) refresh of three new board
  fields — `AI Spend (est.)`, `AI Calls`, `AI Spend As Of` — estimated from local Claude Code transcripts
  at list API pricing. Deliberately decoupled from task completion: the heaviest-spending pattern
  (Workflow-tool batches) doesn't reliably route through `task-execute` Step 9.6, so a hook-tied refresh
  would under-cover exactly the work most worth tracking. No attempt to separate metered-API from
  subscription/Max-plan usage (owner direction: keep it simple, reconcile against the actual invoice
  manually at the portfolio level).
- `devops-portfolio-status` now notes the spend field may be stale and points at `AI Spend As Of` +
  `/project-spend-update` rather than implying it's always current.

GitHub Projects v2 has no `CURRENCY` field type (confirmed via `gh project field-create --help`) — `AI
Spend (est.)` is a plain `NUMBER`, left as-is per owner decision (not worth the churn of a rename).

**Still open** (tracked for a follow-up review, not done here): spot-check the `Active`/`Completed`
Status heuristic guessed for the 156; triage the ~29 legacy pre-2026-taxonomy items now typed `Idea`
(a few look like dead test artifacts, e.g. `#416 "New test Idea"`, that may want closing instead);
decide whether to merge [PR #1283](https://github.com/spaarke-dev/spaarke/pull/1283).

PR: #1283. See `projects` memory note `project_portfolio-board-hygiene-and-ai-cost-tracking.md` for full
continuity context.

---
###### 2026-10-03 — ADR-012: stale `CommandRegistry` example removed (C-19)

`.claude/adr/ADR-012-shared-components.md` cited `CommandRegistry` in the present tense as an example of a shared
service. This PR deletes it (zero consumers, along with `EntityConfigurationService`, `CustomCommandFactory` and
`Toolbar/CommandToolbar`: dead infrastructure from a deleted PCF that carried a loop-over-selection
`deleteRecord`), so the example is removed. The full ADR's mention is left alone: it is past tense, and accurate
as history. Found by the `spaarke-ontology-platform-r1` reuse audit (cleanup item C-19). The ADR-012 UI Components table also drops its `CommandToolbar` row (15 groups, was 16), and the `PageChrome` row now reads `ViewToolbar`, because `PageChrome/CommandBar` is deleted in the same PR.

---
###### 2026-10-03 — root `CLAUDE.md` Calendar row corrected; `CalendarFilterPane` exported from the components barrel

Root `CLAUDE.md`'s "Calendar shared components" row described `CalendarSection` and `CalendarFilterPane` as two
intentional variants without saying that the side pane (`src/solutions/CalendarSidePane/src/App.tsx`) actually
renders **`CalendarSection` + `CalendarFilterOutput`**, not `CalendarFilterPane`. That migration was never finished.
Two files in the same solution (`utils/parseParams.ts`, `utils/postMessage.ts`) imported the type
`CalendarFilterPaneOutput` from `@spaarke/events-components`, which the package's barrels never exported, so the import
could not resolve. **The side pane is not currently in use** (owner, 2026-10-03). It may return, so the import is
fixed rather than the solution deleted: `Spaarke.Events.Components/src/components/index.ts` now re-exports
`CalendarFilterPane` and its types. `IEventDateInfo` is deliberately left out, because `CalendarSection` already
exports a different type under that name from the same barrel. Found by the `spaarke-ontology-platform-r1` reuse
audit (finding X19) and split out of that project's PR because it is unrelated to ontology work.

###### 2026-10-03 — ADR-002 WP-1 amended: a platform-native declarative mechanism may own an invariant (spaarkeai-word-add-in-r1 task 087)

Root `CLAUDE.md` (the "Dataverse write path" pointer row), `.claude/adr/ADR-002-thin-plugins.md` and
`.claude/constraints/plugins.md` now say an invariant's ONE owner is **BFF code, or a platform-native declarative
mechanism** that meets all three conditions: **(a)** Dataverse metadata, no Spaarke code runs (autonumber format,
alternate key); **(b)** the platform applies it on every create/update, whoever writes; **(c)** a scripted
per-environment `-Verify` named in the invariant's registry row. Plugins, low-code plugins, flows, webhooks,
**business rules** (owner: excluded from this revision) and client code never own an invariant. **Autonumber
columns** join "Permitted (not plugins)"; formula/rollup columns (computed on read — no owner) and business rules
(never an owner) are split, which also removes the old contradiction between `plugins.md` ("no owner needed") and
the full ADR ("not a substitute for WP-1 owners"). Origin: task 076's interim `MAT-`/`PRJ-` numbering
(write-path invariant I-11) is owned by Dataverse autonumber; the owner chose "A now, B as its own task" and
approved the wording 2026-10-03. Record: `projects/spaarkeai-word-add-in-r1/notes/087-adr-002-amendment.md`.

---
###### 2026-10-02 — provisioning: Document Intelligence via managed identity; stamps run the AI platform (T243, D22)

`customer-provisioning-orchestration-r1` T243 (owner D13, G17, G29).

- **`.claude/patterns/provisioning/manifest-driven-secret-catalog.md`**: the `from-bicep-output` example no longer
  lists `DocumentIntelligence-ApiKey` (retired by T243).
- The mechanism: the BFF's `TextExtractorService` accepts the API key OR the injected managed identity ("key if
  configured, else MI") and reports a rejected credential plainly; the catalog stops issuing the Document
  Intelligence key and gains the per-env literal `DocumentIntelligence__Enabled=true` — no stamp had ever set the
  BFF's AI master switch, so stamps ran with `NullTextExtractor` and analysis off. Owner D22: T244 + T246 are hard
  prerequisites of T186.

---
###### 2026-10-02 — provisioning: one customer-stamp template (T249, D19)

`customer-provisioning-orchestration-r1` T249 (owner decision D19).

- **`.claude/skills/azure-deploy/SKILL.md`**: `customer.bicep` is the only customer-stamp template (H2a); the
  `model2-full` half of the stamp row and the "routine infrastructure update via `deploy-infrastructure.yml`" path are
  gone — that workflow ("Validate Bicep Infrastructure") now only lints and compiles; the `gh workflow run` example is
  relabelled as on-demand validation.
- **`.claude/skills/ci-cd/SKILL.md`**: `deploy-infrastructure.yml` described as validate-only (no what-if / deploy).
- **`.claude/patterns/provisioning/INDEX.md`**: `stacks/` holds only standalone stacks.
- The mechanism: `stacks/model2-full.{bicep,json}` + its 5 parameter files deleted; the workflow lost its what-if,
  deploy, dispatch inputs and OIDC (it now also compiles `parameters/*.bicepparam`); `customer.bicep` wires SignalR
  to the stamp UAMI, receives the L2 principal on **Model 1** stamps (Website Contributor on the stamp BFF — a Model 2
  stamp is reached through Lighthouse; owner decision), logs Key Vault diagnostics to the workspace resource id, and
  drops `platformKeyVaultName` + the drifting `createdDate: utcNow()` tag. The L2 principal is ONE Worker option,
  `ControlPlaneIdentityOptions` (`ControlPlaneIdentity__PrincipalObjectId`, ValidateOnStart), shared by H2a and H4 —
  it replaces `KvSecretsPopulationOptions.ControlPlanePrincipalObjectId` (owner decision).

---
###### 2026-10-02 — provisioning: Model 1 on the dedicated code path; secret-free default (T225b)

`customer-provisioning-orchestration-r1` T225b (owner decisions D-12, D18).

- **`.claude/skills/provision-environment/SKILL.md`**: Steps 0c / 1.0 / 1c / 1e / 1g / 3 / 4.0 describe the 1:1
  pairing L2 now enforces — `Model1` ↔ `spaarke-hosted-model2`, `Model2` ↔ `customer-owned-model2`;
  `spaarke-hosted-model1-trial` is refused as an unknown profile. The Step 1e PowerShell checks the pair and runs
  the retired-profile stop first and then hard-stops `Model1` until T228 (no per-customer subscription yet — ADR-027;
  T228 removes the stop).
- **`.claude/constraints/provisioning.md`** §KV credential lifecycle: secret-free is the H4 default; H4 omits
  `BFF-API-ClientSecret` and `Dataverse-ClientSecret` on every new stamp (rule 2's hold covers the existing copy);
  the run-context bullet no longer lists a platform vault among L2-owned values. New prerequisite `PRQ-E-14`
  (registry `sprk_credentialmode` column — the H4 secret-free marker needs it).
- **`.claude/patterns/provisioning/manifest-driven-secret-catalog.md`**: `from-platform-vault` retired with
  `BingSearch-ApiKey` / `LlamaParse-ApiKey` (D18); `from-existing-kv` entries never reached on a new stamp.
- The mechanism: H2b, H13's I2 probe and H12c use the stamp's own AI Search / OpenAI for both models (the shared
  endpoints, the Cosmos tenant-filter template store and the `AzureOpenAI-Endpoint` seed entry are deleted);
  customer.bicep's dead `requireSecretFreeIdentity` parameter chain is removed.

---
###### 2026-10-02 — provisioning: Model 1 shared-tier Bicep retired (T225a, D-12)

`customer-provisioning-orchestration-r1` T225a.

- **`.claude/patterns/provisioning/{INDEX,openai-quota-region-composition,operator-rbac-bootstrap,keyvault-reference-identity-invariant}.md`**:
  no longer point at the deleted `stacks/model1-shared.bicep` / `model1-shared-l2-rbac.bicep`. The OpenAI pattern
  points at `customer.bicep`'s `openAiLocation` + `openai.bicep`'s `deployments` default; the two recovery recipes
  use stamp placeholders (`{stampSubscriptionId}`, `rg-spaarke-{customerId}-{env}`, …) instead of the retired shared
  subscription and names — the original incident is named once, as history.
- **`.claude/skills/azure-deploy/SKILL.md`**: the Model 1 Shared row records the deletion; the customer-stamp row names
  both full-stamp templates (`customer.bicep` via H2a, `model2-full.bicep` via `deploy-infrastructure.yml`) and flags
  that they are not reconciled (T235).
- **`.claude/skills/provision-environment/SKILL.md`**: roadmap item 3 parameterizes the OpenAI deployment set in
  `customer.bicep` (it named the deleted stack).
- The mechanism: the Model 1 shared stacks, the shared RBAC module and the four parameter files bound to them are
  deleted; the ARM-artifact workflow publishes only `customer`; `ArmDeploymentRunner` fails closed for Model 1 until
  T225b + T228; `bicep-e2e-dry-run.ps1` builds to a file (Windows `--stdout` crash) and has no EXPECTED_FAILURE mask.

---
###### 2026-10-01 — provisioning: operator intake validated at the edge with the handlers' own rules (T245c, G25 closed)

`customer-provisioning-orchestration-r1` T245c.

- **`.claude/constraints/provisioning.md`** + **`.claude/patterns/provisioning/run-context-contract.md`**: new rule — an
  intake value a handler has rules for is validated at `POST /api/runs` with the same rules: shared code where the
  rule is non-trivial (`UserProvisioningIntake` serves both H11 and the endpoint), the handler's own rejection code
  where it has one. The prerequisite-registry sentence no longer names a `tenancyModel` field that does not exist.
- **`/provision-environment` SKILL.md**: new **Step 1e-bis** (before 1f, so a bad value stops the skill before the
  registry placeholder is written) collects `identityPreset`, the user list (NativeAccount: names; B2BGuest: email),
  the Exchange scope group (prerequisite `PRQ-C-08` — created by the stamp tenant's Exchange admin; the skill never
  creates it), the Graph subscription resources and the Communication default mailbox; batch mode hard-stops on any of
  them. Step 1.0 refuses a batch intake file git would track (it carries personal data — owner decision D15;
  `runs/*-intake.json` is git-ignored). Step 4.0 sends the values (`usersJson` = `ConvertTo-Json -InputObject
  @($users)` — `-AsArray` double-nests; `@($null).Count` is 1, so `$users` is normalised first), sends
  `estimatedMonthlyUsd` as a string (a JSON number fails binding against the string map), and posts with
  `charset=utf-8`. Also fixed: the handler catalog row and the Step 3 plan said Model 2 skips H11 — nothing skips it.
- **`.claude/patterns/provisioning/manifest-driven-secret-catalog.md`**: `Communication-DefaultMailbox` is
  `from-intake-parameter`; only `ContentSafety-ApiKey` (T246) is left on the writer-less run-parameter channel.
- The mechanism: five intake keys required; H11's diagnostics and the Graph collaborators' logs identify users by
  position / Entra object id, never by name, email or UPN; `POST /api/runs` releases the I5 run guard when the run-store
  write fails; `RunContextContractTests`' known-gap list is empty and its catalog-member map derived by reflection.

---
###### 2026-10-01 — provisioning: values L2 owns are configuration or computed, never run parameters (T245b, G25)

`customer-provisioning-orchestration-r1` T245b.

- **`.claude/constraints/provisioning.md`**: new rule — a value L2 owns (its own principal, a platform vault, the SPE
  owning-app credential) is a validated Worker option (`AddOptions().Bind().Validate().ValidateOnStart()`), never a run
  parameter; an idempotency version is computed from the artifact the handler applies (`Handlers/ArtifactVersion.cs`),
  never supplied.
- **`.claude/patterns/provisioning/run-context-contract.md`**: pointer — such values are not `HandlerRunInputs` entries.
- **`/provision-environment` SKILL.md**: Step 6a's operator registry fallback now derives every promoted column from
  the run record (`interStepState.resourceGroupName` / `appServiceName` / `keyVaultName` / `bffBuildId`, the
  `importedSolutions` fingerprint, the run id as cache-bust token) — the same sources H13 uses. Before, `$rgName`,
  `$kvName`, `$deployedBffVersion`, `$cacheBustToken` … were used but never assigned, and two comments named
  InterStepState properties that never existed. Step 0.5b notes the Worker's SPE owner-entry prerequisite.
- The mechanism: `bicepVer` / `indexVer` / `secretsVer` are SHA-256 of the deployed ARM template / the applied index
  schemas / the embedded manifest; `SpeContainerOptions.ContainerTypeOwners`, `KvSecretsPopulationOptions`
  (`ControlPlanePrincipalObjectId`, `PlatformVaultName`) and `E2EAcceptance:ProvisioningScriptsDirectory` fail Worker
  startup when invalid; H9 publishes `InterStepState.BffApiUrl` / `BffBuildId`. 13 keys left `IntakeParameterCatalog`.

---
###### 2026-10-01 — silent-fail trap T7: `Customer__Id` on both BFF slots (T238, D-14)

`customer-provisioning-orchestration-r1` T238 (INCOMING-CUSTOMER-RUNTIME-IDENTITY §1.1–§1.2).

- **Root `CLAUDE.md`** customer-provisioning pointer row: trap catalog T1–T6 → **T1–T7**; and its stale
  "Model 1 (shared trial/SMB)" description corrected — both models are dedicated per-customer stamps since D-12
  (2026-09-30), the shared tier is retired.
- **`/provision-environment` SKILL.md**: trap catalog row + handoff "Traps verified" list gain T7.
- The mechanism: H4b writes `Customer__Id` = the run's customerId (verbatim) to BOTH App Service slots
  (manifest `per_env_settings`, source `from-intake-parameter:customer_id`); H13 trap T7
  (`CustomerIdentityT7Probe`) reads both slots' app settings from ARM and quarantines the run on a missing,
  blank or different value (`h13-trap-T7-customer-identity`).

---
###### 2026-10-01 — `check-task-status-drift.ps1` reads the layout task-create prescribes

`customer-provisioning-orchestration-r1` (T245a follow-up, owner-directed).

- The checker (gating step for `task-execute` Step 10 and `push-to-github` Step 1.65) only parsed rows with the
  marker and a three-digit id in the FIRST cell (`| ✅ 001 |`). The layout `task-create` Step 5 prescribes —
  id first, status in a later cell (`| 001 | Title | … | 🔲 [open] |`) — parsed as nothing: repo-wide ~100 project
  indexes reported `UNPARSEABLE`, and this project 1 of 186 rows, so the gate was red for a parser reason on every
  push. Now: id-first rows read their status from the cell holding the `[token]` (else the glyph); ids may carry
  `.N` / letter suffixes (`081.5`, `245a`); `| **001** | … |` rows (marker `**`, no status) read the later status
  cell; POML ids with an alphabetic prefix (`ENV-001`) pair with the bare index id; `complete` counts as done.
  Rows that parsed before parse identically. `-All`: unpaired 886 → 1,162 (2,438 before the prefix pairing),
  disagreements 74 → 1,540 — real stale statuses that were invisible; 25 projects now read fully clean.

---
###### 2026-10-01 — provisioning run-context contract: every handler input has one producer (T245a, G25)

`customer-provisioning-orchestration-r1` T245a.

- **`.claude/constraints/provisioning.md`** — new BINDING section "Run-context contract": `run.Parameters.NonSecret`
  holds intake values only (`IntakeParameterCatalog`, enforced at `POST /api/runs`); a value one handler produces for
  another goes in a `[ProducedBy]` `InterStepState` property; every handler input is declared in
  `Reconciler/HandlerRunInputs.cs`; `RunContextContractTests` enforces it (DAG ancestry + source scan). Background:
  a real run could not get past H0 — ~20 required inputs had no producer and unit tests seeded them by hand.
- **New pattern** `.claude/patterns/provisioning/run-context-contract.md` (+ INDEX row); `handler-registration-
  completeness.md` gains step 5b (declare the new handler's inputs).
- After the quality gates: the contract scan is a Roslyn syntax walk (aliases, `?.`/`!.`, strings and `nameof` handled;
  declared inputs must also be READ); `run.Parameters.Secrets` has no writer and only H4 may read it; H4's manifest
  sources are checked against `customer.bicep`'s `kvSecretValues` — all three now in the constraint and the pattern.
- **`manifest-driven-secret-catalog.md`** — `value_source` table gains `from-intake-parameter` / `written-by-h3`;
  `from-run-parameter` / `from-existing-kv` marked writer-less (every entry a pinned gap); the closed `per_env_source`
  set (`PerEnvSourceCatalog`, mirrored in the generator) and the `IntakeValues` rename.
- **`/provision-environment` SKILL.md** — real defects fixed: Step 1.0 and Step 4.0 compared `tenancyModel` to
  `'Model2Dedicated'`, which never matches since T223/T224 (`Model1` | `Model2`), so the Model 2 subscription hard stop
  and the `warnAndProceed` ban were dead; the Step 4.0 comment claiming L2 "ignores unknown keys" now documents the
  closed intake set (400 `intake-unknown-key` with `acceptedKeys`); Step 1c literals and examples corrected; Step 5
  notes that H3 now gates H4b / H6 / H8 / H9.

---
###### 2026-09-30 — `ci-cd` + `azure-deploy` skills describe the workflows that exist (plan G24)

`customer-provisioning-orchestration-r1` SESSION 28 (owner: fix drift at discovery).

- **`ci-cd`**: the "Primary CI Pipeline" section described `sdap-ci.yml` as the gate; it is now the CI Router
  (`ci-router.yml` → Tier 1 blocking + Tier 2 advisory), and `Router` is the **only** required status check on
  master (verified 2026-09-30 with `gh api repos/spaarke-dev/spaarke/rules/branches/master`). The "Supporting
  Workflows" table listed four files that do not exist (`build-only.yml`, `dotnet.yml`, `test.yml`,
  `auto-add-to-project.yml`); it now lists the eleven reporting/scheduled/publishing workflows that do. Merge steps,
  diagram, troubleshooting job names, `gh run list --workflow=` and the secrets note follow. `deploy-infrastructure.yml`
  is no longer described as auto-deploying on push (push/PR = validate + what-if; deploy = manual dispatch + approval).
- **`azure-deploy`**: removed the deleted `deploy-platform.yml` / `deploy-slot-swap.yml` rows; corrected
  `deploy-bff-api.yml` (dispatch only — never on merge) and `deploy-office-addins.yml` (auto on push) triggers.
- Same pass outside `.claude/`: `docs/procedures/ci-cd-workflow.md` rewritten against all 23 workflow files; stale
  references fixed in `docs/architecture/ci-cd-architecture.md`, `docs/guides/GITHUB-ENVIRONMENT-PROTECTION.md`,
  `docs/procedures/{DEPENDENCY-MANAGEMENT,testing-and-code-quality}.md`, `config/coverlet-nightly.runsettings`,
  `scripts/Deploy-Platform.ps1`; `workflows-validate.yml` / `ci-router.yml` header comments no longer claim
  "required check" / "shadow mode".

---
###### 2026-09-30 — `/provision-environment` enforces the customerId standard and records the display name (T237)

`customer-provisioning-orchestration-r1` T237 (owner D10, adopting unified-access-control-r2 D-14).

- **Step 1a** now validates `customerId` against the standard `^[a-z][a-z0-9]{2,7}$` (case-sensitive, `\z`-anchored;
  re-prompt in interactive mode, hard stop in batch), refuses the reserved ids `platform` / `shared` / `byok`
  (they name non-customer resource groups — the BFF already refuses them), and tells the operator to abbreviate long
  names once (`northwind` → `nwind`). It used to describe a kebab-case 3–32-character id with no check at all.
- **New Step 1a-bis `displayName`** — the customer's full name, written to `sprk_name` on the registry placeholder so
  the id ↔ name decision is recorded once. `intake.schema.json` gained the matching optional property.
- 🔴 **Step 1f fix**: `$tenancyModelMap` was still keyed `Model1Shared` / `Model2Dedicated`, so after T224 renamed the
  values the lookup returned `$null` and every placeholder row would have been written without `sprk_tenancymodel`.
  Keys are now `Model1` / `Model2`, and a missing mapping stops the step.
- Examples use compliant ids (`acme`, not `trial-acme-2026-08-18`). The Step 1c/1e tenancy and profile prose is still
  pre-T224; that is recorded under plan gap G6 for T225b.
- **Step 1.0 batch validation** now runs `npx -p ajv-cli@5 -p ajv-formats@3 ajv validate --spec=draft2020 -c ajv-formats`
  (same as the `provisioning-prereqs-validate` CI step, which was failing on every run without the formats plugin).
  The old `--strict false` silently skipped `format: uuid`, so a malformed `tenantId` passed batch validation.

---
###### 2026-09-30 — provisioning docs follow T226: H4-shared retired, secret-catalog pattern rewritten

`customer-provisioning-orchestration-r1` T226 retired the H4-shared handler (it copied keys from the
`sprksharedprod-*` services into a shared vault — a cross-customer isolation break under the dedicated-stamp
model) and the `from-shared-service` value source. The procedure surface still described both.

- **`patterns/provisioning/manifest-driven-secret-catalog.md` — rewritten against the code.** The previous
  version documented a `source: { type }` field the manifest has never had (the field is `value_source`), said
  the generator emits deployment-guide sections (it emits four `generated/` artifacts), and pointed at a test
  project that does not exist. It now lists the closed `value_source` set and its writers, the owner D13
  keyless-first rule, the literal-string hazard of an unresolvable KV reference, and the places a new
  `value_source` must change (task 214 changed none of the four code places, so every H4 run failed
  `ManifestReadFailed`).
- **`patterns/provisioning/handler-registration-completeness.md` — corrected against the code.** It described
  `AddKeyedTransient`, `ExecuteAsync`, `HandlerResult.Failed`, lower-case ids and a "3-file dance"; the code uses
  `AddKeyedScoped` factory forwarders, `HandleAsync`, `Success`/`Failure(FailureClass, …)`, `"H4"`-style ids, and
  a new handler also needs `Dispatchable` + a `DagAdvancer.HandlerDependencies` entry (the HANDLER-01 failure).
  Dispatchable count 21 → 20.
- **`constraints/provisioning.md`** — same handler-contract corrections; H4-shared removed as a drift-detection
  handler; the non-existent `HandlerIdempotencyTests` reference replaced; test path corrected.
- **`skills/provision-environment/SKILL.md`** — 🔴 **Step 4.0 now sends `containerTypeId`**: Step 0.5b read it but
  the run payload omitted it, so every run would fail H4's `SPE-ContainerTypeId` write and H8. Step 0.5b skips
  `status: retired` prerequisites (they used to run an empty recipe and report a pass). Handler catalog, DAG
  ordering and `subscriptionId` consumers no longer list H4-shared.

---
###### 2026-10-02 — root `CLAUDE.md` §1.1: product names vs engineering identifiers (spaarke-ontology-platform-r1)

**SpaarkeAi is now called the Spaarke Console.** Added a §1.1 naming table so the rename does **not** require
sweeping ~705 "SpaarkeAi" occurrences across 256 files: product name and engineering identifier are allowed to
differ, and the table is what makes the old name readable. `sprk_spaarkeai`, `src/solutions/SpaarkeAi/` and the
deploy script/workflow names are **explicitly unchanged** — renaming them is tracked at
[#1095](https://github.com/spaarke-dev/spaarke/issues/1095) and deferred because **37 of 62 active projects
declare `SpaarkeAi = Y`** and would inherit the merge conflicts. Same split already set for
~~ledger~~ → Decision Record. Naming authority stays
`projects/spaarke-ontology-platform-r1/notes/ontology-component-model.md` §3.

---
###### 2026-09-30 — `office-addins-deploy`: two manifest eras (spaarkeai-word-add-in-r1 task 078)

The skill's "Manifest Upload After Deploy" told operators to download `outlook/manifest.xml` — a path that
**404s**; Outlook's live XML is `/outlook/outlook-manifest.xml` (a trap this project had recorded, and still fell
into once). Rewritten into two eras: the LIVE XML add-ins (both hosts) and the new **unified app package** (Outlook
+ Word in ONE app, CI artifact `spaarke-addin-unified-package`, uploaded as App type "Teams app", tested first as a
`-TEST` zip assigned to "Just me"). Rollout: `projects/spaarkeai-word-add-in-r1/notes/078-manifest-decision.md`.

---
###### 2026-09-28 — five ADRs amended for D-12: no shared Model 1 tier (owner-approved)

Owner chose "amend all five" after the D-12 doc sweep found 51 BLOCKING files (recorded: 13). CLAUDE.md
§6.5 path **B** throughout. Common cause: each ADR assumed a **shared Model 1 tier** and leaned on a
`tenantId`-keyed control to make it safe — but under D-12 every Model 1 customer presents **Spaarke's**
`tenantId`, so those controls cannot separate customers **and pass anyway**.

- **ADR-009 (Redis caching)** — 🔴 a **MUST NOT was REVERSED**. The concise ADR said *"**MUST NOT** recreate
  per-customer Redis instances. Per-customer Redis is deprecated."* It now **MUST** provision one per
  customer: the key is `tenant:{tenantId}:…`, identical across Model 1 customers, so a shared instance
  **collides** — a correctness defect, not a cost preference. Decision §5's key format and `ITenantCache`
  enforcement are **unchanged**; what changed is the claim made about them (tenant separation, not customer
  separation) plus **one new MUST**: the key part *after* the tenant segment must discriminate the subject.
  That closes a gap the ADR never covered — `spaarke:tenant:{tenantId}:agent-thread:thread:v1` has constants
  in both remaining slots, so **every user in a tenant shares one Foundry thread**. Cross-*user*, inside one
  tenant, independent of tenancy model. Latent (`Enabled` defaults false), not live.
- **ADR-015 (AI data governance)** — Tier 2 + Tier 3 `MUST partition by tenantId` replaced by **MUST reside
  in the customer's own dedicated Cosmos account**, which is what actually backs the 7-year audit and GDPR
  Art. 17 erasure guarantees. Added **MUST NOT** partition by `/tenantId` — inside a per-customer account it
  is one constant value, so every document lands in a single hot partition. ✅ **Resolves a live
  ADR-042/ADR-015 contradiction** that predates D-12: ADR-042 already rejected `/tenantId` on *capacity*
  grounds while ADR-015 mandated it, and neither cited the other. ⚠️ No data migration ordered; ADR-042's
  legacy container stays as-is.
- **ADR-052 (workload placement)** — §6 allowed *"a shared multi-tenant Function app"* for Model 1,
  justified by invariants I2–I5. Now **one Function app per customer stamp in both models**, the **stamp
  UAMI** in both, and a **task hub per stamp** in §7. The old rule was not merely outdated but unsound: it
  cited as its safeguard the very controls that cannot work. The §10 per-tenancy-model approval gate is
  **kept deliberately**. ✅ `WorkloadPlacementDocDriftTests` re-run — 102 passed; it matches
  Functions-vs-BFF phrasings, not tenancy wording.
- **ADR-028 (auth v2)** — mechanical. Its shape table had **three** rows; old *"Model 2 — Spaarke tenant"*
  **is** the new Model 1, old *"Model 2 — customer tenant"* **is** the new Model 2, shared row deleted. ✅
  **Its Decision is untouched and strengthened**: *"every shape is intra-tenant, so MI-FIC covers all of
  them"* now has one fewer special case. Also closed a moot open question (the shared Model 1 app
  registration → one FIC per customer) and replaced the `2b/2c` labels with the condition they stood for.
- **ADR-013 (AI architecture)** — Context + the Azure Resource Requirements tables. Decision unaffected.
  Two TPM tables collapsed to one (quota is per-subscription-per-region). ⚠️ Flagged rather than silently
  fixed: the Model 1 config sample still shows `IndexPerTenant` keyed on `{tenantId}`, which resolves to
  **one index for all customers**.

Also: **D-12 §3's "every Azure resource is dedicated" gained three named exceptions** (owner-decided) —
Static Web Apps, App Insights/Log Analytics, Content Safety — each requiring a **`customerId`**
discriminator. The rule was previously unfalsifiable: all three are shared today with no per-customer
provisioning path, so "every resource" was already untrue. The list is **closed**; the test any future
candidate must pass is *holds no privileged legal content at rest*.

---
###### 2026-09-28 — ADR-027 Decision 1 amended: one Azure subscription PER CUSTOMER (owner-decided)

- **`.claude/adr/ADR-027-…md` Decision 1** rewritten, and the full ADR (`docs/adr/ADR-027-…md`) gains a
  `🟡 AMENDMENT 2026-09-28` block before the body. Was: *environment*-separated subscriptions, with
  *"**Production subscription**: All production shared and customer resources"* — i.e. one production
  subscription holding every customer. Now: **one subscription per customer**, containing that customer's
  resource group, in both D-12 tenancy models. Environment separation survives but is **subordinate** to
  customer separation. Decision 1's original text is retained and marked SUPERSEDED.
- **Constraints amended**: *"**SHOULD** use separate subscriptions for dev vs production"* → **MUST**
  provision one subscription per customer + **MUST NOT** put two customers in one. Azure Management Groups
  raised **SHOULD → MUST** (hand-applied policy does not scale to one subscription per customer). The old
  *"**MAY** add customer-specific subscriptions … NOT required for initial customers"* is **withdrawn**.
- **This answers the ADR's own open question.** ADR-027 §Context item 4 asked *"**Customer isolation**:
  Whether customers need their own subscriptions"* and **never answered it** — Decision 4 turned out to be
  about Dataverse CI/CD, so Decision 1 became the de facto answer by omission. Item 4 is now marked
  ANSWERED. That is what makes this an amendment (path **B**) rather than a violation.
- **Why now**: billing segregation became a requirement (a subscription is Azure's billing boundary, not a
  tag); under D-12 Model 1 **every customer presents the same `tenantId`**, so every `tenantId`-keyed
  isolation control — AI Search filter, Cosmos partition, SPE container resolver, the
  `tenant:{tenantId}:…` Redis key — **cannot separate customers and reports success while failing**; and
  Azure OpenAI TPM quota is **per-subscription-per-region**, so separate resources in one subscription
  still share a quota pool.
- 🔴 **Forced consequence**: a **dedicated App Service Plan per customer**. An app cannot use a plan in a
  different subscription, so "shared plan, dedicated app per customer" is **unavailable**, not rejected on
  preference — a real per-customer cost floor. Verified 2026-09-28 against Microsoft Learn + MS Q&A.
  ⚠️ The move-restriction's "same **resource group**" clause is about *moving* an app and is tighter than
  the create-time rule; only the cross-**subscription** prohibition is load-bearing.
- **Source**: owner decision D-12 §3a (`projects/unified-access-control-r2/notes/D-12-deployment-model-redefinition.md`),
  decided 2026-09-28; CLAUDE.md §6.5 path **B**. ⚠️ Decision 2's `rg-spaarke-platform-{env}` shared-platform
  group is a survivor of the retired shared tier and is **not** resolved by this amendment — tracked in
  D-12 §6 / `COMPONENT-INVENTORY.md` §7.

---
###### 2026-09-18 — ADR-038 A2: ban B8 targets reflection, not `InternalsVisibleTo` (owner-ratified)

- **`.claude/constraints/testing.md` ban 8** rewritten. Was: *"MUST NOT test internal/private methods via
  `[InternalsVisibleTo]` or reflection."* Now: **reflection into non-public members is banned**
  (`BindingFlags.NonPublic`, `GetMethod(…).Invoke`, `PrivateObject`-style); **`internal` +
  `[assembly: InternalsVisibleTo]` is permitted** for a member that is (1) deliberately extracted to be
  assertable, (2) pure or near-pure, and (3) carries a contract the public surface cannot express
  observably. A member made `internal` *only* to be reachable, carrying no contract, is still scaffolding —
  B6 and B9 still apply to it.
- **ADR-038 Amendment A2** added (`docs/adr/ADR-038-testing-strategy.md`); the §7 B8 heading, its
  "why scaffolding" rationale and the enforcement-table entry were retargeted to match. `tests/CLAUDE.md`'s
  B8 heading likewise. ADR-038's §7 summary table needed no change — it already said "via reflection", and
  is now the correct statement rather than the odd one out.
- **Why**: the ADR said three incompatible things. The §7 heading and the enforcement note banned
  `InternalsVisibleTo`; the §7 summary table banned only reflection; and the enforcement table filed B8
  under *"Blocked on a production refactor"*, conceding the ban could not be complied with. A rule in that
  state is cited when convenient and waived when not — the trap ADR-003 A1 named.
- **And B1 forces the narrow reading.** B8's remedy is "test through the public surface", but the defect
  class here is an OData `$filter` string, observable only by intercepting transport (**B1-banned**) or by
  reading the member that builds it. Finding A-5 is the worked example: task 001 could not pin it at all
  until task 007 extracted the predicates as pure `internal` members. Banning both routes leaves a live
  security predicate untestable. The reflection inventory the enforcement table counts — 12 call sites in
  10 files — is a different, tractable population, and it is the one worth banning.
- **Raised by** `unified-access-control-r2` task 106 Step 9.5 (adr-check W5 / code-review F13); **ratified**
  by the owner 2026-09-18; CLAUDE.md §6.5 path **B**. Touches no open task POML — every B8 citation in this
  project is in a completed one.

---
###### 2026-09-15 — ADR-028 A5: the impersonation helper fails closed (task 104, #990)

- **ADR-028 A5** (concise): a factual correction; no rule changes. The warning that "the enforcement is in
  the READ method, not in the helper" now says that the helper refuses too:
  - `DataverseImpersonation.ApplyAsSystemUser` / `ApplyAsEntraUser` throw on an empty id;
  - the Entra-oid path also throws on a tenant mismatch;
  - a request carries exactly one impersonation header.

  This implements ADR-052 §6 prerequisite **P3** on `work/unified-access-control-r2`. #990 closes when that branch merges. The
  MUST that a new impersonated path carry its own refusal is unchanged.
- **Why**: the helper used to add no header for an empty id. A call site that bypassed
  `RetrieveMultipleImpersonatedAsync` therefore ran app-only, unscoped, and still returned HTTP 200.

---
###### 2026-09-15 — ADR-052 §6 / ADR-028 A5: conditional Dataverse impersonation (owner-accepted)

- **ADR-052** (concise + full): an Azure Function — or a BFF job handler — MAY impersonate a Dataverse user, but
  only:
  - for work that user started through an authenticated BFF request;
  - with the caller id taken from a BFF-written, typed requester field on an Entra-only channel;
  - with the impersonated user being the one the output is delivered or attributed to;
  - through the shared fail-closed `Spaarke.Dataverse` helper.

  §5's MUST NOT is narrowed to its real intent: the caller's **token** never leaves the BFF request. The rule is **not
  usable until #988 (Service Bus Entra-only), #989 (typed requester field) and #990 (fail-closed helper) land**.
- **ADR-028 A5**: its scope extends from "a BFF request" to "a BFF-initiated job". OBO, user tokens and confidential
  clients stay forbidden.
- **ArchTest** `WorkloadPlacementGuardTests`: under `src/server/functions/**`, impersonation passes only through
  `DataverseImpersonation`. The raw `MSCRMCallerID` / `CallerObjectId` headers, and the ServiceClient's
  `CallerAADObjectId` (newly listed), stay banned. A positive control covers the helper path.
- **Why**: the blanket ban forced user-initiated async work to run app-only, bypassing the user's row-level security,
  or to stay synchronous. Microsoft documents impersonation for background processing, and it cannot widen the app
  identity's rights. Evidence: `projects/unified-access-control-r2/notes/decisions/function-impersonation-proposal.md`.

---
###### 2026-09-14 — `unified-access-control-r2` task 103: scheduled jobs run once — lease, slot guard, `AddScheduledJob<TJob>`

- **ADR-036 A1.1 (owner decision)**: a configured lease store that stays unreachable through the acquire retries
  means the tick is **not dispatched**, and it is recorded as **failed**, not skipped: every instance loses the store
  together, so nobody ran it. A1 rules 1, 2 and 6 are now implemented (full ADR §5).
- **Pattern `api/scheduled-jobs.md`** rewritten to the shipped framework:
  - register with `AddScheduledJob<TJob>` only;
  - one dispatch per schedule is the host's job, not the job's;
  - a manual trigger of a running job gets 409;
  - the slot guard, and designing a job that must not lose a tick to catch up.
- **Constraints**: `jobs.md` (the lease and the helper as built; `IScheduledJobLease` joins the host-neutrality list)
  and `bff-extensions.md` §D (the registration helper named).
- **Why**: every instance and slot ran its own cron, so the hourly notification scheduler sent duplicates on any
  multi-instance stamp. Three copy-pasted bootstrap hosted services also depended on hosted-service start order.
- **ArchTest**: `WorkloadPlacementGuardTests.ScheduledJobsRegisterThroughAddScheduledJobOnly`. Only
  `SchedulingModule` and the admin `JobsEndpoints` may touch the registry or store, so a per-job bootstrap cannot
  come back.

---
###### 2026-09-13 — `unified-access-control-r2` task 102: **new ADR-052 Workload placement** + ADR-001/004/013/036 amendments

- **New ADR-052** (`docs/adr/` + `.claude/adr/`): where background, scheduled and event-driven work runs — the BFF,
  Azure Functions or Container Apps Jobs — is decided **per workload** on stated signals (F1–F5 favour Functions,
  B1–B4 favour the BFF) against named Spaarke costs; **tie-breaker = fewer moving parts**. A Function reuses the
  stamp's user-assigned managed identity **app-only** (ADR-028 A4's app-only row: no new grants, no user sign-in),
  never the BFF app registration (no confidential client, no OBO). Durable Task is permitted in its own host, never
  inside the BFF. No new WebJobs; no new hand-rolled timer `BackgroundService`. Owner decisions D1–D7, 2026-09-12.
- **Amended (path B, root CLAUDE.md §6.5)**: ADR-001 A1 (narrowed to the BFF runtime; its Functions and Durable
  provisions superseded and kept as marked history) · ADR-004 A1 (queue-driven scope; the Durable prohibition
  withdrawn; atomic receive-side idempotency; duplicate detection is a create-time queue property) · ADR-036 A1
  (runtime as built; one dispatch per schedule via a distributed lease; slot guard; atomic per-unit claim; retry,
  heartbeat, `AddScheduledJob<TJob>` and host-neutrality rules) · ADR-013 and ADR-002 (pointers).
- **Why**: four directive eras contradicted each other on Functions, each deciding the host from the trigger. An
  agent following the newest ADR was flagged for violating an older one, and the project-setup template seeded
  every new project with a flat ban. `WorkloadPlacementDocDriftTests` (Tier 1) now fails the build when a
  contradicting phrasing reappears outside a reasoned `adr052-drift:allow` region.
- **Directives aligned (`.claude/**`)**: constraints `api.md`, `ai.md`, `plugins.md`, `jobs.md` (rewritten —
  placement, queue vs schedule, atomic receive-side claim, the truth about Service Bus duplicate detection, a
  non-generic `IJobHandler` sample, the scheduled-job rules) and `bff-extensions.md` (§A.1, §D, §E, the decision
  table, the source list); patterns `api/background-workers.md`, `api/scheduled-jobs.md` (rewritten to the runtime
  as built), `api/endpoint-definition.md`, `auth/graph-webhooks.md`, `testing/integration-tests.md`; skills
  `code-review` (+ `references/review-checklist.md`), `adr-check` (+ `references/adr-validation-rules.md`),
  `adr-aware`, `task-create`, `design-to-spec`, `mcp-tool-handler`, and `project-setup/references/claudemd-template.md`
  (which had seeded every new project's CLAUDE.md with a flat ban); root `CLAUDE.md` §17 gains a pointer row.
- **A generic `IJobHandler` never existed.** The contract is the non-generic `IJobHandler` (`Services/Jobs/IJobHandler.cs`);
  every generic mention in directives, ADR samples and docs was corrected.
- **ArchTests (Tier 1, blocking)**: `ADR001_MinimalApiTests` now reads method- and parameter-level attributes (the
  class-level-only scan could never fire on a Function) and its message states the rule's real scope, the BFF
  assembly. New `WorkloadPlacementDocDriftTests` (the drift guard, with reasoned `adr052-drift:allow` markers;
  formatting-proof after its first run found bold text slipping past it) and `WorkloadPlacementGuardTests`
  (timer-service ratchet at 14 · `IScheduledJob` host-neutrality · Functions-project location, references and
  app-only identity). Every rule has negative and positive controls.
- **Step 9.5 review fixes (same task)**: the drift guard's BFF-scope exemption now needs the scope phrase
  immediately after the match (a later "in the BFF" no longer hides a flat ban); it catches the house
  `MUST NOT use` / `never use` style, `///`- and `#`-wrapped lines, underscore emphasis, generic-`IJobHandler` crefs,
  and comments that still attribute the timer-service pattern to the BFF-runtime ADR; an unclosed or nested marker now exempts
  nothing. The Functions-project guard walks the whole repository, reads any attribute order, covers the in-process
  SDK, WebJobs and a Durable Task worker, and bans `ClientAssertionCredential` / `ClientCertificateCredential` /
  `ClientSecretCredential` and Dataverse caller impersonation, with a reasoned owner-approved exception list. The
  ratchet baselines are exact. ADR-052 is **Accepted** (2026-09-13) and gains an explicit "no Dataverse
  impersonation" rule (ADR-028 A5) in the Function identity row.
- **Follow-ups filed**: #976–#986 — timer migration, non-conforming consumers, two Service Bus defects, duplicate
  detection, MessageId gaps, `DataverseBackgroundJobStore`, atomic idempotency, the Insights Function Bicep, and
  the SPE container-type grant reconciliation.
- Evidence: `projects/unified-access-control-r2/notes/decisions/workload-placement-policy-evaluation.md`.

---
###### 2026-09-10 — `unified-access-control-r2`: root CLAUDE.md **§10 gains publish-size hazards THREE and FOUR**

- **Root CLAUDE.md §10 only.** No skill, ADR, pattern or constraint changed. §10 already documented two
  hazards that make a publish-size delta lie (the ageing baseline; the zip tool's ~1.3 MB spread on
  byte-identical content). Two more were measured on this project and are now recorded there.
- **Hazard 3 — the build environment.** A publish from a worktree you have been iterating in is not
  comparable to one from a fresh worktree, **even at the same commit, even after an apparent clean**. It
  produced a **+4.95 MB** delta that did not exist — plausible enough to send an agent auditing its code
  instead of its measurement. §10 already prescribed a fresh worktree for *master*; the addition makes it
  explicit that **the branch side needs the same discipline**, which the worked example had not said.
- **Hazard 4 — deep paths break §10's own procedure.** Past `MAX_PATH`, MSBuild reports
  `MSB3030: Could not copy … because it was not found` **for a file that exists**, and the resulting
  partial publish **zips smaller** — so a broken measurement reads as a win. A 262-char scratchpad
  worktree path triggered it; the `C:\` root is also not writable for the zip. The rule added: publish and
  zip from a short path, and **sanity-check the FILE COUNT on both sides** — differing counts mean one
  publish is incomplete and the delta is meaningless.
- **Why this belongs in §10 rather than a note.** Both hazards make the measurement *silently wrong in the
  direction of looking fine*, which is the same false-assurance class §10's existing two hazards guard
  against. A hazard recorded only in a project note is a hazard the next project meets fresh.
- Owner-directed 2026-09-10 ("follow the best practice"). Evidence:
  `projects/unified-access-control-r2/notes/phase4-poa-consolidation.md` (hazard 3, with the
  45.37/45.38 MB corroboration) and `notes/task-029-external-todo-parity.md` (hazard 4).

---


###### 2026-09-04 — `unified-access-control-r2`: **ADR-034 Amendment A1** — the access-conferring allow-list becomes first-class and per-surface (path B)

- **Both ADR-034 versions amended.** Root CLAUDE.md §6.5 **path B**. Adds a distinction the ADR did not
  originally make: discovery over the 6 identity tables stays **correct for AI scoping** and is
  **over-inclusive for authorization**. Nothing is retired — A1 **narrows one consumer**.
- **The prefix convention is replaced by an explicit registry**, covering **contact-typed AND
  organization-typed** lookups. `sprk_assigned*` silently *admits* `sprk_assignedmonitor` (a watcher,
  who should confer nothing) and silently *denies* `sprk_leadcontact` (who should confer access) —
  nobody chose either outcome, a naming convention did. Worse: under a convention, **renaming a column
  grants or revokes access**, so a schema edit no reviewer reads as a security change becomes one.
  A1 makes adding a conferring column a **registry edit** (FR-24).
- **Org-typed conferral was unfiltered.** M4 already resolves `sprk_assignedlawfirm1/2` to
  `Organization` — the precedent exists — but the live filter
  (`FilterToAccessConferringContactRoles`) covers contact-typed lookups **only**. Unfiltered org
  expansion confers access from *any* organization named on a record, **including opposing counsel**.
- **One mechanism, two policies.** The registry is a filter **inside** the canonical resolver (M1), an
  extension — **not** a second membership engine, which this ADR forbids and A1 does not create.
- **The 1-hop cap (M7/N4) is explicitly NOT amended, and needs no exception**: FR-26 denormalizes the
  core ancestor, so every child→core chain is **one hop by construction**. The data model removed the
  need rather than the rule being relaxed. M8/M9 event semantics and N2 unchanged (with the precision
  that real `teammembership` **is** legitimately used — the ban is only on **non-existent** entities).
- **Live-consumer check performed BEFORE amending**, since a per-surface split is only safe if nothing
  else treats unfiltered descriptors as an access answer. All consumers enumerated and classified:
  `AccessibleRecordSetService` (authorization — the one being rehomed), `MembershipEndpoints` (scoping;
  the caller's **own** memberships under OBO — a self-query), the briefing + playbook-node collectors
  (AI scoping), and `IThreadPrivateGrantProvider` (**not** a consumer — doc-comment reference only, no
  code dependency). **No other surface's behaviour contract changes.**
- **Three documented staleness items fixed in the concise ADR, each verified in source**: added
  `ResolveByContactAsync` (`IMembershipResolverService.cs:104` — the contact plane's only membership
  path; its absence implied the plane had none) and `MembershipResponse.RelatedByRole`; corrected the
  identity contract to **`sprk_primarycontact` FIRST with the AAD-oid cross-ref as *fallback*** — the
  table had documented only the fallback as if it were the primary.

###### 2026-09-04 — `unified-access-control-r2`: **ADR-028 Amendment A5** — workforce `systemuser` root sets derive from Dataverse's impersonated answer (path B, narrow)

- **`.claude/adr/ADR-028-spaarke-auth-architecture.md` gains Amendment A5** (concise-only; no full
  `docs/adr/ADR-028-*.md` exists — re-confirmed, consistent with the A2/A3/A4 notes). Root CLAUDE.md
  §6.5 **path B**, deliberately **narrow**: it amends **one clause** of A2 — the parenthesised
  derivation on the `systemuser` branch — and nothing else.
- **The change.** `systemuser` → ~~ADR-034 membership~~ → **Dataverse's own answer via app-only
  impersonated read, ∪ contact grants**. The token model, client surface, plane selection and
  Tier-1/Tier-2 split are all unchanged. A2's clause now carries an inline pointer to A5 so a reader of
  A2 cannot apply the superseded rule.
- **Why.** ADR-034 membership derivation approximates Dataverse by pattern-matching columns and is wrong
  in **both** directions — granting BU-matched records to users whose role depth doesn't cover them, and
  hiding records that were explicitly shared. Dataverse already computes this exactly (ownership, role
  depth, BU, teams, POA shares, hierarchy), at the same 3 round trips. It also **removes the need for a
  systemuser allow-list** — there is no approximation left to tame.
- **Broker-only compliance is recorded IN THE ADR, not just in project notes.** Impersonation is **not**
  OBO: it uses the BFF's **own app-only credential** plus an `MSCRMCallerID` header naming the user to
  scope to. The caller's token is never exchanged or forwarded — satisfying broker-only exactly as the
  implementing code defines it (`AccessibleRecordSetService.cs:22-24`: *"No caller-token exchange (no
  OBO)"*). Recorded here because a future reader meeting "impersonation" on a plane whose defining
  invariant is "no OBO" would otherwise have to re-derive whether they conflict — and could guess wrong.
- **Two precision points that a careless reading gets backwards.** (1) `MSCRMCallerID` takes the Dataverse
  **`systemuserid`**, *not* the AAD `oid` — `notes/access-model-decision.md` states the wrong pairing;
  the live helper uses the right one. (2) The **fail-closed lives in the READ METHOD, not the helper**:
  `RetrieveMultipleImpersonatedAsync` throws on `Guid.Empty` (`DataverseWebApiService.cs:978`), while
  `DataverseImpersonation` deliberately adds *no header* for an empty id — so a **new** impersonated call
  site that bypasses the read method would silently degrade to an unscoped app-only query. A5 requires
  any new access-scoped impersonated path to carry its own refusal.
- **Nothing weakened.** The A1/A2/A3 **no-OBO** prohibition is textually unchanged and still in force;
  the **CIAM/contact plane derivation is untouched** (and impersonation is unavailable to it regardless —
  a `contact` is not a security principal). ADR-034 is **not** amended. Blocking prerequisites recorded:
  `prvActOnBehalfOfAnotherUser` on the BFF app user + the app user staying Organization-scoped, with the
  **NFR-04 negative canary** (task 034) as the standing guard — impersonated reads must return strictly
  fewer rows than app-only, and **equality fails the build**.

###### 2026-09-04 — `unified-access-control-r2`: **ADR-003 Amendment A1** — two-surface authorization + the unified evaluator (path B)

- **`.claude/adr/ADR-003-authorization-seams.md` rewritten; `docs/adr/ADR-003-lean-authorization-seams.md`
  gains Amendment A1.** Root CLAUDE.md §6.5 **path B**. Retires **exactly four** rules that no longer
  described the code: *"two seams only"*, *"new auth logic MUST be an `IAuthorizationRule`"*,
  *"MUST NOT create new service layers for auth"*, and *"cache UAC snapshots per-request only"*.
- **Why, verified in source rather than inferred from docs.** `CachedAccessDataSource` caches access
  data in **`IDistributedCache`** at 2-minute (roles/teams) and 60-second (per-resource) TTLs — that is
  cross-request *and* cross-instance, flatly contradicting "per-request only"; and the external stack
  (`CallerPrincipalResolver` + `AccessibleRecordSetService`) is a **service layer, not a rule**. A rule
  nobody follows is a trap for the next reader, not a guardrail.
- **The replacement contract.** Two enforcement **surfaces** (Dataverse-native vs the BFF evaluator),
  and one evaluator returning **`(recordId → rights)`** — a map, never a bare id set, because a
  `HashSet<Guid>` structurally cannot carry a level (which is why matters and work assignments have
  none today). Additive terms compose by **highest-wins `max()`**; vetoes apply **after** the max in
  the order **deny-list → Restricted**; **Secure suppresses derived-member + org-expansion BEFORE the
  max** for every principal kind; **`"No Access"` is a veto, never a level** — modelled as a level,
  `max()` ignores it and an ethical wall fails silently in exactly the case it exists for.
- **The surface rule agents get wrong.** Ask *"does this read go through the BFF?"*, **not** *"is this
  the MDA?"* — an MDA-hosted PCF reading via the BFF is on the BFF surface, with SPA-equivalent
  exposure. Demonstrated: a user denied Read on all 442 documents saw and downloaded a matter's files
  through an MDA form's embedded PCF.
- **Nothing was weakened.** Fail-closed, machine-readable deny codes, authorize-before-`SpeFileStore`,
  and never-cache-**decisions** are all preserved verbatim. **`OperationAccessRule` is NOT orphaned** —
  the single live `IAuthorizationRule` (registered `SpaarkeCore.cs:96`) stays valid and registered;
  checked before amending, because retiring a MUST that a live consumer depends on would be an
  amendment that breaks running code. Also fixed the concise ADR's dead
  `patterns/auth/authorization-service.md` link (→ `uac-access-control.md`), logged as register §G row 2.
- **Sequencing**: A1 merges **before** task 032 implements the evaluator, so the code lands under an ADR
  that sanctions it rather than in violation of one.

###### 2026-09-03 — `unified-access-control-r2`: task status gets a greppable ASCII token (owner-directed)

- **`task-create`'s `TASK-INDEX.md` template now REQUIRES a bracketed ASCII token in the Status cell**
  — `🔲 [open]` / `🔄 [wip]` / `✅ [done]` / `⚠️ [escalated]` / `🟡 [blocked]`, mapped 1:1 to the POML
  `<status>` vocabulary. The emoji stays (a column of glyphs genuinely scans faster for a human); the
  token is additive in the same cell, so the table shape is unchanged.
- **Why: status is a DATA FIELD, and the emoji encoding made it unreadable by the default text tool.**
  `grep` here silently returns **0** for any character above U+FFFF, and 🔲 is U+1F532 — so
  `grep -c '🔲'` reported **zero open tasks on a project with 37**. ✅ (U+2705) is 3-byte and works,
  which made the failure look like bad data rather than bad tooling; it cost three wrong measurements
  in one session, one of them written into a recovery file as a false claim that the index was
  corrupt. Mechanism: [`FAILURE-MODES.md` G-16](FAILURE-MODES.md#g-16-grep-silently-cannot-match-characters-above-uffff-most-colored-emoji).
- **Owner framing, which is the right one**: *"it might look nice but it needs to be grep'able —
  otherwise we should have a field that is reliably greppable."* The deeper defect it exposes is that
  status is stored **twice** (POML `<status>` + index marker) with nothing keeping them equal — the
  same duplication that produced 17 disagreements across 92 tasks. The ASCII token does not fix the
  duplication; `check-task-status-drift.ps1` is what detects it. Long-term direction: the index should
  be **derived** from the POMLs rather than authored beside them.
- **`check-task-status-drift.ps1` now prefers the token and falls back to the emoji**, so it works on
  both new indexes and the ~150 pre-existing ones. Retrofitted this project's 92 rows; `grep -cF
  '[open]'` now returns **37**, matching the Python-derived audit exactly.
- 🔴 **A third defect caught by the script's own controls**: widening the row regex to span table
  cells made it match rows whose first cell is a **wave label** (`**P0-W0**`) and report a phantom
  drift on task 001. Reverted to the single-cell form with a "do not reintroduce a cell-spanning
  pattern" note. Both controls (seeded drift; unparseable index) re-verified after the parser change —
  that is the third time this guard's controls have caught a defect in the guard before it shipped.

###### 2026-09-03 — `unified-access-control-r2`: task-status drift check — a forcing function for CLAUDE.md §7

- **New `scripts/check-task-status-drift.ps1`**, wired into **`task-execute` Step 10** (the moment both
  writes happen) and **`push-to-github` Step 1.65** (the last reliable hook before the state goes public).
  Completion is recorded in TWO places — the task POML's `<status>` and its `TASK-INDEX.md` row marker —
  and nothing kept them in agreement.
- **The evidence**: a full audit of `unified-access-control-r2` on 2026-09-03 found **17 disagreements
  across 92 tasks** — **14 tasks finished and merged whose POML still said `pending`**, plus one finished
  task the index still showed as `🔄`. The index is updated as work proceeds; the POML status is a
  separate write that nothing enforced, and it was skipped 14 times. A drift of 14 is a missing check,
  not a discipline problem.
- **Both artifacts drift, in both directions** (POML stale ×14, index stale ×1), so the script never
  picks a winner — it names the task, says which side is behind, and tells the operator to resolve from
  a git completion commit.
- **Scoped to the CURRENT project, deliberately.** Repo-wide drift is **82 disagreements across 151
  projects**, concentrated in archived `x-`-prefixed work. Gating on that total would be red on day one
  and waived on day two — the failure that retired the God-class LOC ratchet (CLAUDE.md §11.5). `-All`
  gives a non-blocking repo-wide observation report instead, mirroring `report-large-server-files.ps1`.
- 🔴 **Two defects in the guard, caught by its own controls before it shipped** — the same pattern as
  task 092's route-agreement guard:
  1. **False positives on correct state.** v1 treated only `✅` as terminal, so it reported task 012
     (`completed-with-escalation` / ⚠️) and 034 (`blocked-shipped` / 🟡) as drift on its very first run.
     Both were correctly authored. The marker vocabulary is now matched, not narrowed — a gate that
     cries wolf on correct state is a gate that gets waived.
  2. **A parser that reads nothing must not report "clean".** 137 of 151 projects use an index row
     format this parser does not recognise. Returning "no drift" for them would launder a broken
     instrument into a green check, so POMLs-found-but-zero-index-rows is reported as **UNPARSEABLE**
     and fails in gating mode. This is `FAILURE-MODES.md` AP-12 applied to the checker itself — a
     lesson learned twice on 2026-09-03, when a `grep` with emoji patterns under a non-UTF-8 locale
     reported "0 open tasks" on a 37-open project, and a `jest --rootDir` from the wrong directory
     reported "232 failed suites / 0 tests".

###### 2026-09-25 — ADR-002 review: **no Dataverse plugins reaffirmed + Server-Side Write-Path rule (WP-1…WP-8)**

- **Decision (owner-approved, §6.5 Path C + clarification).** Reviewed ADR-002 against current Microsoft/MVP
  guidance (plugins still .NET Framework-only; packages replace ILMerge; steps still hand-registered; managed
  identity needs one FIC per customer env; low-code/Functions still preview) from the perspective of Spaarke as
  a product. Posture kept: **no plugins at all** — the old "thin plugins with exception approval" path is replaced
  by explicit reopen criteria. The defect the review actually found was **invariants enforced only in client
  wizards** (field mapping, core-ancestor stamping, container/index default-fill), silently skipped by every other
  write path including the Office add-ins. New rule: one BFF server-side owner per invariant; clients preview only;
  invariant-bearing tables written via BFF; security + on-load UX inline; non-product writes → async fix-up +
  reconciliation; security fails closed.
- **Updated**: `.claude/adr/ADR-002-thin-plugins.md`, `.claude/constraints/plugins.md` (+ INDEX),
  `.claude/patterns/dataverse/plugin-structure.md` (retired → redirect) + dataverse/INDEX + patterns INDEX +
  `testing/unit-test-structure.md` + `pcf/control-initialization.md`, `.claude/adr/INDEX.md`,
  `.claude/adr/ADR-028` (BaseProxyPlugin secret defect resolved in source). Skills: `spaarke-conventions`
  (IPlugin "✅ DO" sample removed), `dataverse-deploy` (phantom plugin-deploy CI section removed), `ci-cd`
  (plugin jobs removed; **flagged: `deploy-staging.yml`/`deploy-to-azure.yml` don't exist — section needs
  rewrite**), `adr-check` (+ validation rules), `adr-aware`, `code-review` (+ checklist), `design-to-spec`,
  `project-pipeline`, `project-setup` template, `pcf-deploy`, `code-page-deploy`, skills INDEX. Root `CLAUDE.md`
  §13 entry point + §17 pointer row. New: `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md`.
- **Removed (non-conforming)**: `Spaarke.CustomApiProxy` plugin (HTTP + plaintext secret + ILRepack),
  `EmailProcessingMonitor` PCF (dead endpoint), `scripts/Register-EmailWebhook.ps1` (dead endpoint),
  CrmSdk package pins. `ADR002_PluginTests` rewritten as a repo-wide zero-plugin guard (R1–R5 +
  negative/positive/scanner controls) and **armed in the Tier-1 blocking filter** (verdict-neutral under the PR
  #865 mid-shadow-window precedent). Dead plugin-size CI jobs **deferred** to post-cutover — router/tier2/sdap-ci
  are frozen while the shadow window runs (`projects/ci-cd-unit-test-remediation-r1/notes/post-cutover-adr002-ci-cleanup.md`).

###### 2026-09-29 — `spaarke-ontology-platform-r1`: new `FAILURE-MODES.md` **AP-14** (facade named for an entity it does not write) + an **AP-12** instance (Daily Briefing)

- **New `FAILURE-MODES.md` AP-14: a facade method NAMED for an entity it does not write.** (Authored as
  AP-13; renumbered on merge because `unified-access-control-r2` had already published its own AP-13 —
  a tool with no parameter for a thing you must control — to master. TOC entries added for both, which
  neither had.) Sibling of
  AP-12, separated because an *identifier* is trusted more than a *comment* — it reads as contract
  rather than commentary. Live instance: `IActionSeam.CreateTaskAsync` returns
  `CreateTaskResult.TaskId`, while its implementation `TaskActionCore` writes
  `new Entity("sprk_event")` with an `sprk_eventtype_ref` of type task. An earlier version of that core
  **did** write `new Entity("task")` and was fixed by `email-communication-intelligence-r2`; the name
  was never corrected with it.
- **🔴 Binding rule recorded: Spaarke does not use OOB `task` / `activitypointer`.** Zero such writes
  exist in `src/`. Tasks are `sprk_event` discriminated by `sprk_eventtype_ref`; to-dos are `sprk_todo`.
  The rule is load-bearing because `DailyBriefingCollector` queries `sprk_event`/`sprk_todo` and never
  `task` — so a write to an OOB activity table **succeeds and is then invisible** to the briefing, the
  Navigator, and every `sprk_event` grid. It fails silently; nothing in the build, tests, or runtime
  objects. Prevention: read the `*ActionCore`, not the seam; `grep 'new Entity("'` before asserting
  which table a path writes; treat any platform-vocabulary name in a Spaarke facade as suspect.
- **New AP-12 worked instance: `CommunicationRiActionService`'s Daily Briefing claim.** Its docstring
  states the app-notification "mirrors the action so it surfaces in Daily Briefing (spec Success
  Criterion 2)". Daily Briefing has **no appNotification dependency at all** —
  `DailyBriefingCollector.cs:4` says so explicitly — and queries six channels deterministically
  (`sprk_event`, `sprk_todo`, `sprk_document`, `sprk_matter`, `sprk_project`, `sprk_monitor`); the
  `NotificationCategoryDto` references are the **output** shape, not an input source. The action *does*
  reach the briefing, but via the `sprk_event` it creates. **Right outcome, wrong mechanism** — worse
  than a plain error, because the observable behaviour appears to confirm the false claim.

###### 2026-09-02 — `unified-access-control-r2`: new `FAILURE-MODES.md` **AP-12** — a comment becomes the constraint

- **New `FAILURE-MODES.md` AP-12: prose outlives the mechanism it describes.** Promoted from a single
  observation inside AP-11 ("prose has no compiler") to its own anti-pattern, because the consequence is
  not a wrong destination but a **wrong decision**. **Eight instances in one session.** The worst:
  `PathValidator.SmallUploadMaxBytes` had **zero code references** and its enforcing guard had been
  deleted, yet it became a **real 4 MiB product limit** purely because comments said it was enforced —
  refusing every file between 4 MiB and 250 MB, from three separate client copies of the same fiction.
  Two instances produced **wrong answers to the owner**: a hook docstring naming a privilege route that
  never existed (triggering an unnecessary escalation for a decision already made in code), and
  `ISpeFileOperations` asserting the simple PUT "takes no `@microsoft.graph.conflictBehavior`" — which
  drove a design conclusion **twice, the second time after this project had already written down that
  the claim was false**. Also corrected: three sites describing SPE permissions as "additive-only" and a
  misrouted write as "irreversible" (they are **container-level**; removing the item ends the access —
  the old framing invites hunting for a per-file ACL that does not exist), and a `TokenProvider` whose
  comment claims "authentication handled by browser session" while returning `''`, which makes the
  caller omit the `Authorization` header entirely.
- **Why it is durable, and the prevention.** Deleting code is loud (the build breaks); deleting a *claim*
  is silent, so nobody does it — and an agent reading a file top-to-bottom meets the comment **before**
  the code, so the claim frames the reading of the evidence that would refute it. Rules: treat any
  comment stating a **limit, route, role mapping, capability, or reason-something-isn't-wired** as a
  claim to verify before quoting it to a human; a **constant with zero references means the limit does
  not exist**; grep the prose in the same change that deletes a field or guard; and **correct in place
  with a dated "🔴 do not re-derive" note** rather than silently — one of these had been silently
  corrected before and came back.
- **Including your own project's notes.** This session's handoff asserted a missing `encodeURIComponent`
  that was present two lines above the cited line, and a consolidation plan that would have replaced a
  working upload client with one that cannot authenticate. Re-derive; never inherit a claim.
- Also **back-filled the missing AP-11 TOC entry** (AP-11 shipped 2026-09-01 without one).

###### 2026-09-01 — `spaarkeai-compose-r8`: residual-loss list gains a row, and the guard that keeps it honest gains three

- **[`docs/architecture/COMPOSE-WRITE-RESIDUAL-LOSS.md`](../docs/architecture/COMPOSE-WRITE-RESIDUAL-LOSS.md) republished (2026-09-01).**
  Two rows LEFT §2 — `indentation-dropped` and `paragraph-style-flattened` retired *with their premises*
  (unmodeled paragraph styles and indentation are now carried, so they moved to §3). One row ENTERED §2:
  `section-break-flattened`. That one is **not a new loss** — editing a paragraph holding an interior
  `w:sectPr` always dropped it; what was wrong is that the warning fired at *open*, whole-document, and the
  loss itself was **absent from the list**. The signed set grows from five rows to six, so it is flagged for
  owner accept/decline at UAT rather than added silently (issue #777).
- **`ComposeResidualLossParityTests` — three holes closed, each found by seeding a removal and watching
  nothing happen.** The test whose stated job is to fail when the document and the renderer disagree *in
  either direction* stayed green after a row was added to the document. Causes: (1) no interior-section-break
  family in the measured set — structural, since every other family is a **run** and `w:sectPr` lives in
  `w:pPr`; (2) the code missing from the check's hard-coded `known` list; (3) **Direction A scanned the whole
  document**, so prose *discussing* a code satisfied it — and on the second attempt, so did the sign-off
  amendment's own table. Direction A is now scoped to §2's loss table. **A green guard is evidence about the
  guard, not about the code, until you have watched it go red.**
- **New cross-runtime parity mechanism (issue #699)** — `tests/fixtures/compose-citation-parity/cases.json`,
  45 cases executed by BOTH the C# `CitationResolver` and its TypeScript mirror, plus
  `tests/Spaarke.ArchTests/ComposeCitationResolverParityGuardTests.cs` pinning the leading-label vocabulary,
  the `CitationShape` set and the range separators across both source files. Ported test cases — two
  hand-kept copies of the same expectations — cannot detect drift between themselves.
- **`projects/INDEX.md`** — removed a **duplicate `spaarkeai-compose-r8` row** (two rows, same project,
  branch and worktree path, differing only by date). `/conflict-check` consumes this registry, so a
  duplicate row is a coordination defect, not a cosmetic one.
###### 2026-09-01 — `unified-access-control-r2`: new `FAILURE-MODES.md` **AP-11** — code that runs but reaches the wrong destination

- **New `FAILURE-MODES.md` AP-11.** Three shipped, user-visible defects of one shape, none with a test:
  an upload adapter POSTing to a route the BFF serves at **no** prefix (every external-user upload 404'd);
  a dialog that wrote `sprk_issecure = true` and cascaded the **shared** BU container while never calling
  `provisionSecureProject` (a project *marked* secure, documents in the shared container, **no warning**);
  and an admin Delete that made **no server call**, stripped rows locally, and reported success.
  Root cause is three reinforcing blind spots — no compiler spans the TS↔C# seam; **optional**
  collaborators let an under-wired host silently skip the security leg; and the warning lived on the very
  wrapper the caller bypassed. Prevention: route-agreement fitness functions (extend the census — a guard
  scoped to one file is why the next file slips past), resolve nested `MapGroup` prefixes, refuse rather
  than degrade for security-relevant legs, never claim completion for an enqueue, and **grep the prose**
  when deleting a field (comments and user-facing strings have no compiler and outlive what they describe).
- **New guard** `tests/Spaarke.ArchTests/ClientUploadRouteAgreementTests.cs` (6 tests; ArchTests 176 → 182).
  Rule 1 = the adapter's target route exists server-side; **Rule 2 = it is never repointed at a
  caller-named** drive/container route, because the tempting one-line 404 fix reintroduces the exact defect
  this project removes. Carries two negative controls, three positive controls, and a control on the
  comment-stripper. **Rule 2 found a real over-reach in itself on first run** — it flagged a legitimate
  server-derives READ — and was narrowed to the `uploadFile` body.
- **AP-11 also records the meta-lesson**: a broad automated debt sweep is a **lead list, not a work list**.
  Its #1 severity claim was wrong, it missed the upload 404 entirely, and one "dead code" entry
  (`SprkChatBridge`) would have broken the shared-lib build. An adversarial pass (default verdict
  NOT-DEAD; ten consumption channels incl. `React.lazy(() => import(...))`, ribbon XML, `window.__X__`
  globals, string registries, PCF `dist` deep-imports) is what made it safe to act on. ~48 claims →
  40 confirmed / 3 refuted / 4 undercounted / 3 correctly unsure.

###### 2026-09-01 — `unified-access-control-r2` (#858): `worktree-sync` Step 3 could never complete; merge-gating corrected across two skills

**`worktree-sync` was broken, not merely imprecise.** Its Step 3 "Merge to Master" pushed directly to
master (`git push origin origin/{branch}:master`, with a `temp-master:master` fallback). Both forms are
rejected by the repository ruleset, so **Full Sync mode could not complete on this repo** — for months.
Rewritten to a PR-based flow.

- **`skills/worktree-sync/SKILL.md` Step 3 — rewritten.** PR-only merge: sync master in → verify → open/reuse
  a PR → wait for the FULL check rollup → `gh pr merge`. Includes the explicit "do not restore either form"
  note so the direct push does not come back.
- **`skills/worktree-sync/SKILL.md` Step 4 — two fixes.** (1) Main-repo path resolution used **two**
  `dirname`s on `--git-common-dir`; in a linked worktree that returns the main repo's `.git` directly, so two
  resolved to `C:/code_files` and the step silently no-op'd. Now one, with a `test -d` verification.
  (2) Added a dirty-tree branch distinguishing real uncommitted work (`diff --ignore-all-space` non-empty →
  STOP, never reset over it) from mixed-EOL churn (`i/mixed` blob + `attr/text eol=crlf` → re-dirties after
  BOTH `checkout --` and `stash push`; do not loop).
- **`skills/merge-to-master/SKILL.md` Failure Modes — corrected + extended.** The protected-branch row still
  named `…/branches/master/protection` as the detection fix; that endpoint returns **404 "Branch protection
  has been disabled"** here (classic protection is off; **rulesets** govern), which reads as "unprotected"
  and routes straight back into the rejected direct push. Now points at `…/rules/branches/master`. Step 3
  itself was already correct. Added a row for the required-check trap below.
- **Cross-cutting rule now stated in both skills — gate on the whole rollup, never the required check alone.**
  `Router` is the ONLY required check on master, so it can pass while other jobs are red and `mergeable`
  still reads `MERGEABLE`. Treat `mergeStateStatus: UNSTABLE` as STOP; only `CLEAN` proceeds. Near-miss the
  same day: `Router` green while two jobs were red from a broken solution build.
- **Two verification rules added to `worktree-sync`, both from measured failures.** (1) *Build the solution,
  not a project* — test projects glob `tests/integration/Shared/**`, so a file that compiles where you looked
  can break a project you never opened; a green single-project run is not evidence about the solution.
  (2) *After merging, verify the SUBSTANCE on `origin/master`, not just commit reachability* — this is what
  caught a user-facing string that outlived the mechanism it described, invisible to 11,757 tests and 28 CI
  checks. Comments and message strings have no compiler and often no test.

###### 2026-09-01 — `email-communication-intelligence-r2`: document-profiling failure mode + the 3 AI execution models documented

- **New architecture doc** [`docs/architecture/DOCUMENT-PROFILE-AND-AI-EXECUTION-MODELS.md`](../docs/architecture/DOCUMENT-PROFILE-AND-AI-EXECUTION-MODELS.md) —
  authoritative map of the three ways the BFF runs AI (node playbook · direct Action/linear ADR-043 · legacy sequential),
  the three divergent document-profile entry points (wizard + Compose = direct Action, Outlook/app-only = node playbook),
  the confirmed failure mechanism (Part 4), fix options, and a change-safety checklist. GitHub #919.
- **New `FAILURE-MODES.md` AP-10** — a single-level JSON-aware renderer over a double-nested, re-parsed config. The
  Layer-1 `RenderConfigJsonStructurally` escapes only the outer wrapper; `UpdateRecordNodeExecutor.ParseConfig` re-parses
  the nested `configJson`-as-a-string and throws `0x0A invalid at $.fieldMappings[0].value`. **Corrects the prior
  checkpoint hypothesis** ("falls back to flat at `:2284`" — the fallback never fires; the outer wrapper is valid JSON).
  Root cause settled by pulling the **live** node config from Dataverse, not by forward-reasoning from the renderer.
- **Root `CLAUDE.md` §17** gained a pointer row to the new doc (read-before-changing-the-file/Document-create-pipeline).
- **Not a fix** — this entry is investigation + documentation only; the production renderer is unchanged pending the
  owner's choice among the three fix options in Part 4.

###### 2026-08-31 — `email-communication-intelligence-r2`: infinite lazy-scroll is the standard for scrollable lists

- **New ADR-051** ([`.claude/adr/ADR-051-infinite-scroll-lists.md`](adr/ADR-051-infinite-scroll-lists.md)) — every scrollable
  list uses **infinite lazy-scroll + the canonical thin scrollbar**, **never a pager** (no numbered pages, prev/next,
  "Load more", or down-arrow/chevron next-page control). `<DataGrid>` is the standard impl. Strengthens ADR-021,
  composes under ADR-012. Added to [`adr/INDEX.md`](adr/INDEX.md).
- **New pattern** [`patterns/ui/infinite-scroll-list.md`](patterns/ui/infinite-scroll-list.md) — the how-to: reuse
  `<DataGrid>` (built-in `useLazyLoad` + sentinel `IntersectionObserver`); the **page-fullness `hasMore` fallback**
  (why MDA `Xrm.WebApi` grids silently capped at 25 — the platform strips `@…morerecords`/paging-cookie on FetchXML);
  the custom-scroller recipe; explicit **DO NOT** bans. Registered in [`patterns/ui/INDEX.md`](patterns/ui/INDEX.md).
- **`patterns/ui/thin-scrollbar.md` updated** — the DataGrid `gridScroll` inline drift it had flagged
  (`colorNeutralStroke2` / 4px) was converged onto the canonical `thinScrollbarStyle`; cross-linked to the new list
  pattern.
- **Shared-lib doc** `src/client/shared/CLAUDE.md` gained a "Scrollable Lists — Infinite Lazy-Scroll (ADR-051)" section.
- **Code (context)**: `useLazyLoad` `hasMore` now `moreRecords === true || page-was-full`; DataGrid `gridScroll` uses
  `thinScrollbarStyle`; reconciliation grid pages at 50. Test: `DataGrid/__tests__/useLazyLoad.hasMore.test.ts`.

###### 2026-08-25 — `spaarkeai-compose-r8` task 056: embedded objects carried through an edited paragraph

- **ADR-049 residual list**: the `complex-object-dropped` row moves **§2 (lost) → §3 (carried)**. Images,
  charts, shapes and OLE embeds now survive an edit to their own paragraph. A **text box** keeps the row
  (its words are already preserved as prose; carrying the box too would duplicate the sentence) — the new
  `pictTextBox` parity family keeps the warning code honest, exactly as `fldNested` does for fields.
- **Empirically settled**: the save's body swap does NOT prune main-part relationships. Verified by OPENING
  the saved package and resolving every `r:*` attribute, not by reading the renderer's "orphaned … inert
  weight" remark — now corrected in place. **Second stale-comment correction in this project**, after task
  049's bookmark claim. Evidence: `projects/spaarkeai-compose-r8/notes/056-object-carry-decisions.md` §1.
- **One opaque-carry mechanism, two consumers**: `TryParsePreviousProperties<T>` renamed
  `TryParseOpaqueCarry<T>`. No second contract (CLAUDE.md §11).
- **New gate — parsing is not sufficient for this construct.** Every attribute in the OOXML relationships
  namespace must RESOLVE against the carrier before a subtree is authored: a valid drawing naming a missing
  relationship would produce a file Word reports as damaged, which is worse than the drop it replaces.
- **ADR-049 I-2 unchanged** — no OOXML crosses the wire. A browser keystroke edit keeps its image because
  `ComposeBlockMerge.CarryUnmodeledConstructs` (the task-041 base carry already used for bookmarks and SDT
  shells) restores it from the block's pre-edit base.
- **Corrects task 057's `data-atom-display` fix**, which did not reach the `object` family: the attribute
  was re-emitted only when display text was TRUTHY, and the server emits an `object` atom EMPTY — so the
  placeholder label still leaked (`Object` → `Object: Object` → …) across `getHTML()` round trips. Opaque
  atoms now always emit the attribute, empty when absent; renderable atoms (tab/symbol) untouched.
- **Owner sign-off unblocked**: both rows the owner declined on 2026-08-25 are closed (fields 049/057,
  objects 056). Residual §2 is now nested/unterminated fields, text boxes, footnote refs, endnote refs,
  content controls.

## 2026-08-25 — Compose write fidelity: the CLIENT half of the field carry (task 057, `spaarkeai-compose-r8`)

- Task 049's Word-field carry was **unreachable from a keystroke edit**: `docxBridge.ts` never mapped a
  `field` atom into the posted model, and `composeInlineAtom` did not DECLARE the `data-field-*` payload,
  so ProseMirror dropped it at parse. Both closed. A producer with no consumer — this project's recurring
  failure with the polarity reversed.
- **A field is the first segment present in the run stream and ABSENT from the text coordinate space.** A
  tab or symbol contributes one character, which is what kept task 048's walk byte-identical to
  `rejectStateText`; a field contributes zero. Byte-identity is re-proven by two independent oracles (the
  verbatim-tier gate and the rebuild-tier redline diff), both verified to FAIL under a deliberate
  one-character injection — so they are not tests that only ever pass.
- **Fixed a `getHTML()` round-trip defect**: the atom's placeholder label (`"Field: 4"`) was re-parsed as
  its display text, compounding to `"Field: Field: 4"` on a second pass. Harmless while that string was a
  UI label; a document-content bug once task 057 made it the field's `cachedResult`, and reachable via the
  ~15s dirty-autosave tick. Fixed backward-compatibly (`data-atom-display`, falling back to `textContent`,
  so server HTML is unaffected).
- **Accepted scope extension**: `opaqueAtomNode.ts` + `compose-contracts.ts` sit outside task 057's
  declared outputs. Its escalation trigger fired on the literal predicate ("attributes do not survive the
  round trip") but not on the reasoning behind it — the payload was present in the server's HTML and only
  needed declaring, the same four-line mechanism task 048 used for `symFont`/`symChar` in that file. The
  agent flagged it and offered revert-and-redispatch rather than proceeding silently, which is the
  behaviour the trigger exists to produce.
- **Correction to the published list**: on a keystroke edit the field result's bold/italic/underline are
  NOT carried (an opaque atom holds no marks), so a bold cross-reference in a plain paragraph returns
  plain. `notes/049-field-carry-decisions.md` §4 had claimed those three survive — true of the server path
  only. Both documents corrected; the field itself still survives.

## 2026-08-25 — Compose write fidelity: Word fields carried (task 049, `spaarkeai-compose-r8`)

- `docs/architecture/COMPOSE-WRITE-RESIDUAL-LOSS.md`: the field row moves **§2 (lost) → §3 (carried)**.
  Ordinary Word fields now round-trip an edit to their own paragraph as their **instruction** plus the
  result Word last computed, in the authoring form the document used. §2 keeps a narrower row for
  **nested and unterminated** fields, which have no single reproducible instruction. Owner sign-off on the
  list is now blocked on task 056 (embedded objects) alone.
- **The gate is STRUCTURAL, not a keyword allow-list.** A per-instruction freeze would make one document
  behave two ways, and a frozen `REF` goes *silently wrong* rather than visibly broken — it keeps printing
  "Section 4" after renumbering. `w:fldLock` is carried so fields an author deliberately froze stay frozen.
  Decision record: `projects/spaarkeai-compose-r8/notes/049-field-carry-decisions.md`.
- **Corrects a stale claim in `ComposeDocumentRenderer`** (review 011-P4/P9) that "the model does not carry
  bookmarks". Untrue since task 041 (`ComposeBlockMerge.CarryBookmarks`), and verifying it rather than
  inheriting it is what allowed `REF`/`PAGEREF` to be carried LIVE instead of frozen — a carried
  cross-reference is only an improvement if its target is still there.
- **Known gap, tracked as task 057:** the carry is server-side (projection → model → renderer). A
  *keystroke* edit does not yet preserve a field, because `docxBridge.ts` does not map a `field` atom back
  into the posted model. Task 049 shipped the payload (`data-field-instr` et al) so the client half is a
  small, well-specified change; 057 owns it.

## 2026-08-25 — `spaarkeai-compose-r8` task 055 (whole-document anchored placement)

No procedure-surface change. Recorded for the ADR-049 evidence trail:

- **ADR-049 I-7 strengthened on the client.** The whole-document review-flag channel (`comments[]` — the
  `flag-risks` intent's ENTIRE output) now resolves deterministically and populates
  `AnchoredAnnotationAnchor.paraId`, closing a **dark producer**: that field shipped in R3 FR-11 documented
  as the PRIMARY anchor, with a live consumer (`AnnotationReanchorService` resolves by it first), and
  nothing ever wrote it. Every whole-document review flag had been re-anchoring by fuzzy scorer even when
  the model named its paragraph exactly.
- **One anchor precedence, three consumers.** `widgets/composeAnchorResolution.ts` is now the single home
  of paraId-vs-citation precedence, shared by the AI-edit path (`usePendingRedline`), the advisory-comment
  path (`ComposeEditor.placeAdvisoryComments`) and the review-flag path
  (`ComposeWorkspace.registerAiReviewComments`). Each keeps its own span policy. The two SINKS stay
  separate deliberately — collapsing them would cost either Word `w:comment` export or ledger-key
  idempotency; §11 reasoning in `projects/spaarkeai-compose-r8/notes/055-review-flag-placement-decision.md`.
- **A silent-drop defect fixed.** `registerAiReviewComments` gated on `target_text` alone, so after task
  054 a flag carrying a deterministic anchor with weak prose was dropped — precisely the BEST-anchored
  ones. The gate is now "somewhere to hang it AND something to say".
- **Client tripwire pattern established.** The prose-matching leg moved to `hooks/redlineTextSearch.ts` so
  a test can REPLACE it — the client twin of `ThrowIfTextSearched` (`ComposeEditAnchorPassSeamTests.cs`).
  ts-jest compiles to CommonJS, where a same-module call is un-interceptable, so a module boundary is the
  only available client seam. Future "prove X was never called" client tests should follow this.

## How to maintain this

**Every PR that touches** `.claude/skills/`, `.claude/agents/`, `.claude/settings.json`, `.claude/patterns/`, `.claude/constraints/`, `.claude/FAILURE-MODES.md`, or the root `CLAUDE.md` **MUST add an entry to the `[Unreleased]` section below** before merge.

- One entry per logical change. Cite the commit SHA or PR number.
- Use the categories: **Added**, **Changed**, **Deprecated**, **Removed**, **Fixed**.
- "Bumped version" and trivial typo fixes can be omitted.
- When a project releases (a `work/<project>` branch merges to master), promote `[Unreleased]` to `[<project-name>] - <date>` and start a fresh `[Unreleased]`.

If you're not sure whether to add an entry, add one. Too granular is better than missing.

---

## [Unreleased]

### Added — FAILURE-MODES **G-12**: a stale test assembly behind a *truthful* "up-to-date" build (2026-08-27, `unified-access-control-r2` Wave A)

**Fifth stale-assembly incident in this project across two waves — and the first that the existing defence does not catch.**

The standing rule from the 075 batch is *"always read the build result before the test result."* That defends against a **failed or skipped** build masked by a stale-but-green test summary. G-12 is different: the build **succeeds** and honestly reports "up-to-date", because the falsehood is in **filesystem metadata**, not in the build.

- **Mechanism 1 — backwards-moving mtime.** `Copy-Item` (and `cp -p`, archive extraction, some editor "revert file" paths) **preserves `LastWriteTime`**. Restoring a file from a backup therefore moves its mtime *backwards*, MSBuild's incremental timestamp comparison concludes the existing DLL is newer than its input, compilation is skipped, and `dotnet test --no-build` executes the **previous** assembly. Task 011 hit this as a test failure that *contradicted the source on disk*.
- **Mechanism 2 —** `dotnet build Spaarke.sln` **did not refresh the BFF test project's output**; the test csproj had to be built explicitly.

**Why it earns its own entry rather than a line under AP-8**: perturbation testing is the primary anti-vacuity tool, and a stale assembly silently converts *"I proved this guard is load-bearing"* into *"this guard is untested"* — while looking identical. All five incidents produced **confident, wrong** verification results. Detection is by artifact, not by log: compare the DLL's mtime against the source you just edited.

**Changed**: `.claude/FAILURE-MODES.md` — TOC entry + `### G-12`; anchor verified against the heading.

### Changed — publish-size measurement convention is now BINDING, not descriptive (2026-08-27, `unified-access-control-r2` Wave A)

**Root CLAUDE.md §10's publish-size gate was measuring in two incompatible conventions, and every individual report was correct.**

Three sub-agents on the **identical base commit**, each stating *"compressed incl. PDBs"*, reported **45.07 / 45.07 / 43.78 MB** — a **1.29 MB spread on the same tree**. Cause: the POML corpus carries **two baseline clusters** (~43.65–43.71 MB across 24 POMLs, 44.96 MB across 31), so each agent compared against whichever its own POML cited, computed a small delta, and correctly concluded "within ceiling". The set was incoherent; the defect was visible **only by comparing reports across agents**, which no per-task gate can do.

The convention *was* already written down — but as a parenthetical describing how the **baseline** had been measured, not as a requirement on **your** measurement. That is the gap all three fell into.

**Changed**: `.claude/constraints/azure-deployment.md` § "BFF Publish-Size Per-Task Verification Rule (NFR-01)" — added a binding five-field reporting contract (command · RID/deployment mode · configuration · compression level · PDBs in/out), a MUST NOT on cross-convention comparison, the incident record, and an instruction to re-baseline POMLs citing the stale ~43.7 cluster.

**Impact on the gate**: the ≤60 MB HARD STOP was never at risk. What was degraded is the **≥+5 MB single-task drift detector** — with a 1.3 MB convention gap circulating, a real regression can be absorbed as a convention artifact and vice versa. The gate kept its floor and lost the sensitivity it was added for.

### Fixed — ADR-038 Amendment A1: `tests/Spaarke.ArchTests/**` is now the EIGHTH KEEP path (2026-08-24, `spaarke-auth-v4-dataverse-MI` task 090)

**Closed a contradiction that lived inside ADR-038 itself**, and that had been mitigated at the skill layer
rather than fixed since 2026-06-26:

- ADR-038 §7 bans **B1–B5** (DI-registration tests, ctor null-check tests, `Mock<HttpMessageHandler>` wiring
  tests), and its own "Some discovery loss" consequence names *"NetArchTest-style architecture tests at
  Tier 1"* as the **sanctioned replacement** for what those bans give up.
- But §2's KEEP-path list enumerated **7** categories and **did not include `tests/Spaarke.ArchTests/**`**.
- `/test-diet` is a **mandatory gate at every project close** (root CLAUDE.md §7) and classifies anything
  outside a KEEP path as a path violation → delete candidate. **The gate therefore recommended deleting the
  exact mechanism the ADR prescribes.**

**Why it persisted for two months**: task 063 fixed the *symptom* (heuristic 0 in `/test-diet`, plus naming
the category in `tests/CLAUDE.md`). That made the pain stop, which also made the cause invisible — while
leaving the protection in a skill file and a module directive, neither of which is the ADR. Those drift:
the same task found `/test-diet`'s path list had *also* been missing `tests/integration/seam/**` since
2026-07-09, silently making every vertical-slice-seam test in the repo a delete candidate.

**Changed** — all four surfaces moved together so they cannot disagree:
- `docs/adr/ADR-038-testing-strategy.md` — 7 → **8** KEEP paths; new `structural-fitness-function` row;
  the "discovery loss" consequence now points at its protected home; full **Amendment A1** record appended
- `.claude/constraints/testing.md` — "Seven" → "Eight" KEEP path categories + the new row
- `.claude/skills/test-diet/SKILL.md` — heuristic 1's path list now includes `tests/Spaarke.ArchTests/**`;
  heuristic 0's ratification note updated from OPEN to RATIFIED. **Heuristic 0 is deliberately retained** —
  the path fix alone would still let heuristics 2–12 mis-flag fitness functions on naming (B13) and
  setup-ratio (B15) grounds
- `tests/CLAUDE.md` — "same terms as the seven paths" → the eighth KEEP path, citing A1

**Evidence the category earns it**: graduation criterion 12 was exercised the same day — a deliberate ninth
secret-bearing confidential client made `CredentialGuardTests` (FR-F1) and `CredentialCensusTests` (FR-F2)
fail, naming the offending `file:line`. Note `dotnet build` **succeeds**; the ArchTests fail — the CI gate
is what fails, not the compiler.

### Added (2026-08-23 — the Compose **write-side residual loss list**, with a parity test behind it · `spaarkeai-compose-r8` task 045 / FR-A10)

- **Added — [`docs/architecture/COMPOSE-WRITE-RESIDUAL-LOSS.md`](../docs/architecture/COMPOSE-WRITE-RESIDUAL-LOSS.md)** — publishes exactly what Compose does NOT preserve on save, as the write-side companion to `COMPOSE-READ-REFERENCE-FIDELITY.md` (no duplication: that one is the read path). The **scope rule leads**, because it is what makes the list short and true: loss is **per-edited-block, never per-document** — an untouched block is cloned byte-for-byte, so a construct survives *precisely because* the save never parses it. Eight degradation codes documented; bookmarks and property inheritance documented as carried.
- **Added — `tests/integration/seam/Compose/ComposeResidualLossParityTests.cs`** — the forcing function. FR-A10 required the parity to be **demonstrated, not asserted**, so the document is not maintained by hand-review: the test measures every construct family through the real renderer (twice — untouched block and edited block) and fails if the document and the code disagree **in either direction**. Under-claim (an undocumented loss) and **over-claim** (a code the renderer no longer emits, or a family it actually preserves) are both failures — the second is the direction that lets a residual list rot into fiction while still looking maintained.
- **Fixed — `Services/Compose/ComposeBlockMerge.cs`: an INLINE `w:sdt` content control was dropped in SILENCE.** Found by the parity check on its first run (`edited: 0/1 kept · codes: (none)`), not written into it. Only the *block-level* `SdtBlock` had a shell carry and a warning; an inline control — a party name, an effective date, a defined-term placeholder, the ordinary shape in a legal template — was on no taxonomy list at all. `sdt` joined `ReportableConstructs` reusing the **existing** `hard-tier-sdt-flattened` code (root §11 — its client copy already read *"A content control … was saved as plain text"*), and the now-duplicate explicit warn on the block-level path was removed. A hand-written residual list would have inherited the same blind spot: you cannot document a loss you do not know you have.

### Changed (2026-08-21 — ADR-049 **R8 third amendment**: base re-projection + block copy-through · `spaarkeai-compose-r8`)

Owner-accepted §6.5 **Path B** amendment (*"ADR-049 is fine."*, 2026-08-21). Drafted by task 031 on the evidence of the Phase-3 architecture gate; applied at the start of task 040 rather than at the planned 045 wrap-up task, because while the write was outstanding ADR-049 still told a reader that *"render-on-save supersedes surgical byte-patch"* — the exact guidance that produced the defect 040 exists to fix.

- **Changed — [`.claude/adr/ADR-049-compose-shadow-document.md`](adr/ADR-049-compose-shadow-document.md)** — added the **R8 Path-B Amendment**. **The save renders from the content model AND preserves untouched content; these are not alternatives.** At save time the renderer re-projects the retained baseline server-side, pairs its blocks against the posted model **by document order** (`paraId` corroborates, never keys — duplicates are spec-legal across `mc:AlternateContent` and Word regenerates ids on save), then dispatches per block: unchanged → **clone the baseline's `w:p` subtree verbatim** with zero property logic; changed → render with property inheritance; unmergeable → thin render + warning, **never a content refusal**. Codifies **seven standing invariants** and — load-bearing — the **paired MUST**: *invariants (1) every-save-terminates-in-a-defined-outcome and (2) untouched-blocks-are-preserved are a PAIR; no future amendment may trade one away to obtain the other.* Both prior amendments did exactly that (**R4** took preservation and lost termination → the HTTP 422 treadmill; **R6** took termination and lost preservation → silent whole-body rebuild), which is why this clause exists. Adds normative **mechanism MUSTs** (direct `w:body` children only — never `body.Descendants<Paragraph>()`, which interleaves `w:txbxContent` paragraphs and mis-pairs every block after the first text box; "unchanged" decided against a fresh server-side re-projection, never text equality; comparison **fails closed**, baseline unavailability **fails open**). **Status line + footer updated**; the `docs/adr/` twin the footer said did not exist now does. **Scope guard**: save path only — R4.5's read/reference invariants **F-1…F-5** and **I-7** are untouched, and **I-5 (one body author) is reinforced, not relaxed** — the merge lives inside `ComposeDocumentRenderer`.
- **Added — [`docs/adr/ADR-049-compose-shadow-document.md`](../docs/adr/ADR-049-compose-shadow-document.md)** — the extended record (context, mechanism, consequences, rejected alternatives, evidence). Deliberately scoped to the R8 amendment's full reasoning rather than duplicating the whole ADR: two long documents saying the same thing drift.
- **Changed — [`.claude/adr/INDEX.md`](adr/INDEX.md)** — the 049 row still described R4's surgical `ComposeShadowPatchEngine` byte-patch and I-4 byte-identity as the save contract (never updated for R6 either). An agent scanning only the index would have taken the twice-superseded rule as current. Row rewritten to the R8 contract + status corrected to "Accepted, amended 3×".
- **Changed — [`docs/adr/INDEX.md`](../docs/adr/INDEX.md)** — added the missing ADR-049 rows (main table + Backend/API domain table); `Last Updated` refreshed.
- **Changed — root `CLAUDE.md` §17 Compose row** — same staleness, higher blast radius: root CLAUDE.md loads **every session**, and its Write/save half still read *"edits = step-level ops anchored `(paraId,runIndex,offset)` applied by ONE `ComposeShadowPatchEngine` byte-author"* (R4 — it had never been updated for R6). Replaced with the R8 contract + the paired MUST + a pointer to the extended record; the Read/reference half (R4.5) is unchanged and still accurate.

**Evidence** (measured, not argued — threshold ratified by task 023 *before* any prototype number existed): overall block preservation **18.08% → 100.00%**, near-tier **6.67% → 100%** on every one of 18 corpus documents, zero hard-fails, zero honesty violations, zero cumulative drift over 5 round trips, +2–19 ms per save, no new NuGet, publish 43.68 MB (−1.28 vs the 44.96 MB net10 baseline). `projects/spaarkeai-compose-r8/notes/{gate-contract,control-measurement,merge-prototype-results,gate-decision}.md`.

**Read this caveat with the numbers**: the gate measures **untouched** blocks and excludes the edited one by construction. The paragraph the user types in is still rebuilt from a model carrying `w:jc`/`w:b`/`w:i`. **Task 041 (FR-A04 property inheritance) owns that and is neither optional nor deferrable.** `ComposeShadowPatchEngine` is **NOT** confirmed subsumed (it serves the op-log path) and must not be deleted on this evidence — task 074 stays blocked.

Authored main-session per §3 write boundary.

### Fixed / Added (2026-08-20 — ADR-010 example corrected + new anti-pattern **AP-7** · `spaarke-auth-v4-dataverse-MI` task 011)

- **Fixed — [`.claude/adr/ADR-010-di-minimalism.md`](adr/ADR-010-di-minimalism.md), "Allowed Seams"**: the example read `services.AddSingleton<IAccessDataSource, DataverseAccessDataSource>()`. That is **not what the code does and must not be copied**. `DataverseAccessDataSource` is a **transient typed HttpClient** (`SpaarkeCore`) decorated by a scoped `CachedAccessDataSource`, and it holds **mutable per-instance auth state** (`_currentToken`, the `HttpClient`'s `Authorization` header) — so a singleton registration is a **data race that can bleed a token between users**, not merely an efficiency question. Corrected to the real registration, with the reason stated inline and a pointer to the pattern that *does* solve expensive shared state on a transient type (a static `(tenant|client|secret-fingerprint)` confidential-client cache). The example's actual point — that `IAccessDataSource` is one of only two sanctioned multi-implementation seams — is unchanged. Surfaced by `code-review` finding S-14 at task 011's Step 9.5 gate.

- **Added — [`.claude/FAILURE-MODES.md`](FAILURE-MODES.md) **AP-7: Converting a silent fallback into fail-fast, verified with targeted tests only**. Task 010 correctly replaced a silent `DefaultAzureCredential` fallback with fail-fast validation, verified with targeted seam tests + build + publish + CVE — all green — and shipped **13 failing contract tests**, found only when task 011 ran the full suite. Root cause generalises: **callers that depend on a silent fallback are by definition invisible at the change site** (they supplied nothing — there is no reference, call, or type dependency to grep for), and a targeted test run selects tests *near* the change, which is exactly the set that excludes them. Prevention: run the FULL suite for any change converting a fallback/default/permissive branch into a throw; and when failures surface in a later task, **stash and re-run before calling them pre-existing** — "fails on master too" and "fails without my current edits" are different claims.

### Changed (2026-08-20 — ADR-028 **A4 adoption CONFIRMED** + E4′ wiring correction · `spaarke-auth-v4-dataverse-MI` task 003)

- **Changed — [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](adr/ADR-028-spaarke-auth-architecture.md), Amendment A4**: added an **ADOPTION STATUS** block recording that A4 is no longer accepted-on-reasoning but **verified on the wire**. Task 002 proved, against a real delegated user token on `spaarke-bff-dev/staging`, that the OBO grant succeeds under a Managed-Identity-issued client assertion — Graph/SPE, Dataverse `user_impersonation` (with `upn` preserved, so row-level authorization still evaluates as the *user*), and long-running OBO — with a negative control that fails loudly when the assertion is minted for the wrong identity. **MI-FIC is the adopted credential; the KV-certificate alternative was NOT taken** (it remains sanctioned where the same-tenant rule cannot hold, e.g. an unresolved cross-tenant Model 2 shape). This closes the question that three prior audits closed *wrongly* on an unrecorded premise.

- **Fixed — same file, A4 "Preferred wiring" section**: annotated that `Microsoft.Identity.Web`'s declarative ordered `ClientCredentials` JSON — presented by A4 as the preferred wiring — **is not usable in this codebase** (finding **E4′**). The repo has zero `EnableTokenAcquisition` / `ITokenAcquisition` / `IDownstreamApi` / `ClientCredentials` in any `.cs`; `AddMicrosoftIdentityWebApi` is inbound validation only; `Spaarke.Dataverse` has no Identity.Web reference. The JSON is retained as accurate *general* Microsoft guidance, but the direct-MSAL `.WithClientAssertion` + `ManagedIdentityClientAssertion` path is the mechanism here — **and the ordered fallback the rollback story depends on must therefore be built, not inherited**. Without this note a reader would configure the JSON, observe no effect, and reasonably conclude MI-FIC does not work.

- **Fixed — [`src/server/api/Sprk.Bff.Api/CLAUDE.md`](../src/server/api/Sprk.Bff.Api/CLAUDE.md)** (task 002, listed here for the auth-surface trail; not a `.claude/` file): removed the assertion that OBO *"still requires `BFF-API-ClientSecret` (confidential client per OAuth spec)"* — the exact false sentence that caused three audits to conclude the secret was permanent — and replaced it with the A4 shape plus the empirical evidence.

### Removed / Changed (2026-08-20 — God-class LOC ratchet RETIRED; replaced by complexity guidance)

- **Removed — `tests/Spaarke.ArchTests/GodClassGuardTests.cs`** (the hard CI gate on `src/server` file LOC). It gated on line count — the wrong instrument for a gradual, judgment-laden signal — froze existing large files at arbitrary values, and blocked normal feature work on active files (Compose, Chat) with a build failure that had to be hand-waivered. Per ADR-038's own "coverage = observation, never a gate" precedent, **size is now observed and complexity is evaluated by humans where the work is authored.**
- **Added — [`docs/standards/COMPONENT-COMPLEXITY.md`](../docs/standards/COMPONENT-COMPLEXITY.md)** — the standard: evaluate complexity/cohesion (responsibilities, coupling, ctor deps, branching), not LOC; when a large *cohesive* file is legitimate; decompose when responsibilities diverge. Wired into **root `CLAUDE.md` §11.5** (new) + **§17 pointer** (replaces the god-class-ratchet row), **`task-create` §3.5.6** (component-complexity check), **`code-review`** (maintainability dimension — complexity *direction*, not size), and a **non-blocking observation report** `scripts/report-large-server-files.ps1`. `.claude/patterns/testing/god-class-ratchet.md` converted to a RETIRED redirect stub; pattern INDEXes + project memory updated.

### Added (2026-08-18 — Navigator side-pane architecture pointer · spaarke-side-pane-navigation-history-r1 close-out)

- **Added — root `CLAUDE.md` §17 pointer** to [`docs/architecture/SPAARKE-SIDE-PANE-NAVIGATION.md`](../docs/architecture/SPAARKE-SIDE-PANE-NAVIGATION.md) (the docked "Navigator" pane). Makes the reusable feature discoverable — most importantly the `ensureNavigatorSidePane()` code-page registrar, which the doc designates a **standard code-page build step**. Doc itself refreshed post-UAT (not a `.claude/` file, no changelog obligation, noted here for context): access-based Monitored (Dataverse security trim, no owner filter — no BFF), `sprk_communication`→Email-code-page routing, name-resolution for bookmarks, the filled-evenodd-ring technique for outline pane icons, and entity-scoped ribbon ids.

### Changed (2026-08-17 — ADR-028 **Amendment A4**: secret-free confidential credential for OBO · `spaarke-auth-v4-dataverse-MI`)

Owner-directed §6.5 **path B** amendment. Fixes a rule that was **unsatisfiable for OBO** and had been generating recurring false-positive findings on every auth-touching task.

- **Fixed — [`.claude/skills/adr-check/references/adr-validation-rules.md`](skills/adr-check/references/adr-validation-rules.md)**: the `new ClientSecretCredential` rule excluded matches via `$_.Path -notmatch 'OBO|onBehalfOf'` — a **path** filter that never matched, because "OBO" appears in file *content*, not in file names. **Every OBO site therefore tripped the check on every run**, with no sanctioned alternative to migrate to. Replaced with an **E-3 / E-1 allowlist**, and added a second rule that flags **new** `.WithClientSecret(` sites and per-request `ConfidentialClientApplicationBuilder` construction (client assertions require singleton-cached CCAs). This is the concrete fix for the "cascading CI issues" this amendment was raised to stop.
- **Changed — [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](adr/ADR-028-spaarke-auth-architecture.md)**: split the line-24 MUST into **app-only** (`DefaultAzureCredential`, UAMI) vs **confidential client acting as the BFF identity** (**MI-FIC** default, **Key Vault certificate** alternative, **never a secret**) — `DefaultAzureCredential` cannot perform an OBO exchange, which is why the old rule could not be satisfied. Added **Amendment A4** (required shape, platform constraints incl. same-tenant rule / UAMI-only / `api://AzureADTokenExchange`, normative deployment-shape table — **every Spaarke shape is intra-tenant so MI-FIC covers all of them**; the Spaarke-owned-app-reg-with-customer-tenant-compute shape is explicitly ruled out per owner decision 2026-08-18, so no certificate provisioning is required; the 20-FIC cap is documented as a non-factor, alternatives rejected) and transitional exception **E-3** (retained `BFF-API-ClientSecret`, time-boxed to `spaarke-auth-v4-dataverse-MI`, does **not** license new sites). Replaced the Key Patterns C# sample, which had been teaching `ClientSecretCredential` as the fallback. A4 does **not** weaken the A1/A2/A3 "no OBO on external / collaboration / module-host planes" invariants.
- **Fixed — [`.claude/constraints/auth.md`](constraints/auth.md)**: corrected *"OBO flow (OAuth spec requires confidential client + secret)"* → OAuth requires a confidential **credential** (secret / certificate / federated client assertion). **This single clause foreclosed the question in every prior auth audit.** Added the A4 MUST/MUST NOTs.
- **Changed — [`.claude/skills/adr-check/SKILL.md`](skills/adr-check/SKILL.md)** + **[`.claude/skills/adr-aware/SKILL.md`](skills/adr-aware/SKILL.md)**: added the A4 anti-pattern row; corrected "`ClientSecretCredential` for Graph" → "for app-only".
- **Changed — [`.claude/patterns/auth/service-principal.md`](patterns/auth/service-principal.md)**: updated to A4 (two credential classes, shared provider, singleton CCA caching); corrected the stale claim that the Dataverse SDK is constructed with `ClientSecretCredential` — migrated to MI by `code-quality-and-assurance-r3` #3b.

Evidence base: `projects/spaarke-auth-v4-dataverse-MI/notes/{RESEARCH-FINDINGS,CREDENTIAL-INVENTORY,TENANCY-AND-CREDENTIALS}.md`. MI-as-FIC is **GA since 2025-05-08**; Microsoft ranks client secrets *"Development and testing only."*
### Changed (2026-08-17 — push-to-github Step 1.7 real-DV smoke gate · smart-todo-r5 task 060)

- **Changed — [`.claude/skills/push-to-github/SKILL.md`](skills/push-to-github/SKILL.md)**: added **Step 1.7 — Real-Dataverse Smoke Check (Widget/Dataverse Changes)** (spec FR-20 / PROC-1). For any push that changes Dataverse-querying widget/component/service code, the pre-flight flow now WARNs + asks whether ≥1 real create+read against **real** Dataverse was exercised — a mock/prototype harness passing is not sufficient. Advisory (ask-user-first), same non-blocking shape as Steps 1.5/1.6 — **not** a CI script or hard block (§11 — extended the existing skill rather than authoring a new `/real-dv-smoke` command). Rationale cited in-step: R4 UAT-5/6 burned deploy cycles because the `spaarke-prototype` harness mocked a `sprk_contact` entity that doesn't exist in real Dataverse (real is OOB `contact`); the mock hid the entity-name bug. Also added a "Tips for AI" pointer.



- **Changed — [`.claude/patterns/testing/god-class-ratchet.md`](patterns/testing/god-class-ratchet.md)** + root `CLAUDE.md` §17 pointer: frozen-file count **14 → 13**. `DataverseWebApiService.cs` graduated off the ratchet — RED-4 "B" hardening deleted ~1,414 LOC of runtime-dead document/analysis/KPI/generic/processing-job/communication/health code (unreachable — those interfaces route to the SDK impl per `GraphModule.cs`), shrinking it **2,822 → 1,409** (below the 2,000 ceiling) and narrowing the class declaration to `: IEventDataverseService, IFieldMappingDataverseService`. Waiver removed from `GodClassGuardTests`. Verified: BFF 10,402 tests pass, ArchTests 38/38. Also surfaced **DEF-2** (WebApi field-mapping throws via the unimplemented `GetEntitySetNameAsync` stub — split-brain trap #2 is a *throwing* stub, not a duplicate) → routed in `projects/code-quality-and-assurance-r3/notes/defer-issues.md`.

### Added (2026-08-15 — God-class ratchet documentation · code-quality-and-assurance-r3 followups)

- **Added — [`.claude/patterns/testing/god-class-ratchet.md`](patterns/testing/god-class-ratchet.md)** + root `CLAUDE.md` §17 pointer + patterns INDEX entries. Documents the `GodClassGuardTests` server file-size gate (**no new `src/server/**/*.cs` > 2,000 lines; 14 existing large files frozen at LOC +100 grace**) so an editor/agent knows BEFORE growing a large file. On failure: decompose (preferred) or re-baseline the file's waiver with a PR reason — never silence. Redesigned the guard from an arbitrary single ceiling (4,950→2,700, which left actively-edited files ~24 lines of headroom) to a per-file freeze + grace.

### Added (2026-08-14 — worktree-net10-migrate skill · dotnet-10-upgrade-r1 cutover)

- **Added — [`.claude/skills/worktree-net10-migrate/SKILL.md`](skills/worktree-net10-migrate/SKILL.md)** + exemplar [`scripts/Update-WorktreeToNet10.ps1`](../scripts/Update-WorktreeToNet10.ps1) — one-command, **non-destructive** migrator to bring any worktree onto the net10 baseline (SDK check → dirty-tree guard → `git merge origin/master` → **net8-clobber guard** [every `src/server` csproj must be `net10.0`] → build; interprets NETSDK1045 + Graph 6.5/Kiota 2.0 errors). Registered in `.claude/skills/INDEX.md`. Addresses the live IDE-clobber failure mode (open VS/Rider autosaving stale net8 csproj over a merge → 503 on deploy).

### Changed (2026-08-14 — .NET 10 doc sweep · dotnet-10-upgrade-r1 cutover)

- **Changed — root [`CLAUDE.md`](../CLAUDE.md) §1** + `.claude/patterns/dataverse/web-api-client.md` + `src/server/api/Sprk.Bff.Api/CLAUDE.md` + README + 11 architecture/guide/procedure docs — swept the **normative build-target references from ".NET 8" → ".NET 10"** (and 2 stale "Graph SDK v5" → "v6") now that the backend is retargeted and **`origin/master` runs net10** (BFF + shared libs `net10.0`, `global.json` 10.0.100, dev App Service `DOTNETCORE|10.0`, Functions `dotnet-isolated 10.0`). Left intentionally unchanged: ADR history, `.claude/archive/`, other projects' `projects/*/CLAUDE.md` context files, and behavioral notes accurate from net8+ (`BackgroundServiceExceptionBehavior.StopHost`, `TimeProvider` ".NET 8+", the DATAVERSE-AUTH historical troubleshooting narrative). 19 files / 29 lines. Cutover driver: `dotnet-10-upgrade-r1` (merged to master `d71bd3547`).

### Changed (2026-08-06 — ADR-028 Amendment A3 · spaarke-SPA-external-access-platform-r2 task 010)

- **Changed — [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](adr/ADR-028-spaarke-auth-architecture.md)** — applied **Amendment A3** (resolution path B, root CLAUDE.md §6.5; driver `spaarke-SPA-external-access-platform-r2`). **Generalizes A2's collaboration-host product line into a module-host SPA platform** serving all non-core (SPA) users, and **ratifies the shipped principal-agnostic endpoint pattern as canonical** (teams-app-r1 FR-22: `CallerPrincipalResolver` + `ExternalCollaboration` dual-scheme; 9761 tests pass, CIAM byte-for-byte preserved). Adds **MUST** rules (dual-plane external-app model canonical; authorize via `ICallerPrincipalResolver` + `AuthPolicies.ExternalCollaboration` dual-scheme with plane-agnostic handlers; plane selected only from validated `iss`/`tid` via `DeterminePlane`; new plane/module plugs in via one `ICallerPrincipalStrategy` + one `DeterminePlane` branch; Tier-1 module entitlement ⟂ Tier-2 record scope, both server-enforced) and **MUST NOT** rules (no second maintained workforce entry point; no OBO on either plane — broker-only/app-only preserved; no inferring Tier-1 entitlement from auth/plane/Tier-2; no routing the platform through Xrm-bound `@spaarke/auth`). **All A1+A2 invariants preserved + unweakened; internal Xrm surfaces UNAFFECTED; E-3 direct-Office boundary unchanged.** Applied **concise-only** (no `docs/adr/` full copy exists — mirrors A1/A2). A2 left intact. Task POML: `projects/spaarke-SPA-external-access-platform-r2/tasks/010-adr-028-amendment-a3.poml`.

### Changed (2026-08-05 — Compose render-on-save save-path amendment · spaarkeai-compose-r6, task 001)

- **Changed — [`.claude/adr/ADR-049-compose-shadow-document.md`](adr/ADR-049-compose-shadow-document.md)** — added an **R6 Path-B Amendment** (per CLAUDE.md §6.5) codifying **render-on-save** for the **save path only**: save re-derives a fresh `.docx` from a canonical document model into a new immutable SPE version, **superseding I-4** (untouched-subtree byte-identity) **and the line-40 MUST NOT** ("re-derive the `.docx` from the editor model on save"). Codifies the four spec points — (1) no surgical anchoring on the save path (retires the `ComposeBaselineParaIdStamper` count-gate, the 422 root); (2) version history = the fidelity safety net; (3) representative-corpus round-trip = a CI release gate; (4) `ComposeShadowPatchEngine` retained ONLY for a transitional clean-apply path. **Scope guard**: I-7 (no write-path text-search) satisfied trivially by rendering; the **R4.5 read/reference invariants F-1…F-5 remain in force** (save-path only supersession); no auth/security ADR touched; no unrelated section altered. **Path-B obligation**: MUST merge with or before the dependent R6 Phase-1 code (`spaarkeai-compose-r6` tasks 010/011/012). Authored main-session per §3 write boundary. Source: `projects/spaarkeai-compose-r6/spec.md` ADR-Tensions; summary at `projects/spaarkeai-compose-r6/notes/adr049-amendment-summary.md`.

### Changed (2026-08-03 — ADR-028 Amendment A2 · teams-app-r1 task 002)

- **Changed — [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](adr/ADR-028-spaarke-auth-architecture.md)** — applied **Amendment A2** (resolution path B, root CLAUDE.md §6.5; driver `teams-app-r1`). **Generalizes the A1 exemption** from "external SPA" to "the collaboration hosts (external SPA + Teams tab)" over **one shared standalone-MSAL module with a pluggable authority** (CIAM for the SPA, workforce-multitenant Teams SSO/NAA for the Teams tab). Adds workforce-plane **MUST** rules (workforce Entra multitenant auth; shared pluggable-authority module; broker-only/no-OBO carried into the workforce plane; resolve caller to a `systemuser` **or** `contact` principal + accessible-record-set enforcement) and **MUST NOT** rules (no CIAM-in-Teams; no routing collaboration hosts through Xrm-bound `@spaarke/auth`). **All A1 invariants preserved + unweakened; internal Xrm surfaces (`@spaarke/auth`, PCFs, Code Pages) UNAFFECTED.** Cross-references ADR-034 contact-anchored entry (Path C, additive). Applied **concise-only** (the `docs/adr/` full copy does not exist — mirrors how A1 was applied; a new full ADR was declined as scope creep). Draft: [`projects/teams-app-r1/adr-028-amendment-draft.md`](../projects/teams-app-r1/adr-028-amendment-draft.md).

### Added (2026-08-01 — Canonical modal system · spaarke-modal-system, P0 docs closeout)

- **Added — [`.claude/adr/ADR-050-canonical-modal-shell.md`](adr/ADR-050-canonical-modal-shell.md)** — concise ADR codifying the one-canonical-`SprkModal`-shell + thin-presets decision: MUST compose `ModalWindowControls`/`RecordNavigationModalShell` + keep the Fluent `Dialog` envelope (transform-robust portal) + realize `--sprk-ui-scale` via a scaled Fluent theme (NOT CSS `zoom`) + semantic tokens only (**strengthens ADR-021** — bans `'1px'`/inline color in modal components); MUST NOT hand-roll `position:fixed` overlays or per-surface bespoke chrome. Preserves the Choice Dialog pattern via `ChoiceModal`. Registered in [`.claude/adr/INDEX.md`](adr/INDEX.md) (ADR-050 = next-free; ADR-049 was highest).
- **Added — [`.claude/patterns/ui/modal-shell.md`](patterns/ui/modal-shell.md)** — 25-line component-layer pointer (When / Read These Files / Constraints / Key Rules) → `SprkModal` + presets + `docs/standards/MODAL-DESIGN-SYSTEM.md`. Registered in [`.claude/patterns/ui/INDEX.md`](patterns/ui/INDEX.md).
- **Changed — [`.claude/patterns/ui/record-modal-selection.md`](patterns/ui/record-modal-selection.md)** — added a component-layer cross-link: the decision layer now points at `modal-shell.md` / `SprkModal` for HOW to build the chosen family (its decision content is unchanged).
- **Root `CLAUDE.md` §17** — added a **Modal design system (component layer)** pointer row → `docs/standards/MODAL-DESIGN-SYSTEM.md` (+ ADR-050 + the pattern pointer), sibling to the existing Modal decision-criteria row.
- Non-`.claude/` companions (same project): `docs/standards/MODAL-DESIGN-SYSTEM.md` authored (task 010) and cross-linked back from `MODAL-DECISION-CRITERIA.md`. All `.claude/` writes made main-session per §3 write boundary. spaarke-modal-system tasks 010–013; functional P0 (001–009) shipped the shell + 6 presets in `@spaarke/ui-components` (86 tests).

### Changed (2026-07-28 — Compose read/reference fidelity documented · spaarkeai-compose-fidelity-r4.5)

- **Added — `docs/architecture/COMPOSE-READ-REFERENCE-FIDELITY.md`** — narrative read/reference architecture doc (one reader · text exactness · deterministic numbering engine · `paraId→legal-number` reference layer + `CitationResolver` · honest page/line) with BFF surface, code inventory, and extension recipes. The narrative home the ADR-049 companion only gestures at. Registered in root CLAUDE.md §17 and cross-linked from ADR-049.
- **Root CLAUDE.md §17** — added a **Compose** pointer row (write/save = R4; read/reference = R4.5) → the new arch doc + [`.claude/adr/ADR-049-compose-shadow-document.md`](adr/ADR-049-compose-shadow-document.md). Compose was absent from the §17 pointer table despite five project generations.
- **ADR-049 (Compose Shadow Document)** — added an **R4.5 Read/Reference Fidelity companion** section documenting invariants **F-1…F-5** (one reader / text exactness / deterministic numbering / stable `paraId→legal-number` reference + `CitationResolver` / honest page-line). ADR body + [`.claude/adr/INDEX.md`](adr/INDEX.md) 049 row updated. **No write-side rule changed** — R4.5 built on R4's projection machinery; the two-author split stands. Doc-drift review of R4.5 also confirmed the `mammoth` references in `docs/` (CHAT-ATTACHMENT-POLICY, RECORD-HEADER-PCF, bundle-size assessment) are accurate — they concern SprkChat/attachments where `mammoth` correctly remains; only the *Compose* usage was removed. Authored main-session per §3 write boundary. Merged to master 2026-07-28.
- **Known pre-existing drift (NOT fixed here)**: `docs/adr/INDEX.md` (the *full* ADR index) remains stale (missing ADR-039/040–044/046/047/049) — same item the 2026-07-25 entry flagged; a separate backfill, not R4.5's scope.

### Changed (2026-07-25 — ADR-039 Output Determinism Modes amendment · ai-advanced-capabilities-nda-r1 task 001)

- **Amended — ADR-039 Grounded Execution & Closed Catalogs** (concise [`.claude/adr/ADR-039-grounded-execution-closed-catalogs.md`](adr/ADR-039-grounded-execution-closed-catalogs.md) + full [`docs/adr/ADR-039-grounded-execution-closed-catalogs.md`](../docs/adr/ADR-039-grounded-execution-closed-catalogs.md); `.claude/adr/INDEX.md` 039 row updated). **CLAUDE.md §6.5 Path B.** Adds **Output Determinism Modes** refining grounded-execution invariant (a): a cataloged capability declares `output_determinism` as catalog **data** — `fact` (default, deterministic — extractive/verbatim-cited, prior behavior unchanged) vs `advisory` (probabilistic — permits reasoning/synthesis depth + a Reasoning-tier deployment per ADR-016 while STILL prompt-controlled + schema-validated + source-cited for every factual claim, carrying a not-authoritative disclaimer + human-review surfacing). Advisory is a mode *of* invariant (a), **not** an escape from it — no new entry path, no fourth output category, no new mechanism. The amendment ADDS obligations (cite facts, decline-if-unverifiable, disclaimer, all other ADR-039 invariants hold) and weakens NO prior MUST/MUST NOT. Demand-pull: the first analysis/advisory vertical (NDA review) needs Claude/ChatGPT-level advisory output; the naive "extractive-only" reading of (a) was stricter than the invariant requires (ADR-039's liability posture is about *ungrounded* output, not *reasoned* output over grounded facts). Merge gate (FR-00) for the project's advisory-tier tasks. Authored main-session per §3 write boundary. NOTE: `docs/adr/INDEX.md` is stale (missing ADR-039/040–044/046/047/049) — not amended here to avoid partial-fix inconsistency; flagged for doc-drift-audit.

### Added (2026-07-23 — Assistant UI element criteria pointer · spaarkeai-assistant-enhancements-r1 task 051/090 doc-review)

- **Added — root `CLAUDE.md` §17 pointer** to [`docs/standards/ASSISTANT-UI-ELEMENT-CRITERIA.md`](../docs/standards/ASSISTANT-UI-ELEMENT-CRITERIA.md) (the bubble/chip/card/tab four-question decision + do/don't rules), placed beside its siblings `ASSISTANT-SURFACE-LAUNCH-MECHANISM.md` and `MODAL-DECISION-CRITERIA.md`. Closes the pointer gap found in the R1 pre-090 documentation review (the standards doc shipped 2026-07-22 without a root pointer). No behavioral rule change — a discoverability pointer only. Authored main-session per §3 write boundary.

### Added (2026-07-22 — Assistant surface-launch mechanism doc · spaarkeai-assistant-enhancements-r1)

- **Added — root `CLAUDE.md` §17 pointer** to the new [`docs/architecture/ASSISTANT-SURFACE-LAUNCH-MECHANISM.md`](../docs/architecture/ASSISTANT-SURFACE-LAUNCH-MECHANISM.md). Documents how the Assistant **deterministically** opens follow-on surfaces: `consumerType` (the Binding's routing decision) → `surfaceLaunchRegistry` static lookup → `handleSurfaceLaunch` branches on `kind` (wizard/oob-form via sessionStorage hand-off; workspace-tab/layout via PaneEventBus `widget_load`). Covers the two entry paths (SSE text-path + click/chip), the hand-off envelope, the intentionally-thin BFF (no surface identity server-side — surface identity stays in CODE per ADR-039 / BFF §10), 7 invariants, and the **extension recipe** (new surface = Action+Binding data + ONE registry entry in code). REQUIRED reading before adding any surface-opening capability. Grounds the registry-robustness change (retired the hardcoded `list-tasks` branch; activated the `workspace-tab` kind). Authored main-session per §3 write boundary.

### Added (2026-07-21 — use-case-to-design skill · ai-advanced-capabilities program)

- **Added — `use-case-to-design` skill** ([`.claude/skills/use-case-to-design/SKILL.md`](skills/use-case-to-design/SKILL.md) + `references/design-template.md` + `references/capability-lenses.md`). Codifies a repeatable **6-lens method** (use case → surface/UX → required AI capabilities → have-vs-gap → configuration → acceptance) that emits a complete `design.md` for the use-case-vertical projects under the `ai-advanced-capabilities-*` program. Upstream feeder to `design-to-spec` (writes design.md only; no spec/plan/tasks). Encodes the **REUSE > ACTIVATE > COMPLETE > BUILD** precedence and the demand-pull discipline that avoids the "dark-capability trap" (built-but-unwired horizontal capability, e.g. `MemoryCompositionService`). `capability-lenses.md` carries a 2026-07-21 have-vs-gap snapshot of the current AI stack with file evidence (verify-against-code required). Registered in `skills/INDEX.md` (Project Lifecycle, Tier 0). Context: `projects/ai-advanced-capabilities-development/PROGRAM-ROADMAP.md`. Authored main-session per §3 write boundary.

### Added (2026-07-21 — spaarke-notification-spine-r1 · ADR-047 Notification & Action Spine)

- **Added — `ADR-047` Notification & Action Spine** (concise [`.claude/adr/ADR-047-notification-action-spine.md`](adr/ADR-047-notification-action-spine.md) + full [`docs/adr/ADR-047-notification-action-spine.md`](../docs/adr/ADR-047-notification-action-spine.md); both ADR INDEXes updated). Claims the number reserved by ADR-046/ADR-048. Locks the ONE server-initiated **typed-signal → grounded-action → delivery** spine (Layers A–D) that collapses the `email-communication-solution-r4` / `messaging-communication-app-r3` / `spaarkeai-assistant-enhancements-r1` push forks into one. Six MUST/MUST-NOT commitments (typed signals · shared domain actions · per-source policy · SSE-as-presentation · outbox-before-ping · dumb-transport). Delivery-mode section cites the **task-001 FR-01 spike** decision (GO / Azure SignalR **Serverless** `Microsoft.Azure.SignalR.Management`, in-BFF; +0.30 MB compressed, 0 new HIGH CVE). Records the resolved ADR-043 tension (Notification `Routable=false` flip = Path C, comply-sequenced) + the FR-19 R3 contract-lock. Also updated the ADR-046/ADR-048 rows' "ADR-047 reserved" language to "authored (Proposed)". Authored main-session per §3 write boundary (spaarke-notification-spine-r1 task 010). Status **Proposed** → Accepted at the project gate.

- **Added — `ADR-048` Communication Participant Index** (concise `.claude/adr/ADR-048-communication-participant-index.md` + full `docs/adr/ADR-048-communication-participant-index.md`; both ADR INDEXes → Accepted). Codifies the message-grain `sprk_communicationparticipant` junction (task 003 schema) + the **ADR-034 path-C comply-with-intent** resolution (two typed lookups `sprk_systemuser` XOR `sprk_contact` for a 2-target identity, vs ADR-034's 6-target Guid+type tuple — not an amendment). Authored main-session per §3 write boundary (messaging-communication-app-r2 task 004). Sibling ADR-047 remains reserved for `spaarke-notification-spine-r1` (not claimed). Note: `docs/adr/INDEX.md` was also missing the ADR-046 row (R1 omission) — not back-filled here.

### Fixed + Changed + Added (2026-07-16 — project-setup pipeline modernization; POML template drift)
Trigger: `spaarkeai-compose-r3` project-pipeline run hit a stale POML template. Full audit + recommendations in [`AUDIT-FINDINGS-PIPELINE-MODERNIZATION-2026-07-16.md`](AUDIT-FINDINGS-PIPELINE-MODERNIZATION-2026-07-16.md); source finding in `projects/spaarkeai-compose-r3/notes/FINDING-poml-template-drift.md`.
- **Fixed — `templates/task-execution.template.md` demoted to a lean pointer (v3.0)**. The v2.0/Dec-2025 fossil was missing every modern task-metadata field (`model-tier`, `effort`, `rigor`, `gate`, `parallel-*`, `deps`, `justification`, `steps mode`, `escalation`, `ui-tests`) and carried dead paths (`docs/projects/`, `Spe.Bff.Api`, `docs/reference/adr/`, `docs/ai-knowledge/`). Now a current copy-paste skeleton + field-semantics table pointing at `task-create` as the single source of truth. Kills the drift class (finding rec B).
- **Changed — canonical POML field set reconciled** in `task-create` (Step 4, Step 3.5.5, POML Tag Requirements): `<rigor>` (was `<rigor-hint>`), `<deps>` (was metadata `<dependencies>`), `<gate>` added, `<blocks>` dropped. Deprecated aliases accepted for back-compat.
- **Added — Completeness lint** (finding rec C): `task-create` Validation Checklist step + `code-review` **Step 6.7** (POML completeness) + [`scripts/Validate-TaskPoml.ps1`](../scripts/Validate-TaskPoml.ps1) (regex-based, tolerant of imperfect XML in POML prose; validated clean on the compose-r3 27-POML set + exemplar 050).
- **Fixed — `project-pipeline` producer/consumer gap**: Step 3's POML-generation field list now emits the full canonical set that Step 5 dispatch + `/goal` consume; added §10 Placement-Justification + §11 component-justification prompts; removed the `MAX_THINKING_TOKENS` self-contradiction; `npm run build`→`build:prod` for PCF; planning tier → Opus 4.8 / Fable 5.
- **Added — structured-output schemas** (Agent SDK best practice) at `project-pipeline` Step 2 (discovery) + Step 5 (task outcome) for machine-readable subagent returns.
- **Fixed — `design-to-spec`**: broken §13→§15 root-CLAUDE cross-ref; mojibake artifact; added §10 `<hot-path-declaration>` + §11 component-justification seeding to the spec template.
- **Fixed — `task-execute`**: dead `src/server/api/CLAUDE.md` pointers → `…/Sprk.Bff.Api/CLAUDE.md`; `npm run build`→`build:prod` for PCF (AP-1); retired `Task`/`TaskOutput` tool names → `Agent`; rigor tree now reads the authored `<rigor>` hint; BFF checklist gained §10 publish-size (≤60 MB) + CVE + Placement-Justification; `projects/INDEX.md` maintenance noted at Step 0.5.
- **Changed — `CROSS-REFERENCE-MAP.md`** POML-format rows now name `task-create` as authoritative and the template as a synced pointer.
- Not changed (verified current): `/goal` wave-loop, §6.5 ADR-Tensions enforcement, `project-setup`.

### Changed (2026-07-12 — ADR-012 amendment: `@spaarke/visuals` sibling package, `visual-host-version-update`)
- **Amended ADR-012 (both forms)** — concise [`adr/ADR-012-shared-components.md`](adr/ADR-012-shared-components.md) + full [`docs/adr/ADR-012-shared-component-library.md`](../docs/adr/ADR-012-shared-component-library.md). **Path B amendment** per [CLAUDE.md §6.5](../CLAUDE.md): sanctions `@spaarke/visuals` (`src/client/shared/Spaarke.Visuals/`) as a **governed presentational sibling** to `@spaarke/ui-components` — the canonical home for data-viz primitives (metric cards, charts, gauges, distribution bars, calendar, due-date cards, mini-table). Records the 3-reason justification for a separate package (heavyweight `@fluentui/react-charting` quarantine; strict presentational purity — host binds data, no `Xrm`/`WebAPI`/FetchXML; `@types/react@18` pin for cross-surface JSX safety, subset-safe for both R16 PCF + R19 Code Pages). Restates the **anti-fragmentation boundary** (no ad-hoc per-project viz libs; data binding + card chrome + drill-through stay host-side) and defines a **3-test bar** (distinct contract + quarantine-worthy dep + cross-surface reuse) gating any future governed sibling package. Defers to root [CLAUDE.md §11](../CLAUDE.md) reuse rule. Introduced by `visual-host-version-update` (VHVU-070); merges alongside the dependent extraction code (VHVU-040–060).

### Added (2026-07-10 — ADR-044 Dataverse GUID Canonicalization + pattern)
- **New concise ADR [`ADR-044-dataverse-guid-canonicalization.md`](adr/ADR-044-dataverse-guid-canonicalization.md)** + full [`docs/adr/ADR-044-*.md`](../docs/adr/ADR-044-dataverse-guid-canonicalization.md) + [`adr/INDEX.md`](adr/INDEX.md) row. Codifies (as a binding MUST) the prevention that FAILURE-MODES **AP-3** (GUID case → AI Search `eq` misses) and **AP-6** (GUID braces → `@odata.bind` 400) both point to: canonicalize every Dataverse GUID to bare-lowercase at every boundary via the shared **`cleanGuid`** (client) / single-convergence-point normalize (BFF). Two prod failures, one root cause — elevated to ADR so it's `adr-check`/review-enforceable instead of tribal knowledge. **Status: Accepted (2026-07-10)** — codifies already-shipped, owner-approved behavior (PR #603/#609).
- **Extended pattern [`.claude/patterns/dataverse/relationship-navigation.md`](patterns/dataverse/relationship-navigation.md)** (the pattern that already owns `@odata.bind`/lookups) with a MANDATORY client GUID-normalization rule + the client `cleanGuid` file reference + ADR-044 constraint. This is the per-task-loaded surface, so the rule surfaces at the moment bind code is written.

### Added (2026-07-10 — ADR-042 Memory Architecture & Governance, `spaarke-ai-architecture-redesign-r2` task 065)
- **New concise ADR [`ADR-042-memory-architecture-governance.md`](adr/ADR-042-memory-architecture-governance.md)** + full [`docs/adr/ADR-042-*.md`](../docs/adr/ADR-042-memory-architecture-governance.md) + [`adr/INDEX.md`](adr/INDEX.md) row. Codifies the memory wave (tasks 050/051/052/057, shipped PRs #620/#622): TWO active scopes (Record `(entityType,entityId)` + User `systemuserid`; Conversation stays the ADR-040 ledger), subject-partitioned `memory-items` container (**never `/tenantId`**), per-fact docs with upsert-by-key supersession, the governance envelope (retentionClass→per-item TTL; sensitivity/deletionPolicy/trustLevel inert), the **AI-initiated + silent + provenance-tagged write posture — NO confirmation floor** (operator removed it 2026-07-08; review/delete + provenance + scope isolation are the controls), ADR-015 Tier-3 erasure + ids-only Tier-2 audit, and the explicitly-DEFERRED hard-governance boundary (→ security project #616). **Status: Proposed** — Accepted at the G-R2-B gate.

### Added (2026-07-09 — braced-GUID `@odata.bind` directive, PR #603 / #609)
- **[FAILURE-MODES.md](FAILURE-MODES.md) AP-6** — new anti-pattern entry: interpolating a raw (brace-wrapped, Xrm-sourced) GUID into an `@odata.bind` key predicate causes Dataverse HTTP 400 `Error in query syntax`. Carries the binding **directive on when + how to use the canonical `cleanGuid()`** helper (`import { cleanGuid } from '@spaarke/ui-components'`; wrap every GUID that enters an OData bind/URL; no-op on bare ids; don't hand-roll local `.replace(/[{}]/g,'')`). Sibling of AP-3 (same root cause — Xrm registry-format GUIDs — different symptom). Code: PR #603 (fix across all shared-lib wizard bind sites + Xrm adapter boundaries, merged `d2696b616`), PR #609 (`cleanGuid` barrel export).

### Changed (2026-07-09 — set-regarding-and-field-mapping-resolver-r2 doc consolidation)
- **Root [CLAUDE.md](../CLAUDE.md) §17 Field Mapping row** expanded to advertise the doc's new **code + PCF component inventory**, the **set-regarding / RegardingResolver** relationship (and that **AssociationResolver is retired**), the config enum reference, the Web-API seeding recipe, and the deprecated-guide stub. Doc-side changes (not procedure-surface): expanded [`docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md`](../docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md) with a full code+PCF inventory, resolver section, enum reference and UAT-hardening notes; added a table-nav path + option-set integers + Web-API seeding recipe + resolver note to [`docs/guides/FIELD-MAPPING-ADMIN-GUIDE.md`](../docs/guides/FIELD-MAPPING-ADMIN-GUIDE.md); **deprecated** the contradicting Feb-2026 `docs/product-documentation/field-mapping-admin-guide.md` (sync-modes / Refresh-from-Parent model) to a redirect stub; disambiguated `docs/data-model/field-mapping-reference.md`; fixed stale RegardingResolver/AssociationResolver rows in `src/client/pcf/CLAUDE.md`.

### Added (2026-07-09 — ADR-043 AI Capability Execution Spine, `spaarke-ai-architecture-redesign-r2` Phase E / task E-00)
- **New concise ADR [`ADR-043-ai-capability-execution-spine.md`](adr/ADR-043-ai-capability-execution-spine.md)** + full [`docs/adr/ADR-043-*.md`](../docs/adr/ADR-043-ai-capability-execution-spine.md) + [`adr/INDEX.md`](adr/INDEX.md) row. Codifies the AI execution engine that realizes the ADR-039 catalog contract, closing a verified gap (canonical engine implemented only a narrow slice: input=files-only, disposition=2-of-6, kind=Prompted-only; TWO redundant completion engines, canonical the weaker; disposition triplicated + drift-prone → the compose-r2 routing-promotion 422). Decision: three execution surfaces converging at one disposition→ledger→OutcomeCard layer; **converge the two completion engines onto one ContextBinder/ContextEnvelope input model** (no runtimeInput-straddle); **single-source disposition** (DispositionRoutability — admit follows "router can route it"); deterministic/interactive capabilities via a **deterministic ActionKind + supersession-write** (not a third spine); keep the agent-loop tool spine separate (unify = R8+). **Governance (adopted platform-wide): a `tests/integration/seam/**` vertical-slice KEEP test is the definition-of-done** for execution/dispatch changes (a green contract-shape test is not sufficient — the exact gap that let 016/042 ship "done" while 422-broken) + a named engine owner + a deferral re-parenting rule. Reserves (does not build) the future multi-step "Action Engine" seam per operator-confirmed inputs (hybrid authorization via the ADR-041 gate, closed-catalog-bound steps, ledger-resident plan, framework-agnostic). **Status: Proposed** — Accepted at the Phase-E gate. NOTE: this ADR also adds a KEEP-path category to ADR-038's scope (`tests/integration/seam/**`).

### Added (2026-07-09 — set-regarding-and-field-mapping-resolver-r2)
- **Root [CLAUDE.md](../CLAUDE.md) §17** — new pointer row for the **Field Mapping Framework** architecture doc ([`docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md`](../docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md)) + maker guide ([`docs/guides/FIELD-MAPPING-ADMIN-GUIDE.md`](../docs/guides/FIELD-MAPPING-ADMIN-GUIDE.md)). The framework restores creation-time assigned-resource inheritance for wizard-created children: two Dataverse tables, additive BFF DTO (no new endpoint/service), context-agnostic client engine (`FieldMappingService.ts`) with four mapping types (Copy incl. lookup `@odata.bind` / Default / Concat / Template), new `sprk_expression` Memo column as the Concat/Template extensibility seam, wired into all 7 `Create*Wizard` services. No Dataverse plugin, no form script, no new PCF (client-only).

### Added (2026-07-09 — ADR-041 concise mirror, `spaarke-ai-architecture-redesign-r2` task 043)
- **New concise ADR [`ADR-041-judgment-confirmation-completion-policy.md`](adr/ADR-041-judgment-confirmation-completion-policy.md)** + full [`docs/adr/ADR-041-*.md`](../docs/adr/ADR-041-judgment-confirmation-completion-policy.md) + [`adr/INDEX.md`](adr/INDEX.md) row. Principle-level judgment doctrine above ADR-039 dispatch: **D-F0** resourcefulness (reads free / writes gated; degradation ladder stays below the side-effect line, never weakens a gate), **D-F1** deterministic confirmation (risk-tier × origin × completeness; overlay precedence; E-1..E-6 ruled rows; risk = catalog DATA not runtime LLM judgment per ADR-039; confirmation state = gate-ledger property per ADR-040), **D-F2** truthful completion (OutcomeCard after ledger write; job-aware status; UI-action ack). **Status: Proposed** — Accepted flip gated on G-R2-A (task 049), mirroring the ADR-039/040 promotion-gate convention. Codifies what tasks 030/032/033/034/035/036/037/038 implement so they cite an authority rather than carrying behavior in directives. Records the open item that the Policy v2 engine (032) currently has 0 core call-sites.

### Removed (2026-07-08 — retired the vestigial AIP protocol layer)
- **Deleted `.claude/protocols/` entirely** (`AIP-001-task-execution.md`, `AIP-002-poml-format.md`, `AIP-003-human-escalation.md`, `INDEX.md`). Rationale: the AIP layer was dropped from root CLAUDE.md in the 2026-05-17 rewrite, frozen as "stable — do not modify" by `ai-procedure-refactoring-r1`, and had drifted (AIP-001 still carried stale 50/70/85% context thresholds vs CLAUDE.md's 60/70/85%). It duplicated the executable skills with only footer/bibliography inbound refs — nothing in any execution path. No unique content: POML format is canonical in [`task-create`](skills/task-create/SKILL.md) + [`task-execution.template.md`](templates/task-execution.template.md); execution/handoff in [`task-execute`](skills/task-execute/SKILL.md) + CLAUDE.md §5; escalation in CLAUDE.md §6 + §6.5.
- **Fixed inbound refs**: [`task-execute`](skills/task-execute/SKILL.md) footer, [`ai-procedure-maintenance`](skills/ai-procedure-maintenance/SKILL.md) (Checklist D + path-consistency checks + file-locations table now point to CLAUDE.md + skills; no third "protocol" home), [`CROSS-REFERENCE-MAP.md`](../CROSS-REFERENCE-MAP.md), [`docs/procedures/context-recovery.md`](../docs/procedures/context-recovery.md). Historical `projects/*` + `.claude/archive/` refs left as-is (archival).

### Added / Changed (2026-07-08 — Sonnet-5 optimization tranche 1 + `/goal` wave loop)
- **Per-task `<effort>` + effort rubric** (refines the earlier blanket-`xhigh` default, which the Sonnet-5 guide warns approaches Opus cost). [`task-create`](skills/task-create/SKILL.md) Step 3.5.5b: execution now defaults to **Sonnet 5 @ `high`**; `xhigh` reserved for brownfield/root-cause or complex-but-fully-specified work. `<effort>` added to POML metadata template + validation checklist. [`task-execute`](skills/task-execute/SKILL.md) Step 0.5 declaration adds `@ effort`; [`project-pipeline`](skills/project-pipeline/SKILL.md) Step 5 dispatches `effort = <effort>`.
- **Authoring for literal execution** ([`task-create`](skills/task-create/SKILL.md)): scoped constraints, closed-set acceptance criteria (incl. negative/auth cases), explicit "above and beyond", knowledge-curation/token-discipline note (~30% tokenizer inflation), concrete frontend visual direction (Step 3.65 — no "clean and modern").
- **Step modes + escalation element** (new Step 3.5.5c): `<steps mode="directional|prescriptive">` + optional `<escalation><trigger>`. [`task-execute`](skills/task-execute/SKILL.md) Step 0.5 honors both (`🧭 STEP MODE`), and prunes anti-laziness / forced-progress scaffolding while keeping artifact/gate-producing verification.
- **Coverage-first review** ([`code-review`](skills/code-review/SKILL.md) new Step 0 + [`adr-check`](skills/adr-check/SKILL.md) new Step 0): finding stage maximizes recall (report all + severity + confidence); orchestrator (task-execute Step 9.5) is the documented downstream filter. Removes the Sonnet-5 recall-drop from severity-filtering language.
- **`/goal` wave-completion loop** (optional, wave-level, transcript-only Haiku evaluator; NOT a per-task mechanism, NOT a quality gate). Eligibility rubric + compiled by-reference condition assigned by [`task-create`](skills/task-create/SKILL.md) new **Step 3.85** (machine-verifiable end-state, ≥3 well-specified low-ambiguity tasks, not security/deploy/irreversible). [`project-pipeline`](skills/project-pipeline/SKILL.md) Step 5 applies it to eligible waves; [`task-execute`](skills/task-execute/SKILL.md) new "`/goal` Wave-Completion Loop" section documents prerequisites + three exit states (condition met / BLOCKED.md / turn-cap) with Step 9.5 authority explicitly preserved.
- **Lessons loop** folded into the existing `notes/lessons-learned.md` convention (51+ projects) + `.claude/FAILURE-MODES.md` — no new central lessons store ([`task-create`](skills/task-create/SKILL.md) Step 3.7 + Step 3.4 reference).
- **Root [CLAUDE.md](../CLAUDE.md)** — new §8.5 "Execution Model, Effort & Wave Loops (Sonnet-5)" pointer + AIP-retirement note. **[`project-setup` claudemd-template](skills/project-setup/references/claudemd-template.md)** "Execution Model & Tiering" expanded (effort, step modes, coverage-first, `/goal`).
- **Deferred (separate evaluation, NOT implemented)**: settings-level Stop/PreToolUse hooks, `/loop`, `/batch`, scheduling, plan-mode mandates, and the proposal's calibration-pass (1.10). Per user direction 2026-07-08.

### Fixed (2026-07-08 — permission prompts)
- Added `"PowerShell(*)"` to user-level `~/.claude/settings.json` allow list (alongside the existing `"Bash(*)"`) to stop recurring PowerShell approval prompts across all worktrees. (User-scoped file; not in-repo.)

### Added (2026-07-08 visual-host-create-button-r1 — Sonnet-5 execution model tiering)
- **Model-tier strategy across the task pipeline.** Planning phases (design-to-spec, project-pipeline Steps 0–3) run on Opus 4.8 / Fable 5; task **execution defaults to Sonnet 5 @ effort `xhigh`**, with per-task escalation to Opus/Fable for the minority of high-power tasks. Mechanism is additive (absent tier ⇒ current behavior):
  - [`task-create` SKILL.md](skills/task-create/SKILL.md) — new **Step 3.5.5b** assigns a `<model-tier>` (`sonnet` default; `opus`/`fable` for high-blast-radius / architectural / ADR-migration / security tasks) + a Sonnet-5 "be-explicit" authoring note; `<model-tier>`/`<model-tier-reason>` added to the POML metadata template.
  - [`task-execute` SKILL.md](skills/task-execute/SKILL.md) — Step 0.5 declaration now includes `🔧 MODEL TIER`; serial task flagged above the session model triggers stop-and-escalate; FULL-rigor gates reaffirmed unconditional under Sonnet 5.
  - [`project-pipeline` SKILL.md](skills/project-pipeline/SKILL.md) — Step 5 dispatches each subagent with `model = <model-tier>`.
  - [`project-setup` claudemd-template](skills/project-setup/references/claudemd-template.md) — new "Execution Model & Tiering" section so every project CLAUDE.md carries the strategy.

### Changed (2026-07-08 spaarke-ai-architecture-redesign-r1 — tasks 050/055 close-out sync)
- Root [`CLAUDE.md`](../CLAUDE.md) §10 item 4 — publish-size baseline 45.65 MB (2026-05-26) → **49.63 MB incl. PDBs** (task 055 re-measurement 2026-07-08; PDB-convention reporting note added). Same update mirrored in [`.claude/adr/ADR-029`](adr/ADR-029-bff-publish-hygiene.md) (baseline + NFR-01 thresholds replace the stale 50 MB ceiling + nonexistent script hard-fail guard) and [`.claude/constraints/azure-deployment.md`](constraints/azure-deployment.md) (two baseline lines).
- [`.claude/adr/ADR-040`](adr/ADR-040-session-ledger.md) — inline payload cap MUST upgraded from "cap (pointer beyond cap)" to **enforce-at-cap 128 KB with deterministic truncation marker** (`SessionLedger.CapInlinePayload`, task 055; disposition legs fail loud on truncated payloads).
- [`.claude/constraints/bff-extensions.md`](constraints/bff-extensions.md) + `jps-validate` SKILL.md — three references to the deleted `GET /api/ai/playbook-builder/executor-config-schemas` endpoint re-pointed at `INodeExecutor.GetConfigSchema()` source implementations (`Services/Ai/Nodes/`); `jps-action-create` output-format "Add to Seed-JpsActions.ps1" step replaced with BA-editor/MCP row creation (task 050 builder-surface deletion).

### Changed (2026-07-07 spaarke-ai-architecture-redesign-r1 — task 051/052 procedure-surface sync)
- Root [`CLAUDE.md`](../CLAUDE.md) §17 — "Wiring a new consumer" row retitled to "Wiring a new capability (Action + Binding)" matching the task-052 rewrite of `ai-guide-consumer-wiring.md`.
- [`.claude/catalogs/scope-model-index.json`](catalogs/scope-model-index.json) — regenerated against spaarkedev1 by task 051 (60 Actions / 31 Skills / 31 Knowledge / 40 Tools; entries now carry deployed GUIDs + kind/tier/side-effect metadata).
- `jps-action-create` / `jps-validate` / `jps-playbook-design` SKILL.md — `Seed-JpsActions.ps1` pointers replaced with the MCP-create + `infra/dataverse/` mirror-first flow (script RETIRED by task 051); `sprk_externalid` → `sprk_knowledgecode` column drift fixed in jps-playbook-design.

### Changed (2026-07-07 spaarke-ai-architecture-redesign-r1 — G-P3 UAT hardening: input-schema authoring rules)
- [`.claude/skills/jps-action-create/SKILL.md`](skills/jps-action-create/SKILL.md) — Step 4 checklist gains a **binding `sprk_inputschema` block** for loop-projectable capabilities: OpenAI function-parameters subset required; property-level `"required": true|false` **BANNED** (object-level array only); `type:array` needs `items`; legacy `{"args":[...]}` format retired (rows normalized 2026-07-07); author-mirror-first in `infra/dataverse/inputschemas/` (CI-validated by `CatalogInputSchemaContractTests`; server twin `OpenAiFunctionSchemaValidator`). Root cause: G-P3 UAT 2026-07-07 — one invalid authored schema (task 042's create-task row) 400'd EVERY agent-loop turn platform-wide. `jps-validate` should adopt the same rules (follow-up).
- **Added** `.claude/skills/jps-action-create/examples/{create-task,draft-correspondence,refusal-handler}.json` (2026-07-06, main session) — JPS examples mirrored from ai-redesign-r1 tasks 041/042/033.

### Changed (2026-07-05 spaarke-ai-code-audit-r1 — greenfield convergence: 2 ADR amendments + 2 new ADRs)
- [`.claude/adr/ADR-037-multinode-output-composition.md`](adr/ADR-037-multinode-output-composition.md) (+ full version + INDEX row) — **Path B amendment (operator-approved, ADR review A-2)**: "DeliverComposite by default for future workspace playbooks" RESCINDED (engine frozen per OQ-2, canonical doc §4.2.1); ADR re-scoped to the section-name-keyed streaming + widget contract binding for ANY composite executor; 118R migration superseded; FieldDelta dual-render deletable at cutover (operator: customer continuity not a constraint).
- [`.claude/adr/ADR-013-ai-architecture.md`](adr/ADR-013-ai-architecture.md) (+ full version + INDEX row) — **Path B amendment (A-1)**: canonical invocation verb becomes capability invocation (`invoke(bindingId, args)`, Action+Binding model); `IInvokePlaybookAi` grandfathered as legacy shim (no new consumers); rotted architecture-map appendix replaced with canonical-doc pointer; ALL boundary rules unchanged.
- **Added** [`.claude/adr/ADR-039-grounded-execution-closed-catalogs.md`](adr/ADR-039-grounded-execution-closed-catalogs.md) (+ full version + INDEX row) — Proposed: one dispatch protocol (Event/Click/Text), two closed catalogs, grounded-output invariant, control-flow-is-code, "second intent-detection mechanism = violation". Encodes ratified D5/D6/D7 + OQ-1/OQ-2. Accepted at migration P1.
- **Added** [`.claude/adr/ADR-040-session-ledger.md`](adr/ADR-040-session-ledger.md) (+ full version + INDEX row) — Proposed: append-only addressable session ledger; storage-precedes-rendering; disposition as sole rendering contract; ADR-015 tier mapping. Encodes ratified D2/D8. Accepted at migration P0.
- Context for all four: canonical doc v0.4 (converged target) + `projects/spaarke-ai-code-audit-r1/{ADR-REVIEW-VS-GREENFIELD,OVERLAY-MATRIX,GREENFIELD-CONCEPTUAL-DESIGN}.md`.

### Changed (2026-07-01 spaarkeai-compose-r1 task 102 — ADR-013 Path B amendment)
- [`docs/adr/ADR-013-ai-architecture.md`](../docs/adr/ADR-013-ai-architecture.md) — new §"Amendment 2026-07-01 — Document-context invocation on `IInvokePlaybookAi` facade". Documents the widened facade contract (adds optional `userContext: string?` + `document: DocumentContext?` parameters, both defaulted, positioned after `cancellationToken` so existing 4-arg callers are unaffected). Motivating consumer: `spaarkeai-compose-r1` (Compose R1 drafting workspace). Boundary preserved — CRUD-side code STILL only injects `IInvokePlaybookAi` + `IConsumerRoutingService` (never AI-internal types). Reflection guard test updated with named allow-list for `Sprk.Bff.Api.Services.Ai.DocumentContext` (task 095). Signature change first shipped in tasks 095/096; SSE-mode consumer landed in task 097. Amendment filed via CLAUDE.md §6.5 Path B (amendment) — Path A (per-project exception) rejected because Compose is the first of many document-context consumers (Rewrite, Find Similar, Lookup References, downstream Matter/Communication/Insights consumers all inherit the widened facade cleanly).
- [`.claude/adr/ADR-013-ai-architecture.md`](adr/ADR-013-ai-architecture.md) — status updated to "Accepted (amended 2026-07-01)". Added two MUST rules: (1) use the new optional parameters for document-context invocation (no bypass to `IPlaybookOrchestrationService` allowed); (2) update the `PhaseAVerticalSliceTests.ADR013_InvokePlaybookAiFacade_DoesNotExposeAiInternalTypesInSurface` reflection guard's allow-list with a NAMED entry + citation when adding new types to the facade surface (silent bypass forbidden per CLAUDE.md §6.5).
- [`.claude/adr/INDEX.md`](adr/INDEX.md) — ADR-013 row updated: key constraint cites the amendment; status "Accepted (amended 2026-07-01)".
- **Enforcement of the CLAUDE.md §6.5 protocol in the field**: this is the first Path B amendment landed via the protocol added on 2026-06-29. Silent bypass was avoided; the reflection test's allow-list is a compile-time proof that the amendment was formally landed rather than tolerated.

### Changed (2026-07-01 — ai-spaarke-ai-workspace-UI-r2 Phase 3 doc sharpening, FR-15..FR-19, task 023)
- `docs/standards/MODAL-DECISION-CRITERIA.md` — Added **Two-Layout Standard** section at the top (Layout 1 canonical / Layout 2 justified exception / Layout 3 retired). Strengthened anti-pattern #4 with **verbatim MS Learn 2025-05-07 quote** ("Displaying a form within an IFrame embedded in another form is not supported") + 2026-02-10 CSP admin doc citation + 2026 CSP tightening context. Links to the R2 project's researcher evidence trail.
- `.claude/patterns/ui/record-modal-selection.md` — Rewritten around the two-layout framing. Now cites Layout 1 (85% × 85% via `Xrm.Navigation.navigateTo`) and Layout 2 (`RecordNavigationModalShell` for browse / content-shaped surfaces) as the ONLY two shapes. FR-20 binding (85% × 85% for every entity) called out inline.
- `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` — Added § 6.6 "Row-click behavior for entity-list widgets" citing Communications as the reference example. Documents the free Layout 1 inheritance via `DataGrid.defaultRecordOpen`; documents the rare-case escape hatch (custom `onRecordOpen`) with ADR-conflict-resolution obligations.
- `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md` — Added § 6.5 "Row-open contract" documenting `defaultRecordOpen` post-R2 (single Layout 1 code path, no dispatch on `rowOpen.type`), the new `configjson.rowOpen.formId` field (FR-01/FR-02), deprecation of `formDialogWidthPercent/HeightPercent` per FR-20, and the `onRecordOpen` host escape hatch (with audit note: no shipped consumer uses it).
- `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` — Added § 6.5 "Modal UX standard for record row-clicks" cross-referencing MODAL-DECISION-CRITERIA. Widget authors do NOT decide the modal per widget — the DataGrid framework enforces Layout 1 automatically; RecordNavigationModalShell is the only Layout 2 path.
- **Retired**: iframe-hosted OOB `main.aspx` (Layout 3 anti-pattern). `SmartTodoModal.tsx` was the last Spaarke consumer; deleted 2026-07-01 by R2 FR-14 (task 022). Migration path (`Xrm.Navigation.navigateTo` at Layout 1 via `openSprkTodoAsLayout1` module-scope helper) shipped by R2 FR-13 (task 021).

### Added (2026-07-01 — Modal Decision Criteria standard + pattern pointer)
- `docs/standards/MODAL-DECISION-CRITERIA.md` — NEW. Binding standard covering the three Spaarke modal families: OOB `Xrm.Navigation.navigateTo` (full main-form fidelity, no browse), proprietary Fluent v9 Dialog (single record / preview / picker), and proprietary + [`RecordNavigationModalShell`](../src/client/shared/Spaarke.UI.Components/src/components/RecordNavigationModalShell/README.md) (browse-in-context "1 of N" pattern). TL;DR decision tree + 5 dimensions + 3 worked examples + 6 anti-patterns + hybrid pattern (proprietary browse + OOB escalation). Modeled on [`docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`](../docs/standards/DATA-ACCESS-DECISION-CRITERIA.md) shape.
- `.claude/patterns/ui/record-modal-selection.md` — NEW 25-line pattern pointer per CLAUDE.md §14. Points agents to the standard, the shell README, `RichFilePreviewDialog` (canonical consumer), and `ChoiceDialog` (Family 2 canonical).
- `.claude/patterns/ui/INDEX.md` — added row for `record-modal-selection.md`.
- `CLAUDE.md` §17 — added pointer row for `MODAL-DECISION-CRITERIA.md` (parallel to `DATA-ACCESS-DECISION-CRITERIA.md`).
- **Driver**: chat-review session identified that `RecordNavigationModalShell` (production-ready smart-todo-r4 infrastructure) had no adoption guidance — developers were unaware the browse "1 of N" chrome existed as reusable infrastructure and were defaulting to close/reopen UX or considering per-surface rebuilds. This standard closes the documentation gap so the shell becomes the discoverable default for cross-record browsing.
- **Next step (out of scope for this changelog entry)**: surface inventory to identify which existing modals should migrate to Family 3 (browse-in-context) vs stay Family 1 (OOB `navigateTo`) vs adopt the hybrid pattern.

### Added (2026-06-29 spaarkeai-compose-r1 — ADR Conflict Resolution Protocol governance)
- `CLAUDE.md` — new §6.5 "ADR Conflict Resolution Protocol (BINDING)". Introduces the three resolution paths for ADR conflicts: (A) project-scoped exception with documented rationale, (B) ADR amendment when context has changed, (C) pivot to comply when an ADR-compliant alternative exists. Establishes "silent compliance with a sub-optimal ADR is itself a failure mode" as the principle. Binding for ≥6 months from 2026-06-29.
- `.claude/skills/adr-check/SKILL.md` — new Step 5.5 "Surface Challenge Paths" + updated Output Format Violations block to display the three resolution paths alongside each violation. Reviewer now chooses intentionally instead of defaulting to silent compliance.
- `.claude/skills/code-review/SKILL.md` — Step 6 ADR Compliance Check rewritten to accept reasoned exceptions documented in PR description / `spec.md` "ADR Tensions" section. Silent violations still Critical; documented Path A exceptions = Warning with reviewer judgment. Cross-links CLAUDE.md §6.5.
- `.claude/skills/task-execute/SKILL.md` — Step 9.5 quality gates updated: ADR violations no longer default to "STOP, must fix" silent-comply loop. Agent applies CLAUDE.md §6.5 protocol (path A/B/C choice with user escalation for A and B).
- `.claude/skills/design-to-spec/SKILL.md` — both spec.md templates (inline Step 4 + standalone bottom template) extended with mandatory "ADR Tensions" section (table format: ADR / rule / conflict / path / rationale). Default content if no tensions: explicit "No ADR tensions surfaced" statement.
- `.claude/skills/project-pipeline/SKILL.md` — Step 1 spec validation now requires "ADR Tensions" section; new Step 1.7 processes declared tensions before Step 2 resource discovery (validates rationale concreteness, flags Path B amendment prerequisite, summarizes path counts).
- **Driver**: design conversation during `spaarkeai-compose-r1` surfaced governance gap — agents and humans default to silent ADR compliance even when path A (exception) or path B (amendment) would produce a better technical outcome. User explicit ask: "if we have surfaced a legitimate exception or required modification to an ADR then we MUST surface this conflict and resolve it. We cannot have our ADRs drive us to sub-optimal solutions." This protocol formalizes the resolution.
- Reinforcement points: design-time (`design-to-spec` + `project-pipeline`), code-review-time (`code-review`), task-execute-time (`task-execute` Step 9.5), and ad-hoc (`adr-check`). Five enforcement layers ensure the principle isn't a doc-only addition.

### Added (2026-06-29 spaarke-ai-platform-unification-r7 Wave 6 task 068 — root CLAUDE.md §17 pointer to consumer-wiring guide; Wave 6 task 064 — bff-extensions §G rewrite; Wave 7 — jps-* skill rewrites)
- Root [`CLAUDE.md`](../CLAUDE.md) §17 Pointers — added row for [`docs/guides/ai-guide-consumer-wiring.md`](../docs/guides/ai-guide-consumer-wiring.md) (created Wave 6 task 067 per FR-31). §17 row "BFF additions governance" annotated with §G rewrite date.
- [`.claude/constraints/bff-extensions.md`](constraints/bff-extensions.md) §G "Action / Node / Playbook Config Boundary" — REWRITTEN for R7 single-hop dispatch (FR-29). New 4-Home table reflects dispatch on the NODE (Home C) + Action as reusable prompt template (Home A, dispatch removed) + decorative `sprk_analysisactiontype` lookup table (Home D). New "Binding MUST rules" + "Binding MUST NOT rules" sections explicitly enumerate dropped columns + structural-fallback delete + categorization-only stance. Hot-Path Declaration section RENUMBERED §G → §H to fix duplicate-§G ambiguity introduced by sibling project ci-cd-unit-test-remediation-r1 landing.
- [`.claude/skills/jps-action-create/SKILL.md`](skills/jps-action-create/SKILL.md) — Wave 7 task 070 (FR-32). Step 1.5 Config-Home Guard table updated; Step 5.5 MCP verify drops `_sprk_actiontypeid_value`; new "R7 dispatch model" section with §3.1 WHY citation.
- [`.claude/skills/jps-playbook-design/SKILL.md`](skills/jps-playbook-design/SKILL.md) — Wave 7 task 071. Step 1.5 item 3 replaces 3-tier lookup ladder with single-hop. Step 10 verify-deploy query uses `sprk_executortype`. New 33-executor catalog by tier + Executor-Type-FIRST workflow.
- [`.claude/skills/jps-playbook-audit/SKILL.md`](skills/jps-playbook-audit/SKILL.md) — Wave 7 task 072. Step 2 query updated; Check 3.5 citation corrected; new Check 3.6 enumerates 7 R7 drift patterns A-G mirroring Wave 5 task 050 CSV shape.
- [`.claude/skills/jps-validate/SKILL.md`](skills/jps-validate/SKILL.md) — Wave 7 task 073. Step 7.5 CHECK 25 marked LEGACY; CHECK 26 (structural-fallback) DELETED; new Step 7.6 R7-V-01-V-04 + 6 LEGACY-* drift flags; new Step 7.7 typed-config schema check against Wave 3 BFF endpoint.
- [`.claude/skills/jps-scope-refresh/SKILL.md`](skills/jps-scope-refresh/SKILL.md) — Wave 7 task 074 (FR-33). Two-authoring-surfaces table updated (Node Type OptionSet → Executor Type Choice Set, 33 values). C# enum rename `ActionType` → `ExecutorType` applied throughout. Operational behavior unchanged (terminology touch-up only).

Commits:
- `d79432f9e` — Wave 4 schema drops (043+044, FR-03+FR-04)
- `7f28da008` — Wave 4 AnalysisActionService cleanup (046)
- `dd95dff69` — Wave 4 publish-hygiene gate PASS (047)
- `79ced1c6a` — Wave 8 form default (081, FR-21)
- `6e5e070e3` — Wave 8 placeholder schemas (085, FR-23)
- `2a5ff9e5a` — Wave 8 promptSchemaOverride wiring (087, FR-25)
- `e020c25e4` — Wave 7 jps-* skill rewrites + smoke test (070-075, FR-32/33)
- (this commit) — Wave 6 tasks 064 + 068

### Added (2026-06-25 smart-todo-r4 R4-112 — PCF `noAposStringType` XSD failure mode)
- `.claude/skills/pcf-deploy/SKILL.md` — new row in Failure Modes & Recovery table for `noAposStringType` XSD validation failure (Dataverse PCF import rejects apostrophes in `description-key` attribute values). Discovered during RegardingResolver v1.2.0 deploy (commit 5b7a62812) — `entity's` and `'sprk_todo'` in description-key blocked the import. Comments are fine (XSD skips them); only attribute values matter. Burned ~10 min on first import attempt; this entry saves the next operator.

### Added (2026-06-25 spaarke-ai-platform-chat-routing-redesign-r1 Phase 5R Wave 5-C — ADR-037 Multi-Node Output Composition)
- `.claude/adr/ADR-037-multinode-output-composition.md` — concise ADR (~115 lines). Decision: introduce `NodeType.DeliverComposite` + per-section SSE streaming (`section_started` / `section_data` / `section_completed` keyed by section NAME, not schema position) + FE widget rework consuming `sections: Record<string, SectionState>`. Replaces the legacy 5-coordination-point schema-aware widget model (schema-on-action + schema-aware widget + ordinal indexing + implicit linkage + author-side-only contract) with a 2-point pattern (section name + section state). Legacy `FieldDelta` path preserved via runtime event-type detection for unmigrated playbooks. Chat-destination playbooks STAY single-action (composition adds no value for one streamed paragraph). MUST / MUST NOT rules + backward-compat invariants + reference implementation table (tasks 114R/114a/114b/118R).
- `docs/adr/ADR-037-multinode-output-composition.md` — full ADR with the 5-coordination-point fragility analysis (with examples of how rename / reorder broke rendering silently), 4 alternatives considered + rejection reasons, consequences (positive / negative / neutral), per-playbook migration runbook, open questions (section-name versioning policy, per-section regeneration UX). Driver: 2026-06-24 user design conversation surfaced architectural frailty in legacy widget; Phase 5R Wave 5-C is the rework.
- `.claude/adr/INDEX.md` — new ADR-037 entry placed after ADR-036.

### Added (2026-06-23 spaarke-devops-project-tracking-r1 — GitHub-native portfolio tracking)
- **9 new `/devops-*` skills** — `.claude/skills/devops-{portfolio-setup,epic-create,idea-create,idea-promote,project-start,project-register,project-sync,portfolio-status,project-archive}/SKILL.md`. Lifecycle: capture → promote → start → sync → archive. All idempotent (NFR-04). `/devops-project-start` is THE BLESSED HANDOFF (D-13) — the one canonical bridge from a Project Issue to a local worktree.
- **9 hook injections into existing skills** — `design-to-spec`, `project-pipeline`, `task-create`, `task-execute`, `context-handoff` (HIGHEST VALUE per spec §6.2), `worktree-setup`, `worktree-sync`, `repo-cleanup`, `merge-to-master` each gained a "Portfolio Hook" appendix section (additive only per NFR-03 — existing contracts unchanged). Hooks call `/devops-project-sync` (or `register`/`archive` where appropriate) at end of host skill execution.
- **GitHub Project #2 schema** — `Type=Project` option added (preserving 6 existing); 6 new custom fields (Project Type, Worktree Path, Project Folder, Task Count, Tasks Completed, Project Status); 7 labels (epic, project, backlog, worktree:active/archived, on-hold, cancelled); 3 issue templates at `.github/ISSUE_TEMPLATE/{epic,project,idea}.yml`; 12 initial Epic Issues #421–#432.
- **`.claude/skills/INDEX.md`** — 9 new rows for `/devops-*` family.
- **`CLAUDE.md` §17 Pointers** — new row for portfolio tracking + DevOps procedures.
- **`docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md`** — extended with "Portfolio Integration" section (FR-29): Step 0 idea capture, Epic ↔ Project mechanics, Idea promotion paths, BLESSED HANDOFF walkthrough, auto-hook behaviors table, 9-skill command reference, portfolio-specific troubleshooting.
- **`docs/procedures/AI-CODING-PROCEDURES-GUIDE.md`** — extended with "Portfolio Scenarios" section (FR-30): 7 new scenarios in existing tri-section pattern (capture idea / promote / update status / close project / see what's running / package ideas / stakeholder view).

### Critical lesson surfaced
- **`updateProjectV2Field` reassigns option IDs** — empirically verified during Phase 1 task 001 (2026-06-23). The GitHub GraphQL mutation REPLACES the full option list AND generates new internal option IDs for every option, even unchanged ones. Items currently bound to old option IDs lose their references. The `/devops-portfolio-setup` skill MUST implement snapshot → mutate → reconcile pattern. Logged in `projects/spaarke-devops-project-tracking-r1/notes/spikes/phase1-task001-execution-log-2026-06-23.md`.

### Added (2026-06-22 spaarke-ai-platform-chat-routing-redesign-r1 — Component Justification governance)
- `CLAUDE.md` — new §11 "Component Justification — Default to Reuse (BINDING)" + renumber §11→§12 through §17→§18. Introduces the three-question template (Existing / Extension / Cost-of-doing-nothing) for every NEW service / abstraction / interface / endpoint / DI registration / package / Dataverse column / file surface. Enforcement points: spec authoring (project-pipeline), plan WBS (task-create Step 3.5.6), code review (code-review Step 6.6). Anti-patterns documented from real R1 examples (LegalWorkspace dead-code misreading, sprk_playbookcode field-choice error, 8-tool surface overreach). Driver: chat-routing-redesign-r1 Q&A surfaced three scope-creep failures that the rule would have caught at authoring time.
- `.claude/skills/task-create/SKILL.md` — new Step 3.5.6 "Component Justification Gate (REQUIRED per CLAUDE.md §11)". Requires `<justification>` POML element on every new-component task; decision logic for REWRITE-as-extension vs DEMOTE/DROP vs PROCEED. Scope explicitly excludes pure modifications to existing files.
- `.claude/skills/code-review/SKILL.md` — new Step 6.6 "Component Justification Check (Universal — CLAUDE.md §11)". Extends Step 6.5 (BFF Hygiene) from BFF-only to all new components. Verifies the three answers are concrete (cite file:line, name a concrete failure mode); flags hollow / boilerplate justifications as WARNINGs.

### Changed (2026-06-21 spaarke-ai-platform-chat-routing-redesign-r1 — ADR-030 v2 `memory` channel amendment)
- `.claude/adr/ADR-030-pane-event-bus.md` — v2 amendment adds 5th channel `memory` to the PaneEventBus closed union. New `MemoryPaneEvent` interface with 5 initial discriminants: `promotion_pending`, `promotion_resolved`, `fact_promoted`, `pin_added`, `pin_removed`. Channel union expanded from 4 → 5; sixth channel still requires successor ADR. Amendment Record section appended documenting context (chat-routing-redesign-r1 6-tier memory subsystem), constraints preserved (ADR-015 tier-1 safety on payloads — deterministic IDs + 80-char summaries only; tenant scope via subscriber context), required implementation updates (PaneEventTypes.ts extension; ContextPane subscriber wiring; MatterMemoryPromotionService dispatch site). Driver: chat-routing-redesign-r1 architecture §6.4 promotion approval workflow needed dedicated semantic channel instead of namespaced `workspace.*` workaround.
- `docs/adr/ADR-030-pane-event-bus.md` — full ADR amended in lockstep with concise version. Decision section §1 expanded from 4 → 5 channels; "fifth channel" references throughout updated to "sixth channel"; verification grep commands updated; AI-Directed Coding Guidance updated with new guidance for memory-domain events; Amendment History section appended (v2 record). Both ADR versions stay in sync.

### Added (2026-05-26 R4 Phase 1 F-3 — publish-size per-task verification rule)
- `.claude/constraints/azure-deployment.md` — new "BFF Publish-Size Per-Task Verification Rule (NFR-01)" section. Binding rule: every BFF-touching task MUST run `dotnet publish` + report compressed size + diff vs prior baseline. Ceiling: ≤60 MB (spec NFR-01). Current baseline ~45.65 MB. Escalation thresholds: ≥+5 MB single-task → justification; ≥55 MB cumulative → architecture review; ≥60 MB → HARD STOP. Driver: R4 NFR-01 / F-3 (operationalizes ADR-029).
- `CLAUDE.md` (root) §10 item 4 — strengthened from "verify if adding NuGet packages" → "verify on EVERY BFF-touching task" with explicit `dotnet publish` command, absolute-size + diff reporting requirement, and escalation thresholds. Cross-links to azure-deployment.md.

### Changed (2026-05-26 sdap-bff-api-remediation-fix Phase 5 wrap-up)
- `docs/guides/auth-deployment-setup.md` §3 expanded with new §3.5 covering 25+ App Settings discovered during Phase 5 demo prep beyond the original "8 settings" inventory (MI identity disambiguation 5 keys + Cosmos persistence + AgentService placeholders + feature-flag=false patterns + email subsystem).
- `docs/guides/auth-deployment-setup.md` §7c — drop `-UserPrincipalName` from `Connect-ExchangeOnline` example to avoid the mismatch failure mode discovered in Phase 5 (operator's browser-selected account vs param).
- `.claude/constraints/azure-deployment.md` Publish & Packaging — added linux-x64 RID + sourcemap exclusion MUST rules; baseline compressed size updated 60 → 45 MB (Phase 5 measured 45.65 MB post-Outcome-A).
- `.claude/FAILURE-MODES.md` extended with 4 new entries (AP-4 dev/demo bundle drift `/api` bug; G-5 Dataverse Application User registration; G-6 `Connect-ExchangeOnline -UPN` mismatch; G-7 Git Bash MSYS path mangling).
- `.claude/adr/ADR-007-spefilestore.md` — cross-reference added to refined ADR-013 + the new `Services/Ai/PublicContracts/` facade as parallel example of facade-over-SDK pattern.
- `.claude/adr/ADR-010-di-minimalism.md` — Phase 5 baseline note (265 registrations; +4 from facade is within expected delta).
- `docs/architecture/AI-ARCHITECTURE.md` — new "AI Public Contracts Facade Boundary" section documenting the 4 facade interfaces + 5 documented AI-API-surface exceptions + handler relocation to `Services/Ai/Jobs/`.
- `docs/architecture/AUTH-AND-BFF-URL-PATTERN.md` — cross-env consistency callout + checklist item for `sprk_BffApiBaseUrl` format.
- `docs/architecture/INFRASTRUCTURE-PACKAGING-STRATEGY.md` — new §5 BFF Binary Packaging covering linux-x64 RID + sourcemap exclusion + transitive override pattern + measured baselines.
- `docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md` — added MI + Cosmos DB rows to per-environment resources table.
- `docs/guides/CUSTOMER-DEPLOYMENT-GUIDE.md` — resolved `sprk_BffApiBaseUrl` `/api` suffix contradiction with auth-deployment-setup.md.
- `docs/guides/COMMUNICATION-DEPLOYMENT-GUIDE.md` — added full 17-setting email inventory discovered in Phase 5 (9 Communication + 8 EmailProcessing).
- `docs/guides/DATAVERSE-AUTHENTICATION-GUIDE.md` — added MANDATORY Application User registration section with full Web API walkthrough.
- `docs/guides/PCF-DEPLOYMENT-GUIDE.md` — added URL construction convention section documenting `getBffBaseUrl()` host-only pattern.
- `docs/guides/AI-DEPLOYMENT-GUIDE.md` — added mandatory Cosmos DB infrastructure section (account + DB + 5 containers + RBAC + App Settings).

### Added (2026-05-26)
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/` facade per refined ADR-013 — 4 interfaces (`IBriefingAi`, `IInvoiceAi`, `IRecordMatchingAi`, `IWorkspacePrefillAi`) + 4 implementations. 10 CRUD consumers migrated (Finance, Workspace, Jobs, Dataverse, Filters, Endpoints); 5 documented AI-API-surface exceptions (Chat/Playbook/Builder/Agent endpoints + auth filter). 92% reduction in direct AI injection in CRUD code.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Jobs/` (5 files relocated from `Services/Jobs/{Handlers,}` per FR-E3): AppOnlyDocumentAnalysisJobHandler, BulkRagIndexingJobHandler, EmailAnalysisJobHandler, ProfileSummaryJobHandler, EmbeddingMigrationService.
- LegalWorkspace `/api` prefix fix (3 sites: `FilePreviewDialog.tsx:320`, `closureService.ts:63`, `provisioningService.ts:81`) — commit `2561ce37`. Deployed to both dev + demo `sprk_corporateworkspace` web resource.

### Changed
- **`code-review` + `adr-check` now enforce CLAUDE.md §10 BFF Hygiene + `bff-extensions.md`** — closes the gap where the binding §10 rule was loaded as context but never explicitly checked. `adr-check` Step 2's quick-reference table adds ADR-013 (refined 2026-05-20); new Step 2.5 conditionally loads `bff-extensions.md` and applies its 5-rule pre-merge checklist when changed files touch `Sprk.Bff.Api/`, `Spaarke.Core/`, or `Spaarke.Dataverse/`. `code-review` Step 6 adds ADR-013 to its CRITICAL ADRs list; new Step 6.5 runs the same §10 checklist with explicit severity assignment (missing Placement Justification → Critical; new direct CRUD→AI dep → Critical; new HIGH-severity CVE → Critical). Both edits cite `bff-extensions.md` as the single source of truth — zero duplication of rule content.

### Added
- `.claude/AUDIT-FINDINGS-CLAUDEMD.md` — Phase 3a audit of root `CLAUDE.md` against community best practices + Phase 0 inventory (75-section sign-off table + proposed skeleton + open questions). Commit `0c11cd43`.
- `.claude/archive/2026-05-17/CLAUDE.md` — preserved copy of the 1190-line OLD root `CLAUDE.md` before Phase 3b rewrite (reversibility per NF-1).
- **Auth v2 pre-flight** — STOP banners on 5 partially-superseded docs (`.claude/patterns/auth/spaarke-sso-binding.md`, `.claude/patterns/auth/token-caching.md`, `.claude/constraints/auth.md`, `docs/architecture/AUTH-AND-BFF-URL-PATTERN.md`, `docs/architecture/sdap-auth-patterns.md`) + full-deprecation banners on 2 DEPRECATED-* files. Each banner names what stays canonical (INV-1..INV-7, server-side OBO, `buildBffApiUrl()`, etc.). PF-4..PF-10. Commit `281f7210`.
- **Auth v2 pre-flight** — Pointer row in root `CLAUDE.md` §15 directing all agents (any worktree) to `.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md` as the active auth v2 design until ADR-027 ships. PF-12. Commit `5b04b6ff`.

### Changed
- **Root `CLAUDE.md` rewritten** from 1190 → 264 lines (78% reduction) per Phase 3b. Applies community best practices: project-specific operational rules only; tutorials/marketing/long reference tables moved out; pointer-heavy structure. User-locked decisions: §1 identity updated to "enterprise AI-directed legal operations intelligence platform"; §11 System Entry Points + §12 Context Layer Hierarchy kept inline (user judgment); §13 Knowledge Repository section added pointing at `spaarke/knowledge/` + `researcher` subagent for rapidly-evolving Microsoft platform topics; Rigor Level template kept inline; Hooks: Current Guidance compressed to one paragraph.
- 5 internal contradictions resolved in the rewrite (Hooks System vs Current Guidance; trigger phrases in 2 places; Before-Starting-Work vs Working-Checklist; etc.).
- **Auth v2 pre-flight** — 11 in-scope references updated to point at the new `DEPRECATED-*` filenames with "⛔ DEPRECATED — superseded by Spaarke Auth v2" markers: `.claude/patterns/auth/INDEX.md`, `.claude/patterns/INDEX.md`, `.claude/constraints/auth.md`, `.claude/patterns/auth/spaarke-sso-binding.md`, `.claude/patterns/webresource/{code-page-wizard-wrapper.md, full-page-custom-page.md}`, `.claude/skills/code-page-deploy/SKILL.md`, `docs/architecture/sdap-auth-patterns.md`, `CROSS-REFERENCE-MAP.md`, `src/solutions/SpaarkeAi/src/App.tsx`, `src/solutions/Reporting/{main.tsx, services/authInit.ts, config/runtimeConfig.ts, config/reportingConfig.ts}`. Historical `projects/*` references, `.claude/archive/`, and the audit doc's rename-action narrative left intentionally unchanged. PF-3. Commit `c2198007`.

### Deprecated
- **Auth v2 pre-flight** — Two fully-superseded auth pattern docs renamed with `DEPRECATED-` prefix so the filename itself is a stop signal in Grep/Glob output:
  - `.claude/patterns/auth/msal-client.md` → `.claude/patterns/auth/DEPRECATED-msal-client.md`
  - `.claude/patterns/auth/spaarke-auth-initialization.md` → `.claude/patterns/auth/DEPRECATED-spaarke-auth-initialization.md`
  Both files will be removed when v2 ships (Workstream F4, task 094). PF-1, PF-2. Commit `c2198007`.

### Removed
- The 22 extract-candidate sections totaling ~720 lines from old `CLAUDE.md`. Content remains preserved in `.claude/archive/2026-05-17/CLAUDE.md`. Topics removed: detailed Adaptive Thinking tutorial, Permission Modes tutorial, Hooks System tutorial, Headless Mode, Agent Teams (experimental), Component Skills note (now in `.claude/skills/INDEX.md`), Trigger Phrases table, Slash Commands table, Coding Standards code samples (in `docs/standards/`), Repository Structure tree (in `README.md`), ADR summary table (in `.claude/adr/INDEX.md`), Quality Gates with Hooks (feature not configured), and dated/duplicate sections.

### Fixed
- N/A — Phase 3a/3b are restructuring; no behavioral fixes in this scope.

### Verified
- **Auth v2 pre-flight** — `projects/spaarke-auth-v2-and-hardening/CLAUDE.md` "🚨 ACTIVE AUTH V2 REFACTOR — DO NOT REGRESS" section cross-checked against audit §8.2 Layer 3 (PF-11) requirements. All MUST/MUST NOT bullets present plus extras (/debug endpoint ban, plain-text secret ban, INV-1..INV-8 preservation). No edits required. PF-11. Commit `f58317b0`.

### Retirement note
- All "Auth v2 pre-flight" entries above (PF-1..PF-13) are transitional. They will be retired during Workstream F (Engineering canonical docs): F1 ships ADR-027, F2 partial-rewrites `spaarke-sso-binding.md`, F3 ships `docs/guides/auth-deployment-setup.md`, F4 deletes the `DEPRECATED-*` files and removes the STOP banners + project CLAUDE.md prohibition + root CLAUDE.md pointer row. See `.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md` §8.4–§8.5.

---

## [ai-procedure-quality-r1] - planned for 2026-05-XX

---

## [ai-procedure-quality-r1] - planned for 2026-05-XX

> Entry will be promoted from `[Unreleased]` when the project's PR #294 merges. The deliverables below are the planned set.

### Added
- `.claude/agents/researcher.md` — Opus, effort: high researcher subagent for deep-dive Microsoft platform investigation; accumulates findings via project memory (`MEMORY.md`). Per design.md Directive 1. (Task 010)
- `.claude/skills/_template/SKILL.md` — canonical skill scaffold enforcing the 7 best practices; new skills clone this; existing skills are measured against it during Phase 2a audit. (Task 011)
- `.claude/CHANGELOG.md` — this file. Forward-only convention. (Task 012)
- `.claude/FAILURE-MODES.md` — repo-level catalog of cross-cutting failure patterns. 4 inaugural entries derived from 2026-05-14 incidents. (Task 013)
- `.claude/archive/` directory with date-organized subdirectory convention; reversibility-first removal pattern. (Task 014)
- `scripts/quality/Validate-SkillReferences.ps1` — Light reference check across all 49 skills (file paths, URLs, skill names). Runs in CI; <10s. (Task 065)
- `scripts/quality/Find-SkillReferenceDrift.ps1` — 7-surface drift detector; catches broken refs after rename/split/merge. (Task 066)

### Changed
- Root `CLAUDE.md` rewritten to the tiered target (<200 lines). Reference content moved to subdirectories. The pre-rewrite version is preserved in `.claude/archive/2026-05-14/CLAUDE.md`. (Phase 3b deliverable)
- Multiple skills refined per `.claude/AUDIT-FINDINGS-SKILLS.md`. Specific refactors listed under each skill in the per-skill section of the audit findings. (Phase 2b deliverable)

### Removed
- Skills audit-recommended-and-approved for removal (specific list determined at Human Gate 1). Folders archived to `.claude/archive/2026-05-14/skills/<name>/`, not deleted from disk. (Phase 2b deliverable)

### Fixed
- N/A — Phase 0 inventory surfaced existing issues (5 failing workflows, 3 PCFs with wrong `build:prod`, etc.) but their fixes are in separate scope from this project.

---

*Established 2026-05-14 by project `ai-procedure-quality-r1` (task 012). See [.claude/archive/README.md](archive/README.md) for the reversibility convention referenced above.*
