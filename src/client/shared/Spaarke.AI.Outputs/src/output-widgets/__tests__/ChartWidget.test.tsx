/**
 * ChartWidget — Dark mode and NFR tests
 *
 * Task: AIPU-051 — Dark Mode and NFR Verification
 * Covers:
 *   - NFR-01: renders within 200ms (light and dark)
 *   - NFR-04: renders correctly under webDarkTheme
 *   - ADR-021: no hard-coded colors (verified by no-hardcoded-colors.test.ts)
 *
 * NOTE: ChartWidget uses ResizeObserver to size its SVG. jsdom does not
 * implement ResizeObserver, so the SVG chart may not render inside the
 * container (size stays at 0,0). The widget root and title will still
 * render. The ResizeObserver is mocked to verify the component mounts.
 */

import React from "react";
import "@testing-library/jest-dom";
import ChartWidget from "../ChartWidget";
import {
  renderWithTheme,
  webLightTheme,
  webDarkTheme,
  mockChartProps,
} from "../../__tests__/test-utils";

// Mock ResizeObserver — jsdom does not implement it
global.ResizeObserver = class ResizeObserver {
  observe() {}
  unobserve() {}
  disconnect() {}
};

describe("ChartWidget", () => {
  const props = mockChartProps();

  it("renders in light theme without error", () => {
    const { container } = renderWithTheme(
      <ChartWidget {...props} />,
      webLightTheme
    );
    expect(container.firstChild).toBeTruthy();
  });

  it("renders in dark theme (webDarkTheme) without error", () => {
    const { container } = renderWithTheme(
      <ChartWidget {...props} />,
      webDarkTheme
    );
    expect(container.firstChild).toBeTruthy();
  });

  it("renders within 200ms in light theme (NFR-01)", () => {
    const start = performance.now();
    renderWithTheme(<ChartWidget {...props} />, webLightTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it("renders within 200ms in dark theme (NFR-01)", () => {
    const start = performance.now();
    renderWithTheme(<ChartWidget {...props} />, webDarkTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it("renders chart title", () => {
    const { getByText } = renderWithTheme(
      <ChartWidget {...props} />,
      webLightTheme
    );
    expect(getByText("Matter Costs")).toBeTruthy();
  });

  it("renders loading state in dark theme without error", () => {
    const { container } = renderWithTheme(
      <ChartWidget {...props} isLoading={true} />,
      webDarkTheme
    );
    expect(container.firstChild).toBeTruthy();
  });
});
