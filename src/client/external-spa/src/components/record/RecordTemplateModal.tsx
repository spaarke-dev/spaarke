/**
 * RecordTemplateModal — the config-driven record view of the external SPA and the Teams tab
 * (spaarke-SPA-external-access-platform-r4 tasks 003 and 005, FR-01 / FR-02). Every in-app record open uses it, for all
 * six types: a grid row (`GridWidgetBody`), a `?record=` deep link, a notification and an intake success link (the last
 * three through `RecordOpener`). It replaced R3's stacked record-detail modal (retired in task 005).
 *
 * CONFIG ONLY (FR-01). Everything type-specific comes from the `RecordTemplateConfig` (`recordTemplates.ts`): title,
 * header facts, tabs, the left / right sections and the toolbar actions. Nothing here branches on the entity name. Each
 * section id renders through `sections/` (`renderRecordSection`); a toolbar action whose flow has its own dialogs or
 * notices (Download, Delete request) is an action host in `actions/`, mounted in the record's content for every action
 * id the config lists, which offers or withdraws its action. The four create actions (Upload, New message, Add to-do, Add
 * event) are offered by the section that owns the list and its dialog (`offerAction`); that section renders the dialog (a
 * `FormModal`) itself, so the dialog is a React descendant of this modal (FR-03, task 004).
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
 * to the record, and it leaves through the opener's `onNavigate` (no router import here).
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
import {
  createRightsOf,
  isCreateActionOffered,
  isRecordCreateAction,
  readOnlyNoteOf,
  recordAccessFactsOf,
  yourAccessOf,
  type CreateRight,
  type RecordSectionRightId,
} from './recordAccess';
import { useRecord, type RecordState } from './recordReads';
import { RECORD_ACTION_TEXT, recordTemplateTitle, recordText } from './recordTemplates';
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
  /**
   * The host's in-app navigation (task 007, FR-10): what "Submit invoice" uses to leave for the submission page. The
   * template imports no router, so the opener hands it down (`RecordOpenerProvider`, the grid widgets). Absent: the
   * action is hidden.
   */
  onNavigate?: (path: string) => void;
  /** Renders one section of the shown record. Default: the template's sections (`renderRecordSection`). */
  renderSection?: (id: RecordSectionId, context: RecordSectionContext) => React.ReactNode;
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
  onNavigate,
  renderSection = renderRecordSection,
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
  // The toolbar actions the action hosts offer for the shown record, and one stable reporter per action id.
  const [hostActions, setHostActions] = React.useState<Partial<Record<RecordToolbarActionId, RecordHostAction>>>({});
  const [hostActionCallbacks] = React.useState(
    () => new Map<RecordToolbarActionId, (action: RecordHostAction | null) => void>()
  );
  // Rights a section reports without a toolbar action (the invoice Files list's Upload; task 006 fix round 1), and one
  // stable reporter per right id. A list reports `null` on unmount, so paging to another record forgets the old answer.
  const [sectionRights, setSectionRights] = React.useState<Partial<Record<RecordSectionRightId, CreateRight>>>({});
  const [sectionRightCallbacks] = React.useState(() => new Map<RecordSectionRightId, (right: CreateRight) => void>());
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

  const state = useRecord(detail, open && !isDeleted ? id : undefined, attempt);
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

  const sectionRightCallbackFor = React.useCallback(
    (rightId: RecordSectionRightId): ((right: CreateRight) => void) => {
      let callback = sectionRightCallbacks.get(rightId);
      if (!callback) {
        callback = (right: CreateRight) =>
          setSectionRights(prev => {
            if (right === null && !(rightId in prev)) return prev;
            if (prev[rightId] === right) return prev;
            const next = { ...prev };
            if (right === null) delete next[rightId];
            else next[rightId] = right;
            return next;
          });
        sectionRightCallbacks.set(rightId, callback);
      }
      return callback;
    },
    [sectionRightCallbacks]
  );

  const refreshSection = React.useCallback(
    (section: RecordSectionId) => setSectionSignals(prev => ({ ...prev, [section]: (prev[section] ?? 0) + 1 })),
    []
  );

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
  // link. An action with a host, or one a section offers (Upload, New message, Add to-do, Add event: task 004), is shown
  // while its owner offers it; the rest are hidden, not disabled.
  const handlers: Partial<Record<RecordToolbarActionId, () => void | Promise<void>>> = {
    ...(holdNavigation ? {} : { refresh: handleRefresh }),
    copyLink: handleCopyLink,
  };
  // Task 006 (FR-06 / FR-07): the rights the record's lists reported with their create offers. A create action is shown
  // only when its list offers it AND reported its right as `true` (`isCreateActionOffered`); the header's "Your access"
  // and the sections' read-only lines are derived from the same answers (recordAccess.ts), never from the entity type.
  // A section's right without a toolbar action (the invoice Files list's Upload) counts for "Your access" and its own
  // section's line, and adds nothing to the toolbar.
  const createRights = createRightsOf(config.toolbar, hostActions, sectionRights);
  // The record's route root (its read description's documents root) tells `recordAccessFactsOf` whether `/me` has a level
  // for it, and whether that level is an answer or only the fail-safe guess (no entry for the project).
  const accessFacts = recordAccessFactsOf(detail.documentsRoot, id ?? '', callerContext);
  const yourAccess = yourAccessOf(accessFacts, createRights);
  const actions: RecordCommandBarAction[] = config.toolbar.map(actionId => {
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
  });

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
    // A section that owns a create dialog offers its toolbar action here, only if this record's toolbar lists it (FR-03).
    offerAction: actionId => (config.toolbar.includes(actionId) ? hostActionCallbackFor(actionId) : undefined),
    // Task 006 (FR-07): why this section is read-only, from the rights its lists reported (none while unknown).
    readOnlyNote: readOnlyNoteOf(section, createRights, accessFacts),
    reportRight: sectionRightCallbackFor,
  });

  const hostContext = (actionId: RecordToolbarActionId, row: DetailRecord, rowId: string): RecordActionHostContext => ({
    ...viewContext(row, rowId),
    onBusyChange: busyCallbackFor(`action:${actionId}`),
    onActionChange: hostActionCallbackFor(actionId),
    onNavigate,
    // The same guard as the footer Close: refused while an operation holds the view.
    requestClose: () => {
      if (holdNavigation) return false;
      onClose();
      return true;
    },
  });

  // The action hosts the record's toolbar lists, mounted with the record (their dialogs and notices live here).
  const renderActionHosts = (row: DetailRecord, rowId: string): React.ReactNode => (
    <div className={styles.actionHosts} data-testid="record-template-action-hosts">
      {config.toolbar.map(actionId => {
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
              {renderActionHosts(row, id)}
              <div data-testid="record-template-tab-panel">{one(shownTab, true)}</div>
            </div>
          );
        }
        const twoColumns = config.right.length > 0;
        return (
          <div className={styles.ready}>
            {renderActionHosts(row, id)}
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
    <SprkModal
      open={open}
      onClose={handleClose}
      title={title}
      size="lg"
      padded={false}
      nav={{
        index,
        total: recordIds.length,
        onNavigate: handleNavigate,
        onBeforeNavigate: () => !holdNavigation,
      }}
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
            <RecordCommandBar className={styles.commandBar} actions={actions} ariaLabel={`${config.label} actions`} />
            <RecordTemplateHeader
              name={name ?? config.label}
              typeLabel={config.label}
              facts={config.headerFacts.map(fact => ({ label: fact.label, value: formatDetailValue(record, fact) }))}
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
              <MessageBar className={styles.notice} intent={shownNotice.intent} data-testid="record-template-notice">
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
        {refreshing && <ProgressBar className={styles.progress} thickness="medium" aria-label="Refreshing record" />}
        {/* Keyed by record, so every section's own state resets when the user pages to another row. */}
        <div className={styles.content} key={recordKey} data-testid="record-template-content">
          {renderBody()}
        </div>
      </div>
    </SprkModal>
  );
};

export default RecordTemplateModal;
