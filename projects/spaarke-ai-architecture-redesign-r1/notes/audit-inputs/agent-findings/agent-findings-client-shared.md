# Agent findings — client shared AI libraries (auditor 5/7, 2026-07-05)

Scope: `src/client/shared/` — Spaarke.UI.Components (SprkChat + AI hooks/services), Spaarke.AI.Widgets,
Spaarke.AI.Context, Spaarke.AI.Outputs, Spaarke.DailyBriefing.Components. Audited against MASTER.

## Consumer map (live/dead judgment basis)
- `SprkChat` — exactly TWO real consumers: `code-pages/AnalysisWorkspace/.../ChatPanel.tsx` + `solutions/SpaarkeAi/.../ConversationPane.tsx`.
- `@spaarke/ai-context` — ConversationPane, ThreePaneShell, AnalysisWorkspace `AnalysisAiContext.tsx` (+types by AI.Widgets).
- `@spaarke/ai-widgets` — heavily consumed across SpaarkeAi.
- `@spaarke/ai-outputs` — direct solution consumer only SpaarkeAi `ChatHistoryPanel.tsx`; widgets consumed INTERNALLY by AI.Widgets wrappers.
- `@spaarke/daily-briefing-components` — LIVE via subpath imports (`/components`, `/widgets`) from DailyBriefing solution + LegalWorkspace + SpaarkeAi (barrel grep misses it).

## Architectural direction (auditor's synthesis)
**AI.Widgets (PaneEventBus + AiSessionProvider + R2 registries) is the live platform.** AI.Context survives
only as extracted chat hooks + ChatApiClient + entity resolver for AnalysisWorkspace (its R1 provider is dead).
AI.Outputs survives only as R1 widget components wrapped by AI.Widgets (its R1 registries are dead).
SprkChat remains the single reusable chat control with canonical `useSseStream`.

Recency: SprkChat 2026-06-26 active · AI.Widgets 2026-07-02 most active · DailyBriefing 2026-07-01 active ·
AI.Context 2026-05-26 STALE · AI.Outputs 2026-05-17 STALE.

## 1. Spaarke.UI.Components — SprkChat (all working/exported unless noted)
- `SprkChat.tsx` — Consumer+Dispatcher — full chat surface (input, streaming, plan gate, citations, suggestions, attachments). ADR-012.
- `hooks/useSseStream.ts` (CANONICAL, AIPU2-082 consolidation) — SSE parse+dispatch: token/typing/suggestions/citations/plan_preview/action/doc-stream/playbook_options/context_event/pane events. Shim re-export at `SprkChat/hooks/useSseStream.ts` (intentional, not dead).
- `SprkChat/hooks/useChatSession.ts` — Session — **DUPLICATE of AI.Context copy** (see overlaps).
- `SprkChat/hooks/useChatPlaybooks.ts`, `useChatContextMapping.ts` — Manifest/Session — duplicates likewise.
- `useDynamicSlashCommands.ts` — Dispatcher+Manifest — fetches `/sessions/{id}/commands`, merges DEFAULT_SLASH_COMMANDS (FR-05/11/17).
- `useSelectionListener`, `useChatFileAttachment` (FR-07), `useActionHandlers`/`useActionMenuData`, `QuickActionChips`, `SprkChatSuggestions`, `SprkChatHighlightRefine`, `SprkChatMessageRenderer`, `PlanPreviewCard`, citation popover/marker, upload zone/status, input/context-selector/prompts/menu — all working.
- Top-level hooks: `useSlashCommands`, `useInlineAiActions`/`useInlineAiToolbar` (record-header inline AI), `useAiSummary`, `useAiPrefill`, `useLinearRunProgress` (second, distinct SSE-consuming hook for linear run progress).
- **DEAD**: `SprkChatExportWord.tsx` (index.ts documents unexported/unreferenced); `services/SprkChatBridge.ts` (@deprecated throughout; BroadcastChannel bridge superseded by AnalysisAiContext streaming callbacks).

## 2. Spaarke.AI.Widgets (live platform)
- `AiSessionProvider.tsx` / `useAiSession` — **Session** — chatSessionId/playbookId in localStorage, context-mapping fetch, streaming callbacks, routes SSE pane events → PaneEventBus (output_pane→workspace, source_pane/highlight→context). Replaces R1 StandaloneAiProvider.
- `PaneEventBus` + Provider + hooks + `PaneEventTypes` — **Widget-routing core** — typed multi-subscriber bus, 4 channels (workspace/context/conversation/safety). Fixes R1 single-subscriber-last-wins.
- `StructuredOutputStreamWidget` — schema-driven progressive FieldDelta renderer; exports SUMMARIZE_SCHEMA / INSIGHTS_PLAYBOOK_SCHEMA / SUM_CHAT_OUTPUT_SCHEMA. Load-bearing (R5 task 017).
- `WorkspaceWidgetRegistry` / `ContextWidgetRegistry` — **Manifest** — canonical lazy registries with GenericTextWidget fallback + Pillar-9 visibility.
- `register-workspace-widgets.ts` — registers ~20 widgets (7 R1 output widgets from AI.Outputs, redline, wizards, entity-view, dashboards); baked `sprk_gridconfiguration` GUIDs.
- **Two parallel `register-context-widgets.ts`** (widgets/context/ AND registry/) — index calls both "deliberate" but genuine duplication.
- `WorkspaceWidgetWrapper` — HOC adapting R1 OutputWidgets → R2 interface + serialize/restore (D-08).
- Context widgets (Citation/CodeViewer/DocumentViewer/ImageViewer/LegalLibrary/WebSource wrappers), `ExecutionTraceWidget` (registered `execution-trace`), FindingsWidget, PlaybookGalleryWidget, GetStartedCardsWidget, ProgressTracker, FilePreviewContextWidget (`dispatchSummarizeOnly`), PinnedMemoryListWidget, EntityInfoWidget — working.
- Workspace widgets: RedlineViewer, DocumentViewer, SearchCriteriaResult, DataverseEntityView, MetricsDashboard, WorkspaceLayout — working.
- Wizard **Dispatcher** widgets (thin launchers via widget_load/Xrm.Navigation): CreateMatter, CreateProject, DocumentUpload, SearchSelect, FindSimilar, EmailCompose, MeetingSchedule (AIPU2-104).
- Interactions (Dispatcher): TextSelectionListener/useTextSelection, useCitationLink, TabContextMapping, StageTransitionRules (AIPU2-100/101/103/105).
- Safety/annotation components (safety channel): SafetyAnnotationOverlay, CitationBadge, GroundednessHighlight, ConfidenceIndicator, FeedbackButtons. InsightSummaryCard. Pinned-memory dialogs (R6 P7). `useWorkspaceLayouts` (R4 consolidation).

## 3. Spaarke.AI.Context (STALE except extracted hooks)
- `hooks/useChatSession.ts` / `useChatContextMapping.ts` / `useChatPlaybooks.ts` — working, used by AnalysisWorkspace — **independent duplicates of SprkChat copies** ("Extracted from SprkChat", no shared code). Same BFF contract, two code paths → drift risk.
- `ChatApiClient.ts` — canonical URL builders. `useEntityResolver` — working.
- **DEAD**: `StandaloneAiProvider` (`providers/StandaloneAiContext.tsx`) + `useStandaloneAi.ts` — zero real imports; superseded by AiSessionProvider/useAiSession; still barrel-exported.

## 4. Spaarke.AI.Outputs (STALE except wrapped components)
- Output widgets (11): consumed ONLY via AI.Widgets register-workspace-widgets subpath imports — 7 registered; **ChartWidget, DataTableWidget, TimelineWidget, DocumentCompareWidget NOT registered + no external import → likely dead**.
- Source widgets (6): wrapped by AI.Widgets context wrappers — live.
- **DEAD**: `registry/output-registry.ts` + `source-registry.ts` (R1 registries; references only in own JSDoc; superseded by AI.Widgets registries).
- `chat-history/` (ChatHistoryPanel/ChatSessionCard/useChatHistoryFilter) — live via SpaarkeAi ChatHistoryPanel wrapper (which itself is orphaned per auditor 4 — so this leg is effectively dead-by-transitivity; reconcile in synthesis).
- `cross-pane/` (window CustomEvent bus) — **partial/uncertain**, no in-scope consumer; predates + overlaps PaneEventBus.
- React-19-only, "NOT PCF-safe".

## 5. Spaarke.DailyBriefing.Components — LIVE (subpath imports)
DailyBriefingApp (Consumer), briefing render components (digest/tldr/narrative/citations/todos), hooks
(useBriefingRender/Narration/Actions/Notifications/Preferences/useInlineTodoCreate — `/narrate` AI narration
via BFF), services (briefingService/notificationService/preferencesService), `dailyBriefing.registration.ts`
(Pattern D shim registering as LegalWorkspace section / SpaarkeAi widget). No SSE/PaneEventBus — own BFF services.

## Duplicates / overlaps
1. **Three chat hooks duplicated** (useChatSession/useChatContextMapping/useChatPlaybooks) in SprkChat vs AI.Context — same BFF contract, two implementations.
2. **Two widget-registry systems** — AI.Outputs R1 (dead) vs AI.Widgets R2 (live).
3. **Two register-context-widgets.ts** inside AI.Widgets.
4. **SSE parsing**: shared-lib duplication RESOLVED (AIPU2-082 canonical useSseStream + shim); office-addins `SseClient.ts` separate impl remains; `useLinearRunProgress` is a second distinct SSE hook.
5. **Three cross-pane mechanisms historically**: AI.Outputs cross-pane (CustomEvent), SprkChatBridge (BroadcastChannel, deprecated), PaneEventBus (current).
6. **Two session providers**: StandaloneAiProvider (R1, dead) vs AiSessionProvider (R2, live).

## Dead-code summary
StandaloneAiContext + useStandaloneAi; output-registry + source-registry; SprkChatExportWord; SprkChatBridge;
4 unregistered AI.Outputs output widgets; cross-pane/ (probable).
