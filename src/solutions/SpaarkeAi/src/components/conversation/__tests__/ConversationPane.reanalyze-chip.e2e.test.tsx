/**
 * ConversationPane.reanalyze-chip.e2e.test.tsx — task 038 (FR-D11).
 *
 * Proves the "Reanalyze" follow-on chip: on a `document`-context tab focus, ConversationPane
 * deterministically resolves the task-021-seeded Reanalyze Binding (consumerType `chat-summarize`,
 * consumerCode `reanalyze`) via the EXISTING capability-discovery seam and pushes it onto the
 * REACTIVE chip surface (`useConsumerChips`) — independent of whether the probabilistic
 * proactive-suggest turn (022) happens to select it. Clicking the chip dispatches the Reanalyze
 * Binding through the SAME shared `dispatchConsumer(bindingId, args)` Click path every other
 * consumer chip uses (ADR-039) — no new dispatch endpoint, no new decider.
 *
 * Harness mirrors `ConversationPane.consumer-chips.test.tsx` (mocked `createConsumerDispatcher`
 * spy — avoids hand-rolling an SSE stream) + `ConversationPane.proactive-suggest.e2e.test.tsx`
 * (focus-stamp event harness + `authenticatedFetch` mock for `/api/ai/capabilities` + `/suggest`).
 *
 * @see ../ConversationPane.tsx — `reanalyzeBindingId` memo, the task-038 branch in the
 *      `usePaneEvent("workspace")` focus-stamp subscriber, the catch-up effect, and
 *      `getAppendedLocalChips`'s Reanalyze inclusion.
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
const CHAT_SESSION = '00000000-0000-0000-0000-00000000a038';
const REANALYZE_BINDING_ID = '9c29b488-4291-f111-b8db-7ced8ddc4a05';

// ---------------------------------------------------------------------------
// Mock SprkChat (renders transcriptFooterSlot, where the chip strip lives) +
// createConsumerDispatcher (dispatch spy — avoids hand-rolling an SSE stream).
// ---------------------------------------------------------------------------

const dispatchConsumerSpy = jest.fn(
  () => Promise.resolve({ streamId: 'stream-test', status: 'complete' as const })
);

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  return {
    ...actual,
    SprkChat: (props: ISprkChatProps) => {
      const p = props as ISprkChatProps & { transcriptFooterSlot?: React.ReactNode };
      return <div data-testid="sprkchat-stub">{p.transcriptFooterSlot}</div>;
    },
    createConsumerDispatcher: () => dispatchConsumerSpy,
  };
});

// ---------------------------------------------------------------------------
// authenticatedFetch mock — /api/ai/capabilities returns the task-021-seeded
// Reanalyze Binding (chat-summarize / reanalyze); /suggest returns ZERO
// suggestions so the test proves the DETERMINISTIC push, not the probabilistic
// proactive-suggest turn (022).
// ---------------------------------------------------------------------------

const authenticatedFetchMock = jest.fn(async (url: string) => {
  if (typeof url === 'string' && url.includes('/api/ai/capabilities')) {
    return {
      ok: true,
      status: 200,
      json: async () => ({
        capabilities: [
          {
            bindingId: REANALYZE_BINDING_ID,
            consumerType: 'chat-summarize',
            consumerCode: 'reanalyze',
            displayLabel: 'Reanalyze the loaded document',
            surfaces: [],
            launchArgsSchemaJson: null,
          },
          // A decoy — SAME consumerType, DIFFERENT consumerCode — proves the resolution
          // disambiguates by consumerCode rather than grabbing the first chat-summarize match.
          {
            bindingId: 'decoy-chat-summarize-binding',
            consumerType: 'chat-summarize',
            consumerCode: 'default',
            displayLabel: 'Chat Summarize',
            surfaces: [],
            launchArgsSchemaJson: null,
          },
        ],
      }),
    } as unknown as Response;
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

async function publishFocus(bus: PaneEventBus, overrides: Record<string, unknown> = {}): Promise<void> {
  await act(async () => {
    bus.dispatch('workspace', {
      type: 'active_widget_changed',
      widgetType: 'document-viewer',
      widgetContextType: 'document',
      tabId: 'tab-doc-1',
      displayName: 'NDA.pdf',
      widgetData: { fileName: 'NDA.pdf' },
      ...overrides,
    } as unknown as WorkspacePaneEvent);
    for (let i = 0; i < 10; i++) await Promise.resolve();
  });
}

beforeEach(() => {
  authenticatedFetchMock.mockClear();
  dispatchConsumerSpy.mockClear();
});

describe('task 038 (FR-D11): Reanalyze follow-on chip on a document context', () => {
  it('appears deterministically for a loaded document, even when the proactive-suggest turn (022) selects nothing', async () => {
    const bus = renderPane();

    await publishFocus(bus);

    const chip = await screen.findByTestId(`consumer-chip-${REANALYZE_BINDING_ID}`);
    expect(chip).toHaveTextContent('Reanalyze');
    expect(chip).toHaveAttribute('data-binding-id', REANALYZE_BINDING_ID);

    // Resolved via capability discovery (consumerType + consumerCode), never hardcoded/invented,
    // and disambiguated from the decoy chat-summarize Binding.
    expect(
      authenticatedFetchMock.mock.calls.some(
        (c) => typeof c[0] === 'string' && (c[0] as string).includes('/api/ai/capabilities')
      )
    ).toBe(true);
    expect(screen.queryByTestId('consumer-chip-decoy-chat-summarize-binding')).not.toBeInTheDocument();
  });

  it('does NOT appear for a non-document context tab', async () => {
    const bus = renderPane();

    await publishFocus(bus, { widgetContextType: 'matter-grid' });

    expect(screen.queryByTestId(`consumer-chip-${REANALYZE_BINDING_ID}`)).not.toBeInTheDocument();
  });

  it('click dispatches the Reanalyze Binding through the existing shared Click path (dispatchConsumer) — no new endpoint', async () => {
    const bus = renderPane();
    await publishFocus(bus);

    const chip = await screen.findByTestId(`consumer-chip-${REANALYZE_BINDING_ID}`);

    await act(async () => {
      fireEvent.click(chip);
      for (let i = 0; i < 6; i++) await Promise.resolve();
    });

    expect(dispatchConsumerSpy).toHaveBeenCalledTimes(1);
    const [bindingId] = dispatchConsumerSpy.mock.calls[0] as unknown as [string, unknown];
    expect(bindingId).toBe(REANALYZE_BINDING_ID);
  });

  it('renders under the dark theme without regression (ADR-021)', async () => {
    const bus = renderPane(webDarkTheme);
    await publishFocus(bus);

    const chip = await screen.findByTestId(`consumer-chip-${REANALYZE_BINDING_ID}`);
    expect(chip).toBeInTheDocument();
    expect(chip).toHaveTextContent('Reanalyze');
  });

  it('does NOT re-seed the chip while a dispatch is in flight (the strip is deliberately empty mid-dispatch)', async () => {
    const bus = renderPane();
    await publishFocus(bus);

    const chip = await screen.findByTestId(`consumer-chip-${REANALYZE_BINDING_ID}`);

    // Hold the dispatch pending (never resolves in this test) so the strip stays in the
    // "consumed on click" empty window that `runBindingDispatch` synchronously creates.
    dispatchConsumerSpy.mockImplementationOnce(() => new Promise(() => undefined));

    await act(async () => {
      fireEvent.click(chip);
      for (let i = 0; i < 6; i++) await Promise.resolve();
    });

    // The strip must stay empty while the dispatch is in flight — neither the workspace focus
    // handler nor the reanalyzeBindingId catch-up effect may re-seed Reanalyze during this window.
    expect(screen.queryByTestId('consumer-chips')).not.toBeInTheDocument();

    // A refocus of the SAME document tab while the dispatch is still pending must also NOT re-seed.
    await publishFocus(bus, { tabId: 'tab-doc-1' });
    expect(screen.queryByTestId('consumer-chips')).not.toBeInTheDocument();
  });
});
