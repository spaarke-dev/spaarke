/**
 * R3 task 050 (FR-01 / FR-02 / FR-03) — opening a grid row in the external SPA / Teams tab shows a read-only detail view.
 *
 * What runs for real: the six widget bodies (`createGridWidgetBody`), `RecordDetailModal`, `RecordDetailFields`, the
 * canonical `SprkModal` shell, and the existing `EventsCalendar` / `SmartTodo` / `web-api-client`. What is substituted:
 * the network seam (`bffApiCall`, answering the module read seam's routes — the id-filtered fetch is the ONE detail
 * read for all six entities; the mock models the real server, which attaches `@formattedValues` on the fetch seam only), the shared grid (a fake that reports its rows
 * and opens one on click, exactly the two callbacks the real grid fires), and two browser-only helpers jsdom lacks.
 */
import * as React from 'react';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, render, screen, waitFor, within, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

const { bffApiCall, gridSpecs } = vi.hoisted(() => ({
  bffApiCall: vi.fn(),
  gridSpecs: {} as Record<
    string,
    { entityName: string; idAttribute: string; nameAttribute: string; rows: Array<Record<string, unknown>> }
  >,
}));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  // `getExternalUserContext` (the caller's `/me`, task 054) calls the module's own bffApiCall: route it to the seam too.
  return {
    ...actual,
    bffApiCall,
    bffApiBlob: vi.fn(),
    getExternalUserContext: () => bffApiCall('/api/v1/external/me'),
  };
});

// jsdom has no matchMedia; the widget's ambient-theme helper reads it. The modal inherits the test's provider instead.
vi.mock('@spaarke/ui-components/utils/themeStorage', () => ({
  resolveCodePageTheme: () => null,
  setupCodePageThemeListener: () => () => undefined,
}));
vi.mock('../src/services/gridDataverseClient', () => ({ gridDataverseClient: {} }));

// The shared grid: reports its rows through `onRecordsLoaded` and opens one through `onRecordOpen` when its primary-name
// button is clicked — the two callbacks the real grid fires (DataGrid.tsx).
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

import { ApiError } from '../src/types';
import DocumentsWidgetBody from '../src/widgets/DocumentsWidget';
import InvoicesWidgetBody from '../src/widgets/InvoicesWidget';
import MattersWidgetBody from '../src/widgets/MattersWidget';
import ProjectsWidgetBody from '../src/widgets/ProjectsWidget';
import ServiceRequestsWidgetBody from '../src/widgets/ServiceRequestsWidget';
import WorkAssignmentsWidgetBody from '../src/widgets/WorkAssignmentsWidget';
import { createGridWidgetBody } from '../src/widgets/GridWidgetBody';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const guid = (n: number): string => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;

const ID = {
  project: [guid(101), guid(102)],
  matter: [guid(201), guid(202), guid(203)],
  wa: [guid(301)],
  document: [guid(401)],
  invoice: [guid(501)],
  sr: [guid(601)],
};

/** One widget per entity: the widget body, its grid config id (as in the widget file), and the rows the grid shows. */
interface Case {
  label: string;
  Body: React.ComponentType<{ title: string }>;
  configId: string;
  entityName: string;
  idAttribute: string;
  nameAttribute: string;
  /** Grid rows. The grid shows its own row text; the detail's value comes from the READ, so it differs on purpose. */
  rowIds: string[];
  /** Detail title and one field value the read returns for the first row. */
  title: string;
  fieldTestId: string;
  fieldValue: string;
}

const CASES: Case[] = [
  {
    label: 'Projects',
    Body: ProjectsWidgetBody,
    configId: '61711823-1092-f111-b8dc-7ced8ddc4a05',
    entityName: 'sprk_project',
    idAttribute: 'sprk_projectid',
    nameAttribute: 'sprk_projectnumber',
    rowIds: ID.project,
    title: 'Project Read-Name',
    fieldTestId: 'record-detail-field-sprk_projectnumber',
    fieldValue: 'PRJ-READ-1',
  },
  {
    label: 'Matters',
    Body: MattersWidgetBody,
    configId: '583a2a33-1092-f111-b8dc-7ced8ddc4a05',
    entityName: 'sprk_matter',
    idAttribute: 'sprk_matterid',
    nameAttribute: 'sprk_matternumber',
    rowIds: ID.matter,
    title: 'Matter Read-Name',
    fieldTestId: 'record-detail-field-sprk_matternumber',
    fieldValue: 'MAT-READ-1',
  },
  {
    label: 'Work assignments',
    Body: WorkAssignmentsWidgetBody,
    configId: '42f4102c-1092-f111-b8dc-7ced8ddc4a05',
    entityName: 'sprk_workassignment',
    idAttribute: 'sprk_workassignmentid',
    nameAttribute: 'sprk_name',
    rowIds: ID.wa,
    title: 'WA Read-Name',
    fieldTestId: 'record-detail-field-sprk_workassignmentnumber',
    fieldValue: 'WA-READ-1',
  },
  {
    label: 'Documents',
    Body: DocumentsWidgetBody,
    configId: '3af4102c-1092-f111-b8dc-7ced8ddc4a05',
    entityName: 'sprk_document',
    idAttribute: 'sprk_documentid',
    nameAttribute: 'sprk_documentname',
    rowIds: ID.document,
    title: 'Document Read-Name',
    fieldTestId: 'record-detail-field-sprk_documenttype',
    fieldValue: 'Contract',
  },
  {
    label: 'Invoices',
    Body: InvoicesWidgetBody,
    configId: '3ff4102c-1092-f111-b8dc-7ced8ddc4a05',
    entityName: 'sprk_invoice',
    idAttribute: 'sprk_invoiceid',
    nameAttribute: 'sprk_name',
    rowIds: ID.invoice,
    title: 'INV-READ-1',
    fieldTestId: 'record-detail-field-statuscode',
    fieldValue: 'Approved',
  },
  {
    label: 'Service requests',
    Body: ServiceRequestsWidgetBody,
    configId: '403e5d37-cb94-f111-b8db-00224835447a',
    entityName: 'sprk_servicerequest',
    idAttribute: 'sprk_servicerequestid',
    nameAttribute: 'sprk_name',
    rowIds: ID.sr,
    title: 'SR Read-Name',
    fieldTestId: 'record-detail-field-sprk_servicerequestnumber',
    fieldValue: 'SR-READ-1',
  },
];

/** What the module read seam returns for each entity's detail read of the FIRST row (column → value). */
function detailRow(c: Case, id: string): Record<string, unknown> {
  const base: Record<string, Record<string, unknown>> = {
    sprk_project: { sprk_projectname: 'Project Read-Name', sprk_projectnumber: 'PRJ-READ-1' },
    sprk_matter: { sprk_mattername: 'Matter Read-Name', sprk_matternumber: 'MAT-READ-1' },
    sprk_workassignment: { sprk_name: 'WA Read-Name', sprk_workassignmentnumber: 'WA-READ-1' },
    sprk_document: { sprk_documentname: 'Document Read-Name', sprk_documenttype: 'Contract' },
    // ebilling-r1 task 015 (D-41): the invoice status is statuscode (Approved = 659490000), shown by its label.
    sprk_invoice: {
      sprk_invoicenumber: 'INV-READ-1',
      sprk_name: 'Invoice name',
      statuscode: 659490000,
      '@formattedValues': { statuscode: 'Approved' },
    },
    sprk_servicerequest: { sprk_name: 'SR Read-Name', sprk_servicerequestnumber: 'SR-READ-1' },
  };
  return { [c.idAttribute]: id, ...base[c.entityName] };
}

function setGrids(): void {
  for (const c of CASES) {
    gridSpecs[c.configId] = {
      entityName: c.entityName,
      idAttribute: c.idAttribute,
      nameAttribute: c.nameAttribute,
      // Grid text is deliberately different from the read's values: the detail must show what the READ returned.
      rows: c.rowIds.map((id, i) => ({ [c.idAttribute]: id, [c.nameAttribute]: `grid-row-${c.entityName}-${i + 1}` })),
    };
  }
}

interface Mocks {
  /** Entities whose detail read returns no row (the fetch seam's answer for a record outside the caller's set). */
  deny: Set<string>;
  /**
   * Rows the task 031 document routes return, keyed by `${root}:${recordId}` (root = projects | matters | workassignments |
   * servicerequests | invoices), in the DTO shape (`sprk_name`).
   */
  documents: Record<string, Array<Record<string, unknown>>>;
  /** Rows the invoices rollup (still the module fetch) returns, keyed by `${parentAttribute}:${parentId}`. */
  invoices: Record<string, Array<Record<string, unknown>>>;
  events: Array<Record<string, unknown>>;
  todos: Array<Record<string, unknown>>;
}

let mocks: Mocks;

function conditionOf(fetchXml: string): { attribute: string; value: string } {
  const m = /<condition attribute="([^"]+)" operator="eq" value="([^"]+)"\/>/.exec(fetchXml);
  if (!m) throw new Error(`no id/parent condition in ${fetchXml}`);
  return { attribute: m[1], value: m[2] };
}

/** A detail read is an id-filtered fetch on the entity's own id column; anything else is a rollup by parent lookup. */
function detailReadOf(body: { entityName: string; fetchXml: string }): { c: Case; id: string } | undefined {
  const c = CASES.find(x => x.entityName === body.entityName);
  if (!c) return undefined;
  const cond = conditionOf(body.fetchXml);
  return cond.attribute === c.idAttribute ? { c, id: cond.value } : undefined;
}

/**
 * The reads every detail view makes besides the record itself, with an empty answer: who the caller is (`/me`, task 054)
 * and the record's documents (the task 031 routes). `undefined` = not one of them.
 */
function sideReads(path: string): unknown {
  if (path === '/api/v1/external/me') return { contactId: guid(7), email: 'me@example.test', projects: [] };
  if (
    /^\/api\/v1\/external\/(projects|matters|workassignments|servicerequests|invoices)\/[^/]+\/documents$/.test(path)
  ) {
    return { value: [] };
  }
  return undefined;
}

function installNetwork(): void {
  bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
    const docs =
      /^\/api\/v1\/external\/(projects|matters|workassignments|servicerequests|invoices)\/([^/]+)\/documents$/.exec(
        path
      );
    if (docs) return { value: mocks.documents[`${docs[1]}:${docs[2]}`] ?? [] };
    if (path === '/api/v1/external/me') return sideReads(path);
    // POST /fetch  (every entity's detail by id, and the invoices rollup by parent lookup)
    if (path.endsWith('/api/dataverse/fetch')) {
      const body = JSON.parse(String(options?.body)) as { entityName: string; fetchXml: string };
      const read = detailReadOf(body);
      if (read) {
        return {
          entities: mocks.deny.has(read.c.entityName) ? [] : [detailRow(read.c, read.id)],
          moreRecords: false,
        };
      }
      const cond = conditionOf(body.fetchXml);
      const key = `${cond.attribute}:${cond.value}`;
      if (body.entityName === 'sprk_invoice') return { entities: mocks.invoices[key] ?? [], moreRecords: false };
    }
    if (/\/messages$/.test(path)) return { value: [] };
    if (/\/(projects|matters|workassignments)\/[^/]+\/events$/.test(path)) return { value: mocks.events };
    if (/\/(projects|matters|workassignments)\/[^/]+\/todos$/.test(path)) return { value: mocks.todos };
    throw new Error(`unexpected BFF call ${options?.method ?? 'GET'} ${path}`);
  });
}

/**
 * Installs a network where only the entity's detail read is custom: `read(id)` returns the row, `null` for "no row", or
 * throws. Every other fetch (the rollups) answers empty.
 */
function installDetailRead(entity: string, read: (id: string) => Record<string, unknown> | null): void {
  bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
    // The record route exists on the server but unwraps option sets to a bare integer and emits no `@formattedValues`
    // (RecordService.ProjectEntityToDictionary) — modelled here so a regression to it shows the raw integer.
    const rec = /\/api\/dataverse\/record\/([^/]+)\/([^/?]+)/.exec(path);
    if (rec) {
      if (rec[1] !== entity) throw new Error(`unexpected ${path}`);
      const { '@formattedValues': _dropped, ...unformatted } = read(rec[2]) ?? {};
      return unformatted;
    }
    if (path.endsWith('/messages')) return { value: [] };
    if (sideReads(path) !== undefined) return sideReads(path);
    if (!path.endsWith('/api/dataverse/fetch')) throw new Error(`unexpected ${path}`);
    const body = JSON.parse(String(options?.body)) as { entityName: string; fetchXml: string };
    const detail = detailReadOf(body);
    if (detail && detail.c.entityName === entity) {
      const row = read(detail.id);
      return { entities: row ? [row] : [], moreRecords: false };
    }
    return { entities: [], moreRecords: false };
  });
}

function renderWidget(Body: React.ComponentType<{ title: string }>): void {
  render(
    <FluentProvider theme={webLightTheme}>
      <Body title="widget" />
    </FluentProvider>
  );
}

/** The grid's row button for the n-th (1-based) row of a case. */
function gridRow(c: Case, n: number): HTMLElement {
  return screen.getByRole('button', { name: `grid-row-${c.entityName}-${n}` });
}

/** The modal's header title (the record's name also appears in its Name field, so a text query would be ambiguous). */
const titleOf = (dialog: HTMLElement): string => dialog.querySelector('[id^="sprk-modal-title-"]')?.textContent ?? '';

const caseOf = (entity: string): Case => CASES.find(c => c.entityName === entity)!;

beforeEach(() => {
  setGrids();
  mocks = { deny: new Set(), documents: {}, invoices: {}, events: [], todos: [] };
  bffApiCall.mockReset();
  installNetwork();
});

afterEach(() => {
  vi.restoreAllMocks();
});

// ---------------------------------------------------------------------------
// FR-01 — a row click in each of the six grids opens a read-only detail
// ---------------------------------------------------------------------------

describe.each(CASES)('$label grid', c => {
  it("opens the detail modal with the record's read-only fields when a row is clicked", async () => {
    const open = vi.spyOn(window, 'open').mockImplementation(() => null);
    renderWidget(c.Body);
    expect(screen.queryByRole('dialog')).toBeNull();

    fireEvent.click(gridRow(c, 1));

    const dialog = await screen.findByRole('dialog');
    // The title and the field value are what the READ returned (the grid row text differs on purpose).
    await waitFor(() => expect(titleOf(dialog)).toBe(c.title));
    expect(within(dialog).getByTestId(c.fieldTestId).textContent).toBe(c.fieldValue);

    // Read-only: the record itself has no input. The ONE input in the modal is the Messages compose box (task 051:
    // posting to the thread is not editing the record) - exactly one textbox, inside the Messages section, for an entity
    // with a thread and none at all for a document. Nothing is excluded from the scan, so a stray input anywhere fails.
    const inputs = Array.from(dialog.querySelectorAll('input, textarea, select, [role="textbox"]'));
    if (c.entityName === 'sprk_document') {
      expect(inputs).toHaveLength(0);
    } else {
      const messages = within(dialog).getByTestId('record-detail-messages');
      expect(within(messages).getAllByRole('textbox')).toHaveLength(1);
      expect(inputs).toHaveLength(1);
      expect(messages.contains(inputs[0])).toBe(true);
      expect(inputs[0].tagName).toBe('TEXTAREA');
    }
    expect(dialog.querySelectorAll('input[type="file"]')).toHaveLength(0);

    // In-app: no Xrm navigation and no new window — so it works the same in the browser host and the Teams host.
    expect(open).not.toHaveBeenCalled();
    expect((window as unknown as { Xrm?: unknown }).Xrm).toBeUndefined();
  });

  it('reads the record from the external plane only, through the one id-filtered module fetch', async () => {
    renderWidget(c.Body);
    fireEvent.click(gridRow(c, 1));
    await screen.findByTestId('record-detail-fields');

    const calls = bffApiCall.mock.calls.map(call => String(call[0]));
    expect(calls.length).toBeGreaterThan(0);
    for (const path of calls) expect(path.startsWith('/api/v1/external/')).toBe(true);
    // One read path for all six entities: the record route (which carries no `@formattedValues`) is never used.
    expect(calls.some(p => p.includes('/record/'))).toBe(false);
    expect(calls.some(p => p.endsWith('/api/v1/external/api/dataverse/fetch'))).toBe(true);
    const detailRead = bffApiCall.mock.calls
      .filter(call => String(call[0]).endsWith('/api/dataverse/fetch'))
      .map(call => JSON.parse(String((call[1] as RequestInit).body)) as { entityName: string; fetchXml: string })
      .find(b => b.entityName === c.entityName && b.fetchXml.includes(`attribute="${c.idAttribute}" operator="eq"`));
    expect(detailRead?.fetchXml).toContain(
      `<condition attribute="${c.idAttribute}" operator="eq" value="${c.rowIds[0]}"/>`
    );
  });

  it('shows the denied state and none of the record when the read is refused', async () => {
    mocks.deny.add(c.entityName);
    renderWidget(c.Body);
    fireEvent.click(gridRow(c, 1));

    const denied = await screen.findByTestId('record-detail-denied');
    expect(denied.textContent).toContain("You don't have access to this record");

    // Nothing of the record is on screen: no field block, no section, and neither the read's title nor its values.
    expect(screen.queryByTestId('record-detail-fields')).toBeNull();
    expect(screen.queryByTestId('record-detail-documents')).toBeNull();
    expect(screen.queryByTestId('record-detail-invoices')).toBeNull();
    expect(screen.queryByTestId('record-detail-events')).toBeNull();
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).queryByText(c.title)).toBeNull();
    expect(within(dialog).queryByText(c.fieldValue)).toBeNull();
    // …and the denied record triggered no rollup / events reads.
    const calls = bffApiCall.mock.calls.map(call => String(call[0]));
    expect(calls.some(p => p.endsWith('/events') || p.endsWith('/todos'))).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// FR-01 — 1-of-N browsing through the grid's rows
// ---------------------------------------------------------------------------

describe('browsing', () => {
  const matter = caseOf('sprk_matter');

  it('pages through the grid rows in order and updates the counter', async () => {
    // Each matter's read returns its own name so the paging is visible in the title.
    installDetailRead('sprk_matter', id => ({
      sprk_matterid: id,
      sprk_mattername: `Matter ${id.slice(-3)}`,
      sprk_matternumber: id,
    }));
    renderWidget(matter.Body);

    // Open the SECOND row: the counter starts at 2 of 3.
    fireEvent.click(gridRow(matter, 2));
    const dialog = await screen.findByRole('dialog');
    await waitFor(() => expect(titleOf(dialog)).toBe('Matter 202'));
    expect(within(dialog).getByText('2 of 3')).toBeTruthy();

    fireEvent.click(within(dialog).getByRole('button', { name: 'Next record' }));
    await waitFor(() => expect(titleOf(dialog)).toBe('Matter 203'));
    expect(within(dialog).getByText('3 of 3')).toBeTruthy();
    // Last row: Next is disabled.
    expect((within(dialog).getByRole('button', { name: 'Next record' }) as HTMLButtonElement).disabled).toBe(true);

    fireEvent.click(within(dialog).getByRole('button', { name: 'Previous record' }));
    await waitFor(() => expect(titleOf(dialog)).toBe('Matter 202'));
    fireEvent.click(within(dialog).getByRole('button', { name: 'Previous record' }));
    await waitFor(() => expect(titleOf(dialog)).toBe('Matter 201'));
    expect(within(dialog).getByText('1 of 3')).toBeTruthy();
    expect((within(dialog).getByRole('button', { name: 'Previous record' }) as HTMLButtonElement).disabled).toBe(true);

    // The record read followed the paging: 202 (open), 203, 202 again, 201.
    const readIds = bffApiCall.mock.calls
      .filter(call => String(call[0]).endsWith('/api/dataverse/fetch'))
      .map(call => JSON.parse(String((call[1] as RequestInit).body)) as { entityName: string; fetchXml: string })
      .filter(b => b.entityName === 'sprk_matter')
      .map(b => conditionOf(b.fetchXml).value);
    expect(readIds).toEqual([ID.matter[1], ID.matter[2], ID.matter[1], ID.matter[0]]);
  });

  it("shows a denied record's state while browsing, then the next accessible record", async () => {
    // The second matter is outside the caller's set: the fetch returns no row for it.
    installDetailRead('sprk_matter', id =>
      id === ID.matter[1] ? null : { sprk_matterid: id, sprk_mattername: `Matter ${id.slice(-3)}` }
    );
    renderWidget(matter.Body);

    fireEvent.click(gridRow(matter, 1));
    const dialog = await screen.findByRole('dialog');
    await waitFor(() => expect(titleOf(dialog)).toBe('Matter 201'));

    fireEvent.click(within(dialog).getByRole('button', { name: 'Next record' }));
    await within(dialog).findByTestId('record-detail-denied');
    // The previous record's data is gone while the denied one shows.
    expect(within(dialog).queryByText('Matter 201')).toBeNull();
    expect(within(dialog).getByText('2 of 3')).toBeTruthy();

    fireEvent.click(within(dialog).getByRole('button', { name: 'Next record' }));
    await waitFor(() => expect(titleOf(dialog)).toBe('Matter 203'));
    expect(within(dialog).queryByTestId('record-detail-denied')).toBeNull();
  });

  it('closes with the footer Close button', async () => {
    renderWidget(matter.Body);
    fireEvent.click(gridRow(matter, 1));
    const dialog = await screen.findByRole('dialog');
    fireEvent.click(within(dialog).getByText('Close'));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });

  describe('while a message is being sent (review 051 K3)', () => {
    let finishPost!: (v: unknown) => void;

    /** Opens matter 1 of 3 with a POST that stays in flight until `finishPost`, and starts a send. */
    async function startSend(): Promise<HTMLElement> {
      const inFlight = new Promise(resolve => {
        finishPost = resolve;
      });
      bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
        if (path.endsWith('/messages')) return options?.method === 'POST' ? inFlight : { value: [] };
        if (sideReads(path) !== undefined) return sideReads(path);
        const body = JSON.parse(String(options?.body)) as { entityName: string; fetchXml: string };
        const detail = detailReadOf(body);
        if (detail) {
          return {
            entities: [{ [detail.c.idAttribute]: detail.id, sprk_mattername: `Matter ${detail.id.slice(-3)}` }],
            moreRecords: false,
          };
        }
        return { entities: [], moreRecords: false };
      });
      renderWidget(matter.Body);
      fireEvent.click(gridRow(matter, 1));
      const dialog = await screen.findByRole('dialog');
      await waitFor(() => expect(titleOf(dialog)).toBe('Matter 201'));
      await within(dialog).findByTestId('record-messages-empty');
      fireEvent.change(within(dialog).getByRole('textbox', { name: 'Write a message' }), {
        target: { value: 'in flight' },
      });
      fireEvent.click(within(dialog).getByRole('button', { name: /^Send/ }));
      await waitFor(() => expect(within(dialog).getByRole('button', { name: /^Sending/ })).toBeTruthy());
      return dialog;
    }

    const matterReads = (): string[] =>
      bffApiCall.mock.calls
        .filter(call => String(call[0]).endsWith('/api/dataverse/fetch'))
        .map(call => JSON.parse(String((call[1] as RequestInit).body)) as { entityName: string; fetchXml: string })
        .filter(b => b.entityName === 'sprk_matter')
        .map(b => conditionOf(b.fetchXml).value);

    it('previous / next do not move to another record, and the footer Close is disabled', async () => {
      const dialog = await startSend();

      fireEvent.click(within(dialog).getByRole('button', { name: 'Next record' }));
      // The guard answers on a microtask; give it (and a would-be record read) the chance to run.
      await new Promise(resolve => setTimeout(resolve, 20));
      expect(within(dialog).getByText('1 of 3')).toBeTruthy();
      expect(titleOf(dialog)).toBe('Matter 201');
      expect(matterReads()).toEqual([ID.matter[0]]);
      expect(within(dialog).getByText('Close').closest('button')?.disabled).toBe(true);

      // The send is untouched and still settles in this same view.
      await act(async () => {
        finishPost({});
      });
      await waitFor(() => expect(within(dialog).queryByRole('button', { name: /^Sending/ })).toBeNull());
    });

    it('the x, Escape and the footer Close do not close the dialog', async () => {
      const dialog = await startSend();

      for (const close of within(dialog).getAllByRole('button', { name: 'Close' })) fireEvent.click(close);
      fireEvent.keyDown(dialog, { key: 'Escape', code: 'Escape' });
      fireEvent.keyDown(document.body, { key: 'Escape', code: 'Escape' });
      await new Promise(resolve => setTimeout(resolve, 20));
      expect(screen.queryByRole('dialog')).not.toBeNull();
      expect(within(dialog).getByRole('button', { name: /^Sending/ })).toBeTruthy();
    });

    it('once the send settles, navigation and Close work again', async () => {
      const dialog = await startSend();
      await act(async () => {
        finishPost({});
      });
      await waitFor(() => expect(within(dialog).queryByRole('button', { name: /^Sending/ })).toBeNull());

      expect(within(dialog).getByText('Close').closest('button')?.disabled).toBe(false);
      fireEvent.click(within(dialog).getByRole('button', { name: 'Next record' }));
      await waitFor(() => expect(titleOf(dialog)).toBe('Matter 202'));
      fireEvent.click(within(dialog).getByText('Close'));
      await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    });
  });
});

// ---------------------------------------------------------------------------
// FR-02 / FR-03 — sections
// ---------------------------------------------------------------------------

describe('sections', () => {
  it('Project: documents come from the project documents route and invoices from the module rollup; events and tasks are read-only', async () => {
    const c = caseOf('sprk_project');
    const projectId = ID.project[0];
    mocks.documents[`projects:${projectId}`] = [
      {
        sprk_documentid: guid(901),
        sprk_name: 'Engagement letter',
        sprk_documenttype: 'Letter',
        createdon: '2026-01-05T10:00:00Z',
      },
      {
        sprk_documentid: guid(902),
        sprk_name: 'Term sheet',
        sprk_documenttype: 'Sheet',
        createdon: '2026-01-06T10:00:00Z',
      },
    ];
    mocks.invoices[`sprk_project:${projectId}`] = [
      {
        sprk_invoiceid: guid(951),
        sprk_invoicenumber: 'INV-77',
        statuscode: 659490002,
        '@formattedValues': { statuscode: 'Paid' },
        sprk_totalamount: 1250,
      },
    ];
    mocks.events = [{ sprk_eventid: 'e-1', sprk_name: 'Signing call', sprk_duedate: '2026-03-01' }];
    mocks.todos = [{ sprk_todoid: 't-1', sprk_name: 'Review draft', statuscode: 1 }];

    renderWidget(c.Body);
    fireEvent.click(gridRow(c, 1));
    const dialog = await screen.findByRole('dialog');

    const docs = await within(dialog).findByTestId('record-detail-documents');
    await within(docs).findByText('Engagement letter');
    expect(within(docs).getByText('Term sheet')).toBeTruthy();
    const invoices = within(dialog).getByTestId('record-detail-invoices');
    await within(invoices).findByText('INV-77');
    // The invoice's status is its statuscode label (ebilling-r1 task 015, D-41).
    expect(within(invoices).getByText(/^Paid · /)).toBeTruthy();

    // Documents are the task 031 route (GET), never the module fetch; invoices are still the module rollup narrowed by
    // this record's lookup.
    const calls = bffApiCall.mock.calls.map(call => String(call[0]));
    expect(calls).toContain(`/api/v1/external/projects/${projectId}/documents`);
    const fetches = bffApiCall.mock.calls
      .filter(call => String(call[0]).endsWith('/api/dataverse/fetch'))
      .map(call => JSON.parse(String((call[1] as RequestInit).body)) as { entityName: string; fetchXml: string });
    expect(fetches.some(b => b.entityName === 'sprk_document')).toBe(false);
    const invoiceFetch = fetches.find(b => b.entityName === 'sprk_invoice');
    expect(invoiceFetch?.fetchXml).toContain(
      `<condition attribute="sprk_project" operator="eq" value="${projectId}"/>`
    );
    expect(invoiceFetch?.fetchXml).toContain('<attribute name="statuscode"/>');

    // Events and tasks, read-only: both lists show, and there is no create or toggle affordance.
    const events = within(dialog).getByTestId('record-detail-events');
    await within(events).findByText('Signing call');
    await within(events).findByText('Review draft');
    expect(within(dialog).queryByRole('button', { name: /create event/i })).toBeNull();
    expect(within(dialog).queryByRole('button', { name: /create new task|add task/i })).toBeNull();
  });

  it('Project: shows the empty states when there is nothing', async () => {
    const c = caseOf('sprk_project');
    renderWidget(c.Body);
    fireEvent.click(gridRow(c, 1));
    const dialog = await screen.findByRole('dialog');

    await within(within(dialog).getByTestId('record-detail-documents')).findByText('No Documents');
    expect(within(dialog).getByText('No invoices for this record.')).toBeTruthy();
    await within(dialog).findByText(/No events yet/);
    await within(dialog).findByText('No tasks yet');
  });

  it('Matter: documents come from the matter documents route, invoices roll up by the matter lookup; events/tasks come from the matter routes (task 057)', async () => {
    const c = caseOf('sprk_matter');
    const matterId = ID.matter[0];
    mocks.documents[`matters:${matterId}`] = [
      {
        sprk_documentid: guid(911),
        sprk_name: 'Matter memo',
        sprk_documenttype: 'Memo',
        createdon: '2026-02-01T10:00:00Z',
      },
    ];
    mocks.invoices[`sprk_matter:${matterId}`] = [
      { sprk_invoiceid: guid(961), sprk_name: 'Matter invoice', statuscode: 1, sprk_totalamount: 10 },
    ];
    renderWidget(c.Body);
    fireEvent.click(gridRow(c, 1));
    const dialog = await screen.findByRole('dialog');

    await within(await within(dialog).findByTestId('record-detail-documents')).findByText('Matter memo');
    // Invoice without a number falls back to its name.
    await within(await within(dialog).findByTestId('record-detail-invoices')).findByText('Matter invoice');

    const events = within(dialog).getByTestId('record-detail-events');
    await within(events).findByText(/No events yet/);
    await within(events).findByText('No tasks yet');
    // The static "no reads yet" empty state is gone; the matter's own event and to-do routes are read.
    expect(within(events).queryByTestId('record-detail-events-unavailable')).toBeNull();
    const calls = bffApiCall.mock.calls.map(call => String(call[0]));
    expect(calls).toContain(`/api/v1/external/matters/${matterId}/events`);
    expect(calls).toContain(`/api/v1/external/matters/${matterId}/todos`);
  });

  it('Work assignment: documents and the events section, but no invoices section (invoices have no work-assignment lookup)', async () => {
    const c = caseOf('sprk_workassignment');
    const waId = ID.wa[0];
    mocks.documents[`workassignments:${waId}`] = [
      {
        sprk_documentid: guid(921),
        sprk_name: 'WA brief',
        sprk_documenttype: 'Brief',
        createdon: '2026-02-02T10:00:00Z',
      },
    ];
    renderWidget(c.Body);
    fireEvent.click(gridRow(c, 1));
    const dialog = await screen.findByRole('dialog');

    await within(await within(dialog).findByTestId('record-detail-documents')).findByText('WA brief');
    expect(within(dialog).getByTestId('record-detail-events')).toBeTruthy();
    expect(within(dialog).queryByTestId('record-detail-invoices')).toBeNull();
    const invoiceFetches = bffApiCall.mock.calls.filter(
      call => String(call[0]).endsWith('/fetch') && String((call[1] as RequestInit).body).includes('sprk_invoice')
    );
    expect(invoiceFetches).toHaveLength(0);
  });

  it('sprk_document: no events section, and no documents or invoices section or read', async () => {
    const c = caseOf('sprk_document');
    renderWidget(c.Body);
    fireEvent.click(gridRow(c, 1));
    const dialog = await screen.findByRole('dialog');
    await within(dialog).findByTestId('record-detail-fields');

    expect(within(dialog).queryByTestId('record-detail-events')).toBeNull();
    expect(within(dialog).queryByTestId('record-detail-documents')).toBeNull();
    expect(within(dialog).queryByTestId('record-detail-invoices')).toBeNull();
    const calls = bffApiCall.mock.calls.map(call => String(call[0]));
    expect(calls.some(p => p.endsWith('/events') || p.endsWith('/todos') || p.endsWith('/documents'))).toBe(false);
  });

  it('sprk_invoice: documents from the invoices route (owner decision 2026-10-11); no events section, no invoices rollup', async () => {
    const c = caseOf('sprk_invoice');
    const invoiceId = ID.invoice[0];
    mocks.documents[`invoices:${invoiceId}`] = [
      { sprk_documentid: guid(941), sprk_name: 'Invoice PDF', createdon: '2026-02-03T10:00:00Z' },
    ];
    renderWidget(c.Body);
    fireEvent.click(gridRow(c, 1));
    const dialog = await screen.findByRole('dialog');
    await within(await within(dialog).findByTestId('record-detail-documents')).findByText('Invoice PDF');

    expect(within(dialog).queryByTestId('record-detail-events')).toBeNull();
    expect(within(dialog).queryByTestId('record-detail-invoices')).toBeNull();
    const calls = bffApiCall.mock.calls.map(call => String(call[0]));
    expect(calls).toContain(`/api/v1/external/invoices/${invoiceId}/documents`);
    // Never the module fetch for an invoice's documents.
    const moduleDocumentReads = bffApiCall.mock.calls
      .filter(call => String(call[0]).endsWith('/api/dataverse/fetch'))
      .map(call => JSON.parse(String((call[1] as RequestInit).body)) as { entityName: string })
      .filter(b => b.entityName === 'sprk_document');
    expect(moduleDocumentReads).toHaveLength(0);
    expect(calls.some(p => p.endsWith('/events') || p.endsWith('/todos'))).toBe(false);
  });

  it('sprk_servicerequest: documents from the servicerequests route only; no events section, no invoices rollup', async () => {
    const c = caseOf('sprk_servicerequest');
    const srId = ID.sr[0];
    mocks.documents[`servicerequests:${srId}`] = [
      { sprk_documentid: guid(931), sprk_name: 'Request attachment', createdon: '2026-02-03T10:00:00Z' },
    ];
    renderWidget(c.Body);
    fireEvent.click(gridRow(c, 1));
    const dialog = await screen.findByRole('dialog');

    await within(await within(dialog).findByTestId('record-detail-documents')).findByText('Request attachment');
    expect(within(dialog).queryByTestId('record-detail-events')).toBeNull();
    expect(within(dialog).queryByTestId('record-detail-invoices')).toBeNull();
    const calls = bffApiCall.mock.calls.map(call => String(call[0]));
    expect(calls).toContain(`/api/v1/external/servicerequests/${srId}/documents`);
    // Never the module fetch for a service request's documents (task 031 K3/K4), never an events/to-do read.
    const moduleDocumentReads = bffApiCall.mock.calls
      .filter(call => String(call[0]).endsWith('/api/dataverse/fetch'))
      .map(call => JSON.parse(String((call[1] as RequestInit).body)) as { entityName: string })
      .filter(b => b.entityName === 'sprk_document');
    expect(moduleDocumentReads).toHaveLength(0);
    expect(calls.some(p => p.endsWith('/events') || p.endsWith('/todos'))).toBe(false);
  });

  it.each(['sprk_project', 'sprk_matter', 'sprk_workassignment', 'sprk_invoice', 'sprk_servicerequest'])(
    '%s: shows the Messages section, read from that record thread on the external plane (task 051)',
    async entity => {
      const c = caseOf(entity);
      renderWidget(c.Body);
      fireEvent.click(gridRow(c, 1));
      const dialog = await screen.findByRole('dialog');
      await within(dialog).findByTestId('record-detail-messages');
      await within(dialog).findByTestId('record-messages-empty');

      const roots: Record<string, string> = {
        sprk_project: 'projects',
        sprk_matter: 'matters',
        sprk_workassignment: 'workassignments',
        sprk_invoice: 'invoices',
        sprk_servicerequest: 'servicerequests',
      };
      const root = roots[entity];
      const calls = bffApiCall.mock.calls.map(call => String(call[0]));
      expect(calls).toContain(`/api/v1/external/${root}/${c.rowIds[0]}/messages`);
    }
  );

  it('sprk_document: no Messages section and no thread read (a document has no thread)', async () => {
    const c = caseOf('sprk_document');
    renderWidget(c.Body);
    fireEvent.click(gridRow(c, 1));
    const dialog = await screen.findByRole('dialog');
    await within(dialog).findByTestId('record-detail-fields');

    expect(within(dialog).queryByTestId('record-detail-messages')).toBeNull();
    expect(bffApiCall.mock.calls.some(call => String(call[0]).endsWith('/messages'))).toBe(false);
  });

  it('says a rollup was cut short instead of presenting it as complete', async () => {
    const c = caseOf('sprk_matter');
    bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
      if (path.endsWith('/messages')) return { value: [] };
      if (path.endsWith('/documents'))
        return { value: [{ sprk_documentid: guid(1), sprk_name: 'Only one' }], truncated: true };
      if (sideReads(path) !== undefined) return sideReads(path);
      const body = JSON.parse(String(options?.body)) as { entityName: string; fetchXml: string };
      if (detailReadOf(body))
        return { entities: [{ sprk_matterid: guid(201), sprk_mattername: 'M' }], moreRecords: false };
      return { entities: [], moreRecords: false };
    });
    renderWidget(c.Body);
    fireEvent.click(gridRow(c, 1));
    const dialog = await screen.findByRole('dialog');
    await within(dialog).findByText('Only one');
    expect(within(dialog).getByTestId('truncated-list-notice').textContent).toContain('This list is incomplete');
  });

  it('says a rollup failed instead of showing an empty list', async () => {
    const c = caseOf('sprk_matter');
    bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
      if (path.endsWith('/messages')) return { value: [] };
      if (path.endsWith('/documents')) throw new ApiError(500, 'boom');
      if (sideReads(path) !== undefined) return sideReads(path);
      const body = JSON.parse(String(options?.body)) as { entityName: string; fetchXml: string };
      if (detailReadOf(body))
        return { entities: [{ sprk_matterid: guid(201), sprk_mattername: 'M' }], moreRecords: false };
      return { entities: [], moreRecords: false };
    });
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    renderWidget(c.Body);
    fireEvent.click(gridRow(c, 1));
    const dialog = await screen.findByRole('dialog');
    await within(dialog).findByText(/Failed to load documents/);
    expect(within(dialog).queryByText('No Documents')).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// Option-set columns show their LABEL (F1): the module fetch seam attaches `@formattedValues`; the record route did not
// ---------------------------------------------------------------------------

describe('option-set labels', () => {
  // The real fetch-seam shape: the raw option-set integer AND `@formattedValues[attribute]` = the label.
  const OPTION_CASES = [
    {
      entity: 'sprk_project',
      raw: { statuscode: 1 },
      labels: { statuscode: 'Active' },
      shown: [['record-detail-field-statuscode', 'Active', '1']],
    },
    {
      entity: 'sprk_matter',
      raw: { statuscode: 100000002 },
      labels: { statuscode: 'On hold' },
      shown: [['record-detail-field-statuscode', 'On hold', '100000002']],
    },
    {
      entity: 'sprk_workassignment',
      raw: { statuscode: 1, sprk_priority: 100000001 },
      labels: { statuscode: 'In progress', sprk_priority: 'High' },
      shown: [
        ['record-detail-field-statuscode', 'In progress', '1'],
        ['record-detail-field-sprk_priority', 'High', '100000001'],
      ],
    },
  ] as const;

  it.each(OPTION_CASES)('$entity: shows the formatted label, never the raw option-set integer', async oc => {
    const c = caseOf(oc.entity);
    installDetailRead(oc.entity, id => ({
      ...detailRow(c, id),
      ...oc.raw,
      '@formattedValues': oc.labels,
    }));
    renderWidget(c.Body);
    fireEvent.click(gridRow(c, 1));
    const dialog = await screen.findByRole('dialog');
    await within(dialog).findByTestId('record-detail-fields');

    for (const [testId, label, rawText] of oc.shown) {
      const text = within(dialog).getByTestId(testId).textContent;
      expect(text).toBe(label);
      expect(text).not.toBe(rawText);
    }
    expect(within(dialog).getByTestId('record-detail-fields').textContent).not.toMatch(/\b100000\d{3}\b/);
  });
});

// ---------------------------------------------------------------------------
// Negative cases
// ---------------------------------------------------------------------------

describe('negative cases', () => {
  it('an entity with no detail view keeps row-open a no-op (no modal, no BFF call)', () => {
    gridSpecs['cfg-unknown'] = {
      entityName: 'sprk_gridconfiguration',
      idAttribute: 'sprk_gridconfigurationid',
      nameAttribute: 'sprk_name',
      rows: [{ sprk_gridconfigurationid: guid(1), sprk_name: 'cfg row' }],
    };
    renderWidget(createGridWidgetBody('cfg-unknown'));
    fireEvent.click(screen.getByRole('button', { name: 'cfg row' }));

    expect(screen.queryByRole('dialog')).toBeNull();
    expect(bffApiCall).not.toHaveBeenCalled();
  });

  it('a row id that is not a GUID is never sent to the BFF or interpolated into FetchXML', async () => {
    gridSpecs['cfg-badid'] = {
      entityName: 'sprk_document',
      idAttribute: 'sprk_documentid',
      nameAttribute: 'sprk_documentname',
      rows: [{ sprk_documentid: '"/><injected/>', sprk_documentname: 'bad row' }],
    };
    renderWidget(createGridWidgetBody('cfg-badid'));
    fireEvent.click(screen.getByRole('button', { name: 'bad row' }));

    const error = await screen.findByTestId('record-detail-error');
    // The record read is never made, and the id appears in no call. The only BFF call the open dialog may make is the
    // caller's own identity read (`/me`, task 054: once per opening, it carries no record id).
    expect(bffApiCall.mock.calls.filter(c => c[0] !== '/api/v1/external/me')).toEqual([]);
    expect(JSON.stringify(bffApiCall.mock.calls)).not.toContain('injected');
    // Retrying cannot succeed for a malformed id, so no "Try again" is offered (K4).
    expect(within(error).queryByRole('button', { name: 'Try again' })).toBeNull();
  });

  it('a failed read (5xx) is an error with retry, not a denied state', async () => {
    const c = caseOf('sprk_matter');
    let fail = true;
    installDetailRead('sprk_matter', id => {
      if (fail) throw new ApiError(500, 'boom');
      return { sprk_matterid: id, sprk_mattername: 'Recovered' };
    });
    renderWidget(c.Body);
    fireEvent.click(gridRow(c, 1));
    const dialog = await screen.findByRole('dialog');

    await within(dialog).findByTestId('record-detail-error');
    expect(within(dialog).queryByTestId('record-detail-denied')).toBeNull();

    fail = false;
    fireEvent.click(within(dialog).getByRole('button', { name: 'Try again' }));
    await waitFor(() => expect(titleOf(dialog)).toBe('Recovered'));
  });

  it('a 403 from the fetch is the denied state, not an error', async () => {
    const c = caseOf('sprk_matter');
    installDetailRead('sprk_matter', () => {
      throw new ApiError(403, '{"errorCode":"DV_RECORD_NOT_ACCESSIBLE"}');
    });
    renderWidget(c.Body);
    fireEvent.click(gridRow(c, 1));
    await screen.findByTestId('record-detail-denied');
    expect(screen.queryByTestId('record-detail-error')).toBeNull();
    expect(screen.queryByTestId('record-detail-fields')).toBeNull();
  });

  it('a 404 says the record is no longer available', async () => {
    const c = caseOf('sprk_matter');
    installDetailRead('sprk_matter', () => {
      throw new ApiError(404, 'gone');
    });
    renderWidget(c.Body);
    fireEvent.click(gridRow(c, 1));
    await screen.findByTestId('record-detail-notfound');
  });
});
