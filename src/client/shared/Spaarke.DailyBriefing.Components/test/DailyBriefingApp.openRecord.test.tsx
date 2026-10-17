/**
 * App-level test: FR-19 AC-19b — when `Xrm.Navigation.navigateTo` rejects (or is unavailable) while opening a
 * record from the Daily Briefing, the user gets a non-blocking "Cannot open record" toast instead of a silent
 * failure. Mounts the real `DailyBriefingApp` with a mocked `/render` response (via the `@spaarke/auth`
 * `authenticatedFetch` stub) and a fake `window.Xrm`.
 *
 * Classification (ADR-038 §7): MAINTAIN — a user-visible failure path of the app's record-open handler.
 */
import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { DailyBriefingApp } from '../src/components/DailyBriefingApp';
import { authenticatedFetch } from '@spaarke/auth';

const RECORD_ID = '11111111-1111-1111-1111-111111111111';
const USER_ID = '00000000-0000-0000-0000-000000000001';

// Fluent's Toast / MessageBar observe element size; jsdom has no ResizeObserver.
class ResizeObserverStub {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}
(window as unknown as { ResizeObserver: unknown }).ResizeObserver = ResizeObserverStub;

/** Minimal OkResponse stand-in (jsdom exposes no global Response); briefingService only calls .json(). */
function renderResponse(): { json: () => Promise<unknown> } {
  return {
    json: async () => ({
      tldr: { summary: '', keyTakeaways: [], categoryCount: 1, priorityItemCount: 0, itemRefs: [] },
      // The record is opened from a Know channel bullet (the retired Critical Today list used to offer this link).
      channelNarratives: [
        {
          category: 'matters',
          bullets: [
            {
              narrative: 'Acme v. Globex',
              itemIds: ['item-1'],
              primaryEntityType: 'sprk_matter',
              primaryEntityId: RECORD_ID,
              primaryEntityName: 'Acme v. Globex',
            },
          ],
        },
      ],
      generatedAtUtc: '2026-10-12T08:00:00Z',
    }),
  };
}

function installXrm(navigateTo: jest.Mock | undefined): void {
  (window as unknown as { Xrm?: unknown }).Xrm = {
    WebApi: {
      retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
      retrieveRecord: jest.fn(),
      createRecord: jest.fn(),
      updateRecord: jest.fn(),
      deleteRecord: jest.fn(),
    },
    Utility: {
      getGlobalContext: () => ({
        userSettings: { userId: `{${USER_ID}}` },
        getClientUrl: () => 'https://org.crm.dynamics.com',
      }),
    },
    Navigation: navigateTo ? { navigateTo } : {},
  };
}

async function openFirstRecord(): Promise<void> {
  render(
    <FluentProvider theme={webLightTheme}>
      <DailyBriefingApp params={{}} />
    </FluentProvider>
  );
  fireEvent.click(await screen.findByRole('link', { name: /Acme v\. Globex/ }));
}

beforeEach(() => {
  (authenticatedFetch as jest.Mock).mockReset();
  (authenticatedFetch as jest.Mock).mockImplementation(() => Promise.resolve(renderResponse()));
});

afterEach(() => {
  delete (window as unknown as { Xrm?: unknown }).Xrm;
});

describe('DailyBriefingApp — open record failure (FR-19 AC-19b)', () => {
  it('shows the "Cannot open record" toast when navigateTo rejects', async () => {
    const navigateTo = jest.fn().mockRejectedValue(new Error('no access'));
    installXrm(navigateTo);

    await openFirstRecord();

    await waitFor(() => expect(navigateTo).toHaveBeenCalledTimes(1));
    expect(navigateTo.mock.calls[0][0]).toEqual({
      pageType: 'entityrecord',
      entityName: 'sprk_matter',
      entityId: RECORD_ID,
    });
    expect(await screen.findByText('Cannot open record')).toBeInTheDocument();
    expect(screen.getByText('You may not have access.')).toBeInTheDocument();
  });

  it('does not show the toast when navigateTo resolves', async () => {
    const navigateTo = jest.fn().mockResolvedValue(undefined);
    installXrm(navigateTo);

    await openFirstRecord();

    await waitFor(() => expect(navigateTo).toHaveBeenCalledTimes(1));
    // Let the (absent) catch handler run before asserting the negative.
    await Promise.resolve();
    expect(screen.queryByText('Cannot open record')).not.toBeInTheDocument();
  });
});
