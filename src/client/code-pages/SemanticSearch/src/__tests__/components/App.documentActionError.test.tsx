/**
 * App — a failed document action (open / download / delete / email link / send to index) is shown.
 *
 * `useDocumentActions` (real, from @spaarke/document-operations source) reads what `@spaarke/auth`'s
 * authenticatedFetch THROWS — ApiError (status + ProblemDetails) or AuthError — and sets `actionError`.
 * Before the fix App never read `actionError`, so every failure showed the user nothing at all.
 *
 * Everything around the wiring is stubbed: the search hooks, the catalog/view loaders and the child
 * components. SearchCommandBar's stub exposes the action callbacks as buttons, exactly as App passes them.
 */

import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

const mockFetch = jest.fn<Promise<Response>, [string, RequestInit?]>();

jest.mock('@spaarke/auth', () => ({
  ...jest.requireActual('@spaarke/auth'),
  authenticatedFetch: (url: string, init?: RequestInit) => mockFetch(url, init),
}));
jest.mock('../../services/apiBase', () => ({ getBffBaseUrl: () => 'https://bff.example.test' }));
jest.mock('../../services/aiSearchIndexService', () => ({
  listActiveSearchIndexes: jest.fn(() => new Promise(() => {})),
}));

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

jest.mock('../../components/SearchCommandBar', () => ({
  SearchCommandBar: (props: {
    onDelete: (ids: string[]) => void;
    onOpenInWeb: (id: string) => void;
    onDownload: (id: string) => void;
    onSendToIndex: (ids: string[]) => void;
  }) => (
    <div>
      <button onClick={() => props.onDelete(['doc-1'])}>stub-delete</button>
      <button onClick={() => props.onOpenInWeb('doc-1')}>stub-open</button>
      <button onClick={() => props.onDownload('doc-1')}>stub-download</button>
      <button onClick={() => props.onSendToIndex(['doc-1', 'doc-2'])}>stub-index</button>
    </div>
  ),
}));
const stub = () => null;
jest.mock('../../components/SearchFilterPane', () => ({ SearchFilterPane: stub }));
jest.mock('../../components/ViewToggleToolbar', () => ({ ViewToggleToolbar: stub }));
jest.mock('../../components/StatusBar', () => ({ StatusBar: stub }));
jest.mock('../../components/SearchResultsMap', () => ({ SearchResultsMap: stub }));
jest.mock('../../components/SearchResultsTreemap', () => ({ SearchResultsTreemap: stub }));
jest.mock('../../components/SearchResultsTimeline', () => ({ SearchResultsTimeline: stub }));
jest.mock('../../components/VisualizationSettings', () => ({ VisualizationSettings: stub }));
jest.mock('../../components/SearchResultsGrid', () => ({ SearchResultsGrid: stub }));
jest.mock('../../components/DocumentPreviewDialog', () => ({ DocumentPreviewDialog: stub }));
jest.mock('../../components/EntityRecordDialog', () => ({ openEntityRecord: jest.fn() }));

import { ApiError, AuthError } from '@spaarke/auth';
import { App } from '../../App';

function renderApp() {
  return render(
    <FluentProvider theme={webLightTheme}>
      <App
        initialQuery=""
        initialDomain="documents"
        initialScope="all"
        initialEntityId=""
        initialSavedSearchId=""
        isDark={false}
      />
    </FluentProvider>
  );
}

describe('App — document-action failures are shown', () => {
  beforeEach(() => {
    mockFetch.mockReset();
    jest.spyOn(window, 'confirm').mockReturnValue(true);
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it("delete: a thrown 403 shows the server's detail in the error bar", async () => {
    const detail = 'You do not have permission to delete documents in this matter.';
    mockFetch.mockRejectedValue(new ApiError(detail, 403, { title: 'Forbidden', status: 403, detail }));
    renderApp();

    fireEvent.click(screen.getByText('stub-delete'));

    expect(await screen.findByText(`Couldn't delete the document: ${detail}`)).toBeInTheDocument();
  });

  it('open: a thrown bare 500 shows "temporarily unavailable"', async () => {
    mockFetch.mockRejectedValue(new ApiError('HTTP 500', 500));
    renderApp();

    fireEvent.click(screen.getByText('stub-open'));

    expect(
      await screen.findByText(
        "Couldn't open the document: The document service is temporarily unavailable. Try again in a few minutes."
      )
    ).toBeInTheDocument();
  });

  it('download: an expired sign-in (thrown AuthError) says so', async () => {
    mockFetch.mockRejectedValue(new AuthError('Authentication failed after all retry attempts', 'auth_exhausted'));
    renderApp();

    fireEvent.click(screen.getByText('stub-download'));

    expect(
      await screen.findByText(
        "Couldn't download the document: Your sign-in has expired. Refresh the page to sign in again."
      )
    ).toBeInTheDocument();
  });

  it('send to index: a thrown 404 for two documents names the count', async () => {
    mockFetch.mockRejectedValue(new ApiError('HTTP 404', 404));
    renderApp();

    fireEvent.click(screen.getByText('stub-index'));

    expect(
      await screen.findByText("Couldn't send 2 documents to the index: The document was not found.")
    ).toBeInTheDocument();
  });
});
