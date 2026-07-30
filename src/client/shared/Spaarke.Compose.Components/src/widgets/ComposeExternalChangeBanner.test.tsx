/**
 * ComposeExternalChangeBanner.test.tsx — G8 (FR-07, task 030) coverage for the external-change
 * refresh banner. Presentational component, so tested in isolation (no @spaarke/* sibling imports —
 * runs in the worktree jest, unlike the ComposeWorkspace host tests).
 *
 * Covers: renders only when pending; the FIXED banner wording; the dirty vs clean shapes (Reload
 * shown only when dirty → NFR-08 explicit-choice); dismiss; ADR-021 dark-mode render.
 */

import * as React from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import {
  ComposeExternalChangeBanner,
  EXTERNAL_CHANGE_BANNER_TEXT,
} from './ComposeExternalChangeBanner';

function renderBanner(
  props: Partial<React.ComponentProps<typeof ComposeExternalChangeBanner>> = {},
  theme = webLightTheme
) {
  const merged = {
    pending: true,
    hasUnsavedEdits: false,
    onReload: jest.fn(),
    ...props,
  };
  return {
    ...render(
      <FluentProvider theme={theme}>
        <ComposeExternalChangeBanner {...merged} />
      </FluentProvider>
    ),
    props: merged,
  };
}

describe('ComposeExternalChangeBanner (G8 task 030)', () => {
  it('renders nothing when no external change is pending', () => {
    renderBanner({ pending: false });
    expect(screen.queryByTestId('compose-external-change-banner')).not.toBeInTheDocument();
  });

  it('renders the fixed FR-07 wording when pending', () => {
    renderBanner({ pending: true });
    expect(screen.getByTestId('compose-external-change-banner')).toBeInTheDocument();
    expect(screen.getByText(EXTERNAL_CHANGE_BANNER_TEXT)).toBeInTheDocument();
    expect(EXTERNAL_CHANGE_BANNER_TEXT).toBe('Document updated from document management system version');
  });

  it('clean editor: informational only — NO Reload action (parent already remounted)', () => {
    renderBanner({ pending: true, hasUnsavedEdits: false });
    expect(screen.queryByTestId('compose-external-change-banner-reload')).not.toBeInTheDocument();
    expect(screen.queryByTestId('compose-external-change-banner-unsaved')).not.toBeInTheDocument();
  });

  it('dirty editor: shows an explicit Reload action + an unsaved-edits warning (NFR-08 — never silent)', async () => {
    const user = userEvent.setup();
    const onReload = jest.fn();
    renderBanner({ pending: true, hasUnsavedEdits: true, onReload });

    expect(screen.getByTestId('compose-external-change-banner-unsaved')).toBeInTheDocument();
    const reload = screen.getByTestId('compose-external-change-banner-reload');
    expect(reload).toBeInTheDocument();
    await user.click(reload);
    expect(onReload).toHaveBeenCalledTimes(1);
  });

  it('fires onDismiss when the dismiss affordance is clicked', async () => {
    const user = userEvent.setup();
    const onDismiss = jest.fn();
    renderBanner({ pending: true, onDismiss });

    await user.click(screen.getByTestId('compose-external-change-banner-dismiss'));
    expect(onDismiss).toHaveBeenCalledTimes(1);
  });

  it('omits the dismiss affordance when no onDismiss is wired', () => {
    renderBanner({ pending: true });
    expect(screen.queryByTestId('compose-external-change-banner-dismiss')).not.toBeInTheDocument();
  });

  it('ADR-021: renders under a dark theme (theme tokens, no crash)', () => {
    renderBanner({ pending: true, hasUnsavedEdits: true }, webDarkTheme);
    expect(screen.getByTestId('compose-external-change-banner')).toBeInTheDocument();
    expect(screen.getByText(EXTERNAL_CHANGE_BANNER_TEXT)).toBeInTheDocument();
  });
});
