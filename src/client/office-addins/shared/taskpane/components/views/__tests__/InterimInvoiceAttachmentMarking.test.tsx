/**
 * Task 133 slice A: the interim marking list is a drop-in for ebilling-r1's shared `InvoiceAttachmentMarking`
 * (PR #2342) — same names, props and rules. Must-fire and must-not-fire for `defaultInvoiceMarking` and
 * `validateInvoiceMarking`, and the controlled list itself. Slice B points these at the shared component; they should
 * pass there unchanged.
 */
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import {
  InvoiceAttachmentMarking,
  defaultInvoiceMarking,
  validateInvoiceMarking,
  type InvoiceAttachmentMarkingProps,
  type MarkableAttachment,
} from '../InterimInvoiceAttachmentMarking';

const pdf: MarkableAttachment = {
  id: 'a1',
  fileName: 'INV-4711.pdf',
  contentType: 'application/pdf',
  sizeBytes: 182344,
};
const csv: MarkableAttachment = { id: 'a2', fileName: 'timesheet.csv', contentType: 'text/csv', sizeBytes: 40211 };
/** A PDF by name only (content type does not say PDF). */
const pdfByName: MarkableAttachment = {
  id: 'a3',
  fileName: ' backup.PDF ',
  contentType: 'application/octet-stream',
  sizeBytes: 1000,
};
const tooBig: MarkableAttachment = {
  id: 'a4',
  fileName: 'scan.pdf',
  contentType: 'application/pdf',
  sizeBytes: 30_000_000,
  disabledReason: 'It is larger than 20 MB.',
};

describe('defaultInvoiceMarking', () => {
  it('must-fire: exactly one PDF is the Invoice; the others are Supporting', () => {
    expect(defaultInvoiceMarking([pdf, csv], 'new')).toEqual({ a1: 'invoice', a2: 'supporting' });
  });

  it('a PDF by file name counts (content type octet-stream)', () => {
    expect(defaultInvoiceMarking([pdfByName, csv], 'new')).toEqual({ a3: 'invoice', a2: 'supporting' });
  });

  it('must-fire: an attachment with a disabledReason is skip, and a disabled PDF does not count', () => {
    expect(defaultInvoiceMarking([pdf, tooBig, csv], 'correction')).toEqual({
      a1: 'invoice',
      a4: 'skip',
      a2: 'supporting',
    });
  });

  it('must-fire: of two PDFs, exactly one suggestedInvoice is pre-selected', () => {
    expect(defaultInvoiceMarking([pdf, { ...pdfByName, suggestedInvoice: true }], 'new')).toEqual({
      a1: 'supporting',
      a3: 'invoice',
    });
  });

  it('must-not-fire: two PDFs and none suggested → no Invoice is pre-selected', () => {
    expect(defaultInvoiceMarking([pdf, pdfByName, csv], 'new')).toEqual({
      a1: 'supporting',
      a3: 'supporting',
      a2: 'supporting',
    });
  });

  it('must-not-fire: two suggested PDFs → no Invoice is pre-selected', () => {
    expect(
      defaultInvoiceMarking(
        [
          { ...pdf, suggestedInvoice: true },
          { ...pdfByName, suggestedInvoice: true },
        ],
        'new'
      )
    ).toEqual({ a1: 'supporting', a3: 'supporting' });
  });

  it('must-not-fire: a suggested non-PDF is never the Invoice', () => {
    expect(defaultInvoiceMarking([{ ...csv, suggestedInvoice: true }], 'new')).toEqual({ a2: 'supporting' });
  });

  it('supporting-only never pre-selects an Invoice', () => {
    expect(defaultInvoiceMarking([pdf, csv], 'supporting-only')).toEqual({ a1: 'supporting', a2: 'supporting' });
  });
});

describe('validateInvoiceMarking', () => {
  const all = [pdf, csv, pdfByName, tooBig];

  it('must-fire: zero Invoice marks → no-invoice (new and correction)', () => {
    const value = { a1: 'supporting', a2: 'supporting' } as const;
    expect(validateInvoiceMarking(value, 'new', all)).toEqual({ ok: false, reason: 'no-invoice' });
    expect(validateInvoiceMarking(value, 'correction', all)).toEqual({ ok: false, reason: 'no-invoice' });
  });

  it('must-fire: two Invoice marks → more-than-one-invoice', () => {
    expect(validateInvoiceMarking({ a1: 'invoice', a3: 'invoice' }, 'new', all)).toEqual({
      ok: false,
      reason: 'more-than-one-invoice',
    });
  });

  it('must-fire: a non-PDF marked Invoice → invoice-not-allowed (with attachments)', () => {
    expect(validateInvoiceMarking({ a2: 'invoice', a1: 'supporting' }, 'new', all)).toEqual({
      ok: false,
      reason: 'invoice-not-allowed',
    });
  });

  it('must-fire: a disabled attachment marked Invoice OR Supporting is refused', () => {
    expect(validateInvoiceMarking({ a4: 'invoice', a2: 'supporting' }, 'new', all)).toEqual({
      ok: false,
      reason: 'disabled-attachment-marked',
    });
    expect(validateInvoiceMarking({ a1: 'invoice', a4: 'supporting' }, 'new', all)).toEqual({
      ok: false,
      reason: 'disabled-attachment-marked',
    });
  });

  it('without attachments only counts and mode are checked', () => {
    expect(validateInvoiceMarking({ a2: 'invoice' }, 'new')).toEqual({ ok: true });
  });

  it('must-not-fire: exactly one PDF Invoice, the rest Supporting or skip, disabled ones skip', () => {
    expect(validateInvoiceMarking({ a1: 'invoice', a2: 'skip', a3: 'supporting', a4: 'skip' }, 'new', all)).toEqual({
      ok: true,
    });
    expect(validateInvoiceMarking({ a3: 'invoice', a2: 'supporting' }, 'correction', all)).toEqual({ ok: true });
  });

  it('supporting-only: any Invoice → invoice-not-allowed; otherwise ok', () => {
    expect(validateInvoiceMarking({ a1: 'supporting', a2: 'skip' }, 'supporting-only', all)).toEqual({ ok: true });
    expect(validateInvoiceMarking({ a1: 'invoice' }, 'supporting-only', all)).toEqual({
      ok: false,
      reason: 'invoice-not-allowed',
    });
  });
});

describe('InvoiceAttachmentMarking (controlled list)', () => {
  function renderList(props: Partial<InvoiceAttachmentMarkingProps> = {}) {
    const onChange = jest.fn();
    render(
      <FluentProvider theme={webLightTheme}>
        <InvoiceAttachmentMarking
          attachments={[pdf, csv, pdfByName, tooBig]}
          value={{ a1: 'invoice', a2: 'supporting', a3: 'supporting', a4: 'skip' }}
          onChange={onChange}
          mode="new"
          {...props}
        />
      </FluentProvider>
    );
    return onChange;
  }

  const row = (fileName: string) =>
    screen.getAllByRole('listitem').find(item => within(item).queryByText(fileName.trim()) !== null)!;

  it('a non-PDF row offers no Invoice choice; PDF rows do', () => {
    renderList();
    expect(within(row('timesheet.csv')).queryByRole('radio', { name: 'Invoice' })).toBeNull();
    expect(within(row('INV-4711.pdf')).getByRole('radio', { name: 'Invoice' })).toBeInTheDocument();
    expect(within(row('backup.PDF')).getByRole('radio', { name: 'Invoice' })).toBeInTheDocument();
  });

  it('choosing a mark reports the whole value with that one change', async () => {
    const onChange = renderList();
    await userEvent.click(within(row('backup.PDF')).getByRole('radio', { name: 'Invoice' }));
    expect(onChange).toHaveBeenCalledWith({ a1: 'invoice', a2: 'supporting', a3: 'invoice', a4: 'skip' });
  });

  it('a disabled attachment shows its reason, offers no Invoice and cannot be changed', () => {
    renderList();
    const disabledRow = row('scan.pdf');
    expect(within(disabledRow).getByText('It is larger than 20 MB.')).toBeInTheDocument();
    expect(within(disabledRow).queryByRole('radio', { name: 'Invoice' })).toBeNull();
    for (const radio of within(disabledRow).getAllByRole('radio')) {
      expect(radio).toBeDisabled();
    }
    expect(within(disabledRow).getByRole('radio', { name: "Don't save" })).toBeChecked();
  });

  it('supporting-only offers no Invoice choice', () => {
    renderList({ mode: 'supporting-only', value: { a1: 'supporting', a2: 'supporting', a3: 'skip', a4: 'skip' } });
    expect(screen.queryByRole('radio', { name: 'Invoice' })).toBeNull();
  });
});
