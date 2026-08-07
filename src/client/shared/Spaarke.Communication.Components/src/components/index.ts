/**
 * `@spaarke/communication-components/components` — shared presentational
 * React components. Added email-communication-solution-r5 task 021 alongside
 * `../logic/attachments`; consumed by both the OOB-form PCF (deep-imported
 * source, React 16/17) and the React-19 code-page barrel.
 */

export * from './AttachmentList';
export * from './ReconciliationBrowseShell';
export * from './EmailCardList';
export * from './EmailViewSelector';
export * from './EmailReadingPaneShell';
export * from './EmailBody';
export * from './EmailReadingHeader';
export * from './EmailRecipients';
export * from './EmailAssociationsAndTracking';
export * from './EmailComposeActions';
export * from './EmailWorkspace';

// ReconciliationGrid - Pillar E "Needs review" grid over sprk_communication
// (email-communication-intelligence-r2 task 050, FR-E2). Thin wrapper over the
// shared `<DataGrid configId={...} />` framework — see ReconciliationGrid.tsx.
export * from './ReconciliationGrid';

// ReconcileTabs - Pillar E reconcile tabs (Fields = task 055 / FR-E4; Tasks =
// task 056 / FR-E5) mounted in the browse-shell right pane + email record form.
export * from './ReconcileTabs';
