/**
 * `@spaarke/communication-components` — EmailBody barrel.
 *
 * The reading-pane BODY sub-view (email-communication-solution-r5 task 033):
 * renders the selected email "as sent" via a sandboxed iframe (`.eml` render)
 * with a client-sanitized `sprk_body` degradation path. Fills the task-032
 * `EmailReadingPaneShell` `renderBody(selectedId)` slot.
 */
export { EmailBodyView } from './EmailBodyView';
export { HighlightedText } from './HighlightedText';
export type { HighlightedTextProps } from './HighlightedText';
export type { EmailBodyViewProps, AuthenticatedFetchFn, ReconciliationAttachmentContent } from './EmailBodyView.types';
