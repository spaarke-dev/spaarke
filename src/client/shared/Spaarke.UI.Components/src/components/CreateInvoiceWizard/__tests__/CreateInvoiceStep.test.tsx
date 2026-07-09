/**
 * CreateInvoiceStep — Enter Info manifest + inert AI-prefill seam tests (task 030)
 *
 * Scope (ADR-038 — behavioral, DOM-level; no internals mocked beyond the
 * injected IDataService/fetch boundary):
 *   1. Renders exactly the manifest fields (spec FR-16): Invoice Number, Name
 *      (required), Description, Vendor Organization, Invoice Date.
 *   2. Invoice Date defaults to today's ISO date on mount.
 *   3. Form validity gates on Name only (sprk_name is the sole NOT NULL manifest field).
 *   4. AI prefill seam is wired but INERT: with `uploadedFiles` non-empty, no
 *      spinner renders and no network call is made — acceptance criterion
 *      "Prefill seam present but inert" (spec FR-11).
 *
 * @see ../CreateInvoiceStep.tsx
 * @see CreateEventWizard/__tests__/CreateEventWizard.associateToStep.test.ts (sibling reference shape)
 */
import * as React from 'react';
import { screen, fireEvent, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { CreateInvoiceStep } from '../CreateInvoiceStep';
import { todayIsoDate } from '../formTypes';
import type { IDataService } from '../../../types/serviceInterfaces';
import type { IUploadedFile } from '../../FileUpload/fileUploadTypes';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

function makeDataService(): IDataService {
  return {
    createRecord: jest.fn(async () => 'new-id'),
    retrieveRecord: jest.fn(async () => ({})),
    retrieveMultipleRecords: jest.fn(async () => ({ entities: [] })),
    updateRecord: jest.fn(async () => undefined),
    deleteRecord: jest.fn(async () => undefined),
  };
}

function makeUploadedFile(): IUploadedFile {
  return {
    id: 'local-1',
    name: 'invoice-source.pdf',
    sizeBytes: 2048,
    file: new File(['x'], 'invoice-source.pdf', { type: 'application/pdf' }),
  } as unknown as IUploadedFile;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('CreateInvoiceStep', () => {
  it('renders exactly the manifest fields (Invoice Number, Name, Description, Vendor Organization, Invoice Date)', () => {
    renderWithProviders(
      <CreateInvoiceStep dataService={makeDataService()} onValidChange={jest.fn()} onFormValues={jest.fn()} />
    );

    expect(screen.getByText('Invoice Number')).toBeInTheDocument();
    expect(screen.getByText('Name')).toBeInTheDocument();
    expect(screen.getByText('Description')).toBeInTheDocument();
    expect(screen.getByText('Vendor Organization')).toBeInTheDocument();
    expect(screen.getByText('Invoice Date')).toBeInTheDocument();
  });

  it('defaults Invoice Date to today (ISO) on mount', () => {
    renderWithProviders(
      <CreateInvoiceStep dataService={makeDataService()} onValidChange={jest.fn()} onFormValues={jest.fn()} />
    );

    const dateInput = document.querySelector('input[type="date"]') as HTMLInputElement;
    expect(dateInput).toBeTruthy();
    expect(dateInput.value).toBe(todayIsoDate());
  });

  it('is invalid until Name is filled, then reports validity + values via callbacks', () => {
    const onValidChange = jest.fn();
    const onFormValues = jest.fn();

    renderWithProviders(
      <CreateInvoiceStep dataService={makeDataService()} onValidChange={onValidChange} onFormValues={onFormValues} />
    );

    // Initial mount: Name is empty -> invalid.
    expect(onValidChange).toHaveBeenCalledWith(false);

    const nameInput = screen.getByPlaceholderText('Enter invoice name');
    fireEvent.change(nameInput, { target: { value: 'April Legal Fees' } });

    expect(onValidChange).toHaveBeenLastCalledWith(true);
    const lastCallValues = onFormValues.mock.calls[onFormValues.mock.calls.length - 1][0];
    expect(lastCallValues.name).toBe('April Legal Fees');
  });

  it('collects Invoice Number and Description into form values', () => {
    const onFormValues = jest.fn();

    renderWithProviders(
      <CreateInvoiceStep dataService={makeDataService()} onValidChange={jest.fn()} onFormValues={onFormValues} />
    );

    fireEvent.change(screen.getByPlaceholderText('Enter invoice number'), { target: { value: 'INV-2001' } });
    fireEvent.change(screen.getByPlaceholderText('Describe the invoice...'), {
      target: { value: 'Quarterly outside counsel fees' },
    });

    const lastCallValues = onFormValues.mock.calls[onFormValues.mock.calls.length - 1][0];
    expect(lastCallValues.invoiceNumber).toBe('INV-2001');
    expect(lastCallValues.description).toBe('Quarterly outside counsel fees');
  });

  it('AI prefill seam is inert: no spinner renders and no network call is made, even with uploaded files present', async () => {
    const authenticatedFetch = jest.fn(async () => ({ ok: true, json: async () => ({}) })) as unknown as typeof fetch;

    renderWithProviders(
      <CreateInvoiceStep
        dataService={makeDataService()}
        onValidChange={jest.fn()}
        onFormValues={jest.fn()}
        uploadedFiles={[makeUploadedFile()]}
        authenticatedFetch={authenticatedFetch}
        bffBaseUrl="https://bff.example"
      />
    );

    // No spinner / loading state.
    expect(screen.queryByText('Analyzing uploaded files...')).not.toBeInTheDocument();

    // Give any stray microtask a chance to run, then assert no fetch occurred.
    await waitFor(() => {
      expect(authenticatedFetch).not.toHaveBeenCalled();
    });

    // The form is still fully interactive (not blocked by a loading state).
    expect(screen.getByPlaceholderText('Enter invoice name')).toBeInTheDocument();
  });
});
