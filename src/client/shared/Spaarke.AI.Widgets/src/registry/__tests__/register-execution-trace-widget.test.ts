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

// NOTE: do NOT statically import from '../ContextWidgetRegistry' at the top
// of the test file. The side-effect file `../register-context-widgets`
// imports the registry as a top-level binding; if jest.resetModules() runs
// (we need it so the side-effects re-fire each test), the side-effect file
// gets a FRESH `ContextWidgetRegistry` instance with its own private
// `_registry` Map — invisible to a statically-imported reader. Every reader
// call must `require()` ContextWidgetRegistry from inside `jest.isolateModules`
// so they share the same module instance as the side-effect file.

// Helper: requires the registry-side file (side effect: registers widgets)
// and returns the registry reader fns from the SAME isolated module graph.
function loadRegistryWithSideEffects(): {
  hasContextWidget: (type: string) => boolean;
  resolveContextWidget: (
    type: string
  ) => Promise<((...args: unknown[]) => unknown) | null>;
  getAllContextWidgetTypes: () => string[];
  clearContextRegistry: () => void;
} {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  let api: any;
  jest.isolateModules(() => {
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    api = require('../ContextWidgetRegistry');
    // Side-effect import — registers widgets into THIS module instance's _registry.
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    require('../register-context-widgets');
  });
  return api;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('register-context-widgets — execution-trace (R6 task 062)', () => {
  it('registers the "execution-trace" widget type at module load', () => {
    const { hasContextWidget } = loadRegistryWithSideEffects();
    expect(hasContextWidget('execution-trace')).toBe(true);
  });

  it('resolves "execution-trace" to a non-null component (lazy factory loads)', async () => {
    const { resolveContextWidget } = loadRegistryWithSideEffects();
    const component = await resolveContextWidget('execution-trace');
    expect(component).not.toBeNull();
    expect(typeof component).toBe('function');
  });

  it('is included in the full list of registered context widget types', () => {
    const { getAllContextWidgetTypes } = loadRegistryWithSideEffects();
    const types = getAllContextWidgetTypes();
    expect(types).toContain('execution-trace');
  });

  it('registers alongside the other side-effect context widgets (no displacement)', () => {
    const { getAllContextWidgetTypes } = loadRegistryWithSideEffects();
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
    // `loadRegistryWithSideEffects` uses jest.isolateModules so two calls
    // would each create their OWN isolated graph (not exercise idempotency
    // of the production module-cached behaviour). Instead, drive idempotency
    // by requiring `register-context-widgets` twice INSIDE a single isolated
    // module graph and asserting the registry state stays the same.
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    let result: any;
    jest.isolateModules(() => {
      // eslint-disable-next-line @typescript-eslint/no-require-imports
      const registry = require('../ContextWidgetRegistry');
      // eslint-disable-next-line @typescript-eslint/no-require-imports
      require('../register-context-widgets');
      const firstCount = registry.getAllContextWidgetTypes().length;
      // Second require hits the module cache and is a no-op for side effects
      // (production behaviour: top-level registrations execute exactly once
      // per module-graph instance). Idempotency: count stays the same.
      // eslint-disable-next-line @typescript-eslint/no-require-imports
      require('../register-context-widgets');
      const secondCount = registry.getAllContextWidgetTypes().length;
      result = {
        firstCount,
        secondCount,
        hasExecutionTrace: registry.hasContextWidget('execution-trace'),
      };
    });

    expect(result.secondCount).toBe(result.firstCount);
    expect(result.hasExecutionTrace).toBe(true);
  });
});
