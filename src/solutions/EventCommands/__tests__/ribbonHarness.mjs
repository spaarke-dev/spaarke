// Loads the REAL sprk_event_ribbon_commands.js web resource into an isolated VM context with an injected `Xrm` and an
// optional frozen "now" — so its command functions run exactly as shipped, without a browser.
// spaarke-ontology-platform-r1 task 098. Used by ribbonDates.test.mjs (unit) and the task's live proof (a real Xrm.WebApi
// shim against spaarkedev1).
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import vm from 'node:vm';

const SCRIPT = join(dirname(fileURLToPath(import.meta.url)), '..', 'sprk_event_ribbon_commands.js');

/**
 * @param {object} xrm   The Xrm double (WebApi / Navigation / App / Utility as the tested path needs).
 * @param {Date} [now]   Frozen "now" for `new Date()` with no arguments (local time of the process TZ).
 * @returns {{ Spaarke: any }}
 */
export function loadRibbon(xrm, now) {
  const RealDate = Date;
  const FrozenDate = now
    ? class extends RealDate {
        constructor(...args) {
          super(...(args.length === 0 ? [now.getTime()] : args));
        }
        static now() {
          return now.getTime();
        }
      }
    : RealDate;
  const context = vm.createContext({
    Xrm: xrm,
    Date: FrozenDate,
    console,
    Promise,
    JSON,
    String,
    Number,
    Math,
    isNaN,
    window: { addEventListener() {}, removeEventListener() {} },
    location: { reload() {} },
    prompt: xrm.__prompt ?? (() => null),
  });
  vm.runInContext(readFileSync(SCRIPT, 'utf8'), context, { filename: SCRIPT });
  return { Spaarke: context.Spaarke };
}
