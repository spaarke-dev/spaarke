/**
 * useCommunicationArrivals — FR-22 client-consumer tests (task 045).
 *
 * Proves the core FR-22 acceptance (SC-10) at the hook level, with NO `@spaarke/notifications` /
 * `@microsoft/signalr` runtime dependency — the hook consumes an INJECTED fake client:
 *   1. a consumed `communication-arrived` raises the unread badge (count) + the awareness callback (toast);
 *   2. the awareness path is signal-only and INDEPENDENT of content loading — a parallel ~5s content
 *      poller keeps ticking whether or not a signal arrives, and a consumed signal never triggers a
 *      content fetch (NFR-03 — the spine is not the content channel);
 *   3. `reset()` clears the unread counter.
 */

import * as React from 'react';
import { act, render, screen } from '@testing-library/react';
import {
  useCommunicationArrivals,
  type ArrivalEvent,
  type ArrivalNotificationsClient,
} from './useCommunicationArrivals';

/** In-memory fake spine client — records the handler and lets a test FIRE arrivals synchronously. */
class FakeNotificationsClient implements ArrivalNotificationsClient {
  private handler: ((event: ArrivalEvent) => void) | undefined;
  started = false;
  stopped = false;

  registerHandler(_kind: 'communication-arrived', callback: (event: ArrivalEvent) => void): () => void {
    this.handler = callback;
    return () => {
      this.handler = undefined;
    };
  }

  async start(): Promise<void> {
    this.started = true;
  }

  async stop(): Promise<void> {
    this.stopped = true;
  }

  /** Simulate the client delivering a live `communication-arrived` signal. */
  fire(event: ArrivalEvent): void {
    this.handler?.(event);
  }
}

/**
 * Harness: mounts the hook AND an independent ~5s content poller (mirrors `ConversationView`'s default
 * 5000 ms poll). The content poll body is a spy so a test can prove a consumed signal never triggers it.
 */
function Harness(props: {
  client: FakeNotificationsClient;
  onArrival: (event: ArrivalEvent) => void;
  contentFetch: () => void;
}) {
  const createClient = React.useCallback(() => props.client, [props.client]);
  const { unreadCount, reset } = useCommunicationArrivals({ createClient, onArrival: props.onArrival });

  const [pollTicks, setPollTicks] = React.useState(0);
  React.useEffect(() => {
    const id = setInterval(() => {
      props.contentFetch(); // the content load — INDEPENDENT of the awareness signal
      setPollTicks(n => n + 1);
    }, 5000);
    return () => clearInterval(id);
  }, [props.contentFetch]);

  return (
    <div>
      <span data-testid="unread">{unreadCount}</span>
      <span data-testid="polls">{pollTicks}</span>
      <button type="button" onClick={reset}>
        reset
      </button>
    </div>
  );
}

let arrivalSeq = 0;
function liveArrival(): ArrivalEvent {
  // Signal-only live push: NO envelope (the spine is signal-only on the wire, NFR-02/03).
  arrivalSeq += 1;
  return { outboxRowId: `outbox-${arrivalSeq}`, kind: 'communication-arrived', source: 'live' };
}

describe('useCommunicationArrivals (FR-22 / SC-10)', () => {
  beforeEach(() => jest.useFakeTimers());
  afterEach(() => {
    jest.runOnlyPendingTimers();
    jest.useRealTimers();
  });

  it('raises the unread badge + awareness callback when a communication-arrived signal is consumed', async () => {
    const client = new FakeNotificationsClient();
    const onArrival = jest.fn();
    const contentFetch = jest.fn();

    render(<Harness client={client} onArrival={onArrival} contentFetch={contentFetch} />);
    // Flush the mount effect's async start().
    await act(async () => {
      await Promise.resolve();
    });

    expect(screen.getByTestId('unread').textContent).toBe('0');
    expect(client.started).toBe(true);

    act(() => client.fire(liveArrival()));

    expect(screen.getByTestId('unread').textContent).toBe('1');
    expect(onArrival).toHaveBeenCalledTimes(1);
    // Awareness is signal-only: the delivered event carries no content envelope (NFR-03).
    expect(onArrival.mock.calls[0][0].envelope).toBeUndefined();
    // The signal did NOT trigger a content fetch — content is loaded by the poll, not the spine (NFR-03).
    expect(contentFetch).not.toHaveBeenCalled();
  });

  it('keeps content polling on its own ~5s cadence, independent of arrival signals (NFR-03)', async () => {
    const client = new FakeNotificationsClient();
    const contentFetch = jest.fn();

    render(<Harness client={client} onArrival={jest.fn()} contentFetch={contentFetch} />);
    await act(async () => {
      await Promise.resolve();
    });

    // Content polls with NO signal at all.
    act(() => jest.advanceTimersByTime(5000));
    expect(contentFetch).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId('polls').textContent).toBe('1');
    expect(screen.getByTestId('unread').textContent).toBe('0');

    // A signal bumps the badge but does NOT drive content...
    act(() => client.fire(liveArrival()));
    expect(screen.getByTestId('unread').textContent).toBe('1');
    expect(contentFetch).toHaveBeenCalledTimes(1); // unchanged by the signal

    // ...and content keeps polling AFTER the signal, on its own cadence.
    act(() => jest.advanceTimersByTime(5000));
    expect(contentFetch).toHaveBeenCalledTimes(2);
    expect(screen.getByTestId('polls').textContent).toBe('2');
    expect(screen.getByTestId('unread').textContent).toBe('1'); // still 1 — content poll never bumps the badge
  });

  it('reset() clears the unread counter', async () => {
    const client = new FakeNotificationsClient();

    render(<Harness client={client} onArrival={jest.fn()} contentFetch={jest.fn()} />);
    await act(async () => {
      await Promise.resolve();
    });

    act(() => client.fire(liveArrival()));
    act(() => client.fire(liveArrival()));
    expect(screen.getByTestId('unread').textContent).toBe('2');

    act(() => screen.getByText('reset').click());
    expect(screen.getByTestId('unread').textContent).toBe('0');
  });
});
