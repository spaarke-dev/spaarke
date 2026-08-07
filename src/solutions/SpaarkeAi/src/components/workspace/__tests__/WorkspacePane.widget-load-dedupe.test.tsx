/**
 * WorkspacePane — generic `widget_load` de-dup guard (spaarkeai-assistant-
 * enhancements-r2, Phase 0 Fix 1).
 *
 * UAT: asking the Assistant "do you see the daily briefing tab?" (a second
 * `widget_load` for an already-open workspace LAYOUT) opened a SECOND Daily
 * Briefing tab instead of focusing the existing one. Root cause: the generic
 * `widget_load` handler's `manager.addTab(...)` call had no de-dup guard,
 * unlike the compose branch's instance-keyed reuse and the startup-default
 * effect's `layoutId` match.
 *
 * This suite asserts the fix directly against the generic path (bypassing
 * the compose branch, which already had its own reuse logic and is covered
 * elsewhere):
 *
 *   1. A second `widget_load` for a 'workspace' LAYOUT already open (same
 *      `widgetData.layoutId`) reuses + focuses the existing tab — no second
 *      tab is created.
 *   2. A `widget_load` for a DIFFERENT layoutId still opens a NEW tab — the
 *      'workspace' registry entry is itself allowMultiple:true (different
 *      layouts may coexist side-by-side); only the SAME layoutId de-dupes.
 *   3. A second `widget_load` for a singleton widget type (registry
 *      `allowMultiple: false`) reuses + focuses the existing tab.
 *   4. A second `widget_load` for an `allowMultiple: true` widget type still
 *      stacks a new tab (unaffected by the guard) — e.g. email / document
 *      viewer semantics are preserved.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { act, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';

// ---------------------------------------------------------------------------
// Mock `@spaarke/ai-widgets` — keep the real bus + provider + types, but
// override `useAiSession` (minimal auth stub), `resolveWorkspaceWidget`
// (synchronous stub component), and `getWorkspaceWidgetMetadata` (per-type
// allowMultiple so both branches of the de-dup guard are exercisable).
// ---------------------------------------------------------------------------

const stubWidgetRenderCounts: Record<string, number> = {};

jest.mock('@spaarke/ai-widgets', () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const actual = jest.requireActual('@spaarke/ai-widgets') as any;

  const makeStub = (widgetType: string): React.FC<{ data?: unknown }> => {
    return function StubWidget(): React.JSX.Element {
      stubWidgetRenderCounts[widgetType] = (stubWidgetRenderCounts[widgetType] ?? 0) + 1;
      return <div data-testid={`widget-stub-${widgetType}`}>{widgetType}</div>;
    };
  };

  const METADATA_BY_TYPE: Record<string, { displayName: string; allowMultiple: boolean }> = {
    workspace: { displayName: 'Workspace', allowMultiple: true },
    'singleton-widget': { displayName: 'Singleton Widget', allowMultiple: false },
    'multi-widget': { displayName: 'Multi Widget', allowMultiple: true },
  };

  return {
    ...actual,
    useAiSession: () => ({
      isAuthenticated: true,
      authenticatedFetch: jest.fn().mockResolvedValue({
        ok: false,
        status: 404,
        json: async () => ({}),
      } as Partial<Response> as Response),
      getAccessToken: jest.fn().mockResolvedValue('test-token'),
      bffBaseUrl: 'https://test-bff.example.com',
      tenantId: 'test-tenant',
      chatSessionId: 'session-aaa',
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
    resolveWorkspaceWidget: jest.fn(async (widgetType: string) => {
      return makeStub(widgetType);
    }),
    getWorkspaceWidgetMetadata: jest.fn((widgetType: string) => {
      const meta = METADATA_BY_TYPE[widgetType];
      return {
        displayName: meta?.displayName ?? widgetType,
        category: 'analysis',
        defaultOrder: 100,
        allowMultiple: meta?.allowMultiple ?? true,
      };
    }),
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
  pinWorkspace: jest.fn(),
  unpinWorkspace: jest.fn(),
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

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

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

async function flushAsyncWork(): Promise<void> {
  await act(async () => {
    for (let i = 0; i < 4; i++) {
      await Promise.resolve();
    }
    await new Promise(resolve => setTimeout(resolve, 0));
    for (let i = 0; i < 4; i++) {
      await Promise.resolve();
    }
  });
}

beforeEach(() => {
  for (const key of Object.keys(stubWidgetRenderCounts)) {
    delete stubWidgetRenderCounts[key];
  }
  if (!Element.prototype.scrollIntoView) {
    // eslint-disable-next-line @typescript-eslint/no-empty-function
    Element.prototype.scrollIntoView = function (): void {};
  }
});

// ---------------------------------------------------------------------------
// (1) Same layoutId — second widget_load reuses + focuses, no new tab
// ---------------------------------------------------------------------------

describe('WorkspacePane — widget_load de-dup guard (Fix 1)', () => {
  it('reuses + focuses an already-open workspace LAYOUT tab on a second widget_load for the same layoutId', async () => {
    const { bus } = renderPane();
    await flushAsyncWork();

    act(() => {
      bus.dispatch('workspace', {
        type: 'widget_load',
        widgetType: 'workspace',
        widgetData: { layoutId: 'layout-daily-briefing', layoutName: 'Daily Briefing' },
        displayName: 'Daily Briefing',
      });
    });
    await flushAsyncWork();

    expect(screen.getAllByRole('tab', { name: /daily briefing/i })).toHaveLength(1);

    // Second dispatch — "do you see the daily briefing tab?" style re-open.
    act(() => {
      bus.dispatch('workspace', {
        type: 'widget_load',
        widgetType: 'workspace',
        widgetData: { layoutId: 'layout-daily-briefing', layoutName: 'Daily Briefing' },
        displayName: 'Daily Briefing',
      });
    });
    await flushAsyncWork();

    // Still exactly ONE Daily Briefing tab — no duplicate.
    const tabs = screen.getAllByRole('tab', { name: /daily briefing/i });
    expect(tabs).toHaveLength(1);
    await waitFor(() => {
      expect(tabs[0].getAttribute('aria-selected')).toBe('true');
    });
  });

  // -------------------------------------------------------------------------
  // (2) Different layoutId — two DISTINCT layout tabs coexist
  // -------------------------------------------------------------------------

  it('opens a NEW tab for a different layoutId (different layouts may coexist)', async () => {
    const { bus } = renderPane();
    await flushAsyncWork();

    act(() => {
      bus.dispatch('workspace', {
        type: 'widget_load',
        widgetType: 'workspace',
        widgetData: { layoutId: 'layout-a', layoutName: 'Corporate Workspace' },
        displayName: 'Corporate Workspace',
      });
    });
    await flushAsyncWork();

    act(() => {
      bus.dispatch('workspace', {
        type: 'widget_load',
        widgetType: 'workspace',
        widgetData: { layoutId: 'layout-b', layoutName: 'Litigation Workspace' },
        displayName: 'Litigation Workspace',
      });
    });
    await flushAsyncWork();

    expect(screen.getByRole('tab', { name: /corporate workspace/i })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /litigation workspace/i })).toBeInTheDocument();
  });

  // -------------------------------------------------------------------------
  // (3) Singleton widget type (allowMultiple: false) — de-dupes by widgetType
  // -------------------------------------------------------------------------

  it('reuses + focuses an already-open singleton (allowMultiple:false) widget tab', async () => {
    const { bus } = renderPane();
    await flushAsyncWork();

    act(() => {
      bus.dispatch('workspace', {
        type: 'widget_load',
        widgetType: 'singleton-widget',
        widgetData: { some: 'payload-1' },
      });
    });
    await flushAsyncWork();

    expect(screen.getAllByRole('tab', { name: /singleton widget/i })).toHaveLength(1);

    act(() => {
      bus.dispatch('workspace', {
        type: 'widget_load',
        widgetType: 'singleton-widget',
        widgetData: { some: 'payload-2' },
      });
    });
    await flushAsyncWork();

    const tabs = screen.getAllByRole('tab', { name: /singleton widget/i });
    expect(tabs).toHaveLength(1);
    await waitFor(() => {
      expect(tabs[0].getAttribute('aria-selected')).toBe('true');
    });
  });

  // -------------------------------------------------------------------------
  // (4) allowMultiple:true widget type — still stacks (unaffected)
  // -------------------------------------------------------------------------

  it('still stacks a new tab for an allowMultiple:true widget type', async () => {
    const { bus } = renderPane();
    await flushAsyncWork();

    act(() => {
      bus.dispatch('workspace', {
        type: 'widget_load',
        widgetType: 'multi-widget',
        widgetData: { instance: 1 },
      });
    });
    await flushAsyncWork();

    act(() => {
      bus.dispatch('workspace', {
        type: 'widget_load',
        widgetType: 'multi-widget',
        widgetData: { instance: 2 },
      });
    });
    await flushAsyncWork();

    // TWO stacked tabs — the guard does not touch allowMultiple:true types.
    expect(screen.getAllByRole('tab', { name: /multi widget/i })).toHaveLength(2);
  });
});
