/**
 * MatterHeaderView unit tests.
 *
 * Verifies the FR-12 composition wiring:
 *  - Loading skeleton visible before Xrm resolves
 *  - 5 field labels render after retrieveRecord resolves
 *  - 3 toolbar slots (sparkle / checkmark / annotation) render
 *  - Version footer rendered
 *  - Sparkle click opens the Popover with the `sprk_recordsummary` body
 *  - Sparkle click with null `sprk_recordsummary` shows the empty state
 *  - `useRecordFieldValues` is called with the exact 6-field FR-12 REVISED payload
 *
 * Strategy: `@spaarke/ui-components` is jest-mocked with light stubs so this
 * test exercises MatterHeaderView's OWN wiring (props flow, popover content
 * plumbing, field-list constants). The end-to-end composition of the real
 * shared-lib primitives + hooks is already covered by the Phase 1 integration
 * test at
 * `src/client/shared/Spaarke.UI.Components/src/__tests__/recordHeader.integration.test.tsx`.
 * Duplicating that coverage here would violate ADR-038 test-diet criteria
 * (a MAINTAIN-class test at one KEEP path is enough).
 *
 * Wrapping in FluentProvider mirrors the reference PCFs' test-environment
 * pattern (jsdom has no host theme; the production PCF class uses
 * platform-library auto-theming instead — see MatterHeaderView.tsx).
 *
 * @see FR-12 in projects/record-header-and-notepad-r1/spec.md
 * @see docs/adr/ADR-038-testing-strategy.md
 */

import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

// ─────────────────────────────────────────────────────────────────────────────
// Mock @spaarke/ui-components — light stubs that make the view's wiring
// deterministically observable.
// ─────────────────────────────────────────────────────────────────────────────

const mockUseRecordFieldValues = jest.fn();
const mockUseRecordHeaderToolbarActions = jest.fn();

jest.mock('@spaarke/ui-components', () => {
  const React = require('react');
  return {
    __esModule: true,

    // Field renderers — stubs that render `label` + `value` verbatim so the
    // test can assert both prop flow and label presence.
    TextField: ({ label, value, required }: { label: string; value?: string; required?: boolean }) =>
      React.createElement(
        'div',
        { 'data-testid': 'stub-text-field', 'data-label': label, 'data-required': required ? '1' : '0' },
        React.createElement('span', { 'data-testid': 'stub-field-label' }, label),
        React.createElement('span', { 'data-testid': 'stub-field-value' }, value ?? '')
      ),

    // The RecordHeader flavor is aliased in the shared-lib top-level barrel;
    // the view imports it as `RecordHeaderLookupField`.
    RecordHeaderLookupField: ({ label, value }: { label: string; value?: { name?: string } | null }) =>
      React.createElement(
        'div',
        { 'data-testid': 'stub-lookup-field', 'data-label': label },
        React.createElement('span', { 'data-testid': 'stub-field-label' }, label),
        React.createElement('span', { 'data-testid': 'stub-field-value' }, value?.name ?? '')
      ),

    TextareaField: ({ label, value }: { label: string; value?: string }) =>
      React.createElement(
        'div',
        { 'data-testid': 'stub-textarea-field', 'data-label': label },
        React.createElement('span', { 'data-testid': 'stub-field-label' }, label),
        React.createElement('span', { 'data-testid': 'stub-field-value' }, value ?? '')
      ),

    // FieldGrid — pass-through container.
    FieldGrid: ({ children, columns }: { children: React.ReactNode; columns?: number }) =>
      React.createElement(
        'div',
        { 'data-testid': 'stub-field-grid', 'data-columns': columns ?? 3 },
        children
      ),

    // RecordHeaderShell — pass-through with toolbar + loading indicator.
    RecordHeaderShell: ({
      children,
      toolbar,
      loading,
    }: {
      children: React.ReactNode;
      toolbar: { iconSlots: Array<{ key: string; tooltip: string; onClick: () => void }> };
      loading?: boolean;
    }) =>
      React.createElement(
        'div',
        { 'data-testid': 'stub-record-header-shell', 'data-loading': loading ? '1' : '0' },
        React.createElement(
          'div',
          { 'data-testid': 'stub-toolbar' },
          toolbar.iconSlots.map((slot) =>
            React.createElement(
              'button',
              {
                key: slot.key,
                'data-testid': `stub-toolbar-slot-${slot.key}`,
                'aria-label': slot.tooltip,
                onClick: slot.onClick,
              },
              slot.tooltip
            )
          )
        ),
        loading
          ? React.createElement('div', { 'data-testid': 'stub-skeleton' }, 'loading…')
          : React.createElement('div', { 'data-testid': 'stub-body' }, children)
      ),

    useRecordFieldValues: (...args: unknown[]) => mockUseRecordFieldValues(...args),
    useRecordHeaderToolbarActions: (...args: unknown[]) => mockUseRecordHeaderToolbarActions(...args),
  };
});

import { MatterHeaderView } from '../control/MatterHeaderView';
import { CONTROL_VERSION } from '../control/version';

// ─────────────────────────────────────────────────────────────────────────────
// Fixtures
// ─────────────────────────────────────────────────────────────────────────────

const MATTER_ID = '00000000-0000-0000-0000-000000000001';

const MATTER_RECORD = {
  sprk_matternumber: 'M-2026-001',
  sprk_mattername: 'Acme Litigation Matter',
  sprk_matterdescription: 'A moderately long matter description.',
  sprk_recordsummary: 'AI-generated summary body rendered inside the sparkle popover.',
  sprk_mattertype: { id: 'mt-1', name: 'Litigation', entityType: 'sprk_mattertype' },
  sprk_practicearea: { id: 'pa-1', name: 'Corporate', entityType: 'sprk_practicearea' },
};

/**
 * Build a toolbar-actions return that keeps popover state controlled via a
 * shared ref so the test can drive it through the sparkle click.
 */
function makeToolbarActionsMock(recordSummary: string | null) {
  const state = { open: false };
  const setOpen = jest.fn((next: boolean) => {
    state.open = next;
  });

  return {
    getState: () => state,
    build: () => ({
      toolbarProps: {
        iconSlots: [
          { key: 'sparkle', tooltip: 'AI Summary', onClick: () => setOpen(!state.open) },
          { key: 'checkmark', tooltip: 'Related to-dos', onClick: jest.fn(), badge: 0 },
          { key: 'annotation', tooltip: 'Notepad', onClick: jest.fn(), badge: 0 },
        ],
      },
      sparklePopoverOpen: state.open,
      setSparklePopoverOpen: setOpen,
      sparklePopoverContent:
        recordSummary && recordSummary.length > 0
          ? <div data-testid="stub-popover-summary">{recordSummary}</div>
          : <div data-testid="stub-popover-empty">No summary yet</div>,
    }),
    setOpen,
  };
}

function renderView(recordId: string = MATTER_ID) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <MatterHeaderView recordId={recordId} />
    </FluentProvider>
  );
}

beforeEach(() => {
  mockUseRecordFieldValues.mockReset();
  mockUseRecordHeaderToolbarActions.mockReset();
});

// ─────────────────────────────────────────────────────────────────────────────
// Tests
// ─────────────────────────────────────────────────────────────────────────────

describe('MatterHeaderView', () => {
  it('requests the FR-12 REVISED 6-field payload from useRecordFieldValues', () => {
    mockUseRecordFieldValues.mockReturnValue({ values: null, loading: true, error: null });
    mockUseRecordHeaderToolbarActions.mockReturnValue(makeToolbarActionsMock(null).build());

    renderView();

    expect(mockUseRecordFieldValues).toHaveBeenCalledTimes(1);
    const [entity, recordId, fields] = mockUseRecordFieldValues.mock.calls[0];
    expect(entity).toBe('sprk_matter');
    expect(recordId).toBe(MATTER_ID);
    expect(fields).toEqual([
      'sprk_matternumber',
      'sprk_mattername',
      'sprk_mattertype',
      'sprk_practicearea',
      'sprk_matterdescription',
      'sprk_recordsummary',
    ]);
  });

  it('renders the 5 FR-12 field labels after data load', () => {
    mockUseRecordFieldValues.mockReturnValue({ values: MATTER_RECORD, loading: false, error: null });
    mockUseRecordHeaderToolbarActions.mockReturnValue(makeToolbarActionsMock(MATTER_RECORD.sprk_recordsummary).build());

    renderView();

    // Skeleton absent; body present.
    expect(screen.queryByTestId('stub-skeleton')).toBeNull();
    expect(screen.getByTestId('stub-body')).toBeInTheDocument();

    // Each field label rendered exactly once — assert the presence via
    // data-label attribute so we don't collide with the version footer text.
    expect(screen.getByTestId('stub-field-grid')).toHaveAttribute('data-columns', '3');
    expect(screen.getByText('Matter Number')).toBeInTheDocument();
    expect(screen.getByText('Matter Name')).toBeInTheDocument();
    expect(screen.getByText('Matter Type')).toBeInTheDocument();
    expect(screen.getByText('Practice Area')).toBeInTheDocument();
    expect(screen.getByText('Matter Description')).toBeInTheDocument();

    // Values wire through per prop flow.
    expect(screen.getByText('M-2026-001')).toBeInTheDocument();
    expect(screen.getByText('Acme Litigation Matter')).toBeInTheDocument();
    expect(screen.getByText('Litigation')).toBeInTheDocument();
    expect(screen.getByText('Corporate')).toBeInTheDocument();
    expect(screen.getByText(MATTER_RECORD.sprk_matterdescription)).toBeInTheDocument();
  });

  it('renders 3 toolbar slots (sparkle, checkmark, annotation)', () => {
    mockUseRecordFieldValues.mockReturnValue({ values: MATTER_RECORD, loading: false, error: null });
    mockUseRecordHeaderToolbarActions.mockReturnValue(makeToolbarActionsMock(MATTER_RECORD.sprk_recordsummary).build());

    renderView();

    expect(screen.getByTestId('stub-toolbar-slot-sparkle')).toBeInTheDocument();
    expect(screen.getByTestId('stub-toolbar-slot-checkmark')).toBeInTheDocument();
    expect(screen.getByTestId('stub-toolbar-slot-annotation')).toBeInTheDocument();
    expect(screen.getByTestId('stub-toolbar-slot-sparkle')).toHaveAttribute('aria-label', 'AI Summary');
  });

  it('renders the version footer', () => {
    mockUseRecordFieldValues.mockReturnValue({ values: MATTER_RECORD, loading: false, error: null });
    mockUseRecordHeaderToolbarActions.mockReturnValue(makeToolbarActionsMock(MATTER_RECORD.sprk_recordsummary).build());

    renderView();

    const footer = screen.getByTestId('matter-header-version');
    expect(footer).toBeInTheDocument();
    expect(footer.textContent).toBe(`v${CONTROL_VERSION}`);
    expect(footer).toHaveAttribute('aria-hidden', 'true');
  });

  it('passes sprk_recordsummary to useRecordHeaderToolbarActions.recordSummary and renders it in the popover on sparkle click', () => {
    mockUseRecordFieldValues.mockReturnValue({ values: MATTER_RECORD, loading: false, error: null });
    // Popover starts open so we can observe the summary body without needing
    // rerenders. Real hook uses controlled state; this stub reflects that.
    const actions = makeToolbarActionsMock(MATTER_RECORD.sprk_recordsummary);
    mockUseRecordHeaderToolbarActions.mockReturnValue({
      ...actions.build(),
      sparklePopoverOpen: true,
    });

    renderView();

    // The hook received recordSummary — verify via the mock's call args.
    expect(mockUseRecordHeaderToolbarActions).toHaveBeenCalledWith(
      expect.objectContaining({
        entity: 'sprk_matter',
        recordId: MATTER_ID,
        recordSummary: MATTER_RECORD.sprk_recordsummary,
      })
    );

    // Popover open → summary body rendered.
    expect(screen.getByTestId('stub-popover-summary')).toHaveTextContent(MATTER_RECORD.sprk_recordsummary);
    expect(screen.queryByTestId('stub-popover-empty')).toBeNull();

    // Sparkle button click toggles the setter.
    fireEvent.click(screen.getByTestId('stub-toolbar-slot-sparkle'));
    expect(actions.setOpen).toHaveBeenCalled();
  });

  it('renders the empty-state popover body when sprk_recordsummary is null', () => {
    const recordNoSummary = { ...MATTER_RECORD, sprk_recordsummary: null };
    mockUseRecordFieldValues.mockReturnValue({ values: recordNoSummary, loading: false, error: null });
    const actions = makeToolbarActionsMock(null);
    mockUseRecordHeaderToolbarActions.mockReturnValue({
      ...actions.build(),
      sparklePopoverOpen: true,
    });

    renderView();

    expect(mockUseRecordHeaderToolbarActions).toHaveBeenCalledWith(
      expect.objectContaining({ recordSummary: null })
    );
    expect(screen.getByTestId('stub-popover-empty')).toHaveTextContent('No summary yet');
    expect(screen.queryByTestId('stub-popover-summary')).toBeNull();
  });

  it('renders the loading skeleton while useRecordFieldValues is loading', () => {
    mockUseRecordFieldValues.mockReturnValue({ values: null, loading: true, error: null });
    mockUseRecordHeaderToolbarActions.mockReturnValue(makeToolbarActionsMock(null).build());

    renderView();

    // Shell is `data-loading="1"` and skeleton stub is present; body absent.
    expect(screen.getByTestId('stub-record-header-shell')).toHaveAttribute('data-loading', '1');
    expect(screen.getByTestId('stub-skeleton')).toBeInTheDocument();
    expect(screen.queryByTestId('stub-body')).toBeNull();
    // Toolbar remains rendered even during load (per FR-02 spec — stable chrome).
    expect(screen.getByTestId('stub-toolbar-slot-sparkle')).toBeInTheDocument();
  });
});
