/**
 * WorkspacePane.active-doc-follows-tab.test.tsx — spaarkeai-compose-r2 FIX #6.
 *
 * The manual "Visible to assistant" toggle was REMOVED. The Assistant's active
 * document now FOLLOWS the active workspace tab:
 *
 *   - Activating the Compose tab registers its document as the session's active
 *     document (the compose visibility conduit is driven with visible=true).
 *   - Activating any NON-compose tab (Daily Briefing / dashboard / other)
 *     withdraws it (visible=false) — so exactly one active doc = the selected
 *     Compose tab, and none when a non-document tab is active.
 *   - No "Visible to assistant" switch is rendered anywhere.
 *
 * WorkspacePane reads the conduit via `useComposeVisibility()`; the editor
 * (ComposeWorkspace) publishes the handler. Here the editor is not mounted, so
 * we register a mock visibility handler on the SAME bridge and assert
 * WorkspacePane drives it on tab activation.
 *
 * Test category per ADR-038: Component Test (KEEP — SpaarkeAi presenter render
 * contract observable by the parent + end user).
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';
import {
  ComposeActionBridgeProvider,
  useRegisterComposeVisibilityHandler,
} from '@spaarke/compose-components/context/composeActionBridge';

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
      chatSessionId: 'session-follows-tab',
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
      displayName: widgetType === 'compose' ? 'Compose' : 'Workspace',
      category: 'workspace',
      defaultOrder: 100,
      allowMultiple: widgetType !== 'compose',
    })),
  };
});

// No default layout — keeps the test scoped to the explicit dispatches below.
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

const visibilityMock = jest.fn();

/** Registers a mock compose visibility handler on the bridge (stands in for the live editor). */
function RegisterVisibility(): null {
  useRegisterComposeVisibilityHandler(visibilityMock);
  return null;
}

function renderPane(): { bus: PaneEventBus } {
  const bus = new PaneEventBus();
  render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>
        <ComposeActionBridgeProvider>
          <RegisterVisibility />
          <WorkspacePane />
        </ComposeActionBridgeProvider>
      </PaneEventBusProvider>
    </FluentProvider>,
  );
  return { bus };
}

function composeOpen() {
  return {
    type: 'widget_load' as const,
    widgetType: 'compose',
    widgetData: { compose: { upload: { sessionId: 'session-follows-tab', sessionFileId: 'file-1' } } },
    displayName: 'Compose',
  };
}

function briefingOpen() {
  return {
    type: 'widget_load' as const,
    widgetType: 'workspace',
    widgetData: { layoutId: 'lay-1', layoutName: 'Daily Briefing' },
    displayName: 'Daily Briefing',
  };
}

beforeEach(() => {
  visibilityMock.mockClear();
  authenticatedFetchMock.mockClear();
  if (!Element.prototype.scrollIntoView) {
    // eslint-disable-next-line @typescript-eslint/no-empty-function
    Element.prototype.scrollIntoView = function (): void {};
  }
});

describe('WorkspacePane — active document follows the active tab (FIX #6)', () => {
  it('registers the compose doc (visible=true) when the Compose tab is active', async () => {
    const { bus } = renderPane();

    bus.dispatch('workspace', composeOpen());
    await screen.findByRole('tab', { name: /compose/i });

    await waitFor(() => expect(visibilityMock).toHaveBeenLastCalledWith(true));
  });

  it('withdraws the compose doc (visible=false) when a non-document tab becomes active', async () => {
    const { bus } = renderPane();

    // Compose active first — registers the doc.
    bus.dispatch('workspace', composeOpen());
    await screen.findByRole('tab', { name: /compose/i });
    await waitFor(() => expect(visibilityMock).toHaveBeenLastCalledWith(true));

    visibilityMock.mockClear();

    // Switch to a non-compose (Daily Briefing) tab — auto-activates → withdraw.
    bus.dispatch('workspace', briefingOpen());
    await screen.findByRole('tab', { name: /daily briefing/i });

    await waitFor(() => expect(visibilityMock).toHaveBeenLastCalledWith(false));
  });

  it('renders no "Visible to assistant" toggle', async () => {
    const { bus } = renderPane();

    bus.dispatch('workspace', composeOpen());
    await screen.findByRole('tab', { name: /compose/i });

    expect(screen.queryByRole('switch')).toBeNull();
  });
});
