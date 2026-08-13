/**
 * QuickSwitcher Component Tests
 * (spaarke-side-pane-navigation-history-r1 task 070, spec FR-11)
 *
 * Verifies the task-070 closed acceptance-criteria set + `<ui-tests>`:
 *   - Typing filters the local (already-loaded) Recent/Pinned/Views index
 *     live, instantly, with no network call.
 *   - A query with NO local hit escalates (after the debounce) to a live
 *     `Xrm.WebApi` record lookup, and renders the live result labeled "Live".
 *   - The Ctrl/Cmd+K accelerator focuses the search box from anywhere in the
 *     pane (document-level listener — see QuickSwitcher.tsx module docblock).
 *   - Enter navigates to the top result: `Xrm.Navigation.navigateTo` for a
 *     logical (entityrecord/entitylist) target, `window.open(url, '_blank',
 *     'noopener')` for a `weblink` target.
 *   - An empty query renders no results dropdown (the caller, NavigatorBody,
 *     shows its normal tab content underneath — not an error).
 *   - Renders in light AND dark themes (ADR-021). QuickSwitcher itself does
 *     NOT portal-render anything (no Popover/Menu/Dialog — the results list
 *     is a normal in-tree absolutely-positioned `<div>`), so no re-wrap
 *     assertion is needed here — mirrors RecentTab.tsx/PinnedTab.tsx/
 *     ViewsTab.tsx's own "no portal-rendered component" note. The
 *     info-icon Tooltip's portal re-wrap (which DOES apply) is already
 *     covered by NavigatorBody.test.tsx, unchanged by this task.
 *
 * `navigatorSearchIndex.ts` is a MODULE-SCOPED store — each test seeds it
 * directly via `setRecentSearchEntries`/`setPinnedSearchEntries`/
 * `setViewSearchEntries` (exactly how `RecentTab.tsx`/`PinnedTab.tsx`/
 * `ViewsTab.tsx` report into it after their own load+trim) and resets it in
 * `afterEach` via `__resetSearchIndexForTests` so tests do not leak into
 * each other.
 *
 * @see ../QuickSwitcher.tsx
 * @see ../../services/navigatorSearchIndex.ts
 * @see ../../services/liveSearchService.ts
 * @see ADR-021 Fluent UI v9 design system (tokens, light/dark, keyboard a11y)
 */

import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

import { QuickSwitcher, fuzzyScore } from '../QuickSwitcher';
import {
  __resetSearchIndexForTests,
  setPinnedSearchEntries,
  setRecentSearchEntries,
  setViewSearchEntries,
} from '../../services/navigatorSearchIndex';

// ─────────────────────────────────────────────────────────────────────────────
// Fake Xrm
// ─────────────────────────────────────────────────────────────────────────────

interface FakeXrmOptions {
  /** entity -> records returned by retrieveMultipleRecords for a matching `contains()` filter. */
  liveRecordsByEntity?: Record<string, Array<Record<string, unknown>>>;
  /** userquery rows returned for the live view escalation. */
  liveUserQueries?: Array<Record<string, unknown>>;
}

function buildFakeXrm(options: FakeXrmOptions = {}) {
  const liveRecordsByEntity = options.liveRecordsByEntity ?? {};
  const liveUserQueries = options.liveUserQueries ?? [];

  const retrieveMultipleRecords = jest.fn(async (entity: string, query?: string) => {
    if (entity === 'userquery') {
      return { entities: liveUserQueries };
    }
    const records = liveRecordsByEntity[entity];
    if (!records) return { entities: [] };
    // Only "match" when the query string actually carries a contains() filter
    // (proves the live escalation is query-driven, not a blind fetch-all).
    if (query && query.includes('contains(')) {
      return { entities: records };
    }
    return { entities: [] };
  });

  const navigateTo = jest.fn(async () => undefined);
  const openUrl = jest.fn(async () => undefined);

  const getEntityMetadata = jest.fn(async () => ({ PrimaryNameAttribute: 'name' }));

  return {
    WebApi: {
      retrieveMultipleRecords,
      retrieveRecord: jest.fn(),
      createRecord: jest.fn(),
      updateRecord: jest.fn(),
      deleteRecord: jest.fn(),
    },
    Navigation: { navigateTo, openUrl, openForm: jest.fn() },
    Utility: {
      getGlobalContext: () => ({ userSettings: { userId: 'user-1' } }),
      getEntityMetadata,
    },
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

function renderQuickSwitcher(theme: 'light' | 'dark' = 'light') {
  return render(
    <FluentProvider theme={theme === 'light' ? webLightTheme : webDarkTheme}>
      <QuickSwitcher />
    </FluentProvider>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Suite
// ─────────────────────────────────────────────────────────────────────────────

describe('QuickSwitcher', () => {
  const originalXrm = (window as unknown as { Xrm?: unknown }).Xrm;
  const originalOpen = window.open;

  beforeEach(() => {
    __resetSearchIndexForTests();
    window.open = jest.fn();
  });

  afterEach(() => {
    __resetSearchIndexForTests();
    if (originalXrm) {
      (window as unknown as { Xrm: unknown }).Xrm = originalXrm;
    } else {
      removeMockXrm();
    }
    window.open = originalOpen;
    jest.clearAllMocks();
  });

  // ───────────────────────────────────────────────────────────────────────
  // fuzzyScore — direct unit coverage of the local-match scorer
  // ───────────────────────────────────────────────────────────────────────

  describe('fuzzyScore', () => {
    it('scores a substring match', () => {
      expect(fuzzyScore('acme', 'Acme v. Widget Co')).not.toBeNull();
    });

    it('scores an in-order subsequence match', () => {
      expect(fuzzyScore('avw', 'Acme v. Widget Co')).not.toBeNull();
    });

    it('returns null for a non-match (characters out of order / absent)', () => {
      expect(fuzzyScore('zzz', 'Acme v. Widget Co')).toBeNull();
      expect(fuzzyScore('wa', 'Acme v. Widget Co')).toBeNull(); // "w" appears after "a" only in-order the other way
    });

    it('returns null for a blank query', () => {
      expect(fuzzyScore('', 'Acme v. Widget Co')).toBeNull();
    });
  });

  // ───────────────────────────────────────────────────────────────────────
  // Local fuzzy-match — instant, in-memory (light + dark)
  // ───────────────────────────────────────────────────────────────────────

  it('type_QueryMatchingLocalEntry_FiltersLiveWithNoNetworkCall', async () => {
    installMockXrm();
    setRecentSearchEntries([
      {
        id: 'recent-1',
        label: 'Acme v. Widget Co',
        chipLabel: 'Matter',
        target: { type: 'entityrecord', entityLogicalName: 'sprk_matter', entityId: 'matter-1' },
      },
    ]);
    setPinnedSearchEntries([]);
    setViewSearchEntries([]);

    renderQuickSwitcher('light');
    const user = userEvent.setup();

    await user.type(screen.getByTestId('navigator-quickswitcher-input'), 'acme');

    expect(await screen.findByTestId('navigator-quickswitcher-result-recent-1')).toHaveTextContent(
      'Acme v. Widget Co'
    );
    expect(screen.getByTestId('navigator-quickswitcher-result-recent-1')).toHaveTextContent('Matter');
    // Local hit — the live escalation must never fire.
    const fakeXrm = (window as unknown as { Xrm: ReturnType<typeof buildFakeXrm> }).Xrm;
    expect(fakeXrm.WebApi.retrieveMultipleRecords).not.toHaveBeenCalled();
  });

  it('clear_Query_ShowsNoResultsDropdown', async () => {
    installMockXrm();
    setRecentSearchEntries([
      { id: 'recent-1', label: 'Acme v. Widget Co', chipLabel: 'Matter', target: null },
    ]);

    renderQuickSwitcher('light');
    const user = userEvent.setup();
    const input = screen.getByTestId('navigator-quickswitcher-input');

    await user.type(input, 'acme');
    expect(await screen.findByTestId('navigator-quickswitcher-results')).toBeInTheDocument();

    await user.clear(input);
    // Empty query -> no results dropdown at all (NavigatorBody's normal tab
    // content shows underneath, not an error).
    expect(screen.queryByTestId('navigator-quickswitcher-results')).not.toBeInTheDocument();
  });

  // ───────────────────────────────────────────────────────────────────────
  // Escalation: no local hit -> live Xrm.WebApi lookup (FR-11)
  // ───────────────────────────────────────────────────────────────────────

  it('type_QueryWithNoLocalHit_EscalatesToLiveDataverseLookupAndShowsLiveResult', async () => {
    installMockXrm({
      liveRecordsByEntity: {
        sprk_matter: [{ sprk_matterid: 'matter-live-1', name: 'Zephyr Holdings' }],
      },
    });
    // Local index has entries, but none match "zephyr" — proves the miss is
    // query-specific, not "index empty".
    setRecentSearchEntries([
      { id: 'recent-1', label: 'Acme v. Widget Co', chipLabel: 'Matter', target: null },
    ]);

    renderQuickSwitcher('light');
    const user = userEvent.setup();

    await user.type(screen.getByTestId('navigator-quickswitcher-input'), 'zephyr');

    const result = await screen.findByTestId(
      'navigator-quickswitcher-result-live-record-sprk_matter-matter-live-1',
      {},
      { timeout: 2000 }
    );
    expect(result).toHaveTextContent('Zephyr Holdings');
    expect(result).toHaveTextContent('Live');
  });

  it('type_QueryContainingAmpersand_PercentEncodesTheODataFilterValue', async () => {
    // Code-review fix (task 070): an unencoded "&" in the live-search query
    // would be read as an OData query-option separator, truncating $filter
    // and silently breaking the live escalation. Locks in the
    // `encodeODataStringLiteral` fix in liveSearchService.ts.
    const fakeXrm = installMockXrm({
      liveRecordsByEntity: {
        sprk_matter: [{ sprk_matterid: 'matter-live-2', name: 'Smith & Co' }],
      },
    });
    setRecentSearchEntries([{ id: 'recent-1', label: 'Acme v. Widget Co', chipLabel: 'Matter', target: null }]);

    renderQuickSwitcher('light');
    const user = userEvent.setup();
    // userEvent types "&" literally into a plain <input>; no special escaping needed here.
    await user.type(screen.getByTestId('navigator-quickswitcher-input'), 'Smith & Co');

    await screen.findByTestId(
      'navigator-quickswitcher-result-live-record-sprk_matter-matter-live-2',
      {},
      { timeout: 2000 }
    );

    const matterCall = fakeXrm.WebApi.retrieveMultipleRecords.mock.calls.find(
      ([entity]: [string, string?]) => entity === 'sprk_matter'
    );
    expect(matterCall).toBeDefined();
    const [, query] = matterCall as [string, string];
    expect(query).toContain('%26'); // percent-encoded "&"
    expect(query).not.toMatch(/contains\(name, 'Smith & Co'\)/); // never a raw, unencoded "&" inside the filter
  });

  // ───────────────────────────────────────────────────────────────────────
  // Keyboard accelerator (Ctrl/Cmd+K) — focuses the box from anywhere
  // ───────────────────────────────────────────────────────────────────────

  it('keydown_CtrlKAnywhereInDocument_FocusesSearchBox', async () => {
    installMockXrm();
    renderQuickSwitcher('light');

    const input = screen.getByTestId('navigator-quickswitcher-input');
    expect(input).not.toHaveFocus();

    // Focus lives elsewhere (simulates focus being anywhere else in the pane).
    document.body.focus();

    const event = new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true, cancelable: true });
    document.dispatchEvent(event);

    await waitFor(() => expect(input).toHaveFocus());
  });

  // ───────────────────────────────────────────────────────────────────────
  // Enter navigates the top result
  // ───────────────────────────────────────────────────────────────────────

  it('keydown_EnterWithTopResult_NavigatesViaXrmNavigationForEntityRecordTarget', async () => {
    const fakeXrm = installMockXrm();
    setRecentSearchEntries([
      {
        id: 'recent-1',
        label: 'Acme v. Widget Co',
        chipLabel: 'Matter',
        target: { type: 'entityrecord', entityLogicalName: 'sprk_matter', entityId: 'matter-1' },
      },
    ]);

    renderQuickSwitcher('light');
    const user = userEvent.setup();
    const input = screen.getByTestId('navigator-quickswitcher-input');

    await user.type(input, 'acme');
    await screen.findByTestId('navigator-quickswitcher-result-recent-1');
    await user.keyboard('{Enter}');

    expect(fakeXrm.Navigation.navigateTo).toHaveBeenCalledWith({
      pageType: 'entityrecord',
      entityName: 'sprk_matter',
      entityId: 'matter-1',
    });
    // Selecting a result clears the query.
    expect(input).toHaveValue('');
  });

  it('keydown_EnterWithWeblinkTopResult_OpensViaWindowOpenWithNoopener', async () => {
    installMockXrm();
    setPinnedSearchEntries([
      {
        id: 'pinned-1',
        label: 'External Reference',
        chipLabel: 'Link',
        target: { type: 'weblink', url: 'https://example.com/reference' },
      },
    ]);

    renderQuickSwitcher('light');
    const user = userEvent.setup();
    const input = screen.getByTestId('navigator-quickswitcher-input');

    await user.type(input, 'external');
    await screen.findByTestId('navigator-quickswitcher-result-pinned-1');
    await user.keyboard('{Enter}');

    expect(window.open).toHaveBeenCalledWith('https://example.com/reference', '_blank', 'noopener');
  });

  it('click_ResultRow_NavigatesToClickedTarget', async () => {
    const fakeXrm = installMockXrm();
    setViewSearchEntries([
      {
        id: 'view-1',
        label: 'My Open Matters',
        chipLabel: 'View',
        target: { type: 'entitylist', entityLogicalName: 'sprk_matter', viewId: 'uq-1' },
      },
    ]);

    renderQuickSwitcher('light');
    const user = userEvent.setup();

    await user.type(screen.getByTestId('navigator-quickswitcher-input'), 'open matters');
    await user.click(await screen.findByTestId('navigator-quickswitcher-result-view-1'));

    expect(fakeXrm.Navigation.navigateTo).toHaveBeenCalledWith({
      pageType: 'entitylist',
      entityName: 'sprk_matter',
      viewId: 'uq-1',
    });
  });

  // ───────────────────────────────────────────────────────────────────────
  // Light + dark theme rendering (ADR-021)
  // ───────────────────────────────────────────────────────────────────────

  it('render_DarkTheme_RendersWithoutError', () => {
    installMockXrm();
    expect(() => renderQuickSwitcher('dark')).not.toThrow();
    expect(screen.getByTestId('navigator-quickswitcher-input')).toBeInTheDocument();
  });

  it('render_LightTheme_RendersWithoutError', () => {
    installMockXrm();
    expect(() => renderQuickSwitcher('light')).not.toThrow();
    expect(screen.getByTestId('navigator-quickswitcher-input')).toBeInTheDocument();
  });
});
