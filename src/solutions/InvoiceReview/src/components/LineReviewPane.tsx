/**
 * The right pane's Review tab (ebilling-r1 task 110, prototype LinePanel): for the selected line, its findings (rule, message, suggestion;
 * "Why this was flagged" in a Fluent Accordion), "Your decision" (a Fluent RadioGroup, suggestion first, one Confirm), "What was read"
 * (EvidenceLine rows with Correct), or the decided state (ConsoleKit StatusBar with a statusOverride). With no line selected it shows the
 * invoice-level findings and "Start with the first line to decide".
 *
 * Correct and Reduce are separate affordances (FR-31, prototype answer 3): Correct (✎ on a value) means the READING is wrong and goes to
 * 101's correction route; Reduce is a decision (the BILL is wrong) and goes to 100's decision route. A low-confidence value also gets
 * "Compare with the document, then confirm or correct" from its finding.
 *
 * Nothing is decided here: every choice is sent to the server, and the page shows what the server answers. A missing reason is NOT blocked
 * on the client; the server refuses it (ebilling.line.reason_required) and the refusal is shown.
 */

import * as React from 'react';
import {
  Accordion,
  AccordionHeader,
  AccordionItem,
  AccordionPanel,
  Button,
  Caption1,
  Divider,
  Field,
  Input,
  Link,
  Radio,
  RadioGroup,
  Subtitle2,
  Text,
  Textarea,
  Tooltip,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { Edit16Regular, ErrorCircle16Filled, Info16Regular, PersonWarning16Regular, Warning16Filled } from '@fluentui/react-icons';
import { AiFieldTag, EvidenceLine, StatusBadge, StatusBar, type ConsoleStatusOverride } from '@spaarke/ui-components';
import type { Correction, DecisionAction, ReviewFinding, ReviewLine } from '../services/invoiceReviewApi';
import {
  FIRM_FIRST_WORDS,
  LINE_STATUS,
  fieldLabel,
  findingText,
  hours,
  isCorrectable,
  isDecidable,
  isDecided,
  lineFindingText,
  lineStatusView,
  lineSuggestion,
  money,
  reducedAmount,
  ruleLabel,
  shortDate,
} from '../services/reviewModel';

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL, padding: tokens.spacingHorizontalL },
  head: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  headTitle: { flex: 1, minWidth: 0 },
  muted: { color: tokens.colorNeutralForeground3 },
  section: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS },
  finding: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalS,
    borderLeft: `${tokens.strokeWidthThick} solid ${tokens.colorNeutralStroke2}`,
  },
  findingHead: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalXS, flexWrap: 'wrap' },
  warn: { color: tokens.colorStatusWarningForeground1 },
  critical: { color: tokens.colorStatusDangerForeground1 },
  info: { color: tokens.colorNeutralForeground3 },
  quote: {
    margin: 0,
    paddingLeft: tokens.spacingHorizontalS,
    borderLeft: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke1}`,
    color: tokens.colorNeutralForeground2,
    fontStyle: 'italic',
  },
  fieldRow: { display: 'flex', alignItems: 'flex-start', gap: tokens.spacingHorizontalS },
  fieldBody: { flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXXS },
  fieldLabel: { color: tokens.colorNeutralForeground3, width: '96px', flexShrink: 0, paddingTop: tokens.spacingVerticalXXS },
  optionBody: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    marginLeft: tokens.spacingHorizontalXXL,
    marginBottom: tokens.spacingVerticalS,
  },
  actions: { display: 'flex', gap: tokens.spacingHorizontalS, alignItems: 'center', flexWrap: 'wrap' },
});

/** What the pane asks the page to send. */
export interface PaneDecision {
  action: DecisionAction;
  reason: string | null;
  approvedAmount: number | null;
}

export interface LineReviewPaneProps {
  line: ReviewLine | null;
  invoiceFindings: ReviewFinding[];
  /** Decisions and corrections are offered (flags.canDecide, not refused 403). */
  canWrite: boolean;
  busy: boolean;
  /** The invoice needs its firm first (EB-D-185 / EB-D-197): Confirm (Accept, Reduce, Disallow) and the corrections are disabled. */
  firmNeeded?: boolean;
  /** Correction rows of this line (GET corrections), newest first. */
  corrections: Correction[];
  toDecideCount: number;
  onStart: () => void;
  onDecide: (decision: PaneDecision) => void;
  /** Opens the Correct value dialog for one field. */
  onCorrect: (field: string) => void;
  /** Confirms a value as read (101 `confirm: true`). */
  onConfirm: (field: string) => void;
  /** Boxes a field on the PDF. */
  onShowField: (field: string) => void;
}

function SeverityIcon({ severity }: { severity: string | null }) {
  const s = useStyles();
  if (severity === 'Critical') return <ErrorCircle16Filled className={s.critical} aria-label="Critical" />;
  if (severity === 'Warning') return <Warning16Filled className={s.warn} aria-label="Warning" />;
  return <Info16Regular className={s.info} aria-label="Info" />;
}

/** One policy finding: rule, message, suggestion; "Why this was flagged" discloses version, clause and values read. */
function FindingItem({ f, onCompare }: { f: ReviewFinding; onCompare?: () => void }) {
  const s = useStyles();
  const suggestion = f.suggestedAdjustment;
  return (
    <div className={s.finding} data-testid="finding">
      <div className={s.findingHead}>
        <SeverityIcon severity={f.severity} />
        <Text weight="semibold">{ruleLabel(f)}</Text>
        {f.overridden && <StatusBadge label="Overridden" tone="neutral" />}
      </div>
      <Text>{findingText(f)}</Text>
      {suggestion && (
        <Caption1 className={s.muted}>
          Suggested: {suggestion.kind === 'Reduce' ? `reduce by ${money(suggestion.amount)}` : 'disallow the line'}
        </Caption1>
      )}
      {onCompare && <Link onClick={onCompare}>Compare with the document, then confirm or correct ›</Link>}
      <Accordion collapsible>
        <AccordionItem value="why">
          <AccordionHeader size="small">Why this was flagged</AccordionHeader>
          <AccordionPanel>
            <div className={s.section}>
              <Caption1 className={s.muted}>
                Policy {f.policyCode}
                {f.policyVersionId ? ` · version ${f.policyVersionId.slice(0, 8)}` : ''}
                {f.clauseLocation ? ` · ${f.clauseLocation}` : ''}
              </Caption1>
              {f.clause ? <blockquote className={s.quote}>{f.clause}</blockquote> : <Caption1 className={s.muted}>No clause text was recorded.</Caption1>}
              {f.detailWithheld ? (
                <Caption1 className={s.muted}>The values read are withheld (restricted record).</Caption1>
              ) : (
                f.valuesRead.map((v, i) => (
                  <EvidenceLine
                    key={`${v.name}-${i}`}
                    tier="Fact"
                    text={v.value}
                    subject={v.name}
                    source={`${v.name}${v.belowReliabilityThreshold ? ' (below the reliability threshold)' : ''}`}
                    clause={f.clauseLocation ?? f.policyCode}
                  />
                ))
              )}
            </div>
          </AccordionPanel>
        </AccordionItem>
      </Accordion>
    </div>
  );
}

const DECIDED_OVERRIDE: Readonly<Record<string, ConsoleStatusOverride>> = {
  Accepted: { label: 'Accepted', tone: 'success', description: LINE_STATUS.Accepted.meaning },
  Reduced: { label: 'Reduced', tone: 'warning', description: LINE_STATUS.Reduced.meaning },
  Disallowed: { label: 'Disallowed', tone: 'critical', description: LINE_STATUS.Disallowed.meaning },
};

/** The decided line (StatusBar with the eBilling label, F-3 / task 109). */
function DecidedState({ line }: { line: ReviewLine }) {
  return (
    <StatusBar
      state="Decided"
      statusOverride={DECIDED_OVERRIDE[line.status] ?? { label: lineStatusView(line).label, tone: 'neutral' }}
      decidedBy={line.reviewedBy?.name ?? null}
      decidedOn={line.reviewedOn}
      reason={line.reason}
      note={line.status === 'Reduced' ? `Approved ${money(line.approvedAmount)} of ${money(line.amount)}` : null}
    />
  );
}

type Choice = 'suggested' | 'accept' | 'reduce' | 'disallow';

/** What a decision form is made against: the line, its status and amount, and the suggestion the rules gave. */
function decisionFormKey(line: ReviewLine): string {
  const s = lineSuggestion(line);
  return [line.id, line.status, line.amount, s ? `${s.kind}:${s.amount}` : 'none'].join('|');
}

/** "Your decision": suggestion first, nothing happens until the single Confirm. */
function DecisionForm({ line, busy, firmNeeded, onDecide }: { line: ReviewLine; busy: boolean; firmNeeded: boolean; onDecide: (d: PaneDecision) => void }) {
  const s = useStyles();
  const suggestion = lineSuggestion(line);
  const suggestedAmount = suggestion?.kind === 'Reduce' && suggestion.amount !== null ? reducedAmount(line.amount, suggestion.amount) : null;
  const [choice, setChoice] = React.useState<Choice | ''>(suggestion ? 'suggested' : '');
  const [reason, setReason] = React.useState('');
  const [amountText, setAmountText] = React.useState(suggestedAmount !== null ? suggestedAmount.toFixed(2) : '');

  // "Suggested" with no suggestion (the rules re-ran and the finding is gone) decides nothing: Confirm stays disabled.
  const effective: DecisionAction | null =
    choice === 'suggested' ? (suggestion ? (suggestion.kind === 'Disallow' ? 'disallow' : 'reduce') : null) : choice === '' ? null : choice;

  const confirm = () => {
    if (!effective) return;
    const trimmed = reason.trim();
    if (effective === 'accept') {
      // The server stores no reason for an Accept (it drops it), so none is asked for or sent.
      onDecide({ action: 'accept', reason: null, approvedAmount: null });
      return;
    }
    if (effective === 'disallow') {
      onDecide({ action: 'disallow', reason: trimmed || null, approvedAmount: null });
      return;
    }
    const parsed = Number(amountText.replace(/[, ]/g, ''));
    onDecide({ action: 'reduce', reason: trimmed || null, approvedAmount: amountText.trim() && Number.isFinite(parsed) ? parsed : null });
  };

  const body = (option: Choice) => {
    if (choice !== option || option === 'accept') return null;
    const wantsAmount = option === 'reduce' || (option === 'suggested' && suggestion?.kind === 'Reduce');
    return (
      <div className={s.optionBody}>
        {wantsAmount && (
          <Field label="Approved amount">
            <Input
              value={amountText}
              onChange={(_, d) => setAmountText(d.value)}
              inputMode="decimal"
              aria-label={`Approved amount for line ${line.sequence}`}
            />
          </Field>
        )}
        <Field label="Reason">
          <Textarea value={reason} onChange={(_, d) => setReason(d.value)} resize="vertical" aria-label={`Reason for line ${line.sequence}`} />
        </Field>
      </div>
    );
  };

  return (
    <div className={s.section}>
      <Subtitle2>Your decision</Subtitle2>
      <RadioGroup
        aria-label={`Decision for line ${line.sequence}`}
        name={`decision-${line.id}`}
        value={choice}
        onChange={(_, d) => setChoice(d.value as Choice)}
      >
        {suggestion && (
          <>
            <Radio
              value="suggested"
              label={
                suggestion.kind === 'Reduce'
                  ? `Suggested: reduce to ${money(suggestedAmount)} (−${money(suggestion.amount)})`
                  : 'Suggested: disallow the line'
              }
            />
            {body('suggested')}
          </>
        )}
        <Radio value="accept" label={`Accept at ${money(line.amount)}`} />
        {body('accept')}
        <Radio value="reduce" label="Reduce…" />
        {body('reduce')}
        <Radio value="disallow" label="Disallow" />
        {body('disallow')}
      </RadioGroup>
      {firmNeeded && (
        <Caption1 className={s.muted} data-testid="decision-firm-first">
          {FIRM_FIRST_WORDS}
        </Caption1>
      )}
      <div className={s.actions}>
        <Button appearance="primary" disabled={!effective || busy || firmNeeded} onClick={confirm} data-testid="decision-confirm">
          Confirm
        </Button>
      </div>
    </div>
  );
}

/** The value of a field as the line carries it, formatted. */
function valueOf(line: ReviewLine, field: string): string | null {
  switch (field) {
    case 'Date':
      return line.date ? shortDate(line.date) : null;
    case 'TimekeeperAsBilled':
      return line.timekeeperAsBilled;
    case 'Hours':
    case 'Quantity':
      return line.quantity === null ? null : hours(line.quantity);
    case 'Rate':
    case 'UnitCost':
      return line.rate === null ? null : money(line.rate);
    case 'Amount':
      return money(line.amount);
    case 'Narrative':
      return line.narrative;
    default:
      return null;
  }
}

/** The fields 101 corrects on this line type (InvoiceCorrectionEndpoints: Hours and Rate on fee lines; Amount, Date, Narrative). */
export function correctableFields(line: ReviewLine): string[] {
  if (line.type === 'Fee') return ['Date', 'Hours', 'Rate', 'Amount', 'Narrative'];
  if (line.type === 'Expense') return ['Date', 'Amount', 'Narrative'];
  return [];
}

/** The coded fields 101 corrects (the code must be in the line's code set; the server refuses one that is not). */
export function correctableCodes(line: ReviewLine): Array<{ field: string; label: string; value: string | null }> {
  if (line.type === 'Fee') {
    return [
      { field: 'TaskCode', label: 'Task code', value: line.codes.task.code },
      { field: 'ActivityCode', label: 'Activity code', value: line.codes.activity.code },
    ];
  }
  if (line.type === 'Expense') return [{ field: 'ExpenseCode', label: 'Expense code', value: line.codes.expense.code }];
  return [];
}

function WhatWasRead({
  line,
  canCorrect,
  busy,
  corrections,
  onCorrect,
  onConfirm,
  onShowField,
}: {
  line: ReviewLine;
  canCorrect: boolean;
  busy: boolean;
  corrections: Correction[];
  onCorrect: (field: string) => void;
  onConfirm: (field: string) => void;
  onShowField: (field: string) => void;
}) {
  const s = useStyles();
  const latest = (field: string) => corrections.find(c => c.field === field);
  return (
    <div className={s.section}>
      <Subtitle2>What was read</Subtitle2>
      {correctableFields(line).map(field => {
        const f = line.fields[field];
        const correction = latest(field);
        return (
          <div key={field} className={s.fieldRow} data-testid={`read-${field}`}>
            <Caption1 className={s.fieldLabel}>{fieldLabel(field)}</Caption1>
            <div className={s.fieldBody}>
              <EvidenceLine
                tier="Fact"
                text={valueOf(line, field)}
                subject={fieldLabel(field)}
                source={
                  f?.lowConfidence
                    ? 'Read from the invoice · low confidence, compare with the document'
                    : f?.combined !== null && f?.combined !== undefined
                      ? `Read from the invoice · confidence ${Math.round(f.combined * 100)}%`
                      : 'Read from the invoice'
                }
              />
              {correction && (
                <Caption1 className={s.muted}>
                  {correction.outcome}
                  {correction.aiValue !== null || correction.finalValue !== null ? `: ${correction.aiValue ?? '—'} → ${correction.finalValue ?? '—'}` : ''}
                </Caption1>
              )}
            </div>
            {f?.box && (
              <Button size="small" appearance="subtle" onClick={() => onShowField(field)} aria-label={`Show ${fieldLabel(field)} on the invoice`}>
                Show
              </Button>
            )}
            {canCorrect && (
              <>
                {f?.lowConfidence && (
                  <Button size="small" disabled={busy} onClick={() => onConfirm(field)} data-testid={`confirm-${field}`}>
                    Confirm
                  </Button>
                )}
                <Tooltip content={`Correct ${fieldLabel(field)}: the reading is wrong`} relationship="label">
                  <Button
                    size="small"
                    appearance="subtle"
                    icon={<Edit16Regular />}
                    disabled={busy}
                    onClick={() => onCorrect(field)}
                    data-testid={`correct-${field}`}
                  />
                </Tooltip>
              </>
            )}
          </div>
        );
      })}
      {correctableCodes(line).map(code => (
        <div key={code.field} className={s.fieldRow} data-testid={`read-${code.field}`}>
          <Caption1 className={s.fieldLabel}>{code.label}</Caption1>
          <div className={s.fieldBody}>
            <span>
              <EvidenceLine tier="Interpretation" text={code.value} subject={code.label} source="Coded by AI" /> {code.value && <AiFieldTag />}
            </span>
          </div>
          {canCorrect && (
            <Tooltip content={`Correct ${code.label}`} relationship="label">
              <Button
                size="small"
                appearance="subtle"
                icon={<Edit16Regular />}
                disabled={busy}
                onClick={() => onCorrect(code.field)}
                data-testid={`correct-${code.field}`}
              />
            </Tooltip>
          )}
        </div>
      ))}
    </div>
  );
}

export function LineReviewPane(props: LineReviewPaneProps) {
  const s = useStyles();
  const { line, invoiceFindings, canWrite, busy, firmNeeded = false, corrections, toDecideCount, onStart, onDecide, onCorrect, onConfirm, onShowField } = props;

  if (!line) {
    return (
      <div className={s.root} data-testid="pane-invoice">
        <Subtitle2>Invoice findings</Subtitle2>
        {invoiceFindings.length === 0 ? (
          <Text className={s.muted}>No findings on the invoice as a whole.</Text>
        ) : (
          invoiceFindings.map((f, i) => <FindingItem key={`${f.policyCode}-${f.reasonCode}-${i}`} f={f} />)
        )}
        <Divider />
        {toDecideCount > 0 ? (
          <Button appearance="primary" onClick={onStart} data-testid="start-first">
            Start with the first line to decide
          </Button>
        ) : (
          <Text className={s.muted}>No lines are waiting for a decision.</Text>
        )}
      </div>
    );
  }

  const view = lineStatusView(line);
  const decidable = canWrite && isDecidable(line);
  const correctable = canWrite && isCorrectable(line);
  const lowField = Object.entries(line.fields).find(([name, f]) => f.lowConfidence && correctableFields(line).includes(name))?.[0] ?? null;

  return (
    <div className={s.root} data-testid={`pane-line-${line.id}`}>
      <div className={s.head}>
        <Subtitle2 className={s.headTitle}>
          Line {line.sequence} · {line.type}
        </Subtitle2>
        <StatusBadge label={view.label} tone={view.tone} />
      </div>
      <Caption1 className={s.muted}>
        {shortDate(line.date)} · {line.timekeeper?.name ?? line.timekeeperAsBilled ?? '—'} · {money(line.amount)}
      </Caption1>
      {line.narrative && <Text>{line.narrative}</Text>}
      {line.type === 'Fee' && !line.timekeeper && (
        <div className={s.findingHead}>
          <PersonWarning16Regular className={s.warn} />
          <Caption1>The timekeeper is not on the firm's roster.</Caption1>
        </div>
      )}

      {line.status === 'Pending' && (
        <StatusBar
          state="Open"
          statusOverride={{ label: 'Processing', tone: 'info', description: LINE_STATUS.Pending.meaning }}
          note="This line is still being read or checked. It cannot be decided yet; the page refreshes until it is ready."
        />
      )}

      {(line.findings.length > 0 || line.lineFindings.length > 0) && (
        <div className={s.section}>
          <Subtitle2>What was found</Subtitle2>
          {line.findings.map((f, i) => (
            <FindingItem
              key={`${f.policyCode}-${f.reasonCode}-${i}`}
              f={f}
              onCompare={
                correctable && lowField && f.valuesRead.some(v => v.belowReliabilityThreshold) ? () => onShowField(lowField) : undefined
              }
            />
          ))}
          {line.lineFindings.map((lf, i) => (
            <div key={`${lf.code}-${i}`} className={s.finding} data-testid="line-finding">
              <div className={s.findingHead}>
                <Warning16Filled className={s.warn} />
                <Text>{lineFindingText(lf)}</Text>
              </div>
            </div>
          ))}
        </div>
      )}

      {isDecided(line) && <DecidedState line={line} />}
      {/* Keyed on what the choice was made against: a correction re-runs the rules and can change the status, the amount or the suggestion,
          and a stale "Suggested" choice or amount must not survive that. */}
      {decidable && <DecisionForm key={decisionFormKey(line)} line={line} busy={busy} firmNeeded={firmNeeded} onDecide={onDecide} />}

      <Divider />
      <WhatWasRead
        line={line}
        canCorrect={correctable}
        busy={busy || firmNeeded}
        corrections={corrections}
        onCorrect={onCorrect}
        onConfirm={onConfirm}
        onShowField={onShowField}
      />
    </div>
  );
}
