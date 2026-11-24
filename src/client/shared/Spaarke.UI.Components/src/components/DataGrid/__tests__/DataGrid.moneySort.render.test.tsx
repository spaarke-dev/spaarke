/**
 * DataGrid.moneySort.render.test — the rendered grid (spaarke-SPA-external-access-platform-r4 task 033, FR-20,
 * #2446 / #2447). Money rows as the BFF module fetch sends them: `{ value: n, extensionData: null }` plus
 * `@formattedValues[column]`.
 *
 * Covers the two sort paths the grid has: Fluent's own uncontrolled sort (a header click) and the host-controlled sort
 * task 032 added (`initialSettings.sort`), and the Export to Excel command's CSV.
 *
 * Must-not-fire pair: a text column holding digits still sorts as text, and the export of a text column and a lookup
 * name is exactly what it was. Every other column (an option set, a lookup with no name) exports the text the cell
 * shows (D-34, #3045), never JSON.
 */

// Node's Blob has `.arrayBuffer()`; jsdom's does not (same shim as commandBar/__tests__/csvExport.test.ts).
const { Blob: NodeBlob } = require('node:buffer');
const { TextDecoder: NodeTextDecoder } = require('node:util');
(globalThis as any).Blob = NodeBlob;
(globalThis as any).TextDecoder = NodeTextDecoder;

import * as React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { DataGrid, renderCellValue } from '../DataGrid';
import type { DataGridSettings } from '../index';
import type { IDataverseClient, EntityMetadata, FetchMultipleResult } from '../../../services/IDataverseClient';
import type { DataGridConfiguration } from '../../../types/DataGridConfiguration';

const CONFIG_ID = '1c4e0c33-0000-4000-8000-000000000033';

const FETCH =
  "<fetch><entity name='sprk_invoice'><attribute name='sprk_invoiceid'/><attribute name='sprk_name'/>" +
  "<attribute name='sprk_totalamount'/><attribute name='sprk_status'/><attribute name='sprk_ref'/>" +
  "<attribute name='sprk_owner'/></entity></fetch>";
const LAYOUT =
  "<grid name='result' object='1' jump='sprk_name' select='1'><row name='result' id='sprk_invoiceid'>" +
  "<cell name='sprk_name' width='160'/><cell name='sprk_totalamount' width='140'/>" +
  "<cell name='sprk_status' width='120'/><cell name='sprk_ref' width='120'/><cell name='sprk_owner' width='140'/>" +
  '</row></grid>';

const CONFIG: DataGridConfiguration = {
  _version: '1.0',
  source: { type: 'inline', fetchXml: FETCH, layoutXml: LAYOUT },
  display: { title: 'Invoices' },
  behavior: { selectionMode: 'none', enableSorting: true },
  // Only Export to Excel, so it is the one inline button.
  commandBar: { showDefaultCommands: { newRecord: false, delete: false, refresh: false, editFilters: false } },
};

const METADATA: EntityMetadata = {
  primaryIdAttribute: 'sprk_invoiceid',
  primaryNameAttribute: 'sprk_name',
  attributes: {
    sprk_invoiceid: { attributeType: 'String', isPrimaryId: true },
    sprk_name: { attributeType: 'String', isPrimaryName: true, displayName: 'Invoice' },
    sprk_totalamount: { attributeType: 'Money', displayName: 'Total amount' },
    sprk_status: { attributeType: 'Picklist', displayName: 'Status' },
    sprk_ref: { attributeType: 'String', displayName: 'Reference' },
    sprk_owner: { attributeType: 'Lookup', displayName: 'Owner' },
  },
};

const money = (n: number) => ({ value: n, extensionData: null });

/** Served $10, $9, $100 and one with no amount, so no sort direction matches the served order. */
const ROWS: Array<Record<string, unknown>> = [
  {
    sprk_invoiceid: 'i-10',
    sprk_name: 'INV-ten',
    sprk_totalamount: money(10),
    sprk_status: { value: 1, extensionData: null },
    sprk_ref: '10',
    sprk_owner: { id: 'c-1', name: 'Smith, Jo' },
    '@formattedValues': { sprk_totalamount: '$10.00', sprk_status: 'Draft' },
  },
  {
    sprk_invoiceid: 'i-9',
    sprk_name: 'INV-nine',
    sprk_totalamount: money(9),
    sprk_status: { value: 1, extensionData: null },
    sprk_ref: '9',
    sprk_owner: { id: 'c-2', name: 'Lee' },
    '@formattedValues': { sprk_totalamount: '$9.00', sprk_status: 'Draft' },
  },
  {
    sprk_invoiceid: 'i-100',
    sprk_name: 'INV-hundred',
    sprk_totalamount: money(1200),
    sprk_status: { value: 1, extensionData: null },
    sprk_ref: '100',
    sprk_owner: { id: 'c-3', name: 'Ng' },
    '@formattedValues': { sprk_totalamount: '$1,200.00', sprk_status: 'Draft' },
  },
  {
    sprk_invoiceid: 'i-none',
    sprk_name: 'INV-none',
    sprk_totalamount: null,
    sprk_status: { value: 1, extensionData: null },
    sprk_ref: '1000',
    sprk_owner: null,
    '@formattedValues': { sprk_status: 'Draft' },
  },
];

function makeClient(
  rows: Array<Record<string, unknown>> = ROWS,
  metadata: EntityMetadata = METADATA
): IDataverseClient {
  return {
    retrieveRecord: jest.fn(async () => ({ sprk_configjson: JSON.stringify(CONFIG) })),
    retrieveSavedQueriesForEntity: jest.fn(async () => []),
    retrieveEntityMetadata: jest.fn(async () => metadata),
    retrieveMultipleRecords: jest.fn(
      async (): Promise<FetchMultipleResult> => ({ entities: rows.map(r => ({ ...r })), moreRecords: false })
    ),
  } as unknown as IDataverseClient;
}

function renderGrid(
  initialSettings?: DataGridSettings,
  rows?: Array<Record<string, unknown>>,
  overrides?: React.ComponentProps<typeof DataGrid>['overrides'],
  metadata?: EntityMetadata
) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <DataGrid
        configId={CONFIG_ID}
        dataverseClient={makeClient(rows, metadata)}
        showViewSelector={false}
        onRecordOpen={() => undefined}
        {...(initialSettings ? { initialSettings } : {})}
        {...(overrides ? { overrides } : {})}
      />
    </FluentProvider>
  );
}

/** The invoice names in the order the rows are on screen. */
const rowOrder = () => screen.getAllByText(/^INV-/).map(el => el.textContent);

const headerOf = (label: string) =>
  screen.getAllByRole('columnheader').find(h => h.textContent?.includes(label)) as HTMLElement;

describe('DataGrid — Money column sort', () => {
  it("Fluent's own sort: a header click orders $9, $10, $1,200 by value, the next click reverses it", async () => {
    renderGrid();
    await screen.findByText('INV-ten');
    expect(rowOrder()).toEqual(['INV-ten', 'INV-nine', 'INV-hundred', 'INV-none']); // served order

    fireEvent.click(headerOf('Total amount'));
    // No amount sorts lowest, so it is first ascending.
    expect(rowOrder()).toEqual(['INV-none', 'INV-nine', 'INV-ten', 'INV-hundred']);

    fireEvent.click(headerOf('Total amount'));
    expect(rowOrder()).toEqual(['INV-hundred', 'INV-ten', 'INV-nine', 'INV-none']);
  });

  it('the host-controlled sort (initialSettings.sort) uses the same comparator, both directions', async () => {
    const asc = renderGrid({ sort: { columnId: 'sprk_totalamount', direction: 'ascending' } });
    await screen.findByText('INV-ten');
    expect(rowOrder()).toEqual(['INV-none', 'INV-nine', 'INV-ten', 'INV-hundred']);
    asc.unmount();

    renderGrid({ sort: { columnId: 'sprk_totalamount', direction: 'descending' } });
    await screen.findByText('INV-ten');
    expect(rowOrder()).toEqual(['INV-hundred', 'INV-ten', 'INV-nine', 'INV-none']);
  });

  it('a text column that holds digits still sorts as text (must-not-fire)', async () => {
    renderGrid();
    await screen.findByText('INV-ten');
    fireEvent.click(headerOf('Reference'));
    // '10' < '100' < '1000' < '9' as text; by value it would be 9, 10, 100, 1000.
    expect(rowOrder()).toEqual(['INV-ten', 'INV-hundred', 'INV-none', 'INV-nine']);
  });
});

describe('DataGrid — Export to Excel writes every cell as displayed', () => {
  let captured: Blob | undefined;
  const originalCreate = (URL as unknown as { createObjectURL?: unknown }).createObjectURL;
  const originalRevoke = (URL as unknown as { revokeObjectURL?: unknown }).revokeObjectURL;
  let clickSpy: jest.SpyInstance;

  beforeEach(() => {
    captured = undefined;
    (URL as unknown as { createObjectURL: (b: Blob) => string }).createObjectURL = (b: Blob) => {
      captured = b;
      return 'blob:test';
    };
    (URL as unknown as { revokeObjectURL: () => void }).revokeObjectURL = () => undefined;
    clickSpy = jest.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
  });

  afterEach(() => {
    clickSpy.mockRestore();
    (URL as unknown as { createObjectURL?: unknown }).createObjectURL = originalCreate;
    (URL as unknown as { revokeObjectURL?: unknown }).revokeObjectURL = originalRevoke;
  });

  async function exportedLines(): Promise<string[]> {
    await screen.findByText('INV-ten');
    fireEvent.click(await screen.findByRole('button', { name: /export to excel/i }));
    await waitFor(() => expect(captured).toBeDefined());
    const buf = await (captured as any).arrayBuffer();
    const text = new TextDecoder('utf-8', { ignoreBOM: true }).decode(buf);
    const body = text.charCodeAt(0) === 0xfeff ? text.slice(1) : text;
    return body.split('\r\n');
  }

  it('each Money cell exports its displayed text, equal to the cell on screen', async () => {
    renderGrid();
    const lines = await exportedLines();

    expect(lines[0]).toBe('Invoice,Total amount,Status,Reference,Owner');
    // "$1,200.00" holds a comma, so RFC 4180 quotes it. The unlabelled empty amount is empty, as its cell is.
    expect(lines[1].startsWith('INV-ten,$10.00,')).toBe(true);
    expect(lines[2].startsWith('INV-nine,$9.00,')).toBe(true);
    expect(lines[3].startsWith('INV-hundred,"$1,200.00",')).toBe(true);
    expect(lines[4].startsWith('INV-none,,')).toBe(true);

    // The text the cell shows on screen is the text in the file.
    for (const shown of ['$10.00', '$9.00', '$1,200.00']) {
      expect(screen.getByText(shown)).toBeInTheDocument();
    }
  });

  it('an unlabelled Money object shows the client currency format, never JSON, and exports the same text', async () => {
    const rows = [
      { sprk_invoiceid: 'i-raw', sprk_name: 'INV-raw', sprk_totalamount: { value: 1234.5, extensionData: null } },
    ];
    const { container } = renderGrid(undefined, rows);
    const shown = renderCellValue(1234.5, 'currency'); // the client format, e.g. $1,234.50
    expect(await screen.findByText(shown)).toBeInTheDocument();
    expect(container.textContent).not.toContain('extensionData');
    expect(container.textContent).not.toContain('[object Object]');

    fireEvent.click(await screen.findByRole('button', { name: /export to excel/i }));
    await waitFor(() => expect(captured).toBeDefined());
    const text = new TextDecoder('utf-8', { ignoreBOM: true }).decode(await (captured as any).arrayBuffer());
    const field = shown.includes(',') ? `"${shown}"` : shown;
    expect(text.split('\r\n')[1].startsWith(`INV-raw,${field}`)).toBe(true);
  });

  it('a labelled Money cell still shows its server label, not the client format (must-not-fire)', async () => {
    const rows = [
      {
        sprk_invoiceid: 'i-eur',
        sprk_name: 'INV-eur',
        sprk_totalamount: { value: 1234.5, extensionData: null },
        '@formattedValues': { sprk_totalamount: 'EUR 1.234,50' },
      },
    ];
    renderGrid(undefined, rows);
    expect(await screen.findByText('EUR 1.234,50')).toBeInTheDocument();
    expect(screen.queryByText(renderCellValue(1234.5, 'currency'))).not.toBeInTheDocument();
  });

  /** The CSV text of a grid rendered with these rows (and overrides), split into lines. */
  async function exportOf(
    rows: Array<Record<string, unknown>>,
    overrides?: React.ComponentProps<typeof DataGrid>['overrides'],
    metadata?: EntityMetadata
  ): Promise<{ lines: string[]; container: HTMLElement }> {
    const { container } = renderGrid(undefined, rows, overrides, metadata);
    await screen.findByText(String(rows[0].sprk_name));
    fireEvent.click(await screen.findByRole('button', { name: /export to excel/i }));
    await waitFor(() => expect(captured).toBeDefined());
    const text = new TextDecoder('utf-8', { ignoreBOM: true }).decode(await (captured as any).arrayBuffer());
    return { lines: (text.charCodeAt(0) === 0xfeff ? text.slice(1) : text).split('\r\n'), container };
  }

  it('an option set exports its formatted label; text and lookup names are as before; the cell shows the same', async () => {
    renderGrid();
    const lines = await exportedLines();

    // INV-ten: option set -> "Draft" (was the JSON of the SDK object, D-34 #3045); the lookup name "Smith, Jo" is
    // quoted for its comma; the text column is unchanged.
    expect(lines[1]).toBe('INV-ten,$10.00,Draft,10,"Smith, Jo"');
    expect(lines[2]).toBe('INV-nine,$9.00,Draft,9,Lee');
    expect(lines[3]).toBe('INV-hundred,"$1,200.00",Draft,100,Ng');
    expect(lines[4]).toBe('INV-none,,Draft,1000,');
    expect(screen.getAllByText('Draft')).toHaveLength(4); // the cell on screen is the text in the file
    expect(lines.join('')).not.toContain('extensionData');
  });

  it('an unlabelled option set and a nameless lookup export nothing, not JSON; the cells are empty too', async () => {
    const rows = [
      {
        sprk_invoiceid: 'i-1',
        sprk_name: 'INV-a',
        sprk_totalamount: null,
        sprk_status: { value: 7, extensionData: null }, // option set, no @formattedValues label
        sprk_ref: 'R-1',
        sprk_owner: { id: 'c-9' }, // lookup with no name and no label
      },
    ];
    const { lines, container } = await exportOf(rows);
    expect(lines[1]).toBe('INV-a,,,R-1,');
    expect(lines.join('')).not.toMatch(/value|extensionData|\[object/);
    expect(container.textContent).not.toContain('extensionData');
    expect(container.textContent).not.toContain('[object Object]');
  });

  it('a lookup with a name exports the name; with a label too, the label (the grid shows the label)', async () => {
    const rows = [
      {
        sprk_invoiceid: 'i-1',
        sprk_name: 'INV-a',
        sprk_status: { value: 1, extensionData: null },
        sprk_ref: 'R-1',
        sprk_owner: { id: 'c-1', name: 'Smith, Jo' },
        '@formattedValues': { sprk_status: 'Active' },
      },
      {
        sprk_invoiceid: 'i-2',
        sprk_name: 'INV-b',
        sprk_status: { value: 1, extensionData: null },
        sprk_ref: 'R-2',
        sprk_owner: { id: 'c-2', name: 'raw name' },
        '@formattedValues': { sprk_status: 'Active', sprk_owner: 'Jo Smith (formatted)' },
      },
    ];
    const { lines } = await exportOf(rows);
    expect(lines[1]).toBe('INV-a,,Active,R-1,"Smith, Jo"');
    expect(lines[2]).toBe('INV-b,,Active,R-2,Jo Smith (formatted)');
  });

  it('must-not-fire: text and number columns export exactly as before', async () => {
    const rows = [
      {
        sprk_invoiceid: 'i-1',
        sprk_name: 'INV-a',
        sprk_totalamount: 100, // a plain number under the Money renderer keeps its client currency format
        sprk_status: { value: 1, extensionData: null },
        sprk_ref: 'say "hi", ok',
        sprk_owner: null,
        '@formattedValues': { sprk_status: 'Draft' },
      },
      {
        sprk_invoiceid: 'i-2',
        sprk_name: 'INV-b',
        sprk_totalamount: null,
        sprk_status: { value: 1, extensionData: null },
        sprk_ref: 42, // a number in a text column
        sprk_owner: null,
        '@formattedValues': { sprk_status: 'Draft' },
      },
    ];
    const { lines } = await exportOf(rows);
    const shown = renderCellValue(100, 'currency');
    const money = shown.includes(',') ? `"${shown}"` : shown;
    expect(lines[1]).toBe(`INV-a,${money},Draft,"say ""hi"", ok",`);
    expect(lines[2]).toBe('INV-b,,Draft,42,');
  });

  // F1 (review): a column the host draws with overrides.columnRenderers is exported as what that renderer returns when it
  // returns text, as the cell shows it. The Invoices widget does this for sprk_reviewdetail (an option set the BFF sends as
  // `{ value, extensionData }`), through `reviewDetailCellText`, which also applies the statuscode gate.
  it('a host renderer that returns a string exports that string (the cell text), not the option-set JSON', async () => {
    const rows = [
      {
        sprk_invoiceid: 'i-1',
        sprk_name: 'INV-a',
        sprk_status: { value: 659490000, extensionData: null },
        statuscode: 1,
        sprk_ref: 'R-1',
        sprk_owner: { id: 'c-1', name: 'Lee' },
      },
      {
        // Gate closed: the renderer returns '' for this row, so the field is empty, not the raw option set.
        sprk_invoiceid: 'i-2',
        sprk_name: 'INV-b',
        sprk_status: { value: 659490000, extensionData: null },
        statuscode: 2,
        sprk_ref: 'R-2',
        sprk_owner: null,
      },
    ];
    const seen: Array<[unknown, Record<string, unknown>]> = [];
    const { lines, container } = await exportOf(rows, {
      columnRenderers: {
        sprk_status: (value, row) => {
          seen.push([value, row]);
          return row.statuscode === 1 ? 'Waiting for billing terms' : '';
        },
      },
    });
    expect(lines[1]).toBe('INV-a,,Waiting for billing terms,R-1,Lee');
    expect(lines[2]).toBe('INV-b,,,R-2,');
    expect(lines.join('')).not.toContain('extensionData');
    // The cell on screen is the text in the file.
    expect(container.textContent).toContain('Waiting for billing terms');
    // Called with the arguments the cell render uses: the cell's value and its record.
    expect(
      seen.some(([value, row]) => (value as { value: number }).value === 659490000 && row.sprk_name === 'INV-a')
    ).toBe(true);
  });

  it('must-not-fire: a host renderer that returns an element keeps the raw export of a primitive value', async () => {
    const rows = [
      {
        sprk_invoiceid: 'i-1',
        sprk_name: 'INV-a',
        sprk_status: { value: 1, extensionData: null },
        sprk_ref: 'R-1', // the column the host draws as an element: a primitive
        sprk_owner: { id: 'c-1', name: 'Lee' },
        '@formattedValues': { sprk_status: 'Draft' },
      },
    ];
    const { lines } = await exportOf(rows, {
      columnRenderers: { sprk_ref: v => React.createElement('em', null, String(v)) },
    });
    expect(lines[1]).toBe('INV-a,,Draft,R-1,Lee');
  });

  it('a host renderer that returns an element for an object value exports its label or name, never JSON', async () => {
    const rows = [
      {
        sprk_invoiceid: 'i-1',
        sprk_name: 'INV-a',
        sprk_status: { value: 1, extensionData: null },
        sprk_ref: 'R-1',
        sprk_owner: { id: 'c-1', name: 'Lee' },
        '@formattedValues': { sprk_status: 'Draft' },
      },
      {
        sprk_invoiceid: 'i-2',
        sprk_name: 'INV-b',
        sprk_status: { value: 2, extensionData: null }, // no label
        sprk_ref: 'R-2',
        sprk_owner: { id: 'c-2' },
      },
    ];
    const { lines } = await exportOf(rows, {
      columnRenderers: {
        sprk_status: () => React.createElement('em', null, 'custom'),
        sprk_owner: () => React.createElement('em', null, 'owner'),
      },
    });
    expect(lines[1]).toBe('INV-a,,Draft,R-1,Lee');
    expect(lines[2]).toBe('INV-b,,,R-2,');
    expect(lines.join('')).not.toContain('extensionData');
  });

  // Owner decision 2026-11-24: a date column exports the date the grid shows, not the raw UTC timestamp.
  describe('a date column', () => {
    const AT = '2026-10-02T12:00:00Z';
    const withRefAs = (attr: Record<string, unknown>): EntityMetadata => ({
      ...METADATA,
      attributes: { ...METADATA.attributes, sprk_ref: { displayName: 'Reference', ...attr } as never },
    });
    const row = (ref: unknown) => [
      {
        sprk_invoiceid: 'i-1',
        sprk_name: 'INV-a',
        sprk_status: { value: 1, extensionData: null },
        sprk_ref: ref,
        sprk_owner: null,
        '@formattedValues': { sprk_status: 'Draft', sprk_ref: 'server label' },
      },
    ];

    it('a date-time exports the local date and time on screen, not the UTC timestamp or the server label', async () => {
      const shown = new Date(AT).toLocaleString();
      const { lines, container } = await exportOf(row(AT), undefined, withRefAs({ attributeType: 'DateTime' }));
      const cell = shown.includes(',') ? `"${shown}"` : shown;
      expect(lines[1]).toBe(`INV-a,,Draft,${cell},`);
      expect(lines.join('')).not.toContain(AT);
      expect(container.textContent).toContain(shown); // the cell on screen is the text in the file
    });

    it('a Date Only value exports its calendar day', async () => {
      const shown = new Date(2026, 9, 2).toLocaleDateString();
      const { lines } = await exportOf(
        row('2026-10-02'),
        undefined,
        withRefAs({ attributeType: 'DateTime', dateTimeBehavior: 'DateOnly' })
      );
      expect(lines[1]).toBe(`INV-a,,Draft,${shown.includes(',') ? `"${shown}"` : shown},`);
    });

    it('a host renderer that returns an element for a date column still exports the date the grid would draw', async () => {
      const shown = new Date(AT).toLocaleString();
      const { lines } = await exportOf(
        row(AT),
        { columnRenderers: { sprk_ref: () => React.createElement('em', null, 'when') } },
        withRefAs({ attributeType: 'DateTime' })
      );
      expect(lines[1]).toBe(`INV-a,,Draft,${shown.includes(',') ? `"${shown}"` : shown},`);
    });
  });

  it('text that a spreadsheet would evaluate is written with a leading apostrophe; the cell on screen is unchanged', async () => {
    const rows = [
      {
        sprk_invoiceid: 'i-1',
        sprk_name: 'INV-a',
        sprk_status: { value: 1, extensionData: null },
        sprk_ref: '=1+1',
        sprk_owner: { id: 'c-1', name: '@Lee' },
        '@formattedValues': { sprk_status: 'Draft' },
      },
    ];
    const { lines, container } = await exportOf(rows);
    expect(lines[1]).toBe("INV-a,,Draft,'=1+1,'@Lee");
    expect(container.textContent).toContain('=1+1');
    expect(container.textContent).not.toContain("'=1+1");
  });
});
