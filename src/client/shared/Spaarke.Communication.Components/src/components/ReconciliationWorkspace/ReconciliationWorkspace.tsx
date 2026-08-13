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
import { Tab, TabList, Text, makeStyles, tokens } from '@fluentui/react-components';
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
import type { AuthenticatedFetchFn } from '../EmailBody/EmailBodyView.types';
import type { EmailCitation } from '../../logic/citations';

/** `sprk_communication` primary id — the browse-shell key + queue index anchor. */
const PRIMARY_ID_FIELD = 'sprk_communicationid';

/** The reconciled entity — used for the UAT-Fix#3 targeted single-row re-fetch on confirm. */
const COMMUNICATION_ENTITY = 'sprk_communication';

type TabValue = 'related' | 'fields' | 'tasks';

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
  // 0-based index of the opened row, or null when the shell is closed.
  const [openIndex, setOpenIndex] = React.useState<number | null>(null);
  const [selectedTab, setSelectedTab] = React.useState<TabValue>('related');
  const [activeCitation, setActiveCitation] = React.useState<EmailCitation | undefined>(undefined);
  // Bumped on a Related-to confirm so the tab pane re-derives `regarding` from the
  // (host-refreshed) rows and re-scopes Fields/Tasks (NFR-10).
  const [, forceRescope] = React.useReducer((n: number) => n + 1, 0);

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
    },
    [dataverseClient]
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

  // Live browse queue — re-maps on every rows refresh so "N of M" tracks the grid.
  const queue = React.useMemo<ReconciliationBrowseRecord[]>(() => rows.map(toBrowseRecord), [rows, toBrowseRecord]);

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
                onProposalResolved={onProposalResolved}
              />
            )}

            {effectiveTab === 'tasks' && (
              <TaskReconcileTab
                communicationId={browseRecord.id}
                regarding={regarding}
                authenticatedFetch={authenticatedFetch}
                onCitationClick={handleCitationClick}
                onTaskResolved={onProposalResolved}
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
      s,
    ]
  );

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
