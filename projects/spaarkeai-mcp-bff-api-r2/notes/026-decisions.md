# Task 026 — spaarke_get_portfolio (FR-08)

## Escalation check (result: no escalation)

The REST route's access check is **not** policy logic hidden in a private method. `GET /api/workspace/portfolio` is
`.RequireAuthorization()` plus `WorkspaceAuthorizationFilter`, and the filter only calls `CallerResolution.ResolveObjectId(HttpContext.User)`
(a public static) and answers 401 when the oid is missing. Matter-level access lives in `PortfolioService` (people-targeted set, every
row read as the caller through `IImpersonatedCommunicationQuery`). So the tool makes the same `CallerResolution.ResolveObjectId` call on
`IHttpContextAccessor.HttpContext.User` and calls the same service: no authorization logic is copied, and no shared-method extraction is
needed. (GetPortfolio reads `Items["UserId"]` first, which the filter fills from that same call; the tool has no filter, so it calls the
resolver directly.)

## Decisions

- **Reason codes.** The REST route returns a plain ProblemDetails (401 / 500) with no `reasonCode`. The tool uses
  `DATAVERSE_USER_CONTEXT_REQUIRED` (401, the platform's existing no-caller code), `workspace.portfolio.unavailable` (500, a service
  fault) and `workspace.portfolio.invalid_parameter` (400, any argument). The last two are new, local to the tool, and follow the
  `{slice}.{area}.{reason}` shape of the other slices' codes. Nothing else in the vocabulary changes.
- **No arguments.** The schema is `{"type":"object","properties":{},"additionalProperties":false}` and `RunAsync` refuses any argument with
  the 400 result, so an invented argument is never silently ignored (same stance as the decision tools). `DecisionToolCall.TryReadSingleArgument`
  needs one named argument, so the zero-argument check is written inline (3 lines).
- **Shared helpers reused, not copied:** `DecisionToolCall.LogAroundAsync` (one telemetry line: tool, outcome, rows, ms, correlation id) and
  `DecisionToolCall.Success`. They are `internal` to the assembly and live in the Decisions folder; moving them to `Api/Mcp/` is a change to
  r1's files and sibling tools' imports, so it is left for a cleanup (see "Not done").
- **Text.** Five lines, counts and totals only, invariant culture: active matters, at risk, overdue tasks, spend of budget, utilization.
  No matter names (ADR-015); the test seeds a matter named "Confidential Matter Alpha" and asserts it appears nowhere.
- **Fault handling.** The REST route logs and answers a 500; the tool logs the exception (server side only) and answers an `isError`
  result with fixed text. The service's own graceful empty state (unresolvable caller, failed read: an empty portfolio) is the same on both
  hosts and is not turned into an error here (parity wins; it is also what the Console shows).
- **Result size.** One record of seven numbers (a few hundred bytes); no size test added (justified: the shape cannot grow).

## Test scope justification (acceptance criterion 4)

Beyond the stated contract (parity, no-caller, no widget) the suite adds: both call orders for parity (cache hit vs miss), caller
isolation (two tokens, two different answers, every Dataverse read made as the asking caller's systemuserid), the unresolvable-caller
empty answer equal to REST, the 400 for an argument, the service-fault result with no leaked exception text, and the one telemetry line.
Each maps to a rule in the task's constraints (ADR-028 caller path, ADR-015, the telemetry rule); none tests PortfolioService's
aggregation (PortfolioServiceTests owns it).

## Pre-existing pin that this task (and every other new tool) breaks

`tests/integration/contract/Api/Mcp/McpHostContractTests.cs` `RealBffHost_ToolsList_WhenEnabledWithAValidToken_IsExactlyTheFiveV1Tools`
pins the tool set at exactly five names. It fails once any new tool exists. Tasks 023, 024, 026 and 027 run in parallel and all add
tools, so each editing that list would conflict. NOT edited here (wave rule: own area only). The main session should update the
expected set once, after the wave merges.

## Gates (2026-11-25)

- **Tests.** New: 12/12 pass (`GetPortfolioToolTests`). Before-state proof: with the argument refusal and the no-caller guard mutated out of the tool, 3 of the 12 fail (the argument test and both no-caller theory rows); restored, 12/12. Wider run, `FullyQualifiedName~Mcp|FullyQualifiedName~Workspace` in Sprk.Bff.Api.Tests: 789 passed, 1 skipped, 2 failed; the 2 are the shared tool-set pins in D-026-2 (they pass on the base without the tool). ArchTests: 2848/2848. Conformance (`run-conformance.sh`, with the widget built): exit 0, "Baseline check passed".
- **Publish size** (CLAUDE.md section 10; azure-deployment.md convention: `dotnet publish -c Release -r linux-x64 --no-self-contained src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`, framework-dependent linux-x64, `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, PDBs as published (4 .pdb files)). Fresh worktrees at short paths: master `de76599830` = 43,633,279 bytes (41.61 MiB); this branch with the tool = 43,171,289 bytes (41.17 MiB); delta vs master = -461,990 bytes (-0.44 MiB; the branch also carries r2 phase 0 removing the legacy agent). The tool's own contribution (same branch at `f3a0c8e30d` without it = 43,169,436 bytes) = +1,853 bytes. Far under the +5 MB, 55 MB and 60 MB lines.
- **CVE.** `dotnet list package --vulnerable --include-transitive` on master and on the branch: "has no vulnerable packages" for both. No package added.
- **Code review and ADR check (inline).** ADR-001/008 (no new endpoint; tool found by attribute), ADR-010 (no DI registration), ADR-013 (injects PortfolioService from Services/Workspace, not Services/Ai; ArchTests green), ADR-015 (text and structured content are counts and totals; no matter names; nothing logged but tool, outcome, rows, ms, correlation id), ADR-019 (domain failures are isError results), ADR-028 (runs as the caller, no app-only fallback; test asserts every Dataverse read is made as the asking caller's systemuserid), ADR-029 (section above), ADR-038 (tests on a KEEP path `tests/integration/contract`, doubles only at module boundaries: membership resolver, impersonated Dataverse query, oid resolver; no Mock<HttpMessageHandler> or Mock<IServiceClient>). No F-class finding. K4: the unresolvable-caller empty portfolio is parity with REST (see D-026-1 in defer-issues.md for the real defect behind it).
- **Class sweep (Step 9.5 item 6).** Pattern checked: a tool resolving the caller id itself. The only other site in `Api/Mcp/` is none (decision tools pass `HttpContext` to a capability); nothing to fix.
- **Not done.** (1) The plugin skill under `src/solutions/CopilotPlugins/spaarke/skills/` does not mention `spaarke_get_portfolio` (HOW-TO-ADD-AN-MCP-TOOL asks for it when the model should call the tool; no POML in this project owns it). (2) TASK-INDEX.md and current-task.md left to the main session (wave rule); `check-task-status-drift.ps1` will show 026 behind in the index until then.

## Verifier (main session, 2026-11-25, one adversarial pass, opus)

- F2 (low): the model-visible description said "Covers only the matters the user can open"; the service covers matters the user created, owns or is assigned to, trimmed to what they can open. Fixed in the 023/026 follow-up commit (description reworded). The REST `.WithDescription` has the same wording but is not model-read; left as is.
- All six checks held (caller scope and cache key, REST parity, minimal output, error mapping, tests, ledger). No F1/F3.
- Pre-existing #3205 (empty portfolio cached on a failed read) is open for an owner decision on the REST contract.
