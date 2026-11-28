/**
 * InAppWizardHost.invoice.test.tsx — the Create Invoice wizard in the in-app host (ebilling-r1 def071, GitHub #3335).
 *
 * `launchCreateInvoiceWizard` (the Console's Quick Start card) opens the REAL CreateInvoiceWizard in one SprkModal, named
 * `wizard` size, with the host's navigation service so the Record step can use the Dataverse lookup. With no host mounted the
 * launcher keeps the `navigateTo` dialog every other launcher uses.
 *
 * Classification (ADR-038 §7): MAINTAIN.
 */
import * as React from 'react';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { InAppWizardHost } from '../InAppWizardHost';
import {
  canOpenInApp,
  launchCreateInvoiceWizard,
  navigateToWebResourceSurfaceAsync,
} from '../../WorkspaceShell/wizardLaunchers';

const openLookup = jest.fn();
jest.mock('../../../utils/adapters/xrmDataServiceAdapter', () => ({
  createXrmDataService: () => require('../../../__mocks__/mockDataService').createMockDataService(),
}));
jest.mock('../../../utils/adapters/xrmNavigationServiceAdapter', () => ({
  createXrmNavigationService: () => ({
    openRecord: jest.fn(),
    openDialog: jest.fn(),
    closeDialog: jest.fn(),
    openLookup: (...args: unknown[]) => openLookup(...args),
  }),
}));

let navigateTo: jest.Mock;
beforeEach(() => {
  openLookup.mockReset();
  navigateTo = jest.fn().mockResolvedValue(undefined);
  (window as unknown as { Xrm?: unknown }).Xrm = {
    WebApi: { retrieveRecord: jest.fn() },
    Utility: { lookupObjects: jest.fn(), getGlobalContext: () => ({ userSettings: { userId: 'x' } }) },
    Navigation: { navigateTo },
  };
});
afterEach(() => {
  delete (window as unknown as { Xrm?: unknown }).Xrm;
});

const authenticatedFetch = jest.fn(async () => ({ ok: true, status: 200, json: async () => [] }));
const host = (
  <InAppWizardHost
    authenticatedFetch={authenticatedFetch as unknown as typeof fetch}
    bffBaseUrl="https://bff.example"
    uiScale={1}
  />
);

describe('InAppWizardHost — sprk_createinvoicewizard', () => {
  it('the host opens the Create Invoice wizard in-app (one SprkModal), not navigateTo', async () => {
    renderWithProviders(host);
    expect(canOpenInApp('sprk_createinvoicewizard')).toBe(true);

    act(() => {
      launchCreateInvoiceWizard({ bffBaseUrl: 'https://bff.example' });
    });

    const dialog = await screen.findByRole('dialog');
    expect(document.querySelectorAll('.fui-DialogSurface')).toHaveLength(1);
    expect(document.getElementById(dialog.getAttribute('aria-labelledby')!)).toHaveTextContent('Create New Invoice');
    expect(dialog.style.width).toBe('62vw');
    expect(await screen.findByText(/Choose the record this invoice is for/)).toBeInTheDocument();
    expect(navigateTo).not.toHaveBeenCalled();
  });

  it('with no record context the Record step offers the Dataverse lookup through the host navigation service', async () => {
    openLookup.mockResolvedValue([
      { id: '11111111-1111-4111-8111-111111111111', name: 'Acme v. Roadrunner', entityType: 'sprk_matter' },
    ]);
    renderWithProviders(host);
    act(() => {
      launchCreateInvoiceWizard({ bffBaseUrl: 'https://bff.example' });
    });

    fireEvent.click(await screen.findByRole('button', { name: /choose a record/i }));

    await waitFor(() => expect(screen.getByTestId('selected-record')).toHaveTextContent('Acme v. Roadrunner · Matter'));
    expect(openLookup).toHaveBeenCalledWith(
      expect.objectContaining({ entityTypes: ['sprk_matter', 'sprk_project', 'sprk_workassignment'] })
    );
    expect(screen.getByRole('button', { name: /^next$/i })).toBeEnabled();
  });

  it('must not fire: a frame with no Xrm.Utility.lookupObjects keeps the Record step to the list', async () => {
    (window as unknown as { Xrm: { Utility: object } }).Xrm.Utility = {};
    renderWithProviders(host);
    act(() => {
      launchCreateInvoiceWizard({ bffBaseUrl: 'https://bff.example' });
    });
    expect(await screen.findByText(/Choose the record this invoice is for/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /choose a record/i })).toBeNull();
  });

  it('Cancel closes the wizard and settles the launch', async () => {
    renderWithProviders(host);
    let settled = false;
    act(() => {
      void navigateToWebResourceSurfaceAsync({ webresourceName: 'sprk_createinvoicewizard', data: '' }).then(() => {
        settled = true;
      });
    });
    await screen.findByRole('dialog');
    fireEvent.click(screen.getByRole('button', { name: /^cancel$/i }));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    await waitFor(() => expect(settled).toBe(true));
  });

  it('must not fire: with no host mounted the launcher keeps the navigateTo dialog', () => {
    expect(canOpenInApp('sprk_createinvoicewizard')).toBe(false);
    launchCreateInvoiceWizard({ bffBaseUrl: 'https://bff.example' });
    expect(navigateTo).toHaveBeenCalledTimes(1);
    expect(navigateTo.mock.calls[0][0]).toEqual({
      pageType: 'webresource',
      webresourceName: 'sprk_createinvoicewizard',
      data: 'bffBaseUrl=https%3A%2F%2Fbff.example',
    });
  });
});
