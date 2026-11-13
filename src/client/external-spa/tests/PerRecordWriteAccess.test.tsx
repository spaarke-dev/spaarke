/**
 * unified-access-control-r2 task 036 fix round 1 (F4, open question g): a caller whose write access is decided per record
 * (`/me` `writeAccessPerRecord`, a licensed systemuser under the FR-20 flag) has a `/me` level that says only what they
 * may SEE — mostly ViewOnly. Their create/upload controls follow the record's own list `canCreate` (the BFF asks
 * Dataverse for the record in hand), never the level. Every other caller keeps the level rule, and since R4 D-9 (#2716)
 * the list's `canCreate` must agree with it (a list that sends none follows the level).
 */
import * as React from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AccessLevel } from '../src/types';

const { bffApiCall } = vi.hoisted(() => ({ bffApiCall: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall, bffApiBlob: vi.fn() };
});

import { EventsCalendar } from '../src/components/EventsCalendar';
import { DocumentLibrary } from '../src/components/DocumentLibrary';

const PROJECT = 'p-036';

/** Every list answers empty, with `canCreate` when given (absent = the BFF did not say). */
function answerLists(canCreate?: boolean): void {
  bffApiCall.mockImplementation(async () => ({
    value: [],
    ...(canCreate === undefined ? {} : { canCreate }),
  }));
}

function inFluent(node: React.ReactElement): ReturnType<typeof render> {
  return render(<FluentProvider theme={webLightTheme}>{node}</FluentProvider>);
}

const DOC = { sprk_documentid: 'd-036', sprk_name: 'Brief.pdf', createdon: '2026-10-01T00:00:00Z' };

async function settled(): Promise<void> {
  await waitFor(() => expect(bffApiCall).toHaveBeenCalled());
  await waitFor(() => expect(screen.queryByRole('progressbar')).toBeNull());
}

beforeEach(() => {
  bffApiCall.mockReset();
});

describe('per-record write access: EventsCalendar "Create Event"', () => {
  it('follows the project event list canCreate, not the level', async () => {
    answerLists(true);
    inFluent(<EventsCalendar projectId={PROJECT} accessLevel={AccessLevel.ViewOnly} writeAccessPerRecord />);

    expect(await screen.findByRole('button', { name: /Create Event/ })).toBeTruthy();
  });

  it('canCreate=false hides it for a Collaborate-level caller', async () => {
    answerLists(false);
    inFluent(<EventsCalendar projectId={PROJECT} accessLevel={AccessLevel.Collaborate} writeAccessPerRecord />);
    await settled();

    expect(screen.queryByRole('button', { name: /Create Event/ })).toBeNull();
  });

  // R4 D-9 (#2716): was "every other caller keeps the level rule" over canCreate=false.
  it('every other caller is NOT offered Create Event when the list says canCreate=false, even at Collaborate (D-9)', async () => {
    answerLists(false);
    inFluent(<EventsCalendar projectId={PROJECT} accessLevel={AccessLevel.Collaborate} />);
    await settled();

    expect(screen.queryByRole('button', { name: /Create Event/ })).toBeNull();
  });

  it('every other caller follows the level where the list sent no canCreate (the kept exception)', async () => {
    answerLists(undefined);
    inFluent(<EventsCalendar projectId={PROJECT} accessLevel={AccessLevel.Collaborate} />);

    expect(await screen.findByRole('button', { name: /Create Event/ })).toBeTruthy();
  });

  it('a missing canCreate is "cannot" (fail closed)', async () => {
    answerLists(undefined);
    inFluent(<EventsCalendar projectId={PROJECT} accessLevel={AccessLevel.FullAccess} writeAccessPerRecord />);
    await settled();

    expect(screen.queryByRole('button', { name: /Create Event/ })).toBeNull();
  });
});

describe('per-record write access: a project DocumentLibrary "Upload Document"', () => {
  it('follows the document list canCreate, not the level', async () => {
    answerLists(true);
    inFluent(<DocumentLibrary projectId={PROJECT} accessLevel={AccessLevel.ViewOnly} writeAccessPerRecord />);

    expect(await screen.findByRole('button', { name: /Upload Document/ })).toBeTruthy();
  });

  it('canCreate=false hides it for a Collaborate-level caller', async () => {
    answerLists(false);
    inFluent(<DocumentLibrary projectId={PROJECT} accessLevel={AccessLevel.Collaborate} writeAccessPerRecord />);
    await settled();

    expect(screen.queryByRole('button', { name: /Upload Document/ })).toBeNull();
  });

  // R4 D-9 (#2716): was "every other caller keeps the level rule for a project (the list canCreate is not consulted)".
  it('every other caller is NOT offered Upload on a project when the list says canCreate=false, even at Collaborate (D-9)', async () => {
    answerLists(false);
    inFluent(<DocumentLibrary projectId={PROJECT} accessLevel={AccessLevel.Collaborate} />);
    await settled();

    expect(screen.queryByRole('button', { name: /Upload Document/ })).toBeNull();
  });

  it('every other caller follows the level on a project where the list sent no canCreate (the kept exception)', async () => {
    answerLists(undefined);
    inFluent(<DocumentLibrary projectId={PROJECT} accessLevel={AccessLevel.Collaborate} />);

    expect(await screen.findByRole('button', { name: /Upload Document/ })).toBeTruthy();
  });

  it('a missing canCreate is "cannot" (fail closed)', async () => {
    answerLists(undefined);
    inFluent(<DocumentLibrary projectId={PROJECT} accessLevel={AccessLevel.FullAccess} writeAccessPerRecord />);
    await settled();

    expect(screen.queryByRole('button', { name: /Upload Document/ })).toBeNull();
  });

  it('a /me answer that arrives after the list does not leave Upload hidden (fix round 2, F1)', async () => {
    // The page renders before /me returns (writeAccessPerRecord false), then again with it true. Every list read is held
    // and answered only after the re-render, the first one LAST: the order that left a stale interpretation behind.
    const pending: Array<(value: unknown) => void> = [];
    bffApiCall.mockImplementation(() => new Promise(resolve => pending.push(resolve)));
    const view = inFluent(<DocumentLibrary projectId={PROJECT} accessLevel={AccessLevel.ViewOnly} />);
    await waitFor(() => expect(pending.length).toBeGreaterThan(0));

    view.rerender(
      <FluentProvider theme={webLightTheme}>
        <DocumentLibrary projectId={PROJECT} accessLevel={AccessLevel.ViewOnly} writeAccessPerRecord />
      </FluentProvider>
    );
    await act(async () => {
      for (const resolve of [...pending].reverse()) resolve({ value: [], canCreate: true });
    });

    expect(await screen.findByRole('button', { name: /Upload Document/ })).toBeTruthy();
  });
});

describe('per-record write access: DocumentLibrary download and version history (fix round 2, F1)', () => {
  function answerDocuments(canCreate?: boolean): void {
    bffApiCall.mockImplementation(async () => ({
      value: [DOC],
      ...(canCreate === undefined ? {} : { canCreate }),
    }));
  }

  it('a ViewOnly-level per-record caller may download from a project list that rendered, even without canCreate', async () => {
    answerDocuments(false);
    inFluent(<DocumentLibrary projectId={PROJECT} accessLevel={AccessLevel.ViewOnly} writeAccessPerRecord />);

    expect(await screen.findByRole('button', { name: 'Download Brief.pdf' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'View version history for Brief.pdf' })).toBeTruthy();
  });

  it('the same holds on a matter list', async () => {
    answerDocuments(false);
    inFluent(
      <DocumentLibrary root="matters" recordId="m-036" accessLevel={AccessLevel.ViewOnly} writeAccessPerRecord />
    );

    expect(await screen.findByRole('button', { name: 'Download Brief.pdf' })).toBeTruthy();
  });

  it('every other caller keeps the level rule: a ViewOnly contact is not offered download (unchanged)', async () => {
    answerDocuments(true);
    inFluent(<DocumentLibrary projectId={PROJECT} accessLevel={AccessLevel.ViewOnly} />);
    await screen.findByText('Brief.pdf');

    expect(screen.queryByRole('button', { name: 'Download Brief.pdf' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'View version history for Brief.pdf' })).toBeNull();
  });

  it('every other caller keeps the level rule: a Collaborate contact is offered download (unchanged)', async () => {
    answerDocuments(false);
    inFluent(<DocumentLibrary projectId={PROJECT} accessLevel={AccessLevel.Collaborate} />);

    expect(await screen.findByRole('button', { name: 'Download Brief.pdf' })).toBeTruthy();
  });

  it('a host that says canDownload=false still wins', async () => {
    answerDocuments(true);
    inFluent(
      <DocumentLibrary
        projectId={PROJECT}
        accessLevel={AccessLevel.ViewOnly}
        canDownload={false}
        writeAccessPerRecord
      />
    );
    await screen.findByText('Brief.pdf');

    expect(screen.queryByRole('button', { name: 'Download Brief.pdf' })).toBeNull();
  });
});
