/**
 * App — the page opens on the domain it was asked for (#3171).
 *
 * The search-index list loads after mount. Before the fix, once it arrived the page selected the
 * default index and took ITS domain, so a caller that passed `domain=matters` (the Console's search
 * criteria, for one) landed on Documents. Only `searchIndexName` in the URL was honoured.
 *
 * The search hooks, loaders and children are stubbed; SearchFilterPane's stub records the domain
 * and the selected index that App hands it.
 */

import React from 'react';
import { act, render, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import type { AiSearchIndexRow } from '../../services/aiSearchIndexService';
import type { AppUrlParams, SavedSearch, SearchDomain } from '../../types';
import { DEFAULT_FILTERS } from '../../utils/initialFilters';

const mockListIndexes = jest.fn<Promise<AiSearchIndexRow[]>, []>();
jest.mock('../../services/aiSearchIndexService', () => ({
  listActiveSearchIndexes: () => mockListIndexes(),
}));
jest.mock('../../services/apiBase', () => ({ getBffBaseUrl: () => 'https://bff.example.test' }));

const idle = () => ({
  results: [],
  totalCount: null,
  searchState: 'idle',
  hasMore: false,
  errorMessage: null,
  searchTime: null,
  search: jest.fn(),
  loadMore: jest.fn(),
});
// Separate hooks, so a test can tell which search ran.
const documentSearch = idle();
const recordSearch = idle();
jest.mock('../../hooks/useSemanticSearch', () => ({ useSemanticSearch: () => documentSearch }));
jest.mock('../../hooks/useRecordSearch', () => ({ useRecordSearch: () => recordSearch }));
jest.mock('../../hooks/useFilterOptions', () => ({
  useFilterOptions: () => ({ documentTypes: [], fileTypes: [], matterTypes: [] }),
}));
const savedSearchState: { savedSearches: SavedSearch[]; isLoading: boolean; saveSearch: jest.Mock } = {
  savedSearches: [],
  isLoading: false,
  saveSearch: jest.fn(),
};
jest.mock('../../hooks/useSavedSearches', () => ({
  useSavedSearches: () => savedSearchState,
}));
jest.mock('../../hooks/useSearchViewDefinitions', () => ({
  useSearchViewDefinitions: () => ({ columns: [] }),
}));

interface PaneSnapshot {
  activeDomain: SearchDomain;
  selectedSearchIndexId: string;
  unindexedDomainLabel?: string;
}
interface PaneProps extends PaneSnapshot {
  onSelectSavedSearch: (search: SavedSearch) => void;
  onSearch: (query: string, filters: typeof DEFAULT_FILTERS) => void;
}
const paneProps: PaneSnapshot[] = [];
let selectSavedSearch: ((search: SavedSearch) => void) | null = null;
let runSearch: ((query: string, filters: typeof DEFAULT_FILTERS) => void) | null = null;
jest.mock('../../components/SearchFilterPane', () => ({
  SearchFilterPane: (props: PaneProps) => {
    paneProps.push({
      activeDomain: props.activeDomain,
      selectedSearchIndexId: props.selectedSearchIndexId,
      unindexedDomainLabel: props.unindexedDomainLabel,
    });
    selectSavedSearch = props.onSelectSavedSearch;
    runSearch = props.onSearch;
    return null;
  },
}));
const stub = () => null;
jest.mock('../../components/SearchCommandBar', () => ({ SearchCommandBar: stub }));
jest.mock('../../components/ViewToggleToolbar', () => ({ ViewToggleToolbar: stub }));
jest.mock('../../components/StatusBar', () => ({ StatusBar: stub }));
jest.mock('../../components/SearchResultsMap', () => ({ SearchResultsMap: stub }));
jest.mock('../../components/SearchResultsTreemap', () => ({ SearchResultsTreemap: stub }));
jest.mock('../../components/SearchResultsTimeline', () => ({ SearchResultsTimeline: stub }));
jest.mock('../../components/VisualizationSettings', () => ({ VisualizationSettings: stub }));
jest.mock('../../components/SearchResultsGrid', () => ({ SearchResultsGrid: stub }));
jest.mock('../../components/DocumentPreviewDialog', () => ({ DocumentPreviewDialog: stub }));
jest.mock('../../components/EntityRecordDialog', () => ({ openEntityRecord: jest.fn() }));

import { App } from '../../App';

function row(id: string, label: string, isDefault: boolean, order: number): AiSearchIndexRow {
  return {
    sprk_aisearchindexid: id,
    sprk_displayname: `${label} index`,
    sprk_searchindexname: `${id}-index`,
    sprk_targetentitytype: null,
    sprk_targetentitytypeLabel: label,
    sprk_isdefault: isDefault,
    sprk_displayorder: order,
  };
}

const ALL_DEFAULT = row('all-1', 'All', true, 1);
const MATTERS = row('matter-1', 'Matter', false, 2);
const MATTERS_2 = row('matter-2', 'Matter', false, 3);

function renderApp(urlParams: AppUrlParams, initialSavedSearchId = '') {
  render(
    <FluentProvider theme={webLightTheme}>
      <App
        initialQuery={urlParams.query ?? ''}
        initialDomain={urlParams.domain ?? 'documents'}
        initialScope="all"
        initialEntityId=""
        initialSavedSearchId={initialSavedSearchId}
        isDark={false}
        urlParams={urlParams}
      />
    </FluentProvider>
  );
}

/** What the pane shows once the index list has loaded. */
async function settled() {
  await waitFor(() => expect(paneProps[paneProps.length - 1].selectedSearchIndexId).not.toBe(''));
  return paneProps[paneProps.length - 1];
}

const latest = () => paneProps[paneProps.length - 1];

function savedSearch(id: string, searchDomain: SearchDomain, query: string): SavedSearch {
  return {
    id,
    name: `${searchDomain} search`,
    searchDomain,
    query,
    filters: DEFAULT_FILTERS,
    viewMode: 'grid',
    columns: [],
    sortColumn: 'similarity',
    sortDirection: 'desc',
  };
}

beforeEach(() => {
  paneProps.length = 0;
  selectSavedSearch = null;
  runSearch = null;
  savedSearchState.savedSearches = [];
  savedSearchState.isLoading = false;
  mockListIndexes.mockReset();
  documentSearch.search.mockReset();
  recordSearch.search.mockReset();
});

describe('App — initial search index and domain', () => {
  it('a requested domain keeps the page on that domain and selects an index of it', async () => {
    mockListIndexes.mockResolvedValue([ALL_DEFAULT, MATTERS, MATTERS_2]);
    renderApp({ domain: 'matters' });

    expect(await settled()).toEqual({ activeDomain: 'matters', selectedSearchIndexId: 'matter-1' });
  });

  // The Console's path: a query and a domain. The search fires before the index list arrives;
  // the page must stay on Matters afterwards, so the record results are the ones shown.
  it('with a query, searches records and stays on Matters once the index list arrives', async () => {
    mockListIndexes.mockResolvedValue([ALL_DEFAULT, MATTERS]);
    renderApp({ query: 'indemnity', domain: 'matters' });

    expect(await settled()).toEqual({ activeDomain: 'matters', selectedSearchIndexId: 'matter-1' });
    expect(recordSearch.search).toHaveBeenCalledTimes(1);
    expect(recordSearch.search.mock.calls[0][0]).toBe('indemnity');
    expect(recordSearch.search.mock.calls[0][1]).toEqual(['sprk_matter']);
    expect(documentSearch.search).not.toHaveBeenCalled();
  });

  // An Events or Work Assignment index also counts as "documents"; the general one is preferred.
  it('for documents, prefers a general index over an entity-scoped one listed first', async () => {
    const EVENTS = row('event-1', 'Event', false, 1);
    const DOCS = row('docs-1', 'Document', false, 2);
    mockListIndexes.mockResolvedValue([EVENTS, DOCS, { ...MATTERS, sprk_isdefault: true }]);
    renderApp({ domain: 'documents' });

    expect(await settled()).toEqual({ activeDomain: 'documents', selectedSearchIndexId: 'docs-1' });
  });

  it('prefers the default row when it is of the requested domain', async () => {
    mockListIndexes.mockResolvedValue([ALL_DEFAULT, MATTERS, { ...MATTERS_2, sprk_isdefault: true }]);
    renderApp({ domain: 'matters' });

    expect(await settled()).toEqual({ activeDomain: 'matters', selectedSearchIndexId: 'matter-2' });
  });

  it('with no domain requested, the default index decides (unchanged)', async () => {
    mockListIndexes.mockResolvedValue([MATTERS, ALL_DEFAULT]);
    renderApp({});

    expect(await settled()).toEqual({ activeDomain: 'documents', selectedSearchIndexId: 'all-1' });
  });

  it('an index named in the URL still wins over the domain', async () => {
    mockListIndexes.mockResolvedValue([ALL_DEFAULT, MATTERS]);
    renderApp({ domain: 'matters', searchIndexName: 'all-1-index' });

    expect(await settled()).toEqual({ activeDomain: 'documents', selectedSearchIndexId: 'all-1' });
  });

  // #3195: before, the default index took over and the page moved to Documents, so the invoice
  // results of the first search were no longer shown and nothing said why.
  it('with no index for the requested domain, stays on it, selects nothing and says so', async () => {
    mockListIndexes.mockResolvedValue([ALL_DEFAULT, MATTERS]);
    renderApp({ query: 'retainer', domain: 'invoices' });

    await waitFor(() => expect(latest().unindexedDomainLabel).toBe('Invoices'));
    expect(latest()).toEqual({ activeDomain: 'invoices', selectedSearchIndexId: '', unindexedDomainLabel: 'Invoices' });
    expect(recordSearch.search).toHaveBeenCalledTimes(1);
    expect(recordSearch.search.mock.calls[0][1]).toEqual(['sprk_invoice']);
    expect(documentSearch.search).not.toHaveBeenCalled();
  });
});

describe('App — a saved search runs against an index of its own domain (#3195)', () => {
  it('a saved Matters search chosen while a Documents index is selected selects and searches a Matters index', async () => {
    mockListIndexes.mockResolvedValue([ALL_DEFAULT, MATTERS]);
    renderApp({});
    expect(await settled()).toMatchObject({ activeDomain: 'documents', selectedSearchIndexId: 'all-1' });

    act(() => selectSavedSearch!(savedSearch('s1', 'matters', 'indemnity')));

    await waitFor(() => expect(latest()).toMatchObject({ activeDomain: 'matters', selectedSearchIndexId: 'matter-1' }));
    expect(recordSearch.search).toHaveBeenCalledTimes(1);
    const [query, recordTypes, , envelopeIndex, fragment] = recordSearch.search.mock.calls[0];
    expect(query).toBe('indemnity');
    expect(recordTypes).toEqual(['sprk_matter']);
    expect(envelopeIndex).toBeNull();
    expect(fragment).toMatchObject({ searchIndexName: 'matter-1-index' });
  });

  it('keeps the selected index when it is already of the saved search domain', async () => {
    mockListIndexes.mockResolvedValue([ALL_DEFAULT, MATTERS, MATTERS_2]);
    renderApp({ searchIndexName: 'matter-2-index' });
    expect(await settled()).toMatchObject({ activeDomain: 'matters', selectedSearchIndexId: 'matter-2' });

    act(() => selectSavedSearch!(savedSearch('s1', 'matters', 'indemnity')));

    await waitFor(() => expect(recordSearch.search).toHaveBeenCalledTimes(1));
    expect(latest()).toMatchObject({ activeDomain: 'matters', selectedSearchIndexId: 'matter-2' });
    expect(recordSearch.search.mock.calls[0][4]).toMatchObject({ searchIndexName: 'matter-2-index' });
  });

  it('a saved search of a domain with no index searches the standard index, not the selected one, and says so', async () => {
    mockListIndexes.mockResolvedValue([ALL_DEFAULT, MATTERS]);
    renderApp({ searchIndexName: 'all-1-index' });
    expect(await settled()).toMatchObject({ activeDomain: 'documents', selectedSearchIndexId: 'all-1' });

    act(() => selectSavedSearch!(savedSearch('s1', 'invoices', 'retainer')));

    await waitFor(() => expect(recordSearch.search).toHaveBeenCalledTimes(1));
    expect(latest()).toEqual({ activeDomain: 'invoices', selectedSearchIndexId: '', unindexedDomainLabel: 'Invoices' });
    const [, recordTypes, , envelopeIndex, fragment] = recordSearch.search.mock.calls[0];
    expect(recordTypes).toEqual(['sprk_invoice']);
    expect(envelopeIndex).toBeNull();
    expect(fragment).toBeNull();
  });

  // Review finding: only the first search dropped the URL's index; the next one (Search, Refresh) sent
  // the URL's Documents index with an Invoices record search.
  it('a later search of the unindexed domain still does not send the URL index of another domain', async () => {
    mockListIndexes.mockResolvedValue([ALL_DEFAULT, MATTERS]);
    renderApp({ searchIndexName: 'all-1-index' });
    expect(await settled()).toMatchObject({ activeDomain: 'documents', selectedSearchIndexId: 'all-1' });

    act(() => selectSavedSearch!(savedSearch('s1', 'invoices', 'retainer')));
    await waitFor(() => expect(recordSearch.search).toHaveBeenCalledTimes(1));
    act(() => runSearch!('retainer', DEFAULT_FILTERS));

    await waitFor(() => expect(recordSearch.search).toHaveBeenCalledTimes(2));
    const [, recordTypes, , envelopeIndex, fragment] = recordSearch.search.mock.calls[1];
    expect(recordTypes).toEqual(['sprk_invoice']);
    expect(envelopeIndex).toBeNull();
    expect(fragment).toBeNull();
  });

  // Review finding: a saved search chosen after the list arrived but before React re-rendered read the
  // "still loading" closure and ended on the default index of another domain.
  it('a saved search chosen just as the list arrives, before a re-render, still selects an index of its domain', async () => {
    let resolveList: (rows: AiSearchIndexRow[]) => void = () => undefined;
    mockListIndexes.mockReturnValue(new Promise(resolve => (resolveList = resolve)));
    renderApp({});
    await waitFor(() => expect(selectSavedSearch).not.toBeNull());
    const handlerFromBeforeTheListLoaded = selectSavedSearch!;

    await act(async () => {
      resolveList([ALL_DEFAULT, MATTERS]);
      for (let i = 0; i < 5; i++) await Promise.resolve(); // the list continuation runs; no re-render yet
      handlerFromBeforeTheListLoaded(savedSearch('s1', 'matters', 'indemnity'));
    });

    await waitFor(() => expect(latest()).toMatchObject({ activeDomain: 'matters', selectedSearchIndexId: 'matter-1' }));
    expect(recordSearch.search.mock.calls[recordSearch.search.mock.calls.length - 1][4]).toMatchObject({
      searchIndexName: 'matter-1-index',
    });
  });

  it('a saved search opened from the URL before the index list loads ends on an index of its domain', async () => {
    let resolveList: (rows: AiSearchIndexRow[]) => void = () => undefined;
    mockListIndexes.mockReturnValue(new Promise(resolve => (resolveList = resolve)));
    savedSearchState.savedSearches = [savedSearch('s1', 'matters', 'indemnity')];
    renderApp({}, 's1');

    await waitFor(() => expect(recordSearch.search).toHaveBeenCalledTimes(1));
    await act(async () => resolveList([ALL_DEFAULT, MATTERS]));

    expect(await settled()).toMatchObject({ activeDomain: 'matters', selectedSearchIndexId: 'matter-1' });
    expect(documentSearch.search).not.toHaveBeenCalled();
  });
});
