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
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import {
  NdaReviewSummaryPanel,
  deriveOverallRisk,
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
// 2. NdaReviewSummaryPanel — Fluent v9 UI
// ---------------------------------------------------------------------------

const SAMPLE_FINDINGS: NdaReviewFindingSummary[] = [
  {
    sectionRef: 'Section 4.2, para 2 (p. 3)',
    quotedText: 'Confidential Information means any information marked confidential.',
    riskLevel: 'High',
    explanation: 'The definition is gated on marking, narrower than the standard, which also covers oral/unmarked disclosures.',
    standardRef: 'B3 - Definition of Confidential Information',
  },
  {
    sectionRef: 'Section 8.1 (p. 5)',
    quotedText: 'This Agreement shall remain in effect indefinitely.',
    riskLevel: 'Medium',
    explanation: 'An indefinite term for ordinary non-trade-secret information exceeds the standard 3-5 year survival period.',
    standardRef: 'B8 - Term & confidentiality period',
  },
];

function renderPanel(opts: {
  open?: boolean;
  findings?: NdaReviewFindingSummary[];
  placementFailureCount?: number;
  theme?: typeof webLightTheme;
  onClose?: () => void;
}) {
  const onClose = opts.onClose ?? jest.fn();
  const result = render(
    <FluentProvider theme={opts.theme ?? webLightTheme}>
      <NdaReviewSummaryPanel
        open={opts.open ?? true}
        onClose={onClose}
        findings={opts.findings ?? SAMPLE_FINDINGS}
        placementFailureCount={opts.placementFailureCount}
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

  it('shows the empty state when there are no findings, with no overall-risk badge', () => {
    renderPanel({ findings: [] });
    expect(screen.getByTestId('nda-review-summary-empty')).toBeInTheDocument();
    expect(screen.getByTestId('nda-review-summary-overall-risk-empty')).toBeInTheDocument();
    expect(screen.queryByTestId('nda-review-summary-overall-risk')).not.toBeInTheDocument();
  });

  it('renders overallRisk (derived) + each flagged section with its citation', () => {
    renderPanel({});

    // Overall risk = max(High, Medium) = High
    expect(screen.getByTestId('nda-review-summary-overall-risk')).toHaveTextContent('High');

    expect(screen.getByTestId('nda-review-summary-finding-0')).toHaveTextContent('Section 4.2, para 2 (p. 3)');
    expect(screen.getByTestId('nda-review-summary-finding-0')).toHaveTextContent('Confidential Information means');
    expect(screen.getByTestId('nda-review-summary-finding-0')).toHaveTextContent('High');
    expect(screen.getByTestId('nda-review-summary-finding-0')).toHaveTextContent(
      'B3 - Definition of Confidential Information'
    );

    expect(screen.getByTestId('nda-review-summary-finding-1')).toHaveTextContent('Section 8.1 (p. 5)');
    expect(screen.getByTestId('nda-review-summary-finding-1')).toHaveTextContent('Medium');
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
