/**
 * Test stand-in for `@spaarke/ui-components` (mapped in `jest.config.cjs`). Re-exports the REAL helper the page logic
 * calls, from its dependency-free source file, so the tests exercise production code without loading the whole
 * component barrel. Everything else the page uses is passed in as a dependency and faked per test.
 */
export { cleanGuid } from '../../../../client/shared/Spaarke.UI.Components/src/utils/guid';
