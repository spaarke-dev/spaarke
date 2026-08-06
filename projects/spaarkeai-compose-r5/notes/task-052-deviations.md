# Task 052 — UAT #10/#11 Word/WOPI lock — honest round-trip UX — Deviation & Completion Note

> **Completed**: 2026-07-30 · Rigor FULL · opus/high · SEV-1.

## Decision: honest UX, NOT a fake "Unlock" (platform reality)
A `researcher` investigation against Microsoft docs was definitive: **there is no Graph API to release a Word-for-web co-authoring lock.** It clears only on a clean Word close or SharePoint's **~30-min-from-last-edit** WOPI timeout. `checkout`/`checkin`/`discardCheckout` exist for SPE but act on a *formal checkout* (a different lock), and Spaarke **never does a formal checkout** — so a 423 on our content PUT is **always** the co-authoring lock. Building an "Unlock" button would lie to users in the common case (owner explicitly aligned on the honest approach).

## What was built (client-focused honest round-trip)
- **Server** (`ComposeEndpoints.cs`): rewrote the 423 ProblemDetails copy — removed the misleading "checked out … check it in" (nothing was ever checked out, which confused the UAT user) → honest: *"This document is open in Word — close it there, then click Retry. It also releases automatically within a few minutes. Your Compose changes are safe and still pending."* Title "Open in Word".
- **Client save handler** (`ComposeWorkspace.tsx`): distinct **423 branch** → `saveFailed{ isLock: true }` (new state `saveErrorIsLock`), instead of the generic HTTP-error message.
- **Banner** (`ComposeBannerStack.tsx`): when `saveErrorIsLock`, renders a warning-intent **"Open in Word"** bar with two actions — **Retry Save** (re-runs the save; succeeds once Word is closed) and **Reload from Word** (dispatches `requestLoad` to pull Word's latest version as the new baseline). No fake Unlock.
- **State** (`ComposeWorkspace.types.ts`): `saveErrorIsLock` added; set on 423, cleared on save-start.

## The Word round-trip design (owner's versioning insight, corrected)
The owner asked whether versioning could overcome the lock. Versioning solves the **data** round-trip (lossless), but the lock is **exclusive** — you cannot save from Compose while Word holds it, regardless of version freshness. The working model is **flush-on-open → edit in Word → reload-on-return → close Word → clean save**:
- Open-in-Word already flushes Compose → a new SPE version before Word opens (`openInWordFlushed`).
- Return detection + **Reload from Word** (task 053's `visibilitychange` + reload) pulls Word's version back into TipTap as the new baseline.
- The honest 423 UX (this task) covers the "tried to save while Word still open" case with Retry, instead of a confusing error.

A fuller "handed-to-Word mode" that *disables* Compose save while Word owns the file (rather than letting it 423 → Retry) is a reasonable future polish; the current Retry/Reload flow already resolves the UAT confusion without it.

## What was NOT built (deliberately)
- No Graph `discardCheckout` primitive: it can't clear a co-auth lock and force-discarding another user's *formal* checkout is risky (researcher warning); Spaarke doesn't do formal checkout anyway. No value here.
- No "Unlock" button: honest — no programmatic release exists.

## Tests
- `ComposeBannerStack.test.tsx`: +2 (generic error bar when not a lock; honest "Open in Word" bar with Retry+Reload firing). Suite **7/7**.
- `Def14_ComposeSaveLockedDocumentTests` (regression, KEEP-path): updated both 423 assertions from `"checked out"` → `"open in Word"` (honest copy). Compose suite **822/822**.
- BFF build 0 errors. Client typecheck: no NEW errors (only pre-existing `@spaarke/*` + `unknown`/`any`).

## Step 9.5
ADR-007 (no Graph type added above SpeFileStore — no facade change); ADR-028 (reused existing auth path); ADR-021 (banner intent/actions render both themes); ADR-038 (regression + component tests updated, no banned patterns). Honest UX per §6.5 (no over-promise). No violations.

## Follow-on (separate, cross-cutting)
Retire the Dataverse advisory checkout model (`DocumentCheckoutService`) repo-wide — see `notes/checkout-retirement-plan.md`. It adds friction (checkout prompts, the misleading wording at its source, a stale-flag "can't save" window if the sweeper lags) for value already covered by co-authoring + the FR-08 stale-base re-anchor + SPE versioning.
