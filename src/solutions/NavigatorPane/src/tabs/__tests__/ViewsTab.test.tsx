/**
 * ViewsTab Component Tests
 * (spaarke-side-pane-navigation-history-r1 task 060, spec FR-10 UI)
 *
 * Verifies the task-060 closed acceptance-criteria set + `<ui-tests>`, PLUS
 * the UAT bug fix (`viewType`):
 *   - Given a user with saved userquery views, the Views tab lists them
 *     grouped by entity (`returnedtypecode`), each entity-group heading on a
 *     subtle gray background band.
 *   - Clicking a listed view calls `Xrm.Navigation.navigateTo` with
 *     `pageType:'entitylist'`, the correct `entityName` + `viewId`, AND
 *     `viewType: '4230'` (BUG FIX — without it, `navigateTo` silently opened
 *     the entity's DEFAULT view instead of the selected personal view).
 *   - System `savedquery` views are never shown by default (opt-in / pin-only
 *     per FR-10) — this tab never even queries `savedquery`.
 *   - Negative: a user with zero userqueries sees a benign empty state, not
 *     an error.
 *   - Renders correctly in light AND dark themes (ADR-021); no portal-
 *     rendered component here so no re-wrap assertion is needed (mirrors
 *     RecentTab.tsx/BookmarksTab.tsx's own note).
 *   - No-Xrm degrades to the same safe empty state, never throws.
 *
 * `window.Xrm` is installed directly (mirrors RecentTab.test.tsx /
 * PinnedTab.test.tsx) so `getXrm()` resolves it via the normal frame-walk —
 * no module mock of `ViewService` itself; the fake `Xrm.WebApi` drives the
 * real `ViewService.getAllUserQueries()` code path.
 *
 * @see ../ViewsTab.tsx
 * @see ../../../../client/shared/Spaarke.UI.Components/src/services/ViewService.ts
 * @see ADR-021 Fluent UI v9 design system (tokens, light/dark)
 * @see ADR-022 React 16/17-safe shared-lib code (ViewService/xrmContext this tab consumes)
 */

import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

import { ViewsTab } from '../ViewsTab';

// ─────────────────────────────────────────────────────────────────────────────
// Fixtures
// ─────────────────────────────────────────────────────────────────────────────

const USER_QUERIES = [
  {
    userqueryid: 'uq-matter-1',
    name: 'My Open Matters',
    returnedtypecode: 'sprk_matter',
    fetchxml: "<fetch><entity name='sprk_matter'/></fetch>",
    layoutxml: '',
  },
  {
    userqueryid: 'uq-document-1',
    name: 'My Recent Documents',
    returnedtypecode: 'sprk_document',
    fetchxml: "<fetch><entity name='sprk_document'/></fetch>",
    layoutxml: '',
  },
  {
    userqueryid: 'uq-matter-2',
    name: 'My Closed Matters',
    returnedtypecode: 'sprk_matter',
    fetchxml: "<fetch><entity name='sprk_matter'/></fetch>",
    layoutxml: '',
  },
];

/** Proves system views are never merged in — ViewsTab must never even query this table. */
const SAVED_QUERIES = [
  {
    savedqueryid: 'sq-1',
    name: 'All Matters (System)',
    returnedtypecode: 'sprk_matter',
    fetchxml: "<fetch><entity name='sprk_matter'/></fetch>",
    layoutxml: '',
    isdefault: true,
    querytype: 0,
  },
];

// ─────────────────────────────────────────────────────────────────────────────
// Fake Xrm
// ─────────────────────────────────────────────────────────────────────────────

interface FakeXrmOptions {
  userQueries?: typeof USER_QUERIES;
}

function buildFakeXrm(options: FakeXrmOptions = {}) {
  const userQueries = options.userQueries ?? USER_QUERIES;

  const retrieveMultipleRecords = jest.fn(async (entity: string, _query?: string) => {
    if (entity === 'userquery') {
      return { entities: userQueries };
    }
    if (entity === 'savedquery') {
      // Present in the org, but ViewsTab must never surface these by default.
      return { entities: SAVED_QUERIES };
    }
    return { entities: [] };
  });

  const navigateTo = jest.fn(async () => undefined);

  return {
    WebApi: {
      retrieveMultipleRecords,
      retrieveRecord: jest.fn(),
      createRecord: jest.fn(),
      updateRecord: jest.fn(),
      deleteRecord: jest.fn(),
    },
    Navigation: { navigateTo, openUrl: jest.fn(), openForm: jest.fn() },
  } as {
    WebApi: {
      retrieveMultipleRecords: jest.Mock;
      retrieveRecord: jest.Mock;
      createRecord: jest.Mock;
      updateRecord: jest.Mock;
      deleteRecord: jest.Mock;
    };
    Navigation: { navigateTo: jest.Mock; openUrl: jest.Mock; openForm: jest.Mock };
  };
}

function installMockXrm(options?: FakeXrmOptions) {
  const fakeXrm = buildFakeXrm(options);
  (window as unknown as { Xrm: unknown }).Xrm = fakeXrm;
  return fakeXrm;
}

function removeMockXrm(): void {
  delete (window as unknown as { Xrm?: unknown }).Xrm;
}

function renderViewsTab(theme: 'light' | 'dark' = 'light') {
  return render(
    <FluentProvider theme={theme === 'light' ? webLightTheme : webDarkTheme}>
      <ViewsTab />
    </FluentProvider>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Suite
// ─────────────────────────────────────────────────────────────────────────────

describe('ViewsTab', () => {
  const originalXrm = (window as unknown as { Xrm?: unknown }).Xrm;

  afterEach(() => {
    if (originalXrm) {
      (window as unknown as { Xrm: unknown }).Xrm = originalXrm;
    } else {
      removeMockXrm();
    }
    jest.clearAllMocks();
  });

  // ───────────────────────────────────────────────────────────────────────
  // Views grouped by entity (light + dark)
  // ───────────────────────────────────────────────────────────────────────

  it('render_UserQueriesAcrossTwoEntities_GroupsByEntityWithHeadings', async () => {
    installMockXrm();
    renderViewsTab('light');

    await waitFor(() => {
      expect(screen.getByTestId('views-tab')).toBeInTheDocument();
    });

    expect(screen.getByTestId('views-tab-group-sprk_matter')).toBeInTheDocument();
    expect(screen.getByTestId('views-tab-group-sprk_document')).toBeInTheDocument();

    // Entity heading labels use the known Matter/Document chip-style labels.
    expect(screen.getByTestId('views-tab-group-sprk_matter')).toHaveTextContent('Matter');
    expect(screen.getByTestId('views-tab-group-sprk_document')).toHaveTextContent('Document');

    // Both Matter views listed under the same group.
    expect(screen.getByTestId('views-tab-row-uq-matter-1')).toHaveTextContent('My Open Matters');
    expect(screen.getByTestId('views-tab-row-uq-matter-2')).toHaveTextContent('My Closed Matters');
    expect(screen.getByTestId('views-tab-row-uq-document-1')).toHaveTextContent('My Recent Documents');
  });

  it('render_DarkTheme_RendersAllGroupedViewsWithoutError', async () => {
    installMockXrm();
    expect(() => renderViewsTab('dark')).not.toThrow();

    await waitFor(() => {
      expect(screen.getByTestId('views-tab-row-uq-matter-1')).toBeInTheDocument();
    });
    expect(screen.getByTestId('views-tab-row-uq-document-1')).toBeInTheDocument();
  });

  // ───────────────────────────────────────────────────────────────────────
  // Click opens the entity list with the view selected
  // ───────────────────────────────────────────────────────────────────────

  it('click_ViewRow_InvokesNavigateToWithEntityListPageTypeAndViewId', async () => {
    const fakeXrm = installMockXrm();
    renderViewsTab('light');
    const user = userEvent.setup();

    await waitFor(() => expect(screen.getByTestId('views-tab-row-uq-document-1')).toBeInTheDocument());

    await user.click(screen.getByTestId('views-tab-row-uq-document-1'));

    expect(fakeXrm.Navigation.navigateTo).toHaveBeenCalledWith({
      pageType: 'entitylist',
      entityName: 'sprk_document',
      viewId: 'uq-document-1',
      viewType: '4230',
    });
  });

  it('keydown_EnterOnViewRow_AlsoInvokesNavigateTo', async () => {
    const fakeXrm = installMockXrm();
    renderViewsTab('light');
    const user = userEvent.setup();

    await waitFor(() => expect(screen.getByTestId('views-tab-row-uq-matter-1')).toBeInTheDocument());

    screen.getByTestId('views-tab-row-uq-matter-1').focus();
    await user.keyboard('{Enter}');

    expect(fakeXrm.Navigation.navigateTo).toHaveBeenCalledWith({
      pageType: 'entitylist',
      entityName: 'sprk_matter',
      viewId: 'uq-matter-1',
      viewType: '4230',
    });
  });

  // ───────────────────────────────────────────────────────────────────────
  // Gray background band on entity-group headings (UAT polish)
  // ───────────────────────────────────────────────────────────────────────

  it('render_EntityGroupHeadings_HaveAGrayBackgroundBand', async () => {
    installMockXrm();
    renderViewsTab('light');

    await waitFor(() => {
      expect(screen.getByTestId('views-tab-group-sprk_matter')).toBeInTheDocument();
    });

    const heading = screen.getByText('Matter');
    // The heading's immediate wrapper carries the gray-band background class
    // (a Griffel-generated class — presence of ANY class, not the token
    // value itself, is what's checkable from a jsdom render).
    expect(heading.parentElement?.className).toBeTruthy();
    expect(heading.parentElement).not.toBe(screen.getByTestId('views-tab-group-sprk_matter'));
  });

  // ───────────────────────────────────────────────────────────────────────
  // System views hidden; empty-state on zero userqueries
  // ───────────────────────────────────────────────────────────────────────

  it('render_SystemSavedQueriesExist_NeverQueriesOrSurfacesThem', async () => {
    const fakeXrm = installMockXrm();
    renderViewsTab('light');

    await waitFor(() => {
      expect(screen.getByTestId('views-tab')).toBeInTheDocument();
    });

    // The system view's name never appears anywhere in the rendered tab.
    expect(screen.queryByText('All Matters (System)')).not.toBeInTheDocument();
    // ViewsTab's data path never even queries savedquery.
    expect(fakeXrm.WebApi.retrieveMultipleRecords).not.toHaveBeenCalledWith('savedquery', expect.any(String));
    expect(fakeXrm.WebApi.retrieveMultipleRecords).toHaveBeenCalledWith('userquery', expect.any(String));
  });

  it('render_ZeroUserQueries_ShowsBenignEmptyStateNotError', async () => {
    installMockXrm({ userQueries: [] });
    renderViewsTab('light');

    expect(await screen.findByTestId('views-tab-empty')).toHaveTextContent(
      'Your saved views will appear here.'
    );
    expect(screen.queryByTestId('views-tab-error')).not.toBeInTheDocument();
  });

  // ───────────────────────────────────────────────────────────────────────
  // No-Xrm surface degrades gracefully (negative acceptance criterion)
  // ───────────────────────────────────────────────────────────────────────

  it('mount_NoXrmAvailable_RendersSafeEmptyStateAndDoesNotThrow', async () => {
    removeMockXrm();

    expect(() => renderViewsTab('light')).not.toThrow();

    expect(await screen.findByTestId('views-tab-empty')).toBeInTheDocument();
    expect(screen.queryByTestId('views-tab-error')).not.toBeInTheDocument();
  });
});
