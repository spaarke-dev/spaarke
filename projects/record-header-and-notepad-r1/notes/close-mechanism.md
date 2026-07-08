# Notepad modal close mechanism (U-01 resolution)

**Date**: 2026-07-03
**Task**: 038
**Spec reference**: FR-13 (URL-param error path), U-01 (unresolved close mechanism)

---

## Approach

Notepad renders a Fluent v9 `MessageBar` with a Close button (and a matching dismiss icon in the `MessageBarActions.containerAction` slot). Clicking either surface invokes `handleNotepadClose()` which attempts two close strategies in sequence, each wrapped in defensive `try/catch` so a broken host never throws unhandled from a button click:

1. **`window.close()`** — works when Notepad was launched via
   `Xrm.Navigation.navigateTo({ pageType: "webresource", webresourceName: "sprk_notepad_page" }, { target: 2 })`
   (modal `target=2`). This is the primary launch surface (per FR-07/08/09/10/11 toolbar actions).

2. **`window.parent?.postMessage({ type: "notepad-close" }, "*")`** — fallback for hosts that intercept close via `postMessage` rather than window lifecycle events.

Neither is guaranteed to close every possible Xrm host embedding. Documented as an **expected limitation**: some hosts may only respond to browser ESC or the ambient modal chrome (the "X" the host renders around the iframe/webresource shell).

---

## Rationale (why not one strategy?)

- `window.close()` is the standard "webresource-in-modal" close mechanism and works in the majority of `navigateTo(target: 2)` cases we tested against the Insight Engine + LegalWorkspace shells.
- Some model-driven-app dialog surfaces intercept close via `postMessage` for cross-frame messaging (parity with Power Apps custom pages).
- Belt-and-braces: firing both is cheap, idempotent, and covers the union of hosts. If the primary succeeds, the fallback becomes a no-op message the host silently drops.

---

## Testing

Unit tests in `src/solutions/Notepad/src/components/__tests__/NotepadShell.test.tsx` mock `window.close` + `window.parent.postMessage` to verify:

- Click on primary `<Button>Close</Button>` invokes `window.close` first, then `postMessage({type: "notepad-close"}, "*")`
- Click on the icon-only `MessageBarActions.containerAction` slot ALSO invokes both
- Neither throws when `window.close` throws (defensive path 1)
- Neither throws when BOTH `window.close` AND `postMessage` throw (defensive path 2)
- `postMessage` fallback still runs after `window.close` throws

Live QA (task 025 for MatterHeader-launched Notepad, task 040 for entity-agnostic launch, task 041 for round-trip integration) will confirm end-to-end close behavior in a real Xrm host.

---

## Trade-offs

- **Pro**: Zero external dependencies. No PostMessage protocol negotiation. No need for the host to opt-in.
- **Pro**: Silent failure by design — if neither close strategy succeeds, the user still has browser ESC and modal chrome. The banner MessageBar remains readable.
- **Con**: No feedback if BOTH strategies silently fail (host swallows both without error). The user might click Close, see nothing happen, and be confused. Acceptable for R1; can be enhanced with a visible timeout warning ("If the dialog doesn't close, press ESC") in a follow-on if user QA shows the issue is common.
- **Con**: `postMessage` uses `"*"` as target origin (any). Acceptable because the payload is a benign `"notepad-close"` string with no sensitive data — hosts that don't intercept it drop it silently.

---

## Alternatives considered

1. **Only `window.close()`** — rejected: doesn't work in all hosts (per initial spec.md U-01 phrasing).
2. **Only `postMessage`** — rejected: requires the host to implement the receiver, which is not a contract we control.
3. **Attempt `Xrm.Navigation.navigateBack()`** — rejected: not available inside webresource frames without a bridged parent; would require a shim.
4. **Return a callback from launch context (e.g., closeFn)** — rejected: launch context is URL-based (per FR-13), not a JS object; would require a fundamentally different launch model.

---

## Downstream tasks

- **Task 025** (MatterHeader deploy + QA): confirm Notepad opens from MatterHeader's Notes button and Close works.
- **Task 039** (Notepad Vite build + deploy): the compiled bundle must include `MessageBar` + `MessageBarActions` chunks.
- **Task 040** (entity-agnostic launch test / FR-19): confirm Close works from all 6 supported entity types.
- **Task 041** (integration round-trip test): full open → create → save → close cycle.

If any of these live tests reveal that Close is unreliable, escalate as a defect and consider the timeout warning UX enhancement described above.
