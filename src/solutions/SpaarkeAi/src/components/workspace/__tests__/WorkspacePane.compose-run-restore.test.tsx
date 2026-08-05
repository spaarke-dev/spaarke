/**
 * WorkspacePane.compose-run-restore.test.tsx — UAT round-5 item #13.
 *
 * Pins the home-surface (UNBOUND) Compose-tab persistence + resume contract:
 *
 *   (1) persist-on-open   — opening a Compose tab on the home surface writes a
 *                           durable localStorage snapshot (seed + session);
 *   (2) clear-on-close    — an EXPLICIT tab close drops it from the snapshot
 *                           (agency — not resurrected on return);
 *   (3) cold-load restore — a fresh persisted Compose tab reopens on mount, via
 *                           the SAME widget_load door, with composeSessionId
 *                           threaded into the seed (FR-16 findings recall);
 *   (4) run-in-flight resume — a young in-flight run shows the tab spinner + polls
 *                           compose-outputs and, on findings-after-empty, dispatches
 *                           compose_advisory_comments (the live placement event);
 *   (5) TTL expiry        — a stale persisted tab is NOT restored;
 *   (6) deliberate close  — a closed tab does not resurrect on a later mount.
 *
 * Harness derived from WorkspacePane.compose-multi-tab.test.tsx (home surface:
 * entityContext null, no analysis/compose launch, activeLayout null so the ONLY
 * tabs come from dispatched events / restore). Test category per ADR-038:
 * Component Test (KEEP — SpaarkeAi presenter render contract).
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen, waitFor, act, cleanup } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider, usePaneEvent } from '@spaarke/ai-widgets';
import { COMPOSE_RUN_STATE_STORAGE_KEY, COMPOSE_RUN_STATE_TTL_MS } from '../composeRunPersistence';

// ---------------------------------------------------------------------------
// Controllable fetch mock — GET /tabs 200-empty (no BFF restore), PATCH /tabs
// 204, GET /compose-outputs returns from a per-test queue.
// ---------------------------------------------------------------------------

let composeOutputsQueue: unknown[][] = [];
let composeOutputsCallCount = 0;

const authenticatedFetchMock = jest.fn(async (url: string, init?: RequestInit): Promise<Response> => {
  const method = init?.method ?? 'GET';
  if (method === 'GET' && url.includes('/compose-outputs')) {
    const idx = Math.min(composeOutputsCallCount, composeOutputsQueue.length - 1);
    const body = composeOutputsQueue.length > 0 ? composeOutputsQueue[idx] : [];
    composeOutputsCallCount += 1;
    return { ok: true, status: 200, json: async () => body } as Partial<Response> as Response;
  }
  if (method === 'GET' && url.includes('/tabs')) {
    return { ok: true, status: 200, json: async () => ({ tabs: [], activeTabId: null }) } as Partial<Response> as Response;
  }
  if (method === 'PATCH' && url.includes('/tabs')) {
    return { ok: true, status: 204, json: async () => ({}) } as Partial<Response> as Response;
  }
  return { ok: true, status: 200, json: async () => ({ acknowledged: true }) } as Partial<Response> as Response;
});

jest.mock('@spaarke/ai-widgets', () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const actual = jest.requireActual('@spaarke/ai-widgets') as any;
  // Compose stub renders the seed's document identity + threaded composeSessionId
  // so restore + seed-injection are DOM-observable.
  const composeStub: React.FC<{ data?: unknown }> = ({ data }) => {
    const compose = (data as { compose?: Record<string, unknown> } | null)?.compose ?? {};
    const upload = (compose.upload ?? {}) as { sessionFileId?: string; fileName?: string };
    return (
      <div
        data-testid="compose-stub"
        data-session={String((compose.composeSessionId as string | undefined) ?? '')}
        data-file={String(upload.sessionFileId ?? '')}
      >
        {String(upload.fileName ?? compose.fileName ?? '(blank)')}
      </div>
    );
  };
  const makeStub = (widgetType: string): React.FC<{ data?: unknown }> =>
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
      chatSessionId: 'session-home',
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

jest.mock('../../shell/ThreePaneShell', () => ({
  useAnalysisLaunch: () => null,
  usePaneCollapseContext: () => null,
  useComposeLaunch: () => null,
}));

jest.mock('../../../hooks/useWorkspaceLayouts', () => ({
  useWorkspaceLayouts: () => ({
    layouts: [{ id: 'layout-compose', name: 'Compose', isSystem: true }],
    activeLayout: null, // no auto-install-default: the ONLY tabs come from events/restore
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

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

interface RecordedEvent {
  type: string;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [k: string]: any;
}

function EventRecorder({ sink }: { sink: RecordedEvent[] }): null {
  usePaneEvent('workspace', (event: RecordedEvent) => {
    sink.push(event);
  });
  return null;
}

function renderPane(): { bus: PaneEventBus; events: RecordedEvent[] } {
  const bus = new PaneEventBus();
  const events: RecordedEvent[] = [];
  render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>
        <EventRecorder sink={events} />
        <WorkspacePane />
      </PaneEventBusProvider>
    </FluentProvider>,
  );
  return { bus, events };
}

function composeUploadOpen(fileId: string, fileName = 'NDA.docx') {
  return {
    type: 'widget_load' as const,
    widgetType: 'compose',
    widgetData: {
      compose: { upload: { sessionId: 'session-home', sessionFileId: fileId, fileName }, fileName },
    },
    displayName: fileName,
  };
}

function seedPersisted(tabs: unknown[]): void {
  localStorage.setItem(COMPOSE_RUN_STATE_STORAGE_KEY, JSON.stringify({ version: 1, tabs }));
}

interface ReadTab extends Record<string, unknown> {
  instanceKey: string;
  displayName: string;
  sessionId?: string;
  run?: { inFlight?: boolean; dispatchedAt?: number };
}
function readPersisted(): { version: number; tabs: ReadTab[] } | null {
  const raw = localStorage.getItem(COMPOSE_RUN_STATE_STORAGE_KEY);
  return raw ? (JSON.parse(raw) as { version: number; tabs: ReadTab[] }) : null;
}

beforeEach(() => {
  localStorage.clear();
  authenticatedFetchMock.mockClear();
  composeOutputsQueue = [];
  composeOutputsCallCount = 0;
  if (!Element.prototype.scrollIntoView) {
    // eslint-disable-next-line @typescript-eslint/no-empty-function
    Element.prototype.scrollIntoView = function (): void {};
  }
});

// ---------------------------------------------------------------------------
// (1) persist-on-open
// ---------------------------------------------------------------------------

describe('WorkspacePane item #13 — persist-on-open', () => {
  it('writes the opened home-surface Compose tab to durable localStorage (seed + session)', async () => {
    const { bus } = renderPane();
    await act(async () => {
      bus.dispatch('workspace', composeUploadOpen('file-1'));
    });
    await screen.findByTestId('compose-stub');

    await waitFor(() => {
      expect(readPersisted()).not.toBeNull();
    });
    const snap = readPersisted();
    expect(snap?.tabs).toHaveLength(1);
    expect(snap?.tabs[0].instanceKey).toBe('upload:file-1');
    expect(snap?.tabs[0].sessionId).toBe('session-home');
    expect(snap?.tabs[0].displayName).toBe('NDA.docx');
  });
});

// ---------------------------------------------------------------------------
// (2)/(6) clear-on-explicit-close + not resurrected on remount
// ---------------------------------------------------------------------------

describe('WorkspacePane item #13 — explicit close (agency)', () => {
  it('removes the tab from the snapshot on explicit close and does NOT resurrect it on a later mount', async () => {
    const { bus, events: _events } = renderPane();
    await act(async () => {
      bus.dispatch('workspace', composeUploadOpen('file-2'));
    });
    await screen.findByTestId('compose-stub');
    await waitFor(() => expect(readPersisted()?.tabs).toHaveLength(1));

    // Explicit close via the tab's close affordance (aria-label "Close NDA.docx").
    const closeBtn = await screen.findByRole('button', { name: /close nda\.docx/i });
    await act(async () => {
      closeBtn.click();
    });

    await waitFor(() => {
      expect(readPersisted()).toBeNull();
    });

    // A fresh mount must NOT reopen it (deliberate close is respected).
    renderPane();
    await new Promise(r => setTimeout(r, 50));
    expect(screen.queryByTestId('compose-stub')).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// (3) cold-load restore
// ---------------------------------------------------------------------------

describe('WorkspacePane item #13 — cold-load restore', () => {
  it('reopens a fresh persisted Compose tab and threads composeSessionId into the seed', async () => {
    seedPersisted([
      {
        instanceKey: 'upload:file-9',
        widgetType: 'compose',
        widgetData: { compose: { upload: { sessionFileId: 'file-9', fileName: 'Restored.docx' }, fileName: 'Restored.docx' } },
        displayName: 'Restored.docx',
        savedAt: Date.now(),
        sessionId: 'sess-restore',
      },
    ]);

    renderPane();

    const stub = await screen.findByTestId('compose-stub', undefined, { timeout: 3000 });
    // Seed-injection: composeSessionId threaded so ComposeWorkspace resumes the findings-bearing session.
    expect(stub).toHaveAttribute('data-session', 'sess-restore');
    expect(stub).toHaveAttribute('data-file', 'file-9');
    // The restored tab header is present.
    expect(await screen.findByRole('tab', { name: /restored\.docx/i })).toBeInTheDocument();
  });

  it('does NOT resume (no spinner) when the persisted run is absent / not in-flight', async () => {
    seedPersisted([
      {
        instanceKey: 'upload:file-10',
        widgetType: 'compose',
        widgetData: { compose: { upload: { sessionFileId: 'file-10', fileName: 'Done.docx' }, fileName: 'Done.docx' } },
        displayName: 'Done.docx',
        savedAt: Date.now(),
        sessionId: 'sess-done',
        run: { inFlight: false, dispatchedAt: Date.now() },
      },
    ]);
    renderPane();
    await screen.findByTestId('compose-stub', undefined, { timeout: 3000 });
    await new Promise(r => setTimeout(r, 50));
    expect(screen.queryByRole('status', { name: /review running/i })).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// (5) TTL expiry
// ---------------------------------------------------------------------------

describe('WorkspacePane item #13 — TTL freshness', () => {
  it('does NOT restore a stale (past-TTL) persisted Compose tab', async () => {
    seedPersisted([
      {
        instanceKey: 'upload:file-stale',
        widgetType: 'compose',
        widgetData: { compose: { upload: { sessionFileId: 'file-stale', fileName: 'Old.docx' }, fileName: 'Old.docx' } },
        displayName: 'Old.docx',
        savedAt: Date.now() - COMPOSE_RUN_STATE_TTL_MS - 60_000,
        sessionId: 'sess-old',
      },
    ]);
    renderPane();
    await new Promise(r => setTimeout(r, 80));
    expect(screen.queryByTestId('compose-stub')).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// (4) run-in-flight resume — spinner + poll → materialize
// ---------------------------------------------------------------------------

describe('WorkspacePane item #13 — run-in-flight resume', () => {
  it('shows the tab spinner then dispatches compose_advisory_comments once findings arrive after empty', async () => {
    jest.useFakeTimers();
    try {
      // First poll → empty ledger; subsequent polls → findings present.
      composeOutputsQueue = [
        [], // first check: run still in flight
        [
          {
            key: 'b1@t1',
            bindingId: 'b1',
            turn: 1,
            disposition: 'compose',
            payload: {
              overallRisk: 'high',
              flaggedSections: [{ quotedText: 'Clause 4', explanation: 'One-sided indemnity' }],
            },
          },
        ],
      ];

      seedPersisted([
        {
          instanceKey: 'upload:file-run',
          widgetType: 'compose',
          widgetData: { compose: { upload: { sessionFileId: 'file-run', fileName: 'Running.docx' }, fileName: 'Running.docx' } },
          displayName: 'Running.docx',
          savedAt: Date.now(),
          sessionId: 'sess-run',
          run: { inFlight: true, dispatchedAt: Date.now() },
        },
      ]);

      const { events } = renderPane();

      // Flush the restore setTimeout(0) → widget_load + runResumePoll (spinner + immediate empty poll).
      await act(async () => {
        jest.advanceTimersByTime(1);
        await Promise.resolve();
        await Promise.resolve();
        await Promise.resolve();
      });

      // Spinner is showing while the resume poll is in flight (findings not yet arrived).
      await waitFor(() => {
        expect(screen.queryByRole('status', { name: /review running/i })).not.toBeNull();
      });

      // Advance past the 5s poll interval → second poll sees findings → dispatch.
      await act(async () => {
        jest.advanceTimersByTime(5000);
        await Promise.resolve();
        await Promise.resolve();
        await Promise.resolve();
      });

      const advisory = events.find(e => e.type === 'compose_advisory_comments');
      expect(advisory).toBeDefined();
      expect(advisory?.overallRisk).toBe('high');
      expect(advisory?.sessionId).toBe('sess-run');
      expect(advisory?.advisoryComments).toHaveLength(1);
      expect(advisory?.advisoryComments[0].targetText).toBe('Clause 4');

      // Spinner clears after findings materialize + the in-flight flag is cleared durably.
      await waitFor(() => {
        expect(screen.queryByRole('status', { name: /review running/i })).toBeNull();
      });
      const after = readPersisted();
      expect(after?.tabs[0].run?.inFlight ?? false).toBe(false);
    } finally {
      jest.runOnlyPendingTimers();
      jest.useRealTimers();
    }
  });
});

// ---------------------------------------------------------------------------
// UAT round-6 item #15a — DISPATCH-TIME run flag (the core fix)
// ---------------------------------------------------------------------------

describe('WorkspacePane item #15a — dispatch-time run flag', () => {
  it('stamps the active Compose tab in-flight when a review is DISPATCHED (nda_review_dispatch_active:true) — no dismiss required', async () => {
    const { bus } = renderPane();
    await act(async () => {
      bus.dispatch('workspace', composeUploadOpen('file-d1'));
    });
    await screen.findByTestId('compose-stub');
    await waitFor(() => expect(readPersisted()?.tabs).toHaveLength(1));
    // Precondition: nothing in-flight yet (no review dispatched).
    expect(readPersisted()?.tabs[0].run?.inFlight ?? false).toBe(false);

    // The dispatch chokepoint fires — the owner never dismissed the modal; this is the ONLY trigger now.
    await act(async () => {
      bus.dispatch('workspace', { type: 'nda_review_dispatch_active', dispatchActive: true });
    });

    await waitFor(() => expect(readPersisted()?.tabs[0].run?.inFlight).toBe(true));
    expect(typeof readPersisted()?.tabs[0].run?.dispatchedAt).toBe('number');
    expect(readPersisted()?.tabs[0].sessionId).toBe('session-home');
  });

  it('the round-4 dismiss signal (nda_review_background_run) NO LONGER stamps persistence (dismiss is now a UI-only concern)', async () => {
    const { bus } = renderPane();
    await act(async () => {
      bus.dispatch('workspace', composeUploadOpen('file-d2'));
    });
    await screen.findByTestId('compose-stub');
    await waitFor(() => expect(readPersisted()?.tabs).toHaveLength(1));

    await act(async () => {
      bus.dispatch('workspace', { type: 'nda_review_background_run', backgroundRunActive: true });
    });
    // Give any (would-be) write a chance to land, then assert it did NOT stamp a run flag.
    await new Promise(r => setTimeout(r, 30));
    expect(readPersisted()?.tabs[0].run).toBeUndefined();
  });

  it('clears the in-flight flag on COMPLETION (compose_advisory_comments for that session) — incl. a zero-findings clean review', async () => {
    const { bus } = renderPane();
    await act(async () => {
      bus.dispatch('workspace', composeUploadOpen('file-d3'));
    });
    await screen.findByTestId('compose-stub');
    await waitFor(() => expect(readPersisted()?.tabs).toHaveLength(1));
    await act(async () => {
      bus.dispatch('workspace', { type: 'nda_review_dispatch_active', dispatchActive: true });
    });
    await waitFor(() => expect(readPersisted()?.tabs[0].run?.inFlight).toBe(true));

    // A zero-findings clean review still dispatches compose_advisory_comments (empty array) — the flag
    // is keyed by the completing session, not the active tab.
    await act(async () => {
      bus.dispatch('workspace', {
        type: 'compose_advisory_comments',
        advisoryComments: [],
        overallRisk: 'low',
        sessionId: 'session-home',
        timestamp: new Date().toISOString(),
      });
    });

    await waitFor(() => expect(readPersisted()?.tabs[0].run?.inFlight).toBe(false));
  });

  it('clears the in-flight flag on dispatch FAILURE (nda_review_dispatch_active:false)', async () => {
    const { bus } = renderPane();
    await act(async () => {
      bus.dispatch('workspace', composeUploadOpen('file-d4'));
    });
    await screen.findByTestId('compose-stub');
    await waitFor(() => expect(readPersisted()?.tabs).toHaveLength(1));
    await act(async () => {
      bus.dispatch('workspace', { type: 'nda_review_dispatch_active', dispatchActive: true });
    });
    await waitFor(() => expect(readPersisted()?.tabs[0].run?.inFlight).toBe(true));

    await act(async () => {
      bus.dispatch('workspace', { type: 'nda_review_dispatch_active', dispatchActive: false });
    });

    await waitFor(() => expect(readPersisted()?.tabs[0].run?.inFlight).toBe(false));
  });

  it('the undismissed-modal path: a dispatch-time stamp survives a full navigate-away/return and resumes the spinner + poll (NO modal on restore — the spinner carries it alone)', async () => {
    // Mount 1 (real timers): open the tab, then stamp at DISPATCH time. The user never dismisses the
    // modal — this is the exact owner repro that round-4's dismiss-only stamping missed.
    const first = renderPane();
    await act(async () => {
      first.bus.dispatch('workspace', composeUploadOpen('file-d5', 'Undismissed.docx'));
    });
    await screen.findByTestId('compose-stub');
    await waitFor(() => expect(readPersisted()?.tabs).toHaveLength(1));
    await act(async () => {
      first.bus.dispatch('workspace', { type: 'nda_review_dispatch_active', dispatchActive: true });
    });
    await waitFor(() => expect(readPersisted()?.tabs[0].run?.inFlight).toBe(true));

    // Navigate away → return: unmount, then a fresh cold mount reads the SAME (undismissed) localStorage.
    cleanup();

    jest.useFakeTimers();
    try {
      composeOutputsQueue = [
        [], // first poll: still in flight
        [
          {
            key: 'b1@t1',
            bindingId: 'b1',
            turn: 1,
            disposition: 'compose',
            payload: {
              overallRisk: 'high',
              flaggedSections: [{ quotedText: 'Clause 9', explanation: 'One-sided' }],
            },
          },
        ],
      ];
      composeOutputsCallCount = 0;

      const second = renderPane();
      // Flush the restore setTimeout(0) → widget_load + runResumePoll (spinner + immediate empty poll).
      await act(async () => {
        jest.advanceTimersByTime(1);
        await Promise.resolve();
        await Promise.resolve();
        await Promise.resolve();
      });
      await waitFor(() => {
        expect(screen.queryByRole('status', { name: /review running/i })).not.toBeNull();
      });

      // Second poll (past the 5s interval) sees findings → materialize via compose_advisory_comments.
      await act(async () => {
        jest.advanceTimersByTime(5000);
        await Promise.resolve();
        await Promise.resolve();
        await Promise.resolve();
      });
      const advisory = second.events.find(e => e.type === 'compose_advisory_comments');
      expect(advisory).toBeDefined();
      expect(advisory?.sessionId).toBe('session-home');
      expect(advisory?.advisoryComments).toHaveLength(1);

      // Spinner clears + the flag is cleared durably.
      await waitFor(() => {
        expect(screen.queryByRole('status', { name: /review running/i })).toBeNull();
      });
    } finally {
      jest.runOnlyPendingTimers();
      jest.useRealTimers();
    }
  });
});
