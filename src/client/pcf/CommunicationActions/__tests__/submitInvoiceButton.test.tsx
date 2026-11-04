/**
 * The PCF's "Submit Invoice" toolbar button (ebilling-r1 task 118, review F3).
 *
 * The shared flow has its own suite in `@spaarke/communication-components`; what only this PCF can break is the wiring:
 * the button opens the shared dialog (not an Xrm form), is off without a record id, and a submission the server accepts
 * refreshes the host form. The shared dialog and transport are stand-ins (they are covered where they live and their
 * package is not jest-consumable from this PCF); the app, its Fluent toolbar and the host seams are real.
 */
import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

jest.mock('@spaarke/auth', () => ({ __esModule: true, authenticatedFetch: jest.fn() }));

jest.mock('@spaarke/ui-components', () => ({
  __esModule: true,
  SendEmailPage: () => null,
  searchUsersAndContacts: jest.fn().mockResolvedValue([]),
  createXrmDataService: () => ({}),
  resolveInitialInvoiceRecord: () => undefined,
  cleanGuid: (id: string) => id.replace(/[{}]/g, '').toLowerCase(),
  // Same seam as the real walker: the frame's `Xrm` (jest.setup.ts installs it on `global`).
  getXrm: () => (global as unknown as { Xrm?: unknown }).Xrm,
}));

jest.mock('../CommunicationActions/authInit', () => ({
  __esModule: true,
  initializeAuth: jest.fn().mockResolvedValue(undefined),
  resolveDataverseUrl: () => 'https://test.crm.dynamics.com',
}));

jest.mock('../../shared/utils/environmentVariables', () => ({
  __esModule: true,
  getMsalClientId: jest.fn().mockResolvedValue('client'),
  getBffApiAppId: jest.fn().mockResolvedValue('bff'),
  getApiBaseUrl: jest.fn().mockResolvedValue('https://bff.example'),
  getTenantId: jest.fn().mockResolvedValue('tenant'),
}));

jest.mock(
  '@spaarke/communication-components/logic/invoice',
  () => ({ __esModule: true, createEmailInvoiceSubmission: jest.fn(() => ({ stand: 'in' })) }),
  { virtual: true }
);

// A stand-in for the shared dialog: renders the flow's test id only while open and lets a test accept a submission.
jest.mock(
  '@spaarke/communication-components/components/SubmitInvoice',
  () => {
    const R = jest.requireActual('react') as typeof React;
    return {
      __esModule: true,
      SubmitInvoiceDialog: (props: {
        open: boolean;
        communicationId: string;
        onClose: () => void;
        onAccepted?: () => void;
      }) =>
        props.open
          ? R.createElement(
              'div',
              { 'data-testid': 'submit-invoice-flow', 'data-communication-id': props.communicationId },
              R.createElement('button', { onClick: props.onAccepted }, 'simulate accepted'),
              R.createElement('button', { onClick: props.onClose }, 'close flow')
            )
          : null,
    };
  },
  { virtual: true }
);

import { CommunicationActionsApp } from '../CommunicationActions/CommunicationActionsApp';

const COMM_ID = '22222222-2222-2222-2222-222222222222';

type XrmShim = {
  Navigation: { navigateTo: jest.Mock };
  Page: { data: { refresh?: jest.Mock; entity: { getId: () => string | undefined } } };
};
const xrm = () => (global as unknown as { Xrm: XrmShim }).Xrm;

function makeContext() {
  return {
    parameters: {
      clientAppId: { raw: 'client' },
      bffAppId: { raw: 'bff' },
      apiBaseUrl: { raw: 'https://bff.example' },
      showVersionFooter: { raw: false },
      communicationType: { raw: 100000000 },
    },
    webAPI: {
      retrieveRecord: jest.fn().mockResolvedValue({}),
      retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
    },
  } as unknown as React.ComponentProps<typeof CommunicationActionsApp>['context'];
}

function renderApp() {
  return render(
    <FluentProvider theme={webLightTheme}>
      <CommunicationActionsApp context={makeContext()} readOnly={false} version="1.3.21" />
    </FluentProvider>
  );
}

describe('CommunicationActions - Submit Invoice button (task 118)', () => {
  const originalGetId = () => COMM_ID;

  beforeEach(() => {
    xrm().Navigation.navigateTo.mockClear();
    xrm().Page.data.entity.getId = originalGetId;
    xrm().Page.data.refresh = jest.fn().mockResolvedValue(undefined);
    jest.spyOn(console, 'warn').mockImplementation(() => {});
  });
  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('opens the shared Submit Invoice flow for this communication and does not navigate to an Xrm form', async () => {
    renderApp();
    const button = await screen.findByRole('button', { name: 'Submit Invoice' });
    expect(button).toBeEnabled();
    expect(screen.queryByTestId('submit-invoice-flow')).not.toBeInTheDocument();

    fireEvent.click(button);

    const flow = await screen.findByTestId('submit-invoice-flow');
    expect(flow).toHaveAttribute('data-communication-id', COMM_ID);
    // Not the old "Link Invoice" create form: no sprk_invoice form is launched from this control.
    expect(xrm().Navigation.navigateTo).not.toHaveBeenCalled();
    expect(screen.queryByRole('button', { name: 'Link Invoice' })).not.toBeInTheDocument();
  });

  it('closing the flow hides it again', async () => {
    renderApp();
    fireEvent.click(await screen.findByRole('button', { name: 'Submit Invoice' }));
    fireEvent.click(await screen.findByText('close flow'));
    await waitFor(() => expect(screen.queryByTestId('submit-invoice-flow')).not.toBeInTheDocument());
  });

  it('a submission the server accepts refreshes the host form (once)', async () => {
    renderApp();
    fireEvent.click(await screen.findByRole('button', { name: 'Submit Invoice' }));
    fireEvent.click(await screen.findByText('simulate accepted'));
    await waitFor(() => expect(xrm().Page.data.refresh).toHaveBeenCalledTimes(1));
  });

  it('negative: without a record id the button is disabled and no flow can open', async () => {
    xrm().Page.data.entity.getId = () => undefined;
    renderApp();
    const button = await screen.findByRole('button', { name: 'Submit Invoice' });
    expect(button).toBeDisabled();
    fireEvent.click(button);
    expect(screen.queryByTestId('submit-invoice-flow')).not.toBeInTheDocument();
    expect(xrm().Navigation.navigateTo).not.toHaveBeenCalled();
  });
});
