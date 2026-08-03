# Task 051 Completion — EmailComposer SendEmailDialog → FormModal/md; retire legacy SendEmailDialog (FR-14)

> RIGOR: FULL. Executed per task-execute protocol. **STATUS: ESCALATED — NOT COMPLETED.** Zero source files modified. Two independent, structural blockers were found during investigation (Steps 0–2 of the POML), each individually sufficient to trigger the task's own escalation trigger and CLAUDE.md §6. Per the read-first instruction ("a genuine remaining gap = STOP and report — do not fork or hand-compose SprkModal without reporting") and the escalation trigger ("a live consumer of the legacy `onSend` API... → STOP + surface... rather than silently reshape the send contract"), no code was changed. This file documents both blockers with full evidence, and the P1-escalation status (still OPEN, not closed).

## Summary of the two blockers (independent — either alone stops the task)

1. **Re-base blocker (Step 1):** `EmailComposer` (the engine `<SendEmailDialog>` wraps) is **self-chromed** when `mount="dialog"` — it renders its own full header (title + `ModalWindowControls`) and its own full footer (`ComposerActionBar`: Cancel / Save Draft / Send-with-mailbox-switcher, or Close/Reply/Forward/Edit in view mode) **unconditionally**, with no prop to suppress either. `SprkModal` (which `FormModal` composes) **also** renders its header unconditionally (`title` is a required string; the header `<div>` in `SprkModal.tsx` is not gated by any prop). Composing `FormModal` — or even hand-composing raw `SprkModal` — around `<EmailComposer mount="dialog">` as children produces **two stacked header rows and two competing footers**. This is not "extra footer actions FormModal can't host" (the anticipated failure mode) — it is deeper: the child owns complete chrome already. See "Finding 1" below for full evidence and why the fallback ("hand-compose SprkModal directly," "P2 precedent") also does not resolve this without forking `ComposerActionBar`'s logic into the wrapper.

2. **Retirement blocker (Steps 2–4):** Two live, non-test consumers depend on the legacy `components/SendEmailDialog/` module in ways deletion breaks immediately:
   - `src/pcf-safe.ts` (lines 27–28) — a **direct deep import** of `./components/SendEmailDialog` for both the `SendEmailDialog` component and the `ISendEmailPayload` type. This is inside `Spaarke.UI.Components` itself — deleting the folder breaks the package's own compile, and the task explicitly forbids editing `pcf-safe.ts`.
   - `Spaarke.DailyBriefing.Components/src/components/DailyBriefingApp.tsx` — imports `type ISendEmailPayload` from the **main barrel** (`@spaarke/ui-components`), which today is exported **only** by the legacy module (no equivalent name exists under `EmailComposer`). It also renders `<SendEmailDialog>` with the **fully legacy prop shape** (`onSearchUsers`, `onSend`, `defaultSubject`, `defaultBody`, `maxWidth`, `height`) — exactly the escalation trigger's "live consumer of the legacy `onSend` API." See "Finding 2" for why this is not a trivial port.

Both are documented in full below with grep evidence, exact line numbers, and file paths.

---

## Finding 1 — FormModal/SprkModal composition gap (Step 0/1)

### What FormModal expects (read: `SprkModal/presets/FormModal.tsx`)
`FormModal` is a thin `SprkModal` config: it owns `title` (header) and a fixed Cancel(left)/Save(right) footer (`footerStart`/`footer` slots), and expects `children` to be **only the form fields** — no header or footer of the child's own.

### What the canonical wrapper actually renders today (read: `EmailComposer/wrappers/SendEmailDialog.tsx`)
The wrapper is a raw Fluent `Dialog`/`DialogSurface`/`DialogBody` (NOT `SprkModal`) with hand-rolled sizing (`maxWidth: 1040px`, `width: 92vw`, `height: 72vh`) and `modalType="alert"` (no ESC/backdrop dismiss). It renders `<EmailComposer mount="dialog" isMaximized={maximized} onToggleMaximize={...} onSent={...} onCancel={onClose} .../>` and supplies **no title, no header, no footer of its own** — everything visible is rendered by `EmailComposer`.

### What `EmailComposer` actually renders when `mount="dialog"` (read: `EmailComposer.tsx`)
```
const isChromed = props.mount !== 'inline';   // line 962 — TRUE for 'dialog' AND 'page', no override prop exists
...
{isChromed && (
  <div className={styles.header}>
    <Text as="h2" ...>{props.titleOverride ?? (mode-derived title)}</Text>
    <ModalWindowControls isMaximized={props.isMaximized} onToggleMaximize={props.onToggleMaximize} onClose={props.onCancel} />
  </div>
)}
{middle /* fields + BodyEditor */}
<ComposerActionBar mount={props.mount} mode={state.mode} isSending={...} canSend={...} sendMode={...}
  onSend={...} onSaveDraft={...} onCancel={...} onEdit={...} onReply={...} onForward={...} />
```
`ComposerActionBar.tsx` (line 102): `if (mount === 'inline') return null;` — it renders **only** for `dialog`/`page`. Its footer (verified by reading the full file) is: Cancel (left) + Save Draft + a `SplitButton` Send-with-mailbox-caret-menu (right) in compose/reply/forward/draft modes, or Close/Reply/Forward/Edit in view mode — materially richer than Cancel/Save, and **already fully self-contained** (it is not "extra buttons alongside a host footer"; it *is* the footer).

Checked `EmailComposer.types.ts` and `SprkModal.types.ts` explicitly for an escape hatch (e.g. `hideHeader`/`chromeless`/`mount` override independent of chrome): **none exists**. `mount: EmailComposerMount` (`'page' | 'dialog' | 'inline'`) is required and `isChromed` is hard-derived from it with no override. `SprkModalProps.title` is a required `string` and the header `<div>` in `SprkModal.tsx` (lines 185–226) is unconditional.

### Why the escalation's suggested fallback also doesn't resolve this
The task's fallback ladder is: (1) preset (`FormModal`) → (2) hand-compose `SprkModal` directly with the same contract values → (3) never fork. Rung 2 was evaluated and also fails:
- Passing `mount="dialog"` to `EmailComposer` inside a hand-composed `SprkModal` still double-renders the header (SprkModal's header is unconditional; EmailComposer's is unconditional whenever not `inline`) — **omitting `SprkModal`'s footer slots avoids a double *footer*, but there is no way to omit `SprkModal`'s header.**
- The only way to suppress `EmailComposer`'s own header+footer is `mount="inline"` — but `ComposerActionBar` returns `null` for `inline`, meaning the wrapper would have to **reimplement** Send/Save-Draft/mailbox-switcher/canSend-gating/spinner-state itself, driving the engine via whatever imperative ref exists (there is an `onStateChange`/imperative-handle mechanism used by wizard hosts — confirmed present via `imperative-handle.test.tsx` and code comments, but not investigated further since using it here means duplicating `ComposerActionBar`'s logic at a new call site). That is a fork of `ComposerActionBar`'s behavior in spirit, which the task explicitly rules out ("forking never").
- Checked the P2 precedent (`ChoiceDialog.tsx`, re-based task 041): it is a **clean case** — the pre-re-base `ChoiceDialog` owned no competing header/footer of its own, so delegating fully to `ChoiceModal` was a straightforward thin-adapter swap. It does not establish a solved pattern for "child already owns its own full header+footer," so it does not apply here. This is a genuinely different, harder case than every P1/P2 conversion to date.

### Net for AC #1
Not met literally (`FormModal` is not used). Note for context (not a substitute for the AC): the **current, unmodified** wrapper already satisfies the *intent* behind FR-14/FR-05 — `modalType="alert"` (no ESC/backdrop dismiss) and hand-rolled size numbers (`1040px cap / 92vw / 72vh`) that are **numerically identical** to `SprkModal`'s own `md` `SIZE_SPEC` (`cap: 1040, widthVw: 92, height: '72vh'` — verified in `SprkModal/sizes.ts`), except the wrapper is missing `md`'s `heightMax: 720` tall-monitor cap. This confirms `md`/`alert` was the right target — the gap is specifically the chrome-ownership conflict, not the size/dismiss semantics.

---

## Finding 2 — Retirement blocked by live consumers (Steps 2–4)

### Grep evidence (repo-wide, `src/`, all `import`/`from`/`require` statements naming `SendEmailDialog`)

| File | What it imports | Resolves to today | Safe to delete legacy folder? |
|---|---|---|---|
| `Spaarke.UI.Components/src/pcf-safe.ts:27-28` | `export { SendEmailDialog } ... export type { ISendEmailPayload } from './components/SendEmailDialog'` | **Direct deep import of the LEGACY folder** | **NO — breaks the package's own compile.** Task forbids editing this file. |
| `Spaarke.DailyBriefing.Components/.../DailyBriefingApp.tsx:58` | `import { SendEmailDialog, type ISendEmailPayload, RichFilePreviewDialog } from '@spaarke/ui-components'` | `SendEmailDialog` → canonical (barrel override, line 217, pre-existing since task 021). `ISendEmailPayload` → **legacy only** (no equivalent export anywhere else) | **NO — `ISendEmailPayload` import breaks; see below for the deeper `onSend` contract mismatch.** |
| `src/solutions/LegalWorkspace/.../FilePreviewDialog.tsx:13` | `import { SendEmailDialog } from '@spaarke/ui-components'` | canonical (barrel) | Yes — barrel import, unaffected by folder deletion. Explicitly out of scope per this task's own notes (task 060/W6 concern). |
| `src/solutions/SpaarkeAi/.../ConversationPane.tsx:28` | `import { ..., SendEmailDialog } from "@spaarke/ui-components"` | canonical (barrel) | Yes — unaffected. |
| `Spaarke.Communication.Components/.../useEmailComposeActions.tsx:34` + its test | `import { SendEmailDialog } from '@spaarke/ui-components'` | canonical (barrel) — **verified full canonical prop set is passed** (`mode`, `authenticatedFetch`, `bffBaseUrl`, `onSearchRecipients`, `onSent`, `onError`, etc. — read in full, zero legacy props) | Yes — unaffected, already fully on the canonical contract. |
| `ConversationView.forward.test.tsx`, `ConversationView.emailInFlow.test.tsx` | `from '../../EmailComposer/wrappers/SendEmailDialog'` | canonical wrapper directly (relative path) | Yes — unaffected. |
| `EmailComposer/__tests__/wrappers.test.tsx`, `SendEmailDialog.threadRecord.test.tsx`, `SendEmailDialog.characterize.test.tsx` | `from '../wrappers/SendEmailDialog'` | canonical wrapper directly (relative path) | Yes — unaffected. |
| `components/SendEmailDialog/index.ts`, `.../SendEmailDialog.tsx` | self | the file(s) being deleted | N/A |
| `components/index.ts:42`, `:213` (comment) | `export * from './SendEmailDialog'` (42); comment referencing it (213) | the barrel itself | N/A — this is the edit target |

Also grepped `onSearchUsers`/`ISendEmailPayload` across all of `src/` (27 raw hits) and manually disambiguated every hit via the precise import-statement grep above — the wizard/follow-on-step files that matched (`WorkAssignmentWizardDialog`, `DocumentEmailStep`, `SendEmailFollowOnStep`, etc.) all have an **unrelated, coincidentally-named local `onSearchUsers` handler** and do **not** import `SendEmailDialog` or `ISendEmailPayload` at all — confirmed not consumers.

### Why `DailyBriefingApp.tsx` is the escalation-trigger case, not a trivial port
Read the full usage (lines 536–851). The dialog is rendered as:
```tsx
<SendEmailDialog
  open={true} onClose={...} title={...}
  defaultSubject={...} defaultBody={...}
  onSearchUsers={handleSearchUsers}
  onSend={handleEmailSend}
  maxWidth="720px" height="70vh"
/>
```
— 100% the **legacy** prop shape (`defaultSubject`/`defaultBody`/`onSearchUsers`/`onSend`/`maxWidth`/`height`), none of which exist on the canonical `ISendEmailDialogProps`, which in turn requires `authenticatedFetch`/`bffBaseUrl` (both non-optional) that are never passed here. `handleEmailSend` (the `onSend` implementation) does **two structurally different things** depending on mode, **neither of which maps onto the canonical engine's built-in send path** (`sendCommunication()` against `sprk_communication`, requiring `authenticatedFetch`/`bffBaseUrl`):
- `mode === 'briefing'`: calls `emailBriefingToColleague(recipientEmail)` — a bespoke server-rendered-briefing-summary send, nothing to do with `sprk_communication`.
- `mode === 'item'`: `webApi.createRecord('email', ...)` (a plain Dataverse `email` activity) then the OOB `Microsoft.Dynamics.CRM.SendEmail` bound action — again, a completely different data model from `sprk_communication`.

Porting this consumer to the canonical wrapper would require the canonical `EmailComposer` engine to support an alternate, consumer-supplied send path — new engine capability, not a prop rename, and squarely the "if a legacy consumer needs a non-trivial API port, record it and (if out of scope) file a `/defer` note rather than half-porting" instruction. **Not attempted.** (Whether this file's `SendEmailDialog` usage already silently fails today given the barrel's pre-existing canonical override, or is running against a stale/uncompiled `@spaarke/ui-components` snapshot, was not resolved — `Spaarke.DailyBriefing.Components` is a separate package outside this task's build/verify scope (`npx tsc --noEmit` + scoped jest were run only inside `Spaarke.UI.Components` per the task's own verification instructions) and outside its file list. Flagging for the owner/orchestrating session rather than silently deciding.)

### Net for AC #2/#3/#4
Not met. Folder not deleted; line 42 not removed; line-213-equivalent override untouched (trivially, since nothing was edited); the negative grep (AC #4) **currently fails** — legacy-path consumers exist (`pcf-safe.ts` unconditionally; `DailyBriefingApp.tsx` for the `ISendEmailPayload` type) — which is exactly why deletion was not performed.

---

## P1 escalation status: STILL OPEN (not closed by this task)

`projects/spaarke-modal-system/notes/defer-issues.md` (Decision-pending section, read-only — not edited) states: *"030 escalation — legacy `SendEmailDialog` 'v1.1.59 no-X' decision'... Resolves at P3 / task 051, which retires this legacy dialog entirely."* Because the retirement did not happen, **this escalation remains open**, not resolved. The owner's three original options (accept the interim no-X gap as a time-boxed exception / amend / retire) are unchanged; retirement (the path task 030 assumed would happen here) is now itself blocked on the two live consumers documented above. Recommend the owner either (a) expand this task's scope to also migrate `DailyBriefingApp.tsx` off `onSend`/`ISendEmailPayload` and touch `pcf-safe.ts` in the same change, or (b) accept the interim exception a while longer and re-scope task 051 to only the parts that are safe.

---

## Verification (baseline — no source edits were made)

- `npx tsc --noEmit` (`Spaarke.UI.Components`): **PASS, zero errors.** (Expected — no changes were made; also consistent with `pcf-safe.ts`'s legacy import still resolving today.)
- `npx jest src/components/EmailComposer` (scoped, per task instructions): **17 passed / 1 failed suites — 195 passed / 1 failed tests (196 total).** The one failure (`SendEmailDialog.characterize.test.tsx` → `defaults mode to "compose" when mode is omitted` → `expect(queryByRole('button',{name:'Close'})).toBeNull()`) is **pre-existing**, not introduced by this task (zero diff in the whole `EmailComposer` folder). Cross-confirmed against `notes/task-030-completion.md`, which already lists this exact suite among its 11 pre-existing-failing suites at that time ("`SendEmailDialog.characterize` [EmailComposer — confirmed zero diff in that whole folder via `git diff --stat`]"). The assertion is stale relative to the 2026-07-31 UAT round that added `ModalWindowControls` (incl. a "Close" affordance) to `EmailComposer`'s own header — unrelated to FormModal/re-basing. Not touched: fixing it is coupled to a source change (the blocked re-base) this task did not make, and editing test assertions without a corresponding source change would be scope creep.
- The task's stated baseline reference ("11 suites/22 tests full-suite") matches `task-030-completion.md`'s **full-shared-lib** suite count (2466 tests, 22 failing across 11 suites) at that point in time, not a figure scoped to `EmailComposer` alone — noting the discrepancy for clarity; the actual EmailComposer-scoped count today is 196 tests / 18 suites as measured above.

## Step 9.5 gates

- **Self code-review**: N/A to a diff (none exists — zero files modified). The review target instead is the *decision to escalate rather than force a change*: confirmed correct per (a) the task's own explicit "STOP and report" instruction for a genuine gap, (b) CLAUDE.md §6 (ambiguous/conflicting requirements — the FormModal contract vs. EmailComposer's self-chrome; a live consumer dependency the canonical API can't satisfy without behavior change), and (c) not silently reshaping `EmailComposer`'s or `pcf-safe.ts`'s contracts to force the literal instruction through.
- **adr-check**: ADR-012 ("MUST NOT fork the shell... compose it") — satisfied by declining to fork `ComposerActionBar`'s logic into the wrapper or to force a double-chrome composition. ADR-021/NFR-03 — moot (no styling changed). ADR-028 — moot (no auth-plumbing changed; confirmed the **existing, unmodified** wrapper already passes `authenticatedFetch: AuthenticatedFetchFn` through as a function, never snapshotted). No violations introduced (nothing changed).

## Files modified

**None.** Zero source files were edited or deleted. This is the deliberate, correct outcome of the escalation, not an oversight.

## Acceptance-criteria checklist (from the POML)

| # | Criterion | Pass/Fail |
|---|---|---|
| 1 | Wrapper uses `FormModal` at `md` with `dismiss="alert"`; ESC/backdrop don't dismiss | **FAIL** — blocked (Finding 1). Note: alert-dismiss behavior and `md`-equivalent sizing already hold in the current, unmodified wrapper via its own `modalType="alert"` + hand-rolled numbers that numerically match `SprkModal`'s `md` spec (missing only the `heightMax` tall-monitor cap) — but it does not literally use `FormModal`. |
| 2 | Legacy folder deleted + line-42 `export *` removed | **FAIL** — blocked (Finding 2: `pcf-safe.ts` + `DailyBriefingApp.tsx`). |
| 3 | Line-213-equivalent override retained | **Untouched/trivially true** — nothing was edited, so the existing override (currently at line 217, not 213 — line numbers shifted since the POML's notes were written; content is exactly as described) remains exactly as-is. |
| 4 | Repo-wide grep for legacy-path imports returns zero hits | **FAIL** — grep proves the opposite (2 live consumers found); this is precisely why deletion was withheld. |
| 5 | `authenticatedFetch` as function; no hex/`1px`/inline color; dark parity; build green; EmailComposer tests pass | **Partially holds, incidentally** — true of the current, unmodified wrapper (verified by reading it); shared-lib build is green (`tsc --noEmit` clean); EmailComposer tests are 195/196 (1 pre-existing, unrelated failure). Not a result of any change made in this task. |

## Escalations / deviations

- **Escalation 1 (Finding 1)**: FormModal/SprkModal cannot host `EmailComposer(mount="dialog")` without double-rendering header and footer; the "hand-compose SprkModal directly" fallback also fails without forking `ComposerActionBar`'s logic. Recommend either (a) a follow-on task to add a chrome-suppression capability to `EmailComposer` (e.g., a `hideOwnChrome`/`mount="dialog-embedded"` variant) so a shell can own chrome instead, scoped and reviewed separately given its blast radius across `page`/`inline` consumers, or (b) accept path C (pivot): keep the current raw-`Dialog` implementation as the permanent shape for this one wrapper (already alert-dismiss, already `md`-equivalent sizing, already using the shared `ModalWindowControls`) and treat FR-14's "SendEmailDialog re-based to FormModal/md" acceptance criterion as satisfied-in-spirit-not-literally, with an explicit spec amendment.
- **Escalation 2 (Finding 2)**: Two live consumers block legacy-folder deletion (`pcf-safe.ts`, `DailyBriefingApp.tsx`). `pcf-safe.ts` cannot be touched by this task per its own hard boundary. `DailyBriefingApp.tsx`'s `onSend` usage represents two send flows (`emailBriefingToColleague` BFF call; raw Dataverse `email` activity + OOB `SendEmail` action) that the canonical engine cannot express without new capability — not half-ported per instruction. Recommend a follow-on task (scoped explicitly, with the owner's input on whether `DailyBriefingApp`'s send flows should gain first-class engine support, keep a narrowly-scoped legacy-shim component, or be redesigned) before the legacy folder can be safely deleted.
- **P1 escalation** (`defer-issues.md` "030 escalation... no-X decision"): remains **open** — not closed by this task, since the retirement it depended on did not happen. See "P1 escalation status" section above.
- No git add/commit performed (per hard boundary — also moot, nothing to commit). `TASK-INDEX.md`/`current-task.md`/`.claude/**`/`pcf-safe.ts`/`FormModal`/`SprkModal`/presets — none touched.

---

## Correction (task 100 wrap-up gate, 2026-08-02)

The "Files modified: None / zero source files were edited" statement above is accurate for THE AGENT RUN ONLY. After this escalation report, the MAIN SESSION shipped the interim mitigation in the same P3 commit (`422fa7cce`): `EmailComposer/wrappers/SendEmailDialog.tsx` +6 lines — the `maxHeight` cap aligning the wrapper numerically to `md` (later re-sourced to `SIZE_SPEC.md.heightMax` at the task-100 gate). `defer-issues.md` DEF-002 carries the authoritative narrative. Flagged by the branch code-review gate (finding #3) and corrected here so the two documents agree.
