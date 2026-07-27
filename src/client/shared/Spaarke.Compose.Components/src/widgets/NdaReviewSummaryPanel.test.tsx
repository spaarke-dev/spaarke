/**
 * NdaReviewSummaryPanel.test.tsx — review-summary docked panel (ai-advanced-capabilities-nda-r1
 * task 030, FR-07).
 *
 * Two layers, mirroring `ComposeCommentThread.test.tsx`'s convention:
 *  1. `deriveOverallRisk` — pure function unit tests (empty/undefined-riskLevel handling, max-severity
 *     selection across mixed findings).
 *  2. UI — `NdaReviewSummaryPanel` rendered directly (no editor dependency — this panel is
 *     presentational only): closed renders nothing, empty state, the disclaimer banner is always
 *     present, findings render with citations, the placement-failure notice is optional, and an
 *     ADR-021 dark-mode check (no hex literals in the rendered output).
 */
import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import {
  NdaReviewSummaryPanel,
  deriveOverallRisk,
  deriveTakeaway,
  formatSectionRef,
  formatClauseLocation,
  NDA_REVIEW_DISCLAIMER_TEXT,
  type NdaReviewFindingSummary,
} from './NdaReviewSummaryPanel';

// ---------------------------------------------------------------------------
// 1. deriveOverallRisk — pure function
// ---------------------------------------------------------------------------

describe('deriveOverallRisk', () => {
  it('returns undefined for an empty finding list', () => {
    expect(deriveOverallRisk([])).toBeUndefined();
  });

  it('returns undefined when no finding carries a recognized riskLevel', () => {
    expect(deriveOverallRisk([{ riskLevel: undefined }, { riskLevel: 'Unknown' }])).toBeUndefined();
  });

  it('returns the single riskLevel present', () => {
    expect(deriveOverallRisk([{ riskLevel: 'Medium' }])).toBe('Medium');
  });

  it('returns the MAXIMUM severity across mixed findings, regardless of order', () => {
    expect(deriveOverallRisk([{ riskLevel: 'Low' }, { riskLevel: 'Critical' }, { riskLevel: 'Medium' }])).toBe(
      'Critical'
    );
    expect(deriveOverallRisk([{ riskLevel: 'High' }, { riskLevel: 'Low' }])).toBe('High');
  });

  it('ignores unrecognized riskLevel strings when computing the max', () => {
    expect(deriveOverallRisk([{ riskLevel: 'Medium' }, { riskLevel: 'not-a-level' as string }])).toBe('Medium');
  });
});

// ---------------------------------------------------------------------------
// 1b. deriveTakeaway — short-headline derivation (UAT round-3 S2/S3/S4)
// ---------------------------------------------------------------------------

describe('deriveTakeaway', () => {
  it('extracts the Judgment portion and drops the "Grounded fact —" preamble (S2)', () => {
    const explanation =
      'Grounded fact — The NDA limits use to an undefined "intended purpose". ' +
      'Judgment — Deviates from the standard, creating open-ended scope and enforcement risk.';
    expect(deriveTakeaway(explanation)).toBe(
      'Deviates from the standard, creating open-ended scope and enforcement risk'
    );
  });

  it('strips a leading "This clause/agreement" so it reads as a takeaway, and capitalizes (S3/S4)', () => {
    expect(deriveTakeaway('Judgment — This clause deviates from the standard and raises risk.')).toBe(
      'Deviates from the standard and raises risk'
    );
  });

  it('reduces a multi-sentence judgment to its first sentence (S4 conciseness)', () => {
    const t = deriveTakeaway(
      'Judgment — Exceeds the standard duty of care. It also lacks a carve-out for public information.'
    );
    expect(t).toBe('Exceeds the standard duty of care');
  });

  it('falls back to the explanation (trailing period trimmed) when neither marker is present', () => {
    expect(deriveTakeaway('An indefinite term exceeds the standard survival period.')).toBe(
      'An indefinite term exceeds the standard survival period'
    );
  });

  it('returns empty string for empty input', () => {
    expect(deriveTakeaway('')).toBe('');
  });
});

// ---------------------------------------------------------------------------
// 1c. formatSectionRef — § / ¶ marker parsing (UAT round-4 #3/#9)
// ---------------------------------------------------------------------------

describe('formatSectionRef', () => {
  it('splits "Section 4.2, para 2 (p. 3)" into section 4.2 + paragraph 2', () => {
    expect(formatSectionRef('Section 4.2, para 2 (p. 3)')).toEqual({ section: '4.2', paragraph: '2' });
  });

  it('keeps a heading-name section and its paragraph', () => {
    expect(formatSectionRef('LIMITED USE OF CONFIDENTIAL INFORMATION, para 1 (p. 1)')).toEqual({
      section: 'LIMITED USE OF CONFIDENTIAL INFORMATION',
      paragraph: '1',
    });
  });

  it('returns no paragraph when the ref has none', () => {
    expect(formatSectionRef('Section 8.1 (p. 5)')).toEqual({ section: '8.1', paragraph: undefined });
  });

  it('falls back to the whole string / "Unreferenced" for unparseable / empty input', () => {
    expect(formatSectionRef('Preamble')).toEqual({ section: 'Preamble', paragraph: undefined });
    expect(formatSectionRef(undefined)).toEqual({ section: 'Unreferenced', paragraph: undefined });
  });
});

// ---------------------------------------------------------------------------
// 1d. formatClauseLocation — one clear location line (UAT round-5 #3/#6)
// ---------------------------------------------------------------------------

describe('formatClauseLocation', () => {
  it('renders "Pg N · Sec N · Para N" from a numbered section ref', () => {
    expect(formatClauseLocation('Section 4.2, para 2 (p. 3)')).toBe('Pg 3 · Sec 4.2 · Para 2');
  });

  it('renders the heading verbatim (no "Sec" prefix) when the ref carries a heading name', () => {
    expect(formatClauseLocation('LIMITED USE OF CONFIDENTIAL INFORMATION, para 1 (p. 1)')).toBe(
      'Pg 1 · LIMITED USE OF CONFIDENTIAL INFORMATION · Para 1'
    );
  });

  it('renders just page + paragraph when there is no section identity', () => {
    expect(formatClauseLocation('Paragraph 5 (p. 1)')).toBe('Pg 1 · Para 5');
  });

  it('omits absent parts and never emits § / ¶ glyphs', () => {
    const label = formatClauseLocation('Section 8.1 (p. 5)');
    expect(label).toBe('Pg 5 · Sec 8.1');
    expect(label).not.toContain('§');
    expect(label).not.toContain('¶');
  });

  it('falls back to the raw ref / "Unreferenced" for unparseable / empty input', () => {
    expect(formatClauseLocation('Preamble')).toBe('Preamble');
    expect(formatClauseLocation(undefined)).toBe('Unreferenced');
  });
});

// ---------------------------------------------------------------------------
// 2. NdaReviewSummaryPanel — Fluent v9 UI
// ---------------------------------------------------------------------------

const SAMPLE_FINDINGS: NdaReviewFindingSummary[] = [
  {
    sectionRef: 'Section 4.2, para 2 (p. 3)',
    quotedText: 'Confidential Information means any information marked confidential.',
    riskLevel: 'High',
    explanation:
      'The definition is gated on marking, narrower than the standard, which also covers oral/unmarked disclosures.',
    standardRef: 'B3 - Definition of Confidential Information',
  },
  {
    sectionRef: 'Section 8.1 (p. 5)',
    quotedText: 'This Agreement shall remain in effect indefinitely.',
    riskLevel: 'Medium',
    explanation:
      'An indefinite term for ordinary non-trade-secret information exceeds the standard 3-5 year survival period.',
    standardRef: 'B8 - Term & confidentiality period',
  },
];

function renderPanel(opts: {
  open?: boolean;
  findings?: NdaReviewFindingSummary[];
  placementFailureCount?: number;
  theme?: typeof webLightTheme;
  onClose?: () => void;
  onNavigate?: (finding: NdaReviewFindingSummary) => void;
}) {
  const onClose = opts.onClose ?? jest.fn();
  const result = render(
    <FluentProvider theme={opts.theme ?? webLightTheme}>
      <NdaReviewSummaryPanel
        open={opts.open ?? true}
        onClose={onClose}
        findings={opts.findings ?? SAMPLE_FINDINGS}
        placementFailureCount={opts.placementFailureCount}
        onNavigate={opts.onNavigate}
      />
    </FluentProvider>
  );
  return { ...result, onClose };
}

describe('NdaReviewSummaryPanel', () => {
  it('renders nothing when closed', () => {
    renderPanel({ open: false });
    expect(screen.queryByTestId('nda-review-summary-panel')).not.toBeInTheDocument();
  });

  it('always renders the fixed not-legal-advice disclaimer banner', () => {
    renderPanel({});
    expect(screen.getByTestId('nda-review-summary-disclaimer')).toHaveTextContent(NDA_REVIEW_DISCLAIMER_TEXT);
  });

  it('shows the empty state when there are no findings (UAT round-5 #2 — no overall-risk banner)', () => {
    renderPanel({ findings: [] });
    expect(screen.getByTestId('nda-review-summary-empty')).toBeInTheDocument();
    // UAT round-5 #2 — the overall-risk banner was removed entirely.
    expect(screen.queryByTestId('nda-review-summary-overall-risk')).not.toBeInTheDocument();
    expect(screen.queryByTestId('nda-review-summary-overall-risk-empty')).not.toBeInTheDocument();
  });

  it('renders a concise TL;DR row per finding with a clear location line + risk (UAT round-5 #2/#3/#4)', () => {
    renderPanel({});

    // UAT round-5 #2 — no overall-risk banner anywhere.
    expect(screen.queryByTestId('nda-review-summary-overall-risk')).not.toBeInTheDocument();

    // UAT round-5 #3 — the location renders as a single clear line ("Pg 3 · Sec 4.2 · Para 2"), no § / ¶
    // glyph soup. #4 — no chevron icon.
    const row0 = screen.getByTestId('nda-review-summary-finding-0');
    expect(row0).toHaveTextContent('Pg 3 · Sec 4.2 · Para 2');
    expect(row0).not.toHaveTextContent('§');
    expect(row0).not.toHaveTextContent('¶');
    expect(row0).toHaveTextContent('High');
    expect(row0).toHaveTextContent('narrower than the standard');

    expect(screen.getByTestId('nda-review-summary-finding-1')).toHaveTextContent('Pg 5 · Sec 8.1');
    expect(screen.getByTestId('nda-review-summary-finding-1')).toHaveTextContent('Medium');
  });

  it('does NOT duplicate the in-document comment: no verbatim quote or firm-standard citation (item #3)', () => {
    // The summary is a scan strip, deliberately NOT a second copy of the gutter comment — the verbatim
    // quotedText and the standardRef citation live only on the in-document Review Note.
    renderPanel({});
    const row = screen.getByTestId('nda-review-summary-finding-0');
    expect(row).not.toHaveTextContent('Confidential Information means');
    expect(row).not.toHaveTextContent('B3 - Definition of Confidential Information');
  });

  it('defaults to document order and re-sorts by risk on demand (UAT round-4 #2/#4)', async () => {
    const user = userEvent.setup();
    // Input order Low, Critical (document order). DEFAULT keeps document order; the sort control
    // switches to risk (most-severe-first). testIds stay keyed to the ORIGINAL index.
    renderPanel({
      findings: [
        { sectionRef: 'Clause A', quotedText: 'a', riskLevel: 'Low', explanation: 'minor' },
        { sectionRef: 'Clause B', quotedText: 'b', riskLevel: 'Critical', explanation: 'severe' },
      ],
    });

    // #4 — default is DOCUMENT order: Low (index 0) before Critical (index 1).
    let rows = screen.getAllByTestId(/nda-review-summary-finding-\d+/);
    expect(rows[0]).toHaveTextContent('Low');
    expect(rows[1]).toHaveTextContent('Critical');

    // #2 — switch to risk sort → Critical first.
    await user.click(screen.getByTestId('nda-review-summary-sort'));
    await user.click(await screen.findByTestId('nda-review-summary-sort-risk'));
    rows = screen.getAllByTestId(/nda-review-summary-finding-\d+/);
    expect(rows[0]).toHaveTextContent('Critical');
    expect(rows[1]).toHaveTextContent('Low');
  });

  it('calls onNavigate with the finding when a TL;DR row is clicked (item #3 link-to-section)', () => {
    const onNavigate = jest.fn();
    renderPanel({ onNavigate });
    screen.getByTestId('nda-review-summary-finding-0').click();
    expect(onNavigate).toHaveBeenCalledTimes(1);
    expect(onNavigate.mock.calls[0][0]).toMatchObject({ sectionRef: 'Section 4.2, para 2 (p. 3)' });
  });

  it('UAT round-4 #6 — marks the navigated-to row ACTIVE (aria-current), and moves it when another row is clicked', () => {
    const onNavigate = jest.fn();
    renderPanel({ onNavigate });
    const row0 = screen.getByTestId('nda-review-summary-finding-0');
    const row1 = screen.getByTestId('nda-review-summary-finding-1');
    // No active row before any navigation.
    expect(row0).not.toHaveAttribute('aria-current');
    expect(row1).not.toHaveAttribute('aria-current');

    fireEvent.click(row0);
    expect(screen.getByTestId('nda-review-summary-finding-0')).toHaveAttribute('aria-current', 'true');
    expect(screen.getByTestId('nda-review-summary-finding-1')).not.toHaveAttribute('aria-current');

    // Clicking another row moves the active state (and still navigates).
    fireEvent.click(screen.getByTestId('nda-review-summary-finding-1'));
    expect(screen.getByTestId('nda-review-summary-finding-1')).toHaveAttribute('aria-current', 'true');
    expect(screen.getByTestId('nda-review-summary-finding-0')).not.toHaveAttribute('aria-current');
    expect(onNavigate).toHaveBeenCalledTimes(2);
  });

  it('UAT round-4 #5 — the down-arrow FAB is absent when the panel content fits (nothing below the fold)', () => {
    // jsdom reports scrollHeight === clientHeight (0), so the panel is never "scrollable" here — the FAB
    // must not render. (Live, when findings overflow the 32vh cap, it appears — exercised in UAT.)
    renderPanel({});
    expect(screen.queryByTestId('nda-review-summary-scroll-down')).not.toBeInTheDocument();
  });

  it('renders rows as non-interactive when onNavigate is not wired', () => {
    renderPanel({});
    // No onNavigate → the row is a static div, not a button.
    expect(screen.getByTestId('nda-review-summary-finding-0').tagName).toBe('DIV');
  });

  it('renders the model takeaway (short headline) instead of the full explanation when supplied (S3/S4)', () => {
    renderPanel({
      findings: [
        {
          sectionRef: 'Clause X',
          quotedText: 'q',
          riskLevel: 'High',
          explanation:
            'Grounded fact — a long detailed grounded description of the clause. Judgment — a long detailed judgment.',
          takeaway: 'Broad confidentiality carve-out beyond firm standard',
        },
      ],
    });
    const row = screen.getByTestId('nda-review-summary-finding-0');
    expect(row).toHaveTextContent('Broad confidentiality carve-out beyond firm standard');
    expect(row).not.toHaveTextContent('a long detailed grounded description');
  });

  it('omits the placement-failure notice when the count is zero/absent (nice-to-have, not required)', () => {
    renderPanel({ placementFailureCount: 0 });
    expect(screen.queryByTestId('nda-review-summary-placement-failures')).not.toBeInTheDocument();
  });

  it('shows the placement-failure notice when reachable and > 0', () => {
    renderPanel({ placementFailureCount: 2 });
    expect(screen.getByTestId('nda-review-summary-placement-failures')).toHaveTextContent(
      '2 findings could not be anchored'
    );
  });

  it('calls onClose when the close button is clicked', () => {
    const onClose = jest.fn();
    renderPanel({ onClose });
    screen.getByTestId('nda-review-summary-close').click();
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('ADR-021: renders with only semantic tokens — no hex literals — in light and dark mode', () => {
    const light = renderPanel({ theme: webLightTheme });
    expect(light.container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);

    const dark = renderPanel({ theme: webDarkTheme });
    expect(screen.getAllByTestId('nda-review-summary-panel').length).toBeGreaterThan(0);
    expect(dark.container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
  });
});
