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
const mockResolvePicks = jest.fn();
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
  resolveRecipientPicks: (...args: unknown[]) => mockResolvePicks(...args),
  resolveInitialInvoiceRecord: () => undefined,
  cleanGuid: (id: string) => id.replace(/[{}]/g, '').toLowerCase(),
  getXrm: () => (global as unknown as { Xrm?: unknown }).Xrm,
}));

// The shared auth bootstrap (#2967) is covered in @spaarke/communication-components; here auth is simply ready.
jest.mock('@spaarke/communication-components/logic/pcf-host', () => ({
  __esModule: true,
  usePcfAuthBootstrap: () => ({ authReady: true, authError: null, bffBaseUrl: 'https://bff.example' }),
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
  // Task 238 (b): the PCF's people-picker handler used to carry its own copy of the resolve loop that dropped an
  // unresolvable pick silently. It now runs the shared resolver and returns its result, `skipped` list included.
  it('the recipient lookup runs the shared resolver over the picks and returns its result with the skipped list', async () => {
    const picks = [{ id: '{AAAAAAAA-1111-2222-3333-444444444444}', name: 'Jane Smith', entityType: 'systemuser' }];
    (global as unknown as { Xrm?: unknown }).Xrm = { Utility: { lookupObjects: jest.fn().mockResolvedValue(picks) } };
    const resolved = Object.assign([], { skipped: [{ name: 'Jane Smith', reason: 'the record could not be read.' }] });
    mockResolvePicks.mockResolvedValue(resolved);
    const context = makeContext();
    try {
      render(
        <FluentProvider theme={webLightTheme}>
          <CommunicationActionsApp context={context} readOnly={false} version="1.3.21" />
        </FluentProvider>
      );
      fireEvent.click(await screen.findByRole('button', { name: 'New' }));
      await waitFor(() => expect(mockComposerProps).toBeDefined());

      const lookup = mockComposerProps!.onLookupRecipients as (field: 'to') => Promise<unknown>;
      const result = await lookup('to');

      expect(result).toBe(resolved);
      expect(mockResolvePicks).toHaveBeenCalledWith(picks, expect.any(Function), '[CommunicationActions]');
      // the reader it was given is the control's own Web API
      const reader = mockResolvePicks.mock.calls[0][1] as (e: string, id: string, o: string) => Promise<unknown>;
      await reader('systemuser', 'x', '?$select=internalemailaddress');
      expect(context.webAPI.retrieveRecord).toHaveBeenCalledWith('systemuser', 'x', '?$select=internalemailaddress');
    } finally {
      delete (global as unknown as { Xrm?: unknown }).Xrm;
    }
  });
});
