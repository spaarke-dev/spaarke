/**
 * d3-force test stub — installed via jest.config.ts moduleNameMapper.
 *
 * d3-force ships pure ESM and ts-jest's CommonJS transform can't consume it,
 * so any transitive import (notably @spaarke/ui-components/hooks/useForceSimulation)
 * crashes jsdom test runs with `Unexpected token 'export'`. This module replaces
 * the real d3-force at the resolver level so the wizard's test suite can run
 * any component that transitively imports the @spaarke/ui-components barrel
 * (e.g. ArrangeStep → @spaarke/ui-components → hooks/useForceSimulation).
 *
 * The stub returns chainable-API shims for the d3-force functions used by
 * useForceSimulation; tests that DEPEND on real force-graph behavior (none in
 * the wizard) should mock these per-test instead.
 *
 * Mirrors SpaarkeAi/src/__mocks__/d3-force.ts — DEF-001 wiring for
 * spaarke-dataset-grid-framework-r2.
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
