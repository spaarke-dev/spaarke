/**
 * RecordTemplateModal — the config-driven record view of the external SPA and the Teams tab
 * (spaarke-SPA-external-access-platform-r4 tasks 003 and 005, FR-01 / FR-02). Every in-app record open uses it, for all
 * six types: a grid row (`GridWidgetBody`), a `?record=` deep link, a notification and an intake success link (the last
 * three through `RecordOpener`). It replaced R3's stacked record-detail modal (retired in task 005).
 *
 * CONFIG ONLY (FR-01). Everything type-specific comes from the `RecordTemplateConfig` (`recordTemplates.ts`): title,
 * header facts, tabs, the left / right sections, the toolbar actions and the section actions. Nothing here branches on the
 * entity name. Each section id renders through `sections/` (`renderRecordSection`); an action whose flow has its own
 * dialogs or notices (Download, Delete request, Submit invoice) is an action host in `actions/`, mounted in the record's
 * content for every action id the config lists (toolbar or section), which offers or withdraws its action. The create
 * actions (Upload, New message, Add event) are offered by the section that owns the list and its dialog (`offerAction`);
 * that section renders the dialog (a `FormModal`) itself, so the dialog is a React descendant of this modal (FR-03, task
 * 004).
 *
 * WHERE AN ACTION SHOWS (task 009, owner 2026-11-12). An action the config lists in `toolbar` is a `RecordCommandBar`
 * button (the record-level actions: Refresh, Copy link, Download, Delete request, Submit invoice); one it lists in
 * `sectionActions[section]` is an icon slot in that section card's `HeaderToolbar` (`headerActions` in the section's
 * context). Both are built by one function from the same offers, so the hide-don't-disable rules are the same in both
 * places, and a child dialog's focus returns to whichever button opened it (`useChildDialog`).
 *
 * ENVELOPE (ADR-050). A thin config of the canonical `SprkModal`, size `lg`: its own header carries "‹ N of M ›", the
 * title, the window controls and ×; its footer carries Close only. No nested `RecordNavigationModalShell`.
 *
 * LAYOUT (FR-02). The SprkModal body is unpadded and the template root fills it (height 100%), so the body itself never
 * scrolls. Inside, the toolbar (`RecordCommandBar`), the header and the optional TabList are fixed; only the content
 * region scrolls, with the canonical `thinScrollbarStyle`. The overview is two columns, 66% / 34%, that stack (left
 * first) below 1000px; a type with no right-hand sections uses one full-width column.
 *
 * STATES (R3's, same text and test ids as R3's modal): loading, denied (nothing of the record on screen: no toolbar,
 * header, field or section), not found, error (Try again only when retrying can help), deleted.
 *
 * HOLD. While a section or action host reports an operation in flight (`onBusyChange`: a message send, a recall post),
 * browsing and Close are held (`onBeforeNavigate` returns false; the footer Close is disabled; ×, Escape and the backdrop
 * are ignored), and so is Refresh (task 005, handoff R3 from task 003): it is hidden while the hold is on, and a refresh
 * already in flight when an operation starts keeps the shown record (and so the busy section) on screen until the hold
 * releases, if its answer is not a record (denied, not found, error). Only then does that state replace the sections.
 *
 * DATA. The record is read with the moved R3 read (`useRecord`, `recordReads.ts`) through the entity's
 * `RECORD_DETAIL_ENTITIES` description: the same id-filtered module fetch, the same columns. The caller's `/me` context
 * (who they are, their project levels) is read once per opening, as R3 did, and handed to every section. Refresh re-runs
 * the record read, keeps the record on screen until the new answer arrives, and changes `refreshSignal`, on which every
 * section re-reads. Copy link writes `{origin}{path}?record={token}:{id}`. Submit invoice (Matter, Project, Work assignment;
 * task 007, FR-10) is an action host (`actions/SubmitInvoiceAction.tsx`): offered only when the caller's firms are linked
 * to the record, and (task 010) it opens the submission wizard in a child modal over the record: nothing navigates.
 *
 * COLLAPSIBLE SECTIONS AND CHILD RECORDS (task 013, owner 2026-11-14). The modal tells every section card the record type and
 * the caller's hashed `/me` id (`RecordSectionStateContext`), so a collapsed section is remembered per user and type
 * (`recordSectionState.ts`). It also hosts one child record (`useChildRecordHost`, `ChildRecordModal.tsx`): an invoice or
 * document row in a section opens that record in this same component (`nested`) over this one, through
 * `ChildRecordContext`. A `nested` modal provides no opener, so a child never opens a grandchild.
 *
 * ADR-021: Fluent UI v9 semantic tokens via `makeStyles` only; the modal inherits the host FluentProvider.
 */
import * as React from 'react';
import {
  Button,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  ProgressBar,
  Spinner,
  Tab,
  TabList,
  Text,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import { DismissRegular, LockClosedRegular } from '@fluentui/react-icons';
import type { IHeaderToolbarSlot } from '@spaarke/ui-components/components/HeaderToolbar';
import { SprkModal } from '@spaarke/ui-components/components/SprkModal';
import {
  RECORD_ACTION_ICONS,
  RecordCommandBar,
  type RecordCommandBarAction,
} from '@spaarke/ui-components/components/RecordCommandBar';
import { thinScrollbarStyle } from '@spaarke/ui-components/theme/scrollbar';
import { callerIdentityOf } from '../../api/delete-own';
import { useCallerContext } from '../../hooks/useCallerContext';
import { formatDetailValue, getRecordDetailEntity, type DetailRecord } from '../detail/RecordDetailFields';
import { recordLinkFor } from '../detail/record-targets';
import { RECORD_ACTION_HOSTS } from './actions';
import { ChildRecordContext } from './childRecord';
import { useChildRecordHost } from './ChildRecordModal';
import {
  createRightsOf,
  isCreateActionOffered,
  isRecordCreateAction,
  readOnlyNoteOf,
  recordAccessFactsOf,
  yourAccessOf,
} from './recordAccess';
import { useRecord, type RecordState } from './recordReads';
import { RecordSectionStateContext, sectionStateUserKeyOf } from './recordSectionState';
import { RECORD_ACTION_TEXT, recordActionIdsOf, recordTemplateTitle, recordText } from './recordTemplates';
import type {
  RecordActionHostContext,
  RecordHostAction,
  RecordSectionContext,
  RecordSectionId,
  RecordTemplateConfig,
  RecordTemplateTab,
  RecordToolbarActionId,
  RecordViewContext,
} from './recordTemplate.types';
import { RecordTemplateHeader } from './RecordTemplateHeader';
import { renderRecordSection } from './sections';

// ---------------------------------------------------------------------------
// Contract
// ---------------------------------------------------------------------------

/** What a section renderer gets (defined with the template contract; re-exported for callers of `renderSection`). */
export type { RecordSectionContext } from './recordTemplate.types';

export interface RecordTemplateModalProps {
  open: boolean;
  onClose: () => void;
  /** The record type's template (`getRecordTemplate(entityName)`). */
  config: RecordTemplateConfig;
  /** The browse set's row ids, in order — the "1 of N" set. */
  recordIds: readonly string[];
  /** 0-based position of the shown record in `recordIds`. */
  index: number;
  /** Called with the new position when the user pages to the previous / next row. */
  onIndexChange: (index: number) => void;
  /** Called once when the caller deleted the record shown here, so the opener can re-read its list. */
  onRecordDeleted?: () => void;
  /**
   * Called when the shown record settles: loaded, denied or not found (NOT while loading, and not for a retryable error,
   * where nothing was learned yet). The `?record=` deep link keeps the link in the address bar until then.
   */
  onSettled?: () => void;
  /** Renders one section of the shown record. Default: the template's sections (`renderRecordSection`). */
  renderSection?: (id: RecordSectionId, context: RecordSectionContext) => React.ReactNode;
  /**
   * Task 013. True for a record shown as a CHILD of another record (an invoice or document opened from a row of the record
   * behind it; `ChildRecordModal`): the same modal, with rows that would open a further record behaving as before, so a
   * child never opens a grandchild. Absent everywhere else.
   */
  nested?: boolean;
  /**
   * Task 015. The record as the opener already has it (a row of a list): shown as ready at once, with no read. Refresh and a
   * re-read after a save then read through `readRecord`, else the entity's own reader.
   */
  seed?: DetailRecord;
  /** Task 015. How this opening reads its record again (a child opened from a list re-reads that list). Absent = the entity's reader. */
  readRecord?: (id: string) => Promise<DetailRecord>;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

/** Below this viewport width the two columns stack, left first (FR-02). */
const STACK_BELOW = '@media (max-width: 999.98px)';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    minHeight: 0,
  },
  chrome: {
    flexShrink: 0,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  commandBar: {
    paddingBlock: tokens.spacingVerticalXS,
    paddingInline: tokens.spacingHorizontalS,
    borderBottom: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
  },
  tabs: {
    paddingInline: tokens.spacingHorizontalM,
    borderBottom: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
  },
  notice: {
    marginBlock: tokens.spacingVerticalS,
    marginInline: tokens.spacingHorizontalL,
  },
  noticeLink: {
    fontFamily: tokens.fontFamilyMonospace,
    overflowWrap: 'anywhere',
    userSelect: 'all',
  },
  progress: {
    flexShrink: 0,
  },
  content: {
    flexGrow: 1,
    flexShrink: 1,
    flexBasis: 0,
    minHeight: 0,
    overflowY: 'auto',
    paddingBlock: tokens.spacingVerticalL,
    paddingInline: tokens.spacingHorizontalL,
    backgroundColor: tokens.colorNeutralBackground2,
    ...thinScrollbarStyle,
  },
  columns: {
    display: 'grid',
    columnGap: tokens.spacingHorizontalL,
    rowGap: tokens.spacingVerticalL,
    alignItems: 'start',
  },
  twoColumns: {
    gridTemplateColumns: 'minmax(0, 66fr) minmax(0, 34fr)',
    [STACK_BELOW]: { gridTemplateColumns: 'minmax(0, 1fr)' },
  },
  oneColumn: {
    gridTemplateColumns: 'minmax(0, 1fr)',
  },
  column: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
    minWidth: 0,
  },
  ready: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
  },
  // The action hosts' notices and dialogs (a failed download, a refused delete, the recall box). Usually empty.
  actionHosts: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    ':empty': { display: 'none' },
    ':not(:empty)': { marginBottom: tokens.spacingVerticalL },
  },
  center: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    minHeight: '160px',
    gap: tokens.spacingHorizontalM,
  },
  state: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    minHeight: '200px',
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground2,
    textAlign: 'center',
  },
  stateIcon: {
    fontSize: '40px',
    color: tokens.colorNeutralForeground3,
  },
});

// ---------------------------------------------------------------------------
// Modal
// ---------------------------------------------------------------------------

type Notice = { key: string; intent: 'success' | 'warning'; text: string; link?: string };

const noop = (): void => undefined;

export const RecordTemplateModal: React.FC<RecordTemplateModalProps> = ({
  open,
  onClose,
  config,
  recordIds,
  index,
  onIndexChange,
  onRecordDeleted,
  onSettled,
  renderSection = renderRecordSection,
  nested = false,
  seed,
  readRecord,
}) => {
  const styles = useStyles();
  const detail = getRecordDetailEntity(config.entity);
  const [attempt, setAttempt] = React.useState(0);
  // Records deleted in this view (a 204): shown as deleted, never re-read (the read would answer "no access").
  const [deletedIds, setDeletedIds] = React.useState<ReadonlySet<string>>(() => new Set());
  // Sections and action hosts with an operation in flight (keyed `section:{id}` / `action:{id}`); any one holds
  // browsing, Close and Refresh.
  const [busyKeys, setBusyKeys] = React.useState<ReadonlySet<string>>(() => new Set());
  // One stable callback per key, so a section or host can list it as an effect dependency.
  const [busyCallbacks] = React.useState(() => new Map<string, (busy: boolean) => void>());
  // The actions the action hosts and the sections' lists offer for the shown record (toolbar and section actions alike),
  // and one stable reporter per action id.
  const [hostActions, setHostActions] = React.useState<Partial<Record<RecordToolbarActionId, RecordHostAction>>>({});
  const [hostActionCallbacks] = React.useState(
    () => new Map<RecordToolbarActionId, (action: RecordHostAction | null) => void>()
  );
  // Per-section re-read requests (an action asked one section to re-read), added to that section's `refreshSignal`.
  const [sectionSignals, setSectionSignals] = React.useState<Partial<Record<RecordSectionId, number>>>({});
  // The record on screen when Refresh was pressed, kept until the re-read answers (no flash of the loading state).
  const [stale, setStale] = React.useState<{ key: string; record: DetailRecord } | null>(null);
  const [notice, setNotice] = React.useState<Notice | null>(null);
  const tabs = config.tabs ?? [];
  const [activeTab, setActiveTab] = React.useState<RecordTemplateTab['id']>(tabs[0]?.id ?? 'overview');

  const id = recordIds[index];
  const recordKey = `${config.entity}:${id ?? ''}`;
  const isDeleted = !!id && deletedIds.has(id);
  const holdNavigation = busyKeys.size > 0;

  const state = useRecord(detail, open && !isDeleted ? id : undefined, attempt, { seed, read: readRecord });
  // The refreshed record stays on screen while its re-read is in flight, and also when the re-read did not return a
  // record while an operation is in flight: replacing the sections then would unmount the busy one and lose its result
  // (task 003 review R3). The answer is shown once the hold releases.
  const keepStale =
    stale !== null &&
    stale.key === recordKey &&
    state.status !== 'ready' &&
    (state.status === 'loading' || holdNavigation);
  const refreshing = keepStale && state.status === 'loading';
  const shown: RecordState = keepStale && stale ? { status: 'ready', record: stale.record } : state;

  React.useEffect(() => {
    if (stale && state.status !== 'loading' && (state.status === 'ready' || !holdNavigation)) setStale(null);
  }, [state.status, stale, holdNavigation]);

  // Task 015: the action hosts stay mounted (at one place in the tree) while the record is ready, and when a re-read answers
  // not-found they keep the last ready record: a save refused with 404 re-reads the record, and the dialog that shows
  // "This event is no longer available." must not be unmounted under that message. No toolbar is drawn then (the chrome
  // needs a record), so no action is offered for the vanished record.
  const lastReady = React.useRef<{ key: string; record: DetailRecord } | null>(null);
  const shownRecord = shown.status === 'ready' ? shown.record : null;
  React.useEffect(() => {
    if (shownRecord) lastReady.current = { key: recordKey, record: shownRecord };
  }, [shownRecord, recordKey]);
  const hostRow: DetailRecord | null = isDeleted
    ? null
    : (shownRecord ??
      (shown.status === 'notfound' && lastReady.current?.key === recordKey ? lastReady.current.record : null));

  const settled = !isDeleted && (state.status === 'ready' || state.status === 'denied' || state.status === 'notfound');
  React.useEffect(() => {
    if (settled) onSettled?.();
  }, [settled, id, onSettled]);

  // Who the caller is (delete-own, the requester check) and their project levels (Upload): read once per opening, as R3
  // did. Identity does not change from record to record, so paging does not read it again; `caller` is memoised so the
  // sections that take it see a new object only when the identity changes.
  const callerContext = useCallerContext(open);
  const caller = React.useMemo(() => callerIdentityOf(callerContext), [callerContext]);

  const busyCallbackFor = React.useCallback(
    (key: string): ((busy: boolean) => void) => {
      let callback = busyCallbacks.get(key);
      if (!callback) {
        callback = (busy: boolean) =>
          setBusyKeys(prev => {
            if (prev.has(key) === busy) return prev;
            const next = new Set(prev);
            if (busy) next.add(key);
            else next.delete(key);
            return next;
          });
        busyCallbacks.set(key, callback);
      }
      return callback;
    },
    [busyCallbacks]
  );

  const hostActionCallbackFor = React.useCallback(
    (actionId: RecordToolbarActionId): ((action: RecordHostAction | null) => void) => {
      let callback = hostActionCallbacks.get(actionId);
      if (!callback) {
        callback = (action: RecordHostAction | null) =>
          setHostActions(prev => {
            if (action === null && !(actionId in prev)) return prev;
            const next = { ...prev };
            if (action === null) delete next[actionId];
            else next[actionId] = action;
            return next;
          });
        hostActionCallbacks.set(actionId, callback);
      }
      return callback;
    },
    [hostActionCallbacks]
  );

  const refreshSection = React.useCallback(
    (section: RecordSectionId) => setSectionSignals(prev => ({ ...prev, [section]: (prev[section] ?? 0) + 1 })),
    []
  );

  // Task 013. Collapsed sections are remembered per user (the `/me` identity, hashed) and record type; a section card reads
  // this scope. A record that can open a child record (not itself a child) hosts it here, as a descendant of this modal.
  const sectionStateScope = React.useMemo(
    () => ({ entity: config.entity, userKey: sectionStateUserKeyOf(caller) }),
    [config.entity, caller]
  );
  const childHost = useChildRecordHost(refreshSection);

  // An action deleted the shown record (a 204): show it as deleted, never re-read it, and tell the opener.
  const handleRecordDeleted = React.useCallback(() => {
    if (id) setDeletedIds(prev => new Set(prev).add(id));
    onRecordDeleted?.();
  }, [id, onRecordDeleted]);

  // An entity with no read description is never offered by an opener; render nothing then.
  if (!detail) return null;

  const record = shown.status === 'ready' ? shown.record : null;
  const title = recordTemplateTitle(config, record);

  const handleNavigate = (dir: 'prev' | 'next'): void => {
    const next = dir === 'prev' ? index - 1 : index + 1;
    if (next >= 0 && next < recordIds.length) onIndexChange(next);
  };

  // SprkModal routes the ×, Escape and the backdrop through `onClose`, so one guard covers all three; the footer Close is
  // also disabled so the hold is visible (as R3).
  const handleClose = (): void => {
    if (!holdNavigation) onClose();
  };

  const handleRefresh = (): void => {
    if (state.status === 'ready') setStale({ key: recordKey, record: state.record });
    setNotice(null);
    setAttempt(n => n + 1);
  };
  // What an action host calls after a save (task 015): the same re-read as Refresh. Not held by `holdNavigation`: the host
  // that saved has already reported not busy, and the re-read keeps the shown record until the answer arrives. It does
  // nothing once the record is already not-found: a re-read would learn nothing new, and it would send the state back to
  // loading, which unmounts the hosts and the dialog that is showing why (a second refused save). The toolbar Refresh is
  // `handleRefresh` and is unchanged.
  const refreshRecord = (): void => {
    if (state.status === 'notfound') return;
    handleRefresh();
  };

  const handleCopyLink = async (): Promise<void> => {
    if (!id) return;
    const link = recordLinkFor(config.entity, id, window.location);
    if (!link) {
      setNotice({ key: recordKey, intent: 'warning', text: "A link to this record couldn't be made." });
      return;
    }
    try {
      // No clipboard (an insecure context, a denied permission) is a failure like any other: shown, never thrown.
      if (!navigator.clipboard?.writeText) throw new Error('clipboard unavailable');
      await navigator.clipboard.writeText(link);
      setNotice({ key: recordKey, intent: 'success', text: 'Link copied.' });
    } catch {
      setNotice({ key: recordKey, intent: 'warning', text: "The link couldn't be copied. Copy it from here:", link });
    }
  };

  // The modal's own actions: Refresh (hidden while an operation holds the view, so its result is never replaced) and Copy
  // link. An action with a host, or one a section offers (Upload, New message, Add event: tasks 004 / 012), is
  // shown while its owner offers it; the rest are hidden, not disabled.
  const handlers: Partial<Record<RecordToolbarActionId, () => void | Promise<void>>> = {
    ...(holdNavigation ? {} : { refresh: handleRefresh }),
    copyLink: handleCopyLink,
  };
  // Every action this record's config lists, in the toolbar or in a section's header (task 009).
  const actionIds = recordActionIdsOf(config);
  // Task 006 (FR-06 / FR-07): the rights the record's lists reported with their create offers. A create action is shown
  // only when its list offers it AND reported its right as `true` (`isCreateActionOffered`); the sections' read-only lines
  // are derived from the same answers (recordAccess.ts), never from the entity type.
  const createRights = createRightsOf(actionIds, hostActions);
  // The record's route root (its read description's documents root) tells `recordAccessFactsOf` whether `/me` has a level
  // for it (task 011: `projects` for a project, `records` for a matter or work assignment), and whether that level is an
  // answer or only the fail-safe guess (no entry for the project). "Your access" is that level and nothing else, and it
  // is display only: a level DECIDES writes only for a project caller without `writeAccessPerRecord` (the lists decide
  // the rest; Add event also needs a creatable event type), and a `writeAccessPerRecord` caller sees no level line.
  const accessFacts = recordAccessFactsOf(detail.documentsRoot, id ?? '', callerContext);
  const yourAccess = yourAccessOf(accessFacts);
  // One descriptor per action, wherever it shows (the toolbar or a section's header): the same visibility rule for both.
  const actionOf = (actionId: RecordToolbarActionId): RecordCommandBarAction => {
    const Icon = RECORD_ACTION_ICONS[actionId];
    const handler = handlers[actionId];
    const hosted = hostActions[actionId];
    const offered =
      handler !== undefined ||
      (isRecordCreateAction(actionId) ? isCreateActionOffered(hosted) : hosted?.visible === true);
    return {
      id: actionId,
      label: RECORD_ACTION_TEXT[actionId].label,
      tooltip: (handler === undefined && hosted?.tooltip) || RECORD_ACTION_TEXT[actionId].tooltip,
      icon: <Icon />,
      visible: offered,
      onClick: handler ?? (hosted?.visible ? hosted.onClick : noop),
    };
  };
  const actions: RecordCommandBarAction[] = config.toolbar.map(actionOf);
  // A section's header actions (task 009): its offered ones only, as `HeaderToolbar` icon slots (hide, don't disable).
  const headerActionsOf = (section: RecordSectionId): IHeaderToolbarSlot[] =>
    (config.sectionActions?.[section] ?? [])
      .map(actionOf)
      .filter(action => action.visible === true)
      .map(action => ({ key: action.id, icon: action.icon, tooltip: action.tooltip, onClick: action.onClick }));

  const viewContext = (row: DetailRecord, rowId: string): RecordViewContext => ({
    config,
    detail,
    recordId: rowId,
    record: row,
    title,
    callerContext,
    caller,
    onBusyChange: noop,
    onRecordDeleted: handleRecordDeleted,
    refreshSection,
    refreshRecord,
  });

  const sectionContext = (
    section: RecordSectionId,
    full: boolean,
    row: DetailRecord,
    rowId: string
  ): RecordSectionContext => ({
    ...viewContext(row, rowId),
    full,
    refreshSignal: attempt + (sectionSignals[section] ?? 0),
    onBusyChange: busyCallbackFor(`section:${section}`),
    // A section that owns a create dialog offers its action here, only if this record's config lists it (FR-03): in the
    // toolbar or in a section's header (task 009).
    offerAction: actionId => (actionIds.includes(actionId) ? hostActionCallbackFor(actionId) : undefined),
    headerActions: headerActionsOf(section),
    // Task 006 (FR-07): why this section is read-only, from the rights its lists reported (none while unknown).
    readOnlyNote: readOnlyNoteOf(section, createRights, accessFacts),
  });

  const hostContext = (actionId: RecordToolbarActionId, row: DetailRecord, rowId: string): RecordActionHostContext => ({
    ...viewContext(row, rowId),
    onBusyChange: busyCallbackFor(`action:${actionId}`),
    onActionChange: hostActionCallbackFor(actionId),
  });

  // The action hosts the record's config lists (toolbar or section), mounted with the record (their dialogs and notices
  // live here).
  const renderActionHosts = (row: DetailRecord, rowId: string): React.ReactNode => (
    <div className={styles.actionHosts} data-testid="record-template-action-hosts">
      {actionIds.map(actionId => {
        const Host = RECORD_ACTION_HOSTS[actionId];
        return Host ? <Host key={actionId} {...hostContext(actionId, row, rowId)} /> : null;
      })}
    </div>
  );

  const shownTab: RecordTemplateTab['id'] = tabs.some(t => t.id === activeTab)
    ? activeTab
    : (tabs[0]?.id ?? 'overview');
  const shownNotice = notice && notice.key === recordKey ? notice : null;

  const renderBody = (): React.ReactNode => {
    if (isDeleted) {
      return (
        <div className={styles.state} role="status" data-testid="record-detail-deleted">
          <Text size={400} weight="semibold">
            This {config.label.toLowerCase()} was deleted
          </Text>
        </div>
      );
    }
    switch (shown.status) {
      case 'loading':
        return (
          <div className={styles.center}>
            <Spinner size="medium" label="Loading record..." />
          </div>
        );
      case 'denied':
        return (
          <div className={styles.state} role="alert" data-testid="record-detail-denied">
            <LockClosedRegular className={styles.stateIcon} aria-hidden />
            <Text size={400} weight="semibold">
              You don't have access to this record
            </Text>
          </div>
        );
      case 'notfound':
        return (
          <div className={styles.state} role="status" data-testid="record-detail-notfound">
            <Text size={400} weight="semibold">
              This record is no longer available
            </Text>
          </div>
        );
      case 'error':
        return (
          <div className={styles.state} role="alert" data-testid="record-detail-error">
            <Text size={300}>{shown.message}</Text>
            {shown.retryable && (
              <Button appearance="secondary" onClick={() => setAttempt(n => n + 1)}>
                Try again
              </Button>
            )}
          </div>
        );
      case 'ready': {
        if (!id) return null;
        const row = shown.record;
        const one = (section: RecordSectionId, full: boolean): React.ReactNode => (
          <React.Fragment key={section}>
            {renderSection(section, sectionContext(section, full, row, id))}
          </React.Fragment>
        );
        if (shownTab !== 'overview') {
          return (
            <div className={styles.ready}>
              <div data-testid="record-template-tab-panel">{one(shownTab, true)}</div>
            </div>
          );
        }
        const twoColumns = config.right.length > 0;
        return (
          <div className={styles.ready}>
            <div
              className={mergeClasses(styles.columns, twoColumns ? styles.twoColumns : styles.oneColumn)}
              data-testid="record-template-columns"
              data-columns={twoColumns ? '2' : '1'}
            >
              <div className={styles.column} data-testid="record-template-left">
                {config.left.map(section => one(section, false))}
              </div>
              {twoColumns && (
                <div className={styles.column} data-testid="record-template-right">
                  {config.right.map(section => one(section, false))}
                </div>
              )}
            </div>
          </div>
        );
      }
    }
  };

  const ready = !isDeleted && record !== null;
  const name = record ? recordText(record, config.nameAttribute) : null;

  return (
    <RecordSectionStateContext.Provider value={sectionStateScope}>
      <ChildRecordContext.Provider value={nested ? null : childHost.open}>
        <SprkModal
          open={open}
          onClose={handleClose}
          title={title}
          size="lg"
          padded={false}
          // A child record is one record: no "1 of 1" browsing.
          nav={
            nested
              ? undefined
              : {
                  index,
                  total: recordIds.length,
                  onNavigate: handleNavigate,
                  onBeforeNavigate: () => !holdNavigation,
                }
          }
          footer={
            <Button appearance="primary" onClick={handleClose} disabled={holdNavigation}>
              Close
            </Button>
          }
        >
          <div className={styles.root} data-testid="record-template-body">
            {/* Fixed chrome: only for a record the caller may see (denied / error / deleted show none of it). */}
            {ready && record && (
              <div className={styles.chrome} data-testid="record-template-chrome">
                <RecordCommandBar
                  className={styles.commandBar}
                  actions={actions}
                  ariaLabel={`${config.label} actions`}
                />
                <RecordTemplateHeader
                  name={name ?? config.label}
                  typeLabel={config.label}
                  facts={config.headerFacts.map(fact => ({
                    label: fact.label,
                    value: formatDetailValue(record, fact),
                  }))}
                  // "Your access" (FR-07, task 006): only what the BFF answered for this record; nothing while unknown.
                  access={yourAccess ?? undefined}
                />
                {tabs.length > 0 && (
                  <TabList
                    className={styles.tabs}
                    size="small"
                    selectedValue={shownTab}
                    onTabSelect={(_, data) => setActiveTab(data.value as RecordTemplateTab['id'])}
                    aria-label={`${config.label} sections`}
                  >
                    {tabs.map(tab => (
                      <Tab key={tab.id} value={tab.id}>
                        {tab.label}
                      </Tab>
                    ))}
                  </TabList>
                )}
                {shownNotice && (
                  <MessageBar
                    className={styles.notice}
                    intent={shownNotice.intent}
                    data-testid="record-template-notice"
                  >
                    <MessageBarBody>
                      {shownNotice.text}
                      {shownNotice.link && (
                        <>
                          {' '}
                          <span className={styles.noticeLink}>{shownNotice.link}</span>
                        </>
                      )}
                    </MessageBarBody>
                    <MessageBarActions
                      containerAction={
                        <Button
                          appearance="transparent"
                          icon={<DismissRegular />}
                          aria-label="Dismiss"
                          onClick={() => setNotice(null)}
                        />
                      }
                    />
                  </MessageBar>
                )}
              </div>
            )}
            {refreshing && (
              <ProgressBar className={styles.progress} thickness="medium" aria-label="Refreshing record" />
            )}
            {/* Keyed by record, so every section's own state resets when the user pages to another row. */}
            <div className={styles.content} key={recordKey} data-testid="record-template-content">
              {hostRow && id && renderActionHosts(hostRow, id)}
              {renderBody()}
            </div>
            {!nested && childHost.modal}
          </div>
        </SprkModal>
      </ChildRecordContext.Provider>
    </RecordSectionStateContext.Provider>
  );
};

export default RecordTemplateModal;
