/**
 * CitationWidget — Dark mode and NFR tests
 *
 * Task: AIPU-051 — Dark Mode and NFR Verification
 * Covers:
 *   - NFR-01: renders within 200ms (light and dark)
 *   - NFR-04: renders correctly under webDarkTheme
 *   - ADR-021: no hard-coded colors (verified by no-hardcoded-colors.test.ts)
 */

import React from 'react';
import '@testing-library/jest-dom';
import CitationWidget from '../CitationWidget';
import { renderWithTheme, webLightTheme, webDarkTheme, mockCitationProps } from '../../__tests__/test-utils';

describe('CitationWidget', () => {
  const props = mockCitationProps();

  it('renders in light theme without error', () => {
    const { container } = renderWithTheme(<CitationWidget {...props} />, webLightTheme);
    expect(container.firstChild).toBeTruthy();
  });

  it('renders in dark theme (webDarkTheme) without error', () => {
    const { container } = renderWithTheme(<CitationWidget {...props} />, webDarkTheme);
    expect(container.firstChild).toBeTruthy();
  });

  it('renders within 200ms in light theme (NFR-01)', () => {
    const start = performance.now();
    renderWithTheme(<CitationWidget {...props} />, webLightTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it('renders within 200ms in dark theme (NFR-01)', () => {
    const start = performance.now();
    renderWithTheme(<CitationWidget {...props} />, webDarkTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it('renders citation count header', () => {
    const { getByText } = renderWithTheme(<CitationWidget {...props} />, webLightTheme);
    expect(getByText('2 Citations')).toBeTruthy();
  });

  it('renders citation texts', () => {
    const { getByText } = renderWithTheme(<CitationWidget {...props} />, webLightTheme);
    expect(getByText('Smith v. Jones, 123 F.3d 456 (9th Cir. 2000)')).toBeTruthy();
    expect(getByText('Contract Law Principles, 4th Ed.')).toBeTruthy();
  });

  it('renders citation index badges', () => {
    const { getByText } = renderWithTheme(<CitationWidget {...props} />, webLightTheme);
    expect(getByText('1')).toBeTruthy();
    expect(getByText('2')).toBeTruthy();
  });

  it('renders empty state when no citations', () => {
    const { getByText } = renderWithTheme(<CitationWidget data={{ citations: [] }} />, webDarkTheme);
    expect(getByText('No citations available.')).toBeTruthy();
  });

  it('renders loading state without error', () => {
    const { container } = renderWithTheme(<CitationWidget {...props} isLoading={true} />, webDarkTheme);
    expect(container.firstChild).toBeTruthy();
  });
});
