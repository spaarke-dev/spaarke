/**
 * Task 121 (spaarkeai-word-add-in-r1): `SaveView` reads the open email's RFC Message-ID and hands it to `SaveFlow`
 * as `internetMessageId`, next to the Exchange item id (`itemId`) — two different values.
 *
 * - read through the capability-gated seam task 120 added (`canResolveEmailIdentity` + `getEmailIdentityKeys`), never
 *   a `hostType` check (NFR-10);
 * - not attempted without the capability (a compose item has no Message-ID; Word has no email);
 * - a failure to read it never blocks the Save tab: the save then carries the item id alone.
 *
 * `SaveFlow` is mocked to capture its props (the same narrow seam as `SaveView.emailContentCapture.test.tsx`).
 */
import { render, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SaveView } from '../SaveView';
import { SaveFlow } from '../../SaveFlow';
import type { IHostAdapter } from '@shared/adapters/IHostAdapter';
import type { HostCapabilities } from '@shared/adapters/types';

jest.mock('../../SaveFlow', () => ({ SaveFlow: jest.fn(() => null) }));
const mockedSaveFlow = SaveFlow as unknown as jest.Mock;

const RFC_ID = '<CAF0a1b2c3@mail.example.com>';
const ITEM_ID = 'AAMkAGI2TG93AAA=';

const READ_MODE: HostCapabilities = {
  canGetAttachments: true,
  canGetRecipients: true,
  canGetSender: true,
  canGetDocumentContent: false,
  canGetDocumentUrl: false,
  canReadDocumentStamp: false,
  canWriteDocumentStamp: false,
  canResolveEmailIdentity: true,
  canSaveAsPdf: false,
  canSaveAsEml: true,
  canInsertLink: false,
  canAttachFile: false,
  canOpenBrowserWindow: false,
  canComposeEmail: false,
  canEmailFromPane: false,
  canShowLinkedTodos: true,
  canSuggestRelatedRecords: true,
  canProvideDocumentName: false,
  canDetectDocumentChanges: false,
  canOpenDesktopWord: false,
  minApiVersion: '1.5',
  supportedRequirementSet: 'Mailbox 1.8',
};

function makeOutlookAdapter(
  capabilities: HostCapabilities,
  getEmailIdentityKeys: jest.Mock = jest.fn().mockResolvedValue({ internetMessageId: RFC_ID, exchangeItemId: ITEM_ID })
): IHostAdapter {
  return {
    getHostType: () => 'outlook',
    getItemId: jest.fn().mockResolvedValue(ITEM_ID),
    getItemType: () => 'email',
    getSubject: jest.fn().mockResolvedValue('RE: Discovery schedule'),
    getBody: jest.fn(),
    getAttachments: jest.fn().mockResolvedValue([]),
    getAttachmentContent: jest.fn(),
    getSenderEmail: jest.fn().mockResolvedValue('counsel@contoso.com'),
    getRecipients: jest.fn().mockResolvedValue([]),
    getDocumentContent: jest.fn(),
    getDocumentUrl: jest.fn().mockResolvedValue(null),
    readDocumentStamp: jest.fn().mockResolvedValue(null),
    writeDocumentStamp: jest.fn(),
    getEmailIdentityKeys,
    getCapabilities: () => capabilities,
    initialize: jest.fn().mockResolvedValue(undefined),
    isInitialized: () => true,
    insertLink: jest.fn(),
    attachFile: jest.fn(),
    composeNewEmail: jest.fn(),
    registerDocumentChangeHandler: jest.fn().mockResolvedValue(() => undefined),
  };
}

function renderSaveView(hostAdapter: IHostAdapter) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <SaveView
        hostAdapter={hostAdapter}
        getAccessToken={jest.fn().mockResolvedValue('token')}
        apiBaseUrl="https://bff"
      />
    </FluentProvider>
  );
}

const lastSaveFlowProps = () => mockedSaveFlow.mock.calls[mockedSaveFlow.mock.calls.length - 1]![0];

beforeEach(() => {
  mockedSaveFlow.mockReset();
  mockedSaveFlow.mockImplementation(() => null);
});

describe('SaveView — the email Message-ID for the save (task 121)', () => {
  it('reads the RFC Message-ID in read mode and passes it beside the item id', async () => {
    const adapter = makeOutlookAdapter(READ_MODE);
    renderSaveView(adapter);

    await waitFor(() => expect(mockedSaveFlow).toHaveBeenCalled());
    expect(adapter.getEmailIdentityKeys).toHaveBeenCalledTimes(1);
    expect(lastSaveFlowProps().internetMessageId).toBe(RFC_ID);
    expect(lastSaveFlowProps().itemId).toBe(ITEM_ID);
  });

  it('does not read it without the capability (a compose item) — the save then carries the item id alone', async () => {
    const adapter = makeOutlookAdapter({ ...READ_MODE, canResolveEmailIdentity: false });
    renderSaveView(adapter);

    await waitFor(() => expect(mockedSaveFlow).toHaveBeenCalled());
    expect(adapter.getEmailIdentityKeys).not.toHaveBeenCalled();
    expect('internetMessageId' in lastSaveFlowProps()).toBe(false);
    expect(lastSaveFlowProps().itemId).toBe(ITEM_ID);
  });

  it('a failure to read it never blocks the Save tab', async () => {
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    try {
      const adapter = makeOutlookAdapter(READ_MODE, jest.fn().mockRejectedValue(new Error('not read mode')));
      renderSaveView(adapter);

      await waitFor(() => expect(mockedSaveFlow).toHaveBeenCalled());
      expect('internetMessageId' in lastSaveFlowProps()).toBe(false);
      expect(lastSaveFlowProps().itemId).toBe(ITEM_ID);
    } finally {
      warn.mockRestore();
    }
  });

  it('an item without a Message-ID passes none', async () => {
    const adapter = makeOutlookAdapter(READ_MODE, jest.fn().mockResolvedValue(null));
    renderSaveView(adapter);

    await waitFor(() => expect(mockedSaveFlow).toHaveBeenCalled());
    expect('internetMessageId' in lastSaveFlowProps()).toBe(false);
  });
});
