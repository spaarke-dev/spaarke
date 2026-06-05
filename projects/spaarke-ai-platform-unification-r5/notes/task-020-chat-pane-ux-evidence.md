# Task 020 — D2-11 Chat-Pane Orchestration UX — Evidence

> **Task**: 020 (D2-11) — Chat-pane orchestration UX (file chips, "N files attached" indicator, per-file remove, inline confirmations, multi-file interjection)
> **Wave**: P2-G5 (parallel; siblings 019 (done), 021, 022)
> **Status**: complete (code-authoring only — main session owns commit + build + deploy)
> **Date**: 2026-06-04
> **Executor**: AI Sub-agent (parallel wave)
> **Scope**: CODE AUTHORING — no commit, no push, no npm build (per executor instructions)

---

## 1. Audit-compliance decisions (per `notes/r5-chat-agent-parallel-build-audit.md` §"Task 020")

The R5 audit explicitly asked task 020's sub-agent to evaluate two existing infrastructure components and document the decision (use vs N/A):

### 1.1 `CompoundIntentDetector` — N/A
**Decision**: **N/A. R5 Summarize is read-only; no plan-preview gating required.**

`CompoundIntentDetector` is the chat-agent component that gates WRITE-BACK actions (e.g., "create matter + email client + log activity") behind a plan-preview turn so the user can approve/edit before execution. R5's Summarize flow (`SessionSummarizeOrchestrator` + `InvokeSummarizePlaybookTool`) is **read-only** — it streams a generated summary back to the user but writes nothing to Dataverse, SharePoint Embedded, or any other system of record. Per the audit's explicit guidance: "R5 Summarize is read-only (no write-back to Dataverse / SharePoint), so plan-preview gating is probably N/A." This task confirms that judgment after a full audit of the Summarize tool surface (no `IDocumentMetadataWriter`, `IDataverseCrud`, or other write surface is invoked from the orchestrator).

`CompoundIntentDetector` is NOT invoked, NOT imported, NOT referenced by any task 020 code path.

### 1.2 `PlaybookChatContextProvider` — N/A
**Decision**: **N/A. R5 Summarize is direct-dispatch via the explicit `/summarize` slash command + tri-mode router (task 019); there is no playbook-context-driven UI to load.**

`PlaybookChatContextProvider` loads playbook-context-dependent UI state (capabilities, quick-actions, slash-commands resolved from the active playbook). R5's Summarize trigger is the explicit `/summarize` slash command (hardcoded in `DEFAULT_SLASH_COMMANDS` per task 019's audit-compliant decision; see `notes/r5-chat-agent-parallel-build-audit.md` §"Task 019" — `DynamicCommandResolver` is dormant because the underlying Dataverse field is in flux). The tri-mode routing (`routeSummarizeIntent`) is a pure function consuming only the message text + the host's already-known state (`uploadedFileCount`, `hasActiveWorkspaceDocument`). No playbook context is needed because the playbook is implicit: the `summarize-document-for-chat@v1` playbook is invoked by the chat agent's tool (task 015) once the message reaches the BFF.

`PlaybookChatContextProvider` is NOT invoked, NOT imported, NOT referenced by any task 020 code path.

### 1.3 `DynamicCommandResolver` — N/A (carry-over from task 019)
Already verified by the audit: the Dataverse `sprk_triggerphrases` field is dormant. R5's hardcoded `/summarize` (task 019) is the correct approach today. Task 020 does NOT need to migrate to `DynamicCommandResolver`; the migration is logged as a R6 backlog item ("trigger-phrases re-indexing — Insights r3 work item F-5") in the audit doc.

### 1.4 No parallel chat pane introduced
Per audit guardrail: "extend the existing `ConversationPane.tsx`; do NOT introduce a parallel chat pane file." This task **extends** the existing R3 `ConversationPane.tsx` in place. Net new file count: 1 (the test file at `src/solutions/SpaarkeAi/src/components/conversation/__tests__/ConversationPane.r5.test.tsx`); the test file is colocated per existing project convention and is not a parallel chat pane.

---

## 2. Task-019 TODO resolution map

Task 019 left three load-bearing TODO markers in `ConversationPane.tsx` for this task to pick up. All resolved:

| TODO marker (task 019 evidence note §3) | Location (task 019) | Resolution (task 020) |
|---|---|---|
| `TODO(r5/task-020)` — replace hardcoded `0` with real `uploadedFileCount` | `dispatchSummarizeIntent` body | Replaced with `attachmentChips.length` derived from the new `onAttachmentsChanged` SprkChat callback. The host now mirrors SprkChat's chip array via `useState<AttachmentChip[]>` and derives the count for tri-mode routing. |
| `TODO(r5/tasks-014-015-020)` — dispatch session-files Summarize | `dispatchSummarizeIntent` branch `session-files` | Resolved via the `handleBeforeSendMessage` synchronous interjection emission. Branch (a) falls through to the default SprkChat send (which carries the ready attachments per FR-07's attachments contract; the BFF chat agent's task 015 `InvokeSummarizePlaybookTool` handles dispatch). The R5-specific UI affordance owned by task 020 — the deterministic multi-file combined-summary interjection — is emitted BEFORE the user's message via the new `onBeforeSendMessage` hook. |
| `TODO(r5/task-020)` — open existing wizard via host | `dispatchSummarizeIntent` branch `active-document` | Resolved as **intentional fall-through with documented rationale**. The SpaarkeAi shell does NOT host the R3 `SummarizeFilesDialog` wizard (LegalWorkspace owns it; LegalWorkspace consumers invoke the wizard directly without touching the SpaarkeAi `ConversationPane`). Falling through to the default SprkChat send produces a sensible chat response via the default playbook routing for the active document context. Back-compat preserved by construction. |

**Verification**: `grep -nE "TODO\(r5/task-020\)|TODO\(r5/tasks-014-015-020\)" ConversationPane.tsx` returns 0 hits post-change.

---

## 3. Shared-library extensions (SprkChat) — minimal additive props

Per the POML's ADR-012 + R5 §3.1 reuse mandate, task 020 extends `SprkChat` in `@spaarke/ui-components` with the **smallest possible set of optional, generic callback props** the host needs. All props are context-agnostic (no SpaarkeAi-specific types cross the boundary):

| New prop on `ISprkChatProps` | Purpose | Default behaviour (when absent) |
|---|---|---|
| `onAttachmentsChanged?: (chips: AttachmentChip[]) => void` | Host mirror of chip lifecycle for "N files attached" indicator + `uploadedFileCount` derivation + ready-transition tracking. | No-op — chip lifecycle untouched. |
| `onAttachmentRemoved?: (chip: AttachmentChip, index: number) => void` | Fires BEFORE local splice on dismiss-click so host can cascade to BFF cleanup. | No-op — local splice proceeds as before. |
| `injectLocalMessage?: IChatMessage \| null` | Host-driven one-shot injection of a client-rendered Assistant message (file confirmation, multi-file interjection). | No injection. |
| `onLocalMessageInjected?: () => void` | Fires after SprkChat appends the injected message so host can clear the prop. | Host must clear by null-assignment. |
| `onBeforeSendMessage?: (messageText: string) => void` | Synchronous pre-send hook — host emits deterministic interjections before the user's message + assistant placeholder are appended. | No-op — send proceeds unchanged. |

**Generic shape constraint**: every prop's payload uses primitive types or shapes already in `@spaarke/ui-components` (`AttachmentChip`, `IChatMessage`). NO SpaarkeAi-specific types cross the shared-lib boundary. ADR-012 preserved by inspection.

**Backward compatibility**: every new prop is `?:` optional with explicit no-op fallback. Existing consumers of SprkChat (AnalysisWorkspace, LegalWorkspace, other code pages) are unaffected — they pass none of the new props.

---

## 4. Indicator binding source

Per POML Step 4.1 + Step 10 documentation requirement:

**Chosen source**: `attachmentChips.length` (the host-mirrored copy of SprkChat's `useChatFileAttachment` chips array), populated via the new `onAttachmentsChanged` callback.

**Rationale**:
- `ChatSession.UploadedFiles.length` (BFF manifest) is NOT yet exposed through `useAiSession()` — task 004 ships the BFF model but no `useAiSession` read surface for it exists today (and adding one would be a Phase 3 task, not Phase 2).
- The SprkChat chip array IS the operator-visible truth: a chip in the strip is a file the user has expressed intent to attach. The BFF manifest catches up after the upload completes (or after Summarize sends the attachments inline). For UX purposes the chip count is what the user expects to see in the indicator.
- Per POML Step 5: *"Bind to the active `ChatSession.UploadedFiles.length` (preferred) OR `attachments.length` from the hook if the session-manifest read is not yet available client-side (document the choice)."* — explicit POML allowance.

**Future migration**: when `useAiSession()` exposes `uploadedFileCount` (or a derived `IChatSession.UploadedFiles`), swap the binding by replacing `attachmentChips.length` with `chatSessionUploadedFileCount` (or equivalent). The chip mirror remains useful for the indicator's immediate update on chip-strip mutations even before the BFF manifest round-trip completes.

---

## 5. Cleanup pathway decision (per POML Step 3)

**Decision**: Defer per-file BFF cleanup to Phase 3 backend backlog; rely on session-end HostedService cleanup (R5 task 007) for orphan reconciliation.

**Rationale**:
- No DELETE endpoint exists today for per-file manifest removal. `GET /api/ai/chat/sessions/{sessionId}/files/{fileId}` is the obvious shape, but adding it would be a BFF code change (publish-size delta, ADR-029 verification, test obligation per CLAUDE.md §3.7) — out of scope for a frontend-only orchestration UX task.
- The session-files AI Search index cleanup is owned by R5 task 007's `IHostedService` (`SessionFilesCleanupJob`). It runs aggressively on session end (does NOT wait for the scheduled sweep — per NFR-02), so orphaned index documents are BOUNDED by session lifetime. For a multi-day-old session with leftover files this would be a problem; for an active session where the user is actively removing chips, the user-visible behaviour is correct (the chip vanishes; the session has not yet ended).
- The user-visible UX is unaffected: SprkChat splices the chip locally on `removeFile(index)` (called immediately after the cascade callback returns), so the chip disappears synchronously. The BFF state catches up at session end.

**Implementation**: `handleAttachmentRemoved` in `ConversationPane.tsx` (a) clears the per-id ref entries so re-adding the same file re-fires staging dispatches, (b) logs a structured warning (`console.info`) so the deferred-cleanup gap is observable in dev tools and analytics. Phase 3 picks up the endpoint addition.

**Phase 3 backlog item**: "Add `DELETE /api/ai/chat/sessions/{sessionId}/files/{fileId}` endpoint with transactional manifest + index removal. Update `ConversationPane.handleAttachmentRemoved` to call it. Estimate: ~4h (endpoint + tests + endpoint registration in `AnalysisServicesModule`)." Captured in lessons-learned candidate list.

---

## 6. Interjection emission helper location

Per POML Step 4.4 + Step 10 documentation requirement:

The deterministic multi-file combined-summary interjection logic lives in **`src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx`** at module scope:

- `buildMultiFileSummarizeInterjection(fileCount: number): string | null` — pure helper exported at module scope; returns `null` for `fileCount < 2`, returns the spec-driven body for N >= 2.
- `makeLocalAssistantMessage(content: string): IChatMessage` — pure helper that wraps a body in the `IChatMessage` shape SprkChat consumes via `injectLocalMessage`.
- `handleBeforeSendMessage(messageText: string): void` — the component-scope dispatcher that consumes `routeSummarizeIntent` + the chip count to decide whether to emit the interjection; guards exactly-once-per-turn semantics via `emittedSummarizeInterjectionKeysRef`.

**Why co-locate with `ConversationPane`** (vs `@spaarke/ui-components`):
- The interjection text + tri-mode routing are R5-specific orchestration. Per ADR-012, R5-specific logic does NOT belong in the shared library. The shared lib (SprkChat) exposes a *generic* `injectLocalMessage` prop; the host (SpaarkeAi shell's ConversationPane) owns the R5 semantics.
- Cross-task coordination with task 019: task 019 already exports `routeSummarizeIntent` + `SUMMARIZE_PROMPT_FIRST_INTERJECTION` from the same module. Co-locating task 020's helpers keeps the slash-command + tri-mode logic in one file for easy review.

---

## 7. PaneEventBus dispatch implementation

Per POML goal §6 + ADR-030:

**Dispatched event**: `dispatch('context', { type: 'files_staged', stagedFileIds: string[] })`

**Trigger**: every chip that newly transitions to `status === 'ready'` (deduped via `dispatchedReadyIdsRef`). NOT dispatched for `extracting` or `error` chips (covered by test `(6).does NOT dispatch for chips in extracting or error state`).

**Per-batch granularity**: each `onAttachmentsChanged` invocation that contains newly-ready chips fires exactly ONE dispatch carrying the IDs of the chips that newly became ready in this tick (NOT all ready chips — only the deltas). Verified by test `(6).does NOT dispatch a second event for chips that are already ready`.

**R4 regression preserved**: the existing `handleAttachmentReady` callback (R4 task 042 / W-4 — Assistant → Workspace mount source) is **untouched**. It continues to dispatch `workspace.widget_load` with `DocumentViewerWidgetData` for the first ready file. Both dispatches fire on the same trigger (chip reaches ready) but on DIFFERENT channels (`workspace` for the widget mount; `context` for the file preview affordance). The chip lifecycle now produces TWO PaneEventBus events per ready transition, which is the intended additive design per ADR-030's "additive event types within existing 4 channels" rule.

---

## 8. Dark-mode + a11y verification

Per POML Step 7 + ADR-021:

**Dark mode**: the new "N files attached" indicator styles (`filesAttachedIndicator`, `filesAttachedIndicatorText`, `filesAttachedIndicatorHint`) use ONLY Fluent v9 semantic tokens (`tokens.colorNeutralBackground2`, `tokens.colorNeutralStroke2`, `tokens.colorNeutralForeground2`, `tokens.colorNeutralForeground3`, `tokens.spacing*`, `tokens.fontSize*`, `tokens.fontWeight*`). No hardcoded hex / rgba / px (except `1px` border width which is dimensional, not color). Dark mode contrast is the responsibility of the Fluent v9 theme (`webDarkTheme`); the indicator inherits the same theme switching the SpaarkeAi shell uses for the rest of the UI.

**Screenshots**: out of scope for this sub-agent session (no browser environment available). The main session should capture before/after screenshots during the visual smoke test for the merged PR per POML Step 10.

**Keyboard accessibility**: the chip dismiss button (inside SprkChat) is already keyboard-accessible (existing `Button` from Fluent v9 with `aria-label` — task 020 changes its `onClick` handler but preserves the entire keyboard contract). The new "N files attached" indicator is a status region (`role="status" aria-live="polite"`) — screen readers announce count changes without interrupting user focus.

---

## 9. R4 regression check — `workspace.widget_load` dispatch

Per POML acceptance criterion 6: *"R4 `workspace.widget_load` dispatch for the first ready file STILL fires (regression-tested)."*

`handleAttachmentReady` is preserved verbatim. The only changes around its call site are additive: a parallel `handleAttachmentsChanged` callback wired to the new `onAttachmentsChanged` SprkChat prop. Both callbacks are independent — they fire on different SprkChat triggers (`onAttachmentReady` fires once per ready file; `onAttachmentsChanged` fires on every chip lifecycle mutation). The R4 Workspace dispatch path is unchanged.

**Test coverage**: the test suite covers the `context.files_staged` dispatch path directly. The R4 `workspace.widget_load` regression assertion is NOT in the new test file because the SprkChat-stub mock does not exercise `onAttachmentReady` (it's a different callback chain). The R4 W-4 test suite (in `src/solutions/SpaarkeAi/src/components/conversation/__tests__/` — none exists yet, R4 has its own coverage at the AiSessionProvider level) is unaffected by these changes. Reviewer should confirm the R4 behaviour via the full SpaarkeAi build + visual smoke test.

---

## 10. Test coverage report

Tests added at `src/solutions/SpaarkeAi/src/components/conversation/__tests__/ConversationPane.r5.test.tsx`:

| Test group | Coverage |
|---|---|
| (1) Pure-helper coverage | `buildFileConfirmationMessage` (4 cases incl. empty / singular / plural / overflow truncation), `buildMultiFileSummarizeInterjection` (2 cases incl. zero/single-file null + multi-file format), `makeLocalAssistantMessage` (Assistant role + markdown metadata + ISO timestamp), `routeSummarizeIntent` (4 branches — re-verified post task 020 wiring). |
| (2) Chip indicator | Hidden when no chips; singular copy at 1 chip; plural copy at 2+ chips; reactive updates on add/remove. |
| (3) Per-file remove cascade | Callback wiring; per-id ref cleanup so re-adding the same chip re-fires the staging dispatch. |
| (4) Inline file-confirmation debounce | Multiple chips arriving within 250ms produce ONE consolidated injection; same chips on re-render do NOT re-emit. |
| (5) Multi-file Summarize interjection | Emits for N >= 2 + `/summarize`; does NOT emit for single-file; does NOT emit for non-Summarize; exactly-once-per-turn (retry / resubmission of the same key dedupes). |
| (6) PaneEventBus `context.files_staged` dispatch | Correct staged file IDs; per-batch deltas (already-ready chips not re-dispatched); extracting / error chips not dispatched. |

**Total tests**: 21. **Total assertions**: ~50. **Coverage focus**: every new code path in `ConversationPane.tsx` (handleAttachmentsChanged, handleAttachmentRemoved, handleBeforeSendMessage, handleLocalMessageInjected, all 4 helper exports). The indicator render path is covered via the integration tests in §2.

**Why not snapshot tests for the indicator**: Fluent v9 styles produce dynamic class names that vary between builds; snapshot tests would be brittle. The behavioral assertions (`toHaveTextContent`, `queryByTestId`) capture the operator-visible behavior more robustly.

**Not yet executed**: per the sub-agent's no-build / no-test-run constraints, the tests are authored but NOT executed in this session. The main session SHOULD run `npx jest --testPathPattern ConversationPane.r5` in `src/solutions/SpaarkeAi/` before merging task 020.

---

## 11. BFF publish-size delta

**Delta**: **0 MB** (frontend-only change).

Files modified:
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/types.ts` (+~100 lines — additive types + JSDoc)
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChat.tsx` (+~70 lines — additive props + 3 small effects + onClick wrap)
- `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` (+~300 lines — additive helpers + state + handlers + indicator render)
- `src/solutions/SpaarkeAi/src/components/conversation/__tests__/ConversationPane.r5.test.tsx` (NEW, ~430 lines — Jest + RTL tests)
- `projects/spaarke-ai-platform-unification-r5/notes/task-020-chat-pane-ux-evidence.md` (NEW — this file)
- `projects/spaarke-ai-platform-unification-r5/tasks/020-chat-pane-orchestration-ux.poml` (status: not-started → complete)

Zero BFF code (`src/server/api/Sprk.Bff.Api/`) touched. Per CLAUDE.md §10 NFR-01 + R5 CLAUDE.md §3.6, no `dotnet publish` measurement is required for this task. Confirmed by inspection of the diff scope.

---

## 12. ADR compliance summary

| ADR | Compliance | Evidence |
|---|---|---|
| ADR-010 (DI minimalism) | N/A | No BFF DI changes; frontend-only. |
| ADR-012 (shared component library — context-agnostic) | ✅ | SprkChat's new props use only generic types (`AttachmentChip`, `IChatMessage`, primitive callbacks). NO SpaarkeAi-specific types cross the boundary. R5 orchestration lives in the host shell layer (`SpaarkeAi/src/components/conversation/`), NOT inside `useChatFileAttachment` or SprkChat. |
| ADR-013 (BFF-only AI orchestration; Zone B HTTP consumer) | ✅ | No new BFF surface area; the per-file cleanup endpoint addition is explicitly DEFERRED to Phase 3 (§5 above) to preserve BFF-publish hygiene. |
| ADR-018 (Flag Scope Discipline) | ✅ | Zero new feature flags introduced. Indicator + chip remove + interjection are unconditional UI behaviors. R5 §3.2 reuse mandate preserved. |
| ADR-021 (Fluent v9 + dark mode) | ✅ | All new styles (`filesAttachedIndicator*`) use `tokens.*` semantic tokens only. No hardcoded colors / hex / rgba. Dark mode inherits theme switching from the SpaarkeAi shell's `FluentProvider`. |
| ADR-022 (React 19) | ✅ | All new code uses `React.useState`, `React.useCallback`, `React.useEffect`, `React.useRef` — React 19 hooks. No class components; no module-level side effects beyond pure helper exports. |
| ADR-028 (Spaarke Auth v2 — no token snapshots) | ✅ | Zero new auth boundary surfaces. The deferred cleanup endpoint (Phase 3 backlog) will inherit `authenticatedFetch` from `useAiSession()` per existing pattern. |
| ADR-029 (BFF publish hygiene) | ✅ | Frontend-only; delta = 0 MB (§11). |
| ADR-030 (PaneEventBus closed at 4 channels) | ✅ | Zero new channels. Dispatches `context.files_staged` — an ADDITIVE event type defined by task 016 within the existing `context` channel. Typed via `ContextPaneEvent` discriminant; no `any` at the dispatch boundary. |
| ADR-032 (BFF Null-Object kill-switch) | N/A | No conditional BFF service registrations. |
| Project CLAUDE.md §3.1 (reuse mandate) | ✅ | Extends `ConversationPane` + `SprkChat` in place. NO parallel chat pane. NO parallel chip lifecycle. NO duplicate of `useChatFileAttachment`. |
| Project CLAUDE.md §3.2 (no new flags) | ✅ | Zero new flags. |
| Project CLAUDE.md §3.4 (PaneEventBus additive types) | ✅ | Uses `context.files_staged` from task 016 (already-defined additive type). No new channels. |
| Project CLAUDE.md §3.7 (frontend test obligation) | ✅ | 21 new tests added in `__tests__/ConversationPane.r5.test.tsx` covering every new code path. |

---

## 13. Files modified (summary)

| File | LOC delta (approx) | Change type |
|---|---|---|
| `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/types.ts` | +106 / -3 | Additive — 5 new optional props on `ISprkChatProps` + JSDoc |
| `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChat.tsx` | +75 / -2 | Additive — prop destructure + 3 effect blocks + dismiss-button onClick wrap |
| `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` | +335 / -103 | Additive helpers + state + handlers + indicator render; replaced task 019's TODO scaffolding with real wiring |
| `src/solutions/SpaarkeAi/src/components/conversation/__tests__/ConversationPane.r5.test.tsx` | +430 NEW | Jest + RTL tests (21 tests, ~50 assertions) |
| `projects/spaarke-ai-platform-unification-r5/tasks/020-chat-pane-orchestration-ux.poml` | +3 / -3 | Status: not-started → complete |
| `projects/spaarke-ai-platform-unification-r5/notes/task-020-chat-pane-ux-evidence.md` | NEW | This evidence file |

Net BFF publish-size delta: **0 MB** (no `src/server/api/Sprk.Bff.Api/` files touched).

---

## 14. Status transitions

- Task POML 020 `<status>` updated from `not-started` to `complete`.
- Task 020 `<actual-effort>` recorded as ~3h.
- Task 020 `<started>` + `<completed>` date stamped (2026-06-04).
- `TASK-INDEX.md` and `current-task.md` NOT updated by this sub-agent (per executor instructions — main session owns those transitions).

---

## 15. Next-task handoffs

- **Task 021** (D2-12 per-file "Summarize this only" affordance): consumes the same `attachmentChips` state mirror via the existing `context.files_staged` PaneEventBus event (task 018's `FilePreviewContextWidget` is the natural host for the per-file button). Task 021 should subscribe to `context.file_selected` (already defined by task 016) for the single-file Summarize trigger.
- **Phase 3 backlog**: per-file BFF cleanup endpoint (`DELETE /api/ai/chat/sessions/{sessionId}/files/{fileId}` with transactional manifest + AI Search index removal). Estimate ~4h. Logged as R6 / R5-Phase-3 backlog candidate (§5 above).
- **R6 backlog (carry-over from task 019 + audit)**: `playbook-embeddings` indexing for `summarize-document-for-chat@v1` (Insights r3 work item F-4 backfill). NOT in R5 scope.

---

*Authored by: AI Sub-agent (parallel wave). Reviewed by: main session at code-review + adr-check time.*
