/**
 * InvoiceSubmissionWizard — `lookupRecord` (ebilling-r1 def071, GitHub #3335): a host that can open a Dataverse lookup gives the Record
 * step a "Choose a record" button, so a record outside the first page of the list can be picked. Without the prop the step is exactly
 * what the external SPA (no Xrm) shows.
 */
import * as React from 'react';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { InvoiceSubmissionWizard } from '../InvoiceSubmissionWizard';
import { InvoiceSubmissionError } from '../types';
import type { InvoiceCoreRecordList, InvoiceCoreRecordOption, InvoiceFirmOption } from '../types';

const MATTER: InvoiceCoreRecordOption = {
  kind: 'matter',
  recordId: '11111111-1111-4111-8111-111111111111',
  name: 'Acme v. Roadrunner',
  number: 'M-1001',
};
const OUTSIDE_PAGE: InvoiceCoreRecordOption = {
  kind: 'project',
  recordId: '33333333-3333-4333-8333-333333333333',
  name: 'Beyond the first page',
};

function mount(options: {
  lookupRecord?: () => Promise<InvoiceCoreRecordOption | null>;
  lockedRecord?: InvoiceCoreRecordOption;
  loadRecords?: () => Promise<InvoiceCoreRecordList>;
  loadFirms?: () => Promise<InvoiceFirmOption[] | null>;
  submit?: jest.Mock;
}) {
  return renderWithProviders(
    <InvoiceSubmissionWizard
      embedded
      lookupRecord={options.lookupRecord}
      lockedRecord={options.lockedRecord}
      loadRecords={options.loadRecords ?? jest.fn().mockResolvedValue({ records: [MATTER] })}
      loadFirms={options.loadFirms}
      submit={options.submit ?? jest.fn()}
      getStatus={jest.fn()}
      onClose={jest.fn()}
    />
  );
}

const next = () => screen.getByRole('button', { name: /^next$/i });
const chooseButton = () => screen.getByTestId('choose-record-button');

describe('InvoiceSubmissionWizard — lookupRecord', () => {
  it('must fire: the button picks a record the list does not offer, shows it as selected and enables Next', async () => {
    const lookupRecord = jest.fn().mockResolvedValue(OUTSIDE_PAGE);
    mount({ lookupRecord });
    await screen.findByRole('radio', { name: /Acme v\. Roadrunner/ });
    expect(next()).toBeDisabled();

    fireEvent.click(chooseButton());

    expect(lookupRecord).toHaveBeenCalledTimes(1);
    await waitFor(() =>
      expect(screen.getByTestId('selected-record')).toHaveTextContent('Beyond the first page · Project')
    );
    expect(next()).toBeEnabled();
  });

  it('the looked-up record survives the list arriving after the pick (the list is only a first page)', async () => {
    let release: (list: InvoiceCoreRecordList) => void = () => undefined;
    const loadRecords = jest.fn(() => new Promise<InvoiceCoreRecordList>(resolve => (release = resolve)));
    mount({ lookupRecord: jest.fn().mockResolvedValue(OUTSIDE_PAGE), loadRecords });

    fireEvent.click(chooseButton());
    await waitFor(() => expect(screen.getByTestId('selected-record')).toBeInTheDocument());
    await act(async () => release({ records: [MATTER] }));

    expect(await screen.findByRole('radio', { name: /Acme v\. Roadrunner/ })).toBeInTheDocument();
    expect(screen.getByTestId('selected-record')).toHaveTextContent('Beyond the first page');
    expect(next()).toBeEnabled();
  });

  it('a list pick after a lookup replaces the looked-up record', async () => {
    mount({ lookupRecord: jest.fn().mockResolvedValue(OUTSIDE_PAGE) });
    const radio = await screen.findByRole('radio', { name: /Acme v\. Roadrunner/ });
    fireEvent.click(chooseButton());
    await waitFor(() => expect(screen.getByTestId('selected-record')).toHaveTextContent('Beyond the first page'));
    fireEvent.click(radio);
    await waitFor(() => expect(screen.getByTestId('selected-record')).toHaveTextContent('Acme v. Roadrunner'));
  });

  it('a cancelled lookup (null) keeps the previous selection', async () => {
    mount({ lookupRecord: jest.fn().mockResolvedValue(null) });
    fireEvent.click(await screen.findByRole('radio', { name: /Acme v\. Roadrunner/ }));
    await waitFor(() => expect(next()).toBeEnabled());

    fireEvent.click(chooseButton());

    await waitFor(() => expect(chooseButton()).toBeEnabled());
    expect(screen.getByRole('radio', { name: /Acme v\. Roadrunner/ })).toBeChecked();
    expect(screen.getByTestId('selected-record')).toHaveTextContent('Acme v. Roadrunner');
    expect(next()).toBeEnabled();
  });

  it('a lookup error shows a message, keeps the list and the previous selection', async () => {
    mount({ lookupRecord: jest.fn().mockRejectedValue(new Error('no xrm')) });
    fireEvent.click(await screen.findByRole('radio', { name: /Acme v\. Roadrunner/ }));

    fireEvent.click(chooseButton());

    expect(await screen.findByTestId('record-lookup-error')).toHaveTextContent(/could not be opened/i);
    expect(screen.getByRole('radio', { name: /Acme v\. Roadrunner/ })).toBeChecked();
    expect(next()).toBeEnabled();
  });

  // The adapter reads a record's firms only on submit, so a record looked up before the first submit carries firm key '': the
  // `firm_required` reload (firm A chosen, the list read again under A) must not drop it.
  it('must fire: a looked-up record survives the firm step that firm_required brings in, and the submit carries it with the firm', async () => {
    const ALPHA = { firmId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', name: 'Alpha LLP' };
    const BETA = { firmId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', name: 'Beta LLP' };
    const loadFirms = jest.fn().mockResolvedValueOnce(null).mockResolvedValue([ALPHA, BETA]);
    const submit = jest
      .fn()
      .mockRejectedValueOnce(
        new InvoiceSubmissionError({ statusCode: 400, errorCode: 'ebilling.submission.firm_required' })
      )
      .mockResolvedValue({ submissionId: 's-1', trackingNumber: 'TRK-1' });
    const utils = mount({ lookupRecord: jest.fn().mockResolvedValue(OUTSIDE_PAGE), loadFirms, submit });

    fireEvent.click(await screen.findByTestId('choose-record-button'));
    await waitFor(() => expect(next()).toBeEnabled());
    fireEvent.click(next());
    const input = utils.container.querySelector('input[type="file"]') as HTMLInputElement;
    fireEvent.change(input, {
      target: { files: [new File(['%PDF-1.4'], 'invoice.pdf', { type: 'application/pdf' })] },
    });
    await waitFor(() => expect(next()).toBeEnabled());
    fireEvent.click(next());
    fireEvent.change(await screen.findByLabelText(/Firm invoice number/i), { target: { value: 'INV-9' } });
    fireEvent.click(screen.getByRole('button', { name: /^submit invoice$/i }));

    // The firm step is first now; pick a firm and go on to the Record step: the looked-up record is still the selection.
    expect(await screen.findByText('Choose the firm this invoice is from')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('radio', { name: 'Beta LLP' }));
    fireEvent.click(next());
    await screen.findByRole('radio', { name: /Acme v\. Roadrunner/ });
    expect(screen.getByTestId('selected-record')).toHaveTextContent('Beyond the first page · Project');
    expect(next()).toBeEnabled();

    fireEvent.click(next());
    await waitFor(() => expect(next()).toBeEnabled());
    fireEvent.click(next());
    fireEvent.click(await screen.findByRole('button', { name: /^submit invoice$/i }));

    await waitFor(() => expect(submit).toHaveBeenCalledTimes(2));
    expect(submit.mock.calls[1][0].record).toEqual(OUTSIDE_PAGE);
    expect(submit.mock.calls[1][0].firmId).toBe(BETA.firmId);
  });

  it('must not fire: a record looked up and then replaced by a list pick is not resurrected by the firm reload', async () => {
    const ALPHA = { firmId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', name: 'Alpha LLP' };
    const BETA = { firmId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', name: 'Beta LLP' };
    const loadFirms = jest.fn().mockResolvedValueOnce(null).mockResolvedValue([ALPHA, BETA]);
    const submit = jest
      .fn()
      .mockRejectedValue(
        new InvoiceSubmissionError({ statusCode: 400, errorCode: 'ebilling.submission.firm_required' })
      );
    const utils = mount({ lookupRecord: jest.fn().mockResolvedValue(OUTSIDE_PAGE), loadFirms, submit });

    fireEvent.click(await screen.findByTestId('choose-record-button'));
    await waitFor(() => expect(screen.getByTestId('selected-record')).toHaveTextContent('Beyond the first page'));
    fireEvent.click(await screen.findByRole('radio', { name: /Acme v\. Roadrunner/ }));
    fireEvent.click(next());
    fireEvent.change(utils.container.querySelector('input[type="file"]') as HTMLInputElement, {
      target: { files: [new File(['%PDF-1.4'], 'invoice.pdf', { type: 'application/pdf' })] },
    });
    await waitFor(() => expect(next()).toBeEnabled());
    fireEvent.click(next());
    fireEvent.change(await screen.findByLabelText(/Firm invoice number/i), { target: { value: 'INV-9' } });
    fireEvent.click(screen.getByRole('button', { name: /^submit invoice$/i }));

    await screen.findByText('Choose the firm this invoice is from');
    fireEvent.click(screen.getByRole('radio', { name: 'Beta LLP' }));
    fireEvent.click(next());
    await screen.findByRole('radio', { name: /Acme v\. Roadrunner/ });
    expect(screen.getByTestId('selected-record')).toHaveTextContent('Acme v. Roadrunner');
  });

  it('must not fire: with no lookupRecord there is no button and no selected line (the external SPA step is unchanged)', async () => {
    mount({});
    fireEvent.click(await screen.findByRole('radio', { name: /Acme v\. Roadrunner/ }));
    expect(screen.queryByTestId('choose-record-button')).toBeNull();
    expect(screen.queryByRole('button', { name: /choose a record/i })).toBeNull();
    expect(screen.queryByTestId('selected-record')).toBeNull();
  });

  it('must not fire: a locked record has no Record step, so no lookup button', async () => {
    mount({ lookupRecord: jest.fn(), lockedRecord: MATTER });
    expect(await screen.findByText(/Upload the invoice PDF/)).toBeInTheDocument();
    expect(screen.queryByTestId('choose-record-button')).toBeNull();
    expect(screen.queryByText(/Choose the record this invoice is for/)).toBeNull();
  });
});
