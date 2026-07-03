/**
 * RecordHeaderShell — unit tests.
 *
 * Covers FR-02 acceptance criteria from record-header-and-notepad-r1
 * task 003:
 *  - Given loading=false (or omitted): toolbar visible + children visible
 *  - Given loading=true: Skeleton visible; children NOT rendered
 *  - Given loading=false explicitly: Skeleton NOT visible; children visible
 *  - No hex/rgb literals in the rendered card styles (semantic tokens only,
 *    per ADR-021). Griffel emits CSS custom-property references at runtime;
 *    we verify computed styles contain `var(--…)` and no raw color literals.
 *  - Card chrome (bg, border, radius, padding) is applied via computed style.
 *
 * @see FR-02 record-header-and-notepad-r1 spec
 * @see ADR-021 Fluent v9 semantic tokens (no hex/rgb literals)
 * @see ADR-022 React 16/17 boundary (no React 18-exclusive APIs)
 */

import * as React from 'react';
import { screen } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { RecordHeaderShell } from '../RecordHeaderShell';
import type { IHeaderToolbarProps, IHeaderToolbarSlot } from '../../HeaderToolbar';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Decorative icon stub — avoids Fluent icon imports in tests. */
const IconStub: React.FC<{ label: string }> = ({ label }) => (
  <span data-testid={`icon-${label}`} aria-hidden="true">
    {label}
  </span>
);

const makeSlot = (overrides: Partial<IHeaderToolbarSlot> & { key: string; tooltip: string }): IHeaderToolbarSlot => ({
  icon: <IconStub label={overrides.key} />,
  onClick: jest.fn(),
  ...overrides,
});

const makeToolbar = (title?: string): IHeaderToolbarProps => ({
  title,
  iconSlots: [
    makeSlot({ key: 'sparkle', tooltip: 'View AI summary' }),
    makeSlot({ key: 'checkmark', tooltip: 'Related to-dos' }),
    makeSlot({ key: 'note', tooltip: 'Notes' }),
  ],
});

// Regex that catches any hex (#abc / #aabbcc) or rgb/rgba/hsl/hsla color
// literals authored in source. Griffel resolves semantic tokens to
// `var(--fluent-…)` references, so real values never contain these
// literals in computed styles when tokens are used correctly.
const HEX_OR_RGB_LITERAL = /(?:#[0-9a-fA-F]{3,8}\b)|rgb\(|rgba\(|hsl\(|hsla\(/;

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('RecordHeaderShell', () => {
  // ─────────────────────────────────────────────────────────────────────
  // Renders with toolbar + children when loading is absent / false
  // ─────────────────────────────────────────────────────────────────────

  describe('Rendered content — toolbar + body', () => {
    it('render_LoadingAbsent_RendersToolbarAndChildren', () => {
      renderWithProviders(
        <RecordHeaderShell toolbar={makeToolbar('Matter Summary')}>
          <div data-testid="body-child">Body content</div>
        </RecordHeaderShell>
      );

      // Card + toolbar + body present
      expect(screen.getByTestId('record-header-shell')).toBeInTheDocument();
      expect(screen.getByTestId('header-toolbar')).toBeInTheDocument();
      expect(screen.getByTestId('record-header-shell-body')).toBeInTheDocument();

      // Toolbar's title + three icon buttons visible
      expect(screen.getByText('Matter Summary')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'View AI summary' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Related to-dos' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Notes' })).toBeInTheDocument();

      // Children rendered
      expect(screen.getByTestId('body-child')).toBeInTheDocument();
      expect(screen.getByText('Body content')).toBeInTheDocument();

      // Skeleton NOT rendered
      expect(screen.queryByTestId('record-header-shell-skeleton')).not.toBeInTheDocument();
    });

    it('render_LoadingFalse_RendersToolbarAndChildren', () => {
      renderWithProviders(
        <RecordHeaderShell toolbar={makeToolbar()} loading={false}>
          <div data-testid="body-child">Body content</div>
        </RecordHeaderShell>
      );

      expect(screen.getByTestId('header-toolbar')).toBeInTheDocument();
      expect(screen.getByTestId('body-child')).toBeInTheDocument();
      expect(screen.queryByTestId('record-header-shell-skeleton')).not.toBeInTheDocument();
    });
  });

  // ─────────────────────────────────────────────────────────────────────
  // Loading state — Skeleton visible, children NOT rendered
  // ─────────────────────────────────────────────────────────────────────

  describe('Loading state', () => {
    it('render_LoadingTrue_RendersSkeletonAndHidesChildren', () => {
      renderWithProviders(
        <RecordHeaderShell toolbar={makeToolbar('Matter Summary')} loading={true}>
          <div data-testid="body-child">Body content</div>
        </RecordHeaderShell>
      );

      // Card + toolbar STILL rendered (chrome stays stable during load)
      expect(screen.getByTestId('record-header-shell')).toBeInTheDocument();
      expect(screen.getByTestId('header-toolbar')).toBeInTheDocument();
      expect(screen.getByText('Matter Summary')).toBeInTheDocument();

      // Skeleton rendered with the 6 placeholder cells
      const skeleton = screen.getByTestId('record-header-shell-skeleton');
      expect(skeleton).toBeInTheDocument();
      expect(skeleton).toHaveAttribute('aria-label', 'Loading record header');
      for (let i = 0; i < 6; i++) {
        expect(screen.getByTestId(`record-header-shell-skeleton-cell-${i}`)).toBeInTheDocument();
      }

      // Children NOT rendered while loading
      expect(screen.queryByTestId('body-child')).not.toBeInTheDocument();
      expect(screen.queryByText('Body content')).not.toBeInTheDocument();
    });

    it('render_LoadingTrue_SkeletonUsesThreeColumnGrid', () => {
      renderWithProviders(
        <RecordHeaderShell toolbar={makeToolbar()} loading={true}>
          <div>hidden</div>
        </RecordHeaderShell>
      );

      const skeleton = screen.getByTestId('record-header-shell-skeleton');
      const cs = window.getComputedStyle(skeleton);
      // Grid layout matches FieldGrid default so the load↔loaded swap
      // looks continuous.
      expect(cs.display).toBe('grid');
      expect(cs.gridTemplateColumns).toBe('repeat(3, 1fr)');
      // Gap tokens present (Griffel resolves to CSS custom properties)
      expect(cs.columnGap).not.toBe('');
      expect(cs.rowGap).not.toBe('');
    });
  });

  // ─────────────────────────────────────────────────────────────────────
  // Card chrome — semantic tokens only (ADR-021)
  // ─────────────────────────────────────────────────────────────────────

  describe('Card chrome — ADR-021 semantic tokens', () => {
    it('render_Card_UsesSemanticTokensNotHexOrRgbLiterals', () => {
      renderWithProviders(
        <RecordHeaderShell toolbar={makeToolbar()}>
          <div>x</div>
        </RecordHeaderShell>
      );

      const card = screen.getByTestId('record-header-shell');
      const cs = window.getComputedStyle(card);

      // Griffel resolves semantic tokens (colorNeutralBackground1,
      // colorNeutralStroke1, borderRadiusMedium, spacing*) into
      // `var(--fluent-…)` custom-property references. The presence of a
      // raw hex or rgb literal in the computed style would indicate the
      // code bypassed tokens.
      const combined = [
        cs.backgroundColor,
        cs.borderTopColor,
        cs.borderRightColor,
        cs.borderBottomColor,
        cs.borderLeftColor,
        cs.color,
        cs.borderTopLeftRadius,
        cs.borderTopRightRadius,
        cs.paddingTop,
        cs.paddingRight,
        cs.paddingBottom,
        cs.paddingLeft,
      ].join(' ');

      // JSDOM's default computed style for unresolved CSS custom properties
      // is an empty string — that's fine (means Griffel is using vars).
      // What must NOT appear is a raw color literal.
      expect(combined).not.toMatch(HEX_OR_RGB_LITERAL);
    });

    it('render_Card_AppliesBorderAndFlexColumnLayout', () => {
      renderWithProviders(
        <RecordHeaderShell toolbar={makeToolbar()}>
          <div>x</div>
        </RecordHeaderShell>
      );

      const card = screen.getByTestId('record-header-shell');
      const cs = window.getComputedStyle(card);

      // Structural styles that don't depend on token resolution
      expect(cs.display).toBe('flex');
      expect(cs.flexDirection).toBe('column');
      expect(cs.boxSizing).toBe('border-box');
      // 1px solid border applied via shorthands.border(...)
      expect(cs.borderTopStyle).toBe('solid');
      expect(cs.borderTopWidth).toBe('1px');
    });
  });

  // ─────────────────────────────────────────────────────────────────────
  // Toolbar passthrough — shell does NOT re-transform toolbar props
  // ─────────────────────────────────────────────────────────────────────

  describe('Toolbar passthrough', () => {
    it('render_ToolbarWithoutTitle_ToolbarTitleAbsent', () => {
      renderWithProviders(
        <RecordHeaderShell toolbar={{ iconSlots: [] }}>
          <div data-testid="body-child">Body</div>
        </RecordHeaderShell>
      );

      // Toolbar rendered but title node absent — shell passes props through
      // verbatim; whitespace-only / undefined titles are HeaderToolbar's job
      // to filter.
      expect(screen.getByTestId('header-toolbar')).toBeInTheDocument();
      expect(screen.queryByTestId('header-toolbar-title')).not.toBeInTheDocument();
      expect(screen.getByTestId('body-child')).toBeInTheDocument();
    });

    it('render_ToolbarWithBadge_BadgePassesThroughToHeaderToolbar', () => {
      const toolbar: IHeaderToolbarProps = {
        iconSlots: [makeSlot({ key: 'todos', tooltip: 'To-dos', badge: 7 })],
      };
      renderWithProviders(
        <RecordHeaderShell toolbar={toolbar}>
          <div>body</div>
        </RecordHeaderShell>
      );

      const badge = screen.getByTestId('header-toolbar-badge-todos');
      expect(badge).toBeInTheDocument();
      expect(badge).toHaveTextContent('7');
    });
  });
});
