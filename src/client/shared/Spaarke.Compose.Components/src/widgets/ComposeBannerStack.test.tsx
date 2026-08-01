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
    expect(screen.getByText('Some formatting was simplified')).toBeInTheDocument();
    expect(screen.getByTestId('compose-workspace-import-warning-dismiss')).toBeInTheDocument();
    expect(screen.getByLabelText('Dismiss')).toBeInTheDocument();
  });

  // UAT #10/#11 (task 052) — Word co-authoring lock (423) honest banner.
  it('shows the generic Save-error bar when errorMessage is set and it is NOT a lock', () => {
    renderStack({ errorMessage: 'Failed to save document (HTTP 500).' });
    expect(screen.getByTestId('compose-workspace-error-banner')).toBeInTheDocument();
    expect(screen.queryByTestId('compose-workspace-word-lock-banner')).not.toBeInTheDocument();
  });

  it('shows the honest "Open in Word" bar with Retry + Reload when the save failed with a Word lock', async () => {
    const user = userEvent.setup();
    const onRetrySave = jest.fn();
    const onReloadFromWord = jest.fn();
    renderStack({
      errorMessage: 'This document is open in Word — close it there, then Retry.',
      saveErrorIsLock: true,
      onRetrySave,
      onReloadFromWord,
    });

    expect(screen.getByTestId('compose-workspace-word-lock-banner')).toBeInTheDocument();
    expect(screen.getByText('Open in Word')).toBeInTheDocument();
    // NOT the generic error bar (no misleading "checked out — check it in").
    expect(screen.queryByTestId('compose-workspace-error-banner')).not.toBeInTheDocument();

    await user.click(screen.getByTestId('compose-word-lock-retry'));
    expect(onRetrySave).toHaveBeenCalledTimes(1);
    await user.click(screen.getByTestId('compose-word-lock-reload'));
    expect(onReloadFromWord).toHaveBeenCalledTimes(1);
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
    expect(screen.getByText('Some formatting was simplified')).toBeInTheDocument();
  });

  it('dark mode (ADR-021): renders with no hardcoded hex color', () => {
    const { container } = renderStack({ importWarnings: TWO_WARNINGS }, webDarkTheme);
    expect(screen.getByTestId('compose-workspace-import-warning-banner')).toBeInTheDocument();
    expect(container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
  });
});

// Prong 1 (task 055) — best-effort partial-apply warning banner.
describe('ComposeBannerStack — prong 1 partial-apply banner', () => {
  beforeEach(() => window.sessionStorage.clear());

  it('shows the honest partial-apply warning (applied/total + redo prompt) when some ops were unresolved', () => {
    renderStack({ partialApply: { total: 3, appliedCount: 2, unresolvedCount: 1 } });

    expect(screen.getByTestId('compose-workspace-partial-apply-banner')).toBeInTheDocument();
    expect(screen.getByText("Some edits couldn't be saved")).toBeInTheDocument();
    const body = screen.getByTestId('compose-workspace-partial-apply-banner').textContent ?? '';
    expect(body).toContain('Saved 2 of 3 edits');
    expect(body).toContain('please redo');
  });

  it('suppresses the plain "Saved ✓" success bar when a partial-apply summary is present', () => {
    renderStack({ saveSuccessToken: 1, partialApply: { total: 2, appliedCount: 1, unresolvedCount: 1 } });

    expect(screen.getByTestId('compose-workspace-partial-apply-banner')).toBeInTheDocument();
    expect(screen.queryByTestId('compose-workspace-save-success-banner')).not.toBeInTheDocument();
  });

  it('does NOT render when the whole batch applied (unresolvedCount 0 / null)', () => {
    renderStack({ partialApply: { total: 3, appliedCount: 3, unresolvedCount: 0 } });
    expect(screen.queryByTestId('compose-workspace-partial-apply-banner')).not.toBeInTheDocument();

    renderStack({ partialApply: null });
    expect(screen.queryByTestId('compose-workspace-partial-apply-banner')).not.toBeInTheDocument();
  });

  it('hides after the dismiss control is clicked', async () => {
    const user = userEvent.setup();
    renderStack({ partialApply: { total: 2, appliedCount: 1, unresolvedCount: 1 } });

    expect(screen.getByTestId('compose-workspace-partial-apply-banner')).toBeInTheDocument();
    await user.click(screen.getByTestId('compose-workspace-partial-apply-dismiss'));
    expect(screen.queryByTestId('compose-workspace-partial-apply-banner')).not.toBeInTheDocument();
  });

  it('dark mode (ADR-021): renders with no hardcoded hex color', () => {
    const { container } = renderStack(
      { partialApply: { total: 2, appliedCount: 1, unresolvedCount: 1 } },
      webDarkTheme
    );
    expect(screen.getByTestId('compose-workspace-partial-apply-banner')).toBeInTheDocument();
    expect(container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
  });
});

// ai-advanced-capabilities-agreements-r1 task 032 — FR-16 128KB budget, Leg B: an honest
// degraded-restore notice (never silent absence). Mirrors the partial-apply banner's dismiss +
// reshow-on-a-new-instance convention exactly.
describe('ComposeBannerStack — task 032 review-findings-degraded banner', () => {
  it('shows the "skipped" message with the expected count when a prior review could not be restored', () => {
    renderStack({ reviewFindingsDegraded: { expectedCount: 3, reason: 'skipped' } });

    const banner = screen.getByTestId('compose-workspace-review-findings-degraded-banner');
    expect(banner).toBeInTheDocument();
    expect(screen.getByText("Review results couldn't be fully restored")).toBeInTheDocument();
    expect(banner.textContent).toContain('about 3 findings');
    expect(banner.textContent).toMatch(/exceeded the storage limit/i);
  });

  it('shows the "malformed" message (no count claim) for a corrupted/partial payload', () => {
    renderStack({ reviewFindingsDegraded: { expectedCount: 0, reason: 'malformed' } });

    const banner = screen.getByTestId('compose-workspace-review-findings-degraded-banner');
    expect(banner.textContent).toMatch(/incomplete/i);
    expect(banner.textContent).not.toMatch(/exceeded the storage limit/i);
  });

  it('does NOT render when reviewFindingsDegraded is null/omitted', () => {
    renderStack({ reviewFindingsDegraded: null });
    expect(screen.queryByTestId('compose-workspace-review-findings-degraded-banner')).not.toBeInTheDocument();
    renderStack({});
    expect(screen.queryByTestId('compose-workspace-review-findings-degraded-banner')).not.toBeInTheDocument();
  });

  it('hides after the dismiss control is clicked', async () => {
    const user = userEvent.setup();
    renderStack({ reviewFindingsDegraded: { expectedCount: 2, reason: 'skipped' } });

    expect(screen.getByTestId('compose-workspace-review-findings-degraded-banner')).toBeInTheDocument();
    await user.click(screen.getByTestId('compose-workspace-review-findings-degraded-dismiss'));
    expect(screen.queryByTestId('compose-workspace-review-findings-degraded-banner')).not.toBeInTheDocument();
  });

  it('re-shows when a NEW degraded instance arrives after a dismissal', async () => {
    const user = userEvent.setup();
    const { rerender } = renderStack({ reviewFindingsDegraded: { expectedCount: 2, reason: 'skipped' } });

    await user.click(screen.getByTestId('compose-workspace-review-findings-degraded-dismiss'));
    expect(screen.queryByTestId('compose-workspace-review-findings-degraded-banner')).not.toBeInTheDocument();

    rerender(
      <FluentProvider theme={webLightTheme}>
        <ComposeBannerStack
          errorMessage={null}
          checkoutStatus="idle"
          checkoutLockedBy={null}
          checkoutFailureMessage={null}
          importWarnings={[]}
          pendingAssistantInsert={null}
          reviewFindingsDegraded={{ expectedCount: 5, reason: 'skipped' }}
        />
      </FluentProvider>
    );

    expect(screen.getByTestId('compose-workspace-review-findings-degraded-banner')).toBeInTheDocument();
  });

  it('dark mode (ADR-021): renders with no hardcoded hex color', () => {
    const { container } = renderStack({ reviewFindingsDegraded: { expectedCount: 1, reason: 'malformed' } }, webDarkTheme);
    expect(screen.getByTestId('compose-workspace-review-findings-degraded-banner')).toBeInTheDocument();
    expect(container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
  });
});
