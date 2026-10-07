/**
 * Test-local stand-in for `@spaarke/ui-components`, mapped in `jest.config.js`
 * ONLY for this PCF's test run.
 *
 * Why not the real package: its published `dist/index.js` is ESM (`export *`)
 * and this PCF's `ts-jest` transform does not cover it, so a runtime import
 * throws `SyntaxError: Unexpected token 'export'`; and mapping to the real
 * `src/index.ts` would pull the whole component library (and its ESM-only
 * dependencies) into a suite that tests three pure helpers.
 *
 * So each export the code under test uses is mapped to its REAL source module
 * (no re-implementation), and any OTHER export throws a clear error the moment
 * it is read: a future test that reaches a new export fails loudly here
 * instead of silently receiving `undefined` (task 081 round 3, review L6).
 * To add one: map it below from the real source file that defines it.
 *
 * Plain CommonJS on purpose: it replaces `module.exports` with a Proxy, which
 * TypeScript can only express as `export =` — and that breaks this PCF's own
 * `tsc` / `pcf-scripts build` (TS1203), since the PCF project compiles every
 * `.ts` file it can see. Type-only imports (e.g. `IAttachmentItem`) are erased
 * by TypeScript and never reach this module; type-checking resolves the real
 * package.
 */

/* eslint-disable @typescript-eslint/no-require-imports */
const REAL_EXPORTS = {
  // ES-module marker so `import * as ui` interop returns THIS proxy rather than
  // a copy of its own keys (a copy would turn un-mapped reads back into undefined).
  __esModule: true,
  // `@spaarke/communication-components` `logic/actions/launchCreate.ts`
  getXrm: require('../../../shared/Spaarke.UI.Components/src/utils/xrmContext').getXrm,
};

/** Keys module-interop machinery probes; reading them must not throw. */
const INTEROP_KEYS = new Set(['__esModule', 'default', 'then', 'toJSON', '$$typeof', 'constructor']);

module.exports = new Proxy(REAL_EXPORTS, {
  get(target, prop) {
    if (typeof prop === 'symbol' || INTEROP_KEYS.has(prop)) return target[prop];
    if (Object.prototype.hasOwnProperty.call(target, prop)) return target[prop];
    throw new Error(
      `[CommunicationActions jest] '@spaarke/ui-components' export '${String(prop)}' is not mapped in ` +
        'test-mocks/spaarke-ui-components.js. Map it from its real source module there; ' +
        'the stand-in throws instead of returning undefined.'
    );
  },
});
