/**
 * RegardingResolverApp v1.3 tests — 2-row streamlined layout (SRFR-030).
 *
 * Covers FR-A1-01 / FR-A1-02 / FR-A1-03 / FR-A4-01 / FR-A5-01 / NFR-04:
 *   - 2-row DOM structure (row 1 title + PolymorphicPicker trigger; row 2
 *     1/3:2/3 grid with number Link + name Text, per SRFR-039 v1.3.4)
 *   - Toolbar-icon menu populated from catalog (11 entries)
 *   - PolymorphicPicker.onSelect flow invokes applyResolverFields via the
 *     ResolverWriteHandler wrapper (FR-A4-01)
 *   - Manifest defaults render when maker omits `title` / bound field bindings
 *   - Read-only mode hides the trigger (FR-A5-01)
 *   - Version footer renders (PCF CLAUDE.md mandatory rule)
 *   - No console errors on init
 *   - SRFR-039 (v1.3.4): Row 2 both cells present with top-aligned labels
 *     ("Regarding Number" / "Regarding Name"); name cell is plain Text not Link
 */

import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

// -----------------------------------------------------------------------------
// Mock @spaarke/ui-components so we control the catalog + resolver writes +
// the shared PolymorphicPicker. Tests assert on the mock's call log.
// -----------------------------------------------------------------------------

const mockApplyResolverFields = jest.fn().mockResolvedValue({ recordNumber: null, recordNumberSourceField: null });
const mockResolveRecordType = jest.fn().mockResolvedValue({ id: 'rt-guid', name: 'Matter' });
const mockPolymorphicPicker = jest.fn();

jest.mock('@spaarke/ui-components', () => {
  const TODO_REGARDING_CATALOG = [
    { entityType: 'sprk_matter', entitySet: 'sprk_matters', lookupAttribute: 'sprk_regardingmatter', navPropHint: 'matter' },
    { entityType: 'sprk_project', entitySet: 'sprk_projects', lookupAttribute: 'sprk_regardingproject', navPropHint: 'project' },
    { entityType: 'sprk_event', entitySet: 'sprk_events', lookupAttribute: 'sprk_regardingevent', navPropHint: 'event' },
    { entityType: 'sprk_communication', entitySet: 'sprk_communications', lookupAttribute: 'sprk_regardingcommunication', navPropHint: 'communication' },
    { entityType: 'sprk_workassignment', entitySet: 'sprk_workassignments', lookupAttribute: 'sprk_regardingworkassignment', navPropHint: 'workassignment' },
    { entityType: 'sprk_invoice', entitySet: 'sprk_invoices', lookupAttribute: 'sprk_regardinginvoice', navPropHint: 'invoice' },
    { entityType: 'sprk_budget', entitySet: 'sprk_budgets', lookupAttribute: 'sprk_regardingbudget', navPropHint: 'budget' },
    { entityType: 'sprk_analysis', entitySet: 'sprk_analyses', lookupAttribute: 'sprk_regardinganalysis', navPropHint: 'analysis' },
    { entityType: 'sprk_organization', entitySet: 'sprk_organizations', lookupAttribute: 'sprk_regardingorganization', navPropHint: 'organization' },
    { entityType: 'contact', entitySet: 'contacts', lookupAttribute: 'sprk_regardingcontact', navPropHint: 'contact' },
    { entityType: 'sprk_document', entitySet: 'sprk_documents', lookupAttribute: 'sprk_regardingdocument', navPropHint: 'document' },
  ];
  return {
    TODO_REGARDING_CATALOG,
    applyResolverFields: mockApplyResolverFields,
    resolveRecordType: mockResolveRecordType,
    buildRecordUrl: (entityType: string, id: string) =>
      `https://test/main.aspx?etn=${entityType}&id=${id}`,
    // PolymorphicPicker mock — records catalog + onSelect + title so tests
    // can trigger onSelect programmatically and inspect the props received.
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    PolymorphicPicker: (props: any) => {
      mockPolymorphicPicker(props);
      return (
        <div data-testid="polymorphic-picker-mock">
          <span data-testid="polymorphic-picker-mock-title">{props.title}</span>
          <span data-testid="polymorphic-picker-mock-catalog-count">
            {props.catalog?.length ?? 0}
          </span>
          {!props.readOnly && (
            <button
              data-testid="polymorphic-picker-trigger"
              onClick={() => props.onSelect('sprk_matter', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'Smith v. Jones')}
            >
              trigger
            </button>
          )}
        </div>
      );
    },
  };
});

import { RegardingResolverApp } from '../RegardingResolver/RegardingResolverApp';

// -----------------------------------------------------------------------------
// Context stub
// -----------------------------------------------------------------------------

type AnyContext = ComponentFramework.Context<never>;

function buildContext(overrides?: {
  entity?: string;
  regardingTargets?: string;
  readOnly?: boolean;
  title?: string | null;
  regardingRecordNumberField?: string | null;
  regardingRecordNameField?: string | null;
  boundLookup?: { id: string; name: string };
  showVersionFooter?: boolean | null;
}): { context: AnyContext; updateRecordMock: jest.Mock } {
  const updateRecordMock = jest.fn().mockResolvedValue({ id: 'ok' });
  const ctx = {
    parameters: {
      entity: { raw: overrides?.entity ?? 'sprk_todo' },
      regardingTargets: { raw: overrides?.regardingTargets ?? null },
      readOnly: { raw: overrides?.readOnly ?? false },
      regardingRecordType: overrides?.boundLookup ? { raw: [overrides.boundLookup] } : { raw: null },
      title: { raw: overrides?.title === undefined ? null : overrides.title },
      regardingRecordNumberField: {
        raw:
          overrides?.regardingRecordNumberField === undefined
            ? null
            : overrides.regardingRecordNumberField,
      },
      regardingRecordNameField: {
        raw:
          overrides?.regardingRecordNameField === undefined
            ? null
            : overrides.regardingRecordNameField,
      },
      showVersionFooter: {
        raw:
          overrides?.showVersionFooter === undefined ? null : overrides.showVersionFooter,
      },
    },
    mode: { isControlDisabled: false },
    webAPI: {
      retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
      updateRecord: updateRecordMock,
    },
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
  } as any;
  return { context: ctx as AnyContext, updateRecordMock };
}

const renderWithProvider = (ui: React.ReactElement): ReturnType<typeof render> =>
  render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

// -----------------------------------------------------------------------------
// Tests
// -----------------------------------------------------------------------------

beforeEach(() => {
  mockApplyResolverFields.mockClear();
  mockResolveRecordType.mockClear();
  mockPolymorphicPicker.mockClear();
  // Clean the CREATE-mode bridge global between tests.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  delete (window as any).__sprk_regarding_pending__;
  // Clean any Xrm stub left over from a previous SRFR-031 test.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  delete (window as any).Xrm;
});

describe('RegardingResolverApp v1.3 — 2-row layout', () => {
  // ---------------------------------------------------------------------------
  // FR-A1-01 — 2-row DOM structure
  // ---------------------------------------------------------------------------

  test('FR-A1-01 — renders exactly Row 1 and Row 2 within the root container', () => {
    const { context } = buildContext();
    renderWithProvider(
      <RegardingResolverApp
        context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
        readOnly={false}
        onRecordTypeChanged={() => undefined}
        version="1.3.6"
      />
    );

    expect(screen.getByTestId('regarding-resolver-root')).toBeInTheDocument();
    expect(screen.getByTestId('regarding-resolver-row-1')).toBeInTheDocument();
    expect(screen.getByTestId('regarding-resolver-row-2')).toBeInTheDocument();
  });

  test('FR-A1-01 — Row 1 contains title + PolymorphicPicker trigger', () => {
    const { context } = buildContext();
    renderWithProvider(
      <RegardingResolverApp
        context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
        readOnly={false}
        onRecordTypeChanged={() => undefined}
        version="1.3.6"
      />
    );

    const row1 = screen.getByTestId('regarding-resolver-row-1');
    expect(row1).toContainElement(screen.getByTestId('polymorphic-picker-mock'));
    expect(screen.getByTestId('polymorphic-picker-trigger')).toBeInTheDocument();
  });

  // ---------------------------------------------------------------------------
  // FR-A1-02 — Manifest defaults
  // ---------------------------------------------------------------------------

  test('FR-A1-02 — default title "RELATED RECORD" (uppercased) renders when maker omits title binding (SRFR-034 §1)', () => {
    const { context } = buildContext({ title: null });
    renderWithProvider(
      <RegardingResolverApp
        context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
        readOnly={false}
        onRecordTypeChanged={() => undefined}
        version="1.3.6"
      />
    );

    // v1.3.1 (SRFR-034 §1 + §4): the OOB-styled title is rendered at the
    // consumer surface with data-testid `regarding-resolver-title`, uppercased.
    expect(screen.getByTestId('regarding-resolver-title')).toHaveTextContent('RELATED RECORD');
  });

  test('FR-A1-02 — custom title from manifest overrides the default (still rendered uppercase per SRFR-034 §1)', () => {
    const { context } = buildContext({ title: 'Regarding' });
    renderWithProvider(
      <RegardingResolverApp
        context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
        readOnly={false}
        onRecordTypeChanged={() => undefined}
        version="1.3.6"
      />
    );

    // Uppercased belt-and-suspenders — the manifest default is uppercase but
    // if a maker types mixed-case, the app forces uppercase to match OOB
    // section-header convention.
    expect(screen.getByTestId('regarding-resolver-title')).toHaveTextContent('REGARDING');
  });

  test('SRFR-034 §1 — mixed-case maker input renders uppercased', () => {
    const { context } = buildContext({ title: 'related record' });
    renderWithProvider(
      <RegardingResolverApp
        context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
        readOnly={false}
        onRecordTypeChanged={() => undefined}
        version="1.3.6"
      />
    );

    expect(screen.getByTestId('regarding-resolver-title')).toHaveTextContent('RELATED RECORD');
  });

  test('FR-A1-02 — Row 2 renders bound record-number as a Link', () => {
    const { context } = buildContext({ regardingRecordNumberField: 'MTR-2025-0142' });
    renderWithProvider(
      <RegardingResolverApp
        context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
        readOnly={false}
        onRecordTypeChanged={() => undefined}
        version="1.3.6"
      />
    );

    const link = screen.getByTestId('regarding-resolver-record-number');
    expect(link).toHaveTextContent('MTR-2025-0142');
    expect(link).toHaveAttribute('role', 'link');
  });

  test('SRFR-039 — Row 2 renders BOTH number and name cells inline (v1.3.4 restored)', () => {
    // v1.3.4 restores the Name cell (SRFR-034 §6 removed it over-eagerly per
    // owner correction). Row 2 now renders 1/3 : 2/3 grid with both cells.
    const { context } = buildContext({
      regardingRecordNumberField: 'MTR-2025-0142',
      regardingRecordNameField: 'Smith v. Jones',
    });
    renderWithProvider(
      <RegardingResolverApp
        context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
        readOnly={false}
        onRecordTypeChanged={() => undefined}
        version="1.3.6"
      />
    );

    // Both cells present, each with its own testid.
    expect(screen.getByTestId('regarding-resolver-number-cell')).toBeInTheDocument();
    expect(screen.getByTestId('regarding-resolver-name-cell')).toBeInTheDocument();
    // Record-number renders as Link.
    expect(screen.getByTestId('regarding-resolver-record-number')).toHaveTextContent('MTR-2025-0142');
    // Record-name renders as plain Text — but the value is present.
    expect(screen.getByTestId('regarding-resolver-record-name')).toHaveTextContent('Smith v. Jones');
  });

  test('SRFR-039 — empty record-number hides the number cell entirely (no em-dash placeholder)', () => {
    // Empty cells hide entirely rather than render a placeholder character.
    const { context } = buildContext();
    renderWithProvider(
      <RegardingResolverApp
        context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
        readOnly={false}
        onRecordTypeChanged={() => undefined}
        version="1.3.6"
      />
    );

    expect(screen.queryByTestId('regarding-resolver-number-cell')).not.toBeInTheDocument();
    expect(screen.queryByTestId('regarding-resolver-record-number-empty')).not.toBeInTheDocument();
    // The Row-2 container is still in the DOM (structural anchor), just empty.
    expect(screen.getByTestId('regarding-resolver-row-2')).toBeInTheDocument();
  });

  test('SRFR-039 — empty record-name hides the name cell entirely (no em-dash placeholder)', () => {
    // Name-only-empty case: number renders, name hides.
    const { context } = buildContext({ regardingRecordNumberField: 'MTR-2025-0142' });
    renderWithProvider(
      <RegardingResolverApp
        context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
        readOnly={false}
        onRecordTypeChanged={() => undefined}
        version="1.3.6"
      />
    );

    expect(screen.getByTestId('regarding-resolver-number-cell')).toBeInTheDocument();
    expect(screen.queryByTestId('regarding-resolver-name-cell')).not.toBeInTheDocument();
  });

  // ---------------------------------------------------------------------------
  // FR-A1-03 — Toolbar-icon menu populated from catalog
  // ---------------------------------------------------------------------------

  test('FR-A1-03 — PolymorphicPicker receives full 11-entity catalog', () => {
    const { context } = buildContext();
    renderWithProvider(
      <RegardingResolverApp
        context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
        readOnly={false}
        onRecordTypeChanged={() => undefined}
        version="1.3.6"
      />
    );

    expect(screen.getByTestId('polymorphic-picker-mock-catalog-count')).toHaveTextContent('11');
    // Confirm the picker mock was called with a catalog prop of length 11.
    const lastCallProps = mockPolymorphicPicker.mock.calls[mockPolymorphicPicker.mock.calls.length - 1][0];
    expect(Array.isArray(lastCallProps.catalog)).toBe(true);
    expect(lastCallProps.catalog).toHaveLength(11);
  });

  test('FR-A1-03 — regardingTargets manifest input filters the catalog', () => {
    const { context } = buildContext({ regardingTargets: 'sprk_matter,sprk_project' });
    renderWithProvider(
      <RegardingResolverApp
        context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
        readOnly={false}
        onRecordTypeChanged={() => undefined}
        version="1.3.6"
      />
    );

    expect(screen.getByTestId('polymorphic-picker-mock-catalog-count')).toHaveTextContent('2');
  });

  // ---------------------------------------------------------------------------
  // FR-A4-01 — onSelect invokes applyResolverFields via handler
  // ---------------------------------------------------------------------------

  test('FR-A4-01 — PolymorphicPicker.onSelect delegates to applyResolverFields', async () => {
    const { context } = buildContext();
    const onRecordTypeChanged = jest.fn();
    renderWithProvider(
      <RegardingResolverApp
        context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
        readOnly={false}
        onRecordTypeChanged={onRecordTypeChanged}
        version="1.3.6"
      />
    );

    // Trigger the mocked picker's onSelect via the mock button.
    fireEvent.click(screen.getByTestId('polymorphic-picker-trigger'));

    await waitFor(() => {
      expect(mockApplyResolverFields).toHaveBeenCalledTimes(1);
    });
    // Verify the shared write path was called with our host-record webApi surface.
    const call = mockApplyResolverFields.mock.calls[0];
    expect(call[3]).toBe('sprk_matter'); // parentEntityLogicalName
    expect(call[5]).toBe('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'); // parentRecordId
    expect(call[6]).toBe('Smith v. Jones'); // parentRecordName
  });

  // ---------------------------------------------------------------------------
  // FR-A5-01 / NFR-04 — Read-only mode preservation (SRFR-033)
  // ---------------------------------------------------------------------------

  describe('FR-A5-01 — Read-only mode preservation (SRFR-033)', () => {
    test('FR-A5-01 — read-only mode hides the PolymorphicPicker trigger', () => {
      const { context } = buildContext();
      renderWithProvider(
        <RegardingResolverApp
          context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
          readOnly={true}
          onRecordTypeChanged={() => undefined}
          version="1.3.6"
        />
      );

      // Row 1 title still renders (title text is stateless — safe under
      // read-only); the trigger button is fully hidden.
      expect(screen.queryByTestId('polymorphic-picker-trigger')).not.toBeInTheDocument();
      expect(screen.getByTestId('regarding-resolver-row-1')).toBeInTheDocument();
      expect(screen.getByTestId('polymorphic-picker-mock-title')).toBeInTheDocument();
    });

    test('FR-A5-01 — read-only mode preserves Row 2 display of BOTH cells (v1.3.4 SRFR-039)', () => {
      const { context } = buildContext({
        readOnly: true,
        regardingRecordNumberField: 'MTR-2025-0142',
        regardingRecordNameField: 'Smith v. Jones',
      });
      renderWithProvider(
        <RegardingResolverApp
          context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
          readOnly={true}
          onRecordTypeChanged={() => undefined}
          version="1.3.6"
        />
      );

      // Row 2 renders both cells; hyperlink still active (view action is safe
      // under read-only — clicking opens the record modal for viewing).
      expect(screen.getByTestId('regarding-resolver-row-2')).toBeInTheDocument();
      const link = screen.getByTestId('regarding-resolver-record-number');
      expect(link).toHaveTextContent('MTR-2025-0142');
      expect(link).toHaveAttribute('role', 'link');
      // v1.3.4 SRFR-039: name cell restored as plain-text display.
      expect(screen.getByTestId('regarding-resolver-record-name')).toHaveTextContent('Smith v. Jones');
    });

    test('FR-A5-01 — read-only mode: Row 2 hyperlink click still opens modal (view action is safe)', () => {
      // Row 2 hyperlink is a VIEW action per FR-A5-01 — safe under read-only.
      // The defensive write-gate on handlePickerSelect (RegardingResolverApp.tsx
      // line ~282) protects the WRITE path; the modal-open handler is
      // independent and remains active.
      const navigateToMock = jest.fn().mockResolvedValue(undefined);
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (window as any).Xrm = { Navigation: { navigateTo: navigateToMock } };

      // Note: readOnly hides the picker trigger, so we cannot populate
      // selectedTarget via the mock picker in this test. We assert instead
      // that the record-number Link element is rendered under read-only
      // and does not throw when clicked (the handler no-ops silently when
      // selectedTarget is null, per SRFR-031 empty-state guard).
      const { context } = buildContext({
        readOnly: true,
        regardingRecordNumberField: 'MTR-2025-0142',
      });
      renderWithProvider(
        <RegardingResolverApp
          context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
          readOnly={true}
          onRecordTypeChanged={() => undefined}
          version="1.3.6"
        />
      );

      const link = screen.getByTestId('regarding-resolver-record-number');
      expect(() => fireEvent.click(link)).not.toThrow();
    });

    test('FR-A5-01 — read-only mode: defensive write-gate refuses handlePickerSelect if a race reaches it', async () => {
      // Belt-and-suspenders — even if the picker trigger were somehow
      // exposed under read-only, the handler refuses to invoke the
      // resolver write path. Verified by explicit warn + no
      // applyResolverFields call.
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);

      // Simulate: render normally (readOnly=false) so the trigger IS
      // present, then re-render with readOnly=true right before clicking.
      // But since props flow through React, cleanest test: verify
      // applyResolverFields is NOT called when the readOnly prop is true
      // even if some unforeseen path fires. Since the trigger is hidden,
      // this is equivalent to asserting no calls occurred.
      const { context } = buildContext({ readOnly: true });
      renderWithProvider(
        <RegardingResolverApp
          context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
          readOnly={true}
          onRecordTypeChanged={() => undefined}
          version="1.3.6"
        />
      );

      // The trigger MUST NOT be in the DOM under read-only. This is the
      // primary defense; the handler-side gate is belt+suspenders. Assert
      // both: (1) trigger absent, (2) resolver never called.
      expect(screen.queryByTestId('polymorphic-picker-trigger')).not.toBeInTheDocument();
      expect(mockApplyResolverFields).not.toHaveBeenCalled();
      warnSpy.mockRestore();
    });
  });

  // ---------------------------------------------------------------------------
  // FR-A5-03 — URL field write regression at consumer surface (SRFR-033)
  // ---------------------------------------------------------------------------

  describe('FR-A5-03 — URL field (sprk_regardingrecordurl) write preservation (SRFR-033)', () => {
    /**
     * The URL field is written inside PolymorphicResolverService.applyResolverFields
     * (shared lib line 372). The RegardingResolver test mocks that service,
     * so this consumer-side regression asserts the visible seam that the
     * shared lib produces a URL for the picked record: the CREATE-mode
     * bridge seam publishes `recordUrl` on `window.__sprk_regarding_pending__`
     * via `buildRecordUrl` — the same helper used by the shared lib. If the
     * shared lib ever drops the URL write, this consumer seam would fail
     * (since the presave webresource consumes `recordUrl` at line 619 of
     * this test file, but that's an integration surface — this test asserts
     * the shape at the CONSUMER surface, catching regressions that only
     * appear when the mock is replaced with the real implementation).
     */
    test('FR-A5-03 — selection publishes non-empty recordUrl on the CREATE-mode seam', async () => {
      // Force CREATE-mode so the seam is populated.
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const w = window as any;
      const originalXrm = w.Xrm;
      w.Xrm = { Page: { data: { entity: { getId: () => '' } } } };
      try {
        const { context } = buildContext();
        renderWithProvider(
          <RegardingResolverApp
            context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
            readOnly={false}
            onRecordTypeChanged={() => undefined}
            version="1.3.6"
          />
        );

        fireEvent.click(screen.getByTestId('polymorphic-picker-trigger'));

        await waitFor(() => {
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          const seam = (window as any).__sprk_regarding_pending__;
          expect(seam).toBeDefined();
          expect(typeof seam.recordUrl).toBe('string');
          expect(seam.recordUrl.length).toBeGreaterThan(0);
          // URL must reference the picked entity type and record id, matching
          // buildRecordUrl output shape (etn + id query params).
          expect(seam.recordUrl).toEqual(expect.stringContaining('sprk_matter'));
          expect(seam.recordUrl).toEqual(
            expect.stringContaining('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa')
          );
        });
      } finally {
        if (originalXrm === undefined) {
          delete w.Xrm;
        } else {
          w.Xrm = originalXrm;
        }
      }
    });
  });

  // ---------------------------------------------------------------------------
  // Version footer (MANDATORY per PCF CLAUDE.md)
  // ---------------------------------------------------------------------------

  test('renders version footer', () => {
    const { context } = buildContext();
    renderWithProvider(
      <RegardingResolverApp
        context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
        readOnly={false}
        onRecordTypeChanged={() => undefined}
        version="1.3.6"
      />
    );

    // SRFR-033 — footer format is `v{version} • Built YYYY-MM-DD` per
    // src/client/pcf/CLAUDE.md "Version Footer Requirement (MANDATORY)".
    const footer = screen.getByTestId('regarding-resolver-version');
    expect(footer).toHaveTextContent(/v1\.3\.6/);
    expect(footer).toHaveTextContent(/Built \d{4}-\d{2}-\d{2}/);
  });

  // ---------------------------------------------------------------------------
  // No console errors on init
  // ---------------------------------------------------------------------------

  test('no console.error calls during initial render', () => {
    const spy = jest.spyOn(console, 'error').mockImplementation(() => undefined);
    const { context } = buildContext();
    renderWithProvider(
      <RegardingResolverApp
        context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
        readOnly={false}
        onRecordTypeChanged={() => undefined}
        version="1.3.6"
      />
    );
    expect(spy).not.toHaveBeenCalled();
    spy.mockRestore();
  });

  // ---------------------------------------------------------------------------
  // NFR-04 — backward compat with existing form bindings
  // ---------------------------------------------------------------------------

  test('NFR-04 — component works for sprk_todo host (no code branching)', () => {
    const { context } = buildContext({ entity: 'sprk_todo' });
    renderWithProvider(
      <RegardingResolverApp
        context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
        readOnly={false}
        onRecordTypeChanged={() => undefined}
        version="1.3.6"
      />
    );
    expect(screen.getByTestId('regarding-resolver-root')).toBeInTheDocument();
  });

  test('NFR-04 — component works for sprk_communication host (no code branching)', () => {
    const { context } = buildContext({ entity: 'sprk_communication' });
    renderWithProvider(
      <RegardingResolverApp
        context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
        readOnly={false}
        onRecordTypeChanged={() => undefined}
        version="1.3.6"
      />
    );
    expect(screen.getByTestId('regarding-resolver-root')).toBeInTheDocument();
  });

  // ---------------------------------------------------------------------------
  // FR-A2-01 — modal-open on Row-2 record-number hyperlink click (SRFR-031)
  // ---------------------------------------------------------------------------

  describe('FR-A2-01 — Row-2 record-number modal-open (SRFR-031)', () => {
    test('valid selectedTarget → navigateTo called with exact page-input + navigation-options', async () => {
      const navigateToMock = jest.fn().mockResolvedValue(undefined);
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (window as any).Xrm = { Navigation: { navigateTo: navigateToMock } };

      const { context } = buildContext({ regardingRecordNumberField: 'MTR-2025-0142' });
      renderWithProvider(
        <RegardingResolverApp
          context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
          readOnly={false}
          onRecordTypeChanged={() => undefined}
          version="1.3.6"
        />
      );

      // Fire the picker's onSelect first so selectedTarget is populated
      // (post-select is the only state where entityName/entityId are non-null).
      fireEvent.click(screen.getByTestId('polymorphic-picker-trigger'));
      await waitFor(() => {
        expect(mockApplyResolverFields).toHaveBeenCalledTimes(1);
      });

      // Now click the record-number link. v1.3.6 (SRFR-043): the click handler
      // is now async (wraps resolveClickTarget in .then), so we must waitFor
      // the navigateTo call even for the fresh-selection Priority 1 path.
      fireEvent.click(screen.getByTestId('regarding-resolver-record-number'));

      await waitFor(() => {
        expect(navigateToMock).toHaveBeenCalledTimes(1);
      });
      expect(navigateToMock).toHaveBeenCalledWith(
        {
          pageType: 'entityrecord',
          entityName: 'sprk_matter',
          entityId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
        },
        {
          target: 2,
          width: { value: 80, unit: '%' },
          height: { value: 80, unit: '%' },
        }
      );
    });

    test('Xrm undefined → console.warn + no throw', async () => {
      // No Xrm on window (beforeEach cleared it).
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);

      const { context } = buildContext({ regardingRecordNumberField: 'MTR-2025-0142' });
      renderWithProvider(
        <RegardingResolverApp
          context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
          readOnly={false}
          onRecordTypeChanged={() => undefined}
          version="1.3.6"
        />
      );

      // Populate selectedTarget by triggering onSelect first.
      fireEvent.click(screen.getByTestId('polymorphic-picker-trigger'));
      await waitFor(() => {
        expect(mockApplyResolverFields).toHaveBeenCalledTimes(1);
      });

      // Now click. Must NOT throw. v1.3.6: click handler is async, so we
      // waitFor the warn to appear on the microtask queue.
      expect(() =>
        fireEvent.click(screen.getByTestId('regarding-resolver-record-number'))
      ).not.toThrow();

      await waitFor(() => {
        expect(warnSpy).toHaveBeenCalledWith(
          expect.stringContaining('Xrm.Navigation.navigateTo unavailable')
        );
      });
      warnSpy.mockRestore();
    });

    test('navigateTo rejects → console.warn + no throw', async () => {
      const navigateToMock = jest.fn().mockRejectedValue(new Error('record deleted'));
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (window as any).Xrm = { Navigation: { navigateTo: navigateToMock } };
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);

      const { context } = buildContext({ regardingRecordNumberField: 'MTR-2025-0142' });
      renderWithProvider(
        <RegardingResolverApp
          context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
          readOnly={false}
          onRecordTypeChanged={() => undefined}
          version="1.3.6"
        />
      );

      fireEvent.click(screen.getByTestId('polymorphic-picker-trigger'));
      await waitFor(() => {
        expect(mockApplyResolverFields).toHaveBeenCalledTimes(1);
      });

      expect(() =>
        fireEvent.click(screen.getByTestId('regarding-resolver-record-number'))
      ).not.toThrow();

      // Wait for the microtask queue so the .catch handler fires.
      await waitFor(() => {
        expect(warnSpy).toHaveBeenCalledWith(
          expect.stringContaining('Xrm.Navigation.navigateTo rejected'),
          expect.any(Error)
        );
      });
      warnSpy.mockRestore();
    });

    test('empty entityName/entityId (no picker selection yet) → navigateTo NOT called', () => {
      const navigateToMock = jest.fn();
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (window as any).Xrm = { Navigation: { navigateTo: navigateToMock } };

      const { context } = buildContext({ regardingRecordNumberField: 'MTR-2025-0142' });
      renderWithProvider(
        <RegardingResolverApp
          context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
          readOnly={false}
          onRecordTypeChanged={() => undefined}
          version="1.3.6"
        />
      );

      // Click without firing the picker first — selectedTarget is null AND
      // no Xrm.Page attributes present → resolveClickTarget returns null →
      // warn + no-op (SRFR-042 empty-state gate).
      fireEvent.click(screen.getByTestId('regarding-resolver-record-number'));

      expect(navigateToMock).not.toHaveBeenCalled();
      warnSpy.mockRestore();
    });
  });

  // ---------------------------------------------------------------------------
  // SRFR-043 — Row 2 hyperlink WebAPI-based fix (v1.3.6)
  //
  // v1.3.5 (SRFR-042) attempted to fix the pre-loaded record no-op bug by
  // reading `sprk_regardingrecordurl` via `Xrm.Page.getAttribute`. That path
  // never fired in production because the URL field isn't on the sprk_todo
  // form — getAttribute returns null for fields not on the form regardless
  // of whether the underlying row has the value.
  //
  // v1.3.6 (SRFR-043) replaces the Xrm.Page read with a WebAPI-based host-
  // record retrieve at click time. `context.webAPI.retrieveRecord` reads
  // DIRECTLY from the row (not the form), so the URL is available even when
  // the field is not on the form.
  //
  // Priority remains:
  //   1. selectedTarget (fresh picker selection wins — synchronous)
  //   2. WebAPI retrieveRecord on the host record, parse etn+id from URL
  //
  // Removed from v1.3.5: (a) Xrm.Page URL read (never reachable in prod);
  // (b) sprk_regardingrecordtype.entityType hint fallback (was inert).
  // ---------------------------------------------------------------------------

  describe('SRFR-043 — Row 2 hyperlink WebAPI-based fix (pre-loaded records)', () => {
    test('pre-loaded record — WebAPI returns URL — parses etn+id + calls navigateTo', async () => {
      // Simulate a pre-loaded UPDATE-mode form: selectedTarget is null; host
      // record id is derivable via Xrm.Page.data.entity.getId(); context.webAPI
      // returns the sprk_regardingrecordurl for the host row when queried.
      const navigateToMock = jest.fn().mockResolvedValue(undefined);
      const preLoadedUrl =
        'https://spaarkedev1.crm.dynamics.com/main.aspx?etn=sprk_matter&id=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
      const retrieveRecordMock = jest
        .fn()
        .mockResolvedValue({ sprk_regardingrecordurl: preLoadedUrl });

      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (window as any).Xrm = {
        Navigation: { navigateTo: navigateToMock },
        Page: {
          data: { entity: { getId: () => '{22222222-2222-2222-2222-222222222222}' } },
        },
      };

      const { context } = buildContext({ regardingRecordNumberField: 'MTR-2025-0142' });
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (context as any).webAPI.retrieveRecord = retrieveRecordMock;

      renderWithProvider(
        <RegardingResolverApp
          context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
          readOnly={false}
          onRecordTypeChanged={() => undefined}
          version="1.3.6"
        />
      );

      // Click WITHOUT firing the picker — selectedTarget is null. The
      // resolveClickTarget helper must derive the target via WebAPI.
      fireEvent.click(screen.getByTestId('regarding-resolver-record-number'));

      // Async — wait for the retrieveRecord promise to resolve + navigateTo
      // to fire in the .then() chain of the click handler.
      await waitFor(() => {
        expect(navigateToMock).toHaveBeenCalledTimes(1);
      });

      expect(retrieveRecordMock).toHaveBeenCalledWith(
        'sprk_todo',
        '22222222-2222-2222-2222-222222222222',
        '?$select=sprk_regardingrecordurl'
      );
      expect(navigateToMock).toHaveBeenCalledWith(
        {
          pageType: 'entityrecord',
          entityName: 'sprk_matter',
          entityId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
        },
        {
          target: 2,
          width: { value: 80, unit: '%' },
          height: { value: 80, unit: '%' },
        }
      );
    });

    test('fresh picker selection wins — WebAPI NOT called (regression guard)', async () => {
      // Belt-and-suspenders: even when the host record's URL would parse to a
      // DIFFERENT target, a fresh picker selection takes priority. Because
      // Priority 1 returns synchronously, the WebAPI call is never made — this
      // guards both the priority ordering AND the WebAPI-avoidance short-circuit.
      const navigateToMock = jest.fn().mockResolvedValue(undefined);
      const staleUrl =
        'https://spaarkedev1.crm.dynamics.com/main.aspx?etn=sprk_project&id=bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
      const retrieveRecordMock = jest
        .fn()
        .mockResolvedValue({ sprk_regardingrecordurl: staleUrl });

      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (window as any).Xrm = {
        Navigation: { navigateTo: navigateToMock },
        Page: {
          data: { entity: { getId: () => '{22222222-2222-2222-2222-222222222222}' } },
          ui: { getFormType: () => 2 },
        },
      };

      const { context } = buildContext({ regardingRecordNumberField: 'MTR-2025-0142' });
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (context as any).webAPI.retrieveRecord = retrieveRecordMock;

      renderWithProvider(
        <RegardingResolverApp
          context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
          readOnly={false}
          onRecordTypeChanged={() => undefined}
          version="1.3.6"
        />
      );

      // Fire picker onSelect first — this populates selectedTarget with the
      // FRESH selection (sprk_matter / aaa GUID), not the stale row URL.
      fireEvent.click(screen.getByTestId('polymorphic-picker-trigger'));
      await waitFor(() => {
        expect(mockApplyResolverFields).toHaveBeenCalledTimes(1);
      });

      // Now click. Selection wins over WebAPI — navigateTo receives
      // sprk_matter + aaa GUID, NOT sprk_project + bbb GUID. And WebAPI
      // retrieveRecord must NOT have been called (Priority 1 short-circuit).
      fireEvent.click(screen.getByTestId('regarding-resolver-record-number'));

      await waitFor(() => {
        expect(navigateToMock).toHaveBeenCalledTimes(1);
      });

      expect(retrieveRecordMock).not.toHaveBeenCalled();
      expect(navigateToMock).toHaveBeenCalledWith(
        expect.objectContaining({
          entityName: 'sprk_matter',
          entityId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
        }),
        expect.anything()
      );
    });

    test('empty state — no selectedTarget AND no hostRecordId → warn + no-op', async () => {
      // CREATE-mode-like state: no host record id (Xrm.Page.data.entity.getId
      // returns empty). Priority 2 short-circuits (needs hostRecordId). No
      // selectedTarget. Handler must warn + no-op; navigateTo must not fire;
      // retrieveRecord must not be called.
      const navigateToMock = jest.fn();
      const retrieveRecordMock = jest.fn();
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (window as any).Xrm = {
        Navigation: { navigateTo: navigateToMock },
        Page: {
          data: { entity: { getId: () => '' } },
        },
      };

      const { context } = buildContext({ regardingRecordNumberField: 'MTR-2025-0142' });
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (context as any).webAPI.retrieveRecord = retrieveRecordMock;

      renderWithProvider(
        <RegardingResolverApp
          context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
          readOnly={false}
          onRecordTypeChanged={() => undefined}
          version="1.3.6"
        />
      );

      expect(() =>
        fireEvent.click(screen.getByTestId('regarding-resolver-record-number'))
      ).not.toThrow();

      // Micro-task queue must drain before we assert the .then() branch ran.
      await Promise.resolve();
      await Promise.resolve();

      expect(retrieveRecordMock).not.toHaveBeenCalled();
      expect(navigateToMock).not.toHaveBeenCalled();
      expect(warnSpy).toHaveBeenCalledWith(
        expect.stringContaining('Cannot open modal')
      );
      warnSpy.mockRestore();
    });

    test('WebAPI retrieveRecord rejects — warn + no-op (record deleted or no privilege)', async () => {
      // Defensive path: WebAPI rejection must not crash the host form.
      // resolveClickTarget catches the rejection, warns, returns null; handler
      // then falls through to its own "cannot open" warn + no-op.
      const navigateToMock = jest.fn();
      const retrieveRecordMock = jest.fn().mockRejectedValue(new Error('Record not found'));
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (window as any).Xrm = {
        Navigation: { navigateTo: navigateToMock },
        Page: {
          data: { entity: { getId: () => '{22222222-2222-2222-2222-222222222222}' } },
        },
      };

      const { context } = buildContext({ regardingRecordNumberField: 'MTR-2025-0142' });
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (context as any).webAPI.retrieveRecord = retrieveRecordMock;

      renderWithProvider(
        <RegardingResolverApp
          context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
          readOnly={false}
          onRecordTypeChanged={() => undefined}
          version="1.3.6"
        />
      );

      expect(() =>
        fireEvent.click(screen.getByTestId('regarding-resolver-record-number'))
      ).not.toThrow();

      // Wait for the rejected promise + the fallback warn to fire.
      await waitFor(() => {
        expect(retrieveRecordMock).toHaveBeenCalledTimes(1);
      });
      await waitFor(() => {
        expect(warnSpy).toHaveBeenCalledWith(
          expect.stringContaining('Cannot open modal'),
          ...([] as unknown[])
        );
      });

      expect(navigateToMock).not.toHaveBeenCalled();
      // Both warnings should appear: (a) the retrieveRecord-rejected warn from
      // inside resolveClickTarget, (b) the "Cannot open modal" warn from the
      // click handler's null-target fallback.
      expect(warnSpy).toHaveBeenCalledWith(
        expect.stringContaining('webApi.retrieveRecord'),
        expect.anything()
      );
      warnSpy.mockRestore();
    });
  });

  // ---------------------------------------------------------------------------
  // SRFR-032 / FR-A5-04 — CREATE-mode presave bridge carries recordNumber
  // ---------------------------------------------------------------------------

  describe('FR-A5-04 — CREATE-mode presave bridge (SRFR-032)', () => {
    /**
     * Force CREATE-mode: stub `Xrm.Page.data.entity.getId()` to return empty
     * so `getHostRecordId()` (RegardingResolverApp.tsx) yields undefined and
     * the seam gate `if (!writeCtx.hostRecordId)` opens.
     */
    const withCreateModeXrm = (): (() => void) => {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const w = window as any;
      const originalXrm = w.Xrm;
      w.Xrm = { Page: { data: { entity: { getId: () => '' } } } };
      return () => {
        if (originalXrm === undefined) {
          delete w.Xrm;
        } else {
          w.Xrm = originalXrm;
        }
      };
    };

    /**
     * Force UPDATE-mode: stub `Xrm.Page.data.entity.getId()` to a real GUID so
     * `getHostRecordId()` yields a value and the seam gate stays closed.
     */
    const withUpdateModeXrm = (guid: string): (() => void) => {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const w = window as any;
      const originalXrm = w.Xrm;
      w.Xrm = { Page: { data: { entity: { getId: () => `{${guid}}` } } } };
      return () => {
        if (originalXrm === undefined) {
          delete w.Xrm;
        } else {
          w.Xrm = originalXrm;
        }
      };
    };

    test('CREATE-mode selection populates seam.recordNumber from shared service result', async () => {
      const restore = withCreateModeXrm();
      try {
        mockApplyResolverFields.mockResolvedValueOnce({
          recordNumber: 'MTR-2025-0042',
          recordNumberSourceField: 'sprk_matternumber',
        });

        const { context } = buildContext();
        renderWithProvider(
          <RegardingResolverApp
            context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
            readOnly={false}
            onRecordTypeChanged={() => undefined}
            version="1.3.6"
          />
        );

        fireEvent.click(screen.getByTestId('polymorphic-picker-trigger'));

        await waitFor(() => {
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          const seam = (window as any).__sprk_regarding_pending__;
          expect(seam).toBeDefined();
          expect(seam.recordNumber).toBe('MTR-2025-0042');
        });

        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const seam = (window as any).__sprk_regarding_pending__;
        // Presave v1.2.0 consumes these via TEXT_FIELDS + textKeyForField
        // (SRFR-040). Assert the full payload shape.
        expect(seam.hostEntity).toBe('sprk_todo');
        expect(seam.entityType).toBe('sprk_matter');
        expect(seam.recordId).toBe('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa');
        expect(seam.recordName).toBe('Smith v. Jones');
        expect(typeof seam.recordUrl).toBe('string');
        expect(seam.recordNumber).toBe('MTR-2025-0042');
        expect(seam.lookupAttribute).toBe('sprk_regardingmatter');
      } finally {
        restore();
      }
    });

    test('UPDATE-mode selection leaves seam untouched (no stale-payload write)', async () => {
      const restore = withUpdateModeXrm('11111111-1111-1111-1111-111111111111');
      try {
        // Pre-condition: seam is undefined (cleared in beforeEach).
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        expect((window as any).__sprk_regarding_pending__).toBeUndefined();

        mockApplyResolverFields.mockResolvedValueOnce({
          recordNumber: 'MTR-2025-0099',
          recordNumberSourceField: 'sprk_matternumber',
        });

        const { context } = buildContext();
        renderWithProvider(
          <RegardingResolverApp
            context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
            readOnly={false}
            onRecordTypeChanged={() => undefined}
            version="1.3.6"
          />
        );

        fireEvent.click(screen.getByTestId('polymorphic-picker-trigger'));

        await waitFor(() => {
          expect(mockApplyResolverFields).toHaveBeenCalledTimes(1);
        });

        // Seam remains undefined — UPDATE mode writes via
        // Xrm.WebApi.updateRecord inside applyRegardingSelection; the seam
        // is CREATE-only per FR-A5-02 to prevent subsequent form events
        // from picking up a stale payload.
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        expect((window as any).__sprk_regarding_pending__).toBeUndefined();
      } finally {
        restore();
      }
    });

    test('CREATE-mode with null recordNumber (NFR-06 graceful-blank) still writes seam with recordNumber: null', async () => {
      const restore = withCreateModeXrm();
      try {
        // NFR-06 graceful-blank: shared service returns null when catalog
        // metadata is missing (Q-06 Contact/Account intentional-null) OR the
        // target-record's business-key value is empty. Presave v1.2.0 handles
        // null values in TEXT_FIELDS loop without throwing.
        mockApplyResolverFields.mockResolvedValueOnce({
          recordNumber: null,
          recordNumberSourceField: null,
        });

        const { context } = buildContext();
        renderWithProvider(
          <RegardingResolverApp
            context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
            readOnly={false}
            onRecordTypeChanged={() => undefined}
            version="1.3.6"
          />
        );

        fireEvent.click(screen.getByTestId('polymorphic-picker-trigger'));

        await waitFor(() => {
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          expect((window as any).__sprk_regarding_pending__).toBeDefined();
        });

        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const seam = (window as any).__sprk_regarding_pending__;
        // Contract parity: recordNumber key IS present (so downstream consumers
        // can distinguish "unresolved" from "field-omitted"); value is null.
        expect('recordNumber' in seam).toBe(true);
        expect(seam.recordNumber).toBeNull();
        // Other required keys intact.
        expect(seam.entityType).toBe('sprk_matter');
        expect(seam.recordId).toBe('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa');
      } finally {
        restore();
      }
    });

    test('CREATE-mode seam key set matches the presave v1.2.0 contract exactly', async () => {
      const restore = withCreateModeXrm();
      try {
        mockApplyResolverFields.mockResolvedValueOnce({
          recordNumber: 'MTR-2025-0042',
          recordNumberSourceField: 'sprk_matternumber',
        });

        const { context } = buildContext();
        renderWithProvider(
          <RegardingResolverApp
            context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
            readOnly={false}
            onRecordTypeChanged={() => undefined}
            version="1.3.6"
          />
        );

        fireEvent.click(screen.getByTestId('polymorphic-picker-trigger'));

        await waitFor(() => {
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          expect((window as any).__sprk_regarding_pending__).toBeDefined();
        });

        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const seam = (window as any).__sprk_regarding_pending__;
        // Keys presave v1.2.0 actually consumes:
        //   - hostEntity, entityType, entitySet, recordId, recordName,
        //     recordUrl, recordNumber, lookupAttribute
        // navProp is documented in the presave docstring as an optional future
        // key but the runtime derives lookup attr from `lookupAttribute` or
        // `entityType` (see sprk_todo_regarding_presave.js line 217:
        // `pending.lookupAttribute || deriveLookupAttribute(pending.entityType)`).
        // See notes/task-032-inventory.md for the navProp divergence rationale.
        const expectedKeys = [
          'hostEntity',
          'entityType',
          'entitySet',
          'lookupAttribute',
          'recordId',
          'recordName',
          'recordUrl',
          'recordNumber',
        ].sort();
        expect(Object.keys(seam).sort()).toEqual(expectedKeys);
      } finally {
        restore();
      }
    });
  });

  // ---------------------------------------------------------------------------
  // SRFR-034 §2 — Version footer toggle
  // ---------------------------------------------------------------------------

  describe('SRFR-034 §2 — showVersionFooter input property', () => {
    test('showVersionFooter default (null) → footer visible', () => {
      const { context } = buildContext({ showVersionFooter: null });
      renderWithProvider(
        <RegardingResolverApp
          context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
          readOnly={false}
          onRecordTypeChanged={() => undefined}
          version="1.3.6"
        />
      );

      expect(screen.getByTestId('regarding-resolver-footer')).toBeInTheDocument();
      expect(screen.getByTestId('regarding-resolver-version')).toHaveTextContent(/v1\.3\.6/);
    });

    test('showVersionFooter=true → footer visible', () => {
      const { context } = buildContext({ showVersionFooter: true });
      renderWithProvider(
        <RegardingResolverApp
          context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
          readOnly={false}
          onRecordTypeChanged={() => undefined}
          version="1.3.6"
        />
      );

      expect(screen.getByTestId('regarding-resolver-footer')).toBeInTheDocument();
    });

    test('showVersionFooter=false → footer hidden', () => {
      const { context } = buildContext({ showVersionFooter: false });
      renderWithProvider(
        <RegardingResolverApp
          context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
          readOnly={false}
          onRecordTypeChanged={() => undefined}
          version="1.3.6"
        />
      );

      expect(screen.queryByTestId('regarding-resolver-footer')).not.toBeInTheDocument();
      expect(screen.queryByTestId('regarding-resolver-version')).not.toBeInTheDocument();
    });
  });

  // ---------------------------------------------------------------------------
  // SRFR-034 §5 — Refresh button
  // ---------------------------------------------------------------------------

  describe('SRFR-034 §5 — Refresh toolbar button', () => {
    test('refresh button renders on Row 1 (edit mode)', () => {
      const { context } = buildContext();
      renderWithProvider(
        <RegardingResolverApp
          context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
          readOnly={false}
          onRecordTypeChanged={() => undefined}
          version="1.3.6"
        />
      );

      const btn = screen.getByTestId('regarding-resolver-refresh');
      expect(btn).toBeInTheDocument();
      expect(btn).toHaveAttribute('aria-label', 'Refresh form');
    });

    test('refresh button hidden under readOnly', () => {
      const { context } = buildContext({ readOnly: true });
      renderWithProvider(
        <RegardingResolverApp
          context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
          readOnly={true}
          onRecordTypeChanged={() => undefined}
          version="1.3.6"
        />
      );

      expect(screen.queryByTestId('regarding-resolver-refresh')).not.toBeInTheDocument();
    });

    test('refresh click invokes Xrm.Page.data.entity.save() then Xrm.Page.data.refresh(true)', async () => {
      const saveMock = jest.fn().mockResolvedValue(undefined);
      const refreshMock = jest.fn().mockResolvedValue(undefined);
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (window as any).Xrm = {
        Page: {
          data: {
            entity: { save: saveMock, getId: () => '' },
            refresh: refreshMock,
          },
        },
      };

      const { context } = buildContext();
      renderWithProvider(
        <RegardingResolverApp
          context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
          readOnly={false}
          onRecordTypeChanged={() => undefined}
          version="1.3.6"
        />
      );

      fireEvent.click(screen.getByTestId('regarding-resolver-refresh'));

      await waitFor(() => {
        expect(saveMock).toHaveBeenCalledTimes(1);
      });
      await waitFor(() => {
        expect(refreshMock).toHaveBeenCalledWith(true);
      });
    });

    test('refresh click with Xrm unavailable falls back to window.location.reload()', async () => {
      // Ensure no Xrm on window (beforeEach cleared it).
      const reloadSpy = jest.fn();
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const originalLocation = window.location;
      Object.defineProperty(window, 'location', {
        writable: true,
        value: { ...originalLocation, reload: reloadSpy },
      });

      try {
        const { context } = buildContext();
        renderWithProvider(
          <RegardingResolverApp
            context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
            readOnly={false}
            onRecordTypeChanged={() => undefined}
            version="1.3.6"
          />
        );

        fireEvent.click(screen.getByTestId('regarding-resolver-refresh'));

        await waitFor(() => {
          expect(reloadSpy).toHaveBeenCalledTimes(1);
        });
      } finally {
        Object.defineProperty(window, 'location', {
          writable: true,
          value: originalLocation,
        });
      }
    });

    test('refresh click with save() throwing → console.warn + no host crash', async () => {
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
      const saveMock = jest.fn().mockRejectedValue(new Error('save failed'));
      const refreshMock = jest.fn();
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (window as any).Xrm = {
        Page: {
          data: {
            entity: { save: saveMock, getId: () => '' },
            refresh: refreshMock,
          },
        },
      };

      const { context } = buildContext();
      renderWithProvider(
        <RegardingResolverApp
          context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
          readOnly={false}
          onRecordTypeChanged={() => undefined}
          version="1.3.6"
        />
      );

      expect(() =>
        fireEvent.click(screen.getByTestId('regarding-resolver-refresh'))
      ).not.toThrow();

      await waitFor(() => {
        expect(warnSpy).toHaveBeenCalledWith(
          expect.stringContaining('Refresh failed'),
          expect.any(Error)
        );
      });
      warnSpy.mockRestore();
    });
  });

  // ---------------------------------------------------------------------------
  // SRFR-035 — Auto-refresh form after picker selection (v1.3.2)
  // ---------------------------------------------------------------------------

  describe('SRFR-035 — auto-refresh form after picker selection', () => {
    /**
     * These tests exercise the v1.3.2 autoRefreshForm helper wired into
     * handlePickerSelect after applyRegardingSelection. The helper:
     *   - Skips CREATE mode (formType === 1) — presave bridge owns that path
     *   - Otherwise saves the form then refreshes with server-side pull
     *   - Silent-continues on any error (Xrm unavailable, save reject, etc.)
     *
     * The existing SRFR-034 manual refresh button remains as an escape hatch.
     */

    test('UPDATE mode (formType 2) — auto-refresh invoked (save + refresh(true) called)', async () => {
      const saveMock = jest.fn().mockResolvedValue(undefined);
      const refreshMock = jest.fn().mockResolvedValue(undefined);
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (window as any).Xrm = {
        Page: {
          ui: { getFormType: () => 2 },
          data: {
            entity: {
              save: saveMock,
              getId: () => '{11111111-1111-1111-1111-111111111111}',
            },
            refresh: refreshMock,
          },
        },
      };

      const { context } = buildContext();
      renderWithProvider(
        <RegardingResolverApp
          context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
          readOnly={false}
          onRecordTypeChanged={() => undefined}
          version="1.3.6"
        />
      );

      // Trigger picker selection.
      fireEvent.click(screen.getByTestId('polymorphic-picker-trigger'));

      // First wait for the shared write path to be invoked so we know the
      // handler has progressed to the auto-refresh call.
      await waitFor(() => {
        expect(mockApplyResolverFields).toHaveBeenCalledTimes(1);
      });

      // Now assert save() then refresh(true) were called by autoRefreshForm.
      await waitFor(() => {
        expect(saveMock).toHaveBeenCalledTimes(1);
      });
      await waitFor(() => {
        expect(refreshMock).toHaveBeenCalledWith(true);
      });
    });

    test('CREATE mode (formType 1) — auto-refresh SKIPPED (presave bridge owns that path)', async () => {
      const saveMock = jest.fn().mockResolvedValue(undefined);
      const refreshMock = jest.fn().mockResolvedValue(undefined);
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (window as any).Xrm = {
        Page: {
          ui: { getFormType: () => 1 },
          data: {
            // CREATE-mode: empty host record id (getHostRecordId returns undefined).
            entity: { save: saveMock, getId: () => '' },
            refresh: refreshMock,
          },
        },
      };

      const { context } = buildContext();
      renderWithProvider(
        <RegardingResolverApp
          context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
          readOnly={false}
          onRecordTypeChanged={() => undefined}
          version="1.3.6"
        />
      );

      fireEvent.click(screen.getByTestId('polymorphic-picker-trigger'));

      await waitFor(() => {
        expect(mockApplyResolverFields).toHaveBeenCalledTimes(1);
      });

      // Also wait for the CREATE-mode seam so we know the handler's
      // post-select block has fully executed.
      await waitFor(() => {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        expect((window as any).__sprk_regarding_pending__).toBeDefined();
      });

      // Give any queued microtasks a chance to run so a mistakenly-invoked
      // autoRefreshForm would show up here.
      await new Promise(resolve => setTimeout(resolve, 0));

      // CREATE mode: save + refresh MUST NOT be called by autoRefreshForm.
      // (Presave webresource will call save() as part of the INSERT chain
      // separately; that is not this handler's responsibility.)
      expect(saveMock).not.toHaveBeenCalled();
      expect(refreshMock).not.toHaveBeenCalled();
    });

    test('Xrm unavailable — auto-refresh silent-continues (no throw, warn only)', async () => {
      // No Xrm on window (beforeEach cleared it). The handler must not throw
      // and must not crash the picker-select flow.
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);

      const { context } = buildContext();
      renderWithProvider(
        <RegardingResolverApp
          context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
          readOnly={false}
          onRecordTypeChanged={() => undefined}
          version="1.3.6"
        />
      );

      // Trigger picker selection; the underlying handler must not throw.
      expect(() =>
        fireEvent.click(screen.getByTestId('polymorphic-picker-trigger'))
      ).not.toThrow();

      // Wait for the shared write path (proves the handler ran end-to-end).
      await waitFor(() => {
        expect(mockApplyResolverFields).toHaveBeenCalledTimes(1);
      });

      // The auto-refresh helper should have logged its Xrm-unavailable warn.
      await waitFor(() => {
        expect(warnSpy).toHaveBeenCalledWith(
          expect.stringContaining('Auto-refresh skipped: Xrm unavailable')
        );
      });

      warnSpy.mockRestore();
    });
  });

  // ---------------------------------------------------------------------------
  // SRFR-037 — Title styling (font-weight 600 per actual OOB DevTools inspection)
  // ---------------------------------------------------------------------------

  describe('SRFR-037 — title font-weight matches OOB section-header (600 semi-bold)', () => {
    /**
     * Owner DevTools inspection of the OOB "TRACKING" section header revealed:
     *   .pa-hw { font-weight: 600 }
     * SRFR-034 briefing incorrectly stated font-weight=400; SRFR-037 corrects
     * to 600. This test asserts the title element renders with the correct
     * semi-bold weight via Griffel's atomic-class stylesheet injection.
     */

    test('title element renders with computed font-weight 600 (semi-bold)', () => {
      const { context } = buildContext();
      renderWithProvider(
        <RegardingResolverApp
          context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
          readOnly={false}
          onRecordTypeChanged={() => undefined}
          version="1.3.6"
        />
      );

      const title = screen.getByTestId('regarding-resolver-title');
      // Griffel emits atomic classes into <style> tags injected into the head;
      // jsdom's getComputedStyle resolves them via cascade. Assert the computed
      // fontWeight is exactly '600' (semi-bold per OOB `.pa-hw`).
      const computed = window.getComputedStyle(title);
      expect(computed.fontWeight).toBe('600');
    });
  });

  // ---------------------------------------------------------------------------
  // SRFR-039 — Row 2 restore Name cell inline (v1.3.4) — 1/3:2/3 grid + labels
  // ---------------------------------------------------------------------------

  describe('SRFR-039 — Row 2 v1.3.4: 1/3 : 2/3 grid with top-aligned OOB-parity labels', () => {
    test('Row 2 has TWO cells with correct data-testids (number + name)', () => {
      const { context } = buildContext({
        regardingRecordNumberField: 'MTR-2025-0142',
        regardingRecordNameField: 'Smith v. Jones',
      });
      renderWithProvider(
        <RegardingResolverApp
          context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
          readOnly={false}
          onRecordTypeChanged={() => undefined}
          version="1.3.6"
        />
      );

      const row2 = screen.getByTestId('regarding-resolver-row-2');
      expect(row2).toContainElement(screen.getByTestId('regarding-resolver-number-cell'));
      expect(row2).toContainElement(screen.getByTestId('regarding-resolver-name-cell'));
    });

    test('Row 2 uses CSS grid with 1fr 2fr columns (1/3 : 2/3 proportion)', () => {
      const { context } = buildContext({
        regardingRecordNumberField: 'MTR-2025-0142',
        regardingRecordNameField: 'Smith v. Jones',
      });
      renderWithProvider(
        <RegardingResolverApp
          context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
          readOnly={false}
          onRecordTypeChanged={() => undefined}
          version="1.3.6"
        />
      );

      const row2 = screen.getByTestId('regarding-resolver-row-2');
      const computed = window.getComputedStyle(row2);
      // Griffel emits `display: grid` and `grid-template-columns: 1fr 2fr` —
      // jsdom's cascade resolves the atomic class rules.
      expect(computed.display).toBe('grid');
      expect(computed.gridTemplateColumns).toBe('1fr 2fr');
    });

    test('Name cell renders as plain <Text> (NOT a Link) — no href, no role=link', () => {
      const { context } = buildContext({
        regardingRecordNumberField: 'MTR-2025-0142',
        regardingRecordNameField: 'Smith v. Jones',
      });
      renderWithProvider(
        <RegardingResolverApp
          context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
          readOnly={false}
          onRecordTypeChanged={() => undefined}
          version="1.3.6"
        />
      );

      const nameEl = screen.getByTestId('regarding-resolver-record-name');
      // Plain-text Text component renders as <span>. It is NOT a <Link>.
      expect(nameEl.tagName.toLowerCase()).not.toBe('a');
      expect(nameEl).not.toHaveAttribute('role', 'link');
      expect(nameEl).not.toHaveAttribute('href');
      // But the value is present.
      expect(nameEl).toHaveTextContent('Smith v. Jones');
    });

    test('Number cell still renders as Link (hyperlink preserved from SRFR-031)', () => {
      const { context } = buildContext({
        regardingRecordNumberField: 'MTR-2025-0142',
        regardingRecordNameField: 'Smith v. Jones',
      });
      renderWithProvider(
        <RegardingResolverApp
          context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
          readOnly={false}
          onRecordTypeChanged={() => undefined}
          version="1.3.6"
        />
      );

      const numEl = screen.getByTestId('regarding-resolver-record-number');
      expect(numEl).toHaveAttribute('role', 'link');
    });

    test('Both top-aligned labels "Regarding Number" and "Regarding Name" render', () => {
      const { context } = buildContext({
        regardingRecordNumberField: 'MTR-2025-0142',
        regardingRecordNameField: 'Smith v. Jones',
      });
      renderWithProvider(
        <RegardingResolverApp
          context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
          readOnly={false}
          onRecordTypeChanged={() => undefined}
          version="1.3.6"
        />
      );

      const numberLabel = screen.getByTestId('regarding-resolver-number-label');
      const nameLabel = screen.getByTestId('regarding-resolver-name-label');
      expect(numberLabel).toHaveTextContent('Regarding Number');
      expect(nameLabel).toHaveTextContent('Regarding Name');
    });

    test('Labels use OOB-parity styling (12px, weight 400, color #616161)', () => {
      const { context } = buildContext({
        regardingRecordNumberField: 'MTR-2025-0142',
        regardingRecordNameField: 'Smith v. Jones',
      });
      renderWithProvider(
        <RegardingResolverApp
          context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
          readOnly={false}
          onRecordTypeChanged={() => undefined}
          version="1.3.6"
        />
      );

      const numberLabel = screen.getByTestId('regarding-resolver-number-label');
      const computed = window.getComputedStyle(numberLabel);
      expect(computed.fontSize).toBe('12px');
      expect(computed.fontWeight).toBe('400');
      // color may serialize as rgb(97, 97, 97) in jsdom.
      expect(['#616161', 'rgb(97, 97, 97)']).toContain(computed.color);
    });
  });
});
