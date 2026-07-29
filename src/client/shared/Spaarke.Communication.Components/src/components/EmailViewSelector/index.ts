/**
 * EmailViewSelector — Email surface view-selection wiring (task 031, FR-04).
 *
 * `useEmailViews` — host data hook (fetch saved views, default to "Email —
 * Inbox", re-run FetchXML on selection change).
 * `EmailViewSelector` — thin container rendering the reused
 * `DataGridViewSelector` (`@spaarke/ui-components`, ADR-012).
 */
export { useEmailViews, EMAIL_INBOX_VIEW_NAME } from './useEmailViews';
export type { UseEmailViewsResult } from './useEmailViews';

export { EmailViewSelector } from './EmailViewSelector';
export type { EmailViewSelectorProps } from './EmailViewSelector';
