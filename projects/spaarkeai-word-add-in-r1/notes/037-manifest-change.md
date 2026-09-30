# Task 037 — FR-17: Word ribbon commands (quickSave, shareDocument)

## Manifest question — resolved, not re-asked

The POML's escalation trigger 1 ("STOP if 011 has not landed and the manifest is still XML") did **not**
fire. Verified directly before writing: `word/word-manifest.xml` is the retained, currently-live M365
Admin Center artifact (per its own header comment and `notes/011-word-manifest-migration.md`'s "both the
retained XML for 'Integrated apps' upload and, once desktop/web sideload is verified, the unified JSON
manifest"); `word/manifest.json` is `manifestVersion: devPreview`, still under verification. The XML had
`<Version>1.0.8.0</Version>`, a single `VersionOverrides` with one `ShowTaskpane` control, and **no
`<FunctionFile>` at all** — confirmed the unreachable-stub defect this task exists to fix, and confirmed
the fix belongs in the XML (the format actually live for Word), not a manifest 011 had already replaced.

## What changed in each manifest file

**`word/word-manifest.xml`** (the live artifact):
- `<Version>` bumped `1.0.8.0` → `1.0.9.0`.
- Added `<Set Name="DialogApi" MinVersion="1.1"/>` to `<Requirements><Sets>` (see "Notification surface" below).
- Added `<FunctionFile resid="Commands.Url"/>` as the first child of `<DesktopFormFactor>`.
- Added two `<Control xsi:type="Button">` entries in the existing `SpaarkeGroup`, alongside `SaveButton`:
  `QuickSaveButton` → `<Action xsi:type="ExecuteFunction"><FunctionName>quickSave</FunctionName></Action>`,
  `ShareButton` → same shape with `shareDocument`.
- Added `<bt:Url id="Commands.Url" DefaultValue="https://localhost:3000/word/commands.html"/>` (matches
  webpack's existing `word/commands.html` emission — no new webpack entry needed there) plus the
  corresponding `ShortStrings`/`LongStrings` for both new buttons.
- Both new controls reuse the existing `icon-16/32/80.png` files (same convention task 011 established
  for `SaveButton`, since `save-*.png` files referenced by Outlook's manifest don't exist in
  `shared/assets/`).

**`word/manifest.json`** (kept coherent with the XML, per 011's mirroring convention):
- `"version"` bumped `"1.0.8"` → `"1.0.9"` (3-part, matching Outlook's own `"1.0.22"` precedent — the
  unified manifest schema does not accept a 4-part version).
- Added a `{"name": "DialogApi", "minVersion": "1.1"}` capability.
- Added a `CommandRuntime` entry to `extensions[0].runtimes` (mirrors Outlook's `manifest.json` exactly):
  `code.page`/`code.script` point at `word/commands.html`/`word/commands.js`; `actions` declares
  `quickSave` and `shareDocument` as `executeFunction`.
- Added `QuickSaveButton`/`ShareButton` controls to `extensions[0].ribbons[0].tabs[0].groups[0].controls`,
  each with `actionId` pointing at the matching action.

**`word/taskpane/index.tsx`**: `APP_VERSION` bumped `1.0.8` → `1.0.9` to stay synced with the JSON
manifest version — not in the POML's declared `<outputs>`, but required by the same rule task 011 already
applied to itself ("keep `APP_VERSION` synced with the new versioning source of truth").

### M365 re-registration is now MANDATORY for task 042

The 2026-09-17 decision log entry said "our merged work changed no manifest file" and a re-upload was
only needed if the installed version differed from `1.0.8.0`. **This task changes that**: both manifests
are now bumped past `1.0.8.0`/`1.0.8`. **Task 042 MUST re-register the Word add-in in the M365 Admin
Center** (the retained XML, at minimum) before the ribbon commands can reach real users, and should
re-verify the JSON manifest sideload per 011's still-open verification gap while it has a live host.

## Notification surface: a real API mismatch found and fixed mid-task

The first implementation used the Ribbon API (`Office.ribbon.requestUpdate`) to flash a transient label
on the clicked button ("Saved!" / "Save failed") as the "host notification surface." **`npx tsc --noEmit`
caught this as wrong**: the installed `@types/office-js`'s `Office.Control` (the shape `requestUpdate`'s
`RibbonUpdaterData` accepts) has only `id` and `enabled?: boolean` — **no `label` property exists on the
type at all**. The Ribbon API can only toggle a control's enabled state, not its visible text; my
assumption about its capability was wrong, and the type checker is what caught it (baseline classification
"111 total / 0 production" — the two errors this introduced would have been new PRODUCTION errors had they
shipped).

**Fix**: switched to the Dialog API (`Office.context.ui.displayDialogAsync`), which:
- IS documented as Word-compatible (`DialogApi` requirement set, Excel/Outlook/PowerPoint/Word) and its
  own doc comment in `@types/office-js` explicitly says it is callable "from a UI-less command button."
- Opens `word/commands/notify.html` (new, emitted as `dist/word/commands-notify.html` via a
  `CopyWebpackPlugin` static-copy pattern mirroring `public/auth-callback.html` — no new webpack `entry`).
  The page is dependency-free (no Office.js bootstrap): it reads `message`/`status` from its own query
  string, renders them, and self-closes via `window.close()` after ~2.2s.
- Required a second manifest requirement-set addition (`DialogApi` 1.1) in both manifests (see above).

**Verified**: `npx tsc --noEmit --skipLibCheck` — 0 errors in `word/commands/index.ts` after the fix (back
to the 111-error baseline, all in the known pre-existing test-file buckets). `npx eslint` clean on the
file. The official `office-addin-manifest validate` tool reports the XML's schema as valid with the new
`DialogApi` set present.

**What is genuinely UNVERIFIED** (no live Word host / browser in this session): the exact popup/auto-close
behavior of a dialog opened from a UI-less ExecuteFunction command, and whether `DialogApi 1.1` truly adds
no host beyond what `WordApi 1.3` already excludes (documented as "believed, not verified" directly in the
XML manifest's own Requirements comment). Both are exactly the kind of thing only a live host can confirm
— deferred to task 042's UAT, per this project's established convention for unverifiable-in-this-session
findings (see task 011's own "Blocked — sideload verification" section for the precedent).

## `shareDocument`'s link-minting call: a deliberate small duplicate, not scope creep

`sendEmailService.ts` (task 036, FR-15, wave P3-c sibling) already has a private `mintDocumentShareLink`
that calls the exact route `shareDocument` needs (`POST /api/documents/{documentId}/share-link`). This
task's own POML constraint reads: *"This task is in wave P3-c with 035 and 036. Do not modify … the
share-link/send-email path — those belong to siblings in this wave."* `sendEmailService.ts` **is** the
send-email path (its own header names task 036) — exporting its private function would still be an edit
to that file, so the "extend the existing thing" path (root CLAUDE.md §11) is closed by this task's own
constraint, not by a technical limitation.

**Resolution**: `shared/taskpane/services/shareLinkService.ts` is a new, small, independent file with its
own `mintDocumentShareLink` — same route, same request/response shape, same auth path, ~35 lines,
documented in its own header comment with the three-question justification (existing / extension / cost
of doing nothing). If the wave boundary is later lifted, consolidating both call sites onto one shared
function is a legitimate, low-risk follow-up — flagging it here rather than doing it silently.

## `quickSave`: always an unfiled CREATE — no association

Outlook's `quickSave` files to a **predicted** record (the association engine has an email-specific
prediction call). No equivalent prediction mechanism exists for Word documents anywhere in this codebase.
Rather than build one (far outside this task's scope) or silently skip the "one-click" promise,
`quickSave` always POSTs a `contentType: 'Document'` CREATE with no `targetEntity` — `useSaveFlow.ts`
already documents association as optional for a Document save ("Association is optional - user can save
without selecting an entity"), so an unfiled document is a normal, already-supported state the user can
file later from the pane. This also matches the acceptance criterion's own wording ("exactly one
`sprk_document` is **created**") — a version-save reuse would not create a new row at all.

No document-identity resolution (`resolveDocumentIdentity`/stamp precedence) runs before a `quickSave`
CREATE — that machinery exists (task 012/014/051) but wiring version-vs-create judgment into a one-click
ribbon action is a materially bigger feature (the pane's own `SaveModeSection` UI exists precisely because
that decision needs user-visible disambiguation) and is not described anywhere in this task's acceptance
criteria. `shareDocument`, by contrast, genuinely needs identity resolution (there is no "unfiled share"),
and uses it (see below).

## `computeQuickSaveIdempotencyKey`: generalized, not duplicated

The task's constraint names this exact function. Its pre-037 shape (`(internetMessageId: string, target:
EntitySearchResult) => Promise<string>`) is Outlook-specific — a Word quick-save has no
`internetMessageId` and, per the above, no `target` either. Generalized the function to accept a
discriminated union (`{kind:'email', internetMessageId, target} | {kind:'document', title,
contentBase64}`), preserving the EXACT canonical-string shape for the email path
(`email:{msgId}|{logicalName}:{id}`, unchanged) so Outlook's resulting hashes are byte-identical to
before. The document path canonicalizes over `document:{title}|{contentBase64}` — hashing the full
content (not a separate pre-hash) is what makes two rapid identical clicks collapse to one key while a
genuinely edited re-save produces a different one (the double-click acceptance criterion + a real-edit
counter-case, both unit-tested in `quickSaveHelpers.test.ts`).

Three call sites updated for the new shape: `quickSaveHelpers.ts` itself, its test (2 existing assertions
+ new document-kind + collision-safety assertions), and `outlook/commands/index.ts`'s one call site
(object-literal wrap, same values, same resulting hash). No behavior change for the shipped Outlook path.

## `event.completed()` — where it's proven, not just asserted

`word/commands/__tests__/commands.test.ts` (new — see Report for gate numbers) exercises every branch of
both commands with all collaborators module-mocked (`@shared/adapters`, `@shared/services`,
`documentIdentityService`, `quickSaveHelpers`, `shareLinkService`) and asserts `event.completed` was
called exactly once per scenario: success, document-extraction failure, auth-bootstrap failure,
network/POST failure (`quickSave`); success, the "nothing to share" pane-opening exception, mint failure,
identity-resolution throw, auth-bootstrap failure, and a clipboard-write failure (`shareDocument`); plus
two tests specifically pinning that a broken notification surface (API absent, or `displayDialogAsync`
throwing synchronously) can never block completion.

## The one deliberate "open the pane" exception — stated per the task's own constraint

`shareDocument`, when the open document has **no resolved Spaarke identity** (never saved to Spaarke —
`resolveDocumentIdentity` + stamp precedence both come back non-`'resolved'`), opens the task pane instead
of failing or guessing. This mirrors `outlook/commands/index.ts`'s own `quickSave` precedent ("no
prediction → open the taskpane for an explicit choice"). Every other path in both commands completes
WITHOUT opening the pane. Asserted by the test named `'DELIBERATE EXCEPTION: opens the pane when the
document has no resolved Spaarke identity yet…'`.

## `jest.config.js`: `roots` widened

`word/commands/__tests__/commands.test.ts` is the first test file this package has ever had outside
`shared/`, and jest's `roots: ['<rootDir>/shared']` made it invisible to `npx jest`. Widened to
`['<rootDir>/shared', '<rootDir>/word', '<rootDir>/outlook']` (verified BEFORE widening that zero
`.test.ts(x)` files existed anywhere outside `shared/` — a repo-wide `find` came back empty — so this
newly discovers only this task's own new suite, nothing dormant). `outlook` included for symmetry, since
the same command-context testing pattern belongs there too as a natural follow-up (`outlook/commands`
still has no test file of its own).

## Word adapter host detection: explicit literal, not the taskpane's no-arg form

`ensureBootstrapped()` calls `HostAdapterFactory.createAndInitialize('word')` with the explicit host
literal, not the no-arg form the taskpane's own bootstrap uses (which falls back to `detectHostType()`
reading `Office.context.host` — project CLAUDE.md's 2026-09-09 decision log records this as an unclosed,
must-verify-live question for the taskpane specifically). `word/commands/index.ts` only ever loads inside
`word/commands.html`, a Word-only bundle never shared with Outlook, so the literal is not a guess.
`WordAdapter.initialize()` still independently checks `info.host === Office.HostType.Word` itself, so this
does not weaken the real safety check.

## Deferred / unverified — explicitly, not silently

The four `<ui-tests>` (quickSave from the ribbon, shareDocument from the ribbon, double-click does not
duplicate, failure path completes cleanly) require a live Word desktop host, unavailable in this session.
**UNVERIFIED — deferred to task 042's UAT**, matching tasks 013/021/026/033/034/011's own precedent in
this project. Everything build/typecheck/lint/unit-test-verifiable in this environment passes (see the
Report message for the exact gate numbers).

## What I would change in main-session-owned files (not touched, per instruction)

- `projects/spaarkeai-word-add-in-r1/tasks/TASK-INDEX.md`: mark task 037 ✅ (FR-17 complete, four
  ui-tests UNVERIFIED/deferred to 042).
- `projects/spaarkeai-word-add-in-r1/current-task.md`: reset for the next pending task; note in Decisions
  Made that 037 bumped both Word manifests to 1.0.9(.0), obligating an M365 re-registration at 042, and
  that the Ribbon API was found NOT to support label updates (a genuine `@types/office-js` finding worth
  keeping visible for any future ribbon-feedback work in this project).
- `src/client/office-addins/ci-gated-suites.txt`: add the two new green-from-creation suites —
  `word/commands/__tests__/commands.test.ts` (14 tests) and
  `shared/taskpane/services/__tests__/shareLinkService.test.ts` (7 tests) — under a new section, e.g.
  "--- Word ribbon commands (task 037, FR-17) - green from creation ---". This raises the gate from 44/508
  to 46/~531 (508 baseline + 8 quickSaveHelpers additions + 14 + 7 new-suite tests — the CI run in this
  session's Report gives the exact post-change count).
