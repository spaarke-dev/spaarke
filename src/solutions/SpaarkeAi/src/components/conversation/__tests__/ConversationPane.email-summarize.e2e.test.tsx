/**
 * ConversationPane.email-summarize.e2e.test.tsx — task 042c-fr-c4 (FR-C4).
 *
 * Proves the deterministic "Summarize this email" affordance + the one-shot
 * on-demand email-body handle (ADR-039 click affordance, no classifier;
 * ADR-015 full body on the invoked turn only):
 *
 *   1. On an EMAIL-context tab focus (with a non-null `emlDocumentId`), the
 *      deterministic "Summarize this email" chip renders (local-chip path),
 *      even though `/suggest` returns nothing.
 *   2. Clicking it arms SprkChat's one-shot host→send slot
 *      (`pendingOutboundMessage === "Summarize this email"`) AND stamps the
 *      email's `emlDocumentId` as EXACTLY the next outbound turn's `documentId`
 *      via `onDecorateOutboundBody`; the turn AFTER that carries NO documentId
 *      (one-shot cleared).
 *   3. A NORMAL turn on an active email tab (chip never clicked) attaches NO
 *      documentId — the full body never rides an ambient/every-turn payload.
 *   4. No chip when the email has no `.eml` archive (`emlDocumentId: null`).
 *   5. Re-broadcasting `active_widget_changed` for the SAME email tab (the
 *      companion focus-stamp fix, WorkspacePane task 042c-fr-c4) does NOT
 *      re-fire the proactive-suggest turn (once-per-tabId guarded).
 *   6. Dark-mode render (ADR-021).
 *
 * Harness mirrors `ConversationPane.reanalyze-chip.e2e.test.tsx` (mocked SprkChat
 * rendering `transcriptFooterSlot`; mocked `createConsumerDispatcher`; focus-stamp
 * event + `authenticatedFetch` mock).
 */
import '@testing-library/jest-dom';
import { TextEncoder as NodeTextEncoder, TextDecoder as NodeTextDecoder } from 'util';
// eslint-disable-next-line @typescript-eslint/no-explicit-any
if (typeof (global as any).TextEncoder === 'undefined') (global as any).TextEncoder = NodeTextEncoder;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
if (typeof (global as any).TextDecoder === 'undefined') (global as any).TextDecoder = NodeTextDecoder;
import React, { act } from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme, type Theme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';
import type { WorkspacePaneEvent } from '@spaarke/ai-widgets';
import type { ISprkChatProps } from '@spaarke/ui-components';
import { ComposeActionBridgeProvider } from '@spaarke/compose-components/context/composeActionBridge';

const BFF = 'https://test-bff.example.com';
const CHAT_SESSION = '00000000-0000-0000-0000-0000000042c4';
const EML_DOC_ID = 'eml-archive-doc-777';
const EMAIL_CHIP_TESTID = 'consumer-chip-local:email-summarize';

// Latest values of the additive host-send props, captured on every stub render.
const captured: {
  onDecorateOutboundBody?: (body: Record<string, unknown>) => Promise<Record<string, unknown> | null>;
  pendingOutboundMessage?: string | null;
} = {};

const dispatchConsumerSpy = jest.fn(
  () => Promise.resolve({ streamId: 'stream-test', status: 'complete' as const })
);

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  return {
    ...actual,
    SprkChat: (props: ISprkChatProps) => {
      const p = props as ISprkChatProps & {
        transcriptFooterSlot?: React.ReactNode;
        onDecorateOutboundBody?: (body: Record<string, unknown>) => Promise<Record<string, unknown> | null>;
        pendingOutboundMessage?: string | null;
      };
      captured.onDecorateOutboundBody = p.onDecorateOutboundBody;
      captured.pendingOutboundMessage = p.pendingOutboundMessage;
      return <div data-testid="sprkchat-stub">{p.transcriptFooterSlot}</div>;
    },
    createConsumerDispatcher: () => dispatchConsumerSpy,
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

function renderPane(theme: Theme = webLightTheme): PaneEventBus {
  const bus = new PaneEventBus();
  render(
    <FluentProvider theme={theme}>
      <PaneEventBusProvider bus={bus}>
        <ComposeActionBridgeProvider>
          <ConversationPane />
        </ComposeActionBridgeProvider>
      </PaneEventBusProvider>
    </FluentProvider>
  );
  return bus;
}

/** Publishes the real `active_widget_changed` shape for an email tab. */
async function publishEmailFocus(
  bus: PaneEventBus,
  overrides: Record<string, unknown> = {}
): Promise<void> {
  await act(async () => {
    bus.dispatch('workspace', {
      type: 'active_widget_changed',
      widgetType: 'email',
      widgetContextType: 'email',
      tabId: 'tab-email-1',
      displayName: 'Re: Master Services Agreement',
      widgetData: {
        kind: 'Email',
        emlDocumentId: EML_DOC_ID,
        subject: 'Re: Master Services Agreement',
        from: 'jane@example.com',
        date: '2026-08-06T09:00:00Z',
      },
      ...overrides,
    } as unknown as WorkspacePaneEvent);
    for (let i = 0; i < 10; i++) await Promise.resolve();
  });
}

beforeEach(() => {
  authenticatedFetchMock.mockClear();
  dispatchConsumerSpy.mockClear();
  captured.onDecorateOutboundBody = undefined;
  captured.pendingOutboundMessage = undefined;
  suggestCallCount = 0;
});

describe('task 042c-fr-c4 (FR-C4): summarize-this-email affordance + one-shot documentId', () => {
  it('renders the deterministic "Summarize this email" chip on an email tab with an emlDocumentId', async () => {
    const bus = renderPane();
    await publishEmailFocus(bus);

    const chip = await screen.findByTestId(EMAIL_CHIP_TESTID);
    expect(chip).toHaveTextContent('Summarize this email');
  });

  it('clicking the chip arms the host→send slot AND attaches documentId to EXACTLY the invoked turn', async () => {
    const bus = renderPane();
    await publishEmailFocus(bus);

    const chip = await screen.findByTestId(EMAIL_CHIP_TESTID);
    await act(async () => {
      fireEvent.click(chip);
      for (let i = 0; i < 6; i++) await Promise.resolve();
    });

    // The one-shot host→send slot is armed with the deterministic message.
    expect(captured.pendingOutboundMessage).toBe('Summarize this email');

    // The invoked turn (what SprkChat would send) carries documentId = emlDocumentId.
    let firstTurn: Record<string, unknown> | null = null;
    await act(async () => {
      firstTurn = await captured.onDecorateOutboundBody!({ message: 'Summarize this email' });
    });
    expect(firstTurn).not.toBeNull();
    expect(firstTurn!.documentId).toBe(EML_DOC_ID);

    // The NEXT turn carries NO documentId — one-shot handle was cleared.
    let secondTurn: Record<string, unknown> | null = null;
    await act(async () => {
      secondTurn = await captured.onDecorateOutboundBody!({ message: 'and who signed it?' });
    });
    expect(secondTurn).not.toBeNull();
    expect(secondTurn!).not.toHaveProperty('documentId');
  });

  it('a NORMAL turn on an active email tab (chip never clicked) attaches NO documentId', async () => {
    const bus = renderPane();
    await publishEmailFocus(bus);

    let turn: Record<string, unknown> | null = null;
    await act(async () => {
      turn = await captured.onDecorateOutboundBody!({ message: 'hello' });
    });
    expect(turn).not.toBeNull();
    expect(turn!).not.toHaveProperty('documentId');
    // The ambient compact stamp still rides (FR-A2) — only the full-body handle is withheld.
    expect(turn!.activeContext).toBeDefined();
  });

  it('does NOT render the chip when the email has no .eml archive (emlDocumentId null)', async () => {
    const bus = renderPane();
    await publishEmailFocus(bus, {
      widgetData: { kind: 'Email', emlDocumentId: null, subject: 'No archive yet', from: 'x@y.com', date: '2026-08-06T09:00:00Z' },
    });

    expect(screen.queryByTestId(EMAIL_CHIP_TESTID)).not.toBeInTheDocument();
  });

  it('a re-broadcast for the SAME email tab does NOT re-fire the proactive-suggest turn (companion focus-stamp fix is idempotent)', async () => {
    const bus = renderPane();
    await publishEmailFocus(bus);
    // First focus fired exactly one /suggest.
    expect(suggestCallCount).toBe(1);

    // WorkspacePane re-broadcasts active_widget_changed on an in-tab email change (new emlDocumentId).
    await publishEmailFocus(bus, {
      widgetData: {
        kind: 'Email',
        emlDocumentId: 'eml-archive-doc-999',
        subject: 'A different email in the same tab',
        from: 'bob@example.com',
        date: '2026-08-06T10:00:00Z',
      },
    });

    // once-per-tabId guard holds — no proactive-suggest re-fire.
    expect(suggestCallCount).toBe(1);
  });

  it('renders the chip under the dark theme without regression (ADR-021)', async () => {
    const bus = renderPane(webDarkTheme);
    await publishEmailFocus(bus);

    const chip = await screen.findByTestId(EMAIL_CHIP_TESTID);
    expect(chip).toBeInTheDocument();
    expect(chip).toHaveTextContent('Summarize this email');
  });
});
