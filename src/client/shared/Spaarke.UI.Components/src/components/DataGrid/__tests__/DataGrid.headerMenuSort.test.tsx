/**
 * DataGrid.headerMenuSort.test: the column menu's "A to Z" / "Z to A" items sort the grid
 * (spaarke-SPA-external-access-platform-r4 task 038 E2E finding, 2026-11-25).
 *
 * In a browser almost the whole header is the column-menu trigger, and it stops the click, so a click on a column name
 * opens the menu instead of sorting. Until this fix the menu's sort items were no-ops, so a user could sort only by
 * clicking the thin edge of the header cell. These tests drive the menu, which is what a user actually clicks. The
 * Money-sort tests click the header cell element itself, so they never saw this.
 *
 * Must-fire: the menu sorts on a host that does not opt in to persisted settings, and on one that does (the sort is
 * reported through `onSettingsChange`). Must-not-fire: Enter or Space on a column name opens its menu without sorting,
 * and a grid with sorting off offers no sort items and keeps the served order.
 */

import * as React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { DataGrid } from '../DataGrid';
import type { DataGridSettings } from '../index';
import type { IDataverseClient, EntityMetadata, FetchMultipleResult } from '../../../services/IDataverseClient';
import type { DataGridConfiguration } from '../../../types/DataGridConfiguration';

const CONFIG_ID = '1c4e0c33-0000-4000-8000-000000000038';

const FETCH =
  "<fetch><entity name='sprk_invoice'><attribute name='sprk_invoiceid'/><attribute name='sprk_name'/>" +
  "<attribute name='sprk_totalamount'/></entity></fetch>";
const LAYOUT =
  "<grid name='result' object='1' jump='sprk_name' select='1'><row name='result' id='sprk_invoiceid'>" +
  "<cell name='sprk_name' width='160'/><cell name='sprk_totalamount' width='140'/></row></grid>";

function configOf(enableSorting: boolean): DataGridConfiguration {
  return {
    _version: '1.0',
    source: { type: 'inline', fetchXml: FETCH, layoutXml: LAYOUT },
    display: { title: 'Invoices' },
    behavior: { selectionMode: 'none', enableSorting },
    commandBar: { showDefaultCommands: { newRecord: false, delete: false, refresh: false, editFilters: false } },
  };
}

const METADATA: EntityMetadata = {
  primaryIdAttribute: 'sprk_invoiceid',
  primaryNameAttribute: 'sprk_name',
  attributes: {
    sprk_invoiceid: { attributeType: 'String', isPrimaryId: true },
    sprk_name: { attributeType: 'String', isPrimaryName: true, displayName: 'Invoice' },
    sprk_totalamount: { attributeType: 'Money', displayName: 'Total amount' },
  },
};

const money = (n: number) => ({ value: n, extensionData: null });

/** Served $10, $9, $1,200, so neither sort direction matches the served order. */
const ROWS: Array<Record<string, unknown>> = [
  {
    sprk_invoiceid: 'i-10',
    sprk_name: 'INV-ten',
    sprk_totalamount: money(10),
    '@formattedValues': { sprk_totalamount: '$10.00' },
  },
  {
    sprk_invoiceid: 'i-9',
    sprk_name: 'INV-nine',
    sprk_totalamount: money(9),
    '@formattedValues': { sprk_totalamount: '$9.00' },
  },
  {
    sprk_invoiceid: 'i-1200',
    sprk_name: 'INV-big',
    sprk_totalamount: money(1200),
    '@formattedValues': { sprk_totalamount: '$1,200.00' },
  },
];

function makeClient(enableSorting: boolean): IDataverseClient {
  return {
    retrieveRecord: jest.fn(async () => ({ sprk_configjson: JSON.stringify(configOf(enableSorting)) })),
    retrieveSavedQueriesForEntity: jest.fn(async () => []),
    retrieveEntityMetadata: jest.fn(async () => METADATA),
    retrieveMultipleRecords: jest.fn(
      async (): Promise<FetchMultipleResult> => ({ entities: ROWS.map(r => ({ ...r })), moreRecords: false })
    ),
  } as unknown as IDataverseClient;
}

function renderGrid(options: { enableSorting?: boolean; onSettingsChange?: (s: DataGridSettings) => void } = {}) {
  const { enableSorting = true, onSettingsChange } = options;
  return render(
    <FluentProvider theme={webLightTheme}>
      <DataGrid
        configId={CONFIG_ID}
        dataverseClient={makeClient(enableSorting)}
        showViewSelector={false}
        onRecordOpen={() => undefined}
        {...(onSettingsChange ? { onSettingsChange } : {})}
      />
    </FluentProvider>
  );
}

const rowOrder = () => screen.getAllByText(/^INV-/).map(el => el.textContent);

const headerOf = (label: string) =>
  screen.getAllByRole('columnheader').find(h => h.textContent?.includes(label)) as HTMLElement;

/** Opens a column's menu the way a user does: a click on its column-options button (the column name). */
async function pickSort(label: string, item: 'A to Z' | 'Z to A') {
  fireEvent.click(screen.getByRole('button', { name: `${label} column options` }));
  fireEvent.click(await screen.findByRole('menuitem', { name: item }));
}

describe('DataGrid: the column menu sorts', () => {
  it('Z to A then A to Z order a Money column by value, without persisted settings', async () => {
    renderGrid();
    await screen.findByText('INV-ten');
    expect(rowOrder()).toEqual(['INV-ten', 'INV-nine', 'INV-big']); // served order

    await pickSort('Total amount', 'Z to A');
    expect(rowOrder()).toEqual(['INV-big', 'INV-ten', 'INV-nine']);
    expect(headerOf('Total amount').getAttribute('aria-sort')).toBe('descending');

    await pickSort('Total amount', 'A to Z');
    expect(rowOrder()).toEqual(['INV-nine', 'INV-ten', 'INV-big']);
    expect(headerOf('Total amount').getAttribute('aria-sort')).toBe('ascending');
  });

  it('a menu sort is reported to a host that opted in to persisted settings', async () => {
    const onSettingsChange = jest.fn();
    renderGrid({ onSettingsChange });
    await screen.findByText('INV-ten');

    await pickSort('Total amount', 'Z to A');
    expect(rowOrder()).toEqual(['INV-big', 'INV-ten', 'INV-nine']);
    expect(onSettingsChange).toHaveBeenLastCalledWith(
      expect.objectContaining({ sort: { columnId: 'sprk_totalamount', direction: 'descending' } })
    );
  });

  it('a click on the header cell itself still sorts, and the menu then sorts the other way', async () => {
    renderGrid();
    await screen.findByText('INV-ten');

    fireEvent.click(headerOf('Total amount'));
    expect(rowOrder()).toEqual(['INV-nine', 'INV-ten', 'INV-big']);

    await pickSort('Total amount', 'Z to A');
    expect(rowOrder()).toEqual(['INV-big', 'INV-ten', 'INV-nine']);
  });

  it('Enter or Space on a column name opens its menu without sorting the grid (must-not-fire)', async () => {
    const onSettingsChange = jest.fn();
    renderGrid({ onSettingsChange });
    await screen.findByText('INV-ten');
    const trigger = screen.getByRole('button', { name: 'Total amount column options' });
    trigger.focus();

    // Fluent's header button clicks itself on Enter (key down) and Space (key up); those used to re-sort the grid.
    fireEvent.keyDown(trigger, { key: 'Enter' });
    fireEvent.keyUp(trigger, { key: 'Enter' });
    fireEvent.keyDown(trigger, { key: ' ' });
    fireEvent.keyUp(trigger, { key: ' ' });

    expect(headerOf('Total amount').getAttribute('aria-sort')).toBe('none');
    expect(rowOrder()).toEqual(['INV-ten', 'INV-nine', 'INV-big']);
    expect(onSettingsChange).not.toHaveBeenCalled();
  });

  it('a grid with sorting off offers no sort items, and neither the menu nor a header click sorts (must-not-fire)', async () => {
    renderGrid({ enableSorting: false });
    await screen.findByText('INV-ten');

    // A column with no filter and no sort shows a plain label: no column-options button at all.
    expect(screen.queryByRole('button', { name: 'Total amount column options' })).toBeNull();
    expect(screen.queryByRole('menuitem', { name: 'A to Z' })).toBeNull();
    expect(screen.queryByRole('menuitem', { name: 'Z to A' })).toBeNull();

    fireEvent.click(headerOf('Total amount'));
    expect(headerOf('Total amount').getAttribute('aria-sort')).not.toBe('ascending');
    expect(rowOrder()).toEqual(['INV-ten', 'INV-nine', 'INV-big']);
  });
});
