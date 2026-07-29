# Task 021 — Attachments Layer-1 extraction (`AttachmentList` core + adapters)

**Status:** ✅ complete · **Rigor:** FULL (sonnet/high) · **Date:** 2026-07-28
**Spec:** FR-12 (attachments: promote list; inline images filtered; click → `RichFilePreviewDialog`) + FR-18 (two-layer extraction) · NFR-04 (regression-free) · NFR-05 (React-agnostic adapters + no React-18/19-only API / no `as React.ComponentType` cast) · design Lens 4

---

## What shipped

Promoted the `CommunicationAttachments` PCF's PCF-free presentational core (`AttachmentList.tsx`) and its
two Layer-1 adapters (`CommunicationAttachmentsService.ts`, `AttachmentApiService.ts`) — plus the
`types.ts` contracts they share — into `@spaarke/communication-components`, mirroring task 020's
`logic/connections` scaffold. The PCF's React-16 `CommunicationAttachmentsApp.tsx` view is **unchanged**
— only its import sources moved (local → `@spaarke/communication-components`).

Verbatim copy verified: `diff` against the pre-move files shows ONLY the doc-comment header (task
attribution) and the relative import path (`../types` → `./types`, since `types.ts` now sits alongside
the service inside `logic/attachments/`) differ — zero logic changes.

## New shared-lib layout

```
src/client/shared/Spaarke.Communication.Components/src/
  logic/
    index.ts                            ← now also `export * from './attachments'`
    attachments/
      types.ts                          ← AttachmentType/IAttachmentItem/IAttachmentRecord/IDocumentRecord
      CommunicationAttachmentsService.ts ← host webAPI read + pure helpers (cleanGuid, projectAttachmentRecord,
                                            isEmailMessageAttachment, fileTypeLabel, filterFileAttachments,
                                            isDocumentUploaded)
      AttachmentApiService.ts           ← authenticatedFetch BFF adapter (preview-url / open-links)
      index.ts                          ← export * from all three
  components/                            ← NEW top-level folder (first Layer-2 shared component in this pkg)
    index.ts                            ← export * from './AttachmentList'
    AttachmentList/
      AttachmentList.tsx                ← the promoted presentational core (React.FC, Fluent v9 only)
      index.ts                          ← export * from './AttachmentList'
  index.ts                               ← now also `export * from './components'` (after ./widgets, ./logic)
```

## Barrel + exports contract (extends 020's scaffold; unchanged shape)

```jsonc
"./logic/attachments":              "./src/logic/attachments/index.ts",
"./components":                     "./src/components/index.ts",
"./components/AttachmentList":      "./src/components/AttachmentList/index.ts"
```

Appended to `src/logic/index.ts` (`export * from './attachments'`) and `src/index.ts` (`export * from
'./components'`) — the connections export from 020 is untouched; no barrel edits collided (022 was not
concurrently editing `src/index.ts`/`package.json` at the time this task ran).

## PCF consumption pattern

- PCF deep-imports both subpaths: `@spaarke/communication-components/logic/attachments` and
  `@spaarke/communication-components/components/AttachmentList` — NOT the root barrel (same rationale as
  020: avoids pulling `./widgets` → `@microsoft/signalr` + React-19 widget graph into the React-16 PCF).
- Three resolvers wired (mirrors 020's three-resolver list): PCF `tsconfig.json` `paths` (added two
  granular entries + the `@spaarke/communication-components/*` wildcard), `package.json` `exports` map
  (webpack + node runtime), PCF `jest.config.js` `moduleNameMapper` (deterministic test resolution to
  source, no dist/ build required).
- PCF `package.json` gained `"@spaarke/communication-components": "file:../../shared/Spaarke.Communication.Components"`.
- Deleted local duplicates: `CommunicationAttachments/AttachmentList.tsx`, `CommunicationAttachments/types.ts`,
  `CommunicationAttachments/services/{CommunicationAttachmentsService,AttachmentApiService}.ts` (the
  `services/` folder is now empty). `CommunicationAttachmentsApp.tsx`'s `RichFilePreviewDialog` deep-import
  from `@spaarke/ui-components/dist/...` is untouched (reused as-is, not re-homed).

## A NEW wrinkle vs. 020: this is the first Layer-2 REACT COMPONENT lift in this package (not just pure-TS logic)

Task 020's two extracted modules (`provenance.ts`, `ConnectionsWriteHandler.ts`) have **zero** `import
... from 'react'` — so 020 never exercised the PCF↔shared-lib React-version boundary at the *component*
layer. `AttachmentList.tsx` is a real `React.FC` with JSX, and `Spaarke.Communication.Components`'s own
`devDependencies` pin `@types/react@19` / `react@19` (it's consumed by React-19 code pages too) — the
exact ADR-022 "Shared-Library React-Version Drift" setup.

**Build-time (tsc + webpack): clean, no `TS2786`, no cast needed.** `CommunicationAttachments/tsconfig.json`
already carried a `paths` remap pinning `react`/`react-dom` to the PCF's own `@types/react@16` for the
whole compilation unit (the SAME mechanism task 023 documented for `TrackingFieldTrio` — this PCF just
already had it from its original scaffold, pre-dating this task). That remap is why deep-importing a JSX
source file from a `@types/react@19`-typed sibling package type-checked clean here with zero cast — worth
re-confirming on any future PCF lift of a JSX-returning shared component (check for this remap before
reaching for `as React.ComponentType`, per task 023's note).

**Runtime under Jest: NOT automatically clean — required a new fix.** TypeScript's `paths` remap is
compile-time only; it does not affect Node's `require()` resolution that Jest uses. Left unmapped, Jest
resolved `AttachmentList.tsx`'s `import * as React from 'react'` against
`Spaarke.Communication.Components/node_modules/react` (v19) — a SECOND React module instance from the one
`react-dom`/`@testing-library/react` use in the PCF's own tree (v16) — breaking hooks (`useContext` on
null: classic "two copies of React" symptom). At real PCF runtime this can't happen: webpack externalizes
`react`/`react-dom` to the single Dataverse platform library (`external "Reactv16"` in the build stats) for
every module in the bundle, so there is only ever one React instance. **Fix**: added explicit
`moduleNameMapper` entries in `CommunicationAttachments/jest.config.js` forcing `^react$` / `^react-dom$` /
`^react/jsx-runtime$` to the PCF's own `node_modules` copy — the Jest-time equivalent of webpack's externals
aliasing. Test-config only; no production code changed. **Flag for any future task that Jest-tests a PCF
importing a JSX-returning module from a sibling shared package with a different pinned React major** — this
mapping (or the `tsconfig` remap task 023 documents, whichever the consuming PCF already carries) may be
needed; the two fixes solve different problems (compile-time types vs. runtime module identity) and can
both be required simultaneously.

## Correctness invariants held

- **NFR-05 (React-agnostic adapters):** grep-proven — none of `types.ts` / `CommunicationAttachmentsService.ts`
  / `AttachmentApiService.ts` contain `from 'react'`. The promoted `AttachmentList` uses only `React.FC` +
  standard event types — no `createRoot`/`hydrateRoot`/`use()`/Actions, no `as React.ComponentType` cast.
- **NFR-04 (regression-free):** PCF view untouched (rendering, inline-image filtering via
  `filterFileAttachments`, row-click → `RichFilePreviewDialog` all identical); local duplicates deleted;
  `build:prod` green; 40/40 PCF tests pass (incl. the reused `richFilePreviewDialog` + `spaarkeAuth` mocks).
- **ADR-028 (auth):** `AttachmentApiService` calls only via `authenticatedFetch`; grep confirms no raw
  `fetch(` to the document endpoints, no client-supplied identity.
- **§11 (reuse):** row-click still opens the REUSED `RichFilePreviewDialog` from `@spaarke/ui-components`
  — no forked preview dialog; import unchanged.

## Verification

| Check | Result |
|---|---|
| `@spaarke/communication-components` `npm run build` (tsc) | ✅ green |
| PCF `npm install --legacy-peer-deps --no-audit --no-fund` | ✅ (new `@spaarke/communication-components` dep linked) |
| PCF `npm run build:prod` (pcf-scripts / webpack prod) | ✅ green (440 KiB bundle-size perf warnings only, pre-existing — same MSAL/react-jsx-runtime footprint as before this task) |
| PCF `npx jest` | ✅ 40/40 pass (3 suites: `AttachmentList`, `attachments-service`, `CommunicationAttachmentsApp`) |
| code-review | ✅ clean (0 Critical / 0 Warning) |
| adr-check | ✅ clean (0 violations — ADR-022, ADR-012, ADR-028) |

## Notes for 022 / future barrel editors

- Barrel (`src/index.ts`, `src/logic/index.ts`, `package.json` exports) was edited additively — the
  connections (020) exports are untouched. No collision encountered this run; still coordinate/rebase if
  022 lands concurrently (per the task's `<parallel-reason>`).
- For task 034 (Phase-3 code-page reading-pane attachments view): import via the React-19 barrel path
  `import { AttachmentList, type IAttachmentListProps } from '@spaarke/communication-components';` (or the
  `/components/AttachmentList` subpath) — no cast, no jest react-dedup mapping needed on that side, since
  Code Pages use the shared lib's native React 19 types directly (mirrors task 023's guidance for its own
  future consumer).
