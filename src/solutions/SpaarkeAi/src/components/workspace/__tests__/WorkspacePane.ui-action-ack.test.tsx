/**
 * WorkspacePane — D-F3 UI-action truthfulness client-ack tests
 * (FR-A1-08 / NFR-08 / task AIR2-037).
 *
 * A server-initiated `workspace_open_tab` frame (bridged by useContextEventBridge into a
 * `workspace.widget_load` event carrying `frameId`) is a UI-affecting tool call WAITING
 * for a client ack referencing that exact frame id before its tool result can complete
 * truthfully (see SendWorkspaceArtifactHandler.ExecuteOpenWorkspaceTabAsync +
 * IUiActionAckCoordinator on the server side). These tests assert:
 *
 *   (1) once the tab is actually materialized (manager.addTab), WorkspacePane POSTs the
 *       ack to /ai/chat/sessions/{sessionId}/ack referencing the SAME frameId;
 *   (2) a client-originated widget_load (no frameId — e.g. the Workspaces menu path)
 *       never fires an ack POST (negative guard: no unnecessary/incorrect acks).
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';

interface RecordedFetch {
  url: string;
  method: string;
  body: unknown;
}

const recordedFetches: RecordedFetch[] = [];

const authenticatedFetchMock = jest.fn(
  async (url: string, init?: RequestInit): Promise<Response> => {
    const method = init?.method ?? 'GET';
    recordedFetches.push({
      url,
      method,
      body: init?.body ? JSON.parse(String(init.body)) : undefined,
    });
    if (method === 'GET' && url.includes('/tabs')) {
      // No persisted tabs — restore resolves empty immediately.
      return {
        ok: true,
        status: 200,
        json: async () => ({ tabs: [], activeTabId: null }),
      } as Partial<Response> as Response;
    }
    return {
      ok: true,
      status: 200,
      json: async () => ({ acknowledged: true }),
    } as Partial<Response> as Response;
  },
);

jest.mock('@spaarke/ai-widgets', () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const actual = jest.requireActual('@spaarke/ai-widgets') as any;

  const makeStub = (widgetType: string): React.FC<{ data?: unknown }> => {
    return function StubWidget(): React.JSX.Element {
      return <div data-testid={`widget-stub-${widgetType}`}>{widgetType}</div>;
    };
  };

  return {
    ...actual,
    useAiSession: () => ({
      isAuthenticated: true,
      authenticatedFetch: authenticatedFetchMock,
      getAccessToken: jest.fn().mockResolvedValue('test-token'),
      bffBaseUrl: 'https://test-bff.example.com',
      tenantId: 'test-tenant',
      chatSessionId: 'session-ui-ack',
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
    getWorkspaceWidgetMetadata: jest.fn(() => ({
      displayName: 'Workspace',
      category: 'workspace',
      defaultOrder: 100,
      allowMultiple: true,
    })),
  };
});

// No default layout available — keeps this test scoped to the explicit dispatch below
// (avoids an unrelated auto-installed default tab racing the assertions).
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

// Import AFTER mocks so module resolution picks them up.
import { WorkspacePane } from '../WorkspacePane';

function renderPane(): { bus: PaneEventBus } {
  const bus = new PaneEventBus();
  render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>
        <WorkspacePane />
      </PaneEventBusProvider>
    </FluentProvider>,
  );
  return { bus };
}

beforeEach(() => {
  recordedFetches.length = 0;
  authenticatedFetchMock.mockClear();
  if (!Element.prototype.scrollIntoView) {
    // eslint-disable-next-line @typescript-eslint/no-empty-function
    Element.prototype.scrollIntoView = function (): void {};
  }
});

describe('WorkspacePane — D-F3 UI-action client-ack (task AIR2-037)', () => {
  it('POSTs an ack referencing the frame id once the server-initiated tab is materialized', async () => {
    const { bus } = renderPane();

    bus.dispatch('workspace', {
      type: 'widget_load',
      widgetType: 'workspace',
      widgetData: { layoutId: 'layout-compose', layoutName: 'Compose' },
      displayName: 'Compose',
      frameId: 'server-frame-abc123',
    });

    await screen.findByRole('tab', { name: /compose/i });

    await waitFor(() => {
      const ackCall = recordedFetches.find(f => f.url.includes('/ack'));
      expect(ackCall).toBeDefined();
    });

    const ackCall = recordedFetches.find(f => f.url.includes('/ack'))!;
    expect(ackCall.method).toBe('POST');
    expect(ackCall.url).toContain('/ai/chat/sessions/session-ui-ack/ack');
    expect(ackCall.body).toEqual({ frameId: 'server-frame-abc123' });
  });

  it('does NOT POST an ack for a client-originated widget_load without a frameId', async () => {
    const { bus } = renderPane();

    // Client-originated open (e.g. the Workspaces menu path) — no frameId, because no
    // server-side tool call is waiting on an ack for it.
    bus.dispatch('workspace', {
      type: 'widget_load',
      widgetType: 'workspace',
      widgetData: { layoutId: 'layout-compose', layoutName: 'Compose' },
      displayName: 'Compose',
    });

    await screen.findByRole('tab', { name: /compose/i });

    // Give any (incorrect) ack POST a chance to fire before asserting its absence.
    await new Promise(resolve => setTimeout(resolve, 50));

    expect(recordedFetches.some(f => f.url.includes('/ack'))).toBe(false);
  });
});
