/**
 * DataGrid.configLoadError.test — what an end user sees when the grid's configuration cannot be loaded
 * (spaarke-SPA-external-access-platform-r4 task 023c review F1).
 *
 * The banner is one plain sentence with a Try again button (which re-runs the configuration load). The technical reason
 * (a server message from a metadata read, "Cannot resolve entityName ...") is logged to the console and never shown.
 * Each must-fire has a must-not-fire pair: a grid that loads shows no banner and logs nothing.
 */
import * as React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { DataGrid } from '../DataGrid';
import type {
  IDataverseClient,
  EntityMetadata,
  FetchMultipleResult,
  SavedQueryResult,
} from '../../../services/IDataverseClient';
import type { DataGridConfiguration } from '../../../types/DataGridConfiguration';

const FETCH =
  "<fetch><entity name='sprk_project'><attribute name='sprk_projectid'/><attribute name='sprk_projectname'/></entity></fetch>";
const LAYOUT =
  "<grid name='result' object='1' jump='sprk_projectname' select='1'><row name='result' id='sprk_projectid'>" +
  "<cell name='sprk_projectname' width='220'/></row></grid>";
const CONFIG: DataGridConfiguration = {
  _version: '1.0',
  source: { type: 'inline', fetchXml: FETCH, layoutXml: LAYOUT },
  display: { title: 'Projects' },
};
const METADATA: EntityMetadata = {
  primaryIdAttribute: 'sprk_projectid',
  primaryNameAttribute: 'sprk_projectname',
  attributes: {
    sprk_projectid: { attributeType: 'String', isPrimaryId: true },
    sprk_projectname: { attributeType: 'String', isPrimaryName: true, displayName: 'Project' },
  },
};
const SERVER_DETAIL = 'Principal user (Id=9f1) is missing prvReadsprk_project privilege (Id=77c)';
const USER_TEXT = 'This list could not be loaded. Try again later.';

function makeClient(metadata: () => Promise<EntityMetadata>, config: DataGridConfiguration | null = CONFIG) {
  return {
    retrieveRecord: jest.fn(async () => {
      if (!config) throw new Error('not found');
      return { sprk_configjson: JSON.stringify(config) };
    }),
    retrieveSavedQuery: jest.fn(async (): Promise<SavedQueryResult> => {
      throw new Error('unused');
    }),
    retrieveSavedQueriesForEntity: jest.fn(async () => []),
    retrieveEntityMetadata: jest.fn(metadata),
    retrieveMultipleRecords: jest.fn(
      async (): Promise<FetchMultipleResult> => ({
        entities: [{ sprk_projectid: 'p-1', sprk_projectname: 'Alpha diligence' }],
        moreRecords: false,
      })
    ),
  } as unknown as IDataverseClient & {
    retrieveEntityMetadata: jest.Mock;
    retrieveRecord: jest.Mock;
  };
}

const renderGrid = (client: IDataverseClient) =>
  render(
    <FluentProvider theme={webLightTheme}>
      <DataGrid configId="cfg-1" dataverseClient={client} showViewSelector={false} />
    </FluentProvider>
  );

describe('DataGrid - configuration load failure', () => {
  let consoleError: jest.SpyInstance;
  let consoleWarn: jest.SpyInstance;
  let consoleInfo: jest.SpyInstance;
  beforeEach(() => {
    consoleError = jest.spyOn(console, 'error').mockImplementation(() => undefined);
    consoleWarn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    consoleInfo = jest.spyOn(console, 'info').mockImplementation(() => undefined);
  });
  afterEach(() => {
    consoleError.mockRestore();
    consoleWarn.mockRestore();
    consoleInfo.mockRestore();
  });

  it('a failed metadata read shows one user sentence, never the server detail, and logs the detail', async () => {
    const client = makeClient(async () => {
      throw new Error(SERVER_DETAIL);
    });
    renderGrid(client);

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(USER_TEXT);
    expect(document.body.textContent).not.toContain('prvRead');
    expect(document.body.textContent).not.toContain('Principal user');
    expect(consoleError).toHaveBeenCalledWith(
      expect.stringContaining('configuration did not load'),
      expect.objectContaining({ message: SERVER_DETAIL })
    );
  });

  it('an unresolvable source ("Cannot resolve entityName") says the same sentence, not the developer text', async () => {
    renderGrid(makeClient(async () => METADATA, null));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(USER_TEXT);
    expect(document.body.textContent).not.toContain('Cannot resolve entityName');
    expect(document.body.textContent).not.toContain('[DataGrid]');
    expect(consoleError).toHaveBeenCalledWith(
      expect.stringContaining('configuration did not load'),
      expect.objectContaining({ message: expect.stringContaining('Cannot resolve entityName') })
    );
    // The failed configuration read itself is logged too (it used to become "no config" silently).
    expect(consoleWarn).toHaveBeenCalledWith(expect.stringContaining('read failed'), expect.any(Error));
  });

  it('Try again re-runs the configuration load and the grid then draws', async () => {
    let attempts = 0;
    const client = makeClient(async () => {
      attempts += 1;
      if (attempts === 1) throw new Error(SERVER_DETAIL);
      return METADATA;
    });
    renderGrid(client);

    await screen.findByRole('alert');
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByText('Alpha diligence')).toBeInTheDocument();
    expect(client.retrieveEntityMetadata).toHaveBeenCalledTimes(2);
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('must-not-fire pair: a configuration that loads shows no banner, no Try again, and logs no failure', async () => {
    renderGrid(makeClient(async () => METADATA));

    expect(await screen.findByText('Alpha diligence')).toBeInTheDocument();
    expect(screen.queryByText(USER_TEXT)).toBeNull();
    expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
    expect(consoleError).not.toHaveBeenCalledWith(
      expect.stringContaining('configuration did not load'),
      expect.anything()
    );
    expect(consoleWarn).not.toHaveBeenCalledWith(expect.stringContaining('read failed'), expect.anything());
  });
});
