/**
 * WorkspaceWidgetWrapper — serialize/restore integration tests
 *
 * Covers the acceptance criteria from AIPU2-080:
 *   (a) Widget renders from SSE event data (data prop is passed through to R1 widget).
 *   (b) serializeState() returns only identifiers — not full data payloads.
 *   (c) restoreState() updates queryParams ref and sets isRestoring, enabling
 *       the shell to re-fetch fresh data.
 *
 * Two widgets are tested end-to-end:
 *   - BudgetDashboard (financial, allowMultiple=false)
 *   - SearchResults   (search, allowMultiple=true)
 *
 * React 19 testing via @testing-library/react.
 */

import '@testing-library/jest-dom';
import React from 'react';
import { render, screen, act, waitFor } from '@testing-library/react';
import { createWorkspaceWrapper, WorkspaceWidgetHandle } from '../WorkspaceWidgetWrapper';
import type { WorkspaceWidgetWrapperProps } from '../WorkspaceWidgetWrapper';
import { PaneEventBusProvider } from '../../../events/PaneEventBusContext';

// NOTE (test-repair task 021, 2026-07-08): this file used
// `expect(...).toBeInTheDocument()` without importing '@testing-library/jest-dom',
// which registers that matcher. This was previously masked by the
// PaneEventBusProvider crash below (every test failed before reaching the
// jest-dom assertions), so the missing import went unnoticed until that bug
// was fixed.

// NOTE (test-repair task 021, 2026-07-08): WorkspaceWidgetWrapper's
// WrappedWidget calls useCitationLink() unconditionally (see
// WorkspaceWidgetWrapper.tsx around the `useCitationLink` call site), which
// requires a <PaneEventBusProvider> ancestor (usePaneEventBus() throws
// otherwise). This test file never wrapped its render() calls in a provider
// — every test crashed with "[PaneEventBus] ... must be called inside a
// <PaneEventBusProvider>". Wrapping with <PaneEventBusProvider> (no explicit
// bus needed — it creates its own stable instance) fixes this without
// touching production code or weakening any assertion.

// ---------------------------------------------------------------------------
// Mock Fluent UI components used by the wrapper's loading/error states
// ---------------------------------------------------------------------------

jest.mock('@fluentui/react-components', () => ({
  makeStyles: () => () => ({}),
  mergeClasses: (...args: string[]) => args.filter(Boolean).join(' '),
  tokens: {
    spacingVerticalM: '12px',
    spacingHorizontalL: '16px',
    spacingHorizontalXXL: '32px',
    colorStatusDangerForeground1: 'red',
  },
  Spinner: ({ label }: { label: string }) => <div data-testid="spinner">{label}</div>,
  Text: ({ children, className }: { children: React.ReactNode; className?: string }) => (
    <span className={className}>{children}</span>
  ),
}));

// ---------------------------------------------------------------------------
// Mock R1 BudgetDashboard widget
// ---------------------------------------------------------------------------

interface BudgetData {
  title: string;
  items: { label: string; spent: number; budget: number; currency: string }[];
}

const MockBudgetWidget: React.FC<{ data: BudgetData; isLoading?: boolean; error?: string }> = ({
  data,
  isLoading,
  error,
}) => {
  if (isLoading) return <div data-testid="budget-loading">Loading budget...</div>;
  if (error) return <div data-testid="budget-error">{error}</div>;
  return <div data-testid="budget-dashboard">{data.title}</div>;
};

const budgetLoader = jest.fn(() => Promise.resolve({ default: MockBudgetWidget as React.ComponentType<any> }));

// ---------------------------------------------------------------------------
// Mock R1 SearchResults widget
// ---------------------------------------------------------------------------

interface SearchData {
  query: string;
  results: { id: string; title: string; excerpt: string; score: number }[];
}

const MockSearchWidget: React.FC<{ data: SearchData; isLoading?: boolean; error?: string }> = ({
  data,
  isLoading,
  error,
}) => {
  if (isLoading) return <div data-testid="search-loading">Searching...</div>;
  if (error) return <div data-testid="search-error">{error}</div>;
  return <div data-testid="search-results">{data.query}</div>;
};

const searchLoader = jest.fn(() => Promise.resolve({ default: MockSearchWidget as React.ComponentType<any> }));

// ---------------------------------------------------------------------------
// Widget wrapper instances under test
// ---------------------------------------------------------------------------

const BudgetWrapper = createWorkspaceWrapper<BudgetData>(budgetLoader, 'BudgetDashboard', 1);

const SearchWrapper = createWorkspaceWrapper<SearchData>(searchLoader, 'SearchResults', 1);

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const defaultBudgetData: BudgetData = {
  title: 'Q3 Matter Budget',
  items: [{ label: 'Legal Fees', spent: 5000, budget: 10000, currency: 'USD' }],
};

const defaultSearchData: SearchData = {
  query: 'contract termination clause',
  results: [{ id: 'r1', title: 'Smith v. Jones', excerpt: 'Excerpt text', score: 0.9 }],
};

const defaultQueryParams = {
  sessionId: 'sess-abc-123',
  turnId: '3',
};

function renderBudgetWrapper(props: Partial<WorkspaceWidgetWrapperProps<BudgetData>> = {}) {
  const handle: { current: WorkspaceWidgetHandle | null } = { current: null };
  const result = render(
    <PaneEventBusProvider>
      <BudgetWrapper
        data={defaultBudgetData}
        widgetType="BudgetDashboard"
        queryParams={defaultQueryParams}
        onRegisterHandle={h => {
          handle.current = h;
        }}
        {...props}
      />
    </PaneEventBusProvider>
  );
  return { ...result, handle };
}

function renderSearchWrapper(props: Partial<WorkspaceWidgetWrapperProps<SearchData>> = {}) {
  const handle: { current: WorkspaceWidgetHandle | null } = { current: null };
  const result = render(
    <PaneEventBusProvider>
      <SearchWrapper
        data={defaultSearchData}
        widgetType="SearchResults"
        queryParams={defaultQueryParams}
        onRegisterHandle={h => {
          handle.current = h;
        }}
        {...props}
      />
    </PaneEventBusProvider>
  );
  return { ...result, handle };
}

// ---------------------------------------------------------------------------
// Setup / teardown
// ---------------------------------------------------------------------------

beforeEach(() => {
  jest.clearAllMocks();
});

// ---------------------------------------------------------------------------
// (a) Widget renders from SSE event data
// ---------------------------------------------------------------------------

describe('WorkspaceWidgetWrapper — renders from data prop', () => {
  it('BudgetDashboard: renders R1 widget with data from SSE event', async () => {
    renderBudgetWrapper();

    await waitFor(() => {
      expect(screen.getByTestId('budget-dashboard')).toBeInTheDocument();
    });

    expect(screen.getByTestId('budget-dashboard')).toHaveTextContent('Q3 Matter Budget');
  });

  it('SearchResults: renders R1 widget with data from SSE event', async () => {
    renderSearchWrapper();

    await waitFor(() => {
      expect(screen.getByTestId('search-results')).toBeInTheDocument();
    });

    expect(screen.getByTestId('search-results')).toHaveTextContent('contract termination clause');
  });

  it('BudgetDashboard: shows loading state when isLoading=true', async () => {
    renderBudgetWrapper({ isLoading: true });

    await waitFor(() => {
      expect(screen.getByTestId('budget-loading')).toBeInTheDocument();
    });
  });

  it('SearchResults: shows error state when error is set', async () => {
    renderSearchWrapper({ error: 'BFF returned 503' });

    await waitFor(() => {
      expect(screen.getByTestId('search-error')).toBeInTheDocument();
    });

    expect(screen.getByTestId('search-error')).toHaveTextContent('BFF returned 503');
  });

  it('BudgetDashboard: shows wrapper loading spinner before module resolves', () => {
    // Replace loader with a pending promise to freeze the module load
    const pendingLoader = jest.fn(() => new Promise<{ default: React.ComponentType<any> }>(() => {}));
    const FrozenWrapper = createWorkspaceWrapper<BudgetData>(pendingLoader, 'BudgetDashboard');

    render(
      <PaneEventBusProvider>
        <FrozenWrapper data={defaultBudgetData} widgetType="BudgetDashboard" queryParams={defaultQueryParams} />
      </PaneEventBusProvider>
    );

    // While the module promise is pending, the wrapper shows its own Spinner
    expect(screen.getByTestId('spinner')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// (b) serializeState returns only identifiers — not full data payloads
// ---------------------------------------------------------------------------

describe('WorkspaceWidgetWrapper — serializeState()', () => {
  it('BudgetDashboard: serializeState returns widgetType and version', async () => {
    const { handle } = renderBudgetWrapper();

    await waitFor(() => expect(handle.current).not.toBeNull());

    const state = handle.current!.serializeState();

    expect(state.widgetType).toBe('BudgetDashboard');
    expect(state.version).toBe(1);
  });

  it('BudgetDashboard: serializeState includes only query identifiers — not data', async () => {
    const { handle } = renderBudgetWrapper({
      queryParams: { sessionId: 'sess-xyz', turnId: '7' },
    });

    await waitFor(() => expect(handle.current).not.toBeNull());

    const state = handle.current!.serializeState();

    // Must have the identifiers
    expect(state.queryParams.sessionId).toBe('sess-xyz');
    expect(state.queryParams.turnId).toBe('7');

    // Must NOT contain the full data payload (D-08)
    expect(state.queryParams).not.toHaveProperty('title');
    expect(state.queryParams).not.toHaveProperty('items');
    expect((state as any).data).toBeUndefined();
    expect((state as any).items).toBeUndefined();
  });

  it('SearchResults: serializeState returns identifiers not search result data', async () => {
    const { handle } = renderSearchWrapper({
      queryParams: { sessionId: 'sess-search-01', turnId: '2', searchQuery: 'force majeure' },
    });

    await waitFor(() => expect(handle.current).not.toBeNull());

    const state = handle.current!.serializeState();

    expect(state.widgetType).toBe('SearchResults');
    expect(state.queryParams.sessionId).toBe('sess-search-01');
    expect(state.queryParams.turnId).toBe('2');
    expect(state.queryParams.searchQuery).toBe('force majeure');

    // Must NOT contain result data
    expect(state.queryParams).not.toHaveProperty('results');
    expect(state.queryParams).not.toHaveProperty('query');
    expect((state as any).results).toBeUndefined();
  });

  it('serializeState includes an ISO 8601 timestamp', async () => {
    const { handle } = renderBudgetWrapper();

    await waitFor(() => expect(handle.current).not.toBeNull());

    const state = handle.current!.serializeState();

    expect(state.timestamp).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}/);
  });

  it('serializeState picks up updated queryParams', async () => {
    const initialParams = { sessionId: 'sess-initial', turnId: '1' };
    const { handle, rerender } = renderBudgetWrapper({ queryParams: initialParams });

    await waitFor(() => expect(handle.current).not.toBeNull());

    // Update queryParams via rerender (simulates shell updating props after a new turn)
    rerender(
      <PaneEventBusProvider>
        <BudgetWrapper
          data={defaultBudgetData}
          widgetType="BudgetDashboard"
          queryParams={{ sessionId: 'sess-initial', turnId: '5' }}
          onRegisterHandle={h => {
            handle.current = h;
          }}
        />
      </PaneEventBusProvider>
    );

    const state = handle.current!.serializeState();
    expect(state.queryParams.turnId).toBe('5');
  });
});

// ---------------------------------------------------------------------------
// (c) restoreState calls BFF indirectly — sets isRestoring so shell re-fetches
// ---------------------------------------------------------------------------

describe('WorkspaceWidgetWrapper — restoreState()', () => {
  it('BudgetDashboard: restoreState resolves without throwing', async () => {
    const { handle } = renderBudgetWrapper();

    await waitFor(() => expect(handle.current).not.toBeNull());

    await expect(
      handle.current!.restoreState({
        widgetType: 'BudgetDashboard',
        version: 1,
        queryParams: { sessionId: 'sess-restored', turnId: '2' },
        timestamp: new Date().toISOString(),
      })
    ).resolves.toBeUndefined();
  });

  it('SearchResults: restoreState resolves without throwing', async () => {
    const { handle } = renderSearchWrapper();

    await waitFor(() => expect(handle.current).not.toBeNull());

    await expect(
      handle.current!.restoreState({
        widgetType: 'SearchResults',
        version: 1,
        queryParams: { sessionId: 'sess-sr-99', turnId: '1', searchQuery: 'indemnity clause' },
        timestamp: new Date().toISOString(),
      })
    ).resolves.toBeUndefined();
  });

  it('BudgetDashboard: after restoreState, serializeState returns the restored queryParams', async () => {
    const { handle } = renderBudgetWrapper({
      queryParams: { sessionId: 'sess-old', turnId: '1' },
    });

    await waitFor(() => expect(handle.current).not.toBeNull());

    await act(async () => {
      await handle.current!.restoreState({
        widgetType: 'BudgetDashboard',
        version: 1,
        queryParams: { sessionId: 'sess-restored', turnId: '9' },
        timestamp: new Date().toISOString(),
      });
    });

    // After restore, serializeState should reflect the restored identifiers
    const state = handle.current!.serializeState();
    expect(state.queryParams.sessionId).toBe('sess-restored');
    expect(state.queryParams.turnId).toBe('9');
  });

  it('SearchResults: restoreState stores layout hints in next serializeState', async () => {
    const { handle } = renderSearchWrapper();

    await waitFor(() => expect(handle.current).not.toBeNull());

    const layoutHint = { scrollTop: 240, activeSectionIndex: 2 };

    await act(async () => {
      await handle.current!.restoreState({
        widgetType: 'SearchResults',
        version: 1,
        queryParams: { sessionId: 'sess-sr-01', turnId: '4' },
        layout: layoutHint,
        timestamp: new Date().toISOString(),
      });
    });

    const state = handle.current!.serializeState();
    expect(state.layout).toEqual(layoutHint);
  });

  it('restoreState shows Spinner overlay while waiting for fresh data', async () => {
    // Start with isLoading=false (data is rendered)
    const { handle, rerender } = renderBudgetWrapper({ isLoading: false });

    await waitFor(() => {
      expect(screen.getByTestId('budget-dashboard')).toBeInTheDocument();
    });

    // NOTE (test-repair task 021, 2026-07-08): per WorkspaceWidgetWrapper.tsx's
    // own contract (see its restoreState()/useEffect comments), isRestoring
    // is cleared as soon as isLoadingProp is (or becomes) false — restoreState()
    // itself does NOT set isLoading; the SHELL is responsible for setting
    // isLoading=true (typically before/at the same time it dispatches the
    // restore), and the wrapper clears isRestoring only on isLoadingProp's
    // true->false transition. The ORIGINAL version of this test called
    // restoreState() while isLoadingProp was STILL false — the effect's
    // `!isLoadingProp && isRestoring` check fired within the same act() batch
    // and immediately cleared isRestoring back to false before the spinner
    // assertion ever ran. Simulate the shell's real sequencing: set
    // isLoading=true FIRST, then trigger the restore, so isRestoring is set
    // while isLoadingProp is already true and the clearing effect can't fire.
    rerender(
      <PaneEventBusProvider>
        <BudgetWrapper
          data={defaultBudgetData}
          widgetType="BudgetDashboard"
          queryParams={{ sessionId: 'sess-x', turnId: '0' }}
          isLoading={true}
          onRegisterHandle={h => {
            handle.current = h;
          }}
        />
      </PaneEventBusProvider>
    );

    // Trigger restore
    await act(async () => {
      await handle.current!.restoreState({
        widgetType: 'BudgetDashboard',
        version: 1,
        queryParams: { sessionId: 'sess-x', turnId: '0' },
        timestamp: new Date().toISOString(),
      });
    });

    // The wrapper is now in isRestoring=true state — shows spinner
    expect(screen.getByTestId('spinner')).toBeInTheDocument();

    // Simulate shell completing re-fetch by setting isLoading=false
    rerender(
      <PaneEventBusProvider>
        <BudgetWrapper
          data={{ title: 'Refreshed Budget', items: [] }}
          widgetType="BudgetDashboard"
          queryParams={{ sessionId: 'sess-x', turnId: '0' }}
          isLoading={false}
          onRegisterHandle={h => {
            handle.current = h;
          }}
        />
      </PaneEventBusProvider>
    );

    // After loading completes, isRestoring clears and real widget renders again
    await waitFor(() => {
      expect(screen.getByTestId('budget-dashboard')).toBeInTheDocument();
    });
    expect(screen.getByTestId('budget-dashboard')).toHaveTextContent('Refreshed Budget');
  });
});

// ---------------------------------------------------------------------------
// (d) Loader is called at most once per wrapper instance (caching)
// ---------------------------------------------------------------------------

describe('WorkspaceWidgetWrapper — loader called once', () => {
  it('BudgetDashboard: R1 module loader is called exactly once on mount', async () => {
    const trackedLoader = jest.fn(() => Promise.resolve({ default: MockBudgetWidget as React.ComponentType<any> }));
    const TrackedWrapper = createWorkspaceWrapper<BudgetData>(trackedLoader, 'BudgetDashboard');

    const { rerender } = render(
      <PaneEventBusProvider>
        <TrackedWrapper data={defaultBudgetData} widgetType="BudgetDashboard" queryParams={defaultQueryParams} />
      </PaneEventBusProvider>
    );

    await waitFor(() => expect(screen.getByTestId('budget-dashboard')).toBeInTheDocument());

    // Re-render with different data — should NOT call the loader again
    rerender(
      <PaneEventBusProvider>
        <TrackedWrapper
          data={{ title: 'New Budget', items: [] }}
          widgetType="BudgetDashboard"
          queryParams={defaultQueryParams}
        />
      </PaneEventBusProvider>
    );

    expect(trackedLoader).toHaveBeenCalledTimes(1);
  });
});
