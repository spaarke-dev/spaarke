/**
 * ConversationPane.refresh-suggestions.e2e.test.tsx — task 023 (FR-B4).
 *
 * Proves the manual "refresh suggestions" affordance: after the task-022 first-open turn has placed
 * chips on the strip, a "Refresh suggestions" control appears; clicking it RE-FIRES the grounded
 * suggestion turn for the ACTIVE tab (force=true, bypassing the once-per-tab guard) — the ONLY re-fire
 * path besides first-open (NFR-02). No automatic re-fire is introduced.
 *
 * Harness mirrors `ConversationPane.proactive-suggest.e2e.test.tsx`, except the `SprkChat` stub renders
 * `transcriptFooterSlot` (where the refresh control + chip strip live) so the button is in the DOM.
 */
import '@testing-library/jest-dom';
import { TextEncoder as NodeTextEncoder, TextDecoder as NodeTextDecoder } from 'util';
// eslint-disable-next-line @typescript-eslint/no-explicit-any
if (typeof (global as any).TextEncoder === 'undefined') (global as any).TextEncoder = NodeTextEncoder;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
if (typeof (global as any).TextDecoder === 'undefined') (global as any).TextDecoder = NodeTextDecoder;
import React, { act } from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';
import type { WorkspacePaneEvent } from '@spaarke/ai-widgets';
import type { ISprkChatProps } from '@spaarke/ui-components';
import { ComposeActionBridgeProvider } from '@spaarke/compose-components/context/composeActionBridge';

const BFF = 'https://test-bff.example.com';
const CHAT_SESSION = '00000000-0000-0000-0000-00000000a023';

const authenticatedFetchMock = jest.fn(async (url: string, _init?: RequestInit) => {
  if (typeof url === 'string' && url.includes('/suggest')) {
    return {
      ok: true,
      status: 200,
      json: async () => ({ chips: [{ targetBindingId: 'b1', label: 'Summarize this NDA' }] }),
    } as unknown as Response;
  }
  return { ok: false, status: 404, json: async () => ({}), text: async () => '' } as unknown as Response;
});

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  return {
    ...actual,
    // Render the transcript footer so the task-023 refresh control + chip strip are in the DOM.
    SprkChat: (props: ISprkChatProps) => {
      const p = props as ISprkChatProps & { transcriptFooterSlot?: React.ReactNode };
      return <div data-testid="sprkchat-stub">{p.transcriptFooterSlot}</div>;
    },
  };
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

function renderPane(): PaneEventBus {
  const bus = new PaneEventBus();
  render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>
        <ComposeActionBridgeProvider>
          <ConversationPane />
        </ComposeActionBridgeProvider>
      </PaneEventBusProvider>
    </FluentProvider>
  );
  return bus;
}

async function publishFocus(bus: PaneEventBus, tabId = 'tab-doc-1'): Promise<void> {
  await act(async () => {
    bus.dispatch('workspace', {
      type: 'active_widget_changed',
      widgetType: 'document-viewer',
      widgetContextType: 'document',
      tabId,
      displayName: 'NDA.pdf',
      widgetData: { fileName: 'NDA.pdf' },
    } as unknown as WorkspacePaneEvent);
    for (let i = 0; i < 8; i++) await Promise.resolve();
  });
}

function suggestCallCount(): number {
  return authenticatedFetchMock.mock.calls.filter(
    (c) => typeof c[0] === 'string' && (c[0] as string).includes('/suggest')
  ).length;
}

beforeEach(() => {
  authenticatedFetchMock.mockClear();
});

describe('task 023 (FR-B4): manual refresh-suggestions affordance', () => {
  it('shows a refresh control once chips are present and re-fires the grounded turn for the active tab (bypassing the once-per-tab guard)', async () => {
    const bus = renderPane();

    // First-open turn places chips (one /suggest call).
    await publishFocus(bus, 'tab-doc-1');
    expect(suggestCallCount()).toBe(1);

    // The refresh control appears now that the strip has chips.
    const refreshBtn = await screen.findByRole('button', { name: /refresh suggestions/i });

    // Clicking it re-fires the grounded turn for the active tab — the SECOND call for the SAME tab,
    // which the once-per-tab guard would otherwise have blocked.
    await act(async () => {
      fireEvent.click(refreshBtn);
      for (let i = 0; i < 8; i++) await Promise.resolve();
    });

    expect(suggestCallCount()).toBe(2);
    const suggestOnly = authenticatedFetchMock.mock.calls.filter(
      (c) => typeof c[0] === 'string' && (c[0] as string).includes('/suggest')
    );
    const lastCall = suggestOnly[suggestOnly.length - 1] as [string, RequestInit];
    expect(JSON.parse(lastCall[1].body as string).activeContext.tabId).toBe('tab-doc-1');
  });

  it('does not render the refresh control before any suggestions exist', async () => {
    renderPane();
    // No focus yet → no chips → no refresh control.
    expect(screen.queryByRole('button', { name: /refresh suggestions/i })).toBeNull();
  });
});
