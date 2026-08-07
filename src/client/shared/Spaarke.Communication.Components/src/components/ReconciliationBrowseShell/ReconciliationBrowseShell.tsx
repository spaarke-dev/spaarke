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
import { makeStyles, tokens, Text } from '@fluentui/react-components';
import { SprkModal, PreviewModal } from '@spaarke/ui-components';
import { EmailReadingPaneShell } from '../EmailReadingPaneShell';
import { EmailReadingHeader } from '../EmailReadingHeader';
import { EmailRecipients } from '../EmailRecipients';
import { EmailBodyView } from '../EmailBody';
import { AttachmentList } from '../AttachmentList';
import type { IAttachmentItem } from '../../logic/attachments';
import type { ReconciliationAttachmentContent } from '../EmailBody/EmailBodyView.types';
import type { ReconciliationBrowseRecord, ReconciliationBrowseShellProps } from './ReconciliationBrowseShell.types';

const useStyles = makeStyles({
  // Two-pane body filling the SprkModal (unpadded) `lg`/landscape stage: reader
  // left (1fr), reconcile tabs right (clamped). `minHeight: 0` lets each pane
  // scroll independently inside the modal's height cap.
  twoPane: {
    display: 'grid',
    gridTemplateColumns: '1fr minmax(360px, 480px)',
    height: '100%',
    minHeight: 0,
    width: '100%',
  },
  readerPane: {
    position: 'relative',
    display: 'flex',
    minWidth: 0,
    minHeight: 0,
    height: '100%',
    overflow: 'hidden',
    borderRightWidth: tokens.strokeWidthThin,
    borderRightStyle: 'solid',
    borderRightColor: tokens.colorNeutralStroke2,
  },
  tabsPane: {
    display: 'flex',
    flexDirection: 'column',
    minWidth: 0,
    minHeight: 0,
    height: '100%',
    overflowY: 'auto',
    backgroundColor: tokens.colorNeutralBackground1,
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
  bodySubject: {
    paddingInline: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalXS,
  },
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
        size="lg"
        layout="landscape"
        padded={false}
        uiScale={uiScale}
        nav={{ index, total: queue.length, onNavigate: handleNavigate }}
      >
        {current ? (
          <div className={s.twoPane} data-testid="reconciliation-browse-two-pane">
            <div className={s.readerPane} data-testid="reconciliation-browse-reader">
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
                      />
                    </div>
                    <Text
                      as="h2"
                      className={s.bodySubject}
                      data-testid="reconciliation-browse-subject"
                      truncate
                      wrap={false}
                    >
                      {current.subject || '(no subject)'}
                    </Text>
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
