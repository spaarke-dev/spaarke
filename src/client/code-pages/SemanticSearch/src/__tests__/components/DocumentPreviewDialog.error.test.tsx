/**
 * DocumentPreviewDialog — the line under "Preview unavailable" when the preview URL cannot be loaded.
 *
 * `@spaarke/auth`'s authenticatedFetch never returns a non-2xx Response: it throws ApiError (status +
 * ProblemDetails) or, once its 401 retries are spent, AuthError. Before the fix the dialog's
 * "Failed to load preview: <status>" frame sat behind a dead `!response.ok` check and the catch showed the
 * bare ApiError message ("HTTP 500") or the browser's "Failed to fetch". These tests throw the real classes.
 */

import React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

const mockFetch = jest.fn<Promise<Response>, [string, RequestInit?]>();

jest.mock('@spaarke/auth', () => ({
  ...jest.requireActual('@spaarke/auth'),
  authenticatedFetch: (url: string, init?: RequestInit) => mockFetch(url, init),
}));
jest.mock('../../services/apiBase', () => ({
  getBffBaseUrl: () => 'https://bff.example.test',
}));

import { ApiError, AuthError } from '@spaarke/auth';
import { DocumentPreviewDialog } from '../../components/DocumentPreviewDialog';
import type { DocumentSearchResult } from '../../types';

function renderOpenDialog() {
  const result = { documentId: 'doc-1', name: 'Contract.docx' } as unknown as DocumentSearchResult;
  return render(
    <FluentProvider theme={webLightTheme}>
      <DocumentPreviewDialog open result={result} onClose={jest.fn()} onFindSimilar={jest.fn()} />
    </FluentProvider>
  );
}

describe('DocumentPreviewDialog — preview load failure text', () => {
  beforeEach(() => {
    mockFetch.mockReset();
  });

  it("a thrown ApiError shows the server's detail inside the frame", async () => {
    const detail = 'The document has no file in SharePoint Embedded.';
    mockFetch.mockRejectedValue(new ApiError(detail, 404, { title: 'Not Found', status: 404, detail }));
    renderOpenDialog();

    expect(await screen.findByText(`Failed to load preview: ${detail}`)).toBeInTheDocument();
  });

  it('a thrown bare 500 reads "temporarily unavailable", not "HTTP 500"', async () => {
    mockFetch.mockRejectedValue(new ApiError('HTTP 500', 500));
    renderOpenDialog();

    expect(
      await screen.findByText(
        'Failed to load preview: The preview service is temporarily unavailable. Try again in a few minutes.'
      )
    ).toBeInTheDocument();
    expect(screen.queryByText('HTTP 500')).not.toBeInTheDocument();
  });

  it('a thrown bare 403 reads as a permission sentence', async () => {
    mockFetch.mockRejectedValue(new ApiError('HTTP 403', 403));
    renderOpenDialog();

    expect(
      await screen.findByText('Failed to load preview: You do not have permission to view this document.')
    ).toBeInTheDocument();
  });

  it('an expired sign-in (thrown AuthError) says so', async () => {
    mockFetch.mockRejectedValue(new AuthError('Authentication failed after all retry attempts', 'auth_exhausted'));
    renderOpenDialog();

    expect(
      await screen.findByText('Failed to load preview: Your sign-in has expired. Refresh the page to sign in again.')
    ).toBeInTheDocument();
  });

  it('a network failure does not show "Failed to fetch"', async () => {
    mockFetch.mockRejectedValue(new TypeError('Failed to fetch'));
    renderOpenDialog();

    expect(
      await screen.findByText('Failed to load preview: The Spaarke server could not be reached. Check your connection.')
    ).toBeInTheDocument();
  });

  it('a RETURNED non-2xx (a fetch that does not throw) keeps the framed status', async () => {
    mockFetch.mockResolvedValue({ ok: false, status: 502 } as Response);
    renderOpenDialog();

    expect(await screen.findByText('Failed to load preview (HTTP 502).')).toBeInTheDocument();
  });
});
