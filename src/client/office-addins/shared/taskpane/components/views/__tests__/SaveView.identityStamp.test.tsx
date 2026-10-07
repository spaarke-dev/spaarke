/**
 * Task 089 (UAT-9): after EVERY successful pane save — create or version — `SaveView` marks the document open in
 * Word with the id it was saved as, then hands the completion on unchanged. The write is capability-gated (NFR-10)
 * and never blocks or fails the save.
 *
 * `SaveFlow` is mocked (as in `SaveView.captureAtSave.test.tsx`): this pins the SEAM — the `onComplete` SaveView
 * hands SaveFlow, which `useSaveFlow` calls on every successful completion (both its poll and SSE branches).
 */
import { render, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SaveView } from '../SaveView';
import { SaveFlow } from '../../SaveFlow';
import type { IHostAdapter } from '@shared/adapters/IHostAdapter';
import type { HostCapabilities } from '@shared/adapters/types';

jest.mock('../../SaveFlow', () => ({ SaveFlow: jest.fn(() => null) }));

const mockedSaveFlow = SaveFlow as unknown as jest.Mock;
const SAVED_ID = '3fa85f64-5717-4562-b3fc-2c963f66afa6';

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
  canDetectDocumentChanges: false,
  canOpenDesktopWord: false,
  minApiVersion: '1.3',
  supportedRequirementSet: 'WordApi 1.3',
};

function makeAdapter(capabilities: HostCapabilities, overrides: Partial<IHostAdapter> = {}): IHostAdapter {
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
    registerDocumentChangeHandler: jest.fn().mockResolvedValue(() => undefined),
    ...overrides,
  };
}

async function completeASave(adapter: IHostAdapter, onComplete?: jest.Mock): Promise<void> {
  render(
    <FluentProvider theme={webLightTheme}>
      <SaveView
        hostAdapter={adapter}
        getAccessToken={jest.fn().mockResolvedValue('t')}
        {...(onComplete ? { onComplete } : {})}
      />
    </FluentProvider>
  );
  await waitFor(() => expect(mockedSaveFlow).toHaveBeenCalled());
  const props = mockedSaveFlow.mock.calls[mockedSaveFlow.mock.calls.length - 1]![0] as {
    onComplete: (id: string, url: string) => void;
  };
  props.onComplete(SAVED_ID, '');
}

beforeEach(() => {
  mockedSaveFlow.mockClear();
});

describe('SaveView marks the open document after a successful save (task 089)', () => {
  it('writes the saved id into the open document and still hands the completion to the caller', async () => {
    const adapter = makeAdapter(WORD_CAPABILITIES);
    const onComplete = jest.fn();

    await completeASave(adapter, onComplete);

    await waitFor(() => expect(adapter.writeDocumentStamp).toHaveBeenCalledWith(SAVED_ID));
    expect(onComplete).toHaveBeenCalledWith(SAVED_ID, '');
  });

  it('with the capability false (Outlook, or no CustomXmlParts) no write is attempted and the save is still reported', async () => {
    const adapter = makeAdapter({ ...WORD_CAPABILITIES, canWriteDocumentStamp: false });
    const onComplete = jest.fn();

    await completeASave(adapter, onComplete);

    expect(onComplete).toHaveBeenCalledWith(SAVED_ID, '');
    expect(adapter.writeDocumentStamp).not.toHaveBeenCalled();
  });

  it('a refused write never stops the completion from being reported', async () => {
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    const adapter = makeAdapter(WORD_CAPABILITIES, {
      writeDocumentStamp: jest.fn().mockRejectedValue({ code: 'UNKNOWN_ERROR', message: 'add refused' }),
    });
    const onComplete = jest.fn();

    try {
      await completeASave(adapter, onComplete);
      expect(onComplete).toHaveBeenCalledWith(SAVED_ID, '');
      await waitFor(() => expect(warn).toHaveBeenCalled());
    } finally {
      warn.mockRestore();
    }
  });
});
