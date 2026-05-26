/**
 * Tests for the cross-context auth event broadcasting layer.
 *
 * jsdom does not ship BroadcastChannel, and Node's worker_threads
 * implementation does not deliver messages within the same process reliably
 * for unit testing. We install a minimal in-memory mock that pubs/subs across
 * instances with the same channel name — sufficient to exercise the public
 * surface of broadcastChannel.ts. Real browser environments have a native
 * BroadcastChannel that does the cross-context delivery.
 */

import {
  broadcastLogout,
  onAuthBroadcast,
  _resetBroadcastChannelForTests,
  type AuthBroadcastMessage,
} from '../src/broadcastChannel';

const registry = new Map<string, Set<MockBroadcastChannel>>();

class MockBroadcastChannel {
  private listeners = new Set<(event: MessageEvent) => void>();
  private closed = false;

  constructor(public readonly name: string) {
    let set = registry.get(name);
    if (!set) {
      set = new Set();
      registry.set(name, set);
    }
    set.add(this);
  }

  postMessage(data: unknown): void {
    if (this.closed) return;
    const set = registry.get(this.name);
    if (!set) return;
    // BroadcastChannel does NOT deliver to the sender's own instance
    for (const channel of set) {
      if (channel === this || channel.closed) continue;
      const event = { data } as MessageEvent;
      for (const listener of channel.listeners) {
        listener(event);
      }
    }
  }

  addEventListener(_type: 'message', listener: (event: MessageEvent) => void): void {
    this.listeners.add(listener);
  }

  removeEventListener(_type: 'message', listener: (event: MessageEvent) => void): void {
    this.listeners.delete(listener);
  }

  close(): void {
    this.closed = true;
    this.listeners.clear();
    registry.get(this.name)?.delete(this);
  }
}

beforeAll(() => {
  (globalThis as unknown as { BroadcastChannel: typeof MockBroadcastChannel }).BroadcastChannel =
    MockBroadcastChannel;
});

afterEach(() => {
  _resetBroadcastChannelForTests();
  registry.clear();
});

describe('broadcastChannel', () => {
  it('delivers a logout message to a registered listener', () => {
    const received: AuthBroadcastMessage[] = [];
    const dispose = onAuthBroadcast((msg) => received.push(msg));

    // Send from a separate channel instance (BroadcastChannel never echoes to self)
    const sender = new (globalThis as unknown as { BroadcastChannel: typeof MockBroadcastChannel }).BroadcastChannel(
      'spaarke-auth-events'
    );
    try {
      sender.postMessage({ type: 'logout' } satisfies AuthBroadcastMessage);
      expect(received).toEqual([{ type: 'logout' }]);
    } finally {
      sender.close();
      dispose();
    }
  });

  it('dispose function removes the listener', () => {
    const received: AuthBroadcastMessage[] = [];
    const dispose = onAuthBroadcast((msg) => received.push(msg));
    dispose();

    const sender = new (globalThis as unknown as { BroadcastChannel: typeof MockBroadcastChannel }).BroadcastChannel(
      'spaarke-auth-events'
    );
    try {
      sender.postMessage({ type: 'logout' } satisfies AuthBroadcastMessage);
      expect(received).toEqual([]);
    } finally {
      sender.close();
    }
  });

  it('broadcastLogout is callable without a registered listener (no throw)', () => {
    expect(() => broadcastLogout()).not.toThrow();
  });

  it('ignores messages without a `type` field', () => {
    const received: AuthBroadcastMessage[] = [];
    const dispose = onAuthBroadcast((msg) => received.push(msg));

    const sender = new (globalThis as unknown as { BroadcastChannel: typeof MockBroadcastChannel }).BroadcastChannel(
      'spaarke-auth-events'
    );
    try {
      sender.postMessage('not-an-object');
      sender.postMessage({ foo: 'bar' });
      sender.postMessage({ type: 'logout' } satisfies AuthBroadcastMessage);

      expect(received).toEqual([{ type: 'logout' }]);
    } finally {
      sender.close();
      dispose();
    }
  });
});
