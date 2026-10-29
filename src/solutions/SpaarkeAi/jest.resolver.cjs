/**
 * Jest resolver for the SpaarkeAi solution: one copy of Fluent / Griffel /
 * tabster / keyborg per test run (GitHub #1963, part 2).
 *
 * Why: jest.config.ts maps `@spaarke/ui-components` (and the other shared
 * libraries) to their `src/` trees, so those files are loaded from where they
 * live and their bare imports resolve from THAT package's node_modules. When a
 * developer has also run `npm install` in e.g. Spaarke.UI.Components, tests
 * load two copies of the Fluent stack: FluentProvider (SpaarkeAi's copy) creates
 * the tabster core, while a deep-imported component (UI.Components' copy) looks
 * its modules up in a different tabster instance, and fails with
 * `TypeError: Cannot read properties of undefined (reading 'set')` at
 * tabster getModalizer.cjs / getRestorer.cjs. It stays hidden while both copies happen to
 * be the same version and returns whenever they drift.
 *
 * What: for the packages in SINGLETON, a bare import made by any file OUTSIDE
 * this package's node_modules is resolved as if the import had been written in
 * a SpaarkeAi source file. The default resolver is still used, so package
 * `exports` maps and jest conditions behave exactly as before. If the package is
 * not installed here, the request falls back to normal resolution. Imports made
 * from inside this package's own node_modules are untouched, so nested copies
 * that npm placed there for version-conflict reasons keep working.
 *
 * This is deliberately a resolver, not `moduleNameMapper`: a mapper rewrites
 * every importer (including SpaarkeAi's own and Fluent's internal ones) to a
 * fixed path and bypasses the exports map.
 */
const path = require('path');

const SINGLETON = /^(?:@fluentui\/|@griffel\/|tabster(?:\/|$)|keyborg(?:\/|$))/;
const OWN_NODE_MODULES = path.join(__dirname, 'node_modules') + path.sep;
// A real file that lives in this package, used as the "importer" so that
// resolution walks SpaarkeAi's node_modules.
const OWN_BASEDIR = __dirname;

module.exports = (request, options) => {
  const importer = options.basedir + path.sep;
  if (SINGLETON.test(request) && !importer.startsWith(OWN_NODE_MODULES)) {
    try {
      return options.defaultResolver(request, { ...options, basedir: OWN_BASEDIR });
    } catch (_err) {
      // Not installed in SpaarkeAi: fall through to the importer's own copy.
    }
  }
  return options.defaultResolver(request, options);
};
