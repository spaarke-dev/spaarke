/**
 * The two `@spaarke/ui-components` modules this pane's services import, at their source, for the node jest harness
 * (jest.config.cjs maps `@spaarke/ui-components` here):
 *  - the BFF child-write seam (`splitFilingPayload`, the shared filing rule) — task 147 r1c-v1;
 *  - the ONE `cleanGuid` (`utils/guid`) — master #1121 converged the pane's id cleaning on it.
 * Neither needs the built library or Fluent. Sweep integration (147 x master 400cda274).
 */
export * from '../../../../client/shared/Spaarke.UI.Components/src/utils/adapters/bffChildWriteAdapter';
export { cleanGuid } from '../../../../client/shared/Spaarke.UI.Components/src/utils/guid';
