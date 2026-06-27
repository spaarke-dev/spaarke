# Wave 7 — TextRefinementHandler Migration Notes

**Status**: Completed (2026-06-08)
**Rigor**: STANDARD
**Wave**: 7 (Q9 chat-tool batch migration — trivial group)
**Migration**: hardcoded `TextRefinementTools` → typed `IToolHandler` (`TextRefinementHandler`)

## What was built

### New handler
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/TextRefinementHandler.cs`
  - Implements `IToolHandler` with `SupportedInvocationContexts = Both`.
  - Constructor injects `IChatClient` (singleton) + `ILogger`. Handler is scoped (per `AddToolHandlersFromAssembly`) so singleton-into-scoped DI is safe.
  - 3 internal pipelines: `BuildRefineMessages`, `BuildKeypointsMessages`, `BuildSummaryMessages` (mirror pre-R6 `TextRefinementTools` prompt shape verbatim).
  - Dispatches based on `sprk_configuration.method` discriminator (`"refine" | "keypoints" | "summary"`).
  - Returns `ToolResult.Ok` with `TextRefinementResult { Method, Text }` in `data`.
  - ADR-015 telemetry: handler name + method + IDs + duration + output-length bucket ONLY. Never input text, never instruction, never output content.

### Dataverse seed rows (3)
Each row points at `sprk_handlerclass = TextRefinementHandler` with `sprk_availableincontexts = 100000002` (Both):

| Row | toolcode | name | configuration.method |
|---|---|---|---|
| `infra/dataverse/sprk_analysistool-text-refine-row.json` | `TEXT-REFINE` | `SYS-Text Refinement` | `refine` |
| `infra/dataverse/sprk_analysistool-text-keypoints-row.json` | `TEXT-KEYPOINTS` | `SYS-Text Key Points` | `keypoints` |
| `infra/dataverse/sprk_analysistool-text-summary-row.json` | `TEXT-SUMMARY` | `SYS-Text Summary` | `summary` |

All 3 pass `scripts/Test-AnalysisToolSchemaValid.ps1` (catalog-write-time JSON Schema validation).

### Seed script extension
- `scripts/Seed-TypedHandlers.ps1`:
  - Added Wave 7 section to `$RowFiles` map: 3 new entries keyed by toolcode (since handler-class collides).
  - Extended `Find-ExistingRow` with optional `$ToolCode` parameter to disambiguate when a handler class serves multiple rows.
  - Loop reads `sprk_handlerclass` from payload (not map key) and passes both handler-class + toolcode to the lookup.
  - Pre-existing 1:1 entries (Wave 1, Wave 2, sibling Wave 7 AnalysisQuery) work unchanged — toolcode filter is appended only when supplied.

## Method-dispatch decision

**Chosen approach**: 3 separate `sprk_analysistool` rows, one per LLM-exposed method, all sharing a single C# handler class with method-discrimination via `sprk_configuration.method`.

**Rationale**:
- Preserves the pre-R6 LLM exposure model (`AIFunctionFactory.Create` × 3 separate `AIFunction`s from one `TextRefinementTools` instance) — the LLM sees 3 distinct tools with distinct descriptions, improving tool selection.
- Each row has its own `sprk_jsonschema` describing its specific input shape (`text + instruction` for refine; `text + maxPoints` for keypoints; `text + format` for summary).
- A single handler class keeps the implementation DRY (one prompt builder per method; shared validation + telemetry + error handling).
- Required a small enhancement to `Seed-TypedHandlers.ps1` (toolcode disambiguation in `Find-ExistingRow`) — additive, backward-compatible with existing 1:1 rows.

**Alternative considered**: 1 row with method-as-tool-arg. Rejected because:
- The LLM would see ONE tool with three behaviors hidden behind an arg, losing tool-selection signal.
- The `sprk_jsonschema` would need to model all three input shapes via discriminated union (more complex; less discoverable in admin UI).
- Doesn't match the pre-R6 exposure model.

## Diff summary — `SprkChatAgentFactory.cs`

Removed lines 828–852 (25 lines of `TextRefinementTools` LLM-tool registration including the try/catch wrapper). Replaced with a 10-line comment marker explaining the removal and pointing at the typed handler + `ChatEndpoints.RefineTextAsync` retention rationale.

**Net change**: −15 lines from the factory; sibling tool blocks above (KnowledgeRetrievalTools) and below (WorkingDocumentTools) preserved unchanged.

**`TextRefinementTools` class NOT deleted**: still consumed by `Api/Ai/ChatEndpoints.cs:824` for the SSE-streaming `/refine` endpoint which uses `BuildRefineMessages` directly (not an LLM tool call). The class header XML doc may be updated in a follow-up PR to note this is now its only consumer.

## Tests

- File: `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/TextRefinementHandlerTests.cs`
- Count: **28 tests**
- Pass rate: **28/28 first run** (no flakes, no retries).

Coverage:
- 4-point contract (4 tests: DI registration, HandlerId == nameof, Metadata semver, SupportedToolTypes non-empty) + `SupportedInvocationContexts == Both`.
- Validate (playbook): success, missing extracted text, missing tenantId, missing method, unknown method, invalid summary format.
- ValidateChat: success, missing text, missing tenantId, malformed JSON.
- ExecuteChatAsync per-method dispatch: refine + keypoints + summary (theory test for 3 summary formats).
- Required-field enforcement: refine requires instruction; empty text returns `ValidationFailed`.
- Exception handling: `OperationCanceledException` → `Cancelled`, generic exception → `InternalError`.
- ExecuteAsync (playbook path): uses extracted text + action system prompt.
- ADR-015 telemetry: sentinel-scan for input text + instruction + output content not appearing in logs; method discriminator IS logged for correlation.
- Method-dispatch coverage: all 3 methods through a single handler instance.

## ADR Compliance

| ADR | Status | Evidence |
|---|---|---|
| ADR-010 (DI minimalism) | ✅ | Zero manual DI line. `HandlerType_IsRegisteredInDi` test verifies auto-discovery. |
| ADR-013 (AI architecture) | ✅ | Handler in `Services/Ai/Handlers/`. `IChatClient` stays AI-internal (no PublicContracts contamination). |
| ADR-014 (per-tenant cache) | ✅ | Handler does NOT cache (parity with pre-R6 `TextRefinementTools` — no cache layer there either). `IChatClient`'s downstream caching (if any) preserved. TenantId is asserted on both playbook + chat paths. |
| ADR-015 (telemetry hygiene) | ✅ | Logs handler name + method + IDs + duration + output-length bucket. NEVER input text, NEVER instruction, NEVER output content. Sentinel test `Telemetry_DoesNotLeakInputText_OutputContent_OrInstruction` asserts. |
| ADR-016 (rate limits) | ✅ | LLM call goes through `IChatClient` — same as the rest of the chat agent. Rate-limit gate (per-tenant `ai-context`) is applied by the surrounding chat-agent path. No bypass. |
| ADR-029 (publish hygiene) | ✅ | Zero new NuGet packages. Per-handler delta target ≤+0.5 MB; achieved +0.00 MB. |
| NFR-04 (no Microsoft Agent Framework) | ✅ | Uses `Microsoft.Extensions.AI.IChatClient` (NOT Agent Framework). No new references. |

## BFF publish-size delta (NFR-02)

- **Pre-task baseline** (sibling Wave 7 AnalysisQuery zip): **45.89 MB** compressed.
- **Post-task** (this PR's publish): **45.89 MB** compressed.
- **Delta**: **+0.00 MB** (rounded). Well under ≤+0.5 MB per-handler target and ≤60 MB ceiling.
- **Ceiling clearance**: 14.11 MB headroom remaining.

## MCP verification

`mcp__dataverse__read_query` against `sprk_analysistool` for `sprk_handlerclass = 'TextRefinementHandler'` returns the 3 expected rows:

| sprk_toolcode | sprk_name | sprk_availableincontextsname | sprk_analysistoolid |
|---|---|---|---|
| TEXT-KEYPOINTS | SYS-Text Key Points | Both | de8f5a76-3d63-f111-ab0c-70a8a53ec687 |
| TEXT-REFINE | SYS-Text Refinement | Both | d3fab651-3d63-f111-ab0c-000d3a4d8152 |
| TEXT-SUMMARY | SYS-Text Summary | Both | 05b20190-3d63-f111-ab0c-70a8a53ec687 |

Idempotency confirmed: re-running `Seed-TypedHandlers.ps1 -OnlyHandler TEXT-REFINE` correctly PATCHed the existing row instead of creating a duplicate.

## Files modified

- ADDED `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/TextRefinementHandler.cs`
- ADDED `infra/dataverse/sprk_analysistool-text-refine-row.json`
- ADDED `infra/dataverse/sprk_analysistool-text-keypoints-row.json`
- ADDED `infra/dataverse/sprk_analysistool-text-summary-row.json`
- ADDED `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/TextRefinementHandlerTests.cs`
- MODIFIED `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` (lines 828–852 → comment marker)
- MODIFIED `scripts/Seed-TypedHandlers.ps1` (`$RowFiles` map + `Find-ExistingRow` toolcode disambiguation + main-loop payload-read)

## Stop-and-report triggers — NONE fired

- `TextRefinementTools` state: only `IChatClient` (confirmed — no hidden state).
- Method dispatch: 3 separate rows with method-discriminator is the cleanest mapping; the LLM gets 3 distinct tools. No unanticipated consequences.
- File-overlap risk: shared `Seed-TypedHandlers.ps1` edits were narrow (added 3 map entries + 1 optional parameter + 1 disambiguation filter line); preserve sibling Wave 7 (`AnalysisQueryHandler`) entry above mine. The toolcode-disambiguation change benefits sibling agents too (their 1:1 rows pass through the existing handler-class-only filter unchanged).
- Build: 0 errors, 16 pre-existing warnings (unchanged).
- No new ADR / no `PublicContracts` modification required.
