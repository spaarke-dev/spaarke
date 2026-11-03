/**
 * InvoiceSubmissionWizard — `initialRecord` (ebilling-r1 task 052): the internal wizard, launched from a record, starts with that
 * record already chosen. The host's `loadRecords` stays the authority on what is offered.
 */
import * as React from 'react';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { InvoiceSubmissionWizard } from '../InvoiceSubmissionWizard';
import type { InvoiceCoreRecordOption } from '../types';

const MATTER: InvoiceCoreRecordOption = {
  kind: 'matter',
  recordId: '11111111-1111-4111-8111-111111111111',
  name: 'Acme v. Roadrunner',
  number: 'M-1001',
};
const PROJECT: InvoiceCoreRecordOption = {
  kind: 'project',
  recordId: '22222222-2222-4222-8222-222222222222',
  name: 'Patent portfolio',
  number: 'P-77',
};

function mount(records: InvoiceCoreRecordOption[], initialRecord?: InvoiceCoreRecordOption) {
  return renderWithProviders(
    <InvoiceSubmissionWizard
      embedded
      initialRecord={initialRecord}
      loadRecords={jest.fn().mockResolvedValue({ records })}
      submit={jest.fn()}
      getStatus={jest.fn()}
      onClose={jest.fn()}
    />
  );
}

const next = () => screen.getByRole('button', { name: /^next$/i });

describe('InvoiceSubmissionWizard — initialRecord', () => {
  it('starts with the record chosen, so the user can go straight on', async () => {
    mount([MATTER, PROJECT], MATTER);
    const radio = await screen.findByRole('radio', { name: /Acme v\. Roadrunner/ });
    await waitFor(() => expect(radio).toBeChecked());
    expect(next()).toBeEnabled();
  });

  it('the user can still choose another record', async () => {
    mount([MATTER, PROJECT], MATTER);
    fireEvent.click(await screen.findByRole('radio', { name: /Patent portfolio/ }));
    expect(screen.getByRole('radio', { name: /Patent portfolio/ })).toBeChecked();
    expect(screen.getByRole('radio', { name: /Acme v\. Roadrunner/ })).not.toBeChecked();
  });

  it('is dropped when the host does not offer it (loadRecords is the authority)', async () => {
    mount([PROJECT], MATTER);
    await screen.findByRole('radio', { name: /Patent portfolio/ });
    await waitFor(() => expect(next()).toBeDisabled());
  });

  it('control: with no initialRecord nothing is chosen', async () => {
    mount([MATTER, PROJECT]);
    await screen.findByRole('radio', { name: /Acme v\. Roadrunner/ });
    expect(next()).toBeDisabled();
  });
});
