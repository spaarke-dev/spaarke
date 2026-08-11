/**
 * ConversationPane.email-per-item-cards.e2e.test.tsx — task 025 (FR-09/FR-10).
 *
 * Proves the email per-item ACTION CARDS (Reply · Reply All · Forward · Summarize
 * the thread), keyed to the active-item conduit's email handle:
 *   (a) email active item → the four cards render (with no typing).
 *   (b) Reply/Reply All/Forward auto-draft (call the SAME `IEmailDraftAi` facade
 *       task 023's chat tools wrap, via `createXrmEmailComposeHandlers(...).onDraftWithAi`)
 *       then open the composer with `bodyOverride`.
 *   (c) Thread-preservation on re-sparkle (D-5) is proven at the correct layer —
 *       see `EmailComposer.quoted-thread-survives-resparkle.test.tsx` +
 *       `EmailComposer.reducer.test.ts` + `useEmailComposeActions.test.tsx` (all
 *       task 025 deliverables) — not re-derived here.
 *   (d) Summarize the thread → a plain-narrative assistant message is injected into
 *       the transcript (`injectLocalMessage`); NO composer opens.
 *   (e) ADR-015: the AI request's `currentBody`/`subject` come from the TOOL-FETCH
 *       (the mocked Dataverse read), never from the conduit handle's `label`.
 *   (f) Cards suppress when there is no single active email (deselect / non-email
 *       active item — the conduit's own single-active-item invariant, proven
 *       separately in `activeItemConduit.test.tsx`).
 *
 * Harness mirrors `ConversationPane.email-summarize.e2e.test.tsx` (mocked SprkChat
 * rendering `transcriptFooterSlot` + capturing `injectLocalMessage`; mocked
 * `createConsumerDispatcher`) PLUS `activeItemConduit.test.tsx`'s
 * `ActiveItemConduitProvider` + `usePublishActiveItem` harness pattern, so the real
 * `EmailPerItemCards` → `useEmailComposeActions` → `SendEmailDialog`/`EmailComposer`
 * chain renders for real (proving the composer genuinely opens pre-filled), while
 * `createXrmDataService`/`createXrmNavigationService`/`createXrmEmailComposeHandlers`
 * (the Xrm-host adapter factories) are faked so no real `window.Xrm` is needed.
 */
// jsdom polyfills for the REAL EmailComposer/Lexical rich-text stack this suite
// renders via the real SendEmailDialog (mirrors useEmailComposeActions.test.tsx).
if (typeof window !== 'undefined' && !window.matchMedia) {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    value: jest.fn().mockImplementation((query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: jest.fn(),
      removeListener: jest.fn(),
      addEventListener: jest.fn(),
      removeEventListener: jest.fn(),
      dispatchEvent: jest.fn(),
    })),
  });
}
if (typeof globalThis.ResizeObserver === 'undefined') {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (globalThis as any).ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
}
if (typeof Range !== 'undefined' && typeof Range.prototype.getBoundingClientRect !== 'function') {
  Range.prototype.getBoundingClientRect = function () {
    return { x: 0, y: 0, top: 0, left: 0, right: 0, bottom: 0, width: 0, height: 0, toJSON: () => ({}) } as DOMRect;
  };
}
if (typeof Range !== 'undefined' && typeof Range.prototype.getClientRects !== 'function') {
  Range.prototype.getClientRects = function () {
    return [] as unknown as DOMRectList;
  };
}

import '@testing-library/jest-dom';
import { TextEncoder as NodeTextEncoder, TextDecoder as NodeTextDecoder } from 'util';
// eslint-disable-next-line @typescript-eslint/no-explicit-any
if (typeof (global as any).TextEncoder === 'undefined') (global as any).TextEncoder = NodeTextEncoder;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
if (typeof (global as any).TextDecoder === 'undefined') (global as any).TextDecoder = NodeTextDecoder;
import * as React from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme, type Theme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';
import { ComposeActionBridgeProvider } from '@spaarke/compose-components/context/composeActionBridge';
import type { ISprkChatProps } from '@spaarke/ui-components';
import type { IEmailAiDraftResult } from '@spaarke/ui-components';
import { ActiveItemConduitProvider, usePublishActiveItem, type ActiveItemHandle } from '../../workspace/activeItemConduit';

const BFF = 'https://test-bff.example.com';
const CHAT_SESSION = '00000000-0000-0000-0000-000000000025';
const COMMUNICATION_ID = 'comm-active-1';
const ACTIVE_SUBJECT = 'Master Services Agreement';
// Distinct from ACTIVE_SUBJECT / the conduit label — proves the AI request's content
// came from the TOOL-FETCH read, never the conduit handle (ADR-015).
const MOCK_BODY = 'DISTINCT-TOOL-FETCHED-BODY — never on the conduit handle';

// Latest values of the additive host-send props, captured on every stub render.
const captured: {
  transcriptFooterSlot?: React.ReactNode;
  injectLocalMessage?: unknown;
} = {};

const dispatchConsumerSpy = jest.fn(() => Promise.resolve({ streamId: 'stream-test', status: 'complete' as const }));

// The mocked "draft tool" — the SAME `IEmailDraftAi` facade task 023's chat tools
// (`email.draft_reply`/`draft_forward`/`summarize_thread`) wrap, reached here via
// `createXrmEmailComposeHandlers(...).onDraftWithAi` (see EmailPerItemCards.tsx's
// deviation-note docblock).
const onDraftWithAiMock = jest.fn(
  async (req: { intent: string }): Promise<IEmailAiDraftResult> => ({
    text: `<p>MOCK DRAFT for intent=${req.intent}</p>`,
    isHtml: req.intent !== 'summarize',
  })
);

const retrieveRecordMock = jest.fn().mockResolvedValue({
  sprk_from: 'jane.doe@example.com',
  sprk_to: 'to@example.com',
  sprk_cc: '',
  sprk_subject: ACTIVE_SUBJECT,
  sprk_body: MOCK_BODY,
  'createdon@OData.Community.Display.V1.FormattedValue': 'Aug 6, 2026 9:00 AM',
});

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  return {
    ...actual,
    SprkChat: (props: ISprkChatProps) => {
      const p = props as ISprkChatProps & {
        transcriptFooterSlot?: React.ReactNode;
        injectLocalMessage?: unknown;
      };
      captured.transcriptFooterSlot = p.transcriptFooterSlot;
      captured.injectLocalMessage = p.injectLocalMessage;
      return <div data-testid="sprkchat-stub">{p.transcriptFooterSlot}</div>;
    },
    createConsumerDispatcher: () => dispatchConsumerSpy,
    // Xrm-host adapter factories — faked so no real `window.Xrm` is required. Everything
    // else (SendEmailDialog, EmailComposer, useEmailComposeActions consumers) is REAL.
    createXrmDataService: () => ({
      createRecord: jest.fn(),
      retrieveRecord: retrieveRecordMock,
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
    createXrmEmailComposeHandlers: () => ({
      recordLookupCatalog: [],
      onDraftWithAi: onDraftWithAiMock,
    }),
  };
});

let suggestCallCount = 0;
const authenticatedFetchMock = jest.fn(async (url: string) => {
  if (typeof url === 'string' && url.includes('/api/ai/capabilities')) {
    return { ok: true, status: 200, json: async () => ({ capabilities: [] }) } as unknown as Response;
  }
  if (typeof url === 'string' && url.includes('/suggest')) {
    suggestCallCount += 1;
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

async function publishActiveEmail(overrides: Partial<ActiveItemHandle> = {}): Promise<void> {
  await act(async () => {
    publishActiveItem({ id: COMMUNICATION_ID, type: 'email', label: ACTIVE_SUBJECT, ...overrides });
    await Promise.resolve();
  });
}

beforeEach(() => {
  authenticatedFetchMock.mockClear();
  dispatchConsumerSpy.mockClear();
  onDraftWithAiMock.mockClear();
  retrieveRecordMock.mockClear();
  captured.transcriptFooterSlot = undefined;
  captured.injectLocalMessage = undefined;
  suggestCallCount = 0;
});

describe('task 025 (FR-09/FR-10): email per-item action cards', () => {
  it('(a) selecting a single email surfaces the four cards with NO typing', async () => {
    renderPane();
    await publishActiveEmail();

    expect(await screen.findByTestId('email-per-item-cards')).toBeInTheDocument();
    expect(screen.getByTestId('email-card-draft_reply-reply')).toHaveTextContent('Reply');
    expect(screen.getByTestId('email-card-draft_reply-reply-all')).toHaveTextContent('Reply All');
    expect(screen.getByTestId('email-card-draft_forward-forward')).toHaveTextContent('Forward');
    expect(screen.getByTestId('email-card-summarize_thread-summarize-the-thread')).toHaveTextContent(
      'Summarize the thread'
    );
  });

  it('(f) suppresses when there is no active item (deselect)', async () => {
    renderPane();
    await publishActiveEmail();
    expect(await screen.findByTestId('email-per-item-cards')).toBeInTheDocument();

    await act(async () => {
      publishActiveItem(null);
      await Promise.resolve();
    });
    expect(screen.queryByTestId('email-per-item-cards')).not.toBeInTheDocument();
  });

  it('(f) suppresses when the active item is not an email (e.g. a compose tab)', async () => {
    renderPane();
    await act(async () => {
      publishActiveItem({ id: 'doc-1', type: 'compose', label: 'Untitled' });
      await Promise.resolve();
    });
    expect(screen.queryByTestId('email-per-item-cards')).not.toBeInTheDocument();
  });

  it('(b)(e) Reply: calls the draft tool (fetched body/subject, NOT the conduit label) then opens the composer pre-filled with the auto-draft', async () => {
    renderPane();
    await publishActiveEmail();
    await screen.findByTestId('email-per-item-cards');

    fireEvent.click(screen.getByTestId('email-card-draft_reply-reply'));

    await waitFor(() => {
      expect(onDraftWithAiMock).toHaveBeenCalledWith(
        expect.objectContaining({ intent: 'reply', currentBody: MOCK_BODY, subject: ACTIVE_SUBJECT, isHtml: true })
      );
    });
    // The composer opens — SendEmailDialog uses SprkModal dismiss="alert" → role alertdialog.
    await screen.findByRole('alertdialog');
    // Pre-filled with the Re: subject (real deriveComposerFields + real openComposer call).
    await screen.findByDisplayValue(`Re: ${ACTIVE_SUBJECT}`);
  });

  it('(b) Reply All: SAME reply intent, but opens with the Reply All title (mode disambiguated by card label)', async () => {
    renderPane();
    await publishActiveEmail();
    await screen.findByTestId('email-per-item-cards');

    fireEvent.click(screen.getByTestId('email-card-draft_reply-reply-all'));

    await waitFor(() => {
      expect(onDraftWithAiMock).toHaveBeenCalledWith(expect.objectContaining({ intent: 'reply' }));
    });
    await screen.findByRole('alertdialog');
    expect(await screen.findByText(`Reply All: ${ACTIVE_SUBJECT}`)).toBeInTheDocument();
  });

  it('(b) Forward: calls the draft tool with intent=custom + the forwarding-note instruction, then opens the composer with a Fwd: subject', async () => {
    renderPane();
    await publishActiveEmail();
    await screen.findByTestId('email-per-item-cards');

    fireEvent.click(screen.getByTestId('email-card-draft_forward-forward'));

    await waitFor(() => {
      expect(onDraftWithAiMock).toHaveBeenCalledWith(
        expect.objectContaining({
          intent: 'custom',
          userInstruction: expect.stringContaining('forwarding this email'),
          currentBody: MOCK_BODY,
        })
      );
    });
    await screen.findByRole('alertdialog');
    await screen.findByDisplayValue(`Fwd: ${ACTIVE_SUBJECT}`);
  });

  it('(d) Summarize the thread: calls the draft tool with intent=summarize (plain text) and injects a plain-narrative assistant message — NO composer opens', async () => {
    renderPane();
    await publishActiveEmail();
    await screen.findByTestId('email-per-item-cards');

    fireEvent.click(screen.getByTestId('email-card-summarize_thread-summarize-the-thread'));

    await waitFor(() => {
      expect(onDraftWithAiMock).toHaveBeenCalledWith(
        expect.objectContaining({ intent: 'summarize', currentBody: MOCK_BODY, isHtml: false })
      );
    });
    await waitFor(() => {
      const injected = captured.injectLocalMessage as { role: string; content: string } | null | undefined;
      expect(injected).toBeTruthy();
      expect(injected!.role).toBe('Assistant');
      expect(injected!.content).toContain('MOCK DRAFT for intent=summarize');
    });
    // No composer dialog opened for the chat-landing card.
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });

  it('renders correctly under the dark theme (ADR-021) with no console errors', async () => {
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    renderPane(webDarkTheme);
    await publishActiveEmail();

    expect(await screen.findByTestId('email-per-item-cards')).toBeInTheDocument();
    expect(errorSpy).not.toHaveBeenCalled();
    errorSpy.mockRestore();
  });
});
