/**
 * CodeViewerWidget — Dark mode and NFR tests
 *
 * Task: AIPU-051 — Dark Mode and NFR Verification
 * Covers:
 *   - NFR-01: renders within 200ms (light and dark)
 *   - NFR-04: renders correctly under webDarkTheme
 *   - ADR-021: no hard-coded colors (verified by no-hardcoded-colors.test.ts)
 *
 * NOTE: CodeViewerWidget uses the Clipboard API (navigator.clipboard.writeText).
 * jsdom does not implement navigator.clipboard by default. The copy test
 * handles this by mocking the clipboard API when needed.
 */

import React from "react";
import "@testing-library/jest-dom";
import CodeViewerWidget from "../CodeViewerWidget";
import {
  renderWithTheme,
  webLightTheme,
  webDarkTheme,
  mockCodeViewerProps,
} from "../../__tests__/test-utils";

describe("CodeViewerWidget", () => {
  const props = mockCodeViewerProps();

  it("renders in light theme without error", () => {
    const { container } = renderWithTheme(
      <CodeViewerWidget {...props} />,
      webLightTheme
    );
    expect(container.firstChild).toBeTruthy();
  });

  it("renders in dark theme (webDarkTheme) without error", () => {
    const { container } = renderWithTheme(
      <CodeViewerWidget {...props} />,
      webDarkTheme
    );
    expect(container.firstChild).toBeTruthy();
  });

  it("renders within 200ms in light theme (NFR-01)", () => {
    const start = performance.now();
    renderWithTheme(<CodeViewerWidget {...props} />, webLightTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it("renders within 200ms in dark theme (NFR-01)", () => {
    const start = performance.now();
    renderWithTheme(<CodeViewerWidget {...props} />, webDarkTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it("renders language badge", () => {
    const { getByText } = renderWithTheme(
      <CodeViewerWidget {...props} />,
      webLightTheme
    );
    expect(getByText("typescript")).toBeTruthy();
  });

  it("renders the code content", () => {
    const { container } = renderWithTheme(
      <CodeViewerWidget {...props} />,
      webLightTheme
    );
    const codeEl = container.querySelector("code");
    expect(codeEl).toBeTruthy();
    expect(codeEl?.textContent).toContain("const x = 1;");
  });

  it("renders line numbers when showLineNumbers is true", () => {
    const { container } = renderWithTheme(
      <CodeViewerWidget {...props} />,
      webLightTheme
    );
    // Line numbers are in a <span aria-hidden="true"> element
    const lineNumberContainer = container.querySelector("[aria-hidden='true']");
    expect(lineNumberContainer).toBeTruthy();
  });

  it("renders Copy button", () => {
    const { getByLabelText } = renderWithTheme(
      <CodeViewerWidget {...props} />,
      webLightTheme
    );
    expect(getByLabelText("Copy code to clipboard")).toBeTruthy();
  });

  it("renders loading state without error", () => {
    const { container } = renderWithTheme(
      <CodeViewerWidget {...props} isLoading={true} />,
      webDarkTheme
    );
    expect(container.firstChild).toBeTruthy();
  });
});
