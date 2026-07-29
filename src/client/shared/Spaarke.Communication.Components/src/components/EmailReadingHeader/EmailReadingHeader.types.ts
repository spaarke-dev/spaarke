/**
 * EmailReadingHeader.types.ts
 *
 * Type contract for `<EmailReadingHeader />` + `<EmailReadingAttachments />`
 * (email-communication-solution-r5 task 034, spec FR-11/FR-12). These are the
 * two sub-views that fill the `EmailReadingPaneShell` (task 032) `renderHeader`
 * / `renderAttachments` slots — see
 * `projects/email-communication-solution-r5/notes/task-032-reading-pane-shell.md`.
 *
 * Both components are host-agnostic (ADR-012): they take the portable
 * `IDataService` / `INavigationService` abstractions from `@spaarke/ui-components`
 * (NOT `ComponentFramework.WebApi` / raw `Xrm`) so the eventual `EmailWorkspace`
 * composition root (task 040) can inject either the Xrm-backed or BFF-backed
 * adapter without this view knowing which host it's running under.
 */

/**
 * Subset of a `sprk_communication` record this header reads. Field names
 * verified against `docs/data-model/sprk_communication.md` (also mirrored by
 * `ICommunicationRecord` in the `CommunicationPage` code page — see
 * deviation note in `EmailReadingHeader.tsx`).
 */
export interface IEmailHeaderRecord {
  sprk_communicationid?: string;
  sprk_subject?: string | null;
  sprk_from?: string | null;
  sprk_to?: string | null;
  sprk_cc?: string | null;
  sprk_bcc?: string | null;
  sprk_sentat?: string | null;
  sprk_receiveddate?: string | null;
}

/** Props for `<EmailReadingHeader />` — the envelope header sub-view (FR-11). */
export interface IEmailReadingHeaderProps {
  /** The selected `sprk_communication` id (from the reading-pane shell's `renderHeader` slot). */
  selectedId: string;
  /**
   * Host-injected data adapter (Xrm or BFF — ADR-012 portability). Retrieves
   * the record's envelope fields via `retrieveRecord('sprk_communication', selectedId, select)`.
   */
  dataService: IEmailHeaderDataService;
}

/**
 * Minimal `retrieveRecord` surface this header consumes — a structural subset
 * of `@spaarke/ui-components` `IDataService` so tests can supply a lightweight
 * double without implementing the full 5-method contract.
 */
export interface IEmailHeaderDataService {
  retrieveRecord(entityName: string, id: string, options?: string): Promise<Record<string, unknown>>;
}

/** Props for `<EmailReadingAttachments />` — the attachments sub-view (FR-12). */
export interface IEmailReadingAttachmentsProps {
  /** The selected `sprk_communication` id (from the reading-pane shell's `renderAttachments` slot). */
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
}

/** Minimal `retrieveMultipleRecords` surface this view consumes. */
export interface IEmailAttachmentsDataService {
  retrieveMultipleRecords(entityName: string, options?: string): Promise<{ entities: Record<string, unknown>[] }>;
}

/** Minimal `openRecord` surface this view consumes. */
export interface IEmailAttachmentsNavigationService {
  openRecord(entityName: string, entityId: string): Promise<void>;
}
