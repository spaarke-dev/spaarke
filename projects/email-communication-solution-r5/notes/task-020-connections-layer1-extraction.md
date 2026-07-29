# Task 020 — Connections Layer-1 extraction + shared-lib logic scaffold

**Status:** ✅ complete · **Rigor:** FULL (opus/xhigh) · **Date:** 2026-07-28
**Spec:** FR-13 + FR-18 (two-layer control extraction) · NFR-04 (regression-free) · NFR-05 (React-agnostic core) · design Lens 4

---

## What shipped

Extracted the **production** `CommunicationConnections` PCF's React-agnostic Layer-1 logic into
`@spaarke/communication-components` and repointed the PCF to consume it. The PCF's React-16
`ConnectionsEditor.tsx` view is **unchanged** — only its import source moved.

Source was the PRODUCTION PCF (`src/client/pcf/CommunicationConnections/...`), copied **byte-identical**
(`cp` + `diff -q` verified). The stale `code-pages/CommunicationPage/.../ConnectionsEditor.tsx|provenance.ts`
stub was **NOT** reused (design Lens 4). Grep confirms no r5 code imports the stub.

## New shared-lib layout (the scaffold 021/022 extend)

```
src/client/shared/Spaarke.Communication.Components/src/
  logic/
    index.ts                      ← re-exports ./connections  (add ./<domain> siblings here)
    connections/
      provenance.ts               ← pure TS, ZERO imports (parse/derive/group + type contracts)
      ConnectionsWriteHandler.ts  ← additive write path; only non-lib import is @spaarke/ui-components
      index.ts                    ← export * from both
  index.ts                        ← now also `export * from './logic'` (barrel, for React-19 consumers)
```

## Barrel + exports contract (MIRROR THIS in 021/022)

**`package.json` `exports`** — granular subpaths so PCF consumers deep-import only what they need
(avoids pulling the React-19 widgets/signalr graph). Added by 020:

```jsonc
"./logic":                          "./src/logic/index.ts",
"./logic/connections":              "./src/logic/connections/index.ts",
"./logic/connections/provenance":   "./src/logic/connections/provenance.ts"  // pure-provenance, no write handler
```

**Barrel `src/index.ts`** — `export * from './logic';` added AFTER `export * from './widgets';`.
Barrel edits SERIALIZE across P2 — 021/022 append their `./logic/<domain>` sub-barrels to `src/logic/index.ts`
and add matching `exports` subpaths.

## PCF consumption pattern (why deep-import, not the barrel)

- The barrel (`@spaarke/communication-components`) transitively pulls `./widgets`
  (`CommunicationsWorkspaceWidget`, `@microsoft/signalr`, React-19 types) — must NOT enter a React-16 PCF.
- PCF therefore **deep-imports** `@spaarke/communication-components/logic/connections`. Verified in
  `build:prod` webpack stats: only `@spaarke/ui-components/dist/*` + the logic subtree are bundled; no signalr.
- Three resolvers wired so TS + webpack + jest all agree:
  1. PCF `tsconfig.json` `paths` → shared `src/logic/connections/...` (ts-loader type resolution)
  2. `package.json` `exports` map (webpack 5 + node runtime, enforced)
  3. PCF `jest.config.js` `moduleNameMapper` → shared src (deterministic test resolution)
- PCF `package.json` gained `"@spaarke/communication-components": "file:../../shared/Spaarke.Communication.Components"`.

## Correctness invariants held

- **NFR-05 (React-agnostic):** grep-proven — neither extracted file contains `from 'react'`, a React runtime
  API, or an `as React.ComponentType` cast. Also no `ComponentFramework`/`@fluentui`/`PublicClientApplication`.
- **ADR-045 (additive write):** `applyRegardingSelection` moved verbatim — starts from an empty payload, delegates
  the SET to `applyResolverFields`, never clear-and-set. `unlinkRegarding` still nulls exactly one lookup.
- **NFR-04 (regression-free):** PCF view untouched; local duplicates deleted; `build:prod` green; 41/41 tests pass.

## Verification

| Check | Result |
|---|---|
| `@spaarke/communication-components` `npm run build` (tsc, decl-only) | ✅ green (emitted `dist/logic/connections/*.d.ts`) |
| PCF `npm run build:prod` (pcf-scripts / webpack prod) | ✅ green (perf size warnings only, pre-existing) |
| PCF `npx jest` | ✅ 41/41 pass (5 suites incl. ConnectionsWriteHandler + App additive-write) |
| adr-check | ✅ clean (0 violations) |
| code-review | ✅ clean (0 critical / 0 warning) |

## Notes for 021/022

- Append to `src/logic/index.ts` + add `exports` subpaths — do not create a competing top-level folder.
- Keep PCF consumers on the deep subpath; don't lean on the `@spaarke/communication-components/*` tsconfig wildcard.
- Run `/conflict-check` before the PR — `@spaarke/communication-components` is contended with messaging-r2/r3,
  notification-spine-r1, email-r4. This task edited the barrel `src/index.ts` + `package.json exports` — coordinate.
- One cosmetic follow-up: the extracted `provenance.ts` header comment still cites its historical CommunicationPage
  port origin (informational only — not a reuse). Refresh opportunistically.
