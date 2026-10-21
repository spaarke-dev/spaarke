/**
 * unified-access-control-r2 task 036 fix round 2 (F3): ProjectPage carries `/me` `writeAccessPerRecord` into the
 * Documents section. Rendered for real: the page, its Overview tab, `DocumentLibrary` and `useAccessLevel`; substituted:
 * the network seam (`bffApiCall`), the project read and the `/me` data source. Removing the page's wiring makes a
 * per-record caller's Upload and Download follow the ViewOnly level, and these tests fail.
 */
import * as React from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

const { bffApiCall } = vi.hoisted(() => ({ bffApiCall: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall, bffApiBlob: vi.fn() };
});

vi.mock('../src/api/web-api-client', async () => {
  const actual = await vi.importActual<typeof import('../src/api/web-api-client')>('../src/api/web-api-client');
  return {
    ...actual,
    getProjectById: vi.fn().mockResolvedValue({
      sprk_projectid: 'p-036',
      sprk_name: 'Project 036',
      sprk_referencenumber: 'PRJ-036',
      sprk_status: 1,
      sprk_issecure: false,
    }),
  };
});

// `/me` as the hook's data source (useAccessLevel runs for real): a ViewOnly level, with or without per-record writes.
let perRecord = true;
vi.mock('../src/hooks/useExternalContext', () => ({
  useExternalContext: () => ({
    context: {
      contactId: 'c-036',
      email: 'staff@customer.example',
      projects: [{ projectId: 'p-036', accessLevel: 'ViewOnly' }],
      ...(perRecord ? { writeAccessPerRecord: true } : {}),
    },
    isLoading: false,
    error: null,
  }),
}));

// Tabs this test is not about.
vi.mock('../src/components/EventsCalendar', () => ({ EventsCalendar: () => null }));
vi.mock('../src/components/SmartTodo', () => ({ SmartTodo: () => null }));

import { ProjectPage } from '../src/pages/ProjectPage';

function renderProject(): void {
  render(
    <FluentProvider theme={webLightTheme}>
      <MemoryRouter initialEntries={['/project/p-036']}>
        <Routes>
          <Route path="/project/:id" element={<ProjectPage />} />
        </Routes>
      </MemoryRouter>
    </FluentProvider>
  );
}

beforeEach(() => {
  bffApiCall.mockReset();
  // The project's document list: one document, and the BFF's answer for the record in hand.
  bffApiCall.mockImplementation(async () => ({
    value: [{ sprk_documentid: 'd-036', sprk_name: 'Brief.pdf', createdon: '2026-10-01T00:00:00Z' }],
    canCreate: true,
  }));
});

describe('ProjectPage: /me writeAccessPerRecord reaches the Documents section', () => {
  it('a per-record caller with a ViewOnly level is offered Upload (list canCreate) and Download', async () => {
    perRecord = true;
    renderProject();

    expect(await screen.findByRole('button', { name: /Upload Document/ })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Download Brief.pdf' })).toBeTruthy();
  });

  it('every other ViewOnly caller is offered neither (the level rule, unchanged)', async () => {
    perRecord = false;
    renderProject();

    await screen.findByText('Brief.pdf');
    expect(screen.queryByRole('button', { name: /Upload Document/ })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Download Brief.pdf' })).toBeNull();
  });
});
