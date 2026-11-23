/**
 * DataGrid.emptyState.test — the HOST's empty state (`emptyState`), R4 task 024 (FR-17).
 *
 * Renders the REAL DataGrid with a host row source. Must-fire: a complete empty list shows the host's node in place of
 * the configuration's `display.emptyStateMessage`. Must-not-fire pairs: without the option the configuration's message
 * shows as before; a list with rows, a cut-short (truncated) empty list and a failed load show no empty state at all.
 */
import * as React from 'react';
import { act, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { DataGrid } from '../DataGrid';
import type { DataGridRowSource, DataGridRowSourceRequest, DataGridRowSourceResult } from '../index';
import type { IDataverseClient, EntityMetadata, SavedQueryResult } from '../../../services/IDataverseClient';
import type { DataGridConfiguration } from '../../../types/DataGridConfiguration';

const CONFIG_ID = '61711823-1092-f111-b8dc-7ced8ddc4a05';
const CONFIG_MESSAGE = 'Nothing configured here yet.';
const HOST_TEXT = 'Host empty state';

const FETCH =
  "<fetch><entity name='sprk_thing'><attribute name='sprk_thingid'/><attribute name='sprk_name'/>" +
  "<order attribute='sprk_name' descending='false'/></entity></fetch>";
const LAYOUT =
  "<grid name='result' object='1' jump='sprk_name' select='1'><row name='result' id='sprk_thingid'>" +
  "<cell name='sprk_name' width='220'/></row></grid>";

const CONFIG: DataGridConfiguration = {
  _version: '1.0',
  source: { type: 'inline', fetchXml: FETCH, layoutXml: LAYOUT },
  display: { title: 'Things', emptyStateMessage: CONFIG_MESSAGE },
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
    retrieveMultipleRecords: jest.fn(async () => ({ entities: [], moreRecords: false })),
  } as unknown as IDataverseClient;
}

class StubIntersectionObserver {
  observe() {}
  unobserve() {}
  disconnect() {}
}

function controlledSource() {
  const calls: Array<{
    req: DataGridRowSourceRequest;
    resolve: (r: DataGridRowSourceResult) => void;
    reject: (e: unknown) => void;
  }> = [];
  const source: DataGridRowSource = (req: DataGridRowSourceRequest) =>
    new Promise<DataGridRowSourceResult>((resolve, reject) => {
      calls.push({ req, resolve, reject });
    });
  return { source, calls };
}

type GridProps = React.ComponentProps<typeof DataGrid>;
function ui(extra: Partial<GridProps>) {
  return (
    <FluentProvider theme={webLightTheme}>
      <DataGrid configId={CONFIG_ID} dataverseClient={makeClient()} showViewSelector={false} {...extra} />
    </FluentProvider>
  );
}

const hostNode = <div data-testid="host-empty">{HOST_TEXT}</div>;

describe('DataGrid — host emptyState', () => {
  const originalIo = (globalThis as { IntersectionObserver?: unknown }).IntersectionObserver;
  beforeEach(() => {
    jest.spyOn(console, 'info').mockImplementation(() => undefined);
    (globalThis as { IntersectionObserver?: unknown }).IntersectionObserver = StubIntersectionObserver;
  });
  afterEach(() => {
    jest.restoreAllMocks();
    (globalThis as { IntersectionObserver?: unknown }).IntersectionObserver = originalIo;
  });

  it('a complete empty list shows the host node in place of the configuration message', async () => {
    const { source, calls } = controlledSource();
    render(ui({ rowSource: source, emptyState: hostNode }));
    await waitFor(() => expect(calls).toHaveLength(1));
    await act(async () => calls[0].resolve({ rows: [], hasMore: false }));
    expect(await screen.findByTestId('host-empty')).toHaveTextContent(HOST_TEXT);
    expect(screen.queryByText(CONFIG_MESSAGE)).toBeNull();
  });

  it('control: without the option the configuration message shows as before', async () => {
    const { source, calls } = controlledSource();
    render(ui({ rowSource: source }));
    await waitFor(() => expect(calls).toHaveLength(1));
    await act(async () => calls[0].resolve({ rows: [], hasMore: false }));
    expect(await screen.findByText(CONFIG_MESSAGE)).toBeInTheDocument();
    expect(screen.queryByTestId('host-empty')).toBeNull();
  });

  it('must-not-fire: a list with rows shows no empty state', async () => {
    const { source, calls } = controlledSource();
    render(ui({ rowSource: source, emptyState: hostNode }));
    await waitFor(() => expect(calls).toHaveLength(1));
    await act(async () => calls[0].resolve({ rows: [{ sprk_thingid: 'h-1', sprk_name: 'One' }], hasMore: false }));
    expect(await screen.findByText('One')).toBeInTheDocument();
    expect(screen.queryByTestId('host-empty')).toBeNull();
  });

  it('must-not-fire: a cut-short empty list shows the notice, not the empty state', async () => {
    const { source, calls } = controlledSource();
    render(ui({ rowSource: source, emptyState: hostNode }));
    await waitFor(() => expect(calls).toHaveLength(1));
    await act(async () => calls[0].resolve({ rows: [], hasMore: false, truncated: true }));
    expect(await screen.findByTestId('datagrid-truncated-notice')).toBeInTheDocument();
    expect(screen.queryByTestId('host-empty')).toBeNull();
  });

  it('regression: the empty state never mounts, not even for one render, before page 1 has answered', async () => {
    // Before the fix the render that turned the source on (before the effect that starts the read) counted as empty,
    // so the empty state mounted once and its live region announced "no records" while the list was still loading.
    let mounts = 0;
    const Probe: React.FC = () => {
      React.useEffect(() => {
        mounts += 1;
      }, []);
      return <div data-testid="host-empty">{HOST_TEXT}</div>;
    };
    const { source, calls } = controlledSource();
    render(ui({ rowSource: source, emptyState: <Probe /> }));
    await waitFor(() => expect(calls).toHaveLength(1));
    expect(mounts).toBe(0);
    // Must-fire pair: once page 1 answers with no rows, it mounts.
    await act(async () => calls[0].resolve({ rows: [], hasMore: false }));
    expect(await screen.findByTestId('host-empty')).toBeInTheDocument();
    expect(mounts).toBe(1);
  });

  it('must-not-fire: a failed load shows no empty state', async () => {
    jest.spyOn(console, 'error').mockImplementation(() => undefined);
    const { source, calls } = controlledSource();
    render(ui({ rowSource: source, emptyState: hostNode }));
    await waitFor(() => expect(calls).toHaveLength(1));
    await act(async () => calls[0].reject(new Error('Host read failed.')));
    expect(await screen.findByText('Host read failed.')).toBeInTheDocument();
    expect(screen.queryByTestId('host-empty')).toBeNull();
  });
});
