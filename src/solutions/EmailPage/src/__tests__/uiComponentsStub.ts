/**
 * Test stand-in for `@spaarke/ui-components` (mapped in `jest.config.cjs`). Re-exports the REAL helpers the launch
 * logic calls, from their source files, so the tests exercise production code (including the "Related to" parser the
 * secure-child ribbon and `openEmailCompose` write for) without loading the whole component barrel.
 */
export { cleanGuid } from '../../../../client/shared/Spaarke.UI.Components/src/utils/guid';
export {
  composeRegardingParams,
  parseComposeRegarding,
} from '../../../../client/shared/Spaarke.UI.Components/src/components/EmailComposer/openEmailCompose';
