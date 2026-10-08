/**
 * unified-access-control-r2 task 105 (ISS-002 / #963) — the external app's lists show every row or say plainly that
 * the list was cut short. The BFF adds `truncated: true` to a list it stopped at its row cap (or lost a later page of);
 * each list view renders it as a notice and never as a complete list.
 *
 * The views and the real web-api-client run; only the network seam (bff-client) is substituted, answering each list
 * route with the BFF's own envelope.
 */
import * as React from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AccessLevel } from '../src/types';

const { bffApiCall } = vi.hoisted(() => ({ bffApiCall: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall, bffApiBlob: vi.fn() };
});

import { getDocuments } from '../src/api/web-api-client';
import { DocumentLibrary } from '../src/components/DocumentLibrary';
import { SmartTodo } from '../src/components/SmartTodo';
import { EventsCalendar } from '../src/components/EventsCalendar';
import { ContactsOrganizations } from '../src/components/ContactsOrganizations';

const PROJECT = 'p-105';

/** The BFF's list envelope for every list route: one row each, `truncated` only when cut short. */
function answerEveryList(truncated: boolean): void {
  bffApiCall.mockImplementation(async (path: string) => {
    const envelope = <T,>(row: T) => (truncated ? { value: [row], truncated: true } : { value: [row] });
    if (path.endsWith('/documents')) return envelope({ sprk_documentid: 'd-1', sprk_name: 'Engagement letter' });
    if (path.endsWith('/todos')) return envelope({ sprk_todoid: 't-1', sprk_name: 'Review draft', statuscode: 1 });
    if (path.endsWith('/events')) return envelope({ sprk_eventid: 'e-1', sprk_name: 'Signing' });
    if (path.endsWith('/contacts')) return envelope({ contactid: 'c-1', fullname: 'Ada Lovelace' });
    if (path.endsWith('/organizations')) return envelope({ accountid: 'a-1', name: 'Acme' });
    throw new Error(`unexpected path ${path}`);
  });
}

const views: Array<[string, () => React.ReactElement, number]> = [
  ['documents', () => <DocumentLibrary projectId={PROJECT} accessLevel={AccessLevel.ViewOnly} />, 1],
  ['to-dos', () => <SmartTodo projectId={PROJECT} accessLevel={AccessLevel.ViewOnly} />, 1],
  ['events', () => <EventsCalendar projectId={PROJECT} accessLevel={AccessLevel.ViewOnly} />, 1],
  // Contacts and organisations: one notice per list.
  ['contacts and organisations', () => <ContactsOrganizations projectId={PROJECT} />, 2],
];

function renderView(view: () => React.ReactElement): void {
  render(<FluentProvider theme={webLightTheme}>{view()}</FluentProvider>);
}

beforeEach(() => {
  bffApiCall.mockReset();
});

describe('web-api-client — the list envelope', () => {
  it('reads `truncated: true` as an incomplete list', async () => {
    bffApiCall.mockResolvedValue({ value: [{ sprk_documentid: 'd-1' }], truncated: true });

    await expect(getDocuments(PROJECT)).resolves.toEqual({ items: [{ sprk_documentid: 'd-1' }], truncated: true });
  });

  it('reads a list without the field as complete', async () => {
    bffApiCall.mockResolvedValue({ value: [{ sprk_documentid: 'd-1' }] });

    await expect(getDocuments(PROJECT)).resolves.toEqual({ items: [{ sprk_documentid: 'd-1' }], truncated: false });
  });
});

describe('each list view says when its list was cut short', () => {
  it.each(views)('%s: a truncated list shows the notice', async (_name, view, notices) => {
    answerEveryList(true);
    renderView(view);

    await waitFor(() => expect(screen.getAllByTestId('truncated-list-notice')).toHaveLength(notices));
    expect(screen.getAllByText(/This list is incomplete/).length).toBe(notices);
  });

  it.each(views)('%s: a complete list shows no notice', async (_name, view) => {
    answerEveryList(false);
    renderView(view);

    // Wait until the row is on screen, so the absence below is not just "still loading".
    await screen.findByText(/Engagement letter|Review draft|Signing|Ada Lovelace/);
    await waitFor(() => expect(bffApiCall).toHaveBeenCalled());
    expect(screen.queryByTestId('truncated-list-notice')).toBeNull();
  });
});
