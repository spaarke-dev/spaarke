/**
 * DocumentCompareWidget — Dark mode and NFR tests
 *
 * Task: AIPU-051 — Dark Mode and NFR Verification
 * Covers:
 *   - NFR-01: renders within 200ms (light and dark)
 *   - NFR-04: renders correctly under webDarkTheme
 *   - ADR-021: no hard-coded colors (verified by no-hardcoded-colors.test.ts)
 */

import React from "react";
import "@testing-library/jest-dom";
import DocumentCompareWidget from "../DocumentCompareWidget";
import {
  renderWithTheme,
  webLightTheme,
  webDarkTheme,
  mockDocumentCompareProps,
} from "../../__tests__/test-utils";

describe("DocumentCompareWidget", () => {
  const props = mockDocumentCompareProps();

  it("renders in light theme without error", () => {
    const { container } = renderWithTheme(
      <DocumentCompareWidget {...props} />,
      webLightTheme
    );
    expect(container.firstChild).toBeTruthy();
  });

  it("renders in dark theme (webDarkTheme) without error", () => {
    const { container } = renderWithTheme(
      <DocumentCompareWidget {...props} />,
      webDarkTheme
    );
    expect(container.firstChild).toBeTruthy();
  });

  it("renders within 200ms in light theme (NFR-01)", () => {
    const start = performance.now();
    renderWithTheme(<DocumentCompareWidget {...props} />, webLightTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it("renders within 200ms in dark theme (NFR-01)", () => {
    const start = performance.now();
    renderWithTheme(<DocumentCompareWidget {...props} />, webDarkTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it("renders document labels", () => {
    const { getAllByText } = renderWithTheme(
      <DocumentCompareWidget {...props} />,
      webLightTheme
    );
    // Labels appear in both the header and potentially as inline labels
    expect(getAllByText(/Version 1/).length).toBeGreaterThan(0);
    expect(getAllByText(/Version 2/).length).toBeGreaterThan(0);
  });

  it("renders loading state in dark theme without error", () => {
    const { container } = renderWithTheme(
      <DocumentCompareWidget {...props} isLoading={true} />,
      webDarkTheme
    );
    expect(container.firstChild).toBeTruthy();
  });
});
