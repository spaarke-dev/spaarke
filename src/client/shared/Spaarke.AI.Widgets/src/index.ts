// @spaarke/ai-widgets — barrel export
// Widget implementations are added by subsequent tasks (071–074).

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export * from './types/widget-types';
export * from './types/event-types';

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
