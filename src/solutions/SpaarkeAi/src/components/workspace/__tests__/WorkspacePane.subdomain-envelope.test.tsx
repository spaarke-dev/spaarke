/**
 * WorkspacePane — subDomain envelope contract (task 022, spec FR-09; hub A3 deferred
 * deep-threading legs).
 *
 * Proves the CONVERGENCE point where the two deep-threading legs this task builds meet the
 * shipped wizard-finish door (`bd64a69d4`) — ONE envelope contract, same field name/semantics:
 *
 *   - Door 2 (cold-load / deep-link, EXPLICIT): `AnalysisLaunchContextValue.subDomain` — set when
 *     the launch URL carried an explicit `subDomain` param (main.tsx → App → ThreePaneShell).
 *     Mocked here directly via `useAnalysisLaunch()`'s return value (the established convention
 *     in `WorkspacePane.analysis-entry.test.tsx` — ThreePaneShell itself is not rendered).
 *   - Door 3 (open-existing DERIVATION): the by-analysis reopen effect's `$expand=sprk_agreementtype
 *     ($select=sprk_key)` — this test's `analysisWizardDataService.retrieveRecord` mock supplies the
 *     expanded lookup.
 *
 * Contract asserted: EXPLICIT (door 2) wins over DERIVED (door 3) when both are present; DERIVED
 * fills when explicit is absent; and the negative case — both absent — leaves the envelope's
 * `subDomain` field ABSENT (no fabricated default) on the dispatched `workspace/widget_load`
 * `compose` seed, which is the exact shape `main.tsx`'s `SpaarkeAiWorkspaceRenderer` already reads
 * `seed.subDomain` from (task 022's leg (b) needed zero main.tsx change for this reason).
 *
 * Harness derived from `WorkspacePane.analysis-entry.test.tsx` (same mocks for `@spaarke/ai-widgets`,
 * `useWorkspaceLayouts`, `pinnedWorkspaces`); `createXrmDataService` is extended here with a real
 * `retrieveRecord` jest mock (the sibling file's version is a bare `{ __kind }` stub with no
 * `retrieveRecord`, so its 'existing' test never reaches the by-analysis Compose dispatch).
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';

const authenticatedFetchMock = jest.fn(async (url: string, init?: RequestInit): Promise<Response> => {
  const method = init?.method ?? 'GET';
  if (method === 'GET' && url.includes('/by-analysis/')) {
    // No bound session — falls through to the document-only Compose surface, which is what
    // this contract test exercises. Mirrors the sibling analysis-entry test's 404 convention.
    return { ok: false, status: 404, json: async () => ({}) } as Partial<Response> as Response;
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
      return <div data-testid={`stub-${widgetType}`} data-worktype={String(d.workTypeValue ?? '')} />;
    };
  return {
    ...actual,
    useAiSession: () => ({
      isAuthenticated: true,
      authenticatedFetch: authenticatedFetchMock,
      getAccessToken: jest.fn().mockResolvedValue('test-token'),
      bffBaseUrl: 'https://test-bff.example.com',
      tenantId: 'test-tenant',
      chatSessionId: 'session-subdomain',
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

// Door 2 (cold-load explicit): mutable per-test mock of the AnalysisLaunchContext value.
// ThreePaneShell itself is not rendered (matches WorkspacePane.analysis-entry.test.tsx
// convention) — this directly supplies whatever the props→context plumbing in main.tsx/App.tsx/
// ThreePaneShell.tsx would have produced for a given cold-load URL.
let mockAnalysisLaunch: { mode: 'new' | 'existing'; analysisId?: string; worktype?: string; subDomain?: string } | null =
  null;
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

// Door 3 (open-existing derivation): a real `retrieveRecord` mock, per-test configurable, standing
// in for the by-analysis `$expand=sprk_documentid(...),sprk_agreementtype($select=sprk_key)` read.
let mockRetrieveRecord: jest.Mock;
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
    createXrmDataService: jest.fn(() => ({
      retrieveRecord: (...args: unknown[]) => mockRetrieveRecord(...args),
    })),
    createXrmNavigationService: jest.fn(() => ({ __kind: 'xrm-nav-service' })),
    searchUsersAndContacts: jest.fn(async () => []),
  };
});

// eslint-disable-next-line import/first
import { WorkspacePane } from '../WorkspacePane';

interface WorkspaceWidgetLoadEvent {
  type: string;
  widgetType?: string;
  widgetData?: { compose?: Record<string, unknown> };
}

function renderPane(): { bus: PaneEventBus; composeLoads: WorkspaceWidgetLoadEvent[] } {
  const bus = new PaneEventBus();
  const composeLoads: WorkspaceWidgetLoadEvent[] = [];
  bus.subscribe('workspace', (e: unknown) => {
    const evt = e as WorkspaceWidgetLoadEvent;
    if (evt.type === 'widget_load' && evt.widgetType === 'compose') {
      composeLoads.push(evt);
    }
  });
  render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>
        <WorkspacePane />
      </PaneEventBusProvider>
    </FluentProvider>,
  );
  return { bus, composeLoads };
}

const FULL_SPE_POINTER = {
  sprk_name: 'Acme MSA',
  sprk_worktype: 100000000,
  _sprk_documentid_value: 'doc-guid-1',
  sprk_documentid: {
    sprk_filename: 'Acme MSA.docx',
    sprk_graphitemid: 'drive-item-1',
    sprk_graphdriveid: 'drive-1',
  },
};

beforeEach(() => {
  authenticatedFetchMock.mockClear();
  mockAnalysisLaunch = null;
  mockRetrieveRecord = jest.fn();
  if (!Element.prototype.scrollIntoView) {
    // eslint-disable-next-line @typescript-eslint/no-empty-function
    Element.prototype.scrollIntoView = function (): void {};
  }
});

describe('WorkspacePane — subDomain envelope contract (task 022, FR-09)', () => {
  it('open-existing derivation: no explicit subDomain → the expanded sprk_agreementtype.sprk_key fills the envelope', async () => {
    mockAnalysisLaunch = { mode: 'existing', analysisId: 'analysis-derive-1' };
    mockRetrieveRecord.mockResolvedValue({
      ...FULL_SPE_POINTER,
      sprk_agreementtype: { sprk_key: 'employment' },
    });

    const { composeLoads } = renderPane();

    await waitFor(() => expect(composeLoads.length).toBeGreaterThan(0));
    expect(composeLoads[0].widgetData?.compose?.subDomain).toBe('employment');
    expect(composeLoads[0].widgetData?.compose?.activeWorkType).toBe('agreement-analysis');

    // Query-shape proof: the $expand now carries BOTH sprk_documentid and sprk_agreementtype,
    // and $select carries the raw FK alongside the pre-existing fields.
    const [, , query] = mockRetrieveRecord.mock.calls[0];
    expect(query as string).toContain('_sprk_agreementtype_value');
    expect(query as string).toContain('sprk_agreementtype($select=sprk_key)');
    // Binding naming rule: NOT the reference table's own PK.
    expect(query as string).not.toContain('sprk_agreementtypeid');
  });

  it('cold-load explicit override: analysisLaunch.subDomain wins over a DIFFERENT derived lookup value', async () => {
    mockAnalysisLaunch = { mode: 'existing', analysisId: 'analysis-explicit-1', subDomain: 'nda' };
    mockRetrieveRecord.mockResolvedValue({
      ...FULL_SPE_POINTER,
      // Deliberately different from the explicit value, to prove priority (not just presence).
      sprk_agreementtype: { sprk_key: 'lease' },
    });

    const { composeLoads } = renderPane();

    await waitFor(() => expect(composeLoads.length).toBeGreaterThan(0));
    expect(composeLoads[0].widgetData?.compose?.subDomain).toBe('nda');
  });

  it('negative: both explicit and derived absent → the envelope carries NO subDomain field (no fabricated default)', async () => {
    mockAnalysisLaunch = { mode: 'existing', analysisId: 'analysis-absent-1' };
    // No sprk_agreementtype key at all in the response (unset lookup on the Analysis record).
    mockRetrieveRecord.mockResolvedValue({ ...FULL_SPE_POINTER });

    const { composeLoads } = renderPane();

    await waitFor(() => expect(composeLoads.length).toBeGreaterThan(0));
    expect(composeLoads[0].widgetData?.compose).not.toHaveProperty('subDomain');
  });

  it('explicit-only (no derivation needed): analysisLaunch.subDomain flows through even when the lookup is unset', async () => {
    mockAnalysisLaunch = { mode: 'existing', analysisId: 'analysis-explicit-2', subDomain: 'vendor' };
    mockRetrieveRecord.mockResolvedValue({ ...FULL_SPE_POINTER });

    const { composeLoads } = renderPane();

    await waitFor(() => expect(composeLoads.length).toBeGreaterThan(0));
    expect(composeLoads[0].widgetData?.compose?.subDomain).toBe('vendor');
  });
});
