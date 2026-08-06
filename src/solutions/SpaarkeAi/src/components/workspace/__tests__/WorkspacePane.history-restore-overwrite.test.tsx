/**
 * WorkspacePane — FR-D1 History rich-restore OVERWRITE-HAZARD regression
 * (spaarkeai-assistant-enhancements-r2, task 035).
 *
 * The load-bearing resume fix. Reopening a History session routes through
 * `ConversationPane.handleSelectHistorySession` → `setChatSessionId(newId)`, which
 * re-keys WorkspacePane's chatSessionId-scoped `/tabs` restore effect. BEFORE this
 * fix that effect was blocked on a session SWITCH: `restoreFromPersistence()` is a
 * no-op while any non-Home tab exists, so the PRIOR session's tabs (a) stayed
 * visible on the reopened session and (b) got PATCHed onto the reopened session's
 * `/tabs` store — corrupting it. The fix clears the workspace FIRST (cancelling the
 * spurious empty write-through) so the reopened session's OWN stored tabs restore.
 *
 * Harness mirrors WorkspacePane.tab-restore-race.test.tsx, extended with a DYNAMIC
 * `chatSessionId` (module-level `mockChatSessionId`, flipped + rerendered to model a
 * History reopen) and PER-SESSION `/tabs` snapshots so the overwrite is observable.
 *
 * Assertions (POML acceptance criteria):
 *   (1) Reopening a DIFFERENT session restores THAT session's tabs (Bravo), not the
 *       prior session's (Alpha).                       [criteria 1 + 4 — rich path]
 *   (2) The reopened session's stored tab set is NOT corrupted: no PATCH targeting
 *       session B ever carries session A's tab.        [criterion 2]
 *   (3) The whole thing holds under a dark Fluent theme (ADR-021 — no regression).
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { act } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';

// ---------------------------------------------------------------------------
// Dynamic session + per-session /tabs store. `mockChatSessionId` is read by the
// useAiSession mock (must be `mock*`-prefixed to be referenced from the hoisted
// jest.mock factory); flipping it + rerendering models an in-place History reopen.
// ---------------------------------------------------------------------------

let mockChatSessionId = 'session-A';

interface StoredTab {
  id: string;
  widgetType: string;
  widgetData: unknown;
  displayName: string;
}
interface StoredSnapshot {
  tabs: StoredTab[];
  activeTabId: string | null;
}

const restoreBySession: Record<string, StoredSnapshot> = {
  'session-A': {
    tabs: [
      {
        id: 'wstab-1-workspace',
        widgetType: 'workspace',
        widgetData: { layoutId: 'layout-a', layoutName: 'Alpha WS' },
        displayName: 'Alpha WS',
      },
    ],
    activeTabId: 'wstab-1-workspace',
  },
  'session-B': {
    tabs: [
      {
        id: 'wstab-1-workspace',
        widgetType: 'workspace',
        widgetData: { layoutId: 'layout-b', layoutName: 'Bravo WS' },
        displayName: 'Bravo WS',
      },
    ],
    activeTabId: 'wstab-1-workspace',
  },
  'session-C': {
    tabs: [
      {
        id: 'wstab-1-workspace',
        widgetType: 'workspace',
        widgetData: { layoutId: 'layout-c', layoutName: 'Charlie WS' },
        displayName: 'Charlie WS',
      },
    ],
    activeTabId: 'wstab-1-workspace',
  },
};

// Per-session GET latency. Session B is SLOW so the marker-leak test can switch
// away (to C) and cancel B's adoption restore BEFORE it settles; A/C are fast.
const getDelayMsBySession: Record<string, number> = {
  'session-A': 10,
  'session-B': 150,
  'session-C': 10,
};

interface RecordedPatch {
  session: string;
  tabs: Array<{ id: string; widgetType: string; widgetData: unknown }>;
  activeTabId: string | null;
}
const recordedPatches: RecordedPatch[] = [];

/** Extract the session id from a `/ai/chat/sessions/{id}/tabs` URL. */
function sessionOf(url: string): string {
  const m = /\/sessions\/([^/]+)\/tabs/.exec(url);
  return m ? decodeURIComponent(m[1]) : '';
}

const authenticatedFetchMock = jest.fn(
  async (url: string, init?: RequestInit): Promise<Response> => {
    const method = init?.method ?? 'GET';
    if (method === 'GET' && url.includes('/tabs')) {
      const session = sessionOf(url);
      // Real delay so restore loses the race vs synchronous effects, as in prod.
      await new Promise((resolve) => setTimeout(resolve, getDelayMsBySession[session] ?? 15));
      const snap = restoreBySession[session] ?? { tabs: [], activeTabId: null };
      return { ok: true, status: 200, json: async () => snap } as Partial<Response> as Response;
    }
    if (method === 'PATCH' && url.includes('/tabs')) {
      const body = JSON.parse(String(init?.body)) as Omit<RecordedPatch, 'session'>;
      recordedPatches.push({ session: sessionOf(url), ...body });
      return { ok: true, status: 204, json: async () => ({}) } as Partial<Response> as Response;
    }
    return { ok: false, status: 404, json: async () => ({}) } as Partial<Response> as Response;
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
      chatSessionId: mockChatSessionId,
      setChatSessionId: jest.fn(),
      playbookId: undefined,
      setPlaybookId: jest.fn(),
      entityContext: null, // home surface — no local anchor fallback (keeps the test to the BFF path)
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

// No default layout + no pins — isolates the assertions to the restored tabs.
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

// eslint-disable-next-line @typescript-eslint/no-explicit-any
function renderPane(theme: any = webLightTheme): { rerender: () => void; bus: PaneEventBus } {
  const bus = new PaneEventBus();
  // A FRESH element every render — passing the same element reference to rerender()
  // makes React bail out of re-rendering the WorkspacePane subtree, so chatSessionId
  // would never flip. Rebuild the tree so the useAiSession mock is re-read.
  const build = (): React.JSX.Element => (
    <FluentProvider theme={theme}>
      <PaneEventBusProvider bus={bus}>
        <WorkspacePane />
      </PaneEventBusProvider>
    </FluentProvider>
  );
  const { rerender } = render(build());
  return { rerender: () => rerender(build()), bus };
}

beforeEach(() => {
  recordedPatches.length = 0;
  authenticatedFetchMock.mockClear();
  mockChatSessionId = 'session-A';
  // The leak test opens a compose tab, which composeRunPersistence writes to
  // localStorage (home surface). Without clearing, the NEXT test's compose-run-restore
  // effect reopens that leaked compose tab and corrupts its tab set — deterministic
  // inter-test bleed. Clear it so each test starts from a clean store.
  localStorage.clear();
  if (!Element.prototype.scrollIntoView) {
    // eslint-disable-next-line @typescript-eslint/no-empty-function
    Element.prototype.scrollIntoView = function (): void {};
  }
});

describe('WorkspacePane — FR-D1 History reopen overwrite hazard', () => {
  it('reopening a DIFFERENT session restores ITS tabs (not the prior session tabs), and never PATCHes the prior tabs onto it', async () => {
    const { rerender } = renderPane();

    // Session A restored — its Alpha WS tab is present.
    expect(
      await screen.findByRole('tab', { name: /alpha ws/i }, { timeout: 3000 }),
    ).toBeInTheDocument();

    // From here we only care about writes attributable to the reopen.
    recordedPatches.length = 0;

    // History reopen: adopt a DIFFERENT session in place (the handleSelectHistorySession
    // → setChatSessionId leg) and let the pane re-key its restore effect.
    mockChatSessionId = 'session-B';
    rerender();

    // (1) The reopened session's OWN tab restores…
    expect(
      await screen.findByRole('tab', { name: /bravo ws/i }, { timeout: 3000 }),
    ).toBeInTheDocument();

    // …and the prior session's tab is gone (cleared before restore — not still shown).
    await waitFor(
      () => {
        expect(screen.queryByRole('tab', { name: /alpha ws/i })).not.toBeInTheDocument();
      },
      { timeout: 3000 },
    );

    // (2) The reopened session's stored set is NOT corrupted: no PATCH targeting
    // session B ever carried session A's tab (the pre-fix failure wrote Alpha → B).
    // Give any debounced write-through a chance to flush first.
    await new Promise((resolve) => setTimeout(resolve, 300));
    const bPatches = recordedPatches.filter((p) => p.session === 'session-B');
    for (const patch of bPatches) {
      const layoutIds = patch.tabs.map(
        (t) => (t.widgetData as { layoutId?: string } | null)?.layoutId,
      );
      expect(layoutIds).not.toContain('layout-a'); // the prior session's tab must never reach B
      // Finding 3 — cancellation invariant: session B must NEVER receive an empty-set
      // PATCH. Clearing A's tabs schedules a debounced PATCH of tabs:[] against the NEW
      // chatSessionId (B); the fix cancels it synchronously. Delete the clearTimeout +
      // pendingSnapshotRef=null cancellation and this assertion goes RED (tabs:[] would
      // flush to B, zeroing its server store) — assertion (1) alone does not catch that.
      expect(patch.tabs.length).toBeGreaterThan(0);
    }
  });

  it('does not leak a CANCELLED compose-adoption marker: a later History reopen of that session still clears + restores it (Finding 1)', async () => {
    const { rerender, bus } = renderPane();

    // Session A restored.
    expect(
      await screen.findByRole('tab', { name: /alpha ws/i }, { timeout: 3000 }),
    ).toBeInTheDocument();

    // Compose-ADOPT session B: a compose widget_load carrying composeSessionId sets
    // WorkspacePane's composeAdoptionSessionRef marker to B (and opens a compose tab).
    await act(async () => {
      bus.dispatch('workspace', {
        type: 'widget_load',
        widgetType: 'compose',
        widgetData: { compose: { composeSessionId: 'session-B', speDriveItemId: 'doc-b', fileName: 'DocB.docx' } },
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
      } as any);
      for (let i = 0; i < 6; i++) await Promise.resolve();
    });

    // Adopt B in place (handleSelectHistorySession → setChatSessionId). The switch is a
    // compose adoption (marker===B) → the FULL clear is skipped; B's restore GET (slow,
    // 400ms) is now in flight. Let the effect commit + start its GET, but nowhere near
    // the 400ms settle.
    await act(async () => {
      mockChatSessionId = 'session-B';
      rerender();
      await new Promise((resolve) => setTimeout(resolve, 60));
    });

    // …BEFORE B settles, switch to C (rapid History clicks). This CANCELS B's adoption
    // restore. The effect cleanup must reset the leaked marker; effect(C) then performs
    // a real clear + restores C.
    await act(async () => {
      mockChatSessionId = 'session-C';
      rerender();
      await new Promise((resolve) => setTimeout(resolve, 40));
    });

    expect(
      await screen.findByRole('tab', { name: /charlie ws/i }, { timeout: 3000 }),
    ).toBeInTheDocument();

    // Now genuinely REOPEN session B via History. If the marker had leaked, this switch
    // would be mis-read as a compose adoption, SKIP the full clear, and (with no compose
    // tab present) leave C's tab in place / corrupt B. With the leak fixed, B is cleared
    // and B's OWN tabs restore.
    recordedPatches.length = 0;
    await act(async () => {
      mockChatSessionId = 'session-B';
      rerender();
      await new Promise((resolve) => setTimeout(resolve, 10));
    });

    expect(
      await screen.findByRole('tab', { name: /bravo ws/i }, { timeout: 3000 }),
    ).toBeInTheDocument();
    await waitFor(
      () => {
        expect(screen.queryByRole('tab', { name: /charlie ws/i })).not.toBeInTheDocument();
      },
      { timeout: 3000 },
    );

    // B's store is not corrupted by the cancelled adoption / the C session: no PATCH to
    // B carries C's tab, and none is an empty set.
    await new Promise((resolve) => setTimeout(resolve, 300));
    const bPatches = recordedPatches.filter((p) => p.session === 'session-B');
    for (const patch of bPatches) {
      const layoutIds = patch.tabs.map((t) => (t.widgetData as { layoutId?: string } | null)?.layoutId);
      expect(layoutIds).not.toContain('layout-c');
      expect(patch.tabs.length).toBeGreaterThan(0);
    }
  });

  it('holds the same rich reopen under a dark Fluent theme (ADR-021 — no regression)', async () => {
    const { rerender } = renderPane(webDarkTheme);

    expect(
      await screen.findByRole('tab', { name: /alpha ws/i }, { timeout: 3000 }),
    ).toBeInTheDocument();

    mockChatSessionId = 'session-B';
    rerender();

    expect(
      await screen.findByRole('tab', { name: /bravo ws/i }, { timeout: 3000 }),
    ).toBeInTheDocument();
    await waitFor(
      () => {
        expect(screen.queryByRole('tab', { name: /alpha ws/i })).not.toBeInTheDocument();
      },
      { timeout: 3000 },
    );
  });
});
