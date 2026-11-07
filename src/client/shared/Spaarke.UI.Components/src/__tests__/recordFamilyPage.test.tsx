/**
 * RecordFamily code page - wiring test (field-mapping-server-write-path-r1 task 075 fix round 2).
 *
 * Renders the page's view (`src/solutions/RecordFamily/src/FamilyPage.tsx`, the component `main.tsx` mounts) with a
 * bootstrap double and asserts the two things the page exists to do:
 *   1. the request is gated by `readFamilyParams` - a malformed launch shows the error bar and never fetches;
 *   2. the modal is rendered `embedded` (no Fluent dialog inside the platform dialog), and its Close / Escape reach
 *      the bootstrap's `closeDialog`.
 * Removing `embedded`, or bypassing `readFamilyParams`, fails these.
 *
 * Task 040 (ships with 075): the modal's actions open `RecordFamilyActionPanel` for exactly the ticked rows, against the
 * record the ribbon was used on, and nothing is written until the panel's confirm button. Dropping `onAction` (both
 * actions disabled again) or hosting the panel for another source fails the push tests.
 */

import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { webLightTheme } from '@fluentui/react-components';
import { FamilyPage } from '../../../../../solutions/RecordFamily/src/FamilyPage';
import type { IFamilyPageBootstrap } from '../../../../../solutions/RecordFamily/src/FamilyPage';
import { throwingAuthenticatedFetch } from './helpers/authenticatedFetchDouble';
import { PUSH_PATH } from '../components/RecordFamilyModal/recordFamilyPushService';

const PROJECT = 'bbbbbbbb-0000-0000-0000-000000000002';

const familyBody = {
  anchor: { id: PROJECT, table: 'sprk_project' },
  root: { id: PROJECT, table: 'sprk_project' },
  truncated: false,
  continuation: null,
  members: [
    {
      id: PROJECT,
      table: 'sprk_project',
      depth: 0,
      name: 'Diligence Project',
      recordNumber: null,
      recordType: null,
      status: 'Open',
      modifiedOn: null,
      kind: 'Filing',
      edgeKind: 'FilingCore',
      parentId: null,
    },
  ],
};

function makeBootstrap(
  params: Record<string, string | undefined>,
  over: Partial<IFamilyPageBootstrap> = {}
): IFamilyPageBootstrap & { fetchMock: jest.Mock } {
  const fetchMock = throwingAuthenticatedFetch(
    async () => ({ ok: true, status: 200, json: async () => familyBody }) as unknown as Response
  );
  return {
    theme: webLightTheme,
    params,
    isAuthReady: true,
    authenticatedFetch: fetchMock,
    closeDialog: jest.fn(),
    fetchMock,
    ...over,
  } as IFamilyPageBootstrap & { fetchMock: jest.Mock };
}

const EVENT = 'eeeeeeee-0000-0000-0000-00000000000a';

/** The anchor plus one child Event: the smallest family a person can tick a row in. */
const familyWithChild = {
  ...familyBody,
  members: [
    ...familyBody.members,
    {
      id: EVENT,
      table: 'sprk_event',
      depth: 1,
      name: 'Hearing A',
      recordNumber: null,
      recordType: null,
      status: 'Open',
      modifiedOn: null,
      kind: 'Filing',
      edgeKind: 'FilingCore',
      parentId: PROJECT,
    },
  ],
};

type PushBody = {
  sourceEntity: string;
  sourceRecordId: string;
  targetEntity: string;
  preview?: boolean;
  includeAssignedTo?: boolean;
  childIds?: string[];
};

/** A bootstrap whose server answers the family read and the push route (preview and apply), recording every push body. */
function makePushBootstrap(params: Record<string, string | undefined>) {
  const sent: PushBody[] = [];
  const ok = (body: unknown) => ({ ok: true, status: 200, json: async () => body }) as unknown as Response;
  const fetchMock = throwingAuthenticatedFetch(async (url, init) => {
    if (url === PUSH_PATH) {
      const body = JSON.parse(String(init?.body)) as PushBody;
      sent.push(body);
      return body.preview
        ? ok({
            success: true,
            preview: true,
            updatedCount: 0,
            failedCount: 0,
            skippedNoWriteCount: 0,
            truncated: false,
            skippedRuleCount: 0,
            changes: [
              {
                recordId: EVENT,
                name: 'Hearing A',
                column: 'sprk_clientreference',
                from: 'OLD',
                to: 'REF-1',
                flags: [],
              },
            ],
            errors: [],
          })
        : ok({ success: true, updatedCount: 1, failedCount: 0, errors: [] });
    }
    return ok(familyWithChild);
  });
  const b = {
    theme: webLightTheme,
    params,
    isAuthReady: true,
    authenticatedFetch: fetchMock,
    closeDialog: jest.fn(),
  } as IFamilyPageBootstrap;
  return { b, sent, fetchMock };
}

describe('RecordFamily page (FamilyPage): the push actions (task 040)', () => {
  const LAUNCH = { entityName: 'sprk_project', id: `{${PROJECT.toUpperCase()}}` };

  async function tickHearing() {
    await screen.findByText('Hearing A');
    fireEvent.click(screen.getByRole('checkbox', { name: 'Select Hearing A' }));
  }

  it('offers the actions on ticked rows, opens no panel and sends no push request before one is chosen', async () => {
    const { b, sent } = makePushBootstrap(LAUNCH);
    render(<FamilyPage b={b} />);
    await tickHearing();

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(sent).toHaveLength(0);
  });

  it('"Push field updates" previews the ticked rows against the launching record, and writes only on confirm', async () => {
    const { b, sent } = makePushBootstrap(LAUNCH);
    render(<FamilyPage b={b} />);
    await tickHearing();

    fireEvent.click(screen.getByRole('button', { name: 'Push field updates' }));

    const confirm = await screen.findByRole('button', { name: 'Update 1 record' });
    expect(sent).toHaveLength(1);
    expect(sent[0]).toMatchObject({
      sourceEntity: 'sprk_project',
      sourceRecordId: PROJECT,
      targetEntity: 'sprk_event',
      preview: true,
      includeAssignedTo: false,
      childIds: [EVENT],
    });

    fireEvent.click(confirm);

    expect(await screen.findByText('1 record was updated.')).toBeInTheDocument();
    const applies = sent.filter(x => !x.preview);
    expect(applies).toHaveLength(1);
    expect(applies[0]).toMatchObject({ sourceRecordId: PROJECT, targetEntity: 'sprk_event', childIds: [EVENT] });
    // The platform dialog stays open under the panel; the person closes it.
    expect(b.closeDialog).not.toHaveBeenCalled();
  });

  it('"Update Assigned To" opts in', async () => {
    const { b, sent } = makePushBootstrap(LAUNCH);
    render(<FamilyPage b={b} />);
    await tickHearing();

    fireEvent.click(screen.getByRole('button', { name: 'Update Assigned To' }));

    await screen.findByRole('button', { name: 'Update 1 record' });
    expect(sent[0]).toMatchObject({ preview: true, includeAssignedTo: true, childIds: [EVENT] });
  });

  it('cancelling the panel writes nothing and leaves the platform dialog open', async () => {
    const { b, sent } = makePushBootstrap(LAUNCH);
    render(<FamilyPage b={b} />);
    await tickHearing();
    fireEvent.click(screen.getByRole('button', { name: 'Push field updates' }));
    await screen.findByRole('button', { name: 'Update 1 record' });

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    await waitFor(() => expect(screen.queryByRole('button', { name: 'Update 1 record' })).not.toBeInTheDocument());
    expect(sent.filter(x => !x.preview)).toHaveLength(0);
    expect(b.closeDialog).not.toHaveBeenCalled();
    expect(screen.getByRole('region', { name: 'Family' })).toBeInTheDocument();
  });

  // The embedded modal's Escape handler sits on its region root, so Escape only reaches it while focus is inside the
  // page. The panel unmounts when it closes; focus must come back or Escape would hit <body> and never close the dialog.
  it('after Cancel, focus returns inside the page and Escape still closes the platform dialog', async () => {
    const { b } = makePushBootstrap(LAUNCH);
    render(<FamilyPage b={b} />);
    await tickHearing();
    const action = screen.getByRole('button', { name: 'Push field updates' });
    action.focus();
    fireEvent.click(action);
    await screen.findByRole('button', { name: 'Update 1 record' });

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Update 1 record' })).not.toBeInTheDocument());

    const region = screen.getByRole('region', { name: 'Family' });
    await waitFor(() => expect(region.contains(document.activeElement)).toBe(true));
    expect(document.activeElement).not.toBe(document.body);
    fireEvent.keyDown(document.activeElement as Element, { key: 'Escape', bubbles: true });
    expect(b.closeDialog).toHaveBeenCalledTimes(1);
  });

  it('when nothing had focus as the panel opened, focus falls back to the Family region root', async () => {
    const { b } = makePushBootstrap(LAUNCH);
    render(<FamilyPage b={b} />);
    await tickHearing();
    (document.activeElement as HTMLElement | null)?.blur();
    fireEvent.click(screen.getByRole('button', { name: 'Push field updates' }));
    await screen.findByRole('button', { name: 'Update 1 record' });

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('region', { name: 'Family' })));
  });

  it('after the apply finishes and the panel is closed, focus returns inside the page and Escape closes the platform dialog', async () => {
    const { b } = makePushBootstrap(LAUNCH);
    render(<FamilyPage b={b} />);
    await tickHearing();
    const action = screen.getByRole('button', { name: 'Push field updates' });
    action.focus();
    fireEvent.click(action);
    fireEvent.click(await screen.findByRole('button', { name: 'Update 1 record' }));
    await screen.findByText('1 record was updated.');

    // The panel's own Close button (the dialog is the last 'Close' in the DOM; the page also has one).
    const closes = screen.getAllByRole('button', { name: 'Close' });
    fireEvent.click(closes[closes.length - 1]);
    await waitFor(() => expect(screen.queryByText('1 record was updated.')).not.toBeInTheDocument());

    const region = screen.getByRole('region', { name: 'Family' });
    await waitFor(() => expect(region.contains(document.activeElement)).toBe(true));
    expect(document.activeElement).not.toBe(document.body);
    fireEvent.keyDown(document.activeElement as Element, { key: 'Escape', bubbles: true });
    expect(b.closeDialog).toHaveBeenCalledTimes(1);
  });
});

describe('RecordFamily page (FamilyPage)', () => {
  it('renders the Family embedded: a named region, no dialog, and Close / Escape close the platform dialog', async () => {
    const b = makeBootstrap({ entityName: 'sprk_project', id: `{${PROJECT.toUpperCase()}}` });
    render(<FamilyPage b={b} />);

    await screen.findByText('Diligence Project');
    // embedded: no Fluent Dialog inside the platform dialog (ADR-050), a landmark instead.
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Family' })).toBeInTheDocument();
    expect(b.fetchMock).toHaveBeenCalledTimes(1);
    expect(String(b.fetchMock.mock.calls[0][0])).toContain(PROJECT);

    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    expect(b.closeDialog).toHaveBeenCalledTimes(1);
    fireEvent.keyDown(screen.getByRole('region', { name: 'Family' }), { key: 'Escape' });
    expect(b.closeDialog).toHaveBeenCalledTimes(2);
  });

  it.each([
    ['a missing id', { entityName: 'sprk_project' }],
    ['a missing table', { id: PROJECT }],
    ['a table name that is not a logical name', { entityName: 'sprk_project/../x', id: PROJECT }],
    ['an id that is not a GUID', { entityName: 'sprk_project', id: 'not-a-guid' }],
    ['no params at all', {}],
  ])('shows the error bar and never fetches for %s', (_label, params) => {
    const b = makeBootstrap(params);
    render(<FamilyPage b={b} />);

    expect(screen.getByText('The family could not be opened. Close this window and try again.')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Family' })).not.toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(b.fetchMock).not.toHaveBeenCalled();
  });

  it('waits for auth before anything renders or fetches', () => {
    const b = makeBootstrap({ entityName: 'sprk_project', id: PROJECT }, { isAuthReady: false });
    render(<FamilyPage b={b} />);

    expect(screen.getByText('Initializing...')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Family' })).not.toBeInTheDocument();
    expect(b.fetchMock).not.toHaveBeenCalled();
  });
});
