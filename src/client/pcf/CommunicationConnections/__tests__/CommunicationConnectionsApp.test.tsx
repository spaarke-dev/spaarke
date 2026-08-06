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

import { CommunicationConnectionsApp } from '../CommunicationConnections/CommunicationConnectionsApp';

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
}): { context: AnyContext; retrieveRecord: jest.Mock } {
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

  const ctx = {
    parameters: {
      entity: { raw: 'sprk_communication' },
      associationProvenance: { raw: overrides?.provenance === undefined ? MINIMAL_PROVENANCE : overrides.provenance },
      associationStatus: { raw: null },
      showVersionFooter: { raw: overrides?.showVersionFooter === undefined ? true : overrides.showVersionFooter },
      titleText: { raw: overrides?.titleText === undefined ? null : overrides.titleText },
      readOnly: { raw: false },
    },
    webAPI: { retrieveRecord, updateRecord: jest.fn().mockResolvedValue({ id: 'ok' }) },
    mode: { isControlDisabled: false },
  } as unknown as AnyContext;

  return { context: ctx, retrieveRecord };
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

  // ── FIX A (task 132 / UAT R5): card sources Number + Name from the primary FILED
  // association, NOT the denorm fields (which the inbound path writes backwards). ──
  it('sources Row 2 Number + Name from the primary filed association when the denorm fields are backwards (inbound)', async () => {
    const MATTER_ID = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
    // Inbound-style bad denorm: the record NUMBER sits in the NAME field, number field null.
    const denorm = { number: null, name: 'LITG-119896', url: null };
    // A filed matter lookup whose formatted value is the REAL matter name.
    const filed = [
      {
        field: 'sprk_regardingmatter',
        entityLogicalName: 'sprk_matter',
        id: MATTER_ID,
        formattedName: 'Monte Rosa Biotechnology v Spaarke Inc',
      },
    ];
    // Provenance carries the matter candidate with a RecordNameMatch contributor supplying the NUMBER.
    const provenance = JSON.stringify({
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
                'name="Monte Rosa Biotechnology v Spaarke Inc":number="LITG-119896":reason="reference number in subject"',
            },
          ],
        },
      ],
      signals: [],
    });

    const { context } = buildContext({ denorm, filed, provenance });
    renderApp(context);

    // Name cell = the filed association's real record name (NOT the denorm "LITG-119896").
    await waitFor(() =>
      expect(screen.getByTestId('cc-record-name')).toHaveTextContent('Monte Rosa Biotechnology v Spaarke Inc')
    );
    // Number cell = the provenance-derived reference number (the number that was mislabeled into the name).
    expect(screen.getByTestId('cc-record-number')).toHaveTextContent('LITG-119896');
    // The name field's mislabeled number must NOT appear as the record name.
    expect(screen.getByTestId('cc-record-name')).not.toHaveTextContent('LITG-119896');
  });

  it('number Link opens the filed matter record (filed association entity + id) via navigateTo', async () => {
    const MATTER_ID = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
    const navigateTo = jest.fn().mockResolvedValue(undefined);
    (global as { Xrm?: unknown }).Xrm = {
      Navigation: { navigateTo },
      Page: { data: { entity: { getId: () => HOST_ID } } },
    };

    const denorm = { number: null, name: 'LITG-119896', url: null };
    const filed = [
      {
        field: 'sprk_regardingmatter',
        entityLogicalName: 'sprk_matter',
        id: MATTER_ID,
        formattedName: 'Monte Rosa Biotechnology v Spaarke Inc',
      },
    ];
    const provenance = JSON.stringify({
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
                'name="Monte Rosa Biotechnology v Spaarke Inc":number="LITG-119896":reason="x"',
            },
          ],
        },
      ],
      signals: [],
    });

    const { context } = buildContext({ denorm, filed, provenance });
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
});
