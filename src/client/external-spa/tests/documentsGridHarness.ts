/**
 * documentsGridHarness — what a test needs to run the REAL shared grid under the record Documents list
 * (spaarke-SPA-external-access-platform-r4 task 023c).
 *
 * `DocumentLibrary` mounts `<ExternalDataGrid configId={RECORD_DOCUMENTS_GRID} rowSource=… rowActions=…>`. The grid loads its
 * configuration and entity metadata through `gridDataverseClient`, so a test substitutes that module with
 * `gridClientModule` (the seam, as every SPA grid test does). The configuration it serves is the COMMITTED file
 * `config/external-record-documents.gridconfiguration.json` (the one task 028 writes live), so a column, label, width or
 * `truncateWithTooltip` change in that file shows up in these tests. The client has no row query: `retrieveMultipleRecords`
 * records the query and throws, because the list's rows come from the host's `rowSource` (the record route), never from FetchXML.
 *
 *   vi.mock('../src/services/gridDataverseClient', async () => (await import('./documentsGridHarness')).gridClientModule());
 *
 * A test that already replaces `<ExternalDataGrid>` with a stand-in (the widget grids that open a record) lets the Documents
 * grid through to the real one with `realExternalDataGrid`.
 */
import { readFileSync } from 'node:fs';
import * as path from 'node:path';
import type { ComponentType } from 'react';
import { vi } from 'vitest';
import { screen, within } from '@testing-library/react';

/** The grid configuration id of the Documents list (the same value `DocumentLibrary` names; a test pins the two together). */
export const DOCUMENTS_GRID_ID = '286be8b4-f96f-4d5a-b1d5-82fd83ca9693';

/** The committed configuration file (repo root `config/`), exactly as task 028 writes it live. */
export const DOCUMENTS_GRID_FILE = JSON.parse(
  readFileSync(path.resolve(__dirname, '../../../../config/external-record-documents.gridconfiguration.json'), 'utf8')
) as { id: string; entity: string; configJson: Record<string, unknown> };

/** FetchXML queries the grid ran: none on a host-row-source grid. */
export const gridQueries: string[] = [];

/** A stand-in for the Dataverse client behind the grid: configuration + metadata, and no row query. */
export const documentsGridClient = {
  async retrieveRecord(entity: string, id: string): Promise<Record<string, unknown>> {
    if (entity === 'sprk_gridconfiguration' && id === DOCUMENTS_GRID_FILE.id) {
      return { sprk_configjson: JSON.stringify(DOCUMENTS_GRID_FILE.configJson) };
    }
    throw new Error(`unexpected retrieveRecord ${entity} ${id}`);
  },
  async retrieveSavedQuery(): Promise<never> {
    throw new Error('the documents grid has no saved query');
  },
  async retrieveSavedQueriesForEntity(): Promise<never[]> {
    return [];
  },
  async retrieveEntityMetadata(entity: string) {
    if (entity !== 'sprk_document') throw new Error(`unexpected entity metadata ${entity}`);
    return {
      primaryIdAttribute: 'sprk_documentid',
      primaryNameAttribute: 'sprk_documentname',
      attributes: {
        sprk_documentid: { attributeType: 'Uniqueidentifier', isPrimaryId: true },
        sprk_documentname: { attributeType: 'String', isPrimaryName: true, displayName: 'Document Name' },
        sprk_documenttype: { attributeType: 'String', displayName: 'Document Type' },
        createdon: { attributeType: 'DateTime', displayName: 'Created On' },
        sprk_summary: { attributeType: 'Memo', displayName: 'Summary' },
      },
    };
  },
  async retrieveMultipleRecords(_entity: string, fetchXml: string): Promise<never> {
    gridQueries.push(fetchXml);
    throw new Error('the documents grid must not run FetchXML: its rows come from the host row source');
  },
};

/** The module a test substitutes for `../src/services/gridDataverseClient`. */
export function gridClientModule(): { gridDataverseClient: typeof documentsGridClient } {
  return { gridDataverseClient: documentsGridClient };
}

/** The real `<ExternalDataGrid>` (for a stand-in that lets the Documents grid through). */
export async function realExternalDataGrid(): Promise<ComponentType<Record<string, unknown>>> {
  const actual = await vi.importActual<{ ExternalDataGrid: ComponentType<Record<string, unknown>> }>(
    '../src/widgets/ExternalDataGrid'
  );
  return actual.ExternalDataGrid;
}

/**
 * A stand-in for <ExternalDataGrid> that lets the Documents list's grid through to the real one and gives every other grid
 * (the widget grids that open a record) the stand-in. For vi.mock('../src/widgets/ExternalDataGrid', ...) factories:
 *   ExternalDataGrid: await documentsGridOr(props => ...stand-in...)
 */
export async function documentsGridOr<P extends { configId: string }>(
  Standin: ComponentType<P>
): Promise<ComponentType<P>> {
  const Real = await realExternalDataGrid();
  const R = await import('react');
  const Grid = (props: P) =>
    props.configId === DOCUMENTS_GRID_ID
      ? R.createElement(Real, props as unknown as Record<string, unknown>)
      : R.createElement(Standin, props);
  return Grid;
}

/** The grid body row of a document, by the name shown in its Name cell (waits for the grid to load and the row to draw). */
export async function documentRow(name: string): Promise<HTMLElement> {
  const link = await screen.findByRole('button', { name }, { timeout: 15000 });
  const row = link.closest('[role="row"]');
  if (!row) throw new Error(`"${name}" is not inside a grid row`);
  return row as HTMLElement;
}

/** A row action by its label (its tooltip equals its aria-label): `Download`, `Version history`, `Delete`. */
export const rowAction = (row: HTMLElement, label: string): HTMLElement | null =>
  within(row).queryByRole('button', { name: label });
