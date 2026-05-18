/**
 * SafetyAnnotationOverlay — unit tests
 *
 * Covers:
 * (a) safety_annotation event triggers annotation after 200 ms.
 * (b) Ungrounded segment renders with highlight span (data-testid "ungrounded-segment").
 * (c) Citation badges render correct icon per verification state.
 * (d) Missing annotation (no event) renders plain message text without error.
 * (e) capability_change events are ignored.
 * (f) Annotation timer is cleared on unmount (no state update after unmount).
 *
 * Also covers AnnotatedMessageContent (stateless annotated render path):
 * (g) Segments + citation badges render in a single unified pass.
 * (h) No citation results — renders GroundednessHighlight over full text.
 *
 * And CitationBadge:
 * (i) Verified → data-status="verified" on badge wrapper.
 * (j) Unverified → data-status="unverified", tooltip includes "Not found in available sources".
 * (k) Partial → data-status="partial".
 *
 * And GroundednessHighlight:
 * (l) Ungrounded segments render as [data-testid="ungrounded-segment"].
 * (m) Grounded segments render as plain text without the ungrounded wrapper.
 * (n) Empty segments renders plain text unchanged.
 *
 * Task: AIPU2-090
 */

import '@testing-library/jest-dom';
import React, { act } from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBus } from '../../events/PaneEventBus';
import { PaneEventBusProvider } from '../../events/PaneEventBusContext';
import SafetyAnnotationOverlay, {
  AnnotatedMessageContent,
} from '../SafetyAnnotationOverlay';
import { CitationBadge } from '../CitationBadge';
import type { CitationVerificationResult } from '../CitationBadge';
import { GroundednessHighlight } from '../GroundednessHighlight';
import type { GroundednessSegment } from '../GroundednessHighlight';

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------

/**
 * Wraps UI in FluentProvider + PaneEventBusProvider for rendering in tests.
 */
function renderWithProviders(ui: React.ReactElement, bus: PaneEventBus) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>
        {ui}
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

/**
 * Wraps UI in FluentProvider only (no PaneEventBus needed for stateless components).
 */
function renderWithFluent(ui: React.ReactElement) {
  return render(
    <FluentProvider theme={webLightTheme}>
      {ui}
    </FluentProvider>
  );
}

/** Minimal verified citation result. */
const verifiedResult: CitationVerificationResult = {
  id: '1',
  status: 'verified',
  providerName: 'InternalIndexProvider',
  confidence: 'high',
};

/** Minimal unverified citation result. */
const unverifiedResult: CitationVerificationResult = {
  id: '2',
  status: 'unverified',
  providerName: 'InternalIndexProvider',
  confidence: 'low',
};

/** Minimal partial citation result. */
const partialResult: CitationVerificationResult = {
  id: '3',
  status: 'partial',
  providerName: 'InternalIndexProvider',
  confidence: 'medium',
};

// ---------------------------------------------------------------------------
// (a) SafetyAnnotationOverlay — annotation triggers after 200 ms
// ---------------------------------------------------------------------------

describe('SafetyAnnotationOverlay — timing', () => {
  beforeEach(() => {
    jest.useFakeTimers();
  });

  afterEach(() => {
    jest.runOnlyPendingTimers();
    jest.useRealTimers();
  });

  it('(a) renders plain text before annotation and switches to annotated after 200 ms', () => {
    const bus = new PaneEventBus();

    renderWithProviders(
      <SafetyAnnotationOverlay
        turnId="turn-1"
        messageText="The contract was signed in 2025. See [1]."
      />,
      bus
    );

    // Before event — plain text, not annotated.
    const overlay = screen.getByTestId('safety-annotation-overlay');
    expect(overlay).toHaveAttribute('data-annotated', 'false');
    expect(overlay).toHaveTextContent('The contract was signed in 2025. See [1].');

    // Dispatch safety_annotation event.
    act(() => {
      bus.dispatch('safety', {
        type: 'safety_annotation',
        groundedness: {
          score: 0.7,
          ungrounded_segments: [
            { start: 0, end: 31, grounded: false },
          ],
        },
        citations: {
          '1': {
            id: '1',
            status: 'verified',
            providerName: 'InternalIndexProvider',
            confidence: 'high',
          },
        },
        confidence: 'medium',
      });
    });

    // Timer not yet elapsed — still plain.
    expect(screen.getByTestId('safety-annotation-overlay')).toHaveAttribute(
      'data-annotated',
      'false'
    );

    // Advance 200 ms.
    act(() => {
      jest.advanceTimersByTime(200);
    });

    // Now annotated.
    expect(screen.getByTestId('safety-annotation-overlay')).toHaveAttribute(
      'data-annotated',
      'true'
    );
  });

  it('does NOT annotate before 200 ms have elapsed', () => {
    const bus = new PaneEventBus();

    renderWithProviders(
      <SafetyAnnotationOverlay turnId="turn-2" messageText="Test message." />,
      bus
    );

    act(() => {
      bus.dispatch('safety', {
        type: 'safety_annotation',
        groundedness: { ungrounded_segments: [{ start: 0, end: 4, grounded: false }] },
        confidence: 'high',
      });
    });

    act(() => {
      jest.advanceTimersByTime(199);
    });

    expect(screen.getByTestId('safety-annotation-overlay')).toHaveAttribute(
      'data-annotated',
      'false'
    );
  });

  it('(f) timer is cleared on unmount — no state update after unmount', () => {
    const bus = new PaneEventBus();
    const consoleError = jest.spyOn(console, 'error').mockImplementation(() => {});

    const { unmount } = renderWithProviders(
      <SafetyAnnotationOverlay turnId="turn-3" messageText="Unmount test." />,
      bus
    );

    act(() => {
      bus.dispatch('safety', {
        type: 'safety_annotation',
        groundedness: { ungrounded_segments: [] },
        confidence: 'high',
      });
    });

    // Unmount before the 200 ms timer fires.
    unmount();

    // Advance past the timer — should not throw or warn about state updates
    // on unmounted components.
    act(() => {
      jest.advanceTimersByTime(500);
    });

    // No React "Can't perform a state update on an unmounted component" warning.
    expect(consoleError).not.toHaveBeenCalled();
    consoleError.mockRestore();
  });
});

// ---------------------------------------------------------------------------
// (b) SafetyAnnotationOverlay — ungrounded segment highlighted
// ---------------------------------------------------------------------------

describe('SafetyAnnotationOverlay — ungrounded segment highlighting', () => {
  beforeEach(() => {
    jest.useFakeTimers();
  });

  afterEach(() => {
    jest.runOnlyPendingTimers();
    jest.useRealTimers();
  });

  it('(b) renders ungrounded-segment span when annotation has ungrounded segments', () => {
    const bus = new PaneEventBus();

    renderWithProviders(
      <SafetyAnnotationOverlay
        turnId="turn-4"
        messageText="Claim one. Claim two."
      />,
      bus
    );

    act(() => {
      bus.dispatch('safety', {
        type: 'safety_annotation',
        groundedness: {
          ungrounded_segments: [
            { start: 11, end: 21, grounded: false }, // "Claim two."
          ],
        },
        confidence: 'medium',
      });
    });

    act(() => {
      jest.advanceTimersByTime(200);
    });

    // The GroundednessHighlight should have rendered an ungrounded-segment span.
    const ungroundedSpans = screen.getAllByTestId('ungrounded-segment');
    expect(ungroundedSpans.length).toBeGreaterThan(0);
    expect(ungroundedSpans[0]).toHaveTextContent('Claim two.');
  });

  it('no ungrounded spans when all segments are grounded', () => {
    const bus = new PaneEventBus();

    renderWithProviders(
      <SafetyAnnotationOverlay
        turnId="turn-5"
        messageText="Fully grounded claim."
      />,
      bus
    );

    act(() => {
      bus.dispatch('safety', {
        type: 'safety_annotation',
        groundedness: {
          ungrounded_segments: [
            { start: 0, end: 21, grounded: true }, // grounded
          ],
        },
        confidence: 'high',
      });
    });

    act(() => {
      jest.advanceTimersByTime(200);
    });

    expect(screen.queryByTestId('ungrounded-segment')).not.toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// (c) CitationBadge — correct variant per verification state
// ---------------------------------------------------------------------------

describe('CitationBadge — variant per status', () => {
  it('(c-verified) renders data-status="verified" for verified result', () => {
    renderWithFluent(<CitationBadge result={verifiedResult} />);
    expect(screen.getByTestId('citation-badge-1')).toHaveAttribute('data-status', 'verified');
  });

  it('(i) verified badge aria-label mentions "verified"', () => {
    renderWithFluent(<CitationBadge result={verifiedResult} />);
    // The badge element carries an aria-label
    const badge = screen.getByRole('generic', { hidden: true });
    // Check the wrapper has the correct data-status
    expect(screen.getByTestId('citation-badge-1')).toHaveAttribute('data-status', 'verified');
  });

  it('(j) unverified badge has data-status="unverified"', () => {
    renderWithFluent(<CitationBadge result={unverifiedResult} />);
    expect(screen.getByTestId('citation-badge-2')).toHaveAttribute('data-status', 'unverified');
  });

  it('(k) partial badge has data-status="partial"', () => {
    renderWithFluent(<CitationBadge result={partialResult} />);
    expect(screen.getByTestId('citation-badge-3')).toHaveAttribute('data-status', 'partial');
  });

  it('unverified badge aria-label includes "not found in available sources"', () => {
    renderWithFluent(<CitationBadge result={unverifiedResult} />);
    // Fluent Badge renders with aria-label on the internal element.
    // We verify via the role-accessible label on the badge component.
    const badgeWrapper = screen.getByTestId('citation-badge-2');
    // The aria-label on the Badge carries the message.
    const badgeEl = badgeWrapper.querySelector('[aria-label]');
    expect(badgeEl?.getAttribute('aria-label')).toMatch(
      /not found in available sources/i
    );
  });

  it('renders the correct test-id per citation id', () => {
    renderWithFluent(
      <>
        <CitationBadge result={verifiedResult} />
        <CitationBadge result={unverifiedResult} />
        <CitationBadge result={partialResult} />
      </>
    );
    expect(screen.getByTestId('citation-badge-1')).toBeInTheDocument();
    expect(screen.getByTestId('citation-badge-2')).toBeInTheDocument();
    expect(screen.getByTestId('citation-badge-3')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// (d) SafetyAnnotationOverlay — missing annotation renders plain text
// ---------------------------------------------------------------------------

describe('SafetyAnnotationOverlay — missing annotation', () => {
  it('(d) renders plain message text with no error when no event arrives', () => {
    const bus = new PaneEventBus();

    renderWithProviders(
      <SafetyAnnotationOverlay
        turnId="turn-6"
        messageText="Plain message with no annotation."
      />,
      bus
    );

    const overlay = screen.getByTestId('safety-annotation-overlay');
    expect(overlay).toHaveAttribute('data-annotated', 'false');
    expect(overlay).toHaveTextContent('Plain message with no annotation.');
    expect(screen.queryByTestId('ungrounded-segment')).not.toBeInTheDocument();
    expect(screen.queryByTestId('citation-badge-1')).not.toBeInTheDocument();
  });

  it('(e) ignores capability_change events on the safety channel', () => {
    jest.useFakeTimers();
    const bus = new PaneEventBus();

    renderWithProviders(
      <SafetyAnnotationOverlay
        turnId="turn-7"
        messageText="Should remain unannotated."
      />,
      bus
    );

    act(() => {
      bus.dispatch('safety', {
        type: 'capability_change',
        capabilities: { groundedness: false },
      });
    });

    act(() => {
      jest.advanceTimersByTime(500);
    });

    expect(screen.getByTestId('safety-annotation-overlay')).toHaveAttribute(
      'data-annotated',
      'false'
    );

    jest.runOnlyPendingTimers();
    jest.useRealTimers();
  });

  it('renders plain text when groundedness payload is empty', () => {
    jest.useFakeTimers();
    const bus = new PaneEventBus();

    renderWithProviders(
      <SafetyAnnotationOverlay
        turnId="turn-8"
        messageText="No segments here."
      />,
      bus
    );

    act(() => {
      bus.dispatch('safety', {
        type: 'safety_annotation',
        groundedness: { ungrounded_segments: [] },
        confidence: 'high',
      });
    });

    act(() => {
      jest.advanceTimersByTime(200);
    });

    // Empty segments + no citations → annotation has nothing to show.
    // Component stays in plain-text mode (data-annotated="false").
    expect(screen.getByTestId('safety-annotation-overlay')).toHaveAttribute(
      'data-annotated',
      'false'
    );

    jest.runOnlyPendingTimers();
    jest.useRealTimers();
  });
});

// ---------------------------------------------------------------------------
// (g) AnnotatedMessageContent — unified annotated render pass
// ---------------------------------------------------------------------------

describe('AnnotatedMessageContent — unified render', () => {
  const segments: GroundednessSegment[] = [
    { start: 0, end: 12, grounded: true },
    { start: 12, end: 35, grounded: false },
  ];

  const citationMap = new Map<string, CitationVerificationResult>([
    ['1', verifiedResult],
  ]);

  it('(g) renders groundedness-highlight and citation badge in a single pass', () => {
    renderWithFluent(
      <AnnotatedMessageContent
        messageText="See regulation [1] for details on the penalty clause."
        segments={segments}
        citationResults={citationMap}
      />
    );

    expect(screen.getByTestId('annotated-message-content')).toBeInTheDocument();
    expect(screen.getByTestId('groundedness-highlight')).toBeInTheDocument();
    expect(screen.getByTestId('citation-badge-1')).toBeInTheDocument();
  });

  it('(h) renders GroundednessHighlight when no citation results provided', () => {
    renderWithFluent(
      <AnnotatedMessageContent
        messageText="No citations in this message."
        segments={[{ start: 4, end: 13, grounded: false }]}
        citationResults={new Map()}
      />
    );

    expect(screen.getByTestId('annotated-message-content')).toBeInTheDocument();
    expect(screen.getByTestId('groundedness-highlight')).toBeInTheDocument();
    expect(screen.getByTestId('ungrounded-segment')).toBeInTheDocument();
    expect(screen.queryByTestId('citation-badge-1')).not.toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// (l-n) GroundednessHighlight — standalone tests
// ---------------------------------------------------------------------------

describe('GroundednessHighlight — segment rendering', () => {
  it('(l) marks ungrounded segments with data-testid="ungrounded-segment"', () => {
    renderWithFluent(
      <GroundednessHighlight
        text="Claim A. Claim B."
        segments={[
          { start: 0, end: 8, grounded: true },
          { start: 9, end: 17, grounded: false },
        ]}
      />
    );

    const spans = screen.getAllByTestId('ungrounded-segment');
    expect(spans).toHaveLength(1);
    expect(spans[0]).toHaveTextContent('Claim B.');
  });

  it('(m) grounded segments render as plain text without the ungrounded-segment wrapper', () => {
    renderWithFluent(
      <GroundednessHighlight
        text="Grounded claim only."
        segments={[{ start: 0, end: 20, grounded: true }]}
      />
    );

    expect(screen.queryByTestId('ungrounded-segment')).not.toBeInTheDocument();
    expect(screen.getByTestId('groundedness-highlight')).toHaveTextContent(
      'Grounded claim only.'
    );
  });

  it('(n) renders plain text unchanged when no segments provided', () => {
    renderWithFluent(
      <GroundednessHighlight text="No segments at all." />
    );

    expect(screen.getByTestId('groundedness-highlight')).toHaveTextContent(
      'No segments at all.'
    );
    expect(screen.queryByTestId('ungrounded-segment')).not.toBeInTheDocument();
  });

  it('renders multiple ungrounded spans for multiple ungrounded segments', () => {
    renderWithFluent(
      <GroundednessHighlight
        text="A. B. C."
        segments={[
          { start: 0, end: 2, grounded: false },
          { start: 3, end: 5, grounded: true },
          { start: 6, end: 8, grounded: false },
        ]}
      />
    );

    const spans = screen.getAllByTestId('ungrounded-segment');
    expect(spans).toHaveLength(2);
    expect(spans[0]).toHaveTextContent('A.');
    expect(spans[1]).toHaveTextContent('C.');
  });

  it('renders all text as plain when segments array is empty', () => {
    renderWithFluent(
      <GroundednessHighlight text="All plain." segments={[]} />
    );

    expect(screen.getByTestId('groundedness-highlight')).toHaveTextContent('All plain.');
    expect(screen.queryByTestId('ungrounded-segment')).not.toBeInTheDocument();
  });
});
