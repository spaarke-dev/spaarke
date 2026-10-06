/**
 * unified-access-control-r2 task 140 (verifier item 8 — the six pre-existing `tsc --noEmit` errors, fixed rather than
 * recorded). Two of them were in the dashboard: its event items read `_sprk_projectid_value`, a column `sprk_event` does
 * not have (its project lookup is `sprk_regardingproject`), so every Recent Activity and Upcoming item named its project
 * "Unknown Project". The page now resolves the name from the project rows it already loads. (The page is not routed today
 * — the workspace shell's widget registry mounts these sections — so the defect was latent.) Rendered for real; only the
 * network seams (web-api-client) and the context hook's data source are substituted.
 */
import { describe, it, expect, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

// vi.mock factories are hoisted above the imports, so the date they use is hoisted with them.
const { inFiveDays } = vi.hoisted(() => ({ inFiveDays: new Date(Date.now() + 5 * 24 * 60 * 60 * 1000).toISOString() }));

vi.mock('../src/api/web-api-client', async () => {
  const actual = await vi.importActual<typeof import('../src/api/web-api-client')>('../src/api/web-api-client');
  return {
    ...actual,
    getProjects: vi
      .fn()
      .mockResolvedValue([
        { sprk_projectid: 'p-1', sprk_name: 'Alpha Merger', sprk_referencenumber: 'PRJ-1', modifiedon: inFiveDays },
      ]),
    // The BFF's event shape: the project is named by its id, in the regarding lookup.
    getEvents: vi.fn().mockResolvedValue([
      {
        sprk_eventid: 'e-1',
        sprk_name: 'Signing deadline',
        sprk_duedate: inFiveDays,
        createdon: new Date().toISOString(),
        _sprk_regardingproject_value: 'p-1',
      },
    ]),
    getDocuments: vi.fn().mockResolvedValue([]),
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

describe('OutsideCounselDashboard — event items name their project', () => {
  it('shows the project NAME on Recent Activity and Upcoming items, never "Unknown Project"', async () => {
    render(
      <FluentProvider theme={webLightTheme}>
        <MemoryRouter>
          <OutsideCounselDashboard />
        </MemoryRouter>
      </FluentProvider>
    );

    // Recent Activity: "{project} · {relative date}".
    expect(await screen.findByText(/^Alpha Merger · /)).toBeTruthy();

    // Upcoming: the project on its own line, inside THAT card (My Projects also lists the name, so scope to the card).
    const upcoming = (await screen.findByText('Upcoming Events & Tasks')).closest('[role="group"]') as HTMLElement;
    expect(upcoming).not.toBeNull();
    expect(await within(upcoming).findByText('Alpha Merger')).toBeTruthy();
    expect(screen.queryByText(/Unknown Project/)).toBeNull();
  });
});
