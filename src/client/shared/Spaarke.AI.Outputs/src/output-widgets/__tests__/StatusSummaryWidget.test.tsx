/**
 * StatusSummaryWidget — Dark mode and NFR tests
 *
 * Task: AIPU-051 — Dark Mode and NFR Verification
 * Covers:
 *   - NFR-01: renders within 200ms (light and dark)
 *   - NFR-04: renders correctly under webDarkTheme
 *   - ADR-021: no hard-coded colors (verified by no-hardcoded-colors.test.ts)
 */

import React from 'react';
import '@testing-library/jest-dom';
import StatusSummaryWidget from '../StatusSummaryWidget';
import { renderWithTheme, webLightTheme, webDarkTheme, mockStatusSummaryProps } from '../../__tests__/test-utils';

describe('StatusSummaryWidget', () => {
  const props = mockStatusSummaryProps();

  it('renders in light theme without error', () => {
    const { container } = renderWithTheme(<StatusSummaryWidget {...props} />, webLightTheme);
    expect(container.firstChild).toBeTruthy();
  });

  it('renders in dark theme (webDarkTheme) without error', () => {
    const { container } = renderWithTheme(<StatusSummaryWidget {...props} />, webDarkTheme);
    expect(container.firstChild).toBeTruthy();
  });

  it('renders within 200ms in light theme (NFR-01)', () => {
    const start = performance.now();
    renderWithTheme(<StatusSummaryWidget {...props} />, webLightTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it('renders within 200ms in dark theme (NFR-01)', () => {
    const start = performance.now();
    renderWithTheme(<StatusSummaryWidget {...props} />, webDarkTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it('renders the widget title', () => {
    const { getByText } = renderWithTheme(<StatusSummaryWidget {...props} />, webLightTheme);
    expect(getByText('Contract Health')).toBeTruthy();
  });

  it('renders category labels', () => {
    const { getByText } = renderWithTheme(<StatusSummaryWidget {...props} />, webLightTheme);
    expect(getByText('Compliance')).toBeTruthy();
    expect(getByText('Risk')).toBeTruthy();
  });

  it('renders category summaries', () => {
    const { getByText } = renderWithTheme(<StatusSummaryWidget {...props} />, webLightTheme);
    expect(getByText('All required clauses present.')).toBeTruthy();
  });

  it('renders loading state in dark theme without error', () => {
    const { container } = renderWithTheme(<StatusSummaryWidget {...props} isLoading={true} />, webDarkTheme);
    expect(container.firstChild).toBeTruthy();
  });
});
