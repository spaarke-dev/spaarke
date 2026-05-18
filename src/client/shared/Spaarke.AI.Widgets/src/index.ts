// @spaarke/ai-widgets — barrel export

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
