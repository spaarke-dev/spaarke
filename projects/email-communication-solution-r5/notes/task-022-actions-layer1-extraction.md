# Task 022 — Actions Layer-1 extraction (action-bar / composer-prefill / suggested-create logic)

**Status:** ✅ complete · **Rigor:** FULL (sonnet/high) · **Date:** 2026-07-28
**Spec:** FR-08 (full-width toolbar reusing extracted `CommunicationActionBar` logic) + FR-18 (two-layer extraction)
· NFR-04 (regression-free) · NFR-05 (React-agnostic; no `as React.ComponentType` cast on new code) · design Lens 4

---

## What shipped

Extracted the three discrete Layer-1 modules from the `CommunicationActions` PCF into
`@spaarke/communication-components`, mirroring task 020/021's `logic/<domain>` scaffold:

- `composerPrefill.ts` — `deriveComposerFields`, `splitRecipients`, `buildQuotedThread`, `ComposerMode`,
  `RecordPrefill`, `ComposerFields` (Reply/ReplyAll/Forward recipient + subject + quoted-thread derivation).
- `launchCreate.ts` — `launchCreate`, `CreateKind`, `CreateLaunchTarget`, `LaunchCreateOptions`,
  `CREATE_LAUNCH_TARGETS` (the OOB `Xrm.Navigation.navigateTo` create-from-email seam: event/todo/invoice).
- `attachmentsSource.ts` — `fetchSourceAttachments`, `IActionsWebApi`, `ISourceAttachmentRecord`,
  `buildDocumentLinkUrl`, `projectSourceAttachment`, `filterFileAttachments` (carry-forward attachments on
  reply/forward, injected host webAPI seam).

Verbatim copy verified: the three shared-lib files are byte-identical to the pre-move PCF files except for
the doc-comment header (unchanged actually — same header text carried over). The PCF's React-16
`CommunicationActionsApp.tsx` view is **unchanged** — only its import source for these three modules moved
(local → `@spaarke/communication-components/logic/actions`); rendering, action dispatch, and the
`SendEmailPageR16` cast (a pre-existing React 16/19 type seam, NOT touched by this task — confirmed via
`git diff` showing only the import block changed) are untouched.

## New shared-lib layout

```
src/client/shared/Spaarke.Communication.Components/src/
  logic/
    index.ts                 ← now also re-exports `./actions` (explicit named re-export, see collision note below)
    actions/
      composerPrefill.ts      ← pure recipient/subject/quoted-thread derivation
      launchCreate.ts         ← OOB Xrm.Navigation.navigateTo create-from-email seam
      attachmentsSource.ts    ← injected-webApi carry-forward attachment enumeration
      index.ts                ← export * from all three (verbatim, full export list)
```

## Barrel + exports contract

```jsonc
"./logic/actions": "./src/logic/actions/index.ts"
```

Added to `package.json` `exports` (needed for webpack/Node deep-import resolution — confirmed necessary by
inspecting the already-installed `@spaarke/communication-components` copy in a sibling PCF's `node_modules`,
which enforces the package's `exports` map; TypeScript `paths` alone does not bypass it for the actual
bundler resolution). Added by explicit, minimal, additive single-key edit — same treatment 020/021 already
gave this file for their own subpaths.

## ⚠️ Barrel-collision found and resolved: `filterFileAttachments` name clash

`logic/attachments/CommunicationAttachmentsService.ts` (task 021) and this task's
`logic/actions/attachmentsSource.ts` (task 022) BOTH independently export a function named
`filterFileAttachments` — different row shapes (`IAttachmentItem` vs `ISourceAttachmentRecord`), same name,
both legitimate (attachment-list inline-image filter vs. source-communication carry-forward filter).

`export * from './connections'; export * from './attachments'; export * from './actions';` in
`src/logic/index.ts` failed **`TS2308`** ("Module './actions' has already exported a member named
'filterFileAttachments'... Consider explicitly re-exporting to resolve the ambiguity") — the package builds
with `emitDeclarationOnly: true` + `isolatedModules: true`, and TS's declaration emit cannot silently drop
the ambiguous `export *` member the way plain JS module resolution would.

**Fix** (in `src/logic/index.ts` only — within this task's edit scope): replaced the blanket
`export * from './actions'` with explicit named re-exports, renaming ONLY the colliding symbol at this one
aggregate barrel: `filterFileAttachments as filterActionsFileAttachments`. Everything else re-exports
unrenamed. This means:

- `@spaarke/communication-components/logic/actions` (the subpath the PCF + its tests import) — **fully
  verbatim**, `filterFileAttachments` unrenamed, zero behavior change.
- `@spaarke/communication-components/logic` or `@spaarke/communication-components` (the flattened
  aggregate barrel, consumed by future React-19 code-page work) — sees `filterActionsFileAttachments` for
  this module's filter (to disambiguate from `filterFileAttachments` from `./attachments`).

**Flag for task 034/036 (Phase-3 consumers)**: if importing the actions filter via the flattened barrel
(not the `/logic/actions` subpath), use the renamed `filterActionsFileAttachments`. The subpath import is
unaffected and is what the CommunicationActions PCF itself uses.

## PCF consumption pattern

- PCF deep-imports the single subpath `@spaarke/communication-components/logic/actions` — not the root
  barrel (same rationale as 020/021: avoids pulling `./widgets` → `@microsoft/signalr` + the React-19 widget
  graph into the React-16 PCF).
- Three resolvers wired (mirrors 020/021's three-resolver list): PCF `tsconfig.json` `paths` (added a
  granular `@spaarke/communication-components/logic/actions` entry + the `@spaarke/communication-components/*`
  wildcard), `package.json` `exports` map (webpack + node runtime — see collision note above for why this
  was necessary despite not being explicitly named in the task's barrel-edit scope), PCF `jest.config.js`
  `moduleNameMapper` (deterministic test resolution to source, no `dist/` build required).
- PCF `package.json` gained `"@spaarke/communication-components": "file:../../shared/Spaarke.Communication.Components"`.
- Deleted local duplicates: `CommunicationActions/{composerPrefill,launchCreate,attachmentsSource}.ts`.
- Updated the 3 existing PCF unit-test files' import specifier (`../CommunicationActions/{module}` →
  `@spaarke/communication-components/logic/actions`) — no test assertions changed, only the import path.
- Also fixed a stale doc-comment header in `jest.config.js` (said "CommunicationConnections", a pre-existing
  copy-paste artifact unrelated to this task; corrected while the file was already open for the
  moduleNameMapper edit).

## Correctness invariants held

- **NFR-05 (React-agnostic):** grep-proven — none of `composerPrefill.ts` / `launchCreate.ts` /
  `attachmentsSource.ts` contain `from 'react'`, any React runtime API, or `as React.ComponentType`. The
  `Xrm.Navigation` global (launchCreate) and injected `IActionsWebApi` (attachmentsSource) are host seams,
  preserved exactly.
- **NFR-04 (regression-free):** PCF view untouched (action-bar rendering, Reply/ReplyAll/Forward/New
  dispatch, create-from-email launch, source-attachment carry-forward all identical); local duplicates
  deleted; `build:prod` green; 22/22 PCF tests pass.
- **Canonical compose consumed, not forked:** grep confirms no new composer component in the extracted
  modules — only `ComposerFields` computation; `SendEmailPage`/`SendEmailPageR16` usage in the PCF view is
  unchanged.
- **`launchCreate` contract unchanged:** `CREATE_LAUNCH_TARGETS` (event→`sprk_event`, todo→`sprk_todo`,
  invoice→`sprk_invoice`) and the OOB `Xrm.Navigation.navigateTo` modal seam preserved verbatim.

## Verification

| Check | Result |
|---|---|
| `@spaarke/communication-components` `npm run build` (tsc, declaration-only) | ✅ green (after the barrel-collision fix) |
| PCF `npm install --legacy-peer-deps --no-audit --no-fund` | ✅ (new `@spaarke/communication-components` dep linked; node_modules was not yet installed for this PCF) |
| PCF `npm run build:prod` (pcf-scripts / webpack prod) | ✅ green (2.47 MiB bundle-size perf warnings only — pre-existing class of warning, same as sibling PCFs; not a new regression) |
| PCF `npx jest` | ✅ 22/22 pass (3 suites: `composerPrefill`, `launchCreate`, `attachmentsSource`) |
| code-review | ✅ clean (0 Critical / 0 Warning; the barrel rename is the only structural deviation, documented above) |
| adr-check | ✅ clean (ADR-022 slim-first: no React import/cast in extracted logic, PCF stays a platform-React virtual control — webpack build confirms `external "Reactv16"`; ADR-012: shared lib remains context-agnostic, no dashboard coupling) |

## Barrel-edit scope note (for the orchestrator)

Per the task's guardrail, only `src/logic/index.ts` + new `src/logic/actions/**` files were to be touched in
the shared package. Two additional, minimal, additive edits were required beyond that literal list and are
called out explicitly here for visibility:
1. `package.json` — added the single `"./logic/actions"` exports-map key (necessary for the PCF's webpack
   build to resolve the deep import at all; verified by inspecting the sibling `CommunicationAttachments`
   PCF's already-installed `node_modules/@spaarke/communication-components/package.json`, which shows this
   same mechanism already relied upon for 021's `"./logic/attachments"` entry).
2. `src/logic/index.ts` — beyond the planned one-line addition, needed the explicit-named-re-export form
   (not a plain `export *`) to resolve the `filterFileAttachments` collision with `./attachments` (both
   already-shipped and new code, so this was a compile-blocking discovery, not a design choice up for
   debate).

Neither edit touched `src/index.ts`, `src/components/index.ts`, `src/logic/connections/**`, or
`src/logic/attachments/**` — confirmed via `git diff --stat`, which also surfaced (as expected, from a
concurrent sibling task) an unrelated one-line diff on `src/components/index.ts` this task did not make.
