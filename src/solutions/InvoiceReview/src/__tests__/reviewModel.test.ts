import {
  EMPTY_ADD_LINE_FORM,
  LINE_FINDING_FALLBACK,
  LINE_FINDING_FALLBACK_UNKNOWN,
  buildAddLineRequest,
  buildLineCountCards,
  filterLines,
  findingsBadge,
  groupByRule,
  groupChecks,
  highlightFor,
  isDecidable,
  lineFindingText,
  lineStatusView,
  nextToDecide,
  planSuggestion,
  resolveFilterKey,
  reviewAccess,
} from '../services/reviewModel';
import { field, finding, flagged, line, lineId, review } from './testKit';

const lines = () => [
  line(1),
  flagged(2),
  line(3, { status: 'Pending', statusLabel: 'Processing' }),
  line(4, { status: 'Accepted', statusLabel: 'Accepted', approvedAmount: 1125 }),
  flagged(5),
  line(6, { status: 'Reduced', statusLabel: 'Reduced', approvedAmount: 1000 }),
];

describe('line status (F-1)', () => {
  it('shows stored Pending as Processing and never offers it for a decision', () => {
    const pending = line(3, { status: 'Pending', statusLabel: 'Pending' });
    expect(lineStatusView(pending).label).toBe('Processing');
    expect(isDecidable(pending)).toBe(false);
    expect(isDecidable(line(1))).toBe(true);
    expect(isDecidable(flagged(2))).toBe(true);
    expect(isDecidable(line(4, { status: 'Accepted' }))).toBe(false);
    expect(isDecidable(line(9, { type: 'Adjustment', status: 'Clean' }))).toBe(false);
  });
});

describe('count filters (MetricCardConfig, D-24)', () => {
  it('builds All, Processing, To decide, Clean, Decided with counts over the whole invoice', () => {
    const onSelect = jest.fn();
    const cards = buildLineCountCards({ lines: lines(), selectedKey: 'all', onSelectKey: onSelect });
    expect(cards.map(c => [c.label, c.value, c.selected, c.disableWhenEmpty, c.ariaLabel])).toEqual([
      ['All lines', 6, true, false, 'All lines, 6 lines'],
      ['Processing', 1, false, true, 'Processing, 1 line'],
      ['To decide', 2, false, true, 'To decide, 2 lines'],
      ['Clean', 1, false, true, 'Clean, 1 line'],
      ['Decided', 2, false, true, 'Decided, 2 lines'],
    ]);
    expect(cards[0].progress).toEqual({ done: 2, total: 6 });
  });

  it('selects a card, and choosing the selected card again returns to All', () => {
    const onSelect = jest.fn();
    const cards = buildLineCountCards({ lines: lines(), selectedKey: 'toDecide', onSelectKey: onSelect });
    expect(cards.find(c => c.label === 'To decide')!.selected).toBe(true);
    cards.find(c => c.label === 'To decide')!.onClick!();
    cards.find(c => c.label === 'Clean')!.onClick!();
    expect(onSelect.mock.calls).toEqual([['all'], ['clean']]);
  });

  it('falls back to All when the chosen filter no longer shows a line', () => {
    const all = [line(1), line(2)];
    expect(resolveFilterKey(all, 'toDecide')).toBe('all');
    expect(filterLines(lines(), 'decided').map(l => l.sequence)).toEqual([4, 6]);
  });
});

describe('line findings (EB-D-175/176/101 F2)', () => {
  it('counts line findings in the badge, including a line with no policy findings', () => {
    const l = line(1, {
      status: 'Flagged',
      findingCount: 0,
      lineFindings: [
        { code: 'coding.not_returned', message: 'The AI did not code this line.' },
        { code: 'line.no_description', message: null },
      ],
    });
    const badge = findingsBadge(l);
    expect(badge.count).toBe(2);
    expect(badge.severity).toBe('Warning');
    expect(badge.names).toEqual(['The AI did not code this line.', LINE_FINDING_FALLBACK['line.no_description']]);

    const both = flagged(2, { lineFindings: [{ code: 'rules.not_checked', message: null }] });
    expect(findingsBadge(both).count).toBe(2);
  });

  it('shows a fixed text for each code when the stored message is null', () => {
    expect(lineFindingText({ code: 'coding.not_returned', message: null })).toBe(LINE_FINDING_FALLBACK['coding.not_returned']);
    expect(lineFindingText({ code: 'line.no_description', message: '  ' })).toBe(LINE_FINDING_FALLBACK['line.no_description']);
    expect(lineFindingText({ code: 'rules.not_checked', message: null })).toBe(LINE_FINDING_FALLBACK['rules.not_checked']);
    expect(lineFindingText({ code: 'something.new', message: null })).toBe(LINE_FINDING_FALLBACK_UNKNOWN);
  });
});

describe('By rule (answer 4)', () => {
  // Owner review 2026-11-25 (INV-456.1, dev): line 2 was To decide because the AI's task code (L190) differs from the firm's (L410), a
  // coding finding, not a policy finding, and By rule read "No rule findings on these lines."
  it('lists the non-rule checks: code mismatches, line findings by code and unmatched timekeepers, most severe first', () => {
    const base = line(1).codes;
    const mismatch = { ...base, task: { asBilled: 'L410', code: 'L190', firmCode: 'L410', mismatch: true } };
    const ls = [
      line(1, { timekeeper: null }),
      flagged(2, { findings: [], findingCount: 0, codes: mismatch, timekeeper: null }),
      flagged(3, { findings: [], findingCount: 0, lineFindings: [{ code: 'coding.not_returned', message: null }] }),
      line(4, { type: 'Expense', timekeeper: null }),
      line(5),
    ];
    expect(groupByRule(ls.slice(0, 2))).toEqual([]);
    expect(groupChecks(ls).map(c => [c.key, c.label, c.severity, c.lines.map(l => l.sequence)])).toEqual([
      ['line|coding.not_returned', 'Codes not checked by the AI', 'Warning', [3]],
      ['code-mismatch', "UTBMS code differs from the firm's", 'Warning', [2]],
      ['roster', "Timekeeper not on the firm's roster", 'Info', [1, 2]],
    ]);
    expect(groupChecks(ls).find(c => c.key === 'line|coding.not_returned')!.text).toBe(LINE_FINDING_FALLBACK['coding.not_returned']);
    // Must not fire: a matched timekeeper, agreeing codes, no line findings, and an expense line without a timekeeper.
    expect(groupChecks([line(5), line(4, { type: 'Expense', timekeeper: null })])).toEqual([]);
  });

  it('groups by rule, most severe first, then by count', () => {
    const vague = finding({ policyCode: 'VAGUE', ruleType: 'Narrative', severity: 'Info', suggestedAdjustment: null });
    const dup = finding({ policyCode: 'DUP', ruleType: 'Duplicate', severity: 'Critical', suggestedAdjustment: { kind: 'Disallow', amount: 1125 } });
    const ls = [flagged(1), flagged(2), flagged(3, { findings: [finding(), vague] }), flagged(4, { findings: [vague] }), flagged(5, { findings: [dup] })];
    expect(groupByRule(ls).map(g => [g.label, g.severity, g.lines.length])).toEqual([
      ['DUP', 'Critical', 1],
      ['RATE-CAP', 'Warning', 3],
      ['VAGUE', 'Info', 2],
    ]);
  });

  it('plans one multi-line Reduce with each line at billed minus its own suggestion, leaving out lines that cannot be decided', () => {
    const ls = [
      flagged(1),
      flagged(2, { amount: 2000, findings: [finding({ suggestedAdjustment: { kind: 'Reduce', amount: 300 } })] }),
      flagged(3, { status: 'Reduced' }),
      flagged(4, { status: 'Pending' }),
    ];
    const plan = planSuggestion(groupByRule(ls)[0]);
    expect(plan).toEqual({
      action: 'reduce',
      lines: [
        { lineId: lineId(1), approvedAmount: 1012.5 },
        { lineId: lineId(2), approvedAmount: 1700 },
      ],
      skipped: 2,
    });
  });

  it('offers no single plan when the group mixes Reduce and Disallow', () => {
    const ls = [flagged(1), flagged(2, { findings: [finding({ suggestedAdjustment: { kind: 'Disallow', amount: 1125 } })] })];
    expect(planSuggestion(groupByRule(ls)[0])).toBeNull();
  });
});

describe('next line to decide', () => {
  it('moves forward, wraps, and stops when none is left', () => {
    const ls = lines();
    expect(nextToDecide(ls, null)?.sequence).toBe(2);
    expect(nextToDecide(ls, lineId(2))?.sequence).toBe(5);
    expect(nextToDecide(ls, lineId(5))?.sequence).toBe(2);
    expect(nextToDecide([flagged(2)], lineId(2))).toBeNull();
    expect(nextToDecide([line(1)], null)).toBeNull();
  });
});

describe('PDF highlight (108 contract)', () => {
  it('boxes the row as the union of the line boxes and the clicked value as the field', () => {
    const l = line(1, {
      fields: {
        Date: field({ page: 2, box: { page: 2, x: 0.05, y: 0.4, w: 0.1, h: 0.02 } }),
        Hours: field({ page: 2, box: { page: 2, x: 0.6, y: 0.41, w: 0.05, h: 0.03 } }),
        Narrative: field({ page: 1, box: { page: 1, x: 0.2, y: 0.9, w: 0.3, h: 0.02 } }),
      },
    });
    const h = highlightFor(l, 'Hours')!;
    expect(h.page).toBe(2);
    expect(h.field).toEqual({ x: 0.6, y: 0.41, w: 0.05, h: 0.03 });
    expect(h.row!.x).toBeCloseTo(0.05);
    expect(h.row!.y).toBeCloseTo(0.4);
    expect(h.row!.w).toBeCloseTo(0.6);
    expect(h.row!.h).toBeCloseTo(0.04);
    expect(h.label).toBe('Line 1 · Hours');
  });

  // Owner review 2026-11-25 (INV-456.1, dev): the timekeeper heading sits above every line of that timekeeper, so a row drawn around the
  // TimekeeperAsBilled box covered the timekeeper's earlier lines too. Boxes are the ones dev stored for line 2.
  it('keeps the row to the line itself: the timekeeper heading and the rate table are not part of it', () => {
    const l = line(2, {
      fields: {
        Date: field({ page: 2, box: { page: 2, x: 0.067, y: 0.129, w: 0.075, h: 0.012 } }),
        TimekeeperAsBilled: field({ page: 2, box: { page: 2, x: 0.365, y: 0.085, w: 0.135, h: 0.014 } }),
        TitleAsBilled: field({ page: 2, box: { page: 2, x: 0.508, y: 0.085, w: 0.069, h: 0.014 } }),
        Rate: field({ page: 1, box: { page: 1, x: 0.477, y: 0.288, w: 0.046, h: 0.012 } }),
        Narrative: field({ page: 2, box: { page: 2, x: 0.272, y: 0.128, w: 0.453, h: 0.044 } }),
        Amount: field({ page: 2, box: { page: 2, x: 0.862, y: 0.129, w: 0.068, h: 0.013 } }),
      },
    });
    const h = highlightFor(l, null)!;
    expect(h.page).toBe(2);
    expect(h.row!.y).toBeCloseTo(0.128); // the line's own top, not the heading's 0.085
    expect(h.row!.y + h.row!.h).toBeCloseTo(0.172);
    // A chosen shared value is still shown as the field, over the line's own row.
    const tk = highlightFor(l, 'TimekeeperAsBilled')!;
    expect(tk.field).toEqual({ x: 0.365, y: 0.085, w: 0.135, h: 0.014 });
    expect(tk.row!.y).toBeCloseTo(0.128);
    // The rate sits on page 1, where the line has no row box: the rate alone is boxed.
    const rate = highlightFor(l, 'Rate')!;
    expect(rate.page).toBe(1);
    expect(rate.row).toBeNull();
    expect(rate.field).toEqual({ x: 0.477, y: 0.288, w: 0.046, h: 0.012 });
    // A line whose only boxes are shared still gets a row from them.
    const onlyShared = line(3, { fields: { TimekeeperAsBilled: field({ page: 2, box: { page: 2, x: 0.3, y: 0.5, w: 0.1, h: 0.02 } }) } });
    const fallback = highlightFor(onlyShared, null)!.row!;
    expect(fallback.y).toBeCloseTo(0.5);
    expect(fallback.h).toBeCloseTo(0.02);
  });

  it('boxes only the row with no value chosen, and nothing for a line without boxes', () => {
    expect(highlightFor(line(1), null)!.field).toBeNull();
    expect(highlightFor(line(1, { fields: {} }), 'Hours')).toBeNull();
    expect(highlightFor(null, null)).toBeNull();
  });
});

describe('read-only', () => {
  it('is read-only after a 403, outside In Review, and without the server flag', () => {
    const r = review([]);
    expect(reviewAccess(r, false)).toEqual({ canWrite: true, reason: null });
    expect(reviewAccess(r, true).canWrite).toBe(false);
    expect(reviewAccess({ ...r, header: { ...r.header, status: 'Approved' } }, false)).toEqual({
      canWrite: false,
      reason: 'This invoice is approved; it is shown read-only.',
    });
    expect(reviewAccess({ ...r, flags: { canRead: true, canDecide: false } }, false).canWrite).toBe(false);
  });
});

describe('buildAddLineRequest: number text (164b review K1)', () => {
  const feeWithHours = (hours: string) => buildAddLineRequest({ ...EMPTY_ADD_LINE_FORM, costType: 'fee', hours }, 'req-1');

  it.each(['0x10', '1e3', '1E3', 'Infinity', '--2', '1,5', '1 2', '.', '+', '-.'])('refuses "%s" as a number and names the field', text => {
    expect(feeWithHours(text)).toEqual({ ok: false, message: 'Hours must be a number, for example 2.5.' });
  });

  it.each([
    ['1.5', 1.5],
    ['-2', -2],
    ['  2.5 ', 2.5],
    ['300', 300],
    ['0', 0],
    ['.5', 0.5],
    ['.25', 0.25],
    ['5.', 5],
    ['+2', 2],
    ['-.5', -0.5],
  ])('accepts "%s" as %s', (text, value) => {
    const built = feeWithHours(text);
    expect(built.ok).toBe(true);
    if (built.ok) expect(built.request.hours).toBe(value);
  });

  it('sends a blank as null', () => {
    const built = feeWithHours('  ');
    expect(built.ok && built.request.hours).toBeNull();
  });
});
