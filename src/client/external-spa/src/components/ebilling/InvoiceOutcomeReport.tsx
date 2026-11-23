/**
 * InvoiceOutcomeReport — the firm's on-screen report of an invoice outcome (ebilling-r1 task 134, FR-38 / FR-18 / FR-40, D-66).
 *
 * Renders ONE `FirmOutcome` (`api/ebilling-client.ts`; the server's allow-list, read through the client's own allow-list). It shows
 * only what that object carries and builds no link. It is used in two places: the invoice record's "Outcome" section
 * (`record/sections/OutcomeSection.tsx`) and the rejected-submission result screen (`SubmissionOutcomeReport`, handed to the shared
 * `InvoiceSubmissionOutcome` as its `renderReport`).
 *
 * WHAT IS SHOWN, BY KIND
 *   - Every kind: the summary (tracking number, status, billed total; the approved total only when the outcome has one).
 *   - SubmissionInProgress, Closed: the summary only (the server sends no items for them).
 *   - InvoiceInReview: the summary, the text "Under review", AND the items the server carries (the decided and reviewer-added
 *     lines with their reasons). EB-D-202 supersedes the task's earlier "Under review only"; the client renders what the server returns.
 *   - Rejected, ReturnedToFirm: the items, but NEVER a per-line approved amount (130f K6: nothing is approved by a reject or a return),
 *     and `noticeProse` (the confirmed, firm-safe reason) when present.
 *   - Approved, ApprovedWithAdjustments, SentToAp, Paid: the items and a totals reconciliation.
 *   - SubmissionRejected: the findings that rejected the submission (line number, values read, reason, rule and clause).
 *
 * ITEMS are stacked cards at every width (never a table), lines first (by line number, then date) and invoice-level items after.
 * A citation shows the firm-facing `label`, the rule name, the version, the section reference and the quoted clause; an item with
 * no citation shows its reason only. `noticeProse` is a plain React text node: no HTML is ever interpreted.
 *
 * PRINT (EB-D-254: no PDF in R1). A "Print or save as PDF" button prints a COPY of the whole report in a dedicated surface with no
 * container limits (`outcomePrint.tsx`: a portal root on `<body>`, in a light-theme provider, an `<html>` class while printing, and the
 * one global print rule gated on that class), because the hosts clip the on-screen report (the modal scrolls, the wizard frame has a
 * fixed height). The report's own `@media print` classes lay the copy out: the summary ends the first page when items follow, a card
 * is not split across pages, and the button is not part of the copy.
 *
 * ADR-021: Fluent v9 and semantic tokens only; no hard-coded colour.
 */
import * as React from 'react';
import {
  Badge,
  Button,
  MessageBar,
  MessageBarBody,
  Spinner,
  Text,
  makeStyles,
  mergeClasses,
  tokens,
  type BadgeProps,
} from '@fluentui/react-components';
import { PrintRegular } from '@fluentui/react-icons';
import type {
  FirmOutcome,
  FirmOutcomeCitation,
  FirmOutcomeDecision,
  FirmOutcomeItem,
  FirmOutcomeKind,
} from '../../api/ebilling-client';
import type { InvoiceSubmissionStatusView } from '@spaarke/ui-components/components/InvoiceSubmission';
import { SectionCard } from '../SectionCard';
import { formatDetailValue } from '../detail/RecordDetailFields';
import { useOutcomePrint } from './outcomePrint';
import { useFirmOutcome, type OutcomeLoadState, type OutcomeSubject } from './useFirmOutcome';

/** The class every report carries; the report's own print rules are attached to its classes only. */
export const OUTCOME_REPORT_CLASS = 'sprk-outcome-report';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    minWidth: 0,
    maxWidth: '100%',
    boxSizing: 'border-box',
  },
  summary: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    minWidth: 0,
  },
  // The summary ends the first printed page when items follow; the findings start the next.
  summaryBreak: {
    '@media print': { breakAfter: 'page' },
  },
  facts: {
    display: 'flex',
    flexDirection: 'row',
    flexWrap: 'wrap',
    columnGap: tokens.spacingHorizontalXL,
    rowGap: tokens.spacingVerticalS,
    margin: 0,
  },
  fact: {
    display: 'flex',
    flexDirection: 'column',
    minWidth: 0,
  },
  noMargin: { margin: 0 },
  wrap: { overflowWrap: 'anywhere', minWidth: 0 },
  meta: { color: tokens.colorNeutralForeground3 },
  noticeProse: { whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' },
  actions: {
    display: 'flex',
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalS,
  },
  group: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    minWidth: 0,
  },
  list: {
    listStyleType: 'none',
    margin: 0,
    padding: 0,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  card: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    minWidth: 0,
    paddingBlock: tokens.spacingVerticalS,
    paddingInline: tokens.spacingHorizontalM,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    '@media print': { breakInside: 'avoid' },
  },
  cardHeader: {
    display: 'flex',
    flexDirection: 'row',
    flexWrap: 'wrap',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalS,
    rowGap: tokens.spacingVerticalXS,
    minWidth: 0,
  },
  amounts: {
    display: 'flex',
    flexDirection: 'row',
    flexWrap: 'wrap',
    columnGap: tokens.spacingHorizontalL,
    rowGap: tokens.spacingVerticalXXS,
    margin: 0,
  },
  amount: {
    display: 'flex',
    flexDirection: 'row',
    columnGap: tokens.spacingHorizontalXS,
    minWidth: 0,
  },
  values: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXXS,
    margin: 0,
  },
  citation: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    minWidth: 0,
    marginBlock: tokens.spacingVerticalXS,
    marginInline: 0,
    paddingInlineStart: tokens.spacingHorizontalM,
    borderInlineStartWidth: tokens.strokeWidthThick,
    borderInlineStartStyle: 'solid',
    borderInlineStartColor: tokens.colorNeutralStroke1,
    color: tokens.colorNeutralForeground2,
  },
  quote: {
    margin: 0,
    overflowWrap: 'anywhere',
    fontStyle: 'italic',
  },
  citationMeta: {
    display: 'flex',
    flexDirection: 'row',
    flexWrap: 'wrap',
    columnGap: tokens.spacingHorizontalM,
    color: tokens.colorNeutralForeground3,
  },
  reconciliation: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    margin: 0,
    '@media print': { breakInside: 'avoid' },
  },
  reconciliationRow: {
    display: 'flex',
    flexDirection: 'row',
    justifyContent: 'space-between',
    columnGap: tokens.spacingHorizontalM,
    paddingBlock: tokens.spacingVerticalXXS,
    borderBottom: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
  },
  state: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    alignItems: 'flex-start',
    minWidth: 0,
  },
});

// ---------------------------------------------------------------------------
// Pure helpers (exported for the tests)
// ---------------------------------------------------------------------------

/**
 * An amount as text: two decimals, grouped. The contract carries no currency, so none is shown. It is the SPA's own money
 * formatter (`formatDetailValue`, kind `money`), so the report agrees with the record header's total.
 */
export function formatOutcomeAmount(value: number): string {
  return formatDetailValue({ amount: value }, { attribute: 'amount', kind: 'money' });
}

/** A yyyy-MM-dd calendar date as text; anything else is shown as received. */
export function formatOutcomeDate(date: string): string {
  const parts = /^(\d{4})-(\d{2})-(\d{2})/.exec(date);
  if (!parts) return date;
  try {
    return new Intl.DateTimeFormat('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      timeZone: 'UTC',
    }).format(new Date(Date.UTC(Number(parts[1]), Number(parts[2]) - 1, Number(parts[3]))));
  } catch {
    return date;
  }
}

const DECISION_LABEL: Readonly<Record<FirmOutcomeDecision, string>> = {
  Reduced: 'Reduced',
  Disallowed: 'Disallowed',
  Added: 'Added by reviewer',
  Adjustment: 'Invoice adjustment',
  RejectionFinding: 'Rejected',
};

const DECISION_COLOR: Readonly<Record<FirmOutcomeDecision, BadgeProps['color']>> = {
  Reduced: 'warning',
  Disallowed: 'danger',
  Added: 'informative',
  Adjustment: 'warning',
  RejectionFinding: 'danger',
};

/** Kinds that carry no approval: a per-line approved amount is never shown for them (130f K6). */
const NO_LINE_APPROVAL_KINDS: ReadonlySet<FirmOutcomeKind> = new Set<FirmOutcomeKind>(['Rejected', 'ReturnedToFirm']);

/** Kinds whose totals are reconciled: the invoice was approved. */
const APPROVED_KINDS: ReadonlySet<FirmOutcomeKind> = new Set<FirmOutcomeKind>([
  'Approved',
  'ApprovedWithAdjustments',
  'SentToAp',
  'Paid',
]);

const isInvoiceLevel = (item: FirmOutcomeItem): boolean =>
  item.decision === 'Adjustment' || (item.line === null && item.lineNumber === null);

/**
 * Splits the items into lines (first) and invoice-level items (adjustments and findings about the invoice as a whole), each in
 * a total order: by line number, then by date, then as the server sent them.
 */
export function groupOutcomeItems(items: readonly FirmOutcomeItem[]): {
  lines: FirmOutcomeItem[];
  invoice: FirmOutcomeItem[];
} {
  const indexed = items.map((item, index) => ({ item, index }));
  const key = ({ item, index }: { item: FirmOutcomeItem; index: number }): [number, number, string, number] => [
    item.lineNumber === null ? 1 : 0,
    item.lineNumber ?? 0,
    item.line?.date ?? '9999-99-99',
    index,
  ];
  const compare = (a: (typeof indexed)[number], b: (typeof indexed)[number]): number => {
    const ka = key(a);
    const kb = key(b);
    return ka[0] - kb[0] || ka[1] - kb[1] || (ka[2] < kb[2] ? -1 : ka[2] > kb[2] ? 1 : 0) || ka[3] - kb[3];
  };
  const sorted = [...indexed].sort(compare).map(x => x.item);
  return { lines: sorted.filter(i => !isInvoiceLevel(i)), invoice: sorted.filter(isInvoiceLevel) };
}

/** Billed minus approved, in whole cents (so a float sum never shows a stray fraction). */
export function outcomeDifference(billed: number, approved: number): number {
  return Math.round((billed - approved) * 100) / 100;
}

// ---------------------------------------------------------------------------
// Parts
// ---------------------------------------------------------------------------

const CitationBlock: React.FC<{ citation: FirmOutcomeCitation }> = ({ citation }) => {
  const styles = useStyles();
  const text = (v: string | null): string | null => (v && v.trim() ? v : null);
  const label = text(citation.label);
  const rule = text(citation.ruleName);
  const section = text(citation.sectionReference);
  const quote = text(citation.quote);
  return (
    <div className={styles.citation} data-testid="outcome-citation">
      {label && (
        <Text weight="semibold" className={styles.wrap} data-testid="outcome-citation-label">
          {label}
        </Text>
      )}
      {(rule || citation.version !== null || section) && (
        <div className={styles.citationMeta}>
          {rule && (
            <Text size={200} className={styles.wrap} data-testid="outcome-citation-rule">
              {rule}
            </Text>
          )}
          {citation.version !== null && (
            <Text size={200} data-testid="outcome-citation-version">
              {`Version ${citation.version}`}
            </Text>
          )}
          {section && (
            <Text size={200} className={styles.wrap} data-testid="outcome-citation-section">
              {section}
            </Text>
          )}
        </div>
      )}
      {quote && (
        <blockquote className={styles.quote} data-testid="outcome-citation-quote">
          {quote}
        </blockquote>
      )}
    </div>
  );
};

const ItemCard: React.FC<{ item: FirmOutcomeItem; showLineApproved: boolean }> = ({ item, showLineApproved }) => {
  const styles = useStyles();
  const { line, adjustment } = item;
  const title =
    item.lineNumber !== null
      ? `Line ${item.lineNumber}`
      : line
        ? [line.date ? formatOutcomeDate(line.date) : null, line.timekeeperAsBilled].filter(Boolean).join(' · ')
        : 'Whole invoice';
  const decision = DECISION_LABEL[item.decision] ?? 'Finding';
  const reason = item.reason && item.reason.trim() ? item.reason : null;
  const approved = showLineApproved && line?.approvedAmount != null ? line.approvedAmount : null;
  return (
    <li className={styles.card} data-testid="outcome-item" data-decision={item.decision}>
      <div className={styles.cardHeader}>
        <Badge
          appearance="tint"
          shape="rounded"
          color={DECISION_COLOR[item.decision] ?? 'informative'}
          data-testid="outcome-item-decision"
        >
          {decision}
        </Badge>
        {title && (
          <Text weight="semibold" className={styles.wrap} data-testid="outcome-item-title">
            {title}
          </Text>
        )}
      </div>
      {line?.descriptionExcerpt && (
        <Text className={styles.wrap} data-testid="outcome-item-description">
          {line.descriptionExcerpt}
        </Text>
      )}
      {line && (line.billedAmount !== null || approved !== null) && (
        <dl className={styles.amounts}>
          {line.billedAmount !== null && (
            <div className={styles.amount}>
              <dt className={styles.noMargin}>
                <Text size={200} className={styles.meta}>
                  {item.decision === 'Added' ? 'Amount' : 'Billed'}
                </Text>
              </dt>
              <dd className={styles.noMargin} data-testid="outcome-item-billed">
                <Text size={200}>{formatOutcomeAmount(line.billedAmount)}</Text>
              </dd>
            </div>
          )}
          {approved !== null && (
            <div className={styles.amount}>
              <dt className={styles.noMargin}>
                <Text size={200} className={styles.meta}>
                  Approved
                </Text>
              </dt>
              <dd className={styles.noMargin} data-testid="outcome-item-approved">
                <Text size={200}>{formatOutcomeAmount(approved)}</Text>
              </dd>
            </div>
          )}
        </dl>
      )}
      {adjustment && (
        <dl className={styles.amounts}>
          {adjustment.method && (
            <div className={styles.amount}>
              <dt className={styles.noMargin}>
                <Text size={200} className={styles.meta}>
                  Method
                </Text>
              </dt>
              <dd className={styles.noMargin} data-testid="outcome-item-adjustment-method">
                <Text size={200}>
                  {adjustment.value !== null
                    ? `${adjustment.method} ${adjustment.method === 'Percent' ? `${adjustment.value}%` : formatOutcomeAmount(adjustment.value)}`
                    : adjustment.method}
                </Text>
              </dd>
            </div>
          )}
          {adjustment.amount !== null && (
            <div className={styles.amount}>
              <dt className={styles.noMargin}>
                <Text size={200} className={styles.meta}>
                  Amount
                </Text>
              </dt>
              <dd className={styles.noMargin} data-testid="outcome-item-adjustment-amount">
                <Text size={200}>{formatOutcomeAmount(adjustment.amount)}</Text>
              </dd>
            </div>
          )}
        </dl>
      )}
      {item.valuesRead && item.valuesRead.length > 0 && (
        <dl className={styles.values} aria-label="Values read" data-testid="outcome-item-values">
          {item.valuesRead.map((v, i) => (
            <div key={`${i}:${v.name}`} className={styles.amount}>
              <dt className={styles.noMargin}>
                <Text size={200} className={styles.meta}>
                  {v.name}
                </Text>
              </dt>
              <dd className={mergeClasses(styles.noMargin, styles.wrap)}>
                <Text size={200}>{v.value}</Text>
              </dd>
            </div>
          ))}
        </dl>
      )}
      {reason && (
        <Text className={styles.wrap} data-testid="outcome-item-reason">
          {reason}
        </Text>
      )}
      {item.firmReasons.map((text, i) => (
        <Text
          key={`${i}:${text}`}
          size={200}
          className={mergeClasses(styles.wrap, styles.meta)}
          data-testid="outcome-item-firm-reason"
        >
          {text}
        </Text>
      ))}
      {item.reasonCode && item.reasonCode.trim() && (
        // EB-D-210 / FR-28: every rejecting finding carries its reason code beside the firm-safe description above.
        <Text size={200} className={mergeClasses(styles.wrap, styles.meta)} data-testid="outcome-item-reason-code">
          {`Reason code: ${item.reasonCode}`}
        </Text>
      )}
      {item.citations.map((citation, i) => (
        <CitationBlock key={i} citation={citation} />
      ))}
    </li>
  );
};

const ItemGroup: React.FC<{ heading: string; items: FirmOutcomeItem[]; showLineApproved: boolean; testId: string }> = ({
  heading,
  items,
  showLineApproved,
  testId,
}) => {
  const styles = useStyles();
  if (items.length === 0) return null;
  return (
    <div className={styles.group} data-testid={testId}>
      <Text weight="semibold" as="h3" className={styles.noMargin}>
        {heading}
      </Text>
      <ul className={styles.list} aria-label={heading}>
        {items.map((item, i) => (
          <ItemCard key={i} item={item} showLineApproved={showLineApproved} />
        ))}
      </ul>
    </div>
  );
};

// ---------------------------------------------------------------------------
// The report
// ---------------------------------------------------------------------------

export interface InvoiceOutcomeReportProps {
  outcome: FirmOutcome;
}

/**
 * Renders one outcome. See the file header for what each kind shows. The print button prints a copy of the whole report in the
 * print surface (`outcomePrint.tsx`), never the on-screen element, which its host may clip.
 */
export const InvoiceOutcomeReport: React.FC<InvoiceOutcomeReportProps> = ({ outcome }) => {
  const { start, portal } = useOutcomePrint(() => <ReportContent outcome={outcome} printCopy />);
  return (
    <>
      <ReportContent outcome={outcome} onPrint={start} />
      {portal}
    </>
  );
};

interface ReportContentProps {
  outcome: FirmOutcome;
  /** The on-screen report offers printing; the print copy does not. */
  onPrint?: () => void;
  /** This is the print copy (no button, its own test id). */
  printCopy?: boolean;
}

const ReportContent: React.FC<ReportContentProps> = ({ outcome, onPrint, printCopy = false }) => {
  const styles = useStyles();
  const showLineApproved = !NO_LINE_APPROVAL_KINDS.has(outcome.kind);
  const { lines, invoice } = React.useMemo(() => groupOutcomeItems(outcome.items), [outcome.items]);
  const prose = outcome.noticeProse && outcome.noticeProse.trim() ? outcome.noticeProse : null;
  const reconciled =
    APPROVED_KINDS.has(outcome.kind) && outcome.billedTotal !== null && outcome.approvedTotal !== null
      ? { billed: outcome.billedTotal, approved: outcome.approvedTotal }
      : null;
  const difference = reconciled ? outcomeDifference(reconciled.billed, reconciled.approved) : 0;
  const notShown = outcome.notShownCount !== null && outcome.notShownCount > 0 ? outcome.notShownCount : null;

  return (
    <div
      className={mergeClasses(OUTCOME_REPORT_CLASS, styles.root)}
      data-testid={printCopy ? 'invoice-outcome-report-print' : 'invoice-outcome-report'}
      data-kind={outcome.kind}
    >
      <div
        className={mergeClasses(styles.summary, outcome.items.length > 0 && styles.summaryBreak)}
        data-testid="outcome-summary"
      >
        <dl className={styles.facts}>
          {outcome.trackingNumber && (
            <div className={styles.fact}>
              <dt className={styles.noMargin}>
                <Text size={200} className={styles.meta}>
                  Tracking number
                </Text>
              </dt>
              <dd className={styles.noMargin} data-testid="outcome-tracking">
                <Text weight="semibold" className={styles.wrap}>
                  {outcome.trackingNumber}
                </Text>
              </dd>
            </div>
          )}
          <div className={styles.fact}>
            <dt className={styles.noMargin}>
              <Text size={200} className={styles.meta}>
                Status
              </Text>
            </dt>
            <dd className={styles.noMargin} data-testid="outcome-status">
              <Text weight="semibold">{outcome.statusLabel}</Text>
            </dd>
          </div>
          {outcome.billedTotal !== null && (
            <div className={styles.fact}>
              <dt className={styles.noMargin}>
                <Text size={200} className={styles.meta}>
                  Billed total
                </Text>
              </dt>
              <dd className={styles.noMargin} data-testid="outcome-billed-total">
                <Text weight="semibold">{formatOutcomeAmount(outcome.billedTotal)}</Text>
              </dd>
            </div>
          )}
          {outcome.approvedTotal !== null && (
            <div className={styles.fact}>
              <dt className={styles.noMargin}>
                <Text size={200} className={styles.meta}>
                  Approved total
                </Text>
              </dt>
              <dd className={styles.noMargin} data-testid="outcome-approved-total">
                <Text weight="semibold">{formatOutcomeAmount(outcome.approvedTotal)}</Text>
              </dd>
            </div>
          )}
        </dl>
        {outcome.kind === 'InvoiceInReview' && (
          <Text className={styles.meta} data-testid="outcome-under-review">
            Under review
          </Text>
        )}
        {prose && (
          // Plain text only: the prose is a React text node, never interpreted as markup.
          <Text className={styles.noticeProse} data-testid="outcome-notice-prose">
            {prose}
          </Text>
        )}
        {onPrint && (
          <div className={styles.actions}>
            <Button
              appearance="secondary"
              size="small"
              icon={<PrintRegular />}
              onClick={onPrint}
              data-testid="outcome-print"
            >
              Print or save as PDF
            </Button>
          </div>
        )}
      </div>

      {notShown !== null && (
        <MessageBar intent="warning" data-testid="outcome-not-shown">
          <MessageBarBody>
            {notShown === 1
              ? '1 more finding could not be shown here.'
              : `${notShown} more findings could not be shown here.`}
          </MessageBarBody>
        </MessageBar>
      )}

      <ItemGroup
        heading={outcome.kind === 'SubmissionRejected' ? 'Findings' : 'Lines'}
        items={lines}
        showLineApproved={showLineApproved}
        testId="outcome-lines"
      />
      <ItemGroup
        heading="Whole invoice"
        items={invoice}
        showLineApproved={showLineApproved}
        testId="outcome-invoice-items"
      />

      {reconciled && (
        <dl className={styles.reconciliation} aria-label="Totals" data-testid="outcome-reconciliation">
          <div className={styles.reconciliationRow}>
            <dt className={styles.noMargin}>
              <Text>Billed total</Text>
            </dt>
            <dd className={styles.noMargin} data-testid="outcome-reconciliation-billed">
              <Text>{formatOutcomeAmount(reconciled.billed)}</Text>
            </dd>
          </div>
          {difference !== 0 && (
            <div className={styles.reconciliationRow}>
              <dt className={styles.noMargin}>
                <Text>{difference < 0 ? 'Increase' : 'Reduction'}</Text>
              </dt>
              <dd className={styles.noMargin} data-testid="outcome-reconciliation-difference">
                <Text>{formatOutcomeAmount(Math.abs(difference))}</Text>
              </dd>
            </div>
          )}
          <div className={styles.reconciliationRow}>
            <dt className={styles.noMargin}>
              <Text weight="semibold">Approved total</Text>
            </dt>
            <dd className={styles.noMargin} data-testid="outcome-reconciliation-approved">
              <Text weight="semibold">{formatOutcomeAmount(reconciled.approved)}</Text>
            </dd>
          </div>
        </dl>
      )}
    </div>
  );
};

// ---------------------------------------------------------------------------
// States around the report
// ---------------------------------------------------------------------------

export const OUTCOME_NO_ACCESS_TEXT = "You don't have access to this outcome.";
export const OUTCOME_NOT_FOUND_TEXT = "This outcome isn't available.";
export const OUTCOME_UNAVAILABLE_TEXT = "The outcome couldn't be loaded just now. Please try again.";
export const OUTCOME_ERROR_TEXT = "The outcome couldn't be loaded. Please try again.";

export interface OutcomeBodyProps {
  result: OutcomeLoadState;
  onRetry: () => void;
}

/**
 * The report or the state that stands in for it. The `disabled` state (the eBilling kill switch) renders NOTHING: a caller that
 * owns a container (a section card) checks for it first and drops the container too.
 */
export const OutcomeBody: React.FC<OutcomeBodyProps> = ({ result, onRetry }) => {
  const styles = useStyles();
  switch (result.state) {
    case 'disabled':
      return null;
    case 'loading':
      return (
        <div className={styles.state} data-testid="outcome-loading">
          <Spinner size="small" label="Loading outcome…" labelPosition="after" />
        </div>
      );
    case 'ok':
      return <InvoiceOutcomeReport outcome={result.outcome} />;
    case 'noAccess':
      return (
        <MessageBar intent="warning" data-testid="outcome-no-access">
          <MessageBarBody>{OUTCOME_NO_ACCESS_TEXT}</MessageBarBody>
        </MessageBar>
      );
    case 'notFound':
      return (
        <MessageBar intent="info" data-testid="outcome-not-found">
          <MessageBarBody>{OUTCOME_NOT_FOUND_TEXT}</MessageBarBody>
        </MessageBar>
      );
    case 'unavailable':
    case 'error':
      return (
        <div className={styles.state}>
          <MessageBar
            intent="error"
            data-testid={result.state === 'unavailable' ? 'outcome-unavailable' : 'outcome-error'}
          >
            <MessageBarBody>
              {result.state === 'unavailable' ? OUTCOME_UNAVAILABLE_TEXT : OUTCOME_ERROR_TEXT}
            </MessageBarBody>
          </MessageBar>
          <Button appearance="secondary" size="small" onClick={onRetry} data-testid="outcome-retry">
            Try again
          </Button>
        </div>
      );
  }
};

/**
 * The rejected-submission result screen's report: the SAME report, read from the submission outcome route. Handed to the shared
 * `InvoiceSubmissionOutcome` / wizard as `renderReport`. Renders nothing when eBilling is switched off.
 */
export const SubmissionOutcomeReport: React.FC<{ submissionId: string }> = ({ submissionId }) => {
  const subject = React.useMemo<OutcomeSubject>(() => ({ kind: 'submission', id: submissionId }), [submissionId]);
  const { result, reload } = useFirmOutcome(subject);
  if (result.state === 'disabled') return null;
  return (
    <div data-testid="submission-outcome-report">
      <SectionCard title="Invoice report">
        <OutcomeBody result={result} onRetry={reload} />
      </SectionCard>
    </div>
  );
};

/**
 * The `renderReport` slot of the shared `InvoiceSubmissionOutcome` / `InvoiceSubmissionWizard` (FR-18, FR-38): the same report
 * for a Rejected submission wherever the wizard is hosted (the standalone submission page and the record's modal wizard,
 * `SubmitInvoiceAction`). One definition, so no host shows the shared placeholder instead.
 */
export const renderSubmissionReport = (status: InvoiceSubmissionStatusView): React.ReactNode => (
  <SubmissionOutcomeReport submissionId={status.submissionId} />
);
