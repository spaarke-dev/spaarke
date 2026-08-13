/**
 * ReconciliationWorkspace.test.tsx — the Pillar E reconciliation composition
 * (email-communication-intelligence-r2 task 061). Drives the closed acceptance set
 * through the REAL shipped components (grid + browse shell + reconcile tabs) with a
 * mock `IDataverseClient` (grid data) and a mocked `@spaarke/auth` seam (queue-feed
 * + apply/dismiss):
 *   • renders the framework grid via configId.
 *   • opening a row opens the SprkModal browse shell (two-pane + the A6 PanelSplitter).
 *   • NFR-10 — Fields/Tasks tabs are gated (disabled) until a Related-to record is
 *     confirmed; enabled + functional once `resolveRegarding` returns a regarding.
 *   • a Fields "Accept" invokes the apply path (POST /apply { overrideValue }).
 *   • NFR-11 — a Fields citation click lifts the citation into the shell reader,
 *     which highlights the exact passage.
 *   • the Related-to tab reuses the shipped RelatedToCell (052) when configured.
 *   • ADR-012 — the grid's injected onRecordOpen fires; Xrm.Navigation.navigateTo
 *     does NOT run.
 */
import * as React from 'react';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import type {
  IDataverseClient,
  EntityMetadata,
  SavedQueryResult,
  SavedQuerySummary,
  FetchMultipleResult,
  DataGridConfiguration,
} from '@spaarke/ui-components';
import type { EmailConnectionsReviewProps } from '../../EmailAssociationsAndTracking/EmailAssociationsAndTracking.types';

const mockAuthenticatedFetch = jest.fn();
jest.mock('@spaarke/auth', () => ({
  authenticatedFetch: (...args: [string, RequestInit?]) => mockAuthenticatedFetch(...args),
}));

import { ReconciliationWorkspace } from '../ReconciliationWorkspace';

const CONFIG_ID = '00000000-0000-4000-8000-000000005001';
const COMM_ID = 'comm-open-1';
const REGARDING = { entityType: 'sprk_matter', recordId: 'aaaaaaaa-0000-0000-0000-000000000001' };
const ENTITY_NAME = 'sprk_communication';

const CONFIG: DataGridConfiguration = {
  _version: '1.0',
  source: {
    type: 'inline',
    fetchXml:
      '<fetch><entity name="sprk_communication">' +
      '<attribute name="sprk_communicationid" /><attribute name="sprk_subject" />' +
      '<attribute name="sprk_body" /><attribute name="sprk_associationstatus" />' +
      '<attribute name="sprk_from" /><attribute name="sprk_to" />' +
      '</entity></fetch>',
    layoutXml:
      '<grid name="resultset"><row name="result" id="sprk_communicationid">' +
      '<cell name="sprk_subject" width="260" isfirstcell="true" />' +
      '<cell name="sprk_body" width="320" />' +
      '<cell name="sprk_associationstatus" width="140" />' +
      '<cell name="sprk_from" width="220" />' +
      '<cell name="sprk_to" width="220" />' +
      '</row></grid>',
  },
  display: { title: 'Reconciliation Queue', emptyStateMessage: 'No emails need review right now.' },
  columns: {
    sprk_subject: { label: 'Subject' },
    sprk_body: { label: 'Preview' },
    sprk_associationstatus: { label: 'Status' },
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
    sprk_from: { attributeType: 'String', displayName: 'From' },
    sprk_to: { attributeType: 'String', displayName: 'To' },
  },
};

interface SeedRow {
  sprk_communicationid: string;
  sprk_subject: string;
  sprk_body: string;
  sprk_associationstatus: number;
  sprk_from: string;
  sprk_to: string;
}

function makeRow(overrides: Partial<SeedRow> = {}): SeedRow {
  return {
    sprk_communicationid: COMM_ID,
    sprk_subject: 'Quarterly filing update',
    sprk_body: '<p>Please review the quarterly filing draft before the deadline.</p>',
    sprk_associationstatus: 100000001,
    sprk_from: 'jane.doe@example.com',
    sprk_to: 'counsel@spaarke.com',
    ...overrides,
  };
}

function makeMockClient(rows: SeedRow[]): IDataverseClient {
  return {
    retrieveRecord: jest.fn(async () => ({
      sprk_configjson: JSON.stringify(CONFIG),
    })) as unknown as IDataverseClient['retrieveRecord'],
    retrieveSavedQuery: jest.fn(async (): Promise<SavedQueryResult> => {
      throw new Error('not used by inline source');
    }),
    retrieveSavedQueriesForEntity: jest.fn(async (): Promise<SavedQuerySummary[]> => []),
    retrieveEntityMetadata: jest.fn(async (): Promise<EntityMetadata> => ENTITY_METADATA),
    retrieveMultipleRecords: jest.fn(
      async (): Promise<FetchMultipleResult> => ({ entities: rows, moreRecords: false })
    ),
  };
}

/** A queue-feed GET returning one Fields proposal for COMM_ID; any POST succeeds. */
function proposalItem(overrides: Record<string, unknown> = {}) {
  return {
    communicationId: COMM_ID,
    kind: 'pending-proposal',
    reviewLogId: 'rl-1',
    targetEntity: 'sprk_matter',
    targetField: 'sprk_closingdate',
    fieldType: 'DateTime',
    oldValue: '2026-01-01',
    newValue: '2026-08-15',
    proposalConfidence: 0.9,
    citationSource: 'body',
    citationLocator: 'body: sentence 1',
    citationQuotedText: 'quarterly filing draft',
    ...overrides,
  };
}
function wireAuth(items: unknown[] = [proposalItem()]) {
  mockAuthenticatedFetch.mockImplementation((_url: string, init?: RequestInit) => {
    if (!init || (init.method ?? 'GET') === 'GET') {
      return Promise.resolve({ ok: true, status: 200, json: async () => ({ items, count: items.length }) });
    }
    return Promise.resolve({ ok: true, status: 200, json: async () => ({ auditLogId: 'audit-1' }) });
  });
}

function renderWorkspace(
  props: Partial<React.ComponentProps<typeof ReconciliationWorkspace>> = {},
  rows = [makeRow()]
) {
  const client = makeMockClient(rows);
  const utils = render(
    <FluentProvider theme={webLightTheme}>
      <ReconciliationWorkspace configId={CONFIG_ID} dataverseClient={client} {...props} />
    </FluentProvider>
  );
  return { client, ...utils };
}

/** Open the first row's browse shell (clicks the Subject primary-name link). */
async function openFirstRow(subject = 'Quarterly filing update') {
  const link = await screen.findByRole('button', { name: subject });
  fireEvent.click(link);
  await screen.findByTestId('reconciliation-browse-two-pane');
}

describe('ReconciliationWorkspace', () => {
  beforeEach(() => {
    mockAuthenticatedFetch.mockReset();
    wireAuth();
  });
  afterEach(() => {
    // @ts-expect-error - test cleanup of a global test double
    delete window.Xrm;
    jest.restoreAllMocks();
  });

  it('renders the framework grid via configId', async () => {
    renderWorkspace();
    expect(await screen.findByRole('grid')).toBeInTheDocument();
    expect(screen.getByText('Quarterly filing update')).toBeInTheDocument();
  });

  it('opens the browse shell (two-pane + A6 PanelSplitter) on row-open; navigateTo does NOT run', async () => {
    const navigateTo = jest.fn();
    // @ts-expect-error - minimal Xrm shim for the default-navigateTo negative assertion
    window.Xrm = { Navigation: { navigateTo } };

    renderWorkspace();
    await openFirstRow();

    // The reader reflects the opened row.
    expect(await screen.findByTestId('reconciliation-browse-subject')).toHaveTextContent('Quarterly filing update');
    // A6 — the drag-resize splitter is present (role="separator").
    expect(screen.getByRole('separator', { name: 'Resize panels' })).toBeInTheDocument();
    // ADR-012 — the workspace's injected onRecordOpen handled it; no Xrm navigate.
    expect(navigateTo).not.toHaveBeenCalled();
  });

  it('NFR-10 — gates Fields/Tasks tabs until a Related-to record is confirmed', async () => {
    renderWorkspace({ resolveRegarding: () => null });
    await openFirstRow();

    expect(screen.getByTestId('reconcile-tab-fields')).toBeDisabled();
    expect(screen.getByTestId('reconcile-tab-tasks')).toBeDisabled();
  });

  it('enables Fields once a regarding is resolved and a Fields Accept invokes the apply path', async () => {
    renderWorkspace({ resolveRegarding: () => REGARDING });
    await openFirstRow();

    const fieldsTab = screen.getByTestId('reconcile-tab-fields');
    expect(fieldsTab).not.toBeDisabled();
    fireEvent.click(fieldsTab);

    // The real FieldUpdateReconcileTab loads the confirmed record's proposal.
    const card = await screen.findByTestId('field-reconcile-card');
    fireEvent.click(within(card).getByTestId('field-reconcile-accept'));

    await waitFor(() =>
      expect(mockAuthenticatedFetch).toHaveBeenCalledWith(
        '/communications/proposals/rl-1/apply',
        expect.objectContaining({ method: 'POST' })
      )
    );
    const applyCall = mockAuthenticatedFetch.mock.calls.find(c => String(c[0]).endsWith('/apply'));
    expect(JSON.parse(applyCall![1].body)).toEqual({ overrideValue: '2026-08-15' });
  });

  it('NFR-11 — a Fields citation click lifts the citation into the shell reader (highlight)', async () => {
    renderWorkspace({ resolveRegarding: () => REGARDING });
    await openFirstRow();

    fireEvent.click(screen.getByTestId('reconcile-tab-fields'));
    fireEvent.click(await screen.findByTestId('field-reconcile-citation'));

    // The shell reader highlights the exact cited passage (task 054 / NFR-11).
    const mark = await screen.findByTestId('citation-highlight-mark');
    expect(mark).toHaveTextContent('quarterly filing draft');
  });

  it('renders the full EmailConnectionsReview INLINE in the Related-to tab (UAT Fix #2 — not the RelatedToCell modal)', async () => {
    const resolveReview = () =>
      ({
        communicationId: COMM_ID,
        writeContext: { webApi: {}, hostEntity: ENTITY_NAME, hostRecordId: COMM_ID },
      }) as unknown as EmailConnectionsReviewProps;

    renderWorkspace({ resolveReview, resolveRegarding: () => null });
    await openFirstRow();

    // The browse tab renders the FULL review surface inline — same as the email form —
    // NOT the compact RelatedToCell hidden behind a "Requires review" + open-picker modal.
    const tabBody = await screen.findByTestId('reconcile-tab-body');
    expect(within(tabBody).getByTestId('email-connections-review')).toBeInTheDocument();
    // The manual "Lookup Records" pane is present inline (the side-pane the email page shows).
    expect(within(tabBody).getByTestId('link-another-record')).toBeInTheDocument();
    // And the old modal-behind-a-picker affordance is gone from the tab body.
    expect(within(tabBody).queryByTestId('related-to-open-picker')).not.toBeInTheDocument();
  });
});
