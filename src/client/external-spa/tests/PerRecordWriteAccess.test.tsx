/**
 * unified-access-control-r2 task 036 fix round 1 (F4, open question g): a caller whose write access is decided per record
 * (`/me` `writeAccessPerRecord`, a licensed systemuser under the FR-20 flag) has a `/me` level that says only what they
 * may SEE — mostly ViewOnly. Their create/upload controls follow the record's own list `canCreate` (the BFF asks
 * Dataverse for the record in hand), never the level. Every other caller keeps the level rule, unchanged.
 */
import * as React from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AccessLevel, ApiError } from '../src/types';

const { bffApiCall } = vi.hoisted(() => ({ bffApiCall: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall, bffApiBlob: vi.fn() };
});

import { SmartTodo } from '../src/components/SmartTodo';
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
const OPEN_TODO = { sprk_todoid: 't-036', sprk_name: 'Review brief', statuscode: 1 };

async function settled(): Promise<void> {
  await waitFor(() => expect(bffApiCall).toHaveBeenCalled());
  await waitFor(() => expect(screen.queryByRole('progressbar')).toBeNull());
}

beforeEach(() => {
  bffApiCall.mockReset();
});

describe('per-record write access: SmartTodo "Add Task"', () => {
  it('a ViewOnly-level caller is offered Add Task when the list says canCreate', async () => {
    answerLists(true);
    inFluent(<SmartTodo projectId={PROJECT} accessLevel={AccessLevel.ViewOnly} writeAccessPerRecord />);

    expect(await screen.findByRole('button', { name: 'Create new task' })).toBeTruthy();
  });

  it('a Collaborate-level caller is NOT offered Add Task when the list says canCreate=false', async () => {
    answerLists(false);
    inFluent(<SmartTodo projectId={PROJECT} accessLevel={AccessLevel.Collaborate} writeAccessPerRecord />);
    await settled();

    expect(screen.queryByRole('button', { name: 'Create new task' })).toBeNull();
  });

  it('a missing canCreate is "cannot" (fail closed)', async () => {
    answerLists(undefined);
    inFluent(<SmartTodo projectId={PROJECT} accessLevel={AccessLevel.FullAccess} writeAccessPerRecord />);
    await settled();

    expect(screen.queryByRole('button', { name: 'Create new task' })).toBeNull();
  });

  it('every other caller keeps the level rule, whatever a list says', async () => {
    answerLists(false);
    inFluent(<SmartTodo projectId={PROJECT} accessLevel={AccessLevel.Collaborate} />);

    expect(await screen.findByRole('button', { name: 'Create new task' })).toBeTruthy();
  });
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

  it('every other caller keeps the level rule', async () => {
    answerLists(false);
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

  it('every other caller keeps the level rule for a project (the list canCreate is not consulted)', async () => {
    answerLists(false);
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

describe('per-record write access: SmartTodo status toggle (fix round 2, F1)', () => {
  /** The project to-do list answers one open to-do; a PATCH answers `patch` (a thrown error refuses it). */
  function answerTodos(patch: () => unknown): void {
    bffApiCall.mockImplementation(async (_path: string, options?: RequestInit) => {
      if (options?.method === 'PATCH') return patch();
      return { value: [OPEN_TODO], canCreate: false };
    });
  }

  it('a ViewOnly-level per-record caller is offered the toggle on a project to-do, and PATCH decides', async () => {
    answerTodos(() => undefined);
    inFluent(<SmartTodo projectId={PROJECT} accessLevel={AccessLevel.ViewOnly} writeAccessPerRecord />);

    fireEvent.click(await screen.findByRole('button', { name: 'Mark task complete' }));

    await waitFor(() =>
      expect(bffApiCall).toHaveBeenCalledWith(
        '/api/v1/external/todos/t-036',
        expect.objectContaining({ method: 'PATCH', body: JSON.stringify({ statuscode: 2 }) })
      )
    );
    expect(await screen.findByRole('button', { name: 'Mark task incomplete' })).toBeTruthy();
  });

  it('a 403 reverts the toggle and says so plainly, never the server body', async () => {
    answerTodos(() => {
      throw new ApiError(403, JSON.stringify({ status: 403, detail: 'internal: WriteAccess missing on sprk_todo' }));
    });
    inFluent(<SmartTodo projectId={PROJECT} accessLevel={AccessLevel.ViewOnly} writeAccessPerRecord />);

    fireEvent.click(await screen.findByRole('button', { name: 'Mark task complete' }));

    expect(await screen.findByText('You do not have permission to change this task.')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Mark task complete' })).toBeTruthy();
    expect(screen.queryByText(/WriteAccess missing/)).toBeNull();
  });

  it('every other caller keeps the level rule: a ViewOnly contact is not offered the toggle (unchanged)', async () => {
    answerTodos(() => undefined);
    inFluent(<SmartTodo projectId={PROJECT} accessLevel={AccessLevel.ViewOnly} />);
    await screen.findByText('Review brief');

    expect(screen.queryByRole('button', { name: 'Mark task complete' })).toBeNull();
  });

  // The failure text changed for EVERY caller in fix round 2 (re-check F4): a contact's refusal used to show the raw body.
  it('a Collaborate contact refused by PATCH sees the same plain message, never the server body', async () => {
    answerTodos(() => {
      throw new ApiError(403, JSON.stringify({ status: 403, detail: 'internal: grant does not cover sprk_todo' }));
    });
    inFluent(<SmartTodo projectId={PROJECT} accessLevel={AccessLevel.Collaborate} />);

    fireEvent.click(await screen.findByRole('button', { name: 'Mark task complete' }));

    expect(await screen.findByText('You do not have permission to change this task.')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Mark task complete' })).toBeTruthy();
    expect(screen.queryByText(/grant does not cover/)).toBeNull();
  });

  it('any other failure shows the ProblemDetails detail, else a fixed fallback, never the raw body', async () => {
    answerTodos(() => {
      throw new ApiError(503, JSON.stringify({ status: 503, detail: 'Dataverse is temporarily unavailable.' }));
    });
    const first = inFluent(<SmartTodo projectId={PROJECT} accessLevel={AccessLevel.Collaborate} />);
    fireEvent.click(await screen.findByRole('button', { name: 'Mark task complete' }));
    expect(await screen.findByText('Dataverse is temporarily unavailable.')).toBeTruthy();
    first.unmount();

    answerTodos(() => {
      throw new TypeError('Failed to fetch');
    });
    inFluent(<SmartTodo projectId={PROJECT} accessLevel={AccessLevel.Collaborate} />);
    fireEvent.click(await screen.findByRole('button', { name: 'Mark task complete' }));
    expect(await screen.findByText('The task status could not be updated just now.')).toBeTruthy();
    expect(screen.queryByText(/Failed to fetch/)).toBeNull();
  });

  it('a matter to-do list stays read-only for a per-record caller (task 057)', async () => {
    answerTodos(() => undefined);
    inFluent(<SmartTodo projectId="m-036" root="matters" accessLevel={AccessLevel.ViewOnly} writeAccessPerRecord />);
    await screen.findByText('Review brief');

    expect(screen.queryByRole('button', { name: 'Mark task complete' })).toBeNull();
  });
});
