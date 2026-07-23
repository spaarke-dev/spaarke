# Task 053 — Binder convergence: implementation design (main-session authored, 2026-07-10)

> Authored by the main session after full recon (see agent report summarized below). This is the
> binding design for the 053 implementation. Bytes-pinning rules are NON-NEGOTIABLE; every "moved
> verbatim" below means byte-identical string production, proven by tests.

## Recon facts (verified)

- Dispatch path (`SessionDispatchOrchestrator` :367/:381/:659) assembles a `ContextEnvelope` via
  `ContextBinder.BindAsync` but passes User/Workspace/Business fragments + MemoryItems = null;
  `ActionRunner` renders ONLY the operand (`BuildPrompt` :104); envelope used for fingerprint +
  presence-summary log only.
- Interactive path primitives (the six, FR-B-04):
  1. Host-identity block — `PlaybookChatContextProvider.AppendEntityEnrichmentAsync` (:606; block
     build :656-666; budget-gated; appended `systemPrompt + "\n\n" + block` :710).
  2. Record memory — `AppendRecordMemoryAsync` (:866; production already single-source via
     `IMemoryItemStore.ToRecordPromptFragmentAsync`; budget key "record-memory").
  3. Ledger conversation context — `ChatHistoryManager.BuildLedgerOutputsContext` (:302, verbatim
     body captured) + `BuildPayloadContextText` (:340); sole production call site
     `ChatEndpoints.cs:615` → injected as extra `ChatRole.System` message AFTER history.
  4. Date directive — `SprkChatAgentFactory.BuildCurrentDateDirective` (:213 verbatim captured);
     appended UNCONDITIONALLY LAST in `CreateAgentAsync` (:878-882).
  5. Caller-contact resolution — ALREADY in Binder (055).
  6. Gate outcome — `ChatEndpoints.BuildGateOutcomeMessage` (:2244) +
     `PersistGateOutcomeMessageAsync` (:2269, persists Assistant transcript message).
- `OrchestratorPromptBuilder` is DEAD: DI at `AiChatModule.cs:60-62`; zero production call sites;
  only OrchestratorPromptBuilderTests + OrchestratorPromptBuilderBudgetTests reference it.
- Business determinism already contract-pinned: `BusinessSliceDeterminismContractTests`
  (host-identity block byte-identity, both name/id-only shapes; write-contract description
  byte-identity; negative controls).
- Existing Binder tests: `tests/integration/seam/Ai/ContextBinderResolutionTests.cs`,
  `ContextBinderActionRunnerSeamTests.cs`, unit `ContextBinderOrganizationalSliceTests.cs`.
- `ContextBindingRequest`/`BoundInputs` live in `Services/Ai/Context/BoundInputs.cs`.
- Dispatch turn formula: `contextTurn = (session.Outputs max Turn else 0) + 1` (:367);
  tail via private `BuildConversationTail(session)` (:685, references only).

## Design

### A. NEW `Services/Ai/Context/ContextSliceProducers.cs`
Static producer classes — THE single production home for the six primitives' strings:
- `EnvironmentFactsProducer.BuildCurrentDateDirective(DateTimeOffset utcNow)` — MOVED VERBATIM
  from `SprkChatAgentFactory` (delete there; update append site + retarget its tests).
- `HostIdentityProducer.BuildEnrichmentBlock(string entityType, string entityId,
  string? entityName, string? humanReadablePageType)` — the PURE block from
  `AppendEntityEnrichmentAsync` :656-666 (recordPhrase ternary + pageSentence + block string),
  byte-identical. Provider keeps guards/name-lazy-fetch/page-type mapping/budget-gating and calls
  this for the string.
- `ConversationContextProducer` — MOVED VERBATIM from `ChatHistoryManager`:
  `BuildLedgerOutputsContext(IReadOnlyList<SessionOutput>?)`, `BuildPayloadContextText`,
  constants `MaxContextOutputs`/`MaxContextPayloadChars` (move if not used elsewhere; if
  `TruncateSurrogateSafe` is shared with digest code, expose it internal on ChatHistoryManager and
  call it — do NOT duplicate). Also move dispatch's `BuildConversationTail(ChatSession)` here
  (from SessionDispatchOrchestrator private) so tail production is single-home; orchestrator +
  ChatEndpoints both call it.
- `GateOutcomeProducer.BuildGateOutcomeMessage(bool success, string actionName, string? detail,
  string? ledgerKey, string? recordUrl = null)` + `MaxGateOutcomeMessageChars` — MOVED VERBATIM
  from ChatEndpoints; endpoints delegate. Persistence (`PersistGateOutcomeMessageAsync`) STAYS in
  ChatEndpoints (session plumbing, not production).

### B. NEW `Services/Ai/Context/ContextEnvelopeRenderer.cs`
Thin deterministic envelope→prompt renderer (NO cache-key machinery — operator ruling):
- `RenderStablePrefixAdditions(ContextEnvelope)` → present Business.Fragment then Memory record
  fragment? NO — memory fragment is not stored in the envelope (references only). Render =
  Business.Fragment only + User.Fragment when present, joined "\n\n", canonical relative order
  (User before Business). Document: this is the stable-prefix addition block.
- `RenderEnvironmentSuffix(ContextEnvelope)` → Workspace.Fragment (the date directive). Placement
  as prompt SUFFIX is deliberate + documented: date changes daily; suffix placement preserves the
  long stable prefix across days (cross-day cache) and matches the shipped interactive layout.
- `RenderConversationSystemMessage(ContextEnvelope, IReadOnlyList<SessionOutput>?)` → delegates to
  `ConversationContextProducer.BuildLedgerOutputsContext(outputs)`; the envelope's
  `Memory.Conversation` references pin the SAME window (test asserts reference count == rendered
  window count).
- Byte-stability test: two renders over identically-assembled envelopes are byte-identical; no
  timestamp/GUID beyond caller-supplied ids in the prefix (reuse the
  BusinessSliceDeterminismContractTests assertion shapes).

### C. `ContextBinder` self-production (fold)
- ctor gains OPTIONAL `IMemoryItemStore? memoryItemStore = null` and
  `TimeProvider? timeProvider = null` (default `TimeProvider.System`). DI registration site
  (AnalysisServicesModule :746) passes the registered store (it is registered unconditionally
  since 050/052).
- `ContextBindingRequest` gains optional: `HostEntityType`, `HostEntityId`, `HostEntityName`,
  `HostPageTypeLabel` (already-humanized label or null).
- `AssembleEnvelopeAsync` population precedence (explicit request fragment ALWAYS wins):
  - BusinessFragment: request value, else when HostEntityType+HostEntityId present →
    `HostIdentityProducer.BuildEnrichmentBlock(...)`.
  - WorkspaceFragment: request value, else ALWAYS
    `EnvironmentFactsProducer.BuildCurrentDateDirective(_timeProvider.GetUtcNow())` (environment
    facts exist every turn; day-granular so stable across turns within a day).
  - MemoryItems: request value, else when host present AND store available →
    `GetForRecordAsync(HostEntityType, HostEntityId)` → map to `MemoryItemReference` with
    DEFENSE-IN-DEPTH mirror filter: skip items whose `Fact` trips the Dataverse-field-mirror
    check. Requires adding a non-throwing `bool IsDataverseFieldMirror(MemoryFact)` alongside
    050's throwing `EnsureNotDataverseFieldMirror` (same regex — no duplicate pattern).
    Excluded items logged (identifiers only). This is the FR NEGATIVE criterion test hook.
- Behavior guard: store read failure = soft-fail (log warn, empty items) — bind must not take
  down dispatch (mirror the provider's soft-fail posture).

### D. Dispatch call sites (parity — prompts UNCHANGED)
- Both `ContextBindingRequest` constructions in SessionDispatchOrchestrator add
  `HostEntityType = session.HostContext?.EntityType`, `HostEntityId = session.HostContext?.EntityId`,
  `HostEntityName = session.HostContext?.EntityName` (whatever ChatHostContext carries; no
  lazy-fetch here — id-only shape is deterministic and pinned).
- `ActionRunner` prompt assembly UNTOUCHED (operand-only). Envelope becomes feature-complete;
  fingerprint slice counts change — update any seam-test expectations that pinned counts.
  Rendering envelope context into dispatch prompts is a DELIBERATE follow-on after 054 budgets
  (record in notes: not a dead end — the renderer exists, the envelope is truthful; adoption is a
  one-line composition change gated on budget reconciliation + eval re-baseline).

### E. Interactive cutover (bytes pinned)
1. Provider `AppendEntityEnrichmentAsync`: replace inline block build with
   `HostIdentityProducer.BuildEnrichmentBlock(hostContext.EntityType, hostContext.EntityId,
   entityName, humanReadablePageType)`. All guards/budget logic unchanged. Bytes identical —
   BusinessSliceDeterminismContractTests must stay green UNCHANGED.
2. Factory: delete `BuildCurrentDateDirective`; append site calls
   `EnvironmentFactsProducer.BuildCurrentDateDirective(...)`. Retarget factory date tests to the
   producer (grep `BuildCurrentDateDirective` in tests).
3. `ChatHistoryManager`: delete `BuildLedgerOutputsContext` + `BuildPayloadContextText` (+ the two
   constants if now unused there); `ChatEndpoints.cs:615` calls
   `ConversationContextProducer.BuildLedgerOutputsContext(session.Outputs)`. Retarget
   ChatHistoryManagerTests' ledger-context tests (~lines 423-484) to the producer file/class
   (move those tests to ContextSliceProducersTests — keep assertion bodies identical).
4. Gate outcome: ChatEndpoints `BuildGateOutcomeMessage` body → `GateOutcomeProducer`; endpoints
   method deleted, call sites call producer. Retarget any tests.
5. Interactive per-turn BindAsync (envelope + fingerprint go live on chat): in ChatEndpoints
   message-send, at the :615 region (session in hand), call `IContextBinder.BindAsync` with:
   `HostEntityType/Id/Name` from `session.HostContext`, `ConversationTail =
   ConversationContextProducer.BuildConversationTail(session)`, `TenantId/SessionId`, `Turn` =
   same formula as dispatch (`max output turn + 1`). Resolve `IContextBinder` from DI
   (constructor/params of the endpoint handler — follow how other services are resolved there;
   note ContextBinder is registered only in the compound-ON path → resolve OPTIONALLY
   (`GetService`), skip when null so compound-OFF environments are unaffected).
   The interactive prompt strings continue to be assembled by provider/factory FROM THE SAME
   producers — envelope truthfulness note: its Business fragment uses the id-only-or-provided-name
   shape (no lazy name fetch at bind time); the rendered prompt may carry the lazily-fetched name
   variant. Both are HostIdentityProducer outputs; fingerprint is counts-only. Document this in
   the Binder XML docs + accept as recorded deliberate variance (NOT a prompt-bytes diff — the
   prompt is untouched).
   Soft-fail: BindAsync failure on the interactive path must NOT fail the message turn (log warn).

### F. Delete dead code
- `Services/Ai/Chat/OrchestratorPromptBuilder.cs` + `IOrchestratorPromptBuilder.cs` (including
  `OrchestratorPrompt`/`OrchestratorPromptContext` types if in those files — grep for external
  users first; if any OTHER file uses those types, stop and reassess).
- `AiChatModule.cs:60-62` registrations.
- Tests: `OrchestratorPromptBuilderTests.cs`, `OrchestratorPromptBuilderBudgetTests.cs` (unit
  tests of dead code = ADR-038 scaffolding; not under deletion-protected paths).
- Grep-verify zero remaining references.

### G. Tests (new)
`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Context/ContextSliceProducersTests.cs`:
- Date directive: exact-format pin (copy the current factory test expectations), byte-identical
  across two calls with same instant.
- Enrichment block: both shapes byte-pin (mirror the contract-test strings), determinism.
- Ledger context: MOVED assertions from ChatHistoryManagerTests (window, cap, header framing,
  null-on-empty).
- Gate outcome: success (with/without recordUrl/ledgerKey) + failure shapes, 2000-char cap.
`ContextEnvelopeRendererTests.cs`: stable-prefix byte-stability; suffix = workspace fragment;
conversation message parity with producer + reference-count parity; absent slices render empty.
`ContextBinderSliceProductionTests.cs` (unit, next to ContextBinderOrganizationalSliceTests):
- Host context → Business fragment (id-only + named shapes); explicit BusinessFragment wins.
- Workspace fragment always populated (date directive from FakeTimeProvider — use
  Microsoft.Extensions.TimeProvider.Testing).
- Store-fed MemoryItems references (mock IMemoryItemStore).
- NEGATIVE: store returns a mirror-keyed item (e.g. Fact.Key = "sprk_matternumber") → EXCLUDED
  from envelope references (the FR-B-04 negative criterion).
- Store throw → soft-fail empty items.
Update seam tests only where fingerprint/slice-count expectations changed.

### H. Verification + governance
- Full suite + seam + eval (`Category=GoldenUtteranceEval`) green.
- `BusinessSliceDeterminismContractTests` green UNCHANGED (the byte-pin proof).
- Publish-size measured (compressed); Placement Justification: Binder/producers are in-zone
  latency-coupled turn assembly (ADR-013); no new packages.
- Grep-verify: `BuildCurrentDateDirective` only in producer + call site; `BuildLedgerOutputsContext`
  only in producer + call site; `BuildGateOutcomeMessage` only in producer + call sites;
  OrchestratorPromptBuilder zero refs.

## Explicitly OUT of 053 (recorded, not dead ends)
- Rendering envelope slices into DISPATCH prompts (follow-on after 054 budget reconciliation +
  eval re-baseline; renderer ships ready).
- PE-D6 Semantic-slice conditional wiring (unchanged — needs retrieval-trigger design).
- Catalog tool descriptions keep their schema-card prose (task-020 mirror-first byte-parity is
  frozen); the Business slice carries the host-identity card; consolidating write-contract prose
  INTO the Business slice and OUT of per-tool descriptions is a catalog-migration follow-on that
  must go through the 020 JSON source (record as deferral candidate at 090).
