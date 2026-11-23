/**
 * DataGrid.rowActions.test — the HOST row-actions column (`rowActions`), R4 task 023a.
 *
 * Renders the REAL DataGrid over a mock IDataverseClient. Control: with no `rowActions` there is no actions column
 * and the header has only the configured columns. With `rowActions`: a final column; per row only the actions whose
 * `isVisible` is not false (hidden, never disabled); a click calls `onRecordAction(id, recordId, record, ctx)` and
 * does not select the row; each button's tooltip equals its aria-label; past the inline limit the rest fold into
 * a "More actions" menu.
 */
import * as React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { ArrowDownload20Regular, Delete20Regular, History20Regular, Share20Regular } from '@fluentui/react-icons';
import { DataGrid } from '../DataGrid';
import type { DataGridRowAction } from '../index';
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
        entities: [
          { sprk_thingid: 't-1', sprk_name: 'With file', hasFile: true },
          { sprk_thingid: 't-2', sprk_name: 'No file', hasFile: false },
        ],
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

/** The grid row (role=row) that holds `text`. */
const rowOf = (text: string) => screen.getByText(text).closest('[role="row"]') as HTMLElement;

const DOWNLOAD: DataGridRowAction = {
  id: 'download',
  label: 'Download',
  icon: <ArrowDownload20Regular />,
  isVisible: record => record.hasFile !== false,
};
const DELETE: DataGridRowAction = { id: 'delete', label: 'Delete', icon: <Delete20Regular /> };

describe('DataGrid — rowActions', () => {
  beforeEach(() => {
    jest.spyOn(console, 'info').mockImplementation(() => undefined);
  });
  afterEach(() => jest.restoreAllMocks());

  it('control: with no rowActions there is no actions column', async () => {
    renderGrid();
    expect(await screen.findByText('With file')).toBeInTheDocument();
    expect(screen.queryAllByTestId('datagrid-row-actions')).toHaveLength(0);
    expect(screen.queryByRole('button', { name: 'Download' })).not.toBeInTheDocument();
    expect(screen.queryByText('Actions')).not.toBeInTheDocument();
    // Header: the one configured column (the selection cell is not a columnheader).
    expect(screen.getAllByRole('columnheader')).toHaveLength(1);
  });

  it('control: an empty rowActions array adds no column either', async () => {
    renderGrid({ rowActions: [] });
    expect(await screen.findByText('With file')).toBeInTheDocument();
    expect(screen.queryAllByTestId('datagrid-row-actions')).toHaveLength(0);
    expect(screen.getAllByRole('columnheader')).toHaveLength(1);
  });

  it('adds a final actions column; an action whose isVisible is false is hidden for that row (not disabled)', async () => {
    renderGrid({ rowActions: [DOWNLOAD, DELETE] });
    expect(await screen.findByText('With file')).toBeInTheDocument();

    const headers = screen.getAllByRole('columnheader');
    expect(headers).toHaveLength(2);
    expect(headers[1]).toHaveTextContent('Actions');

    const withFile = within(rowOf('With file'));
    expect(withFile.getByRole('button', { name: 'Download' })).toBeEnabled();
    expect(withFile.getByRole('button', { name: 'Delete' })).toBeEnabled();

    const noFile = within(rowOf('No file'));
    expect(noFile.queryByRole('button', { name: 'Download' })).not.toBeInTheDocument();
    expect(noFile.getByRole('button', { name: 'Delete' })).toBeEnabled();
  });

  it('a click calls onRecordAction(id, recordId, record, ctx) and does not select the row', async () => {
    const onRecordAction = jest.fn();
    renderGrid({ rowActions: [DOWNLOAD, DELETE], onRecordAction });
    expect(await screen.findByText('With file')).toBeInTheDocument();

    fireEvent.click(within(rowOf('With file')).getByRole('button', { name: 'Download' }));
    expect(onRecordAction).toHaveBeenCalledTimes(1);
    expect(onRecordAction).toHaveBeenCalledWith(
      'download',
      't-1',
      expect.objectContaining({ sprk_thingid: 't-1', sprk_name: 'With file' }),
      expect.objectContaining({ configId: CONFIG_ID, entityName: 'sprk_thing', selectedIds: [] })
    );
    expect(rowOf('With file')).toHaveAttribute('aria-selected', 'false');

    fireEvent.click(within(rowOf('No file')).getByRole('button', { name: 'Delete' }));
    expect(onRecordAction).toHaveBeenLastCalledWith(
      'delete',
      't-2',
      expect.objectContaining({ sprk_thingid: 't-2' }),
      expect.anything()
    );
  });

  it("each button's tooltip equals its aria-label", async () => {
    renderGrid({ rowActions: [DOWNLOAD, DELETE] });
    expect(await screen.findByText('With file')).toBeInTheDocument();
    const button = within(rowOf('With file')).getByRole('button', { name: 'Download' });
    expect(button).toHaveAttribute('aria-label', 'Download');
    fireEvent.pointerEnter(button);
    const tooltip = await screen.findByRole('tooltip');
    expect(tooltip).toHaveTextContent(button.getAttribute('aria-label') as string);
  });

  it('past the inline limit the rest fold into a "More actions" menu', async () => {
    const onRecordAction = jest.fn();
    const VERSIONS: DataGridRowAction = { id: 'versions', label: 'Version history', icon: <History20Regular /> };
    const SHARE: DataGridRowAction = { id: 'share', label: 'Share', icon: <Share20Regular /> };
    renderGrid({ rowActions: [DOWNLOAD, VERSIONS, SHARE, DELETE], onRecordAction });
    expect(await screen.findByText('With file')).toBeInTheDocument();

    // Row 1: four visible → two inline + More.
    const withFile = within(rowOf('With file'));
    expect(withFile.getByRole('button', { name: 'Download' })).toBeInTheDocument();
    expect(withFile.getByRole('button', { name: 'Version history' })).toBeInTheDocument();
    expect(withFile.queryByRole('button', { name: 'Share' })).not.toBeInTheDocument();
    expect(withFile.queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument();

    fireEvent.click(withFile.getByRole('button', { name: 'More actions' }));
    const item = await screen.findByRole('menuitem', { name: 'Delete' });
    expect(screen.getByRole('menuitem', { name: 'Share' })).toBeInTheDocument();
    fireEvent.click(item);
    await waitFor(() =>
      expect(onRecordAction).toHaveBeenCalledWith('delete', 't-1', expect.anything(), expect.anything())
    );
    expect(rowOf('With file')).toHaveAttribute('aria-selected', 'false');

    // Row 2: Download hidden → three visible, all inline, no More.
    const noFile = within(rowOf('No file'));
    expect(noFile.getByRole('button', { name: 'Version history' })).toBeInTheDocument();
    expect(noFile.getByRole('button', { name: 'Share' })).toBeInTheDocument();
    expect(noFile.getByRole('button', { name: 'Delete' })).toBeInTheDocument();
    expect(noFile.queryByRole('button', { name: 'More actions' })).not.toBeInTheDocument();
  });

  describe('keyboard activation does not toggle the row selection', () => {
    const VERSIONS: DataGridRowAction = { id: 'versions', label: 'Version history', icon: <History20Regular /> };
    const SHARE: DataGridRowAction = { id: 'share', label: 'Share', icon: <Share20Regular /> };

    async function openMore(onRecordAction: jest.Mock) {
      renderGrid({ rowActions: [DOWNLOAD, VERSIONS, SHARE, DELETE], onRecordAction });
      expect(await screen.findByText('With file')).toBeInTheDocument();
      fireEvent.click(within(rowOf('With file')).getByRole('button', { name: 'More actions' }));
      return screen.findByRole('menuitem', { name: 'Delete' });
    }

    it.each([
      ['Space', ' '],
      ['Enter', 'Enter'],
    ])(
      '%s on a folded "More actions" menu item fires the action once and leaves the row unselected',
      async (_n, key) => {
        const onRecordAction = jest.fn();
        const item = await openMore(onRecordAction);
        fireEvent.keyDown(item, { key });
        // Fluent's aria-button handling activates a div on Enter keydown and on Space keyup.
        if (key === ' ') fireEvent.keyUp(item, { key });
        await waitFor(() => expect(onRecordAction).toHaveBeenCalled());
        expect(onRecordAction).toHaveBeenCalledTimes(1);
        expect(onRecordAction).toHaveBeenCalledWith('delete', 't-1', expect.anything(), expect.anything());
        expect(rowOf('With file')).toHaveAttribute('aria-selected', 'false');
        expect(rowOf('No file')).toHaveAttribute('aria-selected', 'false');
      }
    );

    it('control: Space on the row itself (not on an action) still toggles the selection', async () => {
      renderGrid({ rowActions: [DOWNLOAD, DELETE] });
      expect(await screen.findByText('With file')).toBeInTheDocument();
      const row = rowOf('With file');
      fireEvent.keyDown(row, { key: ' ' });
      await waitFor(() => expect(row).toHaveAttribute('aria-selected', 'true'));
    });

    it('Space and Enter on an inline action button leave the row unselected', async () => {
      const onRecordAction = jest.fn();
      renderGrid({ rowActions: [DOWNLOAD, DELETE], onRecordAction });
      expect(await screen.findByText('With file')).toBeInTheDocument();
      const button = within(rowOf('With file')).getByRole('button', { name: 'Download' });
      fireEvent.keyDown(button, { key: ' ' });
      fireEvent.keyDown(button, { key: 'Enter' });
      expect(rowOf('With file')).toHaveAttribute('aria-selected', 'false');
      // The activation itself is the button's click (a keyboard user's Space / Enter fires it natively).
      fireEvent.click(button);
      expect(onRecordAction).toHaveBeenCalledTimes(1);
      expect(rowOf('With file')).toHaveAttribute('aria-selected', 'false');
    });
  });

  it('the actions column header has no resize handle and does not sort', async () => {
    renderGrid({ rowActions: [DELETE] });
    expect(await screen.findByText('With file')).toBeInTheDocument();
    const [nameHeader, actionsHeader] = screen.getAllByRole('columnheader');
    // Control: the configured column has both.
    expect(nameHeader).toHaveAttribute('aria-sort');
    expect(nameHeader.querySelector('.fui-TableResizeHandle')).not.toBeNull();
    expect(actionsHeader).not.toHaveAttribute('aria-sort');
    expect(actionsHeader.querySelector('[role="separator"], .fui-TableResizeHandle')).toBeNull();
  });
});
