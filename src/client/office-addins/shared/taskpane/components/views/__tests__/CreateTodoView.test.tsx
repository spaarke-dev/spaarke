/**
 * Unit tests for CreateTodoView (spaarkeai-word-add-in-r1 task 091, UAT-2 / UAT-4).
 *
 * Covers the behaviors this task adds, each pinned to a specific owner UAT item:
 * - The Assigned-To contact picker's rows and selected chip show the contact's EMAIL (additive) so two
 *   contacts sharing a name can be told apart, with job title kept as a third, quieter line, and NEVER
 *   the server's literal `"contact"` fallback rendered as a job title (`realJobTitle`).
 * - A successful create shows an unmistakable confirmation (a success `MessageBar`, not only the gray
 *   "Saved" button) naming the To Do, with an "Open in Spaarke" link gated on `canOpenRecord` + `ORG_URL`
 *   (NFR-10) — mirrors `FindView.test.tsx`'s "opening a row" describe block's mocking pattern.
 * - A failed create shows the server's own reason (passed through `onCreateTodo`'s `error`), never a
 *   generic message, and renders no success confirmation.
 *
 * Does NOT re-test: the tab label/icon (covered by `TaskPaneNavigation.test.tsx` /
 * `TaskPaneShell.test.tsx`), the server's email projection (covered server-side by
 * `OfficeEntitySearchMappingTests.cs` / `OfficeEntitySearchAuthorizationContractTests.cs`), or any
 * App.tsx wiring (task 049 already declined to build an App.tsx render harness for the same reason this
 * file avoids re-deriving it — `onCreateTodo` / `onSearchContacts` are mocked at this component's own
 * boundary instead).
 */
import React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { CreateTodoView, type ContactOption, type CreateTodoResult, type SavedTodoContext } from '../CreateTodoView';
import { openRecord } from '../../../services/openRecordLauncher';
import type { IHostAdapter } from '@shared/adapters';
import type { HostCapabilities } from '@shared/adapters/types';

jest.mock('../../../services/openRecordLauncher');
const mockOpenRecord = openRecord as jest.MockedFunction<typeof openRecord>;

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

async function typeName(name: string): Promise<void> {
  // `Field label="Name"` doesn't expose a plain-text accessible label in this Fluent v9 version
  // (a `getByLabelText('Name')` probe found none) — the placeholder is this input's own stable,
  // unambiguous selector.
  const nameInput = screen.getByPlaceholderText('What needs to be done?');
  await userEvent.type(nameInput, name);
}

describe('CreateTodoView', () => {
  afterEach(() => {
    jest.clearAllMocks();
  });

  describe('Assigned-To contact rows and chip (task 091, UAT-2)', () => {
    it('shows name, email, and job title as a quieter third line, when all three are present', async () => {
      const onSearchContacts = jest
        .fn()
        .mockResolvedValue([
          { id: 'c1', name: 'Jane Cooper', email: 'jane.cooper@acme.com', displayInfo: 'General Counsel' },
        ] satisfies ContactOption[]);

      renderWithProvider(
        <CreateTodoView
          hostAdapter={makeHostAdapter()}
          savedContext={SAVED_CONTEXT}
          onCreateTodo={jest.fn()}
          onSearchContacts={onSearchContacts}
        />
      );

      await userEvent.type(screen.getByLabelText('Assigned To'), 'Jane');

      await waitFor(() => expect(screen.getByText('jane.cooper@acme.com')).toBeInTheDocument(), { timeout: 2000 });
      expect(screen.getByText('Jane Cooper')).toBeInTheDocument();
      expect(screen.getByText('General Counsel')).toBeInTheDocument();
    });

    it('shows name only — never the literal "contact" — when the contact has no email and no real job title', async () => {
      const onSearchContacts = jest
        .fn()
        .mockResolvedValue([{ id: 'c2', name: 'Robert Fox', displayInfo: 'contact' }] satisfies ContactOption[]);

      renderWithProvider(
        <CreateTodoView
          hostAdapter={makeHostAdapter()}
          savedContext={SAVED_CONTEXT}
          onCreateTodo={jest.fn()}
          onSearchContacts={onSearchContacts}
        />
      );

      await userEvent.type(screen.getByLabelText('Assigned To'), 'Robert');

      await waitFor(() => expect(screen.getByText('Robert Fox')).toBeInTheDocument(), { timeout: 2000 });
      expect(screen.queryByText('contact')).not.toBeInTheDocument();
    });

    it('the selected chip shows name and email', async () => {
      const onSearchContacts = jest
        .fn()
        .mockResolvedValue([
          { id: 'c3', name: 'Wade Warren', email: 'wade.warren@beta.com' },
        ] satisfies ContactOption[]);

      renderWithProvider(
        <CreateTodoView
          hostAdapter={makeHostAdapter()}
          savedContext={SAVED_CONTEXT}
          onCreateTodo={jest.fn()}
          onSearchContacts={onSearchContacts}
        />
      );

      await userEvent.type(screen.getByLabelText('Assigned To'), 'Wade');
      const row = await screen.findByRole('option', { name: /Wade Warren/ }, { timeout: 2000 });
      await userEvent.click(row);

      // The input + dropdown are gone; the chip remains with both lines.
      expect(screen.getByText('Wade Warren')).toBeInTheDocument();
      expect(screen.getByText('wade.warren@beta.com')).toBeInTheDocument();
    });
  });

  describe('confirmation after a successful create (task 091, UAT-2)', () => {
    const ORG_URL = 'https://contoso.crm.dynamics.com';
    const originalOrgUrl = process.env.ORG_URL;

    afterEach(() => {
      if (originalOrgUrl === undefined) {
        delete process.env.ORG_URL;
      } else {
        process.env.ORG_URL = originalOrgUrl;
      }
    });

    it('shows a success MessageBar naming the To Do, with "Open in Spaarke" when canOpenRecord + ORG_URL are both set', async () => {
      process.env.ORG_URL = ORG_URL;
      const onCreateTodo = jest.fn<Promise<CreateTodoResult>, unknown[]>().mockResolvedValue({
        ok: true,
        todoId: 'todo-123',
      });

      renderWithProvider(
        <CreateTodoView
          hostAdapter={makeHostAdapter()}
          savedContext={SAVED_CONTEXT}
          onCreateTodo={onCreateTodo}
          onSearchContacts={jest.fn().mockResolvedValue([])}
          canOpenRecord
        />
      );

      await typeName('Call the client');
      await userEvent.click(screen.getByRole('button', { name: /^save$/i }));

      await waitFor(() => expect(screen.getByText('To Do created: Call the client')).toBeInTheDocument());
      const openButton = screen.getByRole('button', { name: /open in spaarke/i });

      await userEvent.click(openButton);
      expect(mockOpenRecord).toHaveBeenCalledWith({
        orgUrl: ORG_URL,
        entityType: 'sprk_todo',
        recordId: 'todo-123',
      });
    });

    it('shows the success message WITHOUT the link when canOpenRecord is false', async () => {
      process.env.ORG_URL = ORG_URL;
      const onCreateTodo = jest.fn<Promise<CreateTodoResult>, unknown[]>().mockResolvedValue({
        ok: true,
        todoId: 'todo-123',
      });

      renderWithProvider(
        <CreateTodoView
          hostAdapter={makeHostAdapter()}
          savedContext={SAVED_CONTEXT}
          onCreateTodo={onCreateTodo}
          onSearchContacts={jest.fn().mockResolvedValue([])}
        />
      );

      await typeName('Call the client');
      await userEvent.click(screen.getByRole('button', { name: /^save$/i }));

      await waitFor(() => expect(screen.getByText('To Do created: Call the client')).toBeInTheDocument());
      expect(screen.queryByRole('button', { name: /open in spaarke/i })).not.toBeInTheDocument();
    });

    it('shows the success message WITHOUT the link when ORG_URL is not configured', async () => {
      delete process.env.ORG_URL;
      const onCreateTodo = jest.fn<Promise<CreateTodoResult>, unknown[]>().mockResolvedValue({
        ok: true,
        todoId: 'todo-123',
      });

      renderWithProvider(
        <CreateTodoView
          hostAdapter={makeHostAdapter()}
          savedContext={SAVED_CONTEXT}
          onCreateTodo={onCreateTodo}
          onSearchContacts={jest.fn().mockResolvedValue([])}
          canOpenRecord
        />
      );

      await typeName('Call the client');
      await userEvent.click(screen.getByRole('button', { name: /^save$/i }));

      await waitFor(() => expect(screen.getByText('To Do created: Call the client')).toBeInTheDocument());
      expect(screen.queryByRole('button', { name: /open in spaarke/i })).not.toBeInTheDocument();
    });
  });

  describe('a failed create (task 091, UAT-2 negative case)', () => {
    it("shows the server's own reason, not a generic message, and renders no success confirmation", async () => {
      const onCreateTodo = jest.fn<Promise<CreateTodoResult>, unknown[]>().mockResolvedValue({
        ok: false,
        error: 'This item could not be assigned an owner, so it was not saved.',
      });

      renderWithProvider(
        <CreateTodoView
          hostAdapter={makeHostAdapter()}
          savedContext={SAVED_CONTEXT}
          onCreateTodo={onCreateTodo}
          onSearchContacts={jest.fn().mockResolvedValue([])}
        />
      );

      await typeName('Call the client');
      await userEvent.click(screen.getByRole('button', { name: /^save$/i }));

      await waitFor(() =>
        expect(screen.getByText('This item could not be assigned an owner, so it was not saved.')).toBeInTheDocument()
      );
      expect(screen.queryByText(/To Do created/)).not.toBeInTheDocument();
    });
  });
});
