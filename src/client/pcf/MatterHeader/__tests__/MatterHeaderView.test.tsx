/**
 * MatterHeaderView unit tests.
 *
 * Verifies the FR-12 composition wiring:
 *  - Loading skeleton visible before Xrm resolves
 *  - 5 field labels render after retrieveRecord resolves
 *  - 2 toolbar slots (checkmark / annotation) + the AI Summary sparkle trigger render
 *  - Version footer rendered
 *  - Sparkle click invokes `aiSummary.onFetchSummary` and renders the `sprk_mattersummary`
 *    body (regression coverage for the v1.0.20 field-wiring fix — the popover
 *    previously read the never-populated `sprk_recordsummary` field)
 *  - Sparkle click renders the empty state when `sprk_mattersummary` is null
 *  - `useRecordFieldValues` is called with the exact 6-field FR-12 REVISED payload
 *
 * Strategy: `@spaarke/ui-components` is jest-mocked via PER-SUBPATH `jest.mock()`
 * calls (NOT the bare `'@spaarke/ui-components'` specifier) because
 * MatterHeaderView.tsx imports from deep `dist/*` subpaths (see that file's
 * v1.0.12 comment — this PCF's tsconfig uses legacy `moduleResolution: "node"`
 * and doesn't read the shared lib's `exports` map). A bare-specifier mock never
 * applies to those imports and silently no-ops — see `RelatedDocumentCount.test.tsx`
 * for the established per-subpath precedent this file now follows.
 *
 * `RecordHeaderShell` is stubbed with a minimal re-implementation of the
 * `aiSummary` → sparkle-trigger → popover flow (real behavior lives in
 * `HeaderToolbar` + `AiSummaryPopover`, both already covered by the shared
 * lib's own suites — duplicating that here would violate ADR-038 test-diet
 * criteria). The stub only needs to prove MatterHeaderView wires
 * `toolbar.aiSummary.onFetchSummary` correctly, not re-verify Popover a11y/UX.
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
// Mocks — one per deep `dist/*` subpath MatterHeaderView.tsx actually imports.
// ─────────────────────────────────────────────────────────────────────────────

const mockUseRecordFieldValues = jest.fn();
const mockUseRecordHeaderToolbarActions = jest.fn();

jest.mock('@spaarke/ui-components/dist/components/RecordHeader', () => {
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

    TextareaField: ({ label, value }: { label: string; value?: string }) =>
      React.createElement(
        'div',
        { 'data-testid': 'stub-textarea-field', 'data-label': label },
        React.createElement('span', { 'data-testid': 'stub-field-label' }, label),
        React.createElement('span', { 'data-testid': 'stub-field-value' }, value ?? '')
      ),

    // FieldGrid — pass-through container.
    FieldGrid: ({ children, columns }: { children: React.ReactNode; columns?: number }) =>
      React.createElement('div', { 'data-testid': 'stub-field-grid', 'data-columns': columns ?? 3 }, children),

    // RecordHeaderShell — pass-through card chrome + a minimal re-implementation
    // of the `aiSummary` → sparkle → popover flow (real impl: HeaderToolbar +
    // AiSummaryPopover, both covered by the shared lib's own test suites).
    RecordHeaderShell: ({
      children,
      toolbar,
      loading,
    }: {
      children: React.ReactNode;
      toolbar: {
        iconSlots: Array<{ key: string; tooltip: string; onClick: () => void; badge?: number }>;
        aiSummary?: { onFetchSummary: () => Promise<{ summary: string | null; tldr: string | null }> };
      };
      loading?: boolean;
    }) => {
      const [open, setOpen] = React.useState(false);
      const [summary, setSummary] = React.useState<{ summary: string | null; tldr: string | null } | null>(null);

      const handleSparkleClick = () => {
        setOpen((prev: boolean) => !prev);
        if (!summary && toolbar.aiSummary) {
          void toolbar.aiSummary.onFetchSummary().then(setSummary);
        }
      };

      return React.createElement(
        'div',
        { 'data-testid': 'stub-record-header-shell', 'data-loading': loading ? '1' : '0' },
        React.createElement(
          'div',
          { 'data-testid': 'stub-toolbar' },
          toolbar.aiSummary
            ? React.createElement(
                'button',
                {
                  'data-testid': 'stub-toolbar-slot-sparkle',
                  'aria-label': 'AI Summary',
                  onClick: handleSparkleClick,
                },
                'AI Summary'
              )
            : null,
          toolbar.iconSlots.map(slot =>
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
        open
          ? React.createElement(
              'div',
              { 'data-testid': 'stub-popover' },
              !summary
                ? React.createElement('span', { 'data-testid': 'stub-popover-loading' }, 'Loading…')
                : summary.summary
                  ? React.createElement('span', { 'data-testid': 'stub-popover-summary' }, summary.summary)
                  : React.createElement('span', { 'data-testid': 'stub-popover-empty' }, 'No summary yet')
            )
          : null,
        loading
          ? React.createElement('div', { 'data-testid': 'stub-skeleton' }, 'loading…')
          : React.createElement('div', { 'data-testid': 'stub-body' }, children)
      );
    },
  };
});

jest.mock('@spaarke/ui-components/dist/components/LookupField/LookupField', () => {
  const React = require('react');
  return {
    __esModule: true,
    LookupField: ({ label, value }: { label: string; value?: { name?: string } | null }) =>
      React.createElement(
        'div',
        { 'data-testid': 'stub-lookup-field', 'data-label': label },
        React.createElement('span', { 'data-testid': 'stub-field-label' }, label),
        React.createElement('span', { 'data-testid': 'stub-field-value' }, value?.name ?? '')
      ),
  };
});

jest.mock('@spaarke/ui-components/dist/hooks', () => ({
  __esModule: true,
  useRecordFieldValues: (...args: unknown[]) => mockUseRecordFieldValues(...args),
  useRecordHeaderToolbarActions: (...args: unknown[]) => mockUseRecordHeaderToolbarActions(...args),
}));

jest.mock('@spaarke/ui-components/dist/utils/xrmContext', () => ({
  __esModule: true,
  getXrm: jest.fn(() => null),
}));

import { MatterHeaderView } from '../control/MatterHeaderView';
import { CONTROL_VERSION } from '../control/version';

// ─────────────────────────────────────────────────────────────────────────────
// Fixtures — shape matches the REAL `useRecordFieldValues` contract (raw
// Dataverse WebAPI entity: `_<field>_value` + `...FormattedValue` for lookups).
// ─────────────────────────────────────────────────────────────────────────────

const MATTER_ID = '00000000-0000-0000-0000-000000000001';

const MATTER_RECORD = {
  sprk_matternumber: 'M-2026-001',
  sprk_mattername: 'Acme Litigation Matter',
  sprk_matterdescription: 'A moderately long matter description.',
  sprk_mattersummary: 'AI-generated summary body rendered inside the sparkle popover.',
  _sprk_mattertype_value: 'mt-1',
  '_sprk_mattertype_value@OData.Community.Display.V1.FormattedValue': 'Litigation',
  _sprk_practicearea_value: 'pa-1',
  '_sprk_practicearea_value@OData.Community.Display.V1.FormattedValue': 'Corporate',
};

/** Real hook (v1.0.10+) returns ONLY `{ toolbarProps }` — checkmark + annotation slots. */
function makeToolbarActionsResult() {
  return {
    toolbarProps: {
      iconSlots: [
        { key: 'checkmark', tooltip: 'Related to-dos', onClick: jest.fn(), badge: 0 },
        { key: 'annotation', tooltip: 'Notepad', onClick: jest.fn(), badge: 0 },
      ],
    },
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
  mockUseRecordHeaderToolbarActions.mockReturnValue(makeToolbarActionsResult());
});

// ─────────────────────────────────────────────────────────────────────────────
// Tests
// ─────────────────────────────────────────────────────────────────────────────

describe('MatterHeaderView', () => {
  it('requests the FR-12 REVISED 6-field payload from useRecordFieldValues', () => {
    mockUseRecordFieldValues.mockReturnValue({ values: null, loading: true, error: null });

    renderView();

    // Called on every render (>=1) — the v1.0.7 pending-changes-buffer reset
    // effect triggers one extra re-render on mount, so exact count isn't the
    // invariant under test; every call must carry the same FR-12 args.
    expect(mockUseRecordFieldValues.mock.calls.length).toBeGreaterThanOrEqual(1);
    const [entity, recordId, fields] =
      mockUseRecordFieldValues.mock.calls[mockUseRecordFieldValues.mock.calls.length - 1];
    expect(entity).toBe('sprk_matter');
    expect(recordId).toBe(MATTER_ID);
    expect(fields).toEqual([
      'sprk_matternumber',
      'sprk_mattername',
      '_sprk_mattertype_value',
      '_sprk_practicearea_value',
      'sprk_matterdescription',
      'sprk_mattersummary',
    ]);
  });

  it('renders the 5 FR-12 field labels after data load', () => {
    mockUseRecordFieldValues.mockReturnValue({ values: MATTER_RECORD, loading: false, error: null });

    renderView();

    // Skeleton absent; body present.
    expect(screen.queryByTestId('stub-skeleton')).toBeNull();
    expect(screen.getByTestId('stub-body')).toBeInTheDocument();

    expect(screen.getByTestId('stub-field-grid')).toHaveAttribute('data-columns', '3');
    expect(screen.getByText('Matter Number')).toBeInTheDocument();
    expect(screen.getByText('Matter Name')).toBeInTheDocument();
    expect(screen.getByText('Matter Type')).toBeInTheDocument();
    expect(screen.getByText('Practice Area')).toBeInTheDocument();
    expect(screen.getByText('Matter Description')).toBeInTheDocument();

    // Values wire through per prop flow — lookups resolve via `projectLookup`
    // from the `_field_value` + FormattedValue pair.
    expect(screen.getByText('M-2026-001')).toBeInTheDocument();
    expect(screen.getByText('Acme Litigation Matter')).toBeInTheDocument();
    expect(screen.getByText('Litigation')).toBeInTheDocument();
    expect(screen.getByText('Corporate')).toBeInTheDocument();
    expect(screen.getByText(MATTER_RECORD.sprk_matterdescription)).toBeInTheDocument();
  });

  it('renders the checkmark + annotation toolbar slots plus the AI Summary sparkle trigger', () => {
    mockUseRecordFieldValues.mockReturnValue({ values: MATTER_RECORD, loading: false, error: null });

    renderView();

    expect(screen.getByTestId('stub-toolbar-slot-sparkle')).toBeInTheDocument();
    expect(screen.getByTestId('stub-toolbar-slot-sparkle')).toHaveAttribute('aria-label', 'AI Summary');
    expect(screen.getByTestId('stub-toolbar-slot-checkmark')).toBeInTheDocument();
    expect(screen.getByTestId('stub-toolbar-slot-annotation')).toBeInTheDocument();
  });

  it('renders the version footer', () => {
    mockUseRecordFieldValues.mockReturnValue({ values: MATTER_RECORD, loading: false, error: null });

    renderView();

    const footer = screen.getByTestId('matter-header-version');
    expect(footer).toBeInTheDocument();
    expect(footer.textContent).toBe(`v${CONTROL_VERSION}`);
    expect(footer).toHaveAttribute('aria-hidden', 'true');
  });

  it('sparkle click fetches and renders the sprk_mattersummary body (v1.0.20 field-wiring regression guard)', async () => {
    mockUseRecordFieldValues.mockReturnValue({ values: MATTER_RECORD, loading: false, error: null });

    renderView();

    fireEvent.click(screen.getByTestId('stub-toolbar-slot-sparkle'));

    // Popover resolves asynchronously (matches real AiSummaryPopover's
    // lazy-fetch-on-open contract) — findBy* waits for the state update.
    const summaryEl = await screen.findByTestId('stub-popover-summary');
    expect(summaryEl).toHaveTextContent(MATTER_RECORD.sprk_mattersummary);
    expect(screen.queryByTestId('stub-popover-empty')).toBeNull();
  });

  it('renders the empty-state popover body when sprk_mattersummary is null', async () => {
    const recordNoSummary = { ...MATTER_RECORD, sprk_mattersummary: null };
    mockUseRecordFieldValues.mockReturnValue({ values: recordNoSummary, loading: false, error: null });

    renderView();

    fireEvent.click(screen.getByTestId('stub-toolbar-slot-sparkle'));

    const emptyEl = await screen.findByTestId('stub-popover-empty');
    expect(emptyEl).toHaveTextContent('No summary yet');
    expect(screen.queryByTestId('stub-popover-summary')).toBeNull();
  });

  it('renders the loading skeleton while useRecordFieldValues is loading', () => {
    mockUseRecordFieldValues.mockReturnValue({ values: null, loading: true, error: null });

    renderView();

    // Shell is `data-loading="1"` and skeleton stub is present; body absent.
    expect(screen.getByTestId('stub-record-header-shell')).toHaveAttribute('data-loading', '1');
    expect(screen.getByTestId('stub-skeleton')).toBeInTheDocument();
    expect(screen.queryByTestId('stub-body')).toBeNull();
    // Toolbar remains rendered even during load (per FR-02 spec — stable chrome).
    expect(screen.getByTestId('stub-toolbar-slot-sparkle')).toBeInTheDocument();
  });
});
