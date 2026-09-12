/**
 * Unit tests for DocumentProfileSection (spaarkeai-word-add-in-r1 task 021 FR-07 + task 022 FR-08).
 *
 * Covers:
 * - No-identity state: no network call, honest message, no blank fields; Generate Profile present but
 *   disabled with a stated reason, and clicking it issues no network request.
 * - Completed: all four fields render, keywords as a chip list, the four fields remain read-only
 *   (Generate Profile is the one control this section carries — an action, not a field edit).
 * - Each of the six non-Completed `sprk_filesummarystatus` states renders its own distinct message
 *   (no catch-all default — the closed acceptance set from the POML).
 * - A `summaryStatus` value outside the seven enumerated options fails honestly (escalation-trigger
 *   fail-safe) rather than silently rendering blank fields.
 * - A network/API failure renders an error state, not blank fields or an unhandled throw.
 * - Generate Profile (task 022): busy state while in flight; on 202 the displayed status moves to
 *   Pending and the record is re-read; no confirmation dialog ever appears, including when the current
 *   profile is Completed; a trigger failure surfaces an error without corrupting the read outcome.
 */

import React from 'react';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
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
const mockPost = apiClient.post as jest.Mock;

const DOCUMENT_ID = '11111111-1111-1111-1111-111111111111';
const GENERATE_PROFILE_ROUTE = `/api/office/documents/${DOCUMENT_ID}/generate-profile`;

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

function generateProfileButton() {
  return screen.getByRole('button', { name: /generate profile/i });
}

describe('DocumentProfileSection', () => {
  beforeEach(() => {
    mockGet.mockReset();
    mockPost.mockReset();
  });

  describe('no-identity state', () => {
    it('shows the no-identity message and makes no profile read call', async () => {
      renderWithProvider(<DocumentProfileSection />);

      // Exact match — the Generate Profile disabled-reason text also contains "not yet in Spaarke"
      // (a second, deliberately similar message; see the dedicated test below), so this asserts the
      // body message specifically rather than the shared substring.
      expect(
        screen.getByText('This document is not yet in Spaarke. Save it first to see its AI profile here.')
      ).toBeTruthy();

      // Give effects a chance to run.
      await Promise.resolve();
      expect(mockGet).not.toHaveBeenCalled();
    });

    it('renders Generate Profile disabled with a stated reason, and a click issues no network request', async () => {
      const user = userEvent.setup();
      renderWithProvider(<DocumentProfileSection />);

      const button = generateProfileButton();
      expect(button).toBeDisabled();
      expect(screen.getAllByText(/not yet in Spaarke/i).length).toBeGreaterThan(0);

      await user.click(button);

      expect(mockGet).not.toHaveBeenCalled();
      expect(mockPost).not.toHaveBeenCalled();
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

      // The four profile fields are read-only: no input/textarea, and the ONE button in this section
      // is Generate Profile (an action, not a field save-back) — enabled here since Completed must not
      // block re-generation (spec Assumptions: overwrite is always available).
      expect(container.querySelector('input')).toBeNull();
      expect(container.querySelector('textarea')).toBeNull();
      expect(container.querySelectorAll('button')).toHaveLength(1);
      expect(generateProfileButton()).toBeEnabled();

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

  describe('Generate Profile (task 022, FR-08)', () => {
    it('shows a busy state while in flight, then moves the displayed status to Pending and re-reads', async () => {
      mockGet
        .mockResolvedValueOnce(
          envelope({
            summary: 'A four-paragraph summary of the agreement.',
            tldr: 'Short TL;DR.',
            keywords: 'contract, confidentiality',
            documentType: 'NDA',
            summaryStatus: 100000002,
          })
        )
        .mockResolvedValueOnce(envelope({ summaryStatus: 100000001 }));

      let resolvePost: (value: unknown) => void = () => {
        throw new Error('resolvePost called before it was assigned');
      };
      mockPost.mockImplementationOnce(
        () =>
          new Promise(resolve => {
            resolvePost = resolve;
          })
      );

      const user = userEvent.setup();
      renderWithProvider(<DocumentProfileSection documentId={DOCUMENT_ID} />);
      await waitFor(() => expect(screen.getByText('A four-paragraph summary of the agreement.')).toBeTruthy());

      await user.click(generateProfileButton());

      // Busy state: button disabled and relabeled while the POST is in flight.
      await waitFor(() => expect(screen.getByRole('button', { name: /generating/i })).toBeDisabled());
      expect(mockPost).toHaveBeenCalledWith(GENERATE_PROFILE_ROUTE);

      await act(async () => {
        resolvePost({ documentId: DOCUMENT_ID, correlationId: 'corr-1' });
      });

      // On 202, the displayed status moves to Pending immediately (task 021's SAME status mapping —
      // no second status table) and the hook re-reads the record.
      await waitFor(() => expect(screen.getByText(/in progress/i)).toBeTruthy());
      expect(mockGet).toHaveBeenCalledTimes(2);
      expect(generateProfileButton()).toBeEnabled();
    });

    it('overwrites a Completed profile with no confirmation prompt', async () => {
      mockGet.mockResolvedValueOnce(
        envelope({
          summary: 'A four-paragraph summary of the agreement.',
          tldr: 'Short TL;DR.',
          keywords: 'contract',
          documentType: 'NDA',
          summaryStatus: 100000002,
        })
      );
      mockGet.mockResolvedValueOnce(envelope({ summaryStatus: 100000001 }));
      mockPost.mockResolvedValueOnce({ documentId: DOCUMENT_ID, correlationId: 'corr-2' });

      const user = userEvent.setup();
      renderWithProvider(<DocumentProfileSection documentId={DOCUMENT_ID} />);
      await waitFor(() => expect(screen.getByText('A four-paragraph summary of the agreement.')).toBeTruthy());

      // A single click is the entire interaction — no "are you sure" step per spec Assumptions.
      await user.click(generateProfileButton());

      await waitFor(() => expect(mockPost).toHaveBeenCalledTimes(1));
      expect(screen.queryByRole('dialog')).toBeNull();
      expect(screen.queryByRole('alertdialog')).toBeNull();
    });

    it('disabled without a resolved identity issues no network request when clicked', async () => {
      const user = userEvent.setup();
      renderWithProvider(<DocumentProfileSection />);

      await user.click(generateProfileButton());

      expect(mockPost).not.toHaveBeenCalled();
    });

    it('surfaces a trigger failure without corrupting the read outcome, and never re-reads', async () => {
      mockGet.mockResolvedValueOnce(
        envelope({
          summary: 'A four-paragraph summary of the agreement.',
          tldr: 'Short TL;DR.',
          keywords: 'contract',
          documentType: 'NDA',
          summaryStatus: 100000002,
        })
      );
      mockPost.mockRejectedValueOnce(
        new ApiClientError({
          type: 'about:blank',
          title: 'Too Many Requests',
          status: 429,
          detail: 'Rate limited — try again shortly.',
        })
      );

      const user = userEvent.setup();
      renderWithProvider(<DocumentProfileSection documentId={DOCUMENT_ID} />);
      await waitFor(() => expect(screen.getByText('A four-paragraph summary of the agreement.')).toBeTruthy());

      await user.click(generateProfileButton());

      await waitFor(() => expect(screen.getByText('Rate limited — try again shortly.')).toBeTruthy());
      // The read outcome is untouched by a trigger-POST failure — the Completed fields still render.
      expect(screen.getByText('A four-paragraph summary of the agreement.')).toBeTruthy();
      expect(generateProfileButton()).toBeEnabled();
      expect(mockGet).toHaveBeenCalledTimes(1);
    });

    it('coordinator-review fix: a 503 (AI facade unavailable) shows the error, never "Pending", and never re-reads', async () => {
      // Server-side fix: OFFICE_PROFILE_002 is what OfficeEndpoints.GenerateProfileAsync returns when
      // GenerateProfileDispatchOutcome.FacadeUnavailable comes back — the trigger was NEVER dispatched.
      // The client must never move the displayed status to Pending for a job that will never run.
      mockGet.mockResolvedValueOnce(envelope({ summaryStatus: 100000000 })); // None — not yet profiled
      mockPost.mockRejectedValueOnce(
        new ApiClientError({
          type: 'https://spaarke.com/errors/office/office_profile_002',
          title: 'Service Unavailable',
          status: 503,
          detail: 'Document profiling is currently unavailable. Try again later.',
        })
      );

      const user = userEvent.setup();
      renderWithProvider(<DocumentProfileSection documentId={DOCUMENT_ID} />);
      await waitFor(() => expect(screen.getByText(/has not been profiled yet/i)).toBeTruthy());

      await user.click(generateProfileButton());

      await waitFor(() =>
        expect(screen.getByText('Document profiling is currently unavailable. Try again later.')).toBeTruthy()
      );
      // NEVER "Pending" — the trigger never dispatched, so the pane must not claim it did.
      expect(screen.queryByText(/in progress/i)).toBeNull();
      // The read outcome is unchanged (still "not been profiled yet"), and no re-read was triggered.
      expect(screen.getByText(/has not been profiled yet/i)).toBeTruthy();
      expect(generateProfileButton()).toBeEnabled();
      expect(mockGet).toHaveBeenCalledTimes(1);
    });
  });
});
