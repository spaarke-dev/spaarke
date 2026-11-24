/**
 * DataGrid.moneySort.render.test — the rendered grid (spaarke-SPA-external-access-platform-r4 task 033, FR-20,
 * #2446 / #2447). Money rows as the BFF module fetch sends them: `{ value: n, extensionData: null }` plus
 * `@formattedValues[column]`.
 *
 * Covers the two sort paths the grid has: Fluent's own uncontrolled sort (a header click) and the host-controlled sort
 * task 032 added (`initialSettings.sort`), and the Export to Excel command's CSV.
 *
 * Must-not-fire pair: a text column holding digits still sorts as text, and the export of a text column, a lookup and
 * an option set is exactly what it was.
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

function makeClient(rows: Array<Record<string, unknown>> = ROWS): IDataverseClient {
  return {
    retrieveRecord: jest.fn(async () => ({ sprk_configjson: JSON.stringify(CONFIG) })),
    retrieveSavedQueriesForEntity: jest.fn(async () => []),
    retrieveEntityMetadata: jest.fn(async () => METADATA),
    retrieveMultipleRecords: jest.fn(
      async (): Promise<FetchMultipleResult> => ({ entities: rows.map(r => ({ ...r })), moreRecords: false })
    ),
  } as unknown as IDataverseClient;
}

function renderGrid(initialSettings?: DataGridSettings, rows?: Array<Record<string, unknown>>) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <DataGrid
        configId={CONFIG_ID}
        dataverseClient={makeClient(rows)}
        showViewSelector={false}
        onRecordOpen={() => undefined}
        {...(initialSettings ? { initialSettings } : {})}
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

describe('DataGrid — Export to Excel writes the Money cell as displayed', () => {
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

  it('every other column exports as before (must-not-fire): text, lookup name, option set JSON', async () => {
    renderGrid();
    const lines = await exportedLines();

    // INV-ten: option set object with no .name/.label stays JSON (the label is NOT substituted, only Money changed).
    // That JSON is a known pre-existing defect, D-34 #3045; update these lines when it is fixed.
    expect(lines[1]).toBe('INV-ten,$10.00,"{""value"":1,""extensionData"":null}",10,"Smith, Jo"');
    expect(lines[2]).toBe('INV-nine,$9.00,"{""value"":1,""extensionData"":null}",9,Lee');
    expect(lines[4]).toBe('INV-none,,"{""value"":1,""extensionData"":null}",1000,');
  });
});
