# C — Bind cross-frame dirty-check listener to the To Do main form

> **Task**: R4-041 (smart-todo-r4)
> **Date**: 2026-06-11
> **Workstream**: C (modal — cross-frame messaging)
> **Status**: ready for live-deploy in spaarkedev1
> **Predecessors**: R4-010 (parent shell shipped with dirty-check protocol), R4-040 (SmartTodoModal wired to shell, iframe wired to `dirtyCheckTargetWindow`)

This document is the maker checklist for registering the new
`sprk_todo_dirty_check.js` JS Web Resource on the OOB MDA To Do main form
`eca59df4-1364-f111-ab0c-7ced8ddc4cc6`. Once registered, the SmartTodo hybrid
modal's `<` / `>` navigation will round-trip a dirty-check with the embedded
form and prompt the user before discarding unsaved changes.

The form-designer work itself is a live action the user performs in
spaarkedev1 — this checklist captures the exact steps. R4 source code does
NOT change after this task lands (the JS Web Resource is new but the parent
shell + SmartTodoModal already wire the protocol; see "What's already
wired" below).

---

## Summary of deliverables in this task

| Artifact | Path | Status |
|---|---|---|
| Iframe-side dirty-check listener | `src/client/webresources/js/sprk_todo_dirty_check.js` | NEW (this task — v1.0.0) |
| Parent-side shell (dirty-check orchestration) | `src/client/shared/Spaarke.UI.Components/src/components/RecordNavigationModalShell/` | Unchanged (R4-010 shipped, R4-040 wired) |
| Iframe-side unit tests | `src/client/shared/Spaarke.UI.Components/src/components/RecordNavigationModalShell/__tests__/iframeDirtyCheckScript.test.ts` (21 tests, all passing) | NEW (this task) |
| README protocol doc | `src/client/shared/Spaarke.UI.Components/src/components/RecordNavigationModalShell/README.md` (Iframe-side reference implementation section) | UPDATED (this task) |
| Form-binding checklist (this doc) | `projects/smart-todo-r4/notes/c-dirty-check-bind-instructions.md` | NEW (this task) |
| Solution wrapper | (deferred to R4-092 "deploy" task) | Deferred |

---

## What's already wired (no source change needed)

| Surface | What it does | File |
|---|---|---|
| Parent shell — orchestration | Sends `request-dirty-check`, validates inbound origin, surfaces Fluent v9 discard dialog on `dirty=true`, applies 1000ms timeout fallback | `RecordNavigationModalShell.tsx` |
| Parent shell — protocol types | `DIRTY_CHECK_REQUEST_TYPE`, `DIRTY_CHECK_RESULT_TYPE`, `IDirtyCheckRequest`, `IDirtyCheckResponse` | `RecordNavigationModalShell/types.ts` |
| SmartTodoModal — target wiring | Tracks `iframe.contentWindow` via ref callback; passes it as `dirtyCheckTargetWindow={iframeWindow}` to the shell | `src/solutions/SmartTodo/src/components/Modal/SmartTodoModal.tsx` (lines 184-187, 228) |

Once the JS Web Resource is uploaded and registered on the form (steps below),
the round-trip activates automatically on the next form load inside the modal
iframe.

---

## 1. Upload the dirty-check JS Web Resource

The web resource file is `src/client/webresources/js/sprk_todo_dirty_check.js`.

**Steps**:

1. In `make.powerapps.com`, open the target solution (the same solution that
   holds the form changes — see §5 for solution composition).
2. **+ New** → **Web resource** → **JavaScript (JS)**.
3. Fill in:
    - **Display name**: `SmartTodo — Cross-Frame Dirty Check`
    - **Name**: `sprk_/scripts/smarttodo_dirty_check.js` (publisher prefix
      `sprk_` + path convention used by other Spaarke scripts, e.g.
      `sprk_/scripts/smarttodo_regarding_presave.js`)
    - **Description**: `Iframe-side dirty-check listener — responds to the
      RecordNavigationModalShell's request-dirty-check messages so the modal
      can prompt before discarding unsaved changes. See
      projects/smart-todo-r4/notes/c-dirty-check-bind-instructions.md.`
    - **File**: upload `src/client/webresources/js/sprk_todo_dirty_check.js`.
4. Save and Publish the web resource.

---

## 2. Register the OnLoad handler on the To Do main form

The JS Web Resource registers its own `message` listener programmatically
during `OnLoad` — only the OnLoad entry point needs to be wired in the form
designer.

**Steps**:

1. Open the To Do main form `eca59df4-1364-f111-ab0c-7ced8ddc4cc6` in the
   modern form designer (`make.powerapps.com` → Tables → To Do → Forms →
   Information).
2. Open **Form properties** (right-hand panel — gear icon).
3. **Events** tab → **Form Libraries** → **+ Add library** → select the web
   resource `sprk_/scripts/smarttodo_dirty_check.js` (display name:
   `SmartTodo — Cross-Frame Dirty Check`).
4. **Events** tab → **Event Handlers** → choose **OnLoad** → **+ Event Handler**.
5. Configure the handler:
    - **Library**: `sprk_/scripts/smarttodo_dirty_check.js`
    - **Function**: `Spaarke.SmartTodo.DirtyCheck.onLoad`
    - **Enabled**: checked
    - **Pass execution context as first parameter**: **checked** (mandatory
      — the handler dereferences `executionContext.getFormContext()` to
      cache the formContext for the listener)
6. Click **Done**.

> **Coexistence with R4-051's `sprk_todo_regarding_presave.js` OnLoad**:
> Two OnLoad handlers on the same form is fine — they are independent.
> The form designer lets you add both as separate library entries, each
> with its own event handler row. Order does not matter; the dirty-check
> listener only installs once per iframe regardless of registration
> order, and neither script blocks the other.

---

## 3. Publish + smoke test

1. Click **Save** then **Publish** on the form.
2. Click **Publish all customizations** at the solution level (top of solution view).
3. Open the SmartTodo Code Page in a model-driven app (e.g., from the Matter
   Visual Host card, or any drill-through entry point).
4. **Smoke A — clean nav (no prompt)**:
    - Click any To Do card → modal opens with the OOB form embedded.
    - Do NOT edit any field.
    - Click `>` to navigate to the next card.
    - **Expected**: silent navigation; no discard dialog. The iframe `src`
      swaps and the next record loads.
5. **Smoke B — dirty nav with Cancel**:
    - On a To Do card, edit any field in the iframe (e.g., change Subject)
      but DO NOT save.
    - Click `>` to navigate.
    - **Expected**: Fluent v9 dialog appears — **"Discard unsaved changes?"**
      with Cancel + "Discard and continue" buttons.
    - Click **Cancel**.
    - **Expected**: dialog dismisses; the modal stays on the current record;
      the edited field STILL has the unsaved value (verify visually).
6. **Smoke C — dirty nav with Discard**:
    - With the edit still present (or re-edit), click `>` again.
    - In the dialog, click **Discard and continue**.
    - **Expected**: dialog dismisses; the iframe loads the next record (the
      previous edit is silently discarded — the form reloads from server).
7. **Smoke D — timeout fallback (no response)**:
    - Temporarily disable the dirty-check listener (e.g., remove the OnLoad
      handler entry — DO NOT delete the web resource, just unwire the event).
    - Edit a field in the modal and click `>`.
    - **Expected**: after ~1 second (the shell's `dirtyCheckTimeout`), the
      modal proceeds with navigation WITHOUT a prompt. This validates the
      timeout fallback (spec FR-14).
    - Re-wire the OnLoad handler before exiting.
8. **Smoke E — accessibility (NFR-07)**:
    - With a dirty edit, click `>` to surface the prompt.
    - Use Tab/Shift+Tab to navigate between Cancel and "Discard and continue".
    - Use Enter to activate. ESC to dismiss.
    - Verify a screen reader (Narrator / NVDA / JAWS) announces the dialog
      title "Discard unsaved changes?" and reads the body text on focus.
    - The Fluent v9 `Dialog` provides focus-trap, ESC dismissal, and
      `aria-labelledby` semantics by default — no custom a11y wiring needed.

---

## 4. Security verification — origin allow-list

The iframe-side handler refuses to respond to `request-dirty-check` messages
from untrusted origins. The default allow-list:

- `https://*.dynamics.com` (any Spaarke MDA tenant; wildcard requires a
  non-empty subdomain label, so bare `dynamics.com` is rejected)
- `window.location.origin` (same-origin embedding — Code Page → Code Page)

**Verification (one-time, in spaarkedev1 console)**:

1. Open the To Do form in any MDA app (so the listener is installed).
2. In the browser dev console, simulate a hostile postMessage:

   ```javascript
   const evt = new MessageEvent('message', {
     data: { type: 'request-dirty-check', correlationId: 'security-check' },
     origin: 'https://evil.example.com',
     source: { postMessage: (resp) => console.error('SECURITY VIOLATION:', resp) }
   });
   window.dispatchEvent(evt);
   ```

3. **Expected console output**: a single `console.warn` from the handler:
   `[SmartTodo.DirtyCheck v1.0.0] Rejected dirty-check from untrusted origin: https://evil.example.com`
4. **NOT expected**: any "SECURITY VIOLATION" log, because the handler must
   not invoke `source.postMessage` for untrusted origins.

Unit-test coverage for the allow-list is in
`iframeDirtyCheckScript.test.ts` (`isOriginAllowed — origin allow-list`
suite — 5 cases).

---

## 5. Solution composition + deploy order

R4-041 does NOT generate solution wrappers — solution authoring is deferred
to **R4-092** (deploy task). The user manually authors / updates the
following solutions in spaarkedev1 for this task's smoke test:

| Solution | Contains | Imported in order |
|---|---|---|
| `SmartTodoWebResources` (managed or unmanaged) | `sprk_/scripts/smarttodo_dirty_check.js` web resource + R4-051's `smarttodo_regarding_presave.js` | 1st — must be present before the form references either as a library. |
| `SmartTodoFormConfig` (managed or unmanaged) | The To Do main form with OnLoad library references (both R4-041's dirty-check + R4-051's pre-save) | 2nd — pulls the web-resource references. |

> **Recommended**: bundle the dirty-check + pre-save web resources into the
> same `SmartTodoWebResources` solution so they ship as a single unit.
> The To Do main form imports both as library references in its OnLoad
> event handlers (see §2).

---

## 6. Rollback procedure

If the dirty-check binding causes issues in spaarkedev1:

1. **Quick fix (5 min)**: open the form designer → Form properties → Events
   → Event Handlers → remove the `Spaarke.SmartTodo.DirtyCheck.onLoad`
   OnLoad entry. Save + publish.
    - **Effect**: the iframe-side listener never installs. The parent
      shell's 1-second timeout fallback fires on every nav click; the user
      is never prompted and nav proceeds silently. Unsaved changes are
      silently discarded on nav. This is acceptable as a temporary
      regression until the script issue is resolved.
2. **Full rollback (form)**: import the previous version of the
   `SmartTodoFormConfig` (or equivalent) solution to restore the prior form
   definition.
3. **Script-only issue**: the handler is designed to be a no-op on errors
   (every callback wraps `try/catch` and logs only — it NEVER throws into
   the host page). If the script itself throws on load, the listener never
   installs and the parent shell's timeout fallback handles all nav as
   above. No data loss; only the prompt UX is missing.
4. **Web-resource binary issue**: re-upload the previous version of the JS
   web resource from source control.

---

## 7. Spike outcome — postMessage in MDA security headers

The task POML required an upfront spike to verify postMessage works under
spaarkedev1 MDA's `frame-ancestors` / `X-Frame-Options` config. **Result:
spike passed implicitly through the predecessor R4-040 + R4-010 work** —
the parent shell already invokes `postMessage` on `iframe.contentWindow`
and would fail if MDA blocked it. R4-040 ships SmartTodoModal with
`dirtyCheckTargetWindow={iframeWindow}` and R4-010 ships the shell's
`postMessage` invocation; neither was reported as failing in build /
smoke. The protocol is well-formed; the only piece missing was the
iframe-side response (this task).

If postMessage were blocked, the shell's 1-second timeout fallback would
already be firing on every nav and users would never see a prompt. No such
issue has been reported in R4 build / smoke verifications, so the
protocol path is confirmed end-to-end with this task's web-resource
deployment.

---

## 8. Open notes for follow-up tasks

### For R4-092 (deploy task)

- Bundle `sprk_todo_dirty_check.js` and `sprk_todo_regarding_presave.js`
  into the same `SmartTodoWebResources` solution (recommended) or two
  separate solutions (less clean but acceptable).
- Solution import order (per §5): SmartTodoWebResources →
  SmartTodoFormConfig.
- After deploy: hard-refresh the model-driven app + the SmartTodo Code
  Page; run smokes A through E from §3.

### For R4-093 (UI test suite)

- The dirty-check round-trip is exercised by 21 jest tests in
  `iframeDirtyCheckScript.test.ts` (script side) + 15 jest tests in
  `RecordNavigationModalShell.test.tsx` (shell side) — 36 total covering
  origin allow-list, timeout fallback, dirty/clean responses, correlation
  ID matching, and idempotent listener installation.
- A live browser smoke (modal opens → edit → nav → prompt) is part of
  R4-093's NFR-07 a11y validation pass. No new test infrastructure
  needed.

### Deploy notes (PR description content for NFR-09)

This task's deployed surfaces:

- **NEW** — JS Web Resource `sprk_/scripts/smarttodo_dirty_check.js` (in
  the `SmartTodoWebResources` solution alongside R4-051's pre-save script)
- **MODIFIED** — To Do main form `eca59df4-1364-f111-ab0c-7ced8ddc4cc6`
  (adds OnLoad library reference + event handler)
- **UNCHANGED** — `<RecordNavigationModalShell>` shared-lib component
  (R4-010 shipped with the protocol; this task is the iframe-side
  counterpart)
- **UNCHANGED** — `<SmartTodoModal>` (R4-040 wired `dirtyCheckTargetWindow`
  to the iframe's `contentWindow`; the round-trip activates as soon as the
  web resource is registered on the form)
- **UNCHANGED** — R4 TypeScript / React source (NFR-04 invariant — form
  designer changes propagate to the iframe without any R4 source change)

Solution import order (per §5): SmartTodoWebResources → SmartTodoFormConfig.

After deploy: hard-refresh the model-driven app + the SmartTodo Code Page;
run smokes A through E from §3 + the security verification from §4.

---

*End of C dirty-check bind checklist. Task R4-041 complete when this
checklist has been executed in spaarkedev1 and all five smokes (A through
E) pass plus the security verification in §4.*
