/**
 * @spaarke/ai-widgets — events barrel export
 *
 * Exports the PaneEventBus, typed event definitions, React context provider,
 * and React hooks for cross-pane communication.
 *
 * Internal helpers (usePaneEventBus) are intentionally NOT re-exported here —
 * consumers should use the public hooks (usePaneEvent, useDispatchPaneEvent).
 */

// Core bus implementation
export { PaneEventBus } from './PaneEventBus';

// Typed event definitions and channel map
export type {
  PaneChannel,
  PaneChannelEventMap,
  PaneEventHandler,
  WorkspacePaneEvent,
  ContextPaneEvent,
  ConversationPaneEvent,
  SafetyPaneEvent,
  WizardStepEvent,
} from './PaneEventTypes';

// React context provider
export { PaneEventBusProvider } from './PaneEventBusContext';
export type { PaneEventBusProviderProps } from './PaneEventBusContext';

// React hooks
export { usePaneEvent } from './usePaneEvent';
export { useDispatchPaneEvent } from './useDispatchPaneEvent';
export type { DispatchPaneEvent } from './useDispatchPaneEvent';
