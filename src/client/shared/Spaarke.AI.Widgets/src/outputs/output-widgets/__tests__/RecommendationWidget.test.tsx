/**
 * RecommendationWidget — Dark mode and NFR tests
 *
 * Task: AIPU-051 — Dark Mode and NFR Verification
 * Covers:
 *   - NFR-01: renders within 200ms (light and dark)
 *   - NFR-04: renders correctly under webDarkTheme
 *   - ADR-021: no hard-coded colors (verified by no-hardcoded-colors.test.ts)
 */

import React from 'react';
import '@testing-library/jest-dom';
import RecommendationWidget from '../RecommendationWidget';
import { renderWithTheme, webLightTheme, webDarkTheme, mockRecommendationProps } from '../../__tests__/test-utils';

describe('RecommendationWidget', () => {
  const props = mockRecommendationProps();

  it('renders in light theme without error', () => {
    const { container } = renderWithTheme(<RecommendationWidget {...props} />, webLightTheme);
    expect(container.firstChild).toBeTruthy();
  });

  it('renders in dark theme (webDarkTheme) without error', () => {
    const { container } = renderWithTheme(<RecommendationWidget {...props} />, webDarkTheme);
    expect(container.firstChild).toBeTruthy();
  });

  it('renders within 200ms in light theme (NFR-01)', () => {
    const start = performance.now();
    renderWithTheme(<RecommendationWidget {...props} />, webLightTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it('renders within 200ms in dark theme (NFR-01)', () => {
    const start = performance.now();
    renderWithTheme(<RecommendationWidget {...props} />, webDarkTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it('renders recommendation text', () => {
    const { getByText } = renderWithTheme(<RecommendationWidget {...props} />, webLightTheme);
    expect(getByText('Narrow the indemnification scope.')).toBeTruthy();
    expect(getByText('Add a dispute resolution clause.')).toBeTruthy();
  });

  it('renders priority badges', () => {
    const { getAllByText } = renderWithTheme(<RecommendationWidget {...props} />, webLightTheme);
    expect(getAllByText('High').length).toBeGreaterThan(0);
    expect(getAllByText('Medium').length).toBeGreaterThan(0);
  });

  it('renders rationale text when provided', () => {
    const { getByText } = renderWithTheme(<RecommendationWidget {...props} />, webLightTheme);
    expect(getByText('Current language creates unlimited liability.')).toBeTruthy();
  });

  it('renders loading state in dark theme without error', () => {
    const { container } = renderWithTheme(<RecommendationWidget {...props} isLoading={true} />, webDarkTheme);
    expect(container.firstChild).toBeTruthy();
  });
});
