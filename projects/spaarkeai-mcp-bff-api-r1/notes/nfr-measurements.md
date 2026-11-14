# NFR measurements — task 025 (NFR-01 in-process, NFR-02, NFR-09)

Measured 2026-11-14 on a developer machine (Windows 11, .NET 10.0.15, **Debug** build, one run). In-process numbers are a floor: no Dataverse, no network, no OBO; live latency on the dev BFF is task 042. **Re-run:** `dotnet build tests/integration/Mcp.Conformance.Host -c Debug` then `dotnet tests/integration/Mcp.Conformance.Host/bin/Debug/net10.0/Mcp.Conformance.Host.dll --measure` (about 12 s; prints the tables below; exit 1 if any call returns `isError`, throws or returns 64 KiB or more). Task 042 re-measures live.

## How it is measured

- **Host:** the five production tool classes on the production MCP host (`AddMcpModule`, `MapMcpEndpoints`, rate limiter, A10 filter, real JwtBearer, real Kestrel loopback), `DecisionToolsTestHost.StartAsync(capability, lookup)`. Behind it, module-boundary doubles (`tests/integration/regression/Mcp/McpFiveToolFixtures.cs`): a 200-item lane (paged by the production `DecideLanePaging`), the largest plan the closed catalog can build, 25 lookup candidates, a commit naming all 200 Signals. Two cases: **plausible** (the 4-matter `DecideLaneSeed.Build()`, 20 evidence lines) and **worst case** (`DecideLaneSeed.BuildWorstCase()`: 50 matters, 400-character escape-heavy text in every clipped member, non-null 2,000-character sentences, a billing and privilege mix; the plan item carries 1,000 evidence lines of the longest shape).
- **Size** = UTF-8 bytes of the JSON-RPC `result` of `tools/call` (`content` + `structuredContent`).
- **Time, outside xUnit:** the `--measure` mode of the test-only console project `tests/integration/Mcp.Conformance.Host` (`MeasureMode.cs`; outside `src/`, not in `Spaarke.sln`). It calls each tool 5 times to warm up and 50 times timed, one caller per tool (55 calls fit the 60/min `mcp` limit), and prints p50, p95, max and the largest result. It **fails** when any call returns `isError`, throws or returns 64 KiB or more, and never on elapsed time (a timing bound on a shared runner flakes). It replaced `McpToolMeasurementTests`, a build-class file on a KEEP path with no real assertions (deleted in the 025 fix round). The size bounds are asserted by `McpResultSizeTests` (regression), which now also checks every call is a success before measuring size.

## NFR-01 in-process latency and NFR-02 size (200 seeded items)

Plausible case (4 matters, 20 evidence lines):

| Tool | p50 ms | p95 ms (50 calls) | max ms | largest result (bytes) | < 64 KiB |
|---|---|---|---|---|---|
| `spaarke_show_decisions` (`category: all`) | 1.2 | 1.4 | 6.3 | 15,542 | yes |
| `spaarke_list_decision_page` (page 1) | 1.1 | 1.4 | 2.5 | 18,240 | yes |
| `spaarke_get_decision_plan` (largest catalog plan + 20 evidence lines) | 2.5 | 3.4 | 4.5 | 37,711 | yes |
| `spaarke_search_decision_lookup` (25 candidates) | 0.5 | 0.6 | 1.5 | 5,795 | yes |
| `spaarke_complete_decision` (names all 200 Signals) | 1.1 | 1.3 | 2.3 | 28,485 | yes |

Worst case (50 matters of escape-heavy max-length text, 1,000 evidence lines of the longest shape on the plan item):

| Tool | p50 ms | p95 ms (50 calls) | max ms | largest result (bytes) | < 64 KiB |
|---|---|---|---|---|---|
| `spaarke_show_decisions` (`category: all`) | 1.6 | 2.6 | 6.2 | 26,904 | yes |
| `spaarke_list_decision_page` (page 1) | 1.5 | 1.7 | 2.3 | 27,302 | yes |
| `spaarke_get_decision_plan` | 120.4 | 295.1 | 300.4 | 47,432 | yes |
| `spaarke_search_decision_lookup` (25 candidates) | 0.2 | 0.3 | 1.3 | 5,795 | yes |
| `spaarke_complete_decision` (names all 200 Signals) | 0.3 | 0.4 | 1.1 | 28,485 | yes |

The worst-case plan call is slow in-process only because its test input is 168 MB of stored evidence JSON that the tool parses before cutting it (1,000 lines of 168 KB each; a real item has a handful of lines of a few hundred bytes, the plausible row above). Every other in-process p95 is under 3 ms, so the NFR-01 targets (3 s / 1.5 s / 15 s) are decided by Dataverse and OBO, which task 042 measures live. `McpResultSizeTests` also asserts every category of `spaarke_show_decisions` and all four pages (50 rows each, 200 rows in all) of `spaarke_list_decision_page` are successes under 64 KiB at the worst-case seed.

**`spaarke_search_decision_lookup`:** 0.9 ms p95 in-process per call. It shares the `mcp` rate limit (60 requests per minute per caller, `RateLimitingModule`) with every other tool including `spaarke_complete_decision`, so type-ahead is limited by the budget, not by time: each keystroke that fires a call spends one of the 60 (carried to 032: debounce, 2-character minimum). The tool also mints no extra Dataverse read in this double; live cost is 042.

## Worklist reads (wave 6 review, 022 "opening the widget costs three worklist reads")

Measured per call (`capability.WorklistReads`, pinned by `McpResultSizeTests.WorklistReadsPerCall_*`):

| Call | Worklist reads |
|---|---|
| `spaarke_show_decisions` | 2 (category counts, then page 1) |
| `spaarke_list_decision_page` | 1 per page |
| `spaarke_get_decision_plan` | 1 (the full row) |

Opening the widget is **2** reads (the widget continues from `show_decisions`' cursor), and opening one item adds **1**: the "three" in the review is those two together. No per-read cost is recorded here (live: 042).

## `spaarke_get_decision_plan` and the 64 KiB bound — fixed in the 025 fix round (2026-11-14)

**Finding (task 025, then Fable review F2).** The result carries the item's full worklist row, whose `evidenceRefs` (a JSON array stored as a string, so every `"` is escaped in the result) and `sentence` had no cap anywhere; the tool passed the row through. With the largest plan the catalog can build (28.7 KB by itself, an upper bound) and evidence lines of about 240 bytes each, about 100 lines (65,666 bytes) took the result past 64 KiB and the host dropped it. FR-05 and FR-11 make "every tool result < 64 KiB" a MUST.

**Fix (MCP tool only; the REST routes and `Services/Signals` are untouched, so REST responses are byte-for-byte as before).** `PlanAndLookupToolSupport.BoundRow`, called by `GetDecisionPlanTool`, bounds the copy of the row the tool returns:
- `evidenceRefs` keeps lines in stored order until the next would pass **100 lines** or **20,000 bytes** (the string's JSON-escaped length, which is what it costs in the result). Each kept line has every string member clipped to 400 JSON bytes with `WorklistItemSummary.Clip` (ends in an ellipsis), at most 16 members and no nested value over 400 bytes. A row inside every bound is returned **unchanged, the stored string byte for byte** (the plan REST-parity test relies on it).
- Row text is clipped the same way: `title`/`subjectName` 240, `ruleShortName` 120, `sentence` 4,000 (a 2,000-character sentence, the stored maximum, fits unless it carries hundreds of escapes), `status`/`workType`/`pastDueWording`/`dueDate` 80 bytes.
- `structuredContent` gains two members beside `plan`, `suppression` and `row`, always present: `evidenceTotal` (how many lines the item stores) and `evidenceTruncated` (true when `row.evidenceRefs` holds fewer). When truncated the `content` text adds "The item stores N evidence line(s); the first ones are returned."

**Why 20,000 bytes and 100 lines.** The result is the plan (28.7 KB at the catalog's maximum) + the row. At the caps the row is about 4 KB of text plus at most 20 KB of evidence, so the worst case measured is **47,432 bytes** (table above: 1,000 lines of 1,500-character quote-and-accent members, a 168 MB stored string), 18 KB under 64 KiB. A plausible item (a handful of lines of a few hundred bytes) is far below every cap and returns exactly what REST returns. The 100-line count only matters for very short lines: at the 240-byte typical line the byte budget stops first (about 55 lines).

**Not bounded here.** The plan itself is bounded by the closed catalog and by the option lists the plan route resolves from live metadata (picklists and active currencies); a very large currency list is not capped by this change. The 28.7 KB used above is every catalog action twice. Decision Record steps (`row.decision`) are never on a Decide-lane row of the lanes set.

**What the widget shows when truncated (observation, no client change; routed to task 032).** The widget parses `row.evidenceRefs` with `parseEvidenceRefs` (`Worklist/worklistWizardItem.ts`) and the wizard's "What was found" step lists whatever lines come back (`DecisionWizard/steps/FoundStep.tsx`). It does not read `evidenceTotal` or `evidenceTruncated`, so a truncated item shows its first lines **with no notice that more exist**, and a clipped line or sentence ends in "…". The empty-state text "No evidence was recorded for this item." cannot appear for a truncated item (at least one line is kept). The closed-item "facts as they stood when this was recorded" snapshot (`decisionRecording.ts`, `factSnapshot`) is built from the same parsed list, so it too shows only the returned lines; it is a local display and is not sent to the commit. Task 032 should read `evidenceTotal`/`evidenceTruncated` and say "Showing the first N of M evidence lines".

## 010 K1 — is the `tools/list` assertion the production-DI proof?

`McpHostContractTests`' original host is `McpTestHost` (the 020 shape: `AddMcpModule` + `MapMcpEndpoints` on a test assembly scan), **not** `Program`, so its probe-tool tests were not. Task 025 adds `McpProgramHostFactory` (`CustomWebAppFactory`, i.e. `WebApplicationFactory<Program>`, plus a JwtBearer test scheme registered in the test's own service configuration) and the test `RealBffHost_ToolsList_WhenEnabledWithAValidToken_IsExactlyTheFiveV1Tools`: an authenticated `tools/list` over HTTP through the real `Program` host, asserted equal to exactly `spaarke_show_decisions`, `spaarke_list_decision_page`, `spaarke_get_decision_plan`, `spaarke_search_decision_lookup`, `spaarke_complete_decision`. **That test is the production-DI proof**: the five tools come from the real assembly scan of `Sprk.Bff.Api`. (010's other real-host test, `McpCallerPathTests` Test 5, reads the catalog from `factory.Services` for the same host but does not issue a request.)

## NFR-09 — conformance

Suite `@modelcontextprotocol/conformance` **0.2.0-alpha.12** (the only published version that knows `2026-07-28`; 0.1.16 rejects the version: "Unknown spec version"), pinned by `tests/integration/Mcp.Conformance.Host/conformance/package-lock.json`. Command: `conformance server --url <injected-port url>/mcp/legal-operations --spec-version 2026-07-28 --suite all --expected-failures conformance-baseline.yml`. `--suite all` because the default `active` suite excludes every 2026-07-28 scenario (server-stateless, caching, header validation, sep-2164): `--spec-version 2026-07-28` alone runs 20 older scenarios.

Result: **92 checks passed, 38 failed, all baselined** (exit 0; 41 baseline entries, see below). Every scenario's `wire-schema-valid` check passes. Passing: `tools-list` (4), `server-sse-multiple-streams` (2), `resources-list` (2), `sep-2164-resource-not-found` (3), `http-header-validation` (14), `server-stateless` (24 of 28), `caching` (7 of 8), `dns-rebinding-protection:localhost-host-valid-accepted`, `input-required-result-unsupported-methods` / `validate-input`.

The **41 baseline entries** (per check, in `conformance-baseline.yml`, each with its reason) are **38 failures** (37 need a reference fixture, 1 real gap) and **3 warnings** (SHOULD-level; the runner counts a warning as a failure under a baseline):
- **37 failures need a reference fixture the Spaarke server must not carry** (`test_*` tools, `test://` resources, prompts, completion, `x-mcp-header` tools, diagnostic tools): "Unknown tool" / "Method not available". They say nothing about our transport. The alternative, serving the fixtures, would put test surface on a customer endpoint (or test the fixtures themselves).
- **1 failure is a real gap, `dns-rebinding-protection:localhost-host-rebinding-rejected`:** Host/Origin validation per the MCP transport requirements is not enforced on `/mcp`; low risk because every call needs a bearer token. The fix is a route change, so it is baselined and **reported for an owner decision** (task escalation: a conformance failure that needs a transport change).
- **3 warnings:** 2 call a `test_input_required_*` fixture tool (`input-required-result-missing-input-response`, `input-required-result-ignore-extra-params`); 1 is `sep-2164-resource-not-found:sep-2164-data-uri` (error `data.uri` not echoed): SDK 2.3.1 behaviour, not ours.

(The earlier text of this note said "38 baselined failures: 36 fixture, 1 gap, 1 SHOULD". That miscounted: the file has 41 entries, 38 + 3 as above. The suite's own line, "38 failed", does not count the warnings.)

Seeded negative (acceptance criterion 3): an endpoint filter that rewrites `Accept` to `application/json` was added to `McpEndpoints.MapMcpEndpoints` locally, the host rebuilt and the same command run: **exit 1, 7 passed / 112 failed, "Unexpected failures (not in baseline)"** (`tools-list`, `resources-list`, `server-sse-multiple-streams`, `http-header-validation` and every `server-stateless` check among them). Restored with `git checkout`; SHA-256 `3013c0593ef1f87d940fe530075cb52079e28898595dba5cc12ecc9986562d84` before and after; no src change is in any commit.

No test-auth path under `src/`: grep of `src/server` for the test scheme, the injector, the signing key and the fake handler finds nothing of this task's. (Pre-existing and unrelated: `Analysis:UseStubResolver`, `AnalysisChatContextResolver.cs:123`, a test seam added by RB-T070-03.)

## Seeded faults for the size bounds (025 fix round, 2026-11-14)

Each fault applied to the production file, the named tests run, the file restored (SHA-256 before and after identical).
- **`PlanAndLookupToolSupport.BoundRow` returns the row unchanged** (`PlanAndLookupToolSupport.cs`, SHA-256 `5395c1c9e031d2233607a76748e00ea21fa4ca3c2bae1639cc54358e21032f3f`): `McpResultSizeTests.GetDecisionPlan_At1000MaxLengthEvidenceLines_...` fails (result 168,749,728 bytes) and `GetDecisionPlan_TheLargestCatalogPlan_...` fails (no `evidenceTotal`); 5 other size tests pass.
- **`WorklistItemSummary.Clip` removed from `From`** (title, subject name, rule short name; `WorklistItemSummary.cs`, SHA-256 `94563152ce6aec6270e3cd37d9c1c00ab3edbc0aafb59fda821560bf22f690d6`): `ListDecisionPage_At200WorstCaseItems_...` fails (page 1 is 383,402 bytes) and `ShowDecisions_At200WorstCaseItems_...` fails. The earlier 200-item seed (9-character titles, 4 matters, null sentences) is below every clip budget, so it could not have gone red: a size test over it proved nothing about `Clip`.

## Known limits (conformance and bounds)

- **Host/Origin validation (K2).** Host/Origin validation per the MCP transport requirements is not enforced on `/mcp`; low risk because every call needs a bearer token. The `dns-rebinding-protection` scenario is baselined and needs an owner decision (a route change), not a test change.
- **Scenario-crash entries excuse a crash.** Baseline entries whose id repeats the scenario (`completion-complete:completion-complete`, `tools-call-simple-text:tools-call-simple-text`, and the other `X:X` ids) name the scenario itself as the failing check. They exist because the fixture is missing, but they also excuse that scenario crashing for any other reason.
- **Fixture-feature entries pre-excuse a broken first implementation.** The prompts, completion and `x-mcp-header` (`sep-2243-*`) entries are baselined because the server has none of those features. The day one ships, its first implementation fails the same checks and the baseline excuses it until the entries are removed: re-baseline (delete the entries, run, fix) when any of those features is added. The suite flags an entry that starts passing as stale, but not one that still fails for a new reason.
- **The plan result bounds the row, not the plan** (see the bound section above): option lists resolved from live metadata are not capped by the row bound.
