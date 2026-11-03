/**
 * The Invoice Review workspace (ebilling-r1 task 110; FR-31, FR-32, D-58 layout A): header and totals, count filters, the lines grid (or
 * By rule) on the left, and a resizable right pane with the Review tab (the selected line above the invoice PDF, boxed at the line and
 * value). An action bar carries Accept all clean lines, the ticked-rows actions and the "Decide invoice" slot.
 *
 * Shared components only (ADR-012, task 110 reuse constraint): MetricCardRow fed MetricCardConfig[] (count filters), PanelSplitter +
 * useTwoPanelLayout (the pane), PdfHighlightViewer (108), StatusBar / StatusBadge (109 statusOverride), EvidenceLine, AiFieldTag,
 * FormModal, Fluent Accordion and RadioGroup. Nothing is copied from the prototype.
 *
 * Server truth (ADR-002, I-1): every write is one BFF call; after it succeeds the page re-reads the review and shows the server's totals and
 * statuses. A refused write shows the server's message and changes nothing on the page. A 403 on a write turns the page read-only.
 * While any line is Processing (stored Pending, F-1) the page re-reads the review on a timer and stops when none is.
 *
 * Slots for task 111: `paneTabs` (Context, Related work, History), `headerExtras` (Awaiting approvers), `banners` (the blocked-submission
 * state) and `invoiceActions` (Decide invoice…).
 */

import * as React from 'react';
import {
  Button,
  Caption1,
  Link,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  Spinner,
  Tab,
  TabList,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import {
  MetricCardRow,
  PanelSplitter,
  PdfHighlightViewer,
  StatusBadge,
  useTwoPanelLayout,
  type PdfBytes,
} from '@spaarke/ui-components';
import {
  isIncomplete,
  loadFailureMessage,
  newDecisionId,
  writeFailureMessage,
  type ApiFailure,
  type ApiResult,
  type Correction,
  type InvoiceReview,
  type InvoiceReviewApi,
  type ReviewLine,
} from '../services/invoiceReviewApi';
import {
  CORE_ENTITY,
  CORE_KIND_LABEL,
  buildLineCountCards,
  fieldLabel,
  filterLines,
  highlightFor,
  invoiceStatusView,
  isDecidable,
  money,
  nextToDecide,
  resolveFilterKey,
  reviewAccess,
  shortDate,
  signedMoney,
  type LineFilterKey,
  type RuleGroup,
  type SuggestionPlan,
} from '../services/reviewModel';
import { LinesGrid } from './LinesGrid';
import { LineReviewPane, type PaneDecision } from './LineReviewPane';
import { RuleGroups } from './RuleGroups';
import { BulkDecisionModal, CorrectValueModal, HEADER_FIELDS, type BulkRequest, type CorrectTarget } from './Dialogs';

const useStyles = makeStyles({
  page: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    minHeight: 0,
    backgroundColor: tokens.colorNeutralBackground2,
    color: tokens.colorNeutralForeground1,
  },
  header: {
    display: 'flex',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalL,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    backgroundColor: tokens.colorNeutralBackground1,
    borderBottom: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    flexShrink: 0,
  },
  headMain: { flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXS },
  titleRow: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalM, flexWrap: 'wrap' },
  meta: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalM, flexWrap: 'wrap', color: tokens.colorNeutralForeground3 },
  muted: { color: tokens.colorNeutralForeground3 },
  banners: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    paddingTop: tokens.spacingVerticalS,
    flexShrink: 0,
  },
  strip: {
    display: 'flex',
    alignItems: 'flex-end',
    gap: tokens.spacingHorizontalXXL,
    flexWrap: 'wrap',
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    flexShrink: 0,
  },
  stat: { display: 'flex', flexDirection: 'column' },
  statBig: { fontSize: tokens.fontSizeBase500, lineHeight: tokens.lineHeightBase500, fontWeight: tokens.fontWeightSemibold },
  main: { flex: 1, minHeight: 0, display: 'flex', paddingLeft: tokens.spacingHorizontalXL, paddingRight: tokens.spacingHorizontalXL },
  card: {
    display: 'flex',
    flexDirection: 'column',
    minHeight: 0,
    minWidth: 0,
    backgroundColor: tokens.colorNeutralBackground1,
    borderRadius: tokens.borderRadiusMedium,
    boxShadow: tokens.shadow2,
    overflow: 'hidden',
  },
  toolbar: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderBottom: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    flexWrap: 'wrap',
  },
  cards: { flex: 1, minWidth: 0 },
  paneTabs: { borderBottom: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`, flexShrink: 0, paddingLeft: tokens.spacingHorizontalS },
  paneBody: { flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' },
  paneTop: { flex: '1 1 50%', minHeight: 0, overflowY: 'auto' },
  paneDoc: { flex: '1 1 50%', minHeight: '200px', display: 'flex', flexDirection: 'column', borderTop: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}` },
  paneScroll: { flex: 1, minHeight: 0, overflowY: 'auto' },
  docMessage: { padding: tokens.spacingHorizontalL, color: tokens.colorNeutralForeground3 },
  actionBar: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    marginTop: tokens.spacingVerticalM,
    backgroundColor: tokens.colorNeutralBackground1,
    borderTop: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    flexShrink: 0,
    flexWrap: 'wrap',
  },
  spacer: { flex: 1 },
  centered: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalS,
    height: '100%',
    padding: tokens.spacingHorizontalL,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
});

/** A tab task 111 adds to the pane (Context, Related work, History). */
export interface PaneTabSlot {
  value: string;
  label: string;
  render: (ctx: { review: InvoiceReview; line: ReviewLine | null }) => React.ReactNode;
}

/** What task 111's slots receive. */
export interface ReviewSlotContext {
  review: InvoiceReview;
  canWrite: boolean;
  /** Re-read the review (after an invoice decision). */
  reload: () => Promise<unknown>;
}

export interface InvoiceReviewPageProps {
  invoiceId: string;
  api: InvoiceReviewApi;
  /** Opens the core record (Xrm navigation in the host). */
  openRecord?: (entityName: string, id: string) => void;
  /** Re-read interval while any line is Processing. Default 5 s. */
  pollIntervalMs?: number;
  paneTabs?: PaneTabSlot[];
  headerExtras?: (ctx: ReviewSlotContext) => React.ReactNode;
  banners?: (ctx: ReviewSlotContext) => React.ReactNode;
  invoiceActions?: (ctx: ReviewSlotContext) => React.ReactNode;
}

type View = 'lines' | 'rules';

interface Notice {
  intent: 'success' | 'error' | 'info';
  text: string;
  /**
   * Repeat: the same request again (503 incomplete: the change was saved, a later step did not finish; the same decision id records
   * nothing twice). Reload: re-read the review (409: the invoice or line changed under the user).
   */
  action?: { label: 'Repeat' | 'Reload'; run: () => void };
}

/** The field's editor value in the wire format 101 reads (invariant number, yyyy-mm-dd, text). */
function wireValue(line: ReviewLine, field: string): string {
  switch (field) {
    case 'Date':
      return line.date ?? '';
    case 'Hours':
      return line.quantity === null ? '' : String(line.quantity);
    case 'Rate':
      return line.rate === null ? '' : String(line.rate);
    case 'Amount':
      return String(line.amount);
    case 'Narrative':
      return line.narrative ?? '';
    case 'TaskCode':
      return line.codes.task.code ?? '';
    case 'ActivityCode':
      return line.codes.activity.code ?? '';
    case 'ExpenseCode':
      return line.codes.expense.code ?? '';
    default:
      return '';
  }
}

const CODE_LABEL: Readonly<Record<string, string>> = { TaskCode: 'Task code', ActivityCode: 'Activity code', ExpenseCode: 'Expense code' };

function decisionWords(d: PaneDecision, line: ReviewLine): string {
  if (d.action === 'accept') return `Line ${line.sequence} accepted`;
  if (d.action === 'disallow') return `Line ${line.sequence} disallowed`;
  return `Line ${line.sequence} reduced to ${money(d.approvedAmount)}`;
}

function Stat({ label, value, big, testId }: { label: string; value: string; big?: boolean; testId: string }) {
  const s = useStyles();
  return (
    <div className={s.stat}>
      <Caption1 className={s.muted}>{label}</Caption1>
      <Text className={big ? s.statBig : undefined} data-testid={testId}>
        {value}
      </Text>
    </div>
  );
}

export function InvoiceReviewPage(props: InvoiceReviewPageProps) {
  const { invoiceId, api, openRecord, pollIntervalMs = 5000, paneTabs = [], headerExtras, banners, invoiceActions } = props;
  const s = useStyles();

  const [review, setReview] = React.useState<InvoiceReview | null>(null);
  const [loadFailure, setLoadFailure] = React.useState<ApiFailure | null>(null);
  const [corrections, setCorrections] = React.useState<Correction[]>([]);
  const [refused403, setRefused403] = React.useState(false);
  const [busy, setBusy] = React.useState(false);
  const [notice, setNotice] = React.useState<Notice | null>(null);
  const [filter, setFilter] = React.useState<LineFilterKey>('all');
  const [view, setView] = React.useState<View>('lines');
  const [tab, setTab] = React.useState('review');
  const [checked, setChecked] = React.useState<Set<string>>(new Set());
  const [selectedLineId, setSelectedLineId] = React.useState<string | null>(null);
  const [selectedField, setSelectedField] = React.useState<string | null>(null);
  const [correct, setCorrect] = React.useState<CorrectTarget | null>(null);
  const [bulk, setBulk] = React.useState<BulkRequest | null>(null);
  const [dialogError, setDialogError] = React.useState<string | null>(null);

  const panes = useTwoPanelLayout({ defaultDetailWidth: 560, minPrimaryWidth: 520, minDetailWidth: 420, storageKey: 'sprk-invoice-review-pane' });

  const alive = React.useRef(true);
  /** A review has been shown: a later failed re-read keeps it (and says so) instead of replacing the page with an error. */
  const loaded = React.useRef(false);
  React.useEffect(() => {
    alive.current = true;
    return () => {
      alive.current = false;
    };
  }, []);

  /** Re-read the review (and the corrections). A failed re-read keeps what is shown and says so; a failed first read is the page's error. */
  const reload = React.useCallback(async (): Promise<InvoiceReview | null> => {
    const [r, c] = await Promise.all([api.getReview(invoiceId), api.listCorrections(invoiceId)]);
    if (!alive.current) return null;
    // The corrections list feeds "Corrected from" notes only; its failure never blocks the page.
    if (c.ok) setCorrections(c.value);
    if (r.ok) {
      loaded.current = true;
      setReview(r.value);
      setLoadFailure(null);
      return r.value;
    }
    if (loaded.current) setNotice({ intent: 'error', text: loadFailureMessage(r.failure) });
    else setLoadFailure(r.failure);
    return null;
  }, [api, invoiceId]);

  React.useEffect(() => {
    void reload();
  }, [reload]);

  // F-1: re-read while any line is Processing; stop when none is.
  const processing = review?.lines.some(l => l.status === 'Pending') ?? false;
  React.useEffect(() => {
    if (!processing) return undefined;
    const t = setTimeout(() => void reload(), pollIntervalMs);
    return () => clearTimeout(t);
  }, [processing, review, reload, pollIntervalMs]);

  const lines = React.useMemo(() => (review ? [...review.lines].sort((a, b) => a.sequence - b.sequence) : []), [review]);
  const access = review ? reviewAccess(review, refused403) : { canWrite: false, reason: null };
  const canWrite = access.canWrite;
  const resolvedFilter = resolveFilterKey(lines, filter);
  const shown = React.useMemo(() => filterLines(lines, resolvedFilter), [lines, resolvedFilter]);
  const selectedLine = lines.find(l => l.id === selectedLineId) ?? null;
  const cleanCount = lines.filter(l => l.status === 'Clean' && l.type !== 'Adjustment').length;
  const toDecideCount = lines.filter(l => l.status === 'Flagged').length;
  const checkedIds = [...checked].filter(id => lines.some(l => l.id === id && isDecidable(l)));

  const cards = React.useMemo(
    () => buildLineCountCards({ lines, selectedKey: resolvedFilter, onSelectKey: setFilter }),
    [lines, resolvedFilter]
  );

  const onCheck = React.useCallback(
    (ids: string[], on: boolean) =>
      setChecked(prev => {
        const n = new Set(prev);
        ids.forEach(id => (on ? n.add(id) : n.delete(id)));
        return n;
      }),
    []
  );

  const onSelect = React.useCallback((lineId: string, field: string | null) => {
    setSelectedLineId(lineId);
    setSelectedField(field);
    setTab('review');
  }, []);

  /**
   * One write: busy, the call, then the server's state. A refusal changes nothing on the page; a 403 turns it read-only; a 503 incomplete
   * offers Repeat with the SAME request (same decisionId), which finishes it without a second record.
   */
  const runWrite = React.useCallback(
    async <T,>(
      call: () => Promise<ApiResult<T>>,
      opts: {
        success: string;
        /** The write was sent from a dialog: an ordinary refusal stays in the dialog. */
        inDialog?: boolean;
        /** Closes that dialog: on a 403 or a 503 incomplete the outcome belongs to the page, not the form. */
        closeDialog?: () => void;
        after?: (value: T, fresh: InvoiceReview | null) => void;
      }
    ): Promise<boolean> => {
      setBusy(true);
      setDialogError(null);
      const result = await call();
      if (!alive.current) return false;
      if (result.ok) {
        const fresh = await reload();
        if (!alive.current) return false;
        setBusy(false);
        setNotice({ intent: 'success', text: opts.success });
        opts.after?.(result.value, fresh);
        return true;
      }
      setBusy(false);
      const f = result.failure;
      if (f.status === 403) setRefused403(true);
      const text = writeFailureMessage(f);
      const incomplete = isIncomplete(f);
      if (opts.inDialog && !incomplete && f.status !== 403) {
        setDialogError(text);
        return false;
      }
      opts.closeDialog?.();
      const action: Notice['action'] = incomplete
        ? { label: 'Repeat', run: () => void runWrite(call, { ...opts, inDialog: false }) }
        : f.status === 409
          ? { label: 'Reload', run: () => void reload() }
          : undefined;
      setNotice({ intent: 'error', text, action });
      return false;
    },
    [reload]
  );

  // ── Writes ────────────────────────────────────────────────────────────────

  const decide = (line: ReviewLine, d: PaneDecision) => {
    const decisionId = newDecisionId();
    void runWrite(() => api.decideLine(invoiceId, line.id, { decisionId, ...d }), {
      success: `Saved · ${decisionWords(d, line)}`,
      after: (_, fresh) => {
        // Move on to the next line to decide, from the server's fresh state.
        const next = fresh ? nextToDecide(fresh.lines, line.id) : null;
        setSelectedLineId(next?.id ?? null);
        setSelectedField(null);
      },
    });
  };

  const acceptClean = () => {
    const decisionId = newDecisionId();
    void runWrite(() => api.acceptClean(invoiceId, decisionId), {
      success: 'Saved · Clean lines accepted',
    });
  };

  const submitBulk = (request: BulkRequest, reason: string | null) => {
    const decisionId = newDecisionId();
    void runWrite(
      () =>
        api.decideLines(invoiceId, {
          decisionId,
          action: request.action,
          reason,
          lines: request.lines.map(l => ({ lineId: l.lineId, approvedAmount: request.action === 'reduce' ? l.approvedAmount : null })),
        }),
      {
        success: `Saved · ${request.lines.length} ${request.lines.length === 1 ? 'line' : 'lines'} ${request.action === 'accept' ? 'accepted' : request.action === 'disallow' ? 'disallowed' : 'reduced'}`,
        inDialog: true,
        closeDialog: () => setBulk(null),
        after: () => {
          setBulk(null);
          setChecked(prev => new Set([...prev].filter(id => !request.lines.some(l => l.lineId === id))));
        },
      }
    );
  };

  const submitCorrection = (target: CorrectTarget, field: string, value: string) => {
    const request = { field, value, confirm: false };
    const label = target.lineId ? target.label : (HEADER_FIELDS.find(h => h.field === field)?.label ?? field);
    void runWrite(
      () => (target.lineId ? api.correctLine(invoiceId, target.lineId, request) : api.correctHeader(invoiceId, request)),
      {
        success: `Saved · ${label} corrected${target.lineSequence ? ` on line ${target.lineSequence}` : ''}; the rules ran again`,
        inDialog: true,
        closeDialog: () => setCorrect(null),
        after: () => setCorrect(null),
      }
    );
  };

  const confirmValue = (line: ReviewLine, field: string) => {
    void runWrite(() => api.correctLine(invoiceId, line.id, { field, value: null, confirm: true }), {
      success: `Saved · ${fieldLabel(field)} on line ${line.sequence} confirmed as read`,
    });
  };

  const openCorrect = (line: ReviewLine, field: string) => {
    setDialogError(null);
    const label = CODE_LABEL[field] ?? fieldLabel(field);
    setCorrect({
      lineId: line.id,
      lineSequence: line.sequence,
      field,
      label,
      asRead: wireValue(line, field) || null,
      initial: wireValue(line, field),
    });
    if (line.fields[field]?.box) setSelectedField(field);
  };

  const openHeaderCorrect = () => {
    setDialogError(null);
    setCorrect({ lineId: null, lineSequence: null, field: HEADER_FIELDS[0].field, label: HEADER_FIELDS[0].label, asRead: null, initial: '' });
  };

  const openBulk = (action: 'accept' | 'disallow') => {
    setDialogError(null);
    const picked = lines.filter(l => checkedIds.includes(l.id));
    setBulk({
      action,
      title: `${action === 'accept' ? 'Accept' : 'Disallow'} ${picked.length} selected ${picked.length === 1 ? 'line' : 'lines'}`,
      lines: picked.map(l => ({ lineId: l.id, sequence: l.sequence, billed: l.amount, approvedAmount: null })),
    });
  };

  const openApply = (group: RuleGroup, plan: SuggestionPlan) => {
    setDialogError(null);
    setBulk({
      action: plan.action,
      title: `Apply the ${group.label} suggestion to ${plan.lines.length} ${plan.lines.length === 1 ? 'line' : 'lines'}`,
      lines: plan.lines.map(pl => {
        const l = lines.find(x => x.id === pl.lineId)!;
        return { lineId: pl.lineId, sequence: l.sequence, billed: l.amount, approvedAmount: pl.approvedAmount };
      }),
      skipped: plan.skipped,
    });
  };

  // ── The PDF ───────────────────────────────────────────────────────────────

  const pdfFileId = review?.invoicePdfFileId ?? null;
  const loadPdf = React.useCallback(async (): Promise<PdfBytes> => {
    if (!pdfFileId) throw new Error('No invoice PDF.');
    const r = await api.fetchFile(pdfFileId);
    if (!r.ok) throw new Error(writeFailureMessage(r.failure));
    return r.value;
  }, [api, pdfFileId]);
  const highlight = React.useMemo(() => highlightFor(selectedLine, selectedField), [selectedLine, selectedField]);

  // ── Render ────────────────────────────────────────────────────────────────

  if (!review) {
    if (loadFailure) {
      return (
        <div className={s.centered} data-testid="review-load-error">
          <Text weight="semibold">Unable to open the invoice</Text>
          <Text>{loadFailureMessage(loadFailure)}</Text>
          {loadFailure.errorCode !== 'ebilling.disabled' && (
            <Button appearance="primary" onClick={() => void reload()}>
              Retry
            </Button>
          )}
        </div>
      );
    }
    return (
      <div className={s.centered} data-testid="review-loading">
        <Spinner label="Loading the invoice…" />
      </div>
    );
  }

  const h = review.header;
  const status = invoiceStatusView(h);
  const slotCtx: ReviewSlotContext = { review, canWrite, reload };
  const core = h.coreRecord;
  const totals = review.totals;
  const activeTab = tab === 'review' || paneTabs.some(t => t.value === tab) ? tab : 'review';

  const documentView = !pdfFileId ? (
    <div className={s.docMessage} data-testid="no-pdf">
      This invoice has no PDF that can be shown yet.
    </div>
  ) : (
    <PdfHighlightViewer
      loadData={loadPdf}
      documentName={h.firmInvoiceNumber ? `Invoice ${h.firmInvoiceNumber}` : 'Invoice'}
      highlight={highlight}
      ariaLabel="Invoice document"
    />
  );

  return (
    <div className={s.page} data-testid="invoice-review">
      <header className={s.header}>
        <div className={s.headMain}>
          <div className={s.titleRow}>
            <Text size={500} weight="semibold" data-testid="review-title">
              {h.firm?.name ?? 'Unknown firm'} · Invoice {h.firmInvoiceNumber ?? '—'}
            </Text>
            {h.trackingNumber && <Caption1 className={s.muted}>{h.trackingNumber}</Caption1>}
            <StatusBadge label={status.label} tone={status.tone} />
          </div>
          <div className={s.meta}>
            {core && (
              <Link
                onClick={() => openRecord?.(CORE_ENTITY[core.type] ?? core.type, core.id)}
                disabled={!openRecord || !CORE_ENTITY[core.type]}
                data-testid="core-link"
              >
                {CORE_KIND_LABEL[core.type] ?? core.type}: {core.name ?? core.id} ›
              </Link>
            )}
            <Caption1>
              Period {shortDate(h.periodStart)} – {shortDate(h.periodEnd)}
            </Caption1>
            <Caption1>Invoice date {shortDate(h.invoiceDate)}</Caption1>
            {headerExtras?.(slotCtx)}
          </div>
        </div>
        {canWrite && (
          <Button size="small" appearance="subtle" onClick={openHeaderCorrect} disabled={busy} data-testid="correct-header">
            Correct an invoice value…
          </Button>
        )}
      </header>

      <div className={s.banners}>
        {banners?.(slotCtx)}
        {access.reason && (
          <MessageBar intent="info" data-testid="readonly-notice">
            <MessageBarBody>{access.reason}</MessageBarBody>
          </MessageBar>
        )}
        {notice && (
          <MessageBar intent={notice.intent} data-testid="notice" data-intent={notice.intent}>
            <MessageBarBody>{notice.text}</MessageBarBody>
            <MessageBarActions
              containerAction={
                <Button appearance="transparent" size="small" aria-label="Dismiss" onClick={() => setNotice(null)}>
                  ×
                </Button>
              }
            >
              {notice.action && (
                <Button size="small" onClick={notice.action.run} disabled={busy} data-testid={`notice-${notice.action.label.toLowerCase()}`}>
                  {notice.action.label}
                </Button>
              )}
            </MessageBarActions>
          </MessageBar>
        )}
      </div>

      <div className={s.strip} aria-label="Totals">
        <Stat label="Billed" value={money(totals.billed)} testId="total-billed" />
        <Stat label="Line adjustments" value={signedMoney(totals.lineAdjustments)} testId="total-line-adj" />
        <Stat label="Invoice adjustments" value={signedMoney(totals.invoiceAdjustments)} testId="total-invoice-adj" />
        <Stat label="Approved total" value={money(totals.approved)} big testId="total-approved" />
      </div>

      <div className={s.main} ref={panes.containerRef as React.RefObject<HTMLDivElement>}>
        <div style={{ width: panes.primaryWidth, display: 'flex', minWidth: 0 }}>
          <div className={s.card} style={{ flex: 1 }}>
            <div className={s.toolbar}>
              <div className={s.cards}>
                <MetricCardRow layout="wide" ariaLabel="Line filters" cards={cards} />
              </div>
              {resolvedFilter !== 'all' && (
                <Caption1 className={s.muted}>
                  Showing {shown.length} of {lines.length} · <Link onClick={() => setFilter('all')}>Show all</Link>
                </Caption1>
              )}
              <TabList size="small" selectedValue={view} onTabSelect={(_, d) => setView(d.value as View)} aria-label="View">
                <Tab value="lines">Lines</Tab>
                <Tab value="rules" data-testid="tab-by-rule">
                  By rule
                </Tab>
              </TabList>
            </div>
            {view === 'lines' ? (
              <LinesGrid
                lines={shown}
                checked={checked}
                onCheck={onCheck}
                canTick={canWrite}
                selectedLineId={selectedLineId}
                selectedField={selectedField}
                onSelect={onSelect}
              />
            ) : (
              <RuleGroups lines={shown} canWrite={canWrite} busy={busy} onSelectLine={id => onSelect(id, null)} onApply={openApply} />
            )}
          </div>
        </div>
        <PanelSplitter {...panes.splitterHandlers} isDragging={panes.isDragging} currentRatio={panes.currentRatio} />
        <div className={s.card} style={{ width: panes.detailWidth }}>
          <TabList className={s.paneTabs} size="small" selectedValue={activeTab} onTabSelect={(_, d) => setTab(String(d.value))}>
            <Tab value="review">Review</Tab>
            {paneTabs.map(t => (
              <Tab key={t.value} value={t.value}>
                {t.label}
              </Tab>
            ))}
          </TabList>
          {activeTab === 'review' ? (
            <div className={s.paneBody}>
              <div className={s.paneTop}>
                <LineReviewPane
                  line={selectedLine}
                  invoiceFindings={review.invoiceFindings}
                  canWrite={canWrite}
                  busy={busy}
                  corrections={selectedLine ? corrections.filter(c => c.lineId === selectedLine.id) : []}
                  toDecideCount={toDecideCount}
                  onStart={() => {
                    const first = nextToDecide(lines, null);
                    if (first) onSelect(first.id, null);
                  }}
                  onDecide={d => selectedLine && decide(selectedLine, d)}
                  onCorrect={field => selectedLine && openCorrect(selectedLine, field)}
                  onConfirm={field => selectedLine && confirmValue(selectedLine, field)}
                  onShowField={field => setSelectedField(field)}
                />
              </div>
              <div className={s.paneDoc}>{documentView}</div>
            </div>
          ) : (
            <div className={s.paneScroll}>{paneTabs.find(t => t.value === activeTab)?.render({ review, line: selectedLine })}</div>
          )}
        </div>
      </div>

      <footer className={s.actionBar}>
        {canWrite && checkedIds.length > 0 ? (
          <>
            <Text weight="semibold">{checkedIds.length} selected</Text>
            <Button onClick={() => openBulk('accept')} disabled={busy} data-testid="bulk-accept">
              Accept…
            </Button>
            <Button onClick={() => openBulk('disallow')} disabled={busy} data-testid="bulk-disallow">
              Disallow…
            </Button>
            <Button appearance="subtle" onClick={() => setChecked(new Set())}>
              Clear selection
            </Button>
          </>
        ) : canWrite ? (
          <Button disabled={busy || cleanCount === 0} onClick={acceptClean} data-testid="accept-clean">
            Accept all clean lines ({cleanCount})
          </Button>
        ) : null}
        {busy && <Spinner size="tiny" label="Saving…" />}
        <div className={s.spacer} />
        <Caption1 className={s.muted} data-testid="progress">
          {toDecideCount} to decide
          {processing ? ` · ${lines.filter(l => l.status === 'Pending').length} processing` : ''}
        </Caption1>
        {invoiceActions?.(slotCtx)}
      </footer>

      {correct && (
        <CorrectValueModal
          target={correct}
          busy={busy}
          error={dialogError}
          onClose={() => setCorrect(null)}
          onSubmit={(field, value) => submitCorrection(correct, field, value)}
        />
      )}
      {bulk && (
        <BulkDecisionModal
          request={bulk}
          busy={busy}
          error={dialogError}
          onClose={() => setBulk(null)}
          onSubmit={reason => submitBulk(bulk, reason)}
        />
      )}
    </div>
  );
}
