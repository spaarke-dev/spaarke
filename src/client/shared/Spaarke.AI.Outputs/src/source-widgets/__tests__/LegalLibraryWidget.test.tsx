/**
 * LegalLibraryWidget — Dark mode and NFR tests
 *
 * Task: AIPU-051 — Dark Mode and NFR Verification
 * Covers:
 *   - NFR-01: renders within 200ms (light and dark)
 *   - NFR-04: renders correctly under webDarkTheme
 *   - ADR-021: no hard-coded colors (verified by no-hardcoded-colors.test.ts)
 */

import React from "react";
import "@testing-library/jest-dom";
import LegalLibraryWidget from "../LegalLibraryWidget";
import {
  renderWithTheme,
  webLightTheme,
  webDarkTheme,
  mockLegalLibraryProps,
} from "../../__tests__/test-utils";

describe("LegalLibraryWidget", () => {
  const props = mockLegalLibraryProps();

  it("renders in light theme without error", () => {
    const { container } = renderWithTheme(
      <LegalLibraryWidget {...props} />,
      webLightTheme
    );
    expect(container.firstChild).toBeTruthy();
  });

  it("renders in dark theme (webDarkTheme) without error", () => {
    const { container } = renderWithTheme(
      <LegalLibraryWidget {...props} />,
      webDarkTheme
    );
    expect(container.firstChild).toBeTruthy();
  });

  it("renders within 200ms in light theme (NFR-01)", () => {
    const start = performance.now();
    renderWithTheme(<LegalLibraryWidget {...props} />, webLightTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it("renders within 200ms in dark theme (NFR-01)", () => {
    const start = performance.now();
    renderWithTheme(<LegalLibraryWidget {...props} />, webDarkTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it("renders the citation title", () => {
    const { getByText } = renderWithTheme(
      <LegalLibraryWidget {...props} />,
      webLightTheme
    );
    expect(
      getByText("Brown v. Board of Education, 347 U.S. 483 (1954)")
    ).toBeTruthy();
  });

  it("renders the court name", () => {
    const { getByText } = renderWithTheme(
      <LegalLibraryWidget {...props} />,
      webLightTheme
    );
    expect(getByText("U.S. Supreme Court")).toBeTruthy();
  });

  it("renders the excerpt section label", () => {
    const { getByText } = renderWithTheme(
      <LegalLibraryWidget {...props} />,
      webLightTheme
    );
    expect(getByText("Excerpt")).toBeTruthy();
  });

  it("renders loading state without error", () => {
    const { container } = renderWithTheme(
      <LegalLibraryWidget {...props} isLoading={true} />,
      webDarkTheme
    );
    expect(container.firstChild).toBeTruthy();
  });
});
