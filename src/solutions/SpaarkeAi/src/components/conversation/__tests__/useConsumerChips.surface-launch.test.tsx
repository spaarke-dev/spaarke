/**
 * useConsumerChips — surface_launch branch (spaarkeai-assistant-enhancements-r1
 * task 013 part 1). When a dispatched capability's terminal result carries
 * `disposition === 'surface_launch'` + a `consumerType`, the controller launches the
 * pre-seeded create surface via the ONE shared `launchSurface` (task 012) — a static
 * registry lookup, ZERO intent detection (ADR-039). For any other disposition the
 * launch MUST NOT fire.
 *
 * The dispatcher + launchSurface are mocked at the `@spaarke/ui-components` boundary
 * (same strategy as ConversationPane.consumer-chips.test) so the test drives only the
 * hook's branch logic, not the real SSE/network path.
 */

import '@testing-library/jest-dom';
import { renderHook, act, render, screen, fireEvent } from '@testing-library/react';
import * as React from 'react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import type { DispatchConsumerResult, IChatMessage } from '@spaarke/ui-components';

// ---------------------------------------------------------------------------
// Mock the dispatcher factory + launchSurface at the package boundary.
// ---------------------------------------------------------------------------

const dispatchConsumerMock = jest.fn<Promise<DispatchConsumerResult>, [string, unknown]>();
const launchSurfaceMock = jest.fn<Promise<{ handoffId: string; launched: boolean }>, [unknown]>(() =>
  Promise.resolve({ handoffId: 'h-1', launched: true })
);

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  return {
    ...actual,
    createConsumerDispatcher: () => dispatchConsumerMock,
    launchSurface: (input: unknown) => launchSurfaceMock(input),
  };
});

// eslint-disable-next-line import/first
import { useConsumerChips, type ConsumerChipsDeps } from '../useConsumerChips';

const BFF = 'https://bff.test';

function makeDeps(overrides?: Partial<ConsumerChipsDeps>): ConsumerChipsDeps {
  return {
    bffBaseUrl: BFF,
    getAccessToken: async () => 'tok',
    getSessionId: () => 'session-1',
    dispatch: jest.fn(),
    sessionAttachmentCount: 0,
    enqueueAssistantMessage: jest.fn<void, [IChatMessage]>(),
    inject: jest.fn<void, [IChatMessage]>(),
    ...overrides,
  };
}

/** Let the dispatchConsumer().then(...) microtask chain settle. */
async function flush(): Promise<void> {
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
  });
}

beforeEach(() => {
  dispatchConsumerMock.mockReset();
  launchSurfaceMock.mockClear();
});

describe('useConsumerChips surface_launch branch', () => {
  it('launches the pre-seeded surface for a surface_launch result', async () => {
    dispatchConsumerMock.mockResolvedValue({
      streamId: 's',
      status: 'complete',
      disposition: 'surface_launch',
      consumerType: 'create-matter',
      result: { matter_name: 'Acme v. Beta', matter_description: 'Dispute.', resolvedLookups: {} },
    });

    const { result } = renderHook(() => useConsumerChips(makeDeps()));

    await act(async () => {
      result.current.dispatchBinding('binding-1');
    });
    await flush();

    expect(launchSurfaceMock).toHaveBeenCalledTimes(1);
    const arg = launchSurfaceMock.mock.calls[0][0] as Record<string, unknown>;
    expect(arg).toMatchObject({ consumerType: 'create-matter', bffBaseUrl: BFF });
    // resolvedLookups is lifted OUT of draftValues (never a free-text form field).
    expect(arg.draftValues).toEqual({ matter_name: 'Acme v. Beta', matter_description: 'Dispute.' });
    expect(arg.resolvedLookups).toEqual({});
  });

  it('does NOT launch a surface for a non-surface_launch disposition', async () => {
    dispatchConsumerMock.mockResolvedValue({
      streamId: 's',
      status: 'complete',
      disposition: 'informational',
      result: { tldr: 'summary' },
    });

    const { result } = renderHook(() => useConsumerChips(makeDeps()));

    await act(async () => {
      result.current.dispatchBinding('binding-1');
    });
    await flush();

    expect(launchSurfaceMock).not.toHaveBeenCalled();
  });

  it('does NOT launch a surface when disposition is surface_launch but consumerType is missing', async () => {
    dispatchConsumerMock.mockResolvedValue({
      streamId: 's',
      status: 'complete',
      disposition: 'surface_launch',
      result: { matter_name: 'X' },
    });

    const { result } = renderHook(() => useConsumerChips(makeDeps()));

    await act(async () => {
      result.current.dispatchBinding('binding-1');
    });
    await flush();

    expect(launchSurfaceMock).not.toHaveBeenCalled();
  });
});

describe('useConsumerChips local-action chips (UAT R4-6 / R4-11)', () => {
  it('appends "Ask about these files" after a chat-summarize dispatch, alongside server chips', async () => {
    dispatchConsumerMock.mockResolvedValue({
      streamId: 's',
      status: 'complete',
      disposition: 'informational',
      consumerType: 'chat-summarize',
      result: { tldr: 'A summary.' },
      chips: [{ label: 'Create a matter', bindingId: 'b-create-matter' }],
    } as unknown as DispatchConsumerResult);

    const { result } = renderHook(() => useConsumerChips(makeDeps()));
    await act(async () => {
      result.current.dispatchBinding('b-summarize');
    });
    await flush();

    render(<FluentProvider theme={webLightTheme}>{result.current.consumerChipsSlot}</FluentProvider>);
    // Server chip stays; the local prompt chip is appended (replaces old "Summarize again").
    expect(screen.getByTestId('consumer-chip-b-create-matter')).toBeInTheDocument();
    expect(screen.getByTestId('consumer-chip-local:ask-about-files')).toBeInTheDocument();
  });

  it('prepends Send-as-email + Save-to-document after a draft-correspondence dispatch', async () => {
    dispatchConsumerMock.mockResolvedValue({
      streamId: 's',
      status: 'complete',
      disposition: 'informational',
      consumerType: 'draft-correspondence',
      // Shape that isCorrespondenceDraft() accepts (body + subject).
      result: { body: 'Dear counsel, …', subject: 'Re: Acme' },
      chips: [{ label: 'Create a matter', bindingId: 'b-create-matter' }],
    } as unknown as DispatchConsumerResult);

    const { result } = renderHook(() => useConsumerChips(makeDeps()));
    await act(async () => {
      result.current.dispatchBinding('b-draft');
    });
    await flush();

    render(<FluentProvider theme={webLightTheme}>{result.current.consumerChipsSlot}</FluentProvider>);
    expect(screen.getByTestId('consumer-chip-local:send-as-email')).toBeInTheDocument();
    expect(screen.getByTestId('consumer-chip-local:save-to-document')).toBeInTheDocument();
    expect(screen.getByTestId('consumer-chip-b-create-matter')).toBeInTheDocument();
  });

  it('routes a local chip to onLocalChipAction WITHOUT re-dispatching', async () => {
    const onLocalChipAction = jest.fn<void, [string]>();
    dispatchConsumerMock.mockResolvedValue({
      streamId: 's',
      status: 'complete',
      disposition: 'informational',
      consumerType: 'chat-summarize',
      result: { tldr: 'A summary.' },
      chips: [],
    } as unknown as DispatchConsumerResult);

    const { result } = renderHook(() => useConsumerChips(makeDeps({ onLocalChipAction, sessionAttachmentCount: 1 })));
    await act(async () => {
      result.current.dispatchBinding('b-summarize');
    });
    await flush();

    render(<FluentProvider theme={webLightTheme}>{result.current.consumerChipsSlot}</FluentProvider>);
    fireEvent.click(screen.getByTestId('consumer-chip-local:ask-about-files'));

    expect(onLocalChipAction).toHaveBeenCalledWith('local:ask-about-files');
    // The initial summarize dispatch fired once; the local chip must NOT trigger a second dispatch.
    expect(dispatchConsumerMock).toHaveBeenCalledTimes(1);
  });
});

describe('useConsumerChips — task 031 DEF-09 sessionIdOverride threading', () => {
  it('dispatchBinding threads args.sessionIdOverride verbatim to dispatchConsumer', async () => {
    dispatchConsumerMock.mockResolvedValue({
      streamId: 's',
      status: 'complete',
      disposition: 'compose',
      result: { overallRisk: 'low', flaggedSections: [] },
    });

    const { result } = renderHook(() => useConsumerChips(makeDeps()));
    await act(async () => {
      result.current.dispatchBinding('review-binding-1', {
        slots: { fileIds: ['file-1'] },
        sessionIdOverride: 'doc-session-Z',
      });
    });
    await flush();

    expect(dispatchConsumerMock).toHaveBeenCalledWith(
      'review-binding-1',
      expect.objectContaining({ sessionIdOverride: 'doc-session-Z' })
    );
  });

  it('omitting sessionIdOverride keeps the exact pre-031 call shape (undefined, bound-session behavior)', async () => {
    dispatchConsumerMock.mockResolvedValue({
      streamId: 's',
      status: 'complete',
      disposition: 'informational',
      result: { tldr: 'summary' },
    });

    const { result } = renderHook(() => useConsumerChips(makeDeps()));
    await act(async () => {
      result.current.dispatchBinding('binding-1');
    });
    await flush();

    expect(dispatchConsumerMock).toHaveBeenCalledWith(
      'binding-1',
      expect.objectContaining({ sessionIdOverride: undefined })
    );
  });
});

describe('useConsumerChips — task 031(d) gating: NDA-REVIEW findings never attach edit controls', () => {
  it('an NDA-REVIEW-shaped result renders a PLAIN confirmation message (no composeEdit metadata, no raw JSON)', async () => {
    dispatchConsumerMock.mockResolvedValue({
      streamId: 's',
      status: 'complete',
      disposition: 'compose', // task 030's disposition flip — still dispatched via chips.dispatchBinding
      result: {
        overallRisk: 'medium',
        flaggedSections: [{ quotedText: 'Section 4.2', explanation: 'Broad indemnity.' }],
      },
    });

    const enqueueAssistantMessage = jest.fn<void, [IChatMessage]>();
    const { result } = renderHook(() =>
      useConsumerChips(makeDeps({ enqueueAssistantMessage }))
    );
    await act(async () => {
      result.current.dispatchBinding('review-binding-1', {
        slots: { fileIds: ['file-1'] },
        resultLabel: 'NDA',
      });
    });
    await flush();

    expect(enqueueAssistantMessage).toHaveBeenCalledTimes(1);
    const message = enqueueAssistantMessage.mock.calls[0][0];
    // The per-section Assistant outcome summary (ADR-041 — outcome reporting is kept).
    expect(message.content).toContain('finished reviewing');
    expect(message.content).toContain('NDA');
    // Gating (d): NEVER the edit-controls shape (Accept/Reject/Try-another) and NEVER the raw
    // flaggedSections JSON dumped into the transcript.
    expect(message.metadata?.composeEdit).toBeUndefined();
    expect(message.content).not.toContain('flaggedSections');
    expect(message.content).not.toContain('Section 4.2');
  });
});
