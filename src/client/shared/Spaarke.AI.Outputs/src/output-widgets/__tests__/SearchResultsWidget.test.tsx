/**
 * SearchResultsWidget — Dark mode and NFR tests
 *
 * Task: AIPU-051 — Dark Mode and NFR Verification
 * Covers:
 *   - NFR-01: renders within 200ms (light and dark)
 *   - NFR-04: renders correctly under webDarkTheme
 *   - ADR-021: no hard-coded colors (verified by no-hardcoded-colors.test.ts)
 */

import React from "react";
import "@testing-library/jest-dom";
import SearchResultsWidget from "../SearchResultsWidget";
import {
  renderWithTheme,
  webLightTheme,
  webDarkTheme,
  mockSearchResultsProps,
} from "../../__tests__/test-utils";

describe("SearchResultsWidget", () => {
  const props = mockSearchResultsProps();

  it("renders in light theme without error", () => {
    const { container } = renderWithTheme(
      <SearchResultsWidget {...props} />,
      webLightTheme
    );
    expect(container.firstChild).toBeTruthy();
  });

  it("renders in dark theme (webDarkTheme) without error", () => {
    const { container } = renderWithTheme(
      <SearchResultsWidget {...props} />,
      webDarkTheme
    );
    expect(container.firstChild).toBeTruthy();
  });

  it("renders within 200ms in light theme (NFR-01)", () => {
    const start = performance.now();
    renderWithTheme(<SearchResultsWidget {...props} />, webLightTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it("renders within 200ms in dark theme (NFR-01)", () => {
    const start = performance.now();
    renderWithTheme(<SearchResultsWidget {...props} />, webDarkTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it("renders the search query text", () => {
    const { getByText } = renderWithTheme(
      <SearchResultsWidget {...props} />,
      webLightTheme
    );
    expect(getByText("force majeure clause")).toBeTruthy();
  });

  it("renders result titles", () => {
    const { getByText } = renderWithTheme(
      <SearchResultsWidget {...props} />,
      webLightTheme
    );
    expect(getByText("Contract Analysis 2024")).toBeTruthy();
  });

  it("renders loading state in dark theme without error", () => {
    const { container } = renderWithTheme(
      <SearchResultsWidget {...props} isLoading={true} />,
      webDarkTheme
    );
    expect(container.firstChild).toBeTruthy();
  });
});
