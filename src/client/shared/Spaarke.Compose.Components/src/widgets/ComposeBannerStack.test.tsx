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

describe('ComposeBannerStack — UAT-12 annotation-read-failed warning (do not treat as clean)', () => {
  it('renders the honest "tracked changes and comments couldn\'t be read" banner', () => {
    renderStack({ annotationReadFailed: true });
    expect(screen.getByTestId('compose-workspace-annotation-read-failed-banner')).toBeInTheDocument();
    expect(screen.getByText("Tracked changes and comments couldn't be read")).toBeInTheDocument();
  });

  it('renders nothing for the banner when annotationReadFailed is false/omitted', () => {
    renderStack({ annotationReadFailed: false });
    expect(screen.queryByTestId('compose-workspace-annotation-read-failed-banner')).not.toBeInTheDocument();
  });

  it('hides the banner on dismiss', async () => {
    const user = userEvent.setup();
    renderStack({ annotationReadFailed: true });
    await user.click(screen.getByTestId('compose-workspace-annotation-read-failed-dismiss'));
    expect(screen.queryByTestId('compose-workspace-annotation-read-failed-banner')).not.toBeInTheDocument();
  });
});

describe('ComposeBannerStack — UAT save-driven "not saved yet" notice', () => {
  it('renders the doc-only notice when no review ran', () => {
    renderStack({ unsavedDocumentNotice: { reviewRan: false } });
    expect(screen.getByTestId('compose-workspace-unsaved-notice')).toBeInTheDocument();
    expect(screen.getByText('Not saved yet')).toBeInTheDocument();
    expect(screen.getByTestId('compose-workspace-unsaved-notice').textContent).toContain(
      'This document hasn’t been saved yet'
    );
  });

  it('mentions the analysis when a review ran', () => {
    renderStack({ unsavedDocumentNotice: { reviewRan: true } });
    expect(screen.getByTestId('compose-workspace-unsaved-notice').textContent).toContain(
      'This document and its analysis haven’t been saved yet'
    );
  });

  it('renders nothing when the document is saved (notice null)', () => {
    renderStack({ unsavedDocumentNotice: null });
    expect(screen.queryByTestId('compose-workspace-unsaved-notice')).not.toBeInTheDocument();
  });
});

describe('ComposeBannerStack — UAT-13 association-orphan warning (saved but not filed)', () => {
  it('renders the honest "not filed under its matter" banner with a Retry action', () => {
    const onRetry = jest.fn();
    renderStack({ associationWarning: { documentRecordId: 'doc-1' }, onRetryAssociation: onRetry });

    expect(screen.getByTestId('compose-workspace-association-warning-banner')).toBeInTheDocument();
    expect(screen.getByText('Saved, but not filed under its matter')).toBeInTheDocument();
    expect(screen.getByTestId('compose-workspace-association-warning-retry')).toBeInTheDocument();
  });

  it('invokes onRetryAssociation when Retry is clicked', async () => {
    const user = userEvent.setup();
    const onRetry = jest.fn();
    renderStack({ associationWarning: { documentRecordId: 'doc-1' }, onRetryAssociation: onRetry });

    await user.click(screen.getByTestId('compose-workspace-association-warning-retry'));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it('hides the banner on dismiss', async () => {
    const user = userEvent.setup();
    renderStack({ associationWarning: { documentRecordId: 'doc-1' }, onRetryAssociation: jest.fn() });

    await user.click(screen.getByTestId('compose-workspace-association-warning-dismiss'));
    expect(screen.queryByTestId('compose-workspace-association-warning-banner')).not.toBeInTheDocument();
  });

  it('renders nothing for the banner when associationWarning is null', () => {
    renderStack({ associationWarning: null });
    expect(screen.queryByTestId('compose-workspace-association-warning-banner')).not.toBeInTheDocument();
  });
});

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
    const { container } = renderStack(
      { reviewFindingsDegraded: { expectedCount: 1, reason: 'malformed' } },
      webDarkTheme
    );
    expect(screen.getByTestId('compose-workspace-review-findings-degraded-banner')).toBeInTheDocument();
    expect(container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
  });
});

// 026-F5 (spaarkeai-compose-r6 task 012) — SAVE-time degradation warnings: their own banner family,
// deliberately NOT gated by hideImportWarnings (that suppression covers only the load-time import
// banner), signature-keyed sessionStorage dismissal on a SEPARATE key, cleared when the parent passes
// null (a clean save).
describe('ComposeBannerStack — task 012 save-degradation banner (026-F5)', () => {
  beforeEach(() => window.sessionStorage.clear());

  const SAVE_WARNINGS = [
    { code: 'text-box-flattened', count: 1 },
    { code: 'comment-anchor-dropped', count: 2 },
  ];

  it('renders save warnings with friendly copy EVEN while hideImportWarnings suppresses the import banner', () => {
    renderStack({
      importWarnings: TWO_WARNINGS,
      hideImportWarnings: true,
      saveDegradationWarnings: SAVE_WARNINGS,
    });

    // Import banner suppressed (UAT round-7 #8) …
    expect(screen.queryByTestId('compose-workspace-import-warning-banner')).not.toBeInTheDocument();
    // … but the SAVE-degradation banner still renders — the 026-F5 fix.
    const banner = screen.getByTestId('compose-workspace-save-degradation-banner');
    expect(banner).toBeInTheDocument();
    // UAT-07b (committed earlier) renamed the banner TITLE to "Some formatting was simplified when
    // saving"; this assertion was left on the old "content" wording. Match the shipped title.
    expect(screen.getByText('Some formatting was simplified when saving')).toBeInTheDocument();
    expect(banner.textContent).toContain('A text box was converted to regular text.');
    expect(banner.textContent).toContain("A comment's anchor could not be placed; the comment text was kept. (×2)");
  });

  // UAT-S-01 (2026-08-21, owner UAT of task 017). This banner renders ONLY from the server's response
  // to a COMPLETED save (ComposeWorkspace dispatches `saveDegradationWarnings` from `payload.degradation‑
  // Warnings` on the success branch, and the post-save re-mount carries it forward). Its trailer used to
  // read "The original file is unchanged until you save." — false at every moment it was on screen: the
  // bytes were already written and already carried the simplification being described. The owner saw
  // exactly this in dev. Assert the honest trailer AND assert the false claim is gone, so the old copy
  // cannot come back.
  it('does NOT claim the original file is unchanged — this banner only renders AFTER a completed save', () => {
    renderStack({ saveDegradationWarnings: SAVE_WARNINGS });

    const banner = screen.getByTestId('compose-workspace-save-degradation-banner');
    expect(banner.textContent).not.toMatch(/original file is unchanged/i);
    expect(banner.textContent).not.toMatch(/until you save/i);
    expect(banner.textContent).toContain('These changes are in the version you just saved.');
    expect(banner.textContent).toMatch(/version history/i);
  });

  it('falls back to the generic "(code ×N)" copy for an unknown code', () => {
    renderStack({ saveDegradationWarnings: [{ code: 'mystery-degradation', count: 3 }] });

    const banner = screen.getByTestId('compose-workspace-save-degradation-banner');
    expect(banner.textContent).toContain('Some content was simplified when saving (mystery-degradation ×3).');
  });

  it('renders friendly copy (not the raw code) for previously-cryptic degradation codes (copy-gap 2026-08-18)', () => {
    renderStack({
      saveDegradationWarnings: [
        { code: 'unrepresented-footnote-reference', count: 1 },
        { code: 'field-flattened-to-text', count: 1 },
        { code: 'hard-tier-sdt-flattened', count: 1 },
      ],
    });
    const banner = screen.getByTestId('compose-workspace-save-degradation-banner');
    expect(banner.textContent).toContain("A footnote couldn't be carried into the saved document.");
    expect(banner.textContent).toContain('was saved as plain text and will no longer update automatically.');
    expect(banner.textContent).toContain('A content control');
    // The raw codes must NOT leak into the user-facing copy.
    expect(banner.textContent).not.toContain('unrepresented-footnote-reference');
    expect(banner.textContent).not.toContain('field-flattened-to-text');
  });

  it('renders nothing for null or an empty set (a clean save clears the banner)', () => {
    renderStack({ saveDegradationWarnings: null });
    expect(screen.queryByTestId('compose-workspace-save-degradation-banner')).not.toBeInTheDocument();

    renderStack({ saveDegradationWarnings: [] });
    expect(screen.queryByTestId('compose-workspace-save-degradation-banner')).not.toBeInTheDocument();
  });

  it('dismisses via sessionStorage (SEPARATE key) and a NEW warning set (different signature) re-shows', async () => {
    const user = userEvent.setup();
    const { rerender } = renderStack({ saveDegradationWarnings: SAVE_WARNINGS });

    await user.click(screen.getByTestId('compose-workspace-save-degradation-dismiss'));
    expect(screen.queryByTestId('compose-workspace-save-degradation-banner')).not.toBeInTheDocument();
    // The dismissal landed on the save-degradation key, NOT the import-warnings key.
    const keys = Object.keys(window.sessionStorage);
    expect(keys.some(k => k.startsWith('spaarke-compose:save-degradation-dismissed:'))).toBe(true);
    expect(keys.some(k => k.startsWith('spaarke-compose:import-warnings-dismissed:'))).toBe(false);

    // A DIFFERENT warning set re-shows.
    rerender(
      <FluentProvider theme={webLightTheme}>
        <ComposeBannerStack
          errorMessage={null}
          checkoutStatus="idle"
          checkoutLockedBy={null}
          checkoutFailureMessage={null}
          importWarnings={[]}
          pendingAssistantInsert={null}
          saveDegradationWarnings={[{ code: 'tracked-move-downgraded', count: 1 }]}
        />
      </FluentProvider>
    );
    expect(screen.getByTestId('compose-workspace-save-degradation-banner')).toBeInTheDocument();
    expect(screen.getByTestId('compose-workspace-save-degradation-banner').textContent).toContain(
      'A tracked move was saved as delete + insert.'
    );
  });

  it('dismissing the SAVE banner does not affect the import banner (separate families)', async () => {
    const user = userEvent.setup();
    renderStack({ importWarnings: TWO_WARNINGS, saveDegradationWarnings: SAVE_WARNINGS });

    // Both visible (hideImportWarnings not set here).
    expect(screen.getByTestId('compose-workspace-import-warning-banner')).toBeInTheDocument();
    expect(screen.getByTestId('compose-workspace-save-degradation-banner')).toBeInTheDocument();

    await user.click(screen.getByTestId('compose-workspace-save-degradation-dismiss'));
    expect(screen.queryByTestId('compose-workspace-save-degradation-banner')).not.toBeInTheDocument();
    expect(screen.getByTestId('compose-workspace-import-warning-banner')).toBeInTheDocument();
  });

  it('dark mode (ADR-021): renders with no hardcoded hex color', () => {
    const { container } = renderStack({ saveDegradationWarnings: SAVE_WARNINGS }, webDarkTheme);
    expect(screen.getByTestId('compose-workspace-save-degradation-banner')).toBeInTheDocument();
    expect(container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
  });
});

// ---------------------------------------------------------------------------
// Task 042 (FR-06, PDF intake) — the "Opened from PDF" honest-lossiness notice
// (041-review binding test plan: render / honest copy / dismiss / re-warn / retire).
// ---------------------------------------------------------------------------

describe('ComposeBannerStack — "Opened from PDF" honest-lossiness notice (task 042 / FR-06)', () => {
  beforeEach(() => {
    window.sessionStorage.clear();
  });

  it('renders the notice with HONEST copy: fixed-layout, new-Word-document, version history — and NO "identical to source" claim', () => {
    renderStack({ pdfSourceNotice: true });

    const banner = screen.getByTestId('compose-workspace-pdf-source-banner');
    expect(banner).toBeInTheDocument();
    expect(screen.getByText('Opened from PDF')).toBeInTheDocument();
    // Honesty contract (spec FR-06): fixed-layout lossiness + save-creates-new-Word-doc + the
    // version-history safety net — and never a false fidelity claim.
    expect(banner.textContent).toMatch(/fixed-layout PDF/i);
    expect(banner.textContent).toMatch(/new Word document/i);
    expect(banner.textContent).toMatch(/version history/i);
    expect(banner.textContent).not.toMatch(/identical to (the )?source/i);
  });

  it('does not render when pdfSourceNotice is false/omitted (native docx loads unchanged)', () => {
    renderStack({});
    expect(screen.queryByTestId('compose-workspace-pdf-source-banner')).not.toBeInTheDocument();
  });

  it('dismiss hides the notice; a FRESH PDF open (false → true transition) re-warns', async () => {
    const user = userEvent.setup();
    const { rerender } = renderStack({ pdfSourceNotice: true });

    await user.click(screen.getByTestId('compose-workspace-pdf-source-dismiss'));
    expect(screen.queryByTestId('compose-workspace-pdf-source-banner')).not.toBeInTheDocument();

    // The parent drops the prop between documents (requestLoad resets sourceFormat)…
    rerender(
      <FluentProvider theme={webLightTheme}>
        <ComposeBannerStack
          errorMessage={null}
          checkoutStatus="idle"
          checkoutLockedBy={null}
          checkoutFailureMessage={null}
          importWarnings={[]}
          pendingAssistantInsert={null}
          pdfSourceNotice={false}
        />
      </FluentProvider>
    );
    // …and a fresh PDF open re-warns (honesty over convenience — per-open, not per-session).
    rerender(
      <FluentProvider theme={webLightTheme}>
        <ComposeBannerStack
          errorMessage={null}
          checkoutStatus="idle"
          checkoutLockedBy={null}
          checkoutFailureMessage={null}
          importWarnings={[]}
          pendingAssistantInsert={null}
          pdfSourceNotice={true}
        />
      </FluentProvider>
    );
    expect(screen.getByTestId('compose-workspace-pdf-source-banner')).toBeInTheDocument();
  });

  it('retires when the parent clears the prop after the first successful save (new docx identity)', () => {
    const { rerender } = renderStack({ pdfSourceNotice: true });
    expect(screen.getByTestId('compose-workspace-pdf-source-banner')).toBeInTheDocument();

    rerender(
      <FluentProvider theme={webLightTheme}>
        <ComposeBannerStack
          errorMessage={null}
          checkoutStatus="idle"
          checkoutLockedBy={null}
          checkoutFailureMessage={null}
          importWarnings={[]}
          pendingAssistantInsert={null}
          pdfSourceNotice={false}
        />
      </FluentProvider>
    );
    expect(screen.queryByTestId('compose-workspace-pdf-source-banner')).not.toBeInTheDocument();
  });

  it('renders correctly under the dark theme (ADR-021 — semantic tokens only, no hardcoded colors)', () => {
    renderStack({ pdfSourceNotice: true }, webDarkTheme);
    expect(screen.getByTestId('compose-workspace-pdf-source-banner')).toBeInTheDocument();
  });

  it('pdf-intake-* degradation codes carry friendly copy in the save-warning banner (no raw codes shown to the user)', () => {
    renderStack({
      saveDegradationWarnings: [
        { code: 'pdf-intake-fixed-layout-reflowed', count: 2 },
        { code: 'pdf-intake-table-cell-consolidated', count: 1 },
        { code: 'pdf-intake-table-cell-dropped', count: 1 },
      ],
    });
    const banner = screen.getByTestId('compose-workspace-save-degradation-banner');
    expect(banner.textContent).toMatch(/reflowed from the fixed PDF page layout/i);
    expect(banner.textContent).toMatch(/combined into one cell/i);
    expect(banner.textContent).toMatch(/could not be placed/i);
    // Friendly copy exists for every code — the raw kebab code never leaks into the sentence.
    expect(banner.textContent).not.toMatch(/pdf-intake-fixed-layout-reflowed/);
  });
});
