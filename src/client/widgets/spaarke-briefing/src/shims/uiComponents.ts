/**
 * The build-time subset of `@spaarke/ui-components` this widget bundles (spaarkeai-mcp-bff-api-r2 task 034, FR-16, NFR-03).
 *
 * The Daily Briefing package imports the shared library by its package name (the barrel), which pulls the document viewer, the Office
 * converters, the telemetry client and the rest of the library into a page that has to stay under 3 MB. Each name below is re-exported from its
 * own file (the same deep-path rule the Decisions widget follows), so the bundle holds only what `DailyBriefingApp` and its children import.
 * The build maps the package name to this file (vite alias, tsconfig `paths`, jest `moduleNameMapper`); the deep paths
 * (`@spaarke/ui-components/...`) still reach the library's source.
 *
 * `getXrm` is the one stand-in: a sandboxed widget has no Xrm, so it answers "none" without walking frames. With a data service injected the
 * component never calls it for data or navigation (task 010); if a path ever did, it finds no Xrm and fails closed.
 */
import type { getXrm as sharedGetXrm } from '@spaarke/ui-components/utils/xrmContext';
import type { SendEmailDialog as SharedSendEmailDialog } from '@spaarke/ui-components/components/EmailComposer/wrappers/SendEmailDialog';
import type { RichFilePreviewDialog as SharedRichFilePreviewDialog } from '@spaarke/ui-components/components/FilePreview/RichFilePreviewDialog';

export { describeSendFailure } from '@spaarke/ui-components/components/EmailComposer/describeSendFailure';
export { OOB_MODAL_SIZES } from '@spaarke/ui-components/utils/adapters/oobModalSizes';
export { cleanGuid } from '@spaarke/ui-components/utils/guid';
export { EmptyState } from '@spaarke/ui-components/components/EmptyState';
export { openInDesktopApp, OPEN_IN_DESKTOP_FAILED_MESSAGE } from '@spaarke/ui-components/utils/openInDesktopApp';
export { RowActionMenu } from '@spaarke/ui-components/components/RowActionMenu/RowActionMenu';
export type { RowActionDescriptor } from '@spaarke/ui-components/components/RowActionMenu/RowActionMenu';
export { MicrosoftToDoIcon } from '@spaarke/ui-components/icons/MicrosoftToDoIcon';
export { formatRelativeTime } from '@spaarke/ui-components/utils/relativeTime';
export { parseDueDate, daysBetweenLocalMidnight } from '@spaarke/ui-components/utils/dateLocal';

/** No Xrm in a sandboxed widget. Typed as the real function so the package's calls type-check unchanged. */
export const getXrm = (() => undefined) as unknown as typeof sharedGetXrm;

/**
 * The email composer and the document preview are stand-ins that draw nothing: the component opens them only from actions a widget does not
 * offer (email share, document preview; task 010 hides both when a data service is injected), and the composer alone carries the rich-text
 * editor (lexical) that would take the page past its size budget. Typed as the real components so the package's JSX type-checks unchanged.
 */
export const SendEmailDialog = (() => null) as unknown as typeof SharedSendEmailDialog;
export const RichFilePreviewDialog = (() => null) as unknown as typeof SharedRichFilePreviewDialog;
