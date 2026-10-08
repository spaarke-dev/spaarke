/**
 * jsdom test environment that runs its test file in America/New_York (spaarke-ontology-platform-r1 task 098).
 *
 * Why an environment and not `process.env.TZ = ...` at the top of the test file: Jest gives each test file a COPY of
 * `process.env`, so assigning TZ there never reaches Node and the file keeps the runner's zone. On a UTC CI runner a
 * "negative UTC offset" regression test then passes trivially, or fails its own guard. This constructor runs in the
 * worker's REAL process, where assigning `process.env.TZ` makes Node re-read the zone for every Date in the worker.
 *
 * Teardown puts the worker back in the zone it had before. When TZ was not set, it assigns the zone that was in effect
 * (captured before switching) rather than deleting TZ: on Windows, deleting TZ does not make Node re-read the system
 * zone, so the worker would stay in New York for every later test file.
 *
 * Use it from a docblock on the first line of a test file, with the path relative to that package's rootDir:
 *   - in this package:            @jest-environment ./jest.newYorkEnvironment.js
 *   - from another package (e.g.  @jest-environment ../../shared/Spaarke.UI.Components/jest.newYorkEnvironment.js
 *     VisualHost)
 * jest-environment-jsdom is resolved from the package Jest runs in (its cwd), so a package that uses this file needs
 * only its own jsdom environment installed, not this package's node_modules.
 */
const { TestEnvironment } = require(require.resolve('jest-environment-jsdom', { paths: [process.cwd(), __dirname] }));

class NewYorkJsdomEnvironment extends TestEnvironment {
  constructor(config, context) {
    super(config, context);
    this.previousTz = process.env.TZ;
    this.previousZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
    process.env.TZ = 'America/New_York';
  }

  async teardown() {
    // Never `delete process.env.TZ`: on Windows that leaves New York in effect. Assign what was in effect instead.
    process.env.TZ = this.previousTz !== undefined ? this.previousTz : this.previousZone;
    await super.teardown();
  }
}

module.exports = NewYorkJsdomEnvironment;
