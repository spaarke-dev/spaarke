/**
 * R4 task 043: ProjectPage has ONE "no access" path. Since ebilling-r1's task-130 batch a stale grant answers 403, not
 * 404, so the old 404 "Project Not Found" branch was folded into the 403 handling: a 404 shows the same access message.
 * Must-not-fire: any other failure still shows the generic "Failed to load project" error (a fixed sentence, never the
 * server's body). Also the binder-fault retry on the project read (FR-28).
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { ApiError } from '../src/types';

const { getProjectById } = vi.hoisted(() => ({ getProjectById: vi.fn() }));

vi.mock('../src/api/web-api-client', async () => {
  const actual = await vi.importActual<typeof import('../src/api/web-api-client')>('../src/api/web-api-client');
  return { ...actual, getProjectById };
});

vi.mock('../src/hooks/useExternalContext', () => ({
  useExternalContext: () => ({ context: null, isLoading: false, error: null, refresh: () => undefined }),
}));

import { ProjectPage } from '../src/pages/ProjectPage';
import { BINDER_FAULT_MESSAGE, BINDER_FAULT_RETRY_DELAYS_MS } from '../src/api/me-client';

const ACCESS_MESSAGE = /You do not have permission to view this project/;

function renderPage() {
  render(
    <FluentProvider theme={webLightTheme}>
      <MemoryRouter initialEntries={['/project/p-043']}>
        <Routes>
          <Route path="/project/:id" element={<ProjectPage />} />
        </Routes>
      </MemoryRouter>
    </FluentProvider>
  );
}

afterEach(() => {
  cleanup();
  getProjectById.mockReset();
});

describe('ProjectPage no-access path', () => {
  it.each([403, 404])('ProjectPage_%s_ShowsTheAccessMessage_NoNotFoundBranch', async status => {
    getProjectById.mockRejectedValue(new ApiError(status, 'x'));
    renderPage();
    expect(await screen.findByText(ACCESS_MESSAGE)).toBeTruthy();
    expect(screen.queryByText(/Project Not Found/)).toBeNull();
    expect(screen.queryByText(/could not be found/)).toBeNull();
  });

  it('ProjectPage_500_ShowsTheGenericError_NotTheAccessMessage_NeverTheServerBody', async () => {
    getProjectById.mockRejectedValue(new ApiError(500, '{"title":"boom","traceId":"00-abc"}'));
    renderPage();
    expect(await screen.findByText(/Failed to load project/)).toBeTruthy();
    expect(screen.queryByText(ACCESS_MESSAGE)).toBeNull();
    expect(screen.queryByText(/traceId|boom/)).toBeNull();
    expect(getProjectById).toHaveBeenCalledTimes(1); // a plain 5xx is not retried
  });
});

// R4 FR-28 (#2269): a partner opening a project link has no gate before this page, so the project read can be the first
// BFF call and can meet a binder fault. It is retried 3x (1 s, 2 s, 4 s), then the message with a Retry.
describe('ProjectPage binder fault (503)', () => {
  const binderFault = () =>
    new ApiError(
      503,
      JSON.stringify({
        status: 503,
        reasonCode: 'sdap.access.deny.contact_lookup_failed',
        errorCode: 'sdap.access.deny.contact_lookup_failed',
        traceId: '00-x',
      })
    );

  afterEach(() => {
    vi.useRealTimers();
  });

  it('ProjectPage_BinderFaultEveryTime_ShowsTheMessageWithRetry_RetryRunsTheReadAgain', async () => {
    vi.useFakeTimers();
    getProjectById.mockRejectedValue(binderFault());
    renderPage();
    await act(async () => {
      for (const ms of BINDER_FAULT_RETRY_DELAYS_MS) await vi.advanceTimersByTimeAsync(ms);
    });
    expect(screen.getByText(BINDER_FAULT_MESSAGE)).toBeTruthy();
    expect(getProjectById).toHaveBeenCalledTimes(4);
    expect(screen.queryByText(/Failed to load project|reasonCode|traceId/)).toBeNull();
    expect(screen.queryByText(ACCESS_MESSAGE)).toBeNull();
    vi.useRealTimers();

    // Retry runs the read again (here the server now gives a definite answer).
    getProjectById.mockReset();
    getProjectById.mockRejectedValue(new ApiError(403, 'x'));
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByText(ACCESS_MESSAGE)).toBeTruthy();
    expect(getProjectById).toHaveBeenCalledTimes(1);
  });
});
