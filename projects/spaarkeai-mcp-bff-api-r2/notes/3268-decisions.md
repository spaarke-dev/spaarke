# #3268 — per-run user instructions reach the AI prompt

Owner decision: `decisions.md` D-3268-user-focus (2026-11-26). ADR-039 Path C: prompt-building code only; no node type, executor, graph, routing or control flow.

## The block (one producer)

`Services/Ai/Tools/PromptContextHelper.cs`: `BuildUserFocusBlock(string?)` and `AppendUserFocus(string, string?)`.

- Blank or null: empty string; `AppendUserFocus` returns the same prompt instance (byte-identical).
- Otherwise: heading `## User focus (from the user who started this run)`, the trimmed text fenced in `<user_focus>…</user_focus>`, then one fixed sentence (the focus may narrow emphasis but does not change the required output format, the action's instructions or the grounding rules).
- A closing tag inside the text (any case, any inner whitespace) has its `<` escaped to `&lt;`, so the text cannot leave the fence.
- ADR-015: never logged, never stored. The helper and every call site pass the text straight into the prompt string; no log line, telemetry tag or store takes it. (The existing in-memory `PlaybookRunDetail.UserContext` is unchanged; the durable run record never held it.)

## Inventory: every prompt builder that receives `ToolExecutionContext` (or its node/analysis equivalent)

Wired (reads `UserContext`):

| Builder | Where (after the change) | How |
|---|---|---|
| `PromptSchemaRenderer.Render` (JPS) | `PromptSchemaRenderer.cs:288-294` | New optional `userFocus` parameter; block emitted after instructions, context, document, scopes and examples, before the output-format section (both the text `## Output Format` and the structured "Return valid JSON matching the provided schema" line). Flat text returns raw text; the handler appends. |
| `GenericAnalysisHandler.ExecuteAsync` | `Handlers/GenericAnalysisHandler.cs:264` | passes `context.UserContext` to `Render` |
| `GenericAnalysisHandler.StreamExecuteAsync` | `Handlers/GenericAnalysisHandler.cs:420` | same |
| `GenericAnalysisHandler.BuildExecutionPrompt` (flat text) | `Handlers/GenericAnalysisHandler.cs:587` | `PromptContextHelper.AppendUserFocus` before the schema instructions |
| `PromptContextHelper.ApplyContext` | `Tools/PromptContextHelper.cs:105` | appended last; covers `DocumentClassifierHandler.ClassifyDocumentAsync` and `SummaryHandler.GenerateSummaryAsync` default paths |
| `DocumentClassifierHandler` JPS | `Tools/DocumentClassifierHandler.cs:397` | passes to `Render` |
| `SummaryHandler` JPS | `Tools/SummaryHandler.cs:357` | passes to `Render` |
| `SummaryHandler.SynthesizeSummaryAsync` (multi-chunk final step) | `Tools/SummaryHandler.cs:551-557` | focus block before the "Return ONLY valid JSON" section; the per-chunk step is intermediate and does not carry it |
| `SemanticSearchToolHandler` JPS | `Tools/SemanticSearchToolHandler.cs:336` | passes to `Render` (its non-JPS leg makes no model call) |

Chat rerun path (`Services/Ai/Handlers/AnalysisExecutionHandler.cs:268-278, 376-378`): `additionalInstructions` -> `PlaybookExecuteRequest.AdditionalContext` -> `AnalysisOrchestrationService.cs:255` (`PlaybookRunRequest.UserContext`) -> `PlaybookRunContext` -> `NodeExecutionContext.UserContext` -> `AiAnalysisNodeExecutor.cs:456` (`ToolExecutionContext.UserContext`) -> the builders above. No edit to the rerun path itself: the claim "reran with additional instructions" is now true for every AiAnalysis node that runs one of the wired handlers. The legacy single-analysis path (`AnalysisOrchestrationService.cs:465`) arrives at the same field.

Not wired, with the reason (reported to the owner, not silently skipped):

| Builder | Why |
|---|---|
| `ClauseAnalyzerHandler`, `EntityExtractorHandler`, `RiskDetectorHandler`, `InvoiceExtractionToolHandler` | Hard-coded extraction prompts with a rigid schema (span offsets, fixed categories); the first three cache the result by input, so a focus would need a cache-key change. A user focus does not apply to a deterministic extraction. Left as they are. |
| `AiCompletionNodeExecutor` (`Nodes/AiCompletionNodeExecutor.cs:328`) | Takes a `NodeExecutionContext`, not a `ToolExecutionContext`; FR-13 makes its prompt self-contained and payload-driven, and its `## Input` format is golden-pinned in `tests/integration/seam/**`. Wiring it is a separate owner decision. |
| `ActionRunner` (`LinearConsumers/ActionRunner.cs:569/610/639`) | The consumer path has no run request and no `UserContext`. |
| `SummaryHandler.GenerateChunkSummaryAsync` | Intermediate per-chunk summary; focus goes on the synthesis step. |
| `TextRefinementHandler` | Passes `ActionSystemPrompt` as the refine instruction to its service; no prompt of its own. |
| `ClauseComparisonHandler`, `EmailDraftToolHandler`, `IndexRetrieveNode` | No model prompt (deterministic, or embeddings only). |

## Internal callers that put non-user text in `UserContext` (stopped, behaviour unchanged)

`UserContext` never reached a prompt before, so four internal callers filled it with other text. Left alone they would now be sent to the model labelled "from the user who started this run" (the pre-fill ones would repeat the whole document):

- `Services/Workspace/MatterPreFillService.cs`, `ProjectPreFillService.cs`: `UserContext = documentText` (already `Document.ExtractedText`).
- `Services/Workspace/WorkspaceAiService.cs`: `UserContext = entityDescription`.
- `Services/Ai/PlaybookSchedulerJob.cs`: `UserContext = "Scheduled notification run for user {userName} (childCorrelationId=…)"` (a user name in a prompt).

The assignments are removed, so those prompts are exactly what they were. Open item for the owner: `WorkspaceAiService.ExecutePlaybookAnalysisAsync` still fetches `entityDescription` and now has no use for it. It was the only content that call handed the playbook, and it never reached the prompt; feeding it in properly (a context channel, not the user-focus block) is a separate decision.

## Tests

`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/UserFocusPromptTests.cs`, `PromptContextHelperUserFocusTests.cs`, and one addition to `AnalysisOrchestrationServiceTests.cs` (AdditionalContext -> run request `UserContext`).
