/**
 * WorkspacePane — a 404 from the session /tabs endpoint is benign, so it records NO failure telemetry.
 *
 * GET /tabs 404 = "no tabs known to the BFF for this session yet" (falls through to the local fallback);
 * PATCH /tabs 404 = "session not yet known to the BFF" (write-through is best-effort). Both were
 * exempted only for a RETURNED 404. `@spaarke/auth`'s authenticatedFetch THROWS ApiError(404), which
 * the catches logged as TAB_RESTORE_LOAD_FAILURE / TAB_RESTORE_SAVE_FAILURE — false failures on every
 * new session. The fetch here throws the REAL ApiError (the `@spaarke/auth` jest stub re-exports it).
 *
 * Harness: WorkspacePane.analysis-entry.test.tsx (analysisLaunch 'new' installs a tab, which drives
 * the debounced PATCH write-through).
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { act } from 'react-dom/test-utils';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';
import { ApiError } from '@spaarke/auth';
import * as errorTelemetry from '../../../telemetry/errorTelemetry';

let tabsStatus = 404;

const authenticatedFetchMock = jest.fn(async (url: string, init?: RequestInit): Promise<Response> => {
  if (url.includes('/tabs')) {
    // Production shape: a non-2xx is THROWN, never returned.
    throw new ApiError(`HTTP ${tabsStatus}`, tabsStatus, null);
  }
  void init;
  throw new ApiError('HTTP 404', 404, null);
});

jest.mock('@spaarke/ai-widgets', () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const actual = jest.requireActual('@spaarke/ai-widgets') as any;
  const makeStub = (widgetType: string): React.FC =>
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
      chatSessionId: 'session-tabs-404',
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

jest.mock('../../shell/ThreePaneShell', () => ({
  usePaneCollapseContext: () => null,
  useComposeLaunch: () => null,
  useAnalysisLaunch: () => ({ mode: 'new', worktype: '100000000' }),
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
    createXrmDataService: jest.fn(() => ({ __kind: 'xrm-data-service' })),
    createXrmNavigationService: jest.fn(() => ({ __kind: 'xrm-nav-service' })),
    searchUsersAndContacts: jest.fn(async () => []),
  };
});

// eslint-disable-next-line import/first
import { WorkspacePane } from '../WorkspacePane';

let telemetrySpy: jest.SpyInstance;

beforeEach(() => {
  authenticatedFetchMock.mockClear();
  telemetrySpy = jest.spyOn(errorTelemetry, 'logTelemetryError').mockImplementation(() => undefined);
  if (!Element.prototype.scrollIntoView) {
    // eslint-disable-next-line @typescript-eslint/no-empty-function
    Element.prototype.scrollIntoView = function (): void {};
  }
});

afterEach(() => {
  telemetrySpy.mockRestore();
});

async function renderAndSettle(): Promise<void> {
  await act(async () => {
    render(
      <FluentProvider theme={webLightTheme}>
        <PaneEventBusProvider bus={new PaneEventBus()}>
          <WorkspacePane />
        </PaneEventBusProvider>
      </FluentProvider>,
    );
  });
  await waitFor(() => expect(screen.getByTestId('stub-analysis-hub')).toBeInTheDocument());
  // The installed tab drives the debounced (200 ms) PATCH write-through.
  await waitFor(
    () =>
      expect(
        authenticatedFetchMock.mock.calls.some(([u, i]) => String(u).includes('/tabs') && i?.method === 'PATCH'),
      ).toBe(true),
    { timeout: 3000 },
  );
  await act(async () => {
    await new Promise((r) => setTimeout(r, 50));
  });
}

const loggedNames = (): string[] => telemetrySpy.mock.calls.map(([name]) => String(name));

describe('WorkspacePane — /tabs 404 is benign', () => {
  it('a thrown 404 on GET and PATCH /tabs logs no tab-restore failure telemetry', async () => {
    tabsStatus = 404;
    await renderAndSettle();

    expect(
      authenticatedFetchMock.mock.calls.some(([u, i]) => String(u).includes('/tabs') && (i?.method ?? 'GET') === 'GET'),
    ).toBe(true);
    expect(loggedNames()).not.toContain(errorTelemetry.TELEMETRY_TAB_RESTORE_LOAD_FAILURE);
    expect(loggedNames()).not.toContain(errorTelemetry.TELEMETRY_TAB_RESTORE_SAVE_FAILURE);
  });

  it('a thrown 500 is still recorded as a load and a save failure', async () => {
    tabsStatus = 500;
    await renderAndSettle();

    await waitFor(() => expect(loggedNames()).toContain(errorTelemetry.TELEMETRY_TAB_RESTORE_SAVE_FAILURE));
    expect(loggedNames()).toContain(errorTelemetry.TELEMETRY_TAB_RESTORE_LOAD_FAILURE);
  });
});
