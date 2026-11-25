/**
 * AI output and source pane widgets (src/outputs).
 *
 * Formerly the separate `@spaarke/ai-outputs` package; folded into
 * `@spaarke/ai-widgets` on 2026-11-24 (DEF-006 #2806, ADR-012 census). Its only
 * consumers are this package's context widgets (source widgets) and
 * `register-workspace-widgets.ts` (output widgets, lazily imported). It is
 * internal: the `@spaarke/ai-widgets` barrel does not re-export it, because
 * nothing outside this package uses it and its `DocumentViewerWidget` name
 * collides with the workspace widget of the same name.
 *
 * React 19, NOT PCF-safe.
 */

// Types — widget contracts and registry entry shapes
export * from './types';

// NOTE (Track-B batch 3, ai-architecture-redesign-r1): the R1 widget
// registries and the CustomEvent-based pane-linking module were deleted —
// superseded by the canonical registries + PaneEventBus in @spaarke/ai-widgets.

// Widget modules — populated in Wave 2 and Wave 3
export * from './output-widgets';
export * from './source-widgets';

// chat-history (ChatHistoryPanel, ChatSessionCard, useChatHistoryFilter) was
// DELETED 2026-10-03 (reuse audit C-14): zero consumers — SpaarkeAi's
// HistoryOverlay is the live chat-history UI.
