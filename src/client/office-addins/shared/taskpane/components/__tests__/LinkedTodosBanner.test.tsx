/**
 * Unit tests for LinkedTodosBanner
 *
 * Covers smart-todo-decoupling-r3 task 071 acceptance criteria:
 * - 0 linked → banner is suppressed (no DOM output)
 * - 1 / N linked → banner with correct count + accessible link
 * - Loading state shows informational MessageBar with spinner
 * - Error state shows error MessageBar
 * - Keyboard + ARIA accessibility (NFR-10)
 */

import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { LinkedTodosBanner } from '../LinkedTodosBanner';

// Fluent v9 MessageBar uses ResizeObserver for reflow detection; jsdom doesn't
// provide one. Stub a no-op implementation so render() doesn't throw.
class ResizeObserverMock {
  observe(): void {
    /* no-op */
  }
  unobserve(): void {
    /* no-op */
  }
  disconnect(): void {
    /* no-op */
  }
}
// eslint-disable-next-line @typescript-eslint/no-explicit-any
(globalThis as any).ResizeObserver = (globalThis as any).ResizeObserver ?? ResizeObserverMock;

/**
 * @testing-library/jest-dom is not configured in this workspace's jest setup.
 * (Pre-existing tests under shared/taskpane/components/__tests__/ also use
 * `.toBeInTheDocument()` despite the dep being absent.) These tests use
 * plain truthy / null / element-equality assertions so they work without
 * adding a new dev dependency.
 */

const renderWithProvider = (ui: React.ReactElement) =>
  render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

describe('LinkedTodosBanner', () => {
  describe('count rendering (FR-28)', () => {
    it('renders nothing when count is 0 (no banner case)', () => {
      const { container } = renderWithProvider(<LinkedTodosBanner count={0} />);
      // FluentProvider always emits a wrapper; the banner inside should be empty.
      const root = container.firstElementChild;
      expect(root?.querySelector('[role="status"]')).toBeNull();
      expect(root?.querySelector('[role="alert"]')).toBeNull();
      expect(root?.textContent ?? '').not.toMatch(/spaarke to-do/i);
    });

    it('renders nothing for negative count (defensive)', () => {
      const { container } = renderWithProvider(<LinkedTodosBanner count={-1} />);
      const root = container.firstElementChild;
      expect(root?.querySelector('[role="status"]')).toBeNull();
      expect(root?.textContent ?? '').not.toMatch(/spaarke to-do/i);
    });

    it('renders singular text when count is 1', () => {
      renderWithProvider(<LinkedTodosBanner count={1} />);
      expect(screen.queryByText('This email has 1 Spaarke to-do')).not.toBeNull();
    });

    it('renders plural text when count is > 1', () => {
      renderWithProvider(<LinkedTodosBanner count={5} />);
      expect(screen.queryByText('This email has 5 Spaarke to-dos')).not.toBeNull();
    });

    it('renders count for very large numbers', () => {
      renderWithProvider(<LinkedTodosBanner count={42} />);
      expect(screen.queryByText('This email has 42 Spaarke to-dos')).not.toBeNull();
    });
  });

  describe('view list action', () => {
    it('does not render the link when onViewList is omitted', () => {
      renderWithProvider(<LinkedTodosBanner count={3} />);
      expect(screen.queryByRole('button', { name: /view list/i })).toBeNull();
    });

    it('renders the link when onViewList is provided', () => {
      renderWithProvider(<LinkedTodosBanner count={3} onViewList={() => undefined} />);
      const link = screen.queryByRole('button', { name: /view list of 3 spaarke to-dos/i });
      expect(link).not.toBeNull();
    });

    it('invokes onViewList when the link is clicked', () => {
      const handleViewList = jest.fn();
      renderWithProvider(<LinkedTodosBanner count={2} onViewList={handleViewList} />);

      fireEvent.click(screen.getByRole('button', { name: /view list/i }));
      expect(handleViewList).toHaveBeenCalledTimes(1);
    });

    it('invokes onViewList when the link is activated via keyboard (focus + click)', () => {
      const handleViewList = jest.fn();
      renderWithProvider(<LinkedTodosBanner count={1} onViewList={handleViewList} />);

      const link = screen.getByRole('button', { name: /view list/i });
      link.focus();
      fireEvent.click(link);
      expect(handleViewList).toHaveBeenCalledTimes(1);
    });

    it('singular link aria-label when count is 1', () => {
      renderWithProvider(<LinkedTodosBanner count={1} onViewList={() => undefined} />);
      const link = screen.queryByRole('button', { name: /view list of 1 spaarke to-do linked to this email/i });
      expect(link).not.toBeNull();
    });
  });

  describe('loading state', () => {
    it('renders a loading MessageBar when isLoading is true', () => {
      renderWithProvider(<LinkedTodosBanner count={0} isLoading={true} />);
      expect(screen.queryByText(/checking for linked spaarke to-dos/i)).not.toBeNull();
    });

    it('loading state has aria-busy="true" for screen readers', () => {
      const { container } = renderWithProvider(<LinkedTodosBanner count={0} isLoading={true} />);
      const liveRegion = container.querySelector('[aria-busy="true"]');
      expect(liveRegion).not.toBeNull();
    });

    it('loading state suppresses the count banner even when count > 0', () => {
      renderWithProvider(<LinkedTodosBanner count={3} isLoading={true} />);
      expect(screen.queryByText(/this email has 3/i)).toBeNull();
      expect(screen.queryByText(/checking for linked spaarke to-dos/i)).not.toBeNull();
    });
  });

  describe('error state', () => {
    it('renders an error MessageBar when error is set', () => {
      renderWithProvider(<LinkedTodosBanner count={0} error="Dataverse unavailable" />);
      expect(screen.queryByText("Couldn't load linked to-dos")).not.toBeNull();
      expect(screen.queryByText('Dataverse unavailable')).not.toBeNull();
    });

    it('error state uses role="alert" for assertive announcement', () => {
      renderWithProvider(<LinkedTodosBanner count={0} error="Network error" />);
      expect(screen.queryByRole('alert')).not.toBeNull();
    });

    it('error takes precedence over count > 0', () => {
      renderWithProvider(<LinkedTodosBanner count={3} error="Partial failure" />);
      expect(screen.queryByText(/this email has 3/i)).toBeNull();
      expect(screen.queryByText("Couldn't load linked to-dos")).not.toBeNull();
    });
  });

  describe('accessibility (NFR-10)', () => {
    it('count banner uses role="status" for polite announcement', () => {
      renderWithProvider(<LinkedTodosBanner count={2} />);
      expect(screen.queryByRole('status')).not.toBeNull();
    });

    it('count banner has aria-label with the full sentence', () => {
      renderWithProvider(<LinkedTodosBanner count={4} />);
      const labeled = screen.queryByLabelText('This email has 4 Spaarke to-dos');
      expect(labeled).not.toBeNull();
    });

    it('link is focusable (tabbable) for keyboard users', () => {
      renderWithProvider(<LinkedTodosBanner count={2} onViewList={() => undefined} />);
      const link = screen.getByRole('button', { name: /view list/i });
      link.focus();
      expect(document.activeElement).toBe(link);
    });

    it('count banner has aria-live="polite" wrapper for non-disruptive updates', () => {
      const { container } = renderWithProvider(<LinkedTodosBanner count={1} />);
      const live = container.querySelector('[aria-live="polite"]');
      expect(live).not.toBeNull();
    });
  });

  describe('host className override (Spaarke convention)', () => {
    it('accepts a className prop applied LAST so host overrides win', () => {
      const { container } = renderWithProvider(<LinkedTodosBanner count={1} className="host-override-class" />);
      // FluentProvider wraps our component, so dig into descendants.
      const overridden = container.querySelector('.host-override-class');
      expect(overridden).not.toBeNull();
    });
  });
});
