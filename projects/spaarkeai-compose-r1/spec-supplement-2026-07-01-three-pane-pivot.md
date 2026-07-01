# Spec Supplement — R1 Three-Pane Completion + Streaming Assistant Pane

> **Supplements**: [`spec.md`](spec.md) (baseline R1 spec)
> **Reason for supplement**: R1 UAT 2026-07-01 surfaced that the baseline spec
> implicitly assumed the "SpaarkeAi canonical three-pane UX" (Path A "reuses
> existing SpaarkeAi pattern" per design.md §14 row 3). During implementation
> the Path A modal launch was shortcut to a direct `<ComposeWorkspace>` mount,
> bypassing `<ThreePaneShell>`. This shortcut is **NOT** R1's intended shape.
> This supplement formalizes the three-pane completion as R1 scope, plus the
> Assistant-pane streaming that was consistently the intended AI-response
> surface (dispatched via `PaneEventBus` `conversation` channel already —
> just never wired end-to-end).
> **Not new scope**: this closes gaps between implicit spec assumptions and
> current implementation.
> **Related R3 seed**: fidelity work (preserving DOCX formatting, track
> changes, comments) at [`../spaarkeai-compose-r3/README.md`](../spaarkeai-compose-r3/README.md) — genuinely new scope, tracked separately.

---

## Baseline vs. Current Implementation Gap

| Component | Baseline spec assumption | Current R1 implementation (2026-07-01) | Gap |
|---|---|---|---|
| Modal launch (Path A) | Reuses SpaarkeAi canonical three-pane UX | Direct `<ComposeWorkspace>` mount, bypasses `<ThreePaneShell>` | Shortcut — three panes never render |
| AI response surface | Assistant / Conversation pane (streaming, chat-like) | Fixed banner in `ComposeBannerStack` (non-streaming) | Wrong surface |
| BFF `/api/compose/action/{consumerType}` | Streaming SSE (matches other AI endpoints — `/api/workspace/files/summarize`, `/api/ai/chat/*`) | Aggregated JSON (single response) | Wrong endpoint contract |
| `IInvokePlaybookAi` facade | Supports document-context invocation | Parameter-only; hardcodes `DocumentIds = []` per M365 Copilot adapter path (verified [`InvokePlaybookAi.cs:65-73`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/InvokePlaybookAi.cs#L65)) | Facade too narrow for document-context consumers |
| ComposeWorkspace packaging | Shared lib (mounts via LegalWorkspace's `composeEditor.registration.ts`) | Lives in SpaarkeAi solution; not importable by LegalWorkspace registration (would invert dependency graph) | Packaging violation of Pattern D from Calendar |
| Workspace-tab suppression | Compose mode locks user to Compose surface (no Matters/Documents/DailyBriefing browsing) | Workspace tabs never surface because ThreePaneShell isn't rendered in compose mode | Latent — will materialize when three-pane lands |
| Modal size | 80% × 80% (per user 2026-07-01) | 90% × 90% (baseline) | Trivial |

**All rows above are R1 completion work**, not R2 scope. They close the
gap between R1's canonical UX intent and current implementation.

---

## New Functional Requirements (R1 supplement)

### FR-S1: Three-pane shell as canonical Compose surface (P0)

**Description**: All Compose launches (Path A modal, LegalWorkspace section, direct URL) MUST render `<ThreePaneShell>` inside the modal chrome. The workspace pane (center) mounts `<ComposeWorkspace>`; the Assistant pane (right) is the Conversation pane; the Context pane (left) shows document metadata. Direct-mount of `<ComposeWorkspace>` at the App.tsx root is forbidden.

**Acceptance**:
- Path A modal → SpaarkeAi Code Page → `<ThreePaneShell>` → three panes visible
- Workspace pane displays `<ComposeWorkspace>` with the DOCX loaded
- Assistant pane displays the Conversation pane (chat-like, streaming-ready)
- Context pane displays a Document context section (basic — matter, doc metadata; extensible)

### FR-S2: ComposeWorkspace shared-lib packaging (P0)

**Description**: `ComposeWorkspace` + its child components + its hooks MUST live in `@spaarke/compose-components` so LegalWorkspace's `composeEditor.registration.ts` can import it without circular dependencies (mirrors the Calendar Pattern D precedent).

**Acceptance**:
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.tsx` exists (moved from `src/solutions/SpaarkeAi/...`)
- `composeEditor.registration.ts` swaps `ComposeWorkspacePlaceholder` for real `<ComposeWorkspace>` import
- SpaarkeAi + LegalWorkspace + Compose lib all build clean
- No import from `src/solutions/SpaarkeAi/` back into shared libs (dependency graph unidirectional)

### FR-S3: SSE streaming for `/api/compose/action/{consumerType}` (P0)

**Description**: The endpoint MUST emit `Content-Type: text/event-stream` events matching the pattern established in [`WorkspaceFileEndpoints.SummarizeFileSse`](../../src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceFileEndpoints.cs) and `/api/ai/chat/*`. Progress events (context_loaded, extracting_text, analyzing, streaming) + terminal `[DONE]` sentinel + error events. The aggregated-JSON response shape is retired.

**Acceptance**:
- `POST /api/compose/action/compose-summarize` returns `text/event-stream`
- Emits `AnalysisStreamChunk`-shaped events (or equivalent)
- Ends with `data: [DONE]\n\n` sentinel
- Errors emit an error event, not a 500

### FR-S4: Facade extension for document-context invocation (P0)

**Description**: `IInvokePlaybookAi` MUST accept an optional `documentText` (UserContext) and `DocumentContext` on the invocation. The facade forwards these to `PlaybookRunRequest.UserContext` and `PlaybookRunRequest.Document`. Existing parameter-only callers (M365 Copilot Agent) continue to work — the new args are optional.

**Acceptance**:
- New overload on `IInvokePlaybookAi` (either new method or optional args on existing)
- `InvokePlaybookAi` impl forwards `userContext` → `PlaybookRunRequest.UserContext`
- `NullInvokePlaybookAi` accepts + no-ops
- ComposeEndpoints.DispatchAction uses the new overload with extracted document text

### FR-S5: Server-side DOCX text extraction (P0)

**Description**: Given a `sprk_document` + SPE drive-item id, the BFF MUST extract the DOCX plain text server-side using `DocumentFormat.OpenXml` (already in `Sprk.Bff.Api.csproj` at 3.4.1). Text is passed as `userContext` to the playbook facade.

**Acceptance**:
- New `IDocxTextExtractor` service in `Sprk.Bff.Api.Services.Compose`
- Extracts prose paragraphs (via `<w:t>` elements walk); ignores headers/footers/comments/revision marks
- Bounded (< 100 KB text output; larger docs truncated with warning)
- Unit-tested against known-good DOCX fixtures

### FR-S6: ConversationPane consumes `compose_summarize_request` events + streams SSE (P0)

**Description**: The Conversation (Assistant) pane MUST subscribe to `compose_summarize_request` events on the `conversation` channel. On receipt, it opens a fetch stream to `/api/compose/action/compose-summarize`, consumes SSE events, and progressively renders response tokens in its chat-like surface. The banner-based Summary UI in `ComposeBannerStack` is removed for the summary case (banner stack keeps import warnings + errors).

**Acceptance**:
- ConversationPane handles `compose_summarize_request` events
- Fetch loop consumes SSE incrementally (browser `fetch` + `ReadableStream` OR `EventSource`)
- Tokens render progressively; final aggregated text is complete
- Existing chat functionality is preserved
- `ComposeBannerStack` no longer renders summary states

### FR-S7: Workspace-tab suppression in compose mode (P1)

**Description**: When `composeMode=editor` is active, the workspace-tab UI (Matters/Documents/DailyBriefing/etc.) MUST be hidden. Compose-focused widgets remain addable via the widget-registration mechanism (extensibility preserved for future R2+ widgets — no hardcoded exclusion list beyond "hide non-Compose layouts").

**Acceptance**:
- In compose mode, the workspace layout picker shows only "Compose" (or is hidden)
- Widget-add mechanism remains functional
- Non-compose layouts (Home / Matters / etc.) are not shown to the user during compose mode

### FR-S8: Modal size 80% × 80% (P2, trivial)

Modal launched via `openSpaarkeAiCompose` uses `width/height: 80% × 80%` (was 90% × 90%).

---

## Non-Functional Requirements (R1 supplement)

### NFR-S1: BFF publish size stays ≤ 60 MB compressed (per CLAUDE.md §10 #4)

Adding DocxTextExtractor uses `DocumentFormat.OpenXml` which is already in the transitive dep graph. Zero new NuGet. Publish size delta MUST be ≤ +1 MB (from code additions only).

### NFR-S2: Bundle size impact tracking

SpaarkeAi bundle currently 4761 KB. Moving ComposeWorkspace into shared lib is bundle-size neutral (same code, different module path — Vite tree-shaking already inlines).

### NFR-S3: Backward compatibility

Existing M365 Copilot Agent path (`IInvokePlaybookAi.InvokePlaybookAsync(playbookId, parameters, context, ct)`) MUST continue to work unchanged. The document-context overload is additive.

### NFR-S4: Test coverage

- `IDocxTextExtractor` unit tests against fixture DOCX files
- ComposeEndpoints SSE integration test (in-process) verifying event stream shape
- ConversationPane SSE consumption test (component test with mocked EventSource)

---

## Success Criteria Updates

**SC4** (Path A modal launch) is redefined:

**Was**: "Path A modal launch works against real `sprk_document`" — verified by Compose editor loading DOCX in modal.

**Is**: "Path A modal launch renders the canonical three-pane SpaarkeAi shell inside the modal, with `<ComposeWorkspace>` mounted in the Workspace pane, ConversationPane on the right, Context pane on the left, and non-Compose workspace tabs suppressed."

**SC9-live** (Compose Summarize E2E) is redefined:

**Was**: `compose-summarize` E2E against deployed BFF + real playbook.

**Is**: `compose-summarize` E2E against deployed BFF via SSE streaming into the ConversationPane. Response tokens render progressively as they arrive. Terminal aggregation matches the playbook's terminal output.

**New SC-S1**: Facade extension shipped — `IInvokePlaybookAi` supports document-context invocation without breaking existing M365 Copilot Agent parameter-only path.

---

## ADR Tensions (revisit)

R1 baseline spec declared "No ADR tensions surfaced at design time." This supplement introduces one tension:

### Tension: ADR-013 refined facade contract vs. document-context invocation

**ADR affected**: refined ADR-013 ([`.claude/adr/ADR-013-bff-ai-extraction.md`](../../.claude/adr/ADR-013-bff-ai-extraction.md))

**Rule challenged**: The refined facade `IInvokePlaybookAi` is intentionally narrow (parameter-only) so CRUD code never touches AI internals. Extending it to accept `userContext` + `Document` widens the facade surface.

**Conflict**: A first-class document-context consumer (Compose) needs to invoke playbooks with document text. Options:
- Widen the facade (this supplement's approach)
- Keep facade narrow; have Compose bypass it via `IPlaybookOrchestrationService` (violates ADR-013 spirit)
- Have Compose consume via a different endpoint (e.g., `/api/workspace/files/summarize`) — routes around the facade

**Path chosen**: **Path B — ADR-013 amendment**. The amendment adds: *"The refined facade `IInvokePlaybookAi` MAY accept optional document-context arguments (`userContext`, `Document`) for consumers that dispatch playbooks against a specific document. This preserves the CRUD/AI boundary while supporting document-scoped analysis flows."*

**Rationale**: Path B keeps the facade as the canonical CRUD→AI boundary while extending it minimally to support the second-class of use case (document-context). The alternative — Compose bypassing the facade — sets a bad precedent that CRUD code injects AI internals whenever the facade is inconvenient.

**Impact if amendment accepted**: minor — `IInvokePlaybookAi` interface changes; two implementations update (real + Null-Object); existing callers (M365 Copilot Agent) continue to work (new args are optional).

**File amendment**: to be filed as part of this supplement's Phase 4 wrap-up.

---

## Task Decomposition

The following tasks execute this supplement. Task numbers 091–102 continue the existing R1 numbering. Wrap-up task 090 is superseded by 110 (renumbered wrap-up that includes ADR-013 amendment filing).

| Task | Description | Phase | Dependencies | Est |
|---|---|---|---|---|
| 091 | Move ComposeWorkspace + hooks + types to `@spaarke/compose-components` (packaging refactor) | Phase 7 (three-pane) | — | 3h |
| 092 | Change App.tsx: remove Path A special-case; render ThreePaneShell always; pass composeMode context via layout hint | Phase 7 | 091 | 1h |
| 093 | Swap FU-3 placeholder → real `<ComposeWorkspace>` in composeEditor.registration.ts | Phase 7 | 091, 092 | 1h |
| 094 | Add `IDocxTextExtractor` service (DocumentFormat.OpenXml) + unit tests | Phase 8 (streaming SSE) | — | 2h |
| 095 | Extend `IInvokePlaybookAi` with document-context overload | Phase 8 | 094 | 1.5h |
| 096 | Update `InvokePlaybookAi` + `NullInvokePlaybookAi` implementations | Phase 8 | 095 | 1h |
| 097 | Convert `/api/compose/action/{consumerType}` endpoint to SSE (reuse `WriteSSEAsync` pattern) | Phase 8 | 094, 095, 096 | 2h |
| 098 | ConversationPane: handle `compose_summarize_request` events + consume SSE + progressive render | Phase 9 (Assistant wiring) | 097 | 2.5h |
| 099 | Remove summary states from `ComposeBannerStack`; keep for import warnings + errors | Phase 9 | 098 | 0.5h |
| 100 | Workspace-tab suppression in compose mode | Phase 10 (polish) | 092 | 1.5h |
| 101 | Modal 80×80 in launch-resolver | Phase 10 | — | 0.1h ✅ done 2026-07-01 |
| 102 | ADR-013 amendment file (path B) + concise note in defer-issues.md | Phase 10 | 095 | 0.5h |
| 110 | (renumbered) Project wrap-up with expanded scope: /test-diet + code-review + adr-check + this supplement's success criteria | Wrap-up | all above | 0.5h |

**Total effort estimate**: ~16.5 hours across 12 tasks. Realistic across 2 focused sessions.

---

## References

- Baseline spec: [`spec.md`](spec.md)
- Design decisions: [`design.md`](design.md) — especially §14 (locked decisions) row 3 (Path A modal reuses SpaarkeAi three-pane) which this supplement formalizes
- Current-task supplement: [`current-task.md`](current-task.md) — updated to point at this supplement
- Plan extension: [`plan.md`](plan.md) — phases 7-10 added
- Options analysis for Summarize: [`notes/summarize-fix-options.md`](notes/summarize-fix-options.md) — Option A chosen (facade extension + server-side DOCX extraction)
- Word-fidelity R3 seed: [`../spaarkeai-compose-r3/README.md`](../spaarkeai-compose-r3/README.md)
- Refined ADR-013: [`.claude/adr/ADR-013-bff-ai-extraction.md`](../../.claude/adr/ADR-013-bff-ai-extraction.md) — to be amended per Path B

---

*This supplement was authored during R1 UAT close-out session 2026-07-01. It formalizes work that closes gaps between the baseline spec's implicit assumptions and the current implementation, plus the streaming Summarize UX that was consistently the design intent (via `PaneEventBus` `conversation` channel dispatch) but never wired end-to-end.*
