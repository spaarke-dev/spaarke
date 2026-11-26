/**
 * R4 task 043: ProjectPage has ONE "no access" path. Since ebilling-r1's task-130 batch a stale grant answers 403, not
 * 404, so the old 404 "Project Not Found" branch was folded into the 403 handling: a 404 shows the same access message.
 * Must-not-fire: any other failure still shows the generic "Failed to load project" error.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
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

  it('ProjectPage_500_ShowsTheGenericError_NotTheAccessMessage', async () => {
    getProjectById.mockRejectedValue(new ApiError(500, 'boom'));
    renderPage();
    expect(await screen.findByText(/Failed to load project/)).toBeTruthy();
    expect(screen.queryByText(ACCESS_MESSAGE)).toBeNull();
  });
});
