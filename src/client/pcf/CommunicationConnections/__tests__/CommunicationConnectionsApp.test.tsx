/**
 * CommunicationConnectionsApp v1.6.0 card tests — RegardingResolver parity (task 131 / UAT R5).
 *
 * Locks the reworked on-form COLLAPSED CARD against the RegardingResolver design:
 *   - Row 2 renders the Regarding Number (Link) + Regarding Name (Text) from the
 *     DENORMALIZED primary regarding (context.webAPI.retrieveRecord on the host
 *     communication), with the OOB field labels; empty cells hide (no em-dash).
 *   - Row 1 title is NOT clickable (no modal-open on the title/header row) — the W11
 *     "card header opens modal" behavior is reverted.
 *   - The LOOKUP icon (right of refresh) is now the ONLY way to open the review modal.
 *   - The REFRESH icon saves + refreshes the form (defensive).
 *   - Both refresh + lookup are hidden in read-only.
 *
 * The rich review modal + its provenance-read / task-042 additive write contract are
 * UNCHANGED — those are covered by grouping.test / provenance.test / ConnectionsWriteHandler.test.
 */

import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

// -----------------------------------------------------------------------------
// Mock @spaarke/ui-components — the App + ConnectionsEditor + write handler all
// import from it. We only need the catalog, cleanGuid, the display-name resolver,
// and applyResolverFields (never actually invoked by these card-only tests).
// -----------------------------------------------------------------------------

jest.mock('@spaarke/ui-components', () => {
  const TODO_REGARDING_CATALOG = [
    {
      entityType: 'sprk_matter',
      entitySet: 'sprk_matters',
      lookupAttribute: 'sprk_regardingmatter',
      navPropHint: 'matter',
    },
    { entityType: 'contact', entitySet: 'contacts', lookupAttribute: 'sprk_regardingcontact', navPropHint: 'contact' },
  ];
  return {
    __esModule: true,
    TODO_REGARDING_CATALOG,
    cleanGuid: (id: string) => String(id).replace(/[{}]/g, '').toLowerCase(),
    resolveRecordDisplayNameFieldName: jest.fn().mockResolvedValue(null),
    // task 081 (C-8): the App resolves Xrm through the shared cross-frame
    // walker. Pass the REAL implementation through (from TS source) — the
    // walker is not what these tests are about, and mocking it would hide a
    // frame-walk regression.
    getXrm: jest.requireActual('../../../shared/Spaarke.UI.Components/src/utils/xrmContext').getXrm,
    applyResolverFields: jest.fn().mockResolvedValue({ recordNumber: null, displayName: null }),
    // spaarke-modal-system P7 task 090 (FR-11/FR-18): CommunicationConnectionsApp.tsx
    // imports OOB_MODAL_SIZES from `@spaarke/ui-components` for its
    // navigateTo calls (handleCreateType / handleRecordNumberClick). Mirror
    // the real module's values exactly — see utils/adapters/oobModalSizes.ts.
    OOB_MODAL_SIZES: {
      record: {
        width: { value: 85, unit: '%' as const },
        height: { value: 85, unit: '%' as const },
      },
      createForm: {
        width: { value: 70, unit: '%' as const },
        height: { value: 80, unit: '%' as const },
      },
      wizard: {
        width: { value: 60, unit: '%' as const },
        height: { value: 70, unit: '%' as const },
      },
    },
  };
});

// A confirm re-files through the BFF; stub that seam so the status-advance path can be observed.
jest.mock('../CommunicationConnections/bffWrites', () => ({
  refileThroughBff: jest.fn().mockResolvedValue(undefined),
}));

import { CommunicationConnectionsApp } from '../CommunicationConnections/CommunicationConnectionsApp';
import { refileThroughBff } from '../CommunicationConnections/bffWrites';

// -----------------------------------------------------------------------------
// Context stub
// -----------------------------------------------------------------------------

type AnyContext = ComponentFramework.Context<never>;

const HOST_ID = '22222222-2222-2222-2222-222222222222'; // matches jest.setup Xrm.Page getId

const MINIMAL_PROVENANCE = JSON.stringify({ version: 1, candidates: [], signals: [] });

function buildContext(overrides?: {
  denorm?: { number?: string | null; name?: string | null; url?: string | null };
  provenance?: string | null;
  showVersionFooter?: boolean | null;
  titleText?: string | null;
  /** Populated typed regarding lookups (a "filed" association) added to the host communication record. */
  filed?: { field: string; entityLogicalName: string; id: string; formattedName: string }[];
  /** Rows the viewer's own multi-record reads return, per entity (the shared viewer name resolver). */
  viewerRows?: Record<string, Record<string, unknown>[]>;
}): { context: AnyContext; retrieveRecord: jest.Mock; retrieveMultipleRecords: jest.Mock; updateRecord: jest.Mock } {
  const denorm = overrides?.denorm ?? {
    number: 'MTR-2025-0001',
    name: 'Acme v. Beta',
    url: 'https://test.crm.dynamics.com/main.aspx?etn=sprk_matter&id=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  };

  // The denorm effect retrieves the host communication WITHOUT $select (opts === undefined);
  // the display-name effect passes a `?$select=` third arg. Branch on that.
  const denormRecord: Record<string, unknown> = {
    sprk_regardingrecordnumber: denorm.number ?? undefined,
    sprk_regardingrecordname: denorm.name ?? undefined,
    sprk_regardingrecordurl: denorm.url ?? undefined,
  };
  // Add any filed typed lookups (bound-value + OData formatted-value pair) the App reads to build
  // the authoritative filed-association list.
  for (const f of overrides?.filed ?? []) {
    denormRecord[`_${f.field}_value`] = f.id;
    denormRecord[`_${f.field}_value@OData.Community.Display.V1.FormattedValue`] = f.formattedName;
  }
  const retrieveRecord = jest.fn((entity: string, _id: string, opts?: string) => {
    if (entity === 'sprk_communication' && opts === undefined) return Promise.resolve(denormRecord);
    return Promise.resolve({});
  });
  const updateRecord = jest.fn().mockResolvedValue({ id: 'ok' });
  const retrieveMultipleRecords = jest.fn((entity: string) =>
    Promise.resolve({ entities: overrides?.viewerRows?.[entity] ?? [] })
  );

  const ctx = {
    parameters: {
      entity: { raw: 'sprk_communication' },
      associationProvenance: { raw: overrides?.provenance === undefined ? MINIMAL_PROVENANCE : overrides.provenance },
      associationStatus: { raw: null },
      showVersionFooter: { raw: overrides?.showVersionFooter === undefined ? true : overrides.showVersionFooter },
      titleText: { raw: overrides?.titleText === undefined ? null : overrides.titleText },
      readOnly: { raw: false },
    },
    webAPI: { retrieveRecord, retrieveMultipleRecords, updateRecord },
    mode: { isControlDisabled: false },
  } as unknown as AnyContext;

  return { context: ctx, retrieveRecord, retrieveMultipleRecords, updateRecord };
}

function renderApp(context: AnyContext, readOnly = false): void {
  render(
    <FluentProvider theme={webLightTheme}>
      <CommunicationConnectionsApp context={context} readOnly={readOnly} version="1.6.1" />
    </FluentProvider>
  );
}

describe('CommunicationConnectionsApp — RegardingResolver card parity (task 131)', () => {
  const origXrm = (global as { Xrm?: unknown }).Xrm;
  afterEach(() => {
    (global as { Xrm?: unknown }).Xrm = origXrm;
    jest.clearAllMocks();
  });

  it('renders Row 2 number Link + name Text from the denormalized primary regarding, with OOB labels', async () => {
    const { context } = buildContext();
    renderApp(context);

    await waitFor(() => expect(screen.getByTestId('cc-record-number')).toHaveTextContent('MTR-2025-0001'));
    expect(screen.getByTestId('cc-record-name')).toHaveTextContent('Acme v. Beta');
    expect(screen.getByText('Regarding Number')).toBeInTheDocument();
    expect(screen.getByText('Regarding Name')).toBeInTheDocument();

    // The number is a Link (role="link"); the name is plain text (not a link).
    expect(screen.getByTestId('cc-record-number').getAttribute('role')).toBe('link');
    expect(screen.getByTestId('cc-record-name').getAttribute('role')).toBeNull();
  });

  it('hides empty Row 2 cells entirely when the denorm fields are blank (no em-dash)', async () => {
    const { context } = buildContext({ denorm: { number: null, name: null, url: null } });
    renderApp(context);

    // Wait for the denorm effect to settle, then assert both cells are absent.
    await waitFor(() => expect(screen.getByTestId('cc-title')).toBeInTheDocument());
    expect(screen.queryByTestId('cc-number-cell')).toBeNull();
    expect(screen.queryByTestId('cc-name-cell')).toBeNull();
    expect(screen.queryByText('—')).toBeNull();
  });

  it('does NOT open the review modal when the title/header row is clicked (title is not clickable)', async () => {
    const { context } = buildContext();
    renderApp(context);

    const title = await screen.findByTestId('cc-title');
    // Title is plain Text — no button role, not a button element.
    expect(title.getAttribute('role')).toBeNull();
    expect(title.tagName).not.toBe('BUTTON');

    fireEvent.click(title);
    // Modal content (EditorBody subtitle) must NOT appear.
    expect(screen.queryByText(/What this email is linked to\./i)).toBeNull();
  });

  it('opens the review modal from the LOOKUP icon (the only opener)', async () => {
    const { context } = buildContext();
    renderApp(context);

    const lookup = await screen.findByTestId('cc-lookup');
    fireEvent.click(lookup);

    await waitFor(() => expect(screen.getByText(/What this email is linked to\./i)).toBeInTheDocument());
  });

  it('refresh icon saves + refreshes the form (defensive)', async () => {
    const save = jest.fn().mockResolvedValue(undefined);
    const refresh = jest.fn().mockResolvedValue(undefined);
    (global as { Xrm?: unknown }).Xrm = {
      WebApi: { retrieveRecord: jest.fn(), retrieveMultipleRecords: jest.fn() },
      Navigation: { navigateTo: jest.fn().mockResolvedValue(undefined) },
      Page: { data: { entity: { getId: () => HOST_ID, save }, refresh } },
    };

    const { context } = buildContext();
    renderApp(context);

    const refreshBtn = await screen.findByTestId('cc-refresh');
    fireEvent.click(refreshBtn);

    await waitFor(() => expect(save).toHaveBeenCalled());
    expect(refresh).toHaveBeenCalledWith(true);
  });

  it('number Link opens the primary regarding record via navigateTo (modal, target 2) from the denorm URL', async () => {
    const navigateTo = jest.fn().mockResolvedValue(undefined);
    (global as { Xrm?: unknown }).Xrm = {
      WebApi: { retrieveRecord: jest.fn(), retrieveMultipleRecords: jest.fn() },
      Navigation: { navigateTo },
      Page: { data: { entity: { getId: () => HOST_ID } } },
    };

    const { context } = buildContext();
    renderApp(context);

    const numberLink = await screen.findByTestId('cc-record-number');
    fireEvent.click(numberLink);

    await waitFor(() => expect(navigateTo).toHaveBeenCalled());
    const [pageInput, navOptions] = navigateTo.mock.calls[0];
    expect(pageInput).toMatchObject({
      pageType: 'entityrecord',
      entityName: 'sprk_matter',
      entityId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    });
    expect(navOptions).toMatchObject({ target: 2 });
  });

  it('hides refresh + lookup icons in read-only mode (title + Row 2 still render)', async () => {
    const { context } = buildContext();
    renderApp(context, /* readOnly */ true);

    await waitFor(() => expect(screen.getByTestId('cc-record-name')).toHaveTextContent('Acme v. Beta'));
    expect(screen.queryByTestId('cc-refresh')).toBeNull();
    expect(screen.queryByTestId('cc-lookup')).toBeNull();
    expect(screen.getByTestId('cc-title')).toBeInTheDocument();
  });

  it('renders the version footer', async () => {
    const { context } = buildContext();
    renderApp(context);
    expect(await screen.findByTestId('cc-footer')).toHaveTextContent('v1.6.1');
  });

  // ── FIX A (task 132 / UAT R5): the card sources its Name from the primary FILED association, not the denorm
  // name field. The Number comes from the host record's own number field: the stored trace carries no numbers, and
  // text an older row still carries is never shown. ──
  const MATTER_ID = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
  const FILED_MATTER = [
    {
      field: 'sprk_regardingmatter',
      entityLogicalName: 'sprk_matter',
      id: MATTER_ID,
      formattedName: 'Filed Matter Title',
    },
  ];
  /** A v1 stored row whose contributor still carries name/number text (never shown). */
  const STORED_V1_PROVENANCE = JSON.stringify({
    version: 1,
    direction: 'incoming',
    decision: { status: 'Suggested', autoFiled: false },
    rungsFired: ['RecordNameMatch'],
    candidates: [
      {
        field: 'sprk_regardingmatter',
        targetEntity: 'sprk_matter',
        targetId: MATTER_ID,
        reinforcedConfidence: 0.95,
        deterministicConfidence: 0.95,
        written: false,
        conflict: false,
        contributors: [
          {
            rung: 'RecordNameMatch',
            confidence: 0.95,
            provenance:
              'record-name-match:sprk_matter:where=subject:matched=number:' +
              'name="Stored Matter Title":number="STORED-NUMBER-1":reason="reference number in subject"',
          },
        ],
      },
    ],
    signals: [],
  });
  const HOST_DENORM = { number: 'HOST-NUMBER-1', name: 'Mislabeled Name Field', url: null };

  it('sources Row 2 Name from the primary filed association and Number from the host record, never the stored trace', async () => {
    const { context } = buildContext({ denorm: HOST_DENORM, filed: FILED_MATTER, provenance: STORED_V1_PROVENANCE });
    renderApp(context);

    // Name cell = the filed association's record name (NOT the denorm name field).
    await waitFor(() => expect(screen.getByTestId('cc-record-name')).toHaveTextContent('Filed Matter Title'));
    // Number cell = the host record's own number field.
    expect(screen.getByTestId('cc-record-number')).toHaveTextContent('HOST-NUMBER-1');
    expect(document.body).not.toHaveTextContent('STORED-NUMBER-1');
    expect(document.body).not.toHaveTextContent('Stored Matter Title');
  });

  it("names the card from the viewer's own read through the shared resolver (one batched read, name field only)", async () => {
    const { context, retrieveMultipleRecords } = buildContext({
      denorm: HOST_DENORM,
      filed: FILED_MATTER,
      provenance: STORED_V1_PROVENANCE,
      viewerRows: { sprk_matter: [{ sprk_matterid: MATTER_ID, sprk_mattername: 'Viewer Matter Title' }] },
    });
    renderApp(context);

    await waitFor(() => expect(screen.getByTestId('cc-record-name')).toHaveTextContent('Viewer Matter Title'));
    const matterReads = retrieveMultipleRecords.mock.calls.filter(c => c[0] === 'sprk_matter');
    expect(matterReads).toHaveLength(1);
    expect(matterReads[0][1]).toContain('$select=sprk_matterid,sprk_mattername');
    expect(matterReads[0][1]).toContain(MATTER_ID);
  });

  it('number Link opens the filed matter record (filed association entity + id) via navigateTo', async () => {
    const navigateTo = jest.fn().mockResolvedValue(undefined);
    (global as { Xrm?: unknown }).Xrm = {
      WebApi: { retrieveRecord: jest.fn(), retrieveMultipleRecords: jest.fn() },
      Navigation: { navigateTo },
      Page: { data: { entity: { getId: () => HOST_ID } } },
    };

    const { context } = buildContext({ denorm: HOST_DENORM, filed: FILED_MATTER, provenance: STORED_V1_PROVENANCE });
    renderApp(context);

    const numberLink = await screen.findByTestId('cc-record-number');
    fireEvent.click(numberLink);

    await waitFor(() => expect(navigateTo).toHaveBeenCalled());
    const [pageInput, navOptions] = navigateTo.mock.calls[0];
    expect(pageInput).toMatchObject({
      pageType: 'entityrecord',
      entityName: 'sprk_matter',
      entityId: MATTER_ID,
    });
    expect(navOptions).toMatchObject({ target: 2 });
  });

  // ── The review modal: candidate slots carry the viewer-read name; hiding never changes the review state ──
  const CONTACT_ID = 'cccccccc-cccc-cccc-cccc-cccccccccccc';
  function suggestedCandidate(field: string, entity: string, id: string, confidence: number) {
    return {
      field,
      targetEntity: entity,
      targetId: id,
      targetName: 'Stored Target Name',
      reinforcedConfidence: confidence,
      deterministicConfidence: confidence,
      written: false,
      conflict: false,
      contributors: [{ rung: 'ParticipantCorrelation', confidence, provenance: 'participant-correlation' }],
    };
  }
  function suggestedProvenance(withContact: boolean): string {
    return JSON.stringify({
      version: 2,
      direction: 'incoming',
      decision: { status: 'Suggested', autoFiled: false },
      rungsFired: [],
      candidates: [
        suggestedCandidate('sprk_regardingmatter', 'sprk_matter', MATTER_ID, 0.9),
        ...(withContact ? [suggestedCandidate('sprk_regardingperson', 'contact', CONTACT_ID, 0.8)] : []),
      ],
      signals: [],
    });
  }
  const VIEWER_ROWS = { sprk_matter: [{ sprk_matterid: MATTER_ID, sprk_mattername: 'Viewer Matter Title' }] };

  async function openModalAndConfirmMatter(withContact: boolean): Promise<jest.Mock> {
    const navResponse = { ok: true, json: async () => ({ value: [] }) };
    global.fetch = jest.fn().mockResolvedValue(navResponse) as unknown as typeof fetch;
    const { context, updateRecord } = buildContext({
      provenance: suggestedProvenance(withContact),
      viewerRows: VIEWER_ROWS,
    });
    renderApp(context);
    fireEvent.click(await screen.findByTestId('cc-lookup'));
    await screen.findAllByText('Viewer Matter Title');
    fireEvent.click(screen.getByRole('button', { name: 'Confirm' }));
    await waitFor(() => expect(refileThroughBff).toHaveBeenCalled());
    return updateRecord;
  }

  const statusWrite = (updateRecord: jest.Mock) =>
    updateRecord.mock.calls.filter(c => (c[2] as Record<string, unknown>)?.sprk_associationstatus !== undefined);

  beforeEach(() => (refileThroughBff as jest.Mock).mockClear());

  it('the review modal shows a suggested slot under the viewer-read name; an unreadable candidate gets no slot', async () => {
    const { context } = buildContext({ provenance: suggestedProvenance(true), viewerRows: VIEWER_ROWS });
    renderApp(context);
    fireEvent.click(await screen.findByTestId('cc-lookup'));

    expect((await screen.findAllByText('Viewer Matter Title')).length).toBeGreaterThan(0);
    // One slot only (the contact is not readable), and no stored text anywhere.
    expect(screen.getAllByRole('button', { name: 'Confirm' })).toHaveLength(1);
    expect(document.body).not.toHaveTextContent('Stored Target Name');
  });

  it('confirming every visible slot does not mark the communication resolved while a hidden slot remains', async () => {
    const updateRecord = await openModalAndConfirmMatter(true);
    await new Promise(r => setTimeout(r, 0));
    expect(statusWrite(updateRecord)).toHaveLength(0);
  });

  it('control: with no hidden slot, confirming the last slot marks the communication resolved', async () => {
    const updateRecord = await openModalAndConfirmMatter(false);
    await waitFor(() => expect(statusWrite(updateRecord)).toHaveLength(1));
  });
});
