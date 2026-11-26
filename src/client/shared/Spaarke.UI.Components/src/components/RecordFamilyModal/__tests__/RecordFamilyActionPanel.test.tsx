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
import {
  buildPushRequest,
  groupRowsByTable,
  isNoProfileRefusal,
  parsePushResponse,
  PUSH_FAILED_TEXT,
  PUSH_PATH,
  pushErrorText,
  pushFieldMappings,
  PushRequestError,
  RESTRICTED_SOURCE_NOT_RESTRICTED_REASON_CODE,
  SECURE_SOURCE_NOT_ISOLATED_REASON_CODE,
  SECURE_SOURCE_REFILE_NOT_ALLOWED_REASON_CODE,
  SECURE_SOURCE_UNDETERMINED_REASON_CODE,
  sourceRuleSkipText,
} from '../recordFamilyPushService';
import { apiErrorFor, throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';

/** Task 032b-1: the one fixed line shown when the push skips rules (`skippedRuleCount > 0`). */
const SKIPPED_RULES_TEXT = 'Some fields are copied only when a record is created and are not updated here.';

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

  // Task 032b-1 (review F1): a push skips the rules it does not apply (an invoice's display copies, One-time rules) and counts
  // them; the panel must not then say the records "already have the current values".
  it('with rules the push skipped and nothing to change, says some fields are copied only at create, not "Nothing to update"', async () => {
    const { fetchFn } = pushServer({ preview: () => previewBody([], { skippedRuleCount: 4 }) });
    renderPanel({ authenticatedFetch: fetchFn, action: 'assignedTo' });

    expect(await screen.findByText(SKIPPED_RULES_TEXT)).toBeTruthy();
    expect(screen.queryByText(/Nothing to update/)).toBeNull();
    expect(screen.queryByText(/already have the current values/)).toBeNull();
    expect((screen.getByRole('button', { name: 'Update 0 records' }) as HTMLButtonElement).disabled).toBe(true);
  });

  it('with rules the push skipped and values to change, still says some fields are copied only at create', async () => {
    const { fetchFn } = pushServer({ preview: () => previewBody([CHANGE_A], { skippedRuleCount: 1 }) });
    renderPanel({ authenticatedFetch: fetchFn });

    expect(await screen.findByText(SKIPPED_RULES_TEXT)).toBeTruthy();
    expect(screen.getByText(/1 value on 1 record will change/)).toBeTruthy();
  });

  it('with no skipped rules, keeps "already have the current values" and says nothing about create-only fields', async () => {
    const { fetchFn } = pushServer({ preview: () => previewBody([]) });
    renderPanel({ authenticatedFetch: fetchFn });

    expect(
      await screen.findByText('Nothing to update: the selected records already have the current values.')
    ).toBeTruthy();
    expect(screen.queryByText(SKIPPED_RULES_TEXT)).toBeNull();
  });

  it('"Update Assigned To" says access can change only where the fields grant it', async () => {
    const { fetchFn } = pushServer({ preview: () => previewBody([CHANGE_B]) });
    renderPanel({ authenticatedFetch: fetchFn, action: 'assignedTo' });

    expect(
      await screen.findByText(
        'This update includes the Assigned To fields. On records where these fields grant access, changing them can change who has access.'
      )
    ).toBeTruthy();
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

  // Task 022 fix round 4 (the mapping-source rule): values copied from a secure source are written only into a record owned
  // by that source's isolation owner; a record that is not is returned with a stable code, on the preview and the apply.
  it('says, on the preview, that fields from a secure source are not copied to a record, and does not list it', async () => {
    const { fetchFn, sent } = pushServer({
      preview: () =>
        previewBody([CHANGE_A], {
          errors: [{ recordId: EVENT_B, error: 'server text', reasonCode: SECURE_SOURCE_NOT_ISOLATED_REASON_CODE }],
        }),
      apply: () => ({ success: true, updatedCount: 1, failedCount: 0, errors: [] }),
    });
    renderPanel({ authenticatedFetch: fetchFn });

    expect(await screen.findByText('1 record will not be updated because the source record is secure.')).toBeTruthy();
    expect(document.body.textContent).not.toContain('Some fields');
    expect(screen.queryByText('server text')).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: 'Update 1 record' }));
    await screen.findByText('1 record was updated.');
    expect(sent.filter(b => !b.preview)[0].childIds).toEqual([EVENT_A]);
  });

  it('says why when every record the preview found is kept from the push by the source rule', async () => {
    const { fetchFn } = pushServer({
      preview: () =>
        previewBody([], {
          errors: [{ recordId: EVENT_A, error: 'server text', reasonCode: SECURE_SOURCE_UNDETERMINED_REASON_CODE }],
        }),
    });
    renderPanel({ authenticatedFetch: fetchFn });

    expect(
      await screen.findByText(
        "1 record will not be updated because it could not be checked whether the source record's access allows copying. Try again later."
      )
    ).toBeTruthy();
    expect(screen.getByText('Nothing to update among the records that can be updated.')).toBeTruthy();
    expect(document.body.textContent).not.toContain('already have the current values');
    expect((screen.getByRole('button', { name: 'Update 0 records' }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("shows the source rule's wording, never a generic error, for a record the apply did not write", async () => {
    const { fetchFn } = pushServer({
      preview: () => previewBody([CHANGE_A, CHANGE_B]),
      apply: () => ({
        success: true,
        updatedCount: 1,
        failedCount: 1,
        errors: [{ recordId: EVENT_B, error: 'server text', reasonCode: SECURE_SOURCE_NOT_ISOLATED_REASON_CODE }],
      }),
    });
    renderPanel({ authenticatedFetch: fetchFn });

    fireEvent.click(await screen.findByRole('button', { name: 'Update 2 records' }));

    expect(await screen.findByText('1 record was updated. 1 record was not updated.')).toBeTruthy();
    expect(screen.getByText('No fields were copied to this record because the source record is secure.')).toBeTruthy();
    expect(document.body.textContent).not.toContain('Some fields');
    expect(screen.queryByText('server text')).toBeNull();
  });

  // UAC-r2 (a): a push from a secure source never changes what a record is filed under; such a record is not written at all.
  it("says, on the preview, that a record's filing can't be changed by a push from a secure record, and does not list it", async () => {
    const { fetchFn, sent } = pushServer({
      preview: () =>
        previewBody([CHANGE_A], {
          errors: [
            { recordId: EVENT_B, error: 'server text', reasonCode: SECURE_SOURCE_REFILE_NOT_ALLOWED_REASON_CODE },
          ],
        }),
      apply: () => ({ success: true, updatedCount: 1, failedCount: 0, errors: [] }),
    });
    renderPanel({ authenticatedFetch: fetchFn });

    expect(
      await screen.findByText(
        "1 record will not be updated because its filing can't be changed by a push from a secure or Restricted record."
      )
    ).toBeTruthy();
    expect(screen.queryByText('server text')).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: 'Update 1 record' }));
    await screen.findByText('1 record was updated.');
    expect(sent.filter(b => !b.preview)[0].childIds).toEqual([EVENT_A]);
  });

  it('shows the re-file wording, never a generic error, for a record the apply did not write', async () => {
    const { fetchFn } = pushServer({
      preview: () => previewBody([CHANGE_A, CHANGE_B]),
      apply: () => ({
        success: true,
        updatedCount: 1,
        failedCount: 1,
        errors: [{ recordId: EVENT_B, error: 'server text', reasonCode: SECURE_SOURCE_REFILE_NOT_ALLOWED_REASON_CODE }],
      }),
    });
    renderPanel({ authenticatedFetch: fetchFn });

    fireEvent.click(await screen.findByRole('button', { name: 'Update 2 records' }));

    expect(await screen.findByText('1 record was updated. 1 record was not updated.')).toBeTruthy();
    expect(
      screen.getByText(
        "No fields were copied to this record because its filing can't be changed by a push from a secure or Restricted record."
      )
    ).toBeTruthy();
    expect(screen.queryByText('server text')).toBeNull();
  });

  // Task 022c (the Restricted-source rule): values copied from a source whose access is Restricted are written only into a
  // record that is Restricted; a record that is not is returned with its own code, on the preview and the apply.
  it('says, on the preview, that a record that is not Restricted will not be updated from a Restricted source, and does not list it', async () => {
    const { fetchFn, sent } = pushServer({
      preview: () =>
        previewBody([CHANGE_A], {
          errors: [
            { recordId: EVENT_B, error: 'server text', reasonCode: RESTRICTED_SOURCE_NOT_RESTRICTED_REASON_CODE },
          ],
        }),
      apply: () => ({ success: true, updatedCount: 1, failedCount: 0, errors: [] }),
    });
    renderPanel({ authenticatedFetch: fetchFn });

    expect(
      await screen.findByText(
        "1 record will not be updated because the source record's access is Restricted and its access is not."
      )
    ).toBeTruthy();
    expect(screen.queryByText('server text')).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: 'Update 1 record' }));
    await screen.findByText('1 record was updated.');
    expect(sent.filter(b => !b.preview)[0].childIds).toEqual([EVENT_A]);
  });

  it('shows the Restricted wording, never a generic error, for a record the apply did not write', async () => {
    const { fetchFn } = pushServer({
      preview: () => previewBody([CHANGE_A, CHANGE_B]),
      apply: () => ({
        success: true,
        updatedCount: 1,
        failedCount: 1,
        errors: [{ recordId: EVENT_B, error: 'server text', reasonCode: RESTRICTED_SOURCE_NOT_RESTRICTED_REASON_CODE }],
      }),
    });
    renderPanel({ authenticatedFetch: fetchFn });

    fireEvent.click(await screen.findByRole('button', { name: 'Update 2 records' }));

    expect(await screen.findByText('1 record was updated. 1 record was not updated.')).toBeTruthy();
    expect(
      screen.getByText(
        "No fields were copied to this record because the source record's access is Restricted and this record's is not."
      )
    ).toBeTruthy();
    expect(screen.queryByText('server text')).toBeNull();
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

  it('a table whose profile has no rules (a preview with no changes) does not stop the other table from previewing and applying', async () => {
    const { fetchFn, sent } = pushServer({
      preview: b =>
        b.targetEntity === 'sprk_document'
          ? previewBody([], { warnings: ['Profile has no mapping rules configured.'] })
          : previewBody([CHANGE_A]),
      apply: () => ({ success: true, updatedCount: 1, failedCount: 0, errors: [] }),
    });
    renderPanel({
      authenticatedFetch: fetchFn,
      rows: [...ROWS, { table: 'sprk_document', id: 'dddddddd-0000-0000-0000-00000000000d' }],
    });

    expect(await screen.findByText('Hearing A')).toBeTruthy();
    expect(screen.queryByText(PUSH_FAILED_TEXT)).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Update 1 record' }));
    await screen.findByText('1 record was updated.');

    expect(sent.filter(b => !b.preview).map(b => b.targetEntity)).toEqual(['sprk_event']);
  });

  // Task 076c: a table with no mapping profile from the source is a 404 carrying `field_mapping_profile_missing`.
  const NO_PROFILE = { title: 'Profile Not Found', status: 404, reasonCode: 'field_mapping_profile_missing' };
  const DOC_A = 'dddddddd-0000-0000-0000-00000000000d';

  it('reports a table with no mapping profile (a thrown 404 + reasonCode) for that table, and still previews and applies the others', async () => {
    const onApplied = jest.fn();
    const { fetchFn, sent } = pushServer({
      previewStatus: b => (b.targetEntity === 'sprk_document' ? 404 : 200),
      preview: b =>
        b.targetEntity === 'sprk_document' ? { ...NO_PROFILE, detail: 'SECRET-CAUSE-DETAIL' } : previewBody([CHANGE_A]),
      apply: () => ({ success: true, updatedCount: 1, failedCount: 0, errors: [] }),
    });
    renderPanel({ authenticatedFetch: fetchFn, onApplied, rows: [...ROWS, { table: 'sprk_document', id: DOC_A }] });

    expect(await screen.findByText('No field push is set up from this record to sprk_document.')).toBeTruthy();
    expect(screen.getByText('Hearing A')).toBeTruthy();
    expect(screen.queryByText(PUSH_FAILED_TEXT)).toBeNull();
    expect(document.body.textContent).not.toContain('SECRET-CAUSE-DETAIL');
    fireEvent.click(screen.getByRole('button', { name: 'Update 1 record' }));
    await screen.findByText('1 record was updated.');

    expect(sent.filter(b => !b.preview).map(b => b.targetEntity)).toEqual(['sprk_event']);
    expect(onApplied).toHaveBeenCalledWith({ updatedCount: 1, failedCount: 0, failedTables: [] });
  });

  it('reports a RETURNED 404 + reasonCode for that table too (a fetch that returns failures)', async () => {
    const { fetchFn } = returningFetch(b =>
      b.targetEntity === 'sprk_document' ? json(404, NO_PROFILE) : json(200, previewBody([CHANGE_A]))
    );
    renderPanel({ authenticatedFetch: fetchFn, rows: [...ROWS, { table: 'sprk_document', id: DOC_A }] });

    expect(await screen.findByText('No field push is set up from this record to sprk_document.')).toBeTruthy();
    expect(screen.getByText('Hearing A')).toBeTruthy();
    expect(screen.queryByText(PUSH_FAILED_TEXT)).toBeNull();
  });

  it('a 404 WITHOUT the profile code (the source was not found) still fails the whole preview', async () => {
    const { fetchFn, sent } = pushServer({
      previewStatus: b => (b.targetEntity === 'sprk_document' ? 404 : 200),
      preview: b =>
        b.targetEntity === 'sprk_document' ? { title: 'Not Found', status: 404 } : previewBody([CHANGE_A]),
    });
    renderPanel({ authenticatedFetch: fetchFn, rows: [...ROWS, { table: 'sprk_document', id: DOC_A }] });

    expect(await screen.findByText(PUSH_FAILED_TEXT)).toBeTruthy();
    expect(screen.queryByText(/No field push is set up/)).toBeNull();
    expect(screen.queryByRole('button', { name: /^Update \d/ })).toBeNull();
    expect(sent.filter(b => !b.preview)).toHaveLength(0);
  });

  it('a RETURNED 404 with a different reason code still fails the whole preview', async () => {
    const { fetchFn } = returningFetch(b =>
      b.targetEntity === 'sprk_document'
        ? json(404, { reasonCode: 'something_else' })
        : json(200, previewBody([CHANGE_A]))
    );
    renderPanel({ authenticatedFetch: fetchFn, rows: [...ROWS, { table: 'sprk_document', id: DOC_A }] });

    expect(await screen.findByText(PUSH_FAILED_TEXT)).toBeTruthy();
  });

  it('when every ticked table has no profile, says so per table, shows nothing to apply and not the generic failure', async () => {
    const { fetchFn, sent } = pushServer({ previewStatus: 404, preview: () => NO_PROFILE });
    renderPanel({
      authenticatedFetch: fetchFn,
      rows: [...ROWS, { table: 'sprk_document', id: DOC_A }],
    });

    expect(await screen.findByText('No field push is set up from this record to sprk_event.')).toBeTruthy();
    expect(screen.getByText('No field push is set up from this record to sprk_document.')).toBeTruthy();
    expect(screen.getByText(/Nothing to update/)).toBeTruthy();
    expect(screen.queryByText(PUSH_FAILED_TEXT)).toBeNull();
    expect((screen.getByRole('button', { name: 'Update 0 records' }) as HTMLButtonElement).disabled).toBe(true);
    expect(sent.filter(b => !b.preview)).toHaveLength(0);
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

describe('isNoProfileRefusal', () => {
  const CODE = 'field_mapping_profile_missing';

  it('fires for a thrown ApiError 404 whose problem carries the code', () => {
    expect(isNoProfileRefusal(apiErrorFor(404, { title: 'Profile Not Found', status: 404, reasonCode: CODE }))).toBe(
      true
    );
  });

  it('fires for a returned 404 whose body carried the code', () => {
    expect(isNoProfileRefusal(new PushRequestError(404, CODE))).toBe(true);
  });

  it.each([
    ['a thrown 404 without a code', apiErrorFor(404, { title: 'Not Found', status: 404 })],
    ['a thrown 404 with another code', apiErrorFor(404, { title: 'x', status: 404, reasonCode: 'other' })],
    ['a thrown 404 with no problem body', apiErrorFor(404)],
    ['a thrown 409 carrying the code', apiErrorFor(409, { title: 'x', status: 409, reasonCode: CODE })],
    ['a thrown 500 carrying the code', apiErrorFor(500, { title: 'x', status: 500, reasonCode: CODE })],
    ['a returned 404 without a code', new PushRequestError(404)],
    ['a returned 404 with another code', new PushRequestError(404, 'other')],
    ['a returned 409 carrying the code', new PushRequestError(409, CODE)],
    ['an ordinary Error', new Error(CODE)],
    ['a non-error', null],
  ])('does not fire for %s', (_label, err) => {
    expect(isNoProfileRefusal(err)).toBe(false);
  });

  it('pushFieldMappings carries only the reason code of a returned 404, and survives a body that is not JSON', async () => {
    const request = buildPushRequest(SOURCE, 'sprk_document', { preview: true, includeAssignedTo: false });
    const withCode = async () => json(404, { reasonCode: CODE, detail: 'SECRET-CAUSE-DETAIL' });
    const notJson = async () =>
      ({
        ok: false,
        status: 404,
        json: async () => {
          throw new SyntaxError('Unexpected token');
        },
      }) as unknown as Response;

    const coded = await pushFieldMappings(withCode, request).catch(e => e);
    expect(coded).toBeInstanceOf(PushRequestError);
    expect(coded.reasonCode).toBe(CODE);
    expect(JSON.stringify(coded)).not.toContain('SECRET-CAUSE-DETAIL');
    expect(coded.message).not.toContain('SECRET-CAUSE-DETAIL');

    const plain = await pushFieldMappings(notJson, request).catch(e => e);
    expect(plain).toBeInstanceOf(PushRequestError);
    expect(plain.reasonCode).toBeUndefined();
    expect(isNoProfileRefusal(plain)).toBe(false);
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

  // Task 022 fix round 4: the mapping-source codes are read from the body and mapped to their wording.
  it('reads the reason code of an entry and maps each source-rule code to its wording, else keeps the server text', () => {
    const parsed = parsePushResponse({
      success: true,
      errors: [
        { recordId: EVENT_A, error: 'server text A', reasonCode: SECURE_SOURCE_NOT_ISOLATED_REASON_CODE },
        { recordId: EVENT_B, error: 'server text B', reasonCode: SECURE_SOURCE_UNDETERMINED_REASON_CODE },
        { recordId: EVENT_A, error: 'server text R', reasonCode: SECURE_SOURCE_REFILE_NOT_ALLOWED_REASON_CODE },
        { recordId: EVENT_B, error: 'server text X', reasonCode: RESTRICTED_SOURCE_NOT_RESTRICTED_REASON_CODE },
        { recordId: INVOICE_A, error: 'server text C' },
        { recordId: MATTER, error: 'server text D', reasonCode: 'constructor' },
      ],
    });

    expect(SECURE_SOURCE_REFILE_NOT_ALLOWED_REASON_CODE).toBe('field_mapping.push.secure_source_refile_not_allowed');
    expect(SECURE_SOURCE_UNDETERMINED_REASON_CODE).toBe('field_mapping.push.secure_source_undetermined');
    expect(RESTRICTED_SOURCE_NOT_RESTRICTED_REASON_CODE).toBe('field_mapping.push.restricted_source_not_restricted');
    expect(parsed.errors.map(e => e.reasonCode)).toEqual([
      SECURE_SOURCE_NOT_ISOLATED_REASON_CODE,
      SECURE_SOURCE_UNDETERMINED_REASON_CODE,
      SECURE_SOURCE_REFILE_NOT_ALLOWED_REASON_CODE,
      RESTRICTED_SOURCE_NOT_RESTRICTED_REASON_CODE,
      undefined,
      'constructor',
    ]);
    // The same texts the server sends (FieldMappingPushSourceGate.MessageFor; pinned there by
    // FieldMappingPushSourceGateTests.EachCodesText_IsTheFixedTextTheClientShows).
    expect(parsed.errors.map(pushErrorText)).toEqual([
      'No fields were copied to this record because the source record is secure.',
      "No fields were copied to this record because it could not be checked whether the source record's access allows copying. Try again later.",
      "No fields were copied to this record because its filing can't be changed by a push from a secure or Restricted record.",
      "No fields were copied to this record because the source record's access is Restricted and this record's is not.",
      'server text C',
      'server text D',
    ]);
  });

  // Task 022 fix round 4: each source-rule code skips the record whole; the preview counts say so, singular and plural.
  it('counts the records each source-rule code keeps from the push, as records that will not be updated', () => {
    const entry = (reasonCode: string) => ({ recordId: EVENT_A, error: 'x', reasonCode });
    expect(
      sourceRuleSkipText([
        entry(SECURE_SOURCE_NOT_ISOLATED_REASON_CODE),
        entry(SECURE_SOURCE_NOT_ISOLATED_REASON_CODE),
        entry(SECURE_SOURCE_REFILE_NOT_ALLOWED_REASON_CODE),
        entry(SECURE_SOURCE_UNDETERMINED_REASON_CODE),
        entry(SECURE_SOURCE_UNDETERMINED_REASON_CODE),
        entry(SECURE_SOURCE_UNDETERMINED_REASON_CODE),
      ])
    ).toEqual([
      "1 record will not be updated because its filing can't be changed by a push from a secure or Restricted record.",
      '2 records will not be updated because the source record is secure.',
      "3 records will not be updated because it could not be checked whether the source record's access allows copying. Try again later.",
    ]);
    expect(sourceRuleSkipText([])).toEqual([]);
  });

  // Task 022c: the Restricted-source code is counted too, singular and plural, beside the secure-source codes.
  it('counts the records the Restricted-source code keeps from the push', () => {
    const entry = (reasonCode: string) => ({ recordId: EVENT_A, error: 'x', reasonCode });
    expect(sourceRuleSkipText([entry(RESTRICTED_SOURCE_NOT_RESTRICTED_REASON_CODE)])).toEqual([
      "1 record will not be updated because the source record's access is Restricted and its access is not.",
    ]);
    expect(
      sourceRuleSkipText([
        entry(RESTRICTED_SOURCE_NOT_RESTRICTED_REASON_CODE),
        entry(RESTRICTED_SOURCE_NOT_RESTRICTED_REASON_CODE),
        entry(SECURE_SOURCE_REFILE_NOT_ALLOWED_REASON_CODE),
      ])
    ).toEqual([
      "1 record will not be updated because its filing can't be changed by a push from a secure or Restricted record.",
      "2 records will not be updated because the source record's access is Restricted and their access is not.",
    ]);
  });
});
