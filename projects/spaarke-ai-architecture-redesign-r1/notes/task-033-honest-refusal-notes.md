# Task 033 — Honest Refusal Binding + dispatch_refused telemetry (FR-P2-04) — Task Notes

> Date: 2026-07-06 · Wave W-P2-B · Executed under task-execute FULL rigor.

## What shipped

The fourth outcome of the G-P2 contract (do / clarify-then-do / cited ad-hoc answer / **honest refusal**) as a first-class loop outcome:

| Contract clause | Implementation |
|---|---|
| Refusal template is CATALOG data (ADR-039: routing config only in the Binding table; no hardcoded copy) | Per-tenant `no_match_handler` Binding row (`sprk_playbookconsumer`) targeting the **REF-CHAT@v1** prompted Action; the refusal copy — including the claimable-capability list — lives in the Action's JPS (`notes/jps/REF-CHAT-v1.jps.json`); makers edit it as the catalog grows |
| Refusal is a LOOP OUTCOME, not a fallback dispatcher (no re-detection, no second intent mechanism) | `RefusalCapabilityTool` (new, `Services/Ai/Chat/`) projects the Binding into the loop's tool list via the SAME `ListTextProjectableBindingsAsync` read as every capability (factory FR-P2-01 block); the MODEL invoking the tool IS the refusal decision. A deterministic "Grounded Outcomes" system-prompt directive (appended only when the tool survived pre-filter; constant text — NFR-04 cache-stable) pins the four-outcome contract and forbids free-form apologies/ungrounded answers |
| Ledger write BEFORE render (ADR-040) | Tool executes `IActionRunner` (prompted render) → `IOutputRouter.RouteAsync` (addressable `SessionOutput {bindingId}@t{n}`) → only THEN returns; the relayed text is read back FROM the STORED entry (`ExtractRefusalText(routed.Entry.Payload)`) — render follows store |
| `dispatch_refused` telemetry (NFR-09/ADR-016; identifiers only, NFR-07) | `AiTelemetry` counter **`dispatch_refused`** (meter `Sprk.Bff.Api.Ai`, already exported via `TelemetryModule.AddMeter` + `UseAzureMonitor`) with bounded dims `render_status ∈ {rendered, render_failed}` + `tenant.id`; companion structured log `[FR-P2-04][dispatch_refused]` carries tenant/session/binding/ucid/ledger-key identifiers (traces). No utterance content anywhere; the model's `unsupported_request` label added to `AgentTurnContract.AlwaysRedactedArgNames` so the ToolChain audit redacts it by NAME |
| Refusals never suspend | Terminal turn outcome; no `PendingPlanManager` interaction (task 031's store is for write-shaped dispatch) |
| File-less sessions refuse correctly | Why `SessionDispatchOrchestrator` could NOT be reused for execution: its dispatch envelope requires session files with extractable text ("No session files were available" at zero files). `RefusalCapabilityTool` converges on the same executor + ledger seam (`ActionRunner` → `OutputRouter`) with no file requirement — §11 justification in the class doc-comment |

## Why a consumer-type discriminator is not a "tool-name gate"

The factory's projection loop maps the `no_match_handler` consumer type to `RefusalCapabilityTool` (all other opted-in rows → `BindingCapabilityTool`). This is catalog-data-driven projection-class selection — the same sanctioned pattern as `ConsumerTypes.ChatSummarize` resolution — not gating dispatch/side effects by tool-name lists (the ADR-039 ban). The refusal capability is the canonically-named platform component (§3.10.7.2 Layer 4 "per-tenant no_match_handler Consumer").

## Dataverse rows created (spaarkedev1, 2026-07-06, via Dataverse MCP `create_record`)

| Row | GUID | Key values |
|---|---|---|
| `sprk_analysisaction` **REF-CHAT@v1** | **`8d337be2-3d79-f111-ab0e-7ced8ddc4cc6`** | `sprk_actioncode=REF-CHAT@v1`, `sprk_name=Honest Refusal for Chat`, `sprk_kind=100000000 (Prompted)`, `sprk_modeltier=100000000 (Fast)`, `sprk_temperature=null` (deterministic 0.0 default), `sprk_inputschema` = typed args `{unsupported_request: string, required}`, `sprk_systemprompt` = REF-CHAT@v1 JPS (artifact: [`notes/jps/REF-CHAT-v1.jps.json`](jps/REF-CHAT-v1.jps.json)), `sprk_outputschemajson` = strict `{refusal: string}` (mirrored at `infra/dataverse/outputschemas/ref-chat-v1.schema.json`) |
| `sprk_playbookconsumer` **no_match_handler** | **`48dcd7ec-3d79-f111-ab0e-7ced8ddc4cc6`** | `sprk_consumertype=no_match_handler`, `sprk_consumercode=default`, `sprk_environment=*`, `sprk_priority=500`, `sprk_enabled=true`, `sprk_ucid=L4-REFUSAL`, `sprk_disposition=100000000 (Informational)`, `sprk_risk=100000000 (None)`, `sprk_capturemode=100000000 (Loop Elicitation)`, `sprk_surfaces=assistant`, `sprk_chiptransitions=[]`, `sprk_tooldescription` = maker-authored refusal intent surface (with required `unsupported_request` arg contract), `sprk_action` → REF-CHAT@v1 |

Post-create `read_query` round-trip confirmed all Binding column values (transcript 2026-07-06). `ConsumerTypes.NoMatchHandler` constant added + `ConsumerTypes.All` extended — FR-P0-04 constants↔rows parity holds (row exists; `RoutingConsumerTypeHealthCheckTests` generate rows from `ConsumerTypes.All` dynamically, all green).

## Legacy no-match fallback disposition (NFR-08)

The pre-redesign no-match surface is `DispatchResult.NoMatch` + `PlaybookCandidateSelection.NoMatch` inside the legacy `PlaybookDispatcher`/`PlaybookCandidateSelector`/`IntentRerankerService` pre-pass in `ChatEndpoints.SendMessageAsync`. Per the task brief hard boundary, **task 034 (hard cutover) deletes those call sites wholesale** — deleting them here would collide with 034's grep-zero acceptance. Nothing in the refusal path falls back to, re-detects through, or renders from that stack. Grep evidence (no hardcoded refusal copy; refusal template referenced only via catalog reads):

- `grep -rn "no_match_handler" src/` → hits only in `ConsumerTypes.NoMatchHandler` (constant), `RefusalCapabilityTool` (consumer-type guard + docs), `SprkChatAgentFactory` (projection discriminator + directive tool-name derivation) — no template copy, no appsettings key (shown in transcript).
- `grep -rn "no_match\|NoMatchHandler" src/server/api/Sprk.Bff.Api/appsettings*.json` → zero (routing config only in the Binding table).

## App Insights evidence (step 5) — deploy-gated, deferred to W-P2 gate

The deployed `spaarke-bff-dev` build predates this branch — the refusal tool cannot be exercised against dev until the W-P2 gate deploy (same deferral pattern as task 030's prompt-cache-hit-rate + `/healthz/catalog` flip). Local emission is proven by unit test against the REAL `Sprk.Bff.Api.Ai` meter instrument (MeterListener — `dispatch_refused` measurement with `render_status`/`tenant.id` dims shown in test output). Post-deploy 1-query verification for the gate:

```kusto
// customMetrics — the counter (refusal-backlog aggregate)
customMetrics
| where name == "dispatch_refused"
| extend renderStatus = tostring(customDimensions["render_status"]), tenant = tostring(customDimensions["tenant.id"])
| summarize refusals = sum(valueCount) by renderStatus, tenant, bin(timestamp, 1h)

// traces — the per-refusal identifier log line
traces
| where message startswith "[FR-P2-04][dispatch_refused]"
| project timestamp, message, customDimensions
```

UAT trigger: in the Assistant on spaarkedev1, type an off-catalog utterance (e.g. "translate this NDA into Spanish") → tenant refusal template renders in chat + `SessionOutput` ledger entry + both query results above non-empty.

## Eval suite (NFR-02/NFR-06)

- `golden-utterances.json`: +2 refusal cases — **GU-041** (canonical Scenario D "translate this NDA into Spanish") and **GU-042** (off-domain action "book me a flight…"); both `outcomeClass=refuse`, `schemaConformance=REF-CHAT@v1`, phase P2 (NL-loop dispatch assertion activates with task 037 per the suite's pending-by-design contract).
- New LIVE assertion `P2RefusalSurface_NoMatchHandlerBinding_ResolvesAndProjectsThroughTheClosedCatalog`: drives the REAL `ConsumerRoutingService.ListTextProjectableBindingsAsync` (the exact projection read the factory performs) over a stub row shaped like the seeded spaarkedev1 row; asserts Binding → REF-CHAT@v1 Prompted target + tool-description opt-in; constructs the `RefusalCapabilityTool` projection (name/description from catalog); pins the `ref-chat-v1.schema.json` contract (single required `refusal`, `additionalProperties=false`).

## Integration notes for task 034 (and 032)

- **034 (hard cutover)**: the refusal is fully loop-native — when 034 deletes the `PlaybookDispatcher` pre-pass, no-match stops being a `DispatchResult.NoMatch` silent fall-through and is ONLY reachable as the loop invoking `capability_no_match_handler`. Nothing in this task references the legacy stack; 034 can delete it without touching the refusal path. The "Grounded Outcomes" directive lives in the factory's FR-P2-04 block (after tool-projection finalization) — keep it when relocating/simplifying factory code.
- **032 (elicitation)**: no shared files modified except `SprkChatAgentFactory.cs` (FR-P2-01 projection loop + new FR-P2-04 directive block) and `AgentTurnContract.cs` (one entry added to `AlwaysRedactedArgNames`) — both additive. Refusals never suspend: `RefusalCapabilityTool` never touches `PendingPlanManager` or ledger `Gate` markers. If 032's elicitation directive also appends to `context.SystemPrompt`, order is irrelevant (both deterministic constants).

## Files created/modified

**Created**: `Services/Ai/Chat/RefusalCapabilityTool.cs` · `infra/dataverse/outputschemas/ref-chat-v1.schema.json` · `notes/jps/REF-CHAT-v1.jps.json` · `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/RefusalCapabilityToolTests.cs` · this notes file.
**Modified**: `Services/Ai/PublicContracts/ConsumerTypes.cs` (NoMatchHandler) · `Telemetry/AiTelemetry.cs` (dispatch_refused counter + RecordDispatchRefused) · `Services/Ai/Chat/AgentTurnContract.cs` (redaction name) · `Services/Ai/Chat/AgentToolProjection.cs` (pre-filter covers RefusalCapabilityTool surfaces) · `Services/Ai/Chat/SprkChatAgentFactory.cs` (projection discriminator + grounded-outcomes directive) · `tests/integration/contract/Eval/golden-utterances.json` (GU-041/042) · `tests/integration/contract/Eval/GoldenUtteranceEvalSuiteTests.cs` (P2 refusal-surface live fact).

## Publish size (ADR-029 / NFR-01) — measured 2026-07-06 on the full W-P2 wave tree (030+031 committed + 032+033 working tree)

- `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` → 146 MB uncompressed, 270 files.
- **48.91 MB compressed** (Compress-Archive Optimal, full output incl. 4 PDBs) · **46.18 MB compressed excluding PDBs**.
- Baseline (task 030 notes): 45.59 MB. Delta on the PDB-exclusive figure: **+0.59 MB for the ENTIRE W-P2-B wave (tasks 032 + 033 combined)** — zero NuGet/package changes anywhere in the wave (`git diff *.csproj` empty vs the 030 baseline commit; third-party payload compresses to 41.71 MB with identical composition), so the growth is app-assembly IL only. Task 033's own contribution: one new 368-line source file + ~120 lines of edits (tens of KB of IL). All thresholds clear: ≪ +5 MB single-task escalation, ≪ 55 MB review line, ≪ 60 MB hard ceiling. Note for the W-P2 gate: record whether the project baseline convention includes PDBs (48.91 vs 46.18 discrepancy is purely the 4 PDB files).

## Test results (2026-07-06, shared tree AFTER task 032 landed)

- Targeted: `RefusalCapabilityToolTests` (12) + `AgentTurnLoopContractTests` + `RoutingConsumerTypeHealthCheckTests` = **55/55 green**.
- Eval suite (`--filter Category=GoldenUtteranceEval`): **12/12 green** — includes the new `P2RefusalSurface_NoMatchHandlerBinding_ResolvesAndProjectsThroughTheClosedCatalog` live fact. One cross-task fix was required to green the gate: task 032's **GU-046** declared `outcomeClass=clarify` with `consumerType=matter-pre-fill, catalogStatus=planned`, violating BOTH the P0 invariant (clarify → null consumerType) and the planned-must-not-exist rule (`matter-pre-fill` ∈ `ConsumerTypes.All`). Resolution: evolved the suite invariant (clarify cases MAY name the capability under elicitation — extracted `AssertConsumerTypeGrounding` applying full dispatch-grade closed-catalog grounding when they do; refuse cases still MUST be null) + fixed GU-046 to `catalogStatus=existing`. Needs 032-owner acknowledgment in the wave PR.
- Full unit suite: **8059 total — 7953 passed, 101 skipped, 5 failed**; all 5 on the KNOWN pre-existing list (ExecutorConfigSchemas, KnowledgeDeploymentConfig, DailyBriefingCollector resolver, TemplateContextBuilder TextOnly, SessionFilesCleanup) — identical to task 032's run. **Zero failures attributable to task 033.**

## Step 9.5 quality gates (2026-07-06)

- **code-review: PASS** — 0 Critical, 4 Warnings (accepted/documented), no code fixes required:
  - W1: `RefusalCapabilityTool.InvokeCoreAsync` is a ~130-line orchestration method (>3 concerns by the checklist) — accepted; mirrors the `BindingCapabilityTool.InvokeCoreAsync` / `SessionDispatchOrchestrator.DispatchAsync` dispatch-pipeline shape; splitting would scatter the ADR-040 ledger-before-render ordering contract across methods.
  - W2: unit-test placement outside the 6 KEEP paths — consistent with the entire pre-reorganization Chat unit suite (same acceptance as task 030 W5); the eval additions ARE at the KEEP path (`tests/integration/contract/**`). Defend at /test-diet.
  - W3: each refusal costs one Fast-tier LLM render — FR-P2-04's explicit design (prompt-controlled template); if refusal volume grows, a deterministic-render mode could be added as catalog data. Product observation, not a defect.
  - W4: cross-task edit of 032's GU-046 + the suite clarify invariant during shared-tree verification (see Test results).
  - AI-smell scan: 0 new interfaces (reused IActionRunner/IOutputRouter/IScopeResolverService seams), no catch-log-rethrow (catch performs recovery: grounded degradation + telemetry), 0 DI registrations added, no banned test antipatterns (no Mock<HttpMessageHandler>, no ctor-null tests, no DI-registration tests).
  - BFF Hygiene §10: no packages (CVE check N/A — csproj untouched), no endpoints, no DI registrations; placement justification in class doc-comment (§11 three-question) + this note; publish-size verified above.
- **adr-check: PASS — 0 violations** across ADR-039 (one dispatch protocol — refusal is a loop outcome via projected tool; no re-detection; no tool-name gating; routing config grep-zero outside the Binding table), ADR-040 (ledger write before render, test-proven; addressable `{bindingId}@t{n}` key; append-only; disposition-only rendering), ADR-010 (no interfaces/DI added), ADR-013 (all types in-zone `Services/Ai/**`; no CRUD→AI dependency), ADR-015/NFR-07 (identifiers only; `unsupported_request` redacted by NAME in the ToolChain audit; refusal payload Tier-3 ledger only), ADR-016/NFR-09 (`dispatch_refused` counter with bounded dims per R5SummarizeTelemetry cardinality discipline + structured identifier log line), ADR-038 (eval additions at KEEP path; W2 placement warning accepted). No §6.5 resolution path needed.
