/**
 * GitHub #1571 review F3 (spaarkeai-word-add-in-r1 task 136, fix round 1): a contact search that FAILED (the server
 * refused it, a server fault, the session ended) says "Could not search contacts" with the reason — never the same
 * empty list as "no contact matches". The next successful search clears the message.
 *
 * Same harness as CreateTodoView.test.tsx; `onSearchContacts` is mocked at this component's boundary.
 */
import React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { CreateTodoView, type ContactOption, type SavedTodoContext } from '../CreateTodoView';
import type { IHostAdapter } from '@shared/adapters';
import type { HostCapabilities } from '@shared/adapters/types';

function renderWithProvider(ui: React.ReactElement) {
  return render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);
}

const CAPABILITIES: HostCapabilities = {
  canGetAttachments: false,
  canGetRecipients: false,
  canGetSender: false,
  canGetDocumentContent: true,
  canGetDocumentUrl: true,
  canReadDocumentStamp: true,
  canWriteDocumentStamp: true,
  canSaveAsPdf: true,
  canSaveAsEml: false,
  canInsertLink: true,
  canAttachFile: false,
  canOpenBrowserWindow: false,
  canComposeEmail: false,
  canEmailFromPane: false,
  canShowLinkedTodos: false,
  canSuggestRelatedRecords: false,
  canProvideDocumentName: true,
  canDetectDocumentChanges: false,
  canOpenDesktopWord: false,
  minApiVersion: '1.3',
  supportedRequirementSet: 'WordApi 1.3',
};

function makeHostAdapter(): IHostAdapter {
  return {
    getHostType: () => 'word',
    getItemId: jest.fn().mockResolvedValue('https://contoso.sharepoint.com/Brief.docx'),
    getItemType: () => 'document',
    // Empty — a prefilled subject would compete with this suite's own typed Name values.
    getSubject: jest.fn().mockResolvedValue(''),
    getBody: jest.fn().mockResolvedValue({ content: '', type: 'html' }),
    getAttachments: jest.fn().mockResolvedValue([]),
    getAttachmentContent: jest.fn(),
    getSenderEmail: jest.fn().mockResolvedValue(''),
    getRecipients: jest.fn().mockResolvedValue([]),
    getDocumentContent: jest.fn().mockResolvedValue(new ArrayBuffer(0)),
    getDocumentUrl: jest.fn().mockResolvedValue('https://contoso.sharepoint.com/Brief.docx'),
    readDocumentStamp: jest.fn().mockResolvedValue(null),
    writeDocumentStamp: jest.fn().mockResolvedValue('written'),
    getCapabilities: () => CAPABILITIES,
    initialize: jest.fn().mockResolvedValue(undefined),
    isInitialized: () => true,
    insertLink: jest.fn(),
    attachFile: jest.fn(),
    composeNewEmail: jest.fn(),
    registerDocumentChangeHandler: jest.fn().mockResolvedValue(() => undefined),
  };
}

const SAVED_CONTEXT: SavedTodoContext = {
  regardingEntity: 'Matter',
  regardingRecordId: 'matter-1',
  regardingName: 'M-1001',
};

describe('CreateTodoView — a failed contact search says so (#1571 review F3)', () => {
  afterEach(() => {
    jest.clearAllMocks();
  });

  function renderView(onSearchContacts: jest.Mock) {
    renderWithProvider(
      <CreateTodoView
        hostAdapter={makeHostAdapter()}
        savedContext={SAVED_CONTEXT}
        onCreateTodo={jest.fn()}
        onSearchContacts={onSearchContacts}
      />
    );
  }

  it('a rejected search shows its reason, not an empty result list', async () => {
    const onSearchContacts = jest
      .fn()
      .mockRejectedValue(new Error('Could not search contacts. You do not have permission to search contacts.'));
    renderView(onSearchContacts);

    await userEvent.type(screen.getByLabelText('Assigned To'), 'Jane');

    expect(
      await screen.findByText(
        'Could not search contacts. You do not have permission to search contacts.',
        {},
        { timeout: 2000 }
      )
    ).toBeInTheDocument();
    expect(screen.queryByRole('listbox', { name: 'Contact results' })).not.toBeInTheDocument();
  });

  it('NEGATIVE: a search that finds nothing shows no error', async () => {
    const onSearchContacts = jest.fn().mockResolvedValue([] satisfies ContactOption[]);
    renderView(onSearchContacts);

    await userEvent.type(screen.getByLabelText('Assigned To'), 'Zed');

    await waitFor(() => expect(onSearchContacts).toHaveBeenCalled(), { timeout: 2000 });
    expect(screen.queryByText(/could not search contacts/i)).not.toBeInTheDocument();
  });

  it('the next successful search clears the message', async () => {
    const onSearchContacts = jest
      .fn()
      .mockRejectedValueOnce(new Error('Could not search contacts. Search failed on the server.'))
      .mockResolvedValue([{ id: 'c1', name: 'Jane Cooper' }] satisfies ContactOption[]);
    renderView(onSearchContacts);

    const input = screen.getByLabelText('Assigned To');
    await userEvent.type(input, 'Ja');
    await screen.findByText(/could not search contacts/i, {}, { timeout: 2000 });
    await userEvent.type(input, 'ne');

    await waitFor(() => expect(screen.getByText('Jane Cooper')).toBeInTheDocument(), { timeout: 2000 });
    expect(screen.queryByText(/could not search contacts/i)).not.toBeInTheDocument();
  });
});
