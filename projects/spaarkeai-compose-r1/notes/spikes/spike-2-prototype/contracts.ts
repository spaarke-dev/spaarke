/**
 * Spaarke Compose — Spike #2 Three-Pane Coordination Contracts (LOCKED)
 *
 * Project:    spaarkeai-compose-r1
 * Spike:      #2 — Three-pane coordination wiring
 * Authority:  This file is the LOCKED contract surface for Phase 2 / Phase 4
 *             implementation (tasks 041, 042, 043). Six interfaces, one per
 *             flow per design.md §5. Receivers may be stubs in R1.
 * Source-of-truth: design.md §5 (Six coordinated flows) + §14 row 2 (HostContext
 *             non-extension) + §11 (component reuse map — PaneEventBus).
 * Status:     LOCKED 2026-06-29. Promotion target = a production module to be
 *             created in Phase 4 (task 041 — "Create six TypeScript data-contract
 *             interfaces"). Promotion path: copy this file (or a normalized form)
 *             into `src/solutions/SpaarkeAi/src/types/compose.ts` AND
 *             `src/client/shared/Spaarke.AI.Widgets/src/widgets/compose/contracts.ts`
 *             (or equivalent shared lib). Do NOT modify the contract shapes
 *             without surfacing as an ADR Conflict (CLAUDE.md §6.5).
 *
 * Design constraints (BINDING):
 *  1. Reuse the existing PaneEventBus contract from `@spaarke/ai-widgets`
 *     (`src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts`).
 *     Do NOT create a parallel event bus. The four existing channels
 *     (`workspace`, `context`, `conversation`, `safety`) carry every flow.
 *  2. Per design.md §14 row 2: DO NOT extend HostContext. Transient editor
 *     state (selection span, focused clause, artifact type) is PAYLOAD ONLY,
 *     consumed and discarded.
 *  3. Per refined ADR-013 (2026-05-20): the BFF AI dispatch path is
 *     `IConsumerRoutingService` + `IInvokePlaybookAi`. Frontend payloads that
 *     trigger AI dispatch carry the JPS scope name (`compose-selection` /
 *     `compose-document`) so the BFF facade can resolve the playbook.
 *     Frontend never speaks directly to AI internals (`IOpenAiClient`,
 *     `IPlaybookService`).
 *  4. ADR-015 Tier 3: any field that COULD carry user content from the editor
 *     (selection text, full document text) is annotated below with a privacy
 *     tier note. Tier 3 work-history (in-memory + opt-in chat memory) is the
 *     ONLY allowed sink; trace channels (`context.tool_call_*`, etc.) MUST
 *     NOT log these fields.
 *  5. R1 deliverable: contracts defined + Flows 1, 2, 5 wired with stub
 *     receivers logging payloads. Flows 3, 4, 6 are contract-only in R1
 *     (no runtime wiring required; promoted in R2).
 *
 * Channel mapping (per design.md §5 flow → existing PaneEventBus channel):
 *  | Flow | Direction              | Channel dispatched on | Event discriminant     | R1 runtime? |
 *  |------|------------------------|-----------------------|------------------------|-------------|
 *  | 1    | Workspace → Context    | `context`             | `compose_selection_changed` (additive) | YES wire   |
 *  | 2    | Workspace → Assistant  | `conversation`        | `compose_selection_offer` (additive)   | YES wire   |
 *  | 3    | Context → Workspace    | `workspace`           | `compose_context_insert` (additive)    | stub only  |
 *  | 4    | Context → Assistant    | `conversation`        | `compose_context_offer` (additive)     | stub only  |
 *  | 5    | Assistant → Workspace  | `workspace`           | `compose_assistant_insert` (additive)  | YES wire   |
 *  | 6    | Assistant → Context    | `context`             | `compose_assistant_insight` (additive) | stub only  |
 *
 * Additive-channel rule (ADR-030): the existing four PaneEventBus channels
 * carry the new Compose discriminants additively. NO new channel is
 * introduced. Existing subscribers tolerate unknown event.type values per
 * ADR-030's additive-types rule.
 *
 * IMPORTANT — `unknown` is preferred over `any` per ADR-030. Subscribers
 * MUST narrow on `event.type` before reading discriminant-specific fields.
 */

// ---------------------------------------------------------------------------
// Shared types — pointers, not entities
// ---------------------------------------------------------------------------

/**
 * Stable pointer to a document open in Compose.
 *
 * SPE drive-item id is the canonical pointer because it works for both
 * `sprk_document`-bound docs AND ephemeral docs (Path B per design.md §8).
 * Once an ephemeral doc is promoted to a Document on first Save, the
 * `sprk_documentid` field is also populated — but `speDriveItemId` remains
 * the identity field for session/session-bus correlation.
 *
 * Privacy: identifier only (Tier 1 safe). Not a user-content surface.
 */
export interface ComposeDocumentRef {
  /** SPE drive-item id (always present). Canonical identity. */
  speDriveItemId: string;
  /** Dataverse `sprk_documentid` (present after first-Save promotion). */
  sprkDocumentId?: string;
  /** Optional human-readable file name for UI labelling. */
  fileName?: string;
  /** SPE container id (multi-tenant scoping). */
  containerId?: string;
}

/**
 * Editor selection span — anchors a region of the open document.
 *
 * Per design.md §14 row 2: this is TRANSIENT payload-only state. Subscribers
 * consume and discard. The selection is NOT persisted on the ChatSession.
 *
 * Privacy: `selectionText` is user-content (Tier 3). Subscribers that bridge
 * to LLM prompts MAY include it. Subscribers that bridge to telemetry MUST
 * strip it (mirror the rule on `workspace.user_selection.selectionText`
 * declared in PaneEventTypes.ts).
 */
export interface ComposeSelection {
  /** ProseMirror "from" position (inclusive, character index in TipTap doc). */
  from: number;
  /** ProseMirror "to" position (exclusive). */
  to: number;
  /**
   * The selected text payload.
   *
   * **CAP: ≤2000 characters.** Mirrors design.md §FR-20 selection-span sizing
   * (R2 Explain/Replace actions need ≥1 clause; legal clauses are typically
   * <500 chars but defensively allow 2000). Dispatchers MUST truncate at
   * source. Subscribers SHOULD assert and discard oversized payloads.
   *
   * **Tier 3 only** per ADR-015. Subscribers bridging to telemetry strip
   * this field before logging. Subscribers bridging to LLM prompts include
   * it (that is the design intent).
   */
  selectionText: string;
  /**
   * Short human-readable context label (e.g. "Heading 2", "Clause 3.4",
   * "Table cell"). Optional; emitters supply when ProseMirror node-type
   * narrowing is cheap. Subscribers use for chip preview labelling.
   *
   * Tier 1 safe (configuration metadata).
   */
  contextLabel?: string;
}

// ---------------------------------------------------------------------------
// Flow 1 — Workspace → Context
// ---------------------------------------------------------------------------

/**
 * Flow 1: Workspace → Context. User selects a clause in the Compose editor;
 * the Context pane surfaces matching precedent, playbook entries, prior
 * negotiation history.
 *
 * Channel: `context` (additive discriminant `compose_selection_changed`).
 * Producer: Compose editor surface (ComposeEditor.tsx; ComposeWorkspace.tsx
 *           in R1). Dispatches on TipTap selection-change debounce.
 * Consumer (R1): stub — Context pane subscribes, logs payload, no UI change.
 * Consumer (R2): Context pane drives precedent/playbook/history lookup.
 *
 * Dispatcher contract:
 *   dispatch('context', { type: 'compose_selection_changed', documentRef, selection, sessionId, timestamp });
 *
 * Subscriber contract (R2):
 *   - Narrow on event.type === 'compose_selection_changed'
 *   - Fetch precedent matches via existing precedent lookup service
 *   - Update Context pane right-rail list
 *
 * Frequency: debounced per editor selection-change (target: 250ms debounce
 * to avoid spamming during text drag).
 */
export interface ComposeWorkspaceToContextFlow {
  /** Always 'compose_selection_changed' — additive discriminant on `context` channel. */
  type: 'compose_selection_changed';

  /** Document pointer (Tier 1 safe). */
  documentRef: ComposeDocumentRef;

  /** Selection payload (Tier 3 — user-content via `selectionText`). */
  selection: ComposeSelection;

  /**
   * ChatSession id correlating this flow to the active Compose session
   * (existing ChatSession infrastructure per design.md §6).
   * Tier 1 safe (deterministic identifier).
   */
  sessionId: string;

  /**
   * ISO-8601 UTC timestamp at which the selection event fired.
   * Tier 1 safe (metadata).
   */
  timestamp: string;
}

// ---------------------------------------------------------------------------
// Flow 2 — Workspace → Assistant
// ---------------------------------------------------------------------------

/**
 * Flow 2: Workspace → Assistant. User selects text; Assistant offers
 * playbook actions ("Explain / Replace with standard / Compare / Draft
 * alternative") via the JPS `compose-selection` scope.
 *
 * Channel: `conversation` (additive discriminant `compose_selection_offer`).
 * Producer: Compose editor surface — emits when selection settles (debounce)
 *           AND the selection meets a minimum-size threshold (e.g. ≥10 chars).
 * Consumer (R1): ConversationPane subscribes, logs payload, no UI change
 *                beyond a chip preview (matching existing selection_changed
 *                behaviour on `workspace` channel).
 * Consumer (R2): ConversationPane renders action menu (Explain / Replace /
 *                Compare / Draft alternative) bound to JPS scope inputs.
 *                Action invocation routes through `POST /api/compose/action/
 *                {consumerType}` per design.md §12 + FR-21.
 *
 * Dispatcher contract:
 *   dispatch('conversation', { type: 'compose_selection_offer', documentRef, selection, jpsScope, sessionId, timestamp });
 *
 * Why this is on `conversation` channel (not `workspace`):
 *  - The existing `workspace.selection_changed` already exists for chip
 *    preview routing. Compose's flow goes deeper: it announces availability
 *    of JPS-scope-compatible inputs for AI action dispatch. ConversationPane
 *    is the natural primary subscriber.
 *  - This is additive: `conversation.compose_selection_offer` does NOT
 *    replace `workspace.selection_changed`; both fire (the chip preview
 *    flow runs in parallel via existing mechanism).
 */
export interface ComposeWorkspaceToAssistantFlow {
  /** Always 'compose_selection_offer' — additive on `conversation` channel. */
  type: 'compose_selection_offer';

  /** Document pointer (Tier 1 safe). */
  documentRef: ComposeDocumentRef;

  /** Selection payload (Tier 3 — `selectionText` is user-content). */
  selection: ComposeSelection;

  /**
   * JPS scope name binding this offer to a registered scope.
   *
   * R1 value: always `'compose-selection'`.
   * R2+ may add more scopes (e.g. `'compose-clause-explain'`).
   *
   * The Assistant uses this to look up which playbook actions are valid
   * for this selection context. The BFF endpoint POST
   * `/api/compose/action/{consumerType}` resolves via
   * `IConsumerRoutingService.ResolveAsync(consumerType, jpsScope, ...)`
   * (refined ADR-013 facade — NOT direct AI internals).
   *
   * Tier 1 safe (configuration identifier).
   */
  jpsScope: 'compose-selection';

  /** ChatSession id. Tier 1 safe. */
  sessionId: string;

  /** ISO-8601 UTC timestamp. Tier 1 safe. */
  timestamp: string;
}

// ---------------------------------------------------------------------------
// Flow 3 — Context → Workspace (CONTRACT-ONLY in R1; stub receiver)
// ---------------------------------------------------------------------------

/**
 * Flow 3: Context → Workspace. User drags a precedent clause from Context
 * pane; it drops into the editor at cursor position.
 *
 * Channel: `workspace` (additive discriminant `compose_context_insert`).
 * Producer (R2): Context pane drag/drop handler.
 * Consumer (R1): Compose editor subscribes, logs payload (NO insertion).
 * Consumer (R2): Compose editor inserts content at cursor with
 *                provenance trail.
 *
 * R1 status: contract defined, stub receiver. The shape must be locked
 * even though the runtime is deferred to R2 — otherwise R2 retrofits
 * break Compose's editor.
 *
 * Privacy: `contentHtml` carries clause text (Tier 3 — same status as
 * selection payload).
 */
export interface ComposeContextToWorkspaceFlow {
  /** Always 'compose_context_insert' — additive on `workspace` channel. */
  type: 'compose_context_insert';

  /** Target document. Tier 1 safe. */
  documentRef: ComposeDocumentRef;

  /**
   * Source identifier — the precedent / playbook clause being inserted.
   * Stable ID from the precedent / clause-library system (TBD which
   * Dataverse entity in R2). Tier 1 safe.
   */
  sourceClauseId: string;

  /**
   * The content payload to insert into the editor.
   *
   * **Format**: serialized HTML or ProseMirror JSON. Final shape is a
   * Spike #1 outcome (TipTap OOB roundtrip determines DOCX↔ProseMirror
   * canonical form). Receivers narrow on a `format` discriminant at
   * runtime.
   *
   * **CAP**: ≤32 KB serialized (typical clauses are <2 KB; defensive
   * upper bound for full templates).
   *
   * Tier 3 (carries clause text content).
   */
  contentHtml: string;

  /**
   * Format hint for the content payload. R1 contract supports both;
   * R2 narrows per Spike #1 outcome.
   *
   * - `'html'` — raw HTML markup string
   * - `'prosemirror-json'` — ProseMirror document JSON serialization
   *
   * Tier 1 safe (enum-like configuration).
   */
  format: 'html' | 'prosemirror-json';

  /**
   * Target insertion position in the editor. Optional — if absent,
   * receiver uses current cursor position.
   *
   * Tier 1 safe (numeric position).
   */
  insertAt?: number;

  /** ChatSession id. Tier 1 safe. */
  sessionId: string;

  /** ISO-8601 UTC timestamp. Tier 1 safe. */
  timestamp: string;
}

// ---------------------------------------------------------------------------
// Flow 4 — Context → Assistant (CONTRACT-ONLY in R1; stub receiver)
// ---------------------------------------------------------------------------

/**
 * Flow 4: Context → Assistant. User says "Use this precedent" on a Context
 * pane entry; Assistant takes it as a tool input for the next action.
 *
 * Channel: `conversation` (additive discriminant `compose_context_offer`).
 * Producer (R2): Context pane action handler.
 * Consumer (R1): ConversationPane subscribes, logs payload (no UI change).
 * Consumer (R2): ConversationPane stages the precedent as a JPS scope
 *                input for the next playbook invocation.
 *
 * R1 status: contract defined, stub receiver. Locking the shape now
 * prevents R2 retrofit churn.
 *
 * Privacy: `contentHtml` carries clause text (Tier 3).
 */
export interface ComposeContextToAssistantFlow {
  /** Always 'compose_context_offer' — additive on `conversation` channel. */
  type: 'compose_context_offer';

  /** Active document for context (informational; receiver may use). */
  documentRef: ComposeDocumentRef;

  /** Source precedent / clause id. Tier 1 safe. */
  sourceClauseId: string;

  /**
   * Content payload to offer as scope input. Same format + cap as Flow 3.
   * Tier 3 (carries clause text content).
   */
  contentHtml: string;

  /**
   * JPS scope name to which this offer binds.
   *
   * R1 value: always `'compose-document'` (whole-doc scope — receiver
   * treats the precedent as document-level input). R2 may add narrower
   * scopes.
   *
   * Tier 1 safe.
   */
  jpsScope: 'compose-document';

  /** ChatSession id. Tier 1 safe. */
  sessionId: string;

  /** ISO-8601 UTC timestamp. Tier 1 safe. */
  timestamp: string;
}

// ---------------------------------------------------------------------------
// Flow 5 — Assistant → Workspace
// ---------------------------------------------------------------------------

/**
 * Flow 5: Assistant → Workspace. Assistant drafts text (output of a JPS
 * playbook action like Draft Alternative); the draft inserts into the
 * editor at cursor with a provenance trail.
 *
 * Channel: `workspace` (additive discriminant `compose_assistant_insert`).
 * Producer: ConversationPane after a playbook action completes (R1 smoke
 *           test triggers via `compose-summarize` but does NOT auto-insert
 *           — summary lands in Assistant pane; Flow 5 is for R2 actions
 *           like Draft Alternative that DO insert).
 * Consumer (R1): ComposeEditor subscribes, logs payload + on user confirm
 *                inserts via TipTap commands. The "user confirm" step is
 *                R1 (avoids auto-injection UX risk pre-actions); the
 *                automatic-insertion variant is R2.
 * Consumer (R2): same path, but with auto-insertion + provenance badge.
 *
 * R1 runtime: Flow 5 is wired with stub receiver that LOGS but does NOT
 * insert (per POML §steps[3]). The smoke test for actual insertion is R2.
 *
 * Privacy: `contentHtml` carries LLM-generated draft text (Tier 3 — same
 * sensitivity tier as user content; LLM outputs can echo input).
 */
export interface ComposeAssistantToWorkspaceFlow {
  /** Always 'compose_assistant_insert' — additive on `workspace` channel. */
  type: 'compose_assistant_insert';

  /** Target document. Tier 1 safe. */
  documentRef: ComposeDocumentRef;

  /**
   * Identifier of the playbook node / action that produced this draft.
   *
   * Format: stable node key (matches `nodeId` on the existing
   * `context.playbook_node_completed` trace event). Receiver uses to
   * render provenance badge ("From playbook X, node Y").
   *
   * Tier 1 safe.
   */
  sourceNodeId: string;

  /**
   * Identifier of the playbook (R1 = Document Summary playbook id
   * `47686eb1-9916-f111-8343-7c1e520aa4df` for the smoke test).
   *
   * Tier 1 safe.
   */
  sourcePlaybookId: string;

  /**
   * Draft content payload to insert. Same format + cap as Flow 3.
   * Tier 3 (LLM-generated content).
   */
  contentHtml: string;

  /**
   * Format hint. Same enum as Flow 3.
   * Tier 1 safe.
   */
  format: 'html' | 'prosemirror-json';

  /**
   * Insertion mode signal.
   *
   * - `'replace-selection'` — replace currently-selected range
   * - `'insert-at-cursor'`  — insert at current cursor position
   * - `'append'`            — append to end of document
   *
   * Receiver chooses based on action semantics; emitters supply this so
   * different playbook node types can drive different insertion behaviour.
   * Tier 1 safe.
   */
  insertMode: 'replace-selection' | 'insert-at-cursor' | 'append';

  /**
   * If true, R2 auto-inserts; if false, R1+R2 require user-confirm.
   * R1 stub receiver ignores this flag (logs only).
   * Tier 1 safe.
   */
  requireUserConfirm: boolean;

  /** ChatSession id. Tier 1 safe. */
  sessionId: string;

  /** ISO-8601 UTC timestamp. Tier 1 safe. */
  timestamp: string;
}

// ---------------------------------------------------------------------------
// Flow 6 — Assistant → Context (CONTRACT-ONLY in R1; stub receiver)
// ---------------------------------------------------------------------------

/**
 * Flow 6: Assistant → Context. Assistant produces a derived insight
 * (extracted entity, identified risk, summary statement); the insight
 * persists to matter knowledge graph via Context pane.
 *
 * Channel: `context` (additive discriminant `compose_assistant_insight`).
 * Producer (R2): ConversationPane / orchestrator after a JPS action
 *                yields an insight node-output.
 * Consumer (R1): Context pane subscribes, logs payload (no persistence).
 * Consumer (R2): Context pane persists to matter knowledge graph + renders
 *                in right-rail "derived insights" list.
 *
 * R1 status: contract defined, stub receiver. The matter knowledge graph
 * persistence mechanism is itself a deferred surface (R2 work).
 *
 * Privacy: `insightText` carries LLM-generated insight text (Tier 3).
 */
export interface ComposeAssistantToContextFlow {
  /** Always 'compose_assistant_insight' — additive on `context` channel. */
  type: 'compose_assistant_insight';

  /** Source document. Tier 1 safe. */
  documentRef: ComposeDocumentRef;

  /**
   * Insight kind — enum-like configuration identifier. Receiver routes
   * to the right knowledge-graph projection.
   *
   * R1 set is a starter; R2 expands:
   *  - `'summary'`      — document-level summary
   *  - `'risk'`         — identified risk / red flag
   *  - `'entity'`       — extracted entity (party, date, monetary value)
   *  - `'clause-type'`  — clause classification
   *  - `'recommendation'` — drafting recommendation
   *
   * Tier 1 safe.
   */
  insightKind: 'summary' | 'risk' | 'entity' | 'clause-type' | 'recommendation';

  /**
   * Insight text. Cap: ≤4000 characters (longer than selection cap
   * because insights are derivative narratives).
   *
   * Tier 3 (LLM-generated content).
   */
  insightText: string;

  /**
   * Optional source span linking the insight back to the document region
   * it was derived from. When present, Context pane can render a
   * "Highlight in document" affordance.
   *
   * Tier 1 safe (numeric positions).
   */
  sourceSpan?: { from: number; to: number };

  /**
   * Identifier of the producing playbook node — matches existing
   * `context.playbook_node_completed.nodeId` trace event.
   * Tier 1 safe.
   */
  sourceNodeId: string;

  /** ChatSession id. Tier 1 safe. */
  sessionId: string;

  /** ISO-8601 UTC timestamp. Tier 1 safe. */
  timestamp: string;
}

// ---------------------------------------------------------------------------
// Aggregate event-type unions per channel (for subscriber narrowing)
// ---------------------------------------------------------------------------

/**
 * Compose-specific additions to the existing `workspace` channel.
 * Subscribers narrow first on event.type === 'compose_*' to handle
 * Compose flows distinctly from existing workspace events.
 */
export type ComposeWorkspaceEvent =
  | ComposeContextToWorkspaceFlow
  | ComposeAssistantToWorkspaceFlow;

/**
 * Compose-specific additions to the existing `context` channel.
 */
export type ComposeContextEvent =
  | ComposeWorkspaceToContextFlow
  | ComposeAssistantToContextFlow;

/**
 * Compose-specific additions to the existing `conversation` channel.
 */
export type ComposeConversationEvent =
  | ComposeWorkspaceToAssistantFlow
  | ComposeContextToAssistantFlow;

/**
 * Catch-all union of every Compose flow event for diagnostics / logging.
 */
export type AnyComposeFlowEvent =
  | ComposeWorkspaceToContextFlow
  | ComposeWorkspaceToAssistantFlow
  | ComposeContextToWorkspaceFlow
  | ComposeContextToAssistantFlow
  | ComposeAssistantToWorkspaceFlow
  | ComposeAssistantToContextFlow;

// ---------------------------------------------------------------------------
// Stub receiver contract — R1 logging-only subscribers
// ---------------------------------------------------------------------------

/**
 * Stub receiver function type for R1 contract-only flows.
 *
 * Stub receivers in R1 subscribe to the appropriate PaneEventBus channel,
 * narrow on event.type, and call `logFlowEvent(...)`. They do NOT perform
 * runtime side-effects (no insertion, no persistence, no UI mutation).
 *
 * Promotion to R2: replace `logFlowEvent` with the real handler. The
 * subscriber/dispatcher contract on this file does NOT change.
 *
 * @example
 * // R1 stub:
 * usePaneEvent('workspace', (event) => {
 *   if (event.type === 'compose_assistant_insert') {
 *     logFlowEvent('flow-5', event as ComposeAssistantToWorkspaceFlow);
 *   }
 * });
 *
 * // R2 real:
 * usePaneEvent('workspace', (event) => {
 *   if (event.type === 'compose_assistant_insert') {
 *     const flow = event as ComposeAssistantToWorkspaceFlow;
 *     await applyInsertion(flow);
 *   }
 * });
 */
export type StubReceiver = (flowName: string, event: AnyComposeFlowEvent) => void;

/**
 * Reference stub-receiver implementation. Receives every flow event,
 * logs to console, returns. Used by the R1 prototype (this directory)
 * and the R1 production stub subscribers (Phase 4 tasks 042 + 043).
 */
export const logFlowEvent: StubReceiver = (flowName, event) => {
  // eslint-disable-next-line no-console
  console.info(`[Compose Flow ${flowName}] event.type=${event.type}`, {
    sessionId: event.sessionId,
    timestamp: event.timestamp,
    documentRef: event.documentRef,
  });
};

// ---------------------------------------------------------------------------
// Channel-routing helpers (declarative; no runtime side-effects)
// ---------------------------------------------------------------------------

/**
 * Channel-routing table — maps each flow to its target PaneEventBus
 * channel. Use this when wiring dispatchers; do NOT inline the mapping
 * elsewhere (single source of truth principle).
 */
export const COMPOSE_FLOW_CHANNEL_MAP = {
  flow1_workspace_to_context: 'context',
  flow2_workspace_to_assistant: 'conversation',
  flow3_context_to_workspace: 'workspace',
  flow4_context_to_assistant: 'conversation',
  flow5_assistant_to_workspace: 'workspace',
  flow6_assistant_to_context: 'context',
} as const;

/**
 * R1-runtime vs R1-stub vs R2-runtime matrix per flow + receiver.
 *
 * R1-runtime  = wired end-to-end; stub receivers actually log on event arrival
 * R1-stub     = contract defined; subscriber registers and receives but no UI change
 * R2-runtime  = full feature behaviour (insertion, persistence, action menu)
 */
export const COMPOSE_FLOW_RECEIVER_MATRIX = {
  flow1_workspace_to_context: {
    r1Wired: true,
    r1Behaviour: 'log + (R2) Context pane right-rail precedent lookup',
    r2Behaviour: 'precedent/playbook/history lookup with rendered results',
  },
  flow2_workspace_to_assistant: {
    r1Wired: true,
    r1Behaviour: 'log + (R1 chip preview) — existing chip preview UX via parallel workspace.selection_changed',
    r2Behaviour: 'render Explain/Replace/Compare/Draft action menu bound to JPS scope',
  },
  flow3_context_to_workspace: {
    r1Wired: false,
    r1Behaviour: 'stub — log payload only; no editor mutation',
    r2Behaviour: 'insert clause content at cursor with provenance trail',
  },
  flow4_context_to_assistant: {
    r1Wired: false,
    r1Behaviour: 'stub — log payload only',
    r2Behaviour: 'stage precedent as JPS scope input for next playbook invocation',
  },
  flow5_assistant_to_workspace: {
    r1Wired: true,
    r1Behaviour: 'log + (R1 manual-Confirm) UI; insertion gated behind explicit user confirm',
    r2Behaviour: 'auto-insertion with provenance badge + redo affordance',
  },
  flow6_assistant_to_context: {
    r1Wired: false,
    r1Behaviour: 'stub — log payload only; no persistence',
    r2Behaviour: 'persist to matter knowledge graph + render in derived-insights rail',
  },
} as const;
