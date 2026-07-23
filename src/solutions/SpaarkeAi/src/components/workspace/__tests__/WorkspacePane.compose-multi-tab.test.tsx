/**
 * WorkspacePane.compose-multi-tab.test.tsx — spaarkeai-compose-r2 (multi-Compose-tab).
 *
 * Compose is no longer a hard singleton. This regression suite pins the new contract:
 *
 *   (i)   opening TWO DIFFERENT compose documents yields TWO compose tabs, BOTH mounted
 *         (one keep-alive host per compose tab — no round-7 #1 unmount-on-switch);
 *   (ii)  switching between them preserves each tab's content — the inactive tab's host stays
 *         MOUNTED (hidden via aria-hidden, NOT removed from the DOM), the active one is shown;
 *   (iii) re-seeding the SAME document (same drafting binding, later turn) REUSES its existing
 *         tab — no duplicate;
 *   (iv)  the Workspaces-menu blank "Compose" layout open MINTS a NEW (blank) tab.
 *
 * Harness derived from WorkspacePane.compose-draft-reuse.test.tsx. Auto-install is disabled via a
 * null activeLayout, so the ONLY tabs come from the widget_load events this test dispatches. The
 * compose widget is stubbed to render its seed's ledgerRef so each tab's identity/content is
 * observable in the DOM; PATCH snapshots let us count compose tabs.
 *
 * Test category per ADR-038: Component Test (KEEP — SpaarkeAi presenter render contract observable
 * by the parent + end user).
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen, waitFor, within, fireEvent } from '@testing-library/react';
import { act } from 'react-dom/test-utils';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';

interface RecordedPatch {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  tabs: Array<{ id: string; widgetType: string; widgetData: any }>;
  activeTabId: string | null;
}
const recordedPatches: RecordedPatch[] = [];

const authenticatedFetchMock = jest.fn(async (url: string, init?: RequestInit): Promise<Response> => {
  const method = init?.method ?? 'GET';
  if (method === 'GET' && url.includes('/tabs')) {
    return { ok: true, status: 200, json: async () => ({ tabs: [], activeTabId: null }) } as Partial<Response> as Response;
  }
  if (method === 'PATCH' && url.includes('/tabs')) {
    recordedPatches.push(JSON.parse(String(init?.body)) as RecordedPatch);
    return { ok: true, status: 204, json: async () => ({}) } as Partial<Response> as Response;
  }
  return { ok: true, status: 200, json: async () => ({ acknowledged: true }) } as Partial<Response> as Response;
});

jest.mock('@spaarke/ai-widgets', () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const actual = jest.requireActual('@spaarke/ai-widgets') as any;
  // Compose stub renders the seed's ledgerRef so each tab's content/identity is DOM-observable.
  const composeStub: React.FC<{ data?: unknown; isActiveTab?: boolean }> = ({ data, isActiveTab }) => {
    const ledgerRef =
      (data as { compose?: { draft?: { ledgerRef?: string } } } | null)?.compose?.draft?.ledgerRef ??
      '(blank)';
    return (
      <div data-testid="compose-stub" data-active={String(isActiveTab)}>
        {ledgerRef}
      </div>
    );
  };
  const makeStub = (widgetType: string): React.FC<{ data?: unknown; isActiveTab?: boolean }> =>
    widgetType === 'compose'
      ? composeStub
      : function StubWidget({ data }: { data?: unknown }): React.JSX.Element {
          const layoutName = (data as { layoutName?: string } | null)?.layoutName ?? widgetType;
          return <div data-testid="active-widget-stub">{layoutName}</div>;
        };
  return {
    ...actual,
    useAiSession: () => ({
      isAuthenticated: true,
      authenticatedFetch: authenticatedFetchMock,
      getAccessToken: jest.fn().mockResolvedValue('test-token'),
      bffBaseUrl: 'https://test-bff.example.com',
      tenantId: 'test-tenant',
      chatSessionId: 'session-multi',
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
    getWorkspaceWidgetMetadata: jest.fn((widgetType: string) => ({
      displayName: widgetType === 'compose' ? 'Compose' : 'Workspace',
      category: 'workspace',
      defaultOrder: 100,
      allowMultiple: widgetType !== 'compose',
    })),
  };
});

jest.mock('../../shell/ThreePaneShell', () => ({
  usePaneCollapseContext: () => null,
  useComposeLaunch: () => null,
}));

jest.mock('../../../hooks/useWorkspaceLayouts', () => ({
  useWorkspaceLayouts: () => ({
    layouts: [{ id: 'layout-compose', name: 'Compose', isSystem: true }],
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

function composeDraftOpen(ledgerRef: string) {
  return {
    type: 'widget_load' as const,
    widgetType: 'compose',
    widgetData: { compose: { draft: { ledgerRef, sessionId: 'session-multi' } } },
    displayName: 'Compose',
  };
}

/** The Workspaces-menu blank "Compose" open — a LAYOUT load (widgetType 'workspace') with no seed. */
function menuComposeOpen() {
  return {
    type: 'widget_load' as const,
    widgetType: 'workspace',
    widgetData: { layoutId: 'layout-compose', layoutName: 'Compose' },
    displayName: 'Compose',
  };
}

const keepAliveHosts = () => screen.queryAllByTestId('workspace-compose-keepalive');
const lastPatchComposeTabs = () => {
  const last = recordedPatches[recordedPatches.length - 1];
  return last ? last.tabs.filter((t) => t.widgetType === 'compose') : [];
};

beforeEach(() => {
  recordedPatches.length = 0;
  authenticatedFetchMock.mockClear();
  if (!Element.prototype.scrollIntoView) {
    // eslint-disable-next-line @typescript-eslint/no-empty-function
    Element.prototype.scrollIntoView = function (): void {};
  }
});

describe('WorkspacePane — multiple Compose tabs (spaarkeai-compose-r2)', () => {
  it('opens TWO tabs for TWO different documents, both mounted; switching preserves each (hidden, not unmounted)', async () => {
    const { bus } = renderPane();

    // Open document A, then a DIFFERENT document B (distinct drafting bindings).
    await act(async () => {
      bus.dispatch('workspace', composeDraftOpen('binding-A@t1'));
    });
    await act(async () => {
      bus.dispatch('workspace', composeDraftOpen('binding-B@t1'));
    });

    // (i) TWO compose tabs — both keep-alive hosts mounted.
    await waitFor(() => expect(keepAliveHosts()).toHaveLength(2));
    await waitFor(() => expect(lastPatchComposeTabs()).toHaveLength(2));

    // Both documents' content is rendered (each host keeps its OWN seed).
    const hostFor = (ledgerRef: string) =>
      keepAliveHosts().find((h) => within(h).queryByText(ledgerRef)) ?? null;
    await waitFor(() => {
      expect(hostFor('binding-A@t1')).not.toBeNull();
      expect(hostFor('binding-B@t1')).not.toBeNull();
    });

    // B was opened last → B is active (visible), A is mounted-hidden.
    expect(hostFor('binding-B@t1')).toHaveAttribute('aria-hidden', 'false');
    expect(hostFor('binding-A@t1')).toHaveAttribute('aria-hidden', 'true');

    // (ii) Switch to tab A via the tab strip — A becomes visible, B stays MOUNTED but hidden.
    const tabs = screen.getAllByRole('tab');
    expect(tabs).toHaveLength(2);
    await act(async () => {
      fireEvent.click(tabs[0]); // insertion order → tab[0] is document A
    });

    await waitFor(() => {
      expect(hostFor('binding-A@t1')).toHaveAttribute('aria-hidden', 'false');
    });
    // Both hosts still present (B was NOT unmounted — no round-7 #1 regression).
    expect(keepAliveHosts()).toHaveLength(2);
    expect(hostFor('binding-B@t1')).not.toBeNull();
    expect(hostFor('binding-B@t1')).toHaveAttribute('aria-hidden', 'true');
  });

  it('re-seeding the SAME document (same binding, later turn) REUSES its tab — no duplicate', async () => {
    const { bus } = renderPane();

    await act(async () => {
      bus.dispatch('workspace', composeDraftOpen('binding-A@t1'));
    });
    await act(async () => {
      bus.dispatch('workspace', composeDraftOpen('binding-B@t1'));
    });
    await waitFor(() => expect(keepAliveHosts()).toHaveLength(2));

    // Re-draft document A (same binding, turn 2) — reuses A's tab, does NOT mint a third.
    await act(async () => {
      bus.dispatch('workspace', composeDraftOpen('binding-A@t2'));
    });

    // Still exactly TWO compose tabs — every persisted snapshot stays ≤ 2.
    await waitFor(() => expect(lastPatchComposeTabs()).toHaveLength(2));
    expect(keepAliveHosts()).toHaveLength(2);
    for (const patch of recordedPatches) {
      expect(patch.tabs.filter((t) => t.widgetType === 'compose').length).toBeLessThanOrEqual(2);
    }
  });

  it('the Workspaces-menu blank "Compose" open MINTS a new tab (does not re-activate an existing one)', async () => {
    const { bus } = renderPane();

    await act(async () => {
      bus.dispatch('workspace', composeDraftOpen('binding-A@t1'));
    });
    await waitFor(() => expect(keepAliveHosts()).toHaveLength(1));

    // Blank menu "Compose" open — a NEW blank compose tab, not a re-activation of A.
    await act(async () => {
      bus.dispatch('workspace', menuComposeOpen());
    });

    await waitFor(() => expect(keepAliveHosts()).toHaveLength(2));
    await waitFor(() => expect(lastPatchComposeTabs()).toHaveLength(2));
  });
});
