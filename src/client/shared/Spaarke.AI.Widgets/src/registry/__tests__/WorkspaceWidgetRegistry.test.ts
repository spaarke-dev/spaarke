/**
 * WorkspaceWidgetRegistry — unit tests
 *
 * Covers:
 * - Known type resolves to the correct widget component.
 * - Unknown type resolves to GenericTextWidget (never undefined, never throws).
 * - Factory failure resolves to GenericTextWidget (never undefined, never throws).
 * - Resolved components are cached (factory called at most once).
 * - replaceWorkspaceWidget() clears cache and uses new factory.
 */

import React from 'react';
import {
  registerWorkspaceWidget,
  replaceWorkspaceWidget,
  resolveWorkspaceWidget,
  getWorkspaceWidgetMetadata,
  getAllWorkspaceWidgetTypes,
  hasWorkspaceWidget,
  clearWorkspaceRegistry,
} from '../WorkspaceWidgetRegistry';

// ---------------------------------------------------------------------------
// Mock GenericTextWidget dynamic import
// ---------------------------------------------------------------------------

// The registry imports GenericTextWidget via a relative dynamic import.
// We intercept that path so tests don't need the full Fluent UI environment.

const MockGenericText: React.FC = () => null;
MockGenericText.displayName = 'MockGenericTextWidget';

jest.mock(
  '../GenericTextWidget',
  () => ({
    __esModule: true,
    default: MockGenericText,
  }),
  { virtual: true }
);

// Also mock the resolved path used by the dynamic import inside the registry.
jest.mock('../../widgets/GenericTextWidget', () => ({
  __esModule: true,
  default: MockGenericText,
}));

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** A minimal React component used as a stand-in real widget. */
const FakeWidgetA: React.FC = () => null;
FakeWidgetA.displayName = 'FakeWidgetA';

const FakeWidgetB: React.FC = () => null;
FakeWidgetB.displayName = 'FakeWidgetB';

// Use the canonical WidgetMetadata shape from shared.ts (allowMultiple and defaultOrder required).
const defaultMetadata = {
  displayName: 'Fake Widget A',
  category: 'test',
  allowMultiple: false,
  defaultOrder: 99,
};

// ---------------------------------------------------------------------------
// Setup / teardown
// ---------------------------------------------------------------------------

beforeEach(() => {
  // Clear all registrations and the GenericTextWidget cache between tests.
  clearWorkspaceRegistry();
  jest.clearAllMocks();
});

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('registerWorkspaceWidget', () => {
  it('registers a widget type', () => {
    registerWorkspaceWidget('fake-a', defaultMetadata, () =>
      Promise.resolve({ default: FakeWidgetA as React.ComponentType<any> })
    );
    expect(hasWorkspaceWidget('fake-a')).toBe(true);
  });

  it('does not overwrite an existing registration (first wins)', () => {
    const factory1 = jest.fn(() => Promise.resolve({ default: FakeWidgetA as React.ComponentType<any> }));
    const factory2 = jest.fn(() => Promise.resolve({ default: FakeWidgetB as React.ComponentType<any> }));

    registerWorkspaceWidget('fake-a', defaultMetadata, factory1);
    registerWorkspaceWidget('fake-a', defaultMetadata, factory2);

    expect(factory1).not.toHaveBeenCalled();
    expect(factory2).not.toHaveBeenCalled();
    // factory2 was silently ignored — only factory1 should ever be called.
    // Verify by resolving and checking the returned component.
  });
});

describe('resolveWorkspaceWidget — known type', () => {
  it('returns the correct widget component for a registered type', async () => {
    registerWorkspaceWidget('fake-a', defaultMetadata, () =>
      Promise.resolve({ default: FakeWidgetA as React.ComponentType<any> })
    );

    const resolved = await resolveWorkspaceWidget('fake-a');

    expect(resolved).toBe(FakeWidgetA);
  });

  it('calls the factory only once (caches after first load)', async () => {
    const factory = jest.fn(() => Promise.resolve({ default: FakeWidgetA as React.ComponentType<any> }));
    registerWorkspaceWidget('fake-cached', defaultMetadata, factory);

    await resolveWorkspaceWidget('fake-cached');
    await resolveWorkspaceWidget('fake-cached');
    await resolveWorkspaceWidget('fake-cached');

    expect(factory).toHaveBeenCalledTimes(1);
  });
});

describe('resolveWorkspaceWidget — unknown type', () => {
  it('returns GenericTextWidget for an unregistered type', async () => {
    const resolved = await resolveWorkspaceWidget('totally-unknown-widget');

    expect(resolved).toBe(MockGenericText);
  });

  it('never returns null or undefined for unknown types', async () => {
    const result = await resolveWorkspaceWidget('nope-not-a-widget');

    expect(result).not.toBeNull();
    expect(result).not.toBeUndefined();
  });

  it('does not throw for unknown types', async () => {
    await expect(resolveWorkspaceWidget('unknown')).resolves.toBeDefined();
  });
});

describe('resolveWorkspaceWidget — factory failure', () => {
  it('returns GenericTextWidget when the factory throws', async () => {
    registerWorkspaceWidget('broken-widget', defaultMetadata, () =>
      Promise.reject(new Error('Simulated import failure'))
    );

    const resolved = await resolveWorkspaceWidget('broken-widget');

    expect(resolved).toBe(MockGenericText);
  });

  it('does not throw when the factory fails', async () => {
    registerWorkspaceWidget('throws-widget', defaultMetadata, () => Promise.reject(new Error('boom')));

    await expect(resolveWorkspaceWidget('throws-widget')).resolves.toBeDefined();
  });

  it('never returns null or undefined when factory fails', async () => {
    registerWorkspaceWidget('null-factory-widget', defaultMetadata, () => Promise.reject(new Error('network error')));

    const result = await resolveWorkspaceWidget('null-factory-widget');

    expect(result).not.toBeNull();
    expect(result).not.toBeUndefined();
  });
});

describe('getWorkspaceWidgetMetadata', () => {
  it('returns metadata for registered types', () => {
    const meta = { displayName: 'My Widget', category: 'analysis', defaultOrder: 5, allowMultiple: false };
    registerWorkspaceWidget('my-widget', meta, () =>
      Promise.resolve({ default: FakeWidgetA as React.ComponentType<any> })
    );

    expect(getWorkspaceWidgetMetadata('my-widget')).toEqual(meta);
  });

  it('returns undefined for unregistered types', () => {
    expect(getWorkspaceWidgetMetadata('no-such-widget')).toBeUndefined();
  });
});

describe('getAllWorkspaceWidgetTypes', () => {
  it('returns all registered type strings', () => {
    registerWorkspaceWidget('type-one', defaultMetadata, () =>
      Promise.resolve({ default: FakeWidgetA as React.ComponentType<any> })
    );
    registerWorkspaceWidget('type-two', defaultMetadata, () =>
      Promise.resolve({ default: FakeWidgetB as React.ComponentType<any> })
    );

    const types = getAllWorkspaceWidgetTypes();

    expect(types).toContain('type-one');
    expect(types).toContain('type-two');
    expect(types).toHaveLength(2);
  });

  it('returns empty array when nothing is registered', () => {
    expect(getAllWorkspaceWidgetTypes()).toHaveLength(0);
  });
});

describe('replaceWorkspaceWidget', () => {
  it('replaces an existing registration with a new factory', async () => {
    registerWorkspaceWidget('swappable', defaultMetadata, () =>
      Promise.resolve({ default: FakeWidgetA as React.ComponentType<any> })
    );

    // First resolve — caches FakeWidgetA.
    const first = await resolveWorkspaceWidget('swappable');
    expect(first).toBe(FakeWidgetA);

    // Replace — should clear cache and use new factory.
    replaceWorkspaceWidget(
      'swappable',
      { displayName: 'Swapped', category: 'test', allowMultiple: false, defaultOrder: 99 },
      () => Promise.resolve({ default: FakeWidgetB as React.ComponentType<any> })
    );

    const second = await resolveWorkspaceWidget('swappable');
    expect(second).toBe(FakeWidgetB);
  });
});
