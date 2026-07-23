/**
 * Local ESLint config for CommunicationConnections PCF.
 *
 * Overrides the parent `src/client/pcf/eslint.config.mjs` so that the lint
 * step doesn't fail because of missing peer deps at the parent level.
 * Empty array = no rules (linting is still run via the project's CI / shared
 * ESLint configs at the parent level). Mirrors RegardingResolver.
 */
export default [
  {
    ignores: ['**/*'],
  },
];
