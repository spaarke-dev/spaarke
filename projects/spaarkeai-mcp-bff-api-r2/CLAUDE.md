# Spaarke MCP r2: Ask tool parity and retiring the Spaarke AI agent — project operating manual

> Read with `current-task.md` (current state). Repo-wide rules are in root `CLAUDE.md`; this file holds only what is specific to this project. Guidance: `.claude/skills/project-setup/references/claudemd-template.md`.

## 1. Scope and status

This project removes the legacy "Spaarke AI" declarative agent (`src/solutions/CopilotAgent/`, the `/api/agent/*` gateway, `AgentModule`, `infrastructure/bot-service/`, their CI, scripts, tests and docs) and gives Copilot Chat and Cowork its useful functions as MCP tools on the existing BFF MCP server `/mcp/legal-operations` (r1's host, one connector): search, documents, playbooks, playbook runs, analysis, tasks (events), portfolio, the AI Daily Briefing, the matter AI summary, matter finance and email. It adds durable playbook run status (`sprk_playbookrun` / `sprk_playbooknoderun`), the 30 s start-then-status pattern, caller-scope proofs, three widgets that reuse Console components (Daily Briefing, search results, email compose) on one shared widget MCP transport, a routing eval, and the Dataverse MCP admin position. **Out of scope:** practice-area agents and the attorney profile (specialist-legal-agents-r1), invoice review/approval in Copilot (EB-D-194), intake, a second MCP route/server/connector, `summarizeFile`, the agent chat loop and identity functions, Dataverse's built-in MCP for end users, Outlook as a host, tenant/admin actions.

Status: see `tasks/TASK-INDEX.md` and `current-task.md`. Spec: `spec.md`. Design: `design.md` (v0.3). Plan: `plan.md`.

Tasks (2026-11-25, 35 POMLs): Phase 0 removal, prerequisites and seams 001–012 + deploy 019 · Phase 1 read tools 020–026 + deploy 029 · Phase 2 long-running, AI, widgets, task writes 030–036 + deploy 039 · Phase 3 email, eval, E2E 040–043 + deploy 049 · wrap-up 090. **Next action:** Phase 0 Wave 1 — 001, 002, 005, 006, 008, 009 in parallel (`tasks/TASK-INDEX.md` "Parallel Execution Plan").

## 2. Binding rules for this project

**Tools (FR-02..FR-12)**
- Every tool is a thin adapter over the service or capability its REST route uses, run as the caller: request `HttpContext` via `IHttpContextAccessor`; null or unauthenticated → `isError` `UserContextRequired`; never an app-only fallback (ADR-028 A10, FR-15). Model: `Api/Mcp/LegalOperations/Decisions/CompleteDecisionTool.cs`.
- Every tool carries `[McpArea("legal-operations")]`, explicit `readOnlyHint`, `destructiveHint`, `idempotentHint`, a title, and a "use for / not for" description ≤ 1,024 characters.
- Errors are `isError` results with the REST `reasonCode` through the generalised `McpToolResults` (task 020); never transport errors (ADR-019).
- Register tools only through `McpToolCatalog` (attribute discovery) and widget resources only through `McpModule`'s resource collection; update the MCP tool ledger (`RouteAuthorizationGuardTests.Mcp*.cs`) in the diff that adds the tool.
- MUST NOT copy authorization checks into tools: playbook checks go through task 007's shared method, finance through task 008's; events through task 006's capability (spec MUST NOT).
- Every tool answers within 30 s; longer work returns `{runId, status, consoleLink}` through task 020's helper, and a status tool returns progress or the result (FR-14).
- Writes: reversible writes (task create/complete, run start, analysis start) are model-callable, server-validated, with idempotency keys; `spaarke_send_email` is app-only with an idempotency key; nothing relies on a host consent prompt (NFR-06).
- Model-visible output is minimal: summaries and slim rows; detail to widgets via app-only tools; privileged/secured fields never in model-visible text (NFR-07, ADR-015). Every result < 64 KiB at realistic maxima (50 search hits, 200 tasks) (NFR-02).
- Logs carry tool name, latency, row count, outcome/reason code and correlation id only — never tokens, claims, query text, email bodies or document content (NFR-05).
- MUST qualify "playbook" by format (JPS / legal / contract-review playbook) where it matters in user-facing text (D-209).

**Extractions and persistence**
- REST response bytes stay unchanged through every extraction (events, finance authorization, run status), proved by frozen-bytes or contract tests (model: `tests/integration/contract/Api/Signals/SignalRoutesContractBaselineTests.cs`).
- Run status persists on the run's own execution path; a write failure is logged with an outcome code and never fails the run; no background worker, no hand-rolled `BackgroundService` (FR-13, ADR-052).
- No new summary store: the matter summary is the `matter-summary` Insights topic (FR-10).

**Widgets (FR-16)**
- One Console component per widget behind a data seam; `_meta.ui.csp` empty; all data through MCP tools; Fluent v9 semantic tokens; host light/dark; keyboard operable (NFR-09); < 1 MB target, 3 MB hard ceiling via the widget's `check-bundle-size.mjs` (NFR-03).
- The Console's own uses of `DailyBriefingApp`, `SearchResultsWidget` and `EmailComposer` keep passing their existing tests unchanged.

**Platform**
- MUST NOT add a second MCP route, server or connector (owner Q2). MUST NOT call Dataverse's built-in MCP server or any route that bypasses the BFF's rules (design §4.7). MUST NOT expose invoice review or approval (EB-D-194).
- MUST NOT pass the caller's token downstream, or log it or its claims.
- `Mcp:Enabled=false` still turns off every tool and widget (ADR-032, NFR-10).
- Every BFF task records the publish-size delta vs a fresh master build (+5 MB justify, 55 MB review, 60 MB hard stop) and checks for new HIGH CVEs; the PR states the Placement Justification citing `.claude/constraints/bff-extensions.md` (NFR-04, root §10).
- Conformance (`tests/integration/Mcp.Conformance.Host/`) stays green with every tool registered (NFR-08).
- CAPABILITY-INDEX (`.claude/patterns/CAPABILITY-INDEX.md`) is `.claude/`: tasks that add a canonical component write the proposed row into `notes/capability-index-pending.md`; the main session applies it.
- Deferred work and newly found issues: `/project-defer-issue-tracking` writes `notes/defer-issues.md` AND a GitHub issue — never one without the other.

ADR tensions approved for this project (root CLAUDE.md §6.5): none. Two Path C checks are recorded in spec "ADR Tensions" (model-callable reversible writes vs ADR-028 A10; run persistence vs ADR-052).

**HARD WARNING (hot path).** `design.md` has no `<hot-path-declaration>` block; the block is in `spec.md` ("Hot-Path Declaration": BFF Y, SpaarkeAi N, ci-workflows Y, skill-directives Y, root-CLAUDE-md N). The owner may add it to design.md. Do not edit design.md.

## 3. Owner directives and standing decisions

Full log: `notes/decisions.md`.
- 2026-11-25 — Remove the legacy agent now, no parallel period; tenant steps E1/E2 are the owner's (`projects/copilot-platform-coordination/notes/removal-runbook.md` on `work/copilot-platform-coordination`).
- 2026-11-25 — One route and one connector; routing misses are fixed in tool descriptions, not by a split (Q2).
- 2026-11-25 — 30 s per tool; start + status beyond it (Q3).
- 2026-11-25 — Reversible writes model-callable with server validation and idempotency; email send app-only (spec Owner Clarifications).
- 2026-11-25 — Persist runs to `sprk_playbookrun` / `sprk_playbooknoderun`.
- 2026-11-25 — Email v1: To/Cc, subject, rich body, regarding a matter (required), the matter's documents as attachments, archived to the matter; the model may pre-fill, only the user sends.
- 2026-11-25 — Matter summary = complete the `matter-summary` Insights topic (new `sprk_matter` column + `sprk_aitopicregistry` row), cached else start; not matter-health.
- 2026-11-25 — Briefing = AI Daily Briefing with a data seam in `Spaarke.DailyBriefing.Components`.
- 2026-11-25 — Widgets reuse Console components, one per widget (Q5); Phase 0 parallel with r1, tool phases after r1's PRs (Q7); Dataverse MCP dev/admin only (Q8).

## 4. Coordination

- **spaarkeai-mcp-bff-api-r1** (session `mcp-r1`) — handed ownership of `Api/Mcp/**`, `McpModule`, `McpToolCatalog`, the MCP ledger (`RouteAuthorizationGuardTests.Mcp.cs`), the conformance host and the spaarke-decisions transport and build files to r2 on 2026-11-25 (gate released by joint determination under the owner's delegation, notes/decisions.md D-r1-gate). r1 keeps ISS-010 (#3184: DecisionCommitService, Actions/DecisionActionExecutors, RecordCreationService, CallerWriteAuthorization, CallerRecordAccessProbe, CallerMetadataReadMemo, AssignTaskNotifier, the email-bell job handler) and the Decision wizard heading change (FoundStep.tsx, IssueLine.tsx, composeIssueLine.ts, and the widget test lines that wait on "What was found"). Message mcp-r1 before editing those, and before opening or merging a branch that touches spaarke-decisions. A defect that r1's 042 testing finds in an r2-owned file is filed and discussed before anyone edits. spec.md names PR #3146; that PR has merged.
- **unified-access-control-r2** — reviews every caller-path diff (001, 006, 007, 008, 009, each tool task, 040) and the ledger restructure in 020. Dev BFF deploys run through its session with the owner's per-SHA go.
- **ebilling-r1** — reviews 008 (finance authorization move) and 033 (finance tool); EB-D-194: no invoice review in Copilot.
- **ontology-platform-r1** — reviews capability extractions (006 events, 007 playbook, 008 finance), as in r1.
- **specialist-legal-agents-r1** — future `/mcp/patents` on the same host; reuses FR-13 run records (its D-A18 adds profile-version and parent-run lookups on top).
- **customer-provisioning-orchestration-r1** — owns the control-plane "shared Spaarke Copilot Agent app" (T257; `infrastructure/bicep/platform-controlplane.bicep`, `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §7.12). Task 002 leaves those untouched and files a coordination issue.
- **copilot-platform-coordination** — tenant registration changes (retiring Spaarke AI), runbook E1/E2.
- Don't edit other projects' worktrees; coordinate by message.

## 5. Environment and live actions

- Dev only. Every dev BFF deploy (019, 029, 039, 049) goes through uac-r2's session with the owner's go for the exact SHA, using the `bff-deploy` skill (`scripts/Deploy-BffApi.ps1`) — never raw `az webapp deploy --clean`. Run `context-handoff` after each deploy.
- 019 additionally waits until the owner confirms runbook E1 (uninstall and delete both "Spaarke AI" Registry rows).
- Dev Dataverse schema changes (005, 012) and the `sprk_aitopicregistry` seed (012) need the owner's explicit go at that step; read-only Dataverse MCP checks are fine.
- A dev App Service restart (039, FR-13 check) needs the owner's go.
- Tenant and admin actions (Registry, Azure Bot, Entra app, Key Vault) are the owner's (root §9). Key Vault secret changes follow `.claude/constraints/provisioning.md` "KV credential lifecycle".
- Never `git stash`; never `python3` in Bash; every Agent launch names a model.
- Commits: `export PATH="/c/code_files/spaarke/node_modules/.bin:$PATH"`; end with the attribution line from the session reminder.

## 6. Gotchas — do not re-learn

- 2026-11-25 — `HandoffUrlBuilder` lives in `Api/Agent/` and is registered only in `AgentModule`, but the live chat create-record tool injects it (`Services/Ai/Handlers/DataverseCreateRecordHandler.cs:229`) and many data-mutation tests use it. Relocate and re-register it before deleting `AgentModule`; do not delete it (task 001).
- 2026-11-25 — `PlaybookAuthorizationFilter.cs:576,581` and `PlaybookRouteAuthorizationContractTests.cs:535` use `AgentEndpoints.PlaybookIdRequiredDetail` / `DocumentIdRequiredDetail`, but only inside the agent branch of `AuthorizeRunAsync` (`:569-588`) and an agent-route contract test. Task 001 removes that branch and rewrites any Run-mode proof onto the execute route; it moves a constant only if a non-agent caller is found.
- 2026-11-25 — `Api/Mcp/LegalOperations/Decisions/DecisionsTextSummary.cs:11` mentions "Api/Agent" in a comment; it is an r1 file, so 001 leaves it and 020 clears it (the FR-01 grep is case-insensitive).
- 2026-11-25 — The agent's start/poll shape (`AgentEndpoints.cs:461-481`, `PlaybookStatusEndpoints.cs`) and playbook de-duplication (`AgentEndpoints.cs:62`, `:334`, `:357`) are deleted by 001; later tasks read them with `git show 37f0864afe:<path>`.
- 2026-11-25 — `IPlaybookOrchestrationService.cs:724` and `PlaybookParameterPolicy.cs:18` mention `/api/agent/*` in comments; 001 owns those comment edits, 005 does not edit the interface file.
- 2026-11-25 — The widget bundle gate is `src/client/widgets/spaarke-decisions/scripts/check-bundle-size.mjs` (not `scripts/check-bundle-size.mjs` at repo root).
- 2026-11-25 — The matter summary lives in `sprk_matter.sprk_recordsummary` (registry row `matter-summary/single`, TTL 60 min), not a new column. The `matter-summary` Binding has no `sprk_playbook` Guid, so `InsightsPlaybookExecutionCache` (keyed by playbook id) cannot serve it: task 032 reads the persisted `sprk_recordsummary` envelope, else starts a run (`notes/012-matter-summary-topic.md`).

## 7. Key documents

- `spec.md` · `design.md` · `plan.md` · `notes/decisions.md` · `notes/defer-issues.md` · `notes/reuse-scan.md` · `notes/legacy-agent-inventory-2026-11-25.md`
- Recipe: `docs/guides/HOW-TO-ADD-AN-MCP-TOOL.md`; architecture: `docs/architecture/SPAARKE-MCP-SERVER-ARCHITECTURE.md`, `docs/architecture/SPAARKE-AI-SURFACES-AND-PACKAGING.md`.
- Applicable ADRs: 001, 002, 004, 008, 010, 012, 013, 015, 019, 021, 028, 029, 032, 039, 043, 044, 045, 050, 052 (`.claude/adr/`), ADR-038 (`docs/adr/`).
- Skills: `mcp-tool-handler`, `widget-design`, `fluent-v9-component`, `spaarke-conventions`, `dataverse-create-schema`, `dataverse-deploy`, `dataverse-mcp-usage`, `adr-check`, `code-review`, `test-diet`, `ui-test`, `bff-deploy`, `conflict-check`.
- Related projects: `spaarkeai-mcp-bff-api-r1`, `unified-access-control-r2`, `ebilling-r1`, `spaarke-ontology-platform-r1`, `specialist-legal-agents-r1`, `customer-provisioning-orchestration-r1`, `copilot-platform-coordination`.
