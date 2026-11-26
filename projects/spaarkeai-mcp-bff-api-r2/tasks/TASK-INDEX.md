# Task Index — spaarkeai-mcp-bff-api-r2

> **Project**: Spaarke MCP r2 — Ask tool parity and retiring the Spaarke AI agent
> **Created**: 2026-11-25 (task-create, project-pipeline Step 3)
> **Total tasks**: 35 (31 implementation + 4 deploys + 1 wrap-up)
> **Status tokens** (task-create Step 5): 🔲 `[open]` · 🔄 `[wip]` · ✅ `[done]` · ⚠️ `[escalated]` · 🟡 `[blocked]` · 🚫 `[cancelled]` · ⏸️ `[deferred]`. The bracketed token is mandatory in every status cell (FAILURE-MODES G-16). After any status change run `pwsh scripts/check-task-status-drift.ps1`.

## Task Registry

| ID | Title | Phase | Status | Dependencies | Parallel | Model / effort | Rigor |
|-----|-------|-------|--------|--------------|----------|----------------|-------|
| 001 | Remove the legacy agent from the BFF | 0 | ✅ [done] | none | Group A | opus / high | FULL |
| 002 | Remove the legacy agent outside the BFF | 0 | ✅ [done] | none | Group A | sonnet / high | STANDARD |
| 004 | Dataverse MCP: research re-check and deployment-guide admin paragraph | 0 | ✅ [done] | none | Group A | sonnet / high | MINIMAL |
| 005 | Durable playbook run status (`sprk_playbookrun` / `sprk_playbooknoderun`) | 0 | 🔄 [wip] | none | Group A | opus / high | FULL |
| 006 | Events capability extraction and must-not-leak proofs | 0 | ✅ [done] | none | Group A | opus / high | FULL |
| 008 | Finance summary authorization into one shared method | 0 | 🔄 [wip] | none | Group A | opus / high | FULL |
| 009 | Record search: no query built from raw query text (proof) | 0 | ✅ [done] | none | Group A | opus / high | FULL |
| 010 | Daily Briefing data seam in `Spaarke.DailyBriefing.Components` | 0 | ✅ [done] | none | Group A | sonnet / xhigh | FULL |
| 011 | EmailComposer MCP host (sibling of the Xrm host) | 0 | ✅ [done] | none | Group A | sonnet / high | FULL |
| 012 | Complete the matter-summary Insights topic (column, registry row) | 0 | 🔄 [wip] | none | Group A | sonnet / high | FULL |
| 007 | Playbook run authorization into one shared method | 0 | ✅ [done] | 001 | Group B | opus / high | FULL |
| 003 | Update `.claude/` references to the legacy agent (main session only) | 0 | ✅ [done] | 001, 002 | Group C (main session) | sonnet / high | STANDARD |
| 019 | Deploy Phase 0 to dev (runbook E1 + per-SHA go) | 0 | 🔲 [open] | 001, 002, 005, 006, 007, 008, 009 | Group D (alone) | sonnet / high | STANDARD |
| 020 | Shared tool plumbing: general `McpToolResults`, start/status helper, per-area ledger files | 1 | ✅ [done] | 001 + r1 gate | Group E | opus / high | FULL |
| 021 | Shared widget MCP transport; Decisions widget moved onto it | 1 | ✅ [done] | 011 + r1 gate | Group E | opus / high | FULL |
| 022 | Search tools: `spaarke_search_documents`, `spaarke_find_records` | 1 | 🔲 [open] | 009, 020, 027 | Group F | sonnet / high | FULL |
| 023 | Document tools: `spaarke_list_documents`, `spaarke_get_document` | 1 | 🔄 [wip] | 020 | Group F | sonnet / high | FULL |
| 024 | `spaarke_list_playbooks` | 1 | 🔄 [wip] | 020 | Group F | sonnet / high | FULL |
| 025 | Task read tools: `spaarke_list_tasks`, `spaarke_get_task` | 1 | 🔲 [open] | 006, 020 | Group F | sonnet / high | FULL |
| 026 | `spaarke_get_portfolio` | 1 | ✅ [done] | 020 | Group F | sonnet / high | FULL |
| 027 | Search result authorization into shared methods (prerequisite for 022; added 2026-11-25) | 1 | 🔄 [wip] | 009, 020 | Group F | opus / high | FULL |
| 029 | Deploy Phase 1 to dev; routing-eval run A | 1 | 🔲 [open] | 019, 020–026 | Group G (alone) | sonnet / high | STANDARD |
| 030 | Playbook run tools: `spaarke_run_playbook`, `spaarke_get_playbook_run` | 2 | 🔲 [open] | 005, 007, 020, 024 | Group H | opus / high | FULL |
| 031 | Analysis tools: `spaarke_start_analysis`, `spaarke_get_analysis` | 2 | 🔲 [open] | 020 | Group H | opus / high | FULL |
| 033 | `spaarke_get_matter_finance` | 2 | 🔲 [open] | 008, 020 | Group H | sonnet / high | FULL |
| 034 | `spaarke_get_briefing` + Daily Briefing widget | 2 | 🔲 [open] | 010, 020, 021 | Group H | sonnet / xhigh | FULL |
| 036 | Task write tools: `spaarke_create_task`, `spaarke_complete_task` | 2 | 🔲 [open] | 006, 020, 025 | Group H | sonnet / high | FULL |
| 032 | `spaarke_get_matter_summary` (cached, else start a run) | 2 | 🔲 [open] | 012, 020, 030 | Group I | sonnet / high | FULL |
| 035 | Search results widget | 2 | 🔲 [open] | 021, 022, 034 | Group I | sonnet / high | FULL |
| 039 | Deploy Phase 2 to dev; FR-13 restart check; routing-eval run B | 2 | 🔲 [open] | 029, 030–036 | Group J (alone) | sonnet / high | STANDARD |
| 040 | Email: compose widget, `spaarke_compose_email`, app-only `spaarke_send_email` | 3 | 🔲 [open] | 011, 021, 023, 035, 039 | Group K (alone) | opus / high | FULL |
| 041 | Routing eval runs A/B/C and description fixes | 3 | 🔲 [open] | 029 (runs B, C wait for 039, 049) | Group L (spans phases) | sonnet / high | STANDARD |
| 049 | Deploy Phase 3 to dev; routing-eval run C | 3 | 🔲 [open] | 039, 040 | Group N (alone) | sonnet / high | STANDARD |
| 042 | Latency and result-size run on dev (NFR-01, NFR-02) | 3 | 🔲 [open] | 049 | Group M | sonnet / high | STANDARD |
| 043 | Dev E2E as the test user in Chat and Cowork | 3 | 🔲 [open] | 049 | Group M | sonnet / high | STANDARD |
| 090 | Project wrap-up | Wrap-up | 🔲 [open] | 003, 004, 041, 042, 043, 049 | Group O (alone) | sonnet / high | STANDARD |

Rows are in execution order within each phase. Task numbers 013–018, 027–028, 037–038, 044–048 are unused (reserved for adopted defects or splits).

## Gates and external conditions

- **r1 gate (Phase 1+): RELEASED 2026-11-25** (notes/decisions.md D-r1-gate). All r1 code is on master (#3146, #3152, #3161, #3127); r1 task 042 is live testing and notes only. Ownership of `Api/Mcp/**`, `McpModule`, `McpToolCatalog`, the MCP ledger, the conformance host and the spaarke-decisions transport and build files moved to r2. r1 keeps the ISS-010 files and the Decision wizard heading files (each POML lists them); message mcp-r1 before editing those. Merge master in before Phase 1 (merge, never rebase). spec.md names PR #3146; that PR has already merged. Phase 0 touches no r1-owned file (`Api/Mcp/**`, `McpModule.cs`, `McpToolCatalog`, `RouteAuthorizationGuardTests.Mcp.cs`, the Decisions widget, the conformance host).
- **Deploys** (019, 029, 039, 049) run through unified-access-control-r2's session with the owner's go for the exact SHA, using the `bff-deploy` skill. **019** also waits for the owner to confirm runbook E1 (both "Spaarke AI" Registry rows uninstalled and deleted).
- **Owner go for live Dataverse changes:** 005 (run columns) and 012 (matter-summary column + `sprk_aitopicregistry` row). 019 must not deploy 005's code before its columns exist on dev.
- **Owner go for the dev App Service restart** in 039 (FR-13 live check); **owner-run** routing-eval prompts (041) and Copilot E2E sign-in (043).
- **Reviews:** uac-r2 reviews every caller-path diff (001, 006, 007, 008, 009, 022–026, 030–036, 040) and the ledger restructure in 020; ebilling-r1 reviews 008 and 033; ontology-r1 reviews the capability/authorization extractions (006, 007, 008, 031).
- **Two adversarial-verifier passes** (root §8.5): 001, 006, 007, 008, 009, 025, 030, 031, 033, 036, 040 (auth, security or tenant-isolation tags). All others: one.

## Critical Path

r1 gate → 020 → 024 → 030 → 032 → 039 → 040 → 049 → 042 / 043 → 090

Feeders that must finish before the path reaches them: 001 → 007 → 030; 005 → 030; 012 → 032; 011 → 021 → 034 → 035 → 039 and → 040; 006 → 025 → 036 → 039. The r1 gate in front of 020 is the largest schedule risk; Phase 0 can complete entirely while it is pending.

## High-Risk Items

| Risk | Tasks | Mitigation in the tasks |
|---|---|---|
| Legacy removal breaks a live caller (HandoffUrlBuilder, PlaybookAuthorizationFilter agent branch) | 001 | `HandoffUrlBuilder` relocated, not deleted; agent branch removed with Run-mode proofs moved to the execute route; FR-01 grep scoped; BFF 0 warnings + ArchTests + contract suites |
| REST bytes change during an extraction | 006, 007, 008, 031, 040 | Frozen-bytes baseline written and green before each refactor (Signals baseline pattern) |
| A tool leaks another user's data | 006, 022, 023, 025, 030, 036 | Must-not-leak and uniform not-found tests on KEEP paths; tools never copy checks; uac-r2 review; two verifier passes |
| The model sends email | 040, 049 | `spaarke_send_email` visibility `["app"]`, asserted in tests and on dev; rollback trigger in 049 |
| Run status lost on restart | 005, 030, 039 | Durable records on the run's own path; owner-approved restart check in 039 |
| Shared-file contention (widgets, ledger) | 020, 034, 035, 040 | Per-area ledger partial files (020); widget tasks serialised 034 → 035 → 040 |
| Widget bundle over budget | 021, 034, 035, 040 | `check-bundle-size.mjs` per widget (3 MB hard); escalation trigger |
| r1 gate slips | 020+ | Phase 0 runs fully in parallel; Phase 1 starts the day the gate clears |

## Parallel Execution Plan

```
Phase 0 (startable now; touches no r1 file):
  Wave 1 (parallel, 6 agents): 001, 002, 005, 006, 008, 009 — prereq: none
      — goal-eligible: NO (001/006/008/009 are auth, security or tenant-isolation; 005 has an owner-gated live schema step)
  Wave 2 (parallel, 5 agents): 004, 007, 010, 011, 012 — prereq: 001 (for 007); none for the others
      — plus 003 in the MAIN SESSION (touches .claude/) once 001 and 002 have merged
      — goal-eligible: NO (007 is auth; 012 has an owner-gated live schema step)
  Wave 3 (sequential, GATED): 019 — prereq: 001, 002, 005 (+ dev schema), 006, 007, 008, 009 + runbook E1 + owner per-SHA go
      — goal-eligible: NO (deploy; live change)

Phase 1 (BLOCKED on the r1 gate):
  Wave 4 (parallel, 2 agents): 020, 021 — prereq: r1 gate + 001 (020) / 011 (021)
      — goal-eligible: NO (2-task wave; 020 restructures an auth guard ledger)
  Wave 5 (parallel): 023, 024, 026 + 025 and 027 as separate agents; 022 after 027 — prereq: 020 (+ 009, 027 for 022; 006 for 025)
      — goal-eligible: YES for 022, 023, 024, 026 (see goal-condition below); 025 NO (tenant-isolation: two verifier passes + uac-r2 review outside the goal)
  Wave 6 (sequential, GATED): 029 — prereq: 019, 020–026 + owner per-SHA go; then the owner runs routing-eval A (041)
      — goal-eligible: NO (deploy)

Phase 2:
  Wave 7 (parallel, 5 agents): 030, 031, 033, 034, 036 — prereq: 020 + per task (005, 007, 024 → 030; 008 → 033; 010, 021 → 034; 006, 025 → 036)
      — goal-eligible: NO (030, 031, 033 are auth; 036 is tenant-isolation)
  Wave 8 (parallel, 2 agents): 032, 035 — prereq: 012, 030 (032); 021, 022, 034 (035)
      — goal-eligible: NO (2-task wave)
  Wave 9 (sequential, GATED): 039 — prereq: 029, 030–036 + owner per-SHA go (+ separate restart go); then routing-eval B
      — goal-eligible: NO (deploy, restart)

Phase 3:
  Wave 10 (sequential): 040 — prereq: 011, 021, 023, 035, 039
      — goal-eligible: NO (single task; security)
  Wave 11 (sequential, GATED): 049 — prereq: 039, 040 + owner per-SHA go; then routing-eval C
      — goal-eligible: NO (deploy)
  Wave 12 (parallel, 2 agents): 042, 043 — prereq: 049 (043 owner-run)
      — goal-eligible: NO (live dev; owner-run)
  041 spans Waves 6–12: tabulate run A after 029, B after 039, C after 049; closes after run C.

Wrap-up:
  Wave 13 (sequential): 090 — prereq: 003, 004, 041, 042, 043, 049 (step 6 in the main session)
      — goal-eligible: NO (single-task wrap-up)
```

**Wave 5 goal-condition:**

```
All of the following hold in this session:
(1) Every task in the goal (022, 023, 024, 026) shows its acceptance criteria passing via transcript
    output — "dotnet build src/server/api/Sprk.Bff.Api/ exits 0 with 0 warnings", "dotnet test" exits 0
    for tests/Spaarke.ArchTests, tests/integration/contract/Api/Mcp/**, tests/integration/auth/Mcp/**
    and tests/integration/Mcp.Conformance.Host, and each task's new tool appears in the MCP ledger
    partial it owns (RouteAuthorizationGuardTests.Mcp.{Search,Documents,Playbooks,Portfolio}.cs);
(2) Each task's Step 9.5 gates (code-review + adr-check) have been RUN and their full findings
    surfaced in the transcript, no F-class finding is open, and each task's publish-size delta and
    "dotnet list package --vulnerable --include-transitive" result are printed;
(3) git status shows only the files listed in the four POMLs' relevant-files and outputs, plus
    TASK-INDEX.md and projects/spaarkeai-mcp-bff-api-r2/notes/.
OR: a BLOCKED.md exists under projects/spaarkeai-mcp-bff-api-r2/ documenting a root-CLAUDE.md §6
    escalation, shown in transcript.
Stop after 24 turns if neither state is reached.
```

## Parallel Execution Groups

Tasks in the same group can run simultaneously once prerequisites are met.

| Group | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|-------|-------|--------------|---------------|---------------------|
| A | 001, 002, 004, 005, 006, 008, 009, 010, 011, 012 | none | 001: `Api/Agent/**` (deleted), `AgentModule`, `PlaybookAuthorizationFilter.cs`, new `Services/Ai/Handoff/HandoffUrlBuilder.cs`, `AiModule`/`AiChatModule`, ledger rows in `RouteAuthorizationGuardTests.Ledger.cs`/`.ProvenByTest.cs`, agent tests; 002: `src/solutions/CopilotAgent/**`, `infrastructure/bot-service/**`, `scripts/copilot-agent/**`, `scripts/Deploy-CopilotAgent.ps1`, one workflow, docs; 004: project notes + one deployment-guide paragraph; 005: `PlaybookOrchestrationService.cs`, new `Services/Ai/PlaybookRuns/**`, schema script; 006: `EventEndpoints.cs`, new `Services/Events/**`, `WorkspaceModule.cs`; 008: `FinanceAuthorizationFilter.cs`; 009: `RecordSearch/**` (only if a fix is needed); 010: `Spaarke.DailyBriefing.Components/**`; 011: `Spaarke.UI.Components/.../EmailComposer/**`; 012: two `scripts/dataverse/*.ps1` | ✅ Yes (Wave 1 = 001, 002, 005, 006, 008, 009; Wave 2 = 004, 010, 011, 012 with 007) |
| B | 007 | 001 | `PlaybookAuthorizationFilter.cs` (after 001's edit), new auth test | ✅ Yes (with Wave 2) |
| C | 003 | 001, 002 | `.claude/**` | Main session only |
| D | 019 | Phase 0 code tasks | dev BFF | Alone |
| E | 020, 021 | r1 gate + 001 / 011 | 020: `Api/Mcp/McpToolResults.cs`, new `McpLongRunning.cs`, `DecisionsTextSummary.cs` (comment), `RouteAuthorizationGuardTests.Mcp*.cs`, HOW-TO guide; 021: `src/client/widgets/shared/**`, `spaarke-decisions/**` | ✅ Yes (server vs client) |
| F | 022, 023, 024, 025, 026 | 020 (+ 009, 006) | Each: its own `Api/Mcp/LegalOperations/{Area}/**`, its own ledger partial, its own tests; 023 may extract from `DataverseDocumentsEndpoints.cs` | ✅ Yes |
| G | 029 | Phase 1 | dev BFF | Alone |
| H | 030, 031, 033, 034, 036 | per task | 030: `Playbooks/{Run,GetPlaybookRun}Tool.cs`; 031: `AnalysisAuthorizationFilter.cs`, `Analysis/**`; 033: `Finance/**`; 034: `Briefing/**`, `McpModule.cs`, csproj, `publish-bff-artifact.yml`, `src/client/widgets/spaarke-briefing/**`; 036: `Tasks/{Create,Complete}TaskTool.cs` | ✅ Yes (only 034 touches the widget-shared files) |
| I | 032, 035 | 030 (032); 034 (035) | 032: `MatterSummary/**`; 035: `Search/SearchResultsWidgetResource.cs`, `McpModule.cs`, csproj, `publish-bff-artifact.yml`, `spaarke-search-results/**` | ✅ Yes |
| J | 039 | Phase 2 | dev BFF (+ restart) | Alone |
| K | 040 | 035, 039 (+ 011, 021, 023) | `Communication*AuthorizationFilter.cs`, `Email/**`, `McpModule.cs`, csproj, `publish-bff-artifact.yml`, `spaarke-email-compose/**` | Alone |
| L | 041 | 029 / 039 / 049 | project notes; tool description strings only | ✅ Yes (between deploys) |
| M | 042, 043 | 049 | project notes (+ probe script) | ✅ Yes |
| N | 049 | 039, 040 | dev BFF | Alone |
| O | 090 | all | project docs, architecture doc, `.claude/patterns/CAPABILITY-INDEX.md` (main session) | Alone |
