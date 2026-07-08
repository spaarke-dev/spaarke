/**
 * register-context-widgets — unit tests (single-module registration)
 *
 * ai-architecture-redesign-r1 task 046 / FR-P3-07: the two drifted
 * registration modules were merged into ONE (`src/registry/
 * register-context-widgets.ts`) consumed by both mount paths (barrel +
 * direct entry points). These tests validate the merged module registers
 * the COMPLETE context-widget surface:
 *
 *   - the 8 shell context widgets (progress-tracker, playbook-gallery,
 *     get-started-cards, entity-info, findings, file-preview,
 *     execution-trace, pinned-memory-list), and
 *   - the 6 R1 source widgets (DocumentViewer, WebSource, LegalLibrary,
 *     Citation, ImageViewer, CodeViewer) formerly registered by the deleted
 *     `src/widgets/context/register-context-widgets.ts`.
 *
 * Uses the jest.isolateModules pattern from
 * `register-execution-trace-widget.test.ts` — the module registers via
 * top-level side effects, so each test loads a fresh isolated module graph.
 */

// Mock @spaarke/ui-components to a minimal surface — the only symbol the
// registration module uses is `safeRegister`. Pass-through so behaviour
// matches production for the happy path.
jest.mock(
  '@spaarke/ui-components',
  () => ({
    safeRegister: (_kind: string, _type: string, fn: () => void): void => {
      fn();
    },
  }),
  { virtual: false }
);

const SHELL_TYPES = [
  'progress-tracker',
  'playbook-gallery',
  'get-started-cards',
  'entity-info',
  'findings',
  'file-preview',
  'execution-trace',
  'pinned-memory-list',
] as const;

const SOURCE_TYPES = ['DocumentViewer', 'WebSource', 'LegalLibrary', 'Citation', 'ImageViewer', 'CodeViewer'] as const;

function loadRegistryWithSideEffects(): {
  hasContextWidget: (type: string) => boolean;
  resolveContextWidget: (type: string) => Promise<((...args: unknown[]) => unknown) | null>;
  getAllContextWidgetTypes: () => string[];
} {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  let api: any;
  jest.isolateModules(() => {
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    api = require('../ContextWidgetRegistry');
    // Side-effect import — registers ALL context widgets into THIS module
    // instance's registry.
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    require('../register-context-widgets');
  });
  return api;
}

describe('register-context-widgets — single registration module (task 046)', () => {
  it('registers all 8 shell context widget types', () => {
    const { hasContextWidget } = loadRegistryWithSideEffects();
    for (const type of SHELL_TYPES) {
      expect(hasContextWidget(type)).toBe(true);
    }
  });

  it('registers all 6 R1 source widget types (absorbed from the deleted duplicate module)', () => {
    const { hasContextWidget } = loadRegistryWithSideEffects();
    for (const type of SOURCE_TYPES) {
      expect(hasContextWidget(type)).toBe(true);
    }
  });

  it('registers exactly the 14 merged types (no extras, no drift)', () => {
    const { getAllContextWidgetTypes } = loadRegistryWithSideEffects();
    const types = getAllContextWidgetTypes();
    expect([...types].sort()).toEqual([...SHELL_TYPES, ...SOURCE_TYPES].sort());
  });

  it('resolves a shell widget (execution-trace) to a non-null component', async () => {
    const { resolveContextWidget } = loadRegistryWithSideEffects();
    const component = await resolveContextWidget('execution-trace');
    expect(component).not.toBeNull();
    expect(typeof component).toBe('function');
  });

  it('resolves a source widget (DocumentViewer) to a non-null component', async () => {
    const { resolveContextWidget } = loadRegistryWithSideEffects();
    const component = await resolveContextWidget('DocumentViewer');
    // memo/forwardRef components are exotic objects, plain FCs are functions -
    // accept either; null means the factory failed to load.
    expect(component).not.toBeNull();
    expect(['function', 'object']).toContain(typeof component);
  });

  it('is idempotent — re-requiring inside one module graph does not duplicate', () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    let result: any;
    jest.isolateModules(() => {
      // eslint-disable-next-line @typescript-eslint/no-require-imports
      const registry = require('../ContextWidgetRegistry');
      // eslint-disable-next-line @typescript-eslint/no-require-imports
      require('../register-context-widgets');
      const firstCount = registry.getAllContextWidgetTypes().length;
      // eslint-disable-next-line @typescript-eslint/no-require-imports
      require('../register-context-widgets');
      const secondCount = registry.getAllContextWidgetTypes().length;
      result = { firstCount, secondCount };
    });
    expect(result.secondCount).toBe(result.firstCount);
  });
});
