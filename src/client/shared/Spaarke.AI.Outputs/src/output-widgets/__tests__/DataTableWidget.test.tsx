/**
 * DataTableWidget — Dark mode and NFR tests
 *
 * Task: AIPU-051 — Dark Mode and NFR Verification
 * Covers:
 *   - NFR-01: renders within 200ms (light and dark)
 *   - NFR-04: renders correctly under webDarkTheme
 *   - ADR-021: no hard-coded colors (verified by no-hardcoded-colors.test.ts)
 */

import React from "react";
import "@testing-library/jest-dom";
import DataTableWidget from "../DataTableWidget";
import {
  renderWithTheme,
  webLightTheme,
  webDarkTheme,
  mockDataTableProps,
} from "../../__tests__/test-utils";

describe("DataTableWidget", () => {
  const props = mockDataTableProps();

  it("renders in light theme without error", () => {
    const { container } = renderWithTheme(
      <DataTableWidget {...props} />,
      webLightTheme
    );
    expect(container.firstChild).toBeTruthy();
  });

  it("renders in dark theme (webDarkTheme) without error", () => {
    const { container } = renderWithTheme(
      <DataTableWidget {...props} />,
      webDarkTheme
    );
    expect(container.firstChild).toBeTruthy();
  });

  it("renders within 200ms in light theme (NFR-01)", () => {
    const start = performance.now();
    renderWithTheme(<DataTableWidget {...props} />, webLightTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it("renders within 200ms in dark theme (NFR-01)", () => {
    const start = performance.now();
    renderWithTheme(<DataTableWidget {...props} />, webDarkTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it("renders column headers", () => {
    const { getByText } = renderWithTheme(
      <DataTableWidget {...props} />,
      webLightTheme
    );
    expect(getByText("Name")).toBeTruthy();
    expect(getByText("Amount")).toBeTruthy();
  });

  it("renders row data", () => {
    const { getByText } = renderWithTheme(
      <DataTableWidget {...props} />,
      webLightTheme
    );
    expect(getByText("Legal Fees")).toBeTruthy();
    expect(getByText("Disbursements")).toBeTruthy();
  });

  it("renders filter input", () => {
    const { container } = renderWithTheme(
      <DataTableWidget {...props} />,
      webLightTheme
    );
    const input = container.querySelector("input");
    expect(input).toBeTruthy();
  });

  it("renders loading state in dark theme without error", () => {
    const { container } = renderWithTheme(
      <DataTableWidget {...props} isLoading={true} />,
      webDarkTheme
    );
    expect(container.firstChild).toBeTruthy();
  });
});
