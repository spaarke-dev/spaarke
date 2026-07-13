/**
 * WorkspacePane — FIX #10b: the Compose "Email" affordance mounts the EmailStubWidget tab.
 *
 * Regression guard for the spaarkeai-compose-r2 flip: WorkspacePane's `widget_load` handler gained a
 * `widgetType === 'compose'` branch (Compose DIRECT widget). This test proves the pre-existing
 * `widgetType === 'email'` branch SURVIVED and still fires — the compose branch does NOT shadow it —
 * so "Open in Email" / "Send in Email" (ComposeAiToolbar → handleEmailAction) open the stub email tab
 * carrying the drafted body (open mode) / attachment filename (send mode).
 *
 * Harness derived from WorkspacePane.compose-draft-reuse.test.tsx (null activeLayout → no auto-install
 * tab; EmailStubWidget is statically imported in WorkspacePane, so the REAL component renders here).
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { act } from 'react-dom/test-utils';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';

const authenticatedFetchMock = jest.fn(async (url: string, init?: RequestInit): Promise<Response> => {
  const method = init?.method ?? 'GET';
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
  const makeStub = (widgetType: string): React.FC<{ data?: unknown }> =>
    function StubWidget(): React.JSX.Element {
      return <div data-testid={`stub-${widgetType}`} />;
    };
  return {
    ...actual,
    useAiSession: () => ({
      isAuthenticated: true,
      authenticatedFetch: authenticatedFetchMock,
      getAccessToken: jest.fn().mockResolvedValue('test-token'),
      bffBaseUrl: 'https://test-bff.example.com',
      tenantId: 'test-tenant',
      chatSessionId: 'session-email',
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
      displayName: 'Workspace',
      category: 'workspace',
      defaultOrder: 100,
      allowMultiple: true,
    })),
  };
});

jest.mock('../../shell/ThreePaneShell', () => ({
  usePaneCollapseContext: () => null,
  useComposeLaunch: () => null,
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

// The EXACT interop contract ComposeAiToolbar.handleEmailAction dispatches.
function emailOpen() {
  return {
    type: 'widget_load' as const,
    widgetType: 'email',
    displayName: 'Email',
    widgetData: { layoutName: 'Email', mode: 'open', bodyText: 'Drafted body text' },
  };
}
function emailSend() {
  return {
    type: 'widget_load' as const,
    widgetType: 'email',
    displayName: 'Email',
    widgetData: { layoutName: 'Email', mode: 'send', attachmentFileName: 'Report.docx' },
  };
}

beforeEach(() => {
  authenticatedFetchMock.mockClear();
  if (!Element.prototype.scrollIntoView) {
    // eslint-disable-next-line @typescript-eslint/no-empty-function
    Element.prototype.scrollIntoView = function (): void {};
  }
});

describe('WorkspacePane — FIX #10b email stub mount', () => {
  it('"Open in Email" (widgetType email) mounts EmailStubWidget with the drafted body', async () => {
    const { bus } = renderPane();

    await act(async () => {
      bus.dispatch('workspace', emailOpen());
    });

    await waitFor(() => expect(screen.getByTestId('email-stub-widget')).toBeInTheDocument());
    expect(screen.getByTestId('email-stub-body')).toHaveTextContent('Drafted body text');
  });

  it('"Send in Email" (send mode) mounts the stub with the attachment filename', async () => {
    const { bus } = renderPane();

    await act(async () => {
      bus.dispatch('workspace', emailSend());
    });

    await waitFor(() => expect(screen.getByTestId('email-stub-widget')).toBeInTheDocument());
    expect(screen.getByTestId('email-stub-attachment')).toHaveTextContent('Report.docx');
  });

  it('the compose branch does NOT shadow email: an email load after a compose load still opens the stub', async () => {
    const { bus } = renderPane();

    // A compose DIRECT open first (exercises the widgetType==='compose' branch).
    await act(async () => {
      bus.dispatch('workspace', {
        type: 'widget_load',
        widgetType: 'compose',
        widgetData: { compose: { draft: { ledgerRef: 'b@t1', sessionId: 'session-email' } } },
        displayName: 'Compose',
      });
    });
    // Then an email open — the email branch must still fire and become the active tab.
    await act(async () => {
      bus.dispatch('workspace', emailOpen());
    });

    await waitFor(() => expect(screen.getByTestId('email-stub-widget')).toBeInTheDocument());
  });
});
