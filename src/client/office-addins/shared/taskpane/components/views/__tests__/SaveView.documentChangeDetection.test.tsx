/**
 * Task 094 (owner, 2026-10-04 — "Re-enable on document edits"): `SaveView` registers a content-change
 * handler on the open document for the lifetime of the Save tab, and reports a change by setting
 * `savedState.contentChangedSinceSave` through `onSavedStateChange` — never directly on `SaveFlow`,
 * which has no adapter.
 *
 * `SaveFlow` is mocked out (as in the sibling `SaveView.*.test.tsx` files): this pins the SEAM between
 * `SaveView` and the adapter's `registerDocumentChangeHandler`/`getCapabilities().canDetectDocumentChanges`,
 * not `SaveFlow`'s own button rendering (covered by `SaveFlow.savedState.test.tsx`).
 */
import { render, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SaveView } from '../SaveView';
import { SaveFlow } from '../../SaveFlow';
import type { IHostAdapter } from '@shared/adapters/IHostAdapter';
import type { HostCapabilities } from '@shared/adapters/types';

jest.mock('../../SaveFlow', () => ({ SaveFlow: jest.fn(() => null) }));

const mockedSaveFlow = SaveFlow as unknown as jest.Mock;

const WORD_CAPABILITIES: HostCapabilities = {
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
  canDetectDocumentChanges: true,
  canOpenDesktopWord: false,
  minApiVersion: '1.3',
  supportedRequirementSet: 'WordApi 1.3',
};

function makeAdapter(capabilities: HostCapabilities, registerDocumentChangeHandler: jest.Mock): IHostAdapter {
  return {
    getHostType: () => 'word',
    getItemId: jest.fn().mockResolvedValue('word-doc-1'),
    getItemType: () => 'document',
    getSubject: jest.fn().mockResolvedValue('Brief'),
    getBody: jest.fn().mockResolvedValue({ content: '', type: 'html' }),
    getAttachments: jest.fn().mockResolvedValue([]),
    getAttachmentContent: jest.fn(),
    getSenderEmail: jest.fn().mockResolvedValue(''),
    getRecipients: jest.fn().mockResolvedValue([]),
    getDocumentContent: jest.fn().mockResolvedValue(new ArrayBuffer(0)),
    getDocumentUrl: jest.fn().mockResolvedValue(null),
    readDocumentStamp: jest.fn().mockResolvedValue(null),
    writeDocumentStamp: jest.fn().mockResolvedValue('written'),
    getCapabilities: () => capabilities,
    initialize: jest.fn().mockResolvedValue(undefined),
    isInitialized: () => true,
    insertLink: jest.fn(),
    attachFile: jest.fn(),
    composeNewEmail: jest.fn(),
    registerDocumentChangeHandler,
  };
}

beforeEach(() => {
  mockedSaveFlow.mockClear();
});

describe('SaveView — document change detection (task 094)', () => {
  it('registers a handler when canDetectDocumentChanges is true, and reports a change via onSavedStateChange', async () => {
    let fireChange: (() => void) | undefined;
    const unsubscribe = jest.fn();
    const registerDocumentChangeHandler = jest.fn((onChange: () => void) => {
      fireChange = onChange;
      return Promise.resolve(unsubscribe);
    });
    const onSavedStateChange = jest.fn();
    const adapter = makeAdapter(WORD_CAPABILITIES, registerDocumentChangeHandler);

    render(
      <FluentProvider theme={webLightTheme}>
        <SaveView
          hostAdapter={adapter}
          getAccessToken={jest.fn().mockResolvedValue('token')}
          apiBaseUrl="https://bff"
          onSavedStateChange={onSavedStateChange}
        />
      </FluentProvider>
    );

    await waitFor(() => expect(mockedSaveFlow).toHaveBeenCalled());
    await waitFor(() => expect(registerDocumentChangeHandler).toHaveBeenCalledTimes(1));

    fireChange?.();

    expect(onSavedStateChange).toHaveBeenCalledTimes(1);
    const updater = onSavedStateChange.mock.calls[0]![0] as (prev: Record<string, unknown>) => Record<string, unknown>;
    expect(
      updater({
        savedDocument: null,
        profileRefreshSignal: 0,
        contentChangedSinceSave: false,
      })
    ).toMatchObject({ contentChangedSinceSave: true });
  });

  it('does not register at all when canDetectDocumentChanges is false', async () => {
    const registerDocumentChangeHandler = jest.fn();
    const adapter = makeAdapter(
      { ...WORD_CAPABILITIES, canDetectDocumentChanges: false },
      registerDocumentChangeHandler
    );

    render(
      <FluentProvider theme={webLightTheme}>
        <SaveView
          hostAdapter={adapter}
          getAccessToken={jest.fn().mockResolvedValue('token')}
          apiBaseUrl="https://bff"
          onSavedStateChange={jest.fn()}
        />
      </FluentProvider>
    );

    await waitFor(() => expect(mockedSaveFlow).toHaveBeenCalled());

    expect(registerDocumentChangeHandler).not.toHaveBeenCalled();
  });

  it('removes the handler on unmount (AC3)', async () => {
    const unsubscribe = jest.fn();
    const registerDocumentChangeHandler = jest.fn().mockResolvedValue(unsubscribe);
    const adapter = makeAdapter(WORD_CAPABILITIES, registerDocumentChangeHandler);

    const { unmount } = render(
      <FluentProvider theme={webLightTheme}>
        <SaveView
          hostAdapter={adapter}
          getAccessToken={jest.fn().mockResolvedValue('token')}
          apiBaseUrl="https://bff"
          onSavedStateChange={jest.fn()}
        />
      </FluentProvider>
    );

    await waitFor(() => expect(mockedSaveFlow).toHaveBeenCalled());
    await waitFor(() => expect(registerDocumentChangeHandler).toHaveBeenCalledTimes(1));

    unmount();

    await waitFor(() => expect(unsubscribe).toHaveBeenCalledTimes(1));
  });
});
