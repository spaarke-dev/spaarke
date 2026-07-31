/**
 * ComposeBatchNoteToolProgressModal.test.tsx — ai-advanced-capabilities-agreements-r1 task 041
 * (spec FR-11). Presentational-only tests (no editor dependency), mirroring the sibling
 * `NdaReviewProgressModal.test.tsx` convention.
 */
import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import {
  ComposeBatchNoteToolProgressModal,
  type BatchNoteToolOutcomeDisplay,
} from './ComposeBatchNoteToolProgressModal';

describe('ComposeBatchNoteToolProgressModal', () => {
  it('shows a determinate progress bar + "Note X of N" while running', () => {
    render(
      <FluentProvider theme={webLightTheme}>
        <ComposeBatchNoteToolProgressModal
          toolLabel="Draft compliant alternative"
          progress={{ total: 5, completed: 2, currentThreadId: 'thread-3', outcomes: [] }}
          outcomes={null}
          onClose={jest.fn()}
        />
      </FluentProvider>
    );

    expect(screen.getByTestId('compose-batch-note-tool-progress-modal')).toBeInTheDocument();
    expect(screen.getByText('Running "Draft compliant alternative"…')).toBeInTheDocument();
    expect(screen.getByTestId('compose-batch-note-tool-progress-status')).toHaveTextContent('Note 3 of 5…');
  });

  it('auto-dismisses shortly after an all-success completion', async () => {
    jest.useFakeTimers();
    const onClose = jest.fn();
    const outcomes: BatchNoteToolOutcomeDisplay[] = [
      { threadId: 'a', ok: true, label: 'Note a' },
      { threadId: 'b', ok: true, label: 'Note b' },
    ];
    render(
      <FluentProvider theme={webLightTheme}>
        <ComposeBatchNoteToolProgressModal
          toolLabel="Draft compliant alternative"
          progress={null}
          outcomes={outcomes}
          onClose={onClose}
        />
      </FluentProvider>
    );

    expect(screen.getByText('2 succeeded')).toBeInTheDocument();
    expect(screen.queryByTestId('compose-batch-note-tool-progress-close')).not.toBeInTheDocument();

    jest.advanceTimersByTime(1500);
    expect(onClose).toHaveBeenCalled();
    jest.useRealTimers();
  });

  it('stays open with a Close button and a per-note failure list when any note failed', async () => {
    jest.useFakeTimers();
    const onClose = jest.fn();
    const outcomes: BatchNoteToolOutcomeDisplay[] = [
      { threadId: 'a', ok: true, label: 'Section 3.1' },
      { threadId: 'b', ok: false, error: 'timed out', label: 'Section 4.2' },
      { threadId: 'c', ok: false, error: 'server error', label: 'Section 6.1' },
    ];
    render(
      <FluentProvider theme={webLightTheme}>
        <ComposeBatchNoteToolProgressModal
          toolLabel="Draft compliant alternative"
          progress={null}
          outcomes={outcomes}
          onClose={onClose}
        />
      </FluentProvider>
    );

    expect(screen.getByText('1 succeeded')).toBeInTheDocument();
    expect(screen.getByText('2 failed')).toBeInTheDocument();
    expect(screen.getByText(/Section 4\.2 — timed out/)).toBeInTheDocument();
    expect(screen.getByText(/Section 6\.1 — server error/)).toBeInTheDocument();

    // Never auto-dismisses when there is a failure.
    jest.advanceTimersByTime(5000);
    expect(onClose).not.toHaveBeenCalled();

    screen.getByTestId('compose-batch-note-tool-progress-close').click();
    expect(onClose).toHaveBeenCalledTimes(1);
    jest.useRealTimers();
  });

  it('ADR-021: renders with only semantic tokens — no hex literals — in light and dark mode', () => {
    const outcomes: BatchNoteToolOutcomeDisplay[] = [{ threadId: 'a', ok: false, error: 'x', label: 'Note a' }];

    const light = render(
      <FluentProvider theme={webLightTheme}>
        <ComposeBatchNoteToolProgressModal toolLabel="Tool" progress={null} outcomes={outcomes} onClose={jest.fn()} />
      </FluentProvider>
    );
    expect(light.container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
    light.unmount();

    const dark = render(
      <FluentProvider theme={webDarkTheme}>
        <ComposeBatchNoteToolProgressModal toolLabel="Tool" progress={null} outcomes={outcomes} onClose={jest.fn()} />
      </FluentProvider>
    );
    expect(dark.container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
    dark.unmount();
  });
});
