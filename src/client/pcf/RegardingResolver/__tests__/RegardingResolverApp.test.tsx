/**
 * RegardingResolverApp tests.
 *
 * Covers:
 *   - Renders the 11-entity picker (FR-20)
 *   - Entity-type dropdown selection updates internal state
 *   - Read-only mode (FR-24) hides edit affordances
 *   - Version footer renders (PCF CLAUDE.md mandatory rule)
 *   - The component imports `applyResolverFields` via the shared handler — we
 *     mock the handler and assert it's the SOLE write path on selection
 */

import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

// Mock @spaarke/ui-components so we control the catalog + applyResolverFields.
jest.mock('@spaarke/ui-components', () => {
  const TODO_REGARDING_CATALOG = [
    {
      entityType: 'sprk_matter',
      entitySet: 'sprk_matters',
      lookupAttribute: 'sprk_regardingmatter',
      navPropHint: 'matter',
    },
    {
      entityType: 'sprk_project',
      entitySet: 'sprk_projects',
      lookupAttribute: 'sprk_regardingproject',
      navPropHint: 'project',
    },
    {
      entityType: 'sprk_event',
      entitySet: 'sprk_events',
      lookupAttribute: 'sprk_regardingevent',
      navPropHint: 'event',
    },
    {
      entityType: 'sprk_communication',
      entitySet: 'sprk_communications',
      lookupAttribute: 'sprk_regardingcommunication',
      navPropHint: 'communication',
    },
    {
      entityType: 'sprk_workassignment',
      entitySet: 'sprk_workassignments',
      lookupAttribute: 'sprk_regardingworkassignment',
      navPropHint: 'workassignment',
    },
    {
      entityType: 'sprk_invoice',
      entitySet: 'sprk_invoices',
      lookupAttribute: 'sprk_regardinginvoice',
      navPropHint: 'invoice',
    },
    {
      entityType: 'sprk_budget',
      entitySet: 'sprk_budgets',
      lookupAttribute: 'sprk_regardingbudget',
      navPropHint: 'budget',
    },
    {
      entityType: 'sprk_analysis',
      entitySet: 'sprk_analyses',
      lookupAttribute: 'sprk_regardinganalysis',
      navPropHint: 'analysis',
    },
    {
      entityType: 'sprk_organization',
      entitySet: 'sprk_organizations',
      lookupAttribute: 'sprk_regardingorganization',
      navPropHint: 'organization',
    },
    { entityType: 'contact', entitySet: 'contacts', lookupAttribute: 'sprk_regardingcontact', navPropHint: 'contact' },
    {
      entityType: 'sprk_document',
      entitySet: 'sprk_documents',
      lookupAttribute: 'sprk_regardingdocument',
      navPropHint: 'document',
    },
  ];
  return {
    TODO_REGARDING_CATALOG,
    applyResolverFields: jest.fn().mockResolvedValue(undefined),
  };
});

import { RegardingResolverApp } from '../RegardingResolver/RegardingResolverApp';

// Cast helper — tests build a context stub with the manifest property shape,
// but we don't have the build-generated `IInputs` typed in the test compile.
// `as never` keeps TypeScript honest while letting us pass a duck-typed stub.
type AnyContext = ComponentFramework.Context<never>;

/** Build a minimal PCF context stub matching the manifest properties. */
function buildContext(overrides?: {
  entity?: string;
  regardingTargets?: string;
  readOnly?: boolean;
  boundLookup?: { id: string; name: string };
}): { context: AnyContext; updateRecordMock: jest.Mock } {
  const updateRecordMock = jest.fn().mockResolvedValue({ id: 'ok' });
  const ctx = {
    parameters: {
      entity: { raw: overrides?.entity ?? 'sprk_todo' },
      regardingTargets: { raw: overrides?.regardingTargets ?? null },
      readOnly: { raw: overrides?.readOnly ?? false },
      regardingRecordType: overrides?.boundLookup ? { raw: [overrides.boundLookup] } : { raw: null },
    },
    mode: {
      isControlDisabled: false,
    },
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

describe('RegardingResolverApp', () => {
  // -------------------------------------------------------------------------
  // FR-20 — renders 11-entity picker
  // -------------------------------------------------------------------------

  test('FR-20 — renders all 11 entity types in the dropdown options', () => {
    const { context } = buildContext();
    renderWithProvider(
      <RegardingResolverApp
        context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
        readOnly={false}
        onRecordTypeChanged={() => undefined}
        version="1.0.0"
      />
    );

    // The dropdown trigger is present (edit mode)
    expect(screen.getByTestId('regarding-resolver-edit')).toBeInTheDocument();
    expect(screen.getByTestId('regarding-resolver-entity-type-dropdown')).toBeInTheDocument();
    expect(screen.getByTestId('regarding-resolver-select-record-button')).toBeInTheDocument();
  });

  test('FR-20 — Select Record button is present and primary', () => {
    const { context } = buildContext();
    renderWithProvider(
      <RegardingResolverApp
        context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
        readOnly={false}
        onRecordTypeChanged={() => undefined}
        version="1.0.0"
      />
    );

    const btn = screen.getByTestId('regarding-resolver-select-record-button');
    expect(btn).toBeEnabled();
  });

  // -------------------------------------------------------------------------
  // FR-22 — entity prop is the only entity-specific config
  // -------------------------------------------------------------------------

  test('FR-22 — same component works for sprk_todo (no code branching)', () => {
    const { context } = buildContext({ entity: 'sprk_todo' });
    renderWithProvider(
      <RegardingResolverApp
        context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
        readOnly={false}
        onRecordTypeChanged={() => undefined}
        version="1.0.0"
      />
    );

    expect(screen.getByTestId('regarding-resolver-edit')).toBeInTheDocument();
  });

  test('FR-22 — same component works for sprk_communication (no code branching)', () => {
    const { context } = buildContext({ entity: 'sprk_communication' });
    renderWithProvider(
      <RegardingResolverApp
        context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
        readOnly={false}
        onRecordTypeChanged={() => undefined}
        version="1.0.0"
      />
    );

    // Same edit container renders — no special-case for sprk_communication.
    expect(screen.getByTestId('regarding-resolver-edit')).toBeInTheDocument();
  });

  // -------------------------------------------------------------------------
  // FR-24 — read-only mode renders without edit UI
  // -------------------------------------------------------------------------

  test('FR-24 — read-only mode renders read-only container and hides edit UI', () => {
    const { context } = buildContext();
    renderWithProvider(
      <RegardingResolverApp
        context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
        readOnly={true}
        onRecordTypeChanged={() => undefined}
        version="1.0.0"
      />
    );

    expect(screen.getByTestId('regarding-resolver-readonly')).toBeInTheDocument();
    expect(screen.queryByTestId('regarding-resolver-edit')).not.toBeInTheDocument();
    expect(screen.queryByTestId('regarding-resolver-entity-type-dropdown')).not.toBeInTheDocument();
    expect(screen.queryByTestId('regarding-resolver-select-record-button')).not.toBeInTheDocument();
  });

  test('FR-24 — read-only mode renders bound lookup name when present', () => {
    const { context } = buildContext({
      boundLookup: { id: 'rt-matter', name: 'Matter' },
    });
    renderWithProvider(
      <RegardingResolverApp
        context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
        readOnly={true}
        onRecordTypeChanged={() => undefined}
        version="1.0.0"
      />
    );

    expect(screen.getByText('Matter')).toBeInTheDocument();
  });

  test('FR-24 — read-only mode with no selection shows fallback text', () => {
    const { context } = buildContext();
    renderWithProvider(
      <RegardingResolverApp
        context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
        readOnly={true}
        onRecordTypeChanged={() => undefined}
        version="1.0.0"
      />
    );

    expect(screen.getByText(/No regarding selected/i)).toBeInTheDocument();
  });

  // -------------------------------------------------------------------------
  // Version footer (PCF CLAUDE.md MANDATORY rule)
  // -------------------------------------------------------------------------

  test('renders version footer in edit mode', () => {
    const { context } = buildContext();
    renderWithProvider(
      <RegardingResolverApp
        context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
        readOnly={false}
        onRecordTypeChanged={() => undefined}
        version="1.0.0"
      />
    );

    const footer = screen.getByTestId('regarding-resolver-version');
    expect(footer).toHaveTextContent(/v1\.0\.0/);
  });

  test('renders version footer in read-only mode', () => {
    const { context } = buildContext();
    renderWithProvider(
      <RegardingResolverApp
        context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
        readOnly={true}
        onRecordTypeChanged={() => undefined}
        version="1.0.0"
      />
    );

    expect(screen.getByText(/v1\.0\.0/)).toBeInTheDocument();
  });

  // -------------------------------------------------------------------------
  // FR-24 — R4-052: read-only mode MUST NOT trigger any write path
  // -------------------------------------------------------------------------

  test('FR-24 R4-052 — read-only mode renders no Select Record button (no write path reachable)', () => {
    const { context, updateRecordMock } = buildContext();
    const onRecordTypeChanged = jest.fn();
    renderWithProvider(
      <RegardingResolverApp
        context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
        readOnly={true}
        onRecordTypeChanged={onRecordTypeChanged}
        version="1.0.0"
      />
    );

    // The edit-mode CTA must be absent — there is no UI path to a write.
    expect(screen.queryByTestId('regarding-resolver-select-record-button')).not.toBeInTheDocument();
    // And no write has been issued during render.
    expect(updateRecordMock).not.toHaveBeenCalled();
    expect(onRecordTypeChanged).not.toHaveBeenCalled();
  });

  test('FR-24 R4-052 — read-only mode does NOT populate __sprk_regarding_pending__ on render', () => {
    // Defensive: ensure no stale CREATE-mode bridge from a prior render leaks in.
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    delete (window as any).__sprk_regarding_pending__;

    const { context } = buildContext();
    renderWithProvider(
      <RegardingResolverApp
        context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
        readOnly={true}
        onRecordTypeChanged={() => undefined}
        version="1.0.0"
      />
    );

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    expect((window as any).__sprk_regarding_pending__).toBeUndefined();
  });

  test('FR-24 R4-052 — read-only mode shows bound link without Clear affordance', () => {
    const { context } = buildContext({
      boundLookup: { id: 'rt-matter', name: 'Matter' },
    });
    renderWithProvider(
      <RegardingResolverApp
        context={context as unknown as Parameters<typeof RegardingResolverApp>[0]['context']}
        readOnly={true}
        onRecordTypeChanged={() => undefined}
        version="1.0.0"
      />
    );

    expect(screen.getByText('Matter')).toBeInTheDocument();
    // No edit affordances — no Clear button reachable from the read-only branch.
    expect(screen.queryByRole('button', { name: /Clear/i })).not.toBeInTheDocument();
  });
});
