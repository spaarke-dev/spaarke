/**
 * Task 133 slice A: the interim marking list's agreed contract (ebilling-r1 118's shared component replaces it in
 * slice B). Must-fire and must-not-fire for `defaultInvoiceMarking` and `validateInvoiceMarking`, and the controlled
 * list itself.
 */
import React from 'react';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import {
  InterimInvoiceAttachmentMarking,
  defaultInvoiceMarking,
  validateInvoiceMarking,
  type InvoiceMarking,
  type InvoiceMarkingAttachment,
} from '../InterimInvoiceAttachmentMarking';

const pdf: InvoiceMarkingAttachment = {
  id: 'a1',
  fileName: 'INV-4711.pdf',
  contentType: 'application/pdf',
  sizeBytes: 182344,
};
const csv: InvoiceMarkingAttachment = {
  id: 'a2',
  fileName: 'timesheet.csv',
  contentType: 'text/csv',
  sizeBytes: 40211,
};
const pdf2: InvoiceMarkingAttachment = {
  id: 'a3',
  fileName: 'backup.PDF',
  contentType: 'application/octet-stream',
  sizeBytes: 1000,
};
const tooBig: InvoiceMarkingAttachment = {
  id: 'a4',
  fileName: 'scan.pdf',
  contentType: 'application/pdf',
  sizeBytes: 30_000_000,
  disabledReason: 'It is larger than 20 MB.',
};

describe('defaultInvoiceMarking', () => {
  it('must-fire: exactly one PDF is the Invoice; the others are Supporting', () => {
    expect(defaultInvoiceMarking([pdf, csv])).toEqual({ a1: 'invoice', a2: 'supporting' });
  });

  it('must-fire: an attachment with a disabledReason is skip', () => {
    expect(defaultInvoiceMarking([csv, tooBig])).toEqual({ a2: 'supporting', a4: 'skip' });
  });

  it('a disabled PDF does not count: the one sendable PDF is still the Invoice', () => {
    expect(defaultInvoiceMarking([pdf, tooBig, csv])).toEqual({ a1: 'invoice', a4: 'skip', a2: 'supporting' });
  });

  it('must-not-fire: two PDFs → no Invoice is pre-selected (the user chooses)', () => {
    expect(defaultInvoiceMarking([pdf, pdf2, csv])).toEqual({ a1: 'supporting', a3: 'supporting', a2: 'supporting' });
  });

  it('must-not-fire: no PDF → no Invoice is pre-selected', () => {
    expect(defaultInvoiceMarking([csv])).toEqual({ a2: 'supporting' });
  });

  it('supporting-only never pre-selects an Invoice', () => {
    expect(defaultInvoiceMarking([pdf, csv], 'supporting-only')).toEqual({ a1: 'supporting', a2: 'supporting' });
  });
});

describe('validateInvoiceMarking', () => {
  it('must-fire: zero Invoice marks is refused (new and correction)', () => {
    const marking: InvoiceMarking = { a1: 'supporting', a2: 'supporting' };
    expect(validateInvoiceMarking([pdf, csv], marking, 'new')).toEqual({
      valid: false,
      reason: 'Mark one file as the invoice.',
    });
    expect(validateInvoiceMarking([pdf, csv], marking, 'correction').valid).toBe(false);
  });

  it('must-fire: two Invoice marks are refused', () => {
    expect(validateInvoiceMarking([pdf, pdf2], { a1: 'invoice', a3: 'invoice' }, 'new')).toEqual({
      valid: false,
      reason: 'Only one file can be the invoice.',
    });
  });

  it('must-fire: a disabled attachment cannot be Invoice or Supporting', () => {
    expect(validateInvoiceMarking([tooBig, csv], { a4: 'invoice', a2: 'supporting' }, 'new').valid).toBe(false);
    expect(validateInvoiceMarking([pdf, tooBig], { a1: 'invoice', a4: 'supporting' }, 'new').valid).toBe(false);
  });

  it('must-not-fire: exactly one Invoice, the rest Supporting or skip, disabled ones skip', () => {
    expect(validateInvoiceMarking([pdf, csv, tooBig], { a1: 'invoice', a2: 'skip', a4: 'skip' }, 'new')).toEqual({
      valid: true,
    });
    expect(validateInvoiceMarking([pdf, csv], { a1: 'invoice', a2: 'supporting' }, 'correction')).toEqual({
      valid: true,
    });
  });

  it('an unmarked attachment counts as skip', () => {
    expect(validateInvoiceMarking([pdf, tooBig], { a1: 'invoice' }, 'new')).toEqual({ valid: true });
  });

  it('supporting-only: no Invoice allowed, at least one Supporting needed', () => {
    expect(validateInvoiceMarking([pdf, csv], { a1: 'supporting', a2: 'skip' }, 'supporting-only')).toEqual({
      valid: true,
    });
    expect(validateInvoiceMarking([pdf, csv], { a1: 'invoice', a2: 'supporting' }, 'supporting-only').valid).toBe(
      false
    );
    expect(validateInvoiceMarking([pdf, csv], { a1: 'skip', a2: 'skip' }, 'supporting-only').valid).toBe(false);
  });
});

describe('InterimInvoiceAttachmentMarking (controlled list)', () => {
  function renderList(props: Partial<React.ComponentProps<typeof InterimInvoiceAttachmentMarking>> = {}) {
    const onChange = jest.fn();
    render(
      <FluentProvider theme={webLightTheme}>
        <InterimInvoiceAttachmentMarking
          attachments={[pdf, csv, tooBig]}
          value={{ a1: 'invoice', a2: 'supporting', a4: 'skip' }}
          onChange={onChange}
          mode="new"
          {...props}
        />
      </FluentProvider>
    );
    return onChange;
  }

  it('choosing a new Invoice makes the previous Invoice Supporting', async () => {
    const onChange = renderList();
    const row = screen.getByRole('listitem', { name: 'timesheet.csv' });
    await userEvent.click(within(row).getByRole('radio', { name: 'Invoice' }));
    expect(onChange).toHaveBeenCalledWith({ a1: 'supporting', a2: 'invoice', a4: 'skip' });
  });

  it('a disabled attachment shows its reason and cannot be changed', () => {
    renderList();
    const row = screen.getByRole('listitem', { name: 'scan.pdf' });
    expect(within(row).getByText('It is larger than 20 MB.')).toBeInTheDocument();
    for (const radio of within(row).getAllByRole('radio')) {
      expect(radio).toBeDisabled();
    }
    expect(within(row).getByRole('radio', { name: "Don't save" })).toBeChecked();
  });

  it('supporting-only offers no Invoice choice', () => {
    renderList({ mode: 'supporting-only', value: { a1: 'supporting', a2: 'supporting', a4: 'skip' } });
    expect(screen.queryByRole('radio', { name: 'Invoice' })).toBeNull();
  });
});
