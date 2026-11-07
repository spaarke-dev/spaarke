# Task Index — spaarkeai-mcp-bff-api-r1

> **Project**: Spaarke in Microsoft Copilot and Cowork — MCP server in the BFF (r1)
> **Created**: 2026-11-06 (task-create, project-pipeline Step 3)
> **Re-planned**: 2026-11-07 — owner option C defers `ICallerContext` (spec FR-10 amended; `notes/decisions.md` 2026-11-07): 010 becomes the MCP caller-path verification, 012 is cancelled, and 017 no longer waits on 012.
> **Total tasks**: 28 (27 implementation + 1 wrap-up; 012 cancelled)
> **Status tokens** (task-create Step 5): 🔲 `[open]` · 🔄 `[wip]` · ✅ `[done]` · ⚠️ `[escalated]` · 🟡 `[blocked]` · 🚫 `[cancelled]` · ⏸️ `[deferred]`. The bracketed token is mandatory in every status cell (FAILURE-MODES G-16). After any status change run `pwsh scripts/check-task-status-drift.ps1`.

## Task Registry

| ID | Title | Phase | Status | Dependencies | Parallel | Model / effort | Rigor |
|-----|-------|-------|--------|--------------|----------|----------------|-------|
| 001 | Spike MCP server, echo tool and test widget | 0 | 🔲 [open] | none | Group A | sonnet / high | FULL |
| 002 | A10 connector client, OAuth and connector registration | 0 | 🔲 [open] | 001 | Group B | opus / high | STANDARD |
| 003 | Run and record Phase 0 tests T0–T14, E1–E5 | 0 | 🔲 [open] | 001, 002 | Group C | sonnet / high | STANDARD |
| 011 | Contract baseline for signal, plan and decision routes | A | ✅ [done] | none | Group A | sonnet / high | FULL |
| 012 | B-4b thread ICallerContext through decision slice — cancelled 2026-11-07 (owner option C: `ICallerContext` deferred; MCP tools use the request `HttpContext`) | A | 🚫 [cancelled] | — | — | opus / high | FULL |
| 017 | B-2 one decision result mapper | A | ✅ [done] | 011 | Group A | opus / high | FULL |
| 013 | B-1 decision capability: list and plan | A | ✅ [done] | 011, 017 | Group B | opus / high | FULL |
| 014 | B-1 decision capability: commit | A | ✅ [done] | 013 | Group C | opus / high | FULL |
| 016 | B-3 decision lookup search and REST route | A | 🔲 [open] | 013 | Group C | opus / high | FULL |
| 015 | B-5 slim summary, privilege flag and paging cursor | A | 🔲 [open] | 013, 014 | Group D | sonnet / high | FULL |
| 020 | MCP module, packages and /mcp/{area} host | B | 🔲 [open] | 003, 017 | Group D | sonnet / high | FULL |
| 021 | A10 auth on /mcp, tool policies and route guard | B | 🔲 [open] | 020 | Group E (alone) | opus / high | FULL |
| 022 | spaarke_show_decisions and app-only paging tool | B | 🔲 [open] | 015, 017, 021 | Group F | opus / high | FULL |
| 023 | spaarke_get_decision_plan and lookup search tools | B | 🔲 [open] | 016, 017, 021 | Group F | sonnet / high | FULL |
| 024 | spaarke_complete_decision app-only write tool | B | 🔲 [open] | 014, 017, 021 | Group F | sonnet / high | FULL |
| 010 | MCP caller-path verification (option C) | B | 🔲 [open] | 020, 021, 023, 024 | Group G | opus / high | FULL |
| 025 | MCP conformance in CI and NFR measurements | B | 🔲 [open] | 010, 022, 023, 024 | Group H | sonnet / high | FULL |
| 030 | Widget scaffold, BFF embedding and CI size gate | C | 🔲 [open] | 003, 021 | Group F | sonnet / high | FULL |
| 031 | Widget Decide lane over MCP shims | C | 🔲 [open] | 022, 030 | Group G | sonnet / high | FULL |
| 032 | Widget Decision wizard over MCP shims | C | 🔲 [open] | 023, 024, 031 | Group H | sonnet / xhigh | FULL |
| 040 | Plugin package and spaarke-decisions skill | D | 🔲 [open] | 003, 030 | Group G | sonnet / high | STANDARD |
| 041 | Dev BFF deploy (gated on ontology pair) | D | 🔲 [open] | 025, 032 | Group I (alone) | sonnet / high | STANDARD |
| 043 | Spaarke AI agent fallback on MCP (if T1 fails) | D | 🔲 [open] | 003, 041 | Group J (alone) | sonnet / high | STANDARD |
| 042 | Connector, runbooks and live E2E in Cowork and Chat | D | 🔲 [open] | 040, 041, 043 | Group K (alone) | sonnet / high | STANDARD |
| 050 | Root CLAUDE.md §1.1 row and CHANGELOG | E | 🔲 [open] | 042 | Group L (main session) | sonnet / high | STANDARD |
| 051 | Rewrite mcp-tool-handler; update widget and DA skills | E | 🔲 [open] | 042, 043, 050 | Group L (main session) | sonnet / high | STANDARD |
| 052 | MCP architecture doc and add-a-tool guide | E | 🔲 [open] | 042 | Group L | sonnet / high | STANDARD |
| 090 | Project wrap-up | Wrap-up | 🔲 [open] | 050, 051, 052 | Group M (alone) | sonnet / high | STANDARD |

Rows are listed in execution order within each phase (017 runs before 013 in Phase A). Task 010 keeps its id but is now a Phase B verification task: it needs the MCP host and tools (020, 021, 023, 024) and must finish before 025. Task 012 is cancelled (owner option C, 2026-11-07) and satisfies no dependency; nothing depends on it any more. Task 043 is conditional: task 003 sets it to 🚫 `[cancelled]` if Phase 0 test T1 shows the widget renders in Copilot Chat through the connector; a cancelled 043 satisfies the dependencies of 042 and 051.

## Gates and external conditions

- **T5 (task 003)** gates everything that depends on connector auth. A T5 failure stops the project and raises a follow-up ADR-028 amendment (FR-19).
- **T0 (task 003)** gates 020 and everything after it; **T13** fixes 020's route mechanism; **Size** fixes 030's budget; **T1** decides 043.
- **041 dev deploy** is BLOCKED until ontology's #2580/#2579 pair has deployed to dev AND the owner gives a go for the exact SHA (ontology condition 1). Phases A–C build and test locally until then.
- **Owner-run steps**: 002, 003, 042 and 043 need the owner (Global Admin) for admin-center actions; client-secret writes need human confirmation (root CLAUDE.md §9).
- **Reviews**: ontology-platform reviews every PR touching `Services/Signals/**`, `Api/Signals/**` or the worklist route (013–017); unified-access-control-r2 reviews 010 and 021 (FR-02's four checks); 010 and 021 get two adversarial passes.
- **Option C premise (task 010)**: if 010 shows that an MCP tool does not see the request `HttpContext` (null accessor, different instance, or services other than `RequestServices`), work stops and the owner re-decides FR-10. No `ICallerContext` or app-only fallback is added on the fly.

## Critical Path

011 ✅ → 017 → 013 → 014 → 015 → 022 → 031 → 032 → 041 → 043 → 042 → 050 → 051 → 090

Parallel feeders (equally long, so any slip here moves the end date): 001 → 002 → 003 → 020 → 021 (must finish before 022/023/024/030); 014 → 024 → 010 → 025 → 041; 016 → 023 → 010; 024 → 032; 030 → 040 → 042.

## High-Risk Items

| Risk | Tasks | Mitigation in the tasks |
|---|---|---|
| R2 — A10 OAuth through the connector fails (T5) | 002, 003 | First live test; escalation trigger stops the project and proposes the ADR-028 follow-up |
| Byte-for-byte commit invariants through B-1 | 011, 014 | Golden baselines first (011); 014 is opus, prescriptive, lists every invariant with before/after file:line, and leaves `DecisionCommitService.cs` unedited (option C); ontology review |
| R10 — MCP tools must see the caller's request context (option C replaces the `ICallerContext` refactor) | 010, 013, 014, 021–024 | Decision services keep their `HttpContext`/`ClaimsPrincipal` parameters; tools read `IHttpContextAccessor` and fail closed with `UserContextRequired`; 010 proves accessor = request context and services = `RequestServices`, WhoAmI = 1 per MCP commit and plan-stamp continuity, and pins stateless + `PerSessionExecutionContext=false` (seeding proof); 010 blocks 025 |
| R12 — privilege flag not in the list projection | 015, 022 | 015 adds the column (projection only); 022's marker-string negative test with a seeding proof |
| R1 — no widget in Chat through the connector | 003, 043 | T1 decides; FR-17 agent fallback; FR-14 text path in 022 |
| R5 — `Microsoft.Extensions.AI` bump regresses AI | 020 | AI unit + integration suites before merge (NFR-08); escalation on any failure |
| R3 — widget bundle over budget | 030, 031, 032 | CI size gate (fail above 3 MB); `decisionDraft.ts` re-render fallback via escalation |
| R11 — deploy gate on ontology's pair | 041 | Task stays blocked until both gate conditions are recorded |
| R7 — contention on `AuthorizationModule.cs` and `Services/Signals/**` | 013–017, 021 | `/conflict-check` before each BFF PR; file ownership per wave (below) |

## Parallel Execution Plan

```
Phase 0 + A (Wave 1 starts both):
  Wave 1 (parallel, 2 agents): 001, 017 — prereq: none (001); 011 ✅ (017)   [011 already done]
      — goal-eligible: NO (001 involves a deploy hand-off; 2-task wave)
  Wave 2 (parallel, 2 agents): 002, 013 — prereq: 001 (for 002); 011, 017 (for 013)
      — goal-eligible: NO (2-task wave; 002 is auth and owner-run)
  Wave 3 (parallel, 3 agents): 003, 014, 016 — prereq: 001, 002 (for 003); 013 (for 014, 016)
      — goal-eligible: NO (003 is live owner testing; 014 is a high-risk invariant migration)
  Wave 4 (parallel, 2 agents): 015, 020 — prereq: 013, 014 (for 015); 003, 017 (for 020)
      — goal-eligible: NO (2-task wave)
  Wave 5 (sequential): 021 — prereq: 020
      — goal-eligible: NO (single-task wave; auth-tagged)
  Wave 6 (parallel, 4 agents): 022, 023, 024, 030 — prereq: Wave 5 complete (+ 015 for 022, 016 for 023, 014 for 024, 003 for 030)
      — goal-eligible: YES (see goal-condition below)
  Wave 7 (parallel, 3 agents): 010, 031, 040 — prereq: 020, 021, 023, 024 (010); 022, 030 (031); 003, 030 (040)
      — goal-eligible: NO (010 is auth-tagged and needs uac-r2 review)
  Wave 8 (parallel, 2 agents): 025, 032 — prereq: 010, 022, 023, 024 (025); 023, 024, 031 (032)
      — goal-eligible: NO (2-task wave)
  Wave 9 (sequential, GATED): 041 — prereq: 025, 032 + ontology pair deployed + owner per-SHA go
      — goal-eligible: NO (deploy; irreversible live change)
  Wave 10 (sequential, conditional): 043 — prereq: 003 (T1 failed), 041
      — goal-eligible: NO (single-task, deploy, owner-run)
  Wave 11 (sequential): 042 — prereq: 040, 041, 043 (done or cancelled)
      — goal-eligible: NO (single-task, live tenant, owner-run)
  Wave 12: 052 (sub-agent) in parallel with 050 then 051 (main session, sequential) — prereq: 042 (+ 043, 050 for 051)
      — goal-eligible: NO (050/051 touch .claude/ and root CLAUDE.md — main-session-only)
  Wave 13 (sequential): 090 — prereq: 050, 051, 052
      — goal-eligible: NO (single-task wrap-up)

  012: cancelled 2026-11-07 (owner option C) — runs in no wave.
```

**Wave 6 goal-condition:**

```
All of the following hold in this session:
(1) Every task in Wave 6 (022, 023, 024, 030) shows its acceptance criteria passing via transcript
    output — "dotnet build src/server/api/Sprk.Bff.Api/ exits 0", "dotnet test" for the touched test
    projects exits 0, the laneCountFilters parity jest test passes in
    src/client/shared/Spaarke.UI.Components, and in src/client/widgets/spaarke-decisions "npm test"
    passes and "npm run build" passes its bundle-size check;
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
| A | 001, 011 ✅, 017 | none (001, 011); 011 ✅ (017) | 001: spike worktree + `notes/spikes/`; 011: `tests/integration/contract/Api/Signals/*` (done); 017: `Api/Signals/*Endpoints.cs`, new mapper | ✅ Yes |
| B | 002, 013 | 001 ✅ (002); 011, 017 ✅ (013) | 002: notes only (owner-run); 013: `IDecisionCapability.cs`, `DecisionCapability.cs`, `SignalReadEndpoints.cs`, `DecisionPlanEndpoints.cs`, `SignalsModule.cs` | ✅ Yes |
| C | 003, 014, 016 | 001, 002 ✅ (003); 013 ✅ (014, 016) | 003: `notes/phase0-results.md` (+ 043 status if cancelled); 014: `DecisionCapability.Commit.cs`, `IDecisionCapability.cs`, `DecisionCommitEndpoints.cs` (`DecisionCommitService.cs` not edited); 016: lookup service files, `SignalsModule.cs`, `DecisionPlanEndpoints.cs` | ✅ Yes |
| D | 015, 020 | 013, 014 ✅ (015); 003, 017 ✅ (020) | 015: `SignalReadService.cs`, `WorklistItemSummary.cs`, capability files, `DecisionResultMapper.cs`; 020: csproj, `McpModule.cs`, `Api/Mcp/*`, `RateLimitingModule.cs`, `Program.cs`, `EndpointMappingExtensions.cs` | ✅ Yes |
| E | 021 | 020 ✅ | `AuthorizationModule.cs`, `McpEndpoints.cs`, `McpToolAuthorization.cs`, `RouteAuthorizationGuardTests.cs` | — single task (auth) |
| F | 022, 023, 024, 030 | Wave 5 ✅ (+ 015, 016, 014, 003 as listed) | 022: own tool files, parity fixture, jest parity test; 023/024: own tool files; 030: widget folder, csproj, widget resource, two CI workflows | ✅ Yes |
| G | 010, 031, 040 | 020, 021, 023, 024 ✅ (010); 022, 030 ✅ (031); 003, 030 ✅ (040) | 010: test files only (`tests/integration/{auth,data-mutation}/Mcp/*`); 031: shared-lib Worklist seams, widget lane files; 040: `src/solutions/CopilotPlugins/**`, `client-tests.yml` | ✅ Yes |
| H | 025, 032 | 010, 022, 023, 024 ✅ (025); 023, 024, 031 ✅ (032) | 025: conformance host, `ci-tier1-blocking.yml`, `McpHostContractTests.cs`; 032: widget wizard files, DecisionWizard seams | ✅ Yes |
| I | 041 | 025, 032 ✅ + deploy gate | dev BFF | ❌ No — live deploy, runs alone |
| J | 043 | 003 (T1 failed), 041 ✅ | `src/solutions/CopilotAgent/**` | ❌ No — owner-run deploy, runs alone |
| K | 042 | 040, 041, 043 ✅ | runbook + live evidence notes | ❌ No — live tenant, runs alone |
| L | 050, 051, 052 | 042 ✅ | 050/051: root `CLAUDE.md`, `.claude/**` (main session, 050 before 051); 052: `docs/**` | ⚠️ 052 only — 050 and 051 are main-session-only (root CLAUDE.md §3) |
| M | 090 | 050, 051, 052 ✅ | project files | ❌ No — final task |

**How to execute a group:** confirm all prerequisites are complete → invoke the Agent tool with one task-execute subagent per task in ONE message (model = the task's `<model-tier>`, effort = its `<effort>`) → wait for all to finish before the next group. Tasks 050 and 051 never go to a sub-agent.
