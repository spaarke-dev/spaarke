# Task 030 notes: widget scaffold, BFF embedding, CI size gate

Branch `worktree-agent-a189ad6bd2713fa87` (rebuilt on `work/spaarkeai-mcp-bff-api-r1` @ 6ce612ff0e). Model: Sonnet 5.5 @ high.

## What shipped

- `src/client/widgets/spaarke-decisions/`: Vite 5 + vite-plugin-singlefile, React 18, Fluent v9, `@modelcontextprotocol/ext-apps` 2.0.4 (`react-with-deps`).
  - `src/mcp/mcpFetch.ts`: `createMcpFetch(callTool, routes)`, a `ResponseFetchFn` over `callServerTool`. Success is a 200 with the tool's `structuredContent`; `isError` is a non-2xx with `reasonCode`/`message`/`detail`/`title`/`status`. Never throws; never touches the network. The route table is data supplied by 031/032.
  - `src/mcp/mcpHost.ts` / `mcpHostContext.ts`: `McpHostProvider`, `useMcpHost` (host context, tool result, `callTool`, `openLink`).
  - `src/theme/hostTheme.ts`: light/dark from the host context only (no matchMedia, no `detectDarkModeFromNavbar`).
  - `scripts/check-bundle-size.mjs`: fails above 3 MiB, on a missing file, on `<script src>` or a stylesheet `<link>`; warns above 1 MiB.
- BFF: `DecisionsWidgetResource` (`ui://spaarke/decisions.html`, `text/html;profile=mcp-app`, `_meta.ui.csp = {}` on list and read), registered for the legal-operations area in `McpModule.ConfigureSessionOptions`. csproj embeds `dist/index.html` under `Sprk.Bff.Api.Widgets.decisions.html` with `Condition=Exists`.
- CI: `publish-bff-artifact.yml` sets up Node 20 and builds the widget before `dotnet build`, then fails the job when the published `Sprk.Bff.Api.dll` lacks the resource.

## Measured

- Bundle: `dist/index.html` 762,809 bytes (0.73 MB). Under the 1 MB aim and the 3 MB gate. The 1 MB escalation trigger did not fire.
- Tests: widget jest 66 pass (5 suites). `dotnet test Sprk.Bff.Api.Tests --filter Api.Mcp`: 20 pass, 1 skipped (the not-built case skips when the widget is built, by design; the built case runs).
- ext-apps 2.0.4 drift vs the 1.x samples: none for `useApp`, `ontoolresult`, `onhostcontextchanged`, `getHostContext`, `callServerTool`, `openLink`. The R13 trigger did not fire.
- Local UI check: a throwaway MCP Apps host (not committed) drove the widget; host dark/light flipped the Fluent theme and `callServerTool` was wired. The UI-side `callServerTool` round trip with a real BFF is NOT exercised (031 does that).

## Publish-size delta (BFF-touching task, CLAUDE.md section 10)

Convention (`.claude/constraints/azure-deployment.md`): fresh short-path worktrees, `dotnet publish -c Release`, framework-dependent linux-x64, `Compress-Archive -CompressionLevel Optimal`, PDBs included.

| Build | Files | Zip bytes |
|---|---|---|
| fresh master (942b2889bf), `C:\wt030m` | 191 | 42,147,883 |
| task base 6ce612ff0e, no widget, `C:\wt030o` | 194 | 42,944,039 |
| task head, widget embedded, `C:\wt030h` | 194 | 43,148,675 |

- Head vs fresh master: +1,000,792 bytes (+0.95 MB). Under the +5 MB justification threshold, 43.1 MB total, under the 55 MB review threshold.
- Head vs its own base: +204,636 bytes (+0.20 MB), which is the embedded widget (the 0.8 MB master-to-base gap is other project commits).
- No new files in the publish from this task (194 vs 194).

## CVE check

`dotnet list Sprk.Bff.Api.csproj package --vulnerable --include-transitive`: no vulnerable packages. No NuGet package was added. Widget `npm audit --omit=dev`: 0 vulnerabilities.

## Placement justification (`.claude/constraints/bff-extensions.md`, CLAUDE.md section 10/11)

1. Existing: the legal-operations MCP area (`McpEndpoints`, `McpModule`) already hosts tools; there was no resources surface.
2. Extension: the widget is served as one MCP resource from that same area via `ConfigureSessionOptions`, not a new endpoint, controller or service. One static class, `DecisionsWidgetResource`, plus one registration line.
3. Cost of doing nothing: without a `ui://` resource the host cannot render the Decide lane (FR-12), so the tools return data with no UI.
- Placement is inside the BFF (not a separate host) because the widget has no origin or token of its own; it rides the host's authenticated MCP connection (ADR-028 A10).
- No AI facade impact (no AI types injected); no new DI registration beyond the resource collection on the area.

## Step 9.5

code-review (BFF hygiene, TS and .NET checks) and adr-check (ADR-001/012/021/028/029/038) were run. adversarial-reviewer pass is skipped here by instruction; the main session runs it after merging.

Fixed in scope:
- F2 mcpFetch threw on a malformed percent escape or a throwing argument builder: now 400 `widget.bad_request` (tests added, fail before / pass after: 4 new tests failed against the old code).
- F2 a body member could override the path identifier in the default argument builder: path params now merge last (test added).
- F3 a missing host result object crashed the shim: now 502 `widget.unreadable_result` (test added).
- F3 `minHeight: 100vh` on the shell forced a tall iframe: removed.
- F3 csproj used backslashes in `Include`/`Condition`: now forward slashes (Linux CI safe).
- F1 a skipped widget build would publish an artifact that errors on `resources/read`: new publish step fails the job. Controls: must-fire on the no-widget publish (`C:\wt030o`, "NOT embedded"), must-not-fire on the widget publish (`C:\wt030h`, "embedded").

Known limits (K, not defects on a real path today):
- K2 `deploy-bff-api.yml` and `scripts/Deploy-BffApi.ps1` do not build the widget; a BFF deployed through them (not the H9 artifact path) has no widget embedded and `resources/read` returns an error. Needs an owner decision on whether those paths also build it. See the open items.
- K3 the embed check is a byte search for the logical name in the dll, not a read of the resource bytes.

## Hand-off to 031 / 032

- Supply `McpRoute[]` tables to `createMcpFetch`; the shared `createWorklistService` and decision wizard service work unchanged over it (proved by test for the worklist).
- Use `useMcpHost()` for `callTool`, `openLink` and `hostContext`; do not add a second `useApp`.
- Keep the surface tests green: no fetch/XHR/sockets, no token or Authorization, no storage, no raw colours.
- Add shared-library imports by deep path (ADR-012), never the barrel.

## Open items for the owner

1. Should `deploy-bff-api.yml` / `Deploy-BffApi.ps1` build the widget too, or is the H9 artifact path the only production path?
2. The UI `callServerTool` round trip against a real host has not been exercised.

rework: none
