/**
 * PaneHeader Component Tests
 *
 * Verifies the FR-01 canonical pane-header primitive:
 *   - title text renders
 *   - icon prop renders inside the brand-foreground-colored wrapper
 *   - rightSlot prop renders inside the trailing-edge wrapper
 *   - structural snapshot is stable across renders
 *
 * @see ADR-012 Shared component library
 * @see ADR-021 Fluent UI v9 design tokens
 */

import * as React from 'react';
import { screen } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { PaneHeader } from '../PaneHeader';

describe('PaneHeader', () => {
  // ─────────────────────────────────────────────────────────────────────
  // Title rendering
  // ─────────────────────────────────────────────────────────────────────

  describe('Title rendering', () => {
    it('render_TitleOnly_ShowsTitleText', () => {
      renderWithProviders(<PaneHeader title="Context" />);

      expect(screen.getByText('Context')).toBeInTheDocument();
    });

    it('render_TitleOnly_RendersHeaderRoot', () => {
      renderWithProviders(<PaneHeader title="Context" />);

      const root = screen.getByTestId('pane-header');
      expect(root).toBeInTheDocument();
      // Semantic <header> element for accessibility (ARIA landmark)
      expect(root.tagName.toLowerCase()).toBe('header');
    });

    it('render_TitleOnly_DoesNotRenderIconOrRightSlot', () => {
      renderWithProviders(<PaneHeader title="Context" />);

      expect(screen.queryByTestId('pane-header-icon')).not.toBeInTheDocument();
      expect(screen.queryByTestId('pane-header-right-slot')).not.toBeInTheDocument();
    });
  });

  // ─────────────────────────────────────────────────────────────────────
  // Icon prop
  // ─────────────────────────────────────────────────────────────────────

  describe('Icon prop', () => {
    it('render_WithIcon_RendersIconWrapper', () => {
      renderWithProviders(<PaneHeader title="Workspace" icon={<span data-testid="custom-icon">icon-glyph</span>} />);

      expect(screen.getByTestId('pane-header-icon')).toBeInTheDocument();
      expect(screen.getByTestId('custom-icon')).toBeInTheDocument();
      expect(screen.getByText('icon-glyph')).toBeInTheDocument();
    });

    it('render_WithIcon_IconWrapperIsAriaHidden', () => {
      renderWithProviders(<PaneHeader title="Workspace" icon={<span data-testid="custom-icon">icon-glyph</span>} />);

      // Icon is decorative — the title text already conveys the pane identity
      const iconWrapper = screen.getByTestId('pane-header-icon');
      expect(iconWrapper).toHaveAttribute('aria-hidden', 'true');
    });
  });

  // ─────────────────────────────────────────────────────────────────────
  // RightSlot prop
  // ─────────────────────────────────────────────────────────────────────

  describe('RightSlot prop', () => {
    it('render_WithRightSlot_RendersRightSlotWrapper', () => {
      renderWithProviders(
        <PaneHeader title="Assistant" rightSlot={<button data-testid="right-action">Action</button>} />
      );

      expect(screen.getByTestId('pane-header-right-slot')).toBeInTheDocument();
      expect(screen.getByTestId('right-action')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Action' })).toBeInTheDocument();
    });
  });

  // ─────────────────────────────────────────────────────────────────────
  // All three props together (canonical Context-pane shape)
  // ─────────────────────────────────────────────────────────────────────

  describe('All three props together', () => {
    it('render_AllProps_RendersTitleIconAndRightSlot', () => {
      renderWithProviders(
        <PaneHeader
          title="Context"
          icon={<span data-testid="ctx-icon">D</span>}
          rightSlot={<span data-testid="stage-label">Stage 2</span>}
        />
      );

      expect(screen.getByText('Context')).toBeInTheDocument();
      expect(screen.getByTestId('ctx-icon')).toBeInTheDocument();
      expect(screen.getByTestId('stage-label')).toBeInTheDocument();
    });
  });

  // ─────────────────────────────────────────────────────────────────────
  // Snapshot stability
  // ─────────────────────────────────────────────────────────────────────

  describe('Snapshot', () => {
    it('matches snapshot with all three props', () => {
      const { container } = renderWithProviders(
        <PaneHeader
          title="Context"
          icon={<span data-testid="ctx-icon">D</span>}
          rightSlot={<span data-testid="stage-label">Stage 2</span>}
        />
      );

      expect(container.firstChild).toMatchSnapshot();
    });

    it('matches snapshot with title only', () => {
      const { container } = renderWithProviders(<PaneHeader title="Context" />);

      expect(container.firstChild).toMatchSnapshot();
    });
  });
});
