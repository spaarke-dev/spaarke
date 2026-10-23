/**
 * DataGrid.formattedValues.test — option-set / lookup labels from `@formattedValues`
 * (spaarke-SPA-external-access-platform-r3 task 061, owner check 2 issue 1).
 *
 * The BFF fetch seam returns raw SDK values (an option set arrives as `{ value: 1, extensionData: ... }`) plus a
 * `row['@formattedValues'][column]` display label. The grid must show the label, never the JSON. A row with no
 * label for the column renders exactly as before.
 *
 * Precedence for a cell: @formattedValues[column] (non-blank string) -> `.name` of an object -> primitive -> JSON.
 * Custom column renderers, the primary-name link and the date renderers are unchanged (a server-formatted date
 * would be in the server's culture and zone, and would undo the task 098 calendar-day rule).
 */
import * as React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { DataGrid, renderCellValue, formattedLabelOf } from '../DataGrid';
import type { IDataverseClient, EntityMetadata, FetchMultipleResult } from '../../../services/IDataverseClient';
import type { DataGridConfiguration } from '../../../types/DataGridConfiguration';

const CONFIG_ID = '403e5d37-0000-0000-0000-000000000001';

const FETCH =
  "<fetch><entity name='sprk_servicerequest'><attribute name='sprk_servicerequestid'/>" +
  "<attribute name='sprk_name'/><attribute name='statuscode'/><attribute name='sprk_requestedby'/>" +
  "<attribute name='sprk_note'/></entity></fetch>";
const LAYOUT =
  "<grid name='result' object='1' jump='sprk_name' select='1'><row name='result' id='sprk_servicerequestid'>" +
  "<cell name='sprk_name' width='200'/><cell name='statuscode' width='120'/>" +
  "<cell name='sprk_requestedby' width='160'/><cell name='sprk_note' width='160'/></row></grid>";

const CONFIG: DataGridConfiguration = {
  _version: '1.0',
  source: { type: 'inline', fetchXml: FETCH, layoutXml: LAYOUT },
  display: { title: 'Service Requests' },
};

const METADATA: EntityMetadata = {
  primaryIdAttribute: 'sprk_servicerequestid',
  primaryNameAttribute: 'sprk_name',
  attributes: {
    sprk_servicerequestid: { attributeType: 'String', isPrimaryId: true },
    sprk_name: { attributeType: 'String', isPrimaryName: true, displayName: 'Name' },
    // The real attribute types, so the columns resolve to the production renderers ('badge', 'link'), not 'default'.
    statuscode: { attributeType: 'Status', displayName: 'Status' },
    sprk_requestedby: { attributeType: 'Lookup', displayName: 'Requested by' },
    sprk_note: { attributeType: 'String', displayName: 'Note' },
  },
};

const OPTION_SET_JSON = { value: 1, extensionData: null };

function makeClient(entities: Array<Record<string, unknown>>): IDataverseClient {
  return {
    retrieveRecord: jest.fn(async () => ({
      sprk_configjson: JSON.stringify(CONFIG),
    })) as unknown as IDataverseClient['retrieveRecord'],
    retrieveSavedQueriesForEntity: jest.fn(async () => []),
    retrieveEntityMetadata: jest.fn(async (): Promise<EntityMetadata> => METADATA),
    retrieveMultipleRecords: jest.fn(async (): Promise<FetchMultipleResult> => ({ entities, moreRecords: false })),
  } as unknown as IDataverseClient;
}

function renderGrid(entities: Array<Record<string, unknown>>) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <DataGrid configId={CONFIG_ID} dataverseClient={makeClient(entities)} showViewSelector={false} />
    </FluentProvider>
  );
}

describe('formattedLabelOf', () => {
  it('returns the label for the column', () => {
    expect(formattedLabelOf({ '@formattedValues': { statuscode: 'Draft' } }, 'statuscode')).toBe('Draft');
  });

  it.each([
    ['no @formattedValues', {}],
    ['@formattedValues without the column', { '@formattedValues': { other: 'x' } }],
    ['a blank label', { '@formattedValues': { statuscode: '   ' } }],
    ['an empty label', { '@formattedValues': { statuscode: '' } }],
    ['a non-string label', { '@formattedValues': { statuscode: 7 } }],
    ['a null map', { '@formattedValues': null }],
  ])('returns undefined for %s', (_name, row) => {
    expect(formattedLabelOf(row as Record<string, unknown>, 'statuscode')).toBeUndefined();
  });
});

describe('renderCellValue — without a formatted label (unchanged)', () => {
  it('renders a lookup by name, a primitive as text, and an object as JSON', () => {
    expect(renderCellValue({ name: 'Acme', id: '1' }, 'default')).toBe('Acme');
    expect(renderCellValue(42, 'default')).toBe('42');
    expect(renderCellValue(OPTION_SET_JSON, 'default')).toBe(JSON.stringify(OPTION_SET_JSON));
    expect(renderCellValue(null, 'default')).toBe('');
  });
});

describe('renderCellValue — with a formatted label', () => {
  it('the label wins over the raw option-set object', () => {
    expect(renderCellValue(OPTION_SET_JSON, 'default', 'Draft')).toBe('Draft');
  });

  it('the label wins over a lookup .name', () => {
    expect(renderCellValue({ name: 'raw', id: '1' }, 'default', 'Formatted')).toBe('Formatted');
  });

  it('the label wins for a Money object under the currency renderer', () => {
    expect(renderCellValue({ value: 100 }, 'currency', '$100.00')).toBe('$100.00');
  });

  it.each(['date', 'dateonly', 'datetime'])('does not replace the %s renderer (stays client-formatted)', renderer => {
    const iso = '2026-10-02T12:00:00Z';
    expect(renderCellValue(iso, renderer, 'server string')).toBe(renderCellValue(iso, renderer));
  });

  it('does not replace the percentage renderer, or currency for a plain number (review K2)', () => {
    expect(renderCellValue(0.25, 'percentage', '0.25')).toBe('25%');
    expect(renderCellValue(100, 'currency', 'server')).toBe(renderCellValue(100, 'currency'));
  });

  it('an empty value stays empty', () => {
    expect(renderCellValue(null, 'default', 'Draft')).toBe('');
  });
});

describe('DataGrid — renders @formattedValues labels (task 061)', () => {
  it('shows "Draft" for a statuscode {value:1} row with @formattedValues.statuscode = "Draft", never JSON', async () => {
    const { container } = renderGrid([
      {
        sprk_servicerequestid: 'r-1',
        sprk_name: 'SR-0001',
        statuscode: OPTION_SET_JSON,
        sprk_requestedby: { id: 'c-1', name: 'raw lookup' },
        sprk_note: { value: 5 },
        '@formattedValues': { statuscode: 'Draft', sprk_requestedby: 'Contact One' },
      },
    ]);

    expect(await screen.findByText('Draft')).toBeInTheDocument();
    expect(screen.getByText('Contact One')).toBeInTheDocument();
    expect(screen.queryByText('raw lookup')).not.toBeInTheDocument();
    // Column with no label keeps the old rendering (JSON of the object); the labelled ones are not JSON.
    expect(screen.getByText('{"value":5}')).toBeInTheDocument();
    expect(container.textContent).not.toContain('extensionData');
  });

  it('a row without @formattedValues renders as before (lookup name, primitive, object JSON)', async () => {
    renderGrid([
      {
        sprk_servicerequestid: 'r-2',
        sprk_name: 'SR-0002',
        statuscode: OPTION_SET_JSON,
        sprk_requestedby: { id: 'c-2', name: 'Raw Lookup' },
        sprk_note: 'plain text',
      },
    ]);

    expect(await screen.findByText('Raw Lookup')).toBeInTheDocument();
    expect(screen.getByText('plain text')).toBeInTheDocument();
    expect(screen.getByText(JSON.stringify(OPTION_SET_JSON))).toBeInTheDocument();
  });

  it('sorting the Status column orders rows by the label (review F1: objects used to compare as "[object Object]")', async () => {
    const row = (id: string, label: string) => ({
      sprk_servicerequestid: id,
      sprk_name: `SR-${id}`,
      statuscode: { value: label.length, extensionData: null },
      sprk_requestedby: null,
      sprk_note: null,
      '@formattedValues': { statuscode: label },
    });
    renderGrid([row('1', 'Submitted'), row('2', 'Approved'), row('3', 'Draft')]);

    await screen.findByText('Submitted');
    const statusOrder = () => screen.getAllByText(/^(Submitted|Approved|Draft)$/).map(el => el.textContent);
    expect(statusOrder()).toEqual(['Submitted', 'Approved', 'Draft']);

    // The Fluent header cell sorts on its own click (the label inside it stops propagation, HeaderCellContent.tsx).
    const statusHeader = screen
      .getAllByRole('columnheader')
      .find(h => h.textContent?.includes('Status')) as HTMLElement;
    fireEvent.click(statusHeader);
    expect(statusOrder()).toEqual(['Approved', 'Draft', 'Submitted']);
  });
});
