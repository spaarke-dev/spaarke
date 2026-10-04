/**
 * @spaarke/ai-widgets — Registry barrel
 *
 * Exports all public API surface from WorkspaceWidgetRegistry and
 * ContextWidgetRegistry. Import from "@spaarke/ai-widgets" (via the
 * package src/index.ts) rather than from this file directly.
 */

// ---------------------------------------------------------------------------
// WorkspaceWidgetRegistry — lazy-load registry with GenericTextWidget fallback
// ---------------------------------------------------------------------------

export {
  registerWorkspaceWidget,
  replaceWorkspaceWidget,
  resolveWorkspaceWidget,
  getWorkspaceWidgetMetadata,
  getAllWorkspaceWidgetTypes,
  hasWorkspaceWidget,
  clearWorkspaceRegistry,
} from './WorkspaceWidgetRegistry';

export type { WorkspaceWidgetRegistration } from './WorkspaceWidgetRegistry';

// WidgetMetadata is exported from @spaarke/ai-widgets via types/widget-types.ts.
// Do not re-export here to avoid duplicate identifier errors.

// ---------------------------------------------------------------------------
// ContextWidgetRegistry — lazy-load registry with null-return for unknowns
// ---------------------------------------------------------------------------

export {
  registerContextWidget,
  replaceContextWidget,
  resolveContextWidget,
  hasContextWidget,
  getAllContextWidgetTypes,
  clearContextRegistry,
} from './ContextWidgetRegistry';

export type { ContextWidgetRegistration } from './ContextWidgetRegistry';
