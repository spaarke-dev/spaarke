# Agent findings — SpaarkeAi code page + wizards + LegalWorkspace AI (auditor 4/7, 2026-07-05)

Scope: `src/solutions/SpaarkeAi/**` (full), `WorkspaceLayoutWizard`, LegalWorkspace AI-facing parts,
scan of other code pages. Audited against MASTER. Shared libs covered by auditor 5.

## A. Shell / Infra (all working)
`App.tsx` (auth probe, theme, compose-launch forwarding), `main.tsx` (React 19, ensureAuthInitialized),
`ThreePaneShell.tsx` (+ShellStageManager+SessionRestoreManager — provider tree PaneEventBus→AiSession→
StageManager→ThreePaneLayout, 4-stage lifecycle), `runtimeConfig`, `authInit`, `launch-resolver` (ADR-006),
ribbon launchers ×3, `errorTelemetry`, `usePaneCollapse`, `useSessionRestore` (**Session** — GET
`/api/ai/chat/sessions/{id}/restore`, AIPU2-106).

## B. Conversation pane / Dispatcher core
| Component | Category | Status / notes |
|---|---|---|
| `ConversationPane.tsx` (**2498 lines**) | Dispatcher+Consumer+Widget-routing | **working but accretion R1→R7**, many stacked hotfix layers; the client dispatch hub: onDecorateOutboundBody (soft-slash intentHint + resolved refs), onBeforeSendMessage intent capture, hard-slash exec, `/summarize` tri-mode routing, file-chip lifecycle → `context.files_staged`, playbook-selected bus handling, selection "Refine this?" chip, restore summary block. PRIMARY consolidation candidate. |
| `CommandRouter.ts` | Dispatcher | working — pure parser; closed vocab: 7 hard (`/clear /new-session /help /export /save-to-matter /pin /playbooks`), 4 soft (`/summarize /draft /extract-entities /analyze`), 3 ref sigils (`#scope @entity #file`). R6 P8/FR-48. |
| `HardSlashExecutor.ts` | Dispatcher | working — deterministic exec of 7 hard slashes vs existing BFF endpoints; comment says "SIX" but handles 7. |
| `SoftSlashRouter.ts` | Dispatcher | working — pure `decorateBody()` adds `intentHint` for BFF bias. |
| `ReferenceResolver.ts` | Dispatcher | working — resolves #scope/@entity/#file via `/api/ai/scopes/*` + adapters; tenant-keyed cache; entity polymorphic lookup deferred (degrades). |
| `intentMatcher.ts` | Dispatcher | working — config-driven registry with ONE entry (`summarize-session`); shaped for extension, never extended. |
| `executeSummarizeIntent.ts` | Consumer+Widget-routing | working — promote files → `/documents`, widget_load Summary tab, POST `/summarize` SSE, bridge. |
| `sseToPaneEventBridge.ts` | Widget-routing | working — AnalysisChunk → field_delta/streaming_* transformer. |
| `CommandHelpPanel` / `HelpAffordance` | Dispatcher help UI | working. |
| `HistoryOverlay.tsx` | Session | working — session resume dropdown. |
| `ChatHistoryPanel.tsx` | Session | **dead-code suspect** — superseded by HistoryOverlay; no non-test importer. |

**Caveats**: `/summarize` slash deliberately rerouted to NL agent path ("B9 slash-to-NL rewire", lines
~1200-1218) so the deterministic intentMatcher path is reachable mainly via button-id/held-files, not the
literal slash. `heldFilesRef` documented as effectively always-empty (un-landed cross-package File
forwarding, lines 935-952) → deterministic promotion path may not receive real File binaries in prod
(**partial capability — flag to summarize owner**).

## C. Insights renderer cluster — STRONGEST DEAD-CODE CANDIDATE (~14 files)
`services/insightsQueryClient.ts`, `config/insightsRendererConfig.ts`, `components/conversation/insights/*`
(InsightsResponseRenderer, RagResponseRenderer, PlaybookResponseRenderer, DeclineResponseRenderer,
InsightsErrorRenderer, EmptyResultHint, LowConfidenceBadge + insightsErrorMessages/insightsRetryPolicy/
retryAfterParser/types/index). R5 "Insights Assistant" integration never wired into the chat pane —
references only within the cluster + own tests. Full SSE client with v1.0/1.1 fallback, all dead.

## D. Workspace pane
Working: `WorkspacePane.tsx` (widget_load→addTab→lazy resolve, tab persistence PATCH, restore,
default-layout auto-install, pinned auto-open, compose-mode override; heavy hotfix history),
`WorkspaceTabManager(.tsx)`, `WorkspacePaneMenu`, `ManageWorkspacesPane`, `useWorkspaceLayouts`,
`workspaceLayoutMutations`, `pinnedWorkspaces` (localStorage; **shape duplicated verbatim in
WorkspaceLayoutWizard App.tsx lines 276-348 — "MUST stay in sync" comment, drift risk**).
**Dead/scaffolding matched set (R6 Pillar 6b, built + tested, zero non-test importers)**:
`SendToWorkspaceButton.tsx`, `PinToMatterButton.tsx`, `AddToAssistantToggle.tsx` (redundant —
visibility wired via WorkspacePane.handleToggleVisibility instead).
Vestigial refs in live files: `summaryTabIdRef`/`streamFocusOverrideRef` permanent-null sentinels.

## E. Context pane
Working: `ContextPaneController.tsx` (adaptive pane, selectedTool source of truth, Get-Started 7-card
launchers via shared `launch*Wizard`), `ContextPaneMenu`, `SemanticSearchCriteriaTool` (Consumer),
`useContextTool`, `contextToolPin`.
**Dead**: `notificationContextLoader.ts` — self-documented dead (header lines 44-49).
**Partial**: `ExecutionTraceWidget` mounted but BFF→SSE→bus bridge unbuilt — renders empty (R7 backlog).

## F. WorkspaceLayoutWizard
Manifest surface (3-step layout builder → `/api/workspace/layouts`; SECTION_METADATA_CATALOG). Working.
**No AI pre-fill in this wizard.** Only AI-assisted form fill in solutions = LegalWorkspace CreateMatter `AiFieldTag`.

## G. LegalWorkspace AI-facing parts (dashboard engine embedded via WorkspacePane)
| Component | Category | Notes |
|---|---|---|
| `SummarizeFiles/*` (`summarizeService.ts`) | Consumer | working — `POST /api/workspace/files/summarize` SSE. **Parallel summarize path** to chat-session summarize. |
| `Playbook/*` (`playbookService.ts`, `analysisService.ts`) | Consumer+Manifest | working — playbook card grid, scope configurator, analysis invocation. |
| `FindSimilar/*` | Consumer | working — semantic find-similar. |
| `CreateMatter/AiFieldTag.tsx` (+matterService, DraftSummaryStep) | Consumer | working — AI form-fill markers; AiFieldTag is thin re-export of shared `Spaarke.UI.Components/AiFieldTag`. |
| `ActivityFeed/AISummaryDialog.tsx`, `SmartToDo/TodoAISummaryDialog.tsx` | Consumer | working — AI summaries. |
| `GetStarted/briefingNarrative.ts` + getStartedConfig | Consumer+Manifest | working — briefing fallback + card catalog (overlaps SpaarkeAi's GetStartedCardsWidget catalog). |
| `sections/composeEditor.registration.ts` | Widget-routing | working — compose editor section via `@spaarke/compose-components`. |

Thin re-export code pages (not separate impls): SummarizeFilesWizard, CreateMatterWizard, CreateProjectWizard,
PlaybookLibrary, FindSimilarCodePage, DailyBriefing, DocumentUploadWizard. `CopilotAgent` has NO src files.

## H. Duplicates / overlaps
1. **Two client Summarize implementations**: SpaarkeAi chat path (`executeSummarizeIntent` → `/api/ai/chat/sessions/{id}/summarize`) vs LegalWorkspace wizard (`summarizeService` → `/api/workspace/files/summarize`). Both intentionally live (ConversationPane `routeSummarizeIntent` `active-document` branch falls back to the wizard), but duplicate SSE-parsing + summary rendering.
2. **pinned-workspaces localStorage shape duplicated** (SpaarkeAi vs WorkspaceLayoutWizard).
3. **Manual SSE line-parsers ×3** in this surface alone: executeSummarizeIntent, insightsQueryClient (dead), LegalWorkspace summarizeService. No shared SSE utility.
4. **Two Get-Started card catalogs** (SpaarkeAi widget vs LegalWorkspace config).
5. **Two history surfaces** (ChatHistoryPanel dead vs HistoryOverlay live).
6. **AddToAssistantToggle** duplicates wired visibility toggle.

## I. Dead-code summary
Insights cluster (~14 files), notificationContextLoader, Pillar-6b trio (SendToWorkspace/PinToMatter/
AddToAssistant), ChatHistoryPanel, vestigial refs in WorkspacePane/ConversationPane.
