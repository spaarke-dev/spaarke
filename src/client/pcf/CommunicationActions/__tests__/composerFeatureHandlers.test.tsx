/**
 * The composer's Send Email dialog feature handlers in the CommunicationActions PCF (ontology task 188, D-219 Q3).
 *
 * `EmailComposer` hides the template picker, the AI sparkle and local-file upload when the host passes no
 * `onListEmailTemplates` + `onRenderEmailTemplate`, `onDraftWithAi` or `onUploadLocalAttachment` - silently. The
 * contract here: the PCF hands the composer all four, taken from ONE `createXrmEmailComposeHandlers` instance built
 * with the control's auth-aware fetch and the BFF base URL. The factory and the composer are stand-ins (each has its
 * own suite in `@spaarke/ui-components`); the app and its wiring are real.
 */
import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

const mockAuthFetch = jest.fn();
jest.mock('@spaarke/auth', () => ({ __esModule: true, authenticatedFetch: (...a: unknown[]) => mockAuthFetch(...a) }));

const HANDLERS = {
  onListEmailTemplates: jest.fn(),
  onRenderEmailTemplate: jest.fn(),
  onDraftWithAi: jest.fn(),
  onUploadLocalAttachment: jest.fn(),
};
const mockCreateHandlers = jest.fn((_options?: unknown) => HANDLERS);
let mockComposerProps: Record<string, unknown> | undefined;

jest.mock('@spaarke/ui-components', () => ({
  __esModule: true,
  SendEmailPage: (props: Record<string, unknown>) => {
    mockComposerProps = props;
    return null;
  },
  searchUsersAndContacts: jest.fn().mockResolvedValue([]),
  createXrmDataService: () => ({}),
  createXrmEmailComposeHandlers: (options?: unknown) => mockCreateHandlers(options),
  resolveInitialInvoiceRecord: () => undefined,
  cleanGuid: (id: string) => id.replace(/[{}]/g, '').toLowerCase(),
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
  () => ({ __esModule: true, createEmailInvoiceSubmission: jest.fn(() => ({})) }),
  { virtual: true }
);
jest.mock(
  '@spaarke/communication-components/components/SubmitInvoice',
  () => ({ __esModule: true, SubmitInvoiceDialog: () => null }),
  { virtual: true }
);

import { CommunicationActionsApp } from '../CommunicationActions/CommunicationActionsApp';

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

describe('CommunicationActions - composer feature handlers (task 188)', () => {
  beforeEach(() => {
    mockComposerProps = undefined;
    mockCreateHandlers.mockClear();
    jest.spyOn(console, 'warn').mockImplementation(() => {});
  });
  afterEach(() => {
    jest.restoreAllMocks();
  });

  it.each(['New', 'Reply', 'Forward'])(
    '%s: the composer receives all four feature handlers from the one instance',
    async name => {
      render(
        <FluentProvider theme={webLightTheme}>
          <CommunicationActionsApp context={makeContext()} readOnly={false} version="1.3.21" />
        </FluentProvider>
      );
      fireEvent.click(await screen.findByRole('button', { name }));
      await waitFor(() => expect(mockComposerProps).toBeDefined());

      expect(mockComposerProps!.onListEmailTemplates).toBe(HANDLERS.onListEmailTemplates);
      expect(mockComposerProps!.onRenderEmailTemplate).toBe(HANDLERS.onRenderEmailTemplate);
      expect(mockComposerProps!.onDraftWithAi).toBe(HANDLERS.onDraftWithAi);
      expect(mockComposerProps!.onUploadLocalAttachment).toBe(HANDLERS.onUploadLocalAttachment);
    }
  );

  it('builds the instance with the BFF host and the auth-aware fetch (the handlers are inert without them)', async () => {
    render(
      <FluentProvider theme={webLightTheme}>
        <CommunicationActionsApp context={makeContext()} readOnly={false} version="1.3.21" />
      </FluentProvider>
    );
    fireEvent.click(await screen.findByRole('button', { name: 'New' }));
    await waitFor(() => expect(mockComposerProps).toBeDefined());

    const last = mockCreateHandlers.mock.calls[mockCreateHandlers.mock.calls.length - 1][0] as {
      bffBaseUrl?: string;
      authenticatedFetch?: unknown;
    };
    expect(last.bffBaseUrl).toBe('https://bff.example');
    expect(last.authenticatedFetch).toBeDefined();
  });
});
