/**
 * StatusBadge — unit tests
 *
 * Covers the task-012 (C-4) acceptance criteria:
 *   - Each tone (neutral/info/success/warning/critical) renders distinctly
 *   - An unrecognised tone falls back to the neutral treatment without throwing
 *   - No tone prop (undefined) also falls back to neutral
 *   - Dark mode parity sanity (ADR-021 — component renders without hard-coded colors)
 *   - Accessible label defaults to the visible label, or can be overridden
 *
 * @see ADR-021 Fluent UI v9 semantic tokens
 * @see projects/spaarke-ontology-platform-r1/spec.md FR-28 / FR-41 (C-4)
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

import { StatusBadge } from '../StatusBadge';
import type { StatusBadgeTone } from '../StatusBadge';

function renderWithTheme(ui: React.ReactElement, dark = false): void {
  render(<FluentProvider theme={dark ? webDarkTheme : webLightTheme}>{ui}</FluentProvider>);
}

describe('StatusBadge', () => {
  describe('Renders each known tone', () => {
    const tones: StatusBadgeTone[] = ['neutral', 'info', 'success', 'warning', 'critical'];

    it.each(tones)('renders the %s tone with its label and data-tone attribute', tone => {
      renderWithTheme(<StatusBadge label={`Label-${tone}`} tone={tone} />);

      expect(screen.getByText(`Label-${tone}`)).toBeInTheDocument();
      const badge = screen.getByTestId('status-badge');
      expect(badge).toHaveAttribute('data-tone', tone);
    });
  });

  describe('Fallback behavior', () => {
    it('falls back to the neutral treatment when tone is omitted', () => {
      renderWithTheme(<StatusBadge label="No tone" />);

      const badge = screen.getByTestId('status-badge');
      expect(badge).toHaveAttribute('data-tone', 'neutral');
      expect(screen.getByText('No tone')).toBeInTheDocument();
    });

    it('falls back to the neutral treatment for an unrecognised tone, without throwing', () => {
      expect(() =>
        renderWithTheme(<StatusBadge label="Weird" tone={'not-a-real-tone' as unknown as StatusBadgeTone} />)
      ).not.toThrow();

      const badge = screen.getByTestId('status-badge');
      expect(badge).toHaveAttribute('data-tone', 'neutral');
      expect(screen.getByText('Weird')).toBeInTheDocument();
    });
  });

  describe('Accessibility', () => {
    it('defaults the aria-label to the visible label', () => {
      renderWithTheme(<StatusBadge label="Acknowledged" tone="info" />);
      const badge = screen.getByTestId('status-badge');
      expect(badge).toHaveAttribute('aria-label', 'Acknowledged');
    });

    it('uses an explicit ariaLabel override when provided', () => {
      renderWithTheme(<StatusBadge label="Ack" tone="info" ariaLabel="Signal acknowledged" />);
      const badge = screen.getByTestId('status-badge');
      expect(badge).toHaveAttribute('aria-label', 'Signal acknowledged');
    });
  });

  describe('Dark mode (ADR-021)', () => {
    it('renders every tone without errors under webDarkTheme', () => {
      const tones: StatusBadgeTone[] = ['neutral', 'info', 'success', 'warning', 'critical'];
      for (const tone of tones) {
        const { unmount } = render(
          <FluentProvider theme={webDarkTheme}>
            <StatusBadge label={`Dark-${tone}`} tone={tone} />
          </FluentProvider>
        );
        expect(screen.getByText(`Dark-${tone}`)).toBeInTheDocument();
        unmount();
      }
    });
  });
});
