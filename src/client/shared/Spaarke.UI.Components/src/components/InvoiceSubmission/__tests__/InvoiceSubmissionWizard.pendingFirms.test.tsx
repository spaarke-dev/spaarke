/**
 * InvoiceSubmissionWizard — the firm list pending, in a modal (ebilling-r1 def071 review, F2).
 *
 * The shell fixes its steps at mount, so the wizard waits for the firm list. Embedded, a bare spinner stands in. In a modal (the
 * in-app host, `embedded={false}`) a bare spinner would show OUTSIDE the modal frame (a flash in the Console layout), and unmounting
 * the shell for the reload after `firm_required` would close and reopen the dialog and lose its focus: the dialog stays mounted and
 * shows the spinner inside it.
 */
import * as React from 'react';
import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { InvoiceSubmissionWizard } from '../InvoiceSubmissionWizard';
import { InvoiceSubmissionError } from '../types';
import type { InvoiceFirmOption } from '../types';

const MATTER = {
  kind: 'matter' as const,
  recordId: '11111111-1111-4111-8111-111111111111',
  name: 'Acme v. Roadrunner',
  number: 'M-1001',
};
const ALPHA: InvoiceFirmOption = { firmId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', name: 'Alpha LLP' };
const BETA: InvoiceFirmOption = { firmId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', name: 'Beta LLP' };

function mount(over: Partial<React.ComponentProps<typeof InvoiceSubmissionWizard>>) {
  return renderWithProviders(
    <InvoiceSubmissionWizard
      loadRecords={jest.fn().mockResolvedValue({ records: [MATTER] })}
      submit={jest.fn()}
      getStatus={jest.fn()}
      onClose={jest.fn()}
      {...over}
    />
  );
}

describe('InvoiceSubmissionWizard — firms pending in a modal', () => {
  it('must fire: not embedded, with the firms pending, the spinner is inside a dialog and nothing is rendered outside it', () => {
    const { container } = mount({
      embedded: false,
      loadFirms: () => new Promise<InvoiceFirmOption[] | null>(() => undefined),
    });

    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByRole('progressbar')).toBeInTheDocument();
    // No bare spinner in the host layout (the Console): the render container holds no progressbar of its own.
    expect(within(container).queryByRole('progressbar')).toBeNull();
    // The loading placeholder cannot be submitted.
    expect(within(dialog).getByRole('button', { name: /^submit invoice$/i })).toBeDisabled();
  });

  it('the dialog stays the same element when the firms arrive, and the Choose firm step replaces the spinner', async () => {
    let release: (firms: InvoiceFirmOption[]) => void = () => undefined;
    mount({ embedded: false, loadFirms: () => new Promise<InvoiceFirmOption[]>(resolve => (release = resolve)) });
    const dialog = screen.getByRole('dialog');

    await act(async () => release([ALPHA, BETA]));

    expect(await screen.findByText('Choose the firm this invoice is from')).toBeInTheDocument();
    expect(screen.getByRole('dialog')).toBe(dialog);
    expect(screen.queryByRole('progressbar')).toBeNull();
  });

  it('a firm_required reload keeps the dialog mounted (no close and reopen) and ends on the Choose firm step', async () => {
    const loadFirms = jest.fn().mockResolvedValueOnce(null).mockResolvedValue([ALPHA, BETA]);
    const submit = jest
      .fn()
      .mockRejectedValue(
        new InvoiceSubmissionError({ statusCode: 400, errorCode: 'ebilling.submission.firm_required' })
      );
    const { container } = mount({ embedded: false, loadFirms, submit, initialRecord: MATTER });

    const next = () => screen.getByRole('button', { name: /^next$/i });
    await screen.findByRole('radio', { name: /Acme v\. Roadrunner/ });
    const dialog = screen.getByRole('dialog');
    fireEvent.click(next());
    fireEvent.change(document.querySelector('input[type="file"]') as HTMLInputElement, {
      target: { files: [new File(['%PDF-1.4'], 'invoice.pdf', { type: 'application/pdf' })] },
    });
    await waitFor(() => expect(next()).toBeEnabled());
    fireEvent.click(next());
    fireEvent.change(await screen.findByLabelText(/Firm invoice number/i), { target: { value: 'INV-9' } });
    fireEvent.click(screen.getByRole('button', { name: /^submit invoice$/i }));

    expect(await screen.findByText('Choose the firm this invoice is from')).toBeInTheDocument();
    expect(loadFirms).toHaveBeenCalledTimes(2);
    expect(screen.getByRole('dialog')).toBe(dialog);
    expect(within(container).queryByRole('progressbar')).toBeNull();
  });

  it('must not fire: embedded, the pending state is still the bare spinner with no dialog', () => {
    mount({ embedded: true, loadFirms: () => new Promise<InvoiceFirmOption[] | null>(() => undefined) });
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(screen.getByRole('progressbar')).toBeInTheDocument();
  });
});
