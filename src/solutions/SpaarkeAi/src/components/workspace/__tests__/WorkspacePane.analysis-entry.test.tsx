/**
 * WorkspacePane — Analysis entry-matrix host routing (task 050, spec §12 / FR-14).
 *
 * Proves the four-case host mapping WorkspacePane owns:
 *   - 2a/2b (analysisLaunch.mode='new')      → auto-installs the 'analysis-hub' tab
 *                                               (the hub renders the Create-new cards;
 *                                               regarding pre-set flows via entityContext).
 *   - 2c/2d (analysisLaunch.mode='existing') → resolves the bound session via the
 *                                               task-031 by-analysis endpoint and dispatches
 *                                               conversation.session_switch (no hub cards).
 *   - Thread 1: a 'create-analysis-wizard' widget_load is enriched with the host-coupled
 *     Dataverse services (dataService/navigationService/searchUsers/authenticatedFetch/
 *     bffBaseUrl), preserving the dispatcher's own widgetData (workTypeValue).
 *
 * Harness derived from WorkspacePane.email-stub.test.tsx (null activeLayout → no BFF
 * default auto-install). `useAnalysisLaunch` is a mutable mock so each test sets the mode.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { act } from 'react-dom/test-utils';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';

const authenticatedFetchMock = jest.fn(async (url: string, init?: RequestInit): Promise<Response> => {
  const method = init?.method ?? 'GET';
  if (method === 'GET' && url.includes('/by-analysis/')) {
    return { ok: true, status: 200, json: async () => ({ sessionId: 'sess-existing-1', messageCount: 3, isArchived: false, createdOn: null }) } as Partial<Response> as Response;
  }
  if (method === 'GET' && url.includes('/tabs')) {
    return { ok: true, status: 200, json: async () => ({ tabs: [], activeTabId: null }) } as Partial<Response> as Response;
  }
  if (method === 'PATCH' && url.includes('/tabs')) {
    return { ok: true, status: 204, json: async () => ({}) } as Partial<Response> as Response;
  }
  return { ok: false, status: 404, json: async () => ({}) } as Partial<Response> as Response;
});

jest.mock('@spaarke/ai-widgets', () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const actual = jest.requireActual('@spaarke/ai-widgets') as any;
  const makeStub =
    (widgetType: string): React.FC<{ data?: Record<string, unknown> }> =>
    function StubWidget({ data }): React.JSX.Element {
      const d = data ?? {};
      return (
        <div
          data-testid={`stub-${widgetType}`}
          data-has-dataservice={String(!!d.dataService)}
          data-has-navservice={String(!!d.navigationService)}
          data-has-authfetch={String(!!d.authenticatedFetch)}
          data-has-searchusers={String(!!d.searchUsers)}
          data-worktype={String(d.workTypeValue ?? '')}
        />
      );
    };
  return {
    ...actual,
    useAiSession: () => ({
      isAuthenticated: true,
      authenticatedFetch: authenticatedFetchMock,
      getAccessToken: jest.fn().mockResolvedValue('test-token'),
      bffBaseUrl: 'https://test-bff.example.com',
      tenantId: 'test-tenant',
      chatSessionId: 'session-analysis',
      setChatSessionId: jest.fn(),
      playbookId: undefined,
      setPlaybookId: jest.fn(),
      entityContext: null,
      streaming: { onPaneEvent: null },
      streamingState: { isStreaming: false, tokenCount: 0 },
      turnCount: 0,
      isLoading: false,
    }),
    resolveWorkspaceWidget: jest.fn(async (widgetType: string) => makeStub(widgetType)),
    getWorkspaceWidgetMetadata: jest.fn(() => ({
      displayName: 'Analysis',
      category: 'analysis',
      defaultOrder: 150,
      allowMultiple: false,
    })),
  };
});

let mockAnalysisLaunch: { mode: 'new' | 'existing'; analysisId?: string; worktype?: string } | null = null;
jest.mock('../../shell/ThreePaneShell', () => ({
  usePaneCollapseContext: () => null,
  useComposeLaunch: () => null,
  useAnalysisLaunch: () => mockAnalysisLaunch,
}));

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
    // Identifiable host-service factories so the injection assertion is deterministic
    // and no real Xrm global is required in jsdom.
    createXrmDataService: jest.fn(() => ({ __kind: 'xrm-data-service' })),
    createXrmNavigationService: jest.fn(() => ({ __kind: 'xrm-nav-service' })),
    searchUsersAndContacts: jest.fn(async () => []),
  };
});

// eslint-disable-next-line import/first
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
  authenticatedFetchMock.mockClear();
  mockAnalysisLaunch = null;
  if (!Element.prototype.scrollIntoView) {
    // eslint-disable-next-line @typescript-eslint/no-empty-function
    Element.prototype.scrollIntoView = function (): void {};
  }
});

describe('WorkspacePane — Analysis entry-matrix host routing (task 050, FR-14)', () => {
  it("2a/2b: analysisLaunch.mode='new' auto-installs the Analysis hub tab", async () => {
    mockAnalysisLaunch = { mode: 'new', worktype: '100000000' };

    await act(async () => {
      renderPane();
    });

    await waitFor(() => expect(screen.getByTestId('stub-analysis-hub')).toBeInTheDocument());
  });

  it("2c/2d: analysisLaunch.mode='existing' resolves the bound session and dispatches conversation.session_switch", async () => {
    mockAnalysisLaunch = { mode: 'existing', analysisId: 'analysis-guid-1' };

    const switched: unknown[] = [];
    const { bus } = renderPane();
    bus.subscribe('conversation', (e: unknown) => {
      if ((e as { type?: string }).type === 'session_switch') switched.push(e);
    });

    await waitFor(() =>
      expect(
        authenticatedFetchMock.mock.calls.some(([u]) => String(u).includes('/by-analysis/analysis-guid-1')),
      ).toBe(true),
    );
    await waitFor(() =>
      expect(switched).toContainEqual({ type: 'session_switch', sessionId: 'sess-existing-1' }),
    );
    // No hub cards for the existing-analysis case.
    expect(screen.queryByTestId('stub-analysis-hub')).not.toBeInTheDocument();
  });

  it('Thread 1: a create-analysis-wizard load is enriched with host services, preserving the dispatcher widgetData', async () => {
    const { bus } = renderPane();

    await act(async () => {
      bus.dispatch('workspace', {
        type: 'widget_load',
        widgetType: 'create-analysis-wizard',
        widgetData: { workTypeValue: 100000000, workTypeLabel: 'Agreement Review' },
        displayName: 'Create Agreement Review Analysis',
      });
    });

    const stub = await screen.findByTestId('stub-create-analysis-wizard');
    expect(stub).toHaveAttribute('data-has-dataservice', 'true');
    expect(stub).toHaveAttribute('data-has-navservice', 'true');
    expect(stub).toHaveAttribute('data-has-authfetch', 'true');
    expect(stub).toHaveAttribute('data-has-searchusers', 'true');
    // The dispatcher's own workTypeValue is preserved (injection merges OVER it).
    expect(stub).toHaveAttribute('data-worktype', '100000000');
  });
});
