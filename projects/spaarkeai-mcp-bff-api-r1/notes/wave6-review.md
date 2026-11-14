# Wave 6 review (tasks 022, 023, 024, 030) — 2026-11-14

One adversarial pass per task (Fable, effort high) on the merged branch (`5aea8e3bd9`), then one fix round and a scoped Sonnet re-check of the fix diff (`5aea8e3bd9..537f8564d1`).

## Fixed (F-class)

| Task | Finding | Fix commit |
|---|---|---|
| 022 | Privileged count was page 1's but read as lane-wide | `85272880c2` (line qualified when more pages exist; `privileged.scope`) |
| 022 | Caller guard and principal pass-through untested | `85272880c2` |
| 022 | Empty cursor re-read page 1 instead of `cursor_invalid` | `85272880c2` |
| 023 | Lookup refusal carried the commit route's message | `85272880c2` (`DecisionResultMapper.Lookup.cs`, ontology review) |
| 023, 024 | No per-call telemetry (NFR-05) on plan, lookup, complete | `85272880c2` |
| 024 | Tool errors carried no support reference | `85272880c2` (`status` + `correlationId` in the error body) |
| 030 | Shim error Response did not match REST (status, title, partial facts, host rejection) | `99dfa03919` |
| 030 | Shim tests asserted an invented contract | `99dfa03919` |
| 030 | `Deploy-BffApi.ps1` / `deploy-bff-api.yml` did not build or check the widget | `99dfa03919` |
| 030 | Built-widget test never ran in CI | `99dfa03919` |
| 030 | ArchTests widget exception let `.Add` / `??=` / hand-built resources through | `537f8564d1` |
| 030 (found in fix) | `publish-bff-artifact.yml` was invalid YAML since 030 (unquoted `:` in a step name) | `99dfa03919` |
| 023/024 (main session) | MCP guard read the C# `is` keyword as an AI-internal type (F2) | `b883e97a31` |
| 030 merge (main session) | McpModule's widget `ResourceCollection` broke 3 ArchTests | `b883e97a31` |

Also added (024 K1): `McpToolAuthorization.ValidateTool` refuses a non-read-only tool whose visibility is not exactly `["app"]` (O-14).

## Known limits (K-class) — not defects on a real path; where each is picked up

| Task | Limit | Owner / next |
|---|---|---|
| 022 | Lane-wide privileged count needs a capability change (`Services/Signals`, ontology) | Owner question 1 |
| 022 | Whole-lane category counts let the model infer a privileged item's work type (not in FR-04's list) | Owner call |
| 022 | Non-billing items link to the Console root (no per-item launch parameter) | Owner question 3 |
| 022 | Opening the widget costs three worklist reads | Measure in 025 |
| 023 | 64 KiB bound on the plan result is asserted on a fixture, not enforced (large `evidenceRefs`) | 025 (measure), cap if seen |
| 023 | Type-ahead shares the 60/min `mcp` rate limit with commit | 032 (debounce, 2-char minimum) |
| 023 | Unknown arguments ignored; `openWorldHint` unset; no test through the production assembly scan | 010 / 025 |
| 024 | App-only rests on the host honouring `_meta.ui.visibility`; Cowork's tool list not yet observed | 042 |
| 024 | The REST-parity tests compare against the tool's own JSON options, not ASP.NET's (escaping differs, JSON-equivalent) | Known; doc wording |
| 024 | Commit content hash uses the raw JSON of `values` (a re-serialising host would get `review_changed`) | 042 |
| 024 | Non-domain failures (bad arguments, exceptions) carry no `reasonCode` | Shim handles it (500) |
| 030 | `mcpHost.ts` has no test (handshake merge, `callServerTool` shape) | 031 |
| 030 | Shim rethrows on host rejection; 031/032 must not assume it never throws | 031, 032 |
| 030 | `tsconfig.json` maps `@spaarke/sdap-client`; 032 may need a Vite alias if a value import survives | 032 |
