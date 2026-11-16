/**
 * The preview under a stacked dialog, driven through the real SemanticSearchControl (ontology task 194 fix round).
 *
 * Task 194 made the Email documents wizard and the Find Similar dialog Fluent non-modal so the host lookup pane can
 * open above them. A non-modal dialog has no backdrop of its own, so a click in the margin around it lands on the
 * backdrop of the preview below (a light-dismiss modal). That must not close the preview: the host promises the user
 * lands back on the preview after the wizard (or Find Similar) closes. With nothing stacked on it, a backdrop click
 * still closes the preview as before.
 *
 * Only the edges are replaced: auth bootstrap, the search hook (fixed results), the list view (a stub that opens the
 * preview) and the shared dialogs (stubs that expose their open state and close callbacks).
 */

jest.mock('@spaarke/auth', () => ({
  authenticatedFetch: jest.fn(),
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
jest.mock('@spaarke/ui-components/dist/components/EmailComposer', () => ({
  createXrmEmailComposeHandlers: () => ({}),
}));

interface StubDialogProps {
  open: boolean;
  onClose: () => void;
}
jest.mock('@spaarke/ui-components/dist/components/FindSimilarViewer', () => ({
  FindSimilarViewerDialog: (props: StubDialogProps) =>
    props.open ? (
      <button type="button" onClick={props.onClose}>
        close-find-similar
      </button>
    ) : null,
}));
jest.mock('@spaarke/ui-components/dist/components/DocumentEmailWizard', () => ({
  DocumentEmailWizard: (props: StubDialogProps) =>
    props.open ? (
      <button type="button" onClick={props.onClose}>
        close-wizard
      </button>
    ) : null,
}));

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
    // Opening the preview is ListView's job and not under test; this stub opens it for the one row.
    ListView: (props: { onOpenPreview: (id: string) => void }) => (
      <button type="button" onClick={() => props.onOpenPreview('doc-1')}>
        open-preview
      </button>
    ),
    // The preview's backdrop click (light dismiss) calls onClose, like the real modal.
    FilePreviewDialog: (props: { onClose: () => void; onEmailDocument: () => void; onFindSimilar: () => void }) => (
      <div data-testid="preview">
        <button type="button" onClick={props.onClose}>
          preview-backdrop
        </button>
        <button type="button" onClick={props.onEmailDocument}>
          preview-email
        </button>
        <button type="button" onClick={props.onFindSimilar}>
          preview-find-similar
        </button>
      </div>
    ),
  };
});

import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SemanticSearchControl } from '../SemanticSearchControl';
import { NavigationService } from '../services/NavigationService';
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

async function openPreview(): Promise<void> {
  render(
    <FluentProvider theme={webLightTheme}>
      <SemanticSearchControl context={makeContext()} notifyOutputChanged={jest.fn()} onDocumentSelect={jest.fn()} />
    </FluentProvider>
  );
  fireEvent.click(await screen.findByText('open-preview'));
  expect(screen.getByTestId('preview')).toBeInTheDocument();
}

describe('SemanticSearchControl - a click around a stacked dialog does not close the preview below (task 194)', () => {
  beforeEach(() => {
    jest
      .spyOn(NavigationService.prototype, 'getFindSimilarUrl')
      .mockReturnValue('https://org.crm.example/viewer?data=x');
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('with the Email documents wizard open, the preview stays; after the wizard closes the user is back on it', async () => {
    await openPreview();
    fireEvent.click(screen.getByText('preview-email'));
    expect(screen.getByText('close-wizard')).toBeInTheDocument();

    fireEvent.click(screen.getByText('preview-backdrop'));
    expect(screen.getByTestId('preview')).toBeInTheDocument();

    fireEvent.click(screen.getByText('close-wizard'));
    expect(screen.queryByText('close-wizard')).not.toBeInTheDocument();
    expect(screen.getByTestId('preview')).toBeInTheDocument();
  });

  it('with Find Similar open, the preview stays; after Find Similar closes the user is back on it', async () => {
    await openPreview();
    fireEvent.click(screen.getByText('preview-find-similar'));
    expect(screen.getByText('close-find-similar')).toBeInTheDocument();

    fireEvent.click(screen.getByText('preview-backdrop'));
    expect(screen.getByTestId('preview')).toBeInTheDocument();

    fireEvent.click(screen.getByText('close-find-similar'));
    expect(screen.getByTestId('preview')).toBeInTheDocument();
  });

  it('with nothing stacked on it, a backdrop click still closes the preview', async () => {
    await openPreview();
    fireEvent.click(screen.getByText('preview-backdrop'));
    expect(screen.queryByTestId('preview')).not.toBeInTheDocument();
  });
});
