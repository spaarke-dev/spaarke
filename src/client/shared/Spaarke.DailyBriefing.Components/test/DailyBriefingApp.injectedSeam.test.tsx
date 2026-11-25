/**
 * App-level test: spaarkeai-mcp-bff-api-r2 task 010 (FR-16) - the data and navigation seam of `DailyBriefingApp`.
 *
 * A sandboxed MCP widget has no network, no MSAL and no Xrm, so the host injects a `BriefingDataService` and a
 * `BriefingNavigationHandler`. This mounts the real component with both and proves: the briefing renders from the
 * injected service with ZERO `authenticatedFetch` / `getXrm` / `window.Xrm` access; an action whose capability the host
 * did not inject is hidden, not broken; a rejecting service lands in the existing error state; and the injected
 * preferences service loads and saves. The default path (no props) is covered by the existing app-level tests.
 *
 * Classification (ADR-038 section 7): MAINTAIN - the contract a widget host relies on.
 */
import * as React from 'react';
import { render, renderHook, act, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { DailyBriefingApp } from '../src/components/DailyBriefingApp';
import { useBriefingPreferences } from '../src/hooks/useBriefingPreferences';
import type { BriefingDataService, BriefingNavigationHandler } from '../src/services/briefingDataService';
import type { NarrateResponse } from '../src/services/briefingService';
import { DEFAULT_DAILY_DIGEST_PREFERENCES } from '../src/types/notifications';
import { authenticatedFetch } from '@spaarke/auth';

const mockGetXrm = jest.fn();
jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  return {
    ...actual,
    getXrm: (...args: unknown[]) => {
      mockGetXrm(...args);
      return actual.getXrm(...args);
    },
  };
});

const MATTER_ID = '11111111-1111-1111-1111-111111111111';
const DOC_ID = '33333333-3333-3333-3333-333333333333';

// Fluent's Toast / MessageBar observe element size; jsdom has no ResizeObserver.
class ResizeObserverStub {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}
(window as unknown as { ResizeObserver: unknown }).ResizeObserver = ResizeObserverStub;

const xrmAccess = jest.fn();

function fixtureBriefing(): NarrateResponse {
  return {
    tldr: { summary: '', keyTakeaways: [], categoryCount: 2, priorityItemCount: 0, itemRefs: [] },
    channelNarratives: [
      {
        category: 'matters',
        bullets: [
          {
            narrative: 'Acme v. Globex',
            itemIds: ['item-1'],
            primaryEntityType: 'sprk_matter',
            primaryEntityId: MATTER_ID,
            primaryEntityName: 'Acme v. Globex',
          },
        ],
      },
      {
        category: 'documents',
        bullets: [
          {
            narrative: 'Master services agreement',
            itemIds: [DOC_ID],
            primaryEntityType: 'sprk_document',
            primaryEntityId: DOC_ID,
            primaryEntityName: 'Master services agreement',
          },
        ],
      },
    ],
    generatedAtUtc: '2026-11-25T08:00:00Z',
  } as NarrateResponse;
}

function serviceReturning(data: NarrateResponse): BriefingDataService & { renderBriefing: jest.Mock } {
  return { renderBriefing: jest.fn().mockResolvedValue({ status: 'success', data }) };
}

function mount(dataService: BriefingDataService, navigation?: BriefingNavigationHandler) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <DailyBriefingApp params={{}} dataService={dataService} navigation={navigation} />
    </FluentProvider>
  );
}

beforeEach(() => {
  (authenticatedFetch as jest.Mock).mockClear();
  mockGetXrm.mockClear();
  xrmAccess.mockClear();
  // Any read of window.Xrm is recorded: the injected path must never touch it.
  Object.defineProperty(window, 'Xrm', {
    configurable: true,
    get: () => {
      xrmAccess();
      return undefined;
    },
  });
});

afterEach(() => {
  delete (window as unknown as { Xrm?: unknown }).Xrm;
});

describe('DailyBriefingApp - injected data service and navigation (task 010)', () => {
  it('renders the fixture briefing and opens a record through the host, with no authenticatedFetch, getXrm or window.Xrm access', async () => {
    const service = serviceReturning(fixtureBriefing());
    const openRecord = jest.fn();

    mount(service, { openRecord });

    const link = await screen.findByRole('link', { name: /Acme v\. Globex/ });
    expect(screen.getByText(/Master services agreement/)).toBeInTheDocument();
    fireEvent.click(link);

    expect(openRecord).toHaveBeenCalledWith('sprk_matter', MATTER_ID);
    expect(service.renderBriefing).toHaveBeenCalledTimes(1);
    expect(authenticatedFetch).not.toHaveBeenCalled();
    expect(mockGetXrm).not.toHaveBeenCalled();
    expect(xrmAccess).not.toHaveBeenCalled();
  });

  it('control: with no props the component still uses authenticatedFetch and getXrm (so the zero-call assertions above can fail)', async () => {
    (authenticatedFetch as jest.Mock).mockResolvedValueOnce({ json: async () => fixtureBriefing() });

    render(
      <FluentProvider theme={webLightTheme}>
        <DailyBriefingApp params={{}} />
      </FluentProvider>
    );

    await screen.findByText(/Acme v\. Globex/);
    expect(authenticatedFetch).toHaveBeenCalledWith('/api/ai/daily-briefing/render', expect.anything());
    expect(mockGetXrm).toHaveBeenCalled();
  });

  it('hides the actions whose capability the host did not inject (To Do, dismiss, email, preferences, record links)', async () => {
    mount(serviceReturning(fixtureBriefing()), {});

    await screen.findByText('Acme v. Globex');
    // Both rows are drawn as plain text (the control for the preferences test below, which hides the Documents row).
    expect(screen.getByText('Master services agreement')).toBeInTheDocument();

    // No row menu at all: every item in it (To Do, dismiss, open record, open document) needs a capability.
    expect(screen.queryByRole('button', { name: 'More actions' })).not.toBeInTheDocument();
    // No header menu (Email Briefing, Browse Playbooks) and no Preferences menu.
    expect(screen.queryByRole('button', { name: 'Preferences' })).not.toBeInTheDocument();
    // The matter name is plain text, not a record link.
    expect(screen.queryByRole('link', { name: /Acme v\. Globex/ })).not.toBeInTheDocument();
    expect(authenticatedFetch).not.toHaveBeenCalled();
    expect(xrmAccess).not.toHaveBeenCalled();
  });

  it('offers only "Open record" in a row menu when the host injected just openRecord', async () => {
    mount(serviceReturning(fixtureBriefing()), { openRecord: jest.fn() });

    await screen.findByRole('link', { name: /Acme v\. Globex/ });
    fireEvent.click(screen.getAllByRole('button', { name: 'More actions' })[0]);

    expect(await screen.findByRole('menuitem', { name: 'Open record' })).toBeInTheDocument();
    expect(screen.queryByRole('menuitem', { name: /To Do/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('menuitem', { name: 'Dismiss' })).not.toBeInTheDocument();
  });

  it('shows the "Cannot open record" toast when the host openRecord rejects', async () => {
    const openRecord = jest.fn().mockRejectedValue(new Error('no access'));
    mount(serviceReturning(fixtureBriefing()), { openRecord });

    fireEvent.click(await screen.findByRole('link', { name: /Acme v\. Globex/ }));

    expect(await screen.findByText('Cannot open record')).toBeInTheDocument();
  });

  it('shows the existing error state, not an unhandled rejection, when the injected service rejects', async () => {
    const service: BriefingDataService = {
      renderBriefing: jest.fn().mockRejectedValue(new Error('widget transport down')),
    };

    mount(service, {});

    expect(await screen.findByText('Could not load daily briefing.')).toBeInTheDocument();
    expect(screen.getByText(/widget transport down/)).toBeInTheDocument();
  });

  it('shows the existing unavailable state when the injected service reports it', async () => {
    const service: BriefingDataService = {
      renderBriefing: jest.fn().mockResolvedValue({ status: 'unavailable', reason: 'AI service is busy.' }),
    };

    mount(service, {});

    expect(await screen.findByText('Daily briefing temporarily unavailable.')).toBeInTheDocument();
  });

  it('loads preferences from the injected service and offers the menu when it can save, never through Xrm', async () => {
    const service: BriefingDataService = {
      ...serviceReturning(fixtureBriefing()),
      preferences: {
        load: jest.fn().mockResolvedValue({ ...DEFAULT_DAILY_DIGEST_PREFERENCES, disabledChannels: ['documents'] }),
        save: jest.fn().mockResolvedValue(undefined),
      },
    };

    mount(service, {});

    // The loaded preference is applied: the Documents channel is disabled, so its row is not drawn.
    await screen.findByText('Acme v. Globex');
    await waitFor(() => expect(screen.queryByText('Master services agreement')).not.toBeInTheDocument());

    // The service can save, so the Preferences menu is offered.
    expect(screen.getByRole('button', { name: 'Preferences' })).toBeInTheDocument();
    expect(xrmAccess).not.toHaveBeenCalled();
    expect(authenticatedFetch).not.toHaveBeenCalled();
  });

  it('does not offer the Preferences menu to a host whose preferences service cannot save', async () => {
    const service: BriefingDataService = {
      ...serviceReturning(fixtureBriefing()),
      preferences: { load: jest.fn().mockResolvedValue(DEFAULT_DAILY_DIGEST_PREFERENCES) },
    };

    mount(service, {});

    await screen.findByText('Acme v. Globex');
    expect(screen.queryByRole('button', { name: 'Preferences' })).not.toBeInTheDocument();
  });

  it('wires navigation.browsePlaybooks to the header menu', async () => {
    const browsePlaybooks = jest.fn();
    mount(serviceReturning(fixtureBriefing()), { browsePlaybooks });

    await screen.findByText('Acme v. Globex');
    fireEvent.click(screen.getByRole('button', { name: 'More actions' }));
    fireEvent.click(await screen.findByRole('menuitem', { name: /Browse Playbooks/ }));

    expect(browsePlaybooks).toHaveBeenCalledTimes(1);
    // No Email Briefing: it needs the BFF send pipeline, which an injected host does not have.
    expect(screen.queryByRole('menuitem', { name: /Email/ })).not.toBeInTheDocument();
  });
});

describe('useBriefingPreferences - injected preferences service (task 010)', () => {
  it('saves the merged preferences through the service and records a failed save without throwing', async () => {
    const save = jest.fn().mockRejectedValueOnce(new Error('widget transport down'));
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    const { result } = renderHook(() =>
      useBriefingPreferences(null, '', { load: jest.fn().mockResolvedValue(DEFAULT_DAILY_DIGEST_PREFERENCES), save })
    );

    await act(async () => {
      await result.current.updatePreferences({ autoPopup: false });
    });

    expect(save).toHaveBeenCalledWith({ ...DEFAULT_DAILY_DIGEST_PREFERENCES, autoPopup: false });
    expect(result.current.error).toBe('widget transport down');
    errorSpy.mockRestore();
  });
});
