import '@testing-library/jest-dom';
import * as fs from 'fs';
import * as path from 'path';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { InvoiceReviewPage } from '../components/InvoiceReviewPage';
import { LINE_FINDING_FALLBACK } from '../services/reviewModel';
import {
  ADDED_LINE_ID,
  INVOICE_ID,
  failed,
  field,
  finding,
  flagged,
  forbidden,
  line,
  lineId,
  listSourceFiles,
  mockApi,
  ok,
  problem,
  review,
  type MockApi,
} from './testKit';

function renderPage(api: MockApi, opts: { theme?: typeof webLightTheme; pollIntervalMs?: number; openRecord?: jest.Mock; firmNeeded?: boolean; initialLineId?: string } = {}) {
  return render(
    <FluentProvider theme={opts.theme ?? webLightTheme}>
      <InvoiceReviewPage
        invoiceId={INVOICE_ID}
        initialLineId={opts.initialLineId}
        api={api as never}
        openRecord={opts.openRecord}
        pollIntervalMs={opts.pollIntervalMs ?? 60_000}
        firmNeeded={opts.firmNeeded}
      />
    </FluentProvider>
  );
}

const normal = () =>
  review([
    line(1),
    flagged(2),
    line(3, { status: 'Pending', statusLabel: 'Processing' }),
    flagged(4),
    line(5, { status: 'Accepted', statusLabel: 'Accepted', approvedAmount: 1125, reviewedBy: { id: 'u', name: 'Reviewer One' }, reviewedOn: '2026-11-03T09:00:00Z' }),
  ]);

const row = (n: number) => screen.getByTestId(`line-row-${lineId(n)}`);

describe('InvoiceReviewPage: launch line (EB-D-194)', () => {
  it('preselects a known launch line through the click path and scrolls it into view; an unknown one keeps the default', async () => {
    const scrolled: Element[] = [];
    const original = Element.prototype.scrollIntoView;
    Element.prototype.scrollIntoView = function (this: Element) {
      scrolled.push(this);
    };
    try {
      const known = renderPage(mockApi(normal()), { initialLineId: lineId(4) });
      expect(await screen.findByTestId(`pane-line-${lineId(4)}`)).toBeInTheDocument();
      expect(row(4)).toHaveAttribute('aria-selected', 'true');
      expect(row(2)).toHaveAttribute('aria-selected', 'false');
      await waitFor(() => expect(scrolled).toContain(row(4)));
      known.unmount();

      renderPage(mockApi(normal()), { initialLineId: 'ffffffff-ffff-4fff-8fff-ffffffffffff' });
      await screen.findByTestId('review-title');
      expect(screen.queryByTestId(/^pane-line-/)).not.toBeInTheDocument();
      expect(screen.queryByTestId('notice')).not.toBeInTheDocument();
      screen.getAllByTestId(/^line-row-/).forEach(r => expect(r).toHaveAttribute('aria-selected', 'false'));
    } finally {
      Element.prototype.scrollIntoView = original;
    }
  });
});

describe('InvoiceReviewPage: layout and states', () => {
  it('shows a spinner, then the header, totals, count filters and lines from the read model', async () => {
    const api = mockApi(normal());
    const openRecord = jest.fn();
    renderPage(api, { openRecord });
    expect(screen.getByTestId('review-loading')).toBeInTheDocument();

    expect(await screen.findByTestId('review-title')).toHaveTextContent('Example Firm LLP · Invoice INV-2025-78232');
    expect(screen.getByText('INV-2025-78232.3')).toBeInTheDocument();
    expect(screen.getByTestId('total-billed')).toHaveTextContent('5,000.00');
    expect(screen.getByTestId('total-approved')).toHaveTextContent('5,000.00');

    const filters = screen.getByRole('group', { name: 'Line filters' });
    expect(within(filters).getByRole('button', { name: /All lines, 5 lines/ })).toHaveAttribute('aria-pressed', 'true');
    expect(within(filters).getByRole('button', { name: /To decide, 2 lines/ })).toHaveAttribute('aria-pressed', 'false');
    expect(within(filters).getByRole('button', { name: /Processing, 1 line/ })).toBeInTheDocument();

    expect(api.getReview).toHaveBeenCalledWith(INVOICE_ID);
    expect(api.listCorrections).toHaveBeenCalledWith(INVOICE_ID);
    expect(screen.getAllByTestId(/^line-row-/)).toHaveLength(5);

    fireEvent.click(screen.getByTestId('core-link'));
    expect(openRecord).toHaveBeenCalledWith('sprk_matter', expect.any(String));
  });

  it('filters the grid by a count card and returns to All when it is chosen again', async () => {
    renderPage(mockApi(normal()));
    await screen.findByTestId('review-title');
    const filters = screen.getByRole('group', { name: 'Line filters' });
    fireEvent.click(within(filters).getByRole('button', { name: /To decide, 2 lines/ }));
    expect(screen.getAllByTestId(/^line-row-/)).toHaveLength(2);
    fireEvent.click(within(filters).getByRole('button', { name: /To decide, 2 lines/ }));
    expect(screen.getAllByTestId(/^line-row-/)).toHaveLength(5);
  });

  it('shows a Pending line as Processing with no tick and no decision', async () => {
    renderPage(mockApi(normal()));
    await screen.findByTestId('review-title');
    expect(screen.getByTestId(`processing-${lineId(3)}`)).toHaveTextContent('Processing');
    expect(within(row(3)).queryByRole('checkbox')).not.toBeInTheDocument();
    expect(within(row(2)).getByRole('checkbox')).toBeInTheDocument();

    fireEvent.click(row(3));
    const pane = await screen.findByTestId(`pane-line-${lineId(3)}`);
    expect(within(pane).getAllByText('Processing').length).toBeGreaterThan(0);
    expect(within(pane).queryByTestId('decision-confirm')).not.toBeInTheDocument();
    expect(within(pane).queryByTestId('correct-Hours')).not.toBeInTheDocument();
  });

  it('re-reads the review while a line is Processing and stops when none is', async () => {
    jest.useFakeTimers();
    try {
      const ready = review([line(1), flagged(2), line(3)]);
      const api = mockApi([normal(), ready]);
      renderPage(api, { pollIntervalMs: 1000 });
      await act(async () => {
        await Promise.resolve();
      });
      await screen.findByTestId('review-title');
      expect(api.getReview).toHaveBeenCalledTimes(1);

      await act(async () => {
        jest.advanceTimersByTime(1000);
      });
      await waitFor(() => expect(api.getReview).toHaveBeenCalledTimes(2));
      await waitFor(() => expect(screen.queryByTestId(`processing-${lineId(3)}`)).not.toBeInTheDocument());

      await act(async () => {
        jest.advanceTimersByTime(5000);
      });
      expect(api.getReview).toHaveBeenCalledTimes(2);
    } finally {
      jest.useRealTimers();
    }
  });

  it('shows the kill-switch notice when eBilling is disabled, with no retry', async () => {
    const api = mockApi(normal());
    api.getReview.mockResolvedValue(failed(problem(503, 'ebilling.disabled')));
    renderPage(api);
    const err = await screen.findByTestId('review-load-error');
    expect(err).toHaveTextContent('eBilling is not enabled in this environment.');
    expect(within(err).queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument();
  });

  it('is read-only for a viewer the server says cannot decide: no ticks, no decision, no Correct', async () => {
    const api = mockApi(review([flagged(1), line(2)], { flags: { canRead: true, canDecide: false } }));
    renderPage(api);
    await screen.findByTestId('review-title');
    expect(screen.getByTestId('readonly-notice')).toBeInTheDocument();
    expect(screen.queryAllByRole('checkbox')).toHaveLength(0);
    expect(screen.queryByTestId('accept-clean')).not.toBeInTheDocument();
    expect(screen.queryByTestId('correct-header')).not.toBeInTheDocument();
    fireEvent.click(row(1));
    const pane = await screen.findByTestId(`pane-line-${lineId(1)}`);
    expect(within(pane).queryByTestId('decision-confirm')).not.toBeInTheDocument();
    expect(within(pane).queryByTestId('correct-Hours')).not.toBeInTheDocument();
  });

  it('shows a decided invoice read-only with its status', async () => {
    renderPage(mockApi(review([line(1, { status: 'Accepted', statusLabel: 'Accepted' })], { header: { ...review([]).header, status: 'Approved' } })));
    await screen.findByTestId('review-title');
    expect(screen.getByTestId('readonly-notice')).toHaveTextContent('This invoice is approved; it is shown read-only.');
    expect(screen.getAllByText('Approved').length).toBeGreaterThan(0);
  });

  it('shows the decided state of a decided line through the StatusBar override', async () => {
    renderPage(mockApi(normal()));
    await screen.findByTestId('review-title');
    fireEvent.click(row(5));
    const pane = await screen.findByTestId(`pane-line-${lineId(5)}`);
    const bar = within(pane).getByTestId('status-bar');
    expect(bar).toHaveTextContent('Accepted');
    expect(bar).toHaveTextContent('by Reviewer One');
    expect(within(pane).queryByTestId('decision-confirm')).not.toBeInTheDocument();
  });

  it('shows each line finding as a reason (fixed text when its message is null) and counts it in the badge', async () => {
    const l = line(1, {
      status: 'Flagged',
      statusLabel: 'Flagged',
      findingCount: 0,
      lineFindings: [
        { code: 'coding.not_returned', message: 'The AI returned no codes.' },
        { code: 'line.no_description', message: null },
      ],
    });
    renderPage(mockApi(review([l])));
    await screen.findByTestId('review-title');
    expect(screen.getByTestId(`findings-${lineId(1)}`)).toHaveTextContent('2');
    fireEvent.click(row(1));
    const pane = await screen.findByTestId(`pane-line-${lineId(1)}`);
    const reasons = within(pane).getAllByTestId('line-finding').map(e => e.textContent);
    expect(reasons).toEqual(['The AI returned no codes.', LINE_FINDING_FALLBACK['line.no_description']]);
  });

  it('marks an unmatched timekeeper, a low-confidence value and an AI observation in the grid', async () => {
    const l = flagged(1, {
      timekeeper: null,
      timekeeperAsBilled: 'Unknown Person',
      fields: { ...line(1).fields, Hours: field({ lowConfidence: true, combined: 0.4 }) },
      findings: [finding({ valuesRead: [{ name: 'Block billing', value: 'true', field: 'Observation.BlockBilling', confidence: 0.8, belowReliabilityThreshold: false }] })],
    });
    renderPage(mockApi(review([l])));
    await screen.findByTestId('review-title');
    expect(screen.getByTestId(`unmatched-${lineId(1)}`)).toBeInTheDocument();
    expect(screen.getByTestId(`lowconf-${lineId(1)}-Hours`)).toBeInTheDocument();
    expect(screen.getByTestId(`aiobs-${lineId(1)}`)).toBeInTheDocument();
  });

  it('renders under the dark theme with the same rows as the light theme', async () => {
    // jsdom computes no colours, so what keeps the page theme-correct is the colour-literal source guard below; this checks that the page
    // mounts and renders its content under the dark provider.
    renderPage(mockApi(normal()), { theme: webDarkTheme });
    expect(await screen.findByTestId('invoice-review')).toBeInTheDocument();
    expect(screen.getAllByTestId(/^line-row-/)).toHaveLength(5);
  });
});

describe('InvoiceReviewPage: decisions (task 100)', () => {
  it('decides the first line to decide with the suggested Reduce, shows the server totals and moves to the next line', async () => {
    const after = review([line(1), flagged(2, { status: 'Reduced', statusLabel: 'Reduced', approvedAmount: 1012.5 }), line(3, { status: 'Pending' }), flagged(4), line(5, { status: 'Accepted' })], {
      totals: { billed: 5000, fees: 5000, expenses: 0, lineAdjustments: -112.5, invoiceAdjustments: 0, approved: 4887.5 },
    });
    const api = mockApi([normal(), after]);
    renderPage(api);
    await screen.findByTestId('review-title');

    fireEvent.click(screen.getByTestId('start-first'));
    await screen.findByTestId(`pane-line-${lineId(2)}`);
    // Suggestion first and preselected; nothing is sent until Confirm.
    expect(screen.getByRole('radio', { name: /Suggested: reduce to 1,012.50/ })).toBeChecked();
    expect(api.decideLine).not.toHaveBeenCalled();
    fireEvent.change(screen.getByLabelText(`Reason for line 2`), { target: { value: 'Above the approved rate' } });
    fireEvent.click(screen.getByTestId('decision-confirm'));

    await waitFor(() => expect(api.decideLine).toHaveBeenCalledTimes(1));
    expect(api.decideLine).toHaveBeenCalledWith(INVOICE_ID, lineId(2), {
      decisionId: expect.any(String),
      action: 'reduce',
      reason: 'Above the approved rate',
      approvedAmount: 1012.5,
    });
    await waitFor(() => expect(screen.getByTestId('total-approved')).toHaveTextContent('4,887.50'));
    expect(screen.getByTestId('total-line-adj')).toHaveTextContent('−112.50');
    expect(await screen.findByTestId(`pane-line-${lineId(4)}`)).toBeInTheDocument();
    expect(screen.getByTestId('notice')).toHaveTextContent('Line 2 reduced to 1,012.50');
  });

  it('shows the server refusal of a Reduce without a reason and changes nothing', async () => {
    const api = mockApi(normal());
    api.decideLine.mockResolvedValue(failed(problem(400, 'ebilling.line.reason_required', { detail: 'A reason is required to reduce a line.' })));
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(row(2));
    await screen.findByTestId(`pane-line-${lineId(2)}`);
    fireEvent.click(screen.getByRole('radio', { name: 'Reduce…' }));
    fireEvent.change(screen.getByLabelText('Approved amount for line 2'), { target: { value: '900' } });
    fireEvent.click(screen.getByTestId('decision-confirm'));

    await waitFor(() => expect(screen.getByTestId('notice')).toHaveTextContent('A reason is required to reduce a line.'));
    expect(api.decideLine.mock.calls[0][2]).toMatchObject({ action: 'reduce', reason: null, approvedAmount: 900 });
    expect(api.getReview).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId('total-approved')).toHaveTextContent('5,000.00');
    expect(screen.getByTestId(`pane-line-${lineId(2)}`)).toBeInTheDocument();
  });

  it('turns the page read-only after a 403 on a write', async () => {
    const api = mockApi(normal());
    api.decideLine.mockResolvedValue(failed(problem(403, 'ebilling.decision.not_authorized', { detail: 'You cannot decide lines on this invoice.' })));
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(row(2));
    await screen.findByTestId(`pane-line-${lineId(2)}`);
    fireEvent.click(screen.getByTestId('decision-confirm'));
    await waitFor(() => expect(screen.getByTestId('readonly-notice')).toBeInTheDocument());
    expect(screen.queryByTestId('decision-confirm')).not.toBeInTheDocument();
    expect(screen.queryAllByRole('checkbox')).toHaveLength(0);
  });

  it('offers Repeat after a 503 incomplete and repeats with the same decision id', async () => {
    const api = mockApi(normal());
    api.decideLine
      .mockResolvedValueOnce(failed(problem(503, 'ebilling.decision.incomplete', { detail: 'The line was saved; the record was not.', failedStep: 'record' })))
      .mockResolvedValueOnce(ok({ decisionId: 'x', lines: [], totals: null }));
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(row(2));
    await screen.findByTestId(`pane-line-${lineId(2)}`);
    fireEvent.click(screen.getByTestId('decision-confirm'));
    fireEvent.click(await screen.findByTestId('notice-repeat'));
    await waitFor(() => expect(api.decideLine).toHaveBeenCalledTimes(2));
    expect(api.decideLine.mock.calls[1][2].decisionId).toBe(api.decideLine.mock.calls[0][2].decisionId);
  });

  it('accepts all clean lines in one request', async () => {
    const api = mockApi(normal());
    renderPage(api);
    await screen.findByTestId('review-title');
    expect(screen.getByTestId('accept-clean')).toHaveTextContent('Accept all clean lines (1)');
    fireEvent.click(screen.getByTestId('accept-clean'));
    await waitFor(() => expect(api.acceptClean).toHaveBeenCalledTimes(1));
    expect(api.acceptClean).toHaveBeenCalledWith(INVOICE_ID, expect.any(String));
    await waitFor(() => expect(api.getReview).toHaveBeenCalledTimes(2));
  });

  it('disallows ticked rows with one reason in one request', async () => {
    const api = mockApi(normal());
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(within(row(2)).getByRole('checkbox'));
    fireEvent.click(within(row(4)).getByRole('checkbox'));
    expect(screen.queryByTestId(`pane-line-${lineId(2)}`)).not.toBeInTheDocument();
    fireEvent.click(screen.getByTestId('bulk-disallow'));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Reason'), { target: { value: 'Duplicate entries' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Disallow 2 lines' }));
    await waitFor(() => expect(api.decideLines).toHaveBeenCalledTimes(1));
    expect(api.decideLines).toHaveBeenCalledWith(INVOICE_ID, {
      decisionId: expect.any(String),
      action: 'disallow',
      reason: 'Duplicate entries',
      lines: [
        { lineId: lineId(2), approvedAmount: null },
        { lineId: lineId(4), approvedAmount: null },
      ],
    });
  });

  it('applies a rule suggestion to its lines in one request with a per-line amount (By rule)', async () => {
    const api = mockApi(normal());
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(screen.getByRole('tab', { name: 'By rule' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Apply suggestion to 2 lines…' }));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Reason'), { target: { value: 'Approved rate' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Reduce 2 lines' }));
    await waitFor(() => expect(api.decideLines).toHaveBeenCalledTimes(1));
    expect(api.decideLines.mock.calls[0][1]).toMatchObject({
      action: 'reduce',
      reason: 'Approved rate',
      lines: [
        { lineId: lineId(2), approvedAmount: 1012.5 },
        { lineId: lineId(4), approvedAmount: 1012.5 },
      ],
    });
  });

  it('closes the bulk dialog on a 503 incomplete and repeats the same request from the page', async () => {
    const api = mockApi(normal());
    api.decideLines
      .mockResolvedValueOnce(failed(problem(503, 'ebilling.decision.incomplete', { detail: 'The lines were saved; the totals were not.', failedStep: 'totals' })))
      .mockResolvedValueOnce(ok({ decisionId: 'x', lines: [], totals: null }));
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(within(row(2)).getByRole('checkbox'));
    fireEvent.click(screen.getByTestId('bulk-accept'));
    const dialog = await screen.findByRole('dialog');
    expect(screen.getByText('Accept 1 selected line')).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Accept 1 line' }));
    // The change was saved: the form closes and the page offers Repeat (text query: it also sees content Fluent hides from roles).
    await waitFor(() => expect(screen.queryByText('Accept 1 selected line')).not.toBeInTheDocument());
    fireEvent.click(await screen.findByTestId('notice-repeat'));
    await waitFor(() => expect(api.decideLines).toHaveBeenCalledTimes(2));
    expect(api.decideLines.mock.calls[1][1]).toEqual(api.decideLines.mock.calls[0][1]);
  });

  it('offers Reload when the server says the line changed (409)', async () => {
    const api = mockApi(normal());
    api.decideLine.mockResolvedValue(failed(problem(409, 'ebilling.line.changed', { detail: 'The line changed. Reload and decide again.' })));
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(row(2));
    await screen.findByTestId(`pane-line-${lineId(2)}`);
    fireEvent.click(screen.getByTestId('decision-confirm'));
    fireEvent.click(await screen.findByTestId('notice-reload'));
    await waitFor(() => expect(api.getReview).toHaveBeenCalledTimes(2));
  });

  it('keeps a bulk refusal in the dialog', async () => {
    const api = mockApi(normal());
    api.decideLines.mockResolvedValue(failed(problem(400, 'ebilling.line.reason_required', { detail: 'A reason is required to disallow.' })));
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(within(row(2)).getByRole('checkbox'));
    fireEvent.click(screen.getByTestId('bulk-disallow'));
    const dialog = await screen.findByRole('dialog');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Disallow 1 line' }));
    expect(await within(dialog).findByTestId('bulk-error')).toHaveTextContent('A reason is required to disallow.');
  });
});

describe('InvoiceReviewPage: the invoice needs its firm first (095d-0, EB-D-185 / EB-D-197)', () => {
  const FIRM_SENTENCE = 'This invoice needs its firm first (or its billing rules are being checked again after the firm was set). Add the firm, then decide.';
  const SERVER_SENTENCE = 'This invoice has no firm. A reviewer must add the firm before its lines can be decided.';

  it('must-fire: disables Confirm (Accept, Reduce, Disallow), the ticked-row actions, Accept all clean, the By-rule suggestions and the corrections, and says why', async () => {
    const api = mockApi(normal());
    renderPage(api, { firmNeeded: true });
    await screen.findByTestId('review-title');

    expect(screen.getByTestId('firm-first-notice')).toHaveTextContent(FIRM_SENTENCE);
    expect(screen.getByTestId('accept-clean')).toBeDisabled();
    expect(screen.getByTestId('correct-header')).toBeDisabled();

    fireEvent.click(row(2));
    const pane = await screen.findByTestId(`pane-line-${lineId(2)}`);
    fireEvent.click(within(pane).getByRole('radio', { name: 'Disallow' }));
    expect(within(pane).getByTestId('decision-confirm')).toBeDisabled();
    expect(within(pane).getByTestId('decision-firm-first')).toHaveTextContent(FIRM_SENTENCE);
    expect(within(pane).getByTestId('correct-Hours')).toBeDisabled();

    fireEvent.click(within(row(2)).getByRole('checkbox'));
    expect(screen.getByTestId('bulk-accept')).toBeDisabled();
    expect(screen.getByTestId('bulk-disallow')).toBeDisabled();

    fireEvent.click(screen.getByRole('tab', { name: 'By rule' }));
    expect(await screen.findByRole('button', { name: 'Apply suggestion to 2 lines…' })).toBeDisabled();
    expect(api.decideLine).not.toHaveBeenCalled();
    expect(api.decideLines).not.toHaveBeenCalled();
    expect(api.acceptClean).not.toHaveBeenCalled();
  });

  it('must-not-fire: with no firm need (the default) every one of them is enabled and no notice is shown', async () => {
    renderPage(mockApi(normal()));
    await screen.findByTestId('review-title');

    expect(screen.queryByTestId('firm-first-notice')).not.toBeInTheDocument();
    expect(screen.getByTestId('accept-clean')).toBeEnabled();
    expect(screen.getByTestId('correct-header')).toBeEnabled();
    fireEvent.click(row(2));
    const pane = await screen.findByTestId(`pane-line-${lineId(2)}`);
    fireEvent.click(within(pane).getByRole('radio', { name: 'Disallow' }));
    expect(within(pane).getByTestId('decision-confirm')).toBeEnabled();
    expect(within(pane).queryByTestId('decision-firm-first')).not.toBeInTheDocument();
    expect(within(pane).getByTestId('correct-Hours')).toBeEnabled();
    fireEvent.click(within(row(2)).getByRole('checkbox'));
    expect(screen.getByTestId('bulk-accept')).toBeEnabled();
    expect(screen.getByTestId('bulk-disallow')).toBeEnabled();
  });

  it('keeps the bulk Reduce/Disallow dialog open on a 409 firm_missing, with the typed reason and the server sentence, and offers no Reload', async () => {
    const api = mockApi(normal());
    api.decideLines.mockResolvedValue(failed(problem(409, 'ebilling.line_decision.firm_missing', { detail: SERVER_SENTENCE })));
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(within(row(2)).getByRole('checkbox'));
    fireEvent.click(screen.getByTestId('bulk-disallow'));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Reason'), { target: { value: 'Rate above the agreed cap' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Disallow 1 line' }));

    expect(await within(dialog).findByTestId('bulk-error')).toHaveTextContent(SERVER_SENTENCE);
    expect(dialog).toBeInTheDocument();
    expect(within(dialog).getByLabelText('Reason')).toHaveValue('Rate above the agreed cap');
    expect(screen.queryByTestId('notice-reload')).not.toBeInTheDocument();
    expect(api.getReview).toHaveBeenCalledTimes(1);
  });

  it('keeps the pane form and its reason on a 409 firm_missing, shows the server sentence and offers no Reload', async () => {
    const api = mockApi(normal());
    api.decideLine.mockResolvedValue(failed(problem(409, 'ebilling.line_decision.firm_missing', { detail: SERVER_SENTENCE })));
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(row(2));
    const pane = await screen.findByTestId(`pane-line-${lineId(2)}`);
    fireEvent.click(within(pane).getByRole('radio', { name: 'Disallow' }));
    fireEvent.change(within(pane).getByLabelText('Reason for line 2'), { target: { value: 'Duplicate of line 1' } });
    fireEvent.click(within(pane).getByTestId('decision-confirm'));

    await waitFor(() => expect(screen.getByTestId('notice')).toHaveTextContent(SERVER_SENTENCE));
    expect(screen.queryByTestId('notice-reload')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Reason for line 2')).toHaveValue('Duplicate of line 1');
    expect(api.getReview).toHaveBeenCalledTimes(1);
  });

  it('must-not-fire: any other 409 still closes the dialog and offers Reload', async () => {
    const api = mockApi(normal());
    api.decideLines.mockResolvedValue(failed(problem(409, 'ebilling.line.changed', { detail: 'A line changed. Reload and decide again.' })));
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(within(row(2)).getByRole('checkbox'));
    fireEvent.click(screen.getByTestId('bulk-disallow'));
    const dialog = await screen.findByRole('dialog');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Disallow 1 line' }));
    expect(await screen.findByTestId('notice-reload')).toBeInTheDocument();
  });
});

describe('InvoiceReviewPage: failures and recovery (task 110 fix round 2)', () => {
  it('repeats a failed Accept all clean through the multi-line route with the committed ids and the same decision id', async () => {
    const api = mockApi(normal());
    api.acceptClean.mockResolvedValueOnce(
      failed(problem(503, 'ebilling.decision.incomplete', { detail: 'The lines were saved; the totals were not.', failedStep: 'totals', committedLineIds: [lineId(1)] }))
    );
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(screen.getByTestId('accept-clean'));
    fireEvent.click(await screen.findByTestId('notice-repeat'));
    await waitFor(() => expect(api.decideLines).toHaveBeenCalledTimes(1));
    // Not accept-clean again (its lines are no longer Clean: the server would answer 200 and apply nothing).
    expect(api.acceptClean).toHaveBeenCalledTimes(1);
    expect(api.decideLines).toHaveBeenCalledWith(INVOICE_ID, {
      decisionId: api.acceptClean.mock.calls[0][1],
      action: 'accept',
      reason: null,
      lines: [{ lineId: lineId(1), approvedAmount: null }],
    });
  });

  it('F3-2: Accept all clean keeps its multi-line Repeat (same body, same decision id) after a Repeat that got no answer', async () => {
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    try {
      const api = mockApi(normal());
      api.acceptClean.mockResolvedValueOnce(
        failed(problem(503, 'ebilling.decision.incomplete', { detail: 'The lines were saved; the totals were not.', failedStep: 'totals', committedLineIds: [lineId(1)] }))
      );
      api.decideLines.mockResolvedValueOnce(failed(new TypeError('Failed to fetch')));
      renderPage(api);
      await screen.findByTestId('review-title');
      fireEvent.click(screen.getByTestId('accept-clean'));

      // First Repeat: the multi-line route; it gets no answer, so Repeat stays.
      fireEvent.click(await screen.findByTestId('notice-repeat'));
      await waitFor(() => expect(api.decideLines).toHaveBeenCalledTimes(1));
      await waitFor(() => expect(screen.getByTestId('notice')).toHaveTextContent('No answer came back, so the repeat may not have finished'));

      // Second Repeat: the same closure again (a no-answer failure carries no committed ids to choose a route from), and it goes through.
      fireEvent.click(screen.getByTestId('notice-repeat'));
      await waitFor(() => expect(api.decideLines).toHaveBeenCalledTimes(2));
      await waitFor(() => expect(screen.getByTestId('notice')).toHaveTextContent('Clean lines accepted'));

      expect(api.acceptClean).toHaveBeenCalledTimes(1);
      const [first, second] = api.decideLines.mock.calls.map(c => c[1]);
      expect(second).toEqual(first);
      expect(first).toEqual({ decisionId: api.acceptClean.mock.calls[0][1], action: 'accept', reason: null, lines: [{ lineId: lineId(1), approvedAmount: null }] });
    } finally {
      warn.mockRestore();
    }
  });

  it('shows the server state before offering Repeat after a 503 incomplete', async () => {
    const api = mockApi(normal());
    api.decideLine.mockResolvedValueOnce(failed(problem(503, 'ebilling.decision.incomplete', { detail: 'The line was saved; the record was not.' })));
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(row(2));
    await screen.findByTestId(`pane-line-${lineId(2)}`);
    fireEvent.click(screen.getByTestId('decision-confirm'));
    await screen.findByTestId('notice-repeat');
    // The page re-read the review, so it no longer shows the pre-write state next to a Repeat.
    expect(api.getReview).toHaveBeenCalledTimes(2);
  });

  it('sends no reason for an Accept and offers no reason field for it', async () => {
    const api = mockApi(normal());
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(row(1));
    await screen.findByTestId(`pane-line-${lineId(1)}`);
    fireEvent.click(screen.getByRole('radio', { name: /Accept at/ }));
    expect(screen.queryByLabelText('Reason for line 1')).not.toBeInTheDocument();
    fireEvent.click(screen.getByTestId('decision-confirm'));
    await waitFor(() => expect(api.decideLine).toHaveBeenCalledTimes(1));
    expect(api.decideLine.mock.calls[0][2]).toEqual({ decisionId: expect.any(String), action: 'accept', reason: null, approvedAmount: null });
  });

  it('asks for no reason in the bulk Accept form and sends null', async () => {
    const api = mockApi(normal());
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(within(row(1)).getByRole('checkbox'));
    fireEvent.click(screen.getByTestId('bulk-accept'));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).queryByLabelText('Reason')).not.toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Accept 1 line' }));
    await waitFor(() => expect(api.decideLines).toHaveBeenCalledTimes(1));
    expect(api.decideLines.mock.calls[0][1]).toMatchObject({ action: 'accept', reason: null });
  });

  it('closes a dialog on a 409 and offers Reload on the page (bulk)', async () => {
    const api = mockApi(normal());
    api.decideLines.mockResolvedValue(failed(problem(409, 'ebilling.line.changed', { detail: 'A line changed. Reload and decide again.' })));
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(within(row(2)).getByRole('checkbox'));
    fireEvent.click(screen.getByTestId('bulk-disallow'));
    const dialog = await screen.findByRole('dialog');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Disallow 1 line' }));
    await waitFor(() => expect(screen.queryByText('Disallow 1 selected line')).not.toBeInTheDocument());
    expect(screen.getByTestId('notice')).toHaveTextContent('A line changed. Reload and decide again.');
    expect(screen.queryByTestId('bulk-error')).not.toBeInTheDocument();
    fireEvent.click(screen.getByTestId('notice-reload'));
    await waitFor(() => expect(api.getReview).toHaveBeenCalledTimes(2));
  });

  it('closes the Correct value form on a 409 and offers Reload on the page', async () => {
    const api = mockApi(normal());
    api.correctLine.mockResolvedValue(failed(problem(409, 'ebilling.correction.line_changed', { detail: 'The line changed. Reload and correct it again.' })));
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(row(2));
    fireEvent.click(within(await screen.findByTestId(`pane-line-${lineId(2)}`)).getByTestId('correct-Hours'));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Correct value'), { target: { value: '2' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save correction' }));
    await waitFor(() => expect(screen.queryByTestId('correct-error')).not.toBeInTheDocument());
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(await screen.findByTestId('notice-reload')).toBeInTheDocument();
  });

  it('keeps polling after one failed re-read, offers Reload, and clears the notice when a later re-read works', async () => {
    jest.useFakeTimers();
    try {
      const ready = review([line(1), flagged(2), line(3)]);
      const api = mockApi(normal());
      api.getReview
        .mockResolvedValueOnce(ok(normal()))
        .mockResolvedValueOnce(failed(problem(503, null)))
        .mockResolvedValue(ok(ready));
      renderPage(api, { pollIntervalMs: 1000 });
      await act(async () => {
        await Promise.resolve();
      });
      await screen.findByTestId('review-title');

      await act(async () => {
        jest.advanceTimersByTime(1000);
      });
      await waitFor(() => expect(api.getReview).toHaveBeenCalledTimes(2));
      // The re-read failed: what is shown stays, and the notice offers Reload.
      expect(await screen.findByTestId('notice-reload')).toBeInTheDocument();
      expect(screen.getByTestId('notice')).toHaveTextContent('temporarily unavailable');
      expect(screen.getByTestId(`processing-${lineId(3)}`)).toBeInTheDocument();

      // Polling was not abandoned: the next tick re-reads and the page recovers by itself.
      await act(async () => {
        jest.advanceTimersByTime(1000);
      });
      await waitFor(() => expect(api.getReview).toHaveBeenCalledTimes(3));
      await waitFor(() => expect(screen.queryByTestId(`processing-${lineId(3)}`)).not.toBeInTheDocument());
      expect(screen.queryByTestId('notice')).not.toBeInTheDocument();
    } finally {
      jest.useRealTimers();
    }
  });

  it('says so when a write was saved but the page could not refresh, with Reload', async () => {
    const api = mockApi(normal());
    renderPage(api);
    await screen.findByTestId('review-title');
    api.getReview.mockResolvedValue(failed(problem(503, null)));
    fireEvent.click(row(1));
    await screen.findByTestId(`pane-line-${lineId(1)}`);
    fireEvent.click(screen.getByRole('radio', { name: /Accept at/ }));
    fireEvent.click(screen.getByTestId('decision-confirm'));
    await waitFor(() => expect(screen.getByTestId('notice')).toHaveTextContent('could not refresh'));
    expect(screen.getByTestId('notice')).toHaveAttribute('data-intent', 'warning');
    expect(screen.getByTestId('notice-reload')).toBeInTheDocument();
  });

  it("does not report a plain Saved when the write's re-read loses to a newer poll read that failed", async () => {
    jest.useFakeTimers();
    try {
      const api = mockApi(normal());
      let call = 0;
      let answerWriteRead: (r: Awaited<ReturnType<MockApi['getReview']>>) => void = () => undefined;
      api.getReview.mockImplementation(() => {
        call += 1;
        if (call === 1) return Promise.resolve(ok(normal())); // the first read
        if (call === 2) return new Promise(resolve => (answerWriteRead = resolve)); // the write's re-read: slow
        if (call === 3) return Promise.resolve(failed(problem(503, null))); // a poll read that starts later and fails first
        return Promise.resolve(ok(normal()));
      });
      renderPage(api, { pollIntervalMs: 1000 });
      await act(async () => {
        await Promise.resolve();
      });
      await screen.findByTestId('review-title');
      fireEvent.click(row(2));
      await screen.findByTestId(`pane-line-${lineId(2)}`);
      fireEvent.click(screen.getByRole('radio', { name: /Accept at/ }));
      fireEvent.click(screen.getByTestId('decision-confirm'));
      await waitFor(() => expect(api.getReview).toHaveBeenCalledTimes(2));

      // The poll tick starts the newer read; it fails and is applied first.
      await act(async () => {
        jest.advanceTimersByTime(1000);
      });
      await waitFor(() => expect(api.getReview).toHaveBeenCalledTimes(3));
      await waitFor(() => expect(screen.getByTestId('notice')).toHaveTextContent('temporarily unavailable'));

      // Now the older answer (the write's re-read) arrives: it is dropped as stale, and the page it would have refreshed is not current.
      await act(async () => {
        answerWriteRead(ok(normal()));
      });
      await waitFor(() => expect(screen.getByTestId('notice')).toHaveTextContent('could not refresh'));
      expect(screen.getByTestId('notice')).toHaveAttribute('data-intent', 'warning');
      expect(screen.getByTestId('notice-reload')).toBeInTheDocument();
    } finally {
      jest.useRealTimers();
    }
  });

  it('shows the connection sentence, never the browser text, when a write gets no server answer', async () => {
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    try {
      const api = mockApi(normal());
      api.decideLine.mockResolvedValue(failed(new TypeError('Failed to fetch')));
      renderPage(api);
      await screen.findByTestId('review-title');
      fireEvent.click(row(2));
      await screen.findByTestId(`pane-line-${lineId(2)}`);
      fireEvent.click(screen.getByTestId('decision-confirm'));
      await waitFor(() => expect(screen.getByTestId('notice')).toHaveTextContent('The change could not be sent. Check your connection and retry.'));
      expect(screen.getByTestId('notice')).not.toHaveTextContent('Failed to fetch');
    } finally {
      warn.mockRestore();
    }
  });
});

describe('InvoiceReviewPage: corrections (task 101) and the PDF', () => {
  const lowHours = () =>
    review([
      flagged(7, {
        quantity: 25,
        fields: { ...line(7).fields, Hours: field({ lowConfidence: true, combined: 0.42, box: { page: 1, x: 0.6, y: 0.5, w: 0.05, h: 0.02 } }) },
        findings: [finding({ policyCode: 'HOURS-DAY', valuesRead: [{ name: 'Hours', value: '25', field: 'Hours', confidence: 0.42, belowReliabilityThreshold: true }] })],
      }),
    ]);

  it('boxes the clicked value on the PDF', async () => {
    const api = mockApi(lowHours());
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(screen.getByRole('button', { name: /Hours, line 7, check this value/ }));
    await screen.findByTestId(`pane-line-${lineId(7)}`);
    const viewer = screen.getByTestId('pdf-viewer');
    const h = JSON.parse(viewer.getAttribute('data-highlight')!);
    expect(h.page).toBe(1);
    expect(h.field).toEqual({ x: 0.6, y: 0.5, w: 0.05, h: 0.02 });
    expect(h.label).toBe('Line 7 · Hours');
    await waitFor(() => expect(api.fetchFile).toHaveBeenCalled());
  });

  it('corrects a misread value through the correction route, never a decision', async () => {
    const corrected = review([line(7, { quantity: 2.5 })]);
    const api = mockApi([lowHours(), corrected]);
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(row(7));
    const pane = await screen.findByTestId(`pane-line-${lineId(7)}`);
    fireEvent.click(within(pane).getByText(/Compare with the document, then confirm or correct/));
    expect(JSON.parse(screen.getByTestId('pdf-viewer').getAttribute('data-highlight')!).label).toBe('Line 7 · Hours');

    fireEvent.click(within(pane).getByTestId('correct-Hours'));
    const dialog = await screen.findByRole('dialog');
    expect(dialog).toHaveTextContent('As read: 25');
    expect(screen.getByText('Correct hours · line 7')).toBeInTheDocument();
    fireEvent.change(within(dialog).getByLabelText('Correct value'), { target: { value: '2.5' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save correction' }));

    await waitFor(() => expect(api.correctLine).toHaveBeenCalledWith(INVOICE_ID, lineId(7), { field: 'Hours', value: '2.5', confirm: false }));
    expect(api.decideLine).not.toHaveBeenCalled();
    expect(api.decideLines).not.toHaveBeenCalled();
    await waitFor(() => expect(screen.queryByText('Correct hours · line 7')).not.toBeInTheDocument());
    // The line is Clean after the server re-ran the rules.
    expect(within(row(7)).getByText('Clean')).toBeInTheDocument();
    // The "Suggested: reduce" choice made against the old finding did not survive: nothing is chosen, so Confirm cannot send it.
    const confirm = await screen.findByTestId('decision-confirm');
    expect(confirm).toBeDisabled();
    expect(screen.queryByRole('radio', { name: /Suggested/ })).not.toBeInTheDocument();
    fireEvent.click(confirm);
    expect(api.decideLine).not.toHaveBeenCalled();
  });

  it('confirms a low-confidence value as read', async () => {
    const api = mockApi(lowHours());
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(row(7));
    const pane = await screen.findByTestId(`pane-line-${lineId(7)}`);
    fireEvent.click(within(pane).getByTestId('confirm-Hours'));
    await waitFor(() => expect(api.correctLine).toHaveBeenCalledWith(INVOICE_ID, lineId(7), { field: 'Hours', value: null, confirm: true }));
  });

  it('keeps a refused correction in the dialog with the server message', async () => {
    const api = mockApi(lowHours());
    api.correctLine.mockResolvedValue(failed(problem(400, 'ebilling.correction.value_invalid', { detail: 'Hours must be a number.' })));
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(row(7));
    fireEvent.click(within(await screen.findByTestId(`pane-line-${lineId(7)}`)).getByTestId('correct-Hours'));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Correct value'), { target: { value: 'x' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save correction' }));
    expect(await within(dialog).findByTestId('correct-error')).toHaveTextContent('Hours must be a number.');
  });

  it('corrects a header value through the header route', async () => {
    const api = mockApi(normal());
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(screen.getByTestId('correct-header'));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Correct value'), { target: { value: 'INV-2025-78233' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save correction' }));
    await waitFor(() =>
      expect(api.correctHeader).toHaveBeenCalledWith(INVOICE_ID, { field: 'FirmInvoiceNumber', value: 'INV-2025-78233', confirm: false })
    );
  });

  it('says so when the invoice has no PDF', async () => {
    renderPage(mockApi(review([line(1)], { invoicePdfFileId: null })));
    expect(await screen.findByTestId('no-pdf')).toBeInTheDocument();
  });
});

/** CSS named colours a style value might use. */
const NAMED_COLOURS = new Set([
  'white', 'black', 'red', 'green', 'blue', 'yellow', 'orange', 'purple', 'pink', 'brown', 'gray', 'grey', 'silver', 'gold', 'navy', 'teal',
  'maroon', 'olive', 'lime', 'aqua', 'cyan', 'magenta', 'fuchsia', 'ivory', 'beige', 'tan', 'khaki', 'coral', 'salmon', 'crimson', 'indigo',
  'violet', 'lavender', 'turquoise', 'azure', 'whitesmoke', 'gainsboro', 'lightgray', 'lightgrey', 'darkgray', 'darkgrey', 'dimgray',
  'slategray', 'darkred', 'darkgreen', 'darkblue', 'lightblue', 'lightgreen', 'skyblue', 'royalblue', 'steelblue', 'orangered', 'tomato',
  'firebrick',
]);

/**
 * Colour literals in source text: hex colours, rgb()/rgba()/hsl()/hsla(), and CSS named colours used as a style value. Comments are ignored. A
 * named colour counts only inside the quoted value of a style property (`color: 'white'`, `border: '1px solid red'`), so prose and ids do not
 * trip it. Fluent tokens, `transparent`, `inherit` and `currentColor` are not literals.
 */
function findColourLiterals(source: string): string[] {
  const text = source.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:'"`])\/\/.*$/gm, '$1');
  const found: string[] = [];
  for (const m of text.matchAll(/(^|[^\w&#])(#[0-9a-fA-F]{3,8})(?![\w-])/g)) found.push(m[2]);
  for (const m of text.matchAll(/\b(rgba?|hsla?)\(/g)) found.push(`${m[1]}(`);
  const prop =
    /\b(?:color|backgroundColor|background|border\w*|outline\w*|fill|stroke|boxShadow|textDecorationColor|caretColor|accentColor)\s*:\s*(['"`])([^'"`]*)\1/g;
  for (const m of text.matchAll(prop)) {
    for (const word of m[2].split(/[^A-Za-z]+/)) if (NAMED_COLOURS.has(word.toLowerCase())) found.push(word);
  }
  return found;
}

describe('InvoiceReviewPage: Add line (task 164b)', () => {
  const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

  /** Open the form and fill a Fee: date, timekeeper, hours and rate, a task code, a description and a reason. */
  async function openAndFillFee() {
    fireEvent.click(await screen.findByTestId('add-line'));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Date'), { target: { value: '2025-03-14' } });
    fireEvent.change(within(dialog).getByLabelText('Timekeeper'), { target: { value: 'B. Associate' } });
    fireEvent.change(within(dialog).getByLabelText('Hours'), { target: { value: '2.5' } });
    fireEvent.change(within(dialog).getByLabelText('Rate'), { target: { value: '300' } });
    fireEvent.change(within(dialog).getByLabelText('Task code'), { target: { value: 'L110' } });
    fireEvent.change(within(dialog).getByLabelText('Description'), { target: { value: 'Call with the client' } });
    fireEvent.change(within(dialog).getByLabelText('Reason'), { target: { value: 'The firm left this call off the invoice' } });
    return dialog;
  }

  // hidden: true - the dialog surface can carry aria-hidden once it has been used in jsdom; the element is still the one on screen.
  const submit = (dialog: HTMLElement) => fireEvent.click(within(dialog).getByRole('button', { name: 'Add line', hidden: true }));
  // Queried by DOM, not by role: a role query skips an aria-hidden surface and would pass for the wrong reason.
  const noDialog = () => expect(document.querySelector('[role="dialog"]')).toBeNull();

  it('C1: sends ONE requestId per opened form, and the same one on Repeat after a 503 incomplete', async () => {
    const api = mockApi(normal());
    api.addLine.mockResolvedValueOnce(
      failed(problem(503, 'ebilling.decision.incomplete', { detail: 'The line was saved; the signals were not.', failedStep: 'signals' }))
    );
    renderPage(api);
    await screen.findByTestId('review-title');
    const dialog = await openAndFillFee();
    submit(dialog);

    // The change was saved: the form closes and the page offers Repeat.
    fireEvent.click(await screen.findByTestId('notice-repeat'));
    await waitFor(() => expect(api.addLine).toHaveBeenCalledTimes(2));
    const [first, second] = api.addLine.mock.calls.map(c => c[1]);
    expect(first.requestId).toMatch(UUID);
    expect(second.requestId).toBe(first.requestId);
    expect(second).toEqual(first);
    // Only what the reviewer typed, the cost type and the request id: no total, status, owner, person or line id.
    expect(first).toEqual({
      requestId: first.requestId,
      costType: 'fee',
      date: '2025-03-14',
      timekeeper: 'B. Associate',
      hours: 2.5,
      rate: 300,
      amount: null,
      taskCode: 'L110',
      activityCode: null,
      expenseCode: null,
      description: 'Call with the client',
      reason: 'The firm left this call off the invoice',
    });
    expect(api.addLine.mock.calls[0][0]).toBe(INVOICE_ID);
  });

  it('C1: a refusal that leaves the form open is resent with the same requestId; the next opened form gets a new one', async () => {
    const api = mockApi(normal());
    api.addLine.mockResolvedValueOnce(failed(problem(400, 'ebilling.line_add.invalid', { detail: 'A fee line needs its timekeeper.' })));
    renderPage(api);
    await screen.findByTestId('review-title');
    const dialog = await openAndFillFee();
    submit(dialog);
    expect(await within(dialog).findByTestId('add-line-error')).toHaveTextContent('A fee line needs its timekeeper.');
    submit(dialog);
    await waitFor(() => expect(api.addLine).toHaveBeenCalledTimes(2));
    expect(api.addLine.mock.calls[1][1].requestId).toBe(api.addLine.mock.calls[0][1].requestId);

    // The second send went through (default mock): the form closed. Opening it again is a new add with a new id.
    await waitFor(noDialog);
    fireEvent.click(screen.getByTestId('add-line'));
    const again = await screen.findByRole('dialog');
    fireEvent.change(within(again).getByLabelText('Reason'), { target: { value: 'Another' } });
    submit(again);
    await waitFor(() => expect(api.addLine).toHaveBeenCalledTimes(3));
    expect(api.addLine.mock.calls[2][1].requestId).toMatch(UUID);
    expect(api.addLine.mock.calls[2][1].requestId).not.toBe(api.addLine.mock.calls[0][1].requestId);
  });

  it('C1: an Expense sends only the Expense fields', async () => {
    const api = mockApi(normal());
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(await screen.findByTestId('add-line'));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Timekeeper'), { target: { value: 'left over from the Fee tab' } });
    fireEvent.click(within(dialog).getByRole('radio', { name: 'Expense' }));
    fireEvent.change(within(dialog).getByLabelText('Date'), { target: { value: '2025-03-20' } });
    fireEvent.change(within(dialog).getByLabelText('Amount'), { target: { value: '42.10' } });
    fireEvent.change(within(dialog).getByLabelText('Expense code'), { target: { value: 'E101' } });
    submit(dialog);
    await waitFor(() => expect(api.addLine).toHaveBeenCalledTimes(1));
    expect(api.addLine.mock.calls[0][1]).toMatchObject({
      costType: 'expense',
      timekeeper: null,
      hours: null,
      rate: null,
      taskCode: null,
      activityCode: null,
      amount: 42.1,
      expenseCode: 'E101',
    });
  });

  it('C1: on success the form closes, the page re-reads and the new line is selected', async () => {
    const added = flagged(99, { reviewerAdded: true });
    const api = mockApi([normal(), review([...normal().lines, added])]);
    renderPage(api);
    await screen.findByTestId('review-title');
    submit(await openAndFillFee());
    expect(await screen.findByTestId('notice')).toHaveTextContent('Line added');
    await waitFor(() => expect(api.getReview).toHaveBeenCalledTimes(2));
    expect(await screen.findByTestId(`pane-line-${ADDED_LINE_ID}`)).toBeInTheDocument();
    noDialog();
  });

  it.each([
    [400, 'ebilling.line_add.invalid', 'A fee line needs its timekeeper.', 'A fee line needs its timekeeper.'],
    [400, 'ebilling.line_add.amount_mismatch', 'The amount is not the hours times the rate.', 'The amount is not the hours times the rate.'],
    [400, 'ebilling.line_add.code_not_allowed', 'The task code is not in this invoice code set.', 'The task code is not in this invoice code set.'],
    [400, 'ebilling.line.reason_required', 'server words', 'A reason is required to add a line.'],
    [409, 'ebilling.line_add.firm_missing', 'This invoice has no firm. Add the firm before adding a line; nothing was changed.', 'This invoice has no firm.'],
    [409, 'ebilling.line_add.code_set_unavailable', 'server words', 'codes for this invoice could not be checked'],
    [409, 'ebilling.line_add.procedure_gap', 'server words', 'without the procedure it needs'],
    [409, 'ebilling.line.corrections_pending', 'Repeat the corrections on lines 2 and 4 first.', 'Repeat the corrections on lines 2 and 4 first.'],
    [409, 'ebilling.invoice.totals_refused', 'server words', 'totals would not allow this line'],
    [409, 'ebilling.invoice.busy', 'server words', 'Another change to this invoice is in progress'],
    [409, 'ebilling.line_add.changed', 'server words', 'The invoice changed while the line was being added'],
    [500, 'ebilling.decision.owner_unresolved', 'server words', 'could not be given an owner'],
  ])('C2: %s %s shows in the form, which keeps what was typed and offers no Reload', async (status, code, detail, shown) => {
    const api = mockApi(normal());
    api.addLine.mockResolvedValue(failed(problem(status, code, { detail })));
    renderPage(api);
    await screen.findByTestId('review-title');
    const dialog = await openAndFillFee();
    submit(dialog);
    expect(await within(dialog).findByTestId('add-line-error')).toHaveTextContent(shown);
    expect(dialog).toBeInTheDocument();
    expect(within(dialog).getByLabelText('Reason')).toHaveValue('The firm left this call off the invoice');
    expect(within(dialog).getByLabelText('Hours')).toHaveValue('2.5');
    expect(screen.queryByTestId('notice-reload')).not.toBeInTheDocument();
    expect(api.getReview).toHaveBeenCalledTimes(1);
  });

  const incompleteAddLine = () =>
    failed(problem(503, 'ebilling.decision.incomplete', { detail: 'The line was saved; the signals were not.', failedStep: 'signals' }));

  it('F1-1: a corrections_pending refusal with no detail falls back to the fixed sentence', async () => {
    const api = mockApi(normal());
    api.addLine.mockResolvedValue(failed(problem(409, 'ebilling.line.corrections_pending')));
    renderPage(api);
    await screen.findByTestId('review-title');
    const dialog = await openAndFillFee();
    submit(dialog);
    expect(await within(dialog).findByTestId('add-line-error')).toHaveTextContent('correction on this invoice is still being applied');
  });

  it('K1: a successful Repeat does not close an Add line form the reviewer opened while the Repeat notice was showing', async () => {
    const api = mockApi(normal());
    api.addLine.mockResolvedValueOnce(incompleteAddLine());
    renderPage(api);
    await screen.findByTestId('review-title');
    submit(await openAndFillFee());
    const repeat = await screen.findByTestId('notice-repeat');
    noDialog();

    // Open the form again and type a value before repeating.
    fireEvent.click(await screen.findByTestId('add-line'));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Description'), { target: { value: 'A second call, half typed' } });

    fireEvent.click(repeat);
    await waitFor(() => expect(api.addLine).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.getByTestId('notice')).toHaveTextContent('Line added'));

    // The new form is still open with what was typed.
    expect(document.querySelector('[role="dialog"]')).not.toBeNull();
    expect(within(screen.getByRole('dialog', { hidden: true })).getByLabelText('Description')).toHaveValue('A second call, half typed');
  });

  it('F1-1: a Repeat that gets a 409 busy keeps Repeat with the same requestId and never says nothing was changed', async () => {
    const api = mockApi(normal());
    api.addLine
      .mockResolvedValueOnce(incompleteAddLine())
      .mockResolvedValueOnce(failed(problem(409, 'ebilling.invoice.busy', { detail: 'server words' })))
      .mockResolvedValueOnce(failed(problem(409, 'ebilling.line_add.changed', { detail: 'server words' })));
    renderPage(api);
    await screen.findByTestId('review-title');
    submit(await openAndFillFee());

    fireEvent.click(await screen.findByTestId('notice-repeat'));
    await waitFor(() => expect(api.addLine).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.getByTestId('notice')).toHaveTextContent('Another change to this invoice is in progress'));
    expect(screen.getByTestId('notice')).not.toHaveTextContent('Nothing was changed');
    expect(screen.queryByTestId('notice-reload')).not.toBeInTheDocument();

    // Repeat again (the same closure): a 409 changed is the same.
    fireEvent.click(screen.getByTestId('notice-repeat'));
    await waitFor(() => expect(api.addLine).toHaveBeenCalledTimes(3));
    await waitFor(() => expect(screen.getByTestId('notice')).toHaveTextContent('The invoice changed while the repeat was running'));
    expect(screen.getByTestId('notice')).not.toHaveTextContent('Nothing was changed');

    // And once more it goes through: still the one request id, and the form was not reopened.
    fireEvent.click(screen.getByTestId('notice-repeat'));
    await waitFor(() => expect(api.addLine).toHaveBeenCalledTimes(4));
    const calls = api.addLine.mock.calls.map(c => c[1]);
    expect(new Set(calls.map(c => c.requestId)).size).toBe(1);
    expect(calls[3]).toEqual(calls[0]);
    await waitFor(() => expect(screen.getByTestId('notice')).toHaveTextContent('Line added'));
    noDialog();
  });

  it('F1-1: a Repeat that gets no answer (status 0) or a 5xx keeps Repeat with the same requestId and never says nothing was changed', async () => {
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    try {
      const api = mockApi(normal());
      api.addLine
        .mockResolvedValueOnce(incompleteAddLine())
        .mockResolvedValueOnce(failed(new TypeError('Failed to fetch')))
        .mockResolvedValueOnce(failed(problem(500, null, { detail: 'boom' })));
      renderPage(api);
      await screen.findByTestId('review-title');
      submit(await openAndFillFee());

      fireEvent.click(await screen.findByTestId('notice-repeat'));
      await waitFor(() => expect(screen.getByTestId('notice')).toHaveTextContent('No answer came back, so the repeat may not have finished'));
      expect(screen.getByTestId('notice')).not.toHaveTextContent('Nothing was changed');
      expect(screen.getByTestId('notice')).not.toHaveTextContent('Failed to fetch');

      fireEvent.click(screen.getByTestId('notice-repeat'));
      await waitFor(() => expect(api.addLine).toHaveBeenCalledTimes(3));
      await waitFor(() => expect(screen.getByTestId('notice')).toHaveTextContent('The repeat did not finish'));
      expect(screen.getByTestId('notice')).not.toHaveTextContent('Nothing was changed');

      fireEvent.click(screen.getByTestId('notice-repeat'));
      await waitFor(() => expect(api.addLine).toHaveBeenCalledTimes(4));
      const calls = api.addLine.mock.calls.map(c => c[1]);
      expect(new Set(calls.map(c => c.requestId)).size).toBe(1);
      noDialog();
    } finally {
      warn.mockRestore();
    }
  });

  it('F1-1: a Repeat refused for another reason shows that sentence without "Nothing was changed" and offers no Repeat', async () => {
    const api = mockApi(normal());
    api.addLine
      .mockResolvedValueOnce(incompleteAddLine())
      .mockResolvedValueOnce(failed(problem(409, 'ebilling.invoice.totals_refused', { detail: 'server words' })));
    renderPage(api);
    await screen.findByTestId('review-title');
    submit(await openAndFillFee());
    fireEvent.click(await screen.findByTestId('notice-repeat'));
    await waitFor(() => expect(api.addLine).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.getByTestId('notice')).toHaveTextContent('totals would not allow this line'));
    expect(screen.getByTestId('notice')).not.toHaveTextContent('Nothing was changed');
    expect(screen.queryByTestId('notice-repeat')).not.toBeInTheDocument();
  });

  it('F1-1 (shared path): a decision write whose Repeat gets a 409 busy keeps Repeat with the same decision id', async () => {
    const api = mockApi(normal());
    api.decideLine
      .mockResolvedValueOnce(failed(problem(503, 'ebilling.decision.incomplete', { detail: 'The line was saved; the record was not.', failedStep: 'record' })))
      .mockResolvedValueOnce(failed(problem(409, 'ebilling.invoice.busy', { detail: 'server words' })))
      .mockResolvedValueOnce(ok({ decisionId: 'x', lines: [], totals: null }));
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(row(2));
    await screen.findByTestId(`pane-line-${lineId(2)}`);
    fireEvent.click(screen.getByTestId('decision-confirm'));
    fireEvent.click(await screen.findByTestId('notice-repeat'));
    await waitFor(() => expect(api.decideLine).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.getByTestId('notice')).toHaveTextContent('Another change to this invoice is in progress'));
    expect(screen.getByTestId('notice')).not.toHaveTextContent('Nothing was changed');
    expect(screen.queryByTestId('notice-reload')).not.toBeInTheDocument();
    fireEvent.click(screen.getByTestId('notice-repeat'));
    await waitFor(() => expect(api.decideLine).toHaveBeenCalledTimes(3));
    expect(new Set(api.decideLine.mock.calls.map(c => c[2].decisionId)).size).toBe(1);
  });

  it('F1-3: a first send with no answer says the line may already be added, keeps the form, and resends with the same requestId', async () => {
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    try {
      const api = mockApi(normal());
      api.addLine.mockResolvedValueOnce(failed(new TypeError('Failed to fetch')));
      renderPage(api);
      await screen.findByTestId('review-title');
      const dialog = await openAndFillFee();
      submit(dialog);
      const error = await within(dialog).findByTestId('add-line-error');
      expect(error).toHaveTextContent('No answer came back, so the line may already be added');
      expect(error).toHaveTextContent('it is never added twice');
      expect(error).not.toHaveTextContent('could not be sent');
      expect(error).not.toHaveTextContent('Failed to fetch');
      expect(within(dialog).getByLabelText('Reason')).toHaveValue('The firm left this call off the invoice');
      submit(dialog);
      await waitFor(() => expect(api.addLine).toHaveBeenCalledTimes(2));
      expect(api.addLine.mock.calls[1][1].requestId).toBe(api.addLine.mock.calls[0][1].requestId);
      await waitFor(noDialog);
    } finally {
      warn.mockRestore();
    }
  });

  it('K3: a 200 alreadyApplied is a success: the line is added and the form closes', async () => {
    const added = flagged(99, { reviewerAdded: true });
    const api = mockApi([normal(), review([...normal().lines, added])]);
    api.addLine.mockImplementationOnce(async (_i: string, r: { requestId: string }) =>
      ok({ requestId: r.requestId, lineId: ADDED_LINE_ID, decisionId: 'dd', status: 'Flagged', alreadyApplied: true, totals: null })
    );
    renderPage(api);
    await screen.findByTestId('review-title');
    submit(await openAndFillFee());
    expect(await screen.findByTestId('notice')).toHaveTextContent('Line added');
    expect(await screen.findByTestId(`pane-line-${ADDED_LINE_ID}`)).toBeInTheDocument();
    noDialog();
  });

  it('K3: the submit button is disabled while a send is in flight, so a second click sends nothing', async () => {
    const api = mockApi(normal());
    let release: (v: Awaited<ReturnType<typeof api.addLine>>) => void = () => undefined;
    api.addLine.mockImplementationOnce(
      () =>
        new Promise(resolve => {
          release = resolve;
        })
    );
    renderPage(api);
    await screen.findByTestId('review-title');
    const dialog = await openAndFillFee();
    const button = () => within(dialog).getByRole('button', { name: 'Add line', hidden: true });
    expect(button()).toBeEnabled();
    submit(dialog);
    await waitFor(() => expect(button()).toBeDisabled());
    fireEvent.click(button());
    expect(api.addLine).toHaveBeenCalledTimes(1);
    release(ok({ requestId: 'r', lineId: ADDED_LINE_ID, decisionId: 'dd', status: 'Flagged', alreadyApplied: false, totals: null }));
    await waitFor(noDialog);
  });

  it('C2: text that is not a number is named in the form and nothing is sent', async () => {
    const api = mockApi(normal());
    renderPage(api);
    await screen.findByTestId('review-title');
    const dialog = await openAndFillFee();
    fireEvent.change(within(dialog).getByLabelText('Hours'), { target: { value: 'two' } });
    submit(dialog);
    expect(await within(dialog).findByTestId('add-line-error')).toHaveTextContent('Hours must be a number');
    expect(api.addLine).not.toHaveBeenCalled();
  });

  it('C2: id_reused and not_in_review close the form and offer Reload', async () => {
    for (const code of ['ebilling.line_add.id_reused', 'ebilling.invoice.not_in_review']) {
      const api = mockApi(normal());
      api.addLine.mockResolvedValue(failed(problem(409, code, { detail: 'server words' })));
      const view = renderPage(api);
      await screen.findByTestId('review-title');
      submit(await openAndFillFee());
      fireEvent.click(await screen.findByTestId('notice-reload'));
      await waitFor(() => expect(api.getReview).toHaveBeenCalledTimes(2));
      noDialog();
      view.unmount();
    }
  });

  it('C2: a 403 closes the form and turns the page read-only', async () => {
    const api = mockApi(normal());
    api.addLine.mockResolvedValue(failed(forbidden('finance.access.denied')));
    renderPage(api);
    await screen.findByTestId('review-title');
    submit(await openAndFillFee());
    expect(await screen.findByTestId('readonly-notice')).toBeInTheDocument();
    expect(screen.queryByTestId('add-line')).not.toBeInTheDocument();
    noDialog();
  });

  it('C3: the button shows only when the caller can decide AND the invoice is In Review', async () => {
    const shows = async (overrides: Parameters<typeof review>[1]) => {
      const view = renderPage(mockApi(review([flagged(1)], overrides)));
      await screen.findByTestId('review-title');
      const present = screen.queryByTestId('add-line') !== null;
      view.unmount();
      return present;
    };
    const inReview = review([]).header;
    expect(await shows({})).toBe(true);
    expect(await shows({ flags: { canRead: true, canDecide: false } })).toBe(false);
    for (const [status, statusCode] of [
      ['Approved', 3],
      ['Rejected', 4],
      ['ReturnedToFirm', 5],
      ['SentToAp', 6],
      ['Paid', 7],
      ['Cancelled', 8],
    ] as const) {
      // canDecide stays true: the status alone hides it.
      expect(await shows({ header: { ...inReview, status, statusCode } })).toBe(false);
    }
  });

  it('C3: the button is disabled while the invoice needs its firm first', async () => {
    renderPage(mockApi(normal()), { firmNeeded: true });
    await screen.findByTestId('review-title');
    expect(screen.getByTestId('add-line')).toBeDisabled();
  });

  it('C4: a reviewer-added line carries the badge; intake lines do not', async () => {
    renderPage(mockApi(review([line(1), flagged(2, { reviewerAdded: true }), flagged(3)])));
    await screen.findByTestId('review-title');
    const badge = screen.getByTestId(`reviewer-added-${lineId(2)}`);
    expect(badge).toHaveTextContent('Added');
    expect(badge).toHaveAccessibleName('Added by reviewer');
    expect(within(row(2)).getByTestId(`reviewer-added-${lineId(2)}`)).toBeInTheDocument();
    expect(screen.queryByTestId(`reviewer-added-${lineId(1)}`)).not.toBeInTheDocument();
    expect(screen.queryByTestId(`reviewer-added-${lineId(3)}`)).not.toBeInTheDocument();
    expect(screen.getAllByTestId(/^reviewer-added-/)).toHaveLength(1);
  });
});

describe('source guards', () => {
  it('uses no colour literal anywhere in the page source (dark mode: Fluent tokens only, ADR-021)', () => {
    const srcRoot = path.resolve(__dirname, '..');
    // The same walker task 111's guard asserts covers the decision and tab folders (testKit.listSourceFiles).
    for (const f of listSourceFiles()) {
      const found = findColourLiterals(fs.readFileSync(f, 'utf8'));
      expect({ file: path.relative(srcRoot, f), found }).toEqual({ file: path.relative(srcRoot, f), found: [] });
    }
  });

  it('the colour guard fires on hex, rgb() / hsl() and named colours in style values', () => {
    expect(findColourLiterals("const a = { color: '#fff' };")).toEqual(['#fff']);
    expect(findColourLiterals("const a = { border: '1px solid #CCCCCC80' };")).toEqual(['#CCCCCC80']);
    expect(findColourLiterals("const a = { backgroundColor: 'rgb(0, 0, 0)' };")).toEqual(['rgb(']);
    expect(findColourLiterals('const a = { fill: `hsla(0,0%,0%,.5)` };')).toEqual(['hsla(']);
    expect(findColourLiterals("const a = { backgroundColor: 'white' };")).toEqual(['white']);
    expect(findColourLiterals('const a = { color: "Black", borderTopColor: "red" };')).toEqual(['Black', 'red']);
    expect(findColourLiterals('<div style={{ background: "navy" }} />')).toEqual(['navy']);
  });

  it('the colour guard stays quiet on tokens, transparent, comments, ids and non-colour words', () => {
    expect(findColourLiterals('const a = { color: tokens.colorNeutralForeground1, backgroundColor: tokens.colorNeutralBackground2 };')).toEqual([]);
    expect(findColourLiterals("const a = { backgroundColor: 'transparent', color: 'inherit', borderColor: 'currentColor' };")).toEqual([]);
    expect(findColourLiterals("// a #fff comment and 'white' in prose\n/* rgb(0,0,0) */ const a = 1;")).toEqual([]);
    expect(findColourLiterals("document.getElementById('root'); const href = '#root'; const t = 'Black Friday sale';")).toEqual([]);
    expect(findColourLiterals("const a = { overflow: 'hidden', display: 'flex', position: 'relative' };")).toEqual([]);
  });

  const srcDir = path.resolve(__dirname, '..');
  const files = (dir: string): string[] =>
    fs.readdirSync(dir, { withFileTypes: true }).flatMap(e => {
      const p = path.join(dir, e.name);
      if (e.isDirectory()) return e.name === '__tests__' ? [] : files(p);
      return /\.(ts|tsx)$/.test(e.name) ? [p] : [];
    });

  it('imports nothing from the prototype repository and never writes Dataverse directly', () => {
    for (const f of files(srcDir)) {
      const text = fs.readFileSync(f, 'utf8');
      expect(text).not.toMatch(/spaarke-prototype|ebilling\.r1/);
      expect(text).not.toMatch(/Xrm\.WebApi|createRecord\(|updateRecord\(/);
    }
  });

  it('registers the Navigator side pane on mount', () => {
    const main = fs.readFileSync(path.join(srcDir, 'main.tsx'), 'utf8');
    expect(main).toMatch(/React\.useEffect\(\(\) => \{\s*ensureNavigatorSidePane\(\);\s*\}, \[\]\);/);
  });
});
