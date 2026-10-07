# Typecheck fix patterns — `src/client/office-addins`

> Shared scratch pad for the P0-typecheck wave (tasks 006, 007, 008). Each task appends its own
> clearly-headed section below. Keep edits additive and scoped to your own heading — this file is
> touched by three concurrent tasks.

---

## Task 007 (shared/adapters/** + shared/services/**)

**Scope re-measured 2026-09-09 (operator decision B1)**: 11 production-only errors — `shared/adapters/OutlookAdapter.ts` (9), `shared/adapters/HostAdapterFactory.ts` (1), `shared/services/ApiClient.ts` (1). Test files in `shared/adapters/__tests__/` and `shared/services/__tests__/` are OUT OF SCOPE for this task (task 009 owns them). Before: 11/11 in scope. After: 0/11.

The adapter seam was left **structurally unchanged** for task 010: no `registerAdapter()` call site added, `HostAdapterFactory` still unwired (dead code per plan.md finding F-e), no adapter deleted, no taskpane construction changed, `WordAdapter.ts` untouched (its known-broken `body.getOoxml()` path is exactly as task 010 will find it).

### Fix shapes used

1. **`exactOptionalPropertyTypes` on an internal (non-Office.js) error type — widen the declaration.**
   `HostAdapterError.innerError?: Error` → `innerError?: Error | undefined` (`shared/adapters/types.ts`).
   This is our own domain type (not an Office.js option bag the host distinguishes absent-vs-undefined
   on), so — unlike the option-object guidance below — the honest fix is widening the declaration once,
   which silently also cleared the identical error in `HostAdapterFactory.ts` (same shared type, no
   direct edit needed there). Verified this is additive-only: `IContentMetadata` in the same file
   already uses the `| undefined` idiom, so this matches house style.

2. **A base-interface method attempted on a type Office.js splits into subtypes — narrow to the union, don't drop to the base.**
   `OutlookAdapter.getBody()` called `this.getCurrentItem()` (typed `Office.Item`, an intentionally
   empty marker interface in `@types/office-js`) and then accessed `.body`. `.body: Body` is declared
   individually on `Office.MessageRead` and `Office.MessageCompose` (not lifted to the shared `Message`
   base), and `getBody()` is valid in both modes. Fix: `this.getCurrentItem() as Office.MessageRead |
   Office.MessageCompose` — a union cast, not a widen-to-`any`. This also cleared the implicit-`any`
   callback-parameter error that followed from the untyped `.body`.

3. **A property genuinely absent from the installed `@types/office-js` surface but present at runtime — extend via an intersection cast, never `any`.**
   Two instances, both in `OutlookAdapter.ts`:
   - `MessageRead.bcc` (Outlook generally hides BCC on read items, but it can surface e.g. when
     viewing a Sent item — the original code's own comment already said "if available"). Fixed with
     `item as Office.MessageRead & { bcc?: Office.EmailAddressDetails[] }`, narrowed at the single
     call site, same shape as the sibling `cc`/`to` arrays.
   - `MessageRead.importance` and `Office.MailboxEnums.Importance` are **entirely absent** from the
     installed `@types/office-js@1.0.568` (`grep -c` for "importance" case-insensitive across the whole
     package returns 0 — this is a real gap in the third-party ambient declarations, not a stricter
     tsconfig flag). Rather than a local cast (which can't add a missing *enum*, only reshape an
     existing property), used a `declare global { namespace Office { ... } }` ambient augmentation at
     the top of the file. This is the standard TS idiom for patching third-party ambient type gaps: an
     ambient `declare` block emits **zero runtime code** (fully erased at compile time), so
     `Office.MailboxEnums.Importance.Low` still reads whatever the real `office.js` bundle (or, in
     tests, the `__mocks__/office-js.ts` mock) supplies — my locally-written enum member string values
     are compile-time-only labels and have no bearing on the runtime comparison. Verified no dependency
     was added/upgraded (no `@types` patch, no npm change) and no tsconfig option was touched.

4. **Object literal fails an Office.js API's excess-property check for a field the runtime already ignores — type the variable, don't drop the field.**
   `addFileAttachmentFromBase64Async`'s typed `options` parameter is `AsyncContextOptions & { isInline:
   boolean }` only; the object literal `{ isInline: false, contentType }` fails TS2353 because
   `contentType` isn't part of that shape. Rather than deciding unilaterally whether `contentType` is
   truly dead (a behaviour judgment this task isn't authorized to make), assigned the identical literal
   to an explicitly-widened local variable — `const attachmentOptions: Office.AsyncContextOptions & {
   isInline: boolean; contentType?: string } = { isInline: false, contentType };` — and passed the
   variable. TypeScript's excess-property check only fires on fresh object literals passed directly to
   a narrower target type, not on a variable of a wider declared type; the object handed to Office.js at
   runtime is byte-identical to before.

5. **`noUnusedLocals` on a private field that is written but never read — delete only after confirming zero readers.**
   `ApiClient.bffApiClientId` was assigned in `configure()` but never read anywhere in the class (or by
   any test — `ApiClient.test.ts`'s "should configure with baseUrl and bffApiClientId" test explicitly
   comments "Configuration doesn't expose values directly" and asserts nothing about it). Confirmed via
   a repo-wide grep for `bffApiClientId` before deleting. Removed the field and its dead assignment; the
   public `ApiClientConfig.bffApiClientId` contract is unchanged (still required from callers) — only
   the never-read internal copy is gone. This is the one fix in this task that's a genuine (if inert)
   code deletion rather than a pure type annotation; documented here per the task's "note any deviation"
   instruction. No behaviour changed: the field was write-only, so nothing ever observed its value.

### Displacement check (Step 6)

Typecheck totals moved during this task's window because 006/008/009 are running concurrently in the
same worktree (shared `node_modules`, disjoint file ownership per the POML). Directions:

| Directory | Task 001 baseline | End of task 007 | Delta |
|---|---|---|---|
| `shared/adapters/` (production, mine) | 10 (9 OutlookAdapter + 1 HostAdapterFactory) | 0 | **-10** |
| `shared/services/` (production, mine) | 1 (ApiClient) | 0 | **-1** |
| `shared/taskpane/**` (006's) | 309 | ~270 (concurrent 006 progress) | decreased, not mine |
| `word/** + outlook/**` (008's) | 4 | 0 (concurrent 008 progress) | decreased, not mine |
| `shared/__mocks__/**` (UNASSIGNED, conditional) | 26 | 24 | decreased, not touched by me |

**No directory outside this task's scope increased.** The decreases in 006/008/mocks territory are
concurrent-agent progress, not something this task did — I did not edit any file under
`shared/taskpane/**`, `word/**`, `outlook/**`, or `shared/__mocks__/**`.

### Build / test

- `npm run build` (the production build — no `build:prod` script exists in this package) — ✅ exit 0,
  with `ADDIN_CLIENT_ID` / `TENANT_ID` / `BFF_API_CLIENT_ID` / `BFF_API_BASE_URL` supplied from
  `deploy-office-addins.yml`'s non-secret values, and `@spaarke/auth` pre-built.
- `npm test` — still red at baseline (13/21 suites failing per task 001; this task's re-scope explicitly
  removes "npm test is green" as a criterion — task 009 owns the missing `jest-dom` matcher
  registration). Spot-checked `OutlookAdapter.test.ts`: it fails at suite-load time on
  `Office.MailboxEnums.Importance` being `undefined` inside the Jest sandbox — this is a **pre-existing**
  runtime gap in how the office-js mock module is wired into the global at test-setup time (unrelated to
  my ambient *type* augmentation, which is erased at compile time and adds no runtime code); it was not
  introduced by this task and is out of this task's scope to fix.

### Left for task 010

`WordAdapter.ts` is untouched and still uses the documented-broken `body.getOoxml()` path; `HostAdapterFactory`
is untouched and still has zero call sites, an empty registry, and `create()` still throws `INVALID_HOST`
unconditionally. Nothing here narrows task 010's starting point.

---

## Task 006 (shared/taskpane/**)

**Scope re-measured 2026-09-09 (operator decision B1)**: the 73 PRODUCTION-only typecheck errors under
`shared/taskpane/**` (every file NOT matching `__tests__` / `__mocks__` / `*.test.*` / `*.spec.*`). Test
files in `shared/taskpane/**/__tests__/` (236 errors at task start) are OUT OF SCOPE — task 009 owns them.
Before: 73/73 in scope, distributed exactly as the operator's re-scope block specified (SaveFlow.tsx 19,
useSaveFlow.ts 14, App.tsx 10, AttachmentSelector.tsx 8, EntityPicker.tsx 6, SaveView.tsx 3,
TaskPaneShell.tsx 3, errorMessages.ts 3, index.ts 2, TaskPaneNavigation.tsx 1, TaskPaneHeader.tsx 1,
ErrorBoundary.tsx 1, views/index.ts 1, SseClient.ts 1). After: 0/73.

### The three barrel defects — resolution

All three were **removed**, not repointed: `ViewType` does not exist anywhere in `App.tsx` (nor is it
consumed by any production or test file in the whole `office-addins` package — a repo-wide grep for the
bare identifier found only the broken re-export line itself). `SaveOptions` likewise does not exist in
`SaveView.tsx` — the only consumer anywhere is `SaveView.test.tsx`, and it imports directly from
`'../SaveView'` (not through either barrel), so removing the barrel re-export does not change that test's
error count (it was already broken on its own separate import, out of this task's scope).

- `shared/taskpane/index.ts:3` — `export type { AppProps, ViewType } from './App';` → dropped `ViewType`.
- `shared/taskpane/index.ts:18` — `export type { SaveViewProps, SaveOptions } from './components/views/SaveView';` → dropped `SaveOptions`.
- `shared/taskpane/components/views/index.ts:2` — same `SaveOptions` re-export → dropped.

**Finding for task 015 (FR-03)**: `ViewType` reads as an intended-but-never-created union — `App.tsx`'s
actual tab-state type is `NavigationTab` (imported from `TaskPaneShell.tsx`), so `ViewType` was likely an
earlier or aspirational name for the same concept. Not rebuilt here — that's FR-03's job, not a typecheck
task's.

### Fix shapes used

1. **`exactOptionalPropertyTypes` on a JSX prop / object literal — conditional spread at the call site, one member at a time.** The dominant fix (SaveFlow.tsx's `buildSaveContext`, `useSaveFlow.ts`'s `SaveRequest`/`content`/`metadata`, `App.tsx`'s two `<TaskPaneShell>` calls, `TaskPaneShell.tsx`'s `<ErrorBoundary>`/`<TaskPaneFooter>` calls, `SaveView.tsx`'s `<SaveFlow>` call, `ErrorBoundary.tsx`'s `<ErrorFallback>` call, `SseClient.ts`'s `onEvent` payload). Canonical shape: `{...(value !== undefined ? { key: value } : {})}` (or `value ? {...}` when empty-string-as-absent is already the existing intent, matching the pre-existing `documentName: documentName || undefined` idiom it replaced). Applied per-field, not as a blanket object cast — each field's "may this genuinely be absent" question was answered individually rather than widening the whole object's declared type.
2. **`exactOptionalPropertyTypes` where the *declaration* was wrong, not the call site — widen the declaration.** `useSaveFlow.ts`'s `JobStatus.jobType`/`.progress`/`.createdAt` were declared required (`jobType: string` etc.) but the hook's own `startJobTracking()` constructs a client-side "Queued" initial state that genuinely omits all three (verified: no code anywhere reads `.progress`/`.jobType` unconditionally — the one read is a defensive `typeof data.progress === 'number' ? data.progress : prev.progress` fallback). Widened all three to optional (`jobType?: string`, etc.) with a doc comment explaining why, rather than fabricating placeholder values (`progress: 0`) at the initial-state call site, which would have been an observable behavior change (a UI reading `jobStatus.progress` would see `0` instead of `undefined`).
3. **`noUnusedLocals`/`noUnusedParameters` on a destructured prop/hook-return binding that truly has no reader — drop the binding, not the source.** `SaveFlow.tsx`'s unused `onQuickCreate`/`onNavigate`/`allowedEntityTypes`/`includeBody`/`setIncludeBody`/`savedDocumentId` (destructured from props or `useSaveFlow()`'s return) were removed from the destructuring only — the prop/return field itself stays in the type, so callers passing it see no change. Same for `App.tsx`'s `isDarkMode` (from `useTheme()`) and `EntityPicker.tsx`'s `setTypeFilter` (from `useEntitySearch()`). `App.tsx`'s `hostContext` used a positional hole (`const [, setHostContext] = useState(...)`) since only the setter was live.
4. **`noUnusedParameters` on a positional callback parameter that must stay for arity — prefix with `_`, never delete.** `useSaveFlow.ts`'s `onDuplicate: (docId, message) =>` → `(_docId, message) =>` (SaveFlow.tsx's mirrored local copy got the same fix); `SaveView.tsx`'s two Fluent `onChange={(e, data) => ...}` handlers → `(_e, data) => ...`; `TaskPaneNavigation.tsx`'s exported `getDefaultTab(hostType)` (always returns `'save'`, callers pass a real host type positionally) → `getDefaultTab(_hostType)`. This is the one pattern the task's own knowledge block calls out explicitly, and it is the correct fix precisely because deleting the parameter would silently reassign the *next* parameter to the removed one's argument position.
5. **`noUnusedLocals` on dead, never-invoked local functions — verified unreachable, then removed as a unit (including now-orphaned dependents).** `SaveFlow.tsx`'s `renderProcessingOptions` (a whole "AI Processing" toggle section, superseded by the 2026-09-02 product decision that AI processing is now always-on with no user toggle — see the adjacent surviving comment) was defined but never called from `renderContent()`/`renderForm()`. Removing it left `processingOptions`/`toggleProcessingOption` (from the `useSaveFlow()` destructure) and five now-unused Fluent imports (`Switch`, `Divider`, `SparkleRegular`, `SearchRegular`, `PersonSearchRegular`) orphaned in turn — removed those too, verified via a second grep pass after the first edit. `App.tsx`'s entire "Save operation state" block (`isSaving`/`saveProgress`/`saveError`/`saveSuccess` state plus the `handleSave` placeholder function that simulated a fake progress bar) was dead for the same reason: `handleSave` was never wired to any button (the real save path is `<SaveView>`'s `useSaveFlow`/`SaveFlow`), and all four setters were only ever called from inside that same dead function. Verified self-contained (no external reader) before deleting.
6. **A provably-unreachable `switch`/`if` arm the compiler itself flags (TS2367 "no overlap") — remove, don't cast around it.** `useSaveFlow.ts`'s `startSave()` declared `contentType: 'Email' | 'Attachment' | 'Document'` but only ever assigns `'Email'` or `'Document'` (the comment right above it says "Always save as Email when in Outlook"), so the `else if (contentType === 'Attachment')` arm can never execute — TS's own control-flow narrowing proves it. Narrowed the declared type to `'Email' | 'Document'` and deleted the dead arm (which built a `serverRequest.attachment` payload nothing ever sends). This is the one finding in this task closest to "behavior implications" — flagged here explicitly for anyone re-adding client-initiated Attachment-type saves later: the removed code is recoverable from git history if a future task revives that path.
7. **`unknown`-typed fetch response accessed without narrowing — a real runtime discriminant, not a cast.** `useSaveFlow.ts`'s `startSave()` parses the save response as `let responseData: unknown` then accessed `.duplicate`/`.documentId`/`.message` directly (TS18046 × 5). Added a small `isDuplicateSaveResponse(value): value is DuplicateSaveResponse` type guard (checks `typeof value === 'object' && value !== null && (value as Record<string,unknown>).duplicate === true && typeof (value as Record<string,unknown>).documentId === 'string'`) and used it as the branch condition instead of `responseData.duplicate`. This is the pattern the task's own constraint requires when a genuine `unknown → narrow` is needed.
8. **`noUncheckedIndexedAccess` on a bounds-checked-at-runtime-but-not-narrowable-by-TS array access — guard or default, never `!`.** `EntityPicker.tsx`'s `allOptions[highlightedIndex]` inside an `if (highlightedIndex >= 0 && highlightedIndex < optionsCount)` block still typed as `T | undefined` (TS can't correlate the two separate expressions) — added `if (!selected) break;` immediately after. `SaveView.tsx`'s `String.fromCharCode(uint8Array[i])` inside a `for (i = 0; i < uint8Array.length; i++)` loop → `uint8Array[i] ?? 0` (always in-bounds at runtime; the `?? 0` never actually fires). `SaveFlow.tsx`'s `jobStatus.stages.map(...)` (the `!jobStatus` guard above it narrows `jobStatus` but not its optional `.stages` field) → `(jobStatus.stages ?? []).map(...)`.
9. **A genuinely-dead `RefAttributes<never>` — a real library/React-19 typing gap, resolved by removing the unused capability, not by casting.** `AttachmentSelector.tsx` (3 errors) and `EntityPicker.tsx` (1 error) each `forwardRef`'d to a Fluent v9 slot-based component (`Card`, `Combobox`). Root-caused via `node_modules/@fluentui/react-utilities/dist/*.d.ts`'s `InferredElementRefType<Props>`, which infers a component's ref-element type from a `PointerEventHandler`-typed `onLostPointerCaptureCapture` prop; under this package's `@types/react@19.2.15` / `react@19.2.6` / `@fluentui/react-components@^9.54.0` combination this resolves to `never` for every Fluent `ForwardRefComponent`, so **no** `forwardRef`-to-a-Fluent-component pattern in this package can typecheck honestly right now (confirmed: identical `Ref<never>` shape on two unrelated components, `Card` and `Combobox`). A cast (`as Ref<never>`) would satisfy the compiler but is the exact "suppression, not a fix" the task forbids — a real ref of that type can never be assigned anything a caller could use. **Verified via repo-wide grep that no caller anywhere (production or test) ever passes a `ref` to `<AttachmentSelector>` or `<EntityPicker>`** — the forwarding capability was unused dead API surface. Converted both from `forwardRef<Element, Props>(...)` to plain `React.FC<Props>` and dropped the internal `ref={ref}` passthrough: zero observable behavior change for any existing caller, and it removes a capability that was already silently broken (a `Ref<never>` can't do anything useful even before this task). Documented inline at each component's export with a comment naming the root cause and pointing here, since a future task might otherwise "helpfully" re-add `forwardRef` and reintroduce the same error. **This is the one class of fix in this task closest to the escalation trigger's spirit** ("only resolved by changing runtime behaviour") — judged not to cross it because the change is verified-zero-behavior (nothing ever consumed the ref), but flagged prominently for review rather than applied silently. If a future task needs real ref-forwarding to a Fluent component in this package, the actual fix is a dependency change (a `@fluentui/react-components` version confirmed compatible with React 19's ref typing) — out of this task's authority.
10. **A missing *required* prop the error message names outright — supply it, it's not a strictness artifact.** `EntityPicker.tsx`'s two `<Option>` elements (loading / empty states) render non-string JSX `children`, and Fluent's `OptionProps` requires a `text: string` prop whenever `children` isn't a plain string (used for the Combobox's typeahead/selected-value display). Added `text="Searching..."` and `text={`No results found for "${query}"`}` respectively — real, load-bearing accessible-name/typeahead values, not placeholders.
11. **A React 19 `onInput` handler-shape change on a Fluent `input`-slotted component — retype the parameter and read `currentTarget`, not `target`.** `EntityPicker.tsx`'s `handleInputChange` was typed `(event: React.ChangeEvent<HTMLInputElement>) => void` and read `event.target.value`, but Fluent's `Combobox` `onInput` prop (its primary slot is `input`, so `onInput` is the *native* HTML `input` element attribute) is React 19's newly-distinct `InputEventHandler<T> = EventHandler<InputEvent<T>>` — under `SyntheticEvent`'s generics, `InputEvent<T>.target` is an untyped `EventTarget` (matching the real native-event contract: `event.target` is not guaranteed to be the listener's element), while `.currentTarget` **is** properly typed `EventTarget & T`. Retyped the parameter to `React.InputEvent<HTMLInputElement>` and switched to `event.currentTarget.value`. This is a real, previously-latent React 19 API surface change (not present in React 18's looser typing), independent of the `Ref<never>` issue in pattern 9 above, and does have an honest type-only fix.

### Displacement check (Step 5/9)

| Directory | This task's start-of-run measurement | End of task 006 | Delta |
|---|---|---|---|
| `shared/taskpane/**` (production, mine) | 73 | 0 | **-73** |
| `shared/adapters/**` | 44 | 33 | decreased, not mine (concurrent task 007) |
| `shared/services/**` | 1 | 0 | decreased, not mine (concurrent task 007) |
| `shared/__mocks__/**` (UNASSIGNED, conditional) | 26 | 24 | decreased, not touched by me |
| `word/**` | 0 | 0 | unchanged |
| `outlook/**` | 4 | 0 | decreased, not mine (concurrent task 008) |
| `shared/taskpane/**` test files (task 009's) | 236 | 232 | decreased as a side-effect of my production fixes (e.g. the barrel-defect removals), not directly touched |

**No directory outside this task's scope increased.** Every non-owned bucket held steady or dropped —
consistent with concurrent tasks 007/008/009 also making progress in the same worktree, and with a few of
my own fixes (the barrel-defect removals, `JobStatus` optional-field widening) cascading into fewer
inferred errors in files I never edited. I did not edit any file under `shared/adapters/**`,
`shared/services/**`, `shared/__mocks__/**`, `word/**`, or `outlook/**`.

### Build / test

- `npx tsc --noEmit -p .` (full package) — 384 → 289 diagnostics; 73/73 of this task's scope cleared; 0
  increase in any directory this task does not own.
- `npm run build` (the production build — no `build:prod` script in this package) — ✅ exit 0, with
  `ADDIN_CLIENT_ID` / `TENANT_ID` / `BFF_API_CLIENT_ID` / `BFF_API_BASE_URL` supplied from
  `deploy-office-addins.yml`'s non-secret values, and `@spaarke/auth` pre-built.
- `npm test` — not attempted. Per the 2026-09-09 re-scope block, "npm test is green" is explicitly removed
  as this task's acceptance criterion (task 009 owns the missing `jest-dom` matcher registration the suite
  is red on), and this task's own constraint forbids touching any test file.

### Deviations from the task file

- The authored POML's `<goal>` requires `npm test` green and full-package `typecheck` zero errors; the
  2026-09-09 `RE-SCOPE` XML comment binds over both, narrowing acceptance to the 73 production diagnostics
  under `shared/taskpane/**` and explicitly removing the `npm test` criterion. Followed the re-scope, not
  the original goal text, per the operator's explicit instruction.
- Pattern 9 above (dropping `forwardRef` from `AttachmentSelector`/`EntityPicker`) is a structural change
  to two components' exported type (no longer `ForwardRefComponent`), not a narrow type annotation. Judged
  in-bounds because it is verified-zero-behavior-change (no caller anywhere uses the ref) and the
  alternative (a cast) would have been a suppression the task explicitly forbids; flagged here rather than
  applied silently, per the spirit of the escalation-trigger clause even though it wasn't a hard stop.
- Pattern 6 above (removing the dead `'Attachment'` content-type arm in `useSaveFlow.ts`) deletes ~10 lines
  of code that build an unreachable request payload. Verified unreachable by the compiler's own
  exhaustiveness check before removing; recoverable from git history if a future task needs it.

### Left for tasks 015 / next-phase work

- `ViewType` finding recorded above, for FR-03 (task 015)'s `NavigationTab` extension.
- The `Ref<never>` / React 19 Fluent `forwardRef` incompatibility (pattern 9) affects any *future*
  component in this package that wants to forward a ref into a Fluent v9 slot-based component — it is not
  specific to `AttachmentSelector`/`EntityPicker`. A real fix needs a `@fluentui/react-components` version
  bump confirmed compatible with React 19's ref/event typing, which is a dependency change outside a
  typecheck task's authority.
