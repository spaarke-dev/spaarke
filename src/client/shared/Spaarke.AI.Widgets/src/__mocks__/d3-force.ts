/**
 * d3-force test stub — installed via jest.config.ts moduleNameMapper.
 *
 * d3-force ships pure ESM and ts-jest's CommonJS transform can't consume it,
 * so any transitive import (notably @spaarke/ui-components/hooks/useForceSimulation)
 * crashes jsdom test runs with `Unexpected token 'export'`. This module replaces
 * the real d3-force at the resolver level so the Spaarke.AI.Widgets test suite
 * can run any component that transitively imports the @spaarke/ui-components
 * barrel (register-context-widgets.ts / register-workspace-widgets.ts / the
 * ContextWidgetRegistry + WorkspaceWidgetRegistry all pull it in).
 *
 * The stub returns chainable-API shims for the d3-force functions used by
 * useForceSimulation; tests that DEPEND on real force-graph behaviour (none
 * in this package) should mock these per-test instead.
 *
 * Mirrors src/solutions/SpaarkeAi/src/__mocks__/d3-force.ts verbatim (R5 task
 * 038 origin). Added by ai-architecture-redesign-r2 task 021 (test-repair) —
 * this package transitively imports @spaarke/ui-components the same way
 * SpaarkeAi does but was missing the stub mapping SpaarkeAi already had.
 */

type Chainable = (...args: unknown[]) => Chainable;
const chainable: Chainable = function chainable(): Chainable {
  return chainable;
};

const simulationStub = {
  nodes: chainable,
  force: chainable,
  on: chainable,
  stop: chainable,
  alpha: chainable,
  alphaTarget: chainable,
  alphaDecay: chainable,
  velocityDecay: chainable,
  tick: () => {},
  restart: () => simulationStub,
};

export const forceSimulation = (): typeof simulationStub => simulationStub;
export const forceLink = (): { id: Chainable; distance: Chainable; strength: Chainable } => ({
  id: chainable,
  distance: chainable,
  strength: chainable,
});
export const forceManyBody = (): { strength: Chainable } => ({ strength: chainable });
export const forceCenter = (): unknown => chainable;
export const forceCollide = (): { radius: Chainable; strength: Chainable } => ({
  radius: chainable,
  strength: chainable,
});
export const forceX = (): { strength: Chainable; x: Chainable } => ({ strength: chainable, x: chainable });
export const forceY = (): { strength: Chainable; y: Chainable } => ({ strength: chainable, y: chainable });
export const forceRadial = (): { strength: Chainable; radius: Chainable } => ({
  strength: chainable,
  radius: chainable,
});

export default {
  forceSimulation,
  forceLink,
  forceManyBody,
  forceCenter,
  forceCollide,
  forceX,
  forceY,
  forceRadial,
};
