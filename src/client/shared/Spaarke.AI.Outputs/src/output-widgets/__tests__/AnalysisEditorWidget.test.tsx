/**
 * AnalysisEditorWidget — Dark mode and NFR tests
 *
 * Task: AIPU-051 — Dark Mode and NFR Verification
 * Covers:
 *   - NFR-01: renders within 200ms (light and dark)
 *   - NFR-04: renders correctly under webDarkTheme
 *   - ADR-021: no hard-coded colors (verified by no-hardcoded-colors.test.ts)
 */

import React from "react";
import "@testing-library/jest-dom";
import AnalysisEditorWidget from "../AnalysisEditorWidget";
import {
  renderWithTheme,
  webLightTheme,
  webDarkTheme,
  mockAnalysisEditorProps,
} from "../../__tests__/test-utils";

describe("AnalysisEditorWidget", () => {
  const props = mockAnalysisEditorProps();

  it("renders in light theme without error", () => {
    const { container } = renderWithTheme(
      <AnalysisEditorWidget {...props} />,
      webLightTheme
    );
    expect(container.firstChild).toBeTruthy();
  });

  it("renders in dark theme (webDarkTheme) without error", () => {
    const { container } = renderWithTheme(
      <AnalysisEditorWidget {...props} />,
      webDarkTheme
    );
    expect(container.firstChild).toBeTruthy();
  });

  it("renders within 200ms in light theme (NFR-01)", () => {
    const start = performance.now();
    renderWithTheme(<AnalysisEditorWidget {...props} />, webLightTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it("renders within 200ms in dark theme (NFR-01)", () => {
    const start = performance.now();
    renderWithTheme(<AnalysisEditorWidget {...props} />, webDarkTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it("renders section headings", () => {
    const { getByText } = renderWithTheme(
      <AnalysisEditorWidget {...props} />,
      webLightTheme
    );
    expect(getByText("Executive Summary")).toBeTruthy();
    expect(getByText("Key Risks")).toBeTruthy();
  });

  it("renders section body text", () => {
    const { getByText } = renderWithTheme(
      <AnalysisEditorWidget {...props} />,
      webLightTheme
    );
    expect(
      getByText("This agreement outlines the obligations of both parties.")
    ).toBeTruthy();
  });

  it("renders loading state in dark theme without error", () => {
    const { container } = renderWithTheme(
      <AnalysisEditorWidget {...props} isLoading={true} />,
      webDarkTheme
    );
    expect(container.firstChild).toBeTruthy();
  });
});
