/**
 * WebSourceWidget — Dark mode and NFR tests
 *
 * Task: AIPU-051 — Dark Mode and NFR Verification
 * Covers:
 *   - NFR-01: renders within 200ms (light and dark)
 *   - NFR-04: renders correctly under webDarkTheme
 *   - ADR-021: no hard-coded colors (verified by no-hardcoded-colors.test.ts)
 *
 * NOTE: WebSourceWidget renders a sandboxed <iframe>. jsdom does not load
 * external URLs, so iframe content is not present in test DOM. The URL bar
 * and link elements are tested instead.
 */

import React from 'react';
import '@testing-library/jest-dom';
import WebSourceWidget from '../WebSourceWidget';
import { renderWithTheme, webLightTheme, webDarkTheme, mockWebSourceProps } from '../../__tests__/test-utils';

describe('WebSourceWidget', () => {
  const props = mockWebSourceProps();

  it('renders in light theme without error', () => {
    const { container } = renderWithTheme(<WebSourceWidget {...props} />, webLightTheme);
    expect(container.firstChild).toBeTruthy();
  });

  it('renders in dark theme (webDarkTheme) without error', () => {
    const { container } = renderWithTheme(<WebSourceWidget {...props} />, webDarkTheme);
    expect(container.firstChild).toBeTruthy();
  });

  it('renders within 200ms in light theme (NFR-01)', () => {
    const start = performance.now();
    renderWithTheme(<WebSourceWidget {...props} />, webLightTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it('renders within 200ms in dark theme (NFR-01)', () => {
    const start = performance.now();
    renderWithTheme(<WebSourceWidget {...props} />, webDarkTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it('renders the URL input in the URL bar', () => {
    const { container } = renderWithTheme(<WebSourceWidget {...props} />, webLightTheme);
    const input = container.querySelector('input');
    expect(input).toBeTruthy();
    expect(input?.value).toBe('https://example.com');
  });

  it("renders the 'Open' link", () => {
    const { getByText } = renderWithTheme(<WebSourceWidget {...props} />, webLightTheme);
    expect(getByText('Open')).toBeTruthy();
  });

  it('renders loading state without error', () => {
    const { container } = renderWithTheme(<WebSourceWidget {...props} isLoading={true} />, webDarkTheme);
    expect(container.firstChild).toBeTruthy();
  });
});
