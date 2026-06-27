/**
 * Unit tests for usePaneEvent and useDispatchPaneEvent hooks.
 *
 * Verifies:
 *  - usePaneEvent subscribes on mount and unsubscribes on unmount.
 *  - usePaneEvent handles inline (unstable) handler references correctly.
 *  - useDispatchPaneEvent returns a stable function reference.
 *  - Both hooks throw outside a PaneEventBusProvider.
 */

import '@testing-library/jest-dom';
import React, { useState } from 'react';
import { render, screen, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { PaneEventBus } from '../PaneEventBus';
import { PaneEventBusProvider } from '../PaneEventBusContext';
import { usePaneEvent } from '../usePaneEvent';
import { useDispatchPaneEvent } from '../useDispatchPaneEvent';
import type { WorkspacePaneEvent, ContextPaneEvent } from '../PaneEventTypes';

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------

/**
 * Wraps children in a PaneEventBusProvider that uses a caller-supplied bus
 * so tests can inspect subscriber counts and dispatch events directly.
 */
function Wrapper({ bus, children }: { bus: PaneEventBus; children: React.ReactNode }): React.JSX.Element {
  return <PaneEventBusProvider bus={bus}>{children}</PaneEventBusProvider>;
}

// ---------------------------------------------------------------------------
// usePaneEvent — subscribe / unsubscribe lifecycle
// ---------------------------------------------------------------------------

describe('usePaneEvent — lifecycle', () => {
  it('subscribes on mount', () => {
    const bus = new PaneEventBus();

    function Subscriber() {
      usePaneEvent('workspace', () => {});
      return null;
    }

    expect(bus.subscriberCount('workspace')).toBe(0);
    render(
      <Wrapper bus={bus}>
        <Subscriber />
      </Wrapper>
    );
    expect(bus.subscriberCount('workspace')).toBe(1);
  });

  it('unsubscribes on unmount — no memory leak', () => {
    const bus = new PaneEventBus();

    function Subscriber() {
      usePaneEvent('workspace', () => {});
      return null;
    }

    const { unmount } = render(
      <Wrapper bus={bus}>
        <Subscriber />
      </Wrapper>
    );
    expect(bus.subscriberCount('workspace')).toBe(1);

    unmount();
    expect(bus.subscriberCount('workspace')).toBe(0);
  });

  it('delivers dispatched events to the subscribed component', () => {
    const bus = new PaneEventBus();
    const received: WorkspacePaneEvent[] = [];

    function Subscriber() {
      usePaneEvent('workspace', e => received.push(e));
      return null;
    }

    render(
      <Wrapper bus={bus}>
        <Subscriber />
      </Wrapper>
    );

    act(() => {
      bus.dispatch('workspace', { type: 'tab_change', tabId: 'analysis' });
    });

    expect(received).toHaveLength(1);
    expect(received[0].type).toBe('tab_change');
  });
});

// ---------------------------------------------------------------------------
// usePaneEvent — stable handler ref (inline function support)
// ---------------------------------------------------------------------------

describe('usePaneEvent — inline handler reference', () => {
  it('always calls the latest handler without re-subscribing on each render', () => {
    const bus = new PaneEventBus();
    let capturedTabId: string | undefined;
    let renderCount = 0;

    function Subscriber() {
      const [count, setCount] = useState(0);
      renderCount = count;

      // Inline handler — new function reference each render
      usePaneEvent('workspace', e => {
        if (e.type === 'tab_change') {
          capturedTabId = e.tabId;
          setCount(c => c + 1);
        }
      });

      return <div data-testid="count">{count}</div>;
    }

    render(
      <Wrapper bus={bus}>
        <Subscriber />
      </Wrapper>
    );

    // Subscription count must be exactly 1 even after re-renders
    expect(bus.subscriberCount('workspace')).toBe(1);

    act(() => {
      bus.dispatch('workspace', { type: 'tab_change', tabId: 'summary' });
    });

    expect(capturedTabId).toBe('summary');
    // After the state update triggers a re-render, still exactly 1 subscriber
    expect(bus.subscriberCount('workspace')).toBe(1);
  });
});

// ---------------------------------------------------------------------------
// usePaneEvent — multiple hooks in one tree
// ---------------------------------------------------------------------------

describe('usePaneEvent — multiple hooks', () => {
  it('two components can subscribe to the same channel independently', () => {
    const bus = new PaneEventBus();
    const aLog: string[] = [];
    const bLog: string[] = [];

    function SubscriberA() {
      usePaneEvent('context', e => {
        if (e.type === 'context_update') aLog.push('A');
      });
      return null;
    }

    function SubscriberB() {
      usePaneEvent('context', e => {
        if (e.type === 'context_update') bLog.push('B');
      });
      return null;
    }

    render(
      <Wrapper bus={bus}>
        <SubscriberA />
        <SubscriberB />
      </Wrapper>
    );

    act(() => {
      bus.dispatch('context', { type: 'context_update', contextType: 'document' });
    });

    expect(aLog).toEqual(['A']);
    expect(bLog).toEqual(['B']);
  });

  it('unmounting one subscriber does not affect others', () => {
    const bus = new PaneEventBus();
    const calls: string[] = [];

    function SubscriberA() {
      usePaneEvent('context', () => calls.push('A'));
      return null;
    }

    function SubscriberB() {
      usePaneEvent('context', () => calls.push('B'));
      return null;
    }

    function Parent() {
      const [showA, setShowA] = useState(true);
      return (
        <>
          {showA && <SubscriberA />}
          <SubscriberB />
          <button onClick={() => setShowA(false)}>remove-a</button>
        </>
      );
    }

    render(
      <Wrapper bus={bus}>
        <Parent />
      </Wrapper>
    );

    act(() => {
      bus.dispatch('context', { type: 'stage_change' });
    });
    expect(calls).toEqual(['A', 'B']);

    // Unmount SubscriberA
    act(() => {
      screen.getByRole('button', { name: 'remove-a' }).click();
    });

    act(() => {
      bus.dispatch('context', { type: 'stage_change' });
    });
    expect(calls).toEqual(['A', 'B', 'B']); // Only B fires now
  });
});

// ---------------------------------------------------------------------------
// useDispatchPaneEvent — stable reference
// ---------------------------------------------------------------------------

describe('useDispatchPaneEvent — stable reference', () => {
  it('returns the same function reference across re-renders', () => {
    const bus = new PaneEventBus();
    const dispatchRefs: Function[] = [];

    function Dispatcher() {
      const dispatch = useDispatchPaneEvent();
      dispatchRefs.push(dispatch);

      const [, forceRerender] = useState(0);
      return <button onClick={() => forceRerender(n => n + 1)}>rerender</button>;
    }

    render(
      <Wrapper bus={bus}>
        <Dispatcher />
      </Wrapper>
    );
    expect(dispatchRefs).toHaveLength(1);

    act(() => {
      screen.getByRole('button', { name: 'rerender' }).click();
    });
    expect(dispatchRefs).toHaveLength(2);

    // Both renders produced the same function reference
    expect(dispatchRefs[0]).toBe(dispatchRefs[1]);
  });

  it('dispatched event reaches subscribers registered via usePaneEvent', async () => {
    const bus = new PaneEventBus();
    const user = userEvent.setup();
    const received: ContextPaneEvent[] = [];

    function Subscriber() {
      usePaneEvent('context', e => received.push(e));
      return null;
    }

    function Dispatcher() {
      const dispatch = useDispatchPaneEvent();
      return (
        <button onClick={() => dispatch('context', { type: 'context_highlight', citationId: 'ref-1' })}>
          dispatch
        </button>
      );
    }

    render(
      <Wrapper bus={bus}>
        <Subscriber />
        <Dispatcher />
      </Wrapper>
    );

    await user.click(screen.getByRole('button', { name: 'dispatch' }));

    expect(received).toHaveLength(1);
    expect(received[0]).toEqual({
      type: 'context_highlight',
      citationId: 'ref-1',
    });
  });
});

// ---------------------------------------------------------------------------
// Error boundary: hooks outside provider
// ---------------------------------------------------------------------------

describe('hooks outside PaneEventBusProvider', () => {
  // Suppress expected console.error from React's error boundary
  const originalConsoleError = console.error;
  beforeAll(() => {
    console.error = jest.fn();
  });
  afterAll(() => {
    console.error = originalConsoleError;
  });

  it('usePaneEvent throws a descriptive error outside a provider', () => {
    function BadComponent() {
      usePaneEvent('workspace', () => {});
      return null;
    }

    expect(() => render(<BadComponent />)).toThrow(/PaneEventBusProvider/);
  });

  it('useDispatchPaneEvent throws a descriptive error outside a provider', () => {
    function BadComponent() {
      useDispatchPaneEvent();
      return null;
    }

    expect(() => render(<BadComponent />)).toThrow(/PaneEventBusProvider/);
  });
});
