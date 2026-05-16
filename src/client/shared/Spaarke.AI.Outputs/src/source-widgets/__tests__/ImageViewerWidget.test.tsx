/**
 * ImageViewerWidget — Dark mode and NFR tests
 *
 * Task: AIPU-051 — Dark Mode and NFR Verification
 * Covers:
 *   - NFR-01: renders within 200ms (light and dark)
 *   - NFR-04: renders correctly under webDarkTheme
 *   - ADR-021: no hard-coded colors (verified by no-hardcoded-colors.test.ts)
 *
 * NOTE: ImageViewerWidget uses mouse/wheel event handlers for pan+zoom.
 * Image loading from external URLs is not tested (jsdom does not load images).
 * The widget chrome (toolbar buttons, caption) is verified instead.
 */

import React from "react";
import "@testing-library/jest-dom";
import ImageViewerWidget from "../ImageViewerWidget";
import {
  renderWithTheme,
  webLightTheme,
  webDarkTheme,
  mockImageViewerProps,
} from "../../__tests__/test-utils";

describe("ImageViewerWidget", () => {
  const props = mockImageViewerProps();

  it("renders in light theme without error", () => {
    const { container } = renderWithTheme(
      <ImageViewerWidget {...props} />,
      webLightTheme
    );
    expect(container.firstChild).toBeTruthy();
  });

  it("renders in dark theme (webDarkTheme) without error", () => {
    const { container } = renderWithTheme(
      <ImageViewerWidget {...props} />,
      webDarkTheme
    );
    expect(container.firstChild).toBeTruthy();
  });

  it("renders within 200ms in light theme (NFR-01)", () => {
    const start = performance.now();
    renderWithTheme(<ImageViewerWidget {...props} />, webLightTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it("renders within 200ms in dark theme (NFR-01)", () => {
    const start = performance.now();
    renderWithTheme(<ImageViewerWidget {...props} />, webDarkTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it("renders the image caption", () => {
    const { getByText } = renderWithTheme(
      <ImageViewerWidget {...props} />,
      webLightTheme
    );
    expect(getByText("Figure 1: Agreement structure")).toBeTruthy();
  });

  it("renders zoom control buttons", () => {
    const { getByLabelText } = renderWithTheme(
      <ImageViewerWidget {...props} />,
      webLightTheme
    );
    expect(getByLabelText("Zoom in")).toBeTruthy();
    expect(getByLabelText("Zoom out")).toBeTruthy();
    expect(getByLabelText("Reset zoom and pan")).toBeTruthy();
  });

  it("renders the image element with alt text", () => {
    const { container } = renderWithTheme(
      <ImageViewerWidget {...props} />,
      webLightTheme
    );
    const img = container.querySelector("img");
    expect(img).toBeTruthy();
    expect(img?.getAttribute("alt")).toBe("Contract diagram");
  });

  it("renders loading state without error", () => {
    const { container } = renderWithTheme(
      <ImageViewerWidget {...props} isLoading={true} />,
      webDarkTheme
    );
    expect(container.firstChild).toBeTruthy();
  });
});
