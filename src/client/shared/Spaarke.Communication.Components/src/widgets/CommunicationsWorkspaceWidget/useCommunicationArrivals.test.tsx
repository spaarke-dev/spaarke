/**
 * useCommunicationArrivals — FR-22 client-consumer tests (task 045, Option A: register-only).
 *
 * Proves the core FR-22 acceptance (SC-10) at the hook level, with NO `@spaarke/notifications` /
 * `@microsoft/signalr` runtime dependency — the hook consumes an INJECTED register-only `subscribe`:
 *   1. a consumed `communication-arrived` raises the unread badge (count) + the awareness callback (toast);
 *   2. the awareness path is signal-only and INDEPENDENT of content loading — a parallel ~5s content
 *      poller keeps ticking whether or not a signal arrives, and a consumed signal never triggers a
 *      content fetch (NFR-03 — the spine is not the content channel);
 *   3. `reset()` clears the unread counter;
 *   4. when the host never wired the spine (`subscribe` undefined), awareness is OFF and NO client/connection
 *      is opened (one-connection invariant — the hook constructs nothing);
 *   5. unmount UNREGISTERS the consumer (register-only — it never stops the shared connection).
 */

import * as React from 'react';
import { act, render, screen } from '@testing-library/react';
import { useCommunicationArrivals, type ArrivalEvent, type ArrivalSubscribe } from './useCommunicationArrivals';

/**
 * In-memory fake register-only subscription — records the handler, lets a test FIRE arrivals
 * synchronously, and tracks register/unregister so we can prove the host-owned lifecycle
 * (the hook registers on mount and UNregisters on unmount; it never starts/stops a connection).
 */
function makeFakeSubscribe() {
  let handler: ((event: ArrivalEvent) => void) | undefined;
  let registered = false;
  const subscribe: ArrivalSubscribe = onArrival => {
    handler = onArrival;
    registered = true;
    return () => {
      handler = undefined;
      registered = false;
    };
  };
  return {
    subscribe,
    fire: (event: ArrivalEvent): void => handler?.(event),
    isRegistered: (): boolean => registered,
  };
}

/**
 * Harness: mounts the hook AND an independent ~5s content poller (mirrors `ConversationView`'s default
 * 5000 ms poll). The content poll body is a spy so a test can prove a consumed signal never triggers it.
 */
function Harness(props: {
  subscribe?: ArrivalSubscribe;
  onArrival: (event: ArrivalEvent) => void;
  contentFetch: () => void;
}) {
  const { unreadCount, reset } = useCommunicationArrivals({
    subscribe: props.subscribe,
    onArrival: props.onArrival,
  });

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

  it('raises the unread badge + awareness callback when a communication-arrived signal is consumed', () => {
    const spine = makeFakeSubscribe();
    const onArrival = jest.fn();
    const contentFetch = jest.fn();

    render(<Harness subscribe={spine.subscribe} onArrival={onArrival} contentFetch={contentFetch} />);

    expect(screen.getByTestId('unread').textContent).toBe('0');
    expect(spine.isRegistered()).toBe(true); // registered on mount (host owns start/stop, not this hook)

    act(() => spine.fire(liveArrival()));

    expect(screen.getByTestId('unread').textContent).toBe('1');
    expect(onArrival).toHaveBeenCalledTimes(1);
    // Awareness is signal-only: the delivered event carries no content envelope (NFR-03).
    expect(onArrival.mock.calls[0][0].envelope).toBeUndefined();
    // The signal did NOT trigger a content fetch — content is loaded by the poll, not the spine (NFR-03).
    expect(contentFetch).not.toHaveBeenCalled();
  });

  it('keeps content polling on its own ~5s cadence, independent of arrival signals (NFR-03)', () => {
    const spine = makeFakeSubscribe();
    const contentFetch = jest.fn();

    render(<Harness subscribe={spine.subscribe} onArrival={jest.fn()} contentFetch={contentFetch} />);

    // Content polls with NO signal at all.
    act(() => jest.advanceTimersByTime(5000));
    expect(contentFetch).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId('polls').textContent).toBe('1');
    expect(screen.getByTestId('unread').textContent).toBe('0');

    // A signal bumps the badge but does NOT drive content...
    act(() => spine.fire(liveArrival()));
    expect(screen.getByTestId('unread').textContent).toBe('1');
    expect(contentFetch).toHaveBeenCalledTimes(1); // unchanged by the signal

    // ...and content keeps polling AFTER the signal, on its own cadence.
    act(() => jest.advanceTimersByTime(5000));
    expect(contentFetch).toHaveBeenCalledTimes(2);
    expect(screen.getByTestId('polls').textContent).toBe('2');
    expect(screen.getByTestId('unread').textContent).toBe('1'); // still 1 — content poll never bumps the badge
  });

  it('reset() clears the unread counter', () => {
    const spine = makeFakeSubscribe();

    render(<Harness subscribe={spine.subscribe} onArrival={jest.fn()} contentFetch={jest.fn()} />);

    act(() => spine.fire(liveArrival()));
    act(() => spine.fire(liveArrival()));
    expect(screen.getByTestId('unread').textContent).toBe('2');

    act(() => screen.getByText('reset').click());
    expect(screen.getByTestId('unread').textContent).toBe('0');
  });

  it('stays OFF and opens no connection when the host never wired the spine (subscribe undefined)', () => {
    const contentFetch = jest.fn();
    // No `subscribe` — the host did not wire the spine. The hook must construct NOTHING (one-connection
    // invariant): the widget renders, content still polls, and the unread badge never moves.
    render(<Harness subscribe={undefined} onArrival={jest.fn()} contentFetch={contentFetch} />);

    expect(screen.getByTestId('unread').textContent).toBe('0');
    act(() => jest.advanceTimersByTime(5000));
    expect(contentFetch).toHaveBeenCalledTimes(1); // content still polls
    expect(screen.getByTestId('unread').textContent).toBe('0'); // no awareness, no crash
  });

  it('unregisters the consumer on unmount (register-only — never stops the shared connection)', () => {
    const spine = makeFakeSubscribe();

    const { unmount } = render(<Harness subscribe={spine.subscribe} onArrival={jest.fn()} contentFetch={jest.fn()} />);
    expect(spine.isRegistered()).toBe(true);

    unmount();
    expect(spine.isRegistered()).toBe(false); // unregistered — but the (fake) shared connection was never stopped
  });
});
