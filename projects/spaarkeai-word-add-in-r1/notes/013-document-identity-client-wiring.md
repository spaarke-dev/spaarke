# Task 013 — FR-01 client: getDocumentUrl capability + document identity client wiring

> **Rigor**: FULL · **Model tier**: sonnet @ effort high · **Step mode**: directional
> **Branch**: `worktree-agent-ac823a302d4e33096` (isolated worktree off `work/spaarkeai-word-add-in-r1`)
> **Commits**: `74f255eb7` (implementation), `fc7b81b66` (Step 9.5 self-review refactor)

---

## Summary

`HostCapabilities.canGetDocumentUrl` + `IHostAdapter.getDocumentUrl()` added; `WordAdapter`
implements it against `Office.context.document.url` (exact bytes, no reshaping); `OutlookAdapter`
reports it unsupported via a typed `CAPABILITY_NOT_SUPPORTED` `HostAdapterError`, matching the
existing `getAttachmentContent()` convention. A new `documentIdentityService.ts` calls task 012's
`POST /api/documents/resolve-identity` and maps every documented server outcome to a discriminated
union (`resolved | new | conflict | indeterminate | denied | error`) — it never throws. `App.tsx`
threads the resolved identity into `savedContext` (merged, not replaced), gated only by
`capabilities.canGetDocumentUrl`, never by `hostType`.

---

## Deviations from the POML (documented per step 11)

### D-1: `outlook/OutlookHostAdapter.ts` — a third `IHostAdapter` implementer, not in the POML's file list

The POML's `<relevant-files>` lists only `WordAdapter.ts` and `OutlookAdapter.ts` as the adapters
to modify. Adding a required interface member (`getDocumentUrl()`) to `IHostAdapter` breaks
**every** implementer, and `grep -rn "implements IHostAdapter"` found a third:
`outlook/OutlookHostAdapter.ts`. This class is confirmed **dead code** — the only other reference
to its name in the whole package is a JSDoc `@example` comment string in
`HostAdapterFactory.ts:66` (`* import { OutlookHostAdapter } from '../outlook/OutlookHostAdapter';`),
not an actual import. `HostAdapterFactory` registers `shared/adapters/OutlookAdapter.ts` at the
real task-pane entry points. It is the same situation task 010 found for `word/WordHostAdapter.ts`
(which task 010 deleted) — except task 010's scope was Word-only, so this Outlook duplicate was
never cleaned up.

**Decision**: minimal compile-satisfaction patch only — a one-method stub matching the file's own
existing convention (raw `throw new Error(...)`, not the typed `HostAdapterError` the two live
adapters use), with a `@remarks` block stating plainly that the class is dead code. Did **not**
delete the file (out of scope — mirrors task 010's precedent but deleting it is that kind of
cleanup task's job, not a side effect of adding one interface member) and did **not** give it the
same rigor as the two live adapters (no tests — it already had none).

**Recommendation for the main session / TASK-INDEX**: file a small follow-up cleanup task
("delete `outlook/OutlookHostAdapter.ts`, mirroring task 010's `word/WordHostAdapter.ts` deletion")
so this doesn't accumulate a second compile-satisfaction patch the next time `IHostAdapter` grows
a member.

### D-2: ADR-044 (`cleanGuid`) — a local duplicate, not the canonical `@spaarke/ui-components` import

The task's own constraint says: *"Canonicalize to bare lowercase via the shared `cleanGuid`; no
hand-rolled brace stripping... use the shared helper."* ADR-044's canonical implementation is
`@spaarke/ui-components`'s `PolymorphicResolverService.cleanGuid` — including ADR-044's own
documented "deep-import fallback" for a stale barrel, which is *still* an import **from**
`@spaarke/ui-components`. This package's own project CLAUDE.md ADR-012 decision (2026-09-04) is a
Path A project-scoped exception that forbids importing `@spaarke/ui-components` **at all**
(assumes React 19 APIs this add-in doesn't share; some components are Xrm-bound, which doesn't
exist in an Office host — NFR-03). So the two constraints are in direct tension for this package.

**This is not a novel conflict** — it is the exact shape the project already resolved once, for
`shared/taskpane/services/todoChoices.ts`, documented in the project CLAUDE.md's Decisions Made
table: *"a sanctioned duplicate... the add-in has no Xrm, so it mirrors the mapping (§11
justified)."* Applying that same precedent: `shared/taskpane/utils/cleanGuid.ts` is a
character-for-character copy of the canonical algorithm (strip braces + whitespace, lowercase,
no-op on already-clean input) — it preserves ADR-044's actual invariant (bare-lowercase at the
boundary); it just can't route through the forbidden import. Full reasoning is in the file's own
header comment.

**This is CLAUDE.md §6.5 Path A**, applying an already-approved exception rather than opening a
fresh one. Per this task's boundaries I cannot edit the project CLAUDE.md myself (main-session-owned).

**Recommendation for the main session**: add a Decisions Made row for this, mirroring the
`todoChoices.ts` entry, e.g.: *"ADR-044 → Path A (continuation of the 2026-09-04 ADR-012 exception):
`shared/taskpane/utils/cleanGuid.ts` is a sanctioned local duplicate of the canonical `cleanGuid`,
since ADR-012's Path A blocks importing `@spaarke/ui-components` (the canonical source) at all."*

### D-3: `App.savedContext`'s type widened to `Partial<SavedTodoContext> & DocumentIdentityContext`

Not a deviation from the POML's letter (the POML's step 6 just says "thread... into
`App.savedContext`, joining the existing save-context shape"), but worth recording the design
choice: a resolved Word document can have **no** related record (unassociated), in which case only
`documentId`/`documentName`/`fileName` are known and `SavedTodoContext`'s `regardingEntity`/
`regardingRecordId` (both required in that type) must stay unset. Making them optional on the
merged `AppSavedContext` type, and narrowing back to the strict `SavedTodoContext` only when
passing to `CreateTodoView` (`App.tsx`'s new `todoRegardingContext` derived const), keeps both call
sites type-safe without touching `CreateTodoView.tsx` (not in the POML's file list).

### D-4: Step 9.5 self-review — extracted a pure `applyDocumentIdentityOutcome` function

Acceptance criterion 6 ("...the resolved document id, name and related record land in
`App.savedContext`") had no direct automated proof: this codebase has no `App.test.tsx` (verified —
none exists), so the merge logic was only provable by code inspection if left inline in the
`useEffect`. Extracted the merge (`DocumentIdentityOutcome` → `savedContext` patch, including the
Create-To-Do regarding-field seeding) into `documentIdentityService.applyDocumentIdentityOutcome` —
a pure function with no Office.js / network / React dependency — and added 9 unit tests (merge-not-
replace, no-related-record, missing name, and reference-identity preservation for every
non-`'resolved'` outcome kind, so a `setState` updater can bail out of a re-render). `App.tsx` now
calls this function instead of duplicating the merge inline. This is the strongest automated proof
available for criterion 6 without building an App-level render harness, which doesn't exist in this
codebase and is out of this task's scope to create.

---

## Notable design decisions (not deviations, but worth recording)

1. **`getDocumentUrl()`'s two "no URL" shapes are deliberately asymmetric.** `WordAdapter` (capability
   present, method valid) resolves `null` for an unsaved document — a defined, non-throwing result
   (POML step 3). `OutlookAdapter` (capability absent) **rejects** with a typed
   `CAPABILITY_NOT_SUPPORTED` `HostAdapterError` — matching the one existing precedent for "a
   capability method called on the wrong host with no sane empty sentinel"
   (`WordAdapter.getAttachmentContent()`). This reading of the constraint ("not undefined, not a
   thrown raw Error, not a silent empty string") treats "a thrown **raw** `Error`" as the forbidden
   shape, not "a thrown **typed** `HostAdapterError`" — the latter is the codebase's own established
   failure-signaling convention (used by `ensureInitialized()`, `getReadItem()`/`getComposeItem()`,
   and `getAttachmentContent()` already).
2. **The client-side "not absolute" guard in `resolveDocumentIdentity` does NOT block `file://` URIs.**
   Task 012's live run (notes §8 row #4) fed the server `file:///C:/…/brief.docx` and got
   `resolved:false, reason:not_cloud_document` back **without a Graph call** — the server makes that
   determination, not the client. The guard only short-circuits empty/whitespace/schemeless strings
   (an unsaved document, or genuinely malformed input) — a `file://` URI is a real absolute URI and is
   sent through, matching the documented live contract. Covered by an explicit test
   (`'DOES send a file:// URI to the server rather than blocking it locally'`).
3. **A plain 403 (no `sdap.access.error.system_failure` reasonCode) maps to a new `'denied'` kind**,
   distinct from `'error'`. Not explicitly named in the acceptance criteria, but the briefing's "any
   other 403 means access denied" plus task 012's own §4 residual-disclosure note made this the
   correct, honest mapping rather than folding it into the generic `'error'` bucket.
4. **Identity resolution runs once per pane session**, gated on `isAuthenticated` transitioning true
   (a `useRef` guard, not a `useState`, to avoid an extra re-render) — this also covers the
   "sign in after pane load" path, since `App.tsx`'s `handleSignIn` doesn't re-run `initializeApp()`.

---

## Acceptance criteria — evidence

| # | Criterion | Evidence |
|---|---|---|
| 1 | `HostCapabilities` carries exactly one new field | `git show 74f255eb7 -- shared/adapters/types.ts`: one line added, `canGetDocumentUrl: boolean` |
| 2 | `getDocumentUrl()` declared on `IHostAdapter`, implemented by both adapters, no unimplemented member | `npm run typecheck`: 0 production errors (would fail to compile with an unimplemented interface member) |
| 3 | Word + saved doc → returns the URL, `canGetDocumentUrl` true | `WordAdapter.test.ts` "getDocumentUrl (FR-01 / task 013)" — 4 tests, all passing |
| 4 | Outlook → `canGetDocumentUrl` false, `getDocumentUrl()` signals unsupported (not undefined/raw Error/empty string) | `OutlookAdapter.test.ts` "getDocumentUrl (FR-01 / task 013)" — 3 tests, all passing (rejects typed `HostAdapterError`, confirmed `not.toBeInstanceOf(Error)`) |
| 5 | Word + unsaved doc → defined "no URL" result, not a throw | `WordAdapter.test.ts` "returns null for an unsaved document" (both undefined-url and empty-string-url cases) |
| 6 | Resolved identity lands in `App.savedContext` | `documentIdentityService.test.ts` "applyDocumentIdentityOutcome" (9 tests) + `App.tsx`'s effect calling it; no `App.test.tsx` exists in this codebase to test at the render level — see D-4 |
| 7 | Non-Spaarke document → treated as new, no error state | `documentIdentityService.test.ts` "new document" describe block; `applyDocumentIdentityOutcome` returns `prev` unchanged for `'new'` |
| 8 | Resolver failure → handled error, save flow not blocked | `documentIdentityService.test.ts` "error" describe block (never throws); `App.tsx`'s effect has its own try/catch that only `console.warn`s, never sets `error`/blocks `isInitializing` |
| 9 | No NEW `hostType ===` conditional for document identity | `grep -n "hostType ===" shared/taskpane/**`: two pre-existing matches in `App.tsx` (LinkedTodosBanner logic, unrelated, unchanged by this task) + pre-existing matches in `SaveFlow.tsx`/`useSaveFlow.ts` (untouched files). Zero new matches. |
| 10 | Every GUID canonicalized via shared `cleanGuid`, no hand-rolled stripping | `documentIdentityService.ts` routes `documentId` + `relatedRecord.id` through `cleanGuid` at the point identity crosses into client state; see D-2 for the local-duplicate resolution |
| 11 | No MSAL construction added | `grep -rn "PublicClientApplication\|createNestablePublicClientApplication\|WithClientSecret"` over the changed files: no matches. `documentIdentityService.ts` uses `apiClient` (→ `AuthService` → `@spaarke/auth`) |
| 12 | `npm run typecheck` clean, `npm run build` succeeds, all adapter/service tests pass | See "Build/test evidence" below |

---

## Build/test evidence

**Typecheck** (`npm run typecheck` = `tsc --noEmit --skipLibCheck`):
- Before this task: **0 production errors**, 284 test-file-only errors (all pre-existing, per project CLAUDE.md's "consciously accepted" 2026-09-09 decision).
- After this task: **0 production errors**, 284 test-file-only errors — **the exact same set** (`diff` clean against the pre-task baseline, normalized for line/column numbers).

**Build** (`npm run build`, the production build — no `build:prod` script in this package):
exit 0, with dummy env vars (`ADDIN_CLIENT_ID`/`TENANT_ID`/`BFF_API_CLIENT_ID`/`BFF_API_BASE_URL`) —
the real build requires these at bundle time; no code in this task's diff depends on their values.

**Jest** (`npm test`):

| | Before (baseline) | After (this task) |
|---|---|---|
| Suites | 22 total, 12 passed, **10 failed** | 24 total, 14 passed, **10 failed** |
| Tests | 380 total, 296 passed, **84 failed** | 424 total, 340 passed, **84 failed** |

The failing-suite list is **byte-identical** before/after (`diff` clean on the sorted `PASS`/`FAIL`
lines): `OutlookAdapter.test.ts`, `ApiClient.test.ts`, `EntityPicker.test.tsx`, `SaveFlow.test.tsx`,
`TaskPaneNavigation.test.tsx`, `TaskPaneShell.test.tsx`, `SaveView.test.tsx`, `ShareView.test.tsx`,
`useEntitySearch.test.ts`, `useSaveFlow.test.ts` — the same 10 pre-existing, owned/tracked failures
named in `notes/043-office-addins-ci-gate.md`. Only two suites were **added**, both new and both
100% green: `documentIdentityService.test.ts` (30/30) and `cleanGuid.test.ts` (7/7).

Per-file detail for the two modified adapter test files:

| File | Before | After | Delta |
|---|---|---|---|
| `WordAdapter.test.ts` | 37/37 (gated, green) | **41/41** | +4 new tests, all passing; stays gate-eligible |
| `OutlookAdapter.test.ts` | 11/29 (ungated, red — pre-existing compose-mode-detection defect) | **14/32** | +3 new tests, all passing; the SAME 18 pre-existing failures remain (compose-mode detection, unrelated to this task) |

**Linting**: `npm run lint` is a pre-existing broken script (targets a `src/` directory that
doesn't exist in this package — real code is under `shared/`/`outlook/`/`word/`; not something this
task touched or is in scope to fix). Ran `npx eslint` directly against every file this task
touched: **0 errors, 0 warnings** in all of them, except 2 **pre-existing** `@typescript-eslint/
no-namespace` errors in `OutlookAdapter.ts:53-54` — inside the `declare global { namespace
Office {...} } }` type-augmentation block that predates this task (untouched by this diff).

---

## CI gate allow-list (`ci-gated-suites.txt`) — recommendation, not applied

Per this task's boundaries ("If you add a NEW test file, say whether it belongs in that allow-list,
but do NOT edit CODEOWNERS or workflow files"), I did **not** edit
`src/client/office-addins/ci-gated-suites.txt` myself. Recommendation:

- **`shared/taskpane/services/__tests__/documentIdentityService.test.ts` (30/30, new)** — belongs
  in the allow-list. It is a pure-logic service test (no RTL rendering, no timeout-prone async DOM
  work — the same profile as the already-gated `communicationLookupService.test.ts` /
  `createTodoLauncher.test.ts` / `quickSaveHelpers.test.ts`), and it is the automated proof for this
  task's acceptance criteria 6-8.
- **`shared/taskpane/utils/__tests__/cleanGuid.test.ts` (7/7, new)** — belongs in the allow-list,
  same reasoning (pure-function test, zero flake surface).
- **`shared/adapters/__tests__/WordAdapter.test.ts`** — already gated; stays green (41/41) with this
  task's 4 new tests added. No allow-list change needed.
- **`shared/adapters/__tests__/OutlookAdapter.test.ts`** — still NOT eligible for promotion. Overall
  suite is still red (14/32) on the same 18 pre-existing compose-mode-detection failures from before
  this task (unrelated to `getDocumentUrl()`); this task's 3 new tests all pass but don't change the
  suite's gate-eligibility.

---

## Unverified

- **The four `<ui-tests>` in the POML** (live Word/Outlook host, dark-mode toggle) require a real
  Office host this environment does not have. **Not run, not claimed.**
- **`App.savedContext` receiving the resolved identity at the React-render level** is proven by
  construction (typecheck) + the extracted pure-function unit tests (D-4), but not by a render-based
  integration test — none exists for `App.tsx` in this codebase.

---

## TASK-INDEX recommendation

Task 013 status: **✅ complete**. All 12 acceptance criteria have evidence (11 direct, 1 — #6 — via
the D-4 pure-function extraction rather than a render-level test, documented above as the strongest
available proof). Zero test regressions (exact same 10 pre-existing failing suites / 84 failing
tests). 0 production typecheck errors, build green.

Follow-ups for the main session (not blocking this task):
1. Add a project CLAUDE.md Decisions Made entry for D-2 (ADR-044 Path A continuation).
2. Consider a small cleanup task to delete `outlook/OutlookHostAdapter.ts` (D-1).
3. Consider promoting `documentIdentityService.test.ts` + `cleanGuid.test.ts` into
   `ci-gated-suites.txt` (this task didn't edit that file itself, per its boundaries).
