/**
 * Pillar 9 widget-visibility derivations — unit tests (task 073, D-C-28)
 *
 * Verifies each of the four `getAgentVisibleState` derivations
 * (Summary / DocumentViewer / Dashboard / Table):
 *
 *   - Returns a variant whose `widgetType` discriminator matches the
 *     category (FR-57 shape conformance).
 *   - Honors the privacy default: returns `null` when input is missing /
 *     malformed / structurally insufficient.
 *   - Self-limits per FR-55:
 *       • Summary    — `summary` ≤ 500 chars, `tldr` ≤ 5 entries × 200 chars.
 *       • DocumentViewer — `selectionText` ≤ 200 chars when present.
 *   - Withholds protected fields per ADR-015:
 *       • Dashboard  — NEVER chart data / section payloads.
 *       • Table      — `selectedRows` is a COUNT, NEVER row IDs / content.
 *
 * Covers FR-57 acceptance criteria + the task 073 POML ui-tests:
 *
 *   - "DocumentViewer with selection >200 chars: verify selectionText
 *      truncated to 200" (explicit POML ui-test #2).
 *   - "Mount each widget with realistic state; call getAgentVisibleState;
 *      verify output matches FR-57 shape" (explicit POML ui-test #1 —
 *      structural conformance test).
 */

// Mock `@spaarke/ui-components` at the module boundary — `register-workspace-widgets.ts`
// imports `safeRegister` from there, and the package pulls in `d3-force` (ESM)
// which `ts-jest` cannot parse without explicit `transformIgnorePatterns`
// config. Existing tests (DocumentViewerWidget.test.tsx, WorkspaceLayoutWidget
// .test.tsx) apply the same mock-at-boundary pattern. Each mock function /
// type we substitute below is referenced ONLY indirectly by the registry
// wiring (we do not exercise the underlying components in this suite).
jest.mock('@spaarke/ui-components', () => ({
  // safeRegister — passthrough to the registration callable.
  safeRegister: (_label: string, _type: string, fn: () => void): void => fn(),
  // DataGrid + XrmDataverseClient referenced by DataverseEntityViewWidget at
  // load time (lazy-import factory). The registry-wiring smoke tests do not
  // resolve the factory, so simple stubs suffice.
  DataGrid: () => null,
  XrmDataverseClient: class {},
  // Renderers referenced by other widget modules in the same registration
  // chain. None are exercised in this suite.
  RichFilePreview: () => null,
  DEFAULT_RICH_FILE_PREVIEW_DISABLED_ACTIONS: Object.freeze([]),
  getDefaultWorkspaceRenderer: () => null,
  setDefaultWorkspaceRenderer: () => undefined,
  launchAssignWorkWizard: () => undefined,
}));

import {
  summaryWidgetVisibility,
  documentViewerWidgetVisibility,
  dashboardWidgetVisibility,
  tableWidgetVisibility,
  SELECTION_TEXT_CAP_CHARS,
  SUMMARY_TEXT_CAP_CHARS,
  TLDR_MAX_BULLETS,
  TLDR_BULLET_CAP_CHARS,
} from '../pillar9-visibility';
import type {
  SerializedSummaryState,
  SerializedDocumentViewerState,
  SerializedDashboardState,
  SerializedTableState,
} from '../../../types/SerializedWidgetState';

// ---------------------------------------------------------------------------
// Summary derivation
// ---------------------------------------------------------------------------

describe('summaryWidgetVisibility', () => {
  test('FR-57 shape: returns Summary variant with summary + tldr + hasUserEdits', () => {
    const result = summaryWidgetVisibility({
      mode: 'static',
      prefilledFields: {
        summary: 'A concise agent-derived summary of the document.',
        tldr: ['First key point', 'Second key point'],
      },
    });

    expect(result).not.toBeNull();
    const typed = result as SerializedSummaryState;
    expect(typed.widgetType).toBe('Summary');
    expect(typed.summary).toBe('A concise agent-derived summary of the document.');
    expect(typed.tldr).toEqual(['First key point', 'Second key point']);
    expect(typed.hasUserEdits).toBe(false);

    // FR-57 shape conformance — assert exact keys.
    expect(Object.keys(typed).sort()).toEqual(['hasUserEdits', 'summary', 'tldr', 'widgetType']);
  });

  test('parses tldr from JSON-stringified array', () => {
    const result = summaryWidgetVisibility({
      prefilledFields: {
        summary: 'Body',
        tldr: '["Bullet A","Bullet B"]',
      },
    });
    expect((result as SerializedSummaryState).tldr).toEqual(['Bullet A', 'Bullet B']);
  });

  test('parses tldr from newline-joined string', () => {
    const result = summaryWidgetVisibility({
      prefilledFields: {
        summary: 'Body',
        tldr: 'Bullet A\nBullet B\nBullet C',
      },
    });
    expect((result as SerializedSummaryState).tldr).toEqual(['Bullet A', 'Bullet B', 'Bullet C']);
  });

  test('self-limits summary to SUMMARY_TEXT_CAP_CHARS (500)', () => {
    const longSummary = 'x'.repeat(SUMMARY_TEXT_CAP_CHARS + 250);
    const result = summaryWidgetVisibility({
      prefilledFields: { summary: longSummary, tldr: [] },
    });
    expect((result as SerializedSummaryState).summary.length).toBe(SUMMARY_TEXT_CAP_CHARS);
  });

  test('self-limits tldr to TLDR_MAX_BULLETS (5) × TLDR_BULLET_CAP_CHARS (200)', () => {
    const longBullet = 'y'.repeat(TLDR_BULLET_CAP_CHARS + 50);
    const bullets = Array.from({ length: TLDR_MAX_BULLETS + 3 }, () => longBullet);
    const result = summaryWidgetVisibility({
      prefilledFields: { summary: 'ok', tldr: bullets },
    });
    const typed = result as SerializedSummaryState;
    expect(typed.tldr).toHaveLength(TLDR_MAX_BULLETS);
    expect(typed.tldr.every(b => b.length === TLDR_BULLET_CAP_CHARS)).toBe(true);
  });

  test('opts out (returns null) when neither summary nor tldr have content', () => {
    expect(summaryWidgetVisibility({ prefilledFields: { summary: '', tldr: [] } })).toBeNull();
    expect(summaryWidgetVisibility({ prefilledFields: {} })).toBeNull();
    expect(summaryWidgetVisibility({})).toBeNull();
    expect(summaryWidgetVisibility(null)).toBeNull();
    expect(summaryWidgetVisibility(undefined)).toBeNull();
    expect(summaryWidgetVisibility('not-an-object')).toBeNull();
  });

  test('honors upstream-supplied hasUserEdits flag when present', () => {
    const result = summaryWidgetVisibility({
      prefilledFields: { summary: 'Body', tldr: [] },
      hasUserEdits: true,
    });
    expect((result as SerializedSummaryState).hasUserEdits).toBe(true);
  });
});

// ---------------------------------------------------------------------------
// DocumentViewer derivation
// ---------------------------------------------------------------------------

describe('documentViewerWidgetVisibility', () => {
  test('FR-57 shape: emits widgetType + filename + mimeType + sizeBytes + hasSelection', () => {
    const result = documentViewerWidgetVisibility({
      filename: 'engagement-letter.docx',
      mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      sizeBytes: 87456,
      hasSelection: false,
    });

    expect(result).not.toBeNull();
    const typed = result as SerializedDocumentViewerState;
    expect(typed.widgetType).toBe('DocumentViewer');
    expect(typed.filename).toBe('engagement-letter.docx');
    expect(typed.mimeType).toBe('application/vnd.openxmlformats-officedocument.wordprocessingml.document');
    expect(typed.sizeBytes).toBe(87456);
    expect(typed.hasSelection).toBe(false);
    expect(typed.selectionText).toBeUndefined();

    // FR-57 shape conformance — no extra keys.
    expect(Object.keys(typed).sort()).toEqual(['filename', 'hasSelection', 'mimeType', 'sizeBytes', 'widgetType']);
  });

  test('accepts R4 contentType field when canonical mimeType absent', () => {
    const result = documentViewerWidgetVisibility({
      filename: 'preview.pdf',
      contentType: 'application/pdf',
      sizeBytes: 1024,
      textContent: 'unused',
    });
    expect((result as SerializedDocumentViewerState).mimeType).toBe('application/pdf');
  });

  // POML ui-test #2 — DocumentViewer with selection >200 chars.
  test('selectionText is truncated to 200 chars when input exceeds cap', () => {
    const longSelection = 'a'.repeat(SELECTION_TEXT_CAP_CHARS + 500);
    const result = documentViewerWidgetVisibility({
      filename: 'evidence.pdf',
      mimeType: 'application/pdf',
      sizeBytes: 50_000,
      hasSelection: true,
      selectionText: longSelection,
    });
    const typed = result as SerializedDocumentViewerState;
    expect(typed.hasSelection).toBe(true);
    expect(typed.selectionText).toBeDefined();
    expect(typed.selectionText!.length).toBe(SELECTION_TEXT_CAP_CHARS);
    expect(typed.selectionText).toBe('a'.repeat(SELECTION_TEXT_CAP_CHARS));
  });

  test('selectionText is preserved verbatim when ≤ 200 chars', () => {
    const shortSelection = 'A snippet the user highlighted.';
    const result = documentViewerWidgetVisibility({
      filename: 'evidence.pdf',
      mimeType: 'application/pdf',
      sizeBytes: 50_000,
      hasSelection: true,
      selectionText: shortSelection,
    });
    expect((result as SerializedDocumentViewerState).selectionText).toBe(shortSelection);
  });

  test('omits selectionText when hasSelection is false', () => {
    const result = documentViewerWidgetVisibility({
      filename: 'evidence.pdf',
      mimeType: 'application/pdf',
      sizeBytes: 50_000,
      hasSelection: false,
      selectionText: 'should-not-appear',
    });
    expect((result as SerializedDocumentViewerState).selectionText).toBeUndefined();
  });

  test('opts out (returns null) when filename is missing', () => {
    expect(documentViewerWidgetVisibility({})).toBeNull();
    expect(documentViewerWidgetVisibility({ mimeType: 'text/plain' })).toBeNull();
    expect(documentViewerWidgetVisibility({ filename: '' })).toBeNull();
    expect(documentViewerWidgetVisibility(null)).toBeNull();
    expect(documentViewerWidgetVisibility(undefined)).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// Dashboard derivation
// ---------------------------------------------------------------------------

describe('dashboardWidgetVisibility', () => {
  test('FR-57 shape: emits widgetType + dashboardName + lastViewedSection (when present)', () => {
    const result = dashboardWidgetVisibility({
      dashboardName: 'Corporate Workspace',
      lastViewedSection: 'matters-section',
    });
    expect(result).not.toBeNull();
    const typed = result as SerializedDashboardState;
    expect(typed.widgetType).toBe('Dashboard');
    expect(typed.dashboardName).toBe('Corporate Workspace');
    expect(typed.lastViewedSection).toBe('matters-section');

    // FR-57 shape conformance.
    expect(Object.keys(typed).sort()).toEqual(['dashboardName', 'lastViewedSection', 'widgetType']);
  });

  test('accepts R4 WorkspaceLayoutWidgetData (layoutName)', () => {
    const result = dashboardWidgetVisibility({
      layoutId: 'guid-abc',
      layoutName: 'Calendar',
    });
    expect((result as SerializedDashboardState).dashboardName).toBe('Calendar');
  });

  test('omits lastViewedSection when not provided', () => {
    const result = dashboardWidgetVisibility({ dashboardName: 'Daily Briefing' });
    const typed = result as SerializedDashboardState;
    expect(typed.lastViewedSection).toBeUndefined();
    expect(Object.keys(typed).sort()).toEqual(['dashboardName', 'widgetType']);
  });

  test('NEVER exposes chart data or section payloads (ADR-015 binding)', () => {
    // Even when the input carries chart-data / section-payload fields,
    // the derivation MUST NOT propagate them. This is the binding privacy
    // property tested at the registry boundary.
    const result = dashboardWidgetVisibility({
      dashboardName: 'Corporate Workspace',
      lastViewedSection: 'matters',
      sections: [
        { id: 'matters', rows: [{ id: 'm1', client: 'Acme' }] },
        { id: 'invoices', rows: [{ id: 'i1', amount: 12_345 }] },
      ],
      chartData: { x: [1, 2, 3], y: [4, 5, 6] },
      revenue: 1_234_567,
    });
    const typed = result as SerializedDashboardState;
    expect(typed).toEqual({
      widgetType: 'Dashboard',
      dashboardName: 'Corporate Workspace',
      lastViewedSection: 'matters',
    });
    // Negative assertion: no propagated payloads (cast to access).
    expect((typed as Record<string, unknown>).sections).toBeUndefined();
    expect((typed as Record<string, unknown>).chartData).toBeUndefined();
    expect((typed as Record<string, unknown>).revenue).toBeUndefined();
  });

  test('opts out (returns null) when no dashboard name is present', () => {
    expect(dashboardWidgetVisibility({})).toBeNull();
    expect(dashboardWidgetVisibility({ lastViewedSection: 'orphan' })).toBeNull();
    expect(dashboardWidgetVisibility(null)).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// Table derivation
// ---------------------------------------------------------------------------

describe('tableWidgetVisibility', () => {
  test('FR-57 shape: emits widgetType + rowCount + sortColumn + filteredColumns + selectedRows', () => {
    const result = tableWidgetVisibility({
      rowCount: 47,
      sortColumn: 'createdOn',
      filteredColumns: ['status', 'priority'],
      selectedRows: ['guid-1', 'guid-2', 'guid-3'],
    });
    expect(result).not.toBeNull();
    const typed = result as SerializedTableState;
    expect(typed.widgetType).toBe('Table');
    expect(typed.rowCount).toBe(47);
    expect(typed.sortColumn).toBe('createdOn');
    expect(typed.filteredColumns).toEqual(['status', 'priority']);

    // CRITICAL privacy assertion: selectedRows is a COUNT, NOT the row-id list.
    expect(typed.selectedRows).toBe(3);
    expect(typeof typed.selectedRows).toBe('number');

    // FR-57 shape conformance.
    expect(Object.keys(typed).sort()).toEqual([
      'filteredColumns',
      'rowCount',
      'selectedRows',
      'sortColumn',
      'widgetType',
    ]);
  });

  test('NEVER exposes row IDs (ADR-015 binding — selectedRows is cardinality only)', () => {
    const rowIds = Array.from({ length: 100 }, (_, i) => `guid-${i}`);
    const result = tableWidgetVisibility({
      rowCount: 1_000,
      selectedRows: rowIds,
    });
    const typed = result as SerializedTableState;
    expect(typed.selectedRows).toBe(100);
    // Negative assertion: the row ID list MUST NOT appear anywhere in the output.
    const serialized = JSON.stringify(typed);
    for (const id of rowIds) {
      expect(serialized).not.toContain(id);
    }
  });

  test('NEVER exposes row cell content (ADR-015 binding)', () => {
    const result = tableWidgetVisibility({
      rowCount: 5,
      sortColumn: 'name',
      filteredColumns: ['status'],
      selectedRows: ['guid-1'],
      // Pretend the upstream stuffed cell content; we must NEVER pass it.
      rows: [{ id: 'guid-1', name: 'Acme Holdings', amount: 12_345_678, ssn: '999-99-9999' }],
      data: { sensitive: 'should-not-appear' },
    });
    const typed = result as SerializedTableState;
    const serialized = JSON.stringify(typed);
    expect(serialized).not.toContain('Acme Holdings');
    expect(serialized).not.toContain('12345678');
    expect(serialized).not.toContain('ssn');
    expect(serialized).not.toContain('999-99-9999');
    expect(serialized).not.toContain('sensitive');
    expect(serialized).not.toContain('should-not-appear');
  });

  test('omits optional fields when no current state', () => {
    const result = tableWidgetVisibility({ rowCount: 0 });
    const typed = result as SerializedTableState;
    expect(typed).toEqual({ widgetType: 'Table', rowCount: 0 });
    expect(typed.sortColumn).toBeUndefined();
    expect(typed.filteredColumns).toBeUndefined();
    expect(typed.selectedRows).toBeUndefined();
  });

  test('omits filteredColumns when empty array', () => {
    const result = tableWidgetVisibility({ rowCount: 12, filteredColumns: [] });
    expect((result as SerializedTableState).filteredColumns).toBeUndefined();
  });

  test('omits selectedRows when none selected', () => {
    const result = tableWidgetVisibility({ rowCount: 12, selectedRows: [] });
    expect((result as SerializedTableState).selectedRows).toBeUndefined();
  });

  test('opts out (returns null) when rowCount is missing or non-numeric', () => {
    expect(tableWidgetVisibility({})).toBeNull();
    expect(tableWidgetVisibility({ rowCount: 'not-a-number' })).toBeNull();
    expect(tableWidgetVisibility({ rowCount: NaN })).toBeNull();
    expect(tableWidgetVisibility(null)).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// Registry wiring (smoke test) — verifies the 4 derivations are wired into
// `WorkspaceWidgetRegistry` via the `register-*.ts` side-effect imports.
// The package barrel (`@spaarke/ai-widgets/index.ts`) loads them all at
// module evaluation time; importing the registry helpers below triggers the
// barrel load chain.
// ---------------------------------------------------------------------------

describe('registry wiring (task 072 + 073)', () => {
  // We DO NOT import the package barrel (`../../../index`) because it pulls
  // wizard widgets (e.g. CreateMatterWizardWidget) whose deep ui-components
  // imports (`@spaarke/ui-components/components/CreateMatterWizard`) cannot
  // be resolved in the jest environment (only the package root is mocked
  // above; deep imports aren't covered). Instead we directly import each
  // register-*.ts side-effect file we need to verify. Each performs its
  // `registerWorkspaceWidget` call at module-evaluation time.

  test('Summary + DocumentViewer registrations expose getVisibleState fns', async () => {
    // Trigger only the side effects we care about.
    await import('../register-document-viewer-widget');
    await import('../register-structured-output-stream-widget');
    const { getWorkspaceWidgetVisibleStateFn } = await import('../../../registry/WorkspaceWidgetRegistry');

    // Summary category — StructuredOutputStreamWidget registration.
    expect(typeof getWorkspaceWidgetVisibleStateFn('structured-output-stream')).toBe('function');

    // DocumentViewer category — DocumentViewerWidget registration.
    expect(typeof getWorkspaceWidgetVisibleStateFn('document-viewer')).toBe('function');
  });

  test('Dashboard + Table registrations expose getVisibleState fns', async () => {
    // register-workspace-widgets.ts wires the 'workspace' Dashboard mapping
    // AND the 5 Table mappings (documents-list / matters-list / projects-list
    // / invoices-list / work-assignments-list). It also touches the
    // `@spaarke/ai-outputs` chain which fails to resolve in jest — that's
    // why we late-mock it inline here.
    jest.doMock('@spaarke/ai-outputs/output-widgets/BudgetDashboardWidget', () => ({ default: () => null }), {
      virtual: true,
    });
    jest.doMock('@spaarke/ai-outputs/output-widgets/SearchResultsWidget', () => ({ default: () => null }), {
      virtual: true,
    });
    jest.doMock('@spaarke/ai-outputs/output-widgets/AnalysisEditorWidget', () => ({ default: () => null }), {
      virtual: true,
    });
    jest.doMock('@spaarke/ai-outputs/output-widgets/ContractComparisonWidget', () => ({ default: () => null }), {
      virtual: true,
    });
    jest.doMock('@spaarke/ai-outputs/output-widgets/StatusSummaryWidget', () => ({ default: () => null }), {
      virtual: true,
    });
    jest.doMock('@spaarke/ai-outputs/output-widgets/RecommendationWidget', () => ({ default: () => null }), {
      virtual: true,
    });
    jest.doMock('@spaarke/ai-outputs/output-widgets/ActionPlanWidget', () => ({ default: () => null }), {
      virtual: true,
    });

    await import('../register-workspace-widgets');
    const { getWorkspaceWidgetVisibleStateFn } = await import('../../../registry/WorkspaceWidgetRegistry');

    // Dashboard category — WorkspaceLayoutWidget registration ('workspace').
    expect(typeof getWorkspaceWidgetVisibleStateFn('workspace')).toBe('function');

    // Table category — 5 DataverseEntityViewWidget-backed registrations.
    expect(typeof getWorkspaceWidgetVisibleStateFn('documents-list')).toBe('function');
    expect(typeof getWorkspaceWidgetVisibleStateFn('matters-list')).toBe('function');
    expect(typeof getWorkspaceWidgetVisibleStateFn('projects-list')).toBe('function');
    expect(typeof getWorkspaceWidgetVisibleStateFn('invoices-list')).toBe('function');
    expect(typeof getWorkspaceWidgetVisibleStateFn('work-assignments-list')).toBe('function');
  });

  test('opt-out invariant: widgets that did not register a visibility function return undefined', async () => {
    // Force registry load chain via the same import path the previous test
    // used (registration is first-wins, so subsequent imports are no-ops).
    await import('../register-workspace-widgets');
    const { getWorkspaceWidgetVisibleStateFn } = await import('../../../registry/WorkspaceWidgetRegistry');

    // Pick widgets that intentionally do NOT contribute to agent prompts
    // (per FR-56 — visibility is opt-in, not retrofitted).
    expect(getWorkspaceWidgetVisibleStateFn('redline-viewer')).toBeUndefined();
    expect(getWorkspaceWidgetVisibleStateFn('email-compose')).toBeUndefined();
    expect(getWorkspaceWidgetVisibleStateFn('meeting-schedule')).toBeUndefined();
  });

  test('unknown widget type returns undefined', async () => {
    const { getWorkspaceWidgetVisibleStateFn } = await import('../../../registry/WorkspaceWidgetRegistry');
    expect(getWorkspaceWidgetVisibleStateFn('definitely-not-registered-xyz')).toBeUndefined();
  });
});
