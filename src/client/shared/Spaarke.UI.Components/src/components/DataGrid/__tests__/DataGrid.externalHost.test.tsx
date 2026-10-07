/**
 * DataGrid.externalHost.test — the external-SPA rule of the shared grid (unified-access-control-r2 task 157,
 * owner round 4 item 7, finding F1).
 *
 * Inside the external SPA the grid must show NO view picker and must NEVER request the entity's saved-query
 * list, whatever props reach it. The rule lives in DataGrid itself (DataGridExternalHost.tsx), switched on by
 * the provider the external SPA mounts at its root OR by the constant its Vite build defines. These tests
 * render the REAL DataGrid over a mock IDataverseClient and assert both halves of the rule for both switches,
 * with showViewSelector={true} passed directly (and host-owned externalViews), so the only thing that can keep
 * the picker off is the rule.
 *
 * Controls: the same grid with no external host shows the picker and requests the list, so a passing external
 * case is not a vacuous one (the test can see a picker when there is one).
 */
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { DataGrid } from '../DataGrid';
import { DataGridExternalHostProvider } from '../DataGridExternalHost';
import type {
  IDataverseClient,
  EntityMetadata,
  SavedQueryResult,
  SavedQuerySummary,
  FetchMultipleResult,
} from '../../../services/IDataverseClient';
import type { DataGridConfiguration } from '../../../types/DataGridConfiguration';

const CONFIG_ID = '61711823-1092-f111-b8dc-7ced8ddc4a05';
const OWN_VIEW_ID = '00000000-0000-0000-0000-0000000000aa';

const PROJECT_FETCH =
  "<fetch><entity name='sprk_project'><attribute name='sprk_projectid'/><attribute name='sprk_projectname'/>" +
  "<order attribute='sprk_projectname' descending='false'/></entity></fetch>";
const PROJECT_LAYOUT =
  "<grid name='result' object='1' jump='sprk_projectname' select='1'><row name='result' id='sprk_projectid'>" +
  "<cell name='sprk_projectname' width='220'/></row></grid>";

const INLINE_CONFIG: DataGridConfiguration = {
  _version: '1.0',
  source: { type: 'inline', fetchXml: PROJECT_FETCH, layoutXml: PROJECT_LAYOUT },
  display: { title: 'Projects' },
};

const SAVEDQUERY_CONFIG: DataGridConfiguration = {
  _version: '1.0',
  source: { type: 'savedquery', savedQueryId: OWN_VIEW_ID },
  display: { title: 'Projects' },
};

const SAVEDQUERY_SET_CONFIG: DataGridConfiguration = {
  _version: '1.0',
  source: { type: 'savedquery-set', entityLogicalName: 'sprk_project' },
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

/** Two INTERNAL MDA views of the entity: what the picker would offer outside counsel. */
const INTERNAL_VIEWS: SavedQuerySummary[] = [
  { id: '11111111-0000-0000-0000-000000000001', name: 'Active Projects', isDefault: true, queryType: 0 },
  { id: '11111111-0000-0000-0000-000000000002', name: 'All Projects (internal)', isDefault: false, queryType: 0 },
];

interface MockClient extends IDataverseClient {
  retrieveSavedQueriesForEntity: jest.Mock<Promise<SavedQuerySummary[]>, [string]>;
  retrieveSavedQuery: jest.Mock<Promise<SavedQueryResult>, [string]>;
}

function makeClient(config: DataGridConfiguration): MockClient {
  return {
    retrieveRecord: jest.fn(async () => ({
      sprk_configjson: JSON.stringify(config),
    })) as unknown as IDataverseClient['retrieveRecord'],
    retrieveSavedQuery: jest.fn(
      async (): Promise<SavedQueryResult> => ({
        entityName: 'sprk_project',
        fetchXml: PROJECT_FETCH,
        layoutXml: PROJECT_LAYOUT,
        name: 'Outside counsel projects',
      })
    ),
    retrieveSavedQueriesForEntity: jest.fn(async (): Promise<SavedQuerySummary[]> => INTERNAL_VIEWS),
    retrieveEntityMetadata: jest.fn(async (): Promise<EntityMetadata> => METADATA),
    retrieveMultipleRecords: jest.fn(
      async (): Promise<FetchMultipleResult> => ({
        entities: [{ sprk_projectid: 'p-1', sprk_projectname: 'Alpha diligence' }],
        moreRecords: false,
      })
    ),
  } as unknown as MockClient;
}

/** Host-owned views: also a picker, and also refused on the external host. */
const EXTERNAL_VIEWS = {
  views: [
    { id: 'cfg-a', name: 'Host view A' },
    { id: 'cfg-b', name: 'Host view B' },
  ],
  activeViewId: 'cfg-a',
  onViewChange: () => undefined,
};

function renderInFluent(ui: React.ReactElement) {
  return render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);
}

/** Waits until the grid finished loading (its row is on screen), so an absent picker is not just "not yet". */
async function waitForGridRows() {
  expect(await screen.findByText('Alpha diligence')).toBeInTheDocument();
}

const picker = () => screen.queryByRole('button', { name: /Select view/i });

const BUILD_FLAG = '__SPAARKE_DATAGRID_EXTERNAL_HOST__';

describe('DataGrid — external-SPA host rule (task 157)', () => {
  let consoleInfo: jest.SpyInstance;

  beforeEach(() => {
    consoleInfo = jest.spyOn(console, 'info').mockImplementation(() => undefined);
  });

  afterEach(() => {
    delete (globalThis as Record<string, unknown>)[BUILD_FLAG];
    consoleInfo.mockRestore();
  });

  it('control: with no external host the grid shows the picker and requests the saved-query list', async () => {
    const client = makeClient(INLINE_CONFIG);

    renderInFluent(<DataGrid configId={CONFIG_ID} dataverseClient={client} />);

    await waitForGridRows();
    expect(picker()).toBeInTheDocument();
    expect(client.retrieveSavedQueriesForEntity).toHaveBeenCalledWith('sprk_project');
  });

  it('under DataGridExternalHostProvider: no picker and no saved-query list, even with showViewSelector={true}', async () => {
    const client = makeClient(INLINE_CONFIG);

    renderInFluent(
      <DataGridExternalHostProvider>
        <DataGrid
          configId={CONFIG_ID}
          dataverseClient={client}
          showViewSelector={true}
          externalViews={EXTERNAL_VIEWS}
        />
      </DataGridExternalHostProvider>
    );

    await waitForGridRows();
    expect(picker()).not.toBeInTheDocument();
    expect(screen.queryByText('Host view A')).not.toBeInTheDocument();
    expect(client.retrieveSavedQueriesForEntity).not.toHaveBeenCalled();
  });

  it('under the provider at any depth (a wrapper element between the provider and the grid)', async () => {
    const client = makeClient(INLINE_CONFIG);
    const Wrapper: React.FC<{ children?: React.ReactNode }> = ({ children }) => <section>{children}</section>;

    renderInFluent(
      <DataGridExternalHostProvider>
        <Wrapper>
          {React.cloneElement(<DataGrid configId={CONFIG_ID} dataverseClient={client} />, { showViewSelector: true })}
        </Wrapper>
      </DataGridExternalHostProvider>
    );

    await waitForGridRows();
    expect(picker()).not.toBeInTheDocument();
    expect(client.retrieveSavedQueriesForEntity).not.toHaveBeenCalled();
  });

  it('under the external build constant (no provider): no picker and no saved-query list', async () => {
    (globalThis as Record<string, unknown>)[BUILD_FLAG] = true;
    const client = makeClient(INLINE_CONFIG);

    renderInFluent(<DataGrid configId={CONFIG_ID} dataverseClient={client} showViewSelector={true} />);

    await waitForGridRows();
    expect(picker()).not.toBeInTheDocument();
    expect(client.retrieveSavedQueriesForEntity).not.toHaveBeenCalled();
  });

  it("on the external host a savedquery source still loads the grid's OWN view, and no sibling list", async () => {
    const client = makeClient(SAVEDQUERY_CONFIG);

    renderInFluent(
      <DataGridExternalHostProvider>
        <DataGrid configId={CONFIG_ID} dataverseClient={client} showViewSelector={true} />
      </DataGridExternalHostProvider>
    );

    await waitForGridRows();
    expect(client.retrieveSavedQuery).toHaveBeenCalledTimes(1);
    expect(client.retrieveSavedQuery).toHaveBeenCalledWith(OWN_VIEW_ID);
    expect(client.retrieveSavedQueriesForEntity).not.toHaveBeenCalled();
    expect(picker()).not.toBeInTheDocument();
  });

  it('on the external host a savedquery-set source is refused (it would list the saved queries)', async () => {
    const client = makeClient(SAVEDQUERY_SET_CONFIG);

    renderInFluent(
      <DataGridExternalHostProvider>
        <DataGrid configId={CONFIG_ID} dataverseClient={client} />
      </DataGridExternalHostProvider>
    );

    expect(await screen.findByRole('alert')).toHaveTextContent(/savedquery-set source/);
    expect(client.retrieveSavedQueriesForEntity).not.toHaveBeenCalled();
    expect(client.retrieveSavedQuery).not.toHaveBeenCalled();
  });

  it('F1 (a): with the picker off on an ordinary host, the saved-query list is not requested', async () => {
    const client = makeClient(INLINE_CONFIG);

    renderInFluent(<DataGrid configId={CONFIG_ID} dataverseClient={client} showViewSelector={false} />);

    await waitForGridRows();
    expect(picker()).not.toBeInTheDocument();
    expect(client.retrieveSavedQueriesForEntity).not.toHaveBeenCalled();
  });
});
