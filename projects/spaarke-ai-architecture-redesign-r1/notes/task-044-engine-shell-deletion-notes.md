# Task 044 — Engine-shell deletions (FR-P3-05) + audit F-1 closure — Task Notes

> Date: 2026-07-06 · Wave W-P3-B (serial) · task-execute FULL rigor + TEST-MODIFYING override.
> Frozen-engine rule honored: `git diff --stat` over `PlaybookOrchestrationService.cs` +
> `IPlaybookOrchestrationService.cs` + `Services/Ai/Nodes/**` is EMPTY (shown in transcript).

## What was deleted (grep-zero shown in transcript, NFR-08)

| Target | Files | Callers re-pointed to |
|---|---|---|
| Engine shell (`Services/Ai/PlaybookExecutionEngine.cs` + its interface file) | 2 + tests | InsightsOrchestrator now calls `IPlaybookOrchestrationService.ExecuteAsync` directly with the request-scoped `HttpContext` (the shell's batch method was a pass-through); the shell's conversational + chat-summarize methods had ZERO live callers (chat-summarize cut over at P1 task 020). `SessionState` (live Builder-SSE DTO) relocated to `Services/Ai/SessionState.cs`; every other DTO in the interface file was dead and died with it. |
| Summarize orchestrator shell (+ Null peer) | 2 + 2 test files | `SummarizeSessionEndpoint` resolves the chat-summarize Binding via `IConsumerRoutingService.ResolveBindingAsync` and delegates to **`SessionDispatchOrchestrator.DispatchAsync` — the ONE dispatch seam** (4th caller after the loop's BindingCapabilityTool, chip clicks, gate-resolve). |
| `AnalysisOrchestrationService` legacy path (R7 FR-11) | — | Already deleted by R7 Wave 4 task 042 (2026-06-28); this task swept the remaining tombstone comments so the method name greps zero. |
| File-summarize wrapper (+ Null peer) | 2 | `WorkspaceFileEndpoints.HandleSummarize` composes `IActionResolver` + `IActionRunner` inline (behavior preserved verbatim). **The engine fall-through (`RunSummarizePlaybookAsSSEAsync`, used when the Binding row had no Action target) was DELETED** — a row without an Action now surfaces as an SSE error chunk, never an engine dispatch (hard cutover; this was the last summarize-file engine leg). New ADR-032 peers `NullActionResolver`/`NullActionRunner` keep the unconditional endpoint startable compound-OFF (same `ai.linear-consumers.disabled` errorCode). |
| Document-profile wrapper (+ its dead result DTO) | 2 | `AnalysisEndpoints.ExecuteDocumentProfilePipelineAsync` — pipeline moved verbatim into the (compound-gated) endpoint, composing `IActionResolver`/`IDocumentTextSource`/`IActionRunner`/`IDocumentDataverseService`/`IPostUploadIndexingEnqueuer`. The `/execute` fall-through for non-profile playbooks (ai-summary et al.) continues on the FROZEN `IPlaybookOrchestrationService` directly — already shell-free, untouched. |
| F-1 legs: generic playbook dispatcher + analysis-query + working-document handlers | 3 + 5 test files | DELETED with their facade triangle (interface + impl + Null peer + `PlaybookInvocationResult`/`PlaybookInvocationContext` DTOs), the projector's dynamic-description machinery in `SprkChatAgentFactory` (~330 lines), 5 seed-row JSONs, seed-script entries, and 5 spaarkedev1 catalog rows (deactivated — see `task-044-dataverse-changes.md`). |
| Orphaned R5 summarize telemetry singleton | 2 + tests | Zero emitters remained after the shell deletions — class, DI registration, meter/tracing registrations deleted. |

Combined: **120 files changed, +1,197 / −13,046 lines (net −11,849)**.

## E-2 EngineOutputLedgerAdapter — re-homed, NOT deleted

- Verified before deletion: the adapter's ONLY invocation site was the generic playbook
  dispatcher's chat leg. Relocated to **`AnalysisExecutionHandler.RerunAnalysisAsync`** —
  the sole surviving chat-session-attached engine leg — with ADR-040 ordering preserved:
  ledger `RecordAsync` runs AFTER the engine drain and BEFORE the `document_replace` SSE
  render / the ToolResult return; a ledger-write failure fails the tool call (unstored
  output never renders). Two new maintain-class tests pin the ordering + failure semantics.
- Contract decoupled from the deleted facade DTO: `RecordAsync` now takes a slim
  `EngineRunOutput` record (RunId / TextContent / StructuredData / CitationChunkIds /
  Confidence). The success gate moved to the caller contract (callers record successful
  runs only). Task-040 reverse Binding resolution + interim degrade identity unchanged.
- P3 FR-P3-08 (task 047) adds the record-context leg as the adapter's second caller, as
  already documented on the interface.

## F-1 audit re-trace (task-012 methodology, three deleted handlers + the residual leg)

1. **Grep-zero** for all three handler symbols + the facade triangle across
   `src/ tests/ scripts/ infra/` — SHOWN in transcript (zero hits).
2. **Handler-plane census re-run**: 29 surviving `IToolHandler` implementations; grep for
   app-only Dataverse clients (`IDataverseService`, `IGenericEntityService`,
   `IDocumentDataverseService`, `IAnalysisDataverseService`, `IFieldMappingDataverseService`,
   `TokenCredential`) across `Services/Ai/Handlers/**` returns ZERO injections. The six
   `dataverse.*` handlers remain user-OBO-only (`IDataverseUserClient`, fail-closed).
   F-2c (analysis-query app-only reads by LLM-supplied GUID) and F-2d (working-document
   app-only writes) are closed at the tool plane with their handlers. F-2e
   (RecallSessionFileHandler session-keyed reads) and F-3 (config-plane catalog reads)
   remain per their G-P0 accepted rulings.
3. **No LLM-initiated path reaches the frozen engine's app-only write nodes EXCEPT** the
   documented residual leg below. The loop's tool projection is catalog-only; the five
   catalog rows for the deleted handlers are deactivated on spaarkedev1, so the loop can
   never re-project them.

### Residual F-1 leg — `analysis.rerun` (ESCALATION: standing ruling requested)

`AnalysisExecutionHandler.rerun` (created by task 036) still delegates to
`IAnalysisOrchestrationService.ExecutePlaybookAsync` — the frozen engine, whose node
executors + result persistence run under application identity. This is now the LAST
LLM-reachable app-only engine leg. Status per the caller's directive ("document or
remediate; escalate if remediation is large"): **documented + escalated** — remediation is
large by construction (OBO-migrating the node executors is frozen-engine surgery, out of
scope per spec §Out of Scope; deleting the leg removes a live product capability).

Bounding facts: (a) reach is EXPLICIT in the catalog (`permission_scope=app-only-engine`);
(b) the target playbook + document are BFF-resolved from session context — the LLM supplies
only optional free-text instructions, never target ids, so blast radius is the session's
OWN analysis; (c) capability-gated by `reanalyze`; (d) outputs now ledger-written before
render (E-2 re-home, this task). NOTE for the ruling: the 2026-07-06 G-P2 operator ruling
re-declared the row `side_effect_class=Read`, so rerun executes WITHOUT a confirmation
gate — the ruling should weigh that against the app-only persistence the engine performs.
Recommended disposition: accept-with-note until engine retirement (Track-B / FR-P4-01
re-verifies), or direct a catalog-data risk adjustment.

## Behavior deltas (operator visibility)

1. `/summarize` direct endpoint: FR-04 multi-file interjection chunk retired (all entry
   paths share ONE execution UX); the Binding's transition chips now follow the terminal
   chunk (same as a chip click); the dead `style` body member is accepted-but-ignored
   (wire compatibility; JPS prompt owns style since P1).
2. `/api/workspace/files/summarize`: engine fall-through gone — a summarize-file Binding
   row without an Action target is now an operator-actionable error chunk (it resolved to
   the engine before). The seeded spaarkedev1 row HAS an Action target, so the live path
   is unchanged.
3. Chat loop: the `invoke-playbook`-by-GUID tool is gone from the catalog; "run playbook X"
   style asks now land on capability tools or the honest refusal (ADR-039 posture). The
   system-prompt Session Files manifest + render-routing directives were re-worded
   tool-agnostically (they steered the LLM toward the deleted tool by name).
4. `analysis.rerun` outputs are now addressable ledger entries (`{bindingId}@t{n}` or the
   degrade identity) and join session memory (030's last-8 ledger-outputs context).

## Verification (all SHOWN in transcript)

- **Build**: full solution `dotnet build` — 0 errors.
- **Unit suite**: 7560 total — 7453 passed / 101 skipped / **6 failed, ALL on the known
  pre-existing list** (ExecutorConfigSchemas, KnowledgeDeploymentConfig,
  DailyBriefingCollector resolver, TemplateContextBuilder TextOnly, SessionFilesCleanup,
  AuditLogService flake). Zero failures attributable to task 044. (Suite 7698 → 7560:
  legacy shell tests deleted, new adapter/handler/contract tests added.)
- **Eval suite (NFR-02 merge gate)**: `Category=GoldenUtteranceEval` **35/35 green**.
- **NetArchTest**: 18 passed / 5 failed — the SAME 5 known pre-existing failures
  (ADR-007, ADR-009, ADR-010 ×3); count unchanged → no new arch violations.
- **Frozen engine**: filtered `git diff --stat` over `PlaybookOrchestrationService.cs`,
  `IPlaybookOrchestrationService.cs`, `Services/Ai/Nodes/**` → EMPTY.
- **Grep-zero exception (documented)**: exactly ONE residual textual hit —
  `PlaybookOrchestrationService.cs:1297` (a comment naming the deleted shell). The file is
  the FROZEN surface this task is forbidden to touch; the frozen-engine hard constraint
  outranks literal comment grep-zero. Sweep it when the engine itself retires (Track B).

## Publish size (ADR-029 / NFR-01)

`dotnet publish -c Release` → **46.81 MB compressed** (PowerShell `Compress-Archive
-CompressionLevel Optimal`, incl. PDBs) / 141.43 MB uncompressed / 270 files.
Same-lineage baseline (task 036 method): 46.83 MB → **−0.02 MB net vs the pre-P3 measure
even after waves W-P3-A added create-task/email/briefing capability**; vs task 043's
whole-wave-tree report (~50.02 MB) the same-method measure is **−3.21 MB**. ZERO
csproj/NuGet changes (`git diff HEAD -- *.csproj` shows comment-only) → no new CVE
surface by construction. Far below the 60 MB ceiling / 55 MB review threshold.

## Deferred / follow-ups (for 045 / 046 / 047 / Track B)

- **047 (FR-P3-08)**: wire the record-context leg as `IEngineOutputLedgerAdapter`'s second
  caller (the interface + DI comments already point there). `EngineRunOutput` is the input
  shape.
- **045/046 (client consolidation)**: client comment references to the deleted server
  symbols were re-worded (Compose contracts, DailyBriefing, HardSlashExecutor,
  StructuredOutputStreamWidget, PaneEventTypes examples). No client CODE invoked the
  deleted surfaces (the /summarize + /dispatch + loop wire contracts are unchanged except
  the deltas above). 045's ConversationPane decomposition can rely on `useSseStream`
  tolerating the added `chips` chunk on /summarize (dispatch clients already do).
- **Track B / FR-P4-01**: (1) `DocumentStreamEvent` plumbing (writer + adapter forwarding +
  `ChatInvocationContext.DocumentStreamWriter`) now has ZERO emitters — orphan candidate if
  no P3/P4 handler adopts it; (2) `WorkingDocumentService`/`IWorkingDocumentService` retain
  endpoint + engine-persistence callers (NOT deleted — only the tool-plane leg died);
  (3) `PlaybookCapabilities` write_back/analysis-query capability ids retained for row
  compatibility; (4) the frozen-file comment hit above; (5) `scripts/Fix-SummarizeForChatPlaybookFK.ps1`
  + the two summarize playbook JSONs are engine-era artifacts — sweep with engine retirement;
  (6) residual F-1 `analysis.rerun` leg (escalated above).
- **Doc drift (flagged for the wrap doc-drift-audit, NOT touched per scope)**: guides/
  architecture docs naming the deleted classes (e.g. SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md).

## Step 9.5 quality gates — see transcript (code-review + adr-check reports)
