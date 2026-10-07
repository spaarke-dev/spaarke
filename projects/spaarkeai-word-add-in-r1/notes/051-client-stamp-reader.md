# Task 051 — FR-02 client half: read the custom XML stamp in the pane and apply the identity precedence

> **Status: COMPLETE.** Rigor FULL · sonnet @ effort high · directional. Base: `713710a39` (local
> `work/spaarkeai-word-add-in-r1` tip at dispatch, includes tasks 014, 025, 031, 045, 050, 053).
> Executed in an isolated worktree (`agent-a540227bf466786f7`); this note is written before merge.

**This task completes FR-02.** Task 014 shipped the server-side stamp (writes an invisible custom
XML identity marker into every saved `.docx`); this task adds the reader, so a downloaded, edited,
re-uploaded document now self-identifies instead of forking its record on every round trip.

---

## 1. What shipped

| File | Change |
|---|---|
| `shared/adapters/types.ts` | `HostCapabilities.canReadDocumentStamp: boolean` (new required field) |
| `shared/adapters/IHostAdapter.ts` | `readDocumentStamp(): Promise<string \| null>` (new interface member) |
| `shared/adapters/WordAdapter.ts` | The real reader: `readDocumentStamp()`, `extractStampId()`, `getCustomXmlPartsByNamespace()`, `getPartXml()`, three stamp-contract constants, `canReadDocumentStamp` capability. `checkRequirementSet(set, version?)` — `version` now optional. |
| `shared/adapters/OutlookAdapter.ts` | `readDocumentStamp()` rejects `CAPABILITY_NOT_SUPPORTED` (mirrors `getDocumentUrl()`); `canReadDocumentStamp: false` |
| `outlook/OutlookHostAdapter.ts` | Same two additions, compile-satisfaction only — this class is dead code (task 013 D-1); not exercised by any live path |
| `shared/taskpane/services/documentIdentityService.ts` | New pure function `applyStampPrecedence(urlOutcome, stampDocumentId): DocumentIdentityOutcome` |
| `shared/taskpane/App.tsx` | `resolveOpenDocumentIdentity` now also reads the stamp (capability-gated) and combines it via `applyStampPrecedence` before `settle()` |
| `shared/taskpane/components/views/__tests__/SaveView.captureAtSave.test.tsx` | Mechanical fix: added `canReadDocumentStamp` to two `HostCapabilities` mock literals and `readDocumentStamp` to one `IHostAdapter` mock literal — this GATED file stopped compiling the moment `HostCapabilities`/`IHostAdapter` grew a new required member, exactly the same ripple task 013 D-1 documented for `getDocumentUrl()`. No test logic, assertion, or behavior changed. |
| `shared/adapters/__tests__/WordAdapter.readDocumentStamp.test.ts` | **NEW.** 18 tests: happy path (4), null path (9), capability + runtime guard (4), error handling (1) |
| `shared/adapters/__tests__/OutlookAdapter.readDocumentStamp.test.ts` | **NEW.** 3 tests |
| `shared/taskpane/services/__tests__/documentIdentityService.stampPrecedence.test.ts` | **NEW.** 14 tests covering the full precedence table |

## 2. How the Common API call was promisified and guarded

`WordAdapter.readDocumentStamp()`:
1. `this.ensureInitialized()` — matches `getDocumentUrl()`'s convention; throws `NOT_INITIALIZED` if called before `initialize()`.
2. `if (!this.checkRequirementSet('CustomXmlParts')) return null;` — the runtime guard (019 condition 4), checked **before** any Common API call, using the **bare one-argument** form (`CustomXmlParts` is an unversioned Common set — 019 §6 condition 4). `checkRequirementSet`'s `version` parameter was made optional (`version?: string`) rather than adding a second helper (root CLAUDE.md §11); when `version === undefined` it calls `isSetSupported(set)` with exactly one argument, not `isSetSupported(set, undefined)`.
3. `getCustomXmlPartsByNamespace(namespace)` and `getPartXml(part)` — two private methods, each wrapping one Common API callback (`CustomXmlParts.getByNamespaceAsync`, `CustomXmlPart.getXmlAsync`) in `new Promise((resolve, reject) => {...})`, exactly the pattern `getCompressedFile()` already established for `getFileAsync`. A synchronous throw inside either executor (e.g. `Office.context.document.customXmlParts` being undefined on a misbehaving host) becomes a promise rejection per the Promise spec — no manual `try` needed inside the executor.
4. The whole read (both calls, plus the per-part loop) is wrapped in one `try { ... } catch { return null; }` in `readDocumentStamp()`, so **every** rejection — `getByNamespaceAsync` failing, `getXmlAsync` failing, or `Office.context.document.customXmlParts` being entirely absent — converges on `null`. Pinned by three separate tests exercising each of those three paths independently.
5. `extractStampId(xml)` (module-level, pure) parses with `DOMParser().parseFromString(xml, 'application/xml')`, checks for the `<parsererror>` convention (both jsdom and real browsers report malformed XML this way, not via a thrown exception — checked explicitly, not just wrapped in try/catch), matches root `localName` + `namespaceURI` against the mirrored contract, extracts the one `documentId` child, and validates GUID shape via a linear (non-backtracking) regex before returning a lowercase, brace-stripped candidate.
6. The per-part loop in `readDocumentStamp()` mirrors the server's `OfficeDocumentStamp.TryReadStamp` **exactly**: one `found: string | null` accumulator; a later id that disagrees with an already-found id returns `null` immediately ("no answer is better than the wrong one"); two parts that *agree* just confirm the same id.

**The stamp contract (namespace/root element/id element) is a byte-for-byte VALUE mirror** of
`OfficeDocumentStamp.StampNamespace` / `.StampRootElement` / `.StampIdElement`
(`src/server/api/Sprk.Bff.Api/Services/Office/OfficeDocumentStamp.cs`) — TypeScript cannot reference
a C# constant, so this is documented as a mirror, not a shared reference, with an explicit
re-verification pointer if the server values ever change. The three literal values were confirmed
by reading the server source directly (`grep -n "StampNamespace\|StampRootElement\|StampIdElement"`),
not copied from the task brief.

## 3. The precedence — evidence per branch

`applyStampPrecedence(urlOutcome, stampDocumentId)` in `documentIdentityService.ts`, wired into
`App.tsx`'s `resolveOpenDocumentIdentity` (called once, after both `hostAdapter.getDocumentUrl()` /
`resolveDocumentIdentity()` **and** the capability-gated `hostAdapter.readDocumentStamp()` settle).

| Owner-decision branch | Evidence |
|---|---|
| **(1) A resolved URL WINS** — same id in URL and stamp | `documentIdentityService.stampPrecedence.test.ts` "AC2: same id in URL and stamp — resolved, unchanged, SAME reference" — asserts `result` is the exact same object reference as the input, not just an equal one |
| **(1) A resolved URL WINS** — no stamp at all | Same file, "no stamp at all (null) — resolved, unchanged, SAME reference (exactly as before this task)" |
| **(2) Stamp used on the three 012 fallbacks** — `not_cloud_document` | Same file, "AC1: not_cloud_document + a valid stamp — resolves to the stamp id..." — this is also **the no-Graph-round-trip case**: in `App.tsx`, `url ? await resolveDocumentIdentity(url) : {kind:'new', reason:'not_cloud_document'}` — the ternary's RHS (the only network call in this whole flow) never executes when `url` is falsy, and `applyStampPrecedence` itself makes no network call ever (pure function) — so this path is structurally, not just empirically, zero-network |
| **(2) Stamp used on the three 012 fallbacks** — `not_resolvable` | Same file, dedicated test — resolves via the stamp |
| **(2) Stamp used on the three 012 fallbacks** — `not_spaarke_document` | Same file, dedicated test — resolves via the stamp |
| **(3) Disagreement ⇒ `identity_conflict`** | Same file, "AC3: resolved URL whose id DISAGREES with the stamp — identity_conflict (kind: \"conflict\"), no default save target" — asserts `result` equals `{ kind: 'conflict' }` |

**Everything else (012's own `conflict`, `indeterminate`, `denied`, `error`) is passed through
unchanged** — the owner's precedence names exactly the three `'new'` reasons as the stamp's *only*
fallback trigger; none of these four outcomes is in that list. Pinned by a parameterized test
(`it.each`) asserting all four return the exact input reference unchanged even when a valid stamp is
present.

### The null path (AC4)

`WordAdapter.readDocumentStamp.test.ts`'s "null path" describe block: absent (no matching part),
malformed XML, non-GUID-shaped text, the empty GUID, a foreign-namespace part, `getByNamespaceAsync`
failing, `getXmlAsync` failing, `customXmlParts` itself absent, and — the specific "two disagreeing
ids" case — two parts carrying **different** valid GUIDs. All nine resolve to `null`, none throw.
`documentIdentityService.stampPrecedence.test.ts` then separately proves that a `null` stamp leaves
every non-`'resolved'` outcome **unchanged, by reference** — i.e. "behaviour is exactly as before
this task" is not just claimed, it is the literal same object.

## 4. Design decision: `identity_conflict` reuses the existing `kind: 'conflict'`

Not a new discriminated-union member. Two reasons, both cited in the function's own doc comment:

1. **The server's own `resolve-identity` response already uses the literal string
   `'identity_conflict'`** for a *different* case (a row already holding this file's item id under a
   different drive — 039 §5 / `sprk_graphitemid_uk`), and `documentIdentityService.ts` already mapped
   that to `kind: 'conflict'` before this task touched the file (`resolveDocumentIdentity`, the
   `response.reason === 'identity_conflict'` branch). The owner's use of the same term for the
   stamp/URL disagreement reads as intentional, not coincidental.
2. **`SaveModeSection.resolveSaveMode`'s existing `case 'conflict'`** already renders exactly the
   required treatment — `target: null`, no save-as-new offered, "Save disabled ... ask an admin,
   check again" — which is precisely the owner's words for this task's case too ("no default save
   target ... never a silent choice between them"). Root CLAUDE.md §11 (extend, don't duplicate)
   points the same direction.

**One nuance flagged, not fixed (out of this task's file scope):** `SaveModeSection`'s conflict
message text ("Spaarke already has a record for this file in a different storage location...") was
written for 012's own different-drive case and is worded slightly imprecisely for this task's cause
(a stamp that disagrees with where the file's URL resolves). The *behavior* is correct either way
(Save disabled, no default target); only the copy could be more specific about which of the two
causes fired. `SaveModeSection.tsx` is not in this task's file list and changing it wasn't required by
any acceptance criterion — recorded here for the main session to weigh, not applied.

## 5. Why `documentName`/`fileName` are empty on a stamp-only resolution

A stamp-only match (owner-decision branch 2) has no server call to source a name from — inventing one
would violate "a stamp is a hint, never an authorization" (014 §3) and AC1's "no Graph round-trip".
There is no existing BFF endpoint that resolves a bare `sprk_document` id to name/related-record
without either Graph (forbidden) or a new endpoint (out of scope — server side is task 014's, already
shipped; task 052/054 own the other two open server items and are explicitly not this task's).
`SaveModeSection.resolveSaveMode`'s `documentLabel: identity.documentName || identity.fileName || null`
and its `quoted()` helper already degrade a null label to "the existing document" — a pre-existing,
unmodified fallback. `useRelatedRecord` already treats a `null` `relatedRecord` as `'unassociated'` —
also pre-existing, unmodified. The actual authorization for the eventual save is unchanged: the
version-save endpoint's `OfficeVersionSaveAuthorizationFilter` still requires Dataverse `write` on
`existingDocumentId` at save time, exactly as it already does for the URL-resolved path.

## 6. ADR-044 / layering note

`shared/adapters/**` sits below `shared/taskpane/**` in this package (verified: no
`shared/adapters/**` file imports from `shared/taskpane/**` anywhere in the repo, before or after
this task). `WordAdapter.ts`'s own local GUID-shape normalization (brace-strip + lowercase, inside
`extractStampId`) exists **only** to let that function answer "is this GUID-shaped" — it is not a
second ADR-044 canonicalization boundary. `documentIdentityService.ts`, which already imports the
add-in's local `cleanGuid` (task 013 D-2's sanctioned ADR-012-Path-A duplicate), calls `cleanGuid()`
on the stamp id at both points it can reach saved state in `applyStampPrecedence` — the comparison
against `urlOutcome.documentId`, and the `documentId` field of a stamp-only `'resolved'` outcome. The
raw, adapter-returned id never crosses into `App.savedContext` or a save payload un-cleaned.

## 7. Gates

| Gate | Result |
|---|---|
| `npx tsc --noEmit --skipLibCheck` | **111 total / 0 production** — byte-identical to a freshly-measured baseline at this same commit (see §8; the sibling `@spaarke/auth` package must be built first, see below) |
| `npm run build` (placeholder env vars) | **exit 0**, zero webpack errors |
| `npx jest --ci` (full suite) | **10 failed / 44 passed suites, 84 failed / 647 passed tests** — the failed-suite/test counts are byte-identical to the documented baseline; all 35 new tests across the 3 new suites pass |
| Gated list (41 non-comment lines of `ci-gated-suites.txt`, run via `--runTestsByPath`) | **41 suites passed / 41 total, 473 tests passed / 473 total** |
| `eslint` on every touched file | Clean, except 2 **pre-existing** `@typescript-eslint/no-namespace` errors in `OutlookAdapter.ts` (inside the `declare global { namespace Office {...} } }` block, untouched by this task's diff — same finding task 013's note already recorded) |
| Grep: no `hostType === 'word'` added | Confirmed — zero matches in the diff |
| Grep: no `Word.Document.customXmlParts` / `Word.Document.settings` usage | Confirmed — the only matches are inside doc comments explaining why they are NOT used |
| Grep: no `@spaarke/ui-components` import | Confirmed — zero matches |

## 8. The tsc baseline needed the sibling `@spaarke/auth` package built first

A fresh `npm install` in this worktree left `shared/services/AuthService.ts` unable to resolve
`@spaarke/auth` (`Cannot find module`), because that sibling package's own `dist/` didn't exist yet
(`src/client/shared/Spaarke.Auth` has a `"build": "tsc"` script; nothing runs it automatically).
This showed up as tsc 112/1 and a failed `npm run build` — on **both** this worktree and a throwaway
`git worktree add` at the exact base commit `713710a39` (same 112/1, byte-identical error text via a
full diff), proving it was a pre-existing environment-setup gap, not something this task introduced.
Running `npm install && npm run build` once in `src/client/shared/Spaarke.Auth` resolved it
completely: tsc dropped to the documented **111/0**, file-for-file identical to the task brief's
stated breakdown, and `npm run build` went from a hard module-resolution error to exit 0. Recorded
here because the next task in this package that measures a "fresh" baseline will hit the same trap if
`Spaarke.Auth` isn't already built in its worktree.

## 9. Deviations from the POML

- **`<relevant-files>` named `shared/services/documentIdentityService.ts`; the real path is
  `shared/taskpane/services/documentIdentityService.ts`.** Used the real path (verified via `Glob`
  before writing anything).
- **Touched 4 files beyond the POML's 3-file `<relevant-files role="modify">` list**:
  `shared/adapters/OutlookAdapter.ts`, `outlook/OutlookHostAdapter.ts`, `shared/taskpane/App.tsx`,
  and `shared/taskpane/components/views/__tests__/SaveView.captureAtSave.test.tsx`. All four were
  **required**, not optional: adding a required `IHostAdapter` member and a required
  `HostCapabilities` field ripples to every implementer/literal (exactly the shape task 013's own
  note D-1 documented for `getDocumentUrl()`), and the precedence had to be wired somewhere that
  actually orchestrates both an adapter call and the pure function — `App.tsx` is where task 013
  already did exactly that for the URL-only path. This mirrors task 013's own precedent of touching
  files beyond its POML's list when a required-interface-member ripple demanded it.
- **Kept the new Office.js `customXmlParts` mock local to the two new adapter test files**, rather
  than adding a `setupWordCustomXmlParts` helper to the shared `shared/__mocks__/office-js.ts` (which
  `WordAdapter.test.ts` and other GATED suites also depend on). A shared-mock-file addition was
  considered and rejected: it would have touched a file with a higher blast radius for a
  single-capability need, and this project's `parallel-safe: true` / multi-worktree model makes
  minimizing footprint in shared infrastructure the safer default.
- **Added one clarifying comment mid-task** (`WordAdapter.ts`'s `extractStampId`, the
  `documentElement` null-check) after self-review flagged that `lib.dom.d.ts` types
  `documentElement` as non-nullable — the check is real defense against a degenerate DOMParser output
  the type declaration under-states (WHATWG spec: `Element?`), not AI-smell paranoia; recorded inline
  so a future reader doesn't "fix" it away.
- **Not touched, per the task's explicit scope boundary**: `SaveModeSection.tsx`'s conflict-message
  copy (see §4's nuance), any `.cs` file, task 052 (`problem+json`), task 054 (typed-name Email
  collision).

## 10. What I'd change in the main-session-owned files

- `src/client/office-addins/ci-gated-suites.txt`: add these three new suite paths under a new
  comment block (matching the file's existing per-task convention):
  ```
  # --- client-side stamp reader (task 051, FR-02) - green from creation --------
  # Pins: the reader promisifies the Common API (not Word.Document.customXmlParts),
  # every failure mode (absent/unparseable/disagreeing/unsupported) resolves null
  # never a throw, and the owner's precedence (URL wins, stamp is the fallback on
  # the three 012 "new" reasons, disagreement is identity_conflict).
  shared/adapters/__tests__/WordAdapter.readDocumentStamp.test.ts
  shared/adapters/__tests__/OutlookAdapter.readDocumentStamp.test.ts
  shared/taskpane/services/__tests__/documentIdentityService.stampPrecedence.test.ts
  ```
  New total: 44 suites / 508 tests (41 + 3 suites; 473 + 35 tests).
- `projects/spaarkeai-word-add-in-r1/tasks/TASK-INDEX.md`: 051 🔲 → ✅.
- `projects/spaarkeai-word-add-in-r1/current-task.md`: reset per root CLAUDE.md §7 (this was the
  active task; no dependent task was blocked on it per TASK-INDEX, so "next" is whatever the main
  session picks).
- `projects/spaarkeai-word-add-in-r1/CLAUDE.md`: no new Decisions Made row is strictly required (the
  2026-09-17 precedence + "051 owned here" rows already exist and this task fulfilled them as
  written) — optionally note task 051 closed, and optionally record the `SaveModeSection` message
  wording nuance (§4) as a small follow-up if the main session wants it tracked.
