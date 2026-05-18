/**
 * Unit tests for PaneEventBus
 *
 * Covers the three scenarios required by task AIPU2-074:
 *  (a) Multiple subscribers on the same channel all receive every dispatched event.
 *  (b) Unsubscribed handler no longer receives events after the returned cleanup is called.
 *  (c) Dispatch on a channel with no subscribers does not throw.
 */

import { PaneEventBus } from '../PaneEventBus';
import type { WorkspacePaneEvent, ContextPaneEvent, SafetyPaneEvent } from '../PaneEventTypes';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeBus(): PaneEventBus {
  return new PaneEventBus();
}

// ---------------------------------------------------------------------------
// (a) Multi-subscriber: all handlers on a channel receive every event
// ---------------------------------------------------------------------------

describe('PaneEventBus — multi-subscriber', () => {
  it('calls all subscribers when an event is dispatched', () => {
    const bus = makeBus();
    const calls: string[] = [];

    bus.subscribe('workspace', () => calls.push('handler-A'));
    bus.subscribe('workspace', () => calls.push('handler-B'));
    bus.subscribe('workspace', () => calls.push('handler-C'));

    const event: WorkspacePaneEvent = { type: 'tab_change', tabId: 'analysis' };
    bus.dispatch('workspace', event);

    expect(calls).toEqual(['handler-A', 'handler-B', 'handler-C']);
  });

  it('delivers the correct event payload to each subscriber', () => {
    const bus = makeBus();
    const received: WorkspacePaneEvent[] = [];

    bus.subscribe('workspace', (e) => received.push(e));
    bus.subscribe('workspace', (e) => received.push(e));

    const event: WorkspacePaneEvent = {
      type: 'widget_action',
      widgetType: 'document-summary',
      action: 'expand',
      targetWidgetId: 'widget-42',
    };
    bus.dispatch('workspace', event);

    expect(received).toHaveLength(2);
    expect(received[0]).toBe(event); // same reference — no copying
    expect(received[1]).toBe(event);
  });

  it('subscribers on different channels do not receive each other\'s events', () => {
    const bus = makeBus();
    const workspaceCalls: WorkspacePaneEvent[] = [];
    const contextCalls: ContextPaneEvent[] = [];

    bus.subscribe('workspace', (e) => workspaceCalls.push(e));
    bus.subscribe('context', (e) => contextCalls.push(e));

    bus.dispatch('workspace', { type: 'widget_load', widgetType: 'clause-list' });

    expect(workspaceCalls).toHaveLength(1);
    expect(contextCalls).toHaveLength(0);
  });

  it('dispatching multiple events delivers each to all subscribers in order', () => {
    const bus = makeBus();
    const log: string[] = [];

    bus.subscribe('workspace', (e) => log.push(`A:${e.type}`));
    bus.subscribe('workspace', (e) => log.push(`B:${e.type}`));

    bus.dispatch('workspace', { type: 'widget_load' });
    bus.dispatch('workspace', { type: 'tab_change', tabId: 't1' });

    expect(log).toEqual([
      'A:widget_load',
      'B:widget_load',
      'A:tab_change',
      'B:tab_change',
    ]);
  });
});

// ---------------------------------------------------------------------------
// (b) Unsubscribe: cleaned-up handler no longer receives events
// ---------------------------------------------------------------------------

describe('PaneEventBus — unsubscribe', () => {
  it('stops calling a handler after unsubscribe is invoked', () => {
    const bus = makeBus();
    const calls: number[] = [];

    const unsubscribe = bus.subscribe('context', () => calls.push(1));

    bus.dispatch('context', { type: 'context_update', contextType: 'document' });
    expect(calls).toHaveLength(1);

    unsubscribe();

    bus.dispatch('context', { type: 'context_highlight', citationId: 'ref-99' });
    expect(calls).toHaveLength(1); // no new call after unsubscribe
  });

  it('unsubscribing one handler leaves other handlers on the same channel intact', () => {
    const bus = makeBus();
    const aCalls: number[] = [];
    const bCalls: number[] = [];

    const unsubscribeA = bus.subscribe('context', () => aCalls.push(1));
    bus.subscribe('context', () => bCalls.push(1));

    bus.dispatch('context', { type: 'stage_change' });
    expect(aCalls).toHaveLength(1);
    expect(bCalls).toHaveLength(1);

    unsubscribeA();

    bus.dispatch('context', { type: 'stage_change' });
    expect(aCalls).toHaveLength(1); // A stopped
    expect(bCalls).toHaveLength(2); // B still receives
  });

  it('calling unsubscribe multiple times does not throw', () => {
    const bus = makeBus();
    const unsubscribe = bus.subscribe('conversation', () => {});

    expect(() => {
      unsubscribe();
      unsubscribe(); // idempotent — Set.delete on absent member is safe
    }).not.toThrow();
  });

  it('reflects reduced subscriber count after unsubscribe', () => {
    const bus = makeBus();
    const unsub1 = bus.subscribe('safety', () => {});
    const unsub2 = bus.subscribe('safety', () => {});

    expect(bus.subscriberCount('safety')).toBe(2);

    unsub1();
    expect(bus.subscriberCount('safety')).toBe(1);

    unsub2();
    expect(bus.subscriberCount('safety')).toBe(0);
  });
});

// ---------------------------------------------------------------------------
// (c) Empty channel: dispatch with no subscribers does not throw
// ---------------------------------------------------------------------------

describe('PaneEventBus — empty channel dispatch', () => {
  it('does not throw when dispatching to a channel with no subscribers', () => {
    const bus = makeBus();

    expect(() => {
      bus.dispatch('safety', { type: 'safety_annotation', confidence: 'high' });
    }).not.toThrow();
  });

  it('does not throw when dispatching to a channel after all handlers unsubscribed', () => {
    const bus = makeBus();
    const unsub = bus.subscribe('conversation', () => {});
    unsub();

    expect(() => {
      bus.dispatch('conversation', { type: 'playbook_change', playbookId: 'pb-1' });
    }).not.toThrow();
  });
});

// ---------------------------------------------------------------------------
// Edge cases
// ---------------------------------------------------------------------------

describe('PaneEventBus — edge cases', () => {
  it('handlers added during dispatch are not called for the current event', () => {
    const bus = makeBus();
    const calls: string[] = [];

    bus.subscribe('workspace', () => {
      calls.push('original');
      // Adding a new handler mid-dispatch — should NOT be called this round.
      bus.subscribe('workspace', () => calls.push('late'));
    });

    bus.dispatch('workspace', { type: 'widget_load' });

    expect(calls).toEqual(['original']);

    // On the NEXT dispatch, the late handler should fire.
    bus.dispatch('workspace', { type: 'widget_load' });
    expect(calls).toEqual(['original', 'original', 'late']);
  });

  it('safety channel carries typed payload correctly', () => {
    const bus = makeBus();
    const received: SafetyPaneEvent[] = [];

    bus.subscribe('safety', (e) => received.push(e));

    const event: SafetyPaneEvent = {
      type: 'safety_annotation',
      confidence: 'medium',
      groundedness: { score: 0.82 },
      citations: { 'claim-1': { sourceId: 'doc-7', range: '120-145' } },
    };
    bus.dispatch('safety', event);

    expect(received[0]).toEqual(event);
  });

  it('returns 0 subscriber count on a fresh bus for all channels', () => {
    const bus = makeBus();
    expect(bus.subscriberCount('workspace')).toBe(0);
    expect(bus.subscriberCount('context')).toBe(0);
    expect(bus.subscriberCount('conversation')).toBe(0);
    expect(bus.subscriberCount('safety')).toBe(0);
  });
});
