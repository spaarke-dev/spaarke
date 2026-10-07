/**
 * Jest global setup for the SpaarkeGridCustomizer PCF (unified-access-control-r2 task 168 v1, verifier item 2).
 *
 * index.ts imports `./generated/ManifestTypes`, which the PCF toolchain emits from ControlManifest.Input.xml
 * into the gitignored `generated/` folder. ts-jest type-checks every file it compiles through TypeScript's
 * own module resolution, and jest's `moduleNameMapper` does not apply there, so on a clean checkout the
 * suite failed with TS2307 and ran 0 tests until `npm run build:prod` had emitted the folder.
 *
 * This runs the toolchain's own generator, `pcf-scripts refreshTypes`, once before any test file is
 * compiled. It is offline and takes about a second (it reads the manifest and writes
 * generated/ManifestTypes.d.ts). The tests then type-check against the REAL manifest types, so there is
 * no hand-written stub to drift from the manifest. A failure stops the run.
 */
const { execFileSync } = require('child_process');
const fs = require('fs');
const path = require('path');

module.exports = async function globalSetup() {
  const packageDir = __dirname;
  const cli = require.resolve('pcf-scripts/bin/pcf-scripts.js', { paths: [packageDir] });
  try {
    execFileSync(process.execPath, [cli, 'refreshTypes'], {
      cwd: packageDir,
      stdio: 'pipe',
      // The tests make no network call: the toolchain's telemetry is opted out for this run.
      env: { ...process.env, PP_TOOLS_TELEMETRY_OPTOUT: 'true' },
    });
  } catch (error) {
    const out = [error.stdout, error.stderr].filter(Boolean).map(String).join('\n');
    throw new Error(
      `pcf-scripts refreshTypes failed, so the tests cannot type-check index.ts:\n${out || error.message}`
    );
  }
  const generated = path.join(packageDir, 'generated', 'ManifestTypes.d.ts');
  if (!fs.existsSync(generated)) {
    throw new Error(`pcf-scripts refreshTypes succeeded but did not emit ${generated}`);
  }
};
