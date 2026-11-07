# Phase 0 spike measurements (task 001)

> Recorded 2026-11-06 on the project branch. The spike code lives only on `spike/spaarkeai-mcp-phase0` (worktree `C:\code_files\spaarke-wt-spaarkeai-mcp-phase0-spike`, cut from `origin/master` `f9ba615b09`). It is never merged (ontology condition 1). Nothing from it is on `work/spaarkeai-mcp-bff-api-r1` or master.

## 1. Spike head SHA (for the owner's per-SHA deploy go)

**`5c9e301572`** on `spike/spaarkeai-mcp-phase0` (one commit on top of master `f9ba615b09`). Not pushed to origin; not deployed. **No deploy has happened and none was requested of the dev App Service.** The deploy needs the owner's explicit go for this SHA, in unified-access-control-r2's session (project CLAUDE.md §5). Deployed URL: not yet (record here when the owner confirms).

What the deploy needs from the owner, besides the go: nothing to configure (the spike routes are always mapped; there is no `Mcp:Enabled` switch on the spike); a licensed `systemuser` workforce token for T5 (`spike_whoami`); and the branch pushed to origin, or a deploy from the worktree path (I did not push).

## 2. SDK 2.3.1 API used (`ModelContextProtocol.AspNetCore` 2.3.1, net10.0)

| Need | API used |
|---|---|
| Register | `services.AddMcpServer().WithHttpTransport(o => ...)` |
| Stateless | `HttpServerTransportOptions.Stateless = true` (proxy over `SessionMode = Stateless`; this is also the default as of protocol `2026-07-28`). GET, DELETE and `/sse` are off; `Mcp-Session-Id` unused. |
| Route mapping | `app.MapMcp("/mcp/{area}")` once per route; returns an endpoint-convention builder, so `.RequireAuthorization()` and `.AddEndpointFilter(...)` chain on it. Two `MapMcp` calls in one host work. |
| Tool filtering (T13) | `HttpServerTransportOptions.ConfigureSessionOptions = (HttpContext, McpServerOptions, ct) => ...` runs on **every HTTP request** in stateless mode; the spike picks `serverOptions.ToolCollection` and `ResourceCollection` from `HttpContext.Request.Path`. Tools are built once with `McpServerTool.Create(delegate, McpServerToolCreateOptions)`. |
| Annotations | `McpServerToolCreateOptions.ReadOnly / Idempotent / Destructive / OpenWorld / Title / Description`; `tools/list` returns `annotations.readOnlyHint` and `title`. |
| UI metadata | `McpServerToolCreateOptions.Meta` (a `JsonObject`) carries `ui.resourceUri` on the tool; `McpServerResourceCreateOptions.Meta` and `TextResourceContents.Meta` carry `ui.csp`. |
| Resources | `McpServerResource.Create(delegate returning TextResourceContents, options)` with a fixed `UriTemplate` (no template variables). |
| Tool result | `CallToolResult { Content, StructuredContent }` returned directly. |
| Services in a tool | A tool delegate takes `IServiceProvider` and resolves `IDataverseUserClient` / `IHttpContextAccessor` from it; the OBO client reads the caller's bearer from the ambient `HttpContext`, which is populated in stateless mode. |
| Transport traps confirmed | Without `Accept: application/json, text/event-stream` the endpoint answers **406**. The body of a `tools/list` reply is an SSE frame or JSON; the test parses both. |
| Not used | `McpAuthenticationHandler` / resource-metadata (RFC 9728) — it would add an authentication scheme, which the spike must not (ADR-028). Whether Copilot's connector flow needs the protected-resource metadata document is a T5/T0 question for 003. |

Findings that matter for 020 (production host):
1. **Dependency lift.** `ModelContextProtocol.Core` 2.3.1 depends on `Microsoft.Extensions.AI.Abstractions` **10.11.0** and `Microsoft.Extensions.Logging.Abstractions >= 10.0.13`. The BFF pins `Microsoft.Extensions.AI` 10.3.0, so the publish now carries `Abstractions` 10.11.0 (file 557,640 → 679,224 bytes) next to `Microsoft.Extensions.AI` 10.3.0. This is the NFR-08 bump surface. I ran the AI unit namespaces (`Tests.Ai`, `Tests.Services.Ai`) with the mix: **5315 passed, 0 failed, 1 skipped**. The AI integration suites were not run.
2. **Solution restore breaks.** `Spaarke.sln` restore failed with NU1605 in `tests/integration/Sprk.Provisioning.ControlPlane.NightlyTests` (it pins `Microsoft.Extensions.Logging.Abstractions` 10.0.11 and references `Sprk.Bff.Api`). The spike commit bumps that pin to 10.0.13. Task 020 must do the same; any other project that references the BFF and pins the package lower will fail the same way. Because restore failed, `dotnet format` (the pre-commit hook) failed too.
3. The real-host test factory (`CustomWebAppFactory`) replaces authentication with `FakeAuthHandler`, which sends no `WWW-Authenticate`. The header is asserted in a minimal host with the real `JwtBearer` scheme; the real BFF scheme must be re-checked live (003).

## 3. ext-apps 2.0.4 vs the 1.x samples (risk R13)

- `useApp({ appInfo, capabilities, onAppCreated })` and `app.ontoolresult`, `app.onhostcontextchanged`, `app.getHostContext()`, `app.callServerTool({ name, arguments })` are unchanged from the trey-research `useMcpApp.tsx` (1.x). The widget compiles (`tsc --noEmit` clean) with that shape.
- **New peer dependencies:** 2.x declares `@modelcontextprotocol/client ^2`, `@modelcontextprotocol/core ^2` and `zod ^4` as peers (react and react-dom stay optional). With `--legacy-peer-deps` they are not installed. The package exports `./react-with-deps` and `./app-with-deps`, which bundle them; the spike imports `@modelcontextprotocol/ext-apps/react-with-deps` and the single-file build works (about 540 KB of the bundle is that entry). Types still reference `@modelcontextprotocol/client`, so `skipLibCheck: true` is needed unless the peers are installed.
- New `strict` option on `useApp` / `AppOptions`; new `downloadFile`, `requestDisplayMode`, `requestTeardown`, `sendLog`, `updateModelContext` (present in the d.ts; not exercised).
- `RESOURCE_MIME_TYPE = "text/html;profile=mcp-app"` (matches the brief). The flat `ui/resourceUri` meta key is **deprecated**; the nested `_meta.ui.resourceUri` is the format to emit, and hosts must read both. The spike emits the nested form only. If Copilot ignores it, T1 should retry with the flat key as well.
- `csp` belongs on the **resource** (`resources/read` content item, `resources/list` entry as fallback), not the tool. The spike sets `_meta.ui.csp = {}` on both.
- Single-file build: React 18 + Fluent UI v9 + ext-apps 2.0.4 with `vite-plugin-singlefile` gives **752,030 bytes** (734 KiB). No script or link `src`/`href` to a network origin (the only `https://` strings are XML namespaces and library doc URLs).

## 4. Local T13 result (two tool sets on two routes in one host)

**Passes locally.** In one host (stateless, one `AddMcpServer`), `tools/list` on `/mcp/legal-operations` returns exactly `spike_echo` and `spike_whoami` (both `readOnlyHint: true`), and on `/mcp/spike-area-b` exactly `spike_echo_b`. Calling `spike_whoami` on the area-b route is refused and makes no Dataverse call. So no fallback is needed locally ("one route that filters tools by path" is in fact what the spike does inside one handler; the route mechanism itself, two `MapMcp` calls, works). The remaining T13/T14 questions are Copilot-side (display name in Sources and on the approval card; two connectors in one app package) and need the deployed endpoint.

## 5. Protocol checks (in-process)

Tests: `tests/unit/Sprk.Bff.Api.Tests/Spike/SpikeMcpProtocolTests.cs` (17 cases incl. theory rows) and `SpikeMcpRealHostTests.cs` (2 cases): **19 spike tests, all pass** (the filter `FullyQualifiedName~Spike` also matches 13 older tests; 32 passed, 0 failed in total). Mutation checks: removing `.RequireAuthorization()` on one route fails the anonymous-401 test for it; pointing area-b at the first tool set fails the tool-list test. Covered:
- `tools/list` per route (above); every tool has a title and `readOnlyHint`.
- Anonymous request to either route: **401 with `WWW-Authenticate: Bearer`** (real `JwtBearer`, no fallback policy, so the route's own `.RequireAuthorization()` is what is tested). On the real BFF host (fake auth scheme) both routes give 401.
- No `Accept: application/json, text/event-stream`: **406** on both routes.
- `resources/read` for `ui://spaarke/spike.html` and the 1, 3 and 6 MB variants: `mimeType` `text/html;profile=mcp-app`, `_meta.ui.csp` an empty object; `resources/list` advertises the same.
- Response buffering disabled: the endpoint calls `IHttpResponseBodyFeature.DisableBuffering()` (verified with a recording feature).
- `spike_whoami`: returns the caller's `systemuserid` from `WhoAmI` through `IDataverseUserClient` (fake client in the test; one call); claim flags for `aud`, `scp`, `tid`, `oid`; the token, the scope text and the audience text appear in neither the result nor the logs; a failed OBO gives `status: failed`, a null id and the error **code** only, with no fallback identity.
- **Not proven locally:** a real workforce token's OBO exchange to Dataverse (needs the deployed spike; that is T5).
- The "fails before, passes after" proof: the code is new, so the tests cannot run "before"; the mutation checks above stand in for it.

## 6. Publish size (NFR-04, `azure-deployment.md` per-task rule)

| | Compressed size |
|---|---|
| Fresh master build (`f9ba615b09`) | **39.232 MB** |
| Spike build (`5c9e301572`) | **39.935 MB** |
| **Delta** | **+0.703 MB** (below the +5 MB justification line, far below 55 MB) |

Measurement convention: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o <dir>`; framework-dependent linux-x64 (the csproj RID); Release; `Compress-Archive -CompressionLevel Optimal` over `<dir>\*`; PDBs included (about 4.2 MB of the master figure). The master baseline was published from the spike worktree before any edit (a fresh checkout of master); the worktree path is not short, so treat the figure as comparable between the two builds here, not against the 44.96 MB historical baseline. Files added: `ModelContextProtocol.AspNetCore.dll`, `ModelContextProtocol.Core.dll`, `ModelContextProtocol.dll`; `Microsoft.Extensions.AI.Abstractions.dll` grew; no file removed. The unpadded widget (734 KiB) is embedded; the padding is generated at runtime and is **not** in the publish.

New HIGH CVEs: none (`dotnet list package --vulnerable --include-transitive` on the BFF project: no vulnerable packages).

## 7. Widget sizes

| Resource | Bytes served (asserted ±2 KB) |
|---|---|
| unpadded single-file build (embedded) | 752,193 |
| `ui://spaarke/spike.html` (alias of the 1 MB variant) and `ui://spaarke/spike-1mb.html` | 1,048,576 (1 MB) |
| `ui://spaarke/spike-3mb.html` | 3,145,728 (3 MB) |
| `ui://spaarke/spike-6mb.html` | 6,291,456 (6 MB) |

Padding is a deterministic pseudo-random string in an inline `<script>` (so a host must download and parse it; an HTML comment would parse for free). `ui://spaarke/spike.html` is wired to `spike_echo` (`_meta.ui.resourceUri`); the 3 and 6 MB variants are separate resources, reachable by `resources/read` but not bound to a tool, because the routes' tool sets are fixed by the T13 criterion. For the Size test in Chat/Cowork the variant under `spike.html` has to change (a one-line edit and redeploy) or 003 can bind a variant to `spike_echo_b`'s `_meta`; I did not add a size argument, to keep the tool sets exact.

## 8. Not done, and why

- **Deploy to the dev BFF:** not performed; needs the owner's per-SHA go (escalation trigger 1).
- **UI test in a local MCP Apps host (render, echo round-trip, dark mode):** not run; no browser/Chrome integration in this session, and the ext-apps basic host needs a token-injecting setup. The widget builds and type-checks; rendering is untested. 003 will see it in Copilot first.
- **Full `Sprk.Bff.Api.Tests` run:** started once; on this heavily loaded machine 29+ long-running tests timed out (3 minute durations: host-start tests and seam tests). The four failing classes I re-ran alone (`UserEndpointsTests`, `ContentUnderstandingHostContractTests`, `DiGraphValidationTests`, `DocumentProfileContractTests`) pass (20 passed, 1 skipped). I killed the full run before it finished, so there is no full-suite count. Seam tests that read `C:\code_files\...` corpus paths and Graph/Dataverse live seams are environmental.
- ArchTests (`tests/Spaarke.ArchTests`, route authorization census) were not run; the spike branch is never merged, so they gate nothing here. Production 020 owns B-8.
