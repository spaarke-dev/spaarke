---
name: spaarke-pcf-client-quality-eslint-2026-06
description: Spaarke CI Client Quality job; root src/client/pcf/package.json lacks eslint as direct devDep; npm install --legacy-peer-deps skips peers; npm ci installs the full lockfile tree including peer:true entries; switching from npm ci to npm install --legacy-peer-deps in CI broke ESLint resolution.
metadata:
  type: project
---

Spaarke `src/client/pcf/package.json` declares ESLint plugins (`@eslint/js`, `@microsoft/eslint-plugin-power-apps`, `eslint-plugin-promise`, `eslint-plugin-react`, `eslint-plugin-react-hooks`, `typescript-eslint`) but NOT `eslint` itself as a direct devDep.

The lockfile (`src/client/pcf/package-lock.json` ~line 5017–5077) records `"eslint": { "version": "9.39.4", "dev": true, "peer": true, ... }`. The `"peer": true` flag is critical.

**Behavior difference:**
- `npm ci`: installs the full lockfile tree, including `peer: true` entries → eslint binary at `node_modules/.bin/eslint` and `node_modules/eslint/package.json` both exist → `npx eslint .` finds local copy → ESLint plugins (e.g. `eslint-plugin-react/lib/util/message.js` line 4: `require('eslint/package.json')`) resolve correctly. Green run Jun 12 added 665 packages.
- `npm install --legacy-peer-deps`: skips peer deps entirely (npm v7+ flag restores npm v6 behavior of ignoring peers) → eslint NOT installed → `npx eslint .` falls back to fetching latest eslint (10.5.0) via npx ad-hoc → npx's temp install isn't at the local resolution path → `eslint-plugin-react`'s `require('eslint/package.json')` fails with `Cannot find module 'eslint/package.json'`. Failed run Jun 18 added only 590 packages (75-package gap = eslint + transitives).

**Why:** PR #392 commit 7b3904078 switched both CI install steps in `.github/workflows/sdap-ci.yml` (lines 177, 203) from `npm ci --ignore-scripts` to `npm install --legacy-peer-deps --no-audit --no-fund --ignore-scripts` to handle lockfile drift, but the change exposed the latent missing-eslint-devDep bug because the root PCF package only relied on `npm ci`'s full-tree-install behavior to pull in the peer.

**How to apply:** The systemic fix is to add `"eslint": "^9.39.4"` to `src/client/pcf/package.json` devDependencies and regenerate the lockfile. This is the convention used by 12 of 14 per-PCF subdirs (RegardingResolver, EmailProcessingMonitor, etc.) and by `Spaarke.UI.Components` + `Spaarke.Auth`. Mirrors the [[knowledge-base-layout]] pattern of explicit declarations.

Related (but lower-priority) fragility: the `npx eslint .` step silently auto-installs latest eslint when local one is missing (printing only `npm warn`) — this should ideally use `--no-install` or `node_modules/.bin/eslint` to force local resolution and fail loud instead of fetching a mismatched version.

References:
- `src/client/pcf/package.json` (lines 15–28 — devDeps with no eslint)
- `src/client/pcf/package-lock.json` (lines 5017–5077 — eslint marked peer: true)
- `.github/workflows/sdap-ci.yml` (lines 197–211 — Install PCF deps + ESLint check steps)
- Green run logs (job 81093366981, 2026-06-12): `npm ci` added 665 packages
- Failed run logs (job 82180096873, 2026-06-18): `npm install --legacy-peer-deps` added 590 packages, ESLint fell back to 10.5.0 from registry
