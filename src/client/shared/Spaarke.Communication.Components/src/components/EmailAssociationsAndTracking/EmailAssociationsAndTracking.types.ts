/**
 * Shared prop types for the reading-pane ASSOCIATIONS + TRACKING sub-views
 * (email-communication-solution-r5 task 035, FR-13 / FR-14 / FR-19).
 *
 * `EmailConnectionsReview` fills the task-032 `renderConnections(selectedId)`
 * slot; `EmailTrackingPanel` fills `renderTracking(selectedId)` — see
 * `projects/email-communication-solution-r5/notes/task-032-reading-pane-shell.md`
 * for the slot contract. Neither this file nor the two components edit the
 * shell — the host (task 040 assembly) wires `renderConnections={id => <EmailConnectionsReview communicationId={id} .../>}`
 * and `renderTracking={id => <EmailTrackingPanel .../>}`.
 *
 * `EmailTrackingPanel` is presentational-only (mirrors `EmailCardList`/
 * `TrackingFieldTrio` — host owns the Dataverse read/write; values in,
 * onChange callbacks out). `EmailConnectionsReview` is the one deliberate
 * exception in this package: per the task goal it OWNS the additive-write
 * orchestration (`applyRegardingSelection` / `unlinkRegarding`, the task-020
 * Layer-1 logic) because confirm/change/dismiss/link-another MUST persist
 * via that path (spec FR-13 MUST) — the host only supplies the current
 * record state + a live `IResolverWriteContext`.
 */
import type { FiledAssociation, IResolverWriteContext } from '../../logic/connections';
import type { IAccessPermissionOption, RecordTypeCatalogEntry, IPolymorphicPickerWebApi } from '@spaarke/ui-components';

/**
 * A record the host created via the Quick Start / Create*Wizard surface-launch
 * (email-communication-intelligence-r2 task 064, E1b). Returned by
 * `onLaunchCreateRecord` so the review can file it as the confirmed regarding
 * through the SAME additive `applyRegardingSelection` path a picked record uses
 * (no second write path; NFR-10). `null` return ⇒ the user cancelled the wizard.
 */
export interface CreatedRecordRef {
  /** The created Dataverse record id (bare GUID). */
  id: string;
  /** The created record's entity logical name (e.g. `sprk_matter`). */
  entityType: string;
  /** Optional display name/number for the confirmed chip; the grid refresh re-derives the denorm. */
  name?: string;
}

/**
 * The reconciled email's archived `.eml` source (task 064, E1c). The host passes
 * it to `onLaunchCreateRecord` so the launched wizard's AI-prepopulate step opens
 * pre-seeded with the email content. Carried BY REFERENCE — a `sprk_document`
 * GUID (`sprk_isemailarchive=true`), never inline binary; the host resolves it to
 * a session file via the BFF `.eml`→session-document ingest endpoint.
 */
export interface EmlSource {
  /** The `sprk_document` id of the reconciled email's `.eml` archive (direct resolution). */
  documentId?: string;
  /**
   * The `sprk_communication` id whose archived `.eml` to resolve. Preferred for the
   * reconciliation grid (it shows `sprk_communication` rows): the BFF resolves the
   * communication's archive `.eml` `sprk_document` server-side (via the `sprk_communication`
   * lookup), so no grid-column / client-side document lookup is required.
   */
  communicationId?: string;
}

export interface EmailConnectionsReviewProps {
  /** The selected `sprk_communication` id (the reading-pane `selectedId`). Also drives session-local state reset on selection change. */
  communicationId: string;
  /** `sprk_associationstatus` option value — Resolved (100000000) marks every engine-derived candidate as filed absent per-candidate `written`. */
  associationStatus?: number | null;
  /** Raw `sprk_associationprovenance` JSON — parsed internally via the task-020 `parseProvenance` (no client-side recompute; ADR-045). */
  associationProvenanceJson?: string | null;
  /** Denormalized regarding name — display fallback when there is no provenance/filed data at all. */
  regardingRecordName?: string | null;
  /** Denormalized regarding record number (`sprk_regardingrecordnumber`) — used for the confirmed "{Type}: {number}" chip. */
  regardingRecordNumber?: string | null;
  /** `sprk_regardingrecordtype` FormattedValue (e.g. "Matter") — the human type label for the confirmed chip when the primary is denorm-only. */
  regardingRecordType?: string | null;
  /** The record's actually-filed regarding lookups (incl. manual "Link another" ones the engine never suggested) — merged into engine-derived slots so the surface shows every association, not just suggestions. */
  filedAssociations?: FiledAssociation[];
  /** Write context (webApi + hostEntity + hostRecordId) for the additive write path. `hostRecordId` MUST match `communicationId`. */
  writeContext: IResolverWriteContext;
  /** webApi bridge forwarded to the embedded `PolymorphicPicker` (Change / Link another record — §11 reuse, not a new picker). */
  pickerWebApi?: IPolymorphicPickerWebApi;
  /** Catalog for "Link another record…". Defaults to a catalog derived from `TODO_REGARDING_CATALOG` when omitted. */
  linkAnotherCatalog?: readonly RecordTypeCatalogEntry[];
  /** Resolve a friendly display name for a target (entity + GUID). Falls back to the slot's own `targetName` when omitted/undefined. */
  resolveDisplayName?: (entity: string, id: string) => string | undefined;
  /** Hide every write affordance (Confirm/Change/Remove/Link another) — review-only display. */
  readOnly?: boolean;
  /** Called after ANY successful write (confirm/change/remove/link-another) so the host can refetch the record's provenance/filed-associations. */
  onAssociationsChanged?: () => void;
  /**
   * Optional "Create new" affordance for the UNMATCHED state ("Not filed yet." →
   * Find a record · Create new · Dismiss). When omitted, the "Create new" button
   * is not rendered (the host has no create-a-record-to-associate flow wired) —
   * "Find a record" (the shared picker) + "Dismiss" still appear.
   *
   * FIRE-AND-FORGET fallback (existing consumers, e.g. EmailWorkspace). When
   * `onLaunchCreateRecord` (below) is also supplied it takes precedence and this
   * is ignored for the tile click.
   */
  onCreateNewRecord?: () => void;
  /**
   * Task 064 (E1b): create-a-record-and-file-it launcher. When supplied, the
   * "New record" tile AWAITS this (the host opens Quick Start / a Create*Wizard)
   * and, on a non-null {@link CreatedRecordRef}, files the created record as the
   * confirmed regarding via the SAME additive `applyRegardingSelection` path a
   * picked record uses (no second write path; NFR-10). `null` ⇒ the user
   * cancelled → no write. Zero-arg: the host binds the `.eml` source (E1c) itself
   * (see `ReconciliationWorkspaceProps.onLaunchCreateRecord` + `resolveEmlSource`).
   * Takes precedence over `onCreateNewRecord`; both omitted ⇒ tile not rendered.
   */
  onLaunchCreateRecord?: () => Promise<CreatedRecordRef | null>;
}

export interface EmailTrackingPanelProps {
  monitor: boolean;
  highPriority: boolean;
  accessPermission: number | null;
  /** Injected access-permission segments (value + label + optional color) — entity-agnostic (task 023, FR-14). Caller supplies its `sprk_communication` OptionSet metadata. */
  accessPermissionOptions: IAccessPermissionOption[];
  onMonitorChange: (value: boolean) => void | Promise<void>;
  onHighPriorityChange: (value: boolean) => void | Promise<void>;
  onAccessPermissionChange: (value: number) => void | Promise<void>;
  monitorLabel?: string;
  highPriorityLabel?: string;
  accessPermissionLabel?: string;
  /** Disables the controls (visual + pointer-events) — no write affordance. */
  readOnly?: boolean;
  /**
   * Compact rendering for placement in the reading-pane HEADER BAND
   * (email-communication-solution-r5, layout redesign): hides the "Tracking"
   * section label and the `TrackingFieldTrio` field-caption row, so only the
   * Monitor/High-priority switches + Access segmented control render — no
   * behavior change, purely a tighter visual footprint. Default `false`
   * (the original full section rendering, unused by any current caller now
   * that tracking lives in the header band, but preserved for reuse).
   */
  compact?: boolean;
}
