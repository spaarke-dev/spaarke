/**
 * register-execution-trace-widget — unit tests
 *
 * Covers the R6 task 062 (D-C-15) registration: ExecutionTraceWidget is
 * registered with ContextWidgetRegistry under the 'execution-trace' type
 * key by `src/registry/register-context-widgets.ts`. The widget itself
 * is owned by R6 task 061 (D-C-14); this test ONLY validates the
 * registry-side wiring.
 *
 * Why test against the registry/register-context-widgets.ts file rather
 * than the barrel (src/index.ts):
 *   - The barrel transitively imports `@spaarke/ui-components` which now
 *     exports `useForceSimulation` (d3-force ESM) — that import currently
 *     fails the Jest ts-jest transform on the parallel branch. The
 *     registry-side file uses a narrow `safeRegister` import which we
 *     mock below to keep this test isolated and fast.
 *   - The barrel and the registry-side file BOTH register the widget
 *     (idempotent first-wins per `ContextWidgetRegistry`). Either site
 *     guarantees the shell can resolve the widget. Testing one is
 *     sufficient for the acceptance criteria.
 *
 * Task: R6-062 (D-C-15).
 */

// Mock @spaarke/ui-components to a minimal surface — the only symbol the
// registry-side file uses is `safeRegister`. We pass the registration call
// straight through (no error trapping) so behaviour matches production for
// the happy path under test.
jest.mock(
  '@spaarke/ui-components',
  () => ({
    safeRegister: (_kind: string, _type: string, fn: () => void): void => {
      fn();
    },
  }),
  { virtual: false }
);

import {
  clearContextRegistry,
  getAllContextWidgetTypes,
  hasContextWidget,
  resolveContextWidget,
} from '../ContextWidgetRegistry';

// ---------------------------------------------------------------------------
// Setup / teardown
// ---------------------------------------------------------------------------

beforeEach(() => {
  clearContextRegistry();
  jest.resetModules();
});

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('register-context-widgets — execution-trace (R6 task 062)', () => {
  it('registers the "execution-trace" widget type at module load', () => {
    // Importing the registry-side file triggers the top-level side-effect
    // registrations. We `require` so the mocked `safeRegister` is in scope.
    require('../register-context-widgets');

    expect(hasContextWidget('execution-trace')).toBe(true);
  });

  it('resolves "execution-trace" to a non-null component (lazy factory loads)', async () => {
    require('../register-context-widgets');

    const component = await resolveContextWidget('execution-trace');

    expect(component).not.toBeNull();
    expect(typeof component).toBe('function');
  });

  it('is included in the full list of registered context widget types', () => {
    require('../register-context-widgets');

    const types = getAllContextWidgetTypes();

    expect(types).toContain('execution-trace');
  });

  it('registers alongside the other side-effect context widgets (no displacement)', () => {
    require('../register-context-widgets');

    const types = getAllContextWidgetTypes();

    // The registry-side file registers 6 widgets after task 062:
    //   progress-tracker, playbook-gallery, entity-info, findings,
    //   file-preview, execution-trace.
    // We assert presence rather than exact length so future additive
    // registrations do not flake this test.
    expect(types).toEqual(
      expect.arrayContaining([
        'progress-tracker',
        'playbook-gallery',
        'entity-info',
        'findings',
        'file-preview',
        'execution-trace',
      ])
    );
  });

  it('idempotent: re-requiring the module does not throw or duplicate', () => {
    require('../register-context-widgets');
    const firstCount = getAllContextWidgetTypes().length;

    // jest.resetModules() in beforeEach already isolates each test; for
    // belt-and-braces verify a duplicate require inside the same test is
    // also a no-op (registry is first-wins).
    require('../register-context-widgets');
    const secondCount = getAllContextWidgetTypes().length;

    expect(secondCount).toBe(firstCount);
    expect(hasContextWidget('execution-trace')).toBe(true);
  });
});
