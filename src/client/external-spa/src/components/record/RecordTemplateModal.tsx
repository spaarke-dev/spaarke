/**
 * RecordTemplateModal — the config-driven record view of the external SPA and the Teams tab
 * (spaarke-SPA-external-access-platform-r4 task 003, FR-01 / FR-02). It replaces R3's stacked `RecordDetailModal` for all
 * six types; task 005 supplies the section renderers and switches the openers over.
 *
 * CONFIG ONLY (FR-01). Everything type-specific comes from the `RecordTemplateConfig` (`recordTemplates.ts`): title,
 * header facts, tabs, the left / right sections and the toolbar actions. Nothing here branches on the entity name.
 *
 * ENVELOPE (ADR-050). A thin config of the canonical `SprkModal`, size `lg`: its own header carries "‹ N of M ›", the
 * title, the window controls and ×; its footer carries Close only. No nested `RecordNavigationModalShell`.
 *
 * LAYOUT (FR-02). The SprkModal body is unpadded and the template root fills it (height 100%), so the body itself never
 * scrolls. Inside, the toolbar (`RecordCommandBar`), the header and the optional TabList are fixed; only the content
 * region scrolls, with the canonical `thinScrollbarStyle`. The overview is two columns, 66% / 34%, that stack (left
 * first) below 1000px; a type with no right-hand sections uses one full-width column.
 *
 * STATES (R3's, same text and test ids as `RecordDetailModal`): loading, denied (nothing of the record on screen: no
 * toolbar, header, field or section), not found, error (Try again only when retrying can help), deleted. While a
 * section reports an operation in flight (`onBusyChange`), browsing and Close are held (`onBeforeNavigate` returns false;
 * the footer Close is disabled; ×, Escape and the backdrop are ignored).
 *
 * DATA. The record is read with the moved R3 read (`useRecord`, `recordReads.ts`) through the entity's
 * `RECORD_DETAIL_ENTITIES` description: the same id-filtered module fetch, the same columns. Refresh re-runs that read
 * and keeps the record on screen until the new answer arrives. Copy link writes `{origin}{path}?record={token}:{id}`.
 * The other toolbar actions are hidden until tasks 005/006 wire them (hide, don't disable).
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
import {
  formatDetailValue,
  getRecordDetailEntity,
  type DetailRecord,
  type RecordDetailEntity,
} from '../detail/RecordDetailFields';
import { recordLinkFor } from '../detail/record-targets';
import { useRecord, type RecordState } from './recordReads';
import { RECORD_ACTION_TEXT, recordTemplateTitle, recordText } from './recordTemplates';
import {
  RECORD_SECTION_LABELS,
  type RecordSectionId,
  type RecordTemplateConfig,
  type RecordTemplateTab,
  type RecordToolbarActionId,
} from './recordTemplate.types';
import { RecordTemplateHeader } from './RecordTemplateHeader';

// ---------------------------------------------------------------------------
// Contract
// ---------------------------------------------------------------------------

/** What a section renderer gets (task 005 supplies the renderers). */
export interface RecordSectionContext {
  config: RecordTemplateConfig;
  /** The entity's read description (fields, documents root, invoices lookup...). */
  detail: RecordDetailEntity;
  recordId: string;
  record: DetailRecord;
  /** The modal title ("Matter MAT-001"). */
  title: string;
  /** True when the section is shown alone on its own tab, full width. */
  full: boolean;
  /** Changes when the user presses Refresh (or Try again); a section with its own reads re-reads then. */
  refreshSignal: number;
  /**
   * Report an operation in flight (a message send, a recall post). While any section is busy, browsing and Close are
   * held so the operation's result is not lost. A section clears it when the operation settles and on unmount.
   */
  onBusyChange: (busy: boolean) => void;
  /** The record was deleted by an action in this view: the modal shows the deleted state and tells its opener. */
  onRecordDeleted: () => void;
}

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
  /** Called when the shown record settles: loaded, denied or not found (as `RecordDetailModal`). */
  onSettled?: () => void;
  /** Renders one section of the shown record. Default: a titled placeholder (task 005 replaces it). */
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
  placeholder: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    paddingBlock: tokens.spacingVerticalM,
    paddingInline: tokens.spacingHorizontalL,
    backgroundColor: tokens.colorNeutralBackground1,
    borderRadius: tokens.borderRadiusLarge,
  },
  placeholderHeading: {
    margin: 0,
    fontSize: tokens.fontSizeBase400,
    lineHeight: tokens.lineHeightBase400,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
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
// Default section renderer (placeholder until task 005)
// ---------------------------------------------------------------------------

const SectionPlaceholder: React.FC<{ id: RecordSectionId }> = ({ id }) => {
  const styles = useStyles();
  const label = RECORD_SECTION_LABELS[id];
  return (
    <section className={styles.placeholder} aria-label={label} data-testid={`record-section-${id}`}>
      <h3 className={styles.placeholderHeading}>{label}</h3>
    </section>
  );
};

const renderSectionPlaceholder = (id: RecordSectionId): React.ReactNode => <SectionPlaceholder id={id} />;

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
  renderSection = renderSectionPlaceholder,
}) => {
  const styles = useStyles();
  const detail = getRecordDetailEntity(config.entity);
  const [attempt, setAttempt] = React.useState(0);
  // Records deleted in this view (a 204): shown as deleted, never re-read (the read would answer "no access").
  const [deletedIds, setDeletedIds] = React.useState<ReadonlySet<string>>(() => new Set());
  // Sections with an operation in flight; any one holds browsing and Close.
  const [busySections, setBusySections] = React.useState<ReadonlySet<RecordSectionId>>(() => new Set());
  // One stable callback per section, so a section can list it as an effect dependency.
  const [busyCallbacks] = React.useState(() => new Map<RecordSectionId, (busy: boolean) => void>());
  // The record on screen when Refresh was pressed, kept until the re-read answers (no flash of the loading state).
  const [stale, setStale] = React.useState<{ key: string; record: DetailRecord } | null>(null);
  const [notice, setNotice] = React.useState<Notice | null>(null);
  const tabs = config.tabs ?? [];
  const [activeTab, setActiveTab] = React.useState<RecordTemplateTab['id']>(tabs[0]?.id ?? 'overview');

  const id = recordIds[index];
  const recordKey = `${config.entity}:${id ?? ''}`;
  const isDeleted = !!id && deletedIds.has(id);

  const state = useRecord(detail, open && !isDeleted ? id : undefined, attempt);
  const refreshing = state.status === 'loading' && stale !== null && stale.key === recordKey;
  const shown: RecordState = refreshing && stale ? { status: 'ready', record: stale.record } : state;

  React.useEffect(() => {
    if (state.status !== 'loading' && stale) setStale(null);
  }, [state.status, stale]);

  const settled = !isDeleted && (state.status === 'ready' || state.status === 'denied' || state.status === 'notfound');
  React.useEffect(() => {
    if (settled) onSettled?.();
  }, [settled, id, onSettled]);

  const busyCallbackFor = React.useCallback(
    (section: RecordSectionId): ((busy: boolean) => void) => {
      let callback = busyCallbacks.get(section);
      if (!callback) {
        callback = (busy: boolean) =>
          setBusySections(prev => {
            if (prev.has(section) === busy) return prev;
            const next = new Set(prev);
            if (busy) next.add(section);
            else next.delete(section);
            return next;
          });
        busyCallbacks.set(section, callback);
      }
      return callback;
    },
    [busyCallbacks]
  );

  // An entity with no read description is never offered by an opener; render nothing then.
  if (!detail) return null;

  const record = shown.status === 'ready' ? shown.record : null;
  const title = recordTemplateTitle(config, record);
  const holdNavigation = busySections.size > 0;

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

  // Only the wired actions are visible; the rest are hidden, not disabled, until tasks 005/006 wire them.
  const handlers: Partial<Record<RecordToolbarActionId, () => void | Promise<void>>> = {
    refresh: handleRefresh,
    copyLink: handleCopyLink,
  };
  const actions: RecordCommandBarAction[] = config.toolbar.map(actionId => {
    const Icon = RECORD_ACTION_ICONS[actionId];
    const handler = handlers[actionId];
    return {
      id: actionId,
      label: RECORD_ACTION_TEXT[actionId].label,
      tooltip: RECORD_ACTION_TEXT[actionId].tooltip,
      icon: <Icon />,
      visible: handler !== undefined,
      onClick: handler ?? noop,
    };
  });

  const sectionContext = (section: RecordSectionId, full: boolean, row: DetailRecord, rowId: string) => ({
    config,
    detail,
    recordId: rowId,
    record: row,
    title,
    full,
    refreshSignal: attempt,
    onBusyChange: busyCallbackFor(section),
    onRecordDeleted: () => {
      setDeletedIds(prev => new Set(prev).add(rowId));
      onRecordDeleted?.();
    },
  });

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
          return <div data-testid="record-template-tab-panel">{one(shownTab, true)}</div>;
        }
        const twoColumns = config.right.length > 0;
        return (
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
              // "Your access" (FR-07) is filled by task 006.
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
