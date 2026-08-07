/**
 * WorkspacePane.email-focus-restamp.test.tsx — task 042c-fr-c4 (FR-C4) companion fix.
 *
 * `WorkspaceTabManager.updateTab` fires `_notifyPersistChange` but NOT
 * `_notifyActiveTabChange`, so browsing emails WITHIN the active email tab (each
 * pick re-fires `widget_update` with a fresh `emlDocumentId`) left the
 * ConversationPane focus stamp stale — the "Summarize this email" chip would
 * target the previously-viewed email. WorkspacePane's `widget_update` branch now
 * re-broadcasts `active_widget_changed` (the EXISTING ADR-030 event) when the
 * updated tab IS the active tab AND its data is an Email payload.
 *
 * This asserts:
 *   - a `widget_update` on the ACTIVE email tab re-broadcasts `active_widget_changed`
 *     carrying the UPDATED emlDocumentId;
 *   - a `widget_update` whose payload is NOT an Email kind does NOT re-broadcast.
 *
 * Test category per ADR-038: Component Test (KEEP — WorkspacePane cross-pane
 * broadcast contract observable by the ConversationPane subscriber).
 */
import '@testing-library/jest-dom';
import * as React from 'react';
import { render, act, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';
import type { WorkspacePaneEvent } from '@spaarke/ai-widgets';
import { ComposeActionBridgeProvider } from '@spaarke/compose-components/context/composeActionBridge';

const authenticatedFetchMock = jest.fn(async (url: string, init?: RequestInit): Promise<Response> => {
  const method = init?.method ?? 'GET';
  if (method === 'GET' && url.includes('/tabs')) {
    return { ok: true, status: 200, json: async () => ({ tabs: [], activeTabId: null }) } as Partial<Response> as Response;
  }
  return { ok: true, status: 200, json: async () => ({ acknowledged: true }) } as Partial<Response> as Response;
});

jest.mock('@spaarke/ai-widgets', () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const actual = jest.requireActual('@spaarke/ai-widgets') as any;
  const makeStub = (widgetType: string): React.FC<{ data?: unknown }> =>
    function StubWidget(): React.JSX.Element {
      return <div data-testid={`widget-stub-${widgetType}`}>{widgetType}</div>;
    };
  return {
    ...actual,
    useAiSession: () => ({
      isAuthenticated: true,
      authenticatedFetch: authenticatedFetchMock,
      getAccessToken: jest.fn().mockResolvedValue('test-token'),
      bffBaseUrl: 'https://test-bff.example.com',
      tenantId: 'test-tenant',
      chatSessionId: 'session-email-restamp',
      setChatSessionId: jest.fn(),
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
    resolveWorkspaceWidget: jest.fn(async (widgetType: string) => makeStub(widgetType)),
    getWorkspaceWidgetMetadata: jest.fn((widgetType: string) => ({
      displayName: widgetType === 'email' ? 'Email' : 'Workspace',
      category: 'workspace',
      defaultOrder: 100,
      allowMultiple: true,
      contextType: widgetType === 'email' ? 'email' : undefined,
    })),
  };
});

jest.mock('../../../hooks/useWorkspaceLayouts', () => ({
  useWorkspaceLayouts: () => ({
    layouts: [],
    activeLayout: null,
    isLoading: false,
    refetch: jest.fn(),
    setActiveLayoutById: jest.fn(),
  }),
}));

jest.mock('../../../services/pinnedWorkspaces', () => ({
  getPinnedWorkspaces: jest.fn(() => []),
  prunePinnedToKnown: jest.fn(() => []),
  isPinned: jest.fn(() => false),
  pinWorkspace: jest.fn(),
  unpinWorkspace: jest.fn(),
  setPinnedWorkspacesOrder: jest.fn(),
  moveWorkspaceToTop: jest.fn(),
}));

jest.mock('@spaarke/ui-components', () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const actual = jest.requireActual('@spaarke/ui-components') as any;
  return {
    ...actual,
    PaneHeader: ({ title, rightSlot }: { title: string; rightSlot?: React.ReactNode }) => (
      <div data-testid="pane-header">
        <span>{title}</span>
        {rightSlot}
      </div>
    ),
  };
});

// Import AFTER mocks.
import { WorkspacePane } from '../WorkspacePane';

interface ActiveWidgetChanged {
  type: 'active_widget_changed';
  tabId?: string;
  widgetType?: string;
  widgetData?: { kind?: string; emlDocumentId?: string | null };
}

function isActiveWidgetChanged(e: WorkspacePaneEvent): e is WorkspacePaneEvent & ActiveWidgetChanged {
  return (e as { type?: string }).type === 'active_widget_changed';
}

function renderPane(): { bus: PaneEventBus; activeChanges: ActiveWidgetChanged[] } {
  const bus = new PaneEventBus();
  const activeChanges: ActiveWidgetChanged[] = [];
  bus.subscribe('workspace', (e) => {
    if (isActiveWidgetChanged(e)) activeChanges.push(e as unknown as ActiveWidgetChanged);
  });
  render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>
        <ComposeActionBridgeProvider>
          <WorkspacePane />
        </ComposeActionBridgeProvider>
      </PaneEventBusProvider>
    </FluentProvider>,
  );
  return { bus, activeChanges };
}

function emailOpen(emlDocumentId: string) {
  return {
    type: 'widget_load' as const,
    widgetType: 'email',
    widgetData: { kind: 'Email', emlDocumentId, subject: 'First email', from: 'a@x.com', date: '2026-08-06T09:00:00Z' },
    displayName: 'Inbox',
  };
}

async function flush(): Promise<void> {
  await act(async () => {
    for (let i = 0; i < 12; i++) await Promise.resolve();
  });
}

beforeEach(() => {
  authenticatedFetchMock.mockClear();
  if (!Element.prototype.scrollIntoView) {
    // eslint-disable-next-line @typescript-eslint/no-empty-function
    Element.prototype.scrollIntoView = function (): void {};
  }
});

describe('WorkspacePane — email focus-stamp re-broadcast on in-tab email change (task 042c-fr-c4)', () => {
  it('re-broadcasts active_widget_changed with the UPDATED emlDocumentId when the active email tab changes', async () => {
    const { bus, activeChanges } = renderPane();

    await act(async () => {
      bus.dispatch('workspace', emailOpen('eml-v1') as unknown as WorkspacePaneEvent);
    });
    await flush();

    // The tab open auto-activated → an initial active_widget_changed carrying the new tabId.
    await waitFor(() => expect(activeChanges.length).toBeGreaterThanOrEqual(1));
    const opened = activeChanges[activeChanges.length - 1];
    expect(opened.widgetData?.emlDocumentId).toBe('eml-v1');
    const tabId = opened.tabId!;
    expect(tabId).toBeTruthy();

    const countBefore = activeChanges.length;

    // Browse to a different email WITHIN the same active tab (widget_update with a fresh emlDocumentId).
    await act(async () => {
      bus.dispatch('workspace', {
        type: 'widget_update',
        tabId,
        widgetData: { kind: 'Email', emlDocumentId: 'eml-v2', subject: 'Second email', from: 'b@x.com', date: '2026-08-06T10:00:00Z' },
      } as unknown as WorkspacePaneEvent);
    });
    await flush();

    // A NEW active_widget_changed fired, carrying the UPDATED emlDocumentId.
    expect(activeChanges.length).toBeGreaterThan(countBefore);
    const restamped = activeChanges[activeChanges.length - 1];
    expect(restamped.tabId).toBe(tabId);
    expect(restamped.widgetData?.emlDocumentId).toBe('eml-v2');
  });

  it('does NOT re-broadcast for a widget_update whose payload is not an Email kind', async () => {
    const { bus, activeChanges } = renderPane();

    await act(async () => {
      bus.dispatch('workspace', emailOpen('eml-v1') as unknown as WorkspacePaneEvent);
    });
    await flush();
    await waitFor(() => expect(activeChanges.length).toBeGreaterThanOrEqual(1));
    const tabId = activeChanges[activeChanges.length - 1].tabId!;
    const countBefore = activeChanges.length;

    // A non-Email widget_update on the same tab must NOT re-broadcast active_widget_changed.
    await act(async () => {
      bus.dispatch('workspace', {
        type: 'widget_update',
        tabId,
        widgetData: { kind: 'Summary', body: 'not an email' },
      } as unknown as WorkspacePaneEvent);
    });
    await flush();

    expect(activeChanges.length).toBe(countBefore);
  });
});
