/**
 * ConversationPane.proactive-suggest.e2e.test.tsx — task 022 (FR-B3/B5).
 *
 * Proves the proactive-suggestion TRIGGER: on a workspace tab's FIRST focus (the
 * `active_widget_changed` event carrying a `tabId` + `widgetContextType`), ConversationPane fires
 * exactly ONE `POST /api/ai/chat/sessions/{id}/suggest` and feeds the returned chips to the reactive
 * chip surface. NFR-02 once-per-tab: switching away and back NEVER re-fires (the LLM turn would
 * otherwise repeat on every tab switch — cost + chip spam); a DIFFERENT tab DOES fire again. A focus
 * event with no context type is skipped WITHOUT consuming the tab's one-shot.
 *
 * Harness mirrors `ConversationPane.active-context-decorate.e2e.test.tsx` (mocked `SprkChat` stub,
 * mocked `useAiSession` supplying `authenticatedFetch`/`bffBaseUrl`/`chatSessionId`, real
 * `PaneEventBus`) — the suggest call rides `authenticatedFetch`, captured here as a jest.fn.
 *
 * The server-side ≤3 cap + off-catalog-id drop are covered by AssistantSuggestionServiceTests (BFF).
 *
 * @see ../ConversationPane.tsx — `fireProactiveSuggestion` + the task-022 branch in the
 *      `usePaneEvent("workspace")` focus-stamp subscriber
 */
import '@testing-library/jest-dom';
import { TextEncoder as NodeTextEncoder, TextDecoder as NodeTextDecoder } from 'util';
// eslint-disable-next-line @typescript-eslint/no-explicit-any
if (typeof (global as any).TextEncoder === 'undefined') (global as any).TextEncoder = NodeTextEncoder;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
if (typeof (global as any).TextDecoder === 'undefined') (global as any).TextDecoder = NodeTextDecoder;
import React, { act } from 'react';
import { render } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';
import type { WorkspacePaneEvent } from '@spaarke/ai-widgets';
import type { ISprkChatProps } from '@spaarke/ui-components';
import { ComposeActionBridgeProvider } from '@spaarke/compose-components/context/composeActionBridge';

const BFF = 'https://test-bff.example.com';
const CHAT_SESSION = '00000000-0000-0000-0000-00000000a022';

const authenticatedFetchMock = jest.fn(async (url: string, _init?: RequestInit) => {
  if (typeof url === 'string' && url.includes('/suggest')) {
    return {
      ok: true,
      status: 200,
      json: async () => ({
        chips: [
          { targetBindingId: 'b1', label: 'Summarize this NDA' },
          { targetBindingId: 'b2', label: 'Extract the parties' },
        ],
      }),
    } as unknown as Response;
  }
  if (typeof url === 'string' && url.includes('/api/ai/capabilities')) {
    return { ok: true, status: 200, json: async () => ({ capabilities: [] }) } as unknown as Response;
  }
  return { ok: false, status: 404, json: async () => ({}), text: async () => '' } as unknown as Response;
});

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  return {
    ...actual,
    SprkChat: (_props: ISprkChatProps) => <div data-testid="sprkchat-stub" />,
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

function focusEvent(overrides: Record<string, unknown> = {}): WorkspacePaneEvent {
  return {
    type: 'active_widget_changed',
    widgetType: 'document-viewer',
    widgetContextType: 'document',
    tabId: 'tab-doc-1',
    displayName: 'NDA.pdf',
    widgetData: { fileName: 'NDA.pdf' },
    ...overrides,
  } as unknown as WorkspacePaneEvent;
}

async function publishFocus(bus: PaneEventBus, overrides: Record<string, unknown> = {}): Promise<void> {
  await act(async () => {
    bus.dispatch('workspace', focusEvent(overrides));
    for (let i = 0; i < 6; i++) await Promise.resolve();
  });
}

function suggestCalls(): unknown[] {
  return authenticatedFetchMock.mock.calls.filter(
    (c) => typeof c[0] === 'string' && (c[0] as string).includes('/suggest')
  );
}

beforeEach(() => {
  authenticatedFetchMock.mockClear();
});

describe('task 022 (FR-B3/B5): proactive suggestion trigger', () => {
  it('fires exactly one POST /suggest on a tab first focus, targeting the session + carrying contextType + activeContext', async () => {
    const bus = renderPane();

    await publishFocus(bus);

    const calls = suggestCalls();
    expect(calls).toHaveLength(1);

    const [url, init] = calls[0] as [string, RequestInit];
    expect(url).toBe(`${BFF}/api/ai/chat/sessions/${CHAT_SESSION}/suggest`);
    expect(init.method).toBe('POST');
    const body = JSON.parse(init.body as string);
    expect(body.contextType).toBe('document');
    expect(body.activeContext).toMatchObject({ tabId: 'tab-doc-1', contextType: 'document' });
  });

  it('does NOT re-fire when the same tab is focused again (NFR-02 once-per-tab; no re-fire on switch-back)', async () => {
    const bus = renderPane();

    await publishFocus(bus, { tabId: 'tab-doc-1' });
    await publishFocus(bus, { tabId: 'tab-other' }); // switch away
    await publishFocus(bus, { tabId: 'tab-doc-1' }); // ...and back — must NOT re-fire tab-doc-1

    const firedTabIds = suggestCalls().map((c) => JSON.parse((c as [string, RequestInit])[1].body as string).activeContext.tabId);
    // tab-doc-1 fired once, tab-other fired once — three focus events, two distinct tabs, two calls.
    expect(firedTabIds).toEqual(['tab-doc-1', 'tab-other']);
  });

  it('fires again for a DIFFERENT tabId', async () => {
    const bus = renderPane();

    await publishFocus(bus, { tabId: 'tab-doc-1' });
    await publishFocus(bus, { tabId: 'tab-doc-2' });

    expect(suggestCalls()).toHaveLength(2);
  });

  it('does NOT fire (and does not consume the tab one-shot) when the focus event carries no context type', async () => {
    const bus = renderPane();

    // First focus with no contextType — must be skipped.
    await publishFocus(bus, { tabId: 'tab-doc-9', widgetContextType: undefined });
    expect(suggestCalls()).toHaveLength(0);

    // A later focus of the SAME tab that DOES carry a contextType still fires once.
    await publishFocus(bus, { tabId: 'tab-doc-9', widgetContextType: 'document' });
    expect(suggestCalls()).toHaveLength(1);
  });
});
