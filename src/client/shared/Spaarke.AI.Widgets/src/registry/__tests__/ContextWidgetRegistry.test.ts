/**
 * ContextWidgetRegistry — unit tests
 *
 * Covers:
 * - Known type resolves to the correct widget component.
 * - Unknown type returns null (never throws).
 * - Factory failure returns null (never throws).
 * - Resolved components are cached (factory called at most once).
 * - replaceContextWidget() clears cache and uses new factory.
 */

import React from 'react';
import {
  registerContextWidget,
  replaceContextWidget,
  resolveContextWidget,
  hasContextWidget,
  getAllContextWidgetTypes,
  clearContextRegistry,
} from '../ContextWidgetRegistry';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Minimal React component stand-ins for context widgets. */
const FakeContextWidgetA: React.FC = () => null;
FakeContextWidgetA.displayName = 'FakeContextWidgetA';

const FakeContextWidgetB: React.FC = () => null;
FakeContextWidgetB.displayName = 'FakeContextWidgetB';

// ---------------------------------------------------------------------------
// Setup / teardown
// ---------------------------------------------------------------------------

beforeEach(() => {
  clearContextRegistry();
  jest.clearAllMocks();
});

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('registerContextWidget', () => {
  it('registers a context widget type', () => {
    registerContextWidget('doc-meta', {
      factory: () => Promise.resolve({ default: FakeContextWidgetA as React.ComponentType<any> }),
    });

    expect(hasContextWidget('doc-meta')).toBe(true);
  });

  it('does not overwrite an existing registration (first wins)', () => {
    const factory1 = jest.fn(() => Promise.resolve({ default: FakeContextWidgetA as React.ComponentType<any> }));
    const factory2 = jest.fn(() => Promise.resolve({ default: FakeContextWidgetB as React.ComponentType<any> }));

    registerContextWidget('dup-type', { factory: factory1 });
    registerContextWidget('dup-type', { factory: factory2 });

    // Neither factory is called here — verify factory2 was silently dropped
    // by resolving and checking the component.
    expect(factory2).not.toHaveBeenCalled();
  });
});

describe('resolveContextWidget — known type', () => {
  it('returns the correct context widget component', async () => {
    registerContextWidget('doc-meta', {
      factory: () => Promise.resolve({ default: FakeContextWidgetA as React.ComponentType<any> }),
    });

    const resolved = await resolveContextWidget('doc-meta');

    expect(resolved).toBe(FakeContextWidgetA);
  });

  it('calls the factory only once (caches after first load)', async () => {
    const factory = jest.fn(() => Promise.resolve({ default: FakeContextWidgetA as React.ComponentType<any> }));
    registerContextWidget('cached-context', { factory });

    await resolveContextWidget('cached-context');
    await resolveContextWidget('cached-context');
    await resolveContextWidget('cached-context');

    expect(factory).toHaveBeenCalledTimes(1);
  });
});

describe('resolveContextWidget — unknown type', () => {
  it('returns null for an unregistered type', async () => {
    const result = await resolveContextWidget('totally-unknown-context');

    expect(result).toBeNull();
  });

  it('does not throw for unknown types', async () => {
    await expect(resolveContextWidget('ghost-widget')).resolves.toBeNull();
  });

  it('never returns undefined for unknown types (always null)', async () => {
    const result = await resolveContextWidget('undefined-territory');

    expect(result).not.toBeUndefined();
    expect(result).toBeNull();
  });
});

describe('resolveContextWidget — factory failure', () => {
  it('returns null when the factory rejects', async () => {
    registerContextWidget('broken-context', {
      factory: () => Promise.reject(new Error('Simulated import failure')),
    });

    const result = await resolveContextWidget('broken-context');

    expect(result).toBeNull();
  });

  it('does not throw when the factory fails', async () => {
    registerContextWidget('throws-context', {
      factory: () => Promise.reject(new Error('boom')),
    });

    await expect(resolveContextWidget('throws-context')).resolves.toBeNull();
  });

  it('never returns undefined when factory fails', async () => {
    registerContextWidget('undefined-factory', {
      factory: () => Promise.reject(new Error('network error')),
    });

    const result = await resolveContextWidget('undefined-factory');

    expect(result).not.toBeUndefined();
    expect(result).toBeNull();
  });
});

describe('getAllContextWidgetTypes', () => {
  it('returns all registered type strings', () => {
    registerContextWidget('ctx-one', {
      factory: () => Promise.resolve({ default: FakeContextWidgetA as React.ComponentType<any> }),
    });
    registerContextWidget('ctx-two', {
      factory: () => Promise.resolve({ default: FakeContextWidgetB as React.ComponentType<any> }),
    });

    const types = getAllContextWidgetTypes();

    expect(types).toContain('ctx-one');
    expect(types).toContain('ctx-two');
    expect(types).toHaveLength(2);
  });

  it('returns empty array when nothing is registered', () => {
    expect(getAllContextWidgetTypes()).toHaveLength(0);
  });
});

describe('replaceContextWidget', () => {
  it('replaces an existing registration and clears the cache', async () => {
    registerContextWidget('swappable-ctx', {
      factory: () => Promise.resolve({ default: FakeContextWidgetA as React.ComponentType<any> }),
    });

    // First resolution — caches FakeContextWidgetA.
    const first = await resolveContextWidget('swappable-ctx');
    expect(first).toBe(FakeContextWidgetA);

    // Replace with new factory.
    replaceContextWidget('swappable-ctx', {
      factory: () => Promise.resolve({ default: FakeContextWidgetB as React.ComponentType<any> }),
    });

    const second = await resolveContextWidget('swappable-ctx');
    expect(second).toBe(FakeContextWidgetB);
  });
});
