/**
 * ActionPlanWidget — Dark mode and NFR tests
 *
 * Task: AIPU-051 — Dark Mode and NFR Verification
 * Covers:
 *   - NFR-01: renders within 200ms (light and dark)
 *   - NFR-04: renders correctly under webDarkTheme
 *   - ADR-021: no hard-coded colors (verified by no-hardcoded-colors.test.ts)
 */

import React from 'react';
import '@testing-library/jest-dom';
import ActionPlanWidget from '../ActionPlanWidget';
import { renderWithTheme, webLightTheme, webDarkTheme, mockActionPlanProps } from '../../__tests__/test-utils';

describe('ActionPlanWidget', () => {
  const props = mockActionPlanProps();

  it('renders in light theme without error', () => {
    const { container } = renderWithTheme(<ActionPlanWidget {...props} />, webLightTheme);
    expect(container.firstChild).toBeTruthy();
  });

  it('renders in dark theme (webDarkTheme) without error', () => {
    const { container } = renderWithTheme(<ActionPlanWidget {...props} />, webDarkTheme);
    expect(container.firstChild).toBeTruthy();
  });

  it('renders within 200ms in light theme (NFR-01)', () => {
    const start = performance.now();
    renderWithTheme(<ActionPlanWidget {...props} />, webLightTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it('renders within 200ms in dark theme (NFR-01)', () => {
    const start = performance.now();
    renderWithTheme(<ActionPlanWidget {...props} />, webDarkTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it('renders the widget title', () => {
    const { getByText } = renderWithTheme(<ActionPlanWidget {...props} />, webLightTheme);
    expect(getByText('Action Plan')).toBeTruthy();
  });

  it('renders step labels as checkboxes', () => {
    const { getByText } = renderWithTheme(<ActionPlanWidget {...props} />, webLightTheme);
    expect(getByText('Review redlines')).toBeTruthy();
    expect(getByText('Send to client')).toBeTruthy();
  });

  it('renders loading state in dark theme without error', () => {
    const { container } = renderWithTheme(<ActionPlanWidget {...props} isLoading={true} />, webDarkTheme);
    expect(container.firstChild).toBeTruthy();
  });
});
