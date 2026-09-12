/**
 * Unit tests for DocumentProfileSection (spaarkeai-word-add-in-r1 task 021, FR-07).
 *
 * Covers:
 * - No-identity state: no network call, honest message, no blank fields.
 * - Completed: all four fields render, keywords as a chip list, nothing editable.
 * - Each of the six non-Completed `sprk_filesummarystatus` states renders its own distinct message
 *   (no catch-all default — the closed acceptance set from the POML).
 * - A `summaryStatus` value outside the seven enumerated options fails honestly (escalation-trigger
 *   fail-safe) rather than silently rendering blank fields.
 * - A network/API failure renders an error state, not blank fields or an unhandled throw.
 */

import React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { DocumentProfileSection } from '../DocumentProfileSection';
import { apiClient, ApiClientError } from '@shared/services';

// Fluent v9 MessageBar uses ResizeObserver for reflow detection; jsdom doesn't provide one.
class ResizeObserverMock {
  observe(): void {
    /* no-op */
  }
  unobserve(): void {
    /* no-op */
  }
  disconnect(): void {
    /* no-op */
  }
}
// eslint-disable-next-line @typescript-eslint/no-explicit-any
(globalThis as any).ResizeObserver = (globalThis as any).ResizeObserver ?? ResizeObserverMock;

jest.mock('@shared/services', () => {
  const actual = jest.requireActual('@shared/services');
  return {
    ...actual,
    apiClient: {
      get: jest.fn(),
      post: jest.fn(),
      put: jest.fn(),
      delete: jest.fn(),
      uploadFile: jest.fn(),
      configure: jest.fn(),
    },
  };
});

const mockGet = apiClient.get as jest.Mock;

const DOCUMENT_ID = '11111111-1111-1111-1111-111111111111';

const renderWithProvider = (ui: React.ReactElement) =>
  render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

function envelope(fields: {
  summary?: string | null;
  tldr?: string | null;
  keywords?: string | null;
  documentType?: string | null;
  summaryStatus?: number | null;
}) {
  return { data: fields };
}

describe('DocumentProfileSection', () => {
  beforeEach(() => {
    mockGet.mockReset();
  });

  describe('no-identity state', () => {
    it('shows the no-identity message and makes no profile read call', async () => {
      renderWithProvider(<DocumentProfileSection />);

      expect(screen.getByText(/not yet in Spaarke/i)).toBeTruthy();

      // Give effects a chance to run.
      await Promise.resolve();
      expect(mockGet).not.toHaveBeenCalled();
    });
  });

  describe('Completed status (100000002)', () => {
    it('renders all four profile fields and nothing editable', async () => {
      mockGet.mockResolvedValueOnce(
        envelope({
          summary: 'A four-paragraph summary of the agreement.',
          tldr: 'Short TL;DR.',
          keywords: 'contract, confidentiality, breach',
          documentType: 'NDA',
          summaryStatus: 100000002,
        })
      );

      const { container } = renderWithProvider(<DocumentProfileSection documentId={DOCUMENT_ID} />);

      await waitFor(() => expect(screen.getByText('A four-paragraph summary of the agreement.')).toBeTruthy());

      expect(screen.getByText('Short TL;DR.')).toBeTruthy();
      expect(screen.getByText('NDA')).toBeTruthy();
      expect(screen.getByText('contract')).toBeTruthy();
      expect(screen.getByText('confidentiality')).toBeTruthy();
      expect(screen.getByText('breach')).toBeTruthy();

      // Read-only: no input/textarea/button (save-back) inside this section.
      expect(container.querySelector('input')).toBeNull();
      expect(container.querySelector('textarea')).toBeNull();
      expect(container.querySelector('button')).toBeNull();

      expect(mockGet).toHaveBeenCalledWith(`/api/v1/documents/${DOCUMENT_ID}`);
    });

    it('renders honest empty-value text when a completed profile has a blank field', async () => {
      mockGet.mockResolvedValueOnce(
        envelope({
          summary: '',
          tldr: '',
          keywords: '',
          documentType: '',
          summaryStatus: 100000002,
        })
      );

      renderWithProvider(<DocumentProfileSection documentId={DOCUMENT_ID} />);

      await waitFor(() => expect(screen.getByText('Not classified.')).toBeTruthy());
      expect(screen.getByText('No TL;DR generated.')).toBeTruthy();
      expect(screen.getByText('No summary generated.')).toBeTruthy();
      expect(screen.getByText('No keywords generated.')).toBeTruthy();
    });
  });

  describe.each([
    [100000000, /has not been profiled yet/i],
    [100000001, /in progress/i],
    [100000003, /opted out/i],
    [100000004, /failed/i],
    [100000005, /does not support AI profiling/i],
    [100000006, /skipped/i],
  ])('non-Completed status %i', (statusCode, expectedMessage) => {
    it('renders a distinct message and no profile fields', async () => {
      mockGet.mockResolvedValueOnce(envelope({ summaryStatus: statusCode }));

      renderWithProvider(<DocumentProfileSection documentId={DOCUMENT_ID} />);

      await waitFor(() => expect(screen.getByText(expectedMessage)).toBeTruthy());
      expect(screen.queryByText(/No TL;DR generated\./)).toBeNull();
      expect(screen.queryByText(/No summary generated\./)).toBeNull();
    });
  });

  it('treats a null/missing summaryStatus the same as None', async () => {
    mockGet.mockResolvedValueOnce(envelope({ summaryStatus: null }));

    renderWithProvider(<DocumentProfileSection documentId={DOCUMENT_ID} />);

    await waitFor(() => expect(screen.getByText(/has not been profiled yet/i)).toBeTruthy());
  });

  it('fails honestly (not blank fields) on a summaryStatus outside the seven enumerated options', async () => {
    mockGet.mockResolvedValueOnce(envelope({ summaryStatus: 999999 }));

    renderWithProvider(<DocumentProfileSection documentId={DOCUMENT_ID} />);

    await waitFor(() => expect(screen.getByText(/unrecognized profile status/i)).toBeTruthy());
  });

  it('renders an error state (not an unhandled throw) when the read fails', async () => {
    mockGet.mockRejectedValueOnce(
      new ApiClientError({ type: 'about:blank', title: 'Forbidden', status: 403, detail: 'No access.' })
    );

    renderWithProvider(<DocumentProfileSection documentId={DOCUMENT_ID} />);

    await waitFor(() => expect(screen.getByText('No access.')).toBeTruthy());
  });

  it('re-fetches when documentId changes', async () => {
    mockGet.mockResolvedValueOnce(envelope({ summaryStatus: 100000001 }));
    const { rerender } = renderWithProvider(<DocumentProfileSection documentId={DOCUMENT_ID} />);
    await waitFor(() => expect(screen.getByText(/in progress/i)).toBeTruthy());

    const otherId = '22222222-2222-2222-2222-222222222222';
    mockGet.mockResolvedValueOnce(envelope({ summaryStatus: 100000004 }));
    rerender(
      <FluentProvider theme={webLightTheme}>
        <DocumentProfileSection documentId={otherId} />
      </FluentProvider>
    );

    await waitFor(() => expect(screen.getByText(/^AI profiling failed/i)).toBeTruthy());
    expect(mockGet).toHaveBeenCalledWith(`/api/v1/documents/${otherId}`);
  });
});
