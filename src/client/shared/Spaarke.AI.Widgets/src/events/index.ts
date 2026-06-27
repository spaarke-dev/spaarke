/**
 * @spaarke/ai-widgets — events barrel export
 *
 * Exports the PaneEventBus, typed event definitions, React context provider,
 * and React hooks for cross-pane communication.
 *
 * `usePaneEventBus` is exposed here so host modules (R6 Pillar 8 task 081
 * HardSlashExecutor onwards) can pass the full bus instance into context
 * objects that dispatch on multiple channels. The thin hooks
 * (useDispatchPaneEvent / usePaneEvent) remain the preferred surface for
 * single-channel components.
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
export { PaneEventBusProvider, usePaneEventBus } from './PaneEventBusContext';
export type { PaneEventBusProviderProps } from './PaneEventBusContext';

// React hooks
export { usePaneEvent } from './usePaneEvent';
export { useDispatchPaneEvent } from './useDispatchPaneEvent';
export type { DispatchPaneEvent } from './useDispatchPaneEvent';
