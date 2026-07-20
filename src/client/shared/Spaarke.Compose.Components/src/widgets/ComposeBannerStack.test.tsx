/**
 * ComposeBannerStack.test.tsx — DEF-15 (UAT-R3) coverage for the dismissible
 * "Document opened with N simplification(s)" import-warning banner.
 *
 * Focus: the warning renders a dismiss (×) control and hides on click, while
 * the OTHER banners in the stack are unaffected by that dismissal.
 */

import * as React from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { ComposeBannerStack, type ComposeBannerStackProps } from './ComposeBannerStack';

function renderStack(overrides: Partial<ComposeBannerStackProps> = {}, theme: typeof webLightTheme = webLightTheme) {
  const props: ComposeBannerStackProps = {
    errorMessage: null,
    checkoutStatus: 'idle',
    checkoutLockedBy: null,
    checkoutFailureMessage: null,
    importWarnings: [],
    pendingAssistantInsert: null,
    ...overrides,
  };
  return render(
    <FluentProvider theme={theme}>
      <ComposeBannerStack {...props} />
    </FluentProvider>
  );
}

const TWO_WARNINGS = [
  { type: 'ignored', message: 'a' },
  { type: 'ignored', message: 'b' },
];

describe('ComposeBannerStack — DEF-15 dismissible simplification warning', () => {
  // FR-21 (R3 carry-in): dismissal is now content-signature-keyed sessionStorage (see
  // ComposeBannerStack.tsx). Several `it`s below reuse the SAME `TWO_WARNINGS` content, so the
  // sentinel must be cleared between tests or an earlier test's dismissal would leak into a later
  // one (sessionStorage persists for the whole jsdom test-file lifetime, not per-`it`).
  beforeEach(() => {
    window.sessionStorage.clear();
  });

  it('renders the simplification warning with a dismiss control', () => {
    renderStack({ importWarnings: TWO_WARNINGS });

    expect(screen.getByTestId('compose-workspace-import-warning-banner')).toBeInTheDocument();
    expect(screen.getByText('Document opened with 2 simplification(s)')).toBeInTheDocument();
    expect(screen.getByTestId('compose-workspace-import-warning-dismiss')).toBeInTheDocument();
    expect(screen.getByLabelText('Dismiss')).toBeInTheDocument();
  });

  it('hides the warning after the dismiss control is clicked (per-mount)', async () => {
    const user = userEvent.setup();
    renderStack({ importWarnings: TWO_WARNINGS });

    await user.click(screen.getByTestId('compose-workspace-import-warning-dismiss'));

    expect(screen.queryByTestId('compose-workspace-import-warning-banner')).not.toBeInTheDocument();
  });

  it('dismissing the warning does NOT hide other banners in the stack', async () => {
    const user = userEvent.setup();
    renderStack({ importWarnings: TWO_WARNINGS, errorMessage: 'Save failed' });

    await user.click(screen.getByTestId('compose-workspace-import-warning-dismiss'));

    expect(screen.queryByTestId('compose-workspace-import-warning-banner')).not.toBeInTheDocument();
    // The unrelated save-error banner stays visible.
    expect(screen.getByTestId('compose-workspace-error-banner')).toBeInTheDocument();
  });

  it('re-shows the warning when a NEW set of import warnings arrives after a dismissal', async () => {
    const user = userEvent.setup();
    const { rerender } = renderStack({ importWarnings: TWO_WARNINGS });

    await user.click(screen.getByTestId('compose-workspace-import-warning-dismiss'));
    expect(screen.queryByTestId('compose-workspace-import-warning-banner')).not.toBeInTheDocument();

    // A fresh document load hands a NEW array reference → dismissal resets.
    rerender(
      <FluentProvider theme={webLightTheme}>
        <ComposeBannerStack
          errorMessage={null}
          checkoutStatus="idle"
          checkoutLockedBy={null}
          checkoutFailureMessage={null}
          importWarnings={[{ type: 'ignored', message: 'c' }]}
          pendingAssistantInsert={null}
        />
      </FluentProvider>
    );

    expect(screen.getByTestId('compose-workspace-import-warning-banner')).toBeInTheDocument();
    expect(screen.getByText('Document opened with 1 simplification(s)')).toBeInTheDocument();
  });

  it('dark mode (ADR-021): renders with no hardcoded hex color', () => {
    const { container } = renderStack({ importWarnings: TWO_WARNINGS }, webDarkTheme);
    expect(screen.getByTestId('compose-workspace-import-warning-banner')).toBeInTheDocument();
    expect(container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
  });
});
