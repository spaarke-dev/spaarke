/**
 * HeaderToolbar Component Tests
 *
 * Verifies FR-01 of record-header-and-notepad-r1:
 *   - Renders with 0 / 1 / N iconSlots
 *   - CounterBadge appears only when badge value > 0 (positive finite integer)
 *   - CounterBadge suppressed for undefined, 0, negative, non-finite
 *   - Tooltip content is reachable via aria-label on the icon-only button
 *   - Disabled prop disables the click handler
 *   - Title renders when provided; absent when omitted
 *   - Title truncates on overflow (CSS textOverflow: ellipsis)
 *
 * @see ADR-012 Shared component library
 * @see ADR-021 Fluent UI v9 semantic tokens
 * @see ADR-022 React 16/17 boundary (no React 18-exclusive APIs)
 * @see projects/record-header-and-notepad-r1/spec.md#fr-01
 */

import * as React from 'react';
import { screen, fireEvent } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { HeaderToolbar } from '../HeaderToolbar';
import type { IHeaderToolbarSlot } from '../types';

// A stable, decorative icon stub. Keeps the tests free of Fluent icon imports.
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

describe('HeaderToolbar', () => {
  // ─────────────────────────────────────────────────────────────────────
  // Basic rendering — 0 / 1 / N slots
  // ─────────────────────────────────────────────────────────────────────

  describe('Rendering with iconSlots', () => {
    it('render_ZeroSlots_RendersEmptyIconStrip', () => {
      renderWithProviders(<HeaderToolbar iconSlots={[]} />);

      // Toolbar root is present, icon strip is present but has no buttons
      expect(screen.getByTestId('header-toolbar')).toBeInTheDocument();
      const strip = screen.getByTestId('header-toolbar-icons');
      expect(strip).toBeInTheDocument();
      expect(strip.querySelectorAll('button')).toHaveLength(0);
    });

    it('render_OneSlot_RendersSingleIconWithTooltip', () => {
      const slots = [makeSlot({ key: 'sparkle', tooltip: 'View AI summary' })];

      renderWithProviders(<HeaderToolbar iconSlots={slots} />);

      // Button reachable by its accessible name (from Tooltip relationship="label")
      expect(screen.getByRole('button', { name: 'View AI summary' })).toBeInTheDocument();
      expect(screen.getByTestId('icon-sparkle')).toBeInTheDocument();
      expect(screen.getAllByRole('button')).toHaveLength(1);
    });

    it('render_ThreeSlots_RendersThreeIcons', () => {
      const slots = [
        makeSlot({ key: 'sparkle', tooltip: 'View AI summary' }),
        makeSlot({ key: 'checkmark', tooltip: 'Related to-dos' }),
        makeSlot({ key: 'note', tooltip: 'Notes' }),
      ];

      renderWithProviders(<HeaderToolbar iconSlots={slots} />);

      expect(screen.getAllByRole('button')).toHaveLength(3);
      expect(screen.getByRole('button', { name: 'View AI summary' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Related to-dos' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Notes' })).toBeInTheDocument();
    });
  });

  // ─────────────────────────────────────────────────────────────────────
  // Badge behavior (FR-01 acceptance: badge suppressed on 0 / undefined)
  // ─────────────────────────────────────────────────────────────────────

  describe('Badge behavior', () => {
    it('render_BadgePositive_RendersCounterBadgeWithValue', () => {
      const slots = [makeSlot({ key: 'todos', tooltip: 'To-dos', badge: 5 })];

      renderWithProviders(<HeaderToolbar iconSlots={slots} />);

      const badge = screen.getByTestId('header-toolbar-badge-todos');
      expect(badge).toBeInTheDocument();
      // Fluent CounterBadge renders the count as visible text
      expect(badge).toHaveTextContent('5');
    });

    it('render_BadgeZero_SuppressesBadge', () => {
      const slots = [makeSlot({ key: 'todos', tooltip: 'To-dos', badge: 0 })];

      renderWithProviders(<HeaderToolbar iconSlots={slots} />);

      expect(screen.queryByTestId('header-toolbar-badge-todos')).not.toBeInTheDocument();
    });

    it('render_BadgeUndefined_SuppressesBadge', () => {
      const slots = [makeSlot({ key: 'todos', tooltip: 'To-dos' })];

      renderWithProviders(<HeaderToolbar iconSlots={slots} />);

      expect(screen.queryByTestId('header-toolbar-badge-todos')).not.toBeInTheDocument();
    });

    it('render_BadgeNegative_SuppressesBadge', () => {
      const slots = [makeSlot({ key: 'todos', tooltip: 'To-dos', badge: -3 })];

      renderWithProviders(<HeaderToolbar iconSlots={slots} />);

      expect(screen.queryByTestId('header-toolbar-badge-todos')).not.toBeInTheDocument();
    });

    it('render_BadgeNonFinite_SuppressesBadge', () => {
      const slots = [makeSlot({ key: 'todos', tooltip: 'To-dos', badge: NaN })];

      renderWithProviders(<HeaderToolbar iconSlots={slots} />);

      expect(screen.queryByTestId('header-toolbar-badge-todos')).not.toBeInTheDocument();
    });
  });

  // ─────────────────────────────────────────────────────────────────────
  // Accessibility — tooltip content is reachable
  // ─────────────────────────────────────────────────────────────────────

  describe('Accessibility', () => {
    it('render_Slot_TooltipTextIsAccessibleName', () => {
      const slots = [makeSlot({ key: 'ai', tooltip: 'View AI summary' })];

      renderWithProviders(<HeaderToolbar iconSlots={slots} />);

      const button = screen.getByRole('button', { name: 'View AI summary' });
      // aria-label wired from tooltip so screen readers announce the label
      expect(button).toHaveAttribute('aria-label', 'View AI summary');
    });
  });

  // ─────────────────────────────────────────────────────────────────────
  // Disabled behavior
  // ─────────────────────────────────────────────────────────────────────

  describe('Disabled behavior', () => {
    it('click_DisabledSlot_DoesNotFireHandler', () => {
      const onClick = jest.fn();
      const slots = [makeSlot({ key: 'ai', tooltip: 'View AI summary', onClick, disabled: true })];

      renderWithProviders(<HeaderToolbar iconSlots={slots} />);

      const button = screen.getByRole('button', { name: 'View AI summary' });
      expect(button).toBeDisabled();

      fireEvent.click(button);
      expect(onClick).not.toHaveBeenCalled();
    });

    it('click_EnabledSlot_FiresHandler', () => {
      const onClick = jest.fn();
      const slots = [makeSlot({ key: 'ai', tooltip: 'View AI summary', onClick })];

      renderWithProviders(<HeaderToolbar iconSlots={slots} />);

      fireEvent.click(screen.getByRole('button', { name: 'View AI summary' }));
      expect(onClick).toHaveBeenCalledTimes(1);
    });
  });

  // ─────────────────────────────────────────────────────────────────────
  // Title rendering + overflow truncation
  // ─────────────────────────────────────────────────────────────────────

  describe('Title', () => {
    it('render_WithTitle_RendersTitleText', () => {
      renderWithProviders(<HeaderToolbar title="Matter Summary" iconSlots={[]} />);

      expect(screen.getByTestId('header-toolbar-title')).toBeInTheDocument();
      expect(screen.getByText('Matter Summary')).toBeInTheDocument();
    });

    it('render_WithoutTitle_DoesNotRenderTitleNode', () => {
      renderWithProviders(<HeaderToolbar iconSlots={[]} />);

      expect(screen.queryByTestId('header-toolbar-title')).not.toBeInTheDocument();
    });

    it('render_EmptyStringTitle_DoesNotRenderTitleNode', () => {
      renderWithProviders(<HeaderToolbar title="   " iconSlots={[]} />);

      // Whitespace-only titles are treated as absent to avoid an empty text run
      expect(screen.queryByTestId('header-toolbar-title')).not.toBeInTheDocument();
    });

    it('render_LongTitle_UsesEllipsisTruncation', () => {
      renderWithProviders(
        <HeaderToolbar
          title="A very long matter title that should overflow the toolbar and truncate with ellipsis"
          iconSlots={[]}
        />
      );

      const title = screen.getByTestId('header-toolbar-title');
      const style = window.getComputedStyle(title);
      // Griffel emits `text-overflow: ellipsis` — verify via computed style
      expect(style.textOverflow).toBe('ellipsis');
      expect(style.whiteSpace).toBe('nowrap');
      expect(style.overflow).toBe('hidden');
    });
  });
});
