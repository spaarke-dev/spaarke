/**
 * R3 task 054 (FR-24 documents on the detail view, FR-26 delete own) - what the SPA shows and does.
 *
 * What runs for real: `DocumentLibrary`, `SmartTodo`, `EventsCalendar`, `RecordDetailModal` and the six widget bodies,
 * the shared `SprkModal` / `ConfirmModal`, `RecordMessages`, `delete-own` (the creator rule and the failure messages),
 * `web-api-client` (routes) and Fluent v9. What is substituted: the network seam (`bffApiCall` / `bffApiBlob`, answering
 * the external routes and throwing `ApiError` exactly as bff-client builds it: the raw ProblemDetails body is the
 * message), MSAL's `useMsal` (BffAccessState reads it), the shared grid (reports its rows, opens one on click) and two
 * browser-only helpers jsdom lacks.
 *
 * The SERVER is the authority for every one of these actions (task 031 / 032). These tests prove the SPA offers an
 * action only where it should, asks before a delete, reflects the server's answer, and never hides a refusal.
 */
import * as React from 'react';
import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

const { bffApiCall, bffApiBlob, gridSpecs } = vi.hoisted(() => ({
  bffApiCall: vi.fn(),
  bffApiBlob: vi.fn(),
  gridSpecs: {} as Record<
    string,
    { entityName: string; idAttribute: string; nameAttribute: string; rows: Array<Record<string, unknown>> }
  >,
}));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  // `getExternalUserContext` calls the module's own bffApiCall, so it is routed to the substituted seam as well.
  return { ...actual, bffApiCall, bffApiBlob, getExternalUserContext: () => bffApiCall('/api/v1/external/me') };
});

const { loginRedirect } = vi.hoisted(() => ({ loginRedirect: vi.fn() }));
vi.mock('@azure/msal-react', () => ({
  useMsal: () => ({ instance: { loginRedirect }, accounts: [], inProgress: 'none' }),
}));

vi.mock('@spaarke/ui-components/utils/themeStorage', () => ({
  resolveCodePageTheme: () => null,
  setupCodePageThemeListener: () => () => undefined,
}));
vi.mock('../src/services/gridDataverseClient', () => ({ gridDataverseClient: {} }));

// The shared grid, as in RecordDetailModal.test.tsx: reports its rows and opens one through `onRecordOpen`.
vi.mock('../src/widgets/ExternalDataGrid', async () => {
  const R = await import('react');
  return {
    ExternalDataGrid: (props: {
      configId: string;
      onRecordsLoaded?: (rows: ReadonlyArray<Record<string, unknown>>) => void;
      onRecordOpen?: (id: string, row: Record<string, unknown>, ctx: Record<string, unknown>) => void;
    }) => {
      const spec = gridSpecs[props.configId];
      const { onRecordsLoaded } = props;
      R.useEffect(() => {
        onRecordsLoaded?.(spec.rows);
      }, [onRecordsLoaded, spec.rows]);
      return R.createElement(
        'ul',
        { 'data-testid': 'fake-grid' },
        spec.rows.map(row =>
          R.createElement(
            'li',
            { key: String(row[spec.idAttribute]) },
            R.createElement(
              'button',
              {
                onClick: () =>
                  props.onRecordOpen?.(String(row[spec.idAttribute]), row, {
                    configId: props.configId,
                    entityName: spec.entityName,
                    selectedIds: [],
                  }),
              },
              String(row[spec.nameAttribute])
            )
          )
        )
      );
    },
  };
});

import { AccessLevel, ApiError } from '../src/types';
import { DocumentLibrary } from '../src/components/DocumentLibrary';
import { SmartTodo } from '../src/components/SmartTodo';
import { EventsCalendar } from '../src/components/EventsCalendar';
import { RecordMessages } from '../src/components/detail/RecordMessages';
import { RecordDetailModal } from '../src/components/detail/RecordDetailModal';
import { callerIdentityOf, deleteFailureOf, isCreatedByCaller, type CallerIdentity } from '../src/api/delete-own';
import MattersWidgetBody from '../src/widgets/MattersWidget';
import ProjectsWidgetBody from '../src/widgets/ProjectsWidget';
import ServiceRequestsWidgetBody from '../src/widgets/ServiceRequestsWidget';
import WorkAssignmentsWidgetBody from '../src/widgets/WorkAssignmentsWidget';
import DocumentsWidgetBody from '../src/widgets/DocumentsWidget';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const SLOW = { timeout: 60000 };
const guid = (n: number): string => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;

const ME = guid(7); // the signed-in contact
const SOMEONE = guid(8); // another contact
const SYSTEM_USER = guid(9); // a workforce user (systemuser)
const STAFF = guid(10); // a systemuser who is not the caller

const PROJECT = guid(101);
const MATTER = guid(201);
const WORK_ASSIGNMENT = guid(301);
const SERVICE_REQUEST = guid(601);

const asContact: CallerIdentity = { contactId: ME, systemUserId: null };
const asSystemUser: CallerIdentity = { contactId: null, systemUserId: SYSTEM_USER };

const problem = (status: number, extra: Record<string, unknown> = {}): string =>
  JSON.stringify({ status, title: 'refused', ...extra });

interface DocRow {
  sprk_documentid: string;
  sprk_name: string;
  sprk_documenttype?: string;
  createdon: string;
  _sprk_createdbycontact_value?: string;
  _sprk_createdbyperson_value?: string;
}

const doc = (n: number, name: string, stamps: Partial<DocRow> = {}): DocRow => ({
  sprk_documentid: guid(900 + n),
  sprk_name: name,
  createdon: '2026-03-01T10:00:00Z',
  ...stamps,
});

/** Four documents: the caller's own (contact stamp), someone else's, staff-created (person stamp) and an unstamped one. */
function mixedDocs(): DocRow[] {
  return [
    doc(1, 'Mine.pdf', { _sprk_createdbycontact_value: ME }),
    doc(2, 'Theirs.pdf', { _sprk_createdbycontact_value: SOMEONE }),
    doc(3, 'ByStaff.pdf', { _sprk_createdbyperson_value: STAFF }),
    doc(4, 'Unstamped.pdf'),
  ];
}

const deleteCalls = (): unknown[][] =>
  bffApiCall.mock.calls.filter(c => (c[1] as RequestInit | undefined)?.method === 'DELETE');

/** The Delete action of a named row (undefined-safe). */
const deleteButton = (aria: string): HTMLElement | null => screen.queryByRole('button', { name: aria });

function renderInTheme(ui: React.ReactElement) {
  return render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);
}

/**
 * jsdom does no layout: every element reports `offsetParent === null` and the body a 0x0 rect. Tabster (Fluent v9's focus
 * and modal manager) then treats every element as display:none, so a Dialog finds no focusable child, falls back to focusing
 * its own surface, and the NESTED confirmation's modalizer is never activated. 250 ms later tabster hides that surface
 * (`aria-hidden="true"`) and `getByRole` cannot see Delete/Cancel. Probed (task 054 review fix): with these two geometry
 * stubs and no other change, the confirmation takes over as the active modalizer, focus lands on a button inside it, and it
 * stays unhidden; without them the same test fails. It is jsdom's missing layout, not a defect in SprkModal/ConfirmModal
 * (a browser reports real geometry). The stubs give jsdom the geometry a browser has; nothing else is changed.
 */
const realOffsetParent = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'offsetParent');
function installLayout(): void {
  Object.defineProperty(HTMLElement.prototype, 'offsetParent', {
    configurable: true,
    get(this: HTMLElement) {
      return this.parentNode;
    },
  });
  vi.spyOn(document.body, 'getBoundingClientRect').mockReturnValue({
    x: 0,
    y: 0,
    top: 0,
    left: 0,
    right: 1024,
    bottom: 768,
    width: 1024,
    height: 768,
    toJSON: () => ({}),
  });
}
function uninstallLayout(): void {
  if (realOffsetParent) Object.defineProperty(HTMLElement.prototype, 'offsetParent', realOffsetParent);
}

/**
 * A button on the page once the page is reachable again: while a modal is open tabster hides everything behind it, and lifts
 * that on a 250 ms debounce after the modal closes, so a query made at the instant of closing can still see nothing.
 */
const reachableButton = (name: string | RegExp): Promise<HTMLElement> =>
  screen.findByRole('button', { name }, { timeout: 5000 });

/** Tabster applies (and lifts) `aria-hidden` on a 250 ms debounce after a modal opens or closes. */
const MODALIZER_SETTLE_MS = 350;
const settleModalizer = (): Promise<void> => new Promise(resolve => setTimeout(resolve, MODALIZER_SETTLE_MS));

/**
 * The shared confirmation modal (ConfirmModal -> SprkModal): its dialog element, AFTER tabster has had its hide pass.
 * It must then be reachable by role (not `aria-hidden`): what a keyboard or screen-reader user needs, also when it is
 * nested over the record detail dialog.
 */
async function confirmDialog(title: RegExp = /^Delete (document|task|event)$/): Promise<HTMLElement> {
  const heading = await screen.findByText(title);
  // ConfirmModal is `dismiss="alert"`, which Fluent renders as an alertdialog.
  const dialog = heading.closest('[role="dialog"], [role="alertdialog"]');
  if (!dialog) throw new Error('confirmation title is not inside a dialog');
  await settleModalizer();
  expect(dialog.getAttribute('aria-hidden')).toBeNull();
  return dialog as HTMLElement;
}

beforeEach(() => {
  installLayout();
  bffApiCall.mockReset();
  bffApiBlob.mockReset();
  loginRedirect.mockReset();
  vi.spyOn(console, 'error').mockImplementation(() => undefined);
});

afterEach(() => {
  vi.restoreAllMocks();
  uninstallLayout();
});

// ---------------------------------------------------------------------------
// The creator rule and the failure messages (pure; the components below use them)
// ---------------------------------------------------------------------------

describe('which rows are "mine"', () => {
  it('a contact matches its own contact stamp, never a person stamp, never someone else', () => {
    expect(isCreatedByCaller({ _sprk_createdbycontact_value: ME }, asContact)).toBe(true);
    expect(isCreatedByCaller({ _sprk_createdbycontact_value: SOMEONE }, asContact)).toBe(false);
    // The principal kind decides: a person stamp equal to the contact's own id is not the contact's row.
    expect(isCreatedByCaller({ _sprk_createdbyperson_value: ME }, asContact)).toBe(false);
    expect(isCreatedByCaller({}, asContact)).toBe(false);
  });

  it('a systemuser matches its person stamp, or a contact stamp only through its own linked contact', () => {
    expect(isCreatedByCaller({ _sprk_createdbyperson_value: SYSTEM_USER }, asSystemUser)).toBe(true);
    expect(isCreatedByCaller({ _sprk_createdbyperson_value: STAFF }, asSystemUser)).toBe(false);
    // No linked contact known: a contact stamp never matches.
    expect(isCreatedByCaller({ _sprk_createdbycontact_value: ME }, asSystemUser)).toBe(false);
    const linked: CallerIdentity = { contactId: ME, systemUserId: SYSTEM_USER };
    expect(isCreatedByCaller({ _sprk_createdbycontact_value: ME }, linked)).toBe(true);
    expect(isCreatedByCaller({ _sprk_createdbycontact_value: SOMEONE }, linked)).toBe(false);
  });

  it('ids compare case-insensitively; the empty guid and a missing identity never match', () => {
    expect(isCreatedByCaller({ _sprk_createdbycontact_value: ME.toUpperCase() }, asContact)).toBe(true);
    const empty = '00000000-0000-0000-0000-000000000000';
    expect(callerIdentityOf({ contactId: empty })).toBeNull();
    expect(isCreatedByCaller({ _sprk_createdbycontact_value: empty }, { contactId: empty, systemUserId: null })).toBe(
      false
    );
    expect(isCreatedByCaller({ _sprk_createdbycontact_value: ME }, null)).toBe(false);
    expect(isCreatedByCaller({ _sprk_createdbycontact_value: ME }, undefined)).toBe(false);
    expect(callerIdentityOf(null)).toBeNull();
    expect(callerIdentityOf({ contactId: ME })).toEqual({ contactId: ME, systemUserId: null });
  });
});

describe('what a refused delete says', () => {
  const messageOf = (status: number, body: Record<string, unknown>, noun = 'document'): string => {
    const failure = deleteFailureOf(new ApiError(status, problem(status, body)), noun);
    if (failure.kind !== 'error') throw new Error('expected an error message');
    return failure.message;
  };

  it('403 says it was not deleted and why; 409 distinguishes checked-out from unverified storage', () => {
    expect(messageOf(403, { reasonCode: 'sdap.access.deny.delete_own' })).toMatch(
      /cannot delete this document.*Nothing was deleted/
    );
    expect(messageOf(409, { reasonCode: 'document_locked' })).toMatch(/checked out/);
    expect(messageOf(409, { reasonCode: 'document_storage_unverified' })).toMatch(
      /could not be verified.*Nothing was deleted/
    );
    expect(messageOf(409, { reasonCode: 'document_locked' })).not.toBe(
      messageOf(409, { reasonCode: 'document_storage_unverified' })
    );
  });

  it('503 says nothing was deleted and to retry; 500 says it may be partly removed; anything else is generic', () => {
    expect(messageOf(503, { reasonCode: 'sdap.access.delete_own_unavailable' })).toMatch(
      /nothing was deleted.*try again/i
    );
    expect(messageOf(500, { reasonCode: 'document_row_not_deleted' })).toMatch(/partly removed/);
    expect(messageOf(418, {})).toMatch(/could not be deleted/);
    // The noun follows the row type.
    expect(messageOf(403, {}, 'task')).toMatch(/this task/);
  });

  it('a 401 is the sign-in state, not a message; a server detail is never echoed', () => {
    expect(deleteFailureOf(new ApiError(401, ''), 'document')).toEqual({ kind: 'signin' });
    expect(messageOf(500, { detail: 'internal: SpeFileStore blew up' })).not.toMatch(/SpeFileStore/);
  });
});

// ---------------------------------------------------------------------------
// Documents: list / download / upload on every record type (FR-24)
// ---------------------------------------------------------------------------

describe('DocumentLibrary on each record type', () => {
  const ROOTS = [
    ['projects', PROJECT],
    ['matters', MATTER],
    ['workassignments', WORK_ASSIGNMENT],
    ['servicerequests', SERVICE_REQUEST],
  ] as const;

  beforeEach(() => {
    bffApiCall.mockImplementation(async (path: string) => {
      if (/\/documents$/.test(path)) return { value: [doc(1, 'Mine.pdf', { _sprk_createdbycontact_value: ME })] };
      throw new Error(`unexpected BFF call ${path}`);
    });
  });

  it.each(ROOTS)(
    "%s: lists from, downloads through and uploads to the record's own documents route",
    async (root, id) => {
      const createObjectURL = vi.fn(() => 'blob:test');
      Object.assign(window.URL, { createObjectURL, revokeObjectURL: vi.fn() });
      vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
      bffApiBlob.mockResolvedValue(new Blob(['bytes']));

      renderInTheme(<DocumentLibrary root={root} recordId={id} canUpload canDownload />);
      await screen.findByText('Mine.pdf');
      expect(bffApiCall.mock.calls[0][0]).toBe(`/api/v1/external/${root}/${id}/documents`);

      // Download: the record's own content route, then the bytes are handed to the browser.
      fireEvent.click(screen.getByRole('button', { name: 'Download Mine.pdf' }));
      await waitFor(() => expect(createObjectURL).toHaveBeenCalledTimes(1));
      expect(bffApiBlob).toHaveBeenCalledWith(`/api/v1/external/${root}/${id}/documents/${guid(901)}/content`);

      // Upload: a multipart POST to the same documents route; the record id is the route, never the body.
      bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
        if (options?.method === 'POST') return { sprk_documentid: guid(999) };
        if (/\/documents$/.test(path)) return { value: [] };
        throw new Error(`unexpected BFF call ${path}`);
      });
      fireEvent.click(screen.getByRole('button', { name: /Upload Document/ }));
      const input = await waitFor(() => {
        const el = document.querySelector('input[type="file"]');
        if (!el) throw new Error('no file input yet');
        return el as HTMLInputElement;
      });
      fireEvent.change(input, { target: { files: [new File(['%PDF'], 'new.pdf', { type: 'application/pdf' })] } });
      fireEvent.click(await screen.findByRole('button', { name: 'Upload' }));
      await waitFor(() => {
        const post = bffApiCall.mock.calls.find(c => (c[1] as RequestInit | undefined)?.method === 'POST');
        expect(post?.[0]).toBe(`/api/v1/external/${root}/${id}/documents`);
        expect((post?.[1] as RequestInit).body).toBeInstanceOf(FormData);
      });
    },
    SLOW
  );

  it(
    'a service request reads its documents only from the servicerequests route',
    async () => {
      renderInTheme(
        <DocumentLibrary root="servicerequests" recordId={SERVICE_REQUEST} canUpload={false} canDownload />
      );
      await screen.findByText('Mine.pdf');
      expect(bffApiCall.mock.calls.map(c => c[0])).toEqual([
        `/api/v1/external/servicerequests/${SERVICE_REQUEST}/documents`,
      ]);
    },
    SLOW
  );

  it.each([
    ['empty list', [] as DocRow[]],
    ['non-empty list', [doc(1, 'Mine.pdf')]],
  ])(
    'a caller with no Create right sees no upload control (%s)',
    async (_label, rows) => {
      bffApiCall.mockImplementation(async () => ({ value: rows }));
      renderInTheme(<DocumentLibrary root="matters" recordId={MATTER} canUpload={false} canDownload />);
      await waitFor(() => expect(screen.queryByText(/Loading documents/)).toBeNull());
      expect(screen.queryByRole('button', { name: /Upload Document/ })).toBeNull();
      // Reading is still possible: the rows (and download) remain.
      if (rows.length > 0) expect(screen.getByRole('button', { name: 'Download Mine.pdf' })).toBeTruthy();
    },
    SLOW
  );

  it(
    'a forced upload with no Create right shows the server 403 as an error (no row appears)',
    async () => {
      bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
        if (options?.method === 'POST') throw new ApiError(403, problem(403, { reasonCode: 'insufficient_rights' }));
        return { value: [] };
      });
      renderInTheme(<DocumentLibrary root="matters" recordId={MATTER} canUpload canDownload />);
      fireEvent.click(await screen.findByRole('button', { name: /Upload Document/ }));
      const input = await waitFor(() => {
        const el = document.querySelector('input[type="file"]');
        if (!el) throw new Error('no file input yet');
        return el as HTMLInputElement;
      });
      fireEvent.change(input, { target: { files: [new File(['%PDF'], 'x.pdf', { type: 'application/pdf' })] } });
      fireEvent.click(await screen.findByRole('button', { name: 'Upload' }));

      expect(
        await screen.findByText('Your access level does not permit uploading documents to this matter.')
      ).toBeTruthy();
    },
    SLOW
  );

  it.each([
    [true, true],
    [false, false],
    [undefined, false],
  ])(
    "a matter with no host override follows the list's canCreate (%s): Upload shown = %s",
    async (canCreate, shown) => {
      bffApiCall.mockImplementation(async () => ({
        value: [doc(1, 'Mine.pdf')],
        ...(canCreate === undefined ? {} : { canCreate }),
      }));
      renderInTheme(<DocumentLibrary root="matters" recordId={MATTER} canDownload />);
      await screen.findByText('Mine.pdf');
      expect(screen.queryByRole('button', { name: /Upload Document/ }) !== null).toBe(shown);
    },
    SLOW
  );

  it(
    'a host override beats the list: canUpload={false} hides Upload even when the list says canCreate',
    async () => {
      bffApiCall.mockImplementation(async () => ({ value: [doc(1, 'Mine.pdf')], canCreate: true }));
      renderInTheme(<DocumentLibrary root="matters" recordId={MATTER} canUpload={false} canDownload />);
      await screen.findByText('Mine.pdf');
      expect(screen.queryByRole('button', { name: /Upload Document/ })).toBeNull();
    },
    SLOW
  );

  it(
    "a project shorthand (projectId) ignores the list's canCreate and follows the access level",
    async () => {
      bffApiCall.mockImplementation(async () => ({ value: [doc(1, 'Mine.pdf')], canCreate: true }));
      renderInTheme(<DocumentLibrary projectId={PROJECT} accessLevel={AccessLevel.ViewOnly} />);
      await screen.findByText('Mine.pdf');
      expect(screen.queryByRole('button', { name: /Upload Document/ })).toBeNull();
    },
    SLOW
  );

  it.each([
    ['an empty projectId', { projectId: '' }],
    ['an empty recordId', { root: 'matters', recordId: '' }],
  ])(
    '%s is an error state, not a spinner that never ends, and reads nothing',
    async (_label, props) => {
      bffApiCall.mockImplementation(async () => ({ value: [] }));
      renderInTheme(<DocumentLibrary {...(props as { projectId: string })} />);
      expect(await screen.findByText('Documents cannot be loaded: no record was given.')).toBeTruthy();
      expect(screen.queryByText(/Loading documents/)).toBeNull();
      expect(bffApiCall).not.toHaveBeenCalled();
    },
    SLOW
  );

  it(
    'without a caller identity, no row offers Delete (and no actions column appears for a non-downloader)',
    async () => {
      bffApiCall.mockImplementation(async () => ({ value: mixedDocs() }));
      renderInTheme(<DocumentLibrary root="matters" recordId={MATTER} canUpload={false} canDownload={false} />);
      await screen.findByText('Mine.pdf');
      expect(screen.queryAllByRole('button', { name: /^Delete / })).toHaveLength(0);
      expect(screen.queryAllByRole('button', { name: /^Download / })).toHaveLength(0);
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Delete own document (FR-26)
// ---------------------------------------------------------------------------

describe('delete own document', () => {
  function installList(rows: DocRow[], onDelete?: () => unknown): void {
    bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
      if (options?.method === 'DELETE') {
        if (onDelete) return onDelete();
        return undefined; // 204
      }
      if (/\/documents$/.test(path)) return { value: rows };
      throw new Error(`unexpected BFF call ${path}`);
    });
  }

  const renderLibrary = (caller: CallerIdentity | null = asContact) =>
    renderInTheme(<DocumentLibrary root="matters" recordId={MATTER} canUpload canDownload caller={caller} />);

  it(
    'shows Delete only on the rows the caller created',
    async () => {
      installList(mixedDocs());
      renderLibrary();
      await screen.findByText('Mine.pdf');

      expect(deleteButton('Delete Mine.pdf')).not.toBeNull();
      expect(deleteButton('Delete Theirs.pdf')).toBeNull();
      expect(deleteButton('Delete ByStaff.pdf')).toBeNull();
      expect(deleteButton('Delete Unstamped.pdf')).toBeNull();
      expect(screen.getAllByRole('button', { name: /^Delete / })).toHaveLength(1);
    },
    SLOW
  );

  it(
    'a systemuser caller sees Delete on its person-stamped rows only',
    async () => {
      installList(mixedDocs().map((d, i) => (i === 2 ? { ...d, _sprk_createdbyperson_value: SYSTEM_USER } : d)));
      renderLibrary(asSystemUser);
      await screen.findByText('Mine.pdf');

      expect(deleteButton('Delete ByStaff.pdf')).not.toBeNull();
      expect(deleteButton('Delete Mine.pdf')).toBeNull(); // a contact stamp, and this systemuser has no linked contact
      expect(screen.getAllByRole('button', { name: /^Delete / })).toHaveLength(1);
    },
    SLOW
  );

  it(
    'asks first; Cancel changes nothing and sends nothing',
    async () => {
      installList(mixedDocs());
      renderLibrary();
      await screen.findByText('Mine.pdf');

      fireEvent.click(deleteButton('Delete Mine.pdf')!);
      const dialog = await confirmDialog();
      expect(within(dialog).getByText(/Delete "Mine\.pdf"\?/)).toBeTruthy();
      expect(deleteCalls()).toHaveLength(0);

      fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
      await waitFor(() => expect(screen.queryByText(/^Delete document$/)).toBeNull());
      expect(deleteCalls()).toHaveLength(0);
      expect(screen.getByText('Mine.pdf')).toBeTruthy();
    },
    SLOW
  );

  it(
    'Confirm sends one DELETE by id; a 204 removes the row and the others stay',
    async () => {
      installList(mixedDocs());
      renderLibrary();
      await screen.findByText('Mine.pdf');

      fireEvent.click(deleteButton('Delete Mine.pdf')!);
      const dialog = await confirmDialog();
      const confirm = within(dialog).getByRole('button', { name: 'Delete' });
      fireEvent.click(confirm);
      fireEvent.click(confirm); // a double click must not send twice

      await waitFor(() => expect(screen.queryByText('Mine.pdf')).toBeNull());
      expect(deleteCalls()).toEqual([[`/api/v1/external/documents/${guid(901)}`, { method: 'DELETE' }]]);
      expect(screen.getByText('Theirs.pdf')).toBeTruthy();
      expect(screen.queryByTestId('delete-own-error')).toBeNull();
      expect(screen.queryByText(/^Delete document$/)).toBeNull();
    },
    SLOW
  );

  it(
    'deleting the last document leaves the empty state (and the upload control)',
    async () => {
      installList([doc(1, 'Mine.pdf', { _sprk_createdbycontact_value: ME })]);
      renderLibrary();
      await screen.findByText('Mine.pdf');

      fireEvent.click(deleteButton('Delete Mine.pdf')!);
      fireEvent.click(within(await confirmDialog()).getByRole('button', { name: 'Delete' }));

      await screen.findByText('No Documents');
      expect(await reachableButton(/Upload Document/)).toBeTruthy();
    },
    SLOW
  );

  it.each([
    [403, { reasonCode: 'sdap.access.deny.delete_own' }, /cannot delete this document.*Nothing was deleted/],
    [409, { reasonCode: 'document_locked' }, /checked out/],
    [409, { reasonCode: 'document_storage_unverified' }, /could not be verified/],
    [503, { reasonCode: 'sdap.access.delete_own_unavailable' }, /nothing was deleted.*try again/i],
    [500, { reasonCode: 'document_row_not_deleted' }, /partly removed/],
  ])(
    'a %s (%j) shows an error message and the row stays',
    async (status, body, message) => {
      installList(mixedDocs(), () => {
        throw new ApiError(status, problem(status, body));
      });
      renderLibrary();
      await screen.findByText('Mine.pdf');

      fireEvent.click(deleteButton('Delete Mine.pdf')!);
      fireEvent.click(within(await confirmDialog()).getByRole('button', { name: 'Delete' }));

      const error = await screen.findByTestId('delete-own-error');
      expect(error.textContent).toMatch(message);
      expect(screen.getByText('Mine.pdf')).toBeTruthy();
      expect(screen.queryByText(/^Delete document$/)).toBeNull(); // the dialog closed
      expect(deleteCalls()).toHaveLength(1);
      // The next attempt is possible: the action is still there.
      expect(await reachableButton('Delete Mine.pdf')).toBeTruthy();
    },
    SLOW
  );

  it(
    'a network failure is an error with the row kept, and Dismiss clears it',
    async () => {
      installList(mixedDocs(), () => {
        throw new TypeError('Failed to fetch');
      });
      renderLibrary();
      await screen.findByText('Mine.pdf');

      fireEvent.click(deleteButton('Delete Mine.pdf')!);
      fireEvent.click(within(await confirmDialog()).getByRole('button', { name: 'Delete' }));
      const error = await screen.findByTestId('delete-own-error');
      expect(error.textContent).toMatch(/could not be deleted/);
      expect(screen.getByText('Mine.pdf')).toBeTruthy();

      fireEvent.click(await within(error).findByRole('button', { name: 'Dismiss' }, { timeout: 5000 }));
      await waitFor(() => expect(screen.queryByTestId('delete-own-error')).toBeNull());
    },
    SLOW
  );

  it(
    'a 401 is the shared sign-in state, not an error message, and the row stays',
    async () => {
      installList(mixedDocs(), () => {
        throw new ApiError(401, '');
      });
      renderLibrary();
      await screen.findByText('Mine.pdf');

      fireEvent.click(deleteButton('Delete Mine.pdf')!);
      fireEvent.click(within(await confirmDialog()).getByRole('button', { name: 'Delete' }));

      expect(await screen.findByTestId('bff-access-signin')).toBeTruthy();
      expect(screen.queryByTestId('delete-own-error')).toBeNull();
      expect(screen.getByText('Mine.pdf')).toBeTruthy();
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Delete own to-do / event (FR-26)
// ---------------------------------------------------------------------------

describe('delete own to-do', () => {
  const todo = (n: number, name: string, stamps: Record<string, string> = {}) => ({
    sprk_todoid: guid(700 + n),
    sprk_name: name,
    statuscode: 1,
    ...stamps,
  });

  function install(onDelete?: () => unknown): void {
    bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
      if (options?.method === 'DELETE') return onDelete ? onDelete() : undefined;
      if (path === `/api/v1/external/projects/${PROJECT}/todos`) {
        return {
          value: [
            todo(1, 'My task', { _sprk_createdbycontact_value: ME }),
            todo(2, 'Their task', { _sprk_createdbycontact_value: SOMEONE }),
            todo(3, 'Unstamped task'),
          ],
        };
      }
      throw new Error(`unexpected BFF call ${path}`);
    });
  }

  const renderTodos = (caller: CallerIdentity | null = asContact) =>
    renderInTheme(<SmartTodo projectId={PROJECT} accessLevel={AccessLevel.ViewOnly} caller={caller} />);

  it(
    'a read-only (ViewOnly) caller still gets Delete on their own tasks, and only theirs',
    async () => {
      install();
      renderTodos();
      await screen.findByText('My task');

      expect(screen.getAllByRole('button', { name: /^Delete task / })).toHaveLength(1);
      expect(deleteButton('Delete task My task')).not.toBeNull();
      // No create / toggle affordance appears for ViewOnly: Delete does not widen the read-only view.
      expect(screen.queryByRole('button', { name: /Create new task/ })).toBeNull();
      expect(screen.queryByRole('button', { name: /Mark task complete/ })).toBeNull();
    },
    SLOW
  );

  it(
    'no caller, no Delete',
    async () => {
      install();
      renderTodos(null);
      await screen.findByText('My task');
      expect(screen.queryAllByRole('button', { name: /^Delete task / })).toHaveLength(0);
    },
    SLOW
  );

  it(
    'asks, then a 204 removes it; Cancel does nothing',
    async () => {
      install();
      renderTodos();
      await screen.findByText('My task');

      fireEvent.click(deleteButton('Delete task My task')!);
      let dialog = await confirmDialog();
      fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
      await waitFor(() => expect(screen.queryByText(/^Delete task$/)).toBeNull());
      expect(deleteCalls()).toHaveLength(0);

      fireEvent.click(await reachableButton('Delete task My task'));
      dialog = await confirmDialog();
      fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));
      await waitFor(() => expect(screen.queryByText('My task')).toBeNull());
      expect(deleteCalls()).toEqual([[`/api/v1/external/todos/${guid(701)}`, { method: 'DELETE' }]]);
      expect(screen.getByText('Their task')).toBeTruthy();
    },
    SLOW
  );

  it(
    'a 403 shows an error and the task stays',
    async () => {
      install(() => {
        throw new ApiError(403, problem(403, { reasonCode: 'sdap.access.deny.delete_own' }));
      });
      renderTodos();
      await screen.findByText('My task');

      fireEvent.click(deleteButton('Delete task My task')!);
      fireEvent.click(within(await confirmDialog()).getByRole('button', { name: 'Delete' }));

      expect((await screen.findByTestId('delete-own-error')).textContent).toMatch(/cannot delete this task/);
      expect(screen.getByText('My task')).toBeTruthy();
    },
    SLOW
  );
});

describe('delete own event', () => {
  const event = (n: number, name: string, stamps: Record<string, string> = {}) => ({
    sprk_eventid: guid(800 + n),
    sprk_name: name,
    sprk_duedate: '2026-03-01',
    sprk_status: 659490001,
    ...stamps,
  });

  function install(onDelete?: () => unknown): void {
    bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
      if (options?.method === 'DELETE') return onDelete ? onDelete() : undefined;
      if (path === `/api/v1/external/projects/${PROJECT}/events`) {
        return {
          value: [
            event(1, 'My call', { _sprk_createdbycontact_value: ME }),
            event(2, 'Their call', { _sprk_createdbycontact_value: SOMEONE }),
            event(3, 'Staff call', { _sprk_createdbyperson_value: STAFF }),
          ],
        };
      }
      throw new Error(`unexpected BFF call ${path}`);
    });
  }

  const renderEvents = (caller: CallerIdentity | null = asContact) =>
    renderInTheme(<EventsCalendar projectId={PROJECT} accessLevel={AccessLevel.ViewOnly} caller={caller} />);

  it(
    'shows Delete only on events the caller created',
    async () => {
      install();
      renderEvents();
      await screen.findByText('My call');
      expect(screen.getAllByRole('button', { name: /^Delete event / })).toHaveLength(1);
      expect(deleteButton('Delete event My call')).not.toBeNull();
    },
    SLOW
  );

  it(
    'asks, then a 204 removes it; the others stay',
    async () => {
      install();
      renderEvents();
      await screen.findByText('My call');

      fireEvent.click(deleteButton('Delete event My call')!);
      fireEvent.click(within(await confirmDialog()).getByRole('button', { name: 'Delete' }));

      await waitFor(() => expect(screen.queryByText('My call')).toBeNull());
      expect(deleteCalls()).toEqual([[`/api/v1/external/events/${guid(801)}`, { method: 'DELETE' }]]);
      expect(screen.getByText('Their call')).toBeTruthy();
      expect(screen.getByText('Staff call')).toBeTruthy();
    },
    SLOW
  );

  it(
    'a 503 shows that nothing was deleted and the event stays',
    async () => {
      install(() => {
        throw new ApiError(503, problem(503, { reasonCode: 'sdap.access.delete_own_unavailable' }));
      });
      renderEvents();
      await screen.findByText('My call');

      fireEvent.click(deleteButton('Delete event My call')!);
      fireEvent.click(within(await confirmDialog()).getByRole('button', { name: 'Delete' }));

      expect((await screen.findByTestId('delete-own-error')).textContent).toMatch(/nothing was deleted/i);
      expect(screen.getByText('My call')).toBeTruthy();
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Messages have no Delete
// ---------------------------------------------------------------------------

describe('messages', () => {
  it(
    "a thread has no Delete action, not even on the caller's own message",
    async () => {
      bffApiCall.mockImplementation(async (path: string) => {
        if (path === `/api/v1/external/matters/${MATTER}/messages`) {
          return {
            value: [
              {
                sprk_communicationid: guid(950),
                sprk_body: 'Mine',
                sprk_bodyformat: 100000000,
                sendername: 'Me',
                isMine: true,
              },
              {
                sprk_communicationid: guid(951),
                sprk_body: 'Theirs',
                sprk_bodyformat: 100000000,
                sendername: 'Them',
                isMine: false,
              },
            ],
          };
        }
        throw new Error(`unexpected BFF call ${path}`);
      });
      renderInTheme(<RecordMessages entity="sprk_matter" recordId={MATTER} />);
      await screen.findByText('Mine');

      expect(screen.queryAllByRole('button', { name: /delete|remove/i })).toHaveLength(0);
      expect(deleteCalls()).toHaveLength(0);
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// The detail view
// ---------------------------------------------------------------------------

const GRID = {
  project: {
    configId: '61711823-1092-f111-b8dc-7ced8ddc4a05',
    entity: 'sprk_project',
    id: 'sprk_projectid',
    name: 'sprk_projectnumber',
    recordId: PROJECT,
    Body: ProjectsWidgetBody,
  },
  matter: {
    configId: '583a2a33-1092-f111-b8dc-7ced8ddc4a05',
    entity: 'sprk_matter',
    id: 'sprk_matterid',
    name: 'sprk_matternumber',
    recordId: MATTER,
    Body: MattersWidgetBody,
  },
  wa: {
    configId: '42f4102c-1092-f111-b8dc-7ced8ddc4a05',
    entity: 'sprk_workassignment',
    id: 'sprk_workassignmentid',
    name: 'sprk_workassignmentnumber',
    recordId: WORK_ASSIGNMENT,
    Body: WorkAssignmentsWidgetBody,
  },
  sr: {
    configId: '403e5d37-cb94-f111-b8db-00224835447a',
    entity: 'sprk_servicerequest',
    id: 'sprk_servicerequestid',
    name: 'sprk_name',
    recordId: SERVICE_REQUEST,
    Body: ServiceRequestsWidgetBody,
  },
  document: {
    configId: '3af4102c-1092-f111-b8dc-7ced8ddc4a05',
    entity: 'sprk_document',
    id: 'sprk_documentid',
    name: 'sprk_documentname',
    recordId: guid(990),
    Body: DocumentsWidgetBody,
  },
} as const;

type GridKey = keyof typeof GRID;

const RECORD_ROW: Record<string, Record<string, unknown>> = {
  sprk_project: { sprk_projectname: 'Project P', sprk_projectnumber: 'PRJ-1' },
  sprk_matter: { sprk_mattername: 'Matter M', sprk_matternumber: 'MAT-1' },
  sprk_workassignment: { sprk_name: 'Work W', sprk_workassignmentnumber: 'WA-1' },
  sprk_servicerequest: { sprk_name: 'Request R', sprk_servicerequestnumber: 'SR-1' },
  sprk_document: { sprk_documentname: 'A document', sprk_documenttype: 'Contract' },
};

interface DetailNet {
  /** `/me` answer; `null` makes the call fail. */
  me: { contactId: string; projects: Array<{ projectId: string; accessLevel: string }> } | null;
  documents: DocRow[];
  /** The BFF's `canCreate` on the document list; `undefined` omits the member (a BFF that does not say). */
  documentsCanCreate?: boolean;
  todos: Array<Record<string, unknown>>;
  events: Array<Record<string, unknown>>;
  messages: Array<Record<string, unknown>>;
  onDelete?: (path: string) => unknown;
}

function installDetailNet(key: GridKey, net: DetailNet): void {
  const g = GRID[key];
  bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
    if (options?.method === 'DELETE') return net.onDelete ? net.onDelete(path) : undefined;
    if (path === '/api/v1/external/me') {
      if (!net.me) throw new ApiError(500, 'boom');
      return { email: 'me@example.test', ...net.me };
    }
    if (path.endsWith('/api/dataverse/fetch')) {
      const body = JSON.parse(String(options?.body)) as { entityName: string; fetchXml: string };
      if (body.entityName === g.entity)
        return { entities: [{ [g.id]: g.recordId, ...RECORD_ROW[g.entity] }], moreRecords: false };
      return { entities: [], moreRecords: false };
    }
    if (/\/documents$/.test(path)) {
      return net.documentsCanCreate === undefined
        ? { value: net.documents }
        : { value: net.documents, canCreate: net.documentsCanCreate };
    }
    if (/\/todos$/.test(path)) return { value: net.todos };
    if (/\/events$/.test(path)) return { value: net.events };
    if (/\/messages$/.test(path)) return { value: net.messages };
    throw new Error(`unexpected BFF call ${options?.method ?? 'GET'} ${path}`);
  });
}

function emptyNet(over: Partial<DetailNet> = {}): DetailNet {
  return {
    me: { contactId: ME, projects: [{ projectId: PROJECT, accessLevel: 'Collaborate' }] },
    documents: [],
    documentsCanCreate: true,
    todos: [],
    events: [],
    messages: [],
    ...over,
  };
}

async function openDetail(key: GridKey): Promise<HTMLElement> {
  const g = GRID[key];
  gridSpecs[g.configId] = {
    entityName: g.entity,
    idAttribute: g.id,
    nameAttribute: g.name,
    rows: [{ [g.id]: g.recordId, [g.name]: `grid-${key}` }],
  };
  const Body = g.Body as React.ComponentType<{ title: string }>;
  renderInTheme(<Body title="widget" />);
  fireEvent.click(screen.getByRole('button', { name: `grid-${key}` }));
  const dialog = await screen.findByRole('dialog');
  await within(dialog).findByTestId('record-detail-fields');
  return dialog;
}

describe('the record detail view', () => {
  it(
    "Project, Collaborate: Upload is offered; Delete only on the caller's own document, task and event",
    async () => {
      installDetailNet(
        'project',
        emptyNet({
          documents: mixedDocs(),
          todos: [
            { sprk_todoid: guid(701), sprk_name: 'My task', statuscode: 1, _sprk_createdbycontact_value: ME },
            { sprk_todoid: guid(702), sprk_name: 'Their task', statuscode: 1, _sprk_createdbycontact_value: SOMEONE },
          ],
          events: [
            { sprk_eventid: guid(801), sprk_name: 'My call', _sprk_createdbycontact_value: ME },
            { sprk_eventid: guid(802), sprk_name: 'Their call', _sprk_createdbycontact_value: SOMEONE },
          ],
          messages: [
            { sprk_communicationid: guid(950), sprk_body: 'My note', sprk_bodyformat: 100000000, isMine: true },
          ],
        })
      );
      const dialog = await openDetail('project');
      const docs = await within(dialog).findByTestId('record-detail-documents');
      await within(docs).findByText('Mine.pdf');
      const events = within(dialog).getByTestId('record-detail-events');
      await within(events).findByText('My call');
      await within(events).findByText('My task');

      expect(within(docs).getByRole('button', { name: /Upload Document/ })).toBeTruthy();
      // Delete appears exactly on the caller's rows - once the identity read (/me) has returned.
      await waitFor(() => expect(within(dialog).getAllByRole('button', { name: /^Delete / })).toHaveLength(3));
      expect(within(dialog).getByRole('button', { name: 'Delete Mine.pdf' })).toBeTruthy();
      expect(within(dialog).getByRole('button', { name: 'Delete task My task' })).toBeTruthy();
      expect(within(dialog).getByRole('button', { name: 'Delete event My call' })).toBeTruthy();
      expect(
        within(dialog).queryAllByRole('button', {
          name: /^Delete (task |event )?(Their task|Their call|Theirs\.pdf|ByStaff\.pdf|Unstamped\.pdf)$/,
        })
      ).toHaveLength(0);

      // No message has a Delete action.
      const messages = within(dialog).getByTestId('record-detail-messages');
      await within(messages).findByText('My note');
      expect(within(messages).queryAllByRole('button', { name: /delete|remove/i })).toHaveLength(0);
    },
    SLOW
  );

  it(
    "Project, ViewOnly: no Upload; downloads and the caller's own Delete remain",
    async () => {
      installDetailNet(
        'project',
        emptyNet({
          me: { contactId: ME, projects: [{ projectId: PROJECT, accessLevel: 'ViewOnly' }] },
          documents: mixedDocs(),
        })
      );
      const dialog = await openDetail('project');
      const docs = await within(dialog).findByTestId('record-detail-documents');
      await within(docs).findByText('Mine.pdf');

      await waitFor(() => expect(within(docs).queryAllByRole('button', { name: /^Delete / })).toHaveLength(1));
      expect(within(docs).queryByRole('button', { name: /Upload Document/ })).toBeNull();
      expect(within(docs).getByRole('button', { name: 'Download Theirs.pdf' })).toBeTruthy();
    },
    SLOW
  );

  it(
    'Project, level unknown (/me fails): no Upload and no Delete anywhere, but the documents still list',
    async () => {
      installDetailNet('project', emptyNet({ me: null, documents: mixedDocs() }));
      const dialog = await openDetail('project');
      const docs = await within(dialog).findByTestId('record-detail-documents');
      await within(docs).findByText('Mine.pdf');
      await new Promise(resolve => setTimeout(resolve, 50));

      expect(within(dialog).queryAllByRole('button', { name: /^Delete / })).toHaveLength(0);
      expect(within(docs).queryByRole('button', { name: /Upload Document/ })).toBeNull();
    },
    SLOW
  );

  it(
    'Matter, the BFF says the caller can create: the matter documents route, Upload offered, Delete on own only',
    async () => {
      installDetailNet('matter', emptyNet({ documents: mixedDocs(), documentsCanCreate: true }));
      const dialog = await openDetail('matter');
      const docs = await within(dialog).findByTestId('record-detail-documents');
      await within(docs).findByText('Mine.pdf');

      expect(within(docs).getByRole('button', { name: /Upload Document/ })).toBeTruthy();
      await waitFor(() => expect(within(docs).queryAllByRole('button', { name: /^Delete / })).toHaveLength(1));
      expect(bffApiCall.mock.calls.map(c => String(c[0]))).toContain(`/api/v1/external/matters/${MATTER}/documents`);
    },
    SLOW
  );

  it(
    'Matter, a ViewOnly reader (canCreate false): no Upload; downloads and the own-row Delete remain',
    async () => {
      installDetailNet('matter', emptyNet({ documents: mixedDocs(), documentsCanCreate: false }));
      const dialog = await openDetail('matter');
      const docs = await within(dialog).findByTestId('record-detail-documents');
      await within(docs).findByText('Mine.pdf');

      expect(within(docs).queryByRole('button', { name: /Upload Document/ })).toBeNull();
      expect(within(docs).getByRole('button', { name: 'Download Theirs.pdf' })).toBeTruthy();
      await waitFor(() => expect(within(docs).queryAllByRole('button', { name: /^Delete / })).toHaveLength(1));
    },
    SLOW
  );

  it(
    'Work assignment, a ViewOnly reader (canCreate false): no Upload; a Collaborate one (true) has it',
    async () => {
      installDetailNet('wa', emptyNet({ documents: mixedDocs(), documentsCanCreate: false }));
      const viewOnly = await openDetail('wa');
      const docs = await within(viewOnly).findByTestId('record-detail-documents');
      await within(docs).findByText('Mine.pdf');
      expect(within(docs).queryByRole('button', { name: /Upload Document/ })).toBeNull();
      cleanup();

      installDetailNet('wa', emptyNet({ documents: mixedDocs(), documentsCanCreate: true }));
      const collaborate = await openDetail('wa');
      const docs2 = await within(collaborate).findByTestId('record-detail-documents');
      await within(docs2).findByText('Mine.pdf');
      expect(within(docs2).getByRole('button', { name: /Upload Document/ })).toBeTruthy();
    },
    SLOW
  );

  it(
    'Matter, a ViewOnly reader and no documents: no Upload in the empty state either',
    async () => {
      installDetailNet('matter', emptyNet({ documents: [], documentsCanCreate: false }));
      const dialog = await openDetail('matter');
      const docs = await within(dialog).findByTestId('record-detail-documents');
      await within(docs).findByText(/No documents have been uploaded to this matter yet\.$/);
      expect(within(docs).queryByRole('button', { name: /Upload Document/ })).toBeNull();
    },
    SLOW
  );

  it(
    'Matter, a BFF that does not say (no canCreate member): fail closed, no Upload',
    async () => {
      installDetailNet('matter', emptyNet({ documents: mixedDocs(), documentsCanCreate: undefined }));
      const dialog = await openDetail('matter');
      const docs = await within(dialog).findByTestId('record-detail-documents');
      await within(docs).findByText('Mine.pdf');
      expect(within(docs).queryByRole('button', { name: /Upload Document/ })).toBeNull();
    },
    SLOW
  );

  it(
    "Project: the level from /me still decides Upload; the list's canCreate does not override it",
    async () => {
      installDetailNet(
        'project',
        emptyNet({
          me: { contactId: ME, projects: [{ projectId: PROJECT, accessLevel: 'ViewOnly' }] },
          documents: mixedDocs(),
          documentsCanCreate: true,
        })
      );
      const dialog = await openDetail('project');
      const docs = await within(dialog).findByTestId('record-detail-documents');
      await within(docs).findByText('Mine.pdf');
      await waitFor(() => expect(within(docs).queryAllByRole('button', { name: /^Delete / })).toHaveLength(1));
      expect(within(docs).queryByRole('button', { name: /Upload Document/ })).toBeNull();
    },
    SLOW
  );

  it(
    'Service request: documents via the servicerequests route, deletable like any other; the request itself has no Delete',
    async () => {
      installDetailNet('sr', emptyNet({ documents: mixedDocs() }));
      const dialog = await openDetail('sr');
      const docs = await within(dialog).findByTestId('record-detail-documents');
      await within(docs).findByText('Mine.pdf');
      await waitFor(() => expect(within(dialog).queryAllByRole('button', { name: /^Delete / })).toHaveLength(1));

      // The only Delete in the whole dialog is the caller's own document: nothing deletes the request or a message.
      expect(within(dialog).getByRole('button', { name: 'Delete Mine.pdf' })).toBeTruthy();
      expect(within(dialog).queryAllByRole('button', { name: /delete|remove/i })).toHaveLength(1);

      const calls = bffApiCall.mock.calls.map(c => String(c[0]));
      expect(calls).toContain(`/api/v1/external/servicerequests/${SERVICE_REQUEST}/documents`);
      // Never the module fetch for the documents of a service request.
      const moduleDocumentReads = bffApiCall.mock.calls
        .filter(c => String(c[0]).endsWith('/api/dataverse/fetch'))
        .filter(
          c => (JSON.parse(String((c[1] as RequestInit).body)) as { entityName: string }).entityName === 'sprk_document'
        );
      expect(moduleDocumentReads).toHaveLength(0);
    },
    SLOW
  );

  it(
    'Service request with no documents of its own: no Delete control exists in the dialog at all',
    async () => {
      installDetailNet(
        'sr',
        emptyNet({ documents: [doc(2, 'Theirs.pdf', { _sprk_createdbycontact_value: SOMEONE })] })
      );
      const dialog = await openDetail('sr');
      const docs = await within(dialog).findByTestId('record-detail-documents');
      await within(docs).findByText('Theirs.pdf');
      await new Promise(resolve => setTimeout(resolve, 50));

      expect(within(dialog).queryAllByRole('button', { name: /delete|remove/i })).toHaveLength(0);
    },
    SLOW
  );

  it(
    "a document's own detail has no documents section and no Delete",
    async () => {
      installDetailNet('document', emptyNet());
      const dialog = await openDetail('document');
      await new Promise(resolve => setTimeout(resolve, 50));
      expect(within(dialog).queryByTestId('record-detail-documents')).toBeNull();
      expect(within(dialog).queryAllByRole('button', { name: /delete|remove/i })).toHaveLength(0);
    },
    SLOW
  );

  it(
    'confirm then delete in the detail view: the DELETE goes to the external plane, the row disappears, then a 403 keeps the next',
    async () => {
      let refuse = false;
      installDetailNet(
        'matter',
        emptyNet({
          documents: [
            doc(1, 'Mine.pdf', { _sprk_createdbycontact_value: ME }),
            doc(5, 'Mine too.pdf', { _sprk_createdbycontact_value: ME }),
          ],
          onDelete: () => {
            if (refuse) throw new ApiError(403, problem(403, { reasonCode: 'sdap.access.deny.delete_own' }));
            return undefined;
          },
        })
      );
      const dialog = await openDetail('matter');
      const docs = await within(dialog).findByTestId('record-detail-documents');
      await within(docs).findByText('Mine.pdf');
      await waitFor(() => expect(within(docs).queryAllByRole('button', { name: /^Delete / })).toHaveLength(2));

      // The confirmation appears on top of the detail view; nothing is sent until Delete is pressed.
      fireEvent.click(within(docs).getByRole('button', { name: 'Delete Mine.pdf' }));
      const confirm = await confirmDialog();
      expect(deleteCalls()).toHaveLength(0);
      fireEvent.click(within(confirm).getByRole('button', { name: 'Delete' }));
      await waitFor(() => expect(within(docs).queryByText('Mine.pdf')).toBeNull());
      expect(deleteCalls().map(c => c[0])).toEqual([`/api/v1/external/documents/${guid(901)}`]);
      expect(within(docs).getByText('Mine too.pdf')).toBeTruthy();

      // The detail dialog is reachable again once the confirmation has closed (tabster lifts its aria-hidden on a debounce).
      refuse = true;
      fireEvent.click(await within(docs).findByRole('button', { name: 'Delete Mine too.pdf' }, SLOW));
      fireEvent.click(within(await confirmDialog()).getByRole('button', { name: 'Delete' }));
      expect((await within(dialog).findByTestId('delete-own-error', undefined, SLOW)).textContent).toMatch(
        /cannot delete this document/
      );
      expect(within(docs).getByText('Mine too.pdf')).toBeTruthy();
    },
    SLOW
  );

  it(
    'cancel, then delete in one detail view: the second confirmation is reachable by role and works',
    async () => {
      installDetailNet(
        'matter',
        emptyNet({
          documents: [
            doc(1, 'Mine.pdf', { _sprk_createdbycontact_value: ME }),
            doc(5, 'Mine too.pdf', { _sprk_createdbycontact_value: ME }),
          ],
        })
      );
      const dialog = await openDetail('matter');
      const docs = await within(dialog).findByTestId('record-detail-documents');
      await waitFor(() => expect(within(docs).queryAllByRole('button', { name: /^Delete / })).toHaveLength(2));

      // First confirmation: Cancel sends nothing and closes it.
      fireEvent.click(within(docs).getByRole('button', { name: 'Delete Mine.pdf' }));
      fireEvent.click(within(await confirmDialog()).getByRole('button', { name: 'Cancel' }));
      await waitFor(() => expect(screen.queryByText(/^Delete document$/)).toBeNull());
      expect(deleteCalls()).toHaveLength(0);

      // Second confirmation, same detail view: found by role (not aria-hidden), and Delete works.
      fireEvent.click(await within(docs).findByRole('button', { name: 'Delete Mine.pdf' }, SLOW));
      fireEvent.click(within(await confirmDialog()).getByRole('button', { name: 'Delete' }));
      await waitFor(() => expect(within(docs).queryByText('Mine.pdf')).toBeNull());
      expect(deleteCalls().map(c => c[0])).toEqual([`/api/v1/external/documents/${guid(901)}`]);
      expect(within(docs).getByText('Mine too.pdf')).toBeTruthy();
    },
    SLOW
  );

  it(
    'two consecutive deletes in one detail view: both confirmations are reachable by role and both rows go',
    async () => {
      installDetailNet(
        'matter',
        emptyNet({
          documents: [
            doc(1, 'Mine.pdf', { _sprk_createdbycontact_value: ME }),
            doc(5, 'Mine too.pdf', { _sprk_createdbycontact_value: ME }),
          ],
        })
      );
      const dialog = await openDetail('matter');
      const docs = await within(dialog).findByTestId('record-detail-documents');
      await waitFor(() => expect(within(docs).queryAllByRole('button', { name: /^Delete / })).toHaveLength(2));

      fireEvent.click(within(docs).getByRole('button', { name: 'Delete Mine.pdf' }));
      fireEvent.click(within(await confirmDialog()).getByRole('button', { name: 'Delete' }));
      await waitFor(() => expect(within(docs).queryByText('Mine.pdf')).toBeNull());

      fireEvent.click(await within(docs).findByRole('button', { name: 'Delete Mine too.pdf' }, SLOW));
      fireEvent.click(within(await confirmDialog()).getByRole('button', { name: 'Delete' }));
      await waitFor(() => expect(within(docs).queryByText('Mine too.pdf')).toBeNull());

      expect(deleteCalls().map(c => c[0])).toEqual([
        `/api/v1/external/documents/${guid(901)}`,
        `/api/v1/external/documents/${guid(905)}`,
      ]);
      expect(within(dialog).queryByTestId('delete-own-error')).toBeNull();
    },
    SLOW
  );

  it(
    'paging to the next record does not read /me again (identity does not change per record)',
    async () => {
      installDetailNet('matter', emptyNet({ documents: mixedDocs() }));
      const NEXT = guid(202);
      const Host: React.FC = () => {
        const [index, setIndex] = React.useState(0);
        return (
          <RecordDetailModal
            open
            onClose={() => undefined}
            entityName="sprk_matter"
            recordIds={[MATTER, NEXT]}
            index={index}
            onIndexChange={setIndex}
          />
        );
      };
      renderInTheme(<Host />);
      const dialog = await screen.findByRole('dialog');
      await within(dialog).findByTestId('record-detail-fields');
      await within(dialog).findByText('Mine.pdf');
      await waitFor(() => expect(within(dialog).queryAllByRole('button', { name: /^Delete / })).toHaveLength(1));

      fireEvent.click(within(dialog).getByRole('button', { name: 'Next record' }));
      await within(dialog).findByText('2 of 2');
      await within(dialog).findByTestId('record-detail-fields');
      await within(dialog).findByText('Mine.pdf');
      // Delete is still offered on the second record: the identity from the one read is kept.
      await waitFor(() => expect(within(dialog).queryAllByRole('button', { name: /^Delete / })).toHaveLength(1));

      expect(bffApiCall.mock.calls.filter(c => c[0] === '/api/v1/external/me')).toHaveLength(1);
    },
    SLOW
  );
});
