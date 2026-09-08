# Task 011 — FR-05: Word unified JSON manifest migration

> **Status**: Build-verified, sideload-UNVERIFIED (see § Blocked). Do not treat as fully closed until § Blocked is resolved.

## What was built

- `src/client/office-addins/word/manifest.json` (new) — unified `devPreview` JSON manifest for Word, structurally mirroring `outlook/manifest.json` (same `manifestVersion`, `developer`, `icons`, `authorization`, one `extensions` entry with `requirements`/`runtimes`/`ribbons`, and a new `webApplicationInfo` block Word did not have before).
- `src/client/office-addins/webpack.config.js` — added a second `CopyWebpackPlugin` pattern for `word/manifest.json` that substitutes `ADDIN_CLIENT_ID` (top-level `id` + `webApplicationInfo.id`), `BFF_API_CLIENT_ID` (`webApplicationInfo.resource`) and `ADDIN_BASE_URL` (all `https://localhost:3000` occurrences), reusing the exact same regex/transform pattern as the existing Outlook block. The pre-existing `word-manifest.xml` copy pattern is retained unchanged (base-URL substitution only) and its comment now cross-references the new JSON manifest + this file.
- `src/client/office-addins/word/word-manifest.xml` — retained (per constraint). Two changes: (1) a header comment naming `manifest.json` as the artifact under verification and stating the XML stays until both-surface sideload passes; (2) `WordApi MinVersion` corrected `1.1` → `1.3` (see § WordApi reconciliation) and `<Version>` bumped `1.0.6.0` → `1.0.7.0`.
- `src/client/office-addins/word/taskpane/index.tsx` — `APP_VERSION` bumped `1.0.6` → `1.0.7` and its sync comment repointed from `word-manifest.xml` to `word/manifest.json`, mirroring how `outlook/taskpane/index.tsx` stays synced to `outlook/manifest.json`. Not in the POML's `<outputs>` list but required to satisfy the architecture doc's explicit version-bump rule ("`outlook/manifest.json` + `outlook/taskpane/index.tsx` … and the Word equivalents") — noted here as the one file touched outside the declared output list.

## WordApi requirement-set reconciliation (acceptance criterion 3)

**Declared minimum: WordApi 1.3.** Verified against `@types/office-js@1.0.377`'s `index.d.ts` (the installed type package backing the build), not assumption:

| Call in `shared/adapters/WordAdapter.ts` | Method | API set per `index.d.ts` |
|---|---|---|
| `getItemId()` — `properties.load(['title', 'author', 'creationDate'])` | `Word.DocumentProperties.title` | **WordApi 1.3** |
| `getItemId()` — same call | `Word.DocumentProperties.author` | **WordApi 1.3** |
| `getItemId()` — same call | `Word.DocumentProperties.creationDate` | **WordApi 1.3** |
| `getSubject()` — `properties.load('title')` | `Word.DocumentProperties.title` | **WordApi 1.3** |
| `getBody()` — `body.getHtml()` / `body.load('text')` | `Word.Body.getHtml()` | WordApi 1.1 |
| `getDocumentContent()` — `body.getOoxml()` | `Word.Body.getOoxml()` | WordApi 1.1 |
| `insertLink()` — `selection.insertHtml(...)` | `Word.Range.insertHtml()` | WordApi 1.1 |
| `initialize()` — `getCapabilities()` | `Office.context.requirements.isSetSupported('WordApi', MIN_WORD_API_VERSION)` (self-declared `'1.3'` constant) | n/a (runtime check, not an API call) |

**Forcing API: `Word.DocumentProperties.title` / `.author` / `.creationDate`**, entirely from the whole `Word.DocumentProperties` class being introduced at WordApi 1.3 (every member on that class in the type file is annotated `[Api set: WordApi 1.3]`). Every other Office.js call the adapter makes (`getOoxml`, `getHtml`, `getSelection`, `Range.insertHtml`) is satisfied by WordApi 1.1. The adapter's own `MIN_WORD_API_VERSION = '1.3'` constant (`WordAdapter.ts:41`) is therefore **correct, not over-declared** — it was the manifest (`word-manifest.xml:34`, `WordApi MinVersion="1.1"`) that was stale and under-declared, matching the task's premise. Both manifests now declare `1.3`; the requirement is not raised beyond what the adapter needs, since 1.3 is the true floor.

## Version reconciliation (a deviation from a literal reading of the POML)

The POML's acceptance criterion 4 asks for "a bumped 4-part version" compared to the prior `1.0.6.0`. That phrasing describes the retained **XML** manifest's format (4-part `X.X.X.X` is an XML-schema/M365-Admin-Center requirement — see the architecture doc's manifest-rules table). The unified `devPreview` JSON manifest schema (confirmed against `outlook/manifest.json`'s own working precedent, `"version": "1.0.22"`) uses a SemVer-style 1–3 segment version, not 4. Using a literal 4-part string in `word/manifest.json` risked schema rejection with no upside.

**Resolution (Path C — pivot to comply, not an ADR conflict but the same reasoning shape)**: both artifacts are bumped above `1.0.6.0` above the prior baseline, each in the version format its own schema requires:

- `word/manifest.json` → `"version": "1.0.7"` (3-part, matching Outlook's convention)
- `word/word-manifest.xml` → `<Version>1.0.7.0</Version>` (4-part, unchanged format)
- `word/taskpane/index.tsx` → `APP_VERSION = '1.0.7'` (matches the JSON manifest, mirroring Outlook's `APP_VERSION` ↔ `outlook/manifest.json` sync convention)

## Icon-reference fix (deviation from a literal copy of `outlook/manifest.json`)

`outlook/manifest.json`'s `SaveToSpaarkeButton` control (and its Word XML counterpart is fine here) references `assets/save-16.png` / `save-32.png` / `save-80.png`. Those files **do not exist** in `shared/assets/` (verified: only `icon-16/32/64/80/128.png`, `icon-color.png`, `icon-outline.png`, `spaarke-logo.svg`, `star-logo.svg`). This is a pre-existing gap in the Outlook manifest, out of this task's scope to fix there. Copying it into `word/manifest.json` would have produced a manifest that fails this task's own acceptance criterion ("no unsubstituted placeholder tokens… all icon and URL references resolvable"), so the Word `SaveButton` control instead reuses the existing `icon-16/32/80.png` files (the same three files already used for the ribbon group icon). Structure (three sizes, same shape) is otherwise identical to the Outlook precedent.

## Build verification (actually run)

```
cd src/client/office-addins
npm install --legacy-peer-deps --no-audit --no-fund   # up to date, no changes
npm run build                                          # webpack --mode production --no-bail --stats errors-only
```

- `npm run build` exit code **0**, zero errors reported (`--stats errors-only`).
- `dist/word/manifest.json` emitted, parses as valid JSON (`node -e "require('./dist/word/manifest.json')"` succeeded).
- Substitution verified twice: (1) default env — `id`/`webApplicationInfo.id` → the configured `ADDIN_CLIENT_ID`, `webApplicationInfo.resource` → `api://{BFF_API_CLIENT_ID}`, both correct, no leftover placeholder GUIDs; (2) rebuilt with `ADDIN_BASE_URL=https://example-swa.azurestaticapps.net` — every `https://localhost:3000/...` URL in the emitted manifest (runtime `code.page`, all ribbon/group icon URLs) resolved to the example SWA origin, proving the substitution mechanism end-to-end. This is the identical mechanism `outlook/manifest.json` already uses (verified side-by-side — Outlook's emitted manifest shows the same localhost-passthrough under a bare `npm run build` with no `NODE_ENV`/`ADDIN_BASE_URL` set, confirming this is shared, pre-existing webpack behavior, not something this task introduced).
- Build env: no `.env` existed in the worktree. A temporary `.env` with non-secret placeholder GUIDs (`00000000-...-0001/2/3`) was created only for local build verification, and deleted afterward (`.env` is git-ignored per `.gitignore:11`, confirmed via `git status --porcelain` showing no trace before or after).
- `npm run typecheck`: 384 pre-existing errors (package-wide), **zero** in `word/taskpane/index.tsx` (the only `.ts`/`.tsx` file this task touched) — this task introduces no new typecheck errors. The 384 vs. the 001-baseline's 395 reflects an intervening master merge (`df0371052`), not this task's work; the baseline debt is owned by tasks 006/007/008 and is out of scope here per the CRITICAL CONTEXT brief.
- Prebuild dependency note: `@spaarke/auth`'s `dist/` was not present at task start (gitignored) and had to be `npm install` + `npm run build` (tsc) before `office-addins`' webpack could resolve `@spaarke/auth` — consistent with the gotcha already recorded in the project `CLAUDE.md`.

## M365 Admin Center re-registration obligation (for task 042)

Both manifests changed version (`1.0.6.0` → `1.0.7.0` XML; new `1.0.7` JSON) and the JSON manifest is a **new artifact** with a `webApplicationInfo` SSO binding Word never had. Per `src/client/office-addins/CLAUDE.md` and the architecture doc's manifest-rules table, **any manifest version bump requires M365 Admin Center re-registration** at the new version. Task 042 (deploy + UAT) MUST re-register the Word add-in (both the retained XML for "Integrated apps" upload and, once desktop/web sideload is verified, the unified JSON manifest) before UAT can proceed against the new version. This is a hard dependency task 042 needs to plan for, not merely be aware of.

## 🔴 Blocked — steps 6/7 not performed (sideload verification)

The POML's steps 6/7 and acceptance criteria 6/7 require sideloading `word/manifest.json` onto **Word desktop** and **Word on the web**, confirming install, ribbon button, task pane open, and auth completion on both, and only then deciding `word-manifest.xml`'s disposition.

**This agent has no interactive Office host or browser session available** (no Chrome/browser tool in this session's toolset, no running Word desktop client, no deployed/dev-served add-in to point a host at). Sideload verification could not be attempted, let alone pass or fail — this is a capability gap in this execution environment, not a manifest defect discovered and then dismissed.

**What this means for the task's disposition**:
- `word/word-manifest.xml` is **retained unchanged in content/behavior** (only the version bump + WordApi fix + header comment applied) — no decision to remove it was made, consistent with the constraint that removal requires post-verification evidence this run doesn't have.
- Acceptance criteria 6 and 7 (desktop/web sideload) are **not met** and cannot be marked so from this environment.
- Everything build-time-verifiable (manifest structure, JSON validity, substitution, WordApi reconciliation, version bump, no-CI-file-touch, no-secret-embedded, notes) **is** verified and passing.

**Recommended next step**: an operator (or a session with `--chrome` and a live dev/deployed add-in) performs the two sideloads per the POML's step 6, then either (a) confirms both pass and this task is closed out with the XML-disposition decision made, or (b) the escalation trigger in the POML fires if desktop install fails for a platform reason.

## Deviations summary

1. Icon URLs for the Word `SaveButton` use the existing `icon-16/32/80.png` files instead of copying Outlook's broken `save-16/32/80.png` references (files that don't exist in `shared/assets/`).
2. `word/manifest.json`'s version is 3-part (`1.0.7`) to satisfy the unified-manifest SemVer-style schema, matching Outlook's own precedent (`1.0.22`), while the retained XML keeps 4-part (`1.0.7.0`) per its own schema requirement — both are "bumped above `1.0.6.0`," satisfying the acceptance criterion's intent without violating either manifest's format.
3. `word/taskpane/index.tsx` was touched (not in the POML's declared `<outputs>`) to keep `APP_VERSION` synced with the new versioning source of truth, per the architecture doc's explicit version-bump rule for "the Word equivalents."
4. Sideload verification (steps 6/7, acceptance criteria 6/7) could not be performed — no interactive Office/browser environment available to this agent. Task is NOT marked ✅ complete in `TASK-INDEX.md`; see § Blocked.

## Files touched

- `src/client/office-addins/word/manifest.json` (new)
- `src/client/office-addins/webpack.config.js`
- `src/client/office-addins/word/word-manifest.xml`
- `src/client/office-addins/word/taskpane/index.tsx`
- `projects/spaarkeai-word-add-in-r1/notes/011-word-manifest-migration.md` (this file)

`ci-router.yml`, `ci-tier1-blocking.yml`, `ci-tier2-advisory.yml`, and `deploy-office-addins.yml` were **not** touched.
