/**
 * DataGrid.selectionCell.test — a grid configured with `selectionMode: 'none'` draws no selection checkboxes.
 *
 * Regression: Fluent renders an explicitly passed `selectionCell` slot even when the grid is not selectable, so every
 * `selectionMode: 'none'` grid showed dead "Select all rows" / "Select row" checkboxes (found by R4 task 023c on the
 * external record Documents grid). Control: the default (multi) still draws them.
 */
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { DataGrid } from '../DataGrid';
import type {
  IDataverseClient,
  EntityMetadata,
  FetchMultipleResult,
  SavedQueryResult,
} from '../../../services/IDataverseClient';
import type { DataGridConfiguration } from '../../../types/DataGridConfiguration';

const CONFIG_ID = '61711823-1092-f111-b8dc-7ced8ddc4a05';

const FETCH =
  "<fetch><entity name='sprk_thing'><attribute name='sprk_thingid'/><attribute name='sprk_name'/>" +
  "<order attribute='sprk_name' descending='false'/></entity></fetch>";
const LAYOUT =
  "<grid name='result' object='1' jump='sprk_name' select='1'><row name='result' id='sprk_thingid'>" +
  "<cell name='sprk_name' width='220'/></row></grid>";

const METADATA: EntityMetadata = {
  primaryIdAttribute: 'sprk_thingid',
  primaryNameAttribute: 'sprk_name',
  attributes: {
    sprk_thingid: { attributeType: 'String', isPrimaryId: true },
    sprk_name: { attributeType: 'String', isPrimaryName: true, displayName: 'Name' },
  },
};

function makeClient(config: DataGridConfiguration): IDataverseClient {
  return {
    retrieveRecord: jest.fn(async () => ({ sprk_configjson: JSON.stringify(config) })),
    retrieveSavedQuery: jest.fn(
      async (): Promise<SavedQueryResult> => ({
        entityName: 'sprk_thing',
        fetchXml: FETCH,
        layoutXml: LAYOUT,
        name: 'Things',
      })
    ),
    retrieveSavedQueriesForEntity: jest.fn(async () => []),
    retrieveEntityMetadata: jest.fn(async () => METADATA),
    retrieveMultipleRecords: jest.fn(
      async (): Promise<FetchMultipleResult> => ({
        entities: [
          { sprk_thingid: 't-1', sprk_name: 'First' },
          { sprk_thingid: 't-2', sprk_name: 'Second' },
        ],
        moreRecords: false,
      })
    ),
  } as unknown as IDataverseClient;
}

function renderGrid(selectionMode?: 'none' | 'single' | 'multi') {
  const config: DataGridConfiguration = {
    _version: '1.0',
    source: { type: 'inline', fetchXml: FETCH, layoutXml: LAYOUT },
    display: { title: 'Things' },
    ...(selectionMode ? { behavior: { selectionMode } } : {}),
  };
  return render(
    <FluentProvider theme={webLightTheme}>
      <DataGrid configId={CONFIG_ID} dataverseClient={makeClient(config)} showViewSelector={false} />
    </FluentProvider>
  );
}

describe('DataGrid — selection cell', () => {
  beforeEach(() => {
    jest.spyOn(console, 'info').mockImplementation(() => undefined);
  });
  afterEach(() => jest.restoreAllMocks());

  it("selectionMode 'none' draws no select-all or per-row checkbox", async () => {
    renderGrid('none');
    expect(await screen.findByText('First')).toBeInTheDocument();
    expect(screen.queryByLabelText('Select all rows')).not.toBeInTheDocument();
    expect(screen.queryAllByLabelText('Select row')).toHaveLength(0);
    expect(screen.queryAllByRole('checkbox')).toHaveLength(0);
  });

  it('control: the default (multi) still draws select-all and a checkbox per row', async () => {
    renderGrid();
    expect(await screen.findByText('First')).toBeInTheDocument();
    expect(screen.getByLabelText('Select all rows')).toBeInTheDocument();
    expect(screen.getAllByLabelText('Select row')).toHaveLength(2);
  });
});
