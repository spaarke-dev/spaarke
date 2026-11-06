# Task Index — spaarkeai-mcp-bff-api-r1

> **Project**: Spaarke in Microsoft Copilot and Cowork — MCP server in the BFF (r1)
> **Created**: 2026-11-06 (task-create, project-pipeline Step 3)
> **Total tasks**: 28 (27 implementation + 1 wrap-up)
> **Status tokens** (task-create Step 5): 🔲 `[open]` · 🔄 `[wip]` · ✅ `[done]` · ⚠️ `[escalated]` · 🟡 `[blocked]` · 🚫 `[cancelled]` · ⏸️ `[deferred]`. The bracketed token is mandatory in every status cell (FAILURE-MODES G-16). After any status change run `pwsh scripts/check-task-status-drift.ps1`.

## Task Registry

| ID | Title | Phase | Status | Dependencies | Parallel | Model / effort | Rigor |
|-----|-------|-------|--------|--------------|----------|----------------|-------|
| 001 | Spike MCP server, echo tool and test widget | 0 | 🔲 [open] | none | Group A | sonnet / high | FULL |
| 002 | A10 connector client, OAuth and connector registration | 0 | 🔲 [open] | 001 | Group B | opus / high | STANDARD |
| 003 | Run and record Phase 0 tests T0–T14, E1–E5 | 0 | 🔲 [open] | 001, 002 | Group C | sonnet / high | STANDARD |
| 010 | B-4a ICallerContext and Dataverse client | A | 🔲 [open] | none | Group A | opus / high | FULL |
| 011 | Contract baseline for signal, plan and decision routes | A | 🔲 [open] | none | Group A | sonnet / high | FULL |
| 012 | B-4b thread ICallerContext through decision slice | A | 🔲 [open] | 010, 011 | Group B | opus / high | FULL |
| 017 | B-2 one decision result mapper | A | 🔲 [open] | 011, 012 | Group C | opus / high | FULL |
| 013 | B-1 decision capability: list and plan | A | 🔲 [open] | 011, 017 | Group D | opus / high | FULL |
| 014 | B-1 decision capability: commit | A | 🔲 [open] | 013 | Group E | opus / high | FULL |
| 015 | B-5 slim summary, privilege flag and paging cursor | A | 🔲 [open] | 013, 014 | Group F | sonnet / high | FULL |
| 016 | B-3 decision lookup search and REST route | A | 🔲 [open] | 013 | Group E | opus / high | FULL |
| 020 | MCP module, packages and /mcp/{area} host | B | 🔲 [open] | 003, 017 | Group D | sonnet / high | FULL |
| 021 | A10 auth on /mcp, tool policies and route guard | B | 🔲 [open] | 020 | Group E | opus / high | FULL |
| 022 | spaarke_show_decisions and app-only paging tool | B | 🔲 [open] | 015, 017, 021 | Group G | opus / high | FULL |
| 023 | spaarke_get_decision_plan and lookup search tools | B | 🔲 [open] | 016, 017, 021 | Group F | sonnet / high | FULL |
| 024 | spaarke_complete_decision app-only write tool | B | 🔲 [open] | 014, 017, 021 | Group F | sonnet / high | FULL |
| 025 | MCP conformance in CI and NFR measurements | B | 🔲 [open] | 022, 023, 024 | Group H | sonnet / high | FULL |
| 030 | Widget scaffold, BFF embedding and CI size gate | C | 🔲 [open] | 003, 021 | Group F | sonnet / high | FULL |
| 031 | Widget Decide lane over MCP shims | C | 🔲 [open] | 022, 030 | Group H | sonnet / high | FULL |
| 032 | Widget Decision wizard over MCP shims | C | 🔲 [open] | 023, 024, 031 | Group I | sonnet / xhigh | FULL |
| 040 | Plugin package and spaarke-decisions skill | D | 🔲 [open] | 003, 030 | Group G | sonnet / high | STANDARD |
| 041 | Dev BFF deploy (gated on ontology pair) | D | 🔲 [open] | 025, 032 | Group J (alone) | sonnet / high | STANDARD |
| 043 | Spaarke AI agent fallback on MCP (if T1 fails) | D | 🔲 [open] | 003, 041 | Group K (alone) | sonnet / high | STANDARD |
| 042 | Connector, runbooks and live E2E in Cowork and Chat | D | 🔲 [open] | 040, 041, 043 | Group L (alone) | sonnet / high | STANDARD |
| 050 | Root CLAUDE.md §1.1 row and CHANGELOG | E | 🔲 [open] | 042 | Group M (main session) | sonnet / high | STANDARD |
| 051 | Rewrite mcp-tool-handler; update widget and DA skills | E | 🔲 [open] | 042, 043, 050 | Group M (main session) | sonnet / high | STANDARD |
| 052 | MCP architecture doc and add-a-tool guide | E | 🔲 [open] | 042 | Group M | sonnet / high | STANDARD |
| 090 | Project wrap-up | Wrap-up | 🔲 [open] | 050, 051, 052 | Group N (alone) | sonnet / high | STANDARD |

Rows are listed in execution order within each phase (017 runs before 013 in Phase A). Task 043 is conditional: task 003 sets it to 🚫 `[cancelled]` if Phase 0 test T1 shows the widget renders in Copilot Chat through the connector; a cancelled 043 satisfies the dependencies of 042 and 051.

## Gates and external conditions

- **T5 (task 003)** gates everything that depends on connector auth. A T5 failure stops the project and raises a follow-up ADR-028 amendment (FR-19).
- **T0 (task 003)** gates 020 and everything after it; **T13** fixes 020's route mechanism; **Size** fixes 030's budget; **T1** decides 043.
- **041 dev deploy** is BLOCKED until ontology's #2580/#2579 pair has deployed to dev AND the owner gives a go for the exact SHA (ontology condition 1). Phases A–C build and test locally until then.
- **Owner-run steps**: 002, 003, 042 and 043 need the owner (Global Admin) for admin-center actions; client-secret writes need human confirmation (root CLAUDE.md §9).
- **Reviews**: ontology-platform reviews every PR touching `Services/Signals/**`, `Api/Signals/**` or the worklist route (012–017); unified-access-control-r2 reviews 010 and 021 (FR-02's four checks); 010 and 021 get two adversarial passes.

## Critical Path

010 → 012 → 017 → 013 → 014 → 015 → 022 → 031 → 032 → 041 → 043 → 042 → 050 → 051 → 090

Parallel feeders: 001 → 002 → 003 → 020 → 021 (must finish before 022/023/024/030); 016 → 023; 024 → 032; 022/023/024 → 025 → 041; 030 → 040 → 042.

## High-Risk Items

| Risk | Tasks | Mitigation in the tasks |
|---|---|---|
| R2 — A10 OAuth through the connector fails (T5) | 002, 003 | First live test; escalation trigger stops the project and proposes the ADR-028 follow-up |
| Byte-for-byte commit invariants through B-1 | 011, 014 | Golden baselines first (011); 014 is opus, prescriptive, lists every invariant with before/after file:line; ontology review |
| R10 — `HttpContext` beyond the spec's list | 010, 012, 014 | B-4 split; ArchTest guard with ratchet baseline emptied by 014 |
| R12 — privilege flag not in the list projection | 015, 022 | 015 adds the column (projection only); 022's marker-string negative test with a seeding proof |
| R1 — no widget in Chat through the connector | 003, 043 | T1 decides; FR-17 agent fallback; FR-14 text path in 022 |
| R5 — `Microsoft.Extensions.AI` bump regresses AI | 020 | AI unit + integration suites before merge (NFR-08); escalation on any failure |
| R3 — widget bundle over budget | 030, 031, 032 | CI size gate (fail above 3 MB); `decisionDraft.ts` re-render fallback via escalation |
| R11 — deploy gate on ontology's pair | 041 | Task stays blocked until both gate conditions are recorded |
| R7 — contention on `AuthorizationModule.cs` and `Services/Signals/**` | 012–017, 021 | `/conflict-check` before each BFF PR; file ownership per wave (below) |

## Parallel Execution Plan

```
Phase 0 + A (Wave 1 starts both):
  Wave 1 (parallel, 3 agents): 001, 010, 011 — prereq: none
      — goal-eligible: NO (010 is auth-tagged; 001 involves a deploy hand-off)
  Wave 2 (parallel, 2 agents): 002, 012 — prereq: 001 (for 002); 010, 011 (for 012)
      — goal-eligible: NO (2-task wave; 002 is auth and owner-run)
  Wave 3 (parallel, 2 agents): 003, 017 — prereq: 001, 002 (for 003); 011, 012 (for 017)
      — goal-eligible: NO (2-task wave; 003 is live owner testing)
  Wave 4 (parallel, 2 agents): 013, 020 — prereq: 017 (both); 003 (for 020)
      — goal-eligible: NO (2-task wave)
  Wave 5 (parallel, 3 agents): 014, 016, 021 — prereq: 013 (014, 016); 020 (021)
      — goal-eligible: NO (021 is auth-tagged; 014 is a high-risk invariant migration)
  Wave 6 (parallel, 4 agents): 015, 023, 024, 030 — prereq: Wave 5 complete
      — goal-eligible: YES (see goal-condition below)
  Wave 7 (parallel, 2 agents): 022, 040 — prereq: 015, 021 (022); 003, 030 (040)
      — goal-eligible: NO (2-task wave)
  Wave 8 (parallel, 2 agents): 025, 031 — prereq: 022, 023, 024 (025); 022, 030 (031)
      — goal-eligible: NO (2-task wave)
  Wave 9 (sequential): 032 — prereq: 023, 024, 031
      — goal-eligible: NO (single-task wave)
  Wave 10 (sequential, GATED): 041 — prereq: 025, 032 + ontology pair deployed + owner per-SHA go
      — goal-eligible: NO (deploy; irreversible live change)
  Wave 11 (sequential, conditional): 043 — prereq: 003 (T1 failed), 041
      — goal-eligible: NO (single-task, deploy, owner-run)
  Wave 12 (sequential): 042 — prereq: 040, 041, 043 (done or cancelled)
      — goal-eligible: NO (single-task, live tenant, owner-run)
  Wave 13: 052 (sub-agent) in parallel with 050 then 051 (main session, sequential) — prereq: 042 (+ 043, 050 for 051)
      — goal-eligible: NO (050/051 touch .claude/ and root CLAUDE.md — main-session-only)
  Wave 14 (sequential): 090 — prereq: 050, 051, 052
      — goal-eligible: NO (single-task wrap-up)
```

**Wave 6 goal-condition:**

```
All of the following hold in this session:
(1) Every task in Wave 6 (015, 023, 024, 030) shows its acceptance criteria passing via transcript
    output — "dotnet build src/server/api/Sprk.Bff.Api/ exits 0", "dotnet test" for the touched test
    projects exits 0, and in src/client/widgets/spaarke-decisions "npm test" passes and
    "npm run build" passes its bundle-size check;
(2) Each task's Step 9.5 gates (code-review + adr-check) have been RUN and their full findings
    surfaced in the transcript, and each task's publish-size delta is printed;
(3) git status shows only the wave's expected file changes (the files listed in the four POMLs'
    relevant-files and outputs, plus TASK-INDEX.md and notes/).
OR: a BLOCKED.md exists under projects/spaarkeai-mcp-bff-api-r1/ documenting a root-CLAUDE.md §6
    escalation, shown in transcript.
Stop after 24 turns if neither state is reached.
```

## Parallel Execution Groups

Tasks in the same group can run simultaneously once prerequisites are met.

| Group | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|-------|-------|--------------|---------------|---------------------|
| A | 001, 010, 011 | none | 001: spike worktree + `notes/spikes/`; 010: `Infrastructure/Authentication/*`, `DataverseUserClient.cs`, `CallerWhoAmIMemo.cs`, `SpaarkeCore.cs`, `MiddlewarePipelineExtensions.cs`; 011: `tests/integration/contract/Api/Signals/*` | ✅ Yes |
| B | 002, 012 | 001 ✅ (002); 010, 011 ✅ (012) | 002: notes only (owner-run); 012: seven `Services/Signals` files, forced call sites, new ArchTest guard | ✅ Yes |
| C | 003, 017 | 001, 002 ✅ (003); 011, 012 ✅ (017) | 003: `notes/phase0-results.md` (+ 043 status if cancelled); 017: `Api/Signals/*Endpoints.cs`, new mapper | ✅ Yes |
| D | 013, 020 | 017 ✅; 003 ✅ (020) | 013: `IDecisionCapability.cs`, `DecisionCapability.cs`, `SignalReadEndpoints.cs`, `DecisionPlanEndpoints.cs`, `SignalsModule.cs`; 020: csproj, `McpModule.cs`, `Api/Mcp/*`, `RateLimitingModule.cs`, `Program.cs`, `EndpointMappingExtensions.cs` | ✅ Yes |
| E | 014, 016, 021 | 013 ✅ (014, 016); 020 ✅ (021) | 014: `DecisionCommitService.cs`, `DecisionCapability.Commit.cs`, `IDecisionCapability.cs`, `DecisionCommitEndpoints.cs`, guard baseline; 016: lookup service files, `SignalsModule.cs`, `DecisionPlanEndpoints.cs`; 021: `AuthorizationModule.cs`, `McpEndpoints.cs`, `McpToolAuthorization.cs`, `RouteAuthorizationGuardTests.cs` | ✅ Yes |
| F | 015, 023, 024, 030 | Wave 5 ✅ | 015: `SignalReadService.cs`, `WorklistItemSummary.cs`, capability files, `DecisionResultMapper.cs`; 023/024: own tool files; 030: widget folder, csproj, widget resource, two CI workflows | ✅ Yes |
| G | 022, 040 | 015, 021 ✅ (022); 003, 030 ✅ (040) | 022: own tool files, parity fixture, jest parity test; 040: `src/solutions/CopilotPlugins/**`, `client-tests.yml` | ✅ Yes |
| H | 025, 031 | 022, 023, 024 ✅ (025); 022, 030 ✅ (031) | 025: conformance host, `ci-tier1-blocking.yml`, `McpHostContractTests.cs`; 031: shared-lib Worklist seams, widget lane files | ✅ Yes |
| I | 032 | 023, 024, 031 ✅ | widget wizard files, DecisionWizard seams | — single task |
| J | 041 | 025, 032 ✅ + deploy gate | dev BFF | ❌ No — live deploy, runs alone |
| K | 043 | 003 (T1 failed), 041 ✅ | `src/solutions/CopilotAgent/**` | ❌ No — owner-run deploy, runs alone |
| L | 042 | 040, 041, 043 ✅ | runbook + live evidence notes | ❌ No — live tenant, runs alone |
| M | 050, 051, 052 | 042 ✅ | 050/051: root `CLAUDE.md`, `.claude/**` (main session, 050 before 051); 052: `docs/**` | ⚠️ 052 only — 050 and 051 are main-session-only (root CLAUDE.md §3) |
| N | 090 | 050, 051, 052 ✅ | project files | ❌ No — final task |

**How to execute a group:** confirm all prerequisites are complete → invoke the Agent tool with one task-execute subagent per task in ONE message (model = the task's `<model-tier>`, effort = its `<effort>`) → wait for all to finish before the next group. Tasks 050 and 051 never go to a sub-agent.
