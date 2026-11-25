/**
 * DocumentProfileSection — the "taking longer than expected" hint (spaarkeai-word-add-in-r1 task 137, GitHub #1090).
 *
 * After Generate Profile the pane follows the job for PROFILE_POLL_CAP_MS. A job that was dead-lettered or expired before
 * it started leaves the status Pending for good, so at the cap the pane says so, next to the status: not an error, no new
 * button (Refresh is the existing re-read), Generate Profile stays enabled, and the status is never turned into Completed.
 * A job that finishes before the cap shows its result and no hint.
 */

import React from 'react';
import { act, fireEvent, render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { DocumentProfileSection } from '../DocumentProfileSection';
import { PROFILE_POLL_CAP_MS, PROFILE_POLL_INTERVAL_MS } from '../../hooks/useDocumentProfile';
import { apiClient } from '@shared/services';

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

const DOC = '11111111-1111-1111-1111-111111111111';
const FAILED = 100000004;
const PENDING = 100000001;
const COMPLETED = 100000002;

const HINT = /taking longer than expected. Check back in a few minutes./i;

const renderSection = (ui: React.ReactElement) => render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

async function advance(ms: number) {
  await act(async () => {
    await jest.advanceTimersByTimeAsync(ms);
  });
}

async function clickAndAccept(name: RegExp) {
  await act(async () => {
    fireEvent.click(screen.getByRole('button', { name }));
    await Promise.resolve();
  });
}

describe('DocumentProfileSection — wait hint after Generate Profile (task 137)', () => {
  beforeEach(() => {
    jest.useFakeTimers();
    mockGet.mockReset();
    mockPost.mockReset();
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  async function renderOnAFailedDocument() {
    mockGet.mockResolvedValueOnce({ data: { summaryStatus: FAILED } });
    renderSection(<DocumentProfileSection documentId={DOC} />);
    await act(async () => {
      await Promise.resolve();
    });
    expect(screen.getByText(/AI profiling failed/i)).toBeTruthy();
  }

  it('at the cap with the status still Pending shows the hint, keeps Pending, and never shows Completed', async () => {
    await renderOnAFailedDocument();
    mockPost.mockResolvedValueOnce({ documentId: DOC });
    mockGet.mockResolvedValue({ data: { summaryStatus: PENDING } }); // the job never starts

    await clickAndAccept(/generate profile/i);
    await advance(PROFILE_POLL_CAP_MS - PROFILE_POLL_INTERVAL_MS * 2);
    expect(screen.getByText(/in progress/i)).toBeTruthy();
    expect(screen.queryByText(HINT)).toBeNull(); // not before the cap

    await advance(PROFILE_POLL_INTERVAL_MS * 3);

    expect(screen.getByText(HINT)).toBeTruthy();
    expect(screen.getByText(/in progress/i)).toBeTruthy(); // the last status stays
    expect(screen.queryByText(/Document type/i)).toBeNull(); // no Completed fields
    expect(screen.queryByRole('alert')).toBeNull(); // not an error
    expect(screen.getByRole('button', { name: /generate profile/i })).toBeEnabled();
  });

  it('a result before the cap shows it with no hint, now or after the cap would have passed', async () => {
    await renderOnAFailedDocument();
    mockPost.mockResolvedValueOnce({ documentId: DOC });
    mockGet
      .mockResolvedValueOnce({ data: { summaryStatus: PENDING } })
      .mockResolvedValueOnce({ data: { summaryStatus: COMPLETED, summary: 'New summary.', documentType: 'NDA' } });

    await clickAndAccept(/generate profile/i);
    await advance(PROFILE_POLL_INTERVAL_MS * 2);

    expect(screen.getByText('New summary.')).toBeTruthy();
    expect(screen.queryByText(HINT)).toBeNull();

    await advance(PROFILE_POLL_CAP_MS * 2);
    expect(screen.queryByText(HINT)).toBeNull();
  });

  it('a terminal Failed read before the cap shows Failed with no hint', async () => {
    await renderOnAFailedDocument();
    mockPost.mockResolvedValueOnce({ documentId: DOC });
    mockGet.mockResolvedValueOnce({ data: { summaryStatus: FAILED } });

    await clickAndAccept(/generate profile/i);
    await advance(PROFILE_POLL_INTERVAL_MS);
    await advance(PROFILE_POLL_CAP_MS * 2);

    expect(screen.getByText(/AI profiling failed/i)).toBeTruthy();
    expect(screen.queryByText(HINT)).toBeNull();
  });
});
