/**
 * DocumentViewerWidget — Dark mode and NFR tests
 *
 * Task: AIPU-051 — Dark Mode and NFR Verification
 * Covers:
 *   - NFR-01: renders within 200ms (light and dark)
 *   - NFR-04: renders correctly under webDarkTheme
 *   - ADR-021: no hard-coded colors (verified by no-hardcoded-colors.test.ts)
 *
 * NOTE: DocumentViewerWidget renders an <iframe> or <object> for document
 * preview. jsdom does not load external resources, so the content inside
 * the frame will not be rendered. The widget chrome (toolbar, filename)
 * will render normally.
 */

import React from 'react';
import '@testing-library/jest-dom';
import DocumentViewerWidget from '../DocumentViewerWidget';
import { renderWithTheme, webLightTheme, webDarkTheme, mockDocumentViewerProps } from '../../__tests__/test-utils';

describe('DocumentViewerWidget', () => {
  const props = mockDocumentViewerProps();

  it('renders in light theme without error', () => {
    const { container } = renderWithTheme(<DocumentViewerWidget {...props} />, webLightTheme);
    expect(container.firstChild).toBeTruthy();
  });

  it('renders in dark theme (webDarkTheme) without error', () => {
    const { container } = renderWithTheme(<DocumentViewerWidget {...props} />, webDarkTheme);
    expect(container.firstChild).toBeTruthy();
  });

  it('renders within 200ms in light theme (NFR-01)', () => {
    const start = performance.now();
    renderWithTheme(<DocumentViewerWidget {...props} />, webLightTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it('renders within 200ms in dark theme (NFR-01)', () => {
    const start = performance.now();
    renderWithTheme(<DocumentViewerWidget {...props} />, webDarkTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it('renders the file name in the toolbar', () => {
    const { getByText } = renderWithTheme(<DocumentViewerWidget {...props} />, webLightTheme);
    expect(getByText('Agreement.pdf')).toBeTruthy();
  });

  it('renders loading state without error', () => {
    const { container } = renderWithTheme(<DocumentViewerWidget {...props} isLoading={true} />, webDarkTheme);
    expect(container.firstChild).toBeTruthy();
  });

  it('renders error state without throwing', () => {
    const { container } = renderWithTheme(
      <DocumentViewerWidget {...props} error="Failed to load document." />,
      webDarkTheme
    );
    expect(container.firstChild).toBeTruthy();
  });
});
