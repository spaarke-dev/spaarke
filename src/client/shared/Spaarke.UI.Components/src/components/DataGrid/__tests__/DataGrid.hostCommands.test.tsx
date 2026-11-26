/**
 * DataGrid.hostCommands.test — the grid threads `hostCommands` to its command bar.
 *
 * Renders the REAL DataGrid over a mock IDataverseClient. Control: with no `hostCommands` the toolbar has no
 * host button (so the positive case can see the difference). With `hostCommands` the button is in the grid's
 * toolbar, first, and invoking it calls the host's `onInvoke` (and the grid's `onCommandInvoke` observer, with the
 * host context). The grid's own config is untouched in both cases.
 */
import * as React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { DataGrid } from '../DataGrid';
import type { DataGridHostCommand } from '../index';
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

const CONFIG: DataGridConfiguration = {
  _version: '1.0',
  source: { type: 'inline', fetchXml: FETCH, layoutXml: LAYOUT },
  display: { title: 'Things' },
};

const METADATA: EntityMetadata = {
  primaryIdAttribute: 'sprk_thingid',
  primaryNameAttribute: 'sprk_name',
  attributes: {
    sprk_thingid: { attributeType: 'String', isPrimaryId: true },
    sprk_name: { attributeType: 'String', isPrimaryName: true, displayName: 'Name' },
  },
};

function makeClient(): IDataverseClient {
  return {
    retrieveRecord: jest.fn(async () => ({ sprk_configjson: JSON.stringify(CONFIG) })),
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
        entities: [{ sprk_thingid: 't-1', sprk_name: 'First thing' }],
        moreRecords: false,
      })
    ),
  } as unknown as IDataverseClient;
}

function renderGrid(extra: Partial<React.ComponentProps<typeof DataGrid>> = {}) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <DataGrid configId={CONFIG_ID} dataverseClient={makeClient()} showViewSelector={false} {...extra} />
    </FluentProvider>
  );
}

const toolbarButtons = () =>
  Array.from(screen.getByRole('toolbar', { name: 'Grid actions' }).querySelectorAll('button')).map(
    b => b.getAttribute('aria-label') ?? ''
  );

describe('DataGrid — hostCommands', () => {
  beforeEach(() => {
    jest.spyOn(console, 'info').mockImplementation(() => undefined);
  });
  afterEach(() => jest.restoreAllMocks());

  it('control: with no hostCommands the toolbar has no host button', async () => {
    renderGrid();
    expect(await screen.findByText('First thing')).toBeInTheDocument();
    expect(toolbarButtons()).not.toContain('Submit thing');
    expect(screen.queryByRole('button', { name: 'Submit thing' })).not.toBeInTheDocument();
  });

  it('with hostCommands the command is in the toolbar, first, and invoking it calls the host', async () => {
    const onInvoke = jest.fn();
    const onCommandInvoke = jest.fn();
    const hostCommands: DataGridHostCommand[] = [{ id: 'submit-thing', label: 'Submit thing', onInvoke }];
    renderGrid({ hostCommands, onCommandInvoke });
    expect(await screen.findByText('First thing')).toBeInTheDocument();

    expect(toolbarButtons()[0]).toBe('Submit thing');
    fireEvent.click(screen.getByRole('button', { name: 'Submit thing' }));

    await waitFor(() => expect(onInvoke).toHaveBeenCalledTimes(1));
    expect(onCommandInvoke).toHaveBeenCalledWith(
      'submit-thing',
      [],
      expect.objectContaining({ configId: CONFIG_ID, entityName: 'sprk_thing' })
    );
  });
});
