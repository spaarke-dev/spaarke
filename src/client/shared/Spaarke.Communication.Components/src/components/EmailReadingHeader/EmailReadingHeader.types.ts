/**
 * EmailReadingHeader.types.ts
 *
 * Type contract for `<EmailReadingHeader />` — the reading-pane TITLE BAR (the
 * email subject on its own row with a light-gray background, rendered ABOVE the
 * toolbar) + `<EmailReadingAttachments />` (the attachments sub-view, FR-12).
 *
 * (Reading-pane MAIN-AREA redesign, email-communication-solution-r5: the header
 * band used to carry the subject PLUS a compact tracking trio PLUS an "Open full
 * form" icon. Per the locked owner design the tracking fields are REMOVED from
 * the reading pane entirely, "Open full form" moved into the toolbar, and this
 * component is now just the light-gray title bar showing the subject. It remains
 * PURELY presentational — subject in, nothing out; no data fetch.)
 *
 * `EmailReadingAttachments` is unchanged — still host-agnostic (ADR-012), still
 * takes the portable `IDataService` / `INavigationService` abstractions from
 * `@spaarke/ui-components` (NOT `ComponentFramework.WebApi` / raw `Xrm`).
 */

/** Props for `<EmailReadingHeader />` — the reading-pane TITLE BAR. Purely presentational: the subject arrives from the host (`EmailWorkspace`), fed by the shared `EmailWorkspaceRecordState`. No internal data fetch. */
export interface IEmailReadingHeaderProps {
  /** `EmailWorkspaceRecordState.subject` — `null` while loading or when the field is absent on this deployment. Renders "(no subject)" when falsy. */
  subject: string | null;
}

/** Props for `<EmailReadingAttachments />` — the attachments sub-view (FR-12). */
export interface IEmailReadingAttachmentsProps {
  /** The selected `sprk_communication` id (from the reading-pane shell's `renderBody` slot). */
  selectedId: string;
  /**
   * Host-injected data adapter (Xrm or BFF — ADR-012 portability). Adapted
   * internally to the `IAttachmentsWebApi` shape the promoted (task 021)
   * `CommunicationAttachmentsService` expects.
   */
  dataService: IEmailAttachmentsDataService;
  /**
   * Host-injected navigation adapter (Xrm or BFF — ADR-012 portability).
   * Used for "open record" on a previewed attachment's `sprk_document`.
   */
  navigation: IEmailAttachmentsNavigationService;
  /** BFF API base URL (host only) — passed to `AttachmentApiService` (task 021) for preview/open-link resolution. */
  apiBaseUrl: string;
  /**
   * Fired after the attachment list loads (or reloads) with the number of file
   * attachments (inline images already excluded). Lets a collapsed host section
   * show a header count ("Attachments (3)") without the user expanding it.
   * Fires with `0` when the record has no attachments or on load failure.
   */
  onCountChange?: (count: number) => void;
}

/** Minimal `retrieveMultipleRecords` surface this view consumes. */
export interface IEmailAttachmentsDataService {
  retrieveMultipleRecords(entityName: string, options?: string): Promise<{ entities: Record<string, unknown>[] }>;
}

/** Minimal `openRecord` surface this view consumes. */
export interface IEmailAttachmentsNavigationService {
  openRecord(entityName: string, entityId: string): Promise<void>;
}
