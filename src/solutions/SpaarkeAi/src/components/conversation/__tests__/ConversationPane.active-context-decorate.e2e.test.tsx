/**
 * ConversationPane.active-context-decorate.e2e.test.tsx — task 011 (FR-A2).
 *
 * Proves the `activeContext` field is stamped onto the outbound chat body via the EXISTING
 * `onDecorateOutboundBody` seam (no SprkChat fork): once the workspace channel's
 * `active_widget_changed` event lands (task 010's `activeTabFocusRef` write), every subsequent
 * `handleDecorateOutboundBodyWithRevise` call carries `activeContext` matching the focused tab's
 * compact stamp — {@link ../activeTabFocusStamp.ts}'s `ActiveTabFocusStamp` shape verbatim, unmodified
 * by this task. Before any tab has been focused (the ref is still `null`), the field is OMITTED
 * entirely — never an empty/placeholder stamp.
 *
 * Harness mirrors `ConversationPane.revise-race.e2e.test.tsx`'s minimal decorate-seam capture
 * (mocked `SprkChat` exposing `onDecorateOutboundBody`, mocked `useAiSession` + `useShellStage`,
 * real `PaneEventBus`) — no network/dispatch surface needed since this task never leaves the
 * decorate seam.
 *
 * @see ../ConversationPane.tsx — `activeTabFocusRef` + the task-011 merge in
 *      `handleDecorateOutboundBodyWithRevise`
 * @see ../activeTabFocusStamp.ts — the reused (not redefined) `ActiveTabFocusStamp` shape
 * @see ./activeTabFocusStamp.test.ts — task 010's pure-function coverage for the derivation itself
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
const CHAT_SESSION = '00000000-0000-0000-0000-00000000a011';

const authenticatedFetchMock = jest.fn(async (url: string) => {
  if (url.includes('/api/ai/capabilities')) {
    return { ok: true, status: 200, json: async () => ({ capabilities: [] }) } as unknown as Response;
  }
  return { ok: false, status: 404, json: async () => ({}), text: async () => '' } as unknown as Response;
});

const captured: {
  onDecorateOutboundBody?: (body: Record<string, unknown>) => Promise<Record<string, unknown> | null>;
} = {};

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  return {
    ...actual,
    SprkChat: (props: ISprkChatProps) => {
      const p = props as ISprkChatProps & {
        onDecorateOutboundBody?: (body: Record<string, unknown>) => Promise<Record<string, unknown> | null>;
      };
      captured.onDecorateOutboundBody = p.onDecorateOutboundBody;
      return <div data-testid="sprkchat-stub" />;
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

/** The real dispatcher's `active_widget_changed` shape (`WorkspacePane.broadcastActiveTabChange`). */
function activeWidgetChangedEvent(overrides: Record<string, unknown> = {}): WorkspacePaneEvent {
  return {
    type: 'active_widget_changed',
    widgetType: 'email',
    tabId: 'tab-email-1',
    displayName: 'Re: Master Services Agreement',
    widgetData: { subject: 'Re: Master Services Agreement', from: 'jane@example.com' },
    ...overrides,
  } as unknown as WorkspacePaneEvent;
}

async function publishFocus(bus: PaneEventBus, overrides: Record<string, unknown> = {}): Promise<void> {
  await act(async () => {
    bus.dispatch('workspace', activeWidgetChangedEvent(overrides));
    for (let i = 0; i < 4; i++) await Promise.resolve();
  });
}

beforeEach(() => {
  authenticatedFetchMock.mockClear();
  captured.onDecorateOutboundBody = undefined;
});

describe('task 011 (FR-A2): outbound body activeContext via onDecorateOutboundBody', () => {
  it('omits activeContext entirely when no Workspace tab has been focused yet', async () => {
    renderPane();

    let result: Record<string, unknown> | null = null;
    await act(async () => {
      result = await captured.onDecorateOutboundBody!({ message: 'hello there' });
    });

    expect(result).not.toBeNull();
    expect(result).not.toHaveProperty('activeContext');
  });

  it('stamps activeContext matching the focused tab once active_widget_changed lands', async () => {
    const bus = renderPane();

    await publishFocus(bus);

    let result: Record<string, unknown> | null = null;
    await act(async () => {
      result = await captured.onDecorateOutboundBody!({ message: 'summarize this' });
    });

    expect(result).not.toBeNull();
    expect(result!.activeContext).toEqual({
      widgetType: 'email',
      contextType: undefined,
      tabId: 'tab-email-1',
      displayName: 'Re: Master Services Agreement',
      compactState: { subject: 'Re: Master Services Agreement', from: 'jane@example.com' },
    });
    // The base body fields survive untouched alongside the additive field.
    expect(result!.message).toBe('summarize this');
  });

  it('re-stamps to the NEWLY focused tab on a subsequent active_widget_changed (always the latest tab, never stale)', async () => {
    const bus = renderPane();

    await publishFocus(bus, {
      widgetType: 'email',
      tabId: 'tab-email-1',
      displayName: 'Re: Master Services Agreement',
      widgetData: { subject: 'Re: Master Services Agreement' },
    });
    await publishFocus(bus, {
      widgetType: 'document-viewer',
      tabId: 'tab-doc-2',
      displayName: 'NDA.pdf',
      widgetData: { fileName: 'NDA.pdf', pageCount: 3 },
    });

    let result: Record<string, unknown> | null = null;
    await act(async () => {
      result = await captured.onDecorateOutboundBody!({ message: 'what does this say' });
    });

    expect((result!.activeContext as { tabId: string }).tabId).toBe('tab-doc-2');
    expect((result!.activeContext as { widgetType: string }).widgetType).toBe('document-viewer');
  });
});
