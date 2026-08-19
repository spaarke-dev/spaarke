/**
 * ReconciliationWorkspace.tsx
 *
 * The Pillar E reconciliation COMPOSITION (email-communication-intelligence-r2
 * task 061). It wires the three shipped surfaces into one mountable component so
 * task 062 can drop ONE thing into two hosts (the `sprk_communicationspage` code
 * page + a SpaarkeAi workspace widget) instead of each host re-wiring the grid →
 * shell → tabs → gate → citation glue:
 *
 *   1. `ReconciliationGrid` (050) — the "Needs review" grid over `sprk_communication`.
 *   2. On row-open (`onRecordOpen`), `ReconciliationBrowseShell` (053) opens on the
 *      SAME rows the grid loaded (`onRecordsLoaded`), stepping "N of M".
 *   3. The shell's `renderTabs` slot is a `TabList` → **Related to** (052, via the
 *      reused `RelatedToCell` + `EmailConnectionsReview` write path) · **Fields**
 *      (`FieldUpdateReconcileTab`, 055) · **Tasks** (`TaskReconcileTab`, 056).
 *
 * NFR-10 (association-gates-proposals): the Fields/Tasks tabs are DISABLED until a
 * Related-to record is confirmed for the current record; the confirmed regarding
 * (resolved by the host from the row via `resolveRegarding`) is what scopes their
 * queue-feed fetch, and a later override RE-SCOPES them (the tabs re-fetch on a
 * changed `regarding`). NFR-11 (one reader / exact citations): a tab's citation
 * click lifts the `EmailCitation` into the shell's `activeCitation`, which the
 * shell already forwards to its single reader (054) — no second citation surface.
 *
 * This is COMPOSITION, not re-implementation: it renders the shipped grid, shell,
 * reader, and reconcile tabs verbatim and only owns the orchestration state (which
 * row is open, which tab is selected, the active citation, the confirm handshake).
 *
 * ADR-012 (context-agnostic — `IDataverseClient` + `authenticatedFetch` via props;
 * NO direct `Xrm`), ADR-021 (Fluent v9 semantic tokens; light + dark via the host
 * `FluentProvider`), ADR-022 (`React.FC` + standard hooks — dual-use across hosts).
 */
import * as React from 'react';
import { Badge, Tab, TabList, Text, makeStyles, tokens } from '@fluentui/react-components';
import { CheckmarkCircle16Filled } from '@fluentui/react-icons';
import { authenticatedFetch as defaultAuthenticatedFetch } from '@spaarke/auth';
import {
  DataGridViewSelector,
  type DataGridProps,
  type IDataverseClient,
  type MembershipResolver,
  type SavedView,
} from '@spaarke/ui-components';
import { ReconciliationGrid } from '../ReconciliationGrid';
import { EmailConnectionsReview } from '../EmailAssociationsAndTracking';
import { ReconciliationBrowseShell } from '../ReconciliationBrowseShell';
import type {
  ReconciliationBrowseRecord,
  ReconciliationBrowseShellProps,
} from '../ReconciliationBrowseShell/ReconciliationBrowseShell.types';
import { FieldUpdateReconcileTab, TaskReconcileTab } from '../ReconcileTabs';
import type { ReconcileRegarding, ProposalOutcome } from '../ReconcileTabs/FieldUpdateReconcileTab';
import type {
  EmailConnectionsReviewProps,
  CreatedRecordRef,
  EmlSource,
} from '../EmailAssociationsAndTracking/EmailAssociationsAndTracking.types';
import type { AuthenticatedFetchFn, ReconciliationAttachmentContent } from '../EmailBody/EmailBodyView.types';
import { projectAttachmentRecord, filterFileAttachments, type IAttachmentRecord } from '../../logic/attachments';
import type { EmailCitation } from '../../logic/citations';

/** `sprk_communication` primary id — the browse-shell key + queue index anchor. */
const PRIMARY_ID_FIELD = 'sprk_communicationid';

/** The reconciled entity — used for the UAT-Fix#3 targeted single-row re-fetch on confirm. */
const COMMUNICATION_ENTITY = 'sprk_communication';
/** Intersection entity carrying a communication's file attachments (owner UAT round-3 item 2). */
const ATTACHMENT_ENTITY = 'sprk_communicationattachment';
/** Document entity — the archived `.eml` is a related `sprk_document` flagged `sprk_isemailarchive`. */
const DOCUMENT_ENTITY = 'sprk_document';

/** Per-record reader detail resolved asynchronously on open (grid query omits both). */
interface BrowseDetail {
  attachments: ReconciliationAttachmentContent[];
  emlDocumentId: string | null;
}

/**
 * Resolve a communication's file attachments → reader folds (owner UAT round-3 item 2).
 * Reuses the shared `projectAttachmentRecord`/`filterFileAttachments` (§11 — the same
 * projection the CommunicationAttachments surface uses) over a `_sprk_communication_value`
 * filtered read. Extracted attachment TEXT is a server-side pipeline artifact not exposed
 * as a readable column, so `text`/`extractable` stay unset — the fold renders the file
 * name + an "Open original" link (documentId present), which is what the reviewer needs.
 * Best-effort: any failure → no attachments (the reader simply shows none).
 */
async function resolveAttachments(
  client: IDataverseClient,
  communicationId: string
): Promise<ReconciliationAttachmentContent[]> {
  try {
    const res = await client.retrieveMultipleRecords(
      ATTACHMENT_ENTITY,
      `?$select=sprk_name,sprk_attachmenttype,_sprk_document_value&$filter=_sprk_communication_value eq ${communicationId}&$orderby=createdon asc`
    );
    return filterFileAttachments((res.entities ?? []).map(r => projectAttachmentRecord(r as IAttachmentRecord)))
      .filter(f => f.attachmentId)
      .map(f => ({
        attachmentId: f.attachmentId,
        name: f.name || f.documentName || 'Attachment',
        documentId: f.documentId,
      }));
  } catch {
    return [];
  }
}

/**
 * Resolve the `.eml` archive document id for a communication — the related `sprk_document`
 * flagged `sprk_isemailarchive = true` (mirrors `resolveEmlArchiveDocumentId` /
 * `CommunicationService.FindExistingArchiveDocumentAsync`). Present ⇒ the reader renders
 * the server-sanitized `.eml` (best "as sent" fidelity); absent/failure ⇒ it degrades to
 * `sprk_body` (a normal state, not an error).
 */
async function resolveEmlArchive(client: IDataverseClient, communicationId: string): Promise<string | null> {
  try {
    // The `.eml` archive is linked to the communication via `sprk_document.sprk_relatedcommunication`
    // — NOT `sprk_communication` (that lookup does not exist on `sprk_document`). This is the SAME
    // filter the BFF's `GetEmailArchiveByCommunicationAsync` (DataverseWebApiService/ServiceClientImpl)
    // uses; the email-form `resolveEmlArchiveDocumentId` filters the wrong field and never resolves.
    const res = await client.retrieveMultipleRecords(
      DOCUMENT_ENTITY,
      `?$select=sprk_documentid&$filter=_sprk_relatedcommunication_value eq ${communicationId} and sprk_isemailarchive eq true&$top=1`
    );
    const docId = res.entities?.[0]?.['sprk_documentid'];
    return typeof docId === 'string' && docId.length > 0 ? docId : null;
  } catch {
    return null;
  }
}

/** One item of the BFF `GET /api/communications/{id}/attachments/text` response (B2.1, camelCase). */
interface AttachmentTextItem {
  attachmentId: string;
  documentId?: string | null;
  fileName?: string;
  text?: string | null;
  extractable?: boolean;
}

/** Normalize a Dataverse id for a stable case/brace-insensitive join key. */
function idKey(id: string): string {
  return id.replace(/[{}]/g, '').toLowerCase();
}

/**
 * Fold each attachment's RE-EXTRACTED text into the reader (owner UAT 2026-08-14, B2.1). The extracted
 * text is a transient pipeline artifact never persisted, so the BFF re-extracts it on demand from SPE
 * (its shared ITextExtractor owns a 24h Redis cache, so repeats are cheap). We overlay `text`/`extractable`
 * onto the matching fold by `attachmentId`. Best-effort (NFR-04): any failure (no auth fetch, non-200,
 * parse error) leaves the folds name-only — exactly today's behavior — never throwing into the reader.
 */
async function mergeAttachmentText(
  attachments: ReconciliationAttachmentContent[],
  communicationId: string,
  authFetch: AuthenticatedFetchFn
): Promise<ReconciliationAttachmentContent[]> {
  try {
    const res = await authFetch(`/communications/${encodeURIComponent(communicationId)}/attachments/text`);
    if (!res.ok) return attachments;
    const body = (await res.json()) as { attachments?: AttachmentTextItem[] };
    const byId = new Map<string, AttachmentTextItem>();
    for (const it of body.attachments ?? []) {
      if (it.attachmentId) byId.set(idKey(it.attachmentId), it);
    }
    if (byId.size === 0) return attachments;
    return attachments.map(a => {
      const hit = byId.get(idKey(a.attachmentId));
      return hit ? { ...a, text: hit.text ?? null, extractable: hit.extractable } : a;
    });
  } catch {
    return attachments;
  }
}

/** Resolve both reader-detail pieces (attachments + `.eml` archive) for one record. */
async function resolveBrowseDetail(
  client: IDataverseClient,
  communicationId: string,
  authFetch: AuthenticatedFetchFn
): Promise<BrowseDetail> {
  const cleanId = communicationId.replace(/[{}]/g, '');
  const [attachments, emlDocumentId] = await Promise.all([
    resolveAttachments(client, cleanId),
    resolveEmlArchive(client, cleanId),
  ]);
  // B2.1: overlay re-extracted attachment text onto the reader folds (best-effort; name-only on miss).
  const withText = attachments.length > 0 ? await mergeAttachmentText(attachments, cleanId, authFetch) : attachments;
  return { attachments: withText, emlDocumentId };
}

type TabValue = 'related' | 'fields' | 'tasks';

/** Per-tab progress count for the current record (B2.3 header badges). */
interface TabCount {
  total: number;
  resolved: number;
}

/** Queue-feed item `kind` discriminators used to count field vs task proposals (mirrors the tabs' filters). */
const PENDING_PROPOSAL_KIND = 'pending-proposal';
const CREATE_TASK_KIND = 'create-task';

/**
 * B2.3 progress badges rendered in the browse-shell header (`SprkModal.headerActions`): a Related ✓/•
 * indicator plus "Fields n/N" and "Tasks n/N" resolved-of-total counts for the CURRENT record — the
 * prototype's at-a-glance progress. Totals are seeded eagerly on open + when a tab loads; resolved is
 * tallied by the workspace as the reviewer accepts/rejects. Fluent v9 tokens only (ADR-021).
 */
function ProgressBadges({
  related,
  fields,
  tasks,
}: {
  related: boolean;
  fields?: TabCount;
  tasks?: TabCount;
}): React.ReactElement {
  const frac = (c?: TabCount): string => `${c?.resolved ?? 0}/${c?.total ?? 0}`;
  return (
    <div
      style={{ display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalXS }}
      data-testid="reconciliation-progress-badges"
    >
      <Badge
        appearance="tint"
        color={related ? 'success' : 'informative'}
        icon={related ? <CheckmarkCircle16Filled /> : undefined}
        data-testid="progress-badge-related"
      >
        {related ? 'Related ✓' : 'Related •'}
      </Badge>
      <Badge appearance="tint" color="brand" data-testid="progress-badge-fields">
        Fields {frac(fields)}
      </Badge>
      <Badge appearance="tint" color="brand" data-testid="progress-badge-tasks">
        Tasks {frac(tasks)}
      </Badge>
    </div>
  );
}

export interface ReconciliationWorkspaceProps {
  /** GUID of the "Needs-review" `sprk_gridconfiguration` record (forwarded to the grid). */
  configId?: string;
  /**
   * UAT Fix #5 — optional view-switcher list. When supplied with ≥2 entries, a
   * `DataGridViewSelector` renders above the grid; selecting a view swaps the grid's
   * `configId` to that view's id (each id is a `sprk_gridconfiguration` GUID). The
   * default view (or the first) is active initially. Omitted ⇒ no switcher; the grid
   * uses `configId`. Reconciliation hosts pass `RECONCILIATION_VIEWS`.
   */
  views?: ReadonlyArray<SavedView>;
  /**
   * Dataverse access implementation (ADR-012). Non-MDA hosts (Code Pages,
   * SpaarkeAi widgets) MUST pass an explicit client (`BffDataverseClient`); MDA
   * hosts may omit it to use `<DataGrid />`'s own `XrmDataverseClient`.
   */
  dataverseClient?: IDataverseClient;
  /**
   * Injected authenticated fetch (ADR-028) — threaded into the browse reader's
   * `.eml` render and the Fields/Tasks tabs' queue-feed + apply/dismiss calls.
   * Defaults to `@spaarke/auth` inside each consumer when omitted.
   */
  authenticatedFetch?: AuthenticatedFetchFn;
  /** Resolver for the grid's `behavior.membershipFilter` (FR-E7 / 057). */
  membershipResolver?: MembershipResolver;
  /** `--sprk-ui-scale` forwarded to the browse shell. */
  uiScale?: number;
  /** Additional class merged after the grid frame class. */
  className?: string;

  /**
   * Map a grid row → the browse record the shell reader renders. Defaults to a
   * `sprk_*` field mapper; a host overrides it to supply attachment text / the
   * archived `.eml` document id the grid query does not select.
   */
  toBrowseRecord?: (record: Record<string, unknown>) => ReconciliationBrowseRecord;

  /**
   * Resolve a grid row → its CONFIRMED `ReconcileRegarding` (NFR-10). `null`
   * ⇒ the Fields/Tasks tabs are GATED (disabled) for that record; a non-null
   * value both enables them and scopes their queue-feed fetch. After a Related-to
   * confirm the host is expected to refresh the grid so this reflects the new
   * association (the tabs then re-scope). Omitted ⇒ every record is gated.
   */
  resolveRegarding?: (record: Record<string, unknown>) => ReconcileRegarding | null;

  /**
   * Resolve a grid row → the `EmailConnectionsReview` props for its Related-to
   * picker (052). Supplied ⇒ the grid's association column becomes the interactive
   * Related-to cell AND the browse shell's Related-to tab hosts the same reused
   * picker (single ADR-024 write path). Omitted ⇒ the grid keeps its static status
   * badge and the Related-to tab shows a read-only note.
   */
  resolveReview?: (record: Record<string, unknown>) => EmailConnectionsReviewProps;

  /**
   * Fired after a Related-to association is confirmed (in the grid cell OR the
   * browse tab). The host refreshes the grid so `resolveRegarding` reflects the
   * new association and the Fields/Tasks tabs re-scope (NFR-10).
   */
  onAssociationsChanged?: (record: Record<string, unknown>) => void;

  /** Fired after a Fields/Tasks proposal reaches an outcome (observability). */
  onProposalResolved?: (reviewLogId: string | null, outcome: ProposalOutcome) => void;

  /** Forwarded to the browse shell — opens the raw `.eml`/attachment file. */
  onOpenOriginalActivate?: ReconciliationBrowseShellProps['onOpenOriginalActivate'];
  /** Forwarded to the browse shell reader toolbar (Reply / Save to SharePoint / …). */
  readerActions?: ReconciliationBrowseShellProps['readerActions'];

  /**
   * Task 064 (E1b): "New record" launcher. When supplied, the Related-to review's
   * "New record" tile (grid cell AND browse tab) opens the host's Quick Start /
   * Create*Wizard and, on a created {@link CreatedRecordRef}, files it as the
   * confirmed regarding via the shared additive confirm path (no second write
   * path; NFR-10) — which re-scopes Fields/Tasks. `null` ⇒ cancelled. The
   * workspace binds the current row's `.eml` source (see `resolveEmlSource`) so
   * the launched wizard can pre-seed its AI-prepopulate step (E1c). Omitted ⇒ the
   * review keeps its existing fire-and-forget "New record" behavior (if the host
   * wires that via `resolveReview`) or hides the tile.
   */
  onLaunchCreateRecord?: (ctx: { emlSource?: EmlSource }) => Promise<CreatedRecordRef | null>;

  /**
   * Task 064 (E1c): resolve a grid row → the reconciled email's archived `.eml`
   * source (`sprk_document` id), passed to `onLaunchCreateRecord` so the launched
   * wizard opens pre-seeded with the email content. Omitted / `undefined` ⇒ the
   * wizard opens without a pre-loaded file (E1b still works).
   */
  resolveEmlSource?: (record: Record<string, unknown>) => EmlSource | undefined;
}

/** Read a string field off an untyped grid row. */
function str(record: Record<string, unknown>, field: string): string | null {
  const v = record[field];
  return typeof v === 'string' ? v : v == null ? null : String(v);
}

/** Default grid-row → browse-record mapper (the `sprk_*` fields the grid selects). */
function defaultToBrowseRecord(record: Record<string, unknown>): ReconciliationBrowseRecord {
  return {
    id: str(record, PRIMARY_ID_FIELD) ?? '',
    subject: str(record, 'sprk_subject'),
    from: str(record, 'sprk_from'),
    to: str(record, 'sprk_to'),
    cc: str(record, 'sprk_cc'),
    bcc: str(record, 'sprk_bcc'),
    body: str(record, 'sprk_body'),
    // Reader Date row + TRIAGE box (prototype parity, owner UAT 2026-08-14). Priority/category
    // read the OData FormattedValue (choice/lookup label) with a raw fallback.
    receivedDate: str(record, 'sprk_receiveddate'),
    triageSummary: str(record, 'sprk_triagesummary'),
    triagePriority:
      str(record, 'sprk_triagepriority@OData.Community.Display.V1.FormattedValue') ??
      str(record, 'sprk_triagepriority'),
    triageCategory:
      str(record, 'sprk_triagecategory@OData.Community.Display.V1.FormattedValue') ??
      str(record, 'sprk_triagecategory'),
  };
}

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', minHeight: 0, height: '100%', width: '100%' },
  viewBar: {
    display: 'flex',
    alignItems: 'center',
    paddingInline: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalS,
    flexShrink: 0,
  },
  grid: { flex: '1 1 auto', minHeight: 0 },
  tabRoot: { display: 'flex', flexDirection: 'column', minHeight: 0, minWidth: 0, height: '100%' },
  tabList: {
    paddingInline: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalS,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
  },
  tabBody: { flex: '1 1 auto', minHeight: 0, minWidth: 0 },
  relatedNote: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    padding: tokens.spacingHorizontalXL,
    textAlign: 'center',
    color: tokens.colorNeutralForeground3,
  },
});

export const ReconciliationWorkspace: React.FC<ReconciliationWorkspaceProps> = ({
  configId,
  dataverseClient,
  authenticatedFetch,
  membershipResolver,
  uiScale,
  className,
  views,
  toBrowseRecord = defaultToBrowseRecord,
  resolveRegarding,
  resolveReview,
  onAssociationsChanged,
  onProposalResolved,
  onOpenOriginalActivate,
  readerActions,
  onLaunchCreateRecord,
  resolveEmlSource,
}) => {
  const s = useStyles();

  // UAT Fix #5 — view-switcher. When `views` is supplied, the active view's id is the
  // grid's effective configId; selecting a view swaps the grid query. Default = the
  // flagged default view, else the first, else the `configId` prop.
  const hasViews = !!views && views.length > 0;
  const [activeViewId, setActiveViewId] = React.useState<string>(
    () => views?.find(v => v.isDefault)?.id ?? views?.[0]?.id ?? configId ?? ''
  );
  const effectiveConfigId = hasViews ? activeViewId : configId;

  // The rows the grid loaded (page 1 ∪ page 2 ∪ …) — the browse queue source.
  const [rows, setRows] = React.useState<ReadonlyArray<Record<string, unknown>>>([]);
  // Per-record reader detail (attachments + `.eml` archive id) resolved on open — the grid
  // query carries neither, so this is overlaid onto the browse record (owner UAT round-3 item 2).
  const [detailById, setDetailById] = React.useState<Record<string, BrowseDetail>>({});
  // 0-based index of the opened row, or null when the shell is closed.
  const [openIndex, setOpenIndex] = React.useState<number | null>(null);
  const [selectedTab, setSelectedTab] = React.useState<TabValue>('related');
  const [activeCitation, setActiveCitation] = React.useState<EmailCitation | undefined>(undefined);
  // Bumped on a Related-to confirm so the tab pane re-derives `regarding` from the
  // (host-refreshed) rows and re-scopes Fields/Tasks (NFR-10).
  const [, forceRescope] = React.useReducer((n: number) => n + 1, 0);

  // B2.3 progress counts, keyed by communicationId. `total` is seeded eagerly on open (+ corrected when a tab
  // loads, taking the max so it never shrinks on a tab remount); `resolved` is tallied here as the reviewer
  // accepts/rejects (cumulative, never reset by a tab remount).
  const [fieldCounts, setFieldCounts] = React.useState<Record<string, TabCount>>({});
  const [taskCounts, setTaskCounts] = React.useState<Record<string, TabCount>>({});

  const setTotal = React.useCallback(
    (setter: React.Dispatch<React.SetStateAction<Record<string, TabCount>>>, id: string, total: number) => {
      setter(prev => {
        const cur = prev[id] ?? { total: 0, resolved: 0 };
        const next = Math.max(cur.total, total);
        return next === cur.total ? prev : { ...prev, [id]: { ...cur, total: next } };
      });
    },
    []
  );
  const bumpResolved = React.useCallback(
    (setter: React.Dispatch<React.SetStateAction<Record<string, TabCount>>>, id: string) => {
      setter(prev => {
        const cur = prev[id] ?? { total: 0, resolved: 0 };
        const resolved = cur.resolved + 1;
        return { ...prev, [id]: { total: Math.max(cur.total, resolved), resolved } };
      });
    },
    []
  );

  const handleRecordsLoaded = React.useCallback<NonNullable<DataGridProps['onRecordsLoaded']>>(next => {
    setRows(next);
  }, []);

  // Enrich ONE row in-place with the full `sprk_communication` record. The grid
  // query is deliberately lightweight (no `sprk_body`, no typed `sprk_regarding*`
  // lookups) so the reader body + the NFR-10 gate would otherwise be empty until a
  // confirm. Fetching the full record on open populates BOTH: `sprk_body` (the
  // email thread the reader renders) AND the `_sprk_regarding*_value` lookups
  // `resolveRegarding` reads to un-gate Fields/Tasks for an already-Resolved row.
  // Best-effort (NFR-04): a failed fetch just leaves the lightweight row as-is.
  const enrichRow = React.useCallback(
    (id: string) => {
      if (!id || !dataverseClient) return;
      void dataverseClient
        .retrieveRecord(COMMUNICATION_ENTITY, id)
        .then(fresh => {
          const freshRow = fresh as Record<string, unknown>;
          setRows(prev => prev.map(r => (str(r, PRIMARY_ID_FIELD) === id ? { ...r, ...freshRow } : r)));
          forceRescope();
        })
        .catch(() => forceRescope());
      // Reader detail (attachments + `.eml` archive + re-extracted attachment text) — resolved once per
      // record and cached (owner UAT round-3 item 2; B2.1 text overlay). Best-effort: a miss just leaves
      // the reader body-only. Uses the host fetch when supplied, else the @spaarke/auth default.
      if (!detailById[id]) {
        void resolveBrowseDetail(dataverseClient, id, authenticatedFetch ?? defaultAuthenticatedFetch)
          .then(detail => setDetailById(prev => ({ ...prev, [id]: detail })))
          .catch(() => {});
      }
    },
    [dataverseClient, detailById, authenticatedFetch]
  );

  const handleRecordOpen = React.useCallback<NonNullable<DataGridProps['onRecordOpen']>>(
    (recordId, record) => {
      const idx = rows.findIndex(r => str(r, PRIMARY_ID_FIELD) === recordId);
      setOpenIndex(idx >= 0 ? idx : 0);
      setSelectedTab('related');
      setActiveCitation(undefined);
      // A row could be opened before `onRecordsLoaded` seeded `rows` (defensive):
      // seed a single-record queue from the opened record so the shell still opens.
      if (idx < 0 && record) setRows(prev => (prev.length ? prev : [record]));
      // Pull the full record so the reader shows the thread + the tabs un-gate
      // immediately (not only after a confirm).
      enrichRow(recordId);
    },
    [rows, enrichRow]
  );

  const handleClose = React.useCallback(() => setOpenIndex(null), []);

  // UAT Fix #5 — switch the active view: swap the grid's configId AND close the browse
  // shell (the row set changes, so the open index no longer maps to the same record).
  const handleViewChange = React.useCallback((viewId: string) => {
    setActiveViewId(viewId);
    setOpenIndex(null);
  }, []);

  // Live browse queue — re-maps on every rows refresh so "N of M" tracks the grid. The
  // per-record reader detail (attachments + `.eml` archive id), resolved async on open, is
  // overlaid onto the mapped record so the reader shows attachments + the archived email.
  const queue = React.useMemo<ReconciliationBrowseRecord[]>(
    () =>
      rows.map(r => {
        const base = toBrowseRecord(r);
        const detail = detailById[base.id];
        return detail ? { ...base, attachments: detail.attachments, emlDocumentId: detail.emlDocumentId } : base;
      }),
    [rows, toBrowseRecord, detailById]
  );

  const handleCitationClick = React.useCallback((citation: EmailCitation) => {
    setActiveCitation(citation);
  }, []);

  // A Related-to confirm (grid cell OR browse tab) → notify the host (refresh) and
  // re-derive the gate/scope; jump the reviewer to Fields next.
  //
  // UAT Fix #3 (in-session enable, no remount): instead of the host remounting the whole
  // workspace via a `key` bump (which closed the browse shell and lost the reviewer's
  // place), refresh ONLY the confirmed row in-place — re-fetch it and merge into `rows`,
  // so `resolveRegarding` sees the now-Resolved status + regarding denorm and un-gates
  // Fields/Tasks WHILE the shell stays open. Best-effort (NFR-04): `forceRescope` re-derives
  // whether or not the re-fetch resolves; a failed re-fetch just leaves the row gated.
  const handleConfirmed = React.useCallback(
    (record: Record<string, unknown>) => {
      onAssociationsChanged?.(record);
      setSelectedTab('fields');
      const id = str(record, PRIMARY_ID_FIELD);
      if (id) enrichRow(id);
      else forceRescope();
    },
    [onAssociationsChanged, enrichRow]
  );

  // Task 064 (E1b): wrap the host's per-row review props to inject a zero-arg
  // "New record" launcher bound to THIS row's `.eml` source (E1c). The shared
  // review component stays context-agnostic (ADR-012); the workspace owns the
  // emlSource binding. No-op passthrough when the host wires no launcher.
  const wrapReview = React.useCallback(
    (liveRow: Record<string, unknown>): EmailConnectionsReviewProps => {
      const base = resolveReview!(liveRow);
      if (!onLaunchCreateRecord) return base;
      return {
        ...base,
        onLaunchCreateRecord: () => onLaunchCreateRecord({ emlSource: resolveEmlSource?.(liveRow) }),
      };
    },
    [resolveReview, onLaunchCreateRecord, resolveEmlSource]
  );

  // The grid's interactive Related-to cell (052) — only when the host resolves it.
  const relatedTo = React.useMemo(
    () =>
      resolveReview
        ? { resolveReview: wrapReview, onConfirmed: (record: Record<string, unknown>) => handleConfirmed(record) }
        : undefined,
    [resolveReview, wrapReview, handleConfirmed]
  );

  // The shell's right-pane TabList (Related to · Fields · Tasks) for one record.
  const renderTabs = React.useCallback<NonNullable<ReconciliationBrowseShellProps['renderTabs']>>(
    (browseRecord: ReconciliationBrowseRecord) => {
      const liveRow = rows.find(r => str(r, PRIMARY_ID_FIELD) === browseRecord.id);
      const regarding = liveRow && resolveRegarding ? resolveRegarding(liveRow) : null;
      const gated = !regarding;
      // Task UAT-Fix#2 (FR-E3): the browse tab renders the FULL EmailConnectionsReview
      // INLINE (candidate cards + manual "Lookup Records" pane + Create-new intent) — the
      // SAME surface the email form renders inline (EmailWorkspace.tsx). The compact
      // `RelatedToCell` (which hides the review behind a "Requires review" + picker modal)
      // stays only as the GRID cell renderer, never the browse-tab body.
      const review = liveRow && resolveReview ? wrapReview(liveRow) : null;
      // While gated, the reviewer can only be on the Related-to tab.
      const effectiveTab: TabValue = gated && selectedTab !== 'related' ? 'related' : selectedTab;

      return (
        <div className={s.tabRoot}>
          <TabList
            className={s.tabList}
            selectedValue={effectiveTab}
            onTabSelect={(_e, d) => setSelectedTab(d.value as TabValue)}
          >
            <Tab value="related" data-testid="reconcile-tab-related">
              Related to {regarding ? '✓' : '•'}
            </Tab>
            <Tab value="fields" data-testid="reconcile-tab-fields" disabled={gated}>
              Fields
            </Tab>
            <Tab value="tasks" data-testid="reconcile-tab-tasks" disabled={gated}>
              Tasks
            </Tab>
          </TabList>

          <div className={s.tabBody} data-testid="reconcile-tab-body">
            {effectiveTab === 'related' &&
              (review && liveRow ? (
                <EmailConnectionsReview
                  {...review}
                  variant="reconcile"
                  onAssociationsChanged={() => {
                    // Preserve RelatedToCell's confirm handshake (minus the modal-close):
                    // fire the host's per-row refresh, then the NFR-10 re-gate/jump-to-Fields.
                    review.onAssociationsChanged?.();
                    handleConfirmed(liveRow);
                  }}
                />
              ) : (
                <div className={s.relatedNote} role="note" data-testid="reconcile-related-note">
                  <Text>Related-to review is not configured for this host.</Text>
                </div>
              ))}

            {effectiveTab === 'fields' && (
              <FieldUpdateReconcileTab
                communicationId={browseRecord.id}
                regarding={regarding}
                authenticatedFetch={authenticatedFetch}
                onCitationClick={handleCitationClick}
                onLoadedCount={total => setTotal(setFieldCounts, browseRecord.id, total)}
                onProposalResolved={(id, outcome) => {
                  if (outcome === 'applied' || outcome === 'rejected') bumpResolved(setFieldCounts, browseRecord.id);
                  onProposalResolved?.(id, outcome);
                }}
              />
            )}

            {effectiveTab === 'tasks' && (
              <TaskReconcileTab
                communicationId={browseRecord.id}
                regarding={regarding}
                authenticatedFetch={authenticatedFetch}
                onCitationClick={handleCitationClick}
                onLoadedCount={total => setTotal(setTaskCounts, browseRecord.id, total)}
                onTaskResolved={(id, outcome) => {
                  if (outcome === 'applied' || outcome === 'rejected') bumpResolved(setTaskCounts, browseRecord.id);
                  onProposalResolved?.(id, outcome);
                }}
              />
            )}
          </div>
        </div>
      );
    },
    [
      rows,
      resolveRegarding,
      resolveReview,
      wrapReview,
      selectedTab,
      authenticatedFetch,
      handleCitationClick,
      handleConfirmed,
      onProposalResolved,
      setTotal,
      bumpResolved,
      s,
    ]
  );

  // The currently-open record + whether its Related-to is confirmed — drives the header progress badges.
  const openRecordId = openIndex !== null ? queue[openIndex]?.id : undefined;
  const openRegarding = React.useMemo(() => {
    if (!openRecordId || !resolveRegarding) return null;
    const liveRow = rows.find(r => str(r, PRIMARY_ID_FIELD) === openRecordId);
    return liveRow ? resolveRegarding(liveRow) : null;
  }, [openRecordId, rows, resolveRegarding]);

  // B2.3: eagerly seed Fields/Tasks totals for the open record (once its Related-to is confirmed) so the header
  // badges show a total BEFORE the reviewer visits each tab (prototype parity). Best-effort; the tabs' own
  // onLoadedCount corrects/refreshes it. Uses the ONE queue-feed read the tabs already use.
  React.useEffect(() => {
    if (!openRecordId || !openRegarding?.entityType || !openRegarding?.recordId) return;
    const scope = `${openRegarding.entityType}:${openRegarding.recordId}`;
    const fetchFn = authenticatedFetch ?? defaultAuthenticatedFetch;
    let cancelled = false;
    void (async () => {
      try {
        const res = await fetchFn(`/communications/queue-feed?regarding=${encodeURIComponent(scope)}`);
        const data = (await res.json()) as { items?: Array<{ kind?: string; communicationId?: string }> };
        const items = (data.items ?? []).filter(i => i.communicationId === openRecordId);
        if (cancelled) return;
        setTotal(setFieldCounts, openRecordId, items.filter(i => i.kind === PENDING_PROPOSAL_KIND).length);
        setTotal(setTaskCounts, openRecordId, items.filter(i => i.kind === CREATE_TASK_KIND).length);
      } catch {
        /* best-effort — the tabs' onLoadedCount still seeds totals on visit */
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [openRecordId, openRegarding?.entityType, openRegarding?.recordId, authenticatedFetch, setTotal]);

  const headerBadges = openRecordId ? (
    <ProgressBadges related={!!openRegarding} fields={fieldCounts[openRecordId]} tasks={taskCounts[openRecordId]} />
  ) : undefined;

  return (
    <div className={className ? `${s.root} ${className}` : s.root} data-testid="reconciliation-workspace">
      {hasViews && views!.length > 1 && (
        <div className={s.viewBar} data-testid="reconciliation-view-switcher">
          <DataGridViewSelector views={views!} activeViewId={activeViewId} onViewChange={handleViewChange} />
        </div>
      )}
      <div className={s.grid}>
        <ReconciliationGrid
          configId={effectiveConfigId}
          dataverseClient={dataverseClient}
          membershipResolver={membershipResolver}
          relatedTo={relatedTo}
          onRecordsLoaded={handleRecordsLoaded}
          onRecordOpen={handleRecordOpen}
        />
      </div>

      <ReconciliationBrowseShell
        open={openIndex !== null}
        onClose={handleClose}
        queue={queue}
        initialIndex={openIndex ?? 0}
        onIndexChange={(_i, record) => enrichRow(record.id)}
        renderTabs={renderTabs}
        headerBadges={headerBadges}
        authenticatedFetch={authenticatedFetch}
        activeCitation={activeCitation}
        onOpenOriginalActivate={onOpenOriginalActivate}
        readerActions={readerActions}
        uiScale={uiScale}
      />
    </div>
  );
};
ReconciliationWorkspace.displayName = 'ReconciliationWorkspace';

export default ReconciliationWorkspace;
