/**
 * ContractComparisonWidget — Dark mode and NFR tests
 *
 * Task: AIPU-051 — Dark Mode and NFR Verification
 * Covers:
 *   - NFR-01: renders within 200ms (light and dark)
 *   - NFR-04: renders correctly under webDarkTheme
 *   - ADR-021: no hard-coded colors (verified by no-hardcoded-colors.test.ts)
 */

import React from 'react';
import '@testing-library/jest-dom';
import ContractComparisonWidget from '../ContractComparisonWidget';
import { renderWithTheme, webLightTheme, webDarkTheme, mockContractComparisonProps } from '../../__tests__/test-utils';

describe('ContractComparisonWidget', () => {
  const props = mockContractComparisonProps();

  it('renders in light theme without error', () => {
    const { container } = renderWithTheme(<ContractComparisonWidget {...props} />, webLightTheme);
    expect(container.firstChild).toBeTruthy();
  });

  it('renders in dark theme (webDarkTheme) without error', () => {
    const { container } = renderWithTheme(<ContractComparisonWidget {...props} />, webDarkTheme);
    expect(container.firstChild).toBeTruthy();
  });

  it('renders within 200ms in light theme (NFR-01)', () => {
    const start = performance.now();
    renderWithTheme(<ContractComparisonWidget {...props} />, webLightTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it('renders within 200ms in dark theme (NFR-01)', () => {
    const start = performance.now();
    renderWithTheme(<ContractComparisonWidget {...props} />, webDarkTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it('renders column headers for left and right documents', () => {
    const { getByText } = renderWithTheme(<ContractComparisonWidget {...props} />, webLightTheme);
    expect(getByText('Original')).toBeTruthy();
    expect(getByText('Revised')).toBeTruthy();
  });

  it('renders clause text from both sides', () => {
    const { getByText } = renderWithTheme(<ContractComparisonWidget {...props} />, webLightTheme);
    expect(getByText('Termination requires 30-day notice.')).toBeTruthy();
    expect(getByText('Termination requires 60-day notice.')).toBeTruthy();
  });

  it('renders loading state in dark theme without error', () => {
    const { container } = renderWithTheme(<ContractComparisonWidget {...props} isLoading={true} />, webDarkTheme);
    expect(container.firstChild).toBeTruthy();
  });
});
