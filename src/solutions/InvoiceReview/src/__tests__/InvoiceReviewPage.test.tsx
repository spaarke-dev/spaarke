import '@testing-library/jest-dom';
import * as fs from 'fs';
import * as path from 'path';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { InvoiceReviewPage } from '../components/InvoiceReviewPage';
import { LINE_FINDING_FALLBACK } from '../services/reviewModel';
import { INVOICE_ID, failed, field, finding, flagged, line, lineId, mockApi, ok, problem, review, type MockApi } from './testKit';

function renderPage(api: MockApi, opts: { theme?: typeof webLightTheme; pollIntervalMs?: number; openRecord?: jest.Mock } = {}) {
  return render(
    <FluentProvider theme={opts.theme ?? webLightTheme}>
      <InvoiceReviewPage invoiceId={INVOICE_ID} api={api as never} openRecord={opts.openRecord} pollIntervalMs={opts.pollIntervalMs ?? 60_000} />
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

  it('renders under the dark theme', async () => {
    renderPage(mockApi(normal()), { theme: webDarkTheme });
    expect(await screen.findByTestId('invoice-review')).toBeInTheDocument();
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
    fireEvent.change(within(dialog).getByLabelText('Correct value'), { target: { value: '2.5' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save correction' }));

    await waitFor(() => expect(api.correctLine).toHaveBeenCalledWith(INVOICE_ID, lineId(7), { field: 'Hours', value: '2.5', confirm: false }));
    expect(api.decideLine).not.toHaveBeenCalled();
    expect(api.decideLines).not.toHaveBeenCalled();
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    // The line is Clean after the server re-ran the rules.
    expect(within(row(7)).getByText('Clean')).toBeInTheDocument();
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

describe('source guards', () => {
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
