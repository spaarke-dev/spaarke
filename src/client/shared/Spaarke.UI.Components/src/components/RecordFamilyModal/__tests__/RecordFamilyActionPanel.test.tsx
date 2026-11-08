/**
 * RecordFamilyActionPanel — component tests (field-mapping-server-write-path-r1 task 040, spec FR-35).
 *
 * Every test drives the panel through a `@spaarke/auth`-shaped fetch double (`throwingAuthenticatedFetch`: a 2xx is
 * returned, any other status is THROWN as the real `ApiError`) that answers the push route the way the BFF does.
 *
 * Behaviours covered, and only these: the preview runs on open and WRITES NOTHING; the list shows from -> new and the
 * Assigned-To flags; nothing is applied until the confirm button; the apply sends exactly the previewed ids; the
 * Assigned-To action opts in; no-Write and truncation are said; an empty preview has no confirm; every failure is one fixed
 * text; closing writes nothing. Fix round 1: ticked records the server refuses are said, not dropped; a table with no Filing
 * edge (409) is reported for that table only; a multi-table apply reports what each table did; the preview follows the
 * source record, not the object identity of the prop.
 */

import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { RecordFamilyActionPanel } from '../RecordFamilyActionPanel';
import type { RecordFamilyActionPanelProps } from '../RecordFamilyActionPanel';
import { buildPushRequest, groupRowsByTable, PUSH_FAILED_TEXT, PUSH_PATH } from '../recordFamilyPushService';
import { throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';

const MATTER = 'aaaaaaaa-0000-0000-0000-000000000001';
const EVENT_A = 'eeeeeeee-0000-0000-0000-00000000000a';
const EVENT_B = 'eeeeeeee-0000-0000-0000-00000000000b';
const INVOICE_A = 'ffffffff-0000-0000-0000-00000000000c';

const SOURCE = { table: 'sprk_matter', id: MATTER };
const ROWS = [
  { table: 'sprk_event', id: EVENT_A },
  { table: 'sprk_event', id: EVENT_B },
];

type Body = {
  sourceEntity: string;
  sourceRecordId: string;
  targetEntity: string;
  preview?: boolean;
  includeAssignedTo?: boolean;
  childIds?: string[];
  kind?: string;
};

/** A plain fetch double that RETURNS failures (the external SPA's shape), so a 409 reaches the panel as a returned status. */
function returningFetch(answer: (body: Body) => Response) {
  const sent: Body[] = [];
  const fetchFn = jest.fn(async (_url: string, init?: RequestInit) => {
    const body = JSON.parse(String(init?.body)) as Body;
    sent.push(body);
    return answer(body);
  });
  return { fetchFn, sent };
}

const json = (status: number, body: unknown): Response =>
  ({ ok: status >= 200 && status < 300, status, json: async () => body }) as unknown as Response;

const previewBody = (changes: unknown[], extra: Record<string, unknown> = {}) => ({
  success: true,
  preview: true,
  updatedCount: 0,
  failedCount: 0,
  skippedNoWriteCount: 0,
  truncated: false,
  skippedRuleCount: 0,
  changes,
  errors: [],
  ...extra,
});

const CHANGE_A = {
  recordId: EVENT_A,
  name: 'Hearing A',
  column: 'sprk_clientreference',
  from: 'OLD',
  to: 'REF-1',
  flags: [],
};
const CHANGE_B = {
  recordId: EVENT_B,
  name: 'Hearing B',
  column: 'sprk_assignedattorney1',
  from: 'Ann',
  to: 'Bob',
  flags: ['secure', 'limited'],
};

/** The server double: records every body it was sent, answers a preview or an apply with the given bodies. */
function pushServer(opts: {
  preview?: (b: Body) => unknown;
  apply?: (b: Body) => unknown;
  previewStatus?: number | ((b: Body) => number);
  applyStatus?: number | ((b: Body) => number);
}) {
  const status = (s: number | ((b: Body) => number) | undefined, b: Body) =>
    typeof s === 'function' ? s(b) : (s ?? 200);
  const sent: Body[] = [];
  const fetchFn = throwingAuthenticatedFetch((url, init) => {
    expect(url).toBe(PUSH_PATH);
    expect(init?.method).toBe('POST');
    const body = JSON.parse(String(init?.body)) as Body;
    sent.push(body);
    return body.preview
      ? json(status(opts.previewStatus, body), opts.preview?.(body) ?? previewBody([]))
      : json(
          status(opts.applyStatus, body),
          opts.apply?.(body) ?? { success: true, updatedCount: 0, failedCount: 0, errors: [] }
        );
  });
  return { fetchFn, sent };
}

function renderPanel(
  over: Partial<RecordFamilyActionPanelProps> & Pick<RecordFamilyActionPanelProps, 'authenticatedFetch'>
) {
  const props: RecordFamilyActionPanelProps = {
    open: true,
    onClose: jest.fn(),
    action: 'push',
    source: SOURCE,
    rows: ROWS,
    ...over,
  };
  return {
    props,
    ...render(
      <FluentProvider theme={webLightTheme}>
        <RecordFamilyActionPanel {...props} />
      </FluentProvider>
    ),
  };
}

describe('RecordFamilyActionPanel', () => {
  let warn: jest.SpyInstance;
  beforeEach(() => {
    warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
  });
  afterEach(() => warn.mockRestore());

  it('previews on open, writes nothing, and lists current -> new with the Assigned-To flags', async () => {
    const { fetchFn, sent } = pushServer({ preview: () => previewBody([CHANGE_A, CHANGE_B]) });
    renderPanel({ authenticatedFetch: fetchFn });

    expect(await screen.findByText('Hearing A')).toBeTruthy();
    expect(screen.getByText('OLD')).toBeTruthy();
    expect(screen.getByText('REF-1')).toBeTruthy();
    expect(screen.getByText('Secure')).toBeTruthy();
    expect(screen.getByText('Limited')).toBeTruthy();
    expect(screen.getByText(/2 values on 2 records will change/)).toBeTruthy();

    expect(sent).toHaveLength(1);
    expect(sent[0]).toMatchObject({
      sourceEntity: 'sprk_matter',
      sourceRecordId: MATTER,
      targetEntity: 'sprk_event',
      preview: true,
      includeAssignedTo: false,
      childIds: [EVENT_A, EVENT_B],
    });
    expect(sent[0]).not.toHaveProperty('kind');
    expect(sent.every(b => b.preview === true)).toBe(true);
  });

  it('applies nothing until the confirm button, then sends exactly the previewed ids without preview', async () => {
    const { fetchFn, sent } = pushServer({
      preview: () => previewBody([CHANGE_A]), // EVENT_B has nothing to change, so it is not confirmed
      apply: () => ({ success: true, updatedCount: 1, failedCount: 0, errors: [] }),
    });
    const onApplied = jest.fn();
    renderPanel({ authenticatedFetch: fetchFn, onApplied });

    const confirm = await screen.findByRole('button', { name: 'Update 1 record' });
    expect(sent.filter(b => !b.preview)).toHaveLength(0);

    fireEvent.click(confirm);

    expect(await screen.findByText('1 record was updated.')).toBeTruthy();
    const applies = sent.filter(b => !b.preview);
    expect(applies).toHaveLength(1);
    expect(applies[0].childIds).toEqual([EVENT_A]);
    expect(applies[0].includeAssignedTo).toBe(false);
    expect(onApplied).toHaveBeenCalledWith({ updatedCount: 1, failedCount: 0, failedTables: [] });
  });

  it('"Update Assigned To" opts in on the preview and on the apply', async () => {
    const { fetchFn, sent } = pushServer({
      preview: () => previewBody([CHANGE_B]),
      apply: () => ({ success: true, updatedCount: 1, failedCount: 0, errors: [] }),
    });
    renderPanel({ authenticatedFetch: fetchFn, action: 'assignedTo' });

    expect(await screen.findByText(/includes the Assigned To fields/)).toBeTruthy();
    fireEvent.click(await screen.findByRole('button', { name: 'Update 1 record' }));
    await screen.findByText('1 record was updated.');

    expect(sent).toHaveLength(2);
    expect(sent.every(b => b.includeAssignedTo === true)).toBe(true);
  });

  it('says how many records were left out for no Write and that a partial list is partial', async () => {
    const { fetchFn } = pushServer({
      preview: () => previewBody([CHANGE_A], { skippedNoWriteCount: 3, truncated: true }),
    });
    renderPanel({ authenticatedFetch: fetchFn });

    expect(await screen.findByText('3 records are not listed because you cannot edit them.')).toBeTruthy();
    expect(screen.getByText(/Only part of the list is shown/)).toBeTruthy();
  });

  it('has no confirm button when nothing would change', async () => {
    const { fetchFn, sent } = pushServer({ preview: () => previewBody([]) });
    renderPanel({ authenticatedFetch: fetchFn });

    expect(await screen.findByText(/Nothing to update/)).toBeTruthy();
    expect((screen.getByRole('button', { name: 'Update 0 records' }) as HTMLButtonElement).disabled).toBe(true);
    expect(sent.filter(b => !b.preview)).toHaveLength(0);
  });

  it('makes one preview and one apply per target table', async () => {
    const { fetchFn, sent } = pushServer({
      preview: b =>
        previewBody(
          b.targetEntity === 'sprk_event' ? [CHANGE_A] : [{ ...CHANGE_A, recordId: INVOICE_A, name: 'Invoice 1' }]
        ),
      apply: () => ({ success: true, updatedCount: 1, failedCount: 0, errors: [] }),
    });
    renderPanel({ authenticatedFetch: fetchFn, rows: [...ROWS, { table: 'sprk_invoice', id: INVOICE_A }] });

    fireEvent.click(await screen.findByRole('button', { name: 'Update 2 records' }));
    await screen.findByText('2 records were updated.');

    expect(sent.map(b => `${b.targetEntity}:${b.preview ? 'preview' : 'apply'}`)).toEqual([
      'sprk_event:preview',
      'sprk_invoice:preview',
      'sprk_event:apply',
      'sprk_invoice:apply',
    ]);
    expect(sent[3].childIds).toEqual([INVOICE_A]);
  });

  it("shows the server's fixed per-record text for a record that was refused", async () => {
    const refused = 'This record is not one of the records this push updates; it was left unchanged.';
    const { fetchFn } = pushServer({
      preview: () => previewBody([CHANGE_A, CHANGE_B]),
      apply: () => ({
        success: true,
        updatedCount: 1,
        failedCount: 1,
        errors: [{ recordId: EVENT_B, error: refused }],
      }),
    });
    renderPanel({ authenticatedFetch: fetchFn });

    fireEvent.click(await screen.findByRole('button', { name: 'Update 2 records' }));

    expect(await screen.findByText('1 record was updated. 1 record was not updated.')).toBeTruthy();
    expect(screen.getByText(refused)).toBeTruthy();
    expect(screen.getAllByText('Hearing B').length).toBeGreaterThan(0);
  });

  it.each([403, 404, 500])('shows one fixed text, and no cause, when the preview answers %i', async status => {
    const { fetchFn, sent } = pushServer({
      previewStatus: status,
      preview: () => ({ title: 'Forbidden', detail: 'SECRET-CAUSE-DETAIL', reasonCode: 'internal_code' }),
    });
    renderPanel({ authenticatedFetch: fetchFn });

    expect(await screen.findByText(PUSH_FAILED_TEXT)).toBeTruthy();
    expect(document.body.textContent).not.toContain('SECRET-CAUSE-DETAIL');
    expect(document.body.textContent).not.toContain('internal_code');
    expect(sent.filter(b => !b.preview)).toHaveLength(0);
    expect(screen.queryByRole('button', { name: /^Update \d/ })).toBeNull();
  });

  it('shows the same fixed text when the apply fails', async () => {
    const { fetchFn } = pushServer({
      preview: () => previewBody([CHANGE_A]),
      applyStatus: 500,
      apply: () => ({ detail: 'SECRET-CAUSE-DETAIL' }),
    });
    renderPanel({ authenticatedFetch: fetchFn });

    fireEvent.click(await screen.findByRole('button', { name: 'Update 1 record' }));

    expect(await screen.findByText(PUSH_FAILED_TEXT)).toBeTruthy();
    expect(document.body.textContent).not.toContain('SECRET-CAUSE-DETAIL');
  });

  it('closing from the review writes nothing', async () => {
    const { fetchFn, sent } = pushServer({ preview: () => previewBody([CHANGE_A]) });
    const { props } = renderPanel({ authenticatedFetch: fetchFn });

    await screen.findByText('Hearing A');
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    await waitFor(() => expect(props.onClose).toHaveBeenCalled());
    expect(sent.filter(b => !b.preview)).toHaveLength(0);
  });

  it('says how many ticked records the server will not update, instead of "already have the current values"', async () => {
    const { fetchFn } = pushServer({ preview: () => previewBody([], { refusedChildIds: [EVENT_B] }) });
    renderPanel({ authenticatedFetch: fetchFn });

    expect(
      await screen.findByText('1 selected record cannot be updated from this record, so it is not listed.')
    ).toBeTruthy();
    expect(document.body.textContent).not.toContain('already have the current values');
  });

  it('reports a table with no Filing edge (a thrown 409) for that table, and still previews and applies the others', async () => {
    const onApplied = jest.fn();
    const { fetchFn, sent } = pushServer({
      previewStatus: b => (b.targetEntity === 'sprk_invoice' ? 409 : 200),
      preview: b => (b.targetEntity === 'sprk_invoice' ? { reasonCode: 'x' } : previewBody([CHANGE_A])),
      apply: () => ({ success: true, updatedCount: 1, failedCount: 0, errors: [] }),
    });
    renderPanel({ authenticatedFetch: fetchFn, onApplied, rows: [...ROWS, { table: 'sprk_invoice', id: INVOICE_A }] });

    expect(await screen.findByText(/Records of sprk_invoice cannot be updated from this record/)).toBeTruthy();
    expect(screen.queryByText(PUSH_FAILED_TEXT)).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Update 1 record' }));
    await screen.findByText('1 record was updated.');

    expect(sent.filter(b => !b.preview).map(b => b.targetEntity)).toEqual(['sprk_event']);
    expect(onApplied).toHaveBeenCalledWith({ updatedCount: 1, failedCount: 0, failedTables: [] });
  });

  it('reports a returned 409 for that table too (a fetch that returns failures)', async () => {
    const { fetchFn } = returningFetch(b =>
      b.targetEntity === 'sprk_invoice' ? json(409, {}) : json(200, previewBody([CHANGE_A]))
    );
    renderPanel({ authenticatedFetch: fetchFn, rows: [...ROWS, { table: 'sprk_invoice', id: INVOICE_A }] });

    expect(await screen.findByText(/Records of sprk_invoice cannot be updated from this record/)).toBeTruthy();
    expect(screen.getByText('Hearing A')).toBeTruthy();
  });

  it('a multi-table apply where one table fails shows what was updated, names the table that was not, and reports what was written', async () => {
    const onApplied = jest.fn();
    const { fetchFn } = pushServer({
      preview: b =>
        previewBody(
          b.targetEntity === 'sprk_event' ? [CHANGE_A] : [{ ...CHANGE_A, recordId: INVOICE_A, name: 'Invoice 1' }]
        ),
      applyStatus: b => (b.targetEntity === 'sprk_invoice' ? 500 : 200),
      apply: b =>
        b.targetEntity === 'sprk_invoice'
          ? { detail: 'SECRET-CAUSE-DETAIL' }
          : { success: true, updatedCount: 1, failedCount: 0, errors: [] },
    });
    renderPanel({ authenticatedFetch: fetchFn, onApplied, rows: [...ROWS, { table: 'sprk_invoice', id: INVOICE_A }] });

    fireEvent.click(await screen.findByRole('button', { name: 'Update 2 records' }));

    expect(await screen.findByText('1 record was updated.')).toBeTruthy();
    expect(screen.getByText(/Not completed: sprk_invoice\./)).toBeTruthy();
    expect(screen.getByText(/Updated: sprk_event\./)).toBeTruthy();
    expect(screen.queryByText(PUSH_FAILED_TEXT)).toBeNull();
    expect(document.body.textContent).not.toContain('SECRET-CAUSE-DETAIL');
    expect(onApplied).toHaveBeenCalledWith({ updatedCount: 1, failedCount: 0, failedTables: ['sprk_invoice'] });
  });

  it('does not re-run the preview when the host passes an equal source as a new object', async () => {
    const { fetchFn, sent } = pushServer({ preview: () => previewBody([CHANGE_A]) });
    const onClose = jest.fn();
    const ui = (source: { table: string; id: string }) => (
      <FluentProvider theme={webLightTheme}>
        <RecordFamilyActionPanel
          open
          onClose={onClose}
          action="push"
          source={source}
          rows={ROWS}
          authenticatedFetch={fetchFn}
        />
      </FluentProvider>
    );
    const { rerender } = render(ui({ ...SOURCE }));
    await screen.findByText('Hearing A');

    rerender(ui({ ...SOURCE }));
    await screen.findByText('Hearing A');

    expect(sent).toHaveLength(1);
  });

  it('asks nothing while closed', () => {
    const { fetchFn, sent } = pushServer({});
    renderPanel({ authenticatedFetch: fetchFn, open: false });

    expect(sent).toHaveLength(0);
  });
});

describe('recordFamilyPushService', () => {
  it('canonicalizes ids and names no family kind (the server always uses Filing)', () => {
    const request = buildPushRequest({ table: 'sprk_matter', id: `{${MATTER.toUpperCase()}}` }, 'sprk_event', {
      preview: false,
      includeAssignedTo: false,
      childIds: [`{${EVENT_A.toUpperCase()}}`],
    });

    expect(request).toEqual({
      sourceEntity: 'sprk_matter',
      sourceRecordId: MATTER,
      targetEntity: 'sprk_event',
      preview: false,
      includeAssignedTo: false,
      childIds: [EVENT_A],
    });
  });

  it('groups the ticked rows by table, de-duplicated, in first-seen order', () => {
    expect(
      groupRowsByTable([
        { table: 'sprk_event', id: EVENT_A },
        { table: 'sprk_invoice', id: INVOICE_A },
        { table: 'sprk_event', id: EVENT_A.toUpperCase() },
        { table: 'sprk_event', id: EVENT_B },
      ])
    ).toEqual([
      { table: 'sprk_event', ids: [EVENT_A, EVENT_B] },
      { table: 'sprk_invoice', ids: [INVOICE_A] },
    ]);
  });
});
