# NFR measurements — task 025 (NFR-01 in-process, NFR-02, NFR-09)

Measured 2026-11-14 on a developer machine (Windows 11, .NET 10.0.404, **Debug** build, one run). In-process numbers are a floor: no Dataverse, no network, no OBO; live latency on the dev BFF is task 042. Re-run: `MCP_NFR_MEASUREMENTS_FILE=$PWD/m.md dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~McpToolMeasurementTests"`.

## How it is measured

- **Host:** the five production tool classes on the production MCP host (`AddMcpModule`, `MapMcpEndpoints`, rate limiter, A10 filter, real JwtBearer, real Kestrel loopback), `DecisionToolsTestHost.StartAsync(capability, lookup)`. Behind it, module-boundary doubles (`tests/integration/regression/Mcp/McpFiveToolFixtures.cs`): the 200-item lane of `DecideLaneSeed` (paged by the production `DecideLanePaging`), the largest plan the closed catalog can build, 25 lookup candidates, a commit naming all 200 Signals.
- **Size** = UTF-8 bytes of the JSON-RPC `result` of `tools/call` (`content` + `structuredContent`).
- **Time, without asserting on time:** `McpToolMeasurementTests` (regression/Mcp) is a measurement-only collector. It reads `TimeProvider.System.GetTimestamp()` around each call, sorts, and prints p50/p95/max; it asserts only that 50 calls succeeded and each result was under 64 KiB, never an elapsed time (tests/CLAUDE.md bans `Stopwatch`, `DateTime.UtcNow`, `Task.Delay` in tests; a wall-clock bound flakes on shared runners). 5 warm-up calls per tool are excluded; one caller per tool keeps 55 calls inside the 60/min `mcp` limit.

## NFR-01 in-process latency and NFR-02 size (200 seeded items)

| Tool | p50 ms | p95 ms (50 calls) | max ms | largest result (bytes) | < 64 KiB |
|---|---|---|---|---|---|
| `spaarke_show_decisions` (`category: all`) | 1.8 | 2.6 | 14.9 | 15,542 | yes |
| `spaarke_list_decision_page` (page 1) | 1.6 | 2.4 | 4.0 | 18,240 | yes |
| `spaarke_get_decision_plan` (largest catalog plan + 20 evidence lines) | 2.4 | 3.9 | 8.4 | 37,666 | yes |
| `spaarke_search_decision_lookup` (25 candidates) | 0.7 | 0.9 | 2.3 | 5,795 | yes |
| `spaarke_complete_decision` (names all 200 Signals) | 1.4 | 1.8 | 3.1 | 28,485 | yes |

All in-process p95 values are under 4 ms, so the NFR-01 targets (3 s / 1.5 s / 15 s) are decided entirely by Dataverse and OBO, which task 042 measures live. `McpResultSizeTests` also asserts every category of `spaarke_show_decisions` and all four pages of `spaarke_list_decision_page` (50 rows each) are under 64 KiB.

**`spaarke_search_decision_lookup`:** 0.9 ms p95 in-process per call. It shares the `mcp` rate limit (60 requests per minute per caller, `RateLimitingModule`) with every other tool including `spaarke_complete_decision`, so type-ahead is limited by the budget, not by time: each keystroke that fires a call spends one of the 60 (carried to 032: debounce, 2-character minimum). The tool also mints no extra Dataverse read in this double; live cost is 042.

## Worklist reads (wave 6 review, 022 "opening the widget costs three worklist reads")

Measured per call (`capability.WorklistReads`, pinned by `McpResultSizeTests.WorklistReadsPerCall_*`):

| Call | Worklist reads |
|---|---|
| `spaarke_show_decisions` | 2 (category counts, then page 1) |
| `spaarke_list_decision_page` | 1 per page |
| `spaarke_get_decision_plan` | 1 (the full row) |

Opening the widget is **2** reads (the widget continues from `show_decisions`' cursor), and opening one item adds **1**: the "three" in the review is those two together. No per-read cost is recorded here (live: 042).

## `spaarke_get_decision_plan` and the 64 KiB bound — a finding (not fixed; production code not changed)

The result carries the item's full worklist row, whose `evidenceRefs` (a JSON array stored as a string, so every `"` is escaped in the result) and `sentence` have **no cap anywhere** (`SignalWriter` writes one line per witness; the tool passes the row through). Measured with the largest plan the catalog can build (all 15 actions as actions and as next steps, 28.7 KB by itself, an upper bound: a real plan is far smaller) and evidence lines of about 240 bytes each:

| Evidence lines | Result bytes | < 64 KiB |
|---|---|---|
| 0 | 28,689 | yes |
| 20 | 37,666 | yes |
| 50 | 48,166 | yes |
| 100 | 65,666 | **no** |
| 200 | 100,866 | no |
| 1000 | 382,466 | no |

About 350 result bytes per evidence line. With the worst-case plan the cap is crossed at ~100 lines; with a realistic plan (a few actions, 3–8 KB) at ~170. A rule today produces a handful of witness lines, so no real item is known to exceed it; but nothing prevents one. `McpResultSizeTests` asserts 50 lines with the largest plan (48 KB) and deliberately does not assert a bound on lines. **Report to the owner:** cap or trim `row.evidenceRefs` in the plan tool's result (e.g. count plus the first N lines, or leave the row to the REST route) before a rule with a large witness set ships. It is a change to `GetDecisionPlanTool`, so it was not made in task 025.

## 010 K1 — is the `tools/list` assertion the production-DI proof?

`McpHostContractTests`' original host is `McpTestHost` (the 020 shape: `AddMcpModule` + `MapMcpEndpoints` on a test assembly scan), **not** `Program`, so its probe-tool tests were not. Task 025 adds `McpProgramHostFactory` (`CustomWebAppFactory`, i.e. `WebApplicationFactory<Program>`, plus a JwtBearer test scheme registered in the test's own service configuration) and the test `RealBffHost_ToolsList_WhenEnabledWithAValidToken_IsExactlyTheFiveV1Tools`: an authenticated `tools/list` over HTTP through the real `Program` host, asserted equal to exactly `spaarke_show_decisions`, `spaarke_list_decision_page`, `spaarke_get_decision_plan`, `spaarke_search_decision_lookup`, `spaarke_complete_decision`. **That test is the production-DI proof**: the five tools come from the real assembly scan of `Sprk.Bff.Api`. (010's other real-host test, `McpCallerPathTests` Test 5, reads the catalog from `factory.Services` for the same host but does not issue a request.)

## NFR-09 — conformance

Suite `@modelcontextprotocol/conformance` **0.2.0-alpha.12** (the only published version that knows `2026-07-28`; 0.1.16 rejects the version: "Unknown spec version"), pinned by `tests/integration/Mcp.Conformance.Host/conformance/package-lock.json`. Command: `conformance server --url <injected-port url>/mcp/legal-operations --spec-version 2026-07-28 --suite all --expected-failures conformance-baseline.yml`. `--suite all` because the default `active` suite excludes every 2026-07-28 scenario (server-stateless, caching, header validation, sep-2164): `--spec-version 2026-07-28` alone runs 20 older scenarios.

Result: **92 checks passed, 38 failed, all 38 baselined** (exit 0). Every scenario's `wire-schema-valid` check passes. Passing: `tools-list` (4), `server-sse-multiple-streams` (2), `resources-list` (2), `sep-2164-resource-not-found` (3), `http-header-validation` (14), `server-stateless` (24 of 28), `caching` (7 of 8), `dns-rebinding-protection:localhost-host-valid-accepted`, `input-required-result-unsupported-methods` / `validate-input`.

The 38 baselined failures (per check, in `conformance-baseline.yml`, each with its reason):
- **36 need a reference fixture the Spaarke server must not carry** (`test_*` tools, `test://` resources, prompts, completion, `x-mcp-header` tools, diagnostic tools): "Unknown tool" / "Method not available". They say nothing about our transport. The alternative, serving the fixtures, would put test surface on a customer endpoint (or test the fixtures themselves).
- **1 real gap, `dns-rebinding-protection:localhost-host-rebinding-rejected`:** a request with `Host`/`Origin` = `evil.example.com` gets 200. The MCP route validates neither (`AllowedHosts` is `*`, nothing on `/mcp/*` checks `Origin`; MCP transport spec asks a server to validate `Origin`). Low risk on the deployed BFF (bearer-token auth, no ambient credentials) but a spec gap; the fix is a route change, so it is baselined and **reported for an owner decision** (task escalation: a conformance failure that needs a transport change).
- **1 SHOULD, `sep-2164-resource-not-found:sep-2164-data-uri`** (warning: error `data.uri` not echoed): SDK 2.3.1 behaviour, not ours.

Seeded negative (acceptance criterion 3): an endpoint filter that rewrites `Accept` to `application/json` was added to `McpEndpoints.MapMcpEndpoints` locally, the host rebuilt and the same command run: **exit 1, 7 passed / 112 failed, "Unexpected failures (not in baseline)"** (`tools-list`, `resources-list`, `server-sse-multiple-streams`, `http-header-validation` and every `server-stateless` check among them). Restored with `git checkout`; SHA-256 `3013c0593ef1f87d940fe530075cb52079e28898595dba5cc12ecc9986562d84` before and after; no src change is in any commit.

No test-auth path under `src/`: grep of `src/server` for the test scheme, the injector, the signing key and the fake handler finds nothing of this task's. (Pre-existing and unrelated: `Analysis:UseStubResolver`, `AnalysisChatContextResolver.cs:123`, a test seam added by RB-T070-03.)
