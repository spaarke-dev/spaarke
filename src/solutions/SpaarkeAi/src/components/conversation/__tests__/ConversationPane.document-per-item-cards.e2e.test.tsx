/**
 * ConversationPane.document-per-item-cards.e2e.test.tsx — task 026 (FR-11).
 *
 * Proves the document per-item ACTION CARDS (Summarize · Draft response · Draft memo), keyed to
 * the active-item conduit's document handle:
 *   (a) document active item (published by the WorkspacePane tab-focus branch, tested separately
 *       in `WorkspacePane.document-active-item.test.tsx`) → the three cards render (no typing).
 *   (b) clicking a card arms the ONE-SHOT document handle + sends the deterministic instruction
 *       through the SAME `pendingOutboundMessage` host→send seam `LOCAL_CHIP.emailSummarize` uses
 *       — proven here via the captured `pendingOutboundMessage` prop.
 *   (c) `onDecorateOutboundBody` stamps `body.documentId` with the active document's id for that
 *       ONE turn (ADR-015 on-demand — never a tab/content snapshot; only the id travels) — proven
 *       by invoking the captured decorate function directly, mirroring how SprkChat would call it
 *       when it actually sends the armed `pendingOutboundMessage`.
 *   (d) each card operates on the documentId (distinct instruction text + the SAME armed id) via
 *       the existing document/RAG capability (body.documentId → DocumentContextService, no new
 *       BFF tool asserted here — the server-side pipeline is out of this suite's boundary).
 *   (e) cards suppress when there is no single active document (deselect / a non-document active
 *       item, e.g. email or compose).
 *   (f) email per-item cards (task 025) still render — no regression from this additive change.
 *
 * Harness mirrors `ConversationPane.email-per-item-cards.e2e.test.tsx` (mocked SprkChat rendering
 * `transcriptFooterSlot` + capturing `pendingOutboundMessage`/`onDecorateOutboundBody`) plus
 * `activeItemConduit.test.tsx`'s `ActiveItemConduitProvider` + `usePublishActiveItem` harness
 * pattern — no real Xrm/composer chain needed since documents have no composer surface (task 026
 * finalization; see `register-document-viewer-widget.ts`).
 */
import '@testing-library/jest-dom';
import * as React from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme, type Theme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';
import { ComposeActionBridgeProvider } from '@spaarke/compose-components/context/composeActionBridge';
import type { ISprkChatProps } from '@spaarke/ui-components';
import { ActiveItemConduitProvider, usePublishActiveItem, type ActiveItemHandle } from '../../workspace/activeItemConduit';

const BFF = 'https://test-bff.example.com';
const CHAT_SESSION = '00000000-0000-0000-0000-000000000026';
const DOCUMENT_ID = 'doc-active-1';
const DOCUMENT_LABEL = 'Master Services Agreement.pdf';

// Latest values of the additive host-send props, captured on every stub render.
const captured: {
  transcriptFooterSlot?: React.ReactNode;
  pendingOutboundMessage?: string | null;
  onDecorateOutboundBody?: (body: Record<string, unknown>) => Promise<Record<string, unknown> | null>;
} = {};

const dispatchConsumerSpy = jest.fn(() => Promise.resolve({ streamId: 'stream-test', status: 'complete' as const }));

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  return {
    ...actual,
    SprkChat: (props: ISprkChatProps) => {
      const p = props as ISprkChatProps & {
        transcriptFooterSlot?: React.ReactNode;
        pendingOutboundMessage?: string | null;
        onDecorateOutboundBody?: (body: Record<string, unknown>) => Promise<Record<string, unknown> | null>;
      };
      captured.transcriptFooterSlot = p.transcriptFooterSlot;
      captured.pendingOutboundMessage = p.pendingOutboundMessage;
      captured.onDecorateOutboundBody = p.onDecorateOutboundBody;
      return <div data-testid="sprkchat-stub">{p.transcriptFooterSlot}</div>;
    },
    createConsumerDispatcher: () => dispatchConsumerSpy,
    createXrmDataService: () => ({
      createRecord: jest.fn(),
      retrieveRecord: jest.fn(),
      retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
      updateRecord: jest.fn(),
      deleteRecord: jest.fn(),
    }),
    createXrmNavigationService: () => ({
      openRecord: jest.fn(),
      openRecordModal: jest.fn(),
      openDialog: jest.fn(),
      closeDialog: jest.fn(),
      openLookup: jest.fn(),
    }),
  };
});

const authenticatedFetchMock = jest.fn(async (url: string) => {
  if (typeof url === 'string' && url.includes('/api/ai/capabilities')) {
    return { ok: true, status: 200, json: async () => ({ capabilities: [] }) } as unknown as Response;
  }
  if (typeof url === 'string' && url.includes('/suggest')) {
    return { ok: true, status: 200, json: async () => ({ chips: [] }) } as unknown as Response;
  }
  return { ok: false, status: 404, json: async () => ({}), text: async () => '' } as unknown as Response;
});

jest.mock('@spaarke/ai-widgets', () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const actual = jest.requireActual('@spaarke/ai-widgets') as any;
  return {
    ...actual,
    useAiSession: () => ({
      isAuthenticated: true,
      authenticatedFetch: authenticatedFetchMock,
      getAccessToken: jest.fn(async () => 'token'),
      bffBaseUrl: BFF,
      tenantId: 'test-tenant',
      chatSessionId: CHAT_SESSION,
      setChatSessionId: jest.fn(),
      clearChatSession: jest.fn(),
      playbookId: undefined,
      setPlaybookId: jest.fn(),
      entityContext: null,
      contextMapping: null,
      isLoadingContextMapping: false,
      streaming: { onPaneEvent: null },
      streamingState: { isStreaming: false, tokenCount: 0 },
      turnCount: 0,
      isLoading: false,
    }),
  };
});

jest.mock('../../shell/ThreePaneShell', () => ({
  useShellStage: () => ({
    stage: 'active-chat' as const,
    toLoading: jest.fn(),
    toActiveChat: jest.fn(),
    toReview: jest.fn(),
    reset: jest.fn(),
  }),
  useRestoreContext: () => null,
  usePaneCollapseContext: () => null,
  useComposeLaunch: () => null,
}));

// Import AFTER mocks.
import { ConversationPane } from '../ConversationPane';

let publishActiveItem: (handle: ActiveItemHandle | null) => void = () => {};

function ActiveItemPublisherHarness(): null {
  publishActiveItem = usePublishActiveItem();
  return null;
}

function renderPane(theme: Theme = webLightTheme): PaneEventBus {
  const bus = new PaneEventBus();
  render(
    <FluentProvider theme={theme}>
      <PaneEventBusProvider bus={bus}>
        <ComposeActionBridgeProvider>
          <ActiveItemConduitProvider>
            <ActiveItemPublisherHarness />
            <ConversationPane />
          </ActiveItemConduitProvider>
        </ComposeActionBridgeProvider>
      </PaneEventBusProvider>
    </FluentProvider>
  );
  return bus;
}

async function publishActiveDocument(overrides: Partial<ActiveItemHandle> = {}): Promise<void> {
  await act(async () => {
    publishActiveItem({ id: DOCUMENT_ID, type: 'document', label: DOCUMENT_LABEL, ...overrides });
    await Promise.resolve();
  });
}

beforeEach(() => {
  authenticatedFetchMock.mockClear();
  dispatchConsumerSpy.mockClear();
  captured.transcriptFooterSlot = undefined;
  captured.pendingOutboundMessage = undefined;
  captured.onDecorateOutboundBody = undefined;
});

describe('task 026 (FR-11): document per-item action cards', () => {
  it('(a) focusing a document-viewer tab surfaces the three cards with NO typing', async () => {
    renderPane();
    await publishActiveDocument();

    expect(await screen.findByTestId('document-per-item-cards')).toBeInTheDocument();
    expect(screen.getByTestId('document-card-summarize_document-summarize')).toHaveTextContent('Summarize');
    expect(screen.getByTestId('document-card-draft_document_response-draft-response')).toHaveTextContent(
      'Draft response'
    );
    expect(screen.getByTestId('document-card-draft_document_memo-draft-memo')).toHaveTextContent('Draft memo');
  });

  it('(e) suppresses when there is no active item (deselect)', async () => {
    renderPane();
    await publishActiveDocument();
    expect(await screen.findByTestId('document-per-item-cards')).toBeInTheDocument();

    await act(async () => {
      publishActiveItem(null);
      await Promise.resolve();
    });
    expect(screen.queryByTestId('document-per-item-cards')).not.toBeInTheDocument();
  });

  it('(e) suppresses when the active item is not a document (e.g. an email)', async () => {
    renderPane();
    await act(async () => {
      publishActiveItem({ id: 'comm-1', type: 'email', label: 'Some subject' });
      await Promise.resolve();
    });
    expect(screen.queryByTestId('document-per-item-cards')).not.toBeInTheDocument();
  });

  it('(b)(c)(d) Summarize: arms pendingOutboundMessage with the deterministic instruction, and the decorate seam stamps body.documentId with the active document id (id-keyed — no content)', async () => {
    renderPane();
    await publishActiveDocument();
    await screen.findByTestId('document-per-item-cards');

    fireEvent.click(screen.getByTestId('document-card-summarize_document-summarize'));

    await waitFor(() => {
      expect(captured.pendingOutboundMessage).toBe('Summarize this document');
    });

    // Mirrors the real SprkChat send path: it calls onDecorateOutboundBody(body) with the armed
    // message before dispatching the turn.
    const decorated = await captured.onDecorateOutboundBody!({ message: 'Summarize this document' });
    expect(decorated).not.toBeNull();
    expect(decorated!.documentId).toBe(DOCUMENT_ID);
    // ADR-015: no content field — the label/body is never attached, only the id.
    expect(Object.keys(decorated!)).not.toContain('label');
    expect(Object.keys(decorated!)).not.toContain('content');
    expect(Object.keys(decorated!)).not.toContain('textContent');
  });

  it('(b)(c)(d) Draft response: arms a distinct instruction + the same active document id', async () => {
    renderPane();
    await publishActiveDocument();
    await screen.findByTestId('document-per-item-cards');

    fireEvent.click(screen.getByTestId('document-card-draft_document_response-draft-response'));

    await waitFor(() => {
      expect(captured.pendingOutboundMessage).toBe('Draft a response');
    });
    const decorated = await captured.onDecorateOutboundBody!({ message: 'Draft a response' });
    expect(decorated!.documentId).toBe(DOCUMENT_ID);
  });

  it('(b)(c)(d) Draft memo: arms a distinct instruction + the same active document id', async () => {
    renderPane();
    await publishActiveDocument();
    await screen.findByTestId('document-per-item-cards');

    fireEvent.click(screen.getByTestId('document-card-draft_document_memo-draft-memo'));

    await waitFor(() => {
      expect(captured.pendingOutboundMessage).toBe('Draft a short internal summary for the file');
    });
    const decorated = await captured.onDecorateOutboundBody!({ message: 'Draft a short internal summary for the file' });
    expect(decorated!.documentId).toBe(DOCUMENT_ID);
  });

  it('the one-shot document handle is consumed exactly once — a SECOND decorate call (no new card click) does not re-stamp documentId', async () => {
    renderPane();
    await publishActiveDocument();
    await screen.findByTestId('document-per-item-cards');

    fireEvent.click(screen.getByTestId('document-card-summarize_document-summarize'));
    await waitFor(() => expect(captured.pendingOutboundMessage).toBe('Summarize this document'));

    const first = await captured.onDecorateOutboundBody!({ message: 'Summarize this document' });
    expect(first!.documentId).toBe(DOCUMENT_ID);

    // A normal, unrelated next turn — the armed ref was cleared by the first decorate call.
    const second = await captured.onDecorateOutboundBody!({ message: 'What else can you tell me?' });
    expect(second!.documentId).toBeUndefined();
  });

  it('(f) email per-item cards (task 025) still render — no regression from the additive document cards change', async () => {
    renderPane();
    await act(async () => {
      publishActiveItem({ id: 'comm-1', type: 'email', label: 'Quarterly filing update' });
      await Promise.resolve();
    });

    expect(await screen.findByTestId('email-per-item-cards')).toBeInTheDocument();
    expect(screen.queryByTestId('document-per-item-cards')).not.toBeInTheDocument();
  });

  it('renders correctly under the dark theme (ADR-021) with no console errors', async () => {
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    renderPane(webDarkTheme);
    await publishActiveDocument();

    expect(await screen.findByTestId('document-per-item-cards')).toBeInTheDocument();
    expect(errorSpy).not.toHaveBeenCalled();
    errorSpy.mockRestore();
  });
});
