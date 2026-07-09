/**
 * CreateReportCardStep — Enter Info manifest validation tests (task 040)
 *
 * Scope (ADR-038 — behavioral, DOM-level; no internals mocked beyond the
 * injected IDataService boundary):
 *   1. Renders the manifest fields: Name (required), Due Date, Narrative,
 *      and all 8 assigned-resource lookup labels.
 *   2. Form validity gates on Name ONLY — every other field (narrative, due
 *      date, all 8 resource lookups) is optional per the owner-approved
 *      manifest (notes/field-manifests/reportcard.md).
 *
 * @see ../CreateReportCardStep.tsx
 * @see CreateInvoiceWizard/__tests__/CreateInvoiceStep.test.tsx (sibling reference shape)
 */
import * as React from 'react';
import { screen, fireEvent } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { CreateReportCardStep } from '../CreateReportCardStep';
import type { IDataService } from '../../../types/serviceInterfaces';

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

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('CreateReportCardStep', () => {
  it('renders the manifest fields: Name, Due Date, Narrative', () => {
    renderWithProviders(
      <CreateReportCardStep dataService={makeDataService()} onValidChange={jest.fn()} onFormValues={jest.fn()} />
    );

    expect(screen.getByText('Name')).toBeInTheDocument();
    expect(screen.getByText('Due Date')).toBeInTheDocument();
    expect(screen.getByText('Narrative')).toBeInTheDocument();
  });

  it('renders all 8 assigned-resource lookup labels', () => {
    renderWithProviders(
      <CreateReportCardStep dataService={makeDataService()} onValidChange={jest.fn()} onFormValues={jest.fn()} />
    );

    expect(screen.getByText('Assigned Attorney 1')).toBeInTheDocument();
    expect(screen.getByText('Assigned Attorney 2')).toBeInTheDocument();
    expect(screen.getByText('Assigned Paralegal 1')).toBeInTheDocument();
    expect(screen.getByText('Assigned Paralegal 2')).toBeInTheDocument();
    expect(screen.getByText('Assigned Law Firm 1')).toBeInTheDocument();
    expect(screen.getByText('Assigned Law Firm 2')).toBeInTheDocument();
    expect(screen.getByText('Assigned External')).toBeInTheDocument();
    expect(screen.getByText('Assigned Internal')).toBeInTheDocument();
  });

  it('is invalid until Name is filled, then reports validity + values via callbacks', () => {
    const onValidChange = jest.fn();
    const onFormValues = jest.fn();

    renderWithProviders(
      <CreateReportCardStep dataService={makeDataService()} onValidChange={onValidChange} onFormValues={onFormValues} />
    );

    // Initial mount: Name is empty -> invalid.
    expect(onValidChange).toHaveBeenCalledWith(false);

    const nameInput = screen.getByPlaceholderText('Enter report card name');
    fireEvent.change(nameInput, { target: { value: 'Q3 Compliance Review' } });

    expect(onValidChange).toHaveBeenLastCalledWith(true);
    const lastCallValues = onFormValues.mock.calls[onFormValues.mock.calls.length - 1][0];
    expect(lastCallValues.name).toBe('Q3 Compliance Review');
  });

  it('remains valid with Name filled and every other field left empty (narrative/due date/8 resource lookups all optional)', () => {
    const onValidChange = jest.fn();

    renderWithProviders(
      <CreateReportCardStep dataService={makeDataService()} onValidChange={onValidChange} onFormValues={jest.fn()} />
    );

    fireEvent.change(screen.getByPlaceholderText('Enter report card name'), { target: { value: 'X' } });

    // Only ever called with true after Name is filled — narrative/dueDate/resource
    // lookups being empty must NOT flip validity back to false.
    // (sanity: false was the initial mount call, true is the only call after Name is filled)
    expect(onValidChange.mock.calls.map(c => c[0])).toEqual([false, true]);
  });

  it('collects Narrative and Due Date into form values', () => {
    const onFormValues = jest.fn();

    renderWithProviders(
      <CreateReportCardStep dataService={makeDataService()} onValidChange={jest.fn()} onFormValues={onFormValues} />
    );

    fireEvent.change(screen.getByPlaceholderText('Describe the report card...'), {
      target: { value: 'Quarterly review notes' },
    });
    const dateInput = document.querySelector('input[type="date"]') as HTMLInputElement;
    fireEvent.change(dateInput, { target: { value: '2026-09-30' } });

    const lastCallValues = onFormValues.mock.calls[onFormValues.mock.calls.length - 1][0];
    expect(lastCallValues.narrative).toBe('Quarterly review notes');
    expect(lastCallValues.dueDate).toBe('2026-09-30');
  });
});
