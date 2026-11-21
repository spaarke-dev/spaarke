/**
 * ebilling-r1 task 134 (FR-38, FR-18, D-66, EB-D-202, EB-D-254, EB-D-259; 130f K6): the firm outcome report over its REAL client
 * (`ebilling-client`), the real `OutcomeSection` and the real record template config. Substituted: the network seam (`bffApiCall`,
 * throwing `ApiError` as bff-client builds it) and `window.print`.
 *
 *   GET /api/v1/external/ebilling/invoices/{id}/outcome          -> FirmOutcome (camelCase; enums as PascalCase names)
 *   GET /api/v1/external/ebilling/submissions/{id}/outcome       -> FirmOutcome
 *
 * The result-screen wiring (`InvoiceSubmitPage` passes `renderReport`) is asserted in `InvoiceSubmitPage.test.tsx`.
 */
import * as React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

const h = vi.hoisted(() => ({ bffApiCall: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall: h.bffApiCall };
});

import { ApiError } from '../src/types';
import {
  getInvoiceOutcome,
  getSubmissionOutcome,
  normalizeOutcome,
  type FirmOutcome,
} from '../src/api/ebilling-client';
import {
  InvoiceOutcomeReport,
  OUTCOME_NO_ACCESS_TEXT,
  OUTCOME_REPORT_CLASS,
  OUTCOME_UNAVAILABLE_TEXT,
  groupOutcomeItems,
  outcomeDifference,
} from '../src/components/ebilling/InvoiceOutcomeReport';
import { OutcomeSection } from '../src/components/record/sections/OutcomeSection';
import { RECORD_SECTIONS } from '../src/components/record/sections';
import { RECORD_TEMPLATES } from '../src/components/record/recordTemplates';
import type { RecordSectionContext, RecordSectionId } from '../src/components/record/recordTemplate.types';

const WAIT = { timeout: 12000 };
const guid = (n: number): string => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const INVOICE = guid(134);
const SUBMISSION = guid(135);
const INVOICE_PATH = `/api/v1/external/ebilling/invoices/${INVOICE}/outcome`;
const SUBMISSION_PATH = `/api/v1/external/ebilling/submissions/${SUBMISSION}/outcome`;

const problem = (status: number, extra: Record<string, unknown> = {}): string =>
  JSON.stringify({ status, title: 'refused', detail: 'server detail text that must not be shown', ...extra });

// ---------------------------------------------------------------------------
// Fixtures: the wire shape of FirmOutcome (JsonStringEnumConverter names, camelCase, optional members omitted when null)
// ---------------------------------------------------------------------------

const CITATION = {
  ruleName: 'Approved rates',
  label: 'Outside counsel guidelines',
  version: 3,
  sectionReference: 'Section 4.2',
  quote: 'Rates may not exceed the approved rate.',
};

function lineItem(over: Record<string, unknown> = {}) {
  return {
    decision: 'Reduced',
    lineId: guid(901),
    line: {
      date: '2026-03-02',
      timekeeperAsBilled: 'J. Partner',
      descriptionExcerpt: 'Review of draft agreement',
      billedAmount: 800,
      approvedAmount: 650,
    },
    adjustment: null,
    reason: 'Rate above the approved rate.',
    firmReasons: ['Rates are capped at the approved rate.'],
    citations: [CITATION],
    ...over,
  };
}

function outcome(over: Record<string, unknown> = {}) {
  return {
    kind: 'ApprovedWithAdjustments',
    trackingNumber: 'TRK-500',
    statusLabel: 'Approved',
    billedTotal: 1500,
    approvedTotal: 1250,
    degraded: false,
    items: [
      lineItem(),
      lineItem({
        decision: 'Disallowed',
        lineId: guid(902),
        line: {
          date: '2026-03-01',
          timekeeperAsBilled: 'A. Associate',
          descriptionExcerpt: 'Block billed entry',
          billedAmount: 200,
          approvedAmount: 0,
        },
        reason: 'Block billing is not permitted.',
        firmReasons: [],
        citations: [],
      }),
      {
        decision: 'Adjustment',
        lineId: guid(903),
        line: null,
        adjustment: { method: 'Percent', value: 5, amount: 50 },
        reason: 'Volume discount.',
        firmReasons: [],
        citations: [],
      },
    ],
    ...over,
  };
}

const parsed = (raw: unknown): FirmOutcome => {
  const o = normalizeOutcome(raw);
  if (!o) throw new Error('fixture is not a FirmOutcome');
  return o;
};

function renderReport(raw: unknown, theme = webLightTheme) {
  return render(
    <FluentProvider theme={theme}>
      <InvoiceOutcomeReport outcome={parsed(raw)} />
    </FluentProvider>
  );
}

/** The section's context: `OutcomeSection` reads only the record id and the refresh signal. */
function sectionContext(over: Partial<RecordSectionContext> = {}): RecordSectionContext {
  return { recordId: INVOICE, refreshSignal: 0, full: false, ...over } as unknown as RecordSectionContext;
}

function renderSection(over: Partial<RecordSectionContext> = {}) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <OutcomeSection {...sectionContext(over)} />
    </FluentProvider>
  );
}

let printSpy: ReturnType<typeof vi.spyOn>;

beforeEach(() => {
  h.bffApiCall.mockReset();
  vi.spyOn(console, 'error').mockImplementation(() => {});
  printSpy = vi.spyOn(window, 'print').mockImplementation(() => {});
  localStorage.clear();
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  Object.defineProperty(window, 'innerWidth', { configurable: true, writable: true, value: 1024 });
});

// ---------------------------------------------------------------------------
// The client
// ---------------------------------------------------------------------------

describe('ebilling client', () => {
  it('reads the invoice and submission outcome routes through bffApiCall only (ADR-028)', async () => {
    h.bffApiCall.mockResolvedValue(outcome());

    const invoice = await getInvoiceOutcome(INVOICE);
    const submission = await getSubmissionOutcome(SUBMISSION);

    expect(h.bffApiCall).toHaveBeenNthCalledWith(1, INVOICE_PATH);
    expect(h.bffApiCall).toHaveBeenNthCalledWith(2, SUBMISSION_PATH);
    expect(invoice.state).toBe('ok');
    expect(submission.state).toBe('ok');
  });

  it('refuses an id that is not a guid before any request', async () => {
    expect((await getInvoiceOutcome('not-a-guid')).state).toBe('error');
    expect((await getSubmissionOutcome('../../x')).state).toBe('error');
    expect(h.bffApiCall).not.toHaveBeenCalled();
  });

  it.each([
    ['403 is noAccess', new ApiError(403, problem(403)), 'noAccess'],
    ['404 is notFound', new ApiError(404, problem(404)), 'notFound'],
    [
      '503 ebilling.disabled is disabled',
      new ApiError(503, problem(503, { errorCode: 'ebilling.disabled' })),
      'disabled',
    ],
    [
      'another 503 is unavailable',
      new ApiError(503, problem(503, { errorCode: 'ebilling.firm-outcome.unavailable' })),
      'unavailable',
    ],
    ['a 503 with no body is unavailable', new ApiError(503, ''), 'unavailable'],
    ['500 is error', new ApiError(500, problem(500)), 'error'],
    ['a network failure is error', new Error('Failed to fetch'), 'error'],
  ])('%s', async (_name, failure, state) => {
    h.bffApiCall.mockRejectedValue(failure);
    expect((await getInvoiceOutcome(INVOICE)).state).toBe(state);
  });

  it('a degraded answer, an Unavailable kind and a body that is not the contract are never a report', async () => {
    h.bffApiCall.mockResolvedValueOnce(outcome({ degraded: true }));
    expect((await getInvoiceOutcome(INVOICE)).state).toBe('unavailable');
    h.bffApiCall.mockResolvedValueOnce(outcome({ kind: 'Unavailable', items: [] }));
    expect((await getInvoiceOutcome(INVOICE)).state).toBe('unavailable');
    h.bffApiCall.mockResolvedValueOnce({ unexpected: true });
    expect((await getInvoiceOutcome(INVOICE)).state).toBe('error');
    h.bffApiCall.mockResolvedValueOnce(outcome({ items: 'not a list' }));
    expect((await getInvoiceOutcome(INVOICE)).state).toBe('error');
  });

  it('copies only the members of the contract (an allow-list): unknown keys at every level are dropped', () => {
    const o = parsed(
      outcome({
        overrideReason: 'SECRET-override',
        procedureName: 'SECRET-procedure-name',
        items: [
          lineItem({
            procedureId: 'SECRET-procedure-id',
            citations: [{ ...CITATION, ruleSetName: 'SECRET-rule-set', procedureName: 'SECRET-procedure-name' }],
            line: { ...lineItem().line, reviewer: 'SECRET-reviewer' },
          }),
        ],
      })
    );
    expect(JSON.stringify(o)).not.toContain('SECRET');
  });
});

// ---------------------------------------------------------------------------
// The report
// ---------------------------------------------------------------------------

describe('InvoiceOutcomeReport', () => {
  it('AC1: every item renders its decision, reason, firm reasons and citation (label, rule, version, section, quote); the summary and totals show', () => {
    renderReport(outcome());

    expect(screen.getByTestId('outcome-tracking').textContent).toBe('TRK-500');
    expect(screen.getByTestId('outcome-status').textContent).toBe('Approved');
    expect(screen.getByTestId('outcome-billed-total').textContent).toBe('1,500.00');
    expect(screen.getByTestId('outcome-approved-total').textContent).toBe('1,250.00');

    const items = screen.getAllByTestId('outcome-item');
    expect(items).toHaveLength(3);
    // Lines first, by date (the Disallowed line is the earlier one); the invoice-level adjustment after.
    expect(items.map(i => i.getAttribute('data-decision'))).toEqual(['Disallowed', 'Reduced', 'Adjustment']);

    const reduced = within(items[1]);
    expect(reduced.getByTestId('outcome-item-decision').textContent).toBe('Reduced');
    expect(reduced.getByText('Review of draft agreement')).toBeTruthy();
    expect(reduced.getByTestId('outcome-item-billed').textContent).toBe('800.00');
    expect(reduced.getByTestId('outcome-item-approved').textContent).toBe('650.00');
    expect(reduced.getByTestId('outcome-item-reason').textContent).toBe('Rate above the approved rate.');
    expect(reduced.getByTestId('outcome-item-firm-reason').textContent).toBe('Rates are capped at the approved rate.');
    expect(reduced.getByTestId('outcome-citation-label').textContent).toBe('Outside counsel guidelines');
    expect(reduced.getByTestId('outcome-citation-rule').textContent).toBe('Approved rates');
    expect(reduced.getByTestId('outcome-citation-version').textContent).toBe('Version 3');
    expect(reduced.getByTestId('outcome-citation-section').textContent).toBe('Section 4.2');
    expect(reduced.getByTestId('outcome-citation-quote').textContent).toBe('Rates may not exceed the approved rate.');

    const disallowed = within(items[0]);
    expect(disallowed.getByTestId('outcome-item-decision').textContent).toBe('Disallowed');
    expect(disallowed.getByTestId('outcome-item-reason').textContent).toBe('Block billing is not permitted.');

    const adjustment = within(items[2]);
    expect(adjustment.getByTestId('outcome-item-decision').textContent).toBe('Invoice adjustment');
    expect(adjustment.getByTestId('outcome-item-adjustment-method').textContent).toBe('Percent 5%');
    expect(adjustment.getByTestId('outcome-item-adjustment-amount').textContent).toBe('50.00');
    expect(adjustment.getByTestId('outcome-item-reason').textContent).toBe('Volume discount.');

    // Totals reconciliation for an approved outcome.
    expect(screen.getByTestId('outcome-reconciliation-billed').textContent).toBe('1,500.00');
    expect(screen.getByTestId('outcome-reconciliation-difference').textContent).toBe('250.00');
    expect(screen.getByTestId('outcome-reconciliation-approved').textContent).toBe('1,250.00');
  });

  it('AC1: an item with no citation shows its reason only (no citation block, no label, no clause)', () => {
    renderReport(outcome());
    const disallowed = within(screen.getAllByTestId('outcome-item')[0]);
    expect(disallowed.getByTestId('outcome-item-reason')).toBeTruthy();
    expect(disallowed.queryByTestId('outcome-citation')).toBeNull();
    // Exactly one citation block on the page: the Reduced line's.
    expect(screen.getAllByTestId('outcome-citation')).toHaveLength(1);
  });

  it('shows the firm-facing label of a citation and never a rule set name, even if the body carries one', () => {
    renderReport(
      outcome({
        items: [lineItem({ citations: [{ ...CITATION, ruleSetName: 'INTERNAL-RULE-SET-NAME' }] })],
      })
    );
    expect(screen.getByTestId('outcome-citation-label').textContent).toBe('Outside counsel guidelines');
    expect(document.body.textContent).not.toContain('INTERNAL-RULE-SET-NAME');
  });

  it('a citation with no label, section or quote shows only the parts it has', () => {
    renderReport(
      outcome({
        items: [
          lineItem({
            citations: [
              { ruleName: 'Approved rates', label: null, version: null, sectionReference: null, quote: null },
            ],
          }),
        ],
      })
    );
    expect(screen.getByTestId('outcome-citation-rule').textContent).toBe('Approved rates');
    expect(screen.queryByTestId('outcome-citation-label')).toBeNull();
    expect(screen.queryByTestId('outcome-citation-version')).toBeNull();
    expect(screen.queryByTestId('outcome-citation-section')).toBeNull();
    expect(screen.queryByTestId('outcome-citation-quote')).toBeNull();
  });

  it('EB-D-202: an In Review invoice shows "Under review", the billed total, no approved total, and the items the server carries', () => {
    renderReport(
      outcome({
        kind: 'InvoiceInReview',
        statusLabel: 'In review',
        billedTotal: 1500,
        approvedTotal: null,
        items: [
          lineItem(),
          lineItem({ decision: 'Added', reason: 'Added after review.', citations: [], firmReasons: [] }),
        ],
      })
    );
    expect(screen.getByTestId('outcome-under-review').textContent).toBe('Under review');
    expect(screen.getByTestId('outcome-status').textContent).toBe('In review');
    expect(screen.getByTestId('outcome-billed-total').textContent).toBe('1,500.00');
    expect(screen.queryByTestId('outcome-approved-total')).toBeNull();
    expect(screen.queryByTestId('outcome-reconciliation')).toBeNull();
    expect(screen.getAllByTestId('outcome-item')).toHaveLength(2);
    expect(screen.getByText('Added after review.')).toBeTruthy();
    expect(screen.getByText('Added by reviewer')).toBeTruthy();
  });

  it('a submission in progress shows the status and the billed total only', () => {
    renderReport({
      kind: 'SubmissionInProgress',
      trackingNumber: 'TRK-9',
      statusLabel: 'Processing',
      billedTotal: 900,
      approvedTotal: null,
      items: [],
      degraded: false,
    });
    expect(screen.getByTestId('outcome-status').textContent).toBe('Processing');
    expect(screen.getByTestId('outcome-billed-total').textContent).toBe('900.00');
    expect(screen.queryByTestId('outcome-approved-total')).toBeNull();
    expect(screen.queryByTestId('outcome-under-review')).toBeNull();
    expect(screen.queryByTestId('outcome-item')).toBeNull();
  });

  it('a Closed outcome shows the status label and tracking number only', () => {
    renderReport({
      kind: 'Closed',
      trackingNumber: 'TRK-10',
      statusLabel: 'Replaced by a correction',
      billedTotal: null,
      approvedTotal: null,
      items: [],
      degraded: false,
    });
    expect(screen.getByTestId('outcome-status').textContent).toBe('Replaced by a correction');
    expect(screen.getByTestId('outcome-tracking').textContent).toBe('TRK-10');
    expect(screen.queryByTestId('outcome-billed-total')).toBeNull();
    expect(screen.queryByTestId('outcome-item')).toBeNull();
  });

  it.each(['Rejected', 'ReturnedToFirm'])(
    '130f K6: a %s invoice shows its items but never a per-line approved amount, even when the body carries one',
    kind => {
      renderReport(
        outcome({
          kind,
          statusLabel: kind === 'Rejected' ? 'Rejected' : 'Returned to firm',
          approvedTotal: null,
          items: [lineItem()],
        })
      );
      const item = within(screen.getByTestId('outcome-item'));
      expect(item.getByTestId('outcome-item-billed').textContent).toBe('800.00');
      expect(item.queryByTestId('outcome-item-approved')).toBeNull();
      expect(document.body.textContent).not.toContain('650.00');
      expect(screen.queryByTestId('outcome-approved-total')).toBeNull();
      expect(screen.queryByTestId('outcome-reconciliation')).toBeNull();
    }
  );

  it('control: an approved invoice does show the per-line approved amount', () => {
    renderReport(outcome({ items: [lineItem()] }));
    expect(screen.getByTestId('outcome-item-approved').textContent).toBe('650.00');
  });

  it.each(['Rejected', 'ReturnedToFirm'])(
    '130g: %s shows noticeProse as the firm-safe reason; markup in it is shown literally, never rendered',
    kind => {
      const prose = '<b>x</b><script>window.__pwned = 1</script> Rates were above the approved rate.';
      const { container } = renderReport(outcome({ kind, approvedTotal: null, items: [], noticeProse: prose }));
      const node = screen.getByTestId('outcome-notice-prose');
      expect(node.textContent).toBe(prose);
      expect(container.querySelector('b')).toBeNull();
      expect(container.querySelector('script')).toBeNull();
      expect((window as unknown as { __pwned?: number }).__pwned).toBeUndefined();
      // The prose is a text node: the element holds nothing but text.
      expect(node.children).toHaveLength(0);
    }
  );

  it('130g: an absent noticeProse (or a blank one) shows no reason line', () => {
    renderReport(outcome({ kind: 'Rejected', approvedTotal: null, items: [lineItem()] }));
    expect(screen.queryByTestId('outcome-notice-prose')).toBeNull();
    cleanup();
    renderReport(outcome({ kind: 'Rejected', approvedTotal: null, items: [lineItem()], noticeProse: '   ' }));
    expect(screen.queryByTestId('outcome-notice-prose')).toBeNull();
  });

  it('a rejected submission shows each finding with its line number, the values read, its reason and clause; and the not-shown note', () => {
    renderReport({
      kind: 'SubmissionRejected',
      trackingNumber: 'TRK-77',
      statusLabel: 'Rejected',
      billedTotal: null,
      approvedTotal: null,
      degraded: false,
      notShownCount: 3,
      items: [
        {
          decision: 'RejectionFinding',
          reason: null,
          firmReasons: ['The billed rate is above the approved rate.'],
          citations: [CITATION],
          lineNumber: 7,
          valuesRead: [
            { name: 'Billed rate', value: '540' },
            { name: 'Approved rate', value: '500' },
          ],
          reasonCode: 'approved_rate.above_approved',
        },
        {
          decision: 'RejectionFinding',
          reason: null,
          firmReasons: ['This invoice duplicates an earlier one.'],
          citations: [],
          valuesRead: [],
          reasonCode: 'duplicate.invoice',
        },
        {
          decision: 'RejectionFinding',
          reason: null,
          firmReasons: ['A line has no timekeeper.'],
          citations: [],
          lineNumber: 2,
          valuesRead: [{ name: 'Timekeeper', value: '' }],
          reasonCode: 'timekeeper.missing',
        },
      ],
    });
    const items = screen.getAllByTestId('outcome-item');
    // By line number ascending; the finding about the invoice as a whole after the lines.
    expect(items.map(i => within(i).getByTestId('outcome-item-title').textContent)).toEqual([
      'Line 2',
      'Line 7',
      'Whole invoice',
    ]);
    const seven = within(items[1]);
    expect(seven.getByTestId('outcome-item-decision').textContent).toBe('Rejected');
    const values = seven.getByTestId('outcome-item-values');
    expect(values.textContent).toContain('Billed rate');
    expect(values.textContent).toContain('540');
    expect(values.textContent).toContain('Approved rate');
    expect(values.textContent).toContain('500');
    expect(seven.getByTestId('outcome-item-firm-reason').textContent).toBe(
      'The billed rate is above the approved rate.'
    );
    expect(seven.getByTestId('outcome-citation-quote').textContent).toBe(CITATION.quote);
    expect(screen.getByTestId('outcome-not-shown').textContent).toContain('3 more findings could not be shown');
    expect(screen.getByRole('heading', { name: 'Findings' })).toBeTruthy();
  });

  it('there is no note when notShownCount is absent or zero', () => {
    renderReport(outcome({ kind: 'SubmissionRejected', approvedTotal: null, items: [], notShownCount: 0 }));
    expect(screen.queryByTestId('outcome-not-shown')).toBeNull();
  });

  it('D-66 negative: keys the contract does not name (override reason, procedure name and id, rule set name) never render', () => {
    const { container } = renderReport(
      outcome({
        overrideReason: 'LEAK-override-reason',
        procedureName: 'LEAK-procedure-name',
        procedureId: 'LEAK-procedure-id',
        reviewer: 'LEAK-reviewer',
        items: [
          lineItem({
            overrideReason: 'LEAK-override-reason',
            procedureName: 'LEAK-procedure-name',
            procedureId: 'LEAK-procedure-id',
            documentUrl: 'https://leak.example/doc',
            citations: [
              {
                ...CITATION,
                procedureName: 'LEAK-procedure-name',
                ruleSetName: 'LEAK-rule-set',
                policyId: 'LEAK-policy',
              },
            ],
          }),
        ],
      })
    );
    expect(container.textContent).not.toContain('LEAK');
    expect(container.innerHTML).not.toContain('LEAK');
    expect(container.innerHTML).not.toContain('leak.example');
  });

  it('D-63 / NFR-10: the report builds no link', () => {
    const { container } = renderReport(outcome());
    expect(container.querySelectorAll('a, [href]')).toHaveLength(0);
  });

  it('NFR-13: items are stacked cards in a list at 360 px and at desktop width, never a table', () => {
    for (const width of [360, 1280]) {
      Object.defineProperty(window, 'innerWidth', { configurable: true, writable: true, value: width });
      window.dispatchEvent(new Event('resize'));
      const { container } = renderReport(outcome());
      expect(container.querySelectorAll('table, thead, tr, td')).toHaveLength(0);
      const cards = container.querySelectorAll('ul > li[data-testid="outcome-item"]');
      expect(cards).toHaveLength(3);
      // No inline width anywhere (jsdom does no layout, so the guard is on what the markup can do).
      for (const el of Array.from(container.querySelectorAll<HTMLElement>('*'))) {
        expect(el.style.width).toBe('');
      }
      cleanup();
    }
  });
});

// ---------------------------------------------------------------------------
// Print (EB-D-254)
// ---------------------------------------------------------------------------

describe('print', () => {
  it('the "Print or save as PDF" button calls window.print', () => {
    renderReport(outcome());
    const button = screen.getByRole('button', { name: 'Print or save as PDF' });
    fireEvent.click(button);
    expect(printSpy).toHaveBeenCalledTimes(1);
  });

  it('there is no download control and no report route call (EB-D-254: no PDF in R1)', () => {
    renderReport(outcome());
    expect(screen.queryByRole('button', { name: /download/i })).toBeNull();
    expect(h.bffApiCall).not.toHaveBeenCalled();
  });

  /** Every `@media print` rule the page's style sheets carry, as its selector texts. */
  function printSelectors(): string[] {
    const selectors: string[] = [];
    const visit = (rules: CSSRuleList, inPrint: boolean) => {
      for (const rule of Array.from(rules)) {
        const media = rule as CSSMediaRule;
        if (media.media !== undefined && media.cssRules) {
          visit(media.cssRules, inPrint || media.media.mediaText.includes('print'));
        } else if (inPrint && rule instanceof CSSStyleRule) {
          selectors.push(rule.selectorText);
        }
      }
    };
    for (const sheet of Array.from(document.styleSheets)) {
      try {
        visit(sheet.cssRules, false);
      } catch {
        /* a cross-origin sheet */
      }
    }
    return selectors;
  }

  it('the print rules are scoped to the report: classes of the report only, and no global (body, html, universal, id) selector', () => {
    const { container } = renderReport(outcome());
    const root = screen.getByTestId('invoice-outcome-report');
    expect(root.classList.contains(OUTCOME_REPORT_CLASS)).toBe(true);

    const selectors = printSelectors();
    expect(selectors.length).toBeGreaterThan(0);
    const classesInReport = new Set<string>();
    for (const el of [root, ...Array.from(root.querySelectorAll('*'))])
      el.classList.forEach(c => classesInReport.add(c));
    for (const selector of selectors) {
      expect(selector).toMatch(/^\.[\w-]+/);
      expect(selector).not.toMatch(/(^|[\s,>+~])(html|body|\*|#)/);
      // Every print rule belongs to a class of an element inside the report (nothing reaches the record modal or a grid).
      const classes = Array.from(selector.matchAll(/\.([\w-]+)/g)).map(m => m[1]);
      expect(classes.some(c => classesInReport.has(c))).toBe(true);
    }
    // The report itself carries print rules (light palette, page break, hidden button).
    expect(container.querySelectorAll('[data-testid="outcome-print"]')).toHaveLength(1);
    expect(selectors.some(s => root.classList.contains(s.replace(/^\./, '')))).toBe(true);
  });

  it('a dark-theme page prints in the light palette: the report re-points its colour tokens at the light theme in print', () => {
    renderReport(outcome(), webDarkTheme);
    const root = screen.getByTestId('invoice-outcome-report');
    const rulesOfRoot = Array.from(document.styleSheets).flatMap(sheet => {
      try {
        return Array.from(sheet.cssRules);
      } catch {
        return [];
      }
    });
    const printText = rulesOfRoot
      .filter(r => (r as CSSMediaRule).media?.mediaText.includes('print'))
      .map(r => r.cssText)
      .join('\n');
    expect(printText).toContain(`--colorNeutralBackground1: ${webLightTheme.colorNeutralBackground1}`);
    expect(printText).toContain(`--colorNeutralForeground1: ${webLightTheme.colorNeutralForeground1}`);
    expect(root).toBeTruthy();
  });
});

// ---------------------------------------------------------------------------
// The invoice record's Outcome section
// ---------------------------------------------------------------------------

describe('OutcomeSection', () => {
  it('is registered as a section and is in the sprk_invoice template only, after Information', () => {
    expect(RECORD_SECTIONS.outcome).toBe(OutcomeSection);
    const hasOutcome = (id: string) =>
      [...RECORD_TEMPLATES[id].left, ...RECORD_TEMPLATES[id].right].includes('outcome' as RecordSectionId);
    expect(RECORD_TEMPLATES.sprk_invoice.left).toEqual(['information', 'outcome', 'files']);
    for (const entity of Object.keys(RECORD_TEMPLATES)) {
      expect(hasOutcome(entity)).toBe(entity === 'sprk_invoice');
    }
  });

  it('loads the invoice outcome and shows the report inside the Outcome card', async () => {
    h.bffApiCall.mockResolvedValue(outcome());
    renderSection();
    expect(screen.getByTestId('outcome-loading')).toBeTruthy();
    const report = await screen.findByTestId('invoice-outcome-report', undefined, WAIT);
    expect(report.closest('[data-testid="record-section-outcome"]')).toBeTruthy();
    expect(screen.getByRole('region', { name: 'Outcome' })).toBeTruthy();
    expect(h.bffApiCall).toHaveBeenCalledWith(INVOICE_PATH);
  });

  it('403 shows the no-access state, and never the server text', async () => {
    h.bffApiCall.mockRejectedValue(new ApiError(403, problem(403)));
    renderSection();
    const note = await screen.findByTestId('outcome-no-access', undefined, WAIT);
    expect(note.textContent).toContain(OUTCOME_NO_ACCESS_TEXT);
    expect(document.body.textContent).not.toContain('server detail text');
    expect(screen.queryByTestId('invoice-outcome-report')).toBeNull();
  });

  it('503 ebilling.disabled renders nothing at all: no card, no message', async () => {
    h.bffApiCall.mockRejectedValue(new ApiError(503, problem(503, { errorCode: 'ebilling.disabled' })));
    const { container } = renderSection();
    await waitFor(() => expect(h.bffApiCall).toHaveBeenCalled(), WAIT);
    await waitFor(() => expect(screen.queryByTestId('outcome-loading')).toBeNull(), WAIT);
    expect(container.textContent).toBe('');
    expect(screen.queryByTestId('record-section-outcome')).toBeNull();
  });

  it('another 503 shows the unavailable state with Try again, which reads again', async () => {
    h.bffApiCall.mockRejectedValueOnce(
      new ApiError(503, problem(503, { errorCode: 'ebilling.firm-outcome.unavailable' }))
    );
    h.bffApiCall.mockResolvedValue(outcome());
    renderSection();
    const note = await screen.findByTestId('outcome-unavailable', undefined, WAIT);
    expect(note.textContent).toContain(OUTCOME_UNAVAILABLE_TEXT);
    fireEvent.click(screen.getByTestId('outcome-retry'));
    await screen.findByTestId('invoice-outcome-report', undefined, WAIT);
    expect(h.bffApiCall).toHaveBeenCalledTimes(2);
  });

  it('a degraded answer is the unavailable state, not a report with a missing list', async () => {
    h.bffApiCall.mockResolvedValue(outcome({ degraded: true, items: [] }));
    renderSection();
    await screen.findByTestId('outcome-unavailable', undefined, WAIT);
    expect(screen.queryByTestId('invoice-outcome-report')).toBeNull();
  });

  it('the record Refresh (a new refreshSignal) reads again', async () => {
    h.bffApiCall.mockResolvedValue(outcome());
    const view = renderSection({ refreshSignal: 0 });
    await screen.findByTestId('invoice-outcome-report', undefined, WAIT);
    view.rerender(
      <FluentProvider theme={webLightTheme}>
        <OutcomeSection {...sectionContext({ refreshSignal: 1 })} />
      </FluentProvider>
    );
    await waitFor(() => expect(h.bffApiCall).toHaveBeenCalledTimes(2), WAIT);
  });
});

// ---------------------------------------------------------------------------
// Pure helpers
// ---------------------------------------------------------------------------

describe('helpers', () => {
  it('groups lines (by line number, then date) before invoice-level items, keeping the server order on a tie', () => {
    const items = parsed(
      outcome({
        items: [
          {
            decision: 'Adjustment',
            adjustment: { method: 'Fixed', value: 10, amount: 10 },
            reason: 'a',
            firmReasons: [],
            citations: [],
          },
          lineItem({ reason: 'late', line: { ...lineItem().line, date: '2026-05-01' } }),
          lineItem({ reason: 'early', line: { ...lineItem().line, date: '2026-01-01' } }),
          lineItem({ reason: 'same-1', line: { ...lineItem().line, date: '2026-03-01' } }),
          lineItem({ reason: 'same-2', line: { ...lineItem().line, date: '2026-03-01' } }),
        ],
      })
    ).items;
    const { lines, invoice } = groupOutcomeItems(items);
    expect(lines.map(i => i.reason)).toEqual(['early', 'same-1', 'same-2', 'late']);
    expect(invoice.map(i => i.decision)).toEqual(['Adjustment']);
  });

  it('computes the difference in whole cents', () => {
    expect(outcomeDifference(0.3, 0.1)).toBe(0.2);
    expect(outcomeDifference(1500, 1250)).toBe(250);
    expect(outcomeDifference(100, 120)).toBe(-20);
  });

  it('an approved total that is greater than the billed total reads as an increase', () => {
    renderReport(outcome({ billedTotal: 100, approvedTotal: 120, items: [] }));
    expect(screen.getByTestId('outcome-reconciliation').textContent).toContain('Increase');
    expect(screen.getByTestId('outcome-reconciliation-difference').textContent).toBe('20.00');
  });
});
