/**
 * unified-access-control-r2 task 105 (verifier F3) — the dashboard's Recent Activity, Upcoming and My Documents
 * sections never claim "nothing" when the BFF flagged a list as cut short or unreadable; the document count is a lower
 * bound ("N+") when a list was cut. Rendered for real; only the network seam (web-api-client) and the context hook's
 * data source are substituted.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

const { getEvents, getDocuments } = vi.hoisted(() => ({ getEvents: vi.fn(), getDocuments: vi.fn() }));

vi.mock('../src/api/web-api-client', async () => {
  const actual = await vi.importActual<typeof import('../src/api/web-api-client')>('../src/api/web-api-client');
  return {
    ...actual,
    getProjects: vi.fn().mockResolvedValue({
      items: [{ sprk_projectid: 'p-1', sprk_name: 'Alpha Merger', sprk_referencenumber: 'PRJ-1' }],
      truncated: false,
    }),
    getEvents,
    getDocuments,
  };
});

vi.mock('../src/hooks/useExternalContext', () => ({
  useExternalContext: () => ({
    context: {
      contactId: 'c-1',
      email: 'counsel@firm-a.example',
      projects: [{ projectId: 'p-1', accessLevel: 'ViewOnly' }],
    },
    isLoading: false,
    error: null,
    refresh: vi.fn(),
  }),
}));

import { OutsideCounselDashboard } from '../src/pages/OutsideCounselDashboard';

function renderDashboard(): void {
  render(
    <FluentProvider theme={webLightTheme}>
      <MemoryRouter>
        <OutsideCounselDashboard />
      </MemoryRouter>
    </FluentProvider>
  );
}

const EMPTY_STATES = /No recent activity found|No upcoming events or tasks|No documents found across your projects/;

beforeEach(() => {
  getEvents.mockReset();
  getDocuments.mockReset();
});

describe('OutsideCounselDashboard — cut-short and unreadable lists', () => {
  it('an empty list the BFF flagged truncated says it could not be loaded — never "no activity / no documents"', async () => {
    getEvents.mockResolvedValue({ items: [], truncated: true });
    getDocuments.mockResolvedValue({ items: [], truncated: true });
    renderDashboard();

    // Recent Activity, Upcoming and My Documents each say so.
    await waitFor(() => expect(screen.getAllByText(/could not be loaded just now/)).toHaveLength(3));
    expect(screen.queryByText(EMPTY_STATES)).toBeNull();
  });

  it('a read that throws is shown as could-not-be-loaded, not as an empty list', async () => {
    getEvents.mockRejectedValue(new Error('network'));
    getDocuments.mockRejectedValue(new Error('network'));
    renderDashboard();

    await waitFor(() => expect(screen.getAllByText(/could not be loaded just now/)).toHaveLength(3));
    expect(screen.queryByText(EMPTY_STATES)).toBeNull();
  });

  it('a COMPLETE empty list keeps its empty states and shows no notice', async () => {
    getEvents.mockResolvedValue({ items: [], truncated: false });
    getDocuments.mockResolvedValue({ items: [], truncated: false });
    renderDashboard();

    expect(await screen.findByText(/No documents found across your projects/)).toBeTruthy();
    expect(await screen.findByText(/No recent activity found/)).toBeTruthy();
    expect(screen.queryByTestId('truncated-list-notice')).toBeNull();
  });

  it('a cut-short document list shows its count as a lower bound ("N+")', async () => {
    getEvents.mockResolvedValue({ items: [], truncated: false });
    getDocuments.mockResolvedValue({
      items: Array.from({ length: 12 }, (_, i) => ({ sprk_documentid: `d-${i}`, sprk_name: `Doc ${i}` })),
      truncated: true,
    });
    renderDashboard();

    expect(await screen.findByText('My Documents (12+)')).toBeTruthy();
    expect(screen.getByText(/Showing 10 of more than 12 documents/)).toBeTruthy();
  });

  it('a complete document list shows its exact count', async () => {
    getEvents.mockResolvedValue({ items: [], truncated: false });
    getDocuments.mockResolvedValue({
      items: Array.from({ length: 12 }, (_, i) => ({ sprk_documentid: `d-${i}`, sprk_name: `Doc ${i}` })),
      truncated: false,
    });
    renderDashboard();

    expect(await screen.findByText('My Documents (12)')).toBeTruthy();
    expect(screen.getByText(/Showing 10 of 12 documents/)).toBeTruthy();
  });
});
