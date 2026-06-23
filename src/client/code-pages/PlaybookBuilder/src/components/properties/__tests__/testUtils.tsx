/**
 * Test utilities for PlaybookBuilder component tests (R3 task 094).
 *
 * Provides the canonical `renderWithProviders` helper used across the H2 suite.
 * Mirrors the sibling Spaarke.UI.Components/__mocks__/pcfMocks.tsx pattern —
 * Fluent UI v9 FluentProvider wrapper around RTL.render. Per CLAUDE.md root §10
 * and ADR-021: tests assert semantic-token usage (no hardcoded hex). The
 * dark-mode parity test (`renderInDarkMode`) re-renders the same component
 * under webDarkTheme so token-driven styles can be inspected for drift.
 */
import * as React from 'react';
import { render, RenderOptions } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme, Theme } from '@fluentui/react-components';

/** Render with light theme (default for behavior tests). */
export const renderWithProviders = (ui: React.ReactElement, options?: Omit<RenderOptions, 'wrapper'>) => {
  const Wrapper = ({ children }: { children: React.ReactNode }) => (
    <FluentProvider theme={webLightTheme}>{children}</FluentProvider>
  );
  return render(ui, { wrapper: Wrapper, ...options });
};

/** Render with a chosen theme (used by dark-mode parity tests). */
export const renderWithTheme = (ui: React.ReactElement, theme: Theme, options?: Omit<RenderOptions, 'wrapper'>) => {
  const Wrapper = ({ children }: { children: React.ReactNode }) => (
    <FluentProvider theme={theme}>{children}</FluentProvider>
  );
  return render(ui, { wrapper: Wrapper, ...options });
};

export { webLightTheme, webDarkTheme };
