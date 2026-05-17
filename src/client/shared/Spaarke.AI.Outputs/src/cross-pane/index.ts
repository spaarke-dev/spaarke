/**
 * @spaarke/ai-outputs — Cross-Pane Linking barrel export
 *
 * Cross-pane linking components and utilities implemented in Wave 3 (task 031).
 * This module handles citation clicks in the output pane navigating to and
 * highlighting referenced ranges in the source pane, without any shared React
 * context or Redux store. Communication is via a CustomEvent on document.
 *
 * Exports:
 *   - cross-pane-events: typed event definitions, dispatch helper, subscribe helper
 *   - useCrossPane: React hooks for dispatch (output pane) and subscription (source pane)
 *   - CrossPaneLink: interactive inline component that fires the cross-pane event
 *
 * Note: The CrossPaneLink *interface* (data model for pane link state) is exported
 * from the types barrel (./types/index.ts). The CrossPaneLink *component* (UI element)
 * is exported here.
 */

// Wave 3 (task 031): cross-pane event definitions and helpers
export { CROSS_PANE_LINK_EVENT, dispatchCrossPaneLink, subscribeToCrossPaneLinks } from './cross-pane-events';
export type { CrossPaneLinkEvent } from './cross-pane-events';

// Wave 3 (task 031): React hooks
export { useDispatchCrossPaneLink, useCrossPaneSubscription } from './useCrossPane';

// Wave 3 (task 031): CrossPaneLink interactive component
export { CrossPaneLink } from './CrossPaneLink';
export type { CrossPaneLinkProps } from './CrossPaneLink';
