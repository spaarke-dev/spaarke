/**
 * InAppWizardHost.render.test.tsx — a REAL Create wizard mounted by the in-app host
 * (spaarke-ontology-platform-r1 task 112; D-26; ADR-050 as amended): it renders in ONE SprkModal
 * envelope at the named `wizard` size scaled by the host's uiScale, Escape does not close it
 * (explicit dismiss), Cancel closes it and settles the launcher, and it renders under a dark theme.
 *
 * Two wizards cover both forwarding paths: To Do (TodoWizardDialog → CreateRecordWizard →
 * WizardShell) and Work Assignment (WorkAssignmentWizardDialog → WizardShell). Only the Xrm
 * adapters are faked (no Dataverse in jsdom).
 *
 * Classification (ADR-038 §7): MAINTAIN.
 */
import * as React from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webDarkTheme } from '@fluentui/react-components';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { InAppWizardHost } from '../InAppWizardHost';
import { navigateToWebResourceSurfaceAsync } from '../../WorkspaceShell/wizardLaunchers';

jest.mock('../../../utils/adapters/xrmDataServiceAdapter', () => ({
  createXrmDataService: () => require('../../../__mocks__/mockDataService').createMockDataService(),
}));
jest.mock('../../../utils/adapters/xrmNavigationServiceAdapter', () => ({
  createXrmNavigationService: () => ({
    openRecord: jest.fn(),
    openDialog: jest.fn(),
    closeDialog: jest.fn(),
    openLookup: jest.fn().mockResolvedValue([]),
  }),
}));

let navigateTo: jest.Mock;
beforeEach(() => {
  navigateTo = jest.fn().mockResolvedValue(undefined);
  // The user → business-unit → container chain the host resolves on open.
  const retrieveRecord = jest.fn(async (entity: string) =>
    entity === 'systemuser' ? { _businessunitid_value: 'bu-1' } : { sprk_containerid: 'container-1' }
  );
  (window as unknown as { Xrm?: unknown }).Xrm = {
    WebApi: { retrieveRecord },
    Utility: { getGlobalContext: () => ({ userSettings: { userId: '{11111111-1111-1111-1111-111111111111}' } }) },
    Navigation: { navigateTo },
  };
});
afterEach(() => {
  delete (window as unknown as { Xrm?: unknown }).Xrm;
});

const host = <InAppWizardHost authenticatedFetch={jest.fn()} bffBaseUrl="https://bff.example" uiScale={1.5} />;

function launch(name: 'sprk_createtodowizard' | 'sprk_createworkassignmentwizard') {
  let settled = false;
  act(() => {
    void navigateToWebResourceSurfaceAsync({ webresourceName: name, data: '' }).then(() => {
      settled = true;
    });
  });
  return () => settled;
}

describe.each([
  ['sprk_createtodowizard', 'Create New To Do'],
  ['sprk_createworkassignmentwizard', 'Create Work Assignment'],
] as const)('InAppWizardHost — real %s', (name, title) => {
  it('opens in one SprkModal at the named wizard size scaled by uiScale; Escape keeps it open; Cancel closes it', async () => {
    renderWithProviders(host);
    const isSettled = launch(name);

    const dialog = await screen.findByRole('dialog');
    expect(document.querySelectorAll('.fui-DialogSurface')).toHaveLength(1);
    expect(document.getElementById(dialog.getAttribute('aria-labelledby')!)).toHaveTextContent(title);
    // `wizard` = 62vw × min(74vh, 760px) at scale 1; the host's uiScale 1.5 reaches SprkModal.
    expect(dialog.style.width).toBe('62vw');
    expect(dialog.style.height).toBe('min(74vh, 1140px)');

    fireEvent.keyDown(dialog, { key: 'Escape', code: 'Escape' });
    expect(screen.getByRole('dialog')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /^cancel$/i }));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    await waitFor(() => expect(isSettled()).toBe(true));
    expect(navigateTo).not.toHaveBeenCalled();
  });

  it('renders under a dark theme (the modal inherits the host FluentProvider)', async () => {
    render(<FluentProvider theme={webDarkTheme}>{host}</FluentProvider>);
    launch(name);
    expect(await screen.findByRole('dialog')).toBeInTheDocument();
  });
});

describe('InAppWizardHost — container resolution failure', () => {
  it('a failed SPE container lookup on open is caught (no unhandled rejection) and the wizard stays usable', async () => {
    // No WebApi on the host Xrm → the host resolver throws; CreateRecordWizard used to leave that
    // rejection unhandled (jest fails a test on one, as this case did before the fix).
    (window as unknown as { Xrm?: unknown }).Xrm = { Navigation: { navigateTo } };
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    renderWithProviders(host);
    launch('sprk_createtodowizard');
    expect(await screen.findByRole('dialog')).toBeInTheDocument();
    await waitFor(() =>
      expect(warn).toHaveBeenCalledWith('[CreateRecordWizard] SPE container resolution failed:', expect.any(Error))
    );
    warn.mockRestore();
  });
});
