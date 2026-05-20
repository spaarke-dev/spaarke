/**
 * @spaarke/ai-widgets — register-context-widgets
 *
 * Registers all 6 R1 source widgets in the R2 ContextWidgetRegistry.
 *
 * Each widget is lazy-loaded via a dynamic import() factory so its module
 * is only fetched from the network when the Context pane first needs to
 * render that widget type. The factory returns the default-exported
 * ContextWidgetComponent (an adapted wrapper over the R1 source widget).
 *
 * Widget type strings match the SourceWidgetType enum values from
 * @spaarke/ai-outputs. The server sends these strings in `context_update`
 * events; the ContextPaneController resolves the matching component via
 * resolveContextWidget().
 *
 * Citation highlighting:
 * - DocumentViewer  → queries [data-citation-id] overlay elements; debug
 *                     trace for embedded PDF/iframe viewers.
 * - LegalLibrary    → scrolls blockquote excerpt into view.
 * - Citation        → scrolls matching <li data-citation-id="..."> into view.
 * - WebSource       → no-op (cross-origin iframe).
 * - ImageViewer     → no-op (single image, no addressable sub-regions).
 * - CodeViewer      → no-op (future: may map via selectionRef line range).
 *
 * Usage:
 *   Call registerContextWidgets() once at application startup — it is
 *   idempotent (duplicate registrations are silently ignored by the registry).
 *
 * Task: AIPU2-081
 */

import { registerContextWidget } from '../../registry/ContextWidgetRegistry';

// ---------------------------------------------------------------------------
// Widget type string constants
//
// These MUST match SourceWidgetType enum values from @spaarke/ai-outputs so
// that server-sent context_update events resolve to the correct component.
// We declare them as constants here to avoid importing the enum at module load
// time (keeping this file side-effect-free except for registerContextWidget calls).
// ---------------------------------------------------------------------------

const WIDGET_TYPE_DOCUMENT_VIEWER = 'DocumentViewer';
const WIDGET_TYPE_WEB_SOURCE = 'WebSource';
const WIDGET_TYPE_LEGAL_LIBRARY = 'LegalLibrary';
const WIDGET_TYPE_CITATION = 'Citation';
const WIDGET_TYPE_IMAGE_VIEWER = 'ImageViewer';
const WIDGET_TYPE_CODE_VIEWER = 'CodeViewer';

// ---------------------------------------------------------------------------
// Registration
// ---------------------------------------------------------------------------

/**
 * Register all 6 source context widgets in the ContextWidgetRegistry.
 *
 * Idempotent — safe to call multiple times (registry ignores duplicate
 * registrations after the first call per type, per ContextWidgetRegistry
 * contract).
 *
 * @example
 * // In your application entry point:
 * import { registerContextWidgets } from '@spaarke/ai-widgets';
 * registerContextWidgets();
 */
export function registerContextWidgets(): void {
  // -------------------------------------------------------------------------
  // DocumentViewer — SPE document preview (PDF/iframe)
  // Highlight: queries [data-citation-id] overlay elements; debug trace for embeds.
  // -------------------------------------------------------------------------
  registerContextWidget(WIDGET_TYPE_DOCUMENT_VIEWER, {
    factory: () =>
      import('./DocumentViewerContextWidget') as Promise<{
        default: import('../../types/widget-types').ContextWidgetComponent;
      }>,
  });

  // -------------------------------------------------------------------------
  // WebSource — URL bar + sandboxed iframe web preview
  // Highlight: no-op (cross-origin iframe, cannot scroll programmatically).
  // -------------------------------------------------------------------------
  registerContextWidget(WIDGET_TYPE_WEB_SOURCE, {
    factory: () =>
      import('./WebSourceContextWidget') as Promise<{
        default: import('../../types/widget-types').ContextWidgetComponent;
      }>,
  });

  // -------------------------------------------------------------------------
  // LegalLibrary — structured legal case / statute citation card
  // Highlight: scrolls blockquote excerpt into view.
  // -------------------------------------------------------------------------
  registerContextWidget(WIDGET_TYPE_LEGAL_LIBRARY, {
    factory: () =>
      import('./LegalLibraryContextWidget') as Promise<{
        default: import('../../types/widget-types').ContextWidgetComponent;
      }>,
  });

  // -------------------------------------------------------------------------
  // Citation — numbered citation reference list
  // Highlight: scrolls matching <li data-citation-id="..."> into view.
  // -------------------------------------------------------------------------
  registerContextWidget(WIDGET_TYPE_CITATION, {
    factory: () =>
      import('./CitationContextWidget') as Promise<{
        default: import('../../types/widget-types').ContextWidgetComponent;
      }>,
  });

  // -------------------------------------------------------------------------
  // ImageViewer — image with pan/zoom via CSS transform
  // Highlight: no-op (single image, no addressable sub-regions).
  // -------------------------------------------------------------------------
  registerContextWidget(WIDGET_TYPE_IMAGE_VIEWER, {
    factory: () =>
      import('./ImageViewerContextWidget') as Promise<{
        default: import('../../types/widget-types').ContextWidgetComponent;
      }>,
  });

  // -------------------------------------------------------------------------
  // CodeViewer — monospace code block with line numbers + copy
  // Highlight: no-op (future: may map via selectionRef line range).
  // -------------------------------------------------------------------------
  registerContextWidget(WIDGET_TYPE_CODE_VIEWER, {
    factory: () =>
      import('./CodeViewerContextWidget') as Promise<{
        default: import('../../types/widget-types').ContextWidgetComponent;
      }>,
  });
}
