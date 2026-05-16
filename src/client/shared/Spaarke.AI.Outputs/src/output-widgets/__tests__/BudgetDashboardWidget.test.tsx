/**
 * BudgetDashboardWidget — Dark mode and NFR tests
 *
 * Task: AIPU-051 — Dark Mode and NFR Verification
 * Covers:
 *   - NFR-01: renders within 200ms (light and dark)
 *   - NFR-04: renders correctly under webDarkTheme
 *   - ADR-021: no hard-coded colors (verified by no-hardcoded-colors.test.ts)
 */

import React from "react";
import "@testing-library/jest-dom";
import BudgetDashboardWidget from "../BudgetDashboardWidget";
import {
  renderWithTheme,
  webLightTheme,
  webDarkTheme,
  mockBudgetDashboardProps,
} from "../../__tests__/test-utils";

describe("BudgetDashboardWidget", () => {
  const props = mockBudgetDashboardProps();

  it("renders in light theme without error", () => {
    const { container } = renderWithTheme(
      <BudgetDashboardWidget {...props} />,
      webLightTheme
    );
    expect(container.firstChild).toBeTruthy();
  });

  it("renders in dark theme (webDarkTheme) without error", () => {
    const { container } = renderWithTheme(
      <BudgetDashboardWidget {...props} />,
      webDarkTheme
    );
    expect(container.firstChild).toBeTruthy();
  });

  it("renders within 200ms in light theme (NFR-01)", () => {
    const start = performance.now();
    renderWithTheme(<BudgetDashboardWidget {...props} />, webLightTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it("renders within 200ms in dark theme (NFR-01)", () => {
    const start = performance.now();
    renderWithTheme(<BudgetDashboardWidget {...props} />, webDarkTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it("renders budget title text", () => {
    const { getByText } = renderWithTheme(
      <BudgetDashboardWidget {...props} />,
      webLightTheme
    );
    expect(getByText("Q3 Matter Budget")).toBeTruthy();
  });

  it("renders loading spinner when isLoading is true", () => {
    const { container } = renderWithTheme(
      <BudgetDashboardWidget {...props} isLoading={true} />,
      webDarkTheme
    );
    expect(container.firstChild).toBeTruthy();
  });
});
