/**
 * Task 095f-2ui: the optional Note box on the page's own decisions: the line decision in the pane, the bulk decision, the corrections
 * (correct, confirm, header), and Accept all Clean, which has none.
 *
 * Through the real page, the real shared FormModal and a mocked BFF client (the harness of InvoiceReviewPage.test.tsx). What is asserted is
 * what the client is asked to send: `note` as typed when there is one, no `note` key when blank, the same decision id and note on Repeat, a new
 * decision id for a new submit, and a refused note shown under the Note box and not as a page notice.
 */

import * as fs from 'fs';
import * as path from 'path';
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
const row = (n: number) => screen.getByTestId(`line-row-${lineId(n)}`);

/** The pane of line 2 (a flagged line with a suggested reduction), open. */
async function openLine2(api: MockApi) {
  renderPage(api);
  await screen.findByTestId('review-title');
  fireEvent.click(row(2));
  await screen.findByTestId(`pane-line-${lineId(2)}`);
}

const noteBox = () => screen.getByLabelText('Note for line 2') as HTMLTextAreaElement;
const typeNote = (text: string) => fireEvent.change(noteBox(), { target: { value: text } });
const confirm = () => screen.getByTestId('decision-confirm');
const decideBody = (api: MockApi, i = 0) => api.decideLine.mock.calls[i][2] as Record<string, unknown>;

describe('the line decision: the Note box', () => {
  it('is empty and optional, counts against 2000, and has no maxlength to cut a paste', async () => {
    const api = mockApi(normal());
    await openLine2(api);
    expect(noteBox().value).toBe('');
    expect(noteBox()).not.toBeRequired();
    expect(noteBox()).not.toHaveAttribute('maxlength');
    expect(screen.getByTestId('line-note-count')).toHaveTextContent('0 / 2,000 characters');
    typeNote('  Rate card 2025  ');
    expect(screen.getByTestId('line-note-count')).toHaveTextContent('14 / 2,000 characters');
    // A decision is still one Confirm: typing a note does not stop it.
    expect(confirm()).toBeEnabled();
  });

  it('sends the note as typed with the decision, and the decision id and amount as before', async () => {
    const api = mockApi(normal());
    await openLine2(api);
    typeNote('  Rate card 2025  ');
    fireEvent.click(confirm());
    await waitFor(() => expect(api.decideLine).toHaveBeenCalledTimes(1));
    expect(decideBody(api)).toEqual({ decisionId: expect.any(String), action: 'reduce', reason: null, approvedAmount: 1012.5, note: '  Rate card 2025  ' });
  });

  it('sends a note with an Accept, which has no reason', async () => {
    const api = mockApi(normal());
    await openLine2(api);
    fireEvent.click(screen.getByRole('radio', { name: /^Accept at/ }));
    typeNote('Fine on a second look');
    fireEvent.click(confirm());
    await waitFor(() => expect(api.decideLine).toHaveBeenCalledTimes(1));
    expect(decideBody(api)).toEqual({ decisionId: expect.any(String), action: 'accept', reason: null, approvedAmount: null, note: 'Fine on a second look' });
  });

  it.each([
    ['nothing typed', ''],
    ['spaces', '    '],
    ['a line break and a tab', ' \n\t '],
  ])('with %s the note key is absent (not null, not "")', async (_, text) => {
    const api = mockApi(normal());
    await openLine2(api);
    if (text) typeNote(text);
    fireEvent.click(confirm());
    await waitFor(() => expect(api.decideLine).toHaveBeenCalledTimes(1));
    expect(Object.keys(decideBody(api))).not.toContain('note');
    expect(decideBody(api)).toEqual({ decisionId: expect.any(String), action: 'reduce', reason: null, approvedAmount: 1012.5 });
  });

  it('a paste over 2000 characters is kept whole and counted, Confirm is held and nothing is sent; cut to 2000 it sends the whole 2000', async () => {
    const api = mockApi(normal());
    await openLine2(api);
    typeNote('y'.repeat(2600));
    expect(noteBox().value).toHaveLength(2600);
    expect(noteBox()).not.toHaveAttribute('maxlength');
    expect(screen.getByTestId('line-note-count')).toHaveTextContent('2,600 / 2,000 characters');
    expect(screen.getByTestId('line-note-field')).toHaveTextContent('The note may be at most 2000 characters.');
    expect(confirm()).toBeDisabled();
    fireEvent.click(confirm());
    expect(api.decideLine).not.toHaveBeenCalled();
    typeNote('y'.repeat(2000));
    expect(screen.getByTestId('line-note-count')).toHaveTextContent('2,000 / 2,000 characters');
    expect(confirm()).toBeEnabled();
    fireEvent.click(confirm());
    await waitFor(() => expect(api.decideLine).toHaveBeenCalledTimes(1));
    expect(decideBody(api).note).toBe('y'.repeat(2000));
  });

  it.each([
    ['ebilling.note_too_long', 'The note may be at most 2000 characters.'],
    ['ebilling.note_invalid_characters', 'The note contains a control character that cannot be saved. Remove it and try again.'],
  ])('a 400 %s is a message under the Note box, not a page notice, and the form keeps what was typed', async (code, detail) => {
    const api = mockApi(normal());
    api.decideLine.mockResolvedValue(failed(problem(400, code, { detail })));
    await openLine2(api);
    fireEvent.change(screen.getByLabelText('Reason for line 2'), { target: { value: 'Above the approved rate' } });
    typeNote('a note');
    fireEvent.click(confirm());
    await waitFor(() => expect(within(screen.getByTestId('line-note-field')).getByText(detail)).toBeInTheDocument());
    expect(screen.queryByTestId('notice')).not.toBeInTheDocument();
    expect(noteBox().value).toBe('a note');
    expect(screen.getByLabelText('Reason for line 2')).toHaveValue('Above the approved rate');
    // Nothing was saved, so the page was not re-read; editing the note clears the message.
    expect(api.getReview).toHaveBeenCalledTimes(1);
    typeNote('a shorter note');
    expect(within(screen.getByTestId('line-note-field')).queryByText(detail)).not.toBeInTheDocument();
  });

  it('keeps the reason and the note across a 409 and does not reload the page', async () => {
    const api = mockApi(normal());
    api.decideLine.mockResolvedValue(failed(problem(409, 'ebilling.line.changed', { detail: 'The line changed. Reload and decide again.' })));
    await openLine2(api);
    fireEvent.change(screen.getByLabelText('Reason for line 2'), { target: { value: 'Above the approved rate' } });
    typeNote('Keep this note');
    fireEvent.click(confirm());
    await waitFor(() => expect(screen.getByTestId('notice')).toHaveTextContent('The line changed'));
    expect(noteBox().value).toBe('Keep this note');
    expect(screen.getByLabelText('Reason for line 2')).toHaveValue('Above the approved rate');
    // The page offers Reload, it does not reload by itself.
    expect(api.getReview).toHaveBeenCalledTimes(1);
  });

  it('Repeat after a 503 resends the same decision id and the same note, and a new submit with an edited note is a new decision id', async () => {
    const api = mockApi(normal());
    api.decideLine
      .mockResolvedValueOnce(failed(problem(503, 'ebilling.decision.incomplete', { detail: 'The line was saved; the record was not.', failedStep: 'record' })))
      .mockResolvedValueOnce(ok({ decisionId: 'x', lines: [], totals: null }));
    await openLine2(api);
    typeNote('First note');
    fireEvent.click(confirm());
    fireEvent.click(await screen.findByTestId('notice-repeat'));
    await waitFor(() => expect(api.decideLine).toHaveBeenCalledTimes(2));
    expect(api.decideLine.mock.calls[1][2]).toEqual(api.decideLine.mock.calls[0][2]);
    expect(decideBody(api, 1).note).toBe('First note');
    expect(decideBody(api, 1).decisionId).toBe(decideBody(api, 0).decisionId);
  });

  it('a new submit with an edited note gets a new decision id (the server refuses another note under the same id)', async () => {
    const api = mockApi(normal());
    api.decideLine.mockResolvedValueOnce(failed(problem(409, 'ebilling.line.changed', { detail: 'The line changed.' })));
    await openLine2(api);
    typeNote('First note');
    fireEvent.click(confirm());
    await waitFor(() => expect(screen.getByTestId('notice')).toBeInTheDocument());
    typeNote('Edited note');
    fireEvent.click(confirm());
    await waitFor(() => expect(api.decideLine).toHaveBeenCalledTimes(2));
    expect(decideBody(api, 1).note).toBe('Edited note');
    expect(decideBody(api, 1).decisionId).not.toBe(decideBody(api, 0).decisionId);
  });

});

/**
 * Dark mode is a property of the source (ADR-021): every colour comes from a Fluent token, so the theme provider decides it. A render under
 * `webDarkTheme` proves nothing about that (jsdom resolves no styles), so the Note box's source is scanned instead.
 */
describe('the Note box takes its colours from Fluent tokens only', () => {
  const COLOUR_LITERAL = /#[0-9a-fA-F]{3,8}\b|\b(?:rgb|rgba|hsl|hsla)\(/;
  const noteFieldSource = () => fs.readFileSync(path.resolve(__dirname, '..', 'components', 'NoteField.tsx'), 'utf8');

  it('NoteField.tsx styles with tokens and has no hex, rgb() or hsl() colour', () => {
    const source = noteFieldSource();
    expect(source).toMatch(/color: tokens\.color\w+/);
    expect(source).not.toMatch(COLOUR_LITERAL);
  });

  it('(controls) the scan fires on a hex and an rgb() colour and stays quiet on a token', () => {
    expect(COLOUR_LITERAL.test("const a = { color: '#fff' };")).toBe(true);
    expect(COLOUR_LITERAL.test("const a = { color: 'rgb(0, 0, 0)' };")).toBe(true);
    expect(COLOUR_LITERAL.test('const a = { color: tokens.colorNeutralForeground3 };')).toBe(false);
  });
});

describe('the bulk decision: one Note for every selected line', () => {
  async function openBulk(api: MockApi) {
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(within(row(2)).getByRole('checkbox'));
    fireEvent.click(within(row(4)).getByRole('checkbox'));
    fireEvent.click(screen.getByTestId('bulk-disallow'));
    return screen.findByRole('dialog');
  }

  it('sends the note once, beside the reason and the lines', async () => {
    const api = mockApi(normal());
    const dialog = await openBulk(api);
    fireEvent.change(within(dialog).getByLabelText('Reason'), { target: { value: 'Duplicate entries' } });
    fireEvent.change(within(dialog).getByLabelText('Note'), { target: { value: 'Same entry twice' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Disallow 2 lines' }));
    await waitFor(() => expect(api.decideLines).toHaveBeenCalledTimes(1));
    expect(api.decideLines.mock.calls[0][1]).toEqual({
      decisionId: expect.any(String),
      action: 'disallow',
      reason: 'Duplicate entries',
      lines: [
        { lineId: lineId(2), approvedAmount: null },
        { lineId: lineId(4), approvedAmount: null },
      ],
      note: 'Same entry twice',
    });
  });

  it('omits the key when the note is blank or whitespace', async () => {
    const api = mockApi(normal());
    const dialog = await openBulk(api);
    fireEvent.change(within(dialog).getByLabelText('Reason'), { target: { value: 'Duplicate entries' } });
    fireEvent.change(within(dialog).getByLabelText('Note'), { target: { value: '   ' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Disallow 2 lines' }));
    await waitFor(() => expect(api.decideLines).toHaveBeenCalledTimes(1));
    expect(Object.keys(api.decideLines.mock.calls[0][1])).not.toContain('note');
  });

  it('shows a refused note under the Note box in the form, and keeps the form open with everything typed', async () => {
    const api = mockApi(normal());
    api.decideLines.mockResolvedValue(failed(problem(400, 'ebilling.note_invalid_characters', { detail: 'The note contains a control character that cannot be saved. Remove it and try again.' })));
    const dialog = await openBulk(api);
    fireEvent.change(within(dialog).getByLabelText('Reason'), { target: { value: 'Duplicate entries' } });
    fireEvent.change(within(dialog).getByLabelText('Note'), { target: { value: 'bad\u0007note' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Disallow 2 lines' }));
    await waitFor(() => expect(within(within(dialog).getByTestId('bulk-note-field')).getByText(/control character/)).toBeInTheDocument());
    expect(screen.queryByTestId('notice')).not.toBeInTheDocument();
    expect(within(dialog).queryByTestId('bulk-error')).not.toBeInTheDocument();
    expect(within(dialog).getByLabelText('Note')).toHaveValue('bad\u0007note');
    expect(within(dialog).getByLabelText('Reason')).toHaveValue('Duplicate entries');
  });

  it('Repeat of a partly saved bulk decision resends the same request, note included', async () => {
    const api = mockApi(normal());
    api.decideLines
      .mockResolvedValueOnce(failed(problem(503, 'ebilling.decision.incomplete', { detail: 'The lines were saved; the totals were not.', failedStep: 'totals' })))
      .mockResolvedValueOnce(ok({ decisionId: 'x', lines: [], totals: null }));
    const dialog = await openBulk(api);
    fireEvent.change(within(dialog).getByLabelText('Reason'), { target: { value: 'Duplicate entries' } });
    fireEvent.change(within(dialog).getByLabelText('Note'), { target: { value: 'Same entry twice' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Disallow 2 lines' }));
    fireEvent.click(await screen.findByTestId('notice-repeat'));
    await waitFor(() => expect(api.decideLines).toHaveBeenCalledTimes(2));
    expect(api.decideLines.mock.calls[1][1]).toEqual(api.decideLines.mock.calls[0][1]);
    expect((api.decideLines.mock.calls[1][1] as { note?: string }).note).toBe('Same entry twice');
  });
});

describe('Accept all Clean has no Note box and never sends a note (must-not-fire)', () => {
  it('sends the decision id only, and the page shows no Note box before, during or after', async () => {
    const api = mockApi(review([line(1), line(2), flagged(3)]));
    renderPage(api);
    await screen.findByTestId('review-title');
    expect(screen.queryByLabelText(/^Note/)).not.toBeInTheDocument();
    fireEvent.click(screen.getByTestId('accept-clean'));
    await waitFor(() => expect(api.acceptClean).toHaveBeenCalledTimes(1));
    expect(api.acceptClean.mock.calls[0]).toEqual([INVOICE_ID, expect.any(String)]);
    expect(api.decideLine).not.toHaveBeenCalled();
    expect(api.decideLines).not.toHaveBeenCalled();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/^Note/)).not.toBeInTheDocument();
  });

  it('its Repeat after a partly saved accept goes through the multi-line route with the committed ids and still no note', async () => {
    const api = mockApi(review([line(1), line(2), flagged(3)]));
    api.acceptClean.mockResolvedValueOnce(
      failed(problem(503, 'ebilling.decision.incomplete', { detail: 'Some lines were accepted.', failedStep: 'accept-clean', committedLineIds: [lineId(1)] }))
    );
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(screen.getByTestId('accept-clean'));
    fireEvent.click(await screen.findByTestId('notice-repeat'));
    await waitFor(() => expect(api.decideLines).toHaveBeenCalledTimes(1));
    expect(api.decideLines.mock.calls[0][1]).toEqual({
      decisionId: api.acceptClean.mock.calls[0][1],
      action: 'accept',
      reason: null,
      lines: [{ lineId: lineId(1), approvedAmount: null }],
    });
    expect(Object.keys(api.decideLines.mock.calls[0][1])).not.toContain('note');
  });
});

describe('the corrections: a Note on correct, confirm and the header', () => {
  const lowHours = () =>
    review([
      flagged(7, {
        quantity: 25,
        fields: { ...line(7).fields, Hours: field({ lowConfidence: true, combined: 0.42, box: { page: 1, x: 0.6, y: 0.5, w: 0.05, h: 0.02 } }) },
        findings: [finding({ policyCode: 'HOURS-DAY', valuesRead: [{ name: 'Hours', value: '25', field: 'Hours', confidence: 0.42, belowReliabilityThreshold: true }] })],
      }),
    ]);

  async function openLine7(api: MockApi) {
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(row(7));
    return screen.findByTestId(`pane-line-${lineId(7)}`);
  }

  it('correct: sends the note as typed with the corrected value', async () => {
    const api = mockApi(lowHours());
    const pane = await openLine7(api);
    fireEvent.click(within(pane).getByTestId('correct-Hours'));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Correct value'), { target: { value: '2.5' } });
    fireEvent.change(within(dialog).getByLabelText('Note'), { target: { value: ' Per the timesheet ' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save correction' }));
    await waitFor(() => expect(api.correctLine).toHaveBeenCalledTimes(1));
    expect(api.correctLine).toHaveBeenCalledWith(INVOICE_ID, lineId(7), { field: 'Hours', value: '2.5', confirm: false, note: ' Per the timesheet ' });
  });

  it('correct with a blank note: the key is absent', async () => {
    const api = mockApi(lowHours());
    const pane = await openLine7(api);
    fireEvent.click(within(pane).getByTestId('correct-Hours'));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Correct value'), { target: { value: '2.5' } });
    fireEvent.change(within(dialog).getByLabelText('Note'), { target: { value: '  ' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save correction' }));
    await waitFor(() => expect(api.correctLine).toHaveBeenCalledTimes(1));
    expect(Object.keys(api.correctLine.mock.calls[0][2])).toEqual(['field', 'value', 'confirm']);
  });

  it('confirm as read: opens a small form with the Note, and sends confirm true with the note', async () => {
    const api = mockApi(lowHours());
    const pane = await openLine7(api);
    fireEvent.click(within(pane).getByTestId('confirm-Hours'));
    const dialog = await screen.findByRole('dialog');
    expect(api.correctLine).not.toHaveBeenCalled();
    fireEvent.change(within(dialog).getByLabelText('Note'), { target: { value: 'Matches the PDF' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Confirm as read' }));
    await waitFor(() => expect(api.correctLine).toHaveBeenCalledTimes(1));
    expect(api.correctLine).toHaveBeenCalledWith(INVOICE_ID, lineId(7), { field: 'Hours', value: null, confirm: true, note: 'Matches the PDF' });
  });

  it('header correction: sends the note through the header route', async () => {
    const api = mockApi(normal());
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(screen.getByTestId('correct-header'));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Correct value'), { target: { value: 'INV-2025-78233' } });
    fireEvent.change(within(dialog).getByLabelText('Note'), { target: { value: 'Typo on the scan' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save correction' }));
    await waitFor(() =>
      expect(api.correctHeader).toHaveBeenCalledWith(INVOICE_ID, { field: 'FirmInvoiceNumber', value: 'INV-2025-78233', confirm: false, note: 'Typo on the scan' })
    );
  });

  it('a refused note on a correction is a message under the Note box in the form, not the form-level error', async () => {
    const api = mockApi(lowHours());
    api.correctLine.mockResolvedValue(failed(problem(400, 'ebilling.note_too_long', { detail: 'The note may be at most 2000 characters.' })));
    const pane = await openLine7(api);
    fireEvent.click(within(pane).getByTestId('correct-Hours'));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Correct value'), { target: { value: '2.5' } });
    fireEvent.change(within(dialog).getByLabelText('Note'), { target: { value: 'a note' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save correction' }));
    await waitFor(() => expect(within(within(dialog).getByTestId('correct-note-field')).getByText('The note may be at most 2000 characters.')).toBeInTheDocument());
    expect(within(dialog).queryByTestId('correct-error')).not.toBeInTheDocument();
    expect(screen.queryByTestId('notice')).not.toBeInTheDocument();
    expect(within(dialog).getByLabelText('Note')).toHaveValue('a note');
  });

  it('a refused note on a confirm is a message under the Note box in its form', async () => {
    const api = mockApi(lowHours());
    api.correctLine.mockResolvedValue(failed(problem(400, 'ebilling.note_invalid_characters', { detail: 'The note contains a control character that cannot be saved. Remove it and try again.' })));
    const pane = await openLine7(api);
    fireEvent.click(within(pane).getByTestId('confirm-Hours'));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Note'), { target: { value: 'bad\u0007' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Confirm as read' }));
    await waitFor(() => expect(within(within(dialog).getByTestId('confirm-note-field')).getByText(/control character/)).toBeInTheDocument());
    expect(within(dialog).queryByTestId('confirm-error')).not.toBeInTheDocument();
  });
});
