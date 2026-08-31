/**
 * DEV-ONLY Vite config for the nine-screen render review (sdap-SPE-admin-app-r2).
 *
 *     npx vite --config vite.review.config.ts
 *
 * ⚠️ NEVER used by `npm run build`. It reuses the production config verbatim and changes exactly one
 * thing: `src/services/authInit.ts` is swapped for `dev-review/authInit.mock.ts`, so the app's single
 * data choke point serves fixtures instead of calling MSAL and the BFF.
 *
 * Everything else is the REAL app — real `App.tsx` routing, real `AppShell`, real nine screens, real
 * shared-library resolution via the production config's `resolveSharedLibDeps` plugin.
 *
 * See `dev-review/authInit.mock.ts` for what this can and cannot establish.
 */

import path from "path";
import { fileURLToPath } from "url";
import type { Plugin, UserConfig } from "vite";

import baseConfig from "./vite.config";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const norm = (p: string) => p.replace(/\\/g, "/");

/**
 * Swap `src/services/authInit.ts` for the review mock.
 *
 * Done as a resolver plugin rather than a `resolve.alias` entry because aliases match the IMPORT
 * SPECIFIER, and every consumer imports it relatively (`./authInit`, `../services/authInit`). Those
 * strings differ per file, so there is no single specifier to alias. Resolving first and comparing
 * the resulting absolute path catches all of them regardless of how they spell it.
 */
function swapAuthInitForReview(): Plugin {
  const real = norm(path.resolve(__dirname, "src/services/authInit.ts"));
  const mock = norm(path.resolve(__dirname, "dev-review/authInit.mock.ts"));

  return {
    name: "review-swap-authinit",
    enforce: "pre",
    async resolveId(source, importer, options) {
      if (!importer) return null;
      // skipSelf prevents this hook recursing into itself.
      const resolved = await this.resolve(source, importer, { ...options, skipSelf: true });
      if (!resolved) return null;
      if (norm(resolved.id) === real) {
        return mock;
      }
      return null;
    },
  };
}

const base = baseConfig as UserConfig;

export default {
  ...base,
  plugins: [swapAuthInitForReview(), ...((base.plugins ?? []) as Plugin[])],
  server: {
    /*
     * Bind all interfaces, NOT the default.
     *
     * Vite's default binding came up on the IPv6 loopback only (`[::1]:5178`), so `curl` succeeded
     * over IPv6 while the browser — resolving `localhost` to 127.0.0.1 on Windows — got connection
     * refused. `host: true` binds 0.0.0.0 so IPv4 loopback works too.
     */
    host: true,
    port: 5178,
    strictPort: true,
    open: false,
  },
} satisfies UserConfig;
