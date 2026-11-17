/**
 * Task 095f-2ui, review round 1 (page): what the bulk, correction and Confirm-as-read dialogs do with a 409, the warning for a correction or
 * confirm the server found already in place (it stores no note), and a refused note that must show on its own line only.
 *
 * Through the real page, the real shared FormModal and a mocked BFF client (the harness of noteBoxesPage.test.tsx).
 */

import '@testing-library/jest-dom';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { InvoiceReviewPage } from '../components/InvoiceReviewPage';
import { INVOICE_ID, failed, field, finding, flagged, line, lineId, mockApi, ok, problem, review, type MockApi } from './testKit';

function renderPage(api: MockApi) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <InvoiceReviewPage invoiceId={INVOICE_ID} api={api as never} pollIntervalMs={60_000} />
    </FluentProvider>
  );
}

const normal = () => review([line(1), flagged(2), flagged(4)]);
const lowHours = () =>
  review([
    flagged(7, {
      quantity: 25,
      fields: { ...line(7).fields, Hours: field({ lowConfidence: true, combined: 0.42, box: { page: 1, x: 0.6, y: 0.5, w: 0.05, h: 0.02 } }) },
      findings: [finding({ policyCode: 'HOURS-DAY', valuesRead: [{ name: 'Hours', value: '25', field: 'Hours', confidence: 0.42, belowReliabilityThreshold: true }] })],
    }),
  ]);
const row = (n: number) => screen.getByTestId(`line-row-${lineId(n)}`);
const applied = (alreadyApplied: boolean) =>
  ok({ correction: { id: 'c1', lineId: lineId(7), field: 'Hours', aiValue: '25', finalValue: '2.5', outcome: 'Corrected', isLabel: true, aiConfidence: 0.4 }, alreadyApplied });

async function openLine7Pane(api: MockApi) {
  renderPage(api);
  await screen.findByTestId('review-title');
  fireEvent.click(row(7));
  return screen.findByTestId(`pane-line-${lineId(7)}`);
}

async function openCorrect(api: MockApi) {
  const pane = await openLine7Pane(api);
  fireEvent.click(within(pane).getByTestId('correct-Hours'));
  const dialog = await screen.findByRole('dialog');
  fireEvent.change(within(dialog).getByLabelText('Correct value'), { target: { value: '2.5' } });
  return { dialog, pane };
}

async function openConfirm(api: MockApi) {
  const pane = await openLine7Pane(api);
  fireEvent.click(within(pane).getByTestId('confirm-Hours'));
  return { dialog: await screen.findByRole('dialog'), pane };
}

describe('a correction or confirm the server found already in place stored no note', () => {
  it('a correction that was already in place, sent with a note, is a warning and not "Saved"', async () => {
    const api = mockApi(lowHours());
    api.correctLine.mockResolvedValue(applied(true));
    const { dialog } = await openCorrect(api);
    fireEvent.change(within(dialog).getByLabelText('Note'), { target: { value: 'Per the timesheet' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save correction', hidden: true }));
    const notice = await screen.findByTestId('notice');
    expect(notice).toHaveTextContent('Already corrected to this value; the note was not saved.');
    expect(notice).not.toHaveTextContent('Saved');
    expect(notice).toHaveAttribute('data-intent', 'warning');
  });

  it('a header correction that was already in place, sent with a note, is the same warning', async () => {
    const api = mockApi(normal());
    api.correctHeader.mockResolvedValue(applied(true));
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(screen.getByTestId('correct-header'));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Correct value'), { target: { value: 'INV-2025-78233' } });
    fireEvent.change(within(dialog).getByLabelText('Note'), { target: { value: 'Typo on the scan' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save correction', hidden: true }));
    expect(await screen.findByTestId('notice')).toHaveTextContent('Already corrected to this value; the note was not saved.');
  });

  it('a confirm that was already in place, sent with a note, is a warning and not "Saved"', async () => {
    const api = mockApi(lowHours());
    api.correctLine.mockResolvedValue(applied(true));
    const { dialog } = await openConfirm(api);
    fireEvent.change(within(dialog).getByLabelText('Note'), { target: { value: 'Matches the PDF' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Confirm as read', hidden: true }));
    const notice = await screen.findByTestId('notice');
    expect(notice).toHaveTextContent('Already confirmed; the note was not saved.');
    expect(notice).not.toHaveTextContent('Saved');
    expect(notice).toHaveAttribute('data-intent', 'warning');
  });

  it('(must-not-fire) already in place with NO note keeps the plain success text', async () => {
    const api = mockApi(lowHours());
    api.correctLine.mockResolvedValue(applied(true));
    const { dialog } = await openCorrect(api);
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save correction', hidden: true }));
    const notice = await screen.findByTestId('notice');
    expect(notice).toHaveTextContent('Saved · Hours corrected on line 7; the rules ran again');
    expect(notice).toHaveAttribute('data-intent', 'success');
  });

  it('(must-not-fire) a confirm already in place with NO note keeps the plain success text', async () => {
    const api = mockApi(lowHours());
    api.correctLine.mockResolvedValue(applied(true));
    const { dialog } = await openConfirm(api);
    fireEvent.click(within(dialog).getByRole('button', { name: 'Confirm as read', hidden: true }));
    const notice = await screen.findByTestId('notice');
    expect(notice).toHaveTextContent('Saved · Hours on line 7 confirmed as read');
    expect(notice).toHaveAttribute('data-intent', 'success');
  });

  it('(must-not-fire) a note on a correction that was written now keeps the plain success text', async () => {
    const api = mockApi(lowHours());
    api.correctLine.mockResolvedValue(applied(false));
    const { dialog } = await openCorrect(api);
    fireEvent.change(within(dialog).getByLabelText('Note'), { target: { value: 'Per the timesheet' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save correction', hidden: true }));
    const notice = await screen.findByTestId('notice');
    expect(notice).toHaveTextContent('Saved · Hours corrected on line 7; the rules ran again');
    expect(notice).toHaveAttribute('data-intent', 'success');
  });

  it('(must-not-fire) a confirm written now, with a note, keeps the plain success text', async () => {
    const api = mockApi(lowHours());
    api.correctLine.mockResolvedValue(applied(false));
    const { dialog } = await openConfirm(api);
    fireEvent.change(within(dialog).getByLabelText('Note'), { target: { value: 'Matches the PDF' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Confirm as read', hidden: true }));
    expect(await screen.findByTestId('notice')).toHaveTextContent('Saved · Hours on line 7 confirmed as read');
  });

  // Review round 2: a 503 `incomplete` has already committed the row WITH its note, so the repeat is answered "already applied" and the
  // note WAS saved. The warning is only for a FIRST send that finds the value already in place.
  const incompleteCorrection = () =>
    failed(problem(503, 'ebilling.correction.incomplete', { detail: 'The correction was saved; the totals were not.', failedStep: 'totals' }));

  it('(round 2) the page Repeat of a correction that saved in part is answered already-applied: plain success, no "note was not saved"', async () => {
    const api = mockApi(lowHours());
    api.correctLine.mockResolvedValueOnce(incompleteCorrection()).mockResolvedValueOnce(applied(true));
    const { dialog } = await openCorrect(api);
    fireEvent.change(within(dialog).getByLabelText('Note'), { target: { value: 'Per the timesheet' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save correction', hidden: true }));
    fireEvent.click(await screen.findByTestId('notice-repeat'));
    await waitFor(() => expect(api.correctLine).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.getByTestId('notice')).toHaveAttribute('data-intent', 'success'));
    expect(screen.getByTestId('notice')).toHaveTextContent('Saved · Hours corrected on line 7; the rules ran again');
    expect(screen.getByTestId('notice')).not.toHaveTextContent('not saved');
    expect(api.correctLine.mock.calls[1][2]).toEqual(api.correctLine.mock.calls[0][2]);
  });

  it('(round 2) the page Repeat of a confirm that saved in part is answered already-applied: plain success', async () => {
    const api = mockApi(lowHours());
    api.correctLine.mockResolvedValueOnce(incompleteCorrection()).mockResolvedValueOnce(applied(true));
    const { dialog } = await openConfirm(api);
    fireEvent.change(within(dialog).getByLabelText('Note'), { target: { value: 'Matches the PDF' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Confirm as read', hidden: true }));
    fireEvent.click(await screen.findByTestId('notice-repeat'));
    await waitFor(() => expect(api.correctLine).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.getByTestId('notice')).toHaveAttribute('data-intent', 'success'));
    expect(screen.getByTestId('notice')).toHaveTextContent('Saved · Hours on line 7 confirmed as read');
    expect(screen.getByTestId('notice')).not.toHaveTextContent('not saved');
  });

  it('(round 2) a resend from the same dialog after a send with no answer, answered already-applied, is plain success', async () => {
    const api = mockApi(lowHours());
    api.correctLine.mockResolvedValueOnce(failed(new Error('Failed to fetch'))).mockResolvedValueOnce(applied(true));
    const { dialog } = await openCorrect(api);
    fireEvent.change(within(dialog).getByLabelText('Note'), { target: { value: 'Per the timesheet' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save correction', hidden: true }));
    await within(dialog).findByTestId('correct-error');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save correction', hidden: true }));
    const notice = await screen.findByTestId('notice');
    expect(notice).toHaveTextContent('Saved · Hours corrected on line 7; the rules ran again');
    expect(notice).toHaveAttribute('data-intent', 'success');
    expect(notice).not.toHaveTextContent('not saved');
  });

  it('(round 2) a resend after a 503 that closed the dialog, reopened from its draft and answered already-applied, is plain success', async () => {
    const api = mockApi(lowHours());
    api.correctLine.mockResolvedValueOnce(incompleteCorrection()).mockResolvedValueOnce(applied(true));
    const { dialog, pane } = await openCorrect(api);
    fireEvent.change(within(dialog).getByLabelText('Note'), { target: { value: 'Per the timesheet' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save correction', hidden: true }));
    await screen.findByTestId('notice-repeat');
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    fireEvent.click(within(pane).getByTestId('correct-Hours'));
    const again = await screen.findByRole('dialog');
    expect(within(again).getByLabelText('Note')).toHaveValue('Per the timesheet');
    fireEvent.click(within(again).getByRole('button', { name: 'Save correction', hidden: true }));
    await waitFor(() => expect(api.correctLine).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.getByTestId('notice')).toHaveAttribute('data-intent', 'success'));
    expect(screen.getByTestId('notice')).not.toHaveTextContent('not saved');
  });

  it('(must-not-fire) a first send answered already-applied still warns, even after an earlier 409 that saved nothing', async () => {
    const api = mockApi(lowHours());
    api.correctLine
      .mockResolvedValueOnce(failed(problem(409, 'ebilling.correction.code_set_unavailable', { detail: 'No code set.' })))
      .mockResolvedValueOnce(applied(true));
    const { dialog } = await openCorrect(api);
    fireEvent.change(within(dialog).getByLabelText('Note'), { target: { value: 'Per the timesheet' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save correction', hidden: true }));
    await within(dialog).findByTestId('correct-error');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save correction', hidden: true }));
    const notice = await screen.findByTestId('notice');
    expect(notice).toHaveTextContent('Already corrected to this value; the note was not saved.');
    expect(notice).toHaveAttribute('data-intent', 'warning');
  });
});

describe('the bulk, correction and Confirm-as-read dialogs on a 409', () => {
  async function openBulk(api: MockApi) {
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(within(row(2)).getByRole('checkbox'));
    fireEvent.click(within(row(4)).getByRole('checkbox'));
    fireEvent.click(screen.getByTestId('bulk-disallow'));
    return screen.findByRole('dialog');
  }
  const typeBulk = (dialog: HTMLElement) => {
    fireEvent.change(within(dialog).getByLabelText('Reason'), { target: { value: 'Duplicate entries' } });
    fireEvent.change(within(dialog).getByLabelText('Note'), { target: { value: 'Same entry twice' } });
  };
  const submitBulk = (dialog: HTMLElement) => fireEvent.click(within(dialog).getByRole('button', { name: 'Disallow 2 lines', hidden: true }));

  it.each([
    ['ebilling.invoice.busy', 'Another change to this invoice is in progress.'],
    ['ebilling.line.pending', 'A line is still being processed.'],
    ['ebilling.invoice.concurrent_change', 'The invoice changed while this was running.'],
  ])('a bulk decision answered 409 %s stays open with the sentence in the dialog, keeps the reason and the note, and a resend carries both', async (code, detail) => {
    const api = mockApi(normal());
    api.decideLines.mockResolvedValueOnce(failed(problem(409, code, { detail })));
    const dialog = await openBulk(api);
    typeBulk(dialog);
    const reads = api.getReview.mock.calls.length;
    submitBulk(dialog);
    expect(await within(dialog).findByTestId('bulk-error')).toHaveTextContent(detail);
    expect(dialog).toBeInTheDocument();
    expect(within(dialog).getByLabelText('Reason')).toHaveValue('Duplicate entries');
    expect(within(dialog).getByLabelText('Note')).toHaveValue('Same entry twice');
    expect(screen.queryByTestId('notice')).not.toBeInTheDocument();
    expect(api.getReview.mock.calls.length).toBe(reads);
    submitBulk(dialog);
    await waitFor(() => expect(api.decideLines).toHaveBeenCalledTimes(2));
    expect(api.decideLines.mock.calls[1][1]).toMatchObject({ reason: 'Duplicate entries', note: 'Same entry twice' });
  });

  it('a bulk decision answered 409 line.changed closes the dialog with Reload, and the next opening for the same lines is prefilled', async () => {
    const api = mockApi(normal());
    api.decideLines.mockResolvedValueOnce(failed(problem(409, 'ebilling.line.changed', { detail: 'A line changed. Reload and decide again.' })));
    const first = await openBulk(api);
    typeBulk(first);
    submitBulk(first);
    expect(await screen.findByTestId('notice-reload')).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    fireEvent.click(screen.getByTestId('bulk-disallow'));
    const again = await screen.findByRole('dialog');
    expect(within(again).getByLabelText('Reason')).toHaveValue('Duplicate entries');
    expect(within(again).getByLabelText('Note')).toHaveValue('Same entry twice');
    // It is still a form: the prefilled text can be edited and sent.
    fireEvent.change(within(again).getByLabelText('Note'), { target: { value: 'Edited note' } });
    submitBulk(again);
    await waitFor(() => expect(api.decideLines).toHaveBeenCalledTimes(2));
    expect(api.decideLines.mock.calls[1][1]).toMatchObject({ reason: 'Duplicate entries', note: 'Edited note' });
  });

  it('(must-not-fire) the saved text is not offered to a dialog for other lines, nor after the reviewer cancels, nor after a success', async () => {
    const api = mockApi(normal());
    api.decideLines.mockResolvedValueOnce(failed(problem(409, 'ebilling.line.changed', { detail: 'A line changed.' })));
    const first = await openBulk(api);
    typeBulk(first);
    submitBulk(first);
    await screen.findByTestId('notice-reload');
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    // Other lines: untick line 4, so the dialog is for line 2 alone.
    fireEvent.click(within(row(4)).getByRole('checkbox'));
    fireEvent.click(screen.getByTestId('bulk-disallow'));
    const other = await screen.findByRole('dialog');
    expect(within(other).getByLabelText('Reason')).toHaveValue('');
    expect(within(other).getByLabelText('Note')).toHaveValue('');
    fireEvent.click(within(other).getByRole('button', { name: 'Cancel', hidden: true }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    // Back to the first lines: its draft is still there (cancelling the OTHER dialog did not touch it) and a success drops it.
    fireEvent.click(within(row(4)).getByRole('checkbox'));
    fireEvent.click(screen.getByTestId('bulk-disallow'));
    const back = await screen.findByRole('dialog');
    expect(within(back).getByLabelText('Reason')).toHaveValue('Duplicate entries');
    submitBulk(back);
    await waitFor(() => expect(api.decideLines).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    fireEvent.click(within(row(2)).getByRole('checkbox'));
    fireEvent.click(within(row(4)).getByRole('checkbox'));
    fireEvent.click(screen.getByTestId('bulk-disallow'));
    const fresh = await screen.findByRole('dialog');
    expect(within(fresh).getByLabelText('Reason')).toHaveValue('');
    expect(within(fresh).getByLabelText('Note')).toHaveValue('');
  });

  it('(round 2) cancelling a dialog that stayed open on a busy 409 KEEPS its text for the next opening', async () => {
    const api = mockApi(normal());
    api.decideLines.mockResolvedValueOnce(failed(problem(409, 'ebilling.invoice.busy', { detail: 'Busy.' })));
    const first = await openBulk(api);
    typeBulk(first);
    submitBulk(first);
    await within(first).findByTestId('bulk-error');
    fireEvent.click(within(first).getByRole('button', { name: 'Cancel', hidden: true }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    fireEvent.click(screen.getByTestId('bulk-disallow'));
    const again = await screen.findByRole('dialog');
    expect(within(again).getByLabelText('Reason')).toHaveValue('Duplicate entries');
    expect(within(again).getByLabelText('Note')).toHaveValue('Same entry twice');
  });

  it('(must-not-fire) cancelling a prefilled dialog with no failure showing drops its text', async () => {
    const api = mockApi(normal());
    api.decideLines.mockResolvedValueOnce(failed(problem(409, 'ebilling.line.changed', { detail: 'A line changed.' })));
    const first = await openBulk(api);
    typeBulk(first);
    submitBulk(first);
    await screen.findByTestId('notice-reload');
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    // The next opening is prefilled; the reviewer cancels it without sending (no failure is showing in it).
    fireEvent.click(screen.getByTestId('bulk-disallow'));
    const again = await screen.findByRole('dialog');
    expect(within(again).getByLabelText('Reason')).toHaveValue('Duplicate entries');
    fireEvent.click(within(again).getByRole('button', { name: 'Cancel', hidden: true }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    fireEvent.click(screen.getByTestId('bulk-disallow'));
    const fresh = await screen.findByRole('dialog');
    expect(within(fresh).getByLabelText('Reason')).toHaveValue('');
    expect(within(fresh).getByLabelText('Note')).toHaveValue('');
  });

  it('(round 2) a bulk decision answered 409 line.corrections_pending closes the dialog (it cannot be fixed there), and the next opening keeps the text', async () => {
    const api = mockApi(normal());
    api.decideLines.mockResolvedValueOnce(failed(problem(409, 'ebilling.line.corrections_pending', { detail: 'Repeat the corrections on lines 2 and 4 first.' })));
    const first = await openBulk(api);
    typeBulk(first);
    submitBulk(first);
    expect(await screen.findByTestId('notice')).toHaveTextContent('Repeat the corrections on lines 2 and 4 first.');
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    fireEvent.click(screen.getByTestId('bulk-disallow'));
    const again = await screen.findByRole('dialog');
    expect(within(again).getByLabelText('Reason')).toHaveValue('Duplicate entries');
    expect(within(again).getByLabelText('Note')).toHaveValue('Same entry twice');
  });

  it('(round 2) a correction answered 409 correction.firm_missing closes the dialog, and the next opening keeps the value and the note', async () => {
    const api = mockApi(lowHours());
    api.correctLine.mockResolvedValueOnce(failed(problem(409, 'ebilling.correction.firm_missing', { detail: 'This invoice has no firm. Add the firm before correcting its values; nothing was changed.' })));
    const { dialog, pane } = await openCorrect(api);
    fireEvent.change(within(dialog).getByLabelText('Note'), { target: { value: 'Per the timesheet' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save correction', hidden: true }));
    expect(await screen.findByTestId('notice')).toHaveTextContent('This invoice has no firm.');
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    fireEvent.click(within(pane).getByTestId('correct-Hours'));
    const again = await screen.findByRole('dialog');
    expect(within(again).getByLabelText('Correct value')).toHaveValue('2.5');
    expect(within(again).getByLabelText('Note')).toHaveValue('Per the timesheet');
  });

  it('a correction answered 409 code_set_unavailable stays open with the sentence, the value and the note', async () => {
    const detail = 'This line was not coded, so it has no code set to check a code against.';
    const api = mockApi(lowHours());
    api.correctLine.mockResolvedValueOnce(failed(problem(409, 'ebilling.correction.code_set_unavailable', { detail })));
    const { dialog } = await openCorrect(api);
    fireEvent.change(within(dialog).getByLabelText('Note'), { target: { value: 'Per the timesheet' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save correction', hidden: true }));
    expect(await within(dialog).findByTestId('correct-error')).toHaveTextContent(detail);
    expect(within(dialog).getByLabelText('Correct value')).toHaveValue('2.5');
    expect(within(dialog).getByLabelText('Note')).toHaveValue('Per the timesheet');
    expect(screen.queryByTestId('notice')).not.toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save correction', hidden: true }));
    await waitFor(() => expect(api.correctLine).toHaveBeenCalledTimes(2));
    expect(api.correctLine.mock.calls[1][2]).toEqual({ field: 'Hours', value: '2.5', confirm: false, note: 'Per the timesheet' });
  });

  it('a correction answered 409 invoice.changed closes with Reload, and the next opening for the same value is prefilled', async () => {
    const api = mockApi(lowHours());
    api.correctLine.mockResolvedValueOnce(failed(problem(409, 'ebilling.invoice.changed', { detail: 'The invoice changed. Reload and correct it again.' })));
    const { dialog, pane } = await openCorrect(api);
    fireEvent.change(within(dialog).getByLabelText('Note'), { target: { value: 'Per the timesheet' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save correction', hidden: true }));
    expect(await screen.findByTestId('notice-reload')).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    fireEvent.click(within(pane).getByTestId('correct-Hours'));
    const again = await screen.findByRole('dialog');
    expect(within(again).getByLabelText('Correct value')).toHaveValue('2.5');
    expect(within(again).getByLabelText('Note')).toHaveValue('Per the timesheet');
  });

  it('a header correction answered 409 invoice.changed is prefilled with its value and note on the next opening', async () => {
    const api = mockApi(normal());
    api.correctHeader.mockResolvedValueOnce(failed(problem(409, 'ebilling.invoice.changed', { detail: 'The invoice changed.' })));
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(screen.getByTestId('correct-header'));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Correct value'), { target: { value: 'INV-2025-78233' } });
    fireEvent.change(within(dialog).getByLabelText('Note'), { target: { value: 'Typo on the scan' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save correction', hidden: true }));
    await screen.findByTestId('notice-reload');
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    fireEvent.click(screen.getByTestId('correct-header'));
    const again = await screen.findByRole('dialog');
    expect(within(again).getByLabelText('Correct value')).toHaveValue('INV-2025-78233');
    expect(within(again).getByLabelText('Note')).toHaveValue('Typo on the scan');
  });

  it('a confirm answered 409 extraction_unavailable stays open with the sentence and the note; 409 not_in_review closes it and keeps the note for the next opening', async () => {
    const api = mockApi(lowHours());
    api.correctLine
      .mockResolvedValueOnce(failed(problem(409, 'ebilling.correction.extraction_unavailable', { detail: 'The stored reading of this line is unavailable.' })))
      .mockResolvedValueOnce(failed(problem(409, 'ebilling.invoice.not_in_review', { detail: 'The invoice is no longer in review.' })));
    const { dialog, pane } = await openConfirm(api);
    fireEvent.change(within(dialog).getByLabelText('Note'), { target: { value: 'Matches the PDF' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Confirm as read', hidden: true }));
    expect(await within(dialog).findByTestId('confirm-error')).toHaveTextContent('The stored reading of this line is unavailable.');
    expect(within(dialog).getByLabelText('Note')).toHaveValue('Matches the PDF');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Confirm as read', hidden: true }));
    await screen.findByTestId('notice-reload');
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    fireEvent.click(within(pane).getByTestId('confirm-Hours'));
    const again = await screen.findByRole('dialog');
    expect(within(again).getByLabelText('Note')).toHaveValue('Matches the PDF');
  });
});

describe('a refused note shows on the line it came from', () => {
  it('a fault on line 2 is gone when line 4 is selected, and does not come back when line 2 is selected again', async () => {
    const detail = 'The server refused this note.';
    const api = mockApi(normal());
    api.decideLine.mockResolvedValue(failed(problem(400, 'ebilling.note_too_long', { detail })));
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(row(2));
    await screen.findByTestId(`pane-line-${lineId(2)}`);
    fireEvent.change(screen.getByLabelText('Note for line 2'), { target: { value: 'a note' } });
    fireEvent.click(screen.getByTestId('decision-confirm'));
    await waitFor(() => expect(within(screen.getByTestId('line-note-field')).getByText(detail)).toBeInTheDocument());
    fireEvent.click(row(4));
    await screen.findByTestId(`pane-line-${lineId(4)}`);
    expect(screen.queryByText(detail)).not.toBeInTheDocument();
    fireEvent.click(row(2));
    await screen.findByTestId(`pane-line-${lineId(2)}`);
    expect(screen.queryByText(detail)).not.toBeInTheDocument();
  });

  it('(must-not-fire) a fault on a line stays while that line is the one open', async () => {
    const detail = 'The server refused this note.';
    const api = mockApi(normal());
    api.decideLine.mockResolvedValue(failed(problem(400, 'ebilling.note_too_long', { detail })));
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(row(2));
    await screen.findByTestId(`pane-line-${lineId(2)}`);
    fireEvent.change(screen.getByLabelText('Note for line 2'), { target: { value: 'a note' } });
    fireEvent.click(screen.getByTestId('decision-confirm'));
    await waitFor(() => expect(within(screen.getByTestId('line-note-field')).getByText(detail)).toBeInTheDocument());
    expect(screen.getByLabelText('Note for line 2')).toHaveValue('a note');
  });
});
