/**
 * Pane event types for @spaarke/ai-widgets.
 *
 * Re-exports the typed channel and event definitions from the events module
 * so they are available via both the types barrel and the events barrel.
 *
 * Populated by task AIPU2-074 (PaneEventBus — three-pane event bus).
 */

export type {
  PaneChannel,
  PaneChannelEventMap,
  PaneEventHandler,
  WorkspacePaneEvent,
  ContextPaneEvent,
  ConversationPaneEvent,
  SafetyPaneEvent,
} from '../events/PaneEventTypes';
