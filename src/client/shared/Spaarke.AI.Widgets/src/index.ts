// @spaarke/ai-widgets — barrel export

// ---------------------------------------------------------------------------
// Side-effect: register all R1 output widgets into WorkspaceWidgetRegistry
// (AIPU2-080 — data-refreshed restore, D-08)
// ---------------------------------------------------------------------------
import { registerWorkspaceWidgets } from './widgets/workspace/register-workspace-widgets';
registerWorkspaceWidgets();

// ---------------------------------------------------------------------------
// Side-effect: register all 6 R1 source widgets into ContextWidgetRegistry
// (AIPU2-081 — migrate source widgets to context pane)
// ---------------------------------------------------------------------------
import { registerContextWidgets } from './widgets/context/register-context-widgets';
registerContextWidgets();

// ---------------------------------------------------------------------------
// Types — React component prop contracts (tasks AIPU2-072/073)
// ---------------------------------------------------------------------------

export type {
  WorkspaceWidgetProps,
  WorkspaceWidgetComponent,
  ContextWidgetProps,
  ContextWidgetComponent,
  // Re-exports of task-071 types (via widget-types.ts pass-through):
  WidgetRenderContext,
  Selection,
  ActionResult,
  WidgetState,
  WidgetRegistryEntry,
  WidgetRegistryMetadata,
  WorkspaceWidget,
  WidgetActionDescriptor,
  ContextWidget,
} from './types/widget-types';

// WidgetMetadata — canonical definition from shared.ts (task AIPU2-071).
// Required by WorkspaceWidgetRegistry.registerWorkspaceWidget().
export type { WidgetMetadata } from './types/shared';

export * from './types/event-types';

// ---------------------------------------------------------------------------
// Registries: WorkspaceWidgetRegistry and ContextWidgetRegistry
// ---------------------------------------------------------------------------

// WorkspaceWidgetRegistry — lazy-load with GenericTextWidget fallback
export {
  registerWorkspaceWidget,
  replaceWorkspaceWidget,
  resolveWorkspaceWidget,
  getWorkspaceWidgetMetadata,
  getAllWorkspaceWidgetTypes,
  hasWorkspaceWidget,
  clearWorkspaceRegistry,
} from './registry/WorkspaceWidgetRegistry';

export type { WorkspaceWidgetRegistration } from './registry/WorkspaceWidgetRegistry';

// ContextWidgetRegistry — lazy-load with null-return for unknown types
export {
  registerContextWidget,
  replaceContextWidget,
  resolveContextWidget,
  hasContextWidget,
  getAllContextWidgetTypes,
  clearContextRegistry,
} from './registry/ContextWidgetRegistry';

export type { ContextWidgetRegistration } from './registry/ContextWidgetRegistry';

// ---------------------------------------------------------------------------
// Widgets: GenericTextWidget (fallback for unregistered workspace widget types)
// ---------------------------------------------------------------------------

export { default as GenericTextWidget } from './widgets/GenericTextWidget';

// ---------------------------------------------------------------------------
// Widgets: RedlineViewerWidget — side-by-side document comparison (AIPU2-085)
// Exported so consumers can reference the component directly and type-check
// DocumentDiff payloads. Registration under 'redline-viewer' occurs via the
// register-workspace-widgets side-effect import at the top of this file.
// ---------------------------------------------------------------------------

export { default as RedlineViewerWidget } from './widgets/workspace/RedlineViewerWidget';
export type {
  RedlineViewerData,
  RedlineViewerActions,
  RedlineViewerQueryParams,
  DiffSection,
  DiffChange,
  DiffChangeType,
} from './widgets/workspace/RedlineViewerWidget';
export { serializeRedlineState } from './widgets/workspace/RedlineViewerWidget';

// ---------------------------------------------------------------------------
// Widgets: ProgressTrackerWidget (context pane — workflow step progress)
//
// Exported so consumers can reference the component directly and type-check
// context_update payloads. The registerContextWidget() call below registers
// the widget at module-load time under the 'progress-tracker' type key.
// ---------------------------------------------------------------------------

export { default as ProgressTrackerWidget } from './widgets/context/ProgressTrackerWidget';
export type {
  ProgressTrackerData,
  WorkflowStep,
} from './widgets/context/ProgressTrackerWidget';

import { registerContextWidget } from './registry/ContextWidgetRegistry';
registerContextWidget('progress-tracker', {
  factory: () =>
    import('./widgets/context/ProgressTrackerWidget').then((m) => ({ default: m.default })),
});

// ---------------------------------------------------------------------------
// Widgets: PlaybookGalleryWidget (context pane — Welcome / playbook-gallery stage)
//
// Exported so consumers can reference the component directly and type-check
// PlaybookGalleryData payloads. Registered under 'playbook-gallery'.
// ---------------------------------------------------------------------------

export { default as PlaybookGalleryWidget } from './widgets/context/PlaybookGalleryWidget';
export type {
  PlaybookGalleryData,
  PlaybookSummary,
} from './widgets/context/PlaybookGalleryWidget';

registerContextWidget('playbook-gallery', {
  factory: () =>
    import('./widgets/context/PlaybookGalleryWidget').then((m) => ({ default: m.default })),
});

// ---------------------------------------------------------------------------
// Widgets: FindingsWidget (context pane — sources-citations stage)
//
// Exported so consumers can reference the component directly and type-check
// FindingsData payloads. Registered under 'findings'.
// Citation link clicks dispatch context_highlight to the 'context' channel
// so the active DocumentViewer scrolls to / highlights the cited passage.
// ---------------------------------------------------------------------------

export { default as FindingsWidget } from './widgets/context/FindingsWidget';
export type {
  FindingsData,
  Finding,
  Citation,
  RiskLevel,
} from './widgets/context/FindingsWidget';

registerContextWidget('findings', {
  factory: () =>
    import('./widgets/context/FindingsWidget').then((m) => ({ default: m.default })),
});

// ---------------------------------------------------------------------------
// Providers: AiSessionProvider (R2 session state + PaneEventBus routing)
// ---------------------------------------------------------------------------

// AiSessionProvider — replaces R1 StandaloneAiProvider; routes SSE events to PaneEventBus
export { AiSessionProvider } from './providers/AiSessionProvider';
export type {
  AiSessionContextValue,
  AiSessionProviderProps,
  AiContextMapping,
} from './providers/AiSessionProvider';
export { AI_SESSION_CHAT_SESSION_KEY, AI_SESSION_PLAYBOOK_KEY } from './providers/AiSessionProvider';

// useAiSession — consumer hook for AiSessionContext (replaces R1 useStandaloneAi)
export { useAiSession } from './providers/useAiSession';

// ---------------------------------------------------------------------------
// Components: ConfidenceIndicator (AIPU2-091)
//
// Per-response confidence bar rendered below AI messages. Driven by the safety
// pipeline confidence score; color-coded high/medium/low using Fluent v9
// semantic status tokens. Low confidence adds a disclaimer text.
// ---------------------------------------------------------------------------

export { ConfidenceIndicator } from './components/ConfidenceIndicator';
export type { ConfidenceIndicatorProps, ConfidenceLevel } from './components/ConfidenceIndicator';

// ---------------------------------------------------------------------------
// Components: FeedbackButtons (thumbs up/down + optional comment — AIPU2-092)
//
// Non-intrusive rating control rendered beneath each completed AI message.
// Thumbs-up submits immediately; thumbs-down reveals an optional Textarea.
// Calls POST /api/ai/feedback and shows a brief checkmark confirmation.
// Only render when streaming is complete (isStreaming === false).
// ---------------------------------------------------------------------------

export { FeedbackButtons } from './components/FeedbackButtons';
export type { FeedbackButtonsProps, FeedbackRating } from './components/FeedbackButtons';

// ---------------------------------------------------------------------------
// Events: PaneEventBus, typed channels, React context + hooks
// ---------------------------------------------------------------------------

// Core bus class (for advanced usage and testing — most consumers use the hooks)
export { PaneEventBus } from './events/PaneEventBus';

// Typed event definitions
export type {
  PaneChannel,
  PaneChannelEventMap,
  PaneEventHandler,
  WorkspacePaneEvent,
  ContextPaneEvent,
  ConversationPaneEvent,
  SafetyPaneEvent,
} from './events/PaneEventTypes';

// React context provider — wrap the three-pane shell root with this
export { PaneEventBusProvider } from './events/PaneEventBusContext';
export type { PaneEventBusProviderProps } from './events/PaneEventBusContext';

// React hooks — primary API for components
export { usePaneEvent } from './events/usePaneEvent';
export { useDispatchPaneEvent } from './events/useDispatchPaneEvent';
export type { DispatchPaneEvent } from './events/useDispatchPaneEvent';

// ---------------------------------------------------------------------------
// Safety annotation UI (AIPU2-090 — FR-402, FR-403)
//
// SafetyAnnotationOverlay — subscribes to 'safety' PaneEventBus channel and
// applies retroactive groundedness highlights + citation verification badges
// ~200 ms after the last streaming token (D-03: stream + retroactive).
//
// AnnotatedMessageContent — stateless unified render of groundedness +
// citation badge layers; exported for direct use when annotation state is
// already available (e.g. in tests or server-side pre-annotated payloads).
//
// CitationBadge — inline Fluent v9 Badge for citation verification status.
//   Variants: verified (green CheckmarkCircle), unverified (orange Warning),
//             partial (blue ArrowSwap).
//
// GroundednessHighlight — wraps text with visual indicators for ungrounded
//   segments: dashed underline + colorStatusWarningBackground1 fill.
// ---------------------------------------------------------------------------

export { default as SafetyAnnotationOverlay } from './components/SafetyAnnotationOverlay';
export { AnnotatedMessageContent } from './components/SafetyAnnotationOverlay';
export type {
  SafetyAnnotationOverlayProps,
  AnnotatedMessageContentProps,
} from './components/SafetyAnnotationOverlay';

export { CitationBadge } from './components/CitationBadge';
export type {
  CitationBadgeProps,
  CitationVerificationResult,
  CitationVerificationStatus,
} from './components/CitationBadge';

export { GroundednessHighlight } from './components/GroundednessHighlight';
export type {
  GroundednessHighlightProps,
  GroundednessSegment,
} from './components/GroundednessHighlight';
