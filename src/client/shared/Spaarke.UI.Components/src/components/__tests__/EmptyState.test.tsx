/**
 * EmptyState — shared empty-state shell (task 081 / C-11, F8, F9).
 *
 * Pins the public contract callers rely on: the status/live-region semantics,
 * the optional slots, the `size` switch (compact vs default) and the
 * caller-supplied `className` (used by the DailyBriefing 64px band and the
 * LegalWorkspace notification / feed spacing).
 */
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { EmptyState } from '../EmptyState';

function renderInTheme(ui: React.ReactElement, theme = webLightTheme) {
  return render(<FluentProvider theme={theme}>{ui}</FluentProvider>);
}

describe('EmptyState', () => {
  it('renders heading and description in a polite status region', () => {
    renderInTheme(<EmptyState heading="All caught up" description="Nothing to do." />);
    const region = screen.getByRole('status');
    expect(region).toHaveAttribute('aria-live', 'polite');
    expect(region).toHaveTextContent('All caught up');
    expect(region).toHaveTextContent('Nothing to do.');
  });

  it('defaults to the page-level size', () => {
    renderInTheme(<EmptyState heading="H" />);
    expect(screen.getByRole('status')).toHaveAttribute('data-size', 'default');
  });

  it('size="compact" selects the compact variant with different classes from default', () => {
    const { unmount } = renderInTheme(<EmptyState heading="H" description="D" />);
    const defaultHeadingClass = screen.getByText('H').className;
    const defaultContainerClass = screen.getByRole('status').className;
    unmount();

    renderInTheme(<EmptyState size="compact" heading="H" description="D" />);
    const region = screen.getByRole('status');
    expect(region).toHaveAttribute('data-size', 'compact');
    expect(region.className).not.toBe(defaultContainerClass);
    expect(screen.getByText('H').className).not.toBe(defaultHeadingClass);
  });

  it('wraps the icon in an aria-hidden box and omits it when not given', () => {
    const { container, unmount } = renderInTheme(<EmptyState heading="H" icon={<svg data-testid="icon" />} />);
    expect(screen.getByTestId('icon').parentElement).toHaveAttribute('aria-hidden', 'true');
    unmount();
    renderInTheme(<EmptyState heading="H" />);
    expect(screen.queryByTestId('icon')).toBeNull();
    expect(container.querySelector('[aria-hidden="true"]')).toBeNull();
  });

  it('omits the description when not given and renders the footer as-is', () => {
    renderInTheme(<EmptyState heading="H" footer={<button type="button">Show all updates</button>} />);
    expect(screen.getByRole('button', { name: 'Show all updates' })).toBeInTheDocument();
    expect(screen.getByRole('status').children).toHaveLength(2); // heading + footer
  });

  it('applies ariaLabel to the region and merges the caller className', () => {
    renderInTheme(<EmptyState heading="H" ariaLabel="No playbooks available" className="caller-band" />);
    const region = screen.getByRole('status', { name: 'No playbooks available' });
    expect(region.className).toContain('caller-band');
  });

  it('merges headingClassName / descriptionClassName onto the heading and description (L4 parity hooks)', () => {
    renderInTheme(
      <EmptyState
        size="compact"
        heading="H"
        description="D"
        headingClassName="caller-h"
        descriptionClassName="caller-d"
      />
    );
    expect(screen.getByText('H').className).toContain('caller-h');
    expect(screen.getByText('D').className).toContain('caller-d');
    expect(screen.getByText('H').className).not.toContain('caller-d');
  });

  it('renders in the dark theme (ADR-021 — tokens only, no hard-coded colours)', () => {
    renderInTheme(<EmptyState size="compact" heading="Dark" description="mode" />, webDarkTheme);
    expect(screen.getByRole('status')).toHaveTextContent('Dark');
  });
});
