/**
 * @spaarke/ai-widgets — register-context-widgets
 *
 * THE single context-widget registration module (ai-architecture-redesign-r1
 * task 046 / FR-P3-07 — registry dedupe per OVERLAY-MATRIX C3).
 *
 * Before task 046 the package carried TWO drifted registration modules
 * (`src/registry/register-context-widgets.ts` for the shell context widgets +
 * `src/widgets/context/register-context-widgets.ts` for the six R1 source
 * widgets) PLUS inline `registerContextWidget()` calls in the barrel
 * (`src/index.ts`). This module is now the ONE registration source consumed by
 * both mount paths:
 *
 *   - the dashboard wrapper path (`LegalWorkspaceApp` and other barrel
 *     consumers — `src/index.ts` imports this module once as a side effect),
 *   - the direct widget wrapper path (shell entry points that import widgets
 *     individually without the barrel import this module explicitly).
 *
 * Registration pattern:
 *   - Each registration is lazy: the factory is a dynamic import() that is NOT
 *     invoked until resolveContextWidget() is first called for that type.
 *   - Every call is isolated via safeRegister so a synchronous throw from one
 *     registration cannot skip those that follow (brittleness Phase B.5).
 *   - The registry silently ignores duplicate registrations (first wins), so
 *     importing this module from multiple entry points is safe.
 *
 * Adding a new context widget:
 *   1. Create the widget component in src/widgets/context/
 *   2. Add ONE safeRegisterContext() call below (this file only — do NOT add
 *      inline registrations to index.ts or a second registration module).
 *   3. Ensure the type string matches the value the BFF sends.
 *
 * Tasks: AIPU2-081 + AIPU2-086 (origins) → merged by task 046.
 */

import { registerContextWidget } from './ContextWidgetRegistry';
import { safeRegister } from '@spaarke/ui-components';
import type { ContextWidgetComponent } from '../types/widget-types';

// ai-spaarke-ai-workspace-UI-r1 brittleness Phase B.5 (2026-06-09):
// Isolate each registration so a synchronous throw from one call (factory-
// expression evaluation failure, malformed registration object) does not skip
// the registrations that follow. See safeRegister docblock + the same pattern
// in `../widgets/workspace/register-workspace-widgets.ts`.
function safeRegisterContext(...args: Parameters<typeof registerContextWidget>): void {
  safeRegister('ContextWidget', args[0], () => registerContextWidget(...args));
}

// ===========================================================================
// Shell context widgets (Context-pane stage widgets)
// ===========================================================================

// ---------------------------------------------------------------------------
// progress-tracker — workflow step progress (loading / analysis stages)
// ---------------------------------------------------------------------------

safeRegisterContext('progress-tracker', {
  factory: () => import('../widgets/context/ProgressTrackerWidget').then(m => ({ default: m.default })),
});

// ---------------------------------------------------------------------------
// playbook-gallery
//
// Stage:       Welcome / playbook-selection (before any conversation turn)
// Dispatches:  playbook_change → 'conversation' PaneEventBus channel
// ---------------------------------------------------------------------------

safeRegisterContext('playbook-gallery', {
  factory: () =>
    // Type-erasure cast: ContextWidgetComponent<unknown> at registry vs widget's
    // typed default (ContextWidgetComponent<PlaybookGalleryData>) — registry
    // boundary variance; the widget receives its typed data at render time.
    import('../widgets/context/PlaybookGalleryWidget').then(m => ({
      default: m.default as unknown as ContextWidgetComponent,
    })),
});

// ---------------------------------------------------------------------------
// get-started-cards
//
// Stage:       Welcome (FR-18). Registered for discoverability + symmetry —
//              GetStartedCardsWidget's props (`onCardClick`, `className`) are
//              NOT the standard ContextWidgetProps shape; hosts that need to
//              wire `onCardClick` import the named export directly (this is
//              what ContextPaneController does for the welcome stage).
// ---------------------------------------------------------------------------

safeRegisterContext('get-started-cards', {
  factory: () =>
    import('../widgets/context/GetStartedCardsWidget').then(m => ({
      // Intentional cast: prop-shape divergence documented above.
      default: m.GetStartedCardsWidget as unknown as ContextWidgetComponent,
    })),
});

// ---------------------------------------------------------------------------
// entity-info
//
// Stage:       entity-info (loading and early active-chat stages)
// Purpose:     Displays entity detail (display name, status, client, owner,
//              key dates, budget, custom fields) in the Context pane so users
//              have immediate context without requiring a chat turn.
//              Reacts to subsequent context_update events — updates reactively
//              when the user navigates to a different entity record.
// ---------------------------------------------------------------------------

safeRegisterContext('entity-info', {
  factory: () =>
    import('../widgets/context/EntityInfoWidget').then(m => ({
      default: m.default as unknown as ContextWidgetComponent,
    })),
});

// ---------------------------------------------------------------------------
// findings
//
// Stage:       sources-citations (after analysis completes)
// Purpose:     Displays structured analysis findings — key items, risk levels,
//              and citation links. Citation clicks dispatch context_highlight
//              to the 'context' PaneEventBus channel so the active
//              DocumentViewer scrolls to / highlights the cited passage.
// ---------------------------------------------------------------------------

safeRegisterContext('findings', {
  factory: () =>
    import('../widgets/context/FindingsWidget').then(m => ({
      default: m.default as unknown as ContextWidgetComponent,
    })),
});

// ---------------------------------------------------------------------------
// file-preview (R5 task 018 / D2-09)
//
// Stage:       chat-driven Summarize vertical slice (Context-pane mount,
//              non-modal)
// Purpose:     Inline preview of files uploaded into the active chat session
//              (`ChatSession.UploadedFiles`; spec NFR-02 hard-cap = 20).
//              Wraps the extracted `RichFilePreview` renderer (task 013 /
//              D2-08) instead of rebuilding iframe/metadata/menu UI.
// Dispatches:  `context.file_selected` on the `context` channel; also
//              `workspace.widget_load` for the `toggleWorkspace` per-file
//              action so a file can be pinned into the Workspace pane.
// ---------------------------------------------------------------------------

safeRegisterContext('file-preview', {
  factory: () =>
    import('../widgets/context/FilePreviewContextWidget').then(m => ({
      default: m.default as unknown as ContextWidgetComponent,
    })),
});

// ---------------------------------------------------------------------------
// execution-trace (task 046 / FR-P3-07 — ADR-040 ledger bridge)
//
// Stage:       Active-chat (Context-pane primary widget when trace data is
//              flowing). Subscribes to the `context.tool_chain` event — each
//              event carries one PERSISTED session-ledger ToolChain segment
//              (storage precedes rendering, ADR-040).
// Purpose:     Renders a Claude-Code-like ordered timeline of the agent's
//              tool activity from the session ledger. Per NFR-07 / ADR-015
//              BINDING: renders identifiers / redacted filter summaries /
//              counts / durations ONLY — never user or document content.
// Channel:     Existing `context` PaneEventBus channel — NO new channel
//              (ADR-030 + NFR-05).
// ---------------------------------------------------------------------------

safeRegisterContext('execution-trace', {
  factory: () =>
    import('../widgets/context/ExecutionTraceWidget').then(m => ({
      default: m.default as unknown as ContextWidgetComponent,
    })),
});

// ---------------------------------------------------------------------------
// pinned-memory-list (R6 task 070 / D-C-24 + D-C-25, Pillar 7 — Q7 expansion)
//
// Stage:       Active-chat (Context-pane primary widget when the user opens
//              the Pinned Memory pane). Loads the caller's pinned items via
//              `GET /api/memory/pins` and surfaces create / edit / delete CRUD.
// Standards:   ADR-008 + ADR-016 (auth + rate limit on BFF side), ADR-012,
//              ADR-013 (HTTP-only BFF consumption), ADR-015 (title / content
//              text NEVER logged), ADR-021 (semantic tokens only), ADR-028.
// ---------------------------------------------------------------------------

safeRegisterContext('pinned-memory-list', {
  factory: () =>
    import('../widgets/context/PinnedMemoryListWidget').then(m => ({
      default: m.default as unknown as ContextWidgetComponent,
    })),
});

// ===========================================================================
// R1 source widgets (server-driven `context_update` targets — AIPU2-081)
//
// Widget type strings MUST match the SourceWidgetType enum values from
// @spaarke/ai-outputs; the server sends these strings in `context_update`
// events and ContextPaneController resolves the matching component.
//
// Citation highlighting behaviour per widget:
//   - DocumentViewer  → queries [data-citation-id] overlay elements; debug
//                       trace for embedded PDF/iframe viewers.
//   - LegalLibrary    → scrolls blockquote excerpt into view.
//   - Citation        → scrolls matching <li data-citation-id="..."> into view.
//   - WebSource       → no-op (cross-origin iframe).
//   - ImageViewer     → no-op (single image, no addressable sub-regions).
//   - CodeViewer      → no-op (future: may map via selectionRef line range).
// ===========================================================================

safeRegisterContext('DocumentViewer', {
  factory: () =>
    import('../widgets/context/DocumentViewerContextWidget') as Promise<{
      default: ContextWidgetComponent;
    }>,
});

safeRegisterContext('WebSource', {
  factory: () =>
    import('../widgets/context/WebSourceContextWidget') as Promise<{
      default: ContextWidgetComponent;
    }>,
});

safeRegisterContext('LegalLibrary', {
  factory: () =>
    import('../widgets/context/LegalLibraryContextWidget') as Promise<{
      default: ContextWidgetComponent;
    }>,
});

safeRegisterContext('Citation', {
  factory: () =>
    import('../widgets/context/CitationContextWidget') as Promise<{
      default: ContextWidgetComponent;
    }>,
});

safeRegisterContext('ImageViewer', {
  factory: () =>
    import('../widgets/context/ImageViewerContextWidget') as Promise<{
      default: ContextWidgetComponent;
    }>,
});

safeRegisterContext('CodeViewer', {
  factory: () =>
    import('../widgets/context/CodeViewerContextWidget') as Promise<{
      default: ContextWidgetComponent;
    }>,
});

// ---------------------------------------------------------------------------
// Public registration function (called from entry points)
// ---------------------------------------------------------------------------

/**
 * registerContextWidgets
 *
 * No-op sentinel function — all registrations above execute as top-level
 * side effects when this module is imported. The function exists so that
 * callers can use a named import that makes the side-effect intent explicit:
 *
 *   import { registerContextWidgets } from '@spaarke/ai-widgets';
 *   registerContextWidgets(); // reads as: "ensure context widgets are registered"
 *
 * Idempotent — the registry ignores duplicate registrations (first wins).
 */
export function registerContextWidgets(): void {
  // All registrations execute at module evaluation time (top-level side effects above).
}
