/**
 * @spaarke/ai-widgets — Context Widget Registrations
 *
 * Secondary aggregation file for context pane widget registrations. Shell
 * entry points that consume @spaarke/ai-widgets via the barrel (index.ts)
 * do NOT need to import this file — the barrel handles registration as a
 * side effect. Import this file only in entry points that import widgets
 * individually without going through the barrel.
 *
 * Registration pattern:
 * - Each call to registerContextWidget() is lazy: the factory is a dynamic
 *   import() that is NOT invoked until resolveContextWidget() is first called
 *   for that type. This keeps the initial bundle small.
 * - The type string is the server-driven key: the BFF sends this string in
 *   the widget payload and the ContextPaneController uses it to resolve the
 *   correct component.
 * - The registry silently ignores duplicate registrations (first wins), so
 *   importing this module alongside the barrel is safe but redundant.
 *
 * Adding a new context widget:
 * 1. Create the widget component in src/widgets/context/
 * 2. Add a registerContextWidget() call in index.ts (inline, matching the
 *    progress-tracker and playbook-gallery patterns already present).
 * 3. Mirror the call here for shell entry points that bypass the barrel.
 * 4. Ensure the type string matches the value sent by the BFF.
 *
 * Task: AIPU2-086
 */

import { registerContextWidget } from './ContextWidgetRegistry';
import { safeRegister } from '@spaarke/ui-components';
import type { ContextWidgetComponent } from '../types/widget-types';

// ai-spaarke-ai-workspace-UI-r1 brittleness Phase B.5 (2026-06-09):
// Isolate each registration so a synchronous throw from one call (factory-
// expression evaluation failure, malformed registration object) does not skip
// the registrations that follow. See safeRegister docblock + the same pattern
// in `register-workspace-widgets.ts`.
function safeRegisterContext(...args: Parameters<typeof registerContextWidget>): void {
  safeRegister('ContextWidget', args[0], () => registerContextWidget(...args));
}

// ---------------------------------------------------------------------------
// progress-tracker
// (also registered inline in index.ts — duplicate is safe, first wins)
// ---------------------------------------------------------------------------

safeRegisterContext('progress-tracker', {
  factory: () => import('../widgets/context/ProgressTrackerWidget').then(m => ({ default: m.default })),
});

// ---------------------------------------------------------------------------
// playbook-gallery
//
// Widget type: 'playbook-gallery'
// Stage:       Welcome / playbook-selection (before any conversation turn)
// Dispatches:  playbook_change → 'conversation' PaneEventBus channel
// (also registered inline in index.ts — duplicate is safe, first wins)
// ---------------------------------------------------------------------------

safeRegisterContext('playbook-gallery', {
  factory: () =>
    // Type-erasure cast: ContextWidgetComponent<unknown> at registry vs widget's
    // typed default (ContextWidgetComponent<PlaybookGalleryData>) — registry
    // boundary variance, see ../index.ts for the same pattern.
    import('../widgets/context/PlaybookGalleryWidget').then(m => ({
      default: m.default as unknown as ContextWidgetComponent,
    })),
});

// ---------------------------------------------------------------------------
// entity-info
//
// Widget type: 'entity-info'
// Stage:       entity-info (loading and early active-chat stages)
// Purpose:     Displays entity detail (display name, status, client, owner,
//              key dates, budget, custom fields) in the Context pane so users
//              have immediate context without requiring a chat turn.
//              Reacts to subsequent context_update events — updates reactively
//              when the user navigates to a different entity record.
// ---------------------------------------------------------------------------

safeRegisterContext('entity-info', {
  factory: () =>
    // Type-erasure cast: registry boundary variance (see playbook-gallery above).
    import('../widgets/context/EntityInfoWidget').then(m => ({
      default: m.default as unknown as ContextWidgetComponent,
    })),
});

// ---------------------------------------------------------------------------
// findings
//
// Widget type: 'findings'
// Stage:       sources-citations (after analysis completes)
// Purpose:     Displays structured analysis findings — key items, risk levels,
//              and citation links. Citation clicks dispatch context_highlight
//              to the 'context' PaneEventBus channel so the active
//              DocumentViewer scrolls to / highlights the cited passage.
// (also registered inline in index.ts — duplicate is safe, first wins)
// ---------------------------------------------------------------------------

safeRegisterContext('findings', {
  factory: () =>
    // Type-erasure cast: registry boundary variance (see playbook-gallery above).
    import('../widgets/context/FindingsWidget').then(m => ({
      default: m.default as unknown as ContextWidgetComponent,
    })),
});

// ---------------------------------------------------------------------------
// file-preview (R5 task 018 / D2-09)
//
// Widget type: 'file-preview'
// Stage:       chat-driven Summarize vertical slice (Context-pane mount,
//              non-modal)
// Purpose:     Inline preview of files uploaded into the active chat session
//              (`ChatSession.UploadedFiles`; spec NFR-02 hard-cap = 20).
//              Wraps the extracted `RichFilePreview` renderer (task 013 /
//              D2-08) instead of rebuilding iframe/metadata/menu UI (R5
//              CLAUDE.md §3.1 reuse mandate).
// Dispatches:  `context.file_selected` on the `context` channel when the
//              user picks a different file from the multi-file list
//              (additive ADR-030 event type added by task 016 / D2-06).
//              Also dispatches `workspace.widget_load` for the
//              `toggleWorkspace` per-file action so a file can be pinned
//              into the Workspace pane as a `document-viewer` tab.
// ---------------------------------------------------------------------------

safeRegisterContext('file-preview', {
  factory: () =>
    // Type-erasure cast: registry boundary variance (see playbook-gallery above).
    import('../widgets/context/FilePreviewContextWidget').then(m => ({
      default: m.default as unknown as ContextWidgetComponent,
    })),
});

// ---------------------------------------------------------------------------
// execution-trace (R6 task 062 / D-C-15)
//
// Widget type: 'execution-trace'
// Stage:       Active-chat (Context-pane primary widget when no entity is
//              selected and trace events are flowing). Subscribes to the six
//              `context.*` trace event types added by R6 task 059 (D-C-12):
//              tool_call_started, tool_call_completed, knowledge_retrieved,
//              playbook_node_executing, playbook_node_completed, decision_made.
// Purpose:     Renders a Claude-Code-like ordered timeline of the chat agent's
//              deterministic activity. Per ADR-015 BINDING: renders only the
//              typed enumerated fields (tool name + decision + timestamp +
//              numeric metrics) — NEVER user-message text or document content.
// Channel:     Subscribes to the existing `context` PaneEventBus channel —
//              NO new channel introduced (per ADR-030 + NFR-05).
// (also registered inline in index.ts — duplicate is safe, first wins)
// ---------------------------------------------------------------------------

safeRegisterContext('execution-trace', {
  factory: () =>
    // Type-erasure cast: registry boundary variance (see playbook-gallery above).
    import('../widgets/context/ExecutionTraceWidget').then(m => ({
      default: m.default as unknown as ContextWidgetComponent,
    })),
});

// ---------------------------------------------------------------------------
// Public registration function (called from shell entry points)
// ---------------------------------------------------------------------------

/**
 * registerContextWidgets
 *
 * No-op sentinel function — all registrations above execute as top-level
 * side effects when this module is imported. The function exists so that
 * callers can use a named import that makes the side-effect intent explicit:
 *
 *   import { registerContextWidgets } from './registry/register-context-widgets';
 *   registerContextWidgets(); // reads as: "ensure context widgets are registered"
 *
 * The function body is intentionally empty — the registrations already ran.
 */
export function registerContextWidgets(): void {
  // All registrations execute at module evaluation time (top-level side effects above).
}
