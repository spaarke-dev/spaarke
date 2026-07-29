/**
 * EmailReadingHeader.types.ts
 *
 * Type contract for `<EmailReadingHeader />` (the reading-pane HEADER BAND —
 * subject + compact tracking trio + "Open full form") + `<EmailReadingAttachments />`
 * (email-communication-solution-r5 task 034, spec FR-11/FR-12; reworked for the
 * reading-pane layout redesign — see `EmailReadingHeader.tsx` docblock).
 *
 * `EmailReadingHeader` is PURELY presentational now: it owns NO data fetch.
 * Earlier it ran its own `retrieveRecord('sprk_communication', id, $select)`
 * — a second, redundant per-selection read (the composition root,
 * `EmailWorkspace`, already owns the ONE per-selection read via
 * `useEmailWorkspaceRecord`) — and that `$select` call failed at runtime
 * ("Could not load this email header."). It now takes every value as a prop,
 * fed by the host from `EmailWorkspaceRecordState` (`EmailWorkspace.mapping.ts`).
 *
 * `EmailReadingAttachments` is unchanged — still host-agnostic (ADR-012),
 * still takes the portable `IDataService` / `INavigationService`
 * abstractions from `@spaarke/ui-components` (NOT `ComponentFramework.WebApi`
 * / raw `Xrm`).
 */
import type { IAccessPermissionOption } from '@spaarke/ui-components';

/** Props for `<EmailReadingHeader />` — the reading-pane HEADER BAND. Purely presentational: every value + callback arrives from the host (`EmailWorkspace`), fed by the shared `EmailWorkspaceRecordState` + `useEmailWorkspaceRecord` read/write pair. No internal data fetch. */
export interface IEmailReadingHeaderProps {
  /** The selected `sprk_communication` id — forwarded to the "Open full form" trigger. */
  communicationId: string;
  /** `EmailWorkspaceRecordState.subject` — `null` while loading or when the field is absent on this deployment. Renders "(no subject)" when falsy. */
  subject: string | null;
  /** Tracking trio values (`EmailWorkspaceRecordState.monitor/highPriority/accessPermission`) — rendered compactly via `EmailTrackingPanel`. */
  monitor: boolean;
  highPriority: boolean;
  accessPermission: number | null;
  /** Injected access-permission segments (value + label + optional color) — entity-agnostic (task 023, FR-14). */
  accessPermissionOptions: IAccessPermissionOption[];
  /** Tracking write callbacks — same `useEmailWorkspaceRecord.updateMonitor/updateHighPriority/updateAccessPermission` the host already owns. */
  onMonitorChange: (value: boolean) => void | Promise<void>;
  onHighPriorityChange: (value: boolean) => void | Promise<void>;
  onAccessPermissionChange: (value: number) => void | Promise<void>;
  /** "Open full form" (FR-15) — opens the OOB Email main form as an 85% `navigateTo` modal (`useEmailComposeActions().openFullForm`). */
  onOpenFullForm: (communicationId: string) => Promise<void> | void;
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
}

/** Minimal `retrieveMultipleRecords` surface this view consumes. */
export interface IEmailAttachmentsDataService {
  retrieveMultipleRecords(entityName: string, options?: string): Promise<{ entities: Record<string, unknown>[] }>;
}

/** Minimal `openRecord` surface this view consumes. */
export interface IEmailAttachmentsNavigationService {
  openRecord(entityName: string, entityId: string): Promise<void>;
}
