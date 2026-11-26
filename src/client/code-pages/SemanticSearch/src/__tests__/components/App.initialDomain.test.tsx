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
import { render, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import type { AiSearchIndexRow } from '../../services/aiSearchIndexService';
import type { AppUrlParams, SearchDomain } from '../../types';

const mockListIndexes = jest.fn<Promise<AiSearchIndexRow[]>, []>();
jest.mock('../../services/aiSearchIndexService', () => ({
  listActiveSearchIndexes: () => mockListIndexes(),
}));
jest.mock('../../services/apiBase', () => ({ getBffBaseUrl: () => 'https://bff.example.test' }));

const idleSearch = {
  results: [],
  totalCount: null,
  searchState: 'idle',
  hasMore: false,
  errorMessage: null,
  searchTime: null,
  search: jest.fn(),
  loadMore: jest.fn(),
};
jest.mock('../../hooks/useSemanticSearch', () => ({ useSemanticSearch: () => idleSearch }));
jest.mock('../../hooks/useRecordSearch', () => ({ useRecordSearch: () => idleSearch }));
jest.mock('../../hooks/useFilterOptions', () => ({
  useFilterOptions: () => ({ documentTypes: [], fileTypes: [], matterTypes: [] }),
}));
jest.mock('../../hooks/useSavedSearches', () => ({
  useSavedSearches: () => ({ savedSearches: [], isLoading: false, saveSearch: jest.fn() }),
}));
jest.mock('../../hooks/useSearchViewDefinitions', () => ({
  useSearchViewDefinitions: () => ({ columns: [] }),
}));

const paneProps: Array<{ activeDomain: SearchDomain; selectedSearchIndexId: string }> = [];
jest.mock('../../components/SearchFilterPane', () => ({
  SearchFilterPane: (props: { activeDomain: SearchDomain; selectedSearchIndexId: string }) => {
    paneProps.push({ activeDomain: props.activeDomain, selectedSearchIndexId: props.selectedSearchIndexId });
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

function renderApp(urlParams: AppUrlParams) {
  render(
    <FluentProvider theme={webLightTheme}>
      <App
        initialQuery=""
        initialDomain={urlParams.domain ?? 'documents'}
        initialScope="all"
        initialEntityId=""
        initialSavedSearchId=""
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

beforeEach(() => {
  paneProps.length = 0;
  mockListIndexes.mockReset();
});

describe('App — initial search index and domain', () => {
  it('a requested domain keeps the page on that domain and selects an index of it', async () => {
    mockListIndexes.mockResolvedValue([ALL_DEFAULT, MATTERS, MATTERS_2]);
    renderApp({ domain: 'matters' });

    expect(await settled()).toEqual({ activeDomain: 'matters', selectedSearchIndexId: 'matter-1' });
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

  it('with no index for the requested domain, falls back to the default index', async () => {
    mockListIndexes.mockResolvedValue([ALL_DEFAULT, MATTERS]);
    renderApp({ domain: 'invoices' });

    expect(await settled()).toEqual({ activeDomain: 'documents', selectedSearchIndexId: 'all-1' });
  });
});
