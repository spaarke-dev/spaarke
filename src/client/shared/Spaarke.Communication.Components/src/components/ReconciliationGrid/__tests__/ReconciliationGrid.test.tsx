/**
 * ReconciliationGrid.test.tsx
 *
 * RTL tests for `<ReconciliationGrid />` (email-communication-intelligence-r2
 * task 050, FR-E2). Covers the task's closed acceptance set:
 *  (a) renders the framework `<DataGrid />` (not a bespoke table) via configId,
 *      listing seeded Email `sprk_communication` rows with the reconciliation
 *      cells (Status badge, truncated Preview).
 *  (b) an injected `onRecordOpen` fires when a row is opened (clicking the
 *      Subject column's link — the grid's primary/clickable column, see the
 *      row-open design note in ReconciliationGrid.tsx) and the default
 *      `Xrm.Navigation.navigateTo` does NOT run.
 *  (c) empty state — zero rows renders the configured empty message, no crash.
 *  (d) dark mode — renders under `webDarkTheme` with no console errors (ADR-021
 *      semantic tokens only, no hardcoded colors).
 *
 * A mock `IDataverseClient` drives the framework end-to-end (config record →
 * entity metadata → FetchXML rows) so these assertions exercise the REAL
 * `<DataGrid />` composition, not a stub.
 */
import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import type {
  IDataverseClient,
  EntityMetadata,
  SavedQueryResult,
  SavedQuerySummary,
  FetchMultipleResult,
  DataGridConfiguration,
} from '@spaarke/ui-components';
import { ReconciliationGrid, NEEDS_REVIEW_CONFIG_ID } from '../ReconciliationGrid';

const ENTITY_NAME = 'sprk_communication';

/** Mirrors the shape of needs-review.gridconfiguration.json (decoupled fixture — the JSON file is a deploy-time config artifact, not code under test). */
const NEEDS_REVIEW_CONFIG: DataGridConfiguration = {
  _version: '1.0',
  source: {
    type: 'inline',
    fetchXml:
      '<fetch><entity name="sprk_communication">' +
      '<attribute name="sprk_communicationid" /><attribute name="sprk_subject" />' +
      '<attribute name="sprk_body" /><attribute name="sprk_associationstatus" />' +
      '<attribute name="sprk_receiveddate" /><attribute name="sprk_from" /><attribute name="sprk_to" />' +
      '<filter type="and">' +
      '<condition attribute="sprk_communicationtype" operator="eq" value="100000000" />' +
      '</filter>' +
      '<order attribute="sprk_receiveddate" descending="true" />' +
      '</entity></fetch>',
    layoutXml:
      '<grid name="resultset"><row name="result" id="sprk_communicationid">' +
      '<cell name="sprk_subject" width="260" isfirstcell="true" />' +
      '<cell name="sprk_body" width="320" />' +
      '<cell name="sprk_associationstatus" width="140" />' +
      '<cell name="sprk_receiveddate" width="140" />' +
      '<cell name="sprk_from" width="220" />' +
      '<cell name="sprk_to" width="220" />' +
      '</row></grid>',
  },
  display: {
    // Distinct from the "Needs Review" status-badge label below so tests can
    // assert on the badge text without colliding with the ViewSelector's
    // title button (which also renders `display.title` as visible text).
    title: 'Reconciliation Queue',
    emptyStateMessage: 'No emails need review right now.',
  },
  columns: {
    sprk_subject: { label: 'Subject' },
    sprk_body: { label: 'Preview' },
    sprk_associationstatus: { label: 'Status' },
    sprk_receiveddate: { label: 'Date', renderer: 'date' },
    sprk_from: { label: 'From' },
    sprk_to: { label: 'To' },
  },
};

const ENTITY_METADATA: EntityMetadata = {
  primaryIdAttribute: 'sprk_communicationid',
  primaryNameAttribute: 'sprk_name',
  attributes: {
    sprk_communicationid: { attributeType: 'String', isPrimaryId: true },
    sprk_subject: { attributeType: 'String', displayName: 'Subject' },
    sprk_body: { attributeType: 'String', displayName: 'Preview' },
    sprk_associationstatus: { attributeType: 'Picklist', displayName: 'Status' },
    sprk_receiveddate: { attributeType: 'DateTime', format: 'DateOnly', displayName: 'Date' },
    sprk_from: { attributeType: 'String', displayName: 'From' },
    sprk_to: { attributeType: 'String', displayName: 'To' },
  },
};

interface SeedRow {
  sprk_communicationid: string;
  sprk_subject: string;
  sprk_body: string;
  sprk_associationstatus: number;
  sprk_receiveddate: string;
  sprk_from: string;
  sprk_to: string;
}

/** Builds a mock `IDataverseClient` that resolves the given config + rows through the REAL `<DataGrid />` pipeline. */
function makeMockClient(rows: SeedRow[], config: DataGridConfiguration | null = NEEDS_REVIEW_CONFIG): IDataverseClient {
  return {
    retrieveRecord: jest.fn(async () => ({
      sprk_configjson: config ? JSON.stringify(config) : '',
    })) as unknown as IDataverseClient['retrieveRecord'],
    retrieveSavedQuery: jest.fn(async (): Promise<SavedQueryResult> => {
      throw new Error('not used by inline source');
    }),
    retrieveSavedQueriesForEntity: jest.fn(async (): Promise<SavedQuerySummary[]> => []),
    retrieveEntityMetadata: jest.fn(async (): Promise<EntityMetadata> => ENTITY_METADATA),
    retrieveMultipleRecords: jest.fn(
      async (): Promise<FetchMultipleResult> => ({
        entities: rows,
        moreRecords: false,
      })
    ),
  };
}

function makeRow(overrides: Partial<SeedRow> = {}): SeedRow {
  return {
    sprk_communicationid: 'comm-1',
    sprk_subject: 'Quarterly filing update',
    sprk_body: '<p>Please find attached the latest draft for your review at your earliest convenience.</p>',
    sprk_associationstatus: 100000001, // Pending Review
    sprk_receiveddate: '2026-07-20T10:00:00Z',
    sprk_from: 'jane.doe@example.com',
    sprk_to: 'counsel@spaarke.com',
    ...overrides,
  };
}

function renderGrid(ui: React.ReactElement, theme = webLightTheme) {
  return render(<FluentProvider theme={theme}>{ui}</FluentProvider>);
}

describe('ReconciliationGrid', () => {
  afterEach(() => {
    // @ts-expect-error - test cleanup of a global test double
    delete window.Xrm;
    jest.restoreAllMocks();
  });

  it('renders the framework DataGrid via configId with seeded Email rows (Status badge + truncated Preview cells)', async () => {
    const client = makeMockClient([makeRow()]);

    renderGrid(<ReconciliationGrid configId={NEEDS_REVIEW_CONFIG_ID} dataverseClient={client} />);

    // Framework grid, not a bespoke table.
    expect(await screen.findByRole('grid')).toBeInTheDocument();
    expect(screen.getByText('Quarterly filing update')).toBeInTheDocument();
    // Reviewed-status badge cell (customRenderer — DEFAULT_COLUMN_RENDERERS).
    expect(screen.getByText('Needs Review')).toBeInTheDocument();
    // Truncated body-preview cell (HTML markup stripped, no raw tags visible).
    expect(screen.getByText(/Please find attached/)).toBeInTheDocument();
    expect(screen.queryByText(/<p>/)).not.toBeInTheDocument();
  });

  it('fires an injected onRecordOpen when the row is opened, and Xrm.Navigation.navigateTo does NOT run', async () => {
    const navigateTo = jest.fn();
    // @ts-expect-error - minimal Xrm shim for the default-navigateTo negative assertion
    window.Xrm = { Navigation: { navigateTo } };

    const onRecordOpen = jest.fn();
    const client = makeMockClient([makeRow({ sprk_communicationid: 'comm-open-1', sprk_subject: 'Open me' })]);

    renderGrid(
      <ReconciliationGrid configId={NEEDS_REVIEW_CONFIG_ID} dataverseClient={client} onRecordOpen={onRecordOpen} />
    );

    const subjectLink = await screen.findByRole('button', { name: 'Open me' });
    fireEvent.click(subjectLink);

    expect(onRecordOpen).toHaveBeenCalledTimes(1);
    expect(onRecordOpen).toHaveBeenCalledWith(
      'comm-open-1',
      expect.objectContaining({ sprk_communicationid: 'comm-open-1' }),
      expect.objectContaining({ entityName: ENTITY_NAME })
    );
    expect(navigateTo).not.toHaveBeenCalled();
  });

  it('renders the configured empty state with zero rows — no crash', async () => {
    const client = makeMockClient([]);

    renderGrid(<ReconciliationGrid configId={NEEDS_REVIEW_CONFIG_ID} dataverseClient={client} />);

    expect(await screen.findByText('No emails need review right now.')).toBeInTheDocument();
    expect(screen.queryByText('Quarterly filing update')).not.toBeInTheDocument();
  });

  it('renders correctly under the dark Fluent theme (ADR-021) with no console errors', async () => {
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    const client = makeMockClient([makeRow()]);

    renderGrid(<ReconciliationGrid configId={NEEDS_REVIEW_CONFIG_ID} dataverseClient={client} />, webDarkTheme);

    expect(await screen.findByText('Quarterly filing update')).toBeInTheDocument();
    expect(screen.getByText('Needs Review')).toBeInTheDocument();
    await waitFor(() => expect(errorSpy).not.toHaveBeenCalled());
    errorSpy.mockRestore();
  });
});
