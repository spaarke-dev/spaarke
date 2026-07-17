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

import { renderHook, act } from '@testing-library/react';
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
