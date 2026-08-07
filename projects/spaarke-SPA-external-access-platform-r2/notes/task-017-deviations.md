# Task 017 — Cleanup Dead Power Pages Proxy/Config — Evidence + Deviations

**Status**: COMPLETED 2026-08-06 · Rigor: STANDARD · Escalation trigger: did NOT fire

---

## Scope executed

1. `src/client/external-spa/vite.config.ts` — removed the dev-server proxy for `/_api`, `/_layout`,
   `/_services` (lines 157-172 in the pre-edit file) and the `removeModuleScriptType()` html-transform
   plugin + its registration (lines 22 fn def / 94 registration in the pre-edit file).
2. `src/client/external-spa/powerpages.config.json` — deleted.
3. `src/client/external-spa/README.md` — rewritten to describe the actual SWA-hosted reality (was
   describing `vite-plugin-singlefile` single-file Power Pages web-resource bundling, which is not
   what the current `vite.config.ts` does).

## Escalation-check evidence (dead-ness verification, done BEFORE deleting)

- `Grep "VITE_PORTAL|powerappsportals" src/client/external-spa` → matches ONLY in `vite.config.ts`
  (the proxy block itself) and `README.md` (docs describing the proxy) and `vite-env.d.ts` (an
  unused optional type declaration). **Zero matches in any page/component/hook/api-client source
  file** — nothing in the actual app calls through this proxy.
- `Grep "/_api|/_layout|/_services" src/client/external-spa` → same result: only in `vite.config.ts`
  and `README.md`, never in application code.
- `Grep "/api/v1/collab" src/client/external-spa` → no matches (the SPA doesn't call this route at
  all; it exists only server-side in `Sprk.Bff.Api/Api/ExternalAccess/*`, unrelated to the removed
  proxy).
- `.github/workflows/deploy-external-spa.yml` reviewed: it runs `npm run build` (never `npm run dev`),
  and Vite's `server.proxy` option only applies to the dev server — it has **zero effect on
  `vite build`**. The workflow has no dependency on the removed proxy. No change was needed there.
- Conclusion: the proxy + config were genuinely dead. Deletion was correct; escalation trigger
  (live dependency found, or build/deploy breakage) did not fire.

## The `format: 'iife'` judgment call (kept, not removed)

The task named line `:132` (`format: 'iife',`) alongside line `:22` as "the associated iife
workaround" to remove. On inspection, `format: 'iife'` and `removeModuleScriptType()` were two
halves of one combined workaround (the plugin only makes sense in service of an IIFE bundle needing
its `type="module"` tag stripped so the Power Pages Module Federation host wouldn't intercept it).

Decision: removed the plugin (its JSDoc's SOLE stated purpose is defeating the Power Pages MF host —
unambiguously dead) but **kept `format: 'iife'` unchanged**, only scrubbing the Power-Pages-specific
rationale from its comment. Reasoning:

- Converting the build output format from IIFE to Vite's default ES module output is a materially
  different, higher-blast-radius change than removing a config/proxy — it changes what artifact Vite
  emits, not just removing dead config.
- I was instructed not to run `npm run build` / `npm run build:prod` (the wave orchestrator runs the
  authoritative build afterward), so I have no way to empirically verify a bundle-format change.
- Reasoning-based confidence that a format change would be *safe* (same-origin `crossorigin` is a
  no-op; a `type="module"` tag around an IIFE bundle executes fine since Rollup's IIFE output is
  already strict-mode wrapped; no dynamic `import()` in the codebase so chunking would likely stay
  single-file either way) is not the same as verification, and I judged it out of the conservative,
  scoped-deletion spirit of a STANDARD-rigor cleanup task.
- Net effect of what WAS changed: after removing the plugin, Vite will emit its normal
  `<script type="module" crossorigin src="assets/app.js">` tag around the IIFE-bundled content. This
  is functionally equivalent to before (module scripts are deferred by default, same as the `defer`
  attribute the removed plugin was manually substituting), just without the special-case Power-Pages
  workaround stripping the tag.
- **Recommendation for a follow-up task if desired**: a full IIFE→ESM build-format revert is possible
  now that Power Pages is fully retired, but should be its own task with a real `npm run build` +
  manual smoke-test verification, not bundled into this cleanup.

## Additional stray "Power Pages" text fixed (outside the named 3 files)

The acceptance criteria's literal negative-grep terms (`/_api`, `/_layout`, `/_services`,
`powerpages`) are now zero everywhere in `external-spa` (verified). Task Step 4 additionally asks to
sweep for `"power pages"` (with a space) and allows unrelated legitimate hits to remain. Three
trivial, zero-collision-risk text fixes were made because they were one-line, no-behavior-change, and
not in any concurrent task's file territory:

- `.gitignore` — removed the vestigial `.powerpages-site/` ignore entry.
- `package.json` — `"description"` said "Power Pages Code Page SPA"; corrected to describe the
  Azure Static Web Apps reality.
- `index.html` — one CSS comment said "Reset for Power Pages iframe context"; corrected to "Reset for
  embedded iframe context (Teams tab)" (accurate — the iframe context is now the Teams host, not
  Power Pages).
- `src/vite-env.d.ts` — removed the now-unused `VITE_PORTAL_URL?: string` type declaration (its only
  usage was the deleted proxy block).

## Deliberately NOT touched (documented, not a gap)

Grep also found stale "Power Pages" prose comments (describing history, not live behavior) in:
`src/api/web-api-client.ts`, `src/main.tsx`, `src/hooks/useExternalContext.ts`, `src/types/index.ts`,
`src/components/ContactsOrganizations.tsx`, `src/pages/PlaybookLibraryPage.tsx`,
`src/pages/DocumentUploadPage.tsx`, `src/components/DocumentLibrary.tsx`,
`src/components/EventsCalendar.tsx`.

Verified these are dead prose, not live dependencies: `web-api-client.ts`'s own header comment
("BFF Data Client") and documented route list (`GET /api/v1/external/projects`, etc.) prove every
function already calls the BFF via `bffApiCall`, not a Power Pages Web API. None of these files
contain the literal negative-criterion strings (`/_api`, `/_layout`, `/_services`, `powerpages`).

These were left alone because: (a) they're outside this task's declared `<relevant-files>` /
`<outputs>` scope (`vite.config.ts`, `powerpages.config.json`, `README.md` only); (b) several are
widget/page components that are exactly task 012's concurrent "widget registry + populate Quick
Start" editing territory — touching them risks a merge collision in the parallel wave; (c) Step 4's
acceptance language explicitly allows "or are unrelated legitimate paths" as a valid outcome. This is
recorded here as an explicit scope decision, not an oversight — a future doc-hygiene pass could clean
these prose comments up.

## Verification run

- `npx tsc --noEmit` (project `tsconfig.json`, covers `src/`): clean, zero output.
- `npx tsc --noEmit -p tsconfig.node.json` (covers `vite.config.ts` specifically): 8 pre-existing
  errors, all `Cannot find module 'path'/'url'/'fs'` + `Property 'startsWith' does not exist on type
  'string'` — caused by `tsconfig.node.json` missing `@types/node` / a modern `lib`/`target`, NOT by
  this task's edits. Verified via `git stash` A/B comparison: the pre-edit file had 11 errors in the
  same categories (plus 3 `Cannot find name 'process'` errors that my edit removed along with the
  proxy block that used `process.env.VITE_PORTAL_URL`). My change reduces the error count; it
  introduces none.
- `npm run build` / `npm run build:prod` intentionally NOT run — the wave orchestrator runs the
  authoritative build after all wave-B tasks complete.

## Not touched (per hard constraints)

- `src/auth/**`, `host/TeamsHostAdapter.ts`, `src/config.ts` auth constants, `components/AuthGuard.tsx`
  — untouched (NFR-05 / ADR-028).
- `tasks/TASK-INDEX.md` — not edited (main session aggregates wave status per orchestrator
  instruction). This task's own POML `<status>` was set to `completed`.
- Shell `src/*.tsx` files under active edit by task 012 — untouched.
- `staticwebapp.config.json` / SWA hosting config — untouched (out of scope per task constraint).

## Acceptance criteria — final status

| Criterion | Status |
|---|---|
| `npm run build:prod` green with proxy + iife workaround removed | ⚠️ Not run (forbidden by orchestrator); `tsc --noEmit` clean instead; also note: this project's actual build script is `npm run build` (Vite, not PCF) — `build:prod` does not exist here, confirmed in task 011's notes and `package.json`. Main session's authoritative wave build is the real gate. |
| `powerpages.config.json` no longer exists; `README.md` has no Power Pages content | ✅ Met |
| Negative: repo-wide grep of `external-spa` for `/_api`,`/_layout`,`/_services` proxy, `powerpages` returns zero | ✅ Met (verified) |
| `deploy-external-spa.yml` still builds the app (no proxy dependency remains) | ✅ Met — verified it never depended on the dev-only proxy in the first place |
