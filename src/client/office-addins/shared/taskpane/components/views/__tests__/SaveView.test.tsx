/**
 * Unit tests for SaveView — the adapter-loading shell for the Save tab.
 *
 * REWRITTEN (task 071). The previous version of this file exercised a `hostContext: IHostContext`
 * prop plus `onSave`/`isSaving`/`progress`/`error`/`success` props and asserted on attachment
 * checkboxes, a save button, and a progress bar rendered directly by SaveView. None of that survives
 * in the current component: `SaveView` no longer owns any of that UI. It resolves data from a live
 * `hostAdapter: IHostAdapter` (async `getItemId`/`getSubject`/`getAttachments`/etc., capability-gated
 * per NFR-10), shows its own loading/error state while that resolution is in flight, and then renders
 * `SaveFlow` — which owns the document-info card, attachments UI, save button, progress, and
 * error/success messaging (covered by `SaveFlow.test.tsx` and its many sibling files). `IHostContext`
 * itself is `@deprecated` on `IHostAdapter.ts` (superseded by the adapter methods directly), so the old
 * prop shape isn't a smaller version of the same contract — it's gone.
 *
 * This file was rewritten to test what SaveView is actually responsible for today: adapter-driven
 * loading/error state, per-host-type data gathering (Outlook vs Word — including the two optional,
 * adapter-specific methods gated by `'x' in hostAdapter`), NFR-10 capability-gated flag passthrough,
 * and callback/optional-prop forwarding to SaveFlow. It follows the exact mocking pattern the sibling
 * `SaveView.captureAtSave.test.tsx` already established and proved (mock `SaveFlow`, capture its
 * props, build a fake `IHostAdapter` with a full `HostCapabilities` object) — that file's own header
 * comment says explicitly that this file's old setup does not apply, which is the corroborating
 * finding this rewrite acts on. `captureDocumentContent` wiring is intentionally NOT re-tested here
 * (that file already covers it end-to-end); duplicating it would be padding, not repair.
 */
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SaveView } from '../SaveView';
import { SaveFlow } from '../../SaveFlow';
import type { IHostAdapter } from '@shared/adapters/IHostAdapter';
import type { HostCapabilities } from '@shared/adapters/types';
import type { DocumentIdentityState } from '../../../services/documentIdentityService';

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
  canOpenBrowserWindow: true,
  canSuggestRelatedRecords: true,
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
    getItemId: jest.fn().mockResolvedValue('email-123'),
    getSubject: jest.fn().mockResolvedValue('Important Email'),
    getAttachments: jest.fn().mockResolvedValue([
      { id: 'att-1', name: 'document.pdf', contentType: 'application/pdf', size: 1024, isInline: false },
      { id: 'att-2', name: 'image.png', contentType: 'image/png', size: 2048, isInline: true },
    ]),
    getSenderEmail: jest.fn().mockResolvedValue('sender@example.com'),
    getRecipients: jest.fn().mockResolvedValue([{ email: 'to@example.com', type: 'to', displayName: 'To Person' }]),
    getCapabilities: () => OUTLOOK_CAPABILITIES,
    ...overrides,
  };
}

function renderSaveView(hostAdapter?: IHostAdapter | null, extraProps: Record<string, unknown> = {}) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <SaveView
        // `exactOptionalPropertyTypes` distinguishes "prop omitted" from "prop explicitly undefined" —
        // SaveViewProps.hostAdapter is `IHostAdapter | null` (no `| undefined`), so the "omitted
        // entirely" test case below spreads instead of ever passing a literal `undefined` value.
        {...(hostAdapter !== undefined ? { hostAdapter } : {})}
        getAccessToken={jest.fn().mockResolvedValue('token')}
        apiBaseUrl="https://bff"
        {...extraProps}
      />
    </FluentProvider>
  );
}

beforeEach(() => {
  mockedSaveFlow.mockClear();
});

describe('SaveView', () => {
  describe('loading and error state', () => {
    it('shows a loading state while the adapter resolves', () => {
      const adapter = makeWordAdapter({ getSubject: jest.fn(() => new Promise(() => {})) });
      renderSaveView(adapter);

      expect(screen.getByText('Loading document information...')).toBeInTheDocument();
      expect(mockedSaveFlow).not.toHaveBeenCalled();
    });

    it('shows an error state when no hostAdapter is supplied (null)', async () => {
      renderSaveView(null);

      await waitFor(() => {
        expect(screen.getByText('Unable to load document')).toBeInTheDocument();
      });
      expect(screen.getByText('Host adapter not available')).toBeInTheDocument();
      expect(mockedSaveFlow).not.toHaveBeenCalled();
    });

    it('shows an error state when hostAdapter is omitted entirely (undefined)', async () => {
      renderSaveView(undefined);

      await waitFor(() => {
        expect(screen.getByText('Host adapter not available')).toBeInTheDocument();
      });
    });

    it('surfaces the real error message when adapter loading throws', async () => {
      const adapter = makeWordAdapter({ getSubject: jest.fn().mockRejectedValue(new Error('getSubject exploded')) });
      renderSaveView(adapter);

      await waitFor(() => {
        expect(screen.getByText('getSubject exploded')).toBeInTheDocument();
      });
      expect(mockedSaveFlow).not.toHaveBeenCalled();
    });
  });

  describe('Outlook host: loads email-specific data and forwards it to SaveFlow', () => {
    it('passes hostType, itemId, itemName, attachments, sender and recipients through', async () => {
      const adapter = makeOutlookAdapter();
      renderSaveView(adapter);

      await waitFor(() => expect(mockedSaveFlow).toHaveBeenCalled());

      const props = lastSaveFlowProps();
      expect(props.hostType).toBe('outlook');
      expect(props.itemId).toBe('email-123');
      expect(props.itemName).toBe('Important Email');
      expect(props.attachments).toEqual([
        { id: 'att-1', name: 'document.pdf', contentType: 'application/pdf', size: 1024, isInline: false },
        { id: 'att-2', name: 'image.png', contentType: 'image/png', size: 2048, isInline: true },
      ]);
      expect(props.senderEmail).toBe('sender@example.com');
      expect(props.recipients).toEqual([{ email: 'to@example.com', type: 'to', displayName: 'To Person' }]);
      // Outlook host never calls the Word-only document content/URL methods.
      expect(adapter.getDocumentContent).not.toHaveBeenCalled();
      expect(props.documentUrl).toBeUndefined();
    });

    it('skips the attachments/sender/recipients fetch when those capabilities are false', async () => {
      const degradedCapabilities: HostCapabilities = {
        ...OUTLOOK_CAPABILITIES,
        canGetAttachments: false,
        canGetSender: false,
        canGetRecipients: false,
      };
      const adapter = makeOutlookAdapter({ getCapabilities: () => degradedCapabilities });
      renderSaveView(adapter);

      await waitFor(() => expect(mockedSaveFlow).toHaveBeenCalled());

      expect(adapter.getAttachments).not.toHaveBeenCalled();
      expect(adapter.getSenderEmail).not.toHaveBeenCalled();
      expect(adapter.getRecipients).not.toHaveBeenCalled();
      const props = lastSaveFlowProps();
      expect(props.attachments).toEqual([]);
      expect(props.senderEmail).toBeUndefined();
      // `recipients` state defaults to `[]` (not undefined) and SaveView's conditional spread only
      // omits a key when the state itself is `undefined` — so an un-fetched recipient list still
      // reaches SaveFlow as an empty array, not an absent prop.
      expect(props.recipients).toEqual([]);
    });

    it('reads sender display name and sent date only when the adapter implements those optional, OutlookAdapter-specific methods', async () => {
      const getSenderDisplayName = jest.fn().mockResolvedValue('Sender Display');
      const getSentDate = jest.fn().mockReturnValue(new Date('2026-01-15T10:00:00Z'));
      const adapter = makeOutlookAdapter({ getSenderDisplayName, getSentDate } as Partial<IHostAdapter>);
      renderSaveView(adapter);

      await waitFor(() => expect(mockedSaveFlow).toHaveBeenCalled());

      expect(getSenderDisplayName).toHaveBeenCalled();
      expect(getSentDate).toHaveBeenCalled();
      const props = lastSaveFlowProps();
      expect(props.senderDisplayName).toBe('Sender Display');
      expect(props.sentDate).toEqual(new Date('2026-01-15T10:00:00Z'));
    });

    it('omits senderDisplayName and sentDate when the adapter does not implement those optional methods', async () => {
      const adapter = makeOutlookAdapter();
      renderSaveView(adapter);

      await waitFor(() => expect(mockedSaveFlow).toHaveBeenCalled());

      const props = lastSaveFlowProps();
      expect(props.senderDisplayName).toBeUndefined();
      expect(props.sentDate).toBeUndefined();
    });
  });

  describe('Word host: loads document-specific data and forwards it to SaveFlow', () => {
    it('sets documentUrl from the item id and never calls the Outlook-only methods', async () => {
      const adapter = makeWordAdapter();
      renderSaveView(adapter);

      await waitFor(() => expect(mockedSaveFlow).toHaveBeenCalled());

      const props = lastSaveFlowProps();
      expect(props.hostType).toBe('word');
      expect(props.documentUrl).toBe('https://contoso.sharepoint.com/Brief.docx');
      expect(adapter.getAttachments).not.toHaveBeenCalled();
      expect(adapter.getSenderEmail).not.toHaveBeenCalled();
      expect(adapter.getRecipients).not.toHaveBeenCalled();
      expect(props.attachments).toEqual([]);
      expect(props.senderEmail).toBeUndefined();
      // Same `recipients` state-default note as the Outlook test above.
      expect(props.recipients).toEqual([]);
    });
  });

  describe('NFR-10 capability-gated flags passed to SaveFlow', () => {
    it('reflects true capabilities (canOpenBrowserWindow / canSuggestRelatedRecords / canProvideDocumentName)', async () => {
      const capabilities: HostCapabilities = {
        ...WORD_CAPABILITIES,
        canOpenBrowserWindow: true,
        canSuggestRelatedRecords: true,
        canProvideDocumentName: true,
      };
      const adapter = makeWordAdapter({ getCapabilities: () => capabilities });
      renderSaveView(adapter);

      await waitFor(() => expect(mockedSaveFlow).toHaveBeenCalled());

      const props = lastSaveFlowProps();
      expect(props.canOpenRecord).toBe(true);
      expect(props.canSuggestRelatedRecords).toBe(true);
      expect(props.canProvideDocumentName).toBe(true);
    });

    it('defaults to false when the adapter reports those capabilities as false', async () => {
      // WORD_CAPABILITIES has canProvideDocumentName: true by default (it's the Word-typical value —
      // see its own doc comment on HostCapabilities), so this test explicitly turns all three off
      // rather than relying on the shared fixture's defaults.
      const capabilities: HostCapabilities = {
        ...WORD_CAPABILITIES,
        canOpenBrowserWindow: false,
        canSuggestRelatedRecords: false,
        canProvideDocumentName: false,
      };
      const adapter = makeWordAdapter({ getCapabilities: () => capabilities });
      renderSaveView(adapter);

      await waitFor(() => expect(mockedSaveFlow).toHaveBeenCalled());

      const props = lastSaveFlowProps();
      expect(props.canOpenRecord).toBe(false);
      expect(props.canSuggestRelatedRecords).toBe(false);
      expect(props.canProvideDocumentName).toBe(false);
    });
  });

  describe('onViewDocument', () => {
    it('forwards to the provided onViewDocument callback', async () => {
      const handleViewDocument = jest.fn();
      const adapter = makeWordAdapter();
      renderSaveView(adapter, { onViewDocument: handleViewDocument });

      await waitFor(() => expect(mockedSaveFlow).toHaveBeenCalled());

      const props = lastSaveFlowProps();
      (props.onViewDocument as (url: string) => void)('https://example.com/doc');
      expect(handleViewDocument).toHaveBeenCalledWith('https://example.com/doc');
    });

    it('falls back to window.open when onViewDocument is not provided', async () => {
      const windowOpenSpy = jest.spyOn(window, 'open').mockImplementation(() => null);
      const adapter = makeWordAdapter();
      renderSaveView(adapter);

      await waitFor(() => expect(mockedSaveFlow).toHaveBeenCalled());

      const props = lastSaveFlowProps();
      (props.onViewDocument as (url: string) => void)('https://example.com/doc');
      expect(windowOpenSpy).toHaveBeenCalledWith('https://example.com/doc', '_blank');

      windowOpenSpy.mockRestore();
    });
  });

  describe('optional prop passthrough', () => {
    it('forwards onComplete, onSaved, onQuickCreate, onNavigate, allowedEntityTypes, resolvedDocumentId, documentIdentity and onRetryDocumentIdentity when supplied', async () => {
      const onComplete = jest.fn();
      const onSaved = jest.fn();
      const onQuickCreate = jest.fn();
      const onNavigate = jest.fn();
      const onRetryDocumentIdentity = jest.fn();
      const documentIdentity: DocumentIdentityState = { kind: 'new', reason: 'not_cloud_document' };
      const adapter = makeWordAdapter();

      renderSaveView(adapter, {
        onComplete,
        onSaved,
        onQuickCreate,
        onNavigate,
        allowedEntityTypes: ['Matter', 'Project'],
        resolvedDocumentId: 'doc-999',
        documentIdentity,
        onRetryDocumentIdentity,
      });

      await waitFor(() => expect(mockedSaveFlow).toHaveBeenCalled());

      const props = lastSaveFlowProps();
      expect(props.onComplete).toBe(onComplete);
      expect(props.onSaved).toBe(onSaved);
      expect(props.onQuickCreate).toBe(onQuickCreate);
      expect(props.onNavigate).toBe(onNavigate);
      expect(props.allowedEntityTypes).toEqual(['Matter', 'Project']);
      expect(props.resolvedDocumentId).toBe('doc-999');
      expect(props.documentIdentity).toEqual(documentIdentity);
      expect(props.onRetryDocumentIdentity).toBe(onRetryDocumentIdentity);
    });

    it('omits those optional props entirely (not just undefined-valued) when not supplied', async () => {
      const adapter = makeWordAdapter();
      renderSaveView(adapter);

      await waitFor(() => expect(mockedSaveFlow).toHaveBeenCalled());

      const props = lastSaveFlowProps();
      expect('onComplete' in props).toBe(false);
      expect('onSaved' in props).toBe(false);
      expect('onQuickCreate' in props).toBe(false);
      expect('onNavigate' in props).toBe(false);
      expect('resolvedDocumentId' in props).toBe(false);
      expect('documentIdentity' in props).toBe(false);
      expect('onRetryDocumentIdentity' in props).toBe(false);
    });
  });

  describe('getAccessToken', () => {
    it('uses the provided getAccessToken', async () => {
      const getAccessToken = jest.fn().mockResolvedValue('a-real-token');
      const adapter = makeWordAdapter();
      renderSaveView(adapter, { getAccessToken });

      await waitFor(() => expect(mockedSaveFlow).toHaveBeenCalled());

      expect(lastSaveFlowProps().getAccessToken).toBe(getAccessToken);
    });

    it('falls back to a function that rejects with an honest message when getAccessToken is not provided', async () => {
      const adapter = makeWordAdapter();
      render(
        <FluentProvider theme={webLightTheme}>
          <SaveView hostAdapter={adapter} apiBaseUrl="https://bff" />
        </FluentProvider>
      );

      await waitFor(() => expect(mockedSaveFlow).toHaveBeenCalled());

      const props = lastSaveFlowProps();
      await expect((props.getAccessToken as () => Promise<string>)()).rejects.toThrow('getAccessToken not provided');
    });
  });
});
