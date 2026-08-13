/**
 * SidePane - Shared side pane components for Dataverse web resource side panes
 *
 * Provides reusable layout and interaction patterns for side panes
 * used across Event, Matter, Project, and other entity detail views.
 *
 * @see ADR-012 - Shared Component Library
 * @see Task 108 - Extract shared side pane components
 */

export { SidePaneShell, type SidePaneShellProps } from './SidePaneShell';

// SprkSidePaneHost + sidePaneRegistry — the reusable, multi-contributor
// docked side-pane framework (spaarke-side-pane-navigation-history-r1 task 011).
export { SprkSidePaneHost, type SprkSidePaneHostProps } from './SprkSidePaneHost';
export {
  registerSidePaneContributor,
  replaceSidePaneContributor,
  getSidePaneRegistryEntry,
  hasSidePaneContributor,
  listSidePaneRegistryEntries,
  resolveSidePaneContributor,
  clearSidePaneRegistry,
  type SidePaneRegistryEntry,
  type SidePaneContributorProps,
  type SidePaneContributorComponent,
} from './sidePaneRegistry';
