# Task 020 — Execution Notes (FR-06 filename defaults to Document Name)

## What had already moved since the POML was written

The POML's background section (written before tasks 023/024/026/027/036/040/046/049 landed) described the
server defect as `CreateDocumentRequest { Name = fileName, Description = request.Document?.Title ??
request.Document?.FileName }`. Re-reading `OfficeDocumentPersistence.CreateDocumentWithSpePointersAsync`
before touching it showed task 046(b) had ALREADY added a second overload carrying a separate
`documentName` parameter (`Name = documentName`) — but the *caller*, `OfficeService.SaveAsync`, only ever
populated that parameter for the **Email** branch (`OfficeEmailEnricher.GenerateEmlNames`). The **Document**
branch left it `null`, so `documentName ?? fileName` still fell through to the sanitized `.docx` filename —
the same bug, one layer deeper than the POML described. The fix was to extend the *existing* mechanism
(one new assignment in the `SaveContentType.Document` switch arm of `OfficeService.cs`), not to re-invent
the Name/Description split task 046(b) had already built.

## `getSubject()` returns Word's Title property, not the OS file name — and that is what "the filename" already means in this system

`WordAdapter.getSubject()` (unchanged by this task) returns `properties.title || 'Untitled Document'` — the
Word document's **Title metadata property**, which is normally blank for a real document, not the literal
`.docx` file name on disk. I considered whether FR-06's "the open document's own file name" required parsing
`Office.context.document.url` (via the existing `getDocumentUrl()`, added by task 013) to reach an actual OS
file name instead.

I did **not** do that, for two reasons:
1. `itemName` (→ `getSubject()`) is *already* what the entire existing save pipeline treats as "the
   document's name" — it is the exact value that becomes `effectiveDocumentName` → the `.docx`-suffixed
   upload filename today, with or without this task. Task 020's job (per the binding scope boundary) is to
   surface that EXISTING value as the field's visible default and make it editable/persisted correctly —
   not to change what counts as "the filename."
2. This is independently confirmed by `notes/025-residual-collision-surface.md`'s open question 8
   ("`Untitled Document.docx` is the largest multiplier of collisions") — that observation is only possible
   if the system's notion of "the document's name" is already `getSubject()`'s Title-or-fallback value, not
   an OS file name. Parsing `document.url` for a "more specific" file name is exactly the out-of-scope
   invention the binding scope boundary forbids (that question belongs to the owner, unresolved).

`stripDocumentExtension()` normalizes the visible default only — it strips a trailing `.docx`/`.doc` if
present (matching the POML's own `Acme Merger Agreement.docx` → `Acme Merger Agreement` UI-test example and
the existing test fixtures that already use extension-bearing `itemName` values), and is a no-op otherwise
(`Untitled Document` stays as-is).

## `SaveView.tsx` — re-verified, no change needed

The POML lists `SaveView.tsx` as `role="modify"`. After reading it, no change was required: `hostType` and
`itemName` (from `hostAdapter.getSubject()`) were already threaded through to `SaveFlow` unconditionally.
The new default/pencil behavior is entirely internal to `SaveFlow.tsx`, gated on the `hostType` prop it
already receives. Noted here rather than making a cosmetic edit for its own sake.

## `documentMetadata` — removed, not wired up

Per the constraint ("remove the dead top-level `documentMetadata` object... or add the matching server
member — pick one and say which"): **removed**. `document.title` (Document) and `email.subject` (Email,
unchanged) already carry the user-facing name to the server; adding a `SaveRequest.DocumentMetadata` member
to receive a field that duplicates data already sent elsewhere would be new BFF-contract surface with no
concrete behavior it uniquely enables (CLAUDE.md §11 cost-of-doing-nothing test fails for "add the member").

## Discovered but explicitly OUT of scope: `quickSaveHelpers.ts`

`src/client/office-addins/shared/taskpane/services/quickSaveHelpers.ts` (the Outlook ribbon one-click
quick-save path, task 037/FR-B2) builds its **own** `POST /api/office/save` body, independently of
`useSaveFlow.ts`, and it ALSO sends a top-level `documentMetadata: { name }` that `SaveRequest` does not
receive. This is the same dead-field pattern, but:
- it is **Email-only** (`contentType: 'Email'` is hardcoded in that file) — squarely inside this task's
  binding "do not adapt the Email or Attachment paths" boundary, and
- it is not named in the POML's `<relevant-files>`.

Left untouched. Flagging here (and in the final report) as a candidate for a small follow-up task, not
fixed under task 020.

## Escalation triggers — checked, neither fired

1. *"If separating the display name from the SPE filename would change behavior for Email or Attachment…
   STOP and escalate."* Verified empirically: `SaveFlow.documentName.test.tsx`'s "Outlook path untouched"
   suite asserts `email.subject` / `email.isNameSystemDerived` are byte-for-byte the same before and after
   this task for both the untouched-subject and user-typed-subject cases. Did not fire.
2. *"If `sprk_documentname` turns out to be enforced shorter than 850 characters anywhere in the write
   path… STOP and escalate rather than silently truncating."* Checked `DataverseServiceClientImpl.
   CreateDocumentAsync` (the literal Dataverse write) and `OfficeEndpoints.ValidateSaveRequest` (the
   request-validation method) — neither enforces a shorter bound than 850 on this value (the
   `[MaxLength(500)]` on `DocumentMetadata.Title` is a DataAnnotation that this Minimal API route does not
   auto-validate; `ValidateSaveRequest` is hand-written and checks association/content-type shape only, not
   string lengths). Did not fire. The 850-char bound was implemented at
   `OfficeDocumentPersistence.CreateDocumentWithSpePointersAsync` — the site the POML's step 5 names — rather
   than the (also-considered, and rejected) alternative of the shared `DataverseServiceClientImpl.
   CreateDocumentAsync` choke point, to keep the change scoped to the Office save path only.

## Coordinator follow-up (2026-09-16) — `hostType === 'word'` converted to a capability

The initial implementation gated the pencil/default UI on `hostType === 'word'` directly inside
`SaveFlow.tsx` — the NFR-10 view-level host-branching anti-pattern task 040 exists to find and convert
(`canShowLinkedTodos`, `canSuggestRelatedRecords`). A coordinator review caught it (task 040's own audit
predates task 020 and so could not have) and asked for the same conversion.

**Change**: added `HostCapabilities.canProvideDocumentName` (`shared/adapters/types.ts`), implemented
honestly in `WordAdapter` (`true`, unconditional — `getSubject()` always returns a usable value) and
`OutlookAdapter` (`false`, unconditional), mechanically mirrored into the dead `outlook/OutlookHostAdapter.ts`
for `tsc` (same reason task 036/040 already had to). Threaded from `SaveView` into `SaveFlow` exactly like
`canOpenRecord`/`canSuggestRelatedRecords`, replacing all four `hostType === 'word'` reads task 020 had
added (lazy state initializer, `defaultDocumentName` memo, the sync effect, the JSX render branch).
**Behavior is unchanged** for both hosts — this is a mechanism swap, not a feature change.

**Why Outlook is `false` and not "it can't supply a name"**: Outlook's `getSubject()` returns the email
subject, which is a perfectly good "name" in the abstract. The capability is `false` there because the
Document Name box already has a DIFFERENT, pre-existing contract (task 046 (b)): typing into it overrides
the subject and sets `email.isNameSystemDerived: false`, which gates the collision-avoiding unique suffix
on the stored `.eml` file. Defaulting/pencil-editing that box would make an untouched field register as
"user typed it," silently reintroducing the exact collision task 046 (b) closed. This reasoning lives in
the capability's own doc comment (`types.ts`) so it isn't lost to a future reader who only sees `true`/`false`.

**Tests updated**: `SaveFlow.documentName.test.tsx`'s `renderWord()` now passes `canProvideDocumentName`
explicitly (mirroring what a real Word adapter reports); `renderOutlook()` deliberately omits it (defaults
to `false`, mirroring a real Outlook adapter). `SaveFlow.test.tsx`'s two task-020 smoke tests updated the
same way. `capabilities.test.ts` (task 040's suite, already in `ci-gated-suites.txt`) extended with
assertions for the new field in both adapters' existing test cases — no new test file, per the coordinator's
explicit choice of extending the established suite.

**`notes/parity-checklist.md`** (task 040's artifact) updated: §4's capability table gained a row; a new §9
addendum records this follow-up (§1/§3/§6 are task 040's own audit-run record and were left as historical
fact, not rewritten).

## Environment gap found and fixed (not a code change)

A fresh `npm install` in this isolated worktree left `@spaarke/auth` (a `file:../shared/Spaarke.Auth`
workspace dependency) without its `dist/` build output, producing a spurious
`Cannot find module '@spaarke/auth'` production typecheck error (112 total / 1 production, vs. the stated
111/0 baseline). Root cause: `Spaarke.Auth` had never been built in this worktree — `npm install` alone does
not run a linked local package's own `build` script. Fixed by `npm install` + `npm run build` inside
`src/client/shared/Spaarke.Auth`, then re-running `npm install` in `office-addins`. Confirmed this is
environment-only: `git status`/`git diff` on `AuthService.ts` (the file the error pointed at) were empty
throughout — no code was touched to fix this. Typecheck reads 111 total / 0 production after the fix,
matching the stated baseline exactly.
