/**
 * Bulk download, driven through the real SemanticSearchControl.
 *
 * `bulkDownloadFailureForThrown` has its own unit tests (SemanticSearchApiService.thrownErrors.test.ts) but
 * they only exercise the mapping. What the user sees is decided by the `catch` in the control's
 * `handleBulkDownload`: it must call the mapping and raise the toast. If that catch were changed to swallow
 * the error, every mapping test would still pass while a failed download showed the user nothing. These
 * tests select a document, press the bulk "Download" button and make `authenticatedFetch` THROW the real
 * `ApiError`, exactly as `@spaarke/auth` does for a non-OK response, then assert the toast on screen.
 *
 * Only the edges are replaced: auth bootstrap, the search hook (fixed results), the list view (a stub that
 * selects the one row) and the heavy shared dialogs. The control, the real BulkActionBar and Fluent's
 * Toaster run for real.
 */

const mockAuthenticatedFetch = jest.fn();
jest.mock('@spaarke/auth', () => ({
  // The real guards (structural, like production) - only the network edge is replaced.
  ...jest.requireActual('../../../../shared/Spaarke.Auth/src/errorGuards'),
  authenticatedFetch: (...args: unknown[]) => mockAuthenticatedFetch(...args),
  getTelemetryConnectionString: jest.fn().mockResolvedValue(null),
}));

jest.mock('../authInit', () => ({ initializeAuth: jest.fn().mockResolvedValue(undefined) }));
jest.mock('../../../shared/utils/environmentVariables', () => ({
  getApiBaseUrl: jest.fn().mockResolvedValue('https://api.example.com'),
  resolveSignInIdentity: jest.fn().mockResolvedValue({ tenantId: 't', clientAppId: 'c', bffAppId: 'b' }),
}));
jest.mock('../services/SearchIndexResolver', () => ({
  resolveSearchIndexNameAsync: jest.fn().mockResolvedValue(null),
}));
jest.mock('@spaarke/ui-components/dist/services/AppInsightsService', () => ({
  AppInsightsService: { trackEvent: jest.fn(), initializeFromRuntime: jest.fn() },
}));
jest.mock('@spaarke/ui-components/dist/components/FindSimilarViewer', () => ({ FindSimilarViewerDialog: () => null }));
jest.mock('@spaarke/ui-components/dist/components/DocumentEmailWizard', () => ({ DocumentEmailWizard: () => null }));

const mockResults = [{ documentId: 'doc-1', name: 'Agreement.docx', documentType: 'Contract' }];
jest.mock('../hooks', () => ({
  ...jest.requireActual('../hooks'),
  useSemanticSearch: () => ({
    results: mockResults,
    totalCount: mockResults.length,
    isLoading: false,
    isLoadingMore: false,
    error: null,
    hasMore: false,
    query: '',
    search: jest.fn().mockResolvedValue(undefined),
    loadMore: jest.fn(),
    reset: jest.fn(),
  }),
}));

jest.mock('../components', () => {
  const actual = jest.requireActual('../components');
  return {
    ...actual,
    // Selecting a row is ListView's job and not under test; this stub selects the one row.
    ListView: (props: { onSelectionChange: (ids: Set<string>) => void }) => (
      <button type="button" onClick={() => props.onSelectionChange(new Set(['doc-1']))}>
        select-row
      </button>
    ),
  };
});

import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { ApiError } from '../../../../shared/Spaarke.Auth/src/errors';
import { SemanticSearchControl } from '../SemanticSearchControl';
import type { ISemanticSearchControlProps } from '../types';

const raw = (value: unknown) => ({ raw: value });

function makeContext(): ISemanticSearchControlProps['context'] {
  return {
    parameters: {
      apiBaseUrl: raw('https://api.example.com'),
      tenantId: raw('t'),
      clientAppId: raw('c'),
      bffAppId: raw('b'),
      searchScope: raw('all'),
      compactMode: raw(false),
      showViewToggle: raw(true),
    },
    webAPI: { retrieveRecord: jest.fn(), retrieveMultipleRecords: jest.fn() },
    userSettings: { userId: 'user-1' },
    mode: { isControlDisabled: false },
  } as unknown as ISemanticSearchControlProps['context'];
}

async function openBulkDownload(): Promise<void> {
  render(
    <FluentProvider theme={webLightTheme}>
      <SemanticSearchControl context={makeContext()} notifyOutputChanged={jest.fn()} onDocumentSelect={jest.fn()} />
    </FluentProvider>
  );
  fireEvent.click(await screen.findByText('select-row'));
  fireEvent.click(await screen.findByRole('button', { name: 'Download 1 selected document' }));
}

describe('SemanticSearchControl - a failed bulk download reaches the user', () => {
  beforeEach(() => {
    mockAuthenticatedFetch.mockReset();
    jest.spyOn(console, 'error').mockImplementation(() => undefined);
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('a thrown 413 raises the "Too many documents" toast with no Retry action', async () => {
    mockAuthenticatedFetch.mockRejectedValue(new ApiError('HTTP 413', 413, null));
    await openBulkDownload();

    expect(await screen.findByText('Too many documents')).toBeInTheDocument();
    expect(screen.getByText('Maximum 500 documents per bulk download.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument();
    expect(mockAuthenticatedFetch).toHaveBeenCalledWith(
      'https://api.example.com/api/documents/bulk-download',
      expect.objectContaining({ method: 'POST', body: JSON.stringify({ documentIds: ['doc-1'] }) })
    );
  });

  it('a thrown 404 raises "Download failed" with the no-accessible-documents body and a Retry action', async () => {
    mockAuthenticatedFetch.mockRejectedValue(new ApiError('Not Found', 404, { title: 'Not Found', status: 404 }));
    await openBulkDownload();

    expect(await screen.findByText('Download failed')).toBeInTheDocument();
    expect(screen.getByText('No accessible documents in the current selection.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });

  it('a network failure (not an HTTP error) still raises a "Download failed" toast carrying the message', async () => {
    mockAuthenticatedFetch.mockRejectedValue(new TypeError('Failed to fetch'));
    await openBulkDownload();

    await waitFor(() => expect(screen.getByText('Failed to fetch')).toBeInTheDocument());
    expect(screen.getByText('Download failed')).toBeInTheDocument();
  });
});
