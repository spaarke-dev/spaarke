/**
 * unified-access-control-r2 task 036 fix round 1 (F4, open question g): a caller whose write access is decided per record
 * (`/me` `writeAccessPerRecord`, a licensed systemuser under the FR-20 flag) has a `/me` level that says only what they
 * may SEE — mostly ViewOnly. Their create/upload controls follow the record's own list `canCreate` (the BFF asks
 * Dataverse for the record in hand), never the level. Every other caller keeps the level rule, unchanged.
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

function inFluent(node: React.ReactElement): void {
  render(<FluentProvider theme={webLightTheme}>{node}</FluentProvider>);
}

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
});
