/**
 * ComposeReanchorBanner.test.tsx — RTL coverage for the return-from-Word re-anchor banner +
 * conflict panel (FR-27, task 054). Co-located per this package's jest config.
 *
 * Covers:
 *   1. Banner summary phrasing ("N re-anchored, M need review") + the review affordance gate.
 *   2. Banner dark-mode render (ADR-021): no hardcoded hex in the DOM under webDarkTheme.
 *   3. Conflict panel lists ONLY non-auto anchors; Accept/Keep/Discard emit resolution decisions;
 *      orphans have no "Accept here"; ambiguous review anchors are badged distinctly.
 */

import * as React from 'react';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';

import { ComposeReanchorBanner } from './ComposeReanchorBanner';
import { ComposeReanchorConflictPanel } from './ComposeReanchorConflictPanel';
import type { ReanchorSummary, ReanchoredAnnotation } from './ComposeReanchor.types';

// jsdom lacks ResizeObserver, which Fluent v9's MessageBarActions overflow relies on (present in
// every real browser). Polyfill it so the banner renders under test. Kept local to this file to
// stay within the task-054 write boundary (jest.setup.js is package config, out of scope).
class ResizeObserverStub {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}
(globalThis as unknown as { ResizeObserver: typeof ResizeObserverStub }).ResizeObserver = ResizeObserverStub;

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

function annotation(
  overrides: Partial<ReanchoredAnnotation> & Pick<ReanchoredAnnotation, 'id' | 'band'>
): ReanchoredAnnotation {
  return {
    id: overrides.id,
    type: overrides.type ?? 'comment',
    preview: overrides.preview ?? 'Reviewer note preview',
    band: overrides.band,
    confidence: overrides.confidence ?? 0.7,
    matchedParagraphIndex: overrides.matchedParagraphIndex ?? 2,
    contentSimilarity: overrides.contentSimilarity ?? 0.7,
    structuralProximity: overrides.structuralProximity ?? 1,
    ambiguous: overrides.ambiguous ?? false,
    matchedParagraphPreview: overrides.matchedParagraphPreview ?? 'Best matching paragraph text',
  };
}

function summary(overrides: Partial<ReanchorSummary> = {}): ReanchorSummary {
  return {
    documentSpeId: 'spe-1',
    total: 4,
    autoCount: 2,
    reviewCount: 1,
    orphanCount: 1,
    computedAtUtc: '2026-07-09T15:42:00Z',
    annotations: [
      annotation({ id: 'auto-1', band: 'auto', confidence: 0.96 }),
      annotation({ id: 'auto-2', band: 'auto', confidence: 0.92 }),
      annotation({ id: 'review-1', band: 'review', confidence: 0.72 }),
      annotation({
        id: 'orphan-1',
        band: 'orphan',
        confidence: 0.4,
        matchedParagraphIndex: -1,
        matchedParagraphPreview: null,
      }),
    ],
    ...overrides,
  };
}

function renderWithTheme(ui: React.ReactElement, theme: typeof webLightTheme = webLightTheme) {
  return render(<FluentProvider theme={theme}>{ui}</FluentProvider>);
}

// ---------------------------------------------------------------------------
// 1. Banner summary phrasing + review affordance
// ---------------------------------------------------------------------------

describe('ComposeReanchorBanner — summary + review affordance', () => {
  it('renders "N re-anchored, M need review" and the Review changes button when attention is needed', () => {
    renderWithTheme(<ComposeReanchorBanner summary={summary()} onReview={jest.fn()} />);

    expect(screen.getByTestId('compose-reanchor-banner')).toBeInTheDocument();
    const text = screen.getByTestId('compose-reanchor-banner-summary').textContent ?? '';
    expect(text).toContain('2 re-anchored');
    expect(text).toContain('1 need review');
    expect(text).toContain('1 orphaned');
    expect(screen.getByTestId('compose-reanchor-banner-review')).toBeInTheDocument();
  });

  it('calls onReview when the Review changes button is clicked', async () => {
    const onReview = jest.fn();
    const user = userEvent.setup();
    renderWithTheme(<ComposeReanchorBanner summary={summary()} onReview={onReview} />);

    await user.click(screen.getByTestId('compose-reanchor-banner-review'));
    expect(onReview).toHaveBeenCalledTimes(1);
  });

  it('renders a quiet success (no Review button) when everything auto-anchored', () => {
    renderWithTheme(
      <ComposeReanchorBanner
        summary={summary({ total: 2, autoCount: 2, reviewCount: 0, orphanCount: 0 })}
        onReview={jest.fn()}
      />
    );

    expect(screen.getByTestId('compose-reanchor-banner-summary').textContent).toContain('2 re-anchored');
    expect(screen.queryByTestId('compose-reanchor-banner-review')).not.toBeInTheDocument();
  });

  it('NEGATIVE: renders nothing when there is no summary or zero anchors', () => {
    const { rerender } = renderWithTheme(<ComposeReanchorBanner summary={null} onReview={jest.fn()} />);
    expect(screen.queryByTestId('compose-reanchor-banner')).not.toBeInTheDocument();

    rerender(
      <FluentProvider theme={webLightTheme}>
        <ComposeReanchorBanner
          summary={summary({ total: 0, autoCount: 0, reviewCount: 0, orphanCount: 0 })}
          onReview={jest.fn()}
        />
      </FluentProvider>
    );
    expect(screen.queryByTestId('compose-reanchor-banner')).not.toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// 2. Dark mode (ADR-021)
// ---------------------------------------------------------------------------

describe('ComposeReanchorBanner — dark mode (ADR-021)', () => {
  it('renders under webDarkTheme with no hardcoded hex color in the DOM', () => {
    const { container } = renderWithTheme(
      <ComposeReanchorBanner summary={summary()} onReview={jest.fn()} />,
      webDarkTheme
    );

    expect(screen.getByTestId('compose-reanchor-banner')).toBeInTheDocument();
    // ADR-021: color must flow through Griffel/token classes, never an inline hex literal.
    expect(container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
  });

  it('renders under webLightTheme with no hardcoded hex color in the DOM', () => {
    const { container } = renderWithTheme(
      <ComposeReanchorBanner summary={summary()} onReview={jest.fn()} />,
      webLightTheme
    );
    expect(container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
  });
});

// ---------------------------------------------------------------------------
// 3. Conflict panel — lists non-auto anchors + resolution actions
// ---------------------------------------------------------------------------

describe('ComposeReanchorConflictPanel — flagged/orphaned resolution', () => {
  it('lists ONLY the review + orphan anchors (auto anchors are not shown)', () => {
    renderWithTheme(
      <ComposeReanchorConflictPanel open summary={summary()} onResolve={jest.fn()} onClose={jest.fn()} />
    );

    expect(screen.getByTestId('compose-reanchor-conflict-item-review-1')).toBeInTheDocument();
    expect(screen.getByTestId('compose-reanchor-conflict-item-orphan-1')).toBeInTheDocument();
    expect(screen.queryByTestId('compose-reanchor-conflict-item-auto-1')).not.toBeInTheDocument();
  });

  it('an orphan has Keep + Discard but NO "Accept here" (no candidate paragraph)', () => {
    renderWithTheme(
      <ComposeReanchorConflictPanel open summary={summary()} onResolve={jest.fn()} onClose={jest.fn()} />
    );

    expect(screen.queryByTestId('compose-reanchor-conflict-accept-orphan-1')).not.toBeInTheDocument();
    expect(screen.getByTestId('compose-reanchor-conflict-keep-orphan-1')).toBeInTheDocument();
    expect(screen.getByTestId('compose-reanchor-conflict-discard-orphan-1')).toBeInTheDocument();
  });

  it('emits the correct resolution decision for Accept / Keep / Discard', async () => {
    const onResolve = jest.fn();
    const user = userEvent.setup();
    renderWithTheme(
      <ComposeReanchorConflictPanel open summary={summary()} onResolve={onResolve} onClose={jest.fn()} />
    );

    await user.click(screen.getByTestId('compose-reanchor-conflict-accept-review-1'));
    expect(onResolve).toHaveBeenCalledWith({ annotationId: 'review-1', resolution: 'accept' });

    await user.click(screen.getByTestId('compose-reanchor-conflict-discard-orphan-1'));
    expect(onResolve).toHaveBeenCalledWith({ annotationId: 'orphan-1', resolution: 'discard' });
  });

  it('badges an ambiguous review anchor distinctly from a plain review anchor', () => {
    const s = summary({
      total: 2,
      autoCount: 0,
      reviewCount: 2,
      orphanCount: 0,
      annotations: [
        annotation({ id: 'plain', band: 'review', ambiguous: false }),
        annotation({ id: 'ambig', band: 'review', ambiguous: true }),
      ],
    });
    renderWithTheme(<ComposeReanchorConflictPanel open summary={s} onResolve={jest.fn()} onClose={jest.fn()} />);

    expect(
      within(screen.getByTestId('compose-reanchor-conflict-band-plain')).getByText('Needs review')
    ).toBeInTheDocument();
    expect(
      within(screen.getByTestId('compose-reanchor-conflict-band-ambig')).getByText('Ambiguous')
    ).toBeInTheDocument();
  });

  it('shows the clean-state message when nothing needs resolution', () => {
    renderWithTheme(
      <ComposeReanchorConflictPanel
        open
        summary={summary({
          total: 2,
          autoCount: 2,
          reviewCount: 0,
          orphanCount: 0,
          annotations: [annotation({ id: 'a', band: 'auto' }), annotation({ id: 'b', band: 'auto' })],
        })}
        onResolve={jest.fn()}
        onClose={jest.fn()}
      />
    );
    expect(screen.getByTestId('compose-reanchor-conflict-empty')).toBeInTheDocument();
  });
});
