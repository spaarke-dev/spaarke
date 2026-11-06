import {
  approvalSignature,
  correctionBadge,
  correctionTitle,
  historyItems,
  approveBlocker,
  firmNeededBlocker,
  awaitingLine,
  decisionAccess,
  invoiceDecisionRecord,
  outcomeOfApproval,
  recordBadge,
  recordTitle,
  relatedWorkEmptyText,
  sortNewestFirst,
} from '../services/decisionModel';
import { adjustmentValueError } from '../components/decision/steps';
import { approval, decisionRecord, flagged, line, lineId, review } from './testKit';

const pending = () => review([line(1)]);

describe('decisionAccess: actions come only from the server signals', () => {
  it('offers Decide to an eligible caller on a pending, not-held, In Review invoice with Write', () => {
    expect(decisionAccess(pending(), approval(), true)).toBe('decide');
  });

  it('offers nothing before the approval state is known', () => {
    expect(decisionAccess(pending(), null, true)).toBe('none');
  });

  it('offers nothing to a caller who is not an eligible approver, even when the invoice is pending', () => {
    expect(decisionAccess(pending(), approval({ callerEligible: false, canApprove: false }), true)).toBe('none');
  });

  it('offers nothing without Write, when held, or when the invoice is not In Review', () => {
    expect(decisionAccess(pending(), approval(), false)).toBe('none');
    expect(decisionAccess(pending(), approval({ held: true }), true)).toBe('none');
    const heldReview = review([line(1)]);
    heldReview.header.held = true;
    expect(decisionAccess(heldReview, approval(), true)).toBe('none');
    const closed = review([line(1)]);
    closed.header.status = 'Approved';
    expect(decisionAccess(closed, approval(), true)).toBe('none');
  });

  it('offers to view a decided invoice, to anyone who can read it', () => {
    for (const status of ['Approved', 'Rejected', 'Returned']) {
      expect(decisionAccess(pending(), approval({ status, callerEligible: false, canApprove: false }), false)).toBe('view');
    }
    expect(decisionAccess(pending(), approval({ status: 'Unknown' }), true)).toBe('none');
    expect(decisionAccess(pending(), approval({ status: 'None' }), true)).toBe('none');
  });
});

describe('approveBlocker', () => {
  it('is null when the server says the caller can approve', () => {
    expect(approveBlocker(approval())).toBeNull();
  });

  it('names the open counts first (I-2)', () => {
    expect(approveBlocker(approval({ canApprove: false, openLines: { pending: 0, flagged: 2 } }))).toBe('Not yet: 2 lines to decide.');
    expect(approveBlocker(approval({ canApprove: false, openLines: { pending: 1, flagged: 1 } }))).toBe('Not yet: 1 line to decide, 1 still processing.');
  });

  it('names the firm first when the invoice needs one (EB-D-190), ahead of the open counts', () => {
    expect(approveBlocker(approval({ canApprove: false, firmNeeded: true, openLines: { pending: 0, flagged: 2 } }))).toContain('needs its firm first');
    expect(firmNeededBlocker(approval({ firmNeeded: true }))).toContain('needs its firm first');
    expect(firmNeededBlocker(approval({ firmNeeded: false }))).toBeNull();
    expect(firmNeededBlocker(approval())).toBeNull();
  });

  it('then the hold, the missing approver, and a generic server refusal', () => {
    expect(approveBlocker(approval({ canApprove: false, held: true }))).toContain('held');
    expect(approveBlocker(approval({ canApprove: false, noEligibleApprover: true }))).toContain('No eligible approver');
    expect(approveBlocker(approval({ canApprove: false }))).toContain('Not available right now');
  });
});

describe('awaitingLine', () => {
  it('lists the approvers a pending invoice waits for, at most four names then "and n more"', () => {
    expect(awaitingLine(approval())?.text).toBe('Awaiting: Pat Approver, Sam Second');
    expect(awaitingLine(approval({ eligibleApprovers: ['A', 'B', 'C', 'D', 'E', 'F'] }))?.text).toBe('Awaiting: A, B, C, D and 2 more');
  });

  it('flags an invoice nobody may approve (zero approvers, FR-14) as a warning', () => {
    const none = awaitingLine(approval({ eligibleApprovers: [], noEligibleApprover: true, canApprove: false }));
    expect(none).toEqual({ tone: 'warning', text: expect.stringContaining('No eligible approver') });
    expect(awaitingLine(approval({ eligibleApprovers: [] }))?.tone).toBe('warning');
  });

  it('warns when eligibility could not be resolved, and when the invoice is held', () => {
    expect(awaitingLine(approval({ eligibilityUnavailable: true }))?.tone).toBe('warning');
    expect(awaitingLine(approval({ held: true }))?.text).toContain('On hold');
  });

  it('says how a decided invoice closed, and nothing for an unknown state', () => {
    expect(awaitingLine(approval({ status: 'Approved', decidedBy: 'Pat Approver' }))).toEqual({ tone: 'neutral', text: 'Approved by Pat Approver' });
    expect(awaitingLine(approval({ status: 'Returned' }))?.text).toBe('Returned to firm');
    expect(awaitingLine(approval({ status: 'Unknown' }))).toBeNull();
    expect(awaitingLine(null)).toBeNull();
  });
});

describe('approvalSignature', () => {
  it('changes when a line status, the totals, the hold or the history change, and not otherwise', () => {
    const a = review([line(1), flagged(2)]);
    const same = review([line(1), flagged(2)]);
    expect(approvalSignature(a)).toBe(approvalSignature(same));
    expect(approvalSignature(review([line(1), line(2)]))).not.toBe(approvalSignature(a));
    expect(approvalSignature(review([line(1), flagged(2)], { totals: { ...a.totals, invoiceAdjustments: -250, approved: 4750 } }))).not.toBe(approvalSignature(a));
    const held = review([line(1), flagged(2)]);
    held.header.held = true;
    expect(approvalSignature(held)).not.toBe(approvalSignature(a));
    expect(approvalSignature(review([line(1), flagged(2)], { decisionHistory: [decisionRecord()] }))).not.toBe(approvalSignature(a));
  });
});

describe('the recorded invoice decision', () => {
  it('maps the approval status to an outcome', () => {
    expect(outcomeOfApproval('Approved')).toBe('approve');
    expect(outcomeOfApproval('Rejected')).toBe('reject');
    expect(outcomeOfApproval('Returned')).toBe('return');
    expect(outcomeOfApproval('Pending')).toBeUndefined();
  });

  it('finds the newest invoice-level decision record, ignoring line records and the override', () => {
    const older = decisionRecord({ recordId: 'a', actionCode: 'return-to-firm', subject: 'invoice', lineId: null, decidedOn: '2026-11-01T10:00:00Z' });
    const newer = decisionRecord({ recordId: 'b', actionCode: 'approve-invoice', subject: 'invoice', lineId: null, decidedOn: '2026-11-03T10:00:00Z' });
    const lineRec = decisionRecord({ recordId: 'c', decidedOn: '2026-11-04T10:00:00Z' });
    expect(invoiceDecisionRecord({ decisionHistory: [older, lineRec, newer] })?.recordId).toBe('b');
    expect(invoiceDecisionRecord({ decisionHistory: [lineRec] })).toBeNull();
  });
});

describe('History: newest first and the eBilling words', () => {
  it('sorts newest first, records without a time last, and keeps the server order among ties', () => {
    const r = (id: string, decidedOn: string | null) => decisionRecord({ recordId: id, decidedOn });
    const sorted = sortNewestFirst([r('old', '2026-11-01T10:00:00Z'), r('none', null), r('new', '2026-11-03T10:00:00Z'), r('mid', '2026-11-02T10:00:00Z')]);
    expect(sorted.map(x => x.recordId)).toEqual(['new', 'mid', 'old', 'none']);
    // The server lists oldest first: equal times show the later-listed one first.
    const ties = sortNewestFirst([r('t1', '2026-11-01T10:00:00Z'), r('t2', '2026-11-01T10:00:00Z')]);
    expect(ties.map(x => x.recordId)).toEqual(['t2', 't1']);
  });

  it('labels each record with the action word and a tone, falling back to the stored outcome', () => {
    expect(recordBadge({ actionCode: 'disallow-line', outcome: 'Authorized' })).toEqual({ label: 'Disallowed', tone: 'critical' });
    expect(recordBadge({ actionCode: 'reduce-line', outcome: 'Authorized' })).toEqual({ label: 'Reduced', tone: 'warning' });
    expect(recordBadge({ actionCode: 'approve-invoice', outcome: 'Authorized' })).toEqual({ label: 'Approved', tone: 'success' });
    expect(recordBadge({ actionCode: 'return-to-firm', outcome: null }).label).toBe('Returned to firm');
    expect(recordBadge({ actionCode: null, outcome: 'Denied' })).toEqual({ label: 'Denied', tone: 'critical' });
    expect(recordBadge({ actionCode: 'something-new', outcome: null })).toEqual({ label: 'Recorded', tone: 'neutral' });
  });

  it('titles a line record with its line number and an invoice record with the invoice', () => {
    expect(recordTitle(decisionRecord({ actionCode: 'reduce-line' }), 4)).toBe('Line 4 reduced');
    expect(recordTitle(decisionRecord({ actionCode: 'approve-invoice', subject: 'invoice', lineId: null }), null)).toBe('Invoice approved');
  });
});

describe('History: decisions and corrections together', () => {
  const correction = (id: string, reviewedOn: string | null, overrides = {}) => ({
    id,
    lineId: lineId(1),
    field: 'Hours',
    aiValue: '25',
    finalValue: '2.5',
    outcome: 'Corrected',
    isLabel: true,
    aiConfidence: 0.4,
    reviewedOn,
    ...overrides,
  });

  it('merges both kinds newest first', () => {
    const items = historyItems(
      [decisionRecord({ recordId: 'd1', decidedOn: '2026-11-02T09:00:00Z' })],
      [correction('c-old', '2026-11-01T09:00:00Z'), correction('c-new', '2026-11-03T09:00:00Z')]
    );
    expect(items.map(i => (i.kind === 'decision' ? i.record.recordId : i.correction.id))).toEqual(['c-new', 'd1', 'c-old']);
  });

  it('puts an untimed correction last', () => {
    const items = historyItems([decisionRecord({ recordId: 'd1' })], [correction('c-none', null)]);
    expect(items[items.length - 1].kind).toBe('correction');
  });

  it('says what was done to the value', () => {
    expect(correctionBadge('Corrected')).toEqual({ label: 'Corrected', tone: 'warning' });
    expect(correctionBadge('Confirmed individually').label).toBe('Confirmed');
    expect(correctionBadge('Something else').label).toBe('Something else');
    expect(correctionTitle(correction('c', null), 'Hours', 3)).toBe('Line 3 · Hours corrected');
    expect(correctionTitle(correction('c', null, { lineId: null }), 'Invoice date', null)).toBe('Invoice · Invoice date corrected');
  });
});

describe('relatedWorkEmptyText: what each empty answer means (107)', () => {
  it('has words for every reason code the server sends', () => {
    for (const reason of ['not-a-fee-line', 'no-timekeeper', 'no-linked-contact', 'no-core-record', 'no-line-date', 'no-narrative', 'none-in-window', 'none-indexed']) {
      expect(relatedWorkEmptyText(reason, 7)).not.toContain('No related work was found');
    }
  });

  it('puts the window in the none-in-window sentence and names an unknown reason', () => {
    expect(relatedWorkEmptyText('none-in-window', 14)).toContain('within 14 days');
    expect(relatedWorkEmptyText('brand-new', null)).toBe('No related work was found (brand-new).');
    expect(relatedWorkEmptyText(null, null)).toBe('No related work was found.');
  });
});

describe('adjustmentValueError (shape only: the amount and the cap are the server\'s)', () => {
  it('accepts an empty value and a positive number', () => {
    expect(adjustmentValueError('percent', '')).toBeNull();
    expect(adjustmentValueError('fixed', '250.5')).toBeNull();
  });

  it('refuses zero, negatives, text, and a percentage above 100', () => {
    expect(adjustmentValueError('fixed', '0')).toBe('Enter a positive number.');
    expect(adjustmentValueError('fixed', '-5')).toBe('Enter a positive number.');
    expect(adjustmentValueError('fixed', 'abc')).toBe('Enter a positive number.');
    expect(adjustmentValueError('percent', '101')).toBe('A percentage can be at most 100.');
    expect(adjustmentValueError('fixed', '101')).toBeNull();
  });
});
