/**
 * DataGrid.rowSource.test — the grid takes its rows from a HOST page loader (`rowSource`), R4 task 023a.
 *
 * Renders the REAL DataGrid over a mock IDataverseClient. Each must-fire case has its must-not-fire pair:
 *  - without `rowSource` the FetchXML path runs as before (retrieveMultipleRecords, membership resolver);
 *  - with it, the host's rows show under the configuration's columns, retrieveMultipleRecords and the membership
 *    resolver are never called, pages append on the sentinel, `rowSourceKey` reloads page 1 (aborting the old
 *    request), a rejected page shows the error banner, `truncated` shows the notice, and unmount aborts.
 */
import * as React from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { DataGrid } from '../DataGrid';
import type { DataGridRowSource, DataGridRowSourceRequest, DataGridRowSourceResult } from '../index';
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
  "<attribute name='sprk_kind'/><order attribute='sprk_name' descending='false'/></entity></fetch>";
const LAYOUT =
  "<grid name='result' object='1' jump='sprk_name' select='1'><row name='result' id='sprk_thingid'>" +
  "<cell name='sprk_name' width='220'/><cell name='sprk_kind' width='120'/></row></grid>";

function configWith(behavior: DataGridConfiguration['behavior'] = undefined): DataGridConfiguration {
  return {
    _version: '1.0',
    source: { type: 'inline', fetchXml: FETCH, layoutXml: LAYOUT },
    display: { title: 'Things' },
    ...(behavior ? { behavior } : {}),
  };
}

const METADATA: EntityMetadata = {
  primaryIdAttribute: 'sprk_thingid',
  primaryNameAttribute: 'sprk_name',
  attributes: {
    sprk_thingid: { attributeType: 'String', isPrimaryId: true },
    sprk_name: { attributeType: 'String', isPrimaryName: true, displayName: 'Name' },
    sprk_kind: { attributeType: 'String', displayName: 'Kind' },
  },
};

function makeClient(config: DataGridConfiguration = configWith()): IDataverseClient {
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
        entities: [{ sprk_thingid: 'f-1', sprk_name: 'Fetched thing', sprk_kind: 'Server' }],
        moreRecords: false,
      })
    ),
  } as unknown as IDataverseClient;
}

// ── IntersectionObserver stub: jsdom has none. `intersect()` fires every live observer as if its sentinel showed. ──
type IoCallback = (entries: Array<{ isIntersecting: boolean }>) => void;
const liveObservers = new Set<{ cb: IoCallback }>();
class StubIntersectionObserver {
  private readonly entry: { cb: IoCallback };
  constructor(cb: IoCallback) {
    this.entry = { cb };
  }
  observe() {
    liveObservers.add(this.entry);
  }
  unobserve() {
    liveObservers.delete(this.entry);
  }
  disconnect() {
    liveObservers.delete(this.entry);
  }
}
function intersect() {
  act(() => {
    for (const o of Array.from(liveObservers)) o.cb([{ isIntersecting: true }]);
  });
}

const row = (id: string, name: string, kind = 'Host') => ({ sprk_thingid: id, sprk_name: name, sprk_kind: kind });

/** A row source whose pages the test resolves by hand. */
function controlledSource() {
  const calls: Array<{
    req: DataGridRowSourceRequest;
    resolve: (r: DataGridRowSourceResult) => void;
    reject: (e: unknown) => void;
  }> = [];
  const source: jest.MockedFunction<DataGridRowSource> = jest.fn(
    (req: DataGridRowSourceRequest) =>
      new Promise<DataGridRowSourceResult>((resolve, reject) => {
        calls.push({ req, resolve, reject });
      })
  );
  return { source, calls };
}

type GridProps = React.ComponentProps<typeof DataGrid>;
function ui(client: IDataverseClient, extra: Partial<GridProps> = {}) {
  return (
    <FluentProvider theme={webLightTheme}>
      <DataGrid configId={CONFIG_ID} dataverseClient={client} showViewSelector={false} {...extra} />
    </FluentProvider>
  );
}

describe('DataGrid — rowSource', () => {
  const originalIo = (globalThis as { IntersectionObserver?: unknown }).IntersectionObserver;
  beforeEach(() => {
    jest.spyOn(console, 'info').mockImplementation(() => undefined);
    (globalThis as { IntersectionObserver?: unknown }).IntersectionObserver = StubIntersectionObserver;
    liveObservers.clear();
  });
  afterEach(() => {
    jest.restoreAllMocks();
    (globalThis as { IntersectionObserver?: unknown }).IntersectionObserver = originalIo;
  });

  it('control: without rowSource the FetchXML path runs as before', async () => {
    const client = makeClient();
    render(ui(client));
    expect(await screen.findByText('Fetched thing')).toBeInTheDocument();
    expect(client.retrieveMultipleRecords).toHaveBeenCalledTimes(1);
    const [entity, xml] = (client.retrieveMultipleRecords as jest.Mock).mock.calls[0];
    expect(entity).toBe('sprk_thing');
    expect(xml).toContain('page="1"');
    expect(xml).toContain('count="25"');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.queryByTestId('datagrid-truncated-notice')).not.toBeInTheDocument();
  });

  it("with rowSource the host's rows show under the configured columns and no FetchXML query runs", async () => {
    const client = makeClient();
    const source: jest.MockedFunction<DataGridRowSource> = jest.fn(async () => ({
      rows: [row('h-1', 'Host thing', 'Memo')],
      hasMore: false,
    }));
    render(ui(client, { rowSource: source }));

    expect(await screen.findByText('Host thing')).toBeInTheDocument();
    expect(screen.getByText('Memo')).toBeInTheDocument();
    // Columns still come from the configuration + metadata.
    expect(screen.getByText('Name')).toBeInTheDocument();
    expect(screen.getByText('Kind')).toBeInTheDocument();
    expect(client.retrieveEntityMetadata).toHaveBeenCalledWith('sprk_thing');

    expect(client.retrieveMultipleRecords).not.toHaveBeenCalled();
    expect(screen.queryByText('Fetched thing')).not.toBeInTheDocument();
    expect(source).toHaveBeenCalledTimes(1);
    const req = source.mock.calls[0][0];
    expect(req.page).toBe(1);
    expect(req.pageSize).toBe(25);
    expect(req.signal).toBeInstanceOf(AbortSignal);
    expect(screen.getByText(/Rows: 1/)).toBeInTheDocument();
  });

  it('pages: the sentinel asks for page 2 while hasMore, and the rows are appended', async () => {
    const { source, calls } = controlledSource();
    render(ui(makeClient(), { rowSource: source, pageSize: 1 }));
    await waitFor(() => expect(calls).toHaveLength(1));
    await act(async () => calls[0].resolve({ rows: [row('h-1', 'Page one')], hasMore: true }));
    expect(await screen.findByText('Page one')).toBeInTheDocument();
    expect(screen.getByText(/Rows: 1\+/)).toBeInTheDocument();

    intersect();
    await waitFor(() => expect(calls).toHaveLength(2));
    expect(calls[1].req).toEqual(expect.objectContaining({ page: 2, pageSize: 1 }));
    await act(async () => calls[1].resolve({ rows: [row('h-2', 'Page two')], hasMore: false }));

    expect(await screen.findByText('Page two')).toBeInTheDocument();
    expect(screen.getByText('Page one')).toBeInTheDocument();
    expect(screen.getByText(/Rows: 2/)).toBeInTheDocument();
    // No further page once hasMore is false.
    intersect();
    expect(calls).toHaveLength(2);
  });

  it('rowSourceKey change reloads page 1 and aborts the request in flight; a new inline callback alone does not', async () => {
    const { source, calls } = controlledSource();
    const client = makeClient();
    const { rerender } = render(ui(client, { rowSource: source, rowSourceKey: 'a' }));
    await waitFor(() => expect(calls).toHaveLength(1));
    await act(async () => calls[0].resolve({ rows: [row('h-1', 'Before')], hasMore: true }));
    expect(await screen.findByText('Before')).toBeInTheDocument();

    // Page 2 in flight...
    intersect();
    await waitFor(() => expect(calls).toHaveLength(2));

    // ...a different callback identity with the SAME key does not reload.
    const wrapped: DataGridRowSource = req => source(req);
    rerender(ui(client, { rowSource: wrapped, rowSourceKey: 'a' }));
    expect(calls).toHaveLength(2);
    expect(calls[1].req.signal.aborted).toBe(false);

    // A new key aborts page 2 and reloads page 1.
    rerender(ui(client, { rowSource: wrapped, rowSourceKey: 'b' }));
    await waitFor(() => expect(calls).toHaveLength(3));
    expect(calls[1].req.signal.aborted).toBe(true);
    expect(calls[2].req.page).toBe(1);
    // The aborted page's late answer is ignored.
    await act(async () => calls[1].resolve({ rows: [row('h-x', 'Stale')], hasMore: false }));
    await act(async () => calls[2].resolve({ rows: [row('h-2', 'After')], hasMore: false }));
    expect(await screen.findByText('After')).toBeInTheDocument();
    expect(screen.queryByText('Before')).not.toBeInTheDocument();
    expect(screen.queryByText('Stale')).not.toBeInTheDocument();
    expect(client.retrieveMultipleRecords).not.toHaveBeenCalled();
  });

  it('a rejected page shows the error banner and stops paging', async () => {
    const { source, calls } = controlledSource();
    render(ui(makeClient(), { rowSource: source }));
    await waitFor(() => expect(calls).toHaveLength(1));
    await act(async () => calls[0].reject(new Error('The list could not be read.')));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('The list could not be read.');
    // The grid itself (headers) stays.
    expect(screen.getByText('Name')).toBeInTheDocument();
    intersect();
    expect(calls).toHaveLength(1);
  });

  it('truncated: true shows the notice; truncated absent shows none', async () => {
    const { source, calls } = controlledSource();
    const client = makeClient();
    const { rerender } = render(ui(client, { rowSource: source, rowSourceKey: '1' }));
    await waitFor(() => expect(calls).toHaveLength(1));
    await act(async () => calls[0].resolve({ rows: [row('h-1', 'Only one')], hasMore: false }));
    expect(await screen.findByText('Only one')).toBeInTheDocument();
    expect(screen.queryByTestId('datagrid-truncated-notice')).not.toBeInTheDocument();

    rerender(ui(client, { rowSource: source, rowSourceKey: '2' }));
    await waitFor(() => expect(calls).toHaveLength(2));
    await act(async () => calls[1].resolve({ rows: [row('h-1', 'Only one')], hasMore: false, truncated: true }));
    const notice = await screen.findByTestId('datagrid-truncated-notice');
    // One row: the noun is singular; the "not listed" sentence names the plural.
    expect(notice).toHaveTextContent(
      'This list is incomplete: only 1 row could be shown. Some rows are not listed here.'
    );
    expect(notice).not.toHaveTextContent('1 rows');
  });

  it('truncated notice: several rows use the plural; zero rows say the list could not be loaded', async () => {
    const { source, calls } = controlledSource();
    render(ui(makeClient(), { rowSource: source }));
    await waitFor(() => expect(calls).toHaveLength(1));
    await act(async () =>
      calls[0].resolve({ rows: [row('h-1', 'One'), row('h-2', 'Two')], hasMore: false, truncated: true })
    );
    const notice = await screen.findByTestId('datagrid-truncated-notice');
    expect(notice).toHaveTextContent(
      'This list is incomplete: only 2 rows could be shown. Some rows are not listed here.'
    );
  });

  it('truncated notice with rowNoun: the custom noun is used, singular for 1 and plural otherwise', async () => {
    const noun = { one: 'document', other: 'documents' };
    const first = controlledSource();
    const client = makeClient();
    const { rerender } = render(ui(client, { rowSource: first.source, rowSourceKey: 'a', rowNoun: noun }));
    await waitFor(() => expect(first.calls).toHaveLength(1));
    await act(async () => first.calls[0].resolve({ rows: [row('h-1', 'One')], hasMore: false, truncated: true }));
    expect(await screen.findByTestId('datagrid-truncated-notice')).toHaveTextContent(
      'This list is incomplete: only 1 document could be shown. Some documents are not listed here.'
    );

    rerender(ui(client, { rowSource: first.source, rowSourceKey: 'b', rowNoun: noun }));
    await waitFor(() => expect(first.calls).toHaveLength(2));
    await act(async () =>
      first.calls[1].resolve({ rows: [row('h-1', 'One'), row('h-2', 'Two')], hasMore: false, truncated: true })
    );
    await waitFor(() =>
      expect(screen.getByTestId('datagrid-truncated-notice')).toHaveTextContent(
        'This list is incomplete: only 2 documents could be shown. Some documents are not listed here.'
      )
    );
    // Must-not-fire: the default noun does not leak through.
    expect(screen.getByTestId('datagrid-truncated-notice')).not.toHaveTextContent(/\brows?\b/);

    // Nothing read at all.
    rerender(ui(client, { rowSource: first.source, rowSourceKey: 'c', rowNoun: noun }));
    await waitFor(() => expect(first.calls).toHaveLength(3));
    await act(async () => first.calls[2].resolve({ rows: [], hasMore: false, truncated: true }));
    await waitFor(() =>
      expect(screen.getByTestId('datagrid-truncated-notice')).toHaveTextContent(
        'The documents could not be loaded just now, so none are shown.'
      )
    );
  });

  it('a page with zero rows ends paging even when hasMore is true', async () => {
    const { source, calls } = controlledSource();
    render(ui(makeClient(), { rowSource: source, pageSize: 1 }));
    await waitFor(() => expect(calls).toHaveLength(1));
    await act(async () => calls[0].resolve({ rows: [row('h-1', 'Page one')], hasMore: true }));
    expect(await screen.findByText('Page one')).toBeInTheDocument();

    intersect();
    await waitFor(() => expect(calls).toHaveLength(2));
    await act(async () => calls[1].resolve({ rows: [], hasMore: true }));
    await waitFor(() => expect(screen.getByText(/Rows: 1(?!\+)/)).toBeInTheDocument());

    intersect();
    intersect();
    expect(calls).toHaveLength(2);
  });

  it('control: a non-empty page with hasMore keeps paging', async () => {
    const { source, calls } = controlledSource();
    render(ui(makeClient(), { rowSource: source, pageSize: 1 }));
    await waitFor(() => expect(calls).toHaveLength(1));
    await act(async () => calls[0].resolve({ rows: [row('h-1', 'Page one')], hasMore: true }));
    expect(await screen.findByText('Page one')).toBeInTheDocument();
    intersect();
    await waitFor(() => expect(calls).toHaveLength(2));
    await act(async () => calls[1].resolve({ rows: [row('h-2', 'Page two')], hasMore: true }));
    expect(await screen.findByText('Page two')).toBeInTheDocument();
    intersect();
    await waitFor(() => expect(calls).toHaveLength(3));
  });

  it('unmount aborts the request in flight', async () => {
    const { source, calls } = controlledSource();
    const { unmount } = render(ui(makeClient(), { rowSource: source }));
    await waitFor(() => expect(calls).toHaveLength(1));
    expect(calls[0].req.signal.aborted).toBe(false);
    unmount();
    expect(calls[0].req.signal.aborted).toBe(true);
  });

  it('Refresh reloads page 1 from the host', async () => {
    const source: jest.MockedFunction<DataGridRowSource> = jest.fn(async () => ({
      rows: [row('h-1', 'Host thing')],
      hasMore: false,
    }));
    render(ui(makeClient(), { rowSource: source }));
    expect(await screen.findByText('Host thing')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));
    await waitFor(() => expect(source.mock.calls.length).toBeGreaterThanOrEqual(2));
    expect(source.mock.calls[source.mock.calls.length - 1][0].page).toBe(1);
  });

  describe('membership filter (an overlay the host path never runs)', () => {
    const membershipConfig = configWith({ membershipFilter: true });

    it('control: without rowSource the membership resolver is called', async () => {
      const resolver = jest.fn(async () => ['f-1']);
      render(ui(makeClient(membershipConfig), { membershipResolver: resolver }));
      expect(await screen.findByText('Fetched thing')).toBeInTheDocument();
      expect(resolver).toHaveBeenCalled();
    });

    it('with rowSource the membership resolver is not called', async () => {
      const resolver = jest.fn(async () => ['f-1']);
      const source: DataGridRowSource = async () => ({ rows: [row('h-1', 'Host thing')], hasMore: false });
      const client = makeClient(membershipConfig);
      render(ui(client, { membershipResolver: resolver, rowSource: source }));
      expect(await screen.findByText('Host thing')).toBeInTheDocument();
      expect(resolver).not.toHaveBeenCalled();
      expect(client.retrieveMultipleRecords).not.toHaveBeenCalled();
    });
  });
});
