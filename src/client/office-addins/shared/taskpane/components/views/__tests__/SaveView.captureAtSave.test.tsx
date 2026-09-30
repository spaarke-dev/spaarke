/**
 * Task 045: the pane used to read the open Word document's bytes ONCE, in `SaveView`'s mount effect,
 * and never again — so any edit made after the Save tab opened was silently never uploaded, while the
 * pane reported success (`notes/025-residual-collision-surface.md` M12 / R5 / D-i).
 *
 * This file pins the SEAM between `SaveView` and Office.js: `captureDocumentContent` (the prop
 * `SaveView` now hands to `SaveFlow` instead of a static `documentContentBase64` value) is a LIVE
 * function that reads `hostAdapter.getDocumentContent()` fresh on every call, is never invoked merely
 * by mounting, and is gated on the `canGetDocumentContent` capability (NFR-10) — never `hostType`.
 *
 * `SaveFlow` is mocked out here on purpose. Its own use of the capture function — invoking it at the
 * moment of Save/retry/"Save Another" rather than once — is covered end-to-end (with the real
 * `useSaveFlow` hook and a real `fetch` mock) by `SaveFlow.captureAtSave.test.tsx`. This file is
 * deliberately narrow: it is the reproduce-first proof that `SaveView` no longer captures at mount,
 * and that what it hands down instead is genuinely live.
 *
 * NOT reusing `SaveView.test.tsx`'s setup: that file targets a stale `hostContext`/`onSave` shape that
 * predates the current `hostAdapter`-based `SaveView` (verified 2026-09-17 — it is one of the ten
 * pre-existing failing suites named in this project's CLAUDE.md decisions log) and its mocks would not
 * apply here.
 */
import { render, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SaveView } from '../SaveView';
import { SaveFlow } from '../../SaveFlow';
import type { IHostAdapter } from '@shared/adapters/IHostAdapter';
import type { HostCapabilities } from '@shared/adapters/types';

jest.mock('../../SaveFlow', () => ({ SaveFlow: jest.fn(() => null) }));

const mockedSaveFlow = SaveFlow as unknown as jest.Mock;

function lastSaveFlowProps(): Record<string, unknown> {
  const calls = mockedSaveFlow.mock.calls;
  expect(calls.length).toBeGreaterThan(0);
  return calls[calls.length - 1]![0] as Record<string, unknown>;
}

const WORD_CAPABILITIES: HostCapabilities = {
  canGetAttachments: false,
  canGetRecipients: false,
  canGetSender: false,
  canGetDocumentContent: true,
  canGetDocumentUrl: true,
  canReadDocumentStamp: true,
  canSaveAsPdf: true,
  canSaveAsEml: false,
  canInsertLink: true,
  canAttachFile: false,
  canOpenBrowserWindow: false,
  canComposeEmail: false,
  canShowLinkedTodos: false,
  canSuggestRelatedRecords: false,
  canProvideDocumentName: true,
  minApiVersion: '1.3',
  supportedRequirementSet: 'WordApi 1.3',
};

const OUTLOOK_CAPABILITIES: HostCapabilities = {
  ...WORD_CAPABILITIES,
  canGetAttachments: true,
  canGetRecipients: true,
  canGetSender: true,
  canGetDocumentContent: false,
  canGetDocumentUrl: false,
  canReadDocumentStamp: false,
  canProvideDocumentName: false,
};

function makeWordAdapter(overrides: Partial<IHostAdapter> = {}): IHostAdapter {
  return {
    getHostType: () => 'word',
    getItemId: jest.fn().mockResolvedValue('https://contoso.sharepoint.com/Brief.docx'),
    getItemType: () => 'document',
    getSubject: jest.fn().mockResolvedValue('Brief'),
    getBody: jest.fn().mockResolvedValue({ content: '', type: 'html' }),
    getAttachments: jest.fn().mockResolvedValue([]),
    getAttachmentContent: jest.fn(),
    getSenderEmail: jest.fn().mockResolvedValue(''),
    getRecipients: jest.fn().mockResolvedValue([]),
    getDocumentContent: jest.fn().mockResolvedValue(new ArrayBuffer(0)),
    getDocumentUrl: jest.fn().mockResolvedValue('https://contoso.sharepoint.com/Brief.docx'),
    readDocumentStamp: jest.fn().mockResolvedValue(null),
    getCapabilities: () => WORD_CAPABILITIES,
    initialize: jest.fn().mockResolvedValue(undefined),
    isInitialized: () => true,
    insertLink: jest.fn(),
    attachFile: jest.fn(),
    composeNewEmail: jest.fn(),
    ...overrides,
  };
}

function makeOutlookAdapter(overrides: Partial<IHostAdapter> = {}): IHostAdapter {
  return {
    ...makeWordAdapter(),
    getHostType: () => 'outlook',
    getItemType: () => 'email',
    getCapabilities: () => OUTLOOK_CAPABILITIES,
    ...overrides,
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

beforeEach(() => {
  mockedSaveFlow.mockClear();
});

describe('SaveView -> SaveFlow wiring: captureDocumentContent (task 045)', () => {
  it('passes a live captureDocumentContent function for a Word adapter, and never calls getDocumentContent merely by mounting', async () => {
    const adapter = makeWordAdapter();
    renderSaveView(adapter);

    await waitFor(() => expect(mockedSaveFlow).toHaveBeenCalled());

    // The old defect: SaveView's mount effect called getDocumentContent() once, right here. It must not.
    expect(adapter.getDocumentContent).not.toHaveBeenCalled();

    const props = lastSaveFlowProps();
    expect(typeof props.captureDocumentContent).toBe('function');
    // No static value threaded either — SaveFlow gets ONLY the live function on a real Word adapter.
    expect(props.documentContentBase64).toBeUndefined();
  });

  it("captureDocumentContent reads the adapter's CURRENT bytes on every call — reproduces the fix: B1 then B2, not a cached mount-time snapshot", async () => {
    const getDocumentContent = jest
      .fn()
      .mockResolvedValueOnce(new TextEncoder().encode('DRAFT-1').buffer)
      .mockResolvedValueOnce(new TextEncoder().encode('DRAFT-2-EDITED-AFTER-MOUNT').buffer);
    const adapter = makeWordAdapter({ getDocumentContent });
    renderSaveView(adapter);
    await waitFor(() => expect(mockedSaveFlow).toHaveBeenCalled());

    const capture = lastSaveFlowProps().captureDocumentContent as () => Promise<string>;

    // First save attempt.
    const first = await capture();
    expect(getDocumentContent).toHaveBeenCalledTimes(1);
    expect(getDocumentContent).toHaveBeenCalledWith({ format: 'ooxml' });
    expect(Buffer.from(first, 'base64').toString('utf8')).toBe('DRAFT-1');

    // Simulates a second attempt (a retry, or the next Save after "Save Another") after the user kept
    // editing the open document. The SAME function, invoked again, reads whatever is open NOW — proving
    // this is not the old mount-time snapshot.
    const second = await capture();
    expect(getDocumentContent).toHaveBeenCalledTimes(2);
    expect(Buffer.from(second, 'base64').toString('utf8')).toBe('DRAFT-2-EDITED-AFTER-MOUNT');
  });

  it('normalizes a rejected getDocumentContent (a HostAdapterError { code, message } shape, not instanceof Error) into a real Error carrying the underlying message', async () => {
    const getDocumentContent = jest.fn().mockRejectedValue({
      code: 'CONTENT_RETRIEVAL_FAILED',
      message: 'Failed to read the Word document (getFileAsync).',
    });
    const adapter = makeWordAdapter({ getDocumentContent });
    renderSaveView(adapter);
    await waitFor(() => expect(mockedSaveFlow).toHaveBeenCalled());

    const capture = lastSaveFlowProps().captureDocumentContent as () => Promise<string>;
    await expect(capture()).rejects.toThrow('Failed to read the Word document (getFileAsync).');
  });

  it('a capture rejection with no message falls back to an honest, non-empty message', async () => {
    const getDocumentContent = jest.fn().mockRejectedValue({ code: 'CONTENT_RETRIEVAL_FAILED' });
    const adapter = makeWordAdapter({ getDocumentContent });
    renderSaveView(adapter);
    await waitFor(() => expect(mockedSaveFlow).toHaveBeenCalled());

    const capture = lastSaveFlowProps().captureDocumentContent as () => Promise<string>;
    await expect(capture()).rejects.toThrow(/couldn.t read the document/i);
  });

  it('an Outlook adapter (canGetDocumentContent: false) gets NO captureDocumentContent prop at all — NFR-10: gated on the capability, never hostType', async () => {
    const adapter = makeOutlookAdapter();
    renderSaveView(adapter);
    await waitFor(() => expect(mockedSaveFlow).toHaveBeenCalled());

    expect(adapter.getDocumentContent).not.toHaveBeenCalled();
    const props = lastSaveFlowProps();
    expect(props.captureDocumentContent).toBeUndefined();
  });
});
