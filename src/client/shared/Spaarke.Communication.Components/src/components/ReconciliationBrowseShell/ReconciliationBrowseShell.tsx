/**
 * ReconciliationBrowseShell.tsx
 *
 * The Pillar E reconciliation BROWSE shell (email-communication-intelligence-r2
 * task 053, spec UI model §99 + NFR-11). A reviewer opens a row from the task-050
 * reconciliation grid (via its `onRecordOpen` override) and steps "N of M"
 * prev/next through the WHOLE Needs-review queue without returning to the grid.
 *
 * SHELL CHROME — the canonical `SprkModal` shell + its `nav` ("N of M") contract
 * (`@spaarke/ui-components`; ADR-050). `SprkModal` renders the prev/next chevrons
 * + the single "N of M" counter in its header and takes arbitrary children, so
 * the body is a real TWO-PANE layout. This is NOT a hand-rolled modal and NOT
 * `RecordNavigationModalShell`.
 *
 *   Deviation (owner-approved 2026-08-07): the POML said "use the `BrowseModal`
 *   preset". That preset wraps its children in `PreviewGridBody` — a fixed
 *   `1fr` stage + `320px` label/value metadata grid (a FILE-PREVIEW layout) that
 *   structurally cannot host a two-pane reader + interactive reconcile tabs.
 *   `SprkModal` + `nav` is the exact shell + exact browse mechanism the preset
 *   is built ON (BrowseModal === SprkModal + nav + PreviewGridBody), minus the
 *   preview grid. ADR-050 stays fully satisfied. See
 *   notes/053-browse-shell-and-reader-complete.md.
 *
 * LEFT READER — the reused `EmailReadingPaneShell` in `hideList` (per-record
 * form) mode (ADR-045 — no second reader), whose body composes the recipients
 * block + the attachment-text-folding `EmailBodyView`. `EmailBodyView` renders
 * body + attachment contents as ONE normalized readable surface (NFR-11) — the
 * anchor space task 054 maps citations into. The reader remounts per record
 * (keyed by id) so navigation re-binds it.
 *
 * RIGHT PANE — a `renderTabs` slot for the three reconcile tabs (Related to =
 * task 052 · Fields = 055 · Tasks = 056/057). This task ships the slot frame.
 *
 * OPEN ORIGINAL — an attachment fold's "Open original" link opens an overlay
 * preview (`PreviewModal`) hosting an `AttachmentList` row for that attachment;
 * activating the row calls `onOpenOriginalActivate` (the host opens the raw file).
 *
 * ADR-012 (context-agnostic — host resolves + supplies all data), ADR-021
 * (Fluent v9 tokens, light + dark), ADR-022 (React-version-agnostic).
 */
import * as React from 'react';
import { makeStyles, tokens, Text, Button } from '@fluentui/react-components';
import { Open16Regular } from '@fluentui/react-icons';
import { SprkModal, PreviewModal, PanelSplitter } from '@spaarke/ui-components';
import { EmailReadingPaneShell } from '../EmailReadingPaneShell';
import { EmailReadingHeader } from '../EmailReadingHeader';
import { EmailRecipients } from '../EmailRecipients';
import { EmailBodyView } from '../EmailBody';
import { AttachmentList } from '../AttachmentList';
import type { IAttachmentItem } from '../../logic/attachments';
import type { ReconciliationAttachmentContent } from '../EmailBody/EmailBodyView.types';
import type { ReconciliationBrowseRecord, ReconciliationBrowseShellProps } from './ReconciliationBrowseShell.types';

const useStyles = makeStyles({
  // Two-pane body filling the SprkModal (unpadded) `xl`/landscape stage: reader
  // LEFT (flex-basis = split ratio), a draggable `PanelSplitter` divider, and the
  // reconcile tabs RIGHT (flex 1). A6 (owner UAT 2026-08-10): a 50/50 default with
  // manual horizontal drag-resize replaces the former fixed `1fr minmax()` grid.
  // `minHeight: 0` lets each pane scroll independently inside the modal height cap.
  twoPane: {
    display: 'flex',
    flexDirection: 'row',
    height: '100%',
    minHeight: 0,
    width: '100%',
  },
  // Reader pane width is driven by the split ratio (inline `flex` style). No
  // `borderRight` — the `PanelSplitter` grip IS the divider now (A6).
  readerPane: {
    position: 'relative',
    display: 'flex',
    minWidth: 0,
    minHeight: 0,
    height: '100%',
    overflow: 'hidden',
  },
  // A5 (owner UAT 2026-08-10): modern thin scrollbar on the scrollable tabs pane.
  // Fluent semantic tokens only (ADR-021) — light-gray thumb that resolves
  // theme-aware, transparent track; matches the shared `thinScrollbarStyle`
  // convention (the reused reader already applies it internally). No hard-coded
  // colors. `scrollbarWidth`/`scrollbarColor` cover Firefox; the
  // `::-webkit-scrollbar*` pseudo-elements cover Chromium/Edge/Safari.
  tabsPane: {
    display: 'flex',
    flexDirection: 'column',
    flex: '1 1 0',
    minWidth: 0,
    minHeight: 0,
    height: '100%',
    overflowY: 'auto',
    backgroundColor: tokens.colorNeutralBackground1,
    scrollbarWidth: 'thin',
    scrollbarColor: `${tokens.colorNeutralStroke1} transparent`,
    '::-webkit-scrollbar': { width: '8px', height: '8px' },
    '::-webkit-scrollbar-track': { backgroundColor: 'transparent' },
    '::-webkit-scrollbar-thumb': {
      backgroundColor: tokens.colorNeutralStroke1,
      borderRadius: tokens.borderRadiusMedium,
    },
    '::-webkit-scrollbar-thumb:hover': { backgroundColor: tokens.colorNeutralStroke1Hover },
  },
  tabsPlaceholder: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    padding: tokens.spacingHorizontalXL,
    textAlign: 'center',
    color: tokens.colorNeutralForeground3,
  },
  bodyRegion: {
    display: 'flex',
    flexDirection: 'column',
    flex: '1 1 auto',
    minHeight: 0,
  },
  recipientsWrap: {
    paddingInline: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalS,
  },
  // TRIAGE panel (prototype parity, owner UAT 2026-08-14) — the AI triage summary + optional
  // priority/category, in a subtle boxed band above the body. Semantic tokens only (ADR-021).
  triageBox: {
    marginInline: tokens.spacingHorizontalM,
    marginTop: tokens.spacingVerticalS,
    padding: tokens.spacingHorizontalM,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
  },
  triageLabel: {
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightSemibold,
    letterSpacing: '0.04em',
    color: tokens.colorNeutralForeground3,
  },
  triageText: { fontSize: tokens.fontSizeBase200, color: tokens.colorNeutralForeground1 },
  triageMeta: { fontSize: tokens.fontSizeBase200, color: tokens.colorNeutralForeground3 },
  openOriginalRow: { paddingInline: tokens.spacingHorizontalM, paddingTop: tokens.spacingVerticalXS },
  emptyState: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    minHeight: '240px',
    color: tokens.colorNeutralForeground3,
  },
});

/** Clamp `n` into `[0, len-1]`; returns 0 for an empty queue. */
function clampIndex(n: number, len: number): number {
  if (len <= 0) return 0;
  if (n < 0) return 0;
  if (n > len - 1) return len - 1;
  return n;
}

// A6 split-ratio bounds — reader gets 25–75% of the two-pane body, defaulting to
// a 50/50 split. Keyboard nudges the divider ±2% per arrow press.
const MIN_SPLIT_RATIO = 0.25;
const MAX_SPLIT_RATIO = 0.75;
const DEFAULT_SPLIT_RATIO = 0.5;
const SPLIT_KEYBOARD_STEP = 0.02;

function clampRatio(r: number): number {
  return Math.max(MIN_SPLIT_RATIO, Math.min(MAX_SPLIT_RATIO, r));
}

interface UseSplitRatioResult {
  /** Attach to the flex container that holds reader + splitter + tabs. */
  containerRef: React.RefObject<HTMLDivElement | null>;
  /** Reader-pane proportion (0.25–0.75). */
  ratio: number;
  /** True while the divider is being dragged. */
  isDragging: boolean;
  /** Handlers for the `<PanelSplitter />` grip. */
  splitterHandlers: {
    onMouseDown: (e: React.MouseEvent) => void;
    onKeyDown: (e: React.KeyboardEvent) => void;
    onDoubleClick: () => void;
  };
}

/**
 * Controlled 50/50 split-ratio state for the A6 drag-resize divider. The shared
 * `useThreadPaneLayout` was considered (§11 reuse) but it models a fixed-width,
 * collapsible LEFT sidebar (px width + localStorage persistence + collapse) and
 * is not exported from `@spaarke/ui-components` — a poor fit for a symmetric,
 * ratio-based reader|tabs split. This local hook owns only the ratio + drag/
 * keyboard/reset logic and drives the reused presentational `PanelSplitter` grip.
 * PCF-safe (ADR-022): React-16-compatible hooks only.
 */
function useSplitRatio(defaultRatio = DEFAULT_SPLIT_RATIO): UseSplitRatioResult {
  const containerRef = React.useRef<HTMLDivElement | null>(null);
  const draggingRef = React.useRef(false);
  const [ratio, setRatio] = React.useState(defaultRatio);
  const [isDragging, setIsDragging] = React.useState(false);

  const onMouseMove = React.useCallback((e: MouseEvent) => {
    if (!draggingRef.current) return;
    const rect = containerRef.current?.getBoundingClientRect();
    if (!rect || rect.width <= 0) return;
    setRatio(clampRatio((e.clientX - rect.left) / rect.width));
  }, []);

  const onMouseUp = React.useCallback(() => {
    draggingRef.current = false;
    setIsDragging(false);
    document.body.style.cursor = '';
    document.body.style.userSelect = '';
  }, []);

  React.useEffect(() => {
    if (!isDragging) return;
    document.addEventListener('mousemove', onMouseMove);
    document.addEventListener('mouseup', onMouseUp);
    return () => {
      document.removeEventListener('mousemove', onMouseMove);
      document.removeEventListener('mouseup', onMouseUp);
    };
  }, [isDragging, onMouseMove, onMouseUp]);

  const onMouseDown = React.useCallback((e: React.MouseEvent) => {
    e.preventDefault();
    draggingRef.current = true;
    setIsDragging(true);
    document.body.style.cursor = 'col-resize';
    document.body.style.userSelect = 'none';
  }, []);

  const onKeyDown = React.useCallback((e: React.KeyboardEvent) => {
    let delta = 0;
    if (e.key === 'ArrowLeft') delta = -SPLIT_KEYBOARD_STEP;
    else if (e.key === 'ArrowRight') delta = SPLIT_KEYBOARD_STEP;
    else return;
    e.preventDefault();
    setRatio(prev => clampRatio(prev + delta));
  }, []);

  const onDoubleClick = React.useCallback(() => setRatio(DEFAULT_SPLIT_RATIO), []);

  return { containerRef, ratio, isDragging, splitterHandlers: { onMouseDown, onKeyDown, onDoubleClick } };
}

/**
 * Project an attachment fold into the `IAttachmentItem` shape the reused
 * `AttachmentList` overlay renders. `uploaded: true` (the original is an
 * archived SPE file) drives the green cloud glyph; a present `documentId` makes
 * the row openable (fires `onActivate`).
 */
function toAttachmentItem(att: ReconciliationAttachmentContent): IAttachmentItem {
  return {
    attachmentId: att.attachmentId,
    name: att.name,
    attachmentType: null,
    documentId: att.documentId ?? null,
    documentName: att.name,
    uploaded: true,
  };
}

export const ReconciliationBrowseShell: React.FC<ReconciliationBrowseShellProps> = ({
  open,
  onClose,
  onSave,
  queue,
  initialIndex = 0,
  onIndexChange,
  renderTabs,
  readerActions,
  onOpenOriginalActivate,
  authenticatedFetch,
  activeCitation,
  uiScale,
}) => {
  const s = useStyles();
  const { containerRef, ratio, isDragging, splitterHandlers } = useSplitRatio();

  const [index, setIndex] = React.useState<number>(() => clampIndex(initialIndex, queue.length));

  // (Re)seat the index on the row the host opened whenever the shell (re)opens or
  // a different row is opened. Navigation updates `index` locally; these deps do
  // not change during a browse session, so a manual step is never clobbered.
  React.useEffect(() => {
    if (open) setIndex(clampIndex(initialIndex, queue.length));
    // eslint-disable-next-line react-hooks/exhaustive-deps -- intentionally re-seat only on open/initialIndex, not on live queue-length churn mid-browse.
  }, [open, initialIndex]);

  const current: ReconciliationBrowseRecord | undefined = queue[index];

  const handleNavigate = React.useCallback(
    (dir: 'prev' | 'next') => {
      const next = clampIndex(dir === 'prev' ? index - 1 : index + 1, queue.length);
      if (next !== index) {
        setIndex(next);
        if (queue[next]) onIndexChange?.(next, queue[next]);
      }
    },
    [index, queue, onIndexChange]
  );

  // Open-original overlay state — the attachment whose original is being previewed.
  const [overlayAttachment, setOverlayAttachment] = React.useState<ReconciliationAttachmentContent | null>(null);
  const closeOverlay = React.useCallback(() => setOverlayAttachment(null), []);

  const title = current?.subject || '(no subject)';

  return (
    <>
      <SprkModal
        open={open}
        onClose={onClose}
        title={title}
        size="xl"
        layout="landscape"
        padded={false}
        uiScale={uiScale}
        dismiss="explicit"
        nav={{ index, total: queue.length, onNavigate: handleNavigate }}
        footerStart={
          <Button appearance="secondary" onClick={onClose} data-testid="reconciliation-browse-close">
            Close
          </Button>
        }
        footer={
          <Button appearance="primary" onClick={onSave ?? onClose} data-testid="reconciliation-browse-save">
            Save
          </Button>
        }
      >
        {current ? (
          <div className={s.twoPane} data-testid="reconciliation-browse-two-pane" ref={containerRef}>
            <div
              className={s.readerPane}
              data-testid="reconciliation-browse-reader"
              style={{ flex: `0 0 ${ratio * 100}%` }}
            >
              {/* Reused reader (ADR-045). Keyed by record id so navigation
                  remounts it and re-binds `initialSelectedId` to the new record. */}
              <EmailReadingPaneShell
                key={current.id}
                items={[]}
                hideList
                initialSelectedId={current.id}
                actions={readerActions}
                renderHeader={() => <EmailReadingHeader subject={current.subject ?? null} />}
                renderBody={() => (
                  <div className={s.bodyRegion}>
                    <div className={s.recipientsWrap}>
                      <EmailRecipients
                        from={current.from ?? null}
                        to={current.to ?? null}
                        cc={current.cc}
                        bcc={current.bcc}
                        receivedDate={current.receivedDate}
                        dateLabel={current.outbound ? 'Sent' : 'Received'}
                      />
                    </div>
                    {current.emlDocumentId ? (
                      <div className={s.openOriginalRow}>
                        <Button
                          appearance="subtle"
                          size="small"
                          icon={<Open16Regular />}
                          data-testid="reconciliation-open-original-eml"
                          onClick={() =>
                            setOverlayAttachment({
                              attachmentId: 'eml-archive',
                              name: `${current.subject || 'email'}.eml`,
                              documentId: current.emlDocumentId ?? null,
                            })
                          }
                        >
                          Open original email (.eml)
                        </Button>
                      </div>
                    ) : null}
                    {current.triageSummary || current.triagePriority || current.triageCategory ? (
                      <div className={s.triageBox} data-testid="reconciliation-browse-triage">
                        <Text className={s.triageLabel}>TRIAGE</Text>
                        {current.triagePriority || current.triageCategory ? (
                          <Text className={s.triageMeta}>
                            {[current.triagePriority, current.triageCategory].filter(Boolean).join(' · ')}
                          </Text>
                        ) : null}
                        {current.triageSummary ? <Text className={s.triageText}>{current.triageSummary}</Text> : null}
                      </div>
                    ) : null}
                    <EmailBodyView
                      selectedId={current.id}
                      emlDocumentId={current.emlDocumentId}
                      body={current.body ?? ''}
                      attachments={current.attachments}
                      onOpenOriginal={att => setOverlayAttachment(att)}
                      activeCitation={activeCitation}
                      authenticatedFetch={authenticatedFetch}
                    />
                  </div>
                )}
              />
            </div>

            {/* A6 — draggable vertical divider (reused presentational grip). */}
            <PanelSplitter
              onMouseDown={splitterHandlers.onMouseDown}
              onKeyDown={splitterHandlers.onKeyDown}
              onDoubleClick={splitterHandlers.onDoubleClick}
              isDragging={isDragging}
              currentRatio={ratio}
            />

            <div className={s.tabsPane} data-testid="reconciliation-browse-tabs">
              {renderTabs ? (
                renderTabs(current, index)
              ) : (
                <div className={s.tabsPlaceholder} role="note">
                  <Text size={200}>Reconcile tabs (Related to · Fields · Tasks) load here.</Text>
                </div>
              )}
            </div>
          </div>
        ) : (
          <div className={s.emptyState} role="status" data-testid="reconciliation-browse-empty">
            <Text>No emails to review.</Text>
          </div>
        )}
      </SprkModal>

      {/* Open-original overlay — a PreviewModal hosting the reused AttachmentList
          for the chosen attachment; activating the row opens the raw file. */}
      <PreviewModal
        open={overlayAttachment !== null}
        onClose={closeOverlay}
        title={overlayAttachment?.name ?? 'Original'}
        uiScale={uiScale}
      >
        {overlayAttachment ? (
          <AttachmentList
            items={[toAttachmentItem(overlayAttachment)]}
            onActivate={() => {
              if (current) onOpenOriginalActivate?.(overlayAttachment, current);
            }}
          />
        ) : null}
      </PreviewModal>
    </>
  );
};

ReconciliationBrowseShell.displayName = 'ReconciliationBrowseShell';
