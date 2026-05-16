/**
 * TimelineWidget — Dark mode and NFR tests
 *
 * Task: AIPU-051 — Dark Mode and NFR Verification
 * Covers:
 *   - NFR-01: renders within 200ms (light and dark)
 *   - NFR-04: renders correctly under webDarkTheme
 *   - ADR-021: no hard-coded colors (verified by no-hardcoded-colors.test.ts)
 */

import React from "react";
import "@testing-library/jest-dom";
import TimelineWidget from "../TimelineWidget";
import {
  renderWithTheme,
  webLightTheme,
  webDarkTheme,
  mockTimelineProps,
} from "../../__tests__/test-utils";

describe("TimelineWidget", () => {
  const props = mockTimelineProps();

  it("renders in light theme without error", () => {
    const { container } = renderWithTheme(
      <TimelineWidget {...props} />,
      webLightTheme
    );
    expect(container.firstChild).toBeTruthy();
  });

  it("renders in dark theme (webDarkTheme) without error", () => {
    const { container } = renderWithTheme(
      <TimelineWidget {...props} />,
      webDarkTheme
    );
    expect(container.firstChild).toBeTruthy();
  });

  it("renders within 200ms in light theme (NFR-01)", () => {
    const start = performance.now();
    renderWithTheme(<TimelineWidget {...props} />, webLightTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it("renders within 200ms in dark theme (NFR-01)", () => {
    const start = performance.now();
    renderWithTheme(<TimelineWidget {...props} />, webDarkTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it("renders event labels", () => {
    const { getByText } = renderWithTheme(
      <TimelineWidget {...props} />,
      webLightTheme
    );
    expect(getByText("Contract Signed")).toBeTruthy();
    expect(getByText("First Review")).toBeTruthy();
  });

  it("renders event dates", () => {
    const { getByText } = renderWithTheme(
      <TimelineWidget {...props} />,
      webLightTheme
    );
    expect(getByText("2024-01-15")).toBeTruthy();
  });

  it("renders loading state in dark theme without error", () => {
    const { container } = renderWithTheme(
      <TimelineWidget {...props} isLoading={true} />,
      webDarkTheme
    );
    expect(container.firstChild).toBeTruthy();
  });
});
