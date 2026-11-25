# Decisions — spaarkeai-mcp-bff-api-r2

> Full decision log. `CLAUDE.md` §3 keeps the one-line binding outcome with a link here. Newest first. Superseded entries stay, marked superseded with the date.

## 2026-11-25 — Planning decisions (project-pipeline Step 3)

- **`HandoffUrlBuilder` is relocated, not deleted.** It is injected by `Services/Ai/Handlers/DataverseCreateRecordHandler.cs:229` (chat create-record tool) and used by many data-mutation tests; MCP only mentions it in a comment. Task 001 moves it out of `Api/Agent/` and re-registers it before deleting `AgentModule`.
- **Per-area MCP ledger partial files** (task 020) so Phase 1–3 tool tasks each own one ledger file and can run in parallel. Needs uac-r2 review of the ledger restructure.
- **Widget tasks that touch `McpModule.cs`, `Sprk.Bff.Api.csproj` and `publish-bff-artifact.yml` are serialised** (034 → 035 → 040).
- **CAPABILITY-INDEX rows** are proposed in `notes/capability-index-pending.md` by sub-agent tasks and applied by the main session (sub-agents cannot write `.claude/`).
- **The `AgentEndpoints` constants are not moved first** (spec FR-01 says to). Their only users are the agent branch of `PlaybookAuthorizationFilter.AuthorizeRunAsync` (`:569-588`) and an agent-route contract test; task 001 removes that branch and moves a constant only if a non-agent caller exists.
- **Two authorization extractions the spec did not list as prerequisites** are folded into the tool tasks: 031 exposes `AnalysisAuthorizationFilter`'s create check (POST `/api/ai/analysis/create`) and 040 exposes the `CommunicationAuthorizationFilter` + `CommunicationRecordAuthorizationFilter` Send checks (POST `/api/communications/send`) as shared methods, each behind a frozen-bytes baseline. Both are raised to opus with two verifier passes. Reason: the spec forbids tools copying authorization, and both routes hold their checks in filters.
- **Matter summary start path** (032) reuses 030's run start/status rather than `InsightsPlaybookExecutionCache.GetOrExecuteAsync`, which runs synchronously and cannot meet the 30 s rule; freshness = the 012 column's date vs the registry TTL. Escalation if 030's path cannot target the matter-summary Binding.
- **Shared widget transport placement**: `src/client/widgets/shared/mcp/` (source folder, not a package, not `@spaarke/ui-components`); escalate as an ADR conflict if adr-check flags ADR-012.
- **Routing eval (FR-17) runs at the deploy points**: run A after 029 (≈10 model-visible tools), run B after 039 (≈15–18), run C after 049 (all). The exact model-visible count is recorded at each run.

## 2026-11-25 — Owner decisions (spec interview and pipeline)

See spec.md "Owner Clarifications" for the full table; design.md §9 for Q1–Q8.
