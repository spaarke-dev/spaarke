// Main shell component
export { TaskPaneShell } from './TaskPaneShell';
export type { TaskPaneShellProps, HostType, NavigationTab, ConnectionStatus } from './TaskPaneShell';

// Header component
export { TaskPaneHeader } from './TaskPaneHeader';
export type { TaskPaneHeaderProps } from './TaskPaneHeader';

// Navigation component
export { TaskPaneNavigation, getDefaultTab } from './TaskPaneNavigation';
export type { TaskPaneNavigationProps } from './TaskPaneNavigation';

// Footer component
export { TaskPaneFooter } from './TaskPaneFooter';
export type { TaskPaneFooterProps } from './TaskPaneFooter';

// Error handling
export { ErrorBoundary, withErrorBoundary } from './ErrorBoundary';

// Loading state
export { LoadingSkeleton } from './LoadingSkeleton';
export type { LoadingSkeletonProps } from './LoadingSkeleton';

// Entity Picker component
export { EntityPicker } from './EntityPicker';
export type { EntityPickerProps } from './EntityPicker';

// Attachment Selector component
export { AttachmentSelector } from './AttachmentSelector';
export type { AttachmentSelectorProps } from './AttachmentSelector';

// Save Flow component
export { SaveFlow } from './SaveFlow';
export type { SaveFlowProps } from './SaveFlow';

// Views
export { SaveView } from './views/SaveView';
export type { SaveViewProps } from './views/SaveView';
export { ShareView } from './views/ShareView';
export { StatusView } from './views/StatusView';
export { SignInView } from './views/SignInView';
