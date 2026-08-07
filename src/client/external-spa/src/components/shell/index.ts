/**
 * Module-host workspace shell — the reusable SpaarkeAi-style chassis for the
 * external Legal Service Portal (task 011). Task 012 builds the widget registry,
 * role-defaulted widget tabs, and the widget library on top of these primitives.
 */
export { PortalWorkspaceShell } from './PortalWorkspaceShell';
export type { PortalWorkspaceShellProps } from './PortalWorkspaceShell';

export { TabStrip } from './TabStrip';
export type { TabItem, TabStripProps } from './TabStrip';

export { QuickStartPane } from './QuickStartPane';
export type { QuickStartPaneProps } from './QuickStartPane';

export { AssistantPane } from './AssistantPane';
export type { AssistantPaneProps } from './AssistantPane';

export { WidgetLibraryModal } from './WidgetLibraryModal';
export type { WidgetLibraryModalProps } from './WidgetLibraryModal';

export { useWorkspaceTabs, QUICK_START_TAB } from './useWorkspaceTabs';
export type { WidgetTab, UseWorkspaceTabsResult } from './useWorkspaceTabs';
