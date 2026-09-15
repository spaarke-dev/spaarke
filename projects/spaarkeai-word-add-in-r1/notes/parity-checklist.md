# Task 040 — Outlook parity pass + capability-gating audit (FR-19 / NFR-10)

> Written 2026-09-15. **No live Office host was available for this task** (main-session override). Nothing
> below was exercised in a running Word or Outlook pane. Every "supported" / "gated-absent" / "broken"
> status rests on: (a) the adapter's `getCapabilities()` logic, read directly, (b) a passing unit test that
> exercises that logic (jest + jsdom + the mocked `Office.js` surface — not a real host), or (c) static code
> inspection. The **"Live check" column is `pending — task 042 UAT`** for every row without exception. The
> five `<ui-tests>` in this task's POML are **UNVERIFIED** — see §5.

---

## 1. Raw audit findings (Step 1 — before any change)

Grep sweep of `src/client/office-addins/shared/**` for host-type conditionals (host-name string
comparisons, `Office.context.host` reads, equivalent switches), run 2026-09-15 before any edit:

| # | File:line | What it was | What it became |
|---|---|---|---|
| 1 | `shared/taskpane/App.tsx:510` | `const hostType: HostType = hostAdapter.getHostType() === 'outlook' ? 'outlook' : 'word';` — a tautological ternary (the method already returns the narrow `HostType` union) | Simplified to `const hostType: HostType = hostAdapter.getHostType();` — no behavior change, just removes a no-op host-name comparison |
| 2 | `shared/taskpane/App.tsx:516` | `const indicatorTargetId = hostType === 'outlook' ? communicationId : undefined;` (LinkedTodosBanner gate) | Converted to `hostAdapter.getCapabilities().canShowLinkedTodos` (new capability) |
| 3 | `shared/taskpane/App.tsx:524` | `hostType === 'outlook' && indicatorTargetId !== undefined && (...)` (LinkedTodosBanner visibility) | Converted to `canShowLinkedTodos && indicatorTargetId !== undefined && (...)` |
| 4 | `shared/taskpane/components/SaveFlow.tsx:707` (original numbering) | `if (hostType !== 'outlook' \|\| !itemId) return;` (triage/auto-match-candidates fetch gate) | Converted to `if (!canSuggestRelatedRecords \|\| !itemId) return;` (new capability, threaded as a prop from `SaveView`) |
| 5 | `shared/taskpane/components/SaveFlow.tsx:1014,1020,1025,1127` (original numbering) | Four `hostType === 'outlook'` reads: item-type label ("Email"/"Document"), sender display, sent date, attachment selector visibility | **Left as-is** — classified as host-shaped DATA, not a capability gate (see §3). Comment added pointing here. |
| 6 | `shared/taskpane/components/views/SaveView.tsx:157,196` (original numbering) | `if (type === 'outlook') {...} else if (type === 'word') {...}` — outer scaffold deciding which `IHostAdapter` methods to call to populate host-specific data fields | **Left as-is** — the value-producing calls inside each branch are already capability-gated (`canGetAttachments`/`canGetSender`/`canGetRecipients`/`canGetDocumentContent`); the outer scaffold only picks which capability checks to run. See §3. |
| 7 | `shared/taskpane/hooks/useSaveFlow.ts:882,1011,1020` (original numbering) | Three `context.hostType === 'outlook'` reads deriving `contentType` ('Email'/'Document'), `sourceType` (legacy idempotency-key shape), `includeBody` | **Left as-is** — this is the task-override's own named example of legitimate host-shaped data ("which save content type to send"). See §3. |
| 8 | `shared/taskpane/components/TaskPaneNavigation.tsx` `TAB_CONFIGS.createTodo.availableFor` | `['outlook']` | **NOT CHANGED — flagged as a suspected stale parity gap.** See §2 headline finding below; this is the audit's most significant result. |
| 9 | `shared/taskpane/components/TaskPaneNavigation.tsx` / `TaskPaneShell.tsx` / `TaskPaneToolbar.tsx` — `TAB_CONFIGS[].availableFor: HostType[]` + `getAvailableTabs(hostType)` | Table-driven tab-shell routing | **Classified as legitimate** — the sanctioned task-015 single-table mechanism (not "conditionals scattered through views"); not converted to a per-tab capability boolean. See §3. |
| 10 | `shared/adapters/IHostAdapter.ts`, `HostAdapterFactory.ts`, `WordAdapter.ts:418`, `OutlookAdapter.ts:603` | Host-type checks inside adapter/factory internals (`Office.context.host`, `info.host !== Office.HostType.Word`, etc.) | **Legitimate per this task's own constraint** ("adapters know their own host") — not touched |
| 11 | `shared/taskpane/hooks/useRelatedRecord.ts:71` | A comment referencing the pattern `hostType === 'word'` as the thing to AVOID (already NFR-10-compliant code from task 026) | No change — not a real conditional |
| 12 | `@deprecated HostFeature` (`IHostAdapter.ts` — the `IHostAdapter.ts:248` line cited in the POML is now ~line 291, drift from earlier tasks editing the file) | Referenced by `outlook/OutlookHostAdapter.ts`'s `supportsFeature()` method | **Zero live call sites** confirmed — `OutlookHostAdapter.ts` itself is unreferenced dead code (only a `HostAdapterFactory.ts` doc-comment `@example` mentions its name; no import anywhere). See §4. |

---

## 2. Headline finding — Create To Do tab is likely stale-gated Outlook-only (NOT fixed)

**`TaskPaneNavigation.tsx`'s `TAB_CONFIGS` restricts the `createTodo` tab to `availableFor: ['outlook']`,
with a comment "Outlook only (a To Do is created from an email)."** This predates task 035 (FR-14), which
shipped a fully host-agnostic implementation:

- `CreateTodoView.tsx` takes a generic `IHostAdapter` and calls only `getSubject()` (supported on both
  hosts) — it contains **zero** host-type logic anywhere.
- `App.tsx`'s `todoRegardingContext` / `handleCreateTodo` already thread a Word document's `documentId`
  into the create-To-Do call exactly like Outlook's `communicationId` — see the FR-14 comment block at
  `App.tsx` around `handleCreateTodo` ("documentId is Word-only... communicationId is the Outlook
  counterpart").
- The BFF (`OfficeService.CreateTodoAsync`, task 035) has a document-carrier block independent of and
  parallel to the communication-carrier block (see `notes/035-todo-regarding-decision.md`).
- **Spec's own parity boundary** (`spec.md` Assumptions) lists **"To Do" under the Outlook-PARITY
  (both-hosts) set**, not the Outlook-ONLY set ("Outlook-only: email/attachment save, triage,
  linked-todos" — To Do is not in that list). FR-14's acceptance criterion states directly: *"the To Do is
  created with correct regarding for both Word (document + record) and Outlook (communication + record)."*

**This was NOT fixed in this task.** `TaskPaneNavigation.test.tsx`'s existing test "renders only the Save
tab for Word" explicitly pins `availableFor: ['outlook']` as correct behavior, asserting
`queryByRole('tab', {name: /create to do/i})` is **absent** for Word. This task's hard rule ("never delete
or weaken an existing test") blocks changing the data and the test together, which is what a correct fix
requires (they must land in the same change). A code comment was added at the `TAB_CONFIGS` entry
recording this finding and its evidence for the next task/owner to act on.

**Recommendation**: an explicit owner/main-session decision — either (a) confirm this is intentionally
Outlook-only for r1 (in which case spec.md's Assumptions bullet should be corrected to add "To Do
creation-tab-visibility" to the Outlook-only list, distinct from To Do creation ITSELF being usable via
some other Word entry point), or (b) file a follow-up task that changes `TAB_CONFIGS.createTodo.availableFor`
to `['outlook', 'word']` AND updates `TaskPaneNavigation.test.tsx`'s "renders only the Save tab for Word"
test in the same change.

---

## 3. "Legitimate host-shaped data, not a capability" — reasoning for each deliberately-unconverted case

The main-session override explicitly named one example as OK to leave as data ("which save content type to
send") and asked for an honest judgment call on the rest. Applying the same reasoning consistently:

| Site | Why it's data, not a capability |
|---|---|
| `useSaveFlow.ts` `contentType`/`sourceType`/`includeBody` derivation | Picks WHICH REQUEST SHAPE to build (Email vs Document) for the BFF — not whether a feature is available. The main session's own example. |
| `SaveFlow.tsx` item-type label, sender/sent-date/attachments rendering | By the time these props reach `SaveFlow`, `SaveView` has ALREADY capability-gated which VALUES got populated (`canGetSender`/`canGetAttachments`/`canGetRecipients`/`canGetDocumentContent` — see `SaveView.tsx:159,165,177,202`). `senderEmail`/`senderDisplayName`/`sentDate`/`attachments` are structurally empty on Word regardless of this check (`WordAdapter.getSenderEmail()` always returns `''`, `getAttachments()` always returns `[]`; `sentDate` is populated only via a Duck-typed `'getSentDate' in hostAdapter` check that doesn't even exist on `WordAdapter`). The `hostType === 'outlook'` prefix is consistent-with, not the load-bearing mechanism for, this being Word-safe. |
| `SaveView.tsx`'s outer `if (type === 'outlook') {...} else if (type === 'word') {...}` | Decides WHICH `IHostAdapter` methods to even attempt calling (`getAttachments`/`getSenderEmail`/`getRecipients`/`getSentDate`(duck-typed) vs `getDocumentContent`) — analogous to an adapter selecting its own behavior, except physically located in the view that owns the adapter reference. The actual VALUE-PRODUCING calls inside each branch are already capability-gated. Converting the outer scaffold to also read a capability would only relabel this same branch (there is no meaningfully different "capability" to name — it would just be `getItemType() === 'document'`, which is host identity, not a feature flag). |
| `TAB_CONFIGS[].availableFor: HostType[]` (Save/Find/createTodo tab visibility) | A **single declarative table**, not conditionals scattered through render logic — the sanctioned task-015 replacement for exactly this kind of branching (see `TaskPaneNavigation.tsx`'s header comment, "Renderer decision"). NFR-10's concern is many ad hoc `if (host===...)` checks peppered through view code; one central lookup table achieves the same anti-scatter intent without inventing a same-purpose boolean capability per tab. |

**Converted instead** (the two genuine capability gates found): `canShowLinkedTodos` (App.tsx's
LinkedTodosBanner) and `canSuggestRelatedRecords` (SaveFlow.tsx's "triage" auto-match-candidates fetch) —
both are true FEATURE on/off switches (an entire banner, an entire background fetch), not data-shape
selectors, and both map directly to spec's Outlook-only list ("linked-todos", "triage").

---

## 4. Capability inventory — every shared capability, per host

Legend: **S** = supported, **GA** = gated-absent (intentional, declared), **B** = broken. "Live check" is
`pending — task 042 UAT` for every row (no live host was available to this task).

| Capability | Word | Outlook | Evidence | Live check |
|---|---|---|---|---|
| `canGetAttachments` | GA (`false`, always) | S (read mode + Mailbox 1.8) | `capabilities.test.ts` "reports the full capability set honestly" (both adapters); pre-existing `OutlookAdapter.test.ts` `getCapabilities` describe block | pending — task 042 UAT |
| `canGetRecipients` | GA (`false`, always) | S (both modes) | `capabilities.test.ts` | pending — task 042 UAT |
| `canGetSender` | GA (`false`, always) | S (both modes) | `capabilities.test.ts` | pending — task 042 UAT |
| `canGetDocumentContent` | S (WordApi 1.3 requirement-set check) | GA (`false`, always) | `capabilities.test.ts`; `WordAdapter.test.ts` (existing, gated) | pending — task 042 UAT |
| `canGetDocumentUrl` | S (`true`, always — FR-01/task 013) | GA (`false`, always) | `capabilities.test.ts`; code path `WordAdapter.getDocumentUrl()` reads `Office.context.document.url` directly | pending — task 042 UAT |
| `canSaveAsPdf` | S (server-side conversion, `true` always) | S (`true` always) | `capabilities.test.ts` (both) | pending — task 042 UAT |
| `canSaveAsEml` | GA (`false`, always — Word has no email concept) | S (Mailbox 1.8 requirement-set check) | `capabilities.test.ts` | pending — task 042 UAT |
| `canInsertLink` | S (WordApi 1.3 check) | S (compose mode only; GA in read mode) | `capabilities.test.ts` (Word); `OutlookAdapter.test.ts` existing compose/read describe blocks | pending — task 042 UAT |
| `canAttachFile` | GA (`false`, always — Word has no attach concept) | S (compose mode only) | `capabilities.test.ts`; `OutlookAdapter.test.ts` | pending — task 042 UAT |
| `canOpenBrowserWindow` (task 027) | S (`OpenBrowserWindowApi` 1.1 runtime check) | S (same check) | `capabilities.test.ts` "canOpenBrowserWindow follows the runtime requirement-set check"; `SaveFlow.openRecord.test.tsx` (gated) | pending — task 042 UAT |
| `canComposeEmail` (task 036) | GA (`false`, always — `Office.context.mailbox` doesn't exist in Word; **owner-confirmed Outlook-only for r1**) | S (read mode + Mailbox 1.6) | `WordAdapter.composeEmail.test.ts` + `OutlookAdapter.composeEmail.test.ts` (both gated, pre-existing) | pending — task 042 UAT |
| **`canShowLinkedTodos`** (task 040, this task) | GA (`false`, always — no `sprk_communication` counterpart) | S (`true`, unconditional) | **NEW**: `capabilities.test.ts` — asserts both adapters directly. No component-level test exists (App.tsx has no test file); the feature is currently unwired at both entry points (`communicationId`/`onViewLinkedTodos` are not passed to `<App>` by `outlook/taskpane/index.tsx` or `word/taskpane/index.tsx` — confirmed by reading both files) | pending — task 042 UAT, and pending the future ribbon-wiring task that actually supplies `communicationId` |
| **`canSuggestRelatedRecords`** (task 040, this task) | GA (`false`, always — engine keys off captured-email sender/recipients, which Word has none of) | S (`true`, unconditional) | **NEW**: `capabilities.test.ts`. `SaveFlow.test.tsx` (red/ungated baseline, unaffected — no existing test exercised this code path before or after) | pending — task 042 UAT |

### Spec's Word-only / Outlook-only lists, marked explicitly gated-absent (not gaps)

| Spec item | Host it's absent from | Mechanism |
|---|---|---|
| Document identity (FR-01) | Outlook | `canGetDocumentUrl: false` (Outlook) |
| `.docx` save | Outlook | `canGetDocumentContent: false` (Outlook) |
| Version-save (FR-11) | Outlook | `documentIdentity` prop is `undefined` for a host without `canGetDocumentUrl` (App.tsx comment: "undefined = identity does not apply (Outlook)") |
| Email/attachment save | Word | `canGetAttachments: false`, `canGetSender: false`, `canGetRecipients: false` (Word) — `SaveView.tsx`'s outer scaffold never attempts the Outlook-shaped calls on Word |
| Triage (auto-match "Related to" suggestions) | Word | **`canSuggestRelatedRecords: false`** (Word) — newly formalized this task |
| Linked-todos (banner) | Word | **`canShowLinkedTodos: false`** (Word) — newly formalized this task |

### Tab-shell (not a `HostCapabilities` entry — the table-driven mechanism, §3)

| Tab | Word | Outlook | Note |
|---|---|---|---|
| Save | S | S | `availableFor: ['outlook','word']` |
| Find | S | S | `availableFor: ['outlook','word']` |
| Create To Do | **GA** (`['outlook']` only) | S | **See §2 — suspected stale, not fixed, flagged for owner decision** |
| Share / Search / Recent | GA both hosts | GA both hosts | Commented-out r1 placeholders (spec.md Assumptions: "share/recent tabs stay hidden") — unrelated to host parity |

---

## 5. UI-tests (POML `<ui-tests>`) — UNVERIFIED, no live host

No live Office host (Word or Outlook, desktop or web) was available to this task. **None of the five
`<ui-tests>` below were exercised.** All evidence is unit-test / static-code-level only, per §4's Evidence
column.

| Test | Status |
|---|---|
| "Shared capability works in both hosts" | **UNVERIFIED** — not exercised in either live host |
| "Gated-absent capability is hidden, not broken" | **UNVERIFIED** — not exercised in either live host |
| "No host conditional remains in views" | **Partially verified via grep** (see §6) — the grep itself was run and its output is exact; whether that grep result is *acceptable* (vs. requiring further conversion) is the judgment call documented in §3, not a live-host check |
| "Dark mode survives the refactor (ADR-021)" | **UNVERIFIED** — no view touched by this refactor changed any styling/token usage; no visual regression is plausible from the diff, but this is a static-inspection claim, not a rendered-and-observed one |
| "Keyboard focus is not stranded by gating (NFR-11)" | **UNVERIFIED** — the two new capability gates (`canShowLinkedTodos`, `canSuggestRelatedRecords`) hide an entire banner / skip a background fetch respectively, neither of which introduces a mid-tab-order interactive element that could strand focus when absent (the banner has no focusable children when unrendered; the auto-match fetch only populates optional suggestion cards, not a fixed layout slot) — but this reasoning is inspection-only, not a live tab-through |

---

## 6. Grep acceptance — actual output (not a claim of zero)

Command run (broadened beyond the POML's literal wording to also catch host-string comparisons against a
locally-named variable, e.g. `SaveView.tsx`'s `type`, not just `hostType`):

```
grep -rnE "===\s*'outlook'|===\s*\"outlook\"|===\s*'word'|===\s*\"word\"|!==\s*'outlook'|!==\s*'word'|Office\.context\.host" shared/taskpane/
```

Output (9 real code comparisons + 3 comment-only references, all under `shared/taskpane/**`, all
individually classified and reasoned in §1/§3 above):

```
shared/taskpane/components/SaveFlow.tsx:1032:            <Text weight="semibold">{hostType === 'outlook' ? 'Email' : 'Document'}</Text>
shared/taskpane/components/SaveFlow.tsx:1038:            {hostType === 'outlook' && (senderDisplayName || senderEmail) && (
shared/taskpane/components/SaveFlow.tsx:1043:            {hostType === 'outlook' && sentDate && (
shared/taskpane/components/SaveFlow.tsx:1145:      {hostType === 'outlook' && attachments.length > 0 && (
shared/taskpane/components/views/SaveView.tsx:163:        if (type === 'outlook') {
shared/taskpane/components/views/SaveView.tsx:202:        } else if (type === 'word') {
shared/taskpane/hooks/useSaveFlow.ts:887:        if (context.hostType === 'outlook') {
shared/taskpane/hooks/useSaveFlow.ts:1019:            context.hostType === 'outlook'
shared/taskpane/hooks/useSaveFlow.ts:1028:            includeBody: includeBody && context.hostType === 'outlook',
  (+ 3 comment-only lines: SaveFlow.tsx:1019, SaveView.tsx:157, useRelatedRecord.ts:71 — documentation, not code)
```

This is **not** "zero hits." Per §3, each remaining hit is host-shaped DATA (which content-shape/label/field
to show), not a capability gate — the two genuine capability gates found in this audit (linked-todos,
triage/auto-match) WERE converted (§1 items 2-4) and do not appear in this list. The main session should
review §3's reasoning and confirm or override this classification.

The POML's OWN acceptance criterion #1 also allows hits "confined to `shared/adapters/*Adapter.ts`" — none
of the above are in `shared/adapters/`; all nine are in `shared/taskpane/**`, which is why this is reported
as a deviation rather than a pass.

---

## 7. Other findings

- **`outlook/OutlookHostAdapter.ts`** is confirmed unreferenced dead code (only a `HostAdapterFactory.ts`
  `@example` doc comment names it; zero imports anywhere in `src/`). It still structurally implements
  `IHostAdapter`, so it required a mechanical update (both new capabilities added, mirroring the live
  `OutlookAdapter.ts`'s values) purely to keep `npx tsc --noEmit` at 0 production errors after this task's
  interface change — the same maintenance burden the file's own `canComposeEmail` comment already
  documents from task 036. **Cleanup candidate, not deleted** (per this task's override — out of scope).
- **`@deprecated HostFeature`** (`IHostAdapter.ts`): zero live call sites. Its only reference is inside
  `OutlookHostAdapter.ts` (dead code, see above).
- **`IHostContext` / `IContentData`** (also `@deprecated` in the same file): referenced only by
  `App.tsx` (`IHostContext`, still actively used for `hostContext` state — NOT dead, out of scope for this
  task's `HostFeature`-specific acceptance criterion) and `shared/adapters/index.ts` (barrel re-export) —
  noted for completeness, not acted on.

---

## 8. New test suite added

`shared/adapters/__tests__/capabilities.test.ts` — 5 tests, all passing. Lists both adapters'
`getCapabilities()` output exhaustively (not just the two new fields) so a future capability addition has
an obvious place to extend coverage. **This suite is new and not yet in `ci-gated-suites.txt`** — the main
session owns adding it (this task may not edit that file per its override).
