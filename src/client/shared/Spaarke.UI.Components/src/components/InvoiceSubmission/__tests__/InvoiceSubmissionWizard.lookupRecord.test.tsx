/**
 * InvoiceSubmissionWizard — `lookupRecord` (ebilling-r1 def071, GitHub #3335): a host that can open a Dataverse lookup gives the Record
 * step a "Choose a record" button, so a record outside the first page of the list can be picked. Without the prop the step is exactly
 * what the external SPA (no Xrm) shows.
 */
import * as React from 'react';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { InvoiceSubmissionWizard } from '../InvoiceSubmissionWizard';
import type { InvoiceCoreRecordList, InvoiceCoreRecordOption } from '../types';

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
}) {
  return renderWithProviders(
    <InvoiceSubmissionWizard
      embedded
      lookupRecord={options.lookupRecord}
      lockedRecord={options.lockedRecord}
      loadRecords={options.loadRecords ?? jest.fn().mockResolvedValue({ records: [MATTER] })}
      submit={jest.fn()}
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
